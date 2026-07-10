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
        public static List<TreeNode> ReadTree(Document document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var roots = new List<TreeNode>();
            foreach (Model model in document.Models)
            {
                if (model.RootItem == null) continue;
                roots.Add(BuildNode(model.RootItem));
            }
            return roots;
        }

        private static TreeNode BuildNode(ModelItem item)
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

            foreach (PropertyCategory category in item.PropertyCategories)
            {
                var categoryEntry = new PropertyCategoryEntry
                {
                    CategoryName = category.Name,
                    CategoryDisplayName = category.DisplayName,
                };

                foreach (DataProperty property in category.Properties)
                {
                    categoryEntry.Properties.Add(new PropertyEntry
                    {
                        Name = property.Name,
                        DisplayName = property.DisplayName,
                        Value = property.Value?.ToDisplayString(),
                        DataType = property.Value?.DataType.ToString(),
                    });
                }

                node.PropertyCategories.Add(categoryEntry);
            }

            foreach (ModelItem child in item.Children)
            {
                node.Children.Add(BuildNode(child));
            }

            return node;
        }
    }
}
