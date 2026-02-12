using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UnityMCP.Editor.Core
{
    /// <summary>
    /// P/Invoke bindings and polling loop for the MCP proxy plugin.
    /// The plugin maintains an HTTP server that survives domain reloads,
    /// ensuring AI assistants never receive connection errors during Unity recompilation.
    ///
    /// C# polls for pending requests via EditorApplication.update, eliminating
    /// ThreadAbortException by keeping all managed code on the main thread.
    /// </summary>
    [InitializeOnLoad]
    public static class MCPProxy
    {
        private const string DLL_NAME = "UnityMCPProxy";
        private const int DEFAULT_PORT = 8080;
        private const int STARTUP_RETRY_DELAY_MS = 200;
        private const int MAX_START_RETRIES = 5;

        /// <summary>
        /// Maximum response size supported by the proxy buffer.
        /// Must match PROXY_MAX_RESPONSE_SIZE in proxy.h.
        /// Responses exceeding this limit will trigger an error response.
        /// </summary>
        public const int MaxResponseSize = 262144;  // 256KB

        #region P/Invoke Declarations

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern int StartServer(int port);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void StopServer();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SetPollingActive(int active);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetPendingRequest();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SendResponse([MarshalAs(UnmanagedType.LPStr)] string json);

        #endregion

        /// <summary>
        /// Tracks whether the proxy has been successfully initialized.
        /// </summary>
        private static bool s_initialized = false;

        /// <summary>
        /// Gets whether the MCP proxy is currently active.
        /// </summary>
        public static bool IsInitialized => s_initialized;

        /// <summary>
        /// Gets or sets whether verbose logging is enabled.
        /// When false, only warnings and errors are logged.
        /// </summary>
        public static bool VerboseLogging { get; set; } = false;

        /// <summary>
        /// Starts the MCP proxy server.
        /// </summary>
        public static void Start()
        {
            Initialize();
        }

        /// <summary>
        /// Stops the MCP proxy server and cleans up resources.
        /// </summary>
        public static void Stop()
        {
            if (!s_initialized)
            {
                return;
            }

            EditorApplication.update -= PollForRequests;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            EditorApplication.quitting -= OnQuit;

            try
            {
                SetPollingActive(0);
                StopServer();
                if (VerboseLogging) Debug.Log("[MCPProxy] MCP proxy stopped");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[MCPProxy] Error during stop: {exception.Message}");
            }

            s_initialized = false;
        }

        /// <summary>
        /// Static constructor called automatically by Unity due to [InitializeOnLoad].
        /// Attempts to initialize the proxy on editor startup and after domain reloads.
        /// </summary>
        static MCPProxy()
        {
            Initialize();
        }

        /// <summary>
        /// Initializes the proxy by starting the server and activating polling.
        /// If the port is temporarily held (e.g. old DLL being unloaded), retries with delays.
        /// </summary>
        private static void Initialize()
        {
            if (s_initialized)
            {
                return;
            }

            try
            {
                int result = StartServer(DEFAULT_PORT);

                // If failed to bind, retry with delays. The old DLL's DllMain cleanup
                // may need a moment to close the listen socket before we can bind.
                if (result < 0)
                {
                    for (int retry = 0; retry < MAX_START_RETRIES; retry++)
                    {
                        Thread.Sleep(STARTUP_RETRY_DELAY_MS);
                        result = StartServer(DEFAULT_PORT);
                        if (result >= 0)
                        {
                            break;
                        }
                    }

                    if (result < 0)
                    {
                        Debug.LogWarning($"[MCPProxy] Failed to start server (result={result}).");
                        return;
                    }
                }

                // Activate polling and hook into EditorApplication.update
                SetPollingActive(1);
                EditorApplication.update += PollForRequests;

                // Register for domain unload to safely deactivate polling before C# code becomes invalid
                AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
                EditorApplication.quitting += OnQuit;

                // Enable running in background so server responds when Unity is not focused
                Application.runInBackground = true;

                s_initialized = true;
                if (VerboseLogging) Debug.Log($"[MCPProxy] MCP proxy initialized on port {DEFAULT_PORT}");
            }
            catch (DllNotFoundException dllException)
            {
                Debug.LogWarning($"[MCPProxy] Plugin not found: {dllException.Message}.");
            }
            catch (EntryPointNotFoundException entryPointException)
            {
                Debug.LogWarning($"[MCPProxy] Plugin entry point not found: {entryPointException.Message}.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[MCPProxy] Failed to initialize proxy: {exception.GetType().Name}: {exception.Message}.");
                Debug.LogException(exception);
            }
        }

        /// <summary>
        /// Polls the proxy for pending requests on every editor update tick.
        /// Runs on Unity's main thread, so no ThreadAbortException is possible.
        /// </summary>
        private static void PollForRequests()
        {
            IntPtr ptr = GetPendingRequest();
            if (ptr == IntPtr.Zero)
            {
                return;
            }

            string jsonRequest = Marshal.PtrToStringAnsi(ptr);
            string requestId = ExtractRequestId(jsonRequest);

            try
            {
                string response = MCPServer.Instance.HandleRequest(jsonRequest);

                if (response != null && response.Length >= MaxResponseSize)
                {
                    Debug.LogWarning($"[MCPProxy] Response size ({response.Length} bytes) exceeds maximum ({MaxResponseSize} bytes). Returning error response.");
                    string errorResponse = BuildErrorResponse(
                        -32603,
                        $"Response too large ({response.Length} bytes). Maximum supported size is {MaxResponseSize - 1} bytes. Try reducing max_depth or using more specific queries.",
                        requestId);
                    SendResponse(errorResponse);
                    return;
                }

                SendResponse(response);
            }
            catch (Exception exception)
            {
                string errorResponse = BuildErrorResponse(-32603, exception.Message, requestId);
                SendResponse(errorResponse);
            }
        }

        /// <summary>
        /// Called before Unity reloads the C# domain (e.g., after script recompilation).
        /// Deactivates polling to prevent request delivery during domain reload.
        /// </summary>
        private static void OnBeforeReload()
        {
            try
            {
                SetPollingActive(0);
                EditorApplication.update -= PollForRequests;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[MCPProxy] Error deactivating polling: {exception.Message}");
            }
        }

        /// <summary>
        /// Called when the Unity Editor is quitting.
        /// Cleans up by deactivating polling and stopping the server.
        /// </summary>
        private static void OnQuit()
        {
            try
            {
                EditorApplication.update -= PollForRequests;
                SetPollingActive(0);
                StopServer();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[MCPProxy] Error during shutdown: {exception.Message}");
            }

            s_initialized = false;
        }

        /// <summary>
        /// Extracts the "id" field from a JSON-RPC request string.
        /// Uses simple string parsing to avoid JSON deserialization overhead.
        /// </summary>
        /// <param name="json">The JSON-RPC request string.</param>
        /// <returns>The request ID as a string, or "null" if not found.</returns>
        private static string ExtractRequestId(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return "null";
            }

            // Simple parser to find "id" field value
            int idIndex = json.IndexOf("\"id\"", StringComparison.Ordinal);
            if (idIndex < 0)
            {
                return "null";
            }

            // Find the colon after "id"
            int colonIndex = json.IndexOf(':', idIndex + 4);
            if (colonIndex < 0)
            {
                return "null";
            }

            // Skip whitespace after colon
            int valueStart = colonIndex + 1;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
            {
                valueStart++;
            }

            if (valueStart >= json.Length)
            {
                return "null";
            }

            char firstChar = json[valueStart];

            // Handle string ID
            if (firstChar == '"')
            {
                int endQuote = json.IndexOf('"', valueStart + 1);
                if (endQuote > valueStart)
                {
                    // Return with quotes for JSON embedding
                    return json.Substring(valueStart, endQuote - valueStart + 1);
                }
            }
            // Handle numeric ID
            else if (char.IsDigit(firstChar) || firstChar == '-')
            {
                int valueEnd = valueStart;
                while (valueEnd < json.Length &&
                       (char.IsDigit(json[valueEnd]) || json[valueEnd] == '-' || json[valueEnd] == '.' ||
                        json[valueEnd] == 'e' || json[valueEnd] == 'E' || json[valueEnd] == '+'))
                {
                    valueEnd++;
                }
                return json.Substring(valueStart, valueEnd - valueStart);
            }
            // Handle null
            else if (json.Substring(valueStart).StartsWith("null", StringComparison.Ordinal))
            {
                return "null";
            }

            return "null";
        }

        /// <summary>
        /// Builds a JSON-RPC error response with proper formatting.
        /// </summary>
        /// <param name="code">The error code.</param>
        /// <param name="message">The error message.</param>
        /// <param name="requestId">The request ID (already formatted for JSON - string IDs include quotes).</param>
        /// <returns>A valid JSON-RPC error response string.</returns>
        private static string BuildErrorResponse(int code, string message, string requestId)
        {
            return $"{{\"jsonrpc\":\"2.0\",\"error\":{{\"code\":{code},\"message\":\"{EscapeJson(message)}\"}},\"id\":{requestId}}}";
        }

        /// <summary>
        /// Escapes special characters in a string for safe inclusion in JSON.
        /// </summary>
        /// <param name="str">The string to escape.</param>
        /// <returns>The escaped string safe for JSON embedding.</returns>
        private static string EscapeJson(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return string.Empty;
            }

            return str
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }
}
