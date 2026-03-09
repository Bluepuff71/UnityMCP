using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityMCP.Editor.Core;

namespace UnityMCP.Editor.UI.Tabs
{
    /// <summary>
    /// Status tab: connection card, configuration, and remote access settings.
    /// </summary>
    public class StatusTab : ITab
    {
        private const string VerboseLoggingPrefKey = "UnityMCP_VerboseLogging";

        public VisualElement Root { get; }

        // Connection card elements
        private readonly VisualElement _connectionCard;
        private readonly Label _endpointLabel;
        private readonly Label _toolCountLabel;
        private readonly Label _resourceCountLabel;
        private readonly Label _promptCountLabel;
        private readonly Label _recipeCountLabel;

        // Configuration elements
        private readonly IntegerField _portField;
        private readonly Button _applyPortButton;
        private readonly Label _portHint;
        private readonly Toggle _verboseToggle;

        // Remote access elements (assigned in BuildRemoteAccessContent, not constructor)
        private Foldout _remoteAccessFoldout;
        private VisualElement _remoteAccessContent;
        private Toggle _remoteAccessToggle;
        private Label _apiKeyLabel;
        private Label _tlsStatusLabel;
        private Label _remoteEndpointLabel;
        private VisualElement _warningBox;

        public StatusTab()
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1;

            ScrollView scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.AddToClassList("scroll-view");
            Root.Add(scrollView);

            // --- Connection Card ---
            _connectionCard = new VisualElement();
            _connectionCard.AddToClassList("connection-card");
            _connectionCard.AddToClassList("card--accent-left");

            // "Endpoint" section label
            Label endpointHeader = new Label("ENDPOINT");
            endpointHeader.AddToClassList("connection-card__section-label");
            _connectionCard.Add(endpointHeader);

            // Endpoint value + copy button
            VisualElement endpointRow = new VisualElement();
            endpointRow.AddToClassList("row");
            endpointRow.style.alignItems = Align.Center;
            _endpointLabel = new Label();
            _endpointLabel.AddToClassList("connection-card__endpoint");
            _endpointLabel.style.flexGrow = 1;
            endpointRow.Add(_endpointLabel);

            Button copyEndpointButton = new Button(CopyEndpoint) { text = "Copy" };
            copyEndpointButton.AddToClassList("button--accent");
            copyEndpointButton.AddToClassList("button--small");
            endpointRow.Add(copyEndpointButton);
            _connectionCard.Add(endpointRow);

            // Divider
            VisualElement divider = new VisualElement();
            divider.AddToClassList("connection-card__divider");
            _connectionCard.Add(divider);

            // "Registered" section label
            Label registeredHeader = new Label("REGISTERED");
            registeredHeader.AddToClassList("connection-card__section-label");
            _connectionCard.Add(registeredHeader);

            // Stat pills row — evenly spaced
            VisualElement statRow = new VisualElement();
            statRow.AddToClassList("connection-card__stats");

            _toolCountLabel = CreateStatPill("0 Tools");
            _resourceCountLabel = CreateStatPill("0 Resources");
            _promptCountLabel = CreateStatPill("0 Prompts");
            _recipeCountLabel = CreateStatPill("0 Recipes");

            statRow.Add(_toolCountLabel);
            statRow.Add(_resourceCountLabel);
            statRow.Add(_promptCountLabel);
            statRow.Add(_recipeCountLabel);
            _connectionCard.Add(statRow);

            scrollView.Add(_connectionCard);
            scrollView.Add(CreateSpacer(12));

            // --- Configuration Section ---
            Label configHeader = new Label("Configuration");
            configHeader.AddToClassList("section-header");
            scrollView.Add(configHeader);

            // Port row
            VisualElement portRow = new VisualElement();
            portRow.AddToClassList("row");
            portRow.style.marginBottom = 4;

            _portField = new IntegerField("Port");
            _portField.value = MCPServer.Instance?.Port ?? 8080;
            _portField.style.flexGrow = 1;
            _portField.RegisterValueChangedCallback(OnPortChanged);
            portRow.Add(_portField);

            _applyPortButton = new Button(ApplyPort) { text = "Apply" };
            _applyPortButton.AddToClassList("button--small");
            _applyPortButton.AddToClassList("button--accent");
            _applyPortButton.SetEnabled(false);
            portRow.Add(_applyPortButton);

            scrollView.Add(portRow);

            _portHint = new Label("Stop the server to change the port.");
            _portHint.AddToClassList("muted");
            _portHint.style.display = MCPProxy.IsInitialized ? DisplayStyle.Flex : DisplayStyle.None;
            scrollView.Add(_portHint);

            // Verbose logging toggle
            _verboseToggle = new Toggle("Verbose Logging");
            _verboseToggle.value = MCPProxy.VerboseLogging;
            _verboseToggle.RegisterValueChangedCallback(evt =>
            {
                MCPProxy.VerboseLogging = evt.newValue;
                EditorPrefs.SetBool(VerboseLoggingPrefKey, evt.newValue);
            });
            _verboseToggle.style.marginTop = 4;
            scrollView.Add(_verboseToggle);

            scrollView.Add(CreateSpacer(12));

            // --- Remote Access Section ---
            _remoteAccessFoldout = new Foldout { text = "Remote Access", value = false };
            _remoteAccessContent = BuildRemoteAccessContent();
            _remoteAccessFoldout.Add(_remoteAccessContent);
            scrollView.Add(_remoteAccessFoldout);
        }

        public void OnActivate()
        {
            RefreshConnectionCard();
            RefreshRemoteAccess();
        }

        public void OnDeactivate() { }

        public void Refresh()
        {
            RefreshConnectionCard();
        }

        #region Connection Card

        private void RefreshConnectionCard()
        {
            bool isRunning = MCPProxy.IsInitialized;
            int port = MCPServer.Instance?.Port ?? 8080;

            _connectionCard.EnableInClassList("card--running", isRunning);
            _connectionCard.EnableInClassList("card--stopped", !isRunning);

            // Endpoint
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
            _endpointLabel.text = endpoint;

            // Stat pills
            _toolCountLabel.text = $"{ToolRegistry.Count} Tools";
            _resourceCountLabel.text = $"{ResourceRegistry.Count} Resources";
            _promptCountLabel.text = $"{PromptRegistry.Count} Prompts";
            _recipeCountLabel.text = $"{RecipeRegistry.Count} Recipes";

            // Port field state
            _portField.SetEnabled(!isRunning);
            _applyPortButton.SetEnabled(!isRunning && _portField.value != port);
            _portHint.style.display = isRunning ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void CopyEndpoint()
        {
            EditorGUIUtility.systemCopyBuffer = _endpointLabel.text;
        }

        #endregion

        #region Configuration

        private void OnPortChanged(ChangeEvent<int> evt)
        {
            int currentPort = MCPServer.Instance?.Port ?? 8080;
            _applyPortButton.SetEnabled(!MCPProxy.IsInitialized && evt.newValue != currentPort);
        }

        private void ApplyPort()
        {
            int portValue = _portField.value;
            if (portValue > 0 && portValue <= 65535)
            {
                MCPServer.Instance.Port = portValue;
            }
            else
            {
                Debug.LogWarning("[MCPServerWindow] Port must be between 1 and 65535.");
            }
        }

        #endregion

        #region Remote Access

        private VisualElement BuildRemoteAccessContent()
        {
            VisualElement content = new VisualElement();

            // Enable toggle
            _remoteAccessToggle = new Toggle("Enable Remote Access");
            _remoteAccessToggle.value = MCPProxy.RemoteAccessEnabled;
            _remoteAccessToggle.RegisterValueChangedCallback(evt =>
            {
                MCPProxy.RemoteAccessEnabled = evt.newValue;
                MCPProxy.Restart();
                RefreshRemoteAccess();
            });
            content.Add(_remoteAccessToggle);

            // Remote details (shown when enabled)
            _remoteAccessDetailsContainer = new VisualElement();
            _remoteAccessDetailsContainer.style.marginTop = 4;

            // API key row
            VisualElement apiKeyRow = new VisualElement();
            apiKeyRow.AddToClassList("row");

            Label apiKeyHeader = new Label("API Key");
            apiKeyHeader.style.minWidth = 60;
            apiKeyRow.Add(apiKeyHeader);

            _apiKeyLabel = new Label();
            _apiKeyLabel.AddToClassList("mono");
            _apiKeyLabel.style.flexGrow = 1;
            _apiKeyLabel.style.overflow = Overflow.Hidden;
            apiKeyRow.Add(_apiKeyLabel);

            Button copyKeyButton = new Button(CopyApiKey) { text = "Copy" };
            copyKeyButton.AddToClassList("button--small");
            copyKeyButton.AddToClassList("button--accent");
            apiKeyRow.Add(copyKeyButton);

            Button regenKeyButton = new Button(RegenerateApiKey) { text = "Regenerate" };
            regenKeyButton.AddToClassList("button--small");
            regenKeyButton.AddToClassList("button--warning");
            apiKeyRow.Add(regenKeyButton);

            _remoteAccessDetailsContainer.Add(apiKeyRow);

            // TLS status row
            VisualElement tlsRow = new VisualElement();
            tlsRow.AddToClassList("row");
            tlsRow.style.marginTop = 4;

            Label tlsHeader = new Label("TLS");
            tlsHeader.style.minWidth = 60;
            tlsRow.Add(tlsHeader);

            _tlsStatusLabel = new Label();
            _tlsStatusLabel.AddToClassList("pill");
            tlsRow.Add(_tlsStatusLabel);

            _remoteAccessDetailsContainer.Add(tlsRow);

            // Endpoint row
            VisualElement remoteEndpointRow = new VisualElement();
            remoteEndpointRow.AddToClassList("row");
            remoteEndpointRow.style.marginTop = 4;

            Label endpointHeader = new Label("Endpoint");
            endpointHeader.style.minWidth = 60;
            remoteEndpointRow.Add(endpointHeader);

            _remoteEndpointLabel = new Label();
            _remoteEndpointLabel.AddToClassList("mono");
            remoteEndpointRow.Add(_remoteEndpointLabel);

            _remoteAccessDetailsContainer.Add(remoteEndpointRow);

            // Warning box
            _warningBox = new VisualElement();
            _warningBox.AddToClassList("warning-box");
            _warningBox.style.marginTop = 8;
            _warningBox.Add(new Label(
                "Remote access binds to all network interfaces with TLS encryption and API key authentication. " +
                "Ensure your firewall is configured appropriately."));
            _remoteAccessDetailsContainer.Add(_warningBox);

            content.Add(_remoteAccessDetailsContainer);

            return content;
        }

        private VisualElement _remoteAccessDetailsContainer;

        private void RefreshRemoteAccess()
        {
            bool enabled = MCPProxy.RemoteAccessEnabled;
            _remoteAccessToggle.SetValueWithoutNotify(enabled);
            _remoteAccessDetailsContainer.style.display = enabled ? DisplayStyle.Flex : DisplayStyle.None;

            if (!enabled) return;

            // API key
            string apiKey = MCPProxy.ApiKey;
            string displayKey = string.IsNullOrEmpty(apiKey)
                ? "(none)"
                : (apiKey.Length > 24 ? apiKey.Substring(0, 24) + "..." : apiKey);
            _apiKeyLabel.text = displayKey;

            // TLS status
            if (!MCPProxy.IsTlsSupported)
            {
                _tlsStatusLabel.text = "Not Available";
                _tlsStatusLabel.EnableInClassList("pill--error", true);
                _tlsStatusLabel.EnableInClassList("pill--success", false);
            }
            else
            {
                string certDir = CertificateGenerator.GetCertDirectory();
                DateTimeOffset? expiry = CertificateGenerator.GetCertificateExpiry(certDir);
                if (expiry.HasValue)
                {
                    _tlsStatusLabel.text = $"Active (expires {expiry.Value:yyyy-MM-dd})";
                    _tlsStatusLabel.EnableInClassList("pill--success", true);
                    _tlsStatusLabel.EnableInClassList("pill--error", false);
                }
                else
                {
                    _tlsStatusLabel.text = "No Certificate";
                    _tlsStatusLabel.EnableInClassList("pill--error", true);
                    _tlsStatusLabel.EnableInClassList("pill--success", false);
                }
            }

            // Remote endpoint
            int port = MCPServer.Instance?.Port ?? 8080;
            string lanIp = NetworkUtils.GetLanIpAddress();
            _remoteEndpointLabel.text = $"https://{lanIp}:{port}/";
        }

        private void CopyApiKey()
        {
            EditorGUIUtility.systemCopyBuffer = MCPProxy.ApiKey;
        }

        private void RegenerateApiKey()
        {
            MCPProxy.ApiKey = MCPProxy.GenerateApiKey();
            MCPProxy.Restart();
            RefreshRemoteAccess();
        }

        #endregion

        #region Helpers

        private static Label CreateStatPill(string text)
        {
            Label pill = new Label(text);
            pill.AddToClassList("pill");
            pill.AddToClassList("pill--stat");
            return pill;
        }

        private static VisualElement CreateSpacer(int height)
        {
            VisualElement spacer = new VisualElement();
            spacer.style.height = height;
            return spacer;
        }

        #endregion
    }
}
