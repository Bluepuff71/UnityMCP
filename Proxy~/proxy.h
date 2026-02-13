/*
 * UnityMCP Proxy - Header
 *
 * HTTP server plugin that survives Unity domain reloads.
 * Acts as a proxy between external MCP clients and Unity's C# code.
 *
 * C# polls for pending requests via GetPendingRequest() on EditorApplication.update,
 * eliminating ThreadAbortException by keeping all managed code on the main thread.
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
#define PROXY_MAX_REQUEST_SIZE 262144   /* 256KB */
#define PROXY_REQUEST_TIMEOUT_MS 30000
#define PROXY_RECOMPILE_POLL_INTERVAL_MS 50

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
 * Activate or deactivate C# polling.
 * Call with 1 after registering EditorApplication.update handler.
 * Call with 0 before domain reload to prevent request delivery.
 *
 * @param active 1 to activate, 0 to deactivate
 */
EXPORT void SetPollingActive(int active);

/*
 * Get the pending request body, if any.
 * Returns a pointer to a static buffer containing the request JSON,
 * or NULL if no request is pending.
 * The returned pointer is valid until the next call to GetPendingRequest()
 * or until the request is cleared by the next incoming request.
 *
 * @return Pointer to request body string, or NULL if no pending request
 */
EXPORT const char* GetPendingRequest(void);

/*
 * Send a response back to the waiting HTTP request.
 * Must be called from C# after receiving a request via GetPendingRequest().
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
 * Check if C# polling is currently active.
 *
 * @return 1 if active, 0 if not (e.g., during domain reload)
 */
EXPORT int IsPollerActive(void);

/*
 * Get the process ID of this library instance.
 * Used to verify if an existing server belongs to the same process.
 *
 * @return The process ID as an unsigned long
 */
EXPORT unsigned long GetNativeProcessId(void);

/*
 * Configure the bind address for the server.
 * Must be called before StartServer().
 * Defaults to "127.0.0.1" (localhost only).
 * Use "0.0.0.0" for remote access.
 *
 * @param address The IP address to bind to
 */
EXPORT void ConfigureBindAddress(const char* address);

/*
 * Configure the API key for bearer token authentication.
 * Must be called before StartServer().
 * Pass an empty string to disable authentication.
 *
 * @param key The API key string (e.g., "umcp_...")
 */
EXPORT void ConfigureApiKey(const char* key);

/*
 * Configure TLS with PEM-encoded certificate and private key.
 * Must be called before StartServer().
 * Both cert and key must be provided to enable TLS.
 *
 * @param cert_pem PEM-encoded certificate
 * @param key_pem PEM-encoded private key
 */
EXPORT void ConfigureTls(const char* cert_pem, const char* key_pem);

/*
 * Check if the native proxy was compiled with TLS support.
 * Used by C# to detect builds without -DMG_TLS=MG_TLS_BUILTIN.
 *
 * @return 1 if TLS is supported, 0 if not
 */
EXPORT int GetTlsSupported(void);

#ifdef __cplusplus
}
#endif

#endif /* UNITY_MCP_PROXY_H */
