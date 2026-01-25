using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace UnityMCP.Editor.Resources.Build
{
    /// <summary>
    /// Resource provider for build configuration settings.
    /// </summary>
    public static class BuildSettings
    {
        /// <summary>
        /// Gets the current build configuration settings including target platform,
        /// scenes in build, and various build options.
        /// </summary>
        /// <returns>Object containing build settings information.</returns>
        [MCPResource("build://settings", "Build target, scenes, and configuration")]
        public static object GetBuildSettings()
        {
            // Get scenes in build settings
            var scenesInBuild = new List<object>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                scenesInBuild.Add(new
                {
                    path = scene.path,
                    enabled = scene.enabled,
                    guid = scene.guid.ToString()
                });
            }

            // Get current build target info
            var activeBuildTarget = EditorUserBuildSettings.activeBuildTarget;
            var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(activeBuildTarget);

            // Get scripting defines for current platform
            PlayerSettings.GetScriptingDefineSymbols(
                UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup),
                out string[] scriptingDefines);

            return new
            {
                target = new
                {
                    platform = activeBuildTarget.ToString(),
                    group = buildTargetGroup.ToString(),
                    isSupported = BuildPipeline.IsBuildTargetSupported(buildTargetGroup, activeBuildTarget)
                },
                scenes = new
                {
                    count = scenesInBuild.Count,
                    enabledCount = scenesInBuild.Count(s => ((dynamic)s).enabled),
                    list = scenesInBuild.ToArray()
                },
                options = new
                {
                    development = EditorUserBuildSettings.development,
                    allowDebugging = EditorUserBuildSettings.allowDebugging,
                    buildScriptsOnly = EditorUserBuildSettings.buildScriptsOnly,
                    compressWithLz4 = EditorUserBuildSettings.GetCompressionType(buildTargetGroup).ToString(),
                    exportAsGoogleAndroidProject = buildTargetGroup == BuildTargetGroup.Android
                        ? EditorUserBuildSettings.exportAsGoogleAndroidProject
                        : (bool?)null
                },
                player = new
                {
                    companyName = PlayerSettings.companyName,
                    productName = PlayerSettings.productName,
                    bundleVersion = PlayerSettings.bundleVersion,
                    scriptingBackend = PlayerSettings.GetScriptingBackend(
                        UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup)).ToString(),
                    apiCompatibilityLevel = PlayerSettings.GetApiCompatibilityLevel(
                        UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup)).ToString()
                },
                scripting = new
                {
                    defines = scriptingDefines,
                    defineCount = scriptingDefines.Length
                },
                paths = new
                {
                    lastBuildLocation = EditorUserBuildSettings.GetBuildLocation(activeBuildTarget)
                }
            };
        }
    }
}
