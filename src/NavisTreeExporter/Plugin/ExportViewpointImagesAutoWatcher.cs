using System;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using NavisTreeExporter.Core;

namespace NavisTreeExporter.Plugin
{
    /// <summary>
    /// Bootstraps the unattended "auto mode" for Export Viewpoint Images.
    ///
    /// AddInPlugin (ExportViewpointImagesAddin) only runs when its ribbon
    /// button is clicked - a first attempt at hooking a Load()/Unload()
    /// override directly on that class failed with CS0115 ("no suitable
    /// method to override") on a real build, confirming AddInPlugin has no
    /// such hook. EventWatcherPlugin is the SDK's actual mechanism for code
    /// that needs to run automatically as soon as Navisworks loads plugins,
    /// via OnLoaded()/OnUnloading() overrides - the standard place to
    /// subscribe/unsubscribe API events for a plugin's whole lifetime.
    ///
    /// This class doesn't do any event-watching of its own; it just uses
    /// EventWatcherPlugin as an "always instantiated at startup" hook to
    /// start polling for a document to open when NAVIS_AUTO_EXPORT_IMAGES=1
    /// is set (see AutoExportSettings). The actual capture loop lives in
    /// ViewpointCaptureService.RunAutoExport, shared with the interactive
    /// button's code.
    ///
    /// NOTE: EventWatcherPlugin as the eager-instantiation mechanism,
    /// document.Models.Count as the "is a document loaded" signal, and the
    /// "작업 중" loading-dialog title prefix used to detect when the file
    /// has actually finished loading (see ViewpointCaptureService) are all
    /// unverified against the real SDK/locale - same caveat as the rest of
    /// this plugin, subject to adjustment from real build/runtime errors.
    /// </summary>
    [Plugin("NavisTreeExporter.ExportViewpointImagesAutoWatcher", "NTE",
        DisplayName = "Export Viewpoint Images Auto Watcher")]
    public class ExportViewpointImagesAutoWatcher : EventWatcherPlugin
    {
        private const int AutoModeTimeoutMinutes = 10;
        private const int LoadingDialogTimeoutMinutes = 20;

        private System.Windows.Forms.Timer _autoTimer;
        private AutoExportSettings _autoSettings;
        private DateTime _autoStartedAtUtc;

        public override void OnLoaded()
        {
            var settings = AutoExportSettings.FromEnvironment();
            if (settings == null) return; // interactive-only session, nothing to do

            _autoSettings = settings;
            _autoStartedAtUtc = DateTime.UtcNow;
            _autoTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _autoTimer.Tick += AutoTimer_Tick;
            _autoTimer.Start();
        }

        public override void OnUnloading()
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
                ViewpointCaptureService.WriteTopLevelAutoLog(_autoSettings, "No document finished opening within " +
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

            // Every stage below gets a timestamped entry here, and the
            // whole list is handed to RunAutoExport, which appends its own
            // per-viewpoint entries and writes it all out as one continuous
            // _export_log.txt - so a run's whole timeline (not just the
            // per-viewpoint part) is visible in one place to see exactly
            // where it's spending time or going wrong.
            var log = new System.Collections.Generic.List<string>();
            var runStopwatch = System.Diagnostics.Stopwatch.StartNew();
            log.Add($"[T+{runStopwatch.Elapsed.TotalSeconds:0.0}s] Document detected (Models.Count > 0).");

            // Bring the window to the front right away - it needs to stay
            // frontmost/unoccluded for the whole run since the capture is a
            // real screen grab (Graphics.CopyFromScreen), not an off-screen
            // render. Otherwise geometry can keep loading behind other
            // windows and the eventual screenshot shows whatever else was
            // on top instead of the model.
            var mainHandle = ViewpointCaptureService.FindNavisworksMainWindow();
            log.Add($"[T+{runStopwatch.Elapsed.TotalSeconds:0.0}s] Main window: {ViewpointCaptureService.DescribeWindow(mainHandle)}");
            ViewpointCaptureService.BringToForeground(mainHandle);

            // Unconditional floor before checking for the loading dialog at
            // all: on a real run there's a real gap between Models.Count
            // becoming nonzero and Navisworks' own "작업 중" loading dialog
            // actually appearing on screen (plus more delay before the
            // dialog shows up in the first place after the process
            // launches). Checking for the dialog during that gap sees
            // nothing yet and wrongly concludes loading is already done -
            // this plain sleep (not stability-based, always taken in full)
            // outlasts that gap so the reactive dialog-wait below only
            // starts once the dialog has had a real chance to show up.
            log.Add($"[T+{runStopwatch.Elapsed.TotalSeconds:0.0}s] Starting mandatory min-wait " +
                $"({_autoSettings.MinInitialWaitSeconds}s, before checking for the loading dialog)...");
            var minWaitStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var minWaitUntil = DateTime.UtcNow.AddSeconds(_autoSettings.MinInitialWaitSeconds);
            while (DateTime.UtcNow < minWaitUntil)
            {
                ViewpointCaptureService.BringToForeground(mainHandle);
                System.Windows.Forms.Application.DoEvents();
                System.Threading.Thread.Sleep(500);
            }
            log.Add($"[T+{runStopwatch.Elapsed.TotalSeconds:0.0}s] Min-wait done (actual {minWaitStopwatch.Elapsed.TotalSeconds:0.0}s).");

            // Models.Count > 0 fires well before Navisworks' own file-loading
            // progress dialog ("작업 중... (NN.N%)") actually closes on
            // larger models - wait for that dialog to disappear instead of
            // guessing a fixed delay, since load time varies a lot by file
            // size. Falls back to just proceeding if it somehow never shows
            // up or never closes within the timeout, so a detection miss
            // can't hang the run forever.
            log.Add($"[T+{runStopwatch.Elapsed.TotalSeconds:0.0}s] Watching for the loading dialog ('작업 중...') to close " +
                $"(timeout {LoadingDialogTimeoutMinutes}m)...");
            var dialogStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var dialogWasSeenVisible = false;
            var loadingTimeoutAt = DateTime.UtcNow.AddMinutes(LoadingDialogTimeoutMinutes);
            while (true)
            {
                var visible = ViewpointCaptureService.IsLoadingDialogVisible();
                if (visible) dialogWasSeenVisible = true;
                if (!visible || DateTime.UtcNow >= loadingTimeoutAt) break;

                System.Windows.Forms.Application.DoEvents();
                System.Threading.Thread.Sleep(250);
            }
            log.Add(dialogWasSeenVisible
                ? $"[T+{runStopwatch.Elapsed.TotalSeconds:0.0}s] Loading dialog closed (was visible for {dialogStopwatch.Elapsed.TotalSeconds:0.0}s)."
                : $"[T+{runStopwatch.Elapsed.TotalSeconds:0.0}s] Loading dialog was never seen visible " +
                  $"(checked for {dialogStopwatch.Elapsed.TotalSeconds:0.0}s - either it never showed, or it closed before the min-wait above finished).");

            // Extra grace period after the loading dialog closes - rendering
            // can still catch up for a moment even once loading itself is
            // done. This used to be a stability check (wait for two/several
            // consecutive captures to look the same) instead of a flat
            // sleep, but on a real run with a large model it kept declaring
            // "stable" during a multi-second pause between geometry
            // streaming bursts, well before the model had actually finished
            // - the view genuinely doesn't change for stretches of several
            // seconds at a time mid-load, so no streak length reliably
            // told loading-paused apart from loading-done. Unconditional
            // flat wait instead, same as the pre-dialog floor above.
            log.Add($"[T+{runStopwatch.Elapsed.TotalSeconds:0.0}s] Starting post-dialog wait " +
                $"({_autoSettings.InitialWaitSeconds}s, flat/unconditional)...");
            var settleStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var settleUntil = DateTime.UtcNow.AddSeconds(_autoSettings.InitialWaitSeconds);
            while (DateTime.UtcNow < settleUntil)
            {
                ViewpointCaptureService.BringToForeground(mainHandle);
                System.Windows.Forms.Application.DoEvents();
                System.Threading.Thread.Sleep(500);
            }
            log.Add($"[T+{runStopwatch.Elapsed.TotalSeconds:0.0}s] Post-dialog wait done " +
                $"(actual {settleStopwatch.Elapsed.TotalSeconds:0.0}s). Starting viewpoint loop.");

            // Compact summary of the three wait stages above, prepended to
            // every saved image's filename in this run - lets the timing
            // be checked at a glance from a folder listing/thumbnail view,
            // not just from the log file. L=min-wait, D=dialog-wait
            // (actual duration, or "none" if never seen visible),
            // S=post-dialog settle wait.
            var fileNamePrefix = "L" + (int)minWaitStopwatch.Elapsed.TotalSeconds + "_" +
                (dialogWasSeenVisible ? "D" + (int)dialogStopwatch.Elapsed.TotalSeconds : "Dnone") + "_" +
                "S" + (int)settleStopwatch.Elapsed.TotalSeconds + "_";

            ViewpointCaptureService.RunAutoExport(document, _autoSettings, mainHandle, log, fileNamePrefix);
        }
    }
}
