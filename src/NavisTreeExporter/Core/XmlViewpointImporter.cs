using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Autodesk.Navisworks.Api;

namespace NavisTreeExporter.Core
{
    /// <summary>
    /// Builds Viewpoint objects directly from a Navisworks "saved viewpoints"
    /// XML export (the file produced by the Saved Viewpoints panel's
    /// Export... command) - there is no .NET API to trigger Navisworks' own
    /// XML *import*, confirmed via Autodesk's own forums ("There is no API
    /// solution to use the standard import of XML files... you would need
    /// to build a plugin/program that will read the XML files and create
    /// the corresponding saved viewpoints"). So this reads the XML itself
    /// and reconstructs each view's camera as a Viewpoint, applied directly
    /// to Document.CurrentViewpoint - no need to insert these into the
    /// document's own SavedViewpoints tree at all.
    ///
    /// Looks for &lt;view name="..."&gt; elements anywhere in the document
    /// (regardless of folder nesting), each expected to contain
    /// &lt;viewpoint&gt;/&lt;camera&gt;/&lt;position&gt;/&lt;pos3f x y z/&gt;
    /// and &lt;viewpoint&gt;/&lt;camera&gt;/&lt;rotation&gt;/
    /// &lt;quaternion a b c d/&gt;, matching a real exported sample file.
    ///
    /// The quaternion-to-direction/up conversion mirrors a working
    /// open-source implementation (teocomi/BCFier's NavisView.cs
    /// GetViewDirection/GetViewUp, which rotate base vectors (0,0,-1) and
    /// (0,1,0) by the camera's Rotation3D via the standard quaternion
    /// sandwich product q*v*q^-1) rather than being derived from scratch,
    /// but - like the rest of this project - is still unverified against a
    /// real build/run.
    ///
    /// NOTE: clip plane (&lt;clipplaneset&gt;) reconstruction uses
    /// View.SetClippingPlanes(string json) - confirmed to exist and take a
    /// JSON-serialized clip plane description, but not documented anywhere
    /// found. A first attempt guessed an "OrientedBox" wrapper (by analogy
    /// with an Autodesk forum post about section-box clipping) and was
    /// rejected outright by a real SetClippingPlanes call. Calling
    /// View.GetClippingPlanes() on a real (disabled) view instead revealed
    /// the actual schema directly:
    /// {"Type":"ClipPlaneSet","Version":1,"Planes":[],"Linked":false,"Enabled":false}
    /// - a flat "Planes" array plus top-level Linked/Enabled, matching the
    /// XML's own &lt;clipplaneset linked="" enabled="" mode="planes"&gt;
    /// almost exactly. BuildClipPlanesJson below builds that same shape
    /// directly from the XML's 6 named half-space planes
    /// (top/bottom/front/back/left/right, each with its own enabled
    /// state), passing each plane's normal/distance through unchanged
    /// (no box-coordinate conversion needed once the real "Planes" mode
    /// schema was known). The individual plane object's own key names
    /// ("Normal"/"Distance"/"Enabled") are still a best-effort guess by
    /// analogy with the confirmed top-level schema, not confirmed against
    /// a real enabled clip - the next thing to check on a real run.
    /// </summary>
    public static class XmlViewpointImporter
    {
        public static List<(string Name, Viewpoint Viewpoint, string ClipPlanesJson)> Load(Document document, string xmlPath)
        {
            var results = new List<(string, Viewpoint, string)>();
            var xml = XDocument.Load(xmlPath);

            foreach (var viewElement in xml.Descendants("view"))
            {
                var name = (string)viewElement.Attribute("name");
                if (string.IsNullOrWhiteSpace(name)) name = "Viewpoint";

                var viewpointElement = viewElement.Element("viewpoint");
                var cameraElement = viewpointElement?.Element("camera");
                if (cameraElement == null) continue;

                var posElement = cameraElement.Element("position")?.Element("pos3f");
                var quatElement = cameraElement.Element("rotation")?.Element("quaternion");
                if (posElement == null || quatElement == null) continue;

                var position = new Point3D(
                    ParseDouble(posElement.Attribute("x")),
                    ParseDouble(posElement.Attribute("y")),
                    ParseDouble(posElement.Attribute("z")));

                var rotation = new Rotation3D(
                    ParseDouble(quatElement.Attribute("a")),
                    ParseDouble(quatElement.Attribute("b")),
                    ParseDouble(quatElement.Attribute("c")),
                    ParseDouble(quatElement.Attribute("d")));

                var viewpoint = document.CurrentViewpoint.CreateCopy();
                viewpoint.Position = position;
                viewpoint.AlignDirection(RotateVector(rotation, new Vector3D(0, 0, -1)));
                viewpoint.AlignUp(RotateVector(rotation, new Vector3D(0, 1, 0)));

                var projection = (string)cameraElement.Attribute("projection");
                viewpoint.Projection = string.Equals(projection, "ortho", StringComparison.OrdinalIgnoreCase)
                    ? ViewpointProjection.Orthographic
                    : ViewpointProjection.Perspective;

                if (TryParseDouble(viewpointElement.Attribute("focal"), out var focal))
                {
                    viewpoint.FocalDistance = focal;
                }

                // "angular" on <viewpoint> and "height" on <camera> were the
                // same value (within float rounding) in a real sample file -
                // both look like the vertical field of view in radians.
                if (TryParseDouble(viewpointElement.Attribute("angular"), out var angular) ||
                    TryParseDouble(cameraElement.Attribute("height"), out angular))
                {
                    viewpoint.HeightField = angular;
                }

                var clipJson = BuildClipPlanesJson(viewElement.Element("clipplaneset"));

                results.Add((name, viewpoint, clipJson));
            }

            return results;
        }

        /// <summary>
        /// Builds the JSON for View.SetClippingPlanes from a &lt;clipplaneset&gt;
        /// element, matching the real schema confirmed via
        /// View.GetClippingPlanes() on a real (disabled) view:
        /// {"Type":"ClipPlaneSet","Version":1,"Planes":[],"Linked":false,"Enabled":false}
        /// Every viewpoint gets an explicit JSON (disabled if the XML has
        /// no enabled clipplaneset), not just the ones with clipping, since
        /// clip state lives on the document's active view and would
        /// otherwise leak from whichever viewpoint was captured previously
        /// in the same run.
        /// </summary>
        private static string BuildClipPlanesJson(XElement clipplaneset)
        {
            var enabled = clipplaneset != null && (string)clipplaneset.Attribute("enabled") == "1";
            var linked = clipplaneset != null && (string)clipplaneset.Attribute("linked") == "1";
            var inv = CultureInfo.InvariantCulture;

            var planes = new System.Text.StringBuilder();
            if (enabled)
            {
                var clipplanesElement = clipplaneset.Element("clipplanes");
                if (clipplanesElement != null)
                {
                    foreach (var clipplane in clipplanesElement.Elements("clipplane"))
                    {
                        var planeElement = clipplane.Element("plane");
                        if (planeElement == null) continue;

                        var distance = ParseDouble(planeElement.Attribute("distance"));
                        var normalElement = planeElement.Element("vec3f");
                        double nx = 0, ny = 0, nz = 0;
                        if (normalElement != null)
                        {
                            nx = ParseDouble(normalElement.Attribute("x"));
                            ny = ParseDouble(normalElement.Attribute("y"));
                            nz = ParseDouble(normalElement.Attribute("z"));
                        }

                        var planeEnabled = (string)clipplane.Attribute("state") == "enabled";

                        if (planes.Length > 0) planes.Append(",");
                        planes.Append("{\"Normal\":{\"X\":" + nx.ToString(inv) + ",\"Y\":" + ny.ToString(inv) + ",\"Z\":" + nz.ToString(inv) + "},"
                            + "\"Distance\":" + distance.ToString(inv) + ","
                            + "\"Enabled\":" + (planeEnabled ? "true" : "false") + "}");
                    }
                }
            }

            return "{\"Type\":\"ClipPlaneSet\",\"Version\":1,\"Planes\":[" + planes + "],"
                + "\"Linked\":" + (linked ? "true" : "false") + ","
                + "\"Enabled\":" + (enabled ? "true" : "false") + "}";
        }

        private static double ParseDouble(XAttribute attribute)
        {
            return attribute == null ? 0 : double.Parse(attribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static bool TryParseDouble(XAttribute attribute, out double value)
        {
            if (attribute == null)
            {
                value = 0;
                return false;
            }

            return double.TryParse(attribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// Rotates <paramref name="baseVector"/> by <paramref name="rotation"/>
        /// via the standard quaternion sandwich product q * v * q^-1 (v
        /// embedded as a pure quaternion with a zero scalar part).
        /// </summary>
        private static Vector3D RotateVector(Rotation3D rotation, Vector3D baseVector)
        {
            var pure = new Rotation3D(baseVector.X, baseVector.Y, baseVector.Z, 0);
            var temp = MultiplyRotation3D(pure, rotation.Invert());
            var rotated = MultiplyRotation3D(rotation, temp);

            var result = new Vector3D(rotated.A, rotated.B, rotated.C);
            result.Normalize();
            return result;
        }

        private static Rotation3D MultiplyRotation3D(Rotation3D r2, Rotation3D r1)
        {
            var a = r2.D * r1.A + r2.A * r1.D + r2.B * r1.C - r2.C * r1.B;
            var b = r2.D * r1.B + r2.B * r1.D + r2.C * r1.A - r2.A * r1.C;
            var c = r2.D * r1.C + r2.C * r1.D + r2.A * r1.B - r2.B * r1.A;
            var d = r2.D * r1.D - r2.A * r1.A - r2.B * r1.B - r2.C * r1.C;
            return new Rotation3D(a, b, c, d);
        }
    }
}
