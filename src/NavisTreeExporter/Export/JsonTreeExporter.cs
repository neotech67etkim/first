using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using NavisTreeExporter.Core;

namespace NavisTreeExporter.Export
{
    public static class JsonTreeExporter
    {
        public static void Export(IEnumerable<TreeNode> roots, string filePath)
        {
            var payload = new
            {
                ExportedAtUtc = DateTime.UtcNow,
                Roots = roots,
            };

            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
            };

            var json = JsonConvert.SerializeObject(payload, settings);
            File.WriteAllText(filePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }
}
