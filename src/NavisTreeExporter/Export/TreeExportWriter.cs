using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Navisworks.Api;
using Newtonsoft.Json;
using NavisTreeExporter.Core;

namespace NavisTreeExporter.Export
{
    /// <summary>
    /// Walks the selection tree and writes JSON + CSV in a single pass,
    /// streaming each item straight to disk as it's visited instead of
    /// building the whole tree (with properties) in memory first. On a large
    /// model with properties included, the in-memory version could grow to
    /// several GB and exhaust system memory.
    /// </summary>
    public static class TreeExportWriter
    {
        // Navisworks's localized UI labels (class/category/property display
        // names) come back mojibake'd on this Korean Windows install - the
        // classic signature of UTF-8 bytes that got decoded as CP949 somewhere
        // in the API's native interop layer (e.g. "파일" -> "?뚯씪",
        // "그룹" -> "洹몃９"). Internal identifiers (ClassName, CategoryName,
        // PropertyName) and user-entered item names are plain ASCII/already
        // correct and are left untouched. Re-encoding the corrupted string as
        // CP949 recovers the original UTF-8 bytes, which we then decode
        // properly.
        private static readonly Encoding Cp949 = Encoding.GetEncoding(949);

        private static string FixMojibake(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            try
            {
                var bytes = Cp949.GetBytes(value);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception)
            {
                return value;
            }
        }

        public static void Export(
            Document document,
            ExportDetailLevel detailLevel,
            string jsonPath,
            string itemsCsvPath,
            string propertiesCsvPath,
            ExportProgressReporter progress)
        {
            var includeBasicInfo = detailLevel != ExportDetailLevel.NamesOnly;
            var includeProperties = detailLevel == ExportDetailLevel.HierarchyAndProperties;

            using (var jsonStream = new StreamWriter(jsonPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            using (var jsonWriter = new JsonTextWriter(jsonStream) { Formatting = Formatting.Indented })
            using (var itemsCsv = new StreamWriter(itemsCsvPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            using (var propertiesCsv = includeProperties
                ? new StreamWriter(propertiesCsvPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
                : null)
            {
                itemsCsv.WriteLine(includeBasicInfo
                    ? string.Join(",", "Path", "DisplayName", "ClassName", "ClassDisplayName", "InstanceGuid", "ParentPath", "HasGeometry", "IsHidden", "Depth")
                    : string.Join(",", "Path", "DisplayName", "ParentPath", "Depth"));

                propertiesCsv?.WriteLine(string.Join(",",
                    "ItemPath", "InstanceGuid", "CategoryName", "CategoryDisplayName",
                    "PropertyName", "PropertyDisplayName", "Value", "DataType"));

                jsonWriter.WriteStartObject();
                jsonWriter.WritePropertyName("ExportedAtUtc");
                jsonWriter.WriteValue(DateTime.UtcNow);
                jsonWriter.WritePropertyName("Roots");
                jsonWriter.WriteStartArray();

                foreach (Model model in document.Models)
                {
                    ModelItem rootItem;
                    try
                    {
                        rootItem = model.RootItem;
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (rootItem == null) continue;

                    WriteNode(rootItem, includeBasicInfo, includeProperties, progress, jsonWriter, itemsCsv, propertiesCsv, parentPath: null, depth: 0);
                }

                jsonWriter.WriteEndArray();
                jsonWriter.WriteEndObject();
            }
        }

        private static void WriteNode(
            ModelItem item,
            bool includeBasicInfo,
            bool includeProperties,
            ExportProgressReporter progress,
            JsonTextWriter jsonWriter,
            StreamWriter itemsCsv,
            StreamWriter propertiesCsv,
            string parentPath,
            int depth)
        {
            string displayName = null;
            string className = null;
            string classDisplayName = null;
            string instanceGuid = null;
            var hasGeometry = false;
            var isHidden = false;

            try
            {
                displayName = item.DisplayName;
                if (includeBasicInfo)
                {
                    className = item.ClassName;
                    classDisplayName = FixMojibake(item.ClassDisplayName);
                    instanceGuid = item.InstanceGuid == Guid.Empty ? null : item.InstanceGuid.ToString();
                    hasGeometry = item.HasGeometry;
                    isHidden = item.IsHidden;
                }
            }
            catch (Exception)
            {
                // Keep whatever fields were read before the failure - a single
                // unusual item shouldn't abort the whole export.
            }

            var path = string.IsNullOrEmpty(parentPath) ? displayName : parentPath + "/" + displayName;

            jsonWriter.WriteStartObject();
            WriteJsonString(jsonWriter, "DisplayName", displayName);
            if (includeBasicInfo)
            {
                WriteJsonString(jsonWriter, "ClassName", className);
                WriteJsonString(jsonWriter, "ClassDisplayName", classDisplayName);
                WriteJsonString(jsonWriter, "InstanceGuid", instanceGuid);
                jsonWriter.WritePropertyName("HasGeometry");
                jsonWriter.WriteValue(hasGeometry);
                jsonWriter.WritePropertyName("IsHidden");
                jsonWriter.WriteValue(isHidden);
            }

            itemsCsv.WriteLine(includeBasicInfo
                ? string.Join(",",
                    CsvEscape(path), CsvEscape(displayName), CsvEscape(className), CsvEscape(classDisplayName),
                    CsvEscape(instanceGuid), CsvEscape(parentPath), hasGeometry, isHidden, depth)
                : string.Join(",", CsvEscape(path), CsvEscape(displayName), CsvEscape(parentPath), depth));

            if (includeProperties)
            {
                jsonWriter.WritePropertyName("PropertyCategories");
                jsonWriter.WriteStartArray();

                try
                {
                    foreach (PropertyCategory category in item.PropertyCategories)
                    {
                        var categoryName = category.Name;
                        var categoryDisplayName = FixMojibake(category.DisplayName);

                        jsonWriter.WriteStartObject();
                        WriteJsonString(jsonWriter, "CategoryName", categoryName);
                        WriteJsonString(jsonWriter, "CategoryDisplayName", categoryDisplayName);
                        jsonWriter.WritePropertyName("Properties");
                        jsonWriter.WriteStartArray();

                        foreach (DataProperty property in category.Properties)
                        {
                            ReadProperty(property, out var propName, out var propDisplayName, out var propValue, out var propDataType);

                            jsonWriter.WriteStartObject();
                            WriteJsonString(jsonWriter, "Name", propName);
                            WriteJsonString(jsonWriter, "DisplayName", propDisplayName);
                            WriteJsonString(jsonWriter, "Value", propValue);
                            WriteJsonString(jsonWriter, "DataType", propDataType);
                            jsonWriter.WriteEndObject();

                            propertiesCsv.WriteLine(string.Join(",",
                                CsvEscape(path), CsvEscape(instanceGuid), CsvEscape(categoryName), CsvEscape(categoryDisplayName),
                                CsvEscape(propName), CsvEscape(propDisplayName), CsvEscape(propValue), CsvEscape(propDataType)));
                        }

                        jsonWriter.WriteEndArray();
                        jsonWriter.WriteEndObject();
                    }
                }
                catch (Exception)
                {
                    // A single item's property data being unreadable shouldn't
                    // abort the whole export - keep whatever was written so far.
                }

                jsonWriter.WriteEndArray();
            }

            progress?.ReportItem();

            jsonWriter.WritePropertyName("Children");
            jsonWriter.WriteStartArray();

            foreach (ModelItem child in SafeGetChildren(item))
            {
                WriteNode(child, includeBasicInfo, includeProperties, progress, jsonWriter, itemsCsv, propertiesCsv, path, depth + 1);
            }

            jsonWriter.WriteEndArray();
            jsonWriter.WriteEndObject();
        }

        private static IEnumerable<ModelItem> SafeGetChildren(ModelItem item)
        {
            try
            {
                return item.Children ?? Enumerable.Empty<ModelItem>();
            }
            catch (Exception)
            {
                return Enumerable.Empty<ModelItem>();
            }
        }

        private static void ReadProperty(DataProperty property, out string name, out string displayName, out string value, out string dataType)
        {
            name = property.Name;
            displayName = FixMojibake(property.DisplayName);
            value = null;
            dataType = null;

            VariantData variant;
            try
            {
                variant = property.Value;
            }
            catch (Exception)
            {
                return;
            }

            if (variant == null) return;

            // VariantData's formatting/type accessors are third-party code
            // with undocumented failure modes for unusual property values
            // (seen NotSupportedException for "IsDisplayString" and
            // NullReferenceException for something else) - catch broadly so
            // one odd property never aborts the whole export.
            try
            {
                value = variant.ToDisplayString();
            }
            catch (Exception)
            {
                value = null;
            }

            try
            {
                dataType = variant.IsDisplayString ? "DisplayString" : variant.DataType.ToString();
            }
            catch (Exception)
            {
                dataType = null;
            }
        }

        private static void WriteJsonString(JsonTextWriter writer, string name, string value)
        {
            if (value == null) return;
            writer.WritePropertyName(name);
            writer.WriteValue(value);
        }

        private static string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var mustQuote = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            if (!mustQuote) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
