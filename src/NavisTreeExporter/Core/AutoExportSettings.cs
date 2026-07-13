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

        private AutoExportSettings(string outputDir, int waitSeconds, int initialWaitSeconds)
        {
            OutputDir = outputDir;
            WaitSeconds = waitSeconds;
            InitialWaitSeconds = initialWaitSeconds;
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

            return new AutoExportSettings(outputDir, waitSeconds, initialWaitSeconds);
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
