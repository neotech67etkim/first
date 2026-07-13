using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Autodesk.Navisworks.Api;
using NavisTreeExporter.Core;

namespace NavisTreeExporter.Plugin
{
    /// <summary>
    /// Win32 screen-capture + viewport-cropping helpers, plus the unattended
    /// auto-mode export loop, shared by the interactive ribbon button
    /// (ExportViewpointImagesAddin) and the auto-mode watcher
    /// (ExportViewpointImagesAutoWatcher). See ExportViewpointImagesAddin's
    /// class doc-comment for the capture approach's history (why it's
    /// capture-then-crop, not window hiding).
    /// </summary>
    internal static class ViewpointCaptureService
    {
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

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private static readonly IntPtr HwndNoTopmost = new IntPtr(-2);

        private const uint GwChild = 5;
        private const int SwRestore = 9;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpShowWindow = 0x0040;

        // Navisworks shows a small progress dialog titled e.g. "작업 중...
        // (29.0%)" (percentage changes as it updates) while it's actually
        // streaming/loading a file - document.Models.Count > 0 becomes true
        // well before this dialog closes on larger models, so it's a much
        // better "is loading actually done" signal than a fixed delay.
        private const string LoadingDialogTitlePrefix = "작업 중";

        // Docked panels whose window bounds are used to narrow the capture
        // crop away from - they overlay the 3D viewport's own screen region,
        // so simply cropping to the viewport's rect isn't enough. Add more
        // titles here if other panels need excluding too.
        private static readonly string[] PanelTitlesToExclude = { "선택 트리", "저장된 관측점" };

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>
        /// Restores the window if minimized and forces it to the top of the
        /// z-order so it's the one actually visible on screen. Auto mode
        /// captures whatever is physically on screen
        /// (Graphics.CopyFromScreen captures screen pixels, not a specific
        /// window's content), so if Navisworks isn't the frontmost window -
        /// still starting up behind other windows, a notification popup
        /// stealing focus, etc. - the capture ends up showing whatever is
        /// actually on top instead, which is what was happening when a
        /// screenshot came back showing an unrelated app on a different
        /// monitor, with Navisworks never visibly appearing at all.
        ///
        /// SetForegroundWindow alone isn't reliable for this: Windows
        /// restricts it so a background process generally can't steal
        /// input focus (a call from a Timer tick, as here, is exactly the
        /// kind of call that gets silently refused - the window just
        /// flashes in the taskbar instead of actually coming forward).
        /// Z-order is a separate, unrestricted thing from input focus
        /// though, so SetWindowPos with HWND_TOPMOST (then immediately
        /// HWND_NOTOPMOST, so it doesn't permanently stay pinned above
        /// everything) reliably makes it the visible/topmost window for the
        /// screenshot even when SetForegroundWindow is refused.
        /// SetForegroundWindow is still attempted too since it doesn't
        /// hurt and helps when it *is* allowed.
        ///
        /// Multi-monitor itself isn't a separate concern: GetWindowRect and
        /// CopyFromScreen both operate in the same virtual-screen
        /// coordinate space (negative coordinates for monitors to the
        /// left/above the primary are handled fine) as long as the window
        /// is actually visible on top at that location.
        /// </summary>
        internal static void BringToForeground(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;

            try
            {
                if (IsIconic(hWnd))
                {
                    ShowWindow(hWnd, SwRestore);
                }

                SetWindowPos(hWnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
                SetWindowPos(hWnd, HwndNoTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
                SetForegroundWindow(hWnd);
            }
            catch (Exception)
            {
                // Best-effort - a failed foreground/restore call shouldn't
                // abort the whole run.
            }
        }

        /// <summary>
        /// True if a visible top-level window belonging to this process has
        /// a title starting with "작업 중" (Navisworks' file-loading
        /// progress dialog). Checks all of this process's top-level windows
        /// rather than just descendants of the main window, since the
        /// progress dialog may or may not be a child of it.
        /// </summary>
        internal static bool IsLoadingDialogVisible()
        {
            var currentPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            var found = false;

            EnumWindowsProc visit = (hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out var pid);
                if (pid == currentPid && IsWindowVisible(hWnd) &&
                    GetWindowTitle(hWnd).StartsWith(LoadingDialogTitlePrefix, StringComparison.Ordinal))
                {
                    found = true;
                    return false; // stop enumerating, we have our answer
                }

                return true;
            };

            EnumWindows(visit, IntPtr.Zero);
            return found;
        }

        /// <summary>
        /// Captures the whole main window, then crops the result down to
        /// <paramref name="viewportRect"/> (falls back to the full window if
        /// that's null/couldn't be determined).
        /// </summary>
        internal static bool CaptureAndCropToViewport(IntPtr mainHandle, RECT? viewportRect, string outputPath)
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
        /// main window handle is invalid or no candidate was found.
        /// </summary>
        internal static RECT? ComputeViewportRect(IntPtr mainHandle)
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

        /// <summary>
        /// Unattended export: fixed wait per viewpoint instead of the
        /// Capture prompt, no MessageBox anywhere (nobody's watching), and
        /// the process exits itself when finished so a scheduled task that
        /// launched Navisworks headlessly-but-visibly completes cleanly.
        /// Writes a log + a _COMPLETE.txt marker into the output subfolder
        /// so a separate upload script can tell a run finished successfully
        /// before touching the files.
        /// </summary>
        internal static void RunAutoExport(Document document, AutoExportSettings settings)
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
                    log.Add($"Main window handle: {mainHandle}, title: '{GetWindowTitle(mainHandle)}'" +
                        (GetWindowRect(mainHandle, out var mainRect)
                            ? $", rect: ({mainRect.Left},{mainRect.Top})-({mainRect.Right},{mainRect.Bottom})"
                            : ", rect: <unavailable>"));

                    BringToForeground(mainHandle);
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

                        // Re-assert foreground right before capturing - the wait above
                        // is long enough for something else (a notification popup, etc.)
                        // to have stolen focus in the meantime.
                        BringToForeground(mainHandle);
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

        internal static void WriteTopLevelAutoLog(AutoExportSettings settings, string message)
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
    }
}
