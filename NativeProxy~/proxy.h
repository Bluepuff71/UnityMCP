/*
 * UnityMCP Native Proxy - Header
 *
 * This native plugin provides an HTTP server that survives Unity domain reloads.
 * It acts as a proxy between the external MCP server and Unity's managed code.
 *
 * License: GPLv2 (compatible with Mongoose library)
 */

#ifndef UNITY_MCP_PROXY_H
#define UNITY_MCP_PROXY_H

#include <stddef.h>

#ifdef _WIN32
    #define EXPORT __declspec(dllexport)
#else
    #define EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/*
 * Configuration constants
 */
#define PROXY_MAX_RESPONSE_SIZE 262144  /* 256KB */
#define PROXY_REQUEST_TIMEOUT_MS 30000

/*
 * Callback type for C# request handler.
 * The callback receives the raw JSON-RPC request body.
 * C# must call SendResponse() with the JSON response.
 */
typedef void (*RequestCallback)(const char* json_request);

/*
 * Start the HTTP server on the specified port.
 *
 * @param port The port number to listen on (e.g., 8080)
 * @return 0 on success, -1 if failed to bind, 1 if already running
 */
EXPORT int StartServer(int port);

/*
 * Stop the HTTP server and release resources.
 * Safe to call even if server is not running.
 */
EXPORT void StopServer(void);

/*
 * Register the C# callback for handling requests.
 * Call with NULL to unregister (e.g., before domain reload).
 *
 * @param callback The callback function, or NULL to unregister
 */
EXPORT void RegisterCallback(RequestCallback callback);

/*
 * Send a response back to the waiting HTTP request.
 * Must be called from C# after receiving a request via the callback.
 *
 * @param json The JSON-RPC response string
 */
EXPORT void SendResponse(const char* json);

/*
 * Check if the server is currently running.
 *
 * @return 1 if running, 0 if not
 */
EXPORT int IsServerRunning(void);

/*
 * Check if a C# callback is currently registered.
 *
 * @return 1 if callback is valid, 0 if not (e.g., during domain reload)
 */
EXPORT int IsCallbackValid(void);

/*
 * Get the process ID of this native library instance.
 * Used to verify if an existing server belongs to the same process.
 *
 * @return The process ID as an unsigned long
 */
EXPORT unsigned long GetNativeProcessId(void);

#ifdef __cplusplus
}
#endif

#endif /* UNITY_MCP_PROXY_H */
