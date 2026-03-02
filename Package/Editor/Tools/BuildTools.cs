using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityMCP.Editor.Services;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Build Unity player builds and poll job status.
    /// </summary>
    [MCPTool("build", "Manage player builds: start a build or poll job status", Category = "Build")]
    public static class BuildTools
    {
        [MCPAction("start", Description = "Start a player build, returns job_id for polling")]
        public static object Start(
            [MCPParam("target", "Build target (StandaloneWindows64, Android, iOS, WebGL, etc.)", required: true)] string target,
            [MCPParam("output_path", "Output path for the build", required: true)] string outputPath,
            [MCPParam("scenes", "Scenes to include (paths). If null, uses scenes from build settings.")] List<object> scenes = null,
            [MCPParam("development", "Development build with debugging support")] bool development = false)
        {
            try
            {
                if (BuildJobManager.IsBuilding)
                {
                    var currentJob = BuildJobManager.CurrentJob;
                    return new
                    {
                        success = false,
                        error = "A build is already in progress.",
                        existing_job_id = currentJob?.jobId,
                        elapsed_seconds = currentJob?.GetElapsedSeconds()
                    };
                }

                if (!TryParseBuildTarget(target, out BuildTarget buildTarget))
                {
                    return new
                    {
                        success = false,
                        error = $"Invalid build target: '{target}'. Valid targets include: StandaloneWindows64, StandaloneOSX, Android, iOS, WebGL, StandaloneLinux64, etc.",
                        valid_targets = GetValidBuildTargets()
                    };
                }

                List<string> scenePaths = GetScenePaths(scenes);
                if (scenePaths.Count == 0)
                {
                    return new
                    {
                        success = false,
                        error = "No scenes specified and no scenes enabled in build settings."
                    };
                }

                string normalizedOutputPath = NormalizeOutputPath(outputPath, buildTarget);
                string outputDirectory = Path.GetDirectoryName(normalizedOutputPath);
                if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
                {
                    try
                    {
                        Directory.CreateDirectory(outputDirectory);
                    }
                    catch (Exception directoryException)
                    {
                        return new
                        {
                            success = false,
                            error = $"Failed to create output directory: {directoryException.Message}"
                        };
                    }
                }

                var job = BuildJobManager.StartJob(target, normalizedOutputPath, scenePaths, development);
                if (job == null)
                {
                    return new
                    {
                        success = false,
                        error = "Failed to start build job."
                    };
                }

                BuildOptions options = BuildOptions.None;
                if (development)
                {
                    options |= BuildOptions.Development;
                }

                string[] sceneArray = scenePaths.ToArray();
                string capturedJobId = job.jobId;

                EditorApplication.delayCall += () =>
                {
                    BuildReport report;
                    try
                    {
                        report = BuildPipeline.BuildPlayer(
                            sceneArray,
                            normalizedOutputPath,
                            buildTarget,
                            options
                        );
                        BuildJobManager.CompleteJob(report);
                    }
                    catch (Exception buildException)
                    {
                        BuildJobManager.SetCurrentJobError($"Build threw exception: {buildException.Message}");
                    }
                };

                return new
                {
                    success = true,
                    job_id = capturedJobId,
                    status = "building",
                    target = target,
                    output_path = normalizedOutputPath,
                    development = development,
                    scene_count = scenePaths.Count,
                    message = $"Build started. Poll with build action='get_job' using job_id '{capturedJobId}' to track progress."
                };
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[BuildTools] Error starting build: {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error starting build: {exception.Message}"
                };
            }
        }

        [MCPAction("get_job", Description = "Get build job status and result", ReadOnlyHint = true)]
        public static object GetJob(
            [MCPParam("job_id", "Job ID from build start", required: true)] string jobId,
            [MCPParam("include_details", "Include detailed build steps and messages")] bool includeDetails = true)
        {
            try
            {
                if (string.IsNullOrEmpty(jobId))
                {
                    return new
                    {
                        success = false,
                        error = "job_id is required."
                    };
                }

                var job = BuildJobManager.GetJob(jobId);
                if (job == null)
                {
                    return new
                    {
                        success = false,
                        error = $"Job '{jobId}' not found. It may have expired or never existed."
                    };
                }

                return new
                {
                    success = true,
                    job = job.ToSerializable(includeDetails)
                };
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[BuildTools] Error getting job: {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error getting job: {exception.Message}"
                };
            }
        }

        #region Helper Methods

        private static bool TryParseBuildTarget(string targetString, out BuildTarget buildTarget)
        {
            if (Enum.TryParse(targetString, true, out buildTarget))
            {
                return true;
            }

            string lowerTarget = targetString.ToLowerInvariant();
            switch (lowerTarget)
            {
                case "windows": case "win64": case "windows64":
                    buildTarget = BuildTarget.StandaloneWindows64; return true;
                case "win32": case "windows32":
                    buildTarget = BuildTarget.StandaloneWindows; return true;
                case "mac": case "macos": case "osx":
                    buildTarget = BuildTarget.StandaloneOSX; return true;
                case "linux": case "linux64":
                    buildTarget = BuildTarget.StandaloneLinux64; return true;
                case "web":
                    buildTarget = BuildTarget.WebGL; return true;
                default:
                    buildTarget = default; return false;
            }
        }

        private static List<string> GetValidBuildTargets()
        {
            return new List<string>
            {
                "StandaloneWindows64", "StandaloneWindows", "StandaloneOSX",
                "StandaloneLinux64", "Android", "iOS", "WebGL",
                "tvOS", "PS4", "PS5", "XboxOne", "Switch"
            };
        }

        private static List<string> GetScenePaths(List<object> scenes)
        {
            if (scenes != null && scenes.Count > 0)
            {
                return scenes
                    .Select(s => s?.ToString())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }

            return EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToList();
        }

        private static string NormalizeOutputPath(string outputPath, BuildTarget target)
        {
            string extension = GetBuildExtension(target);
            if (!string.IsNullOrEmpty(extension))
            {
                string currentExtension = Path.GetExtension(outputPath);
                if (string.IsNullOrEmpty(currentExtension))
                {
                    return outputPath + extension;
                }
            }
            return outputPath;
        }

        private static string GetBuildExtension(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return ".exe";
                case BuildTarget.StandaloneOSX:
                    return ".app";
                case BuildTarget.Android:
                    return ".apk";
                default:
                    return "";
            }
        }

        #endregion
    }
}
