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
    /// NOTE: clip plane (&lt;clipplaneset&gt;) reconstruction is the least
    /// confident part of this file. There's no directly-documented .NET API
    /// for it either - the closest found is View.GetClippingPlanes()/
    /// SetClippingPlanes(string json) taking a JSON-serialized clip plane
    /// description (confirmed to exist via Autodesk forum posts showing a
    /// "ClipPlaneSet"/"OrientedBox3D" JSON example for section-box
    /// clipping), which is what BuildClipPlanesJson below constructs. The
    /// exact JSON key names/casing are a best-effort guess by analogy with
    /// that example and Rotation3D's own A/B/C/D property names, not
    /// confirmed against real output - likely to need adjustment from a
    /// real run. A real exported file has 6 named half-space planes
    /// (top/bottom/front/back/left/right, only some "enabled" - the rest
    /// "default"/inactive) in the box's own rotated local frame rather
    /// than a world-space box, converted into local min/max here using the
    /// plane semantics worked out from a real sample (a viewpoint with
    /// only top+bottom "enabled" turned out to be a thin horizontal slice
    /// of one floor/deck, which only makes sense if the kept region is
    /// where dot(Normal, Point) &gt; Distance for each enabled plane).
    /// </summary>
    public static class XmlViewpointImporter
    {
        private const double UnboundedExtent = 1000000;

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
        /// element, or the "disabled" JSON if that element is missing/not
        /// enabled - every viewpoint needs an explicit disable, not just
        /// the ones without clipping, since clip state is set on the
        /// document's active view and would otherwise leak from whichever
        /// viewpoint was captured previously in the same run.
        /// </summary>
        private static string BuildClipPlanesJson(XElement clipplaneset)
        {
            var enabled = clipplaneset != null && (string)clipplaneset.Attribute("enabled") == "1";

            var minX = -UnboundedExtent, maxX = UnboundedExtent;
            var minY = -UnboundedExtent, maxY = UnboundedExtent;
            var minZ = -UnboundedExtent, maxZ = UnboundedExtent;
            double ra = 0, rb = 0, rc = 0, rd = 1;

            if (enabled)
            {
                var rotationElement = clipplaneset.Element("box-rotation")?.Element("rotation")?.Element("quaternion");
                if (rotationElement != null)
                {
                    ra = ParseDouble(rotationElement.Attribute("a"));
                    rb = ParseDouble(rotationElement.Attribute("b"));
                    rc = ParseDouble(rotationElement.Attribute("c"));
                    rd = ParseDouble(rotationElement.Attribute("d"));
                }

                var clipplanesElement = clipplaneset.Element("clipplanes");
                if (clipplanesElement != null)
                {
                    foreach (var clipplane in clipplanesElement.Elements("clipplane"))
                    {
                        if ((string)clipplane.Attribute("state") != "enabled") continue;

                        var planeElement = clipplane.Element("plane");
                        if (planeElement == null) continue;
                        var distance = ParseDouble(planeElement.Attribute("distance"));

                        switch ((string)clipplane.Attribute("alignment"))
                        {
                            case "top": maxZ = distance; break;
                            case "bottom": minZ = distance; break;
                            case "front": maxY = distance; break;
                            case "back": minY = distance; break;
                            case "right": maxX = distance; break;
                            case "left": minX = distance; break;
                        }
                    }
                }
            }

            var inv = CultureInfo.InvariantCulture;
            return "{\"Type\":\"ClipPlaneSet\",\"Version\":1,\"OrientedBox\":{"
                + "\"Type\":\"OrientedBox3D\","
                + "\"Enabled\":" + (enabled ? "true" : "false") + ","
                + "\"Box\":{"
                + "\"Min\":{\"X\":" + minX.ToString(inv) + ",\"Y\":" + minY.ToString(inv) + ",\"Z\":" + minZ.ToString(inv) + "},"
                + "\"Max\":{\"X\":" + maxX.ToString(inv) + ",\"Y\":" + maxY.ToString(inv) + ",\"Z\":" + maxZ.ToString(inv) + "}"
                + "},"
                + "\"Rotation\":{\"X\":" + ra.ToString(inv) + ",\"Y\":" + rb.ToString(inv) + ",\"Z\":" + rc.ToString(inv) + ",\"W\":" + rd.ToString(inv) + "}"
                + "}}";
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
