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
        private int _portInput;
        private string _lastError;
        private Dictionary<string, bool> _categoryFoldouts = new Dictionary<string, bool>();

        private const string DocumentationUrl = "https://github.com/anthropics/anthropic-cookbook/tree/main/misc/model_context_protocol";

        [MenuItem("Window/Unity MCP")]
        public static void ShowWindow()
        {
            var window = GetWindow<MCPServerWindow>("Unity MCP");
            window.minSize = new Vector2(300, 400);
        }

        private void OnEnable()
        {
            _portInput = MCPServer.Instance.Port;
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

            DrawToolsSection();
        }

        private void DrawToolbar()
        {
            // Server is running if either native proxy or managed server is active
            bool isRunning = NativeProxy.IsInitialized || (MCPServer.Instance?.IsRunning ?? false);

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // Status indicator
            string statusText;
            if (NativeProxy.IsInitialized)
            {
                statusText = "\u25CF Running - Native";
            }
            else if (MCPServer.Instance?.IsRunning ?? false)
            {
                statusText = "\u25CF Running - Fallback";
            }
            else
            {
                statusText = "\u25CB Stopped";
            }

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
                    // Stop both native proxy and managed server
                    NativeProxy.Stop();
                    MCPServer.Instance.Stop();
                }
                else
                {
                    // Try native proxy first, then fall back to managed server
                    NativeProxy.Start();
                    MCPServer.Instance.Start();
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
            bool isRunning = NativeProxy.IsInitialized || (MCPServer.Instance?.IsRunning ?? false);
            int port = MCPServer.Instance?.Port ?? 8080;
            string endpoint = $"http://localhost:{port}/";

            EditorGUILayout.LabelField("Server Information", EditorStyles.boldLabel);

            EditorGUI.indentLevel++;

            // Endpoint with copy button
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Endpoint", endpoint);
            if (GUILayout.Button("Copy", GUILayout.Width(50)))
            {
                EditorGUIUtility.systemCopyBuffer = endpoint;
                Debug.Log($"[MCPServerWindow] Copied endpoint to clipboard: {endpoint}");
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
            bool isRunning = NativeProxy.IsInitialized || (MCPServer.Instance?.IsRunning ?? false);

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
                    Debug.Log($"[MCPServerWindow] Port changed to {_portInput}");
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

            EditorGUI.indentLevel--;
        }

        private void DrawErrorMessage()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(_lastError, MessageType.Error);
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
                Debug.Log("[MCPServerWindow] Tools refreshed");
            }
            catch (Exception exception)
            {
                _lastError = $"Failed to refresh tools: {exception.Message}";
                Debug.LogError($"[MCPServerWindow] {_lastError}");
            }
        }
    }
}
