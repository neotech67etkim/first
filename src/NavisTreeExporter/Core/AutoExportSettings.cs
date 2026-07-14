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
        private const int DefaultInitialWaitSeconds = 300;
        private const int DefaultMinInitialWaitSeconds = 300;

        public string OutputDir { get; }

        public int WaitSeconds { get; }

        /// <summary>
        /// Unconditional wait (seconds), always taken in full, after the
        /// loading dialog closes and before the per-viewpoint capture loop
        /// starts. This used to be a stability check (wait until the view
        /// holds still) instead of a flat sleep, but on a real run with a
        /// large model that kept declaring "stable" during multi-second
        /// pauses between geometry-streaming bursts, well before loading
        /// had actually finished - no streak length reliably told
        /// loading-paused apart from loading-done, so this is a plain flat
        /// wait instead, same idea as MinInitialWaitSeconds. Confirmed on a
        /// real .nwf (which references source files rather than embedding
        /// them) that the loading dialog closing does NOT mean the
        /// references have finished refreshing to their latest state - a
        /// plain (non-automated) open of the same file showed the up to
        /// date model, but the automated capture showed stale/outdated
        /// geometry until this wait was raised to 300s, so the reference
        /// refresh genuinely can take several minutes after the dialog closes.
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
