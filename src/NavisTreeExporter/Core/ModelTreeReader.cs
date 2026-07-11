using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;

namespace NavisTreeExporter.Core
{
    /// <summary>
    /// Walks the selection tree of an open Navisworks document and converts it
    /// into plain DTOs (<see cref="TreeNode"/>) that are safe to serialize
    /// outside of the Navisworks API object model.
    /// </summary>
    public static class ModelTreeReader
    {
        public static List<TreeNode> ReadTree(Document document, bool includeProperties, ExportProgressReporter progress = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var roots = new List<TreeNode>();
            foreach (Model model in document.Models)
            {
                if (model.RootItem == null) continue;
                roots.Add(BuildNode(model.RootItem, includeProperties, progress));
            }
            return roots;
        }

        private static TreeNode BuildNode(ModelItem item, bool includeProperties, ExportProgressReporter progress)
        {
            var node = new TreeNode
            {
                DisplayName = item.DisplayName,
                ClassName = item.ClassName,
                ClassDisplayName = item.ClassDisplayName,
                InstanceGuid = item.InstanceGuid == Guid.Empty ? null : item.InstanceGuid.ToString(),
                HasGeometry = item.HasGeometry,
                IsHidden = item.IsHidden,
            };

            // Reading PropertyCategories/Properties means a separate API call
            // per category and per property, so on large models this is by
            // far the most expensive part of the walk. Skip it entirely when
            // the caller only needs the hierarchy.
            if (includeProperties)
            {
                try
                {
                    foreach (PropertyCategory category in item.PropertyCategories)
                    {
                        var categoryEntry = new PropertyCategoryEntry
                        {
                            CategoryName = category.Name,
                            CategoryDisplayName = category.DisplayName,
                        };

                        foreach (DataProperty property in category.Properties)
                        {
                            categoryEntry.Properties.Add(ReadProperty(property));
                        }

                        node.PropertyCategories.Add(categoryEntry);
                    }
                }
                catch (Exception)
                {
                    // A single item's property data being unreadable shouldn't
                    // abort the whole export - keep whatever categories were
                    // read so far and move on.
                }
            }

            progress?.ReportItem();

            foreach (ModelItem child in item.Children)
            {
                node.Children.Add(BuildNode(child, includeProperties, progress));
            }

            return node;
        }

        private static PropertyEntry ReadProperty(DataProperty property)
        {
            var entry = new PropertyEntry
            {
                Name = property.Name,
                DisplayName = property.DisplayName,
            };

            VariantData value;
            try
            {
                value = property.Value;
            }
            catch (Exception)
            {
                return entry;
            }

            if (value == null) return entry;

            // VariantData's formatting/type accessors are third-party code
            // with undocumented failure modes for unusual property values
            // (already seen NotSupportedException for "IsDisplayString" and
            // NullReferenceException for something else) - catch broadly so
            // one odd property never aborts the whole export.
            try
            {
                entry.Value = value.ToDisplayString();
            }
            catch (Exception)
            {
                entry.Value = null;
            }

            try
            {
                entry.DataType = value.IsDisplayString ? "DisplayString" : value.DataType.ToString();
            }
            catch (Exception)
            {
                entry.DataType = null;
            }

            return entry;
        }
    }
}
