using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityMCP.Editor;
using UnityMCP.Editor.Core;

#pragma warning disable CS0618 // EditorUtility.InstanceIDToObject is deprecated but still functional

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Diagnostic tool that inspects the current editor state and returns structured findings
    /// across console errors, missing references, component issues, shader issues, and build readiness.
    /// </summary>
    public static class DiagnoseScene
    {
        #region Constants

        private const int MaxIssues = 50;
        private const int MaxConsoleEntriesToScan = 200;
        private const int MaxMessageLength = 200;

        // Console log mode bit flags (from Unity's internal ConsoleFlags)
        private const int ModeBitError = 1 << 0;
        private const int ModeBitAssert = 1 << 1;
        private const int ModeBitFatal = 1 << 4;
        private const int ModeBitAssetImportError = 1 << 6;
        private const int ModeBitScriptingError = 1 << 8;
        private const int ModeBitScriptCompileError = 1 << 11;
        private const int ModeBitScriptingException = 1 << 17;

        private const int ErrorMask = ModeBitError | ModeBitAssert | ModeBitFatal |
                                      ModeBitAssetImportError | ModeBitScriptingError |
                                      ModeBitScriptCompileError | ModeBitScriptingException;

        /// <summary>
        /// All valid check category names.
        /// </summary>
        private static readonly HashSet<string> ValidCheckNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "console", "references", "components", "shaders", "build"
        };

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

        static DiagnoseScene()
        {
            InitializeConsoleReflection();
        }

        private static void InitializeConsoleReflection()
        {
            try
            {
                logEntriesType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.LogEntries");
                logEntryType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.LogEntry");

                if (logEntriesType == null || logEntryType == null)
                {
                    reflectionError = "Could not find LogEntries or LogEntry types.";
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
                    reflectionError = "Could not find one or more required reflection members.";
                    return;
                }

                isReflectionInitialized = true;
            }
            catch (Exception exception)
            {
                reflectionError = $"Failed to initialize console reflection: {exception.Message}";
            }
        }

        #endregion

        #region Main Tool Entry Point

        /// <summary>
        /// Diagnoses the current editor state and returns structured findings across multiple categories.
        /// </summary>
        [MCPTool("diagnose", "Scans for missing references, shader issues, console errors, and build readiness. Use when something looks wrong or before building to catch problems early.", ReadOnlyHint = true, Category = "Diagnostics")]
        public static object Diagnose(
            [MCPParam("scope", "Scope of inspection: 'selected' (current selection), 'scene' (active scene), or 'project' (shader/build checks only)", Enum = new[] { "selected", "scene", "project" })] string scope = "scene",
            [MCPParam("checks", "Optional array of specific checks to run: console, references, components, shaders, build")] List<object> checks = null)
        {
            try
            {
                string normalizedScope = (scope ?? "scene").ToLowerInvariant().Trim();
                if (normalizedScope != "selected" && normalizedScope != "scene" && normalizedScope != "project")
                {
                    throw MCPException.InvalidParams($"Invalid scope: '{scope}'. Valid scopes: selected, scene, project");
                }

                // Parse which checks to run
                HashSet<string> checksToRun = ParseChecksFilter(checks);

                // Resolve GameObjects for the given scope
                GameObject[] scopeGameObjects = ResolveScope(normalizedScope);

                var allIssues = new List<Dictionary<string, object>>();

                // 1. Console errors
                if (checksToRun.Contains("console"))
                {
                    CheckConsoleErrors(allIssues);
                }

                // 2. Missing references (skip for project scope)
                if (checksToRun.Contains("references") && normalizedScope != "project")
                {
                    CheckMissingReferences(allIssues, scopeGameObjects);
                }

                // 3. Component issues (skip for project scope)
                if (checksToRun.Contains("components") && normalizedScope != "project")
                {
                    CheckComponentIssues(allIssues, scopeGameObjects);
                }

                // 4. Shader issues
                if (checksToRun.Contains("shaders"))
                {
                    if (normalizedScope == "project")
                    {
                        CheckShaderIssuesProject(allIssues);
                    }
                    else
                    {
                        CheckShaderIssues(allIssues, scopeGameObjects);
                    }
                }

                // 5. Build readiness
                if (checksToRun.Contains("build"))
                {
                    CheckBuildReadiness(allIssues);
                }

                // Cap total issues
                bool issuesTruncated = allIssues.Count > MaxIssues;
                int totalIssueCount = allIssues.Count;
                if (issuesTruncated)
                {
                    allIssues = allIssues.Take(MaxIssues).ToList();
                }

                // Build summary counts
                int errorCount = allIssues.Count(issue => (string)issue["severity"] == "error");
                int warningCount = allIssues.Count(issue => (string)issue["severity"] == "warning");
                int infoCount = allIssues.Count(issue => (string)issue["severity"] == "info");

                return new
                {
                    success = true,
                    scope = normalizedScope,
                    checksRun = checksToRun.OrderBy(c => c).ToList(),
                    summary = new
                    {
                        total = totalIssueCount,
                        errors = errorCount,
                        warnings = warningCount,
                        info = infoCount,
                        truncated = issuesTruncated ? (bool?)true : null
                    },
                    issues = allIssues
                };
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error running diagnostics: {exception.Message}"
                };
            }
        }

        #endregion

        #region Scope Resolution

        /// <summary>
        /// Resolves the set of GameObjects to inspect based on the given scope.
        /// </summary>
        private static GameObject[] ResolveScope(string scope)
        {
            switch (scope)
            {
                case "selected":
                    return Selection.gameObjects;

                case "scene":
                    return GetAllSceneGameObjects();

                case "project":
                    // Project scope does not scan individual GameObjects
                    return Array.Empty<GameObject>();

                default:
                    return Array.Empty<GameObject>();
            }
        }

        /// <summary>
        /// Gets all GameObjects in the active scene, including children of root objects.
        /// </summary>
        private static GameObject[] GetAllSceneGameObjects()
        {
            Scene activeScene = EditorSceneManager.GetActiveScene();
            GameObject[] rootGameObjects = activeScene.GetRootGameObjects();
            var allGameObjects = new List<GameObject>();

            foreach (GameObject rootObject in rootGameObjects)
            {
                if (rootObject == null)
                {
                    continue;
                }

                allGameObjects.Add(rootObject);

                Transform[] childTransforms = rootObject.GetComponentsInChildren<Transform>(true);
                foreach (Transform childTransform in childTransforms)
                {
                    if (childTransform != null && childTransform.gameObject != rootObject)
                    {
                        allGameObjects.Add(childTransform.gameObject);
                    }
                }
            }

            return allGameObjects.ToArray();
        }

        #endregion

        #region Check: Console Errors

        /// <summary>
        /// Reads recent console errors/exceptions, groups them by type, and suggests common fixes.
        /// </summary>
        private static void CheckConsoleErrors(List<Dictionary<string, object>> issues)
        {
            if (!isReflectionInitialized)
            {
                issues.Add(CreateIssue("warning", "console",
                    $"Console API not available: {reflectionError}",
                    "This Unity version may not support console reflection."));
                return;
            }

            try
            {
                var errorGroups = new Dictionary<string, int>();
                int totalErrorCount = 0;

                startGettingEntriesMethod.Invoke(null, null);

                try
                {
                    int consoleEntryCount = (int)getCountMethod.Invoke(null, null);
                    object logEntry = Activator.CreateInstance(logEntryType);
                    int entriesToScan = Math.Min(consoleEntryCount, MaxConsoleEntriesToScan);

                    // Scan from the most recent entries backwards
                    int startIndex = Math.Max(0, consoleEntryCount - entriesToScan);

                    for (int i = startIndex; i < consoleEntryCount; i++)
                    {
                        getEntryInternalMethod.Invoke(null, new object[] { i, logEntry });

                        int mode = (int)modeField.GetValue(logEntry);

                        if ((mode & ErrorMask) == 0)
                        {
                            continue;
                        }

                        totalErrorCount++;

                        string message = (string)messageField.GetValue(logEntry) ?? "";
                        string firstLine = ExtractFirstLine(message);

                        // Truncate for grouping key
                        string groupingKey = firstLine.Length > MaxMessageLength
                            ? firstLine.Substring(0, MaxMessageLength)
                            : firstLine;

                        if (errorGroups.ContainsKey(groupingKey))
                        {
                            errorGroups[groupingKey]++;
                        }
                        else
                        {
                            errorGroups[groupingKey] = 1;
                        }
                    }
                }
                finally
                {
                    endGettingEntriesMethod.Invoke(null, null);
                }

                if (totalErrorCount == 0)
                {
                    return;
                }

                // Sort by count descending and emit issues for top error groups
                var sortedErrorGroups = errorGroups
                    .OrderByDescending(kvp => kvp.Value)
                    .ToList();

                foreach (var errorGroup in sortedErrorGroups)
                {
                    if (issues.Count >= MaxIssues)
                    {
                        break;
                    }

                    string countSuffix = errorGroup.Value > 1 ? $" ({errorGroup.Value} occurrences)" : "";
                    string suggestion = SuggestConsoleErrorFix(errorGroup.Key);

                    issues.Add(CreateIssue("error", "console",
                        $"{errorGroup.Key}{countSuffix}",
                        suggestion));
                }
            }
            catch (Exception exception)
            {
                issues.Add(CreateIssue("warning", "console",
                    $"Failed to read console: {exception.Message}",
                    "Try using 'console_read' tool directly."));
            }
        }

        /// <summary>
        /// Suggests a fix for common console error patterns.
        /// </summary>
        private static string SuggestConsoleErrorFix(string errorMessage)
        {
            string lowerMessage = errorMessage.ToLowerInvariant();

            if (lowerMessage.Contains("nullreferenceexception"))
            {
                return "Check for unassigned references or destroyed objects. Use null checks before accessing object members.";
            }

            if (lowerMessage.Contains("missingreferenceexception"))
            {
                return "A referenced object has been destroyed. Ensure references are cleared when objects are removed.";
            }

            if (lowerMessage.Contains("indexoutofrangeexception"))
            {
                return "An array or list index is out of bounds. Verify collection size before accessing elements.";
            }

            if (lowerMessage.Contains("stackoverflow"))
            {
                return "Infinite recursion detected. Check for circular method calls or recursive property access.";
            }

            if (lowerMessage.Contains("cannot load") || lowerMessage.Contains("failed to load"))
            {
                return "Asset could not be loaded. Verify the asset exists and the path is correct.";
            }

            if (lowerMessage.Contains("shader error") || lowerMessage.Contains("shader compilation"))
            {
                return "Shader compilation failed. Check shader syntax and target platform compatibility.";
            }

            if (lowerMessage.Contains("script compilation") || lowerMessage.Contains("cs0"))
            {
                return "Script compilation error. Fix the reported C# errors before entering Play mode.";
            }

            if (lowerMessage.Contains("the type or namespace"))
            {
                return "Missing type or namespace. Add the required 'using' directive or install the missing package.";
            }

            if (lowerMessage.Contains("serializationexception"))
            {
                return "Serialization error. Ensure all serialized types are marked [Serializable] and have valid default constructors.";
            }

            return "Review the error message and stacktrace. Use 'console_read' for more details.";
        }

        #endregion

        #region Check: Missing References

        /// <summary>
        /// Scans GameObjects for null serialized references (assigned references that point to missing objects).
        /// </summary>
        private static void CheckMissingReferences(List<Dictionary<string, object>> issues, GameObject[] gameObjects)
        {
            foreach (GameObject gameObject in gameObjects)
            {
                if (issues.Count >= MaxIssues)
                {
                    break;
                }

                if (gameObject == null)
                {
                    continue;
                }

                Component[] components = gameObject.GetComponents<Component>();
                foreach (Component component in components)
                {
                    if (issues.Count >= MaxIssues)
                    {
                        break;
                    }

                    // A null component entry means a missing script
                    if (component == null)
                    {
                        string gameObjectPath = GetGameObjectPath(gameObject);
                        issues.Add(CreateIssue("error", "references",
                            $"Missing script on '{gameObjectPath}'.",
                            "Remove the missing script component or re-import the script asset."));
                        continue;
                    }

                    try
                    {
                        var serializedObject = new SerializedObject(component);
                        var property = serializedObject.GetIterator();

                        while (property.NextVisible(true))
                        {
                            if (issues.Count >= MaxIssues)
                            {
                                break;
                            }

                            if (property.propertyType == SerializedPropertyType.ObjectReference &&
                                property.objectReferenceValue == null &&
                                property.objectReferenceInstanceIDValue != 0)
                            {
                                string gameObjectPath = GetGameObjectPath(gameObject);
                                string componentTypeName = component.GetType().Name;

                                issues.Add(CreateIssue("warning", "references",
                                    $"Missing reference: '{property.propertyPath}' on {componentTypeName} at '{gameObjectPath}'.",
                                    "Re-assign the reference in the Inspector or remove the component if it is no longer needed."));
                            }
                        }
                    }
                    catch
                    {
                        // Skip components that fail serialization inspection
                    }
                }
            }
        }

        #endregion

        #region Check: Component Issues

        /// <summary>
        /// Checks for common component configuration problems:
        /// Rigidbody without collider, multiple cameras, and duplicate EventSystems.
        /// </summary>
        private static void CheckComponentIssues(List<Dictionary<string, object>> issues, GameObject[] gameObjects)
        {
            int activeCameraCount = 0;
            int eventSystemCount = 0;
            Type eventSystemType = ResolveType("UnityEngine.EventSystems.EventSystem");

            foreach (GameObject gameObject in gameObjects)
            {
                if (issues.Count >= MaxIssues)
                {
                    break;
                }

                if (gameObject == null)
                {
                    continue;
                }

                string gameObjectPath = GetGameObjectPath(gameObject);

                // Check for Rigidbody without Collider (3D)
                Rigidbody rigidbody3D = gameObject.GetComponent<Rigidbody>();
                if (rigidbody3D != null)
                {
                    Collider collider3D = gameObject.GetComponent<Collider>();
                    if (collider3D == null)
                    {
                        issues.Add(CreateIssue("warning", "components",
                            $"Rigidbody without Collider on '{gameObjectPath}'.",
                            "Add a Collider component (BoxCollider, SphereCollider, etc.) for physics interactions."));
                    }
                }

                // Check for Rigidbody2D without Collider2D
                Rigidbody2D rigidbody2D = gameObject.GetComponent<Rigidbody2D>();
                if (rigidbody2D != null)
                {
                    Collider2D collider2D = gameObject.GetComponent<Collider2D>();
                    if (collider2D == null)
                    {
                        issues.Add(CreateIssue("warning", "components",
                            $"Rigidbody2D without Collider2D on '{gameObjectPath}'.",
                            "Add a Collider2D component (BoxCollider2D, CircleCollider2D, etc.) for physics interactions."));
                    }
                }

                // Count active cameras
                Camera camera = gameObject.GetComponent<Camera>();
                if (camera != null && camera.enabled && gameObject.activeInHierarchy)
                {
                    activeCameraCount++;
                }

                // Count EventSystems
                if (eventSystemType != null)
                {
                    Component eventSystem = gameObject.GetComponent(eventSystemType);
                    if (eventSystem != null)
                    {
                        eventSystemCount++;
                    }
                }
            }

            // Report multiple cameras
            if (activeCameraCount > 1)
            {
                issues.Add(CreateIssue("warning", "components",
                    $"Multiple active cameras detected ({activeCameraCount}).",
                    "Ensure only one camera is active at a time, or configure their depth/render targets properly."));
            }

            // Report duplicate EventSystems
            if (eventSystemCount > 1)
            {
                issues.Add(CreateIssue("warning", "components",
                    $"Duplicate EventSystems detected ({eventSystemCount}).",
                    "Remove extra EventSystem objects. Only one EventSystem should exist per scene."));
            }
        }

        #endregion

        #region Check: Shader Issues

        /// <summary>
        /// Checks renderers in the given GameObjects for materials using error or fallback shaders.
        /// </summary>
        private static void CheckShaderIssues(List<Dictionary<string, object>> issues, GameObject[] gameObjects)
        {
            foreach (GameObject gameObject in gameObjects)
            {
                if (issues.Count >= MaxIssues)
                {
                    break;
                }

                if (gameObject == null)
                {
                    continue;
                }

                Renderer[] renderers = gameObject.GetComponents<Renderer>();
                foreach (Renderer renderer in renderers)
                {
                    if (issues.Count >= MaxIssues)
                    {
                        break;
                    }

                    if (renderer == null)
                    {
                        continue;
                    }

                    Material[] sharedMaterials = renderer.sharedMaterials;
                    foreach (Material material in sharedMaterials)
                    {
                        if (issues.Count >= MaxIssues)
                        {
                            break;
                        }

                        if (material == null)
                        {
                            string gameObjectPath = GetGameObjectPath(gameObject);
                            issues.Add(CreateIssue("warning", "shaders",
                                $"Null material slot on Renderer at '{gameObjectPath}'.",
                                "Assign a valid material to the renderer's material slot."));
                            continue;
                        }

                        if (material.shader == null)
                        {
                            continue;
                        }

                        string shaderName = material.shader.name;
                        if (IsErrorShader(shaderName))
                        {
                            string gameObjectPath = GetGameObjectPath(gameObject);
                            issues.Add(CreateIssue("error", "shaders",
                                $"Material '{material.name}' using error shader '{shaderName}' on '{gameObjectPath}'.",
                                "Re-assign the correct shader or fix shader compilation errors. The original shader may be missing or broken."));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Checks all materials in the project for error/fallback shaders (project scope).
        /// </summary>
        private static void CheckShaderIssuesProject(List<Dictionary<string, object>> issues)
        {
            string[] materialGuids = AssetDatabase.FindAssets("t:Material");

            foreach (string materialGuid in materialGuids)
            {
                if (issues.Count >= MaxIssues)
                {
                    break;
                }

                string assetPath = AssetDatabase.GUIDToAssetPath(materialGuid);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);

                if (material == null || material.shader == null)
                {
                    continue;
                }

                string shaderName = material.shader.name;
                if (IsErrorShader(shaderName))
                {
                    issues.Add(CreateIssue("error", "shaders",
                        $"Material '{material.name}' at '{assetPath}' using error shader '{shaderName}'.",
                        "Re-assign the correct shader or fix shader compilation errors. The original shader may be missing or broken."));
                }
            }
        }

        /// <summary>
        /// Determines whether a shader name indicates an error/fallback shader.
        /// </summary>
        private static bool IsErrorShader(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName))
            {
                return false;
            }

            return shaderName.Contains("Error") ||
                   shaderName.Equals("Hidden/InternalErrorShader", StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region Check: Build Readiness

        /// <summary>
        /// Checks build settings, active platform, and script compilation status.
        /// </summary>
        private static void CheckBuildReadiness(List<Dictionary<string, object>> issues)
        {
            // Check scenes in build settings
            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
            if (buildScenes == null || buildScenes.Length == 0)
            {
                issues.Add(CreateIssue("error", "build",
                    "No scenes in Build Settings.",
                    "Add scenes via File > Build Settings > Add Open Scenes, or drag scenes into the build list."));
            }
            else
            {
                int enabledSceneCount = buildScenes.Count(scene => scene.enabled);
                if (enabledSceneCount == 0)
                {
                    issues.Add(CreateIssue("error", "build",
                        "All scenes in Build Settings are disabled.",
                        "Enable at least one scene in File > Build Settings."));
                }

                // Check for missing scene files
                foreach (EditorBuildSettingsScene buildScene in buildScenes)
                {
                    if (issues.Count >= MaxIssues)
                    {
                        break;
                    }

                    if (!buildScene.enabled)
                    {
                        continue;
                    }

                    string scenePath = buildScene.path;
                    if (string.IsNullOrEmpty(scenePath))
                    {
                        issues.Add(CreateIssue("warning", "build",
                            "Build Settings contains a scene with an empty path.",
                            "Remove invalid entries from the build scene list."));
                        continue;
                    }

                    if (!System.IO.File.Exists(scenePath))
                    {
                        issues.Add(CreateIssue("error", "build",
                            $"Build scene not found: '{scenePath}'.",
                            "Remove the missing scene from Build Settings or restore the scene file."));
                    }
                }
            }

            // Report active build target
            BuildTarget activeBuildTarget = EditorUserBuildSettings.activeBuildTarget;
            issues.Add(CreateIssue("info", "build",
                $"Active build target: {activeBuildTarget}.",
                "Change via File > Build Settings if this is not the intended platform."));

            // Check script compilation status
            if (EditorApplication.isCompiling)
            {
                issues.Add(CreateIssue("warning", "build",
                    "Scripts are currently compiling.",
                    "Wait for compilation to finish before building or entering Play mode."));
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Parses the optional checks filter into a set of check names to run.
        /// If null or empty, all checks are included.
        /// </summary>
        private static HashSet<string> ParseChecksFilter(List<object> checks)
        {
            if (checks == null || checks.Count == 0)
            {
                return new HashSet<string>(ValidCheckNames, StringComparer.OrdinalIgnoreCase);
            }

            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object checkValue in checks)
            {
                string checkName = checkValue?.ToString()?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(checkName))
                {
                    continue;
                }

                if (!ValidCheckNames.Contains(checkName))
                {
                    throw MCPException.InvalidParams(
                        $"Unknown check: '{checkName}'. Valid checks: {string.Join(", ", ValidCheckNames)}");
                }

                result.Add(checkName);
            }

            if (result.Count == 0)
            {
                return new HashSet<string>(ValidCheckNames, StringComparer.OrdinalIgnoreCase);
            }

            return result;
        }

        /// <summary>
        /// Creates a structured issue dictionary.
        /// </summary>
        private static Dictionary<string, object> CreateIssue(string severity, string category, string description, string suggestion)
        {
            return new Dictionary<string, object>
            {
                { "severity", severity },
                { "category", category },
                { "description", description },
                { "suggestion", suggestion }
            };
        }

        /// <summary>
        /// Gets the full hierarchy path of a GameObject.
        /// </summary>
        private static string GetGameObjectPath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return string.Empty;
            }

            try
            {
                var pathSegments = new Stack<string>();
                Transform currentTransform = gameObject.transform;
                while (currentTransform != null)
                {
                    pathSegments.Push(currentTransform.name);
                    currentTransform = currentTransform.parent;
                }
                return string.Join("/", pathSegments);
            }
            catch
            {
                return gameObject.name;
            }
        }

        /// <summary>
        /// Extracts the first line from a message string.
        /// </summary>
        private static string ExtractFirstLine(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            int newlineIndex = message.IndexOf('\n');
            return newlineIndex >= 0 ? message.Substring(0, newlineIndex) : message;
        }

        /// <summary>
        /// Resolves a type by its fully qualified name across all loaded assemblies.
        /// Returns null if the type cannot be found.
        /// </summary>
        private static Type ResolveType(string fullyQualifiedTypeName)
        {
            if (string.IsNullOrEmpty(fullyQualifiedTypeName))
            {
                return null;
            }

            // Try direct resolution first
            Type resolvedType = Type.GetType(fullyQualifiedTypeName);
            if (resolvedType != null)
            {
                return resolvedType;
            }

            // Search all loaded assemblies
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                resolvedType = assembly.GetType(fullyQualifiedTypeName);
                if (resolvedType != null)
                {
                    return resolvedType;
                }
            }

            return null;
        }

        #endregion
    }
}
