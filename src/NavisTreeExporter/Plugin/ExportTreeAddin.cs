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
    /// Reads the selection tree of the active document and writes it out as
    /// JSON + CSV for later comparison against other design data.
    ///
    /// Registered as an AddInPlugin, so Navisworks places a button for it
    /// under the ribbon's "Add-ins" tab automatically — no custom ribbon
    /// tab/group layout is required.
    ///
    /// Vendor id "NTE" is a placeholder — replace it with your own
    /// registered Autodesk vendor code before distributing this add-in.
    /// </summary>
    [Plugin("NavisTreeExporter.ExportTree", "NTE",
        DisplayName = "Export Selection Tree",
        ToolTip = "Export the selection tree (hierarchy + properties) to JSON and CSV")]
    [AddInPlugin(AddInLocation.AddIn)]
    public class ExportTreeAddin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            var document = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (document == null)
            {
                MessageBox.Show("열려 있는 Navisworks 문서가 없습니다.", "Export Selection Tree",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 0;
            }

            ExportDetailLevel detailLevel;
            bool geometryOnly;
            using (var optionsForm = new ExportOptionsForm())
            {
                optionsForm.ShowDialog();
                if (optionsForm.SelectedLevel == null) return 0;
                detailLevel = optionsForm.SelectedLevel.Value;
                geometryOnly = optionsForm.GeometryOnly;
            }

            var includeProperties = detailLevel == ExportDetailLevel.HierarchyAndProperties;

            using (var folderDialog = new FolderBrowserDialog { Description = "내보낼 폴더를 선택하세요" })
            {
                if (folderDialog.ShowDialog() != DialogResult.OK) return 0;

                using (var progressForm = new ExportProgressForm())
                {
                    progressForm.Show();
                    progressForm.SetIndeterminate("내보내는 중...");
                    progressForm.Refresh();
                    System.Windows.Forms.Application.DoEvents();

                    var baseName = BuildBaseFileName(document);
                    var jsonPath = Path.Combine(folderDialog.SelectedPath, baseName + ".json");
                    var itemsCsvPath = Path.Combine(folderDialog.SelectedPath, baseName + "_items.csv");
                    var propertiesCsvPath = includeProperties
                        ? Path.Combine(folderDialog.SelectedPath, baseName + "_properties.csv")
                        : null;

                    try
                    {
                        var progress = new ExportProgressReporter(
                            processed =>
                            {
                                progressForm.ReportProgress(processed);
                                System.Windows.Forms.Application.DoEvents();
                            },
                            () => progressForm.CancelRequested);

                        // Streams the tree straight to disk (JSON + CSV) as it's
                        // walked, so the full tree/properties never sit in memory
                        // at once - important on large models with properties
                        // included, where that used to exhaust system memory.
                        TreeExportWriter.Export(document, detailLevel, geometryOnly, jsonPath, itemsCsvPath, propertiesCsvPath, progress);

                        var resultMessage =
                            "내보내기 완료:" + Environment.NewLine +
                            jsonPath + Environment.NewLine +
                            itemsCsvPath;

                        if (includeProperties)
                        {
                            resultMessage += Environment.NewLine + propertiesCsvPath;
                        }

                        progressForm.Close();

                        MessageBox.Show(resultMessage, "Export Selection Tree",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (OperationCanceledException)
                    {
                        progressForm.Close();
                        DeleteIfExists(jsonPath);
                        DeleteIfExists(itemsCsvPath);
                        DeleteIfExists(propertiesCsvPath);
                        MessageBox.Show("내보내기가 취소되었습니다.", "Export Selection Tree",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        progressForm.Close();
                        DeleteIfExists(jsonPath);
                        DeleteIfExists(itemsCsvPath);
                        DeleteIfExists(propertiesCsvPath);
                        MessageBox.Show(
                            "내보내기 중 오류가 발생했습니다:" + Environment.NewLine +
                            ex.GetType().Name + ": " + ex.Message + Environment.NewLine + Environment.NewLine +
                            ex.StackTrace,
                            "Export Selection Tree", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }

            return 0;
        }

        private static void DeleteIfExists(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            try
            {
                if (File.Exists(filePath)) File.Delete(filePath);
            }
            catch (IOException)
            {
                // Best-effort cleanup - leaving a partial file behind on a
                // locked-file edge case isn't worth failing the cancel path over.
            }
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
