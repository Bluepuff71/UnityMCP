/*
 * UnityMCP Native Proxy - Header
 *
 * This native plugin provides an HTTP server that survives Unity domain reloads.
 * It acts as a proxy between the external MCP server and Unity's managed code.
 */

#ifndef UNITY_MCP_PROXY_H
#define UNITY_MCP_PROXY_H

#ifdef _WIN32
    #define EXPORT __declspec(dllexport)
#else
    #define EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

// TODO: Add function declarations

#ifdef __cplusplus
}
#endif

#endif // UNITY_MCP_PROXY_H
