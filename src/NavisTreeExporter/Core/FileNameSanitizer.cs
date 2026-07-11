using System;
using System.IO;

namespace NavisTreeExporter.Core
{
    public static class FileNameSanitizer
    {
        public static string Sanitize(string name, string fallback)
        {
            if (string.IsNullOrWhiteSpace(name)) return fallback;

            var invalidChars = Path.GetInvalidFileNameChars();
            var safeChars = Array.ConvertAll(name.ToCharArray(), c => Array.IndexOf(invalidChars, c) >= 0 ? '_' : c);
            var safe = new string(safeChars);
            return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
        }
    }
}
