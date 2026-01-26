using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Utilities;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Tool for managing C# scripts: create, read, update, delete, and validate.
    /// </summary>
    public static class ManageScript
    {
        /// <summary>
        /// Valid script types for template generation.
        /// </summary>
        private static readonly string[] ValidScriptTypes = { "monobehaviour", "scriptableobject", "editor", "plain" };

        /// <summary>
        /// Manages C# scripts: create, read, update, delete, and validate.
        /// </summary>
        /// <param name="action">The action to perform: create, read, update, delete, validate</param>
        /// <param name="name">Script name without .cs extension</param>
        /// <param name="path">Directory path relative to Assets (default: "Scripts")</param>
        /// <param name="contents">Script contents for create/update</param>
        /// <param name="scriptType">Template type for create: MonoBehaviour, ScriptableObject, Editor, Plain</param>
        /// <param name="namespaceName">Namespace for the script</param>
        /// <returns>Result object indicating success or failure with appropriate data.</returns>
        [MCPTool("manage_script", "Manage C# scripts: create, read, update, delete, validate", Category = "Asset")]
        public static object Execute(
            [MCPParam("action", "Action: create, read, update, delete, validate", required: true)] string action,
            [MCPParam("name", "Script name without .cs extension", required: true)] string name,
            [MCPParam("path", "Directory path relative to Assets (default: Scripts)")] string path = "Scripts",
            [MCPParam("contents", "Script contents for create/update")] string contents = null,
            [MCPParam("script_type", "Template type: MonoBehaviour, ScriptableObject, Editor, Plain (default: MonoBehaviour)")] string scriptType = "MonoBehaviour",
            [MCPParam("namespace_name", "Namespace for the script")] string namespaceName = null)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                throw MCPException.InvalidParams("The 'action' parameter is required.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw MCPException.InvalidParams("The 'name' parameter is required.");
            }

            // Validate script name
            string validationError = ValidateScriptName(name);
            if (validationError != null)
            {
                throw MCPException.InvalidParams(validationError);
            }

            string normalizedAction = action.Trim().ToLowerInvariant();

            try
            {
                return normalizedAction switch
                {
                    "create" => HandleCreate(name, path, contents, scriptType, namespaceName),
                    "read" => HandleRead(name, path),
                    "update" => HandleUpdate(name, path, contents),
                    "delete" => HandleDelete(name, path),
                    "validate" => HandleValidate(name, path, contents),
                    _ => throw MCPException.InvalidParams($"Unknown action: '{action}'. Valid actions: create, read, update, delete, validate")
                };
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ManageScript] Error executing action '{action}': {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error executing action '{action}': {exception.Message}"
                };
            }
        }

        #region Action Handlers

        /// <summary>
        /// Creates a new C# script file with optional template.
        /// </summary>
        private static object HandleCreate(string name, string path, string contents, string scriptType, string namespaceName)
        {
            string scriptPath = BuildScriptPath(name, path);

            // Check if script already exists
            if (File.Exists(GetFullPath(scriptPath)))
            {
                return new
                {
                    success = false,
                    error = $"Script already exists at '{scriptPath}'."
                };
            }

            // Ensure parent directory exists
            string parentDirectory = Path.GetDirectoryName(scriptPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parentDirectory) && !AssetDatabase.IsValidFolder(parentDirectory))
            {
                if (!PathUtilities.EnsureFolderExists(parentDirectory, out string folderError))
                {
                    return new { success = false, error = folderError };
                }
            }

            // Determine script contents
            string scriptContents;
            string templateUsed = null;

            if (!string.IsNullOrWhiteSpace(contents))
            {
                scriptContents = contents;
            }
            else
            {
                // Generate from template
                string normalizedScriptType = scriptType?.Trim().ToLowerInvariant() ?? "monobehaviour";
                scriptContents = GenerateTemplate(name, normalizedScriptType, namespaceName);
                templateUsed = normalizedScriptType;
            }

            try
            {
                // Write the script file
                string fullPath = GetFullPath(scriptPath);
                File.WriteAllText(fullPath, scriptContents, Encoding.UTF8);

                // Refresh AssetDatabase to import the new script
                AssetDatabase.Refresh();

                return new
                {
                    success = true,
                    message = $"Script '{name}.cs' created successfully.",
                    path = scriptPath,
                    fullPath,
                    templateUsed,
                    lineCount = scriptContents.Split('\n').Length
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error creating script: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Reads the contents of a C# script file.
        /// </summary>
        private static object HandleRead(string name, string path)
        {
            string scriptPath = BuildScriptPath(name, path);
            string fullPath = GetFullPath(scriptPath);

            if (!File.Exists(fullPath))
            {
                return new
                {
                    success = false,
                    error = $"Script not found at '{scriptPath}'."
                };
            }

            try
            {
                string contents = File.ReadAllText(fullPath, Encoding.UTF8);
                FileInfo fileInfo = new FileInfo(fullPath);

                return new
                {
                    success = true,
                    path = scriptPath,
                    fullPath,
                    contents,
                    lineCount = contents.Split('\n').Length,
                    fileSize = fileInfo.Length,
                    lastModified = fileInfo.LastWriteTimeUtc.ToString("o")
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error reading script: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Updates the contents of a C# script file (full replace).
        /// </summary>
        private static object HandleUpdate(string name, string path, string contents)
        {
            if (string.IsNullOrEmpty(contents))
            {
                throw MCPException.InvalidParams("The 'contents' parameter is required for update action.");
            }

            string scriptPath = BuildScriptPath(name, path);
            string fullPath = GetFullPath(scriptPath);

            if (!File.Exists(fullPath))
            {
                return new
                {
                    success = false,
                    error = $"Script not found at '{scriptPath}'."
                };
            }

            try
            {
                // Read previous contents for comparison
                string previousContents = File.ReadAllText(fullPath, Encoding.UTF8);
                int previousLineCount = previousContents.Split('\n').Length;

                // Write new contents
                File.WriteAllText(fullPath, contents, Encoding.UTF8);

                // Refresh AssetDatabase to reimport the script
                AssetDatabase.ImportAsset(scriptPath, ImportAssetOptions.ForceUpdate);

                int newLineCount = contents.Split('\n').Length;

                return new
                {
                    success = true,
                    message = $"Script '{name}.cs' updated successfully.",
                    path = scriptPath,
                    fullPath,
                    previousLineCount,
                    newLineCount,
                    linesChanged = newLineCount - previousLineCount
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error updating script: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Deletes a C# script file.
        /// </summary>
        private static object HandleDelete(string name, string path)
        {
            string scriptPath = BuildScriptPath(name, path);
            string fullPath = GetFullPath(scriptPath);

            if (!File.Exists(fullPath))
            {
                return new
                {
                    success = false,
                    error = $"Script not found at '{scriptPath}'."
                };
            }

            try
            {
                string guid = AssetDatabase.AssetPathToGUID(scriptPath);

                // Use AssetDatabase to delete (handles .meta file too)
                bool deleted = AssetDatabase.DeleteAsset(scriptPath);

                if (deleted)
                {
                    return new
                    {
                        success = true,
                        message = $"Script '{name}.cs' deleted successfully.",
                        deletedPath = scriptPath,
                        deletedGuid = guid
                    };
                }
                else
                {
                    return new
                    {
                        success = false,
                        error = $"Failed to delete script at '{scriptPath}'."
                    };
                }
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error deleting script: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Validates script syntax (basic structural validation).
        /// </summary>
        private static object HandleValidate(string name, string path, string contents)
        {
            string scriptContents;
            string scriptPath = null;

            if (!string.IsNullOrWhiteSpace(contents))
            {
                // Validate provided contents
                scriptContents = contents;
            }
            else
            {
                // Read from file
                scriptPath = BuildScriptPath(name, path);
                string fullPath = GetFullPath(scriptPath);

                if (!File.Exists(fullPath))
                {
                    return new
                    {
                        success = false,
                        error = $"Script not found at '{scriptPath}'. Provide 'contents' parameter to validate inline content."
                    };
                }

                scriptContents = File.ReadAllText(fullPath, Encoding.UTF8);
            }

            // Perform validation
            var validationResult = ValidateScriptSyntax(scriptContents, name);

            return new
            {
                success = true,
                path = scriptPath,
                isValid = validationResult.IsValid,
                errors = validationResult.Errors.Length > 0 ? validationResult.Errors : null,
                warnings = validationResult.Warnings.Length > 0 ? validationResult.Warnings : null,
                info = new
                {
                    hasClassDefinition = validationResult.HasClassDefinition,
                    className = validationResult.ClassName,
                    hasNamespace = validationResult.HasNamespace,
                    namespaceName = validationResult.NamespaceName,
                    bracesBalanced = validationResult.BracesBalanced,
                    lineCount = scriptContents.Split('\n').Length
                }
            };
        }

        #endregion

        #region Template Generation

        /// <summary>
        /// Generates a script template based on the script type.
        /// </summary>
        private static string GenerateTemplate(string name, string scriptType, string namespaceName)
        {
            bool useNamespace = !string.IsNullOrWhiteSpace(namespaceName);
            string indent = useNamespace ? "    " : "";

            return scriptType switch
            {
                "monobehaviour" => GenerateMonoBehaviourTemplate(name, namespaceName, useNamespace, indent),
                "scriptableobject" => GenerateScriptableObjectTemplate(name, namespaceName, useNamespace, indent),
                "editor" => GenerateEditorTemplate(name, namespaceName, useNamespace, indent),
                "plain" => GeneratePlainClassTemplate(name, namespaceName, useNamespace, indent),
                _ => GenerateMonoBehaviourTemplate(name, namespaceName, useNamespace, indent) // Default to MonoBehaviour
            };
        }

        /// <summary>
        /// Generates a MonoBehaviour script template.
        /// </summary>
        private static string GenerateMonoBehaviourTemplate(string name, string namespaceName, bool useNamespace, string indent)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("using UnityEngine;");
            stringBuilder.AppendLine();

            if (useNamespace)
            {
                stringBuilder.AppendLine($"namespace {namespaceName}");
                stringBuilder.AppendLine("{");
            }

            stringBuilder.AppendLine($"{indent}public class {name} : MonoBehaviour");
            stringBuilder.AppendLine($"{indent}{{");
            stringBuilder.AppendLine($"{indent}    void Start()");
            stringBuilder.AppendLine($"{indent}    {{");
            stringBuilder.AppendLine($"{indent}    }}");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine($"{indent}    void Update()");
            stringBuilder.AppendLine($"{indent}    {{");
            stringBuilder.AppendLine($"{indent}    }}");
            stringBuilder.AppendLine($"{indent}}}");

            if (useNamespace)
            {
                stringBuilder.AppendLine("}");
            }

            return stringBuilder.ToString();
        }

        /// <summary>
        /// Generates a ScriptableObject script template.
        /// </summary>
        private static string GenerateScriptableObjectTemplate(string name, string namespaceName, bool useNamespace, string indent)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("using UnityEngine;");
            stringBuilder.AppendLine();

            if (useNamespace)
            {
                stringBuilder.AppendLine($"namespace {namespaceName}");
                stringBuilder.AppendLine("{");
            }

            stringBuilder.AppendLine($"{indent}[CreateAssetMenu(fileName = \"{name}\", menuName = \"ScriptableObjects/{name}\")]");
            stringBuilder.AppendLine($"{indent}public class {name} : ScriptableObject");
            stringBuilder.AppendLine($"{indent}{{");
            stringBuilder.AppendLine($"{indent}}}");

            if (useNamespace)
            {
                stringBuilder.AppendLine("}");
            }

            return stringBuilder.ToString();
        }

        /// <summary>
        /// Generates an Editor script template.
        /// </summary>
        private static string GenerateEditorTemplate(string name, string namespaceName, bool useNamespace, string indent)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("using UnityEngine;");
            stringBuilder.AppendLine("using UnityEditor;");
            stringBuilder.AppendLine();

            if (useNamespace)
            {
                stringBuilder.AppendLine($"namespace {namespaceName}");
                stringBuilder.AppendLine("{");
            }

            stringBuilder.AppendLine($"{indent}public class {name} : EditorWindow");
            stringBuilder.AppendLine($"{indent}{{");
            stringBuilder.AppendLine($"{indent}    [MenuItem(\"Window/{name}\")]");
            stringBuilder.AppendLine($"{indent}    public static void ShowWindow()");
            stringBuilder.AppendLine($"{indent}    {{");
            stringBuilder.AppendLine($"{indent}        GetWindow<{name}>(\"{name}\");");
            stringBuilder.AppendLine($"{indent}    }}");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine($"{indent}    void OnGUI()");
            stringBuilder.AppendLine($"{indent}    {{");
            stringBuilder.AppendLine($"{indent}    }}");
            stringBuilder.AppendLine($"{indent}}}");

            if (useNamespace)
            {
                stringBuilder.AppendLine("}");
            }

            return stringBuilder.ToString();
        }

        /// <summary>
        /// Generates a plain C# class template.
        /// </summary>
        private static string GeneratePlainClassTemplate(string name, string namespaceName, bool useNamespace, string indent)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("using System;");
            stringBuilder.AppendLine();

            if (useNamespace)
            {
                stringBuilder.AppendLine($"namespace {namespaceName}");
                stringBuilder.AppendLine("{");
            }

            stringBuilder.AppendLine($"{indent}public class {name}");
            stringBuilder.AppendLine($"{indent}{{");
            stringBuilder.AppendLine($"{indent}}}");

            if (useNamespace)
            {
                stringBuilder.AppendLine("}");
            }

            return stringBuilder.ToString();
        }

        #endregion

        #region Validation

        /// <summary>
        /// Validates a script name (must be a valid C# identifier).
        /// </summary>
        private static string ValidateScriptName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Script name cannot be empty.";
            }

            // Check for invalid characters
            if (!Regex.IsMatch(name, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
            {
                return $"Invalid script name '{name}'. Must start with a letter or underscore, and contain only letters, numbers, and underscores.";
            }

            // Check for C# keywords
            string[] reservedKeywords = {
                "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
                "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
                "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
                "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
                "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
                "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
                "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
                "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
                "void", "volatile", "while"
            };

            if (Array.IndexOf(reservedKeywords, name.ToLowerInvariant()) >= 0)
            {
                return $"Script name '{name}' is a C# reserved keyword.";
            }

            return null; // Valid
        }

        /// <summary>
        /// Validates script syntax (basic structural validation).
        /// </summary>
        private static ValidationResult ValidateScriptSyntax(string contents, string expectedClassName)
        {
            var result = new ValidationResult();

            // Check for balanced braces
            int braceCount = 0;
            foreach (char character in contents)
            {
                if (character == '{') braceCount++;
                if (character == '}') braceCount--;
            }

            result.BracesBalanced = braceCount == 0;
            if (!result.BracesBalanced)
            {
                result.AddError($"Unbalanced braces: {(braceCount > 0 ? $"{braceCount} unclosed '{{'" : $"{-braceCount} extra '}}'")})");
            }

            // Check for class definition
            Match classMatch = Regex.Match(contents, @"\bclass\s+(\w+)");
            if (classMatch.Success)
            {
                result.HasClassDefinition = true;
                result.ClassName = classMatch.Groups[1].Value;

                // Check if class name matches expected name
                if (!string.IsNullOrEmpty(expectedClassName) && result.ClassName != expectedClassName)
                {
                    result.AddWarning($"Class name '{result.ClassName}' does not match script name '{expectedClassName}'.");
                }
            }
            else
            {
                result.HasClassDefinition = false;
                result.AddError("No class definition found.");
            }

            // Check for namespace
            Match namespaceMatch = Regex.Match(contents, @"\bnamespace\s+([\w.]+)");
            if (namespaceMatch.Success)
            {
                result.HasNamespace = true;
                result.NamespaceName = namespaceMatch.Groups[1].Value;
            }

            // Check for common issues
            if (contents.Contains("public class") && !contents.Contains("using"))
            {
                result.AddWarning("No 'using' directives found. Consider adding necessary using statements.");
            }

            // Check for balanced parentheses
            int parenCount = 0;
            foreach (char character in contents)
            {
                if (character == '(') parenCount++;
                if (character == ')') parenCount--;
            }
            if (parenCount != 0)
            {
                result.AddError($"Unbalanced parentheses: {(parenCount > 0 ? $"{parenCount} unclosed '('" : $"{-parenCount} extra ')'")}");
            }

            // Check for balanced brackets
            int bracketCount = 0;
            foreach (char character in contents)
            {
                if (character == '[') bracketCount++;
                if (character == ']') bracketCount--;
            }
            if (bracketCount != 0)
            {
                result.AddError($"Unbalanced brackets: {(bracketCount > 0 ? $"{bracketCount} unclosed '['" : $"{-bracketCount} extra ']'")}");
            }

            // Set overall validity
            result.IsValid = result.Errors.Length == 0;

            return result;
        }

        /// <summary>
        /// Validation result data structure.
        /// </summary>
        private class ValidationResult
        {
            public bool IsValid { get; set; } = true;
            public bool BracesBalanced { get; set; } = true;
            public bool HasClassDefinition { get; set; }
            public string ClassName { get; set; }
            public bool HasNamespace { get; set; }
            public string NamespaceName { get; set; }

            private readonly System.Collections.Generic.List<string> _errors = new System.Collections.Generic.List<string>();
            private readonly System.Collections.Generic.List<string> _warnings = new System.Collections.Generic.List<string>();

            public string[] Errors => _errors.ToArray();
            public string[] Warnings => _warnings.ToArray();

            public void AddError(string error)
            {
                _errors.Add(error);
            }

            public void AddWarning(string warning)
            {
                _warnings.Add(warning);
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Builds the full asset path for a script.
        /// </summary>
        private static string BuildScriptPath(string name, string path)
        {
            // Normalize the path
            string normalizedPath = PathUtilities.NormalizePath(path);

            // Ensure the name has .cs extension
            string fileName = name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.cs";

            // Build full path
            return $"{normalizedPath}/{fileName}";
        }

        /// <summary>
        /// Gets the full file system path for an asset path.
        /// </summary>
        private static string GetFullPath(string assetPath)
        {
            return Path.GetFullPath(assetPath);
        }

        #endregion
    }
}
