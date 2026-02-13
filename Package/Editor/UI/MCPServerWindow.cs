using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityMCP.Editor.Core;

namespace UnityMCP.Editor.UI
{
    /// <summary>
    /// Editor window for controlling the MCP server and viewing its status.
    /// Provides start/stop controls, port configuration, and displays registered tools.
    /// </summary>
    public class MCPServerWindow : EditorWindow
    {
        private Vector2 _toolListScrollPosition;
        private Vector2 _activityScrollPosition;
        private int _portInput;
        private string _lastError;
        private Dictionary<string, bool> _categoryFoldouts = new Dictionary<string, bool>();
        private bool _remoteAccessFoldout = false;
        private bool _activityFoldout = true;

        private const string DocumentationUrl = "https://github.com/anthropics/anthropic-cookbook/tree/main/misc/model_context_protocol";
        private const string VerboseLoggingPrefKey = "UnityMCP_VerboseLogging";
        private const string ActivityDetailPrefKey = "UnityMCP_ActivityDetail";

        [MenuItem("Window/Unity MCP")]
        public static void ShowWindow()
        {
            var window = GetWindow<MCPServerWindow>("Unity MCP");
            window.minSize = new Vector2(300, 400);
        }

        private void OnEnable()
        {
            _portInput = MCPServer.Instance.Port;
            MCPProxy.VerboseLogging = EditorPrefs.GetBool(VerboseLoggingPrefKey, false);
            ActivityLog.OnEntryAdded += OnActivityEntryAdded;
        }

        private void OnDisable()
        {
            ActivityLog.OnEntryAdded -= OnActivityEntryAdded;
        }

        private void OnActivityEntryAdded()
        {
            Repaint();
        }

        /// <summary>
        /// Called 10 times per second while the window is visible.
        /// Used to refresh the server status display.
        /// </summary>
        private void OnInspectorUpdate()
        {
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();

            EditorGUILayout.Space(8);

            DrawServerInfo();

            EditorGUILayout.Space(8);

            DrawPortConfiguration();

            if (!string.IsNullOrEmpty(_lastError))
            {
                DrawErrorMessage();
            }

            EditorGUILayout.Space(12);

            DrawRemoteAccessSection();

            EditorGUILayout.Space(12);

            DrawActivitySection();

            EditorGUILayout.Space(12);

            DrawToolsSection();
        }

        private void DrawToolbar()
        {
            bool isRunning = MCPProxy.IsInitialized;

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // Status indicator
            string statusText = isRunning ? "\u25CF Running" : "\u25CB Stopped";

            GUI.color = isRunning ? Color.green : Color.gray;
            GUILayout.Label(statusText, EditorStyles.boldLabel, GUILayout.Width(140));
            GUI.color = Color.white;

            GUILayout.FlexibleSpace();

            // Help button
            if (GUILayout.Button("?", EditorStyles.toolbarButton, GUILayout.Width(24)))
            {
                Application.OpenURL(DocumentationUrl);
            }

            // Start/Stop button
            if (GUILayout.Button(isRunning ? "Stop" : "Start", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                ToggleServer(isRunning);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void ToggleServer(bool isCurrentlyRunning)
        {
            _lastError = null;

            try
            {
                if (isCurrentlyRunning)
                {
                    MCPProxy.Stop();
                }
                else
                {
                    MCPProxy.Start();
                }
            }
            catch (Exception exception)
            {
                _lastError = exception.Message;
                Debug.LogError($"[MCPServerWindow] Failed to {(isCurrentlyRunning ? "stop" : "start")} server: {exception.Message}");
            }
        }

        private void DrawServerInfo()
        {
            bool isRunning = MCPProxy.IsInitialized;
            int port = MCPServer.Instance?.Port ?? 8080;

            string endpoint;
            if (MCPProxy.RemoteAccessEnabled)
            {
                string lanIp = NetworkUtils.GetLanIpAddress();
                endpoint = $"https://{lanIp}:{port}/";
            }
            else
            {
                endpoint = $"http://localhost:{port}/";
            }

            EditorGUILayout.LabelField("Server Information", EditorStyles.boldLabel);

            EditorGUI.indentLevel++;

            // Endpoint with copy button
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Endpoint", endpoint);
            if (GUILayout.Button("Copy", GUILayout.Width(50)))
            {
                EditorGUIUtility.systemCopyBuffer = endpoint;
                if (MCPProxy.VerboseLogging) Debug.Log($"[MCPServerWindow] Copied endpoint to clipboard: {endpoint}");
            }
            EditorGUILayout.EndHorizontal();

            // Tool and resource counts
            int toolCount = ToolRegistry.Count;
            int resourceCount = ResourceRegistry.Count;
            EditorGUILayout.LabelField("Tools", toolCount.ToString());
            EditorGUILayout.LabelField("Resources", resourceCount.ToString());

            EditorGUI.indentLevel--;
        }

        private void DrawPortConfiguration()
        {
            bool isRunning = MCPProxy.IsInitialized;

            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);

            EditorGUI.indentLevel++;

            EditorGUI.BeginDisabledGroup(isRunning);

            EditorGUILayout.BeginHorizontal();
            _portInput = EditorGUILayout.IntField("Port", _portInput);

            bool portChanged = _portInput != MCPServer.Instance.Port;
            EditorGUI.BeginDisabledGroup(!portChanged || isRunning);
            if (GUILayout.Button("Apply", GUILayout.Width(50)))
            {
                if (_portInput > 0 && _portInput <= 65535)
                {
                    MCPServer.Instance.Port = _portInput;
                    if (MCPProxy.VerboseLogging) Debug.Log($"[MCPServerWindow] Port changed to {_portInput}");
                }
                else
                {
                    _lastError = "Port must be between 1 and 65535";
                }
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (isRunning)
            {
                EditorGUILayout.HelpBox("Stop the server to change the port.", MessageType.Info);
            }

            EditorGUI.EndDisabledGroup();

            // Verbose logging toggle (always enabled)
            EditorGUILayout.Space(4);
            bool verboseLogging = EditorGUILayout.Toggle("Verbose Logging", MCPProxy.VerboseLogging);
            if (verboseLogging != MCPProxy.VerboseLogging)
            {
                MCPProxy.VerboseLogging = verboseLogging;
                EditorPrefs.SetBool(VerboseLoggingPrefKey, verboseLogging);
            }

            EditorGUI.indentLevel--;
        }

        private void DrawErrorMessage()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(_lastError, MessageType.Error);
        }

        private void DrawRemoteAccessSection()
        {
            _remoteAccessFoldout = EditorGUILayout.Foldout(_remoteAccessFoldout, "Remote Access", true, EditorStyles.foldoutHeader);

            if (!_remoteAccessFoldout)
                return;

            EditorGUI.indentLevel++;

            // Enable toggle
            bool remoteEnabled = MCPProxy.RemoteAccessEnabled;
            bool newRemoteEnabled = EditorGUILayout.Toggle("Enable Remote Access", remoteEnabled);
            if (newRemoteEnabled != remoteEnabled)
            {
                MCPProxy.RemoteAccessEnabled = newRemoteEnabled;
                MCPProxy.Restart();
            }

            if (MCPProxy.RemoteAccessEnabled)
            {
                EditorGUILayout.Space(4);

                // API Key display
                EditorGUILayout.BeginHorizontal();
                string apiKey = MCPProxy.ApiKey;
                string displayKey = string.IsNullOrEmpty(apiKey)
                    ? "(none)"
                    : (apiKey.Length > 20 ? apiKey.Substring(0, 20) + "..." : apiKey);
                EditorGUILayout.LabelField("API Key", displayKey);

                if (GUILayout.Button("Copy", GUILayout.Width(50)))
                {
                    EditorGUIUtility.systemCopyBuffer = apiKey;
                }
                if (GUILayout.Button("Regenerate", GUILayout.Width(80)))
                {
                    MCPProxy.ApiKey = MCPProxy.GenerateApiKey();
                    MCPProxy.Restart();
                }
                EditorGUILayout.EndHorizontal();

                // TLS status
                string tlsStatus;
                if (!MCPProxy.IsTlsSupported)
                {
                    tlsStatus = "Not available (native proxy compiled without TLS)";
                }
                else
                {
                    string certDir = CertificateGenerator.GetCertDirectory();
                    var expiry = CertificateGenerator.GetCertificateExpiry(certDir);
                    if (expiry.HasValue)
                        tlsStatus = "Active (self-signed, expires " + expiry.Value.ToString("yyyy-MM-dd") + ")";
                    else
                        tlsStatus = "No certificate";
                }
                EditorGUILayout.LabelField("TLS", tlsStatus);

                // Endpoint
                int port = MCPServer.Instance?.Port ?? 8080;
                string lanIp = NetworkUtils.GetLanIpAddress();
                EditorGUILayout.LabelField("Endpoint", $"https://{lanIp}:{port}/");

                EditorGUILayout.Space(4);

                // Warning
                EditorGUILayout.HelpBox(
                    "Remote access binds to all network interfaces with TLS encryption and API key authentication. " +
                    "Ensure your firewall is configured appropriately.",
                    MessageType.Warning);
            }

            EditorGUI.indentLevel--;
        }

        private void DrawActivitySection()
        {
            EditorGUILayout.BeginHorizontal();
            _activityFoldout = EditorGUILayout.Foldout(_activityFoldout, "Recent Activity", true, EditorStyles.foldoutHeader);

            GUILayout.FlexibleSpace();

            // Detail toggle
            bool showDetail = EditorPrefs.GetBool(ActivityDetailPrefKey, false);
            bool newShowDetail = GUILayout.Toggle(showDetail, "Detail", EditorStyles.miniButton, GUILayout.Width(50));
            if (newShowDetail != showDetail)
                EditorPrefs.SetBool(ActivityDetailPrefKey, newShowDetail);

            if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(40)))
                ActivityLog.Clear();

            EditorGUILayout.EndHorizontal();

            if (!_activityFoldout)
                return;

            var entries = ActivityLog.Entries;
            if (entries.Count == 0)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("No activity recorded yet.", EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
                return;
            }

            _activityScrollPosition = EditorGUILayout.BeginScrollView(
                _activityScrollPosition, GUILayout.MaxHeight(160));

            // Show newest first
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                string time = entry.timestamp.ToString("HH:mm:ss");
                string status = entry.success ? "OK" : "FAIL";
                string line;

                if (newShowDetail && !string.IsNullOrEmpty(entry.detail))
                    line = $"[{time}] {entry.toolName} ({entry.detail}) \u2192 {status}";
                else
                    line = $"[{time}] {entry.toolName} \u2192 {status}";

                if (!entry.success)
                    GUI.color = new Color(1f, 0.6f, 0.6f);

                EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
                GUI.color = Color.white;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawToolsSection()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Registered Tools", EditorStyles.boldLabel);

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Refresh", GUILayout.Width(60)))
            {
                RefreshTools();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // Tool list with scroll view, grouped by category
            var toolsByCategory = ToolRegistry.GetDefinitionsByCategory().ToList();

            if (toolsByCategory.Count == 0)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("No tools registered", EditorStyles.miniLabel);
                EditorGUILayout.HelpBox(
                    "Create tools by adding [MCPTool] attribute to static methods.\n\n" +
                    "Example:\n" +
                    "[MCPTool(\"my_tool\", \"Description of my tool\")]\n" +
                    "public static string MyTool([MCPParam(\"param\", \"Description\")] string param) { ... }",
                    MessageType.Info);
                EditorGUI.indentLevel--;
            }
            else
            {
                _toolListScrollPosition = EditorGUILayout.BeginScrollView(
                    _toolListScrollPosition,
                    GUILayout.ExpandHeight(true));

                foreach (var group in toolsByCategory)
                {
                    DrawCategoryFoldout(group.Key, group.ToList());
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawCategoryFoldout(string category, List<ToolDefinition> tools)
        {
            // Initialize foldout state if needed (default to expanded)
            if (!_categoryFoldouts.ContainsKey(category))
                _categoryFoldouts[category] = true;

            // Draw foldout header with tool count
            string header = $"{category} ({tools.Count})";
            _categoryFoldouts[category] = EditorGUILayout.Foldout(
                _categoryFoldouts[category], header, true, EditorStyles.foldoutHeader);

            // Draw tools if expanded
            if (_categoryFoldouts[category])
            {
                EditorGUI.indentLevel++;
                foreach (var tool in tools)
                    DrawToolEntry(tool);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawToolEntry(ToolDefinition tool)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Tool name
            EditorGUILayout.LabelField(tool.name, EditorStyles.boldLabel);

            // Tool description
            if (!string.IsNullOrEmpty(tool.description))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField(tool.description, EditorStyles.wordWrappedMiniLabel);
                EditorGUI.indentLevel--;
            }

            // Parameter count
            if (tool.inputSchema != null && tool.inputSchema.properties.Count > 0)
            {
                EditorGUI.indentLevel++;
                int requiredCount = tool.inputSchema.required?.Count ?? 0;
                int totalCount = tool.inputSchema.properties.Count;
                string paramInfo = requiredCount > 0
                    ? $"{totalCount} parameter(s), {requiredCount} required"
                    : $"{totalCount} parameter(s)";
                EditorGUILayout.LabelField(paramInfo, EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        private void RefreshTools()
        {
            _lastError = null;

            try
            {
                ToolRegistry.RefreshTools();
                if (MCPProxy.VerboseLogging) Debug.Log("[MCPServerWindow] Tools refreshed");
            }
            catch (Exception exception)
            {
                _lastError = $"Failed to refresh tools: {exception.Message}";
                Debug.LogError($"[MCPServerWindow] {_lastError}");
            }
        }
    }
}
