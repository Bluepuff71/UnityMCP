using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
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

        /// <summary>
        /// Maximum response size supported by the proxy buffer.
        /// Must match PROXY_MAX_RESPONSE_SIZE in proxy.h.
        /// Responses exceeding this limit will trigger an error response.
        /// </summary>
        public const int MaxResponseSize = 262144;  // 256KB

        /// <summary>
        /// Maximum request size supported by the proxy buffer.
        /// Must match PROXY_MAX_REQUEST_SIZE in proxy.h.
        /// </summary>
        private const int MaxRequestSize = 262144;  // 256KB

        #region P/Invoke Declarations

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern int StartServer(int port);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void StopServer();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SetPollingActive(int active);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetNextRequest(StringBuilder outJson, int outJsonSize, StringBuilder outSessionId, int outSessionIdSize);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SendResponseForSlot(int slotId, [MarshalAs(UnmanagedType.LPStr)] string json);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetQueueDepth();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetActiveSessionCount();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ConfigureBindAddress([MarshalAs(UnmanagedType.LPStr)] string address);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ConfigureApiKey([MarshalAs(UnmanagedType.LPStr)] string key);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ConfigureTls(
            [MarshalAs(UnmanagedType.LPStr)] string certPem,
            [MarshalAs(UnmanagedType.LPStr)] string keyPem);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetTlsSupported();

        #endregion

        /// <summary>
        /// Tracks whether the proxy has been successfully initialized.
        /// </summary>
        private static bool s_initialized = false;

        /// <summary>
        /// Re-entrancy guard for PollForRequests. Although GetNextRequest() atomically
        /// transitions slots from pending to processing, this guard provides additional
        /// safety against re-entrant EditorApplication.update calls during tool execution
        /// (e.g., from Camera.Render or AssetDatabase operations).
        /// </summary>
        private static bool s_isProcessingRequest = false;

        /// <summary>
        /// Reusable StringBuilder buffers for GetNextRequest P/Invoke marshaling.
        /// Pre-allocated to match native buffer sizes.
        /// </summary>
        private static readonly StringBuilder s_requestBuffer = new StringBuilder(MaxRequestSize);
        private static readonly StringBuilder s_sessionIdBuffer = new StringBuilder(64);

        /// <summary>
        /// Timestamp of last session pruning check.
        /// </summary>
        private static DateTime s_lastPruneTime = DateTime.Now;
        private static readonly TimeSpan PruneInterval = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Deduplication cache: maps "{sessionId}:{requestId}" → cached response.
        /// The MCP transport may retry requests with the same ID if the first
        /// attempt times out. Without deduplication, each retry executes the
        /// tool again (creating duplicate GameObjects, etc.).
        /// </summary>
        private static readonly Dictionary<string, string> s_responseCache = new Dictionary<string, string>();
        private static readonly Queue<string> s_responseCacheOrder = new Queue<string>();
        private const int ResponseCacheCapacity = 20;

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
        /// Gets or sets whether remote access is enabled.
        /// When enabled, binds to 0.0.0.0 with TLS and API key authentication.
        /// Note: API key is stored in EditorPrefs (plaintext on disk).
        /// </summary>
        public static bool RemoteAccessEnabled
        {
            get => EditorPrefs.GetBool("UnityMCP_RemoteAccess", false);
            set => EditorPrefs.SetBool("UnityMCP_RemoteAccess", value);
        }

        /// <summary>
        /// Gets or sets the API key used for bearer token authentication.
        /// Auto-generated on first enable of remote access.
        /// </summary>
        public static string ApiKey
        {
            get => EditorPrefs.GetString("UnityMCP_ApiKey", "");
            set => EditorPrefs.SetString("UnityMCP_ApiKey", value);
        }

        /// <summary>
        /// Gets or sets the maximum number of calls allowed in a single batch.
        /// </summary>
        public static int BatchMaxSize
        {
            get => EditorPrefs.GetInt("UnityMCP_BatchMaxSize", 20);
            set => EditorPrefs.SetInt("UnityMCP_BatchMaxSize", value);
        }

        /// <summary>
        /// Gets or sets the maximum number of consecutive errors before a batch is stopped.
        /// </summary>
        public static int BatchMaxConsecutiveErrors
        {
            get => EditorPrefs.GetInt("UnityMCP_BatchMaxConsecutiveErrors", 3);
            set => EditorPrefs.SetInt("UnityMCP_BatchMaxConsecutiveErrors", value);
        }

        /// <summary>
        /// Gets or sets the maximum error percentage (0-100) before a batch is stopped.
        /// </summary>
        public static int BatchMaxErrorPercent
        {
            get => EditorPrefs.GetInt("UnityMCP_BatchMaxErrorPercent", 50);
            set => EditorPrefs.SetInt("UnityMCP_BatchMaxErrorPercent", value);
        }

        /// <summary>
        /// Checks whether the loaded native proxy was compiled with TLS support.
        /// Returns false if the DLL is missing or outdated.
        /// </summary>
        public static bool IsTlsSupported
        {
            get
            {
                try { return GetTlsSupported() != 0; }
                catch { return false; }
            }
        }

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
        /// Restarts the MCP proxy server (stop + reconfigure + start).
        /// Used when remote access settings change.
        /// </summary>
        public static void Restart()
        {
            Stop();
            Initialize();
        }

        /// <summary>
        /// Generates a cryptographically random API key with the "umcp_" prefix.
        /// </summary>
        public static string GenerateApiKey()
        {
            var bytes = new byte[24];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return "umcp_" + BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// Applies remote access configuration to the native proxy.
        /// Must be called before StartServer().
        /// </summary>
        private static void ApplyRemoteAccessConfig()
        {
            if (RemoteAccessEnabled)
            {
                // Verify native proxy was compiled with TLS support
                try
                {
                    if (GetTlsSupported() == 0)
                    {
                        Debug.LogError("[MCPProxy] Cannot enable remote access: native proxy was compiled without TLS support. " +
                            "Rebuild with -DMG_TLS=MG_TLS_BUILTIN.");
                        RemoteAccessEnabled = false;
                        ConfigureBindAddress("127.0.0.1");
                        ConfigureApiKey("");
                        ConfigureTls("", "");
                        return;
                    }
                }
                catch (EntryPointNotFoundException)
                {
                    Debug.LogError("[MCPProxy] Cannot enable remote access: native proxy is outdated (missing GetTlsSupported). " +
                        "Please restart the editor to load the updated plugin.");
                    RemoteAccessEnabled = false;
                    ConfigureBindAddress("127.0.0.1");
                    ConfigureApiKey("");
                    ConfigureTls("", "");
                    return;
                }

                // Ensure API key exists
                if (string.IsNullOrEmpty(ApiKey))
                    ApiKey = GenerateApiKey();

                // Load or generate TLS certificate
                string certDir = CertificateGenerator.GetCertDirectory();
                var (certPem, keyPem) = CertificateGenerator.GenerateOrLoad(certDir);

                ConfigureBindAddress("0.0.0.0");
                ConfigureApiKey(ApiKey);

                if (!string.IsNullOrEmpty(certPem) && !string.IsNullOrEmpty(keyPem))
                {
                    ConfigureTls(certPem, keyPem);
                    if (VerboseLogging) Debug.Log("[MCPProxy] Remote access enabled with TLS + API key");
                }
                else
                {
                    // Refuse to start remote access without TLS — API key would be sent in plaintext
                    Debug.LogError("[MCPProxy] Cannot enable remote access: TLS certificate generation failed. " +
                        "Place your own cert.pem and key.pem in: " + certDir);
                    RemoteAccessEnabled = false;
                    ConfigureBindAddress("127.0.0.1");
                    ConfigureApiKey("");
                    ConfigureTls("", "");
                }
            }
            else
            {
                ConfigureBindAddress("127.0.0.1");
                ConfigureApiKey("");
                ConfigureTls("", "");
            }
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
        /// </summary>
        private static void Initialize()
        {
            if (s_initialized)
            {
                return;
            }

            try
            {
                // Configure remote access before starting the server
                ApplyRemoteAccessConfig();

                int result = StartServer(DEFAULT_PORT);
                if (result < 0)
                {
                    Debug.LogWarning($"[MCPProxy] Failed to bind to port {DEFAULT_PORT}. If you just updated the package, restart the Unity Editor. Otherwise, check if another Unity instance is using the same port.");
                    return;
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
        /// Drains the native request queue one request at a time using the slot-based API.
        /// Runs on Unity's main thread, so no ThreadAbortException is possible.
        /// </summary>
        private static void PollForRequests()
        {
            // Re-entrancy guard against re-entrant EditorApplication.update calls during
            // tool execution (e.g., from Camera.Render or AssetDatabase operations).
            if (s_isProcessingRequest) return;

            // Defer request consumption while Unity is compiling. Requests stay in
            // the native queue; the HTTP connections will timeout or get domain-reload
            // errors from the native proxy.
            if (EditorApplication.isCompiling) return;

            // Periodically prune expired sessions
            var now = DateTime.Now;
            if (now - s_lastPruneTime > PruneInterval)
            {
                s_lastPruneTime = now;
                SessionManager.PruneExpiredSessions();
            }

            // Get next pending request from the native queue
            s_requestBuffer.Clear();
            s_sessionIdBuffer.Clear();
            int slotId = GetNextRequest(s_requestBuffer, MaxRequestSize, s_sessionIdBuffer, 64);
            if (slotId < 0) return;

            s_isProcessingRequest = true;
            try
            {
                string jsonRequest = s_requestBuffer.ToString();
                string sessionId = s_sessionIdBuffer.ToString();

                ProcessRequest(slotId, jsonRequest, sessionId);
            }
            finally
            {
                s_isProcessingRequest = false;
            }
        }

        /// <summary>
        /// Processes a single request from the native queue, routing it through
        /// session management and the MCP server.
        /// </summary>
        /// <param name="slotId">The native slot ID for routing the response back.</param>
        /// <param name="jsonRequest">The raw JSON-RPC request string.</param>
        /// <param name="sessionId">The MCP session ID for this request.</param>
        private static void ProcessRequest(int slotId, string jsonRequest, string sessionId)
        {
            string requestId = ExtractRequestId(jsonRequest);
            string toolName = ExtractToolName(jsonRequest);
            string argumentsSummary = toolName != null ? ExtractArguments(jsonRequest) : null;

            // Deduplication: include sessionId in cache key to avoid cross-session collisions
            string cacheKey = $"{sessionId}:{requestId}";
            if (requestId != null && requestId != "null" && s_responseCache.TryGetValue(cacheKey, out string cachedResponse))
            {
                SendResponseForSlot(slotId, cachedResponse);
                return;
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                string response = MCPServer.Instance.HandleRequest(jsonRequest, sessionId);

                if (response != null && response.Length >= MaxResponseSize)
                {
                    stopwatch.Stop();
                    Debug.LogWarning($"[MCPProxy] Response size ({response.Length} bytes) exceeds maximum ({MaxResponseSize} bytes). Returning error response.");
                    string errorResponse = BuildErrorResponse(
                        -32603,
                        $"Response too large ({response.Length} bytes). Maximum supported size is {MaxResponseSize - 1} bytes. Try reducing max_depth or using more specific queries.",
                        requestId);
                    CacheResponse(cacheKey, errorResponse);
                    SendResponseForSlot(slotId, errorResponse);
                    if (toolName != null)
                        ActivityLog.Record(toolName, false, "Response too large",
                            stopwatch.ElapsedMilliseconds, argumentsSummary, response.Length, sessionId);
                    return;
                }

                CacheResponse(cacheKey, response);
                SendResponseForSlot(slotId, response);
                stopwatch.Stop();
                if (toolName != null)
                    ActivityLog.Record(toolName, true, null,
                        stopwatch.ElapsedMilliseconds, argumentsSummary, response?.Length ?? 0, sessionId);
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                string errorResponse = BuildErrorResponse(-32603, exception.Message, requestId);
                CacheResponse(cacheKey, errorResponse);
                SendResponseForSlot(slotId, errorResponse);
                if (toolName != null)
                    ActivityLog.Record(toolName, false, exception.Message,
                        stopwatch.ElapsedMilliseconds, argumentsSummary, 0, sessionId);
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
        /// Extracts the tool name from a JSON-RPC tools/call request.
        /// Looks for "method":"tools/call" then finds "name" inside "params".
        /// Returns null for non-tool-call requests (e.g., initialize, resources/list).
        /// </summary>
        /// <param name="json">The JSON-RPC request string.</param>
        /// <returns>The tool name, or null if this is not a tools/call request.</returns>
        private static string ExtractToolName(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            // Only extract tool name from tools/call requests
            if (json.IndexOf("\"tools/call\"", StringComparison.Ordinal) < 0)
                return null;

            // Find "params" object, then "name" inside it
            int paramsIndex = json.IndexOf("\"params\"", StringComparison.Ordinal);
            if (paramsIndex < 0)
                return null;

            // Find "name" after params
            int nameKeyIndex = json.IndexOf("\"name\"", paramsIndex, StringComparison.Ordinal);
            if (nameKeyIndex < 0)
                return null;

            // Find colon after "name"
            int colonIndex = json.IndexOf(':', nameKeyIndex + 6);
            if (colonIndex < 0)
                return null;

            // Skip whitespace
            int valueStart = colonIndex + 1;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
                valueStart++;

            if (valueStart >= json.Length || json[valueStart] != '"')
                return null;

            int endQuote = json.IndexOf('"', valueStart + 1);
            if (endQuote <= valueStart)
                return null;

            return json.Substring(valueStart + 1, endQuote - valueStart - 1);
        }

        /// <summary>
        /// Extracts a compact summary of tool arguments from a JSON-RPC request.
        /// Uses brace-matching to find the "arguments" object, then truncates to maxLength.
        /// Returns null for non-tool-call requests or if arguments not found.
        /// </summary>
        private static string ExtractArguments(string json, int maxLength = 120)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            int paramsIndex = json.IndexOf("\"params\"", StringComparison.Ordinal);
            if (paramsIndex < 0)
                return null;

            int argsKeyIndex = json.IndexOf("\"arguments\"", paramsIndex, StringComparison.Ordinal);
            if (argsKeyIndex < 0)
                return null;

            int colonIndex = json.IndexOf(':', argsKeyIndex + 11);
            if (colonIndex < 0)
                return null;

            int valueStart = colonIndex + 1;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
                valueStart++;

            if (valueStart >= json.Length || json[valueStart] != '{')
                return null;

            // Brace-match to find the end of the arguments object
            int depth = 0;
            int valueEnd = valueStart;
            bool inString = false;
            bool escaped = false;

            for (int i = valueStart; i < json.Length; i++)
            {
                char c = json[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (c == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }
                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }
                if (inString) continue;

                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        valueEnd = i + 1;
                        break;
                    }
                }
            }

            if (depth != 0)
                return null;

            string argsJson = json.Substring(valueStart, valueEnd - valueStart);
            if (argsJson.Length > maxLength)
                argsJson = argsJson.Substring(0, maxLength) + "...";

            return argsJson;
        }

        /// <summary>
        /// Caches a response by request ID for deduplication.
        /// Maintains a bounded FIFO cache of recent responses.
        /// </summary>
        private static void CacheResponse(string requestId, string response)
        {
            if (requestId == null || requestId == "null" || response == null)
                return;

            s_responseCache[requestId] = response;
            s_responseCacheOrder.Enqueue(requestId);

            while (s_responseCacheOrder.Count > ResponseCacheCapacity)
            {
                string oldestRequestId = s_responseCacheOrder.Dequeue();
                s_responseCache.Remove(oldestRequestId);
            }
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
