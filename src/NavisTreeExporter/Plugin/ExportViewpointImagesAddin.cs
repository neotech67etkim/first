using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using NavisTreeExporter.Core;

namespace NavisTreeExporter.Plugin
{
    /// <summary>
    /// Moves the camera to each saved viewpoint in turn and saves a
    /// screenshot of the Navisworks main window as a PNG. Requires the
    /// Navisworks window to stay visible/unminimized for the duration (it's
    /// a real screen capture, not an off-screen render).
    ///
    /// NOTE: the saved-viewpoint-restoration call
    /// (Document.CurrentViewpoint.CopyFrom) is unverified against the real
    /// Navisworks SDK - see SavedViewpointCollector for the same caveat on
    /// the tree-walking side.
    /// </summary>
    [Plugin("NavisTreeExporter.ExportViewpointImages", "NTE",
        DisplayName = "Export Viewpoint Images",
        ToolTip = "Move the camera to each saved viewpoint and save a screenshot of each")]
    [AddInPlugin(AddInLocation.AddIn)]
    public class ExportViewpointImagesAddin : AddInPlugin
    {
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public override int Execute(params string[] parameters)
        {
            var document = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (document == null)
            {
                MessageBox.Show("열려 있는 Navisworks 문서가 없습니다.", "Export Viewpoint Images",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 0;
            }

            List<(string Path, SavedViewpoint Viewpoint)> viewpoints;
            try
            {
                viewpoints = SavedViewpointCollector.Collect(document);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "저장된 관측점을 읽는 중 오류가 발생했습니다:" + Environment.NewLine +
                    ex.GetType().Name + ": " + ex.Message,
                    "Export Viewpoint Images", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 0;
            }

            if (viewpoints.Count == 0)
            {
                MessageBox.Show("저장된 관측점이 없습니다.", "Export Viewpoint Images",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            using (var folderDialog = new FolderBrowserDialog { Description = "이미지를 저장할 폴더를 선택하세요" })
            {
                if (folderDialog.ShowDialog() != DialogResult.OK) return 0;

                using (var progressForm = new ExportProgressForm())
                {
                    progressForm.Show();
                    progressForm.SetIndeterminate($"관측점 캡처 준비 중... (0 / {viewpoints.Count})");
                    progressForm.Refresh();
                    System.Windows.Forms.Application.DoEvents();

                    var savedCount = 0;
                    var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    try
                    {
                        for (var i = 0; i < viewpoints.Count; i++)
                        {
                            if (progressForm.CancelRequested) break;

                            var (path, viewpoint) = viewpoints[i];
                            progressForm.SetIndeterminate($"관측점 캡처 중... ({i + 1} / {viewpoints.Count}) {path}");
                            System.Windows.Forms.Application.DoEvents();

                            try
                            {
                                document.CurrentViewpoint.CopyFrom(viewpoint.Viewpoint);
                            }
                            catch (Exception)
                            {
                                continue; // skip viewpoints that fail to apply, keep going
                            }

                            // Hide our own (TopMost) progress window before capturing -
                            // otherwise it's sitting on top of the Navisworks view and
                            // ends up in the screenshot instead of the model.
                            progressForm.Hide();

                            // Pump messages + a short sleep so the progress window
                            // actually disappears and the 3D view finishes redrawing
                            // before we grab the screen - DoEvents alone doesn't
                            // guarantee either has happened yet.
                            for (var pump = 0; pump < 5; pump++)
                            {
                                System.Windows.Forms.Application.DoEvents();
                                System.Threading.Thread.Sleep(60);
                            }

                            var fileName = FileNameSanitizer.Sanitize(path, "Viewpoint" + i);
                            var uniqueFileName = fileName;
                            var suffix = 1;
                            while (!usedNames.Add(uniqueFileName))
                            {
                                uniqueFileName = fileName + "_" + suffix;
                                suffix++;
                            }

                            var imagePath = Path.Combine(folderDialog.SelectedPath, uniqueFileName + ".png");
                            var captured = CaptureMainWindow(imagePath);

                            progressForm.Show();
                            System.Windows.Forms.Application.DoEvents();

                            if (captured)
                            {
                                savedCount++;
                            }
                        }

                        progressForm.Close();

                        MessageBox.Show(
                            $"완료: {savedCount} / {viewpoints.Count}개 이미지를 저장했습니다." + Environment.NewLine +
                            folderDialog.SelectedPath,
                            "Export Viewpoint Images", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        progressForm.Close();
                        MessageBox.Show(
                            "내보내기 중 오류가 발생했습니다:" + Environment.NewLine +
                            ex.GetType().Name + ": " + ex.Message + Environment.NewLine + Environment.NewLine +
                            ex.StackTrace,
                            "Export Viewpoint Images", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }

            return 0;
        }

        private static bool CaptureMainWindow(string outputPath)
        {
            var handle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            if (handle == IntPtr.Zero) return false;
            if (!GetWindowRect(handle, out var rect)) return false;

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return false;

            using (var bitmap = new System.Drawing.Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new System.Drawing.Size(width, height));
                bitmap.Save(outputPath, ImageFormat.Png);
            }

            return true;
        }
    }
}
