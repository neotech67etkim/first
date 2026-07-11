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

            var serializer = new JsonSerializer
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
            };

            // Serialize straight to the file stream instead of building the
            // whole document as one in-memory string first (JsonConvert.
            // SerializeObject) - on a large tree that single string/
            // StringBuilder got big enough to trigger a NullReferenceException
            // deep inside Newtonsoft's JsonTextWriter/StringBuilder internals.
            using (var streamWriter = new StreamWriter(filePath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            using (var jsonWriter = new JsonTextWriter(streamWriter))
            {
                serializer.Serialize(jsonWriter, payload);
            }
        }
    }
}
