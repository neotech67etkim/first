using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;

namespace NavisTreeExporter.Core
{
    /// <summary>
    /// Walks Document.SavedViewpoints and flattens it into a list of
    /// (path, viewpoint) pairs, recursing through folders.
    ///
    /// NOTE: written without access to the Navisworks SDK - the saved
    /// viewpoints tree API (FolderItem/SavedItem/SavedViewpoint class names,
    /// RootItem) is unverified and may need a small fix once this actually
    /// compiles against the real API.
    /// </summary>
    public static class SavedViewpointCollector
    {
        public static List<(string Path, SavedViewpoint Viewpoint)> Collect(Document document)
        {
            var results = new List<(string, SavedViewpoint)>();

            if (document.SavedViewpoints.RootItem is FolderItem rootFolder)
            {
                foreach (SavedItem child in rootFolder.Children)
                {
                    CollectFrom(child, null, results);
                }
            }

            return results;
        }

        private static void CollectFrom(SavedItem item, string parentPath, List<(string, SavedViewpoint)> results)
        {
            string name;
            try
            {
                name = item.DisplayName;
            }
            catch (Exception)
            {
                name = null;
            }

            var path = string.IsNullOrEmpty(parentPath) ? name : parentPath + "/" + name;

            if (item is SavedViewpoint viewpoint)
            {
                results.Add((path, viewpoint));
                return;
            }

            if (item is FolderItem folder)
            {
                foreach (SavedItem child in folder.Children)
                {
                    CollectFrom(child, path, results);
                }
            }
        }
    }
}
