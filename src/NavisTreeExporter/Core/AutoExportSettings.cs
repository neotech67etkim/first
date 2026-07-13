using System;

namespace NavisTreeExporter.Core
{
    /// <summary>
    /// Settings for the unattended ("auto") mode of Export Viewpoint Images,
    /// read from environment variables set by the launching process (a
    /// scheduled-task script that starts Navisworks with a file to open).
    /// Auto mode only activates when NAVIS_AUTO_EXPORT_IMAGES=1 is present -
    /// the interactive button click behaves exactly as before otherwise.
    /// </summary>
    public sealed class AutoExportSettings
    {
        private const int DefaultWaitSeconds = 15;
        private const int DefaultInitialWaitSeconds = 60;
        private const int DefaultMinInitialWaitSeconds = 20;

        public string OutputDir { get; }

        public int WaitSeconds { get; }

        /// <summary>
        /// Upper bound (seconds) on how long to wait for the view to settle
        /// after a document finishes opening (i.e. once
        /// document.Models.Count > 0 and the loading dialog has closed)
        /// before the capture loop starts. Settling is detected by the view
        /// holding still for several consecutive checks in a row (see
        /// ViewpointCaptureService.WaitUntilStable) rather than a flat
        /// sleep, so this is a cap, not a wait that's always taken in full
        /// - but it needs real headroom since large models can keep
        /// streaming geometry in bursts well after the dialog closes.
        /// </summary>
        public int InitialWaitSeconds { get; }

        /// <summary>
        /// Unconditional minimum wait (seconds), applied right after
        /// document.Models.Count > 0 becomes true and before checking for
        /// the loading dialog at all. On a real run there's a real gap
        /// between the document object existing and the "작업 중" loading
        /// dialog actually appearing on screen - checking for the dialog
        /// during that gap sees nothing yet and wrongly concludes loading
        /// is already done. This floor is a plain, unconditional sleep (not
        /// stability-based) sized to outlast that gap so the dialog-wait
        /// logic that follows only starts once the dialog has had a chance
        /// to actually show up.
        /// </summary>
        public int MinInitialWaitSeconds { get; }

        private AutoExportSettings(string outputDir, int waitSeconds, int initialWaitSeconds, int minInitialWaitSeconds)
        {
            OutputDir = outputDir;
            WaitSeconds = waitSeconds;
            InitialWaitSeconds = initialWaitSeconds;
            MinInitialWaitSeconds = minInitialWaitSeconds;
        }

        /// <summary>
        /// Returns null when auto mode isn't requested (or is missing
        /// required settings), in which case the plugin should fall back to
        /// its normal interactive behavior.
        /// </summary>
        public static AutoExportSettings FromEnvironment()
        {
            if (Environment.GetEnvironmentVariable("NAVIS_AUTO_EXPORT_IMAGES") != "1")
            {
                return null;
            }

            var outputDir = Environment.GetEnvironmentVariable("NAVIS_AUTO_OUTPUT_DIR");
            if (string.IsNullOrWhiteSpace(outputDir))
            {
                return null;
            }

            var waitSeconds = ReadPositiveInt("NAVIS_AUTO_WAIT_SECONDS", DefaultWaitSeconds);
            var initialWaitSeconds = ReadPositiveInt("NAVIS_AUTO_INITIAL_WAIT_SECONDS", DefaultInitialWaitSeconds);
            var minInitialWaitSeconds = ReadPositiveInt("NAVIS_AUTO_MIN_INITIAL_WAIT_SECONDS", DefaultMinInitialWaitSeconds);

            return new AutoExportSettings(outputDir, waitSeconds, initialWaitSeconds, minInitialWaitSeconds);
        }

        private static int ReadPositiveInt(string envVarName, int defaultValue)
        {
            var raw = Environment.GetEnvironmentVariable(envVarName);
            if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var parsed) && parsed > 0)
            {
                return parsed;
            }

            return defaultValue;
        }
    }
}
