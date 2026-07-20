using System;
using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;

namespace NavisTreeExporter.Plugin
{
    /// <summary>
    /// Diagnostic-only button: reads View.GetClippingPlanes() on the
    /// currently active view and shows it in a copyable message box (also
    /// puts it on the clipboard). Exists purely to answer one question -
    /// what does Navisworks' own JSON actually look like for a real,
    /// currently-*enabled* clip state - since every guessed encoding tried
    /// so far in XmlViewpointImporter has been rejected by SetClippingPlanes
    /// with the same generic ArgumentException regardless of shape, and
    /// GetClippingPlanes() has so far only been confirmed for the *disabled*
    /// default state ({"Type":"ClipPlaneSet","Version":1,"Planes":[],
    /// "Linked":false,"Enabled":false}).
    ///
    /// Usage: open any model, turn on a section/clip plane using
    /// Navisworks' own built-in Viewpoint > Sectioning tools (not this
    /// plugin), then click this button - the JSON shown is what a real
    /// enabled clip state looks like, which is what BuildClipPlaneJsonVariants
    /// needs to match.
    /// </summary>
    [Plugin("NavisTreeExporter.DebugClipPlanes", "NTE",
        DisplayName = "Debug: Show Clip Planes JSON",
        ToolTip = "Show the active view's current clip plane JSON (for matching SetClippingPlanes' expected schema)")]
    [AddInPlugin(AddInLocation.AddIn)]
    public class DebugClipPlanesAddin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            var document = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (document == null)
            {
                MessageBox.Show("열려 있는 Navisworks 문서가 없습니다.", "Debug: Show Clip Planes JSON",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 0;
            }

            try
            {
                var json = document.ActiveView.GetClippingPlanes();

                try { Clipboard.SetText(json ?? ""); } catch (Exception) { /* best-effort */ }

                MessageBox.Show(
                    "현재 클립 평면 JSON (클립보드에도 복사됨):" + Environment.NewLine + Environment.NewLine + json,
                    "Debug: Show Clip Planes JSON", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "GetClippingPlanes 호출 중 오류가 발생했습니다:" + Environment.NewLine +
                    ex.GetType().Name + ": " + ex.Message,
                    "Debug: Show Clip Planes JSON", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return 0;
        }
    }
}
