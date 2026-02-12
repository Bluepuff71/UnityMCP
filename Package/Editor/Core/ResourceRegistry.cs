using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityMCP.Editor.Utilities;

namespace UnityMCP.Editor.Core
{
    /// <summary>
    /// Registry for discovering and invoking MCP resources marked with [MCPResource] attribute.
    /// Supports both static URIs and parameterized URI templates (e.g., "scene://gameobject/{id}").
    /// </summary>
    public static class ResourceRegistry
    {
        private static Dictionary<string, ResourceInfo> _resources = new Dictionary<string, ResourceInfo>();
        private static List<ResourceInfo> _parameterizedResources = new List<ResourceInfo>();
        private static bool _isInitialized = false;

        /// <summary>
        /// Gets the number of registered resources.
        /// </summary>
        public static int Count => _resources.Count;

        /// <summary>
        /// Auto-discover resources when the editor loads.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void DiscoverResources()
        {
            RefreshResources();
        }

        /// <summary>
        /// Manually refresh the resource registry. Useful for testing or after loading new assemblies.
        /// </summary>
        public static void RefreshResources()
        {
            _resources.Clear();
            _parameterizedResources.Clear();
            _isInitialized = false;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Skip system and Unity assemblies for performance
                string assemblyName = assembly.FullName;
                if (assemblyName.StartsWith("System", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("Unity.", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("UnityEngine", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("UnityEditor", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("mscorlib", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("netstandard", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("Microsoft.", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("Mono.", StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    DiscoverResourcesInAssembly(assembly);
                }
                catch (ReflectionTypeLoadException reflectionException)
                {
                    Debug.LogWarning($"[ResourceRegistry] Failed to load types from assembly {assembly.GetName().Name}: {reflectionException.Message}");
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[ResourceRegistry] Error scanning assembly {assembly.GetName().Name}: {exception.Message}");
                }
            }

            _isInitialized = true;
            int totalResources = _resources.Count + _parameterizedResources.Count;
            if (MCPProxy.VerboseLogging) Debug.Log($"[ResourceRegistry] Discovered {totalResources} MCP resources ({_resources.Count} static, {_parameterizedResources.Count} parameterized)");
        }

        private static void DiscoverResourcesInAssembly(Assembly assembly)
        {
            foreach (var type in assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public))
                {
                    try
                    {
                        var resourceAttribute = method.GetCustomAttribute<MCPResourceAttribute>();
                        if (resourceAttribute != null)
                        {
                            var resourceInfo = new ResourceInfo(resourceAttribute, method);

                            // Check if this is a parameterized URI template
                            if (resourceInfo.IsParameterized)
                            {
                                // Validate that method parameters match URI template parameters
                                var uriParams = resourceInfo.GetTemplateParameterNames();
                                var methodParams = method.GetParameters();

                                if (uriParams.Count != methodParams.Length)
                                {
                                    Debug.LogWarning($"[ResourceRegistry] Resource method {type.FullName}.{method.Name} has mismatched parameter count. URI template has {uriParams.Count} parameters, method has {methodParams.Length}. Skipping.");
                                    continue;
                                }

                                // Check for duplicate parameterized resources
                                bool isDuplicate = _parameterizedResources.Any(existingResource => existingResource.Uri == resourceAttribute.Uri);
                                if (isDuplicate)
                                {
                                    Debug.LogWarning($"[ResourceRegistry] Duplicate parameterized resource URI '{resourceAttribute.Uri}' found in {type.FullName}.{method.Name}. Skipping.");
                                    continue;
                                }

                                _parameterizedResources.Add(resourceInfo);
                            }
                            else
                            {
                                // Static resource - validate no required parameters
                                var parameters = method.GetParameters();
                                if (parameters.Length > 0 && parameters.Any(p => !p.HasDefaultValue))
                                {
                                    Debug.LogWarning($"[ResourceRegistry] Static resource method {type.FullName}.{method.Name} has required parameters. Static resource methods should have no required parameters. Skipping.");
                                    continue;
                                }

                                if (_resources.ContainsKey(resourceAttribute.Uri))
                                {
                                    Debug.LogWarning($"[ResourceRegistry] Duplicate resource URI '{resourceAttribute.Uri}' found in {type.FullName}.{method.Name}. Skipping.");
                                    continue;
                                }

                                _resources[resourceAttribute.Uri] = resourceInfo;
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"[ResourceRegistry] Error processing method {type.FullName}.{method.Name}: {exception.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Gets all resource definitions for the MCP resources/list response.
        /// Includes both static resources and parameterized URI templates.
        /// </summary>
        public static IEnumerable<ResourceDefinition> GetDefinitions()
        {
            EnsureInitialized();
            var staticDefinitions = _resources.Values.Select(resourceInfo => resourceInfo.ToDefinition());
            var parameterizedDefinitions = _parameterizedResources.Select(resourceInfo => resourceInfo.ToDefinition());
            return staticDefinitions.Concat(parameterizedDefinitions);
        }

        /// <summary>
        /// Gets a specific resource definition by URI or URI template.
        /// For parameterized resources, pass the template (e.g., "scene://gameobject/{id}").
        /// </summary>
        public static ResourceDefinition GetDefinition(string uri)
        {
            EnsureInitialized();

            // Check static resources first
            if (_resources.TryGetValue(uri, out var resourceInfo))
            {
                return resourceInfo.ToDefinition();
            }

            // Check parameterized resources by template
            var parameterizedResource = _parameterizedResources.FirstOrDefault(r => r.Uri == uri);
            if (parameterizedResource != null)
            {
                return parameterizedResource.ToDefinition();
            }

            return null;
        }

        /// <summary>
        /// Checks if a resource with the given URI exists.
        /// Supports both static URIs and URIs that match parameterized templates.
        /// </summary>
        public static bool HasResource(string uri)
        {
            EnsureInitialized();

            // Check static resources first
            if (_resources.ContainsKey(uri))
            {
                return true;
            }

            // Check if URI matches any parameterized resource template
            return _parameterizedResources.Any(resource => resource.MatchesUri(uri));
        }

        /// <summary>
        /// Invokes a resource by URI and returns its content.
        /// Supports both static URIs and parameterized URIs (e.g., "scene://gameobject/12345").
        /// </summary>
        /// <param name="uri">The URI of the resource to invoke.</param>
        /// <returns>The ResourceContent result.</returns>
        /// <exception cref="MCPException">Thrown if the resource is not found or invocation fails.</exception>
        public static ResourceContent Invoke(string uri)
        {
            EnsureInitialized();

            // Check static resources first
            if (_resources.TryGetValue(uri, out var resourceInfo))
            {
                return resourceInfo.Invoke(uri);
            }

            // Check parameterized resources
            foreach (var parameterizedResource in _parameterizedResources)
            {
                if (parameterizedResource.MatchesUri(uri))
                {
                    return parameterizedResource.Invoke(uri);
                }
            }

            throw new MCPException($"Unknown resource: {uri}", MCPErrorCodes.MethodNotFound);
        }

        private static void EnsureInitialized()
        {
            if (!_isInitialized)
            {
                RefreshResources();
            }
        }
    }

    /// <summary>
    /// Internal class holding metadata and invocation logic for a discovered resource.
    /// Supports both static URIs and parameterized URI templates.
    /// </summary>
    internal class ResourceInfo
    {
        private readonly MCPResourceAttribute _attribute;
        private readonly MethodInfo _method;
        private readonly ParameterInfo[] _parameters;
        private readonly Regex _uriPattern;
        private readonly List<string> _templateParameterNames;
        private readonly bool _isParameterized;

        // Regex to find template parameters like {id} or {type}
        private static readonly Regex TemplateParameterRegex = new Regex(@"\{(\w+)\}", RegexOptions.Compiled);

        public string Uri => _attribute.Uri;
        public string Description => _attribute.Description;
        public bool IsParameterized => _isParameterized;

        public ResourceInfo(MCPResourceAttribute attribute, MethodInfo method)
        {
            _attribute = attribute;
            _method = method;
            _parameters = method.GetParameters();
            _templateParameterNames = new List<string>();

            // Check if URI contains template parameters
            var matches = TemplateParameterRegex.Matches(attribute.Uri);
            _isParameterized = matches.Count > 0;

            if (_isParameterized)
            {
                // Extract parameter names from template
                foreach (Match match in matches)
                {
                    _templateParameterNames.Add(match.Groups[1].Value);
                }

                // Build regex pattern to match actual URIs
                // Escape the URI pattern and replace {param} with named capture groups
                string pattern = "^" + Regex.Escape(attribute.Uri) + "$";
                foreach (var parameterName in _templateParameterNames)
                {
                    // Replace escaped \{paramName\} with a capturing group
                    pattern = pattern.Replace($"\\{{{parameterName}\\}}", $"(?<{parameterName}>[^/]+)");
                }
                _uriPattern = new Regex(pattern, RegexOptions.Compiled);
            }
        }

        /// <summary>
        /// Gets the list of parameter names defined in the URI template.
        /// </summary>
        public List<string> GetTemplateParameterNames() => _templateParameterNames;

        /// <summary>
        /// Checks if the given URI matches this resource's URI pattern.
        /// </summary>
        public bool MatchesUri(string uri)
        {
            if (!_isParameterized)
            {
                return uri == _attribute.Uri;
            }

            return _uriPattern.IsMatch(uri);
        }

        /// <summary>
        /// Extracts parameter values from a URI that matches this resource's template.
        /// </summary>
        public Dictionary<string, string> ExtractParameters(string uri)
        {
            var parameters = new Dictionary<string, string>();

            if (!_isParameterized)
            {
                return parameters;
            }

            var match = _uriPattern.Match(uri);
            if (match.Success)
            {
                foreach (var parameterName in _templateParameterNames)
                {
                    var group = match.Groups[parameterName];
                    if (group.Success)
                    {
                        parameters[parameterName] = group.Value;
                    }
                }
            }

            return parameters;
        }

        /// <summary>
        /// Creates a ResourceDefinition for the MCP resources/list response.
        /// </summary>
        public ResourceDefinition ToDefinition()
        {
            // Extract a name from the URI (last segment after ://)
            string name = ExtractNameFromUri(_attribute.Uri);

            return new ResourceDefinition(
                _attribute.Uri,
                name,
                _attribute.Description,
                "application/json"
            );
        }

        private static string ExtractNameFromUri(string uri)
        {
            // For URIs like "editor://active_tool", extract "active_tool"
            // For URIs like "scene://gameobject/{id}", extract "gameobject"
            int schemeEnd = uri.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd >= 0)
            {
                string path = uri.Substring(schemeEnd + 3);
                // Remove any template parameters
                int templateStart = path.IndexOf('{');
                if (templateStart > 0)
                {
                    path = path.Substring(0, templateStart).TrimEnd('/');
                }
                return path;
            }
            return uri;
        }

        /// <summary>
        /// Invokes the resource and returns its content.
        /// For parameterized resources, extracts parameters from the URI and passes them to the method.
        /// </summary>
        public ResourceContent Invoke(string uri)
        {
            try
            {
                object result;

                if (_isParameterized)
                {
                    // Extract parameters from URI and invoke with them
                    var extractedParameters = ExtractParameters(uri);
                    var invokeArguments = new object[_parameters.Length];

                    for (int parameterIndex = 0; parameterIndex < _parameters.Length; parameterIndex++)
                    {
                        var parameter = _parameters[parameterIndex];

                        // Try to get the parameter value by MCPParam name first, then by parameter name
                        string parameterName = parameter.Name;
                        var mcpParamAttribute = parameter.GetCustomAttribute<MCPParamAttribute>();
                        if (mcpParamAttribute != null)
                        {
                            parameterName = mcpParamAttribute.Name;
                        }

                        if (extractedParameters.TryGetValue(parameterName, out var stringValue))
                        {
                            invokeArguments[parameterIndex] = ConvertParameter(stringValue, parameter.ParameterType);
                        }
                        else if (parameter.HasDefaultValue)
                        {
                            invokeArguments[parameterIndex] = parameter.DefaultValue;
                        }
                        else
                        {
                            throw new MCPException($"Missing required parameter: {parameterName}", MCPErrorCodes.InvalidParams);
                        }
                    }

                    result = _method.Invoke(null, invokeArguments);
                }
                else
                {
                    // Static resource - invoke with no arguments
                    result = _method.Invoke(null, null);
                }

                if (result == null)
                {
                    return ResourceContent.Text(uri, "null", "application/json");
                }

                // If the result is already a ResourceContent, return it directly
                if (result is ResourceContent resourceContent)
                {
                    return resourceContent;
                }

                // Convert the result to JSON
                string jsonResult = JsonConvert.SerializeObject(result, JsonUtilities.DefaultSettings);

                return new ResourceContent
                {
                    uri = uri,
                    mimeType = "application/json",
                    text = jsonResult
                };
            }
            catch (TargetInvocationException invocationException)
            {
                // Unwrap the inner exception for cleaner error messages
                var innerException = invocationException.InnerException ?? invocationException;

                if (innerException is MCPException)
                {
                    throw innerException;
                }

                throw new MCPException($"Resource invocation failed: {innerException.Message}", innerException, MCPErrorCodes.InternalError);
            }
            catch (Exception exception)
            {
                throw new MCPException($"Resource invocation failed: {exception.Message}", exception, MCPErrorCodes.InternalError);
            }
        }

        /// <summary>
        /// Converts a string parameter value to the target type.
        /// </summary>
        private static object ConvertParameter(string value, Type targetType)
        {
            if (targetType == typeof(string))
            {
                return value;
            }

            if (targetType == typeof(int))
            {
                return int.Parse(value);
            }

            if (targetType == typeof(long))
            {
                return long.Parse(value);
            }

            if (targetType == typeof(bool))
            {
                return bool.Parse(value);
            }

            if (targetType == typeof(float))
            {
                return float.Parse(value);
            }

            if (targetType == typeof(double))
            {
                return double.Parse(value);
            }

            if (targetType.IsEnum)
            {
                return Enum.Parse(targetType, value, ignoreCase: true);
            }

            // Try generic conversion
            return Convert.ChangeType(value, targetType);
        }
    }
}
