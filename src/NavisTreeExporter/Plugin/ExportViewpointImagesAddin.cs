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
    /// Captures the whole main window, then crops the result - no window
    /// hiding/showing involved (an earlier attempt at hiding docked panels
    /// ended up hiding an essential frame window and broke the whole
    /// layout). The Selection Tree / Saved Viewpoints panels are drawn as an
    /// overlay directly on top of the 3D viewport's own window region rather
    /// than shrinking it, so the crop rectangle is narrowed using those
    /// panels' own window bounds (read-only lookups) instead of trusting the
    /// viewport's rect alone.
    ///
    /// NOTE: the saved-viewpoint-restoration call
    /// (Document.CurrentViewpoint.CopyFrom) is unverified against the real
    /// Navisworks SDK - see SavedViewpointCollector for the same caveat on
    /// the tree-walking side.
    ///
    /// Also supports an unattended "auto mode" for daily scheduled runs
    /// (see AutoExportSettings): when NAVIS_AUTO_EXPORT_IMAGES=1 is set in
    /// the environment before Navisworks starts (e.g. by a scheduled-task
    /// script that also passes a file to open on the command line), Load()
    /// starts polling for a document to finish opening and then runs the
    /// whole export with no dialogs - a fixed wait per viewpoint instead of
    /// the interactive Capture prompt, log files instead of MessageBox, and
    /// the process exits itself when done so the scheduled task completes.
    /// The Load()/Unload() override names and the "is a document loaded"
    /// check (document.Models.Count) are unverified against the real SDK,
    /// same caveat as the rest of this file.
    /// </summary>
    [Plugin("NavisTreeExporter.ExportViewpointImages", "NTE",
        DisplayName = "Export Viewpoint Images",
        ToolTip = "Move the camera to each saved viewpoint and save a screenshot of each")]
    [AddInPlugin(AddInLocation.AddIn)]
    public class ExportViewpointImagesAddin : AddInPlugin
    {
        private const int AutoModeTimeoutMinutes = 10;

        private System.Windows.Forms.Timer _autoTimer;
        private AutoExportSettings _autoSettings;
        private DateTime _autoStartedAtUtc;

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        private const uint GwChild = 5;

        // Docked panels whose window bounds are used to narrow the capture
        // crop away from - they overlay the 3D viewport's own screen region,
        // so simply cropping to the viewport's rect isn't enough. Add more
        // titles here if other panels need excluding too.
        private static readonly string[] PanelTitlesToExclude = { "선택 트리", "저장된 관측점" };

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>
        /// Called once when Navisworks loads the plugin. If auto mode is
        /// requested via environment variables, starts polling for a
        /// document to open so the export can run unattended - otherwise a
        /// no-op, leaving the normal ribbon-button behavior untouched.
        /// </summary>
        public override void Load()
        {
            var settings = AutoExportSettings.FromEnvironment();
            if (settings == null) return;

            _autoSettings = settings;
            _autoStartedAtUtc = DateTime.UtcNow;
            _autoTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _autoTimer.Tick += AutoTimer_Tick;
            _autoTimer.Start();
        }

        public override void Unload()
        {
            _autoTimer?.Stop();
            _autoTimer?.Dispose();
            _autoTimer = null;
        }

        private void AutoTimer_Tick(object sender, EventArgs e)
        {
            if ((DateTime.UtcNow - _autoStartedAtUtc).TotalMinutes > AutoModeTimeoutMinutes)
            {
                _autoTimer.Stop();
                WriteTopLevelAutoLog(_autoSettings, "No document finished opening within " +
                    AutoModeTimeoutMinutes + " minutes - giving up.");
                Environment.Exit(1);
                return;
            }

            Document document;
            try
            {
                document = Autodesk.Navisworks.Api.Application.ActiveDocument;
            }
            catch (Exception)
            {
                return;
            }

            if (document == null) return;

            bool hasModel;
            try
            {
                hasModel = document.Models.Count > 0;
            }
            catch (Exception)
            {
                hasModel = false;
            }

            if (!hasModel) return;

            _autoTimer.Stop();
            RunAutoExport(document, _autoSettings);
        }

        /// <summary>
        /// Unattended export: fixed wait per viewpoint instead of the
        /// Capture prompt, no MessageBox anywhere (nobody's watching), and
        /// the process exits itself when finished so a scheduled task that
        /// launched Navisworks headlessly-but-visibly completes cleanly.
        /// Writes a log + a _COMPLETE.txt marker into the output subfolder
        /// so a separate upload script can tell a run finished successfully
        /// before touching the files.
        /// </summary>
        private static void RunAutoExport(Document document, AutoExportSettings settings)
        {
            var log = new List<string>();
            var savedCount = 0;
            var totalCount = 0;
            string subfolder = null;

            try
            {
                var viewpoints = SavedViewpointCollector.Collect(document);
                totalCount = viewpoints.Count;
                log.Add($"[{DateTime.Now:O}] Found {totalCount} saved viewpoint(s).");

                subfolder = Path.Combine(settings.OutputDir, BuildRunFolderName(document));
                Directory.CreateDirectory(subfolder);

                var mainHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (mainHandle == IntPtr.Zero)
                {
                    log.Add("Could not find the Navisworks main window handle - aborting.");
                }
                else
                {
                    var viewportRect = ComputeViewportRect(mainHandle);
                    var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    for (var i = 0; i < viewpoints.Count; i++)
                    {
                        var (path, viewpoint) = viewpoints[i];

                        try
                        {
                            document.CurrentViewpoint.CopyFrom(viewpoint.Viewpoint);
                        }
                        catch (Exception ex)
                        {
                            log.Add($"[skip] {path}: failed to apply viewpoint ({ex.Message})");
                            continue;
                        }

                        System.Windows.Forms.Application.DoEvents();
                        System.Threading.Thread.Sleep(settings.WaitSeconds * 1000);
                        System.Windows.Forms.Application.DoEvents();

                        var fileName = FileNameSanitizer.Sanitize(path, "Viewpoint" + i);
                        var uniqueFileName = fileName;
                        var suffix = 1;
                        while (!usedNames.Add(uniqueFileName))
                        {
                            uniqueFileName = fileName + "_" + suffix;
                            suffix++;
                        }

                        var imagePath = Path.Combine(subfolder, uniqueFileName + ".png");
                        if (CaptureAndCropToViewport(mainHandle, viewportRect, imagePath))
                        {
                            savedCount++;
                            log.Add($"[ok] {path} -> {uniqueFileName}.png");
                        }
                        else
                        {
                            log.Add($"[fail] {path}: capture failed");
                        }
                    }
                }

                log.Add($"[{DateTime.Now:O}] Done: {savedCount} / {totalCount} saved.");
                File.WriteAllLines(Path.Combine(subfolder, "_export_log.txt"), log);
                File.WriteAllText(Path.Combine(subfolder, "_COMPLETE.txt"),
                    $"saved={savedCount}{Environment.NewLine}total={totalCount}{Environment.NewLine}finishedAt={DateTime.Now:O}");
            }
            catch (Exception ex)
            {
                log.Add($"[{DateTime.Now:O}] FAILED: {ex.GetType().Name}: {ex.Message}");
                log.Add(ex.StackTrace);
                try
                {
                    if (subfolder != null)
                    {
                        Directory.CreateDirectory(subfolder);
                        File.WriteAllLines(Path.Combine(subfolder, "_export_log.txt"), log);
                    }
                    else
                    {
                        WriteTopLevelAutoLog(settings, string.Join(Environment.NewLine, log));
                    }
                }
                catch (Exception)
                {
                    // Best-effort logging only - don't let a logging failure
                    // stop the process from exiting.
                }
            }
            finally
            {
                Environment.Exit(0);
            }
        }

        private static string BuildRunFolderName(Document document)
        {
            string baseName;
            try
            {
                baseName = Path.GetFileNameWithoutExtension(document.CurrentFileName);
            }
            catch (Exception)
            {
                baseName = null;
            }

            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "Model";
            }

            var sanitized = FileNameSanitizer.Sanitize(baseName, "Model");
            return sanitized + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }

        private static void WriteTopLevelAutoLog(AutoExportSettings settings, string message)
        {
            try
            {
                Directory.CreateDirectory(settings.OutputDir);
                File.AppendAllText(
                    Path.Combine(settings.OutputDir, "_auto_export_errors.log"),
                    $"[{DateTime.Now:O}] {message}{Environment.NewLine}");
            }
            catch (Exception)
            {
                // Nothing more we can do - there's no user to show a dialog to.
            }
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
                // this is entirely read-only (no hiding/showing), so it
                // can't break the window layout. Start from the largest
                // visible leaf window, then narrow it away from any docked
                // panel that overlaps it horizontally.
                var mainHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                var viewportRect = ComputeViewportRect(mainHandle);

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
        /// Locates the 3D viewport's rect (largest visible leaf window,
        /// narrowed away from any overlapping docked panel), or null if the
        /// main window handle is invalid or no candidate was found. Shared
        /// by the interactive Execute() path and the unattended auto-mode
        /// path so both crop the same way.
        /// </summary>
        private static RECT? ComputeViewportRect(IntPtr mainHandle)
        {
            if (mainHandle == IntPtr.Zero) return null;

            var rect = FindLargestVisibleDescendant(mainHandle);
            if (rect == null) return null;

            return NarrowAwayFromPanels(mainHandle, rect.Value);
        }

        /// <summary>
        /// Finds the largest visible *leaf* window (no child windows of its
        /// own) under <paramref name="parent"/>, recursing through all
        /// descendants. Docking frameworks typically nest the 3D viewport's
        /// actual rendering surface a few levels deep inside container
        /// windows that also host the docked panes as siblings - comparing
        /// every visible window regardless of depth picked one of those
        /// outer containers instead. Restricting to leaf windows targets the
        /// actual rendering surface, which reliably has no children of its own.
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

        /// <summary>
        /// Pulls the left/right edges of <paramref name="viewport"/> in to
        /// exclude any panel in <see cref="PanelTitlesToExclude"/> that
        /// overlaps it - whichever side of the viewport's center each panel
        /// sits on.
        /// </summary>
        private static RECT NarrowAwayFromPanels(IntPtr mainHandle, RECT viewport)
        {
            var centerX = (viewport.Left + viewport.Right) / 2;

            foreach (var title in PanelTitlesToExclude)
            {
                var panelRect = FindWindowRectByTitle(mainHandle, title);
                if (panelRect == null) continue;

                var p = panelRect.Value;
                var panelCenterX = (p.Left + p.Right) / 2;

                if (panelCenterX < centerX)
                {
                    viewport.Left = Math.Max(viewport.Left, p.Right);
                }
                else
                {
                    viewport.Right = Math.Min(viewport.Right, p.Left);
                }
            }

            return viewport;
        }

        private static RECT? FindWindowRectByTitle(IntPtr parent, string title)
        {
            RECT? found = null;

            EnumWindowsProc visit = null;
            visit = (hWnd, lParam) =>
            {
                if (found == null && IsWindowVisible(hWnd) && GetWindowTitle(hWnd) == title && GetWindowRect(hWnd, out var rect))
                {
                    found = rect;
                    return true;
                }

                EnumChildWindows(hWnd, visit, IntPtr.Zero);
                return true;
            };

            EnumChildWindows(parent, visit, IntPtr.Zero);
            return found;
        }

        private static string GetWindowTitle(IntPtr hWnd)
        {
            var buffer = new System.Text.StringBuilder(256);
            GetWindowText(hWnd, buffer, buffer.Capacity);
            return buffer.ToString();
        }
    }
}
