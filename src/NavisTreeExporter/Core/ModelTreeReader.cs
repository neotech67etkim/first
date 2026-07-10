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

            var value = property.Value;
            if (value == null) return entry;

            try
            {
                entry.Value = value.ToDisplayString();
            }
            catch (NotSupportedException)
            {
                entry.Value = null;
            }

            try
            {
                // VariantData.DataType throws NotSupportedException when the
                // value only exists as a display string (no typed backing value).
                entry.DataType = value.IsDisplayString ? "DisplayString" : value.DataType.ToString();
            }
            catch (NotSupportedException)
            {
                entry.DataType = null;
            }

            return entry;
        }
    }
}
