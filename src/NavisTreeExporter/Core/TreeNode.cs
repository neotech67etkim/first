using System.Collections.Generic;

namespace NavisTreeExporter.Core
{
    public sealed class TreeNode
    {
        public string DisplayName { get; set; }
        public string ClassName { get; set; }
        public string ClassDisplayName { get; set; }
        public string InstanceGuid { get; set; }
        public bool HasGeometry { get; set; }
        public bool IsHidden { get; set; }
        public List<PropertyCategoryEntry> PropertyCategories { get; } = new List<PropertyCategoryEntry>();
        public List<TreeNode> Children { get; } = new List<TreeNode>();
    }

    public sealed class PropertyCategoryEntry
    {
        public string CategoryName { get; set; }
        public string CategoryDisplayName { get; set; }
        public List<PropertyEntry> Properties { get; } = new List<PropertyEntry>();
    }

    public sealed class PropertyEntry
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Value { get; set; }
        public string DataType { get; set; }
    }
}
