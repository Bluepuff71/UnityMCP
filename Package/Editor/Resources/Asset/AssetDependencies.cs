using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace UnityMCP.Editor.Resources.Asset
{
    /// <summary>
    /// Resource provider for asset dependency information.
    /// </summary>
    public static class AssetDependencies
    {
        /// <summary>
        /// Gets dependency information for a specific asset.
        /// Returns both direct dependencies (what the asset uses) and reverse dependencies (what uses the asset).
        /// </summary>
        /// <param name="assetPath">The asset path relative to the project (e.g., "Assets/Scripts/MyScript.cs").</param>
        /// <returns>Object containing dependency information.</returns>
        [MCPResource("assets://dependencies/{path}", "Asset dependencies - what an asset uses and what uses it")]
        public static object GetAssetDependencies([MCPParam("path", "Asset path relative to project (e.g., Assets/Scripts/MyScript.cs)")] string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return new
                {
                    error = true,
                    message = "Asset path is required"
                };
            }

            // Normalize path separators
            assetPath = assetPath.Replace("\\", "/");

            // Ensure path starts with Assets/
            if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                !assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                assetPath = "Assets/" + assetPath;
            }

            // Check if asset exists
            string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(assetGuid))
            {
                return new
                {
                    error = true,
                    message = $"Asset not found at path: {assetPath}"
                };
            }

            try
            {
                // Get direct dependencies (what this asset uses)
                string[] directDependencyPaths = AssetDatabase.GetDependencies(assetPath, recursive: false);

                // Remove self from dependencies
                directDependencyPaths = directDependencyPaths
                    .Where(path => !path.Equals(assetPath, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                var directDependencies = directDependencyPaths
                    .Select(path => BuildAssetInfo(path))
                    .ToArray();

                // Get recursive dependencies
                string[] allDependencyPaths = AssetDatabase.GetDependencies(assetPath, recursive: true);
                allDependencyPaths = allDependencyPaths
                    .Where(path => !path.Equals(assetPath, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                // Get reverse dependencies (what uses this asset)
                var reverseDependencies = FindReverseDependencies(assetPath);

                // Get asset info
                var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                var assetType = mainAsset != null ? mainAsset.GetType().Name : "Unknown";
                var assetLabels = AssetDatabase.GetLabels(mainAsset);

                return new
                {
                    asset = new
                    {
                        path = assetPath,
                        guid = assetGuid,
                        type = assetType,
                        labels = assetLabels
                    },
                    dependencies = new
                    {
                        direct = new
                        {
                            count = directDependencies.Length,
                            assets = directDependencies
                        },
                        recursive = new
                        {
                            count = allDependencyPaths.Length,
                            assets = allDependencyPaths.Select(path => BuildAssetInfo(path)).ToArray()
                        }
                    },
                    reverseDependencies = new
                    {
                        count = reverseDependencies.Length,
                        assets = reverseDependencies
                    },
                    summary = new
                    {
                        directDependencyCount = directDependencies.Length,
                        recursiveDependencyCount = allDependencyPaths.Length,
                        reverseDependencyCount = reverseDependencies.Length,
                        isRootAsset = reverseDependencies.Length == 0,
                        isLeafAsset = directDependencies.Length == 0
                    }
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    error = true,
                    message = $"Failed to get dependencies for asset: {exception.Message}",
                    path = assetPath
                };
            }
        }

        /// <summary>
        /// Finds assets that depend on the specified asset.
        /// </summary>
        private static object[] FindReverseDependencies(string targetAssetPath)
        {
            var reverseDependencies = new List<object>();

            // Get all asset paths in the project
            string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();

            // Filter to only include Assets/ folder (exclude Packages/)
            var projectAssetPaths = allAssetPaths
                .Where(path => path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (string assetPath in projectAssetPaths)
            {
                // Skip the target asset itself
                if (assetPath.Equals(targetAssetPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Skip directories
                if (AssetDatabase.IsValidFolder(assetPath))
                {
                    continue;
                }

                // Get direct dependencies of this asset
                string[] dependencies = AssetDatabase.GetDependencies(assetPath, recursive: false);

                // Check if target asset is in the dependencies
                if (dependencies.Any(dep => dep.Equals(targetAssetPath, StringComparison.OrdinalIgnoreCase)))
                {
                    reverseDependencies.Add(BuildAssetInfo(assetPath));
                }
            }

            return reverseDependencies.ToArray();
        }

        /// <summary>
        /// Builds an info object for an asset path.
        /// </summary>
        private static object BuildAssetInfo(string assetPath)
        {
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            string assetType = asset != null ? asset.GetType().Name : "Unknown";

            // Extract file extension
            string extension = System.IO.Path.GetExtension(assetPath);

            return new
            {
                path = assetPath,
                guid = guid,
                type = assetType,
                extension = extension
            };
        }
    }
}
