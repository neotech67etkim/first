using System.Collections.Generic;
using System.IO;
using System.Text;
using NavisTreeExporter.Core;

namespace NavisTreeExporter.Export
{
    /// <summary>
    /// Flattens the tree into two CSV files that are easy to join/pivot when
    /// comparing against other design data later:
    ///   *_items.csv      - one row per tree item (hierarchy)
    ///   *_properties.csv - one row per (item, property) pair (long format)
    /// </summary>
    public static class CsvTreeExporter
    {
        public static void ExportItems(IEnumerable<TreeNode> roots, string filePath)
        {
            using (var writer = new StreamWriter(filePath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
                writer.WriteLine(string.Join(",",
                    "Path", "DisplayName", "ClassName", "ClassDisplayName",
                    "InstanceGuid", "ParentPath", "HasGeometry", "IsHidden", "Depth"));

                foreach (var root in roots)
                {
                    WriteItemRow(writer, root, path: root.DisplayName, parentPath: string.Empty, depth: 0);
                }
            }
        }

        public static void ExportProperties(IEnumerable<TreeNode> roots, string filePath)
        {
            using (var writer = new StreamWriter(filePath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
                writer.WriteLine(string.Join(",",
                    "ItemPath", "InstanceGuid", "CategoryName", "CategoryDisplayName",
                    "PropertyName", "PropertyDisplayName", "Value", "DataType"));

                foreach (var root in roots)
                {
                    WritePropertyRows(writer, root, root.DisplayName);
                }
            }
        }

        private static void WriteItemRow(StreamWriter writer, TreeNode node, string path, string parentPath, int depth)
        {
            writer.WriteLine(string.Join(",",
                Escape(path), Escape(node.DisplayName), Escape(node.ClassName), Escape(node.ClassDisplayName),
                Escape(node.InstanceGuid), Escape(parentPath), node.HasGeometry, node.IsHidden, depth));

            foreach (var child in node.Children)
            {
                WriteItemRow(writer, child, path + "/" + child.DisplayName, path, depth + 1);
            }
        }

        private static void WritePropertyRows(StreamWriter writer, TreeNode node, string path)
        {
            foreach (var category in node.PropertyCategories)
            {
                foreach (var property in category.Properties)
                {
                    writer.WriteLine(string.Join(",",
                        Escape(path), Escape(node.InstanceGuid), Escape(category.CategoryName), Escape(category.CategoryDisplayName),
                        Escape(property.Name), Escape(property.DisplayName), Escape(property.Value), Escape(property.DataType)));
                }
            }

            foreach (var child in node.Children)
            {
                WritePropertyRows(writer, child, path + "/" + child.DisplayName);
            }
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            bool mustQuote = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            if (!mustQuote) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
