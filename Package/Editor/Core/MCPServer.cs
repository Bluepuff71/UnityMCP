using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityMCP.Editor.Utilities;

namespace UnityMCP.Editor.Core
{
    /// <summary>
    /// Handles automatic restart of MCP server after domain reload.
    /// </summary>
    [InitializeOnLoad]
    internal static class MCPServerDomainReload
    {
        private const string SessionStateKey = "UnityMCP_ServerShouldRun";
        private const string SessionStatePortKey = "UnityMCP_ServerPort";

        static MCPServerDomainReload()
        {
            // Check if server should be running after domain reload
            if (SessionState.GetBool(SessionStateKey, false))
            {
                int port = SessionState.GetInt(SessionStatePortKey, 8080);
                MainThreadDispatcher.Enqueue(() =>
                {
                    MCPServer.Instance.Port = port;
                    MCPServer.Instance.Start();
                });
            }
        }

        public static void SetShouldRun(bool shouldRun, int port)
        {
            SessionState.SetBool(SessionStateKey, shouldRun);
            SessionState.SetInt(SessionStatePortKey, port);
        }
    }

    /// <summary>
    /// HTTP server that handles MCP (Model Context Protocol) JSON-RPC requests.
    /// Provides tool discovery and invocation for AI assistants.
    /// </summary>
    public class MCPServer
    {
        private static MCPServer _instance;
        private static readonly object InstanceLock = new object();

        private HttpListener _listener;
        private CancellationTokenSource _cancellationTokenSource;
        private int _port = 8080;
        private const string ServerName = "UnityMCP";
        private const string ServerVersion = "0.1.0";
        private const int MainThreadTimeoutSeconds = 30;

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
        /// Gets whether the server is currently running.
        /// </summary>
        public bool IsRunning => _listener?.IsListening ?? false;

        /// <summary>
        /// Gets or sets the port the server listens on.
        /// Can only be changed when the server is not running.
        /// </summary>
        public int Port
        {
            get => _port;
            set
            {
                if (IsRunning)
                {
                    Debug.LogWarning("[MCPServer] Cannot change port while server is running. Stop the server first.");
                    return;
                }
                _port = value;
            }
        }

        /// <summary>
        /// Starts the HTTP server.
        /// </summary>
        public void Start()
        {
            if (IsRunning)
            {
                Debug.LogWarning("[MCPServer] Server is already running.");
                return;
            }

            // If native proxy is handling HTTP, don't start managed server
            if (NativeProxy.IsInitialized)
            {
                Debug.Log("[MCPServer] Native proxy is active, skipping managed HTTP server.");
                MCPServerDomainReload.SetShouldRun(true, _port);
                return;
            }

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{_port}/");
                _listener.Start();

                _cancellationTokenSource = new CancellationTokenSource();

                // Persist state for domain reload
                MCPServerDomainReload.SetShouldRun(true, _port);

                // Enable running in background so server responds when Unity is not focused
                Application.runInBackground = true;

                Debug.Log($"[MCPServer] Started on http://localhost:{_port}/");

                ListenAsync(_cancellationTokenSource.Token);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[MCPServer] Failed to start: {exception.Message}");
                Stop();
            }
        }

        /// <summary>
        /// Stops the HTTP server.
        /// </summary>
        /// <param name="clearPersistence">If true, clears the persisted state so server won't restart after domain reload.</param>
        public void Stop(bool clearPersistence = true)
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            if (_listener != null)
            {
                try
                {
                    _listener.Stop();
                    _listener.Close();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[MCPServer] Error during shutdown: {exception.Message}");
                }
                finally
                {
                    _listener = null;
                }
            }

            // Clear persistence so server won't auto-restart after domain reload
            if (clearPersistence)
            {
                MCPServerDomainReload.SetShouldRun(false, _port);
            }

            Debug.Log("[MCPServer] Stopped.");
        }

        private async void ListenAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync().ConfigureAwait(false);

                    // Handle request without awaiting to allow concurrent requests
                    _ = HandleRequestAsync(context);
                }
                catch (ObjectDisposedException)
                {
                    // Listener was disposed, exit gracefully
                    break;
                }
                catch (HttpListenerException listenerException)
                {
                    // Listener was stopped, exit gracefully
                    if (listenerException.ErrorCode == UnityConstants.HttpOperationAborted)
                    {
                        break;
                    }
                    Debug.LogWarning($"[MCPServer] Listener error: {listenerException.Message}");
                }
                catch (Exception exception)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        Debug.LogWarning($"[MCPServer] Error accepting connection: {exception.Message}");
                    }
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            try
            {
                // Add CORS headers for browser-based clients
                AddCorsHeaders(context.Response);

                // Handle CORS preflight
                if (context.Request.HttpMethod == "OPTIONS")
                {
                    context.Response.StatusCode = 204;
                    context.Response.Close();
                    return;
                }

                // Only accept POST requests for JSON-RPC
                if (context.Request.HttpMethod != "POST")
                {
                    await WriteErrorResponse(context, 405, "Method Not Allowed");
                    return;
                }

                // Read and parse the request
                JObject requestObject;
                string requestId = null;

                try
                {
                    string requestBody = await ReadRequestBody(context.Request);
                    requestObject = JObject.Parse(requestBody);
                    requestId = requestObject["id"]?.ToString();
                }
                catch (JsonException jsonException)
                {
                    await WriteJsonRpcResponse(context, CreateErrorResponse(
                        MCPErrorCodes.ParseError,
                        $"Parse error: {jsonException.Message}",
                        null));
                    return;
                }

                // Validate JSON-RPC request
                string method = requestObject["method"]?.ToString();
                if (string.IsNullOrEmpty(method))
                {
                    await WriteJsonRpcResponse(context, CreateErrorResponse(
                        MCPErrorCodes.InvalidRequest,
                        "Missing 'method' field",
                        requestId));
                    return;
                }

                JToken paramsToken = requestObject["params"];

                // Route to handler
                JObject response = method switch
                {
                    "initialize" => HandleInitialize(requestId),
                    "tools/list" => HandleToolsList(requestId),
                    "tools/call" => await HandleToolsCall(paramsToken, requestId),
                    "resources/list" => HandleResourcesList(requestId),
                    "resources/read" => HandleResourcesRead(paramsToken, requestId),
                    _ => CreateErrorResponse(MCPErrorCodes.MethodNotFound, $"Method not found: {method}", requestId)
                };

                await WriteJsonRpcResponse(context, response);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[MCPServer] Error handling request: {exception}");

                try
                {
                    await WriteJsonRpcResponse(context, CreateErrorResponse(
                        MCPErrorCodes.InternalError,
                        $"Internal error: {exception.Message}",
                        null));
                }
                catch
                {
                    // Response already started or connection closed
                }
            }
        }

        private void AddCorsHeaders(HttpListenerResponse response)
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
            response.Headers.Add("Access-Control-Max-Age", "86400");
        }

        private async Task<string> ReadRequestBody(HttpListenerRequest request)
        {
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                return await reader.ReadToEndAsync();
            }
        }

        private async Task WriteJsonRpcResponse(HttpListenerContext context, JObject responseObject)
        {
            string responseJson = responseObject.ToString(Formatting.None);
            byte[] responseBytes = Encoding.UTF8.GetBytes(responseJson);

            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = responseBytes.Length;
            context.Response.StatusCode = 200;

            await context.Response.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
            context.Response.Close();
        }

        private async Task WriteErrorResponse(HttpListenerContext context, int statusCode, string message)
        {
            context.Response.StatusCode = statusCode;
            byte[] responseBytes = Encoding.UTF8.GetBytes(message);
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
            context.Response.Close();
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
        /// Handles a raw JSON-RPC request. Called from NativeProxy.
        /// This method is synchronous and can be called from any thread.
        /// Tool and resource invocations are automatically dispatched to Unity's main thread.
        /// </summary>
        /// <param name="jsonRequest">The raw JSON-RPC request string.</param>
        /// <returns>The JSON-RPC response string.</returns>
        public string HandleRequest(string jsonRequest)
        {
            try
            {
                var requestObject = JObject.Parse(jsonRequest);
                string requestId = requestObject["id"]?.ToString();
                string method = requestObject["method"]?.ToString();

                if (string.IsNullOrEmpty(method))
                {
                    return CreateErrorResponse(MCPErrorCodes.InvalidRequest, "Missing 'method' field", requestId)
                        .ToString(Formatting.None);
                }

                JToken paramsToken = requestObject["params"];

                JObject response = method switch
                {
                    "initialize" => HandleInitialize(requestId),
                    "tools/list" => HandleToolsList(requestId),
                    "tools/call" => HandleToolsCallSync(paramsToken, requestId),
                    "resources/list" => HandleResourcesList(requestId),
                    "resources/read" => HandleResourcesRead(paramsToken, requestId),
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
        }

        #endregion

        #region MCP Method Handlers

        private JObject HandleInitialize(string requestId)
        {
            var result = new JObject
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new JObject
                {
                    ["tools"] = new JObject(),
                    ["resources"] = new JObject()
                },
                ["serverInfo"] = new JObject
                {
                    ["name"] = ServerName,
                    ["version"] = ServerVersion
                }
            };

            return CreateSuccessResponse(result, requestId);
        }

        private JObject HandleToolsList(string requestId)
        {
            var toolDefinitions = ToolRegistry.GetDefinitions().ToList();
            var toolsArray = new JArray();

            foreach (var tool in toolDefinitions)
            {
                var toolObject = new JObject
                {
                    ["name"] = tool.name,
                    ["description"] = tool.description,
                    ["inputSchema"] = SerializeInputSchema(tool.inputSchema)
                };
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
            var schemaObject = new JObject
            {
                ["type"] = propertySchema.type
            };

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

            if (propertySchema.@default != null)
            {
                schemaObject["default"] = JToken.FromObject(propertySchema.@default);
            }

            return schemaObject;
        }

        private async Task<JObject> HandleToolsCall(JToken paramsToken, string requestId)
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
                object result = await InvokeToolOnMainThreadAsync(toolName, argumentsObject);

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
                var contentArray = new JArray();
                contentArray.Add(new JObject
                {
                    ["type"] = "text",
                    ["text"] = mcpException.Message
                });

                var toolResult = new JObject
                {
                    ["content"] = contentArray,
                    ["isError"] = true
                };

                return CreateSuccessResponse(toolResult, requestId);
            }
            catch (Exception exception)
            {
                var contentArray = new JArray();
                contentArray.Add(new JObject
                {
                    ["type"] = "text",
                    ["text"] = $"Tool execution failed: {exception.Message}"
                });

                var toolResult = new JObject
                {
                    ["content"] = contentArray,
                    ["isError"] = true
                };

                return CreateSuccessResponse(toolResult, requestId);
            }
        }

        /// <summary>
        /// Synchronous version of HandleToolsCall for use with NativeProxy.
        /// This method blocks until the tool execution completes on the main thread.
        /// </summary>
        private JObject HandleToolsCallSync(JToken paramsToken, string requestId)
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
                var contentArray = new JArray();
                contentArray.Add(new JObject
                {
                    ["type"] = "text",
                    ["text"] = mcpException.Message
                });

                var toolResult = new JObject
                {
                    ["content"] = contentArray,
                    ["isError"] = true
                };

                return CreateSuccessResponse(toolResult, requestId);
            }
            catch (Exception exception)
            {
                var contentArray = new JArray();
                contentArray.Add(new JObject
                {
                    ["type"] = "text",
                    ["text"] = $"Tool execution failed: {exception.Message}"
                });

                var toolResult = new JObject
                {
                    ["content"] = contentArray,
                    ["isError"] = true
                };

                return CreateSuccessResponse(toolResult, requestId);
            }
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

        private Task<object> InvokeToolOnMainThreadAsync(string toolName, JObject arguments)
        {
            var taskCompletionSource = new TaskCompletionSource<object>();

            object result = null;
            Exception error = null;
            var completedEvent = new ManualResetEventSlim(false);

            // Schedule on Unity main thread using dispatcher that works even when Unity is not in focus
            MainThreadDispatcher.Enqueue(() =>
            {
                try
                {
                    var argumentsDictionary = ConvertJObjectToDictionary(arguments);
                    result = ToolRegistry.Invoke(toolName, argumentsDictionary);
                }
                catch (Exception exception)
                {
                    error = exception;
                }
                finally
                {
                    completedEvent.Set();
                }
            });

            // Wait for completion on a background thread
            Task.Run(() =>
            {
                try
                {
                    int timeoutMilliseconds = MainThreadTimeoutSeconds * 1000;
                    if (!completedEvent.Wait(timeoutMilliseconds))
                    {
                        taskCompletionSource.TrySetException(
                            new TimeoutException($"Tool invocation timed out after {MainThreadTimeoutSeconds} seconds"));
                        return;
                    }

                    if (error != null)
                    {
                        taskCompletionSource.TrySetException(error);
                    }
                    else
                    {
                        taskCompletionSource.TrySetResult(result);
                    }
                }
                finally
                {
                    completedEvent.Dispose();
                }
            });

            return taskCompletionSource.Task;
        }

        /// <summary>
        /// Synchronous version of InvokeToolOnMainThreadAsync for use with NativeProxy.
        /// This method blocks the calling thread until the tool execution completes on Unity's main thread.
        /// Uses DispatchAndWait for thread-safe main thread dispatch.
        /// </summary>
        private object InvokeToolOnMainThread(string toolName, JObject arguments)
        {
            int timeoutMilliseconds = MainThreadTimeoutSeconds * 1000;

            return MainThreadDispatcher.DispatchAndWait(() =>
            {
                var argumentsDictionary = ConvertJObjectToDictionary(arguments);
                return ToolRegistry.Invoke(toolName, argumentsDictionary);
            }, timeoutMilliseconds);
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

        private ResourceContent InvokeResourceOnMainThread(string resourceUri)
        {
            int timeoutMilliseconds = MainThreadTimeoutSeconds * 1000;

            return MainThreadDispatcher.DispatchAndWait(() =>
            {
                return ResourceRegistry.Invoke(resourceUri);
            }, timeoutMilliseconds);
        }

        #endregion
    }
}
