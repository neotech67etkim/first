using System;
using System.IO;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using NavisTreeExporter.Core;
using NavisTreeExporter.Export;

namespace NavisTreeExporter.Plugin
{
    /// <summary>
    /// Ribbon command that reads the selection tree of the active document
    /// and writes it out as JSON + CSV for later comparison against other
    /// design data.
    ///
    /// Vendor id "NTE" is a placeholder — replace it with your own
    /// registered Autodesk vendor code before distributing this add-in.
    ///
    /// NOTE: the Ribbon-related attributes below were written without access
    /// to the Navisworks SDK (no compiler available in this environment).
    /// Verify the exact attribute properties against the Ribbon sample that
    /// ships with the Navisworks SDK for your version before relying on this.
    /// </summary>
    [Plugin("NavisTreeExporter.ExportTree", "NTE",
        DisplayName = "Export Selection Tree",
        ToolTip = "Export the selection tree (hierarchy + properties) to JSON and CSV")]
    [RibbonTab("NavisTreeExporter.RibbonTab", DisplayName = "Tree Export")]
    [RibbonGroup("NavisTreeExporter.RibbonGroup", DisplayName = "Export")]
    [Command("NavisTreeExporter.ExportTree.Command",
        DisplayName = "Export Tree",
        ToolTip = "Export the current selection tree to JSON/CSV")]
    public class ExportTreeAddin : CommandHandlerPlugin
    {
        public override int ExecuteCommand(string name, params string[] parameters)
        {
            var document = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (document == null)
            {
                MessageBox.Show("열려 있는 Navisworks 문서가 없습니다.", "Export Selection Tree",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 0;
            }

            using (var folderDialog = new FolderBrowserDialog { Description = "내보낼 폴더를 선택하세요" })
            {
                if (folderDialog.ShowDialog() != DialogResult.OK) return 0;

                try
                {
                    var roots = ModelTreeReader.ReadTree(document);
                    var baseName = BuildBaseFileName(document);

                    var jsonPath = Path.Combine(folderDialog.SelectedPath, baseName + ".json");
                    var itemsCsvPath = Path.Combine(folderDialog.SelectedPath, baseName + "_items.csv");
                    var propertiesCsvPath = Path.Combine(folderDialog.SelectedPath, baseName + "_properties.csv");

                    JsonTreeExporter.Export(roots, jsonPath);
                    CsvTreeExporter.ExportItems(roots, itemsCsvPath);
                    CsvTreeExporter.ExportProperties(roots, propertiesCsvPath);

                    MessageBox.Show(
                        "내보내기 완료:" + Environment.NewLine +
                        jsonPath + Environment.NewLine +
                        itemsCsvPath + Environment.NewLine +
                        propertiesCsvPath,
                        "Export Selection Tree", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("내보내기 중 오류가 발생했습니다:" + Environment.NewLine + ex.Message,
                        "Export Selection Tree", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            return 0;
        }

        public override CommandState CanExecuteCommand(string name)
        {
            return new CommandState(Autodesk.Navisworks.Api.Application.ActiveDocument != null);
        }

        private static string BuildBaseFileName(Document document)
        {
            var title = string.IsNullOrWhiteSpace(document.Title) ? "NavisworksTree" : Path.GetFileNameWithoutExtension(document.Title);
            var invalidChars = Path.GetInvalidFileNameChars();
            var safeTitleChars = Array.ConvertAll(title.ToCharArray(), c => Array.IndexOf(invalidChars, c) >= 0 ? '_' : c);
            var safeTitle = new string(safeTitleChars);
            return $"{safeTitle}_{DateTime.Now:yyyyMMdd_HHmmss}";
        }
    }
}
