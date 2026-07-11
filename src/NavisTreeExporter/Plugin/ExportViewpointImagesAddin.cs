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
    /// Captures the whole main window, then crops to the 3D viewport's
    /// rectangle - no window hiding/showing involved, since that turned out
    /// to be risky (an earlier attempt at hiding docked panels ended up
    /// hiding an essential frame window and broke the whole layout).
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

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        private const uint GwChild = 5;

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

                // Figure out where the 3D viewport is just once, up front -
                // this is read-only (no hiding/showing), so it can't break
                // the window layout. It's the largest visible sub-window
                // under the main window, since ribbon/menus and every docked
                // pane are reliably smaller.
                var mainHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                RECT? viewportRect = mainHandle != IntPtr.Zero ? FindLargestVisibleDescendant(mainHandle) : null;

                try
                {
                    if (mainHandle == IntPtr.Zero)
                    {
                        MessageBox.Show("Navisworks 창을 찾지 못했습니다.", "Export Viewpoint Images",
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
                            // screenshot instead of the model. (This is our own small
                            // dialog, not part of the Navisworks window, so it's safe.)
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
                            if (CaptureAndCropToViewport(mainHandle, viewportRect, imagePath))
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

        /// <summary>
        /// Captures the whole main window, then crops the result down to
        /// <paramref name="viewportRect"/> (falls back to the full window if
        /// that's null/couldn't be determined).
        /// </summary>
        private static bool CaptureAndCropToViewport(IntPtr mainHandle, RECT? viewportRect, string outputPath)
        {
            if (!GetWindowRect(mainHandle, out var windowRect)) return false;

            var fullWidth = windowRect.Right - windowRect.Left;
            var fullHeight = windowRect.Bottom - windowRect.Top;
            if (fullWidth <= 0 || fullHeight <= 0) return false;

            using (var fullBitmap = new System.Drawing.Bitmap(fullWidth, fullHeight, PixelFormat.Format32bppArgb))
            {
                using (var graphics = System.Drawing.Graphics.FromImage(fullBitmap))
                {
                    graphics.CopyFromScreen(windowRect.Left, windowRect.Top, 0, 0, new System.Drawing.Size(fullWidth, fullHeight));
                }

                if (viewportRect != null)
                {
                    var v = viewportRect.Value;
                    var cropRect = new System.Drawing.Rectangle(
                        v.Left - windowRect.Left,
                        v.Top - windowRect.Top,
                        v.Right - v.Left,
                        v.Bottom - v.Top);
                    cropRect.Intersect(new System.Drawing.Rectangle(0, 0, fullWidth, fullHeight));

                    if (cropRect.Width > 0 && cropRect.Height > 0)
                    {
                        using (var cropped = fullBitmap.Clone(cropRect, fullBitmap.PixelFormat))
                        {
                            cropped.Save(outputPath, ImageFormat.Png);
                        }
                        return true;
                    }
                }

                fullBitmap.Save(outputPath, ImageFormat.Png);
                return true;
            }
        }

        /// <summary>
        /// Finds the largest visible *leaf* window (no child windows of its
        /// own) under <paramref name="parent"/>, recursing through all
        /// descendants. Docking frameworks typically nest the 3D viewport's
        /// actual rendering surface a few levels deep inside container
        /// windows that also host the docked panes as siblings - comparing
        /// every visible window regardless of depth picked one of those
        /// outer containers instead (still including both side panels).
        /// Restricting to leaf windows targets the actual rendering surface,
        /// which reliably has no children of its own.
        /// </summary>
        private static RECT? FindLargestVisibleDescendant(IntPtr parent)
        {
            RECT? largest = null;
            long largestArea = 0;

            EnumWindowsProc visit = null;
            visit = (hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd) && GetWindow(hWnd, GwChild) == IntPtr.Zero && GetWindowRect(hWnd, out var rect))
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
