using UnityEditor;
using UnityEngine;

namespace UnityMCP.Editor.Resources.Project
{
    /// <summary>
    /// Resource provider for project information.
    /// </summary>
    public static class ProjectInfo
    {
        /// <summary>
        /// Gets information about the current Unity project.
        /// </summary>
        /// <returns>Object containing project path, name, and Unity version.</returns>
        [MCPResource("project://info", "Project path, name, and Unity version")]
        public static object Get()
        {
            // Application.dataPath returns the Assets folder path
            // Get the project root by removing "/Assets" from the end
            string dataPath = Application.dataPath;
            string projectPath = dataPath.Substring(0, dataPath.Length - "/Assets".Length);

            return new
            {
                projectPath = projectPath,
                assetsPath = dataPath,
                productName = Application.productName,
                companyName = Application.companyName,
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                isPlaying = Application.isPlaying,
                isEditor = Application.isEditor,
                isBatchMode = Application.isBatchMode,
                systemLanguage = Application.systemLanguage.ToString(),
                targetFrameRate = Application.targetFrameRate,
                buildGuid = Application.buildGUID,
                identifier = Application.identifier,
                version = Application.version,
                editorPaths = new
                {
                    applicationPath = EditorApplication.applicationPath,
                    applicationContentsPath = EditorApplication.applicationContentsPath,
                    isTemporaryProject = EditorApplication.isTemporaryProject
                }
            };
        }
    }
}
