/*
 * UnityMCP Proxy - Implementation
 *
 * HTTP server plugin that survives Unity domain reloads.
 * Acts as a proxy between external MCP clients and Unity's C# code.
 *
 * When C# is unavailable (during recompile), it blocks until polling is re-activated.
 * When C# is available, it stores the request in a buffer and poll-waits for the response.
 *
 * License: GPLv2 (compatible with Mongoose library)
 */

#include "proxy.h"
#include "mongoose.h"
#include <string.h>
#include <stdio.h>
#include <stdlib.h>

#ifdef _WIN32
    #include <windows.h>
    typedef HANDLE ThreadHandle;
    #define PROXY_SLEEP_MS(ms) Sleep((DWORD)(ms))
    #define GET_PROCESS_ID() ((unsigned long)GetCurrentProcessId())
#else
    #include <pthread.h>
    #include <unistd.h>
    typedef pthread_t ThreadHandle;
    #define PROXY_SLEEP_MS(ms) usleep((ms) * 1000)
    #define GET_PROCESS_ID() ((unsigned long)getpid())
#endif

/*
 * Internal state
 */
static struct mg_mgr s_mgr;
static struct mg_connection* s_listener = NULL;
static volatile int s_running = 0;
static volatile int s_poller_active = 0;
static ThreadHandle s_server_thread;

/* Request buffer for C# polling */
static char s_request_buffer[PROXY_MAX_REQUEST_SIZE];
static volatile int s_has_request = 0;

/* Response buffer for synchronous C# response */
static char s_response_buffer[PROXY_MAX_RESPONSE_SIZE];
static volatile int s_has_response = 0;

/* Flag set by DllMain/destructor to signal the server thread to exit and clean up */
static volatile int s_unloading = 0;

/*
 * Buffer for building dynamic error responses with request ID
 */
static char s_error_response_buffer[1024];

/*
 * Extract the "id" field from a JSON-RPC request.
 * Returns a pointer to a static buffer containing the id value (including quotes for strings),
 * or "null" if not found or on parse error.
 */
static char s_id_buffer[256];
static const char* ExtractJsonRpcId(const char* json, size_t json_len)
{
    /* Simple JSON parser to find "id" field */
    const char* id_key = "\"id\"";
    const char* pos = json;
    const char* end = json + json_len;

    while (pos < end)
    {
        /* Find "id" key */
        const char* found = strstr(pos, id_key);
        if (found == NULL || found >= end)
        {
            return "null";
        }

        /* Move past the key */
        pos = found + 4; /* strlen("\"id\"") */

        /* Skip whitespace */
        while (pos < end && (*pos == ' ' || *pos == '\t' || *pos == '\n' || *pos == '\r'))
        {
            pos++;
        }

        /* Expect colon */
        if (pos >= end || *pos != ':')
        {
            continue; /* Not the right "id", keep searching */
        }
        pos++;

        /* Skip whitespace */
        while (pos < end && (*pos == ' ' || *pos == '\t' || *pos == '\n' || *pos == '\r'))
        {
            pos++;
        }

        if (pos >= end)
        {
            return "null";
        }

        /* Parse the value */
        if (*pos == '"')
        {
            /* String value - find the closing quote */
            const char* start = pos;
            pos++;
            while (pos < end && *pos != '"')
            {
                if (*pos == '\\' && pos + 1 < end)
                {
                    pos++; /* Skip escaped character */
                }
                pos++;
            }
            if (pos < end)
            {
                pos++; /* Include closing quote */
                size_t len = pos - start;
                if (len >= sizeof(s_id_buffer))
                {
                    len = sizeof(s_id_buffer) - 1;
                }
                memcpy(s_id_buffer, start, len);
                s_id_buffer[len] = '\0';
                return s_id_buffer;
            }
        }
        else if (*pos == '-' || (*pos >= '0' && *pos <= '9'))
        {
            /* Number value */
            const char* start = pos;
            while (pos < end && ((*pos >= '0' && *pos <= '9') || *pos == '-' || *pos == '.' || *pos == 'e' || *pos == 'E' || *pos == '+'))
            {
                pos++;
            }
            size_t len = pos - start;
            if (len >= sizeof(s_id_buffer))
            {
                len = sizeof(s_id_buffer) - 1;
            }
            memcpy(s_id_buffer, start, len);
            s_id_buffer[len] = '\0';
            return s_id_buffer;
        }
        else if (strncmp(pos, "null", 4) == 0)
        {
            return "null";
        }

        /* Unknown value type, return null */
        return "null";
    }

    return "null";
}

/*
 * Build a JSON-RPC error response with the given error code, message, and request ID.
 */
static const char* BuildErrorResponse(int code, const char* message, const char* id)
{
    snprintf(s_error_response_buffer, sizeof(s_error_response_buffer),
        "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":%d,\"message\":\"%s\"},\"id\":%s}",
        code, message, id);
    return s_error_response_buffer;
}

/*
 * CORS headers for all responses
 */
static const char* CORS_HEADERS =
    "Content-Type: application/json\r\n"
    "Access-Control-Allow-Origin: *\r\n"
    "Access-Control-Allow-Methods: POST, OPTIONS\r\n"
    "Access-Control-Allow-Headers: Content-Type\r\n";

/*
 * Server thread function.
 * Polls the Mongoose event manager in a loop until s_running is cleared.
 * When s_unloading is set (DLL being unloaded), the thread cleans up
 * sockets itself since StopServer can't wait for the thread from DllMain.
 */
#ifdef _WIN32
static DWORD WINAPI ServerThreadFunc(LPVOID param)
{
    (void)param;
    while (s_running)
    {
        mg_mgr_poll(&s_mgr, 10);
    }
    /* If DLL is being unloaded, thread must clean up (StopServer can't wait from DllMain) */
    if (s_unloading)
    {
        s_listener = NULL;
        s_poller_active = 0;
        s_has_request = 0;
        mg_mgr_free(&s_mgr);
    }
    return 0;
}
#else
static void* ServerThreadFunc(void* param)
{
    (void)param;
    while (s_running)
    {
        mg_mgr_poll(&s_mgr, 10);
    }
    /* If DLL is being unloaded, thread must clean up (StopServer can't wait from destructor) */
    if (s_unloading)
    {
        s_listener = NULL;
        s_poller_active = 0;
        s_has_request = 0;
        mg_mgr_free(&s_mgr);
    }
    return NULL;
}
#endif

/*
 * Handle an incoming HTTP request.
 *
 * This function processes the HTTP request:
 * 1. CORS preflight (OPTIONS) -> 204 No Content
 * 2. Non-POST methods -> 405 Method Not Allowed
 * 3. Request too large -> 413 error
 * 4. If poller not active -> Block and poll until poller becomes active
 * 5. Copy request to buffer, set s_has_request, poll-wait for s_has_response
 */
static void HandleHttpRequest(struct mg_connection* connection, struct mg_http_message* http_message)
{
    /* Handle CORS preflight request */
    if (mg_strcmp(http_message->method, mg_str("OPTIONS")) == 0)
    {
        mg_http_reply(connection, 204, CORS_HEADERS, "");
        return;
    }

    /* Only allow POST method for JSON-RPC */
    if (mg_strcmp(http_message->method, mg_str("POST")) != 0)
    {
        mg_http_reply(connection, 405,
            "Content-Type: text/plain\r\n"
            "Access-Control-Allow-Origin: *\r\n",
            "Method Not Allowed. Use POST for JSON-RPC requests.");
        return;
    }

    /* Extract request body */
    size_t body_length = http_message->body.len;
    if (body_length == 0)
    {
        mg_http_reply(connection, 400, CORS_HEADERS,
            "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32700,"
            "\"message\":\"Parse error: Empty request body.\"},\"id\":null}");
        return;
    }

    /* Reject requests larger than the buffer */
    if (body_length >= PROXY_MAX_REQUEST_SIZE)
    {
        mg_http_reply(connection, 200, CORS_HEADERS, "%s",
            BuildErrorResponse(-32600, "Request too large", "null"));
        return;
    }

    /* Copy request body to static buffer (no malloc) */
    memcpy(s_request_buffer, http_message->body.buf, body_length);
    s_request_buffer[body_length] = '\0';

    /* Extract the request ID for use in error responses */
    const char* request_id = ExtractJsonRpcId(s_request_buffer, body_length);

    /* Block and poll until poller becomes active (handles domain reload) */
    if (!s_poller_active)
    {
        uint64_t poll_start_time = mg_millis();
        while (!s_poller_active)
        {
            uint64_t elapsed_time = mg_millis() - poll_start_time;
            if (elapsed_time >= PROXY_REQUEST_TIMEOUT_MS)
            {
                mg_http_reply(connection, 200, CORS_HEADERS, "%s",
                    BuildErrorResponse(-32000, "Unity recompilation timed out.", request_id));
                return;
            }
            if (!s_running)
            {
                mg_http_reply(connection, 200, CORS_HEADERS, "%s",
                    BuildErrorResponse(-32000, "Server is shutting down.", request_id));
                return;
            }
            PROXY_SLEEP_MS(PROXY_RECOMPILE_POLL_INTERVAL_MS);
        }
    }

    /* Clear response state and signal request available */
    s_has_response = 0;
    s_response_buffer[0] = '\0';
    s_has_request = 1;

    /* Poll-wait for C# to process the request and call SendResponse() */
    {
        uint64_t wait_start_time = mg_millis();
        while (!s_has_response)
        {
            uint64_t elapsed_time = mg_millis() - wait_start_time;
            if (elapsed_time >= PROXY_REQUEST_TIMEOUT_MS)
            {
                s_has_request = 0;
                mg_http_reply(connection, 200, CORS_HEADERS, "%s",
                    BuildErrorResponse(-32000, "Request processing timed out.", request_id));
                return;
            }
            if (!s_running)
            {
                s_has_request = 0;
                mg_http_reply(connection, 200, CORS_HEADERS, "%s",
                    BuildErrorResponse(-32000, "Server is shutting down.", request_id));
                return;
            }
            if (!s_poller_active)
            {
                s_has_request = 0;
                mg_http_reply(connection, 200, CORS_HEADERS, "%s",
                    BuildErrorResponse(-32000, "Request interrupted by Unity domain reload. Please retry.", request_id));
                return;
            }
            PROXY_SLEEP_MS(1);
        }
    }

    /* Send the response */
    s_has_request = 0;
    mg_http_reply(connection, 200, CORS_HEADERS, "%s", s_response_buffer);
}

/*
 * Mongoose event handler for all connection events.
 */
static void EventHandler(struct mg_connection* connection, int event, void* event_data)
{
    if (event == MG_EV_HTTP_MSG)
    {
        struct mg_http_message* http_message = (struct mg_http_message*)event_data;
        HandleHttpRequest(connection, http_message);
    }
}

/*
 * Start the HTTP server on the specified port.
 */
EXPORT int StartServer(int port)
{
    if (s_running)
    {
        return 1;  /* Already running */
    }

    /* Reset unload flag */
    s_unloading = 0;

    /* Initialize the event manager */
    mg_mgr_init(&s_mgr);

    /* Build the listen address string */
    char listen_address[64];
    snprintf(listen_address, sizeof(listen_address), "http://0.0.0.0:%d", port);

    /* Start listening for HTTP connections */
    s_listener = mg_http_listen(&s_mgr, listen_address, EventHandler, NULL);
    if (s_listener == NULL)
    {
        mg_mgr_free(&s_mgr);
        return -1;  /* Failed to bind to port */
    }

    /* Set running flag before creating thread */
    s_running = 1;

    /* Create the server thread */
#ifdef _WIN32
    s_server_thread = CreateThread(NULL, 0, ServerThreadFunc, NULL, 0, NULL);
    if (s_server_thread == NULL)
    {
        s_running = 0;
        mg_mgr_free(&s_mgr);
        return -1;  /* Failed to create thread */
    }
#else
    if (pthread_create(&s_server_thread, NULL, ServerThreadFunc, NULL) != 0)
    {
        s_running = 0;
        mg_mgr_free(&s_mgr);
        return -1;  /* Failed to create thread */
    }
#endif

    return 0;
}

/*
 * Stop the HTTP server and release resources.
 */
EXPORT void StopServer(void)
{
    if (!s_running)
    {
        return;
    }

    /* Signal the thread to stop */
    s_running = 0;

    /* Wait for the server thread to exit */
#ifdef _WIN32
    if (s_server_thread != NULL)
    {
        WaitForSingleObject(s_server_thread, INFINITE);
        CloseHandle(s_server_thread);
        s_server_thread = NULL;
    }
#else
    pthread_join(s_server_thread, NULL);
#endif

    s_listener = NULL;
    s_poller_active = 0;
    s_has_request = 0;

    mg_mgr_free(&s_mgr);
}

/*
 * Activate or deactivate C# polling.
 */
EXPORT void SetPollingActive(int active)
{
    s_poller_active = active ? 1 : 0;

    if (!active)
    {
        /* Clear response state when deactivating */
        s_has_response = 0;
        s_response_buffer[0] = '\0';
    }
}

/*
 * Get the pending request body, if any.
 */
EXPORT const char* GetPendingRequest(void)
{
    if (s_has_request)
    {
        return s_request_buffer;
    }
    return NULL;
}

/*
 * Send a response back to the waiting HTTP request.
 * Note: Response size validation is handled by the C# layer which has access
 * to the request ID for proper JSON-RPC error responses.
 */
EXPORT void SendResponse(const char* json)
{
    if (json == NULL)
    {
        return;
    }

    size_t json_length = strlen(json);
    if (json_length >= PROXY_MAX_RESPONSE_SIZE)
    {
        /*
         * Response should have been validated by C# layer.
         * If we get here, truncate but this should not happen in normal operation.
         */
        strncpy(s_response_buffer, json, PROXY_MAX_RESPONSE_SIZE - 1);
        s_response_buffer[PROXY_MAX_RESPONSE_SIZE - 1] = '\0';
    }
    else
    {
        strcpy(s_response_buffer, json);
    }

    s_has_response = 1;
}

/*
 * Check if the server is currently running.
 */
EXPORT int IsServerRunning(void)
{
    return s_running;
}

/*
 * Check if C# polling is currently active.
 */
EXPORT int IsPollerActive(void)
{
    return s_poller_active;
}

/*
 * Get the process ID of this library instance.
 */
EXPORT unsigned long GetNativeProcessId(void)
{
    return GET_PROCESS_ID();
}

/*
 * DLL/shared library unload cleanup.
 *
 * When Unity reloads the plugin (e.g. package update), the old DLL is
 * unloaded while its server thread may still be running. Without cleanup, the
 * listen socket leaks and the new DLL can never bind to the same port.
 *
 * We signal the thread to stop and give it time to close sockets. We cannot
 * call WaitForSingleObject/pthread_join here (loader lock on Windows), so a
 * brief sleep lets the thread's 10ms poll loop notice and run cleanup.
 */
#ifdef _WIN32
BOOL WINAPI DllMain(HINSTANCE hinstDLL, DWORD fdwReason, LPVOID lpvReserved)
{
    (void)hinstDLL;
    (void)lpvReserved;

    if (fdwReason == DLL_PROCESS_DETACH && s_running)
    {
        s_unloading = 1;
        s_running = 0;
        Sleep(100);
    }
    return TRUE;
}
#else
__attribute__((destructor))
static void OnDllUnload(void)
{
    if (s_running)
    {
        s_unloading = 1;
        s_running = 0;
        usleep(100000); /* 100ms */
    }
}
#endif
