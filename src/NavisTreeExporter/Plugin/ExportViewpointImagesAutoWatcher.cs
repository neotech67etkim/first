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

            // Bring the window to the front right away - it needs to stay
            // frontmost/unoccluded for the whole run since the capture is a
            // real screen grab (Graphics.CopyFromScreen), not an off-screen
            // render. Otherwise geometry can keep loading behind other
            // windows and the eventual screenshot shows whatever else was
            // on top instead of the model.
            var mainHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            ViewpointCaptureService.BringToForeground(mainHandle);

            // Models.Count > 0 fires well before Navisworks' own file-loading
            // progress dialog ("작업 중... (NN.N%)") actually closes on
            // larger models - wait for that dialog to disappear instead of
            // guessing a fixed delay, since load time varies a lot by file
            // size. Falls back to just proceeding if it somehow never shows
            // up or never closes within the timeout, so a detection miss
            // can't hang the run forever.
            var loadingTimeoutAt = DateTime.UtcNow.AddMinutes(LoadingDialogTimeoutMinutes);
            while (ViewpointCaptureService.IsLoadingDialogVisible() && DateTime.UtcNow < loadingTimeoutAt)
            {
                System.Windows.Forms.Application.DoEvents();
                System.Threading.Thread.Sleep(250);
            }

            // Extra grace period after the loading dialog closes - rendering
            // can still catch up for a moment even once loading itself is done.
            var waitUntil = DateTime.UtcNow.AddSeconds(_autoSettings.InitialWaitSeconds);
            while (DateTime.UtcNow < waitUntil)
            {
                System.Windows.Forms.Application.DoEvents();
                System.Threading.Thread.Sleep(50);
            }

            ViewpointCaptureService.RunAutoExport(document, _autoSettings);
        }
    }
}
