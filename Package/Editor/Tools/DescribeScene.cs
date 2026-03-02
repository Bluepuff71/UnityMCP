using System;
using System.Collections.Generic;
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
    /// Provides an AI-optimized narrative summary of the active scene, including
    /// camera, lighting, key objects, and lightweight issue detection.
    /// </summary>
    public static class DescribeScene
    {
        #region Constants

        private const int DefaultPageSize = 50;
        private const int MaxPageSize = 200;

        // Mode bit flags for log entry types (mirrors ReadConsole.cs)
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
        private static FieldInfo messageField;

        private static bool isReflectionInitialized;
        private static string reflectionError;

        static DescribeScene()
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
                    reflectionError = "Could not find LogEntries or LogEntry type.";
                    return;
                }

                startGettingEntriesMethod = logEntriesType.GetMethod("StartGettingEntries", BindingFlags.Static | BindingFlags.Public);
                endGettingEntriesMethod = logEntriesType.GetMethod("EndGettingEntries", BindingFlags.Static | BindingFlags.Public);
                getCountMethod = logEntriesType.GetMethod("GetCount", BindingFlags.Static | BindingFlags.Public);
                getEntryInternalMethod = logEntriesType.GetMethod("GetEntryInternal", BindingFlags.Static | BindingFlags.Public);

                modeField = logEntryType.GetField("mode", BindingFlags.Instance | BindingFlags.Public);
                messageField = logEntryType.GetField("message", BindingFlags.Instance | BindingFlags.Public);

                if (startGettingEntriesMethod == null || endGettingEntriesMethod == null ||
                    getCountMethod == null || getEntryInternalMethod == null ||
                    modeField == null || messageField == null)
                {
                    reflectionError = "Could not find one or more required methods/fields on LogEntries/LogEntry.";
                    return;
                }

                isReflectionInitialized = true;
            }
            catch (Exception exception)
            {
                reflectionError = $"Failed to initialize reflection: {exception.Message}";
            }
        }

        #endregion

        #region Main Tool Entry Point

        /// <summary>
        /// Returns a narrative, AI-optimized summary of the active scene including
        /// camera info, lighting, key objects, and lightweight issue detection.
        /// </summary>
        [MCPTool("scene_describe", "Returns a narrative summary of the active scene including camera, lighting, key objects, and issue detection. Good starting point to understand scene state before making changes.",
            Category = "Scene", ReadOnlyHint = true)]
        public static object Describe(
            [MCPParam("page_size", "Maximum number of root objects to include per page (default: 50)", Minimum = 1, Maximum = 200)] int pageSize = DefaultPageSize,
            [MCPParam("cursor", "Starting index for root object pagination (default: 0)", Minimum = 0)] int cursor = 0)
        {
            try
            {
                // Check for prefab stage first
                var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                Scene activeScene;
                bool inPrefabMode = false;

                if (prefabStage != null)
                {
                    activeScene = prefabStage.scene;
                    inPrefabMode = true;
                }
                else
                {
                    activeScene = EditorSceneManager.GetActiveScene();
                }

                if (!activeScene.IsValid() || !activeScene.isLoaded)
                {
                    return new
                    {
                        success = false,
                        error = "No valid and loaded scene is active."
                    };
                }

                // Clamp pagination parameters
                int resolvedPageSize = Mathf.Clamp(pageSize, 1, MaxPageSize);
                int resolvedCursor = Mathf.Max(0, cursor);

                // Gather scene data
                GameObject[] rootObjects = activeScene.GetRootGameObjects();
                int rootCount = rootObjects.Length;

                // Calculate total object count across the entire scene
                int totalObjectCount = 0;
                foreach (var rootObject in rootObjects)
                {
                    if (rootObject != null)
                    {
                        totalObjectCount += rootObject.GetComponentsInChildren<Transform>(true).Length;
                    }
                }

                // Build the narrative summary
                var summaryBuilder = new StringBuilder();

                // --- Scene header ---
                string sceneName = string.IsNullOrEmpty(activeScene.name) ? "(Untitled)" : activeScene.name;
                summaryBuilder.AppendLine($"Scene: \"{sceneName}\" ({rootCount} root objects, {totalObjectCount} total)");

                if (inPrefabMode)
                {
                    summaryBuilder.AppendLine("  [Currently in Prefab Editing Mode]");
                }

                // --- Camera section ---
                AppendCameraSection(summaryBuilder, rootObjects);

                // --- Lighting section ---
                AppendLightingSection(summaryBuilder, rootObjects);

                // --- Key Objects (paginated) ---
                if (resolvedCursor > rootCount)
                {
                    resolvedCursor = rootCount;
                }

                int endIndex = Mathf.Min(rootCount, resolvedCursor + resolvedPageSize);
                bool truncated = endIndex < rootCount;

                summaryBuilder.AppendLine();
                if (resolvedCursor >= endIndex && rootCount > 0)
                {
                    summaryBuilder.AppendLine($"Key Objects: (no objects at offset {resolvedCursor}, {rootCount} total)");
                }
                else if (truncated || resolvedCursor > 0)
                {
                    summaryBuilder.AppendLine($"Key Objects: (root GameObjects {resolvedCursor + 1}-{endIndex} of {rootCount})");
                }
                else
                {
                    summaryBuilder.AppendLine("Key Objects:");
                }

                for (int i = resolvedCursor; i < endIndex; i++)
                {
                    var rootObject = rootObjects[i];
                    if (rootObject == null)
                    {
                        continue;
                    }

                    AppendGameObjectSummaryLine(summaryBuilder, rootObject);
                }

                // --- Issues section ---
                var detectedIssues = DetectIssues(rootObjects);
                summaryBuilder.AppendLine();
                if (detectedIssues.Count > 0)
                {
                    summaryBuilder.AppendLine("Issues Detected:");
                    foreach (string issue in detectedIssues)
                    {
                        summaryBuilder.AppendLine($"  - {issue}");
                    }
                }
                else
                {
                    summaryBuilder.AppendLine("Issues Detected: None");
                }

                int? nextCursor = truncated ? endIndex : (int?)null;

                return new
                {
                    success = true,
                    description = summaryBuilder.ToString().TrimEnd(),
                    cursor = resolvedCursor,
                    pageSize = resolvedPageSize,
                    nextCursor,
                    total = rootCount,
                    truncated
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error describing scene: {exception.Message}"
                };
            }
        }

        #endregion

        #region Camera Section

        /// <summary>
        /// Appends camera information to the summary. Reports the main camera or first found camera.
        /// </summary>
        private static void AppendCameraSection(StringBuilder summaryBuilder, GameObject[] rootObjects)
        {
            // Find all cameras in the scene (including inactive and nested)
            var allCameras = new List<Camera>();
            foreach (var rootObject in rootObjects)
            {
                if (rootObject == null)
                {
                    continue;
                }

                var cameras = rootObject.GetComponentsInChildren<Camera>(true);
                allCameras.AddRange(cameras);
            }

            if (allCameras.Count == 0)
            {
                summaryBuilder.AppendLine("Camera: None found");
                return;
            }

            // Prefer the main camera, otherwise use the first one
            Camera primaryCamera = Camera.main;
            if (primaryCamera == null)
            {
                primaryCamera = allCameras[0];
            }

            Vector3 cameraPosition = primaryCamera.transform.position;
            string positionText = FormatVector3(cameraPosition);

            summaryBuilder.Append($"Camera: \"{primaryCamera.gameObject.name}\" at {positionText}");
            summaryBuilder.Append($", FOV {primaryCamera.fieldOfView:F1}");
            summaryBuilder.AppendLine($", clipping {primaryCamera.nearClipPlane:G4}-{primaryCamera.farClipPlane:G4}");

            if (allCameras.Count > 1)
            {
                summaryBuilder.AppendLine($"  ({allCameras.Count} cameras total in scene)");
            }
        }

        #endregion

        #region Lighting Section

        /// <summary>
        /// Appends lighting information to the summary with a count and breakdown by type.
        /// </summary>
        private static void AppendLightingSection(StringBuilder summaryBuilder, GameObject[] rootObjects)
        {
            var allLights = new List<Light>();
            foreach (var rootObject in rootObjects)
            {
                if (rootObject == null)
                {
                    continue;
                }

                var lights = rootObject.GetComponentsInChildren<Light>(true);
                allLights.AddRange(lights);
            }

            if (allLights.Count == 0)
            {
                summaryBuilder.AppendLine("Lighting: No lights in scene");
                return;
            }

            // Group lights by type for summary
            var lightTypeGroups = allLights
                .GroupBy(light => light.type)
                .OrderByDescending(group => group.Count())
                .Select(group => $"{group.Count()} {group.Key}")
                .ToList();

            string lightingSummary = string.Join(", ", lightTypeGroups);
            summaryBuilder.AppendLine($"Lighting: {allLights.Count} lights -- {lightingSummary}");
        }

        #endregion

        #region Key Objects Section

        /// <summary>
        /// Appends a single-line summary of a root GameObject including its position, components, and child count.
        /// </summary>
        private static void AppendGameObjectSummaryLine(StringBuilder summaryBuilder, GameObject gameObject)
        {
            Vector3 objectPosition = gameObject.transform.position;
            string positionText = FormatVector3(objectPosition);

            // Gather component names, excluding Transform (always present)
            var componentNames = new List<string>();
            try
            {
                var components = gameObject.GetComponents<Component>();
                foreach (var component in components)
                {
                    if (component == null)
                    {
                        componentNames.Add("(Missing Script)");
                        continue;
                    }

                    string typeName = component.GetType().Name;
                    if (typeName != "Transform" && typeName != "RectTransform")
                    {
                        componentNames.Add(typeName);
                    }
                }
            }
            catch
            {
                // Ignore errors when reading components
            }

            string componentsText = componentNames.Count > 0
                ? string.Join(", ", componentNames)
                : "none";

            int childCount = gameObject.transform.childCount;
            string activeIndicator = gameObject.activeSelf ? "" : " (inactive)";

            summaryBuilder.AppendLine($"  - \"{gameObject.name}\" at {positionText} with [{componentsText}] [{childCount} children]{activeIndicator}");
        }

        #endregion

        #region Issue Detection

        /// <summary>
        /// Performs lightweight issue detection on the scene and returns a list of issue descriptions.
        /// </summary>
        private static List<string> DetectIssues(GameObject[] rootObjects)
        {
            var issues = new List<string>();

            // Collect all GameObjects (including nested) for analysis
            var allGameObjects = new List<GameObject>();
            foreach (var rootObject in rootObjects)
            {
                if (rootObject == null)
                {
                    continue;
                }

                var transforms = rootObject.GetComponentsInChildren<Transform>(true);
                foreach (var transform in transforms)
                {
                    if (transform != null && transform.gameObject != null)
                    {
                        allGameObjects.Add(transform.gameObject);
                    }
                }
            }

            // 1. Missing material: renderer with null material
            DetectMissingMaterials(issues, allGameObjects);

            // 2. Rigidbody without collider (on non-kinematic) or collider without rigidbody concerns
            DetectRigidbodyColliderMismatch(issues, allGameObjects);

            // 3. Camera with no AudioListener in scene
            DetectMissingAudioListener(issues, allGameObjects);

            // 4. Multiple EventSystems
            DetectMultipleEventSystems(issues, allGameObjects);

            // 5. Console errors/warnings
            DetectConsoleIssues(issues);

            return issues;
        }

        /// <summary>
        /// Detects renderers with missing (null) materials.
        /// </summary>
        private static void DetectMissingMaterials(List<string> issues, List<GameObject> allGameObjects)
        {
            int missingMaterialCount = 0;
            string firstOffenderName = null;

            foreach (var gameObject in allGameObjects)
            {
                var renderer = gameObject.GetComponent<Renderer>();
                if (renderer == null)
                {
                    continue;
                }

                var sharedMaterials = renderer.sharedMaterials;
                foreach (var material in sharedMaterials)
                {
                    if (material == null)
                    {
                        missingMaterialCount++;
                        if (firstOffenderName == null)
                        {
                            firstOffenderName = gameObject.name;
                        }
                        break; // One per renderer is enough
                    }
                }
            }

            if (missingMaterialCount > 0)
            {
                string objectReference = missingMaterialCount == 1
                    ? $"on \"{firstOffenderName}\""
                    : $"on {missingMaterialCount} objects (first: \"{firstOffenderName}\")";
                issues.Add($"Missing material {objectReference}");
            }
        }

        /// <summary>
        /// Detects non-kinematic Rigidbodies without any Collider on the same GameObject.
        /// </summary>
        private static void DetectRigidbodyColliderMismatch(List<string> issues, List<GameObject> allGameObjects)
        {
            int rigidbodyWithoutColliderCount = 0;
            string firstRigidbodyOffenderName = null;

            foreach (var gameObject in allGameObjects)
            {
                var rigidbody = gameObject.GetComponent<Rigidbody>();
                if (rigidbody != null && !rigidbody.isKinematic)
                {
                    var collider = gameObject.GetComponent<Collider>();
                    if (collider == null)
                    {
                        rigidbodyWithoutColliderCount++;
                        if (firstRigidbodyOffenderName == null)
                        {
                            firstRigidbodyOffenderName = gameObject.name;
                        }
                    }
                }

                // Also check 2D variants
                var rigidbody2D = gameObject.GetComponent<Rigidbody2D>();
                if (rigidbody2D != null && rigidbody2D.bodyType != RigidbodyType2D.Kinematic)
                {
                    var collider2D = gameObject.GetComponent<Collider2D>();
                    if (collider2D == null)
                    {
                        rigidbodyWithoutColliderCount++;
                        if (firstRigidbodyOffenderName == null)
                        {
                            firstRigidbodyOffenderName = gameObject.name;
                        }
                    }
                }
            }

            if (rigidbodyWithoutColliderCount > 0)
            {
                string objectReference = rigidbodyWithoutColliderCount == 1
                    ? $"on \"{firstRigidbodyOffenderName}\""
                    : $"on {rigidbodyWithoutColliderCount} objects (first: \"{firstRigidbodyOffenderName}\")";
                issues.Add($"Non-kinematic Rigidbody without Collider {objectReference}");
            }
        }

        /// <summary>
        /// Detects scenes that have a Camera but no AudioListener anywhere in the scene.
        /// </summary>
        private static void DetectMissingAudioListener(List<string> issues, List<GameObject> allGameObjects)
        {
            bool hasCamera = false;
            bool hasAudioListener = false;

            foreach (var gameObject in allGameObjects)
            {
                if (!hasCamera && gameObject.GetComponent<Camera>() != null)
                {
                    hasCamera = true;
                }

                if (!hasAudioListener && gameObject.GetComponent<AudioListener>() != null)
                {
                    hasAudioListener = true;
                }

                if (hasCamera && hasAudioListener)
                {
                    break;
                }
            }

            if (hasCamera && !hasAudioListener)
            {
                issues.Add("Camera present but no AudioListener found in scene");
            }
        }

        /// <summary>
        /// Detects multiple EventSystem instances in the scene.
        /// </summary>
        private static void DetectMultipleEventSystems(List<string> issues, List<GameObject> allGameObjects)
        {
            // Use reflection to avoid hard dependency on UnityEngine.EventSystems
            Type eventSystemType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                eventSystemType = assembly.GetType("UnityEngine.EventSystems.EventSystem");
                if (eventSystemType != null)
                {
                    break;
                }
            }

            if (eventSystemType == null)
            {
                return; // EventSystems module not present
            }

            int eventSystemCount = 0;
            foreach (var gameObject in allGameObjects)
            {
                if (gameObject.GetComponent(eventSystemType) != null)
                {
                    eventSystemCount++;
                }
            }

            if (eventSystemCount > 1)
            {
                issues.Add($"Multiple EventSystems found ({eventSystemCount}) -- only one should be active");
            }
        }

        /// <summary>
        /// Reads the Unity Console via reflection to detect recent errors and warnings.
        /// </summary>
        private static void DetectConsoleIssues(List<string> issues)
        {
            if (!isReflectionInitialized)
            {
                return; // Silently skip if console reflection is unavailable
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

                if (errorCount > 0 || warningCount > 0)
                {
                    var consoleParts = new List<string>();
                    if (errorCount > 0)
                    {
                        consoleParts.Add($"{errorCount} error{(errorCount != 1 ? "s" : "")}");
                    }
                    if (warningCount > 0)
                    {
                        consoleParts.Add($"{warningCount} warning{(warningCount != 1 ? "s" : "")}");
                    }
                    issues.Add($"Console has {string.Join(" and ", consoleParts)} (use console_read for details)");
                }
            }
            catch
            {
                // Silently skip console reading errors -- not critical for scene description
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Formats a Vector3 as a compact readable string.
        /// </summary>
        private static string FormatVector3(Vector3 vector)
        {
            return $"({vector.x:G4}, {vector.y:G4}, {vector.z:G4})";
        }

        #endregion
    }
}
