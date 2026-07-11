namespace NavisTreeExporter.Core
{
    public enum ExportDetailLevel
    {
        /// <summary>DisplayName + hierarchy only. Fastest, smallest output.</summary>
        NamesOnly,

        /// <summary>Hierarchy plus ClassName/ClassDisplayName/InstanceGuid/HasGeometry/IsHidden.</summary>
        HierarchyAndBasicInfo,

        /// <summary>Everything, including PropertyCategories/Properties. Slowest, largest output.</summary>
        HierarchyAndProperties,
    }
}
