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
        private const int DefaultInitialWaitSeconds = 15;

        public string OutputDir { get; }

        public int WaitSeconds { get; }

        /// <summary>
        /// Extra grace period after a document finishes opening (i.e. once
        /// document.Models.Count > 0) before the capture loop starts -
        /// geometry can still be streaming/rendering in for a while after
        /// that point on heavier models.
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
