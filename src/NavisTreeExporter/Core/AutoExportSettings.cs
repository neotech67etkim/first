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
        private const int DefaultWaitSeconds = 8;

        public string OutputDir { get; }

        public int WaitSeconds { get; }

        private AutoExportSettings(string outputDir, int waitSeconds)
        {
            OutputDir = outputDir;
            WaitSeconds = waitSeconds;
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

            var waitSeconds = DefaultWaitSeconds;
            var waitEnv = Environment.GetEnvironmentVariable("NAVIS_AUTO_WAIT_SECONDS");
            if (!string.IsNullOrWhiteSpace(waitEnv) && int.TryParse(waitEnv, out var parsed) && parsed > 0)
            {
                waitSeconds = parsed;
            }

            return new AutoExportSettings(outputDir, waitSeconds);
        }
    }
}
