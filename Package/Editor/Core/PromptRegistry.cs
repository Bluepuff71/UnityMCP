using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnityMCP.Editor.Core
{
    /// <summary>
    /// Registry for discovering and invoking MCP prompts marked with [MCPPrompt] attribute.
    /// </summary>
    public static class PromptRegistry
    {
        private static Dictionary<string, PromptInfo> _prompts = new Dictionary<string, PromptInfo>();
        private static bool _isInitialized = false;
        private static readonly object _lock = new object();

        /// <summary>
        /// Gets the number of registered prompts.
        /// </summary>
        public static int Count { get { lock (_lock) { return _prompts.Count; } } }

        /// <summary>
        /// Auto-discover prompts when the editor loads.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void DiscoverPrompts()
        {
            RefreshPrompts();
        }

        /// <summary>
        /// Manually refresh the prompt registry. Useful for testing or after loading new assemblies.
        /// </summary>
        public static void RefreshPrompts()
        {
            lock (_lock)
            {
                _prompts.Clear();
                _isInitialized = false;

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
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
                        DiscoverPromptsInAssembly(assembly);
                    }
                    catch (ReflectionTypeLoadException reflectionException)
                    {
                        Debug.LogWarning($"[PromptRegistry] Failed to load types from assembly {assembly.GetName().Name}: {reflectionException.Message}");
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"[PromptRegistry] Error scanning assembly {assembly.GetName().Name}: {exception.Message}");
                    }
                }

                _isInitialized = true;
                if (MCPProxy.VerboseLogging) Debug.Log($"[PromptRegistry] Discovered {_prompts.Count} MCP prompts");
            }
        }

        private static void DiscoverPromptsInAssembly(Assembly assembly)
        {
            foreach (var type in assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public))
                {
                    try
                    {
                        var promptAttribute = method.GetCustomAttribute<MCPPromptAttribute>();
                        if (promptAttribute != null)
                        {
                            if (_prompts.ContainsKey(promptAttribute.Name))
                            {
                                Debug.LogWarning($"[PromptRegistry] Duplicate prompt name '{promptAttribute.Name}' found in {type.FullName}.{method.Name}. Skipping.");
                                continue;
                            }

                            var promptInfo = new PromptInfo(promptAttribute, method);
                            _prompts[promptAttribute.Name] = promptInfo;
                        }
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"[PromptRegistry] Error processing method {type.FullName}.{method.Name}: {exception.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Gets all prompt definitions for the MCP prompts/list response.
        /// </summary>
        public static IEnumerable<PromptDefinition> GetDefinitions()
        {
            EnsureInitialized();
            List<PromptInfo> snapshot;
            lock (_lock)
            {
                snapshot = _prompts.Values.ToList();
            }
            return snapshot.Select(promptInfo => promptInfo.ToDefinition());
        }

        /// <summary>
        /// Checks if a prompt with the given name exists.
        /// </summary>
        public static bool HasPrompt(string name)
        {
            EnsureInitialized();
            lock (_lock)
            {
                return _prompts.ContainsKey(name);
            }
        }

        /// <summary>
        /// Invokes a prompt by name with the given arguments.
        /// </summary>
        /// <param name="name">The name of the prompt to invoke.</param>
        /// <param name="arguments">A dictionary of argument names to their string values.</param>
        /// <returns>The PromptResult containing messages.</returns>
        /// <exception cref="MCPException">Thrown if the prompt is not found or invocation fails.</exception>
        public static PromptResult Invoke(string name, Dictionary<string, string> arguments)
        {
            EnsureInitialized();

            PromptInfo promptInfo;
            lock (_lock)
            {
                if (!_prompts.TryGetValue(name, out promptInfo))
                {
                    throw new MCPException($"Unknown prompt: {name}", MCPErrorCodes.MethodNotFound);
                }
            }

            return promptInfo.Invoke(arguments);
        }

        private static void EnsureInitialized()
        {
            lock (_lock)
            {
                if (!_isInitialized)
                {
                    RefreshPrompts();
                }
            }
        }
    }

    /// <summary>
    /// Internal class holding metadata and invocation logic for a discovered prompt.
    /// </summary>
    internal class PromptInfo
    {
        private readonly MCPPromptAttribute _attribute;
        private readonly MethodInfo _method;
        private readonly ParameterInfo[] _parameters;
        private readonly List<PromptArgumentMetadata> _argumentMetadata;

        public string Name => _attribute.Name;
        public string Description => _attribute.Description;

        public PromptInfo(MCPPromptAttribute attribute, MethodInfo method)
        {
            _attribute = attribute;
            _method = method;
            _parameters = method.GetParameters();
            _argumentMetadata = new List<PromptArgumentMetadata>();

            BuildArgumentMetadata();
        }

        private void BuildArgumentMetadata()
        {
            foreach (var parameter in _parameters)
            {
                var mcpParamAttribute = parameter.GetCustomAttribute<MCPParamAttribute>();

                string parameterName = mcpParamAttribute?.Name ?? parameter.Name;
                string parameterDescription = mcpParamAttribute?.Description ?? "";
                bool isRequired = mcpParamAttribute?.Required ?? !parameter.HasDefaultValue;

                _argumentMetadata.Add(new PromptArgumentMetadata
                {
                    Name = parameterName,
                    Description = parameterDescription,
                    Required = isRequired,
                    ParameterInfo = parameter
                });
            }
        }

        /// <summary>
        /// Creates a PromptDefinition for the MCP prompts/list response.
        /// </summary>
        public PromptDefinition ToDefinition()
        {
            var definition = new PromptDefinition
            {
                name = _attribute.Name,
                description = _attribute.Description,
                arguments = new List<PromptArgument>()
            };

            foreach (var metadata in _argumentMetadata)
            {
                definition.arguments.Add(new PromptArgument
                {
                    name = metadata.Name,
                    description = metadata.Description,
                    required = metadata.Required
                });
            }

            return definition;
        }

        /// <summary>
        /// Invokes the prompt with the given arguments.
        /// </summary>
        public PromptResult Invoke(Dictionary<string, string> arguments)
        {
            arguments = arguments ?? new Dictionary<string, string>();

            var invokeArguments = new object[_parameters.Length];

            for (int parameterIndex = 0; parameterIndex < _parameters.Length; parameterIndex++)
            {
                var parameter = _parameters[parameterIndex];
                var metadata = _argumentMetadata.FirstOrDefault(m => m.ParameterInfo == parameter);

                if (metadata == null)
                {
                    throw new MCPException($"Internal error: Argument metadata not found for {parameter.Name}", MCPErrorCodes.InternalError);
                }

                if (arguments.TryGetValue(metadata.Name, out var argumentValue))
                {
                    invokeArguments[parameterIndex] = argumentValue;
                }
                else if (metadata.Required)
                {
                    throw new MCPException($"Missing required argument: {metadata.Name}", MCPErrorCodes.InvalidParams);
                }
                else if (parameter.HasDefaultValue)
                {
                    invokeArguments[parameterIndex] = parameter.DefaultValue;
                }
                else
                {
                    invokeArguments[parameterIndex] = null;
                }
            }

            try
            {
                var result = _method.Invoke(null, invokeArguments);
                if (result is PromptResult promptResult)
                {
                    return promptResult;
                }
                throw new MCPException($"Prompt method must return PromptResult", MCPErrorCodes.InternalError);
            }
            catch (TargetInvocationException invocationException)
            {
                var innerException = invocationException.InnerException ?? invocationException;
                if (innerException is MCPException)
                {
                    throw innerException;
                }
                throw new MCPException($"Prompt execution failed: {innerException.Message}", innerException, MCPErrorCodes.InternalError);
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new MCPException($"Prompt invocation failed: {exception.Message}", exception, MCPErrorCodes.InternalError);
            }
        }
    }

    /// <summary>
    /// Internal metadata for a prompt argument.
    /// </summary>
    internal class PromptArgumentMetadata
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public bool Required { get; set; }
        public ParameterInfo ParameterInfo { get; set; }
    }
}
