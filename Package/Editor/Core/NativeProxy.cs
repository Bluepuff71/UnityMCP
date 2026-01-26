using System;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace UnityMCP.Editor.Core
{
    /// <summary>
    /// Delegate for receiving JSON-RPC requests from the native proxy.
    /// Must use Cdecl calling convention to match the native plugin.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void RequestCallback([MarshalAs(UnmanagedType.LPStr)] string jsonRequest);

    /// <summary>
    /// P/Invoke bindings for the native MCP proxy plugin.
    /// The native plugin maintains an HTTP server that survives domain reloads,
    /// ensuring AI assistants never receive connection errors during Unity recompilation.
    /// </summary>
    [InitializeOnLoad]
    public static class NativeProxy
    {
        private const string DLL_NAME = "UnityMCPProxy";
        private const int DEFAULT_PORT = 8080;

        #region P/Invoke Declarations

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern int StartServer(int port);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void StopServer();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void RegisterCallback(RequestCallback callback);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void SendResponse([MarshalAs(UnmanagedType.LPStr)] string json);

        #endregion

        /// <summary>
        /// Stored callback delegate to prevent garbage collection.
        /// The native code holds a pointer to this delegate, so it must remain alive.
        /// </summary>
        private static RequestCallback s_callback;

        /// <summary>
        /// Tracks whether the native proxy has been successfully initialized.
        /// </summary>
        private static bool s_initialized = false;

        /// <summary>
        /// Gets whether the native proxy is currently active.
        /// </summary>
        public static bool IsInitialized => s_initialized;

        /// <summary>
        /// Starts the native proxy server.
        /// </summary>
        public static void Start()
        {
            Initialize();
        }

        /// <summary>
        /// Stops the native proxy server and cleans up resources.
        /// </summary>
        public static void Stop()
        {
            if (!s_initialized)
            {
                return;
            }

            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            EditorApplication.quitting -= OnQuit;

            try
            {
                RegisterCallback(null);
                StopServer();
                Debug.Log("[NativeProxy] Native MCP proxy stopped");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[NativeProxy] Error during stop: {exception.Message}");
            }

            s_initialized = false;
        }

        /// <summary>
        /// Static constructor called automatically by Unity due to [InitializeOnLoad].
        /// Attempts to initialize the native proxy on editor startup and after domain reloads.
        /// </summary>
        static NativeProxy()
        {
            Initialize();
        }

        /// <summary>
        /// Initializes the native proxy by starting the server and registering the callback.
        /// Falls back gracefully if the native plugin is not available.
        /// </summary>
        private static void Initialize()
        {
            if (s_initialized)
            {
                return;
            }

            try
            {
                // Start server - returns 0 on fresh start, 1 if already running (survives domain reload)
                int result = StartServer(DEFAULT_PORT);

                // Result codes: 0 = started fresh, 1 = already running, -1 = failed to bind
                if (result < 0)
                {
                    Debug.LogWarning($"[NativeProxy] Failed to start native server (result={result}), falling back to managed server.");
                    return;
                }

                // Register callback - must store delegate to prevent GC collection
                s_callback = OnRequest;
                RegisterCallback(s_callback);

                // Register for domain unload to safely unregister callback before C# code becomes invalid
                AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
                EditorApplication.quitting += OnQuit;

                // Enable running in background so server responds when Unity is not focused
                Application.runInBackground = true;

                s_initialized = true;
                Debug.Log($"[NativeProxy] Native MCP proxy initialized on port {DEFAULT_PORT}");
            }
            catch (DllNotFoundException dllException)
            {
                Debug.LogWarning($"[NativeProxy] Native plugin not found: {dllException.Message}. Falling back to managed server.");
            }
            catch (EntryPointNotFoundException entryPointException)
            {
                Debug.LogWarning($"[NativeProxy] Native plugin entry point not found: {entryPointException.Message}. Falling back to managed server.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[NativeProxy] Failed to initialize native proxy: {exception.GetType().Name}: {exception.Message}. Falling back to managed server.");
                Debug.LogException(exception);
            }
        }

        /// <summary>
        /// Called before Unity reloads the C# domain (e.g., after script recompilation).
        /// Unregisters the callback to prevent the native code from calling into invalid C# code.
        /// </summary>
        private static void OnBeforeReload()
        {
            // Unregister callback before domain unloads to prevent native code calling invalid C# code
            try
            {
                RegisterCallback(null);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[NativeProxy] Error unregistering callback: {exception.Message}");
            }
        }

        /// <summary>
        /// Called when the Unity Editor is quitting.
        /// Cleans up by unregistering the callback and stopping the native server.
        /// </summary>
        private static void OnQuit()
        {
            try
            {
                RegisterCallback(null);
                StopServer();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[NativeProxy] Error during shutdown: {exception.Message}");
            }

            s_initialized = false;
        }

        /// <summary>
        /// Callback invoked by the native proxy when an HTTP request is received.
        /// Processes the JSON-RPC request through MCPServer and sends back the response.
        /// </summary>
        /// <param name="jsonRequest">The raw JSON-RPC request string from the client.</param>
        private static void OnRequest(string jsonRequest)
        {
            try
            {
                string response = MCPServer.Instance.HandleRequest(jsonRequest);
                SendResponse(response);
            }
            catch (Exception exception)
            {
                // Build error response manually to ensure valid JSON-RPC is always returned
                string errorResponse = $"{{\"jsonrpc\":\"2.0\",\"error\":{{\"code\":-32603,\"message\":\"{EscapeJson(exception.Message)}\"}},\"id\":null}}";
                SendResponse(errorResponse);
            }
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
