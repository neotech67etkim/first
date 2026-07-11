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
    /// screenshot of just the 3D viewport as a PNG. Requires the Navisworks
    /// window to stay visible/unminimized for the duration (it's a real
    /// screen capture, not an off-screen render).
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

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SwHide = 0;
        private const int SwShow = 5;

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

                // Find the 3D viewport once (the largest visible sub-window
                // under the main window - ribbon/menus and every docked pane
                // are reliably smaller), then hide every other titled window
                // under the main window so none of them can end up in the
                // screenshot, regardless of which panels happen to be open.
                var mainHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                var viewport = mainHandle != IntPtr.Zero ? FindLargestVisibleDescendant(mainHandle) : null;
                var captureRect = viewport?.Rect ?? GetRectOrNull(mainHandle);
                var hiddenPanels = (mainHandle != IntPtr.Zero && viewport != null)
                    ? HideOtherTitledPanels(mainHandle, viewport.Value.Handle)
                    : new List<IntPtr>();
                System.Windows.Forms.Application.DoEvents();

                try
                {
                    if (captureRect == null)
                    {
                        MessageBox.Show("캡처할 화면 영역을 찾지 못했습니다.", "Export Viewpoint Images",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return 0;
                    }

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
                            if (CaptureRegion(captureRect.Value, imagePath))
                            {
                                savedCount++;
                            }
                        }
                    }

                    RestorePanels(hiddenPanels);
                    System.Windows.Forms.Application.DoEvents();

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
                finally
                {
                    // Safety net in case an exception skipped the normal
                    // restore above - ShowWindow on an already-visible
                    // window is a harmless no-op.
                    RestorePanels(hiddenPanels);
                }
            }

            return 0;
        }

        private static bool CaptureRegion(RECT rect, string outputPath)
        {
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
            return handle != IntPtr.Zero && GetWindowRect(handle, out var rect) ? rect : (RECT?)null;
        }

        /// <summary>
        /// Finds the largest visible window under <paramref name="parent"/>
        /// (recursing through all descendants, not just direct children).
        /// Ribbon/menus and every docked pane are reliably smaller than the
        /// 3D viewport, so "largest" is a decent stand-in for "the viewport"
        /// without needing to know its exact window class name.
        /// </summary>
        private static (IntPtr Handle, RECT Rect)? FindLargestVisibleDescendant(IntPtr parent)
        {
            IntPtr largestHandle = IntPtr.Zero;
            RECT largestRect = default;
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
                        largestRect = rect;
                        largestHandle = hWnd;
                    }
                }

                // EnumChildWindows only walks direct children, so recurse
                // manually to reach grandchildren (docked panes, the 3D
                // viewport control, etc. are usually nested a few levels deep).
                EnumChildWindows(hWnd, visit, IntPtr.Zero);
                return true;
            };

            EnumChildWindows(parent, visit, IntPtr.Zero);
            return largestHandle == IntPtr.Zero ? ((IntPtr, RECT)?)null : (largestHandle, largestRect);
        }

        /// <summary>
        /// Hides every visible descendant window under <paramref name="mainHandle"/>
        /// that has a non-empty window title (docked panes like Selection
        /// Tree, Saved Viewpoints, Properties, etc. all set their caption
        /// text as the window's title) except <paramref name="keepHandle"/>
        /// (the 3D viewport). Generic/title-based rather than hardcoding
        /// specific panel names, so any open panel gets hidden, not just
        /// the ones tested so far.
        /// </summary>
        private static List<IntPtr> HideOtherTitledPanels(IntPtr mainHandle, IntPtr keepHandle)
        {
            var hidden = new List<IntPtr>();

            EnumWindowsProc visit = null;
            visit = (hWnd, lParam) =>
            {
                if (hWnd != keepHandle && IsWindowVisible(hWnd) && !string.IsNullOrWhiteSpace(GetWindowTitle(hWnd)))
                {
                    ShowWindow(hWnd, SwHide);
                    hidden.Add(hWnd);
                }

                EnumChildWindows(hWnd, visit, IntPtr.Zero);
                return true;
            };

            EnumChildWindows(mainHandle, visit, IntPtr.Zero);
            return hidden;
        }

        private static void RestorePanels(List<IntPtr> handles)
        {
            foreach (var hWnd in handles)
            {
                ShowWindow(hWnd, SwShow);
            }
        }

        private static string GetWindowTitle(IntPtr hWnd)
        {
            var buffer = new System.Text.StringBuilder(256);
            GetWindowText(hWnd, buffer, buffer.Capacity);
            return buffer.ToString();
        }
    }
}
