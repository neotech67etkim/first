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
    /// found. Calling View.GetClippingPlanes() on a real (disabled) view
    /// confirmed the top-level shape directly:
    /// {"Type":"ClipPlaneSet","Version":1,"Planes":[],"Linked":false,"Enabled":false}
    /// - matching the XML's own &lt;clipplaneset linked="" enabled=""
    /// mode="planes"&gt; almost exactly. But the individual plane object's
    /// own keys are still unconfirmed - a first guess
    /// ({"Normal":{...},"Distance":...,"Enabled":...} per plane) was
    /// rejected outright by SetClippingPlanes on a real run even with a
    /// valid top-level shape, meaning something about the per-plane
    /// encoding is wrong. BuildClipPlaneJsonVariants below produces several
    /// plausible encodings instead of a single guess; RunAutoExport tries
    /// each in turn per viewpoint (only a few milliseconds each, no extra
    /// waiting involved) and logs which one Navisworks actually accepts.
    /// </summary>
    public static class XmlViewpointImporter
    {
        private readonly struct PlaneData
        {
            internal double Nx { get; }
            internal double Ny { get; }
            internal double Nz { get; }
            internal double Distance { get; }
            internal bool Enabled { get; }
            internal string Alignment { get; }

            internal PlaneData(double nx, double ny, double nz, double distance, bool enabled, string alignment)
            {
                Nx = nx;
                Ny = ny;
                Nz = nz;
                Distance = distance;
                Enabled = enabled;
                Alignment = alignment;
            }
        }

        public static List<(string Name, Viewpoint Viewpoint, List<(string Label, string Json)> ClipPlaneVariants)> Load(Document document, string xmlPath)
        {
            var results = new List<(string, Viewpoint, List<(string, string)>)>();
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

                var clipVariants = BuildClipPlaneJsonVariants(viewElement.Element("clipplaneset"));

                results.Add((name, viewpoint, clipVariants));
            }

            return results;
        }

        /// <summary>
        /// Produces several plausible SetClippingPlanes JSON encodings for
        /// the same &lt;clipplaneset&gt;, most-likely-correct first, all
        /// sharing the confirmed top-level shape
        /// ({"Type":"ClipPlaneSet","Version":1,"Planes":[...],"Linked":bool,"Enabled":bool})
        /// but differing in how each entry of "Planes" is encoded. Disabled
        /// clipplaneset (or none at all) still returns one variant - an
        /// empty, disabled ClipPlaneSet, needed on every viewpoint so clip
        /// state doesn't leak from whichever viewpoint was captured
        /// previously in the same run.
        /// </summary>
        private static List<(string Label, string Json)> BuildClipPlaneJsonVariants(XElement clipplaneset)
        {
            var enabled = clipplaneset != null && (string)clipplaneset.Attribute("enabled") == "1";
            var linked = clipplaneset != null && (string)clipplaneset.Attribute("linked") == "1";

            var planes = new List<PlaneData>();
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
                        var alignment = (string)clipplane.Attribute("alignment") ?? "";

                        planes.Add(new PlaneData(nx, ny, nz, distance, planeEnabled, alignment));
                    }
                }
            }

            var variants = new List<(string, string)>();

            if (!enabled || planes.Count == 0)
            {
                variants.Add(("disabled", BuildTopLevel(false, linked, "")));
                return variants;
            }

            var enabledOnly = new List<PlaneData>();
            foreach (var p in planes)
            {
                if (p.Enabled) enabledOnly.Add(p);
            }

            // 1. Flat: {"Normal":{...},"Distance":n,"Enabled":bool} - all 6 planes.
            variants.Add(("flat-all6", BuildTopLevel(true, linked, EncodePlanesFlat(planes, includeAll: true))));
            // 2. Flat, only the actually-enabled planes (omit the 4 inactive ones entirely).
            variants.Add(("flat-enabledOnly", BuildTopLevel(true, linked, EncodePlanesFlat(enabledOnly, includeAll: true))));
            // 3. Nested "Plane" sub-object, mirroring the XML's own
            //    <clipplane><plane distance=".."><vec3f/></plane></clipplane>
            //    nesting - all 6 planes.
            variants.Add(("nested-all6", BuildTopLevel(true, linked, EncodePlanesNested(planes))));
            // 4. Nested, only enabled planes.
            variants.Add(("nested-enabledOnly", BuildTopLevel(true, linked, EncodePlanesNested(enabledOnly))));
            // 5. Flat but "State" as a string ("enabled"/"default") instead
            //    of an "Enabled" bool, matching the XML attribute literally -
            //    all 6 planes.
            variants.Add(("flat-state-all6", BuildTopLevel(true, linked, EncodePlanesFlatState(planes))));

            return variants;
        }

        private static string BuildTopLevel(bool enabled, bool linked, string planesJson)
        {
            return "{\"Type\":\"ClipPlaneSet\",\"Version\":1,\"Planes\":[" + planesJson + "],"
                + "\"Linked\":" + (linked ? "true" : "false") + ","
                + "\"Enabled\":" + (enabled ? "true" : "false") + "}";
        }

        private static string EncodePlanesFlat(List<PlaneData> planes, bool includeAll)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            foreach (var p in planes)
            {
                if (sb.Length > 0) sb.Append(",");
                sb.Append("{\"Normal\":{\"X\":" + p.Nx.ToString(inv) + ",\"Y\":" + p.Ny.ToString(inv) + ",\"Z\":" + p.Nz.ToString(inv) + "},"
                    + "\"Distance\":" + p.Distance.ToString(inv) + ","
                    + "\"Enabled\":" + (p.Enabled ? "true" : "false") + "}");
            }
            return sb.ToString();
        }

        private static string EncodePlanesNested(List<PlaneData> planes)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            foreach (var p in planes)
            {
                if (sb.Length > 0) sb.Append(",");
                sb.Append("{\"Enabled\":" + (p.Enabled ? "true" : "false") + ","
                    + "\"Plane\":{\"Normal\":{\"X\":" + p.Nx.ToString(inv) + ",\"Y\":" + p.Ny.ToString(inv) + ",\"Z\":" + p.Nz.ToString(inv) + "},"
                    + "\"Distance\":" + p.Distance.ToString(inv) + "}}");
            }
            return sb.ToString();
        }

        private static string EncodePlanesFlatState(List<PlaneData> planes)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            foreach (var p in planes)
            {
                if (sb.Length > 0) sb.Append(",");
                sb.Append("{\"Normal\":{\"X\":" + p.Nx.ToString(inv) + ",\"Y\":" + p.Ny.ToString(inv) + ",\"Z\":" + p.Nz.ToString(inv) + "},"
                    + "\"Distance\":" + p.Distance.ToString(inv) + ","
                    + "\"State\":\"" + (p.Enabled ? "enabled" : "default") + "\"}");
            }
            return sb.ToString();
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
