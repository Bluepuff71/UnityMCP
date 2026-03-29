/*
 * UnityMCP Proxy - Implementation
 *
 * HTTP server plugin that survives Unity domain reloads.
 * Acts as a proxy between external MCP clients and Unity's C# code.
 *
 * Supports up to PROXY_MAX_SLOTS concurrent HTTP connections using a
 * non-blocking poll-check model. Each incoming request is assigned a slot,
 * and the Mongoose poll loop checks for completed responses to send back.
 *
 * Threading model:
 *   - Mongoose thread: runs the event loop, handles HTTP I/O, checks slot states
 *   - C# main thread: calls GetNextRequest/SendResponseForSlot
 *   - Synchronization: volatile int state field on each slot
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
    #define MEMORY_BARRIER() MemoryBarrier()
#else
    #include <pthread.h>
    #include <unistd.h>
    typedef pthread_t ThreadHandle;
    #define PROXY_SLEEP_MS(ms) usleep((ms) * 1000)
    #define GET_PROCESS_ID() ((unsigned long)getpid())
    #define MEMORY_BARRIER() __sync_synchronize()
#endif

/*
 * Internal state
 */
static struct mg_mgr s_mgr;
static struct mg_connection* s_listener = NULL;
static volatile int s_running = 0;
static volatile int s_poller_active = 0;
static ThreadHandle s_server_thread;

/* Remote access configuration (set before StartServer) */
static char s_bind_address[64] = "127.0.0.1";
static char s_api_key[256] = "";
static char s_tls_cert[8192] = "";
static char s_tls_key[8192] = "";
static int  s_tls_enabled = 0;

/* Request slots for concurrent connection handling */
static RequestSlot s_slots[PROXY_MAX_SLOTS];
static int s_session_counter = 0;

/* Flag set by DllMain/destructor to signal the server thread to exit and clean up */
static volatile int s_unloading = 0;


/*
 * Buffer for building dynamic error responses with request ID.
 * Only used within the Mongoose thread.
 */
static char s_error_response_buffer[1024];

/*
 * Extract the "id" field from a JSON-RPC request.
 * Returns a pointer to a static buffer containing the id value (including quotes for strings),
 * or "null" if not found or on parse error.
 * Only used within the Mongoose thread.
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
 * Build a JSON-RPC error response with an additional data object for recovery guidance.
 * data_json must be a pre-formatted JSON object string (e.g. {"recoverable":true}).
 */
static const char* BuildErrorResponseWithData(int code, const char* message, const char* data_json, const char* id)
{
    snprintf(s_error_response_buffer, sizeof(s_error_response_buffer),
        "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":%d,\"message\":\"%s\",\"data\":%s},\"id\":%s}",
        code, message, data_json, id);
    return s_error_response_buffer;
}

/*
 * Build an error response directly into a slot's response buffer.
 * Used when we need to write errors for slots without clobbering the shared s_error_response_buffer.
 */
static void BuildSlotErrorResponse(RequestSlot* slot, int code, const char* message, const char* data_json)
{
    /* Extract request ID from the slot's request body */
    const char* request_id = ExtractJsonRpcId(slot->request, strlen(slot->request));

    if (data_json != NULL)
    {
        snprintf(slot->response, PROXY_MAX_RESPONSE_SIZE,
            "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":%d,\"message\":\"%s\",\"data\":%s},\"id\":%s}",
            code, message, data_json, request_id);
    }
    else
    {
        snprintf(slot->response, PROXY_MAX_RESPONSE_SIZE,
            "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":%d,\"message\":\"%s\"},\"id\":%s}",
            code, message, request_id);
    }
}

/*
 * CORS headers: local mode includes Access-Control-Allow-Origin: *,
 * remote mode omits it to prevent cross-origin requests from arbitrary websites.
 */
static const char* CORS_HEADERS_LOCAL =
    "Content-Type: application/json\r\n"
    "Access-Control-Allow-Origin: *\r\n"
    "Access-Control-Allow-Methods: POST, OPTIONS\r\n"
    "Access-Control-Allow-Headers: Content-Type, Authorization, Mcp-Session-Id\r\n";

static const char* CORS_HEADERS_REMOTE =
    "Content-Type: application/json\r\n"
    "Access-Control-Allow-Methods: POST, OPTIONS\r\n"
    "Access-Control-Allow-Headers: Content-Type, Authorization, Mcp-Session-Id\r\n";

static const char* GetCorsHeaders(void)
{
    return (s_api_key[0] != '\0') ? CORS_HEADERS_REMOTE : CORS_HEADERS_LOCAL;
}

/*
 * Generate a unique session ID for a new MCP session.
 * Format: umcp_{pid}_{counter}_{timestamp}
 */
static void GenerateSessionId(char* out, size_t out_size)
{
    snprintf(out, out_size, "umcp_%lu_%d_%lu",
        GET_PROCESS_ID(), ++s_session_counter, (unsigned long)mg_millis());
}

/*
 * Find a free slot (state == SLOT_STATE_EMPTY).
 * Returns a pointer to the slot, or NULL if all slots are occupied.
 * Only called from the Mongoose thread.
 */
static RequestSlot* FindFreeSlot(void)
{
    int i;
    for (i = 0; i < PROXY_MAX_SLOTS; i++)
    {
        if (s_slots[i].state == SLOT_STATE_EMPTY)
        {
            return &s_slots[i];
        }
    }
    return NULL;
}

/*
 * Clear all slots to empty state.
 */
static void ClearAllSlots(void)
{
    int i;
    for (i = 0; i < PROXY_MAX_SLOTS; i++)
    {
        s_slots[i].state = SLOT_STATE_EMPTY;
    }
}

/*
 * Initialize all slots with their IDs.
 */
static void InitSlots(void)
{
    int i;
    for (i = 0; i < PROXY_MAX_SLOTS; i++)
    {
        memset(&s_slots[i], 0, sizeof(RequestSlot));
        s_slots[i].slot_id = i;
    }
}

/*
 * Build response headers including the session ID.
 * Returns a pointer to a static buffer (only used from Mongoose thread).
 */
static char s_response_headers[512];
static const char* BuildResponseHeaders(const char* session_id)
{
    snprintf(s_response_headers, sizeof(s_response_headers),
        "%sMcp-Session-Id: %s\r\n", GetCorsHeaders(), session_id);
    return s_response_headers;
}

/*
 * Handle an incoming HTTP request (non-blocking).
 *
 * This function processes the HTTP request:
 * 1. CORS preflight (OPTIONS) -> 204 No Content
 * 2. Non-POST methods -> 405 Method Not Allowed
 * 3. Validate auth, body size
 * 4. Find a free slot, copy request + session ID, set state=pending
 * 5. Store slot pointer in connection->fn_data for poll-check
 * 6. Return immediately (do NOT block)
 */
static void HandleHttpRequest(struct mg_connection* connection, struct mg_http_message* http_message)
{
    /* Handle CORS preflight request */
    if (mg_strcmp(http_message->method, mg_str("OPTIONS")) == 0)
    {
        mg_http_reply(connection, 204, GetCorsHeaders(), "");
        return;
    }

    /* Only allow POST method for JSON-RPC */
    if (mg_strcmp(http_message->method, mg_str("POST")) != 0)
    {
        mg_http_reply(connection, 405,
            (s_api_key[0] != '\0')
                ? "Content-Type: text/plain\r\n"
                : "Content-Type: text/plain\r\nAccess-Control-Allow-Origin: *\r\n",
            "Method Not Allowed. Use POST for JSON-RPC requests.");
        return;
    }

    /* Validate API key if configured */
    if (s_api_key[0] != '\0')
    {
        struct mg_str *auth_header = mg_http_get_header(http_message, "Authorization");
        size_t key_len = strlen(s_api_key);
        int valid = 0;

        if (auth_header != NULL && auth_header->len >= 7 + key_len &&
            strncmp(auth_header->buf, "Bearer ", 7) == 0 &&
            (auth_header->len - 7) == key_len)
        {
            /* Constant-time comparison to prevent timing attacks */
            volatile unsigned char result = 0;
            const char *a = auth_header->buf + 7;
            const char *b = s_api_key;
            size_t i;
            for (i = 0; i < key_len; i++)
            {
                result |= (unsigned char)(a[i] ^ b[i]);
            }
            valid = (result == 0);
        }

        if (!valid)
        {
            mg_http_reply(connection, 401, GetCorsHeaders(),
                "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32000,"
                "\"message\":\"Unauthorized: invalid or missing API key\"},\"id\":null}");
            return;
        }
    }

    /* Extract request body */
    size_t body_length = http_message->body.len;
    if (body_length == 0)
    {
        mg_http_reply(connection, 400, GetCorsHeaders(),
            "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32700,"
            "\"message\":\"Parse error: Empty request body.\"},\"id\":null}");
        return;
    }

    /* Reject requests larger than the buffer */
    if (body_length >= PROXY_MAX_REQUEST_SIZE)
    {
        mg_http_reply(connection, 200, GetCorsHeaders(), "%s",
            BuildErrorResponse(-32600, "Request too large", "null"));
        return;
    }

    /* Find a free slot */
    RequestSlot* slot = FindFreeSlot();
    if (slot == NULL)
    {
        /* Extract request ID for the error response */
        /* We need a temporary buffer since we can't use the slot */
        char temp_body[512];
        size_t copy_len = body_length < sizeof(temp_body) - 1 ? body_length : sizeof(temp_body) - 1;
        memcpy(temp_body, http_message->body.buf, copy_len);
        temp_body[copy_len] = '\0';
        const char* request_id = ExtractJsonRpcId(temp_body, copy_len);

        mg_http_reply(connection, 200, GetCorsHeaders(), "%s",
            BuildErrorResponse(-32000, "Server busy: max concurrent requests reached", request_id));
        return;
    }

    /* Copy request body into slot */
    memcpy(slot->request, http_message->body.buf, body_length);
    slot->request[body_length] = '\0';

    /* Extract or generate session ID */
    struct mg_str *session_header = mg_http_get_header(http_message, "Mcp-Session-Id");
    if (session_header != NULL && session_header->len > 0 && session_header->len < PROXY_SESSION_ID_SIZE)
    {
        memcpy(slot->session_id, session_header->buf, session_header->len);
        slot->session_id[session_header->len] = '\0';
    }
    else
    {
        GenerateSessionId(slot->session_id, sizeof(slot->session_id));
    }

    /* Initialize slot timing */
    slot->enqueue_time = mg_millis();
    slot->response[0] = '\0';

    /* Memory barrier: ensure all slot data is written before state becomes visible */
    MEMORY_BARRIER();

    /* Mark slot as pending — C# can now pick it up */
    slot->state = SLOT_STATE_PENDING;

    /* Store slot pointer in connection for poll-check */
    connection->fn_data = slot;
}

/*
 * Check pending responses on poll events (non-blocking).
 * Called from the Mongoose thread on every MG_EV_POLL (~10ms).
 *
 * For each connection with a pending slot (fn_data != NULL), checks:
 * - Response ready (state == responded): send HTTP reply, free slot
 * - Timeout: write timeout error, send it, free slot
 * - Poller inactive (domain reload): write error, send it, free slot
 * - Server shutting down: write error, send it, free slot
 */
static void CheckPendingResponse(struct mg_connection* connection)
{
    RequestSlot* slot = (RequestSlot*)connection->fn_data;
    if (slot == NULL)
    {
        return;
    }

    /* Read state with barrier to see latest value from C# thread */
    MEMORY_BARRIER();
    int current_state = slot->state;

    if (current_state == SLOT_STATE_RESPONDED)
    {
        /* Response is ready — send it back */
        mg_http_reply(connection, 200, BuildResponseHeaders(slot->session_id), "%s", slot->response);
        slot->state = SLOT_STATE_EMPTY;
        connection->fn_data = NULL;
    }
    else if (!s_running)
    {
        /* Server is shutting down */
        BuildSlotErrorResponse(slot, -32000, "Server is shutting down.", NULL);
        mg_http_reply(connection, 200, BuildResponseHeaders(slot->session_id), "%s", slot->response);
        slot->state = SLOT_STATE_EMPTY;
        connection->fn_data = NULL;
    }
    else if (!s_poller_active && current_state == SLOT_STATE_PENDING)
    {
        /* Poller deactivated (domain reload) and C# hasn't picked it up yet */
        BuildSlotErrorResponse(slot, -32000, "Request interrupted by Unity domain reload. Please retry.",
            "{\"recoverable\":true,\"retryAfterMs\":2000,\"reason\":\"domain_reload\"}");
        mg_http_reply(connection, 200, BuildResponseHeaders(slot->session_id), "%s", slot->response);
        slot->state = SLOT_STATE_EMPTY;
        connection->fn_data = NULL;
    }
    else if (mg_millis() - slot->enqueue_time >= PROXY_REQUEST_TIMEOUT_MS)
    {
        /* Request timed out */
        if (current_state == SLOT_STATE_PENDING)
        {
            BuildSlotErrorResponse(slot, -32000, "Unity recompilation timed out.",
                "{\"recoverable\":true,\"retryAfterMs\":5000,\"reason\":\"recompilation\"}");
        }
        else
        {
            BuildSlotErrorResponse(slot, -32000, "Request processing timed out.",
                "{\"recoverable\":true,\"retryAfterMs\":2000,\"reason\":\"timeout\"}");
        }
        mg_http_reply(connection, 200, BuildResponseHeaders(slot->session_id), "%s", slot->response);
        slot->state = SLOT_STATE_EMPTY;
        connection->fn_data = NULL;
    }
}

/*
 * Handle connection close.
 * If the connection had an assigned slot that hasn't been responded to yet,
 * free the slot so it can be reused.
 */
static void HandleConnectionClose(struct mg_connection* connection)
{
    RequestSlot* slot = (RequestSlot*)connection->fn_data;
    if (slot != NULL)
    {
        slot->state = SLOT_STATE_EMPTY;
        connection->fn_data = NULL;
    }
}

/*
 * Mongoose event handler for all connection events.
 */
static void EventHandler(struct mg_connection* connection, int event, void* event_data)
{
    if (event == MG_EV_ACCEPT && s_tls_enabled)
    {
        struct mg_tls_opts opts;
        memset(&opts, 0, sizeof(opts));
        opts.cert = mg_str(s_tls_cert);
        opts.key = mg_str(s_tls_key);
        mg_tls_init(connection, &opts);
    }
    else if (event == MG_EV_HTTP_MSG)
    {
        struct mg_http_message* http_message = (struct mg_http_message*)event_data;
        HandleHttpRequest(connection, http_message);
    }
    else if (event == MG_EV_POLL)
    {
        CheckPendingResponse(connection);
    }
    else if (event == MG_EV_CLOSE)
    {
        HandleConnectionClose(connection);
    }
}

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
        ClearAllSlots();
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
        ClearAllSlots();
        mg_mgr_free(&s_mgr);
    }
    return NULL;
}
#endif

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

    /* Initialize slots */
    InitSlots();

    /* Initialize the event manager */
    mg_mgr_init(&s_mgr);

    /* Build the listen address string */
    char listen_address[128];
    if (s_tls_enabled && s_tls_cert[0] && s_tls_key[0])
        snprintf(listen_address, sizeof(listen_address), "https://%s:%d", s_bind_address, port);
    else
        snprintf(listen_address, sizeof(listen_address), "http://%s:%d", s_bind_address, port);

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
    ClearAllSlots();

    mg_mgr_free(&s_mgr);
}

/*
 * Activate or deactivate C# polling.
 *
 * When deactivating (domain reload), write error responses into any
 * pending/processing slots so the Mongoose poll loop can send them
 * and free the connections.
 */
EXPORT void SetPollingActive(int active)
{
    s_poller_active = active ? 1 : 0;

    if (!active)
    {
        /*
         * Write domain-reload error responses into pending/processing slots.
         * The Mongoose poll loop (CheckPendingResponse) will send these
         * and transition the slots back to empty.
         */
        int i;
        for (i = 0; i < PROXY_MAX_SLOTS; i++)
        {
            int slot_state = s_slots[i].state;
            if (slot_state == SLOT_STATE_PENDING || slot_state == SLOT_STATE_PROCESSING)
            {
                BuildSlotErrorResponse(&s_slots[i], -32000,
                    "Request interrupted by Unity domain reload. Please retry.",
                    "{\"recoverable\":true,\"retryAfterMs\":2000,\"reason\":\"domain_reload\"}");
                MEMORY_BARRIER();
                s_slots[i].state = SLOT_STATE_RESPONDED;
            }
        }
    }
}

/*
 * Get the next pending request.
 * Iterates slots, finds the first with state==pending, atomically transitions
 * it to processing, and copies the request JSON and session ID to output buffers.
 *
 * Called from the C# main thread.
 */
EXPORT int GetNextRequest(char* outJson, int outJsonSize, char* outSessionId, int outSessionIdSize)
{
    if (outJson == NULL || outJsonSize <= 0)
    {
        return -1;
    }

    int i;
    for (i = 0; i < PROXY_MAX_SLOTS; i++)
    {
        if (s_slots[i].state == SLOT_STATE_PENDING)
        {
            /*
             * Claim this slot: pending -> processing.
             * Thread safety invariant: this function is ONLY called from the C# main thread
             * (single caller). The Mongoose thread never writes pending->processing.
             * SetPollingActive(0) can write to pending slots from the main thread, but
             * it and GetNextRequest are never called concurrently (both run on main thread).
             */
            s_slots[i].state = SLOT_STATE_PROCESSING;
            MEMORY_BARRIER();

            /* Copy request JSON */
            strncpy(outJson, s_slots[i].request, outJsonSize - 1);
            outJson[outJsonSize - 1] = '\0';

            /* Copy session ID */
            if (outSessionId != NULL && outSessionIdSize > 0)
            {
                strncpy(outSessionId, s_slots[i].session_id, outSessionIdSize - 1);
                outSessionId[outSessionIdSize - 1] = '\0';
            }

            return s_slots[i].slot_id;
        }
    }

    return -1;
}

/*
 * Send a response for a specific slot.
 * Copies the response JSON into the slot buffer and sets state to responded.
 * The Mongoose poll loop will pick it up and send the HTTP reply.
 *
 * Called from the C# main thread.
 */
EXPORT void SendResponseForSlot(int slotId, const char* json)
{
    if (slotId < 0 || slotId >= PROXY_MAX_SLOTS)
    {
        return;
    }

    if (json == NULL)
    {
        return;
    }

    RequestSlot* slot = &s_slots[slotId];

    size_t json_length = strlen(json);
    if (json_length >= PROXY_MAX_RESPONSE_SIZE)
    {
        /*
         * Response should have been validated by C# layer.
         * If we get here, truncate but this should not happen in normal operation.
         */
        strncpy(slot->response, json, PROXY_MAX_RESPONSE_SIZE - 1);
        slot->response[PROXY_MAX_RESPONSE_SIZE - 1] = '\0';
    }
    else
    {
        strcpy(slot->response, json);
    }

    /* Memory barrier: ensure response data is written before state change is visible */
    MEMORY_BARRIER();

    slot->state = SLOT_STATE_RESPONDED;
}

/*
 * Get the number of active (non-empty) slots.
 */
EXPORT int GetQueueDepth(void)
{
    int count = 0;
    int i;
    for (i = 0; i < PROXY_MAX_SLOTS; i++)
    {
        if (s_slots[i].state != SLOT_STATE_EMPTY)
        {
            count++;
        }
    }
    return count;
}

/*
 * Get the number of distinct session IDs across active slots.
 */
EXPORT int GetActiveSessionCount(void)
{
    /* Collect unique session IDs from non-empty slots */
    char seen_sessions[PROXY_MAX_SLOTS][PROXY_SESSION_ID_SIZE];
    int unique_count = 0;
    int i, j;

    for (i = 0; i < PROXY_MAX_SLOTS; i++)
    {
        if (s_slots[i].state == SLOT_STATE_EMPTY)
        {
            continue;
        }

        if (s_slots[i].session_id[0] == '\0')
        {
            continue;
        }

        /* Check if we've already seen this session ID */
        int already_seen = 0;
        for (j = 0; j < unique_count; j++)
        {
            if (strcmp(seen_sessions[j], s_slots[i].session_id) == 0)
            {
                already_seen = 1;
                break;
            }
        }

        if (!already_seen && unique_count < PROXY_MAX_SLOTS)
        {
            strncpy(seen_sessions[unique_count], s_slots[i].session_id, PROXY_SESSION_ID_SIZE - 1);
            seen_sessions[unique_count][PROXY_SESSION_ID_SIZE - 1] = '\0';
            unique_count++;
        }
    }

    return unique_count;
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
 * Configure the bind address for the server.
 * Must be called before StartServer(). Defaults to "127.0.0.1".
 */
EXPORT void ConfigureBindAddress(const char* address)
{
    if (address == NULL) return;
    strncpy(s_bind_address, address, sizeof(s_bind_address) - 1);
    s_bind_address[sizeof(s_bind_address) - 1] = '\0';
}

/*
 * Configure the API key for bearer token authentication.
 * Pass an empty string to disable authentication.
 */
EXPORT void ConfigureApiKey(const char* key)
{
    if (key == NULL)
    {
        s_api_key[0] = '\0';
        return;
    }
    strncpy(s_api_key, key, sizeof(s_api_key) - 1);
    s_api_key[sizeof(s_api_key) - 1] = '\0';
}

/*
 * Configure TLS with PEM-encoded certificate and private key.
 * Both must be provided to enable TLS.
 */
EXPORT void ConfigureTls(const char* cert_pem, const char* key_pem)
{
    if (cert_pem == NULL || key_pem == NULL ||
        cert_pem[0] == '\0' || key_pem[0] == '\0')
    {
        s_tls_enabled = 0;
        s_tls_cert[0] = '\0';
        s_tls_key[0] = '\0';
        return;
    }
    strncpy(s_tls_cert, cert_pem, sizeof(s_tls_cert) - 1);
    s_tls_cert[sizeof(s_tls_cert) - 1] = '\0';
    strncpy(s_tls_key, key_pem, sizeof(s_tls_key) - 1);
    s_tls_key[sizeof(s_tls_key) - 1] = '\0';
    s_tls_enabled = 1;
}

/*
 * Check if the native proxy was compiled with TLS support.
 */
EXPORT int GetTlsSupported(void)
{
#if MG_TLS != MG_TLS_NONE
    return 1;
#else
    return 0;
#endif
}

/*
 * DLL/shared library unload cleanup.
 *
 * Called when the process exits (DLL_PROCESS_DETACH / destructor).
 * Note: Unity never unloads native plugins during the editor session —
 * they persist until the editor process exits.
 *
 * We signal the server thread to stop and give it time to close sockets.
 * We cannot call WaitForSingleObject/pthread_join here (loader lock on
 * Windows), so a brief sleep lets the thread's 10ms poll loop notice
 * and run cleanup.
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
