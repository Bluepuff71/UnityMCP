using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMCP.Editor;
using UnityMCP.Editor.Core;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Provides export functionality for the active scene including unitypackage export,
    /// screenshot gallery capture, and markdown report generation.
    /// </summary>
    public static class ExportTools
    {
        #region Constants

        /// <summary>
        /// Default output directory for exports, located in the project's temporary cache path.
        /// </summary>
        private static string DefaultOutputDirectory => Path.Combine(Application.temporaryCachePath, "UnityMCP", "Exports");

        /// <summary>
        /// Camera angle definitions used for screenshot gallery captures.
        /// Each entry maps an angle name to a readable label and Euler rotation.
        /// </summary>
        private static readonly (string name, string label, Vector3 euler)[] GalleryAngles =
        {
            ("front", "Front", new Vector3(0, 0, 0)),
            ("top", "Top", new Vector3(90, 0, 0)),
            ("right", "Right", new Vector3(0, -90, 0)),
            ("isometric", "Isometric", new Vector3(30, -45, 0))
        };

        // Mode bit flags for log entry types (mirrors DescribeScene.cs / ReadConsole.cs)
        private const int ModeBitError = 1 << 0;
        private const int ModeBitAssert = 1 << 1;
        private const int ModeBitFatal = 1 << 4;
        private const int ModeBitAssetImportError = 1 << 6;
        private const int ModeBitScriptingError = 1 << 8;
        private const int ModeBitScriptCompileError = 1 << 11;
        private const int ModeBitScriptingException = 1 << 17;

        private const int ModeBitAssetImportWarning = 1 << 7;
        private const int ModeBitScriptingWarning = 1 << 9;
        private const int ModeBitScriptCompileWarning = 1 << 12;

        private const int ErrorMask = ModeBitError | ModeBitAssert | ModeBitFatal |
                                      ModeBitAssetImportError | ModeBitScriptingError |
                                      ModeBitScriptCompileError | ModeBitScriptingException;
        private const int WarningMask = ModeBitAssetImportWarning | ModeBitScriptingWarning | ModeBitScriptCompileWarning;

        #endregion

        #region Reflection Cache

        private static Type logEntriesType;
        private static Type logEntryType;

        private static MethodInfo startGettingEntriesMethod;
        private static MethodInfo endGettingEntriesMethod;
        private static MethodInfo getCountMethod;
        private static MethodInfo getEntryInternalMethod;

        private static FieldInfo modeField;

        private static bool isReflectionInitialized;

        static ExportTools()
        {
            InitializeReflection();
        }

        private static void InitializeReflection()
        {
            try
            {
                logEntriesType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.LogEntries");
                logEntryType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.LogEntry");

                if (logEntriesType == null || logEntryType == null)
                {
                    return;
                }

                startGettingEntriesMethod = logEntriesType.GetMethod("StartGettingEntries", BindingFlags.Static | BindingFlags.Public);
                endGettingEntriesMethod = logEntriesType.GetMethod("EndGettingEntries", BindingFlags.Static | BindingFlags.Public);
                getCountMethod = logEntriesType.GetMethod("GetCount", BindingFlags.Static | BindingFlags.Public);
                getEntryInternalMethod = logEntriesType.GetMethod("GetEntryInternal", BindingFlags.Static | BindingFlags.Public);

                modeField = logEntryType.GetField("mode", BindingFlags.Instance | BindingFlags.Public);

                if (startGettingEntriesMethod == null || endGettingEntriesMethod == null ||
                    getCountMethod == null || getEntryInternalMethod == null ||
                    modeField == null)
                {
                    return;
                }

                isReflectionInitialized = true;
            }
            catch (Exception)
            {
                // Silently fail -- console counts are non-critical for reports
            }
        }

        #endregion

        #region Main Tool Entry Point

        /// <summary>
        /// Exports the current scene in one of three formats: unitypackage, screenshot gallery, or markdown report.
        /// </summary>
        /// <param name="format">The export format: unitypackage, screenshot_gallery, or report.</param>
        /// <param name="outputPath">Custom output directory path. Defaults to the project temp directory.</param>
        /// <param name="includeDependencies">Whether to include asset dependencies in unitypackage exports.</param>
        /// <returns>Result object describing the export outcome.</returns>
        [MCPTool("export_scene", "Export the current scene as a unitypackage, screenshot gallery, or markdown report",
            Category = "Export", DestructiveHint = true, OpenWorldHint = true)]
        public static object Export(
            [MCPParam("format", "Export format", required: true, Enum = new[] { "unitypackage", "screenshot_gallery", "report" })] string format,
            [MCPParam("output_path", "Custom output directory path (default: project temp directory)")] string outputPath = null,
            [MCPParam("include_dependencies", "Include asset dependencies in unitypackage export (default: true)")] bool includeDependencies = true)
        {
            try
            {
                // Validate the active scene
                Scene activeScene = EditorSceneManager.GetActiveScene();
                if (!activeScene.IsValid() || !activeScene.isLoaded)
                {
                    return new
                    {
                        success = false,
                        error = "No valid and loaded scene is active."
                    };
                }

                string scenePath = activeScene.path;
                if (string.IsNullOrEmpty(scenePath))
                {
                    return new
                    {
                        success = false,
                        error = "The active scene has not been saved. Please save the scene before exporting."
                    };
                }

                // Resolve output directory
                string resolvedOutputDirectory = string.IsNullOrWhiteSpace(outputPath)
                    ? DefaultOutputDirectory
                    : outputPath;

                string normalizedFormat = (format ?? "").ToLowerInvariant().Trim();

                return normalizedFormat switch
                {
                    "unitypackage" => ExportUnityPackage(activeScene, scenePath, resolvedOutputDirectory, includeDependencies),
                    "screenshot_gallery" => ExportScreenshotGallery(activeScene, resolvedOutputDirectory),
                    "report" => ExportReport(activeScene, scenePath),
                    _ => throw MCPException.InvalidParams($"Unknown format: '{format}'. Valid formats: unitypackage, screenshot_gallery, report")
                };
            }
            catch (MCPException)
            {
                throw; // Let MCP protocol exceptions propagate
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ExportTools] Error during export: {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error during export: {exception.Message}"
                };
            }
        }

        #endregion

        #region Unitypackage Export

        /// <summary>
        /// Exports the current scene and optionally its dependencies as a .unitypackage file.
        /// </summary>
        private static object ExportUnityPackage(Scene activeScene, string scenePath, string outputDirectory, bool includeDependencies)
        {
            try
            {
                // Ensure the output directory exists
                if (!Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                // Build the output file path
                string sanitizedSceneName = SanitizeFileName(activeScene.name);
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string outputFileName = $"{sanitizedSceneName}_{timestamp}.unitypackage";
                string outputFilePath = Path.Combine(outputDirectory, outputFileName);

                // Configure export options
                ExportPackageOptions exportOptions = includeDependencies
                    ? ExportPackageOptions.IncludeDependencies | ExportPackageOptions.Recurse
                    : ExportPackageOptions.Default;

                // Perform the export
                AssetDatabase.ExportPackage(scenePath, outputFilePath, exportOptions);

                // Verify the file was created
                if (!File.Exists(outputFilePath))
                {
                    return new
                    {
                        success = false,
                        error = "Export completed but the output file was not found. The export may have failed silently."
                    };
                }

                long fileSizeBytes = new FileInfo(outputFilePath).Length;

                return new
                {
                    success = true,
                    format = "unitypackage",
                    message = $"Scene '{activeScene.name}' exported as unitypackage.",
                    file_path = outputFilePath,
                    file_size_bytes = fileSizeBytes,
                    include_dependencies = includeDependencies
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error exporting unitypackage: {exception.Message}"
                };
            }
        }

        #endregion

        #region Screenshot Gallery Export

        /// <summary>
        /// Captures screenshots of the active scene from four angles (front, top, right, isometric)
        /// using the Scene View camera and saves them as PNG files.
        /// </summary>
        private static object ExportScreenshotGallery(Scene activeScene, string outputDirectory)
        {
            try
            {
                // Get or validate Scene View
                SceneView sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null)
                {
                    return new
                    {
                        success = false,
                        error = "No active Scene View found. Open a Scene View window first."
                    };
                }

                Camera sceneCamera = sceneView.camera;
                if (sceneCamera == null)
                {
                    return new
                    {
                        success = false,
                        error = "Scene View camera is not available."
                    };
                }

                // Compute scene center from root object bounds for camera framing
                Vector3 sceneCenter = ComputeSceneCenter(activeScene);

                // Ensure the output directory exists
                string galleryDirectory = Path.Combine(outputDirectory, "ScreenshotGallery");
                if (!Directory.Exists(galleryDirectory))
                {
                    Directory.CreateDirectory(galleryDirectory);
                }

                string sanitizedSceneName = SanitizeFileName(activeScene.name);
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                int captureWidth = (int)sceneView.position.width;
                int captureHeight = (int)sceneView.position.height;

                if (captureWidth <= 0 || captureHeight <= 0)
                {
                    return new
                    {
                        success = false,
                        error = "Scene View has invalid dimensions. Ensure it is visible and has a non-zero size."
                    };
                }

                var capturedFilePaths = new List<string>();
                RenderTexture previousTargetTexture = sceneCamera.targetTexture;
                RenderTexture previousActiveRenderTexture = RenderTexture.active;

                try
                {
                    foreach (var (angleName, angleLabel, eulerAngles) in GalleryAngles)
                    {
                        // Position the camera at the desired angle
                        Quaternion angleRotation = Quaternion.Euler(eulerAngles);
                        sceneView.LookAt(sceneCenter, angleRotation);
                        sceneView.Repaint();

                        // Create a temporary RenderTexture for the capture
                        RenderTexture renderTexture = new RenderTexture(captureWidth, captureHeight, 24);

                        try
                        {
                            sceneCamera.targetTexture = renderTexture;
                            sceneCamera.Render();

                            // Read pixels into a Texture2D
                            RenderTexture.active = renderTexture;
                            Texture2D screenshotTexture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);

                            try
                            {
                                screenshotTexture.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
                                screenshotTexture.Apply();

                                // Encode and save
                                byte[] pngBytes = screenshotTexture.EncodeToPNG();
                                string screenshotFileName = $"{sanitizedSceneName}_{angleName}_{timestamp}.png";
                                string screenshotFilePath = Path.Combine(galleryDirectory, screenshotFileName);
                                File.WriteAllBytes(screenshotFilePath, pngBytes);

                                capturedFilePaths.Add(screenshotFilePath);
                            }
                            finally
                            {
                                UnityEngine.Object.DestroyImmediate(screenshotTexture);
                            }
                        }
                        finally
                        {
                            renderTexture.Release();
                            UnityEngine.Object.DestroyImmediate(renderTexture);
                        }
                    }
                }
                finally
                {
                    // Restore camera and RenderTexture state
                    sceneCamera.targetTexture = previousTargetTexture;
                    RenderTexture.active = previousActiveRenderTexture;
                }

                return new
                {
                    success = true,
                    format = "screenshot_gallery",
                    message = $"Captured {capturedFilePaths.Count} screenshots of scene '{activeScene.name}'.",
                    file_paths = capturedFilePaths,
                    resolution = new { width = captureWidth, height = captureHeight },
                    angles = GalleryAngles.Select(a => a.name).ToList()
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error capturing screenshot gallery: {exception.Message}"
                };
            }
        }

        #endregion

        #region Report Export

        /// <summary>
        /// Generates a Markdown summary report of the active scene including hierarchy overview,
        /// component inventory, asset dependencies, and console status.
        /// </summary>
        private static object ExportReport(Scene activeScene, string scenePath)
        {
            try
            {
                var reportBuilder = new StringBuilder();
                GameObject[] rootObjects = activeScene.GetRootGameObjects();

                // --- Scene Header ---
                string sceneName = string.IsNullOrEmpty(activeScene.name) ? "(Untitled)" : activeScene.name;
                reportBuilder.AppendLine($"# Scene Report: {sceneName}");
                reportBuilder.AppendLine();
                reportBuilder.AppendLine($"- **Path:** `{scenePath}`");
                reportBuilder.AppendLine($"- **Root Objects:** {rootObjects.Length}");

                int totalObjectCount = 0;
                foreach (var rootObject in rootObjects)
                {
                    if (rootObject != null)
                    {
                        totalObjectCount += rootObject.GetComponentsInChildren<Transform>(true).Length;
                    }
                }
                reportBuilder.AppendLine($"- **Total GameObjects:** {totalObjectCount}");
                reportBuilder.AppendLine();

                // --- Hierarchy Overview ---
                reportBuilder.AppendLine("## Hierarchy Overview");
                reportBuilder.AppendLine();
                reportBuilder.AppendLine("| Root Object | Children | Active |");
                reportBuilder.AppendLine("|---|---|---|");

                foreach (var rootObject in rootObjects)
                {
                    if (rootObject == null)
                    {
                        continue;
                    }

                    // Child count is total descendants minus the root itself
                    int descendantCount = rootObject.GetComponentsInChildren<Transform>(true).Length - 1;
                    string activeStatus = rootObject.activeSelf ? "Yes" : "No";
                    reportBuilder.AppendLine($"| {rootObject.name} | {descendantCount} | {activeStatus} |");
                }

                reportBuilder.AppendLine();

                // --- Component Inventory ---
                reportBuilder.AppendLine("## Component Inventory");
                reportBuilder.AppendLine();

                var componentTypeCounts = new Dictionary<string, int>();

                foreach (var rootObject in rootObjects)
                {
                    if (rootObject == null)
                    {
                        continue;
                    }

                    var allComponents = rootObject.GetComponentsInChildren<Component>(true);
                    foreach (var component in allComponents)
                    {
                        if (component == null)
                        {
                            // Track missing scripts separately
                            string missingKey = "(Missing Script)";
                            componentTypeCounts.TryGetValue(missingKey, out int missingCount);
                            componentTypeCounts[missingKey] = missingCount + 1;
                            continue;
                        }

                        string typeName = component.GetType().Name;
                        componentTypeCounts.TryGetValue(typeName, out int currentCount);
                        componentTypeCounts[typeName] = currentCount + 1;
                    }
                }

                // Sort by count descending, then by name
                var sortedComponentTypes = componentTypeCounts
                    .OrderByDescending(kvp => kvp.Value)
                    .ThenBy(kvp => kvp.Key)
                    .ToList();

                reportBuilder.AppendLine("| Component Type | Count |");
                reportBuilder.AppendLine("|---|---|");

                foreach (var kvp in sortedComponentTypes)
                {
                    reportBuilder.AppendLine($"| {kvp.Key} | {kvp.Value} |");
                }

                reportBuilder.AppendLine();

                // --- Asset Dependencies ---
                reportBuilder.AppendLine("## Asset Dependencies");
                reportBuilder.AppendLine();

                string[] dependencies = AssetDatabase.GetDependencies(scenePath, recursive: true);

                // Group dependencies by file extension
                var dependencyGroups = dependencies
                    .Where(dependencyPath => !string.IsNullOrEmpty(dependencyPath))
                    .GroupBy(dependencyPath => Path.GetExtension(dependencyPath).ToLowerInvariant())
                    .OrderByDescending(group => group.Count())
                    .ToList();

                reportBuilder.AppendLine($"**Total Dependencies:** {dependencies.Length}");
                reportBuilder.AppendLine();
                reportBuilder.AppendLine("| Extension | Count |");
                reportBuilder.AppendLine("|---|---|");

                foreach (var group in dependencyGroups)
                {
                    string extensionLabel = string.IsNullOrEmpty(group.Key) ? "(no extension)" : group.Key;
                    reportBuilder.AppendLine($"| {extensionLabel} | {group.Count()} |");
                }

                reportBuilder.AppendLine();

                // --- Console Status ---
                reportBuilder.AppendLine("## Console Status");
                reportBuilder.AppendLine();

                AppendConsoleStatus(reportBuilder);

                string reportContent = reportBuilder.ToString().TrimEnd();

                return new
                {
                    success = true,
                    format = "report",
                    message = $"Report generated for scene '{activeScene.name}'.",
                    report = reportContent
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error generating report: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Appends console error and warning counts to the report using reflection-based log access.
        /// </summary>
        private static void AppendConsoleStatus(StringBuilder reportBuilder)
        {
            if (!isReflectionInitialized)
            {
                reportBuilder.AppendLine("Console API not available (reflection initialization failed).");
                return;
            }

            try
            {
                int errorCount = 0;
                int warningCount = 0;

                startGettingEntriesMethod.Invoke(null, null);

                try
                {
                    int totalEntryCount = (int)getCountMethod.Invoke(null, null);
                    object logEntry = Activator.CreateInstance(logEntryType);

                    for (int i = 0; i < totalEntryCount; i++)
                    {
                        getEntryInternalMethod.Invoke(null, new object[] { i, logEntry });
                        int mode = (int)modeField.GetValue(logEntry);

                        if ((mode & ErrorMask) != 0)
                        {
                            errorCount++;
                        }
                        else if ((mode & WarningMask) != 0)
                        {
                            warningCount++;
                        }
                    }
                }
                finally
                {
                    endGettingEntriesMethod.Invoke(null, null);
                }

                if (errorCount == 0 && warningCount == 0)
                {
                    reportBuilder.AppendLine("No errors or warnings in the console.");
                }
                else
                {
                    reportBuilder.AppendLine($"- **Errors:** {errorCount}");
                    reportBuilder.AppendLine($"- **Warnings:** {warningCount}");
                }
            }
            catch (Exception)
            {
                reportBuilder.AppendLine("Unable to read console entries.");
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Computes the center point of all renderable objects in the scene for camera framing.
        /// Falls back to Vector3.zero if no renderers are found.
        /// </summary>
        private static Vector3 ComputeSceneCenter(Scene activeScene)
        {
            GameObject[] rootObjects = activeScene.GetRootGameObjects();
            Bounds combinedBounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;

            foreach (var rootObject in rootObjects)
            {
                if (rootObject == null)
                {
                    continue;
                }

                var renderers = rootObject.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in renderers)
                {
                    if (renderer == null)
                    {
                        continue;
                    }

                    if (!hasBounds)
                    {
                        combinedBounds = renderer.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        combinedBounds.Encapsulate(renderer.bounds);
                    }
                }
            }

            return hasBounds ? combinedBounds.center : Vector3.zero;
        }

        /// <summary>
        /// Sanitizes a string for use as a file name by replacing invalid characters with underscores.
        /// </summary>
        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return "Untitled";
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new StringBuilder(fileName.Length);

            foreach (char character in fileName)
            {
                if (Array.IndexOf(invalidChars, character) >= 0)
                {
                    sanitized.Append('_');
                }
                else
                {
                    sanitized.Append(character);
                }
            }

            return sanitized.ToString();
        }

        #endregion
    }
}
