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

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

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

                var savedCount = 0;
                var skippedCount = 0;
                var cancelled = false;
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                try
                {
                    for (var i = 0; i < viewpoints.Count; i++)
                    {
                        var (path, viewpoint) = viewpoints[i];

                        try
                        {
                            document.CurrentViewpoint.CopyFrom(viewpoint.Viewpoint);
                        }
                        catch (Exception)
                        {
                            continue; // skip viewpoints that fail to apply, keep going
                        }

                        System.Windows.Forms.Application.DoEvents();

                        using (var prompt = new CapturePromptForm())
                        {
                            prompt.SetStatus($"({i + 1} / {viewpoints.Count}) {path}" + Environment.NewLine +
                                "로딩이 끝나면 캡처를 누르세요.");
                            prompt.Show();

                            while (prompt.Result == null)
                            {
                                System.Windows.Forms.Application.DoEvents();
                                System.Threading.Thread.Sleep(30);
                            }

                            var result = prompt.Result.Value;

                            if (result == CapturePromptForm.PromptResult.Cancel)
                            {
                                cancelled = true;
                                break;
                            }

                            if (result == CapturePromptForm.PromptResult.Skip)
                            {
                                skippedCount++;
                                continue;
                            }

                            // Hide the prompt itself before capturing - otherwise it's
                            // sitting on top of the Navisworks view and ends up in the
                            // screenshot instead of the model.
                            prompt.Hide();
                            for (var pump = 0; pump < 3; pump++)
                            {
                                System.Windows.Forms.Application.DoEvents();
                                System.Threading.Thread.Sleep(30);
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
                            if (CaptureMainWindow(imagePath))
                            {
                                savedCount++;
                            }
                        }
                    }

                    var summary = cancelled
                        ? $"취소됨: {savedCount} / {viewpoints.Count}개 이미지를 저장한 상태에서 중단했습니다."
                        : $"완료: {savedCount} / {viewpoints.Count}개 이미지를 저장했습니다.";
                    if (skippedCount > 0)
                    {
                        summary += Environment.NewLine + $"건너뛴 관측점: {skippedCount}개";
                    }

                    MessageBox.Show(summary + Environment.NewLine + folderDialog.SelectedPath,
                        "Export Viewpoint Images", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "내보내기 중 오류가 발생했습니다:" + Environment.NewLine +
                        ex.GetType().Name + ": " + ex.Message + Environment.NewLine + Environment.NewLine +
                        ex.StackTrace,
                        "Export Viewpoint Images", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            return 0;
        }

        private static bool CaptureMainWindow(string outputPath)
        {
            var mainHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            if (mainHandle == IntPtr.Zero) return false;

            // Ribbon/menus, the Selection Tree pane, and the Saved Viewpoints
            // pane are all much smaller than the 3D view, so picking the
            // largest visible sub-window under the main window is a decent
            // heuristic for "just the 3D viewport" without needing to know
            // its exact window class name.
            var viewportRect = FindLargestVisibleDescendant(mainHandle) ?? GetRectOrNull(mainHandle);
            if (viewportRect == null) return false;

            var rect = viewportRect.Value;
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

        private static RECT? GetRectOrNull(IntPtr handle)
        {
            return GetWindowRect(handle, out var rect) ? rect : (RECT?)null;
        }

        private static RECT? FindLargestVisibleDescendant(IntPtr parent)
        {
            RECT? largest = null;
            long largestArea = 0;

            EnumWindowsProc visit = null;
            visit = (hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd) && GetWindowRect(hWnd, out var rect))
                {
                    long area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
                    if (area > largestArea)
                    {
                        largestArea = area;
                        largest = rect;
                    }
                }

                // EnumChildWindows only walks direct children, so recurse
                // manually to reach grandchildren (docked panes, the 3D
                // viewport control, etc. are usually nested a few levels deep).
                EnumChildWindows(hWnd, visit, IntPtr.Zero);
                return true;
            };

            EnumChildWindows(parent, visit, IntPtr.Zero);
            return largest;
        }
    }
}
