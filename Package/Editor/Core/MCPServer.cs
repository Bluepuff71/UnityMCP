using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityMCP.Editor.Core
{
    /// <summary>
    /// Handles MCP (Model Context Protocol) JSON-RPC requests.
    /// Provides tool discovery and invocation for AI assistants.
    /// HTTP transport is handled by the native proxy; this class only handles request routing and response building.
    /// </summary>
    public class MCPServer
    {
        private static MCPServer _instance;
        private static readonly object InstanceLock = new object();

        private int _port = 8080;
        private const string ServerName = "UnityMCP";
        internal const string ServerVersion = "2.3.0";
        private const int MainThreadTimeoutSeconds = 30;

        private const string ServerInstructions =
@"UnityMCP controls the Unity Editor. Follow these practices:

SAFETY: Save a checkpoint (manage_checkpoint action='save') before destructive operations like deleting objects, modifying materials, or editing scripts. Use manage_checkpoint action='restore' to roll back if something goes wrong.

WORKFLOW: find -> inspect -> modify -> verify. Use find_gameobject or get_scene_hierarchy to locate objects, manage_component action='inspect' to read state, then modify, then verify with vision_capture or another inspect call.

DISCOVERY: Call get_unity_guide with topic='getting_started' for workflow recipes and best practices. Call search_tools with no args for a category overview of all tools.

VERIFICATION: Check read_console after refresh_unity or script changes to catch compile errors. Use describe_scene for a quick scene overview. Use diagnose to scan for missing references, shader issues, and build problems.

ASYNC JOBS: Build, test, and profiler operations return a job_id. Poll the same tool with action='get_job' until status is 'completed' or 'failed'.

MULTI-AGENT: You may be sharing this Unity Editor with other agents. Call agent_list to see who is connected. Destructive tool calls automatically lock target resources to your session — if another agent already locked a resource, your call will be rejected with details about who holds the lock. When rejected, work on a different resource or call agent_lock_query to check status. Release locks you no longer need with agent_lock_release. Use agent_set_name to identify yourself to other agents.

COORDINATION: Locks are per-resource (individual GameObjects, files, components), not global. Multiple agents can safely modify different objects in the same scene simultaneously. Before starting a multi-step operation on a resource, consider pre-locking it with agent_lock_acquire to prevent interruption.";

        private const string CheckpointNudgeBlanket =
            " Save a checkpoint (manage_checkpoint action='save') before making changes.";
        private const string CheckpointNudgePrefix =
            " Save a checkpoint (manage_checkpoint action='save') before ";

        /// <summary>
        /// Gets the singleton instance of the MCP server.
        /// </summary>
        public static MCPServer Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (InstanceLock)
                    {
                        if (_instance == null)
                        {
                            _instance = new MCPServer();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Gets or sets the port the server listens on.
        /// </summary>
        public int Port
        {
            get => _port;
            set => _port = value;
        }

        #region JSON-RPC Response Builders

        private JObject CreateSuccessResponse(JToken result, string requestId)
        {
            var response = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["result"] = result,
            };

            if (requestId != null)
            {
                response["id"] = requestId;
            }

            return response;
        }

        private JObject CreateErrorResponse(int code, string message, string requestId, JToken data = null)
        {
            var errorObject = new JObject
            {
                ["code"] = code,
                ["message"] = message
            };

            if (data != null)
            {
                errorObject["data"] = data;
            }

            var response = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["error"] = errorObject
            };

            if (requestId != null)
            {
                response["id"] = requestId;
            }

            return response;
        }

        #endregion

        #region Public API for Native Proxy

        /// <summary>
        /// Handles a raw JSON-RPC request. Called from MCPProxy.
        /// This method is synchronous and runs on Unity's main thread.
        /// </summary>
        /// <param name="jsonRequest">The raw JSON-RPC request string.</param>
        /// <returns>The JSON-RPC response string.</returns>
        public string HandleRequest(string jsonRequest)
        {
            return HandleRequest(jsonRequest, null);
        }

        /// <summary>
        /// Handles a raw JSON-RPC request with session context. Called from MCPProxy.
        /// This method is synchronous and runs on Unity's main thread.
        /// </summary>
        /// <param name="jsonRequest">The raw JSON-RPC request string.</param>
        /// <param name="sessionId">The MCP session ID for this request.</param>
        /// <returns>The JSON-RPC response string.</returns>
        public string HandleRequest(string jsonRequest, string sessionId)
        {
            try
            {
                // Set ambient session context for tool invocations
                RequestContext.CurrentSessionId = sessionId;

                var requestObject = JObject.Parse(jsonRequest);
                string requestId = requestObject["id"]?.ToString();
                string method = requestObject["method"]?.ToString();

                if (string.IsNullOrEmpty(method))
                {
                    return CreateErrorResponse(MCPErrorCodes.InvalidRequest, "Missing 'method' field", requestId)
                        .ToString(Formatting.None);
                }

                JToken paramsToken = requestObject["params"];

                // Session tracking: touch existing sessions or auto-create after domain reload.
                // Only reject requests with no session ID at all (proxy always provides one
                // for initialized clients via the Mcp-Session-Id header).
                if (method != "initialize" && method != "notifications/initialized")
                {
                    if (string.IsNullOrEmpty(sessionId))
                    {
                        return CreateErrorResponse(MCPErrorCodes.InvalidRequest,
                            "Missing session ID. Send initialize first.", requestId)
                            .ToString(Formatting.None);
                    }

                    if (!SessionManager.TouchSession(sessionId))
                    {
                        // Unknown session — likely lost to domain reload. Re-create it.
                        SessionManager.CreateSession(sessionId);
                    }
                }

                JObject response = method switch
                {
                    "initialize" => HandleInitialize(paramsToken, requestId, sessionId),
                    "tools/list" => HandleToolsList(requestId),
                    "tools/call" => HandleToolsCall(paramsToken, requestId, sessionId),
                    "resources/list" => HandleResourcesList(requestId),
                    "resources/templates/list" => HandleResourcesTemplatesList(requestId),
                    "resources/read" => HandleResourcesRead(paramsToken, requestId),
                    "prompts/list" => HandlePromptsList(requestId),
                    "prompts/get" => HandlePromptsGet(paramsToken, requestId),
                    _ => CreateErrorResponse(MCPErrorCodes.MethodNotFound, $"Method not found: {method}", requestId)
                };

                return response.ToString(Formatting.None);
            }
            catch (JsonException jsonException)
            {
                return CreateErrorResponse(MCPErrorCodes.ParseError, $"Parse error: {jsonException.Message}", null)
                    .ToString(Formatting.None);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[MCPServer] Error handling request: {exception}");
                return CreateErrorResponse(MCPErrorCodes.InternalError, $"Internal error: {exception.Message}", null)
                    .ToString(Formatting.None);
            }
            finally
            {
                RequestContext.CurrentSessionId = null;
            }
        }

        #endregion

        #region MCP Method Handlers

        private JObject HandleInitialize(JToken paramsToken, string requestId, string sessionId = null)
        {
            // Create or retrieve session for this agent
            SessionManager.CreateSession(sessionId);

            // Use clientInfo.name from the MCP initialize request as the display name
            string agentName = paramsToken?["clientInfo"]?["name"]?.ToString();
            if (!string.IsNullOrEmpty(sessionId) && !string.IsNullOrEmpty(agentName))
                SessionManager.SetSessionName(sessionId, agentName);

            var result = new JObject
            {
                ["protocolVersion"] = "2025-03-26",
                ["capabilities"] = new JObject
                {
                    ["tools"] = new JObject(),
                    ["resources"] = new JObject(),
                    ["prompts"] = new JObject()
                },
                ["serverInfo"] = new JObject
                {
                    ["name"] = ServerName,
                    ["version"] = ServerVersion
                },
                ["instructions"] = ServerInstructions
            };

            if (!string.IsNullOrEmpty(sessionId))
            {
                result["sessionId"] = sessionId;
            }

            return CreateSuccessResponse(result, requestId);
        }

        private JObject HandleToolsList(string requestId)
        {
            var toolDefinitions = ToolRegistry.GetDefinitions().ToList();
            var toolsArray = new JArray();

            foreach (var tool in toolDefinitions)
            {
                var description = tool.description;

                // Auto-inject checkpoint nudge for destructive tools that modify scene/assets
                bool isDestructive = tool.annotations?.destructiveHint == true;
                bool isReadOnly = tool.annotations?.readOnlyHint == true;
                if (isDestructive && !isReadOnly && !description.Contains("checkpoint"))
                {
                    if (tool.destructiveActions != null && tool.destructiveActions.Count > 0)
                    {
                        // Targeted nudge listing specific destructive actions
                        string actionList = string.Join(", ", tool.destructiveActions);
                        description += CheckpointNudgePrefix + actionList + " actions.";
                    }
                    else
                    {
                        description += CheckpointNudgeBlanket;
                    }
                }

                var toolObject = new JObject
                {
                    ["name"] = tool.name,
                    ["description"] = description,
                    ["inputSchema"] = SerializeInputSchema(tool.inputSchema)
                };

                if (tool.annotations != null)
                {
                    var annotationsObject = new JObject();
                    if (tool.annotations.readOnlyHint.HasValue)
                        annotationsObject["readOnlyHint"] = tool.annotations.readOnlyHint.Value;
                    if (tool.annotations.destructiveHint.HasValue)
                        annotationsObject["destructiveHint"] = tool.annotations.destructiveHint.Value;
                    if (tool.annotations.idempotentHint.HasValue)
                        annotationsObject["idempotentHint"] = tool.annotations.idempotentHint.Value;
                    if (tool.annotations.openWorldHint.HasValue)
                        annotationsObject["openWorldHint"] = tool.annotations.openWorldHint.Value;
                    if (tool.annotations.batchableHint.HasValue)
                        annotationsObject["batchableHint"] = tool.annotations.batchableHint.Value;
                    if (tool.annotations.title != null)
                        annotationsObject["title"] = tool.annotations.title;
                    if (annotationsObject.Count > 0)
                        toolObject["annotations"] = annotationsObject;
                }

                toolsArray.Add(toolObject);
            }

            var result = new JObject
            {
                ["tools"] = toolsArray
            };

            return CreateSuccessResponse(result, requestId);
        }

        private JObject SerializeInputSchema(InputSchema inputSchema)
        {
            if (inputSchema == null)
            {
                return new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject(),
                    ["required"] = new JArray()
                };
            }

            var propertiesObject = new JObject();
            foreach (var property in inputSchema.properties)
            {
                propertiesObject[property.Key] = SerializePropertySchema(property.Value);
            }

            var requiredArray = new JArray();
            foreach (var required in inputSchema.required)
            {
                requiredArray.Add(required);
            }

            return new JObject
            {
                ["type"] = inputSchema.type ?? "object",
                ["properties"] = propertiesObject,
                ["required"] = requiredArray
            };
        }

        private JObject SerializePropertySchema(PropertySchema propertySchema)
        {
            var schemaObject = new JObject();
            if (!string.IsNullOrEmpty(propertySchema.type))
            {
                schemaObject["type"] = propertySchema.type;
            }

            if (!string.IsNullOrEmpty(propertySchema.description))
            {
                schemaObject["description"] = propertySchema.description;
            }

            if (propertySchema.@enum != null && propertySchema.@enum.Count > 0)
            {
                var enumArray = new JArray();
                foreach (var value in propertySchema.@enum)
                {
                    enumArray.Add(value);
                }
                schemaObject["enum"] = enumArray;
            }

            if (propertySchema.items != null)
            {
                schemaObject["items"] = SerializePropertySchema(propertySchema.items);
            }

            if (propertySchema.additionalProperties != null)
            {
                schemaObject["additionalProperties"] = SerializePropertySchema(propertySchema.additionalProperties);
            }

            if (propertySchema.@default != null)
            {
                schemaObject["default"] = JToken.FromObject(propertySchema.@default);
            }

            if (propertySchema.minimum.HasValue)
            {
                schemaObject["minimum"] = propertySchema.minimum.Value;
            }

            if (propertySchema.maximum.HasValue)
            {
                schemaObject["maximum"] = propertySchema.maximum.Value;
            }

            return schemaObject;
        }

        private JObject HandleToolsCall(JToken paramsToken, string requestId, string sessionId = null)
        {
            if (paramsToken == null)
            {
                return CreateErrorResponse(MCPErrorCodes.InvalidParams, "Missing params", requestId);
            }

            string toolName = paramsToken["name"]?.ToString();
            if (string.IsNullOrEmpty(toolName))
            {
                return CreateErrorResponse(MCPErrorCodes.InvalidParams, "Missing 'name' in params", requestId);
            }

            if (!ToolRegistry.HasTool(toolName))
            {
                return CreateErrorResponse(MCPErrorCodes.MethodNotFound, $"Unknown tool: {toolName}", requestId);
            }

            JObject argumentsObject = paramsToken["arguments"] as JObject ?? new JObject();

            try
            {
                // Lock enforcement: check and auto-lock for destructive tools
                var toolDefinition = ToolRegistry.GetDefinitions()
                    .FirstOrDefault(t => t.name == toolName);

                if (toolDefinition?.annotations?.destructiveHint == true && !string.IsNullOrEmpty(sessionId))
                {
                    var argumentsDictionary = ConvertJObjectToDictionary(argumentsObject);
                    string lockError = LockManager.CheckAndAutoLock(toolName, argumentsDictionary, sessionId);
                    if (lockError != null)
                    {
                        return CreateSuccessResponse(BuildToolErrorResult(lockError), requestId);
                    }
                }

                object result = InvokeToolOnMainThread(toolName, argumentsObject);

                var contentArray = new JArray();
                contentArray.Add(new JObject
                {
                    ["type"] = "text",
                    ["text"] = SerializeToolResult(result)
                });

                var toolResult = new JObject
                {
                    ["content"] = contentArray,
                    ["isError"] = false
                };

                return CreateSuccessResponse(toolResult, requestId);
            }
            catch (MCPException mcpException)
            {
                return CreateSuccessResponse(BuildToolErrorResult(mcpException.Message), requestId);
            }
            catch (Exception exception)
            {
                return CreateSuccessResponse(BuildToolErrorResult($"Tool execution failed: {exception.Message}"), requestId);
            }
            finally
            {
                // Release auto-acquired locks after tool execution; manual locks persist
                if (!string.IsNullOrEmpty(sessionId))
                    LockManager.ReleaseAutoLocks(sessionId);
            }
        }

        private static JObject BuildToolErrorResult(string errorMessage)
        {
            var contentArray = new JArray();
            contentArray.Add(new JObject
            {
                ["type"] = "text",
                ["text"] = errorMessage
            });

            return new JObject
            {
                ["content"] = contentArray,
                ["isError"] = true
            };
        }

        private string SerializeToolResult(object result)
        {
            if (result == null)
            {
                return "null";
            }

            if (result is string stringResult)
            {
                return stringResult;
            }

            if (result is ToolResult toolResult)
            {
                // If it's already a ToolResult, serialize the content
                if (toolResult.content.Count == 1 && toolResult.content[0].type == "text")
                {
                    return toolResult.content[0].text;
                }
                return JsonConvert.SerializeObject(toolResult.content);
            }

            // For other types, serialize to JSON
            return JsonConvert.SerializeObject(result, Formatting.Indented);
        }

        /// <summary>
        /// Invokes a tool on Unity's main thread.
        /// Since PollForRequests already runs on the main thread, this executes directly.
        /// </summary>
        private object InvokeToolOnMainThread(string toolName, JObject arguments)
        {
            var argumentsDictionary = ConvertJObjectToDictionary(arguments);
            return ToolRegistry.Invoke(toolName, argumentsDictionary);
        }

        private Dictionary<string, object> ConvertJObjectToDictionary(JObject jObject)
        {
            var result = new Dictionary<string, object>();
            foreach (var property in jObject.Properties())
            {
                result[property.Name] = ConvertJToken(property.Value);
            }
            return result;
        }

        private object ConvertJToken(JToken token)
        {
            return token.Type switch
            {
                JTokenType.Object => ConvertJObjectToDictionary((JObject)token),
                JTokenType.Array => token.Select(ConvertJToken).ToList(),
                JTokenType.String => token.Value<string>(),
                JTokenType.Integer => token.Value<long>(),
                JTokenType.Float => token.Value<double>(),
                JTokenType.Boolean => token.Value<bool>(),
                JTokenType.Null => null,
                _ => token.ToString()
            };
        }

        private JObject HandleResourcesList(string requestId)
        {
            var resourceDefinitions = ResourceRegistry.GetDefinitions().ToList();
            var resourcesArray = new JArray();

            foreach (var resource in resourceDefinitions)
            {
                var resourceObject = new JObject
                {
                    ["uri"] = resource.uri,
                    ["name"] = resource.name,
                    ["description"] = resource.description,
                    ["mimeType"] = resource.mimeType ?? "application/json"
                };
                resourcesArray.Add(resourceObject);
            }

            var result = new JObject
            {
                ["resources"] = resourcesArray
            };

            return CreateSuccessResponse(result, requestId);
        }

        private JObject HandleResourcesTemplatesList(string requestId)
        {
            var templateDefinitions = ResourceRegistry.GetTemplateDefinitions().ToList();
            var templatesArray = new JArray();

            foreach (var template in templateDefinitions)
            {
                var templateObject = new JObject
                {
                    ["uriTemplate"] = template.uriTemplate,
                    ["name"] = template.name,
                    ["description"] = template.description,
                    ["mimeType"] = template.mimeType ?? "application/json"
                };
                templatesArray.Add(templateObject);
            }

            var result = new JObject
            {
                ["resourceTemplates"] = templatesArray
            };

            return CreateSuccessResponse(result, requestId);
        }

        private JObject HandleResourcesRead(JToken paramsToken, string requestId)
        {
            if (paramsToken == null)
            {
                return CreateErrorResponse(MCPErrorCodes.InvalidParams, "Missing params", requestId);
            }

            string resourceUri = paramsToken["uri"]?.ToString();
            if (string.IsNullOrEmpty(resourceUri))
            {
                return CreateErrorResponse(MCPErrorCodes.InvalidParams, "Missing 'uri' in params", requestId);
            }

            if (!ResourceRegistry.HasResource(resourceUri))
            {
                return CreateErrorResponse(MCPErrorCodes.MethodNotFound, $"Unknown resource: {resourceUri}", requestId);
            }

            try
            {
                ResourceContent content = InvokeResourceOnMainThread(resourceUri);

                var contentsArray = new JArray();
                var contentObject = new JObject
                {
                    ["uri"] = content.uri,
                    ["mimeType"] = content.mimeType ?? "application/json"
                };

                if (!string.IsNullOrEmpty(content.text))
                {
                    contentObject["text"] = content.text;
                }
                else if (!string.IsNullOrEmpty(content.blob))
                {
                    contentObject["blob"] = content.blob;
                }

                contentsArray.Add(contentObject);

                var resourceResult = new JObject
                {
                    ["contents"] = contentsArray
                };

                return CreateSuccessResponse(resourceResult, requestId);
            }
            catch (MCPException mcpException)
            {
                return CreateErrorResponse(mcpException.ErrorCode, mcpException.Message, requestId);
            }
            catch (Exception exception)
            {
                return CreateErrorResponse(MCPErrorCodes.InternalError, $"Resource read failed: {exception.Message}", requestId);
            }
        }

        /// <summary>
        /// Invokes a resource on Unity's main thread.
        /// Since PollForRequests already runs on the main thread, this executes directly.
        /// </summary>
        private ResourceContent InvokeResourceOnMainThread(string resourceUri)
        {
            return ResourceRegistry.Invoke(resourceUri);
        }

        private JObject HandlePromptsList(string requestId)
        {
            var promptDefinitions = PromptRegistry.GetDefinitions().ToList();
            var promptsArray = new JArray();

            foreach (var prompt in promptDefinitions)
            {
                var promptObject = new JObject
                {
                    ["name"] = prompt.name,
                    ["description"] = prompt.description
                };

                if (prompt.arguments != null && prompt.arguments.Count > 0)
                {
                    var argumentsArray = new JArray();
                    foreach (var argument in prompt.arguments)
                    {
                        var argumentObject = new JObject
                        {
                            ["name"] = argument.name
                        };
                        if (!string.IsNullOrEmpty(argument.description))
                        {
                            argumentObject["description"] = argument.description;
                        }
                        argumentObject["required"] = argument.required;
                        argumentsArray.Add(argumentObject);
                    }
                    promptObject["arguments"] = argumentsArray;
                }

                promptsArray.Add(promptObject);
            }

            var result = new JObject
            {
                ["prompts"] = promptsArray
            };

            return CreateSuccessResponse(result, requestId);
        }

        private JObject HandlePromptsGet(JToken paramsToken, string requestId)
        {
            if (paramsToken == null)
            {
                return CreateErrorResponse(MCPErrorCodes.InvalidParams, "Missing params", requestId);
            }

            string promptName = paramsToken["name"]?.ToString();
            if (string.IsNullOrEmpty(promptName))
            {
                return CreateErrorResponse(MCPErrorCodes.InvalidParams, "Missing 'name' in params", requestId);
            }

            if (!PromptRegistry.HasPrompt(promptName))
            {
                return CreateErrorResponse(MCPErrorCodes.MethodNotFound, $"Unknown prompt: {promptName}", requestId);
            }

            // Extract arguments
            var arguments = new Dictionary<string, string>();
            var argumentsToken = paramsToken["arguments"] as JObject;
            if (argumentsToken != null)
            {
                foreach (var property in argumentsToken.Properties())
                {
                    arguments[property.Name] = property.Value?.ToString();
                }
            }

            try
            {
                PromptResult promptResult = InvokePromptOnMainThread(promptName, arguments);

                var messagesArray = new JArray();
                if (promptResult.messages != null)
                {
                    foreach (var message in promptResult.messages)
                    {
                        var messageObject = new JObject
                        {
                            ["role"] = message.role,
                            ["content"] = new JObject
                            {
                                ["type"] = message.content.type,
                                ["text"] = message.content.text
                            }
                        };
                        messagesArray.Add(messageObject);
                    }
                }

                var resultObject = new JObject
                {
                    ["messages"] = messagesArray
                };

                if (!string.IsNullOrEmpty(promptResult.description))
                {
                    resultObject["description"] = promptResult.description;
                }

                return CreateSuccessResponse(resultObject, requestId);
            }
            catch (MCPException mcpException)
            {
                return CreateErrorResponse(mcpException.ErrorCode, mcpException.Message, requestId);
            }
            catch (Exception exception)
            {
                return CreateErrorResponse(MCPErrorCodes.InternalError, $"Prompt execution failed: {exception.Message}", requestId);
            }
        }

        /// <summary>
        /// Invokes a prompt on Unity's main thread.
        /// Since PollForRequests already runs on the main thread, this executes directly.
        /// </summary>
        private PromptResult InvokePromptOnMainThread(string promptName, Dictionary<string, string> arguments)
        {
            return PromptRegistry.Invoke(promptName, arguments);
        }

        #endregion
    }
}
