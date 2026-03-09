using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.UI.Tabs;

namespace UnityMCP.Editor.UI
{
    /// <summary>
    /// Editor window for controlling the MCP server and viewing its status.
    /// UI Toolkit implementation with tabbed interface.
    /// </summary>
    public class MCPServerWindow : EditorWindow
    {
        private const string DocumentationUrl = "https://github.com/Bluepuff71/UnityMCP";
        private const string VerboseLoggingPrefKey = "UnityMCP_VerboseLogging";
        private const string ActiveTabPrefKey = "UnityMCP_ActiveTab";

        private VisualElement _statusDot;
        private Label _statusLabel;
        private Label _activeToolLabel;
        private Button _toggleButton;
        private VisualElement _tabContentContainer;
        private readonly List<Button> _tabButtons = new List<Button>();

        private ITab[] _tabs;
        private int _activeTabIndex;

        [MenuItem("Window/Unity MCP")]
        public static void ShowWindow()
        {
            MCPServerWindow window = GetWindow<MCPServerWindow>("Unity MCP");
            window.minSize = new Vector2(340, 450);
        }

        public void CreateGUI()
        {
            // Load stylesheet
            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.emeryporter.unitymcp/Editor/UI/MCPServerWindow.uss");
            if (styleSheet != null)
            {
                rootVisualElement.styleSheets.Add(styleSheet);
            }

            rootVisualElement.AddToClassList("window-root");

            // Initialize verbose logging from prefs
            MCPProxy.VerboseLogging = EditorPrefs.GetBool(VerboseLoggingPrefKey, false);

            // Build persistent header
            VisualElement header = BuildHeader();
            rootVisualElement.Add(header);

            // Build tab bar
            VisualElement tabBar = BuildTabBar();
            rootVisualElement.Add(tabBar);

            // Build tab content container
            _tabContentContainer = new VisualElement();
            _tabContentContainer.style.flexGrow = 1;
            _tabContentContainer.style.overflow = Overflow.Hidden;
            rootVisualElement.Add(_tabContentContainer);

            // Create tabs
            _tabs = new ITab[]
            {
                new StatusTab(),
                new ActivityTab(),
                new ToolsTab(),
                new CheckpointsTab()
            };

            foreach (ITab tab in _tabs)
            {
                tab.Root.AddToClassList("tab-content");
                tab.Root.style.display = DisplayStyle.None;
                _tabContentContainer.Add(tab.Root);
            }

            // Activate saved tab (or default to Status)
            int savedTab = EditorPrefs.GetInt(ActiveTabPrefKey, 0);
            if (savedTab < 0 || savedTab >= _tabs.Length) savedTab = 0;
            SwitchToTab(savedTab);

            // Subscribe to activity events for active tool indicator
            ActivityLog.OnEntryAdded += OnActivityEntryAdded;
        }

        private void OnDestroy()
        {
            ActivityLog.OnEntryAdded -= OnActivityEntryAdded;

            if (_tabs != null)
            {
                foreach (ITab tab in _tabs)
                {
                    tab.OnDeactivate();
                    if (tab is IDisposable disposable)
                        disposable.Dispose();
                }
            }
        }

        /// <summary>
        /// Called ~10 times per second. Update status indicators.
        /// </summary>
        private void OnInspectorUpdate()
        {
            if (_statusDot == null || _tabs == null) return;

            bool isRunning = MCPProxy.IsInitialized;

            // Update status dot
            _statusDot.EnableInClassList("status-dot--running", isRunning);
            _statusDot.EnableInClassList("status-dot--stopped", !isRunning);

            // Update status label
            _statusLabel.text = isRunning ? "Running" : "Stopped";

            // Update toggle button
            _toggleButton.text = isRunning ? "Stop" : "Start";

            // Let active tab refresh
            if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Length)
            {
                _tabs[_activeTabIndex].Refresh();
            }
        }

        private void OnActivityEntryAdded()
        {
            // Show the most recent tool name briefly
            IReadOnlyList<ActivityLog.Entry> entries = ActivityLog.Entries;
            if (entries.Count > 0 && _activeToolLabel != null)
            {
                ActivityLog.Entry latest = entries[entries.Count - 1];
                _activeToolLabel.text = latest.toolName;
                _activeToolLabel.style.display = DisplayStyle.Flex;

                // Schedule hide after 2 seconds
                _activeToolLabel.schedule.Execute(() =>
                {
                    if (_activeToolLabel != null)
                        _activeToolLabel.style.display = DisplayStyle.None;
                }).StartingIn(2000);
            }
        }

        #region Header

        private VisualElement BuildHeader()
        {
            VisualElement header = new VisualElement();
            header.AddToClassList("header");

            // Left: status dot + label
            VisualElement left = new VisualElement();
            left.AddToClassList("header__left");

            _statusDot = new VisualElement();
            _statusDot.AddToClassList("status-dot");
            _statusDot.AddToClassList(MCPProxy.IsInitialized ? "status-dot--running" : "status-dot--stopped");
            left.Add(_statusDot);

            _statusLabel = new Label(MCPProxy.IsInitialized ? "Running" : "Stopped");
            _statusLabel.AddToClassList("status-label");
            left.Add(_statusLabel);

            header.Add(left);

            // Center: active tool indicator
            VisualElement center = new VisualElement();
            center.AddToClassList("header__center");

            _activeToolLabel = new Label("");
            _activeToolLabel.AddToClassList("active-tool-label");
            _activeToolLabel.style.display = DisplayStyle.None;
            center.Add(_activeToolLabel);

            header.Add(center);

            // Right: help + start/stop buttons
            VisualElement right = new VisualElement();
            right.AddToClassList("header__right");

            Button helpButton = new Button(() => Application.OpenURL(DocumentationUrl)) { text = "?" };
            helpButton.AddToClassList("header-button");
            helpButton.AddToClassList("header-button--help");
            right.Add(helpButton);

            _toggleButton = new Button(OnToggleServer) { text = MCPProxy.IsInitialized ? "Stop" : "Start" };
            _toggleButton.AddToClassList("header-button");
            right.Add(_toggleButton);

            header.Add(right);

            return header;
        }

        private void OnToggleServer()
        {
            try
            {
                if (MCPProxy.IsInitialized)
                    MCPProxy.Stop();
                else
                    MCPProxy.Start();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[MCPServerWindow] Failed to toggle server: {exception.Message}");
            }
        }

        #endregion

        #region Tab Bar

        private VisualElement BuildTabBar()
        {
            VisualElement tabBar = new VisualElement();
            tabBar.AddToClassList("tab-bar");

            string[] tabNames = { "Status", "Activity", "Registry", "Checkpoints" };

            for (int i = 0; i < tabNames.Length; i++)
            {
                int tabIndex = i; // Capture for closure
                Button tabButton = new Button(() => SwitchToTab(tabIndex)) { text = tabNames[i] };
                tabButton.AddToClassList("tab-button");
                _tabButtons.Add(tabButton);
                tabBar.Add(tabButton);
            }

            return tabBar;
        }

        private void SwitchToTab(int index)
        {
            if (index < 0 || index >= _tabs.Length) return;

            // Deactivate current tab
            if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Length)
            {
                _tabs[_activeTabIndex].Root.style.display = DisplayStyle.None;
                _tabs[_activeTabIndex].OnDeactivate();
                if (_activeTabIndex < _tabButtons.Count)
                    _tabButtons[_activeTabIndex].RemoveFromClassList("tab-button--active");
            }

            // Activate new tab
            _activeTabIndex = index;
            _tabs[index].Root.style.display = DisplayStyle.Flex;
            _tabs[index].OnActivate();
            if (index < _tabButtons.Count)
                _tabButtons[index].AddToClassList("tab-button--active");

            // Persist selection
            EditorPrefs.SetInt(ActiveTabPrefKey, index);
        }

        #endregion
    }
}
