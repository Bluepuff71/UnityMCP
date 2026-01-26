using System;
using UnityEditor;

namespace UnityMCP.Editor.Utilities
{
    /// <summary>
    /// Provides utility methods for normalizing and managing Unity asset paths.
    /// </summary>
    public static class PathUtilities
    {
        /// <summary>
        /// Normalizes an asset path to use forward slashes and ensure it starts with "Assets/".
        /// </summary>
        /// <param name="path">The path to normalize.</param>
        /// <returns>
        /// A normalized path starting with "Assets/" using forward slashes,
        /// or an empty string if the input is null or empty.
        /// </returns>
        /// <remarks>
        /// This method performs the following normalizations:
        /// <list type="bullet">
        /// <item>Replaces backslashes with forward slashes</item>
        /// <item>Trims leading and trailing whitespace</item>
        /// <item>Removes leading and trailing slashes</item>
        /// <item>Ensures the path starts with "Assets/"</item>
        /// <item>Normalizes the "Assets" prefix to proper casing</item>
        /// </list>
        /// </remarks>
        /// <example>
        /// <code>
        /// string path = PathUtilities.NormalizePath(@"assets\Materials\MyMaterial.mat");
        /// // Returns: "Assets/Materials/MyMaterial.mat"
        /// </code>
        /// </example>
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            // Replace backslashes with forward slashes and trim whitespace
            string normalizedPath = path.Replace('\\', '/').Trim();

            // Remove leading/trailing slashes
            normalizedPath = normalizedPath.Trim('/');

            // Ensure path starts with "Assets"
            if (!normalizedPath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath = "Assets/" + normalizedPath;
            }

            // Normalize case for "Assets" prefix
            if (normalizedPath.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.Equals("assets", StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath = "Assets" + normalizedPath.Substring(6);
            }

            return normalizedPath;
        }

        /// <summary>
        /// Ensures a folder exists within the Unity Assets folder, creating it recursively if needed.
        /// </summary>
        /// <param name="path">The asset folder path to ensure exists (e.g., "Assets/Materials/Textures").</param>
        /// <param name="error">
        /// When this method returns false, contains an error message describing what failed.
        /// When this method returns true, this parameter is null.
        /// </param>
        /// <returns>True if the folder exists or was created successfully; otherwise, false.</returns>
        /// <remarks>
        /// This method uses Unity's <see cref="AssetDatabase.CreateFolder"/> to create folders,
        /// ensuring proper integration with the Unity asset pipeline. The path is automatically
        /// normalized before processing.
        /// </remarks>
        /// <example>
        /// <code>
        /// if (!PathUtilities.EnsureFolderExists("Assets/Materials/Textures", out string error))
        /// {
        ///     Debug.LogError($"Failed to create folder: {error}");
        /// }
        /// </code>
        /// </example>
        public static bool EnsureFolderExists(string path, out string error)
        {
            error = null;
            string normalizedPath = path.Replace('\\', '/').TrimEnd('/');

            if (AssetDatabase.IsValidFolder(normalizedPath))
            {
                return true;
            }

            // Split path into parts
            string[] pathParts = normalizedPath.Split('/');
            string currentPath = string.Empty;

            for (int partIndex = 0; partIndex < pathParts.Length; partIndex++)
            {
                if (partIndex == 0)
                {
                    currentPath = pathParts[partIndex];
                    if (!currentPath.Equals("Assets", StringComparison.OrdinalIgnoreCase))
                    {
                        currentPath = "Assets";
                    }
                    continue;
                }

                string parentPath = currentPath;
                currentPath = $"{currentPath}/{pathParts[partIndex]}";

                if (!AssetDatabase.IsValidFolder(currentPath))
                {
                    string folderGuid = AssetDatabase.CreateFolder(parentPath, pathParts[partIndex]);
                    if (string.IsNullOrEmpty(folderGuid))
                    {
                        error = $"Failed to create folder at '{currentPath}'.";
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
