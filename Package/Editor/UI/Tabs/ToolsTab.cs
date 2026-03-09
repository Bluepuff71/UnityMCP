using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using UnityMCP.Editor.Core;

namespace UnityMCP.Editor.UI.Tabs
{
    /// <summary>
    /// Registry tab: browsable catalog of MCP tools, resources, prompts, and recipes
    /// with inner sub-tabs, category grouping, search, and expandable detail.
    /// </summary>
    public class ToolsTab : ITab
    {
        private enum SubTab { Tools, Resources, Prompts, Recipes }

        public VisualElement Root { get; }

        private readonly Label _summaryLabel;
        private readonly TextField _searchField;
        private readonly ScrollView _scrollView;
        private readonly VisualElement _listContainer;
        private readonly VisualElement _emptyState;

        private readonly Button _tabTools;
        private readonly Button _tabResources;
        private readonly Button _tabPrompts;
        private readonly Button _tabRecipes;

        private string _searchFilter = "";
        private SubTab _currentSubTab = SubTab.Tools;
        private List<IGrouping<string, ToolDefinition>> _cachedToolsByCategory;

        public ToolsTab()
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1;

            // Summary bar
            VisualElement summaryBar = new VisualElement();
            summaryBar.AddToClassList("row--spaced");
            summaryBar.style.marginBottom = 6;

            _summaryLabel = new Label();
            _summaryLabel.AddToClassList("muted");
            summaryBar.Add(_summaryLabel);

            Button refreshButton = new Button(OnRefresh) { text = "Refresh" };
            refreshButton.AddToClassList("button--small");
            refreshButton.AddToClassList("button--accent");
            summaryBar.Add(refreshButton);

            Root.Add(summaryBar);

            // Inner sub-tab bar
            VisualElement subTabBar = new VisualElement();
            subTabBar.AddToClassList("reg-tab-bar");

            _tabTools = CreateSubTab("Tools", SubTab.Tools);
            _tabResources = CreateSubTab("Resources", SubTab.Resources);
            _tabPrompts = CreateSubTab("Prompts", SubTab.Prompts);
            _tabRecipes = CreateSubTab("Recipes", SubTab.Recipes);

            subTabBar.Add(_tabTools);
            subTabBar.Add(_tabResources);
            subTabBar.Add(_tabPrompts);
            subTabBar.Add(_tabRecipes);

            _tabTools.AddToClassList("reg-tab--active");
            Root.Add(subTabBar);

            // Search field
            _searchField = new TextField();
            _searchField.AddToClassList("search-field");
            _searchField.RegisterValueChangedCallback(evt =>
            {
                _searchFilter = evt.newValue ?? "";
                RebuildList();
            });
            Root.Add(_searchField);

            _searchField.value = "";
            SetPlaceholder(_searchField, "Search by name, description, or parameter...");

            // Scroll view
            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.AddToClassList("scroll-view");
            Root.Add(_scrollView);

            _listContainer = _scrollView.contentContainer;

            // Empty state
            _emptyState = new VisualElement();
            _emptyState.AddToClassList("empty-state");
            Label emptyLabel = new Label("No items registered.");
            emptyLabel.AddToClassList("empty-state-text");
            _emptyState.Add(emptyLabel);
        }

        public void OnActivate()
        {
            RefreshCache();
            RebuildList();
        }

        public void OnDeactivate() { }

        public void Refresh() { }

        private void OnRefresh()
        {
            ToolRegistry.RefreshTools();
            RefreshCache();
            RebuildList();
        }

        private void RefreshCache()
        {
            _cachedToolsByCategory = ToolRegistry.GetDefinitionsByCategory().ToList();
            int toolCount = ToolRegistry.Count;
            int resourceCount = ResourceRegistry.Count;
            int promptCount = PromptRegistry.Count;
            int recipeCount = RecipeRegistry.Count;
            _summaryLabel.text = $"{toolCount} tools \u00b7 {resourceCount} resources \u00b7 {promptCount} prompts \u00b7 {recipeCount} recipes";
        }

        #region Sub-Tab Switching

        private Button CreateSubTab(string label, SubTab subTab)
        {
            Button button = new Button(() => SwitchSubTab(subTab)) { text = label };
            button.AddToClassList("reg-tab");
            return button;
        }

        private void SwitchSubTab(SubTab subTab)
        {
            _currentSubTab = subTab;

            _tabTools.EnableInClassList("reg-tab--active", subTab == SubTab.Tools);
            _tabResources.EnableInClassList("reg-tab--active", subTab == SubTab.Resources);
            _tabPrompts.EnableInClassList("reg-tab--active", subTab == SubTab.Prompts);
            _tabRecipes.EnableInClassList("reg-tab--active", subTab == SubTab.Recipes);

            RebuildList();
        }

        #endregion

        #region List Building

        private void RebuildList()
        {
            _listContainer.Clear();
            _emptyState.RemoveFromHierarchy();

            bool hasFilter = !string.IsNullOrWhiteSpace(_searchFilter);
            string filterLower = hasFilter ? _searchFilter.ToLowerInvariant() : "";
            bool hasVisibleItems = false;

            switch (_currentSubTab)
            {
                case SubTab.Tools:
                    hasVisibleItems = BuildToolsContent(filterLower, hasFilter);
                    break;
                case SubTab.Resources:
                    hasVisibleItems = BuildResourcesContent(filterLower, hasFilter);
                    break;
                case SubTab.Prompts:
                    hasVisibleItems = BuildPromptsContent(filterLower, hasFilter);
                    break;
                case SubTab.Recipes:
                    hasVisibleItems = BuildRecipesContent(filterLower, hasFilter);
                    break;
            }

            if (!hasVisibleItems)
            {
                if (hasFilter)
                {
                    VisualElement noResults = new VisualElement();
                    noResults.AddToClassList("empty-state");
                    Label noResultsLabel = new Label($"No {_currentSubTab.ToString().ToLowerInvariant()} matching \"{_searchFilter}\"");
                    noResultsLabel.AddToClassList("empty-state-text");
                    noResults.Add(noResultsLabel);
                    _listContainer.Add(noResults);
                }
                else
                {
                    _scrollView.Add(_emptyState);
                }
            }
        }

        #endregion

        #region Tools Content

        private bool BuildToolsContent(string filterLower, bool hasFilter)
        {
            if (_cachedToolsByCategory == null || _cachedToolsByCategory.Count == 0)
                return false;

            bool hasVisibleTools = false;

            foreach (IGrouping<string, ToolDefinition> group in _cachedToolsByCategory)
            {
                List<ToolDefinition> tools = group.ToList();

                if (hasFilter)
                {
                    tools = tools.Where(t =>
                        MatchesFilter(t.name, filterLower) ||
                        MatchesFilter(t.description, filterLower) ||
                        MatchesParamFilter(t.inputSchema, filterLower)
                    ).ToList();

                    if (tools.Count == 0) continue;
                }

                hasVisibleTools = true;
                _listContainer.Add(BuildCollapsibleSection(group.Key, tools.Count, hasFilter, container =>
                {
                    foreach (ToolDefinition tool in tools)
                        container.Add(BuildToolCard(tool));
                }));
            }

            return hasVisibleTools;
        }

        private static bool MatchesParamFilter(InputSchema schema, string filterLower)
        {
            if (schema?.properties == null) return false;
            foreach (KeyValuePair<string, PropertySchema> kvp in schema.properties)
            {
                if (MatchesFilter(kvp.Key, filterLower)) return true;
                if (MatchesFilter(kvp.Value?.description, filterLower)) return true;
            }
            return false;
        }

        private VisualElement BuildToolCard(ToolDefinition tool)
        {
            VisualElement card = new VisualElement();
            card.AddToClassList("reg-card");
            card.AddToClassList("reg-card--tool");

            // Header row: name + pills
            VisualElement headerRow = new VisualElement();
            headerRow.AddToClassList("reg-card__header");

            Label nameLabel = new Label(tool.name);
            nameLabel.AddToClassList("reg-card__name");
            headerRow.Add(nameLabel);

            if (tool.inputSchema?.properties != null && tool.inputSchema.properties.Count > 0)
            {
                int totalParams = tool.inputSchema.properties.Count;
                int requiredParams = tool.inputSchema.required?.Count ?? 0;
                string paramText = requiredParams > 0
                    ? $"{totalParams} params ({requiredParams} req)"
                    : $"{totalParams} params";

                headerRow.Add(CreatePill(paramText, "pill--stat"));
            }

            if (tool.annotations != null)
            {
                if (tool.annotations.readOnlyHint == true)
                    headerRow.Add(CreatePill("read-only", "pill--readonly"));
                if (tool.annotations.destructiveHint == true)
                    headerRow.Add(CreatePill("destructive", "pill--destructive"));
                if (tool.annotations.idempotentHint == true)
                    headerRow.Add(CreatePill("idempotent", "pill--idempotent"));
            }

            card.Add(headerRow);

            if (!string.IsNullOrEmpty(tool.description))
            {
                Label descLabel = new Label(tool.description);
                descLabel.AddToClassList("reg-card__desc");
                card.Add(descLabel);
            }

            // Expandable parameter detail
            bool hasParams = tool.inputSchema?.properties != null && tool.inputSchema.properties.Count > 0;
            if (hasParams)
            {
                VisualElement detailPanel = BuildParamDetailPanel(tool.inputSchema);
                detailPanel.style.display = DisplayStyle.None;
                card.Add(detailPanel);

                card.RegisterCallback<ClickEvent>(evt =>
                {
                    bool isExpanded = detailPanel.style.display == DisplayStyle.Flex;
                    detailPanel.style.display = isExpanded ? DisplayStyle.None : DisplayStyle.Flex;
                    card.EnableInClassList("reg-card--expanded", !isExpanded);
                });
                card.AddToClassList("reg-card--clickable");
            }

            return card;
        }

        private static VisualElement BuildParamDetailPanel(InputSchema schema)
        {
            VisualElement panel = new VisualElement();
            panel.AddToClassList("reg-detail");

            VisualElement divider = new VisualElement();
            divider.AddToClassList("reg-detail__divider");
            panel.Add(divider);

            Label headerLabel = new Label("PARAMETERS");
            headerLabel.AddToClassList("reg-detail__header");
            panel.Add(headerLabel);

            HashSet<string> requiredSet = new HashSet<string>(schema.required ?? new List<string>());

            foreach (KeyValuePair<string, PropertySchema> kvp in schema.properties)
            {
                panel.Add(BuildParamRow(kvp.Key, kvp.Value, requiredSet.Contains(kvp.Key)));
            }

            return panel;
        }

        private static VisualElement BuildParamRow(string paramName, PropertySchema prop, bool isRequired)
        {
            VisualElement paramRow = new VisualElement();
            paramRow.AddToClassList("reg-param");

            VisualElement nameTypeRow = new VisualElement();
            nameTypeRow.AddToClassList("reg-param__header");

            Label nameLabel = new Label(paramName);
            nameLabel.AddToClassList("reg-param__name");
            nameTypeRow.Add(nameLabel);

            if (!string.IsNullOrEmpty(prop?.type))
            {
                Label typeLabel = new Label(prop.type);
                typeLabel.AddToClassList("reg-param__type");
                nameTypeRow.Add(typeLabel);
            }

            if (isRequired)
            {
                Label reqLabel = new Label("required");
                reqLabel.AddToClassList("reg-param__required");
                nameTypeRow.Add(reqLabel);
            }

            paramRow.Add(nameTypeRow);

            if (prop != null && !string.IsNullOrEmpty(prop.description))
            {
                Label descLabel = new Label(prop.description);
                descLabel.AddToClassList("reg-param__desc");
                paramRow.Add(descLabel);
            }

            string meta = BuildParamMeta(prop);
            if (!string.IsNullOrEmpty(meta))
            {
                Label metaLabel = new Label(meta);
                metaLabel.AddToClassList("reg-param__meta");
                paramRow.Add(metaLabel);
            }

            return paramRow;
        }

        private static string BuildParamMeta(PropertySchema prop)
        {
            if (prop == null) return null;

            List<string> parts = new List<string>();

            if (prop.@default != null)
                parts.Add($"default: {prop.@default}");

            if (prop.@enum != null && prop.@enum.Count > 0)
            {
                string enumValues = string.Join(", ", prop.@enum.Take(6));
                if (prop.@enum.Count > 6)
                    enumValues += $", +{prop.@enum.Count - 6} more";
                parts.Add(enumValues);
            }

            if (prop.minimum.HasValue && !double.IsNaN(prop.minimum.Value))
                parts.Add($"min: {prop.minimum.Value}");

            if (prop.maximum.HasValue && !double.IsNaN(prop.maximum.Value))
                parts.Add($"max: {prop.maximum.Value}");

            return parts.Count > 0 ? string.Join(" \u00b7 ", parts) : null;
        }

        #endregion

        #region Resources Content

        private bool BuildResourcesContent(string filterLower, bool hasFilter)
        {
            List<ResourceDefinition> resources = ResourceRegistry.GetDefinitions().ToList();
            List<ResourceTemplate> templates = ResourceRegistry.GetTemplateDefinitions().ToList();

            if (hasFilter)
            {
                resources = resources.Where(r =>
                    MatchesFilter(r.name, filterLower) ||
                    MatchesFilter(r.uri, filterLower) ||
                    MatchesFilter(r.description, filterLower)
                ).ToList();

                templates = templates.Where(t =>
                    MatchesFilter(t.name, filterLower) ||
                    MatchesFilter(t.uriTemplate, filterLower) ||
                    MatchesFilter(t.description, filterLower)
                ).ToList();
            }

            if (resources.Count == 0 && templates.Count == 0) return false;

            // Group by derived category
            var resourcesByCategory = resources
                .GroupBy(r => DeriveResourceCategory(r.uri))
                .OrderBy(g => g.Key);

            var templatesByCategory = templates
                .GroupBy(t => DeriveResourceCategory(t.uriTemplate))
                .OrderBy(g => g.Key);

            // Merge categories
            Dictionary<string, List<VisualElement>> categoryCards = new Dictionary<string, List<VisualElement>>();

            foreach (var group in resourcesByCategory)
            {
                if (!categoryCards.ContainsKey(group.Key))
                    categoryCards[group.Key] = new List<VisualElement>();
                foreach (ResourceDefinition resource in group)
                    categoryCards[group.Key].Add(BuildResourceCard(resource));
            }

            foreach (var group in templatesByCategory)
            {
                if (!categoryCards.ContainsKey(group.Key))
                    categoryCards[group.Key] = new List<VisualElement>();
                foreach (ResourceTemplate template in group)
                    categoryCards[group.Key].Add(BuildTemplateCard(template));
            }

            foreach (var kvp in categoryCards.OrderBy(k => k.Key))
            {
                _listContainer.Add(BuildCollapsibleSection(kvp.Key, kvp.Value.Count, hasFilter, container =>
                {
                    foreach (VisualElement card in kvp.Value)
                        container.Add(card);
                }, "reg-section-header--resource"));
            }

            return true;
        }

        private static string DeriveResourceCategory(string uri)
        {
            if (string.IsNullOrEmpty(uri)) return "Other";
            int schemeEnd = uri.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd < 0) return "Other";
            string scheme = uri.Substring(0, schemeEnd);
            return char.ToUpper(scheme[0]) + scheme.Substring(1);
        }

        private static VisualElement BuildResourceCard(ResourceDefinition resource)
        {
            VisualElement card = new VisualElement();
            card.AddToClassList("reg-card");
            card.AddToClassList("reg-card--resource");

            VisualElement headerRow = new VisualElement();
            headerRow.AddToClassList("reg-card__header");

            Label nameLabel = new Label(resource.name ?? resource.uri);
            nameLabel.AddToClassList("reg-card__name");
            headerRow.Add(nameLabel);

            if (!string.IsNullOrEmpty(resource.mimeType))
                headerRow.Add(CreatePill(resource.mimeType, "pill--readonly"));

            card.Add(headerRow);

            if (!string.IsNullOrEmpty(resource.uri))
            {
                Label uriLabel = new Label(resource.uri);
                uriLabel.AddToClassList("reg-card__uri");
                card.Add(uriLabel);
            }

            if (!string.IsNullOrEmpty(resource.description))
            {
                Label descLabel = new Label(resource.description);
                descLabel.AddToClassList("reg-card__desc");
                card.Add(descLabel);
            }

            return card;
        }

        private static VisualElement BuildTemplateCard(ResourceTemplate template)
        {
            VisualElement card = new VisualElement();
            card.AddToClassList("reg-card");
            card.AddToClassList("reg-card--resource");

            VisualElement headerRow = new VisualElement();
            headerRow.AddToClassList("reg-card__header");

            Label nameLabel = new Label(template.name ?? template.uriTemplate);
            nameLabel.AddToClassList("reg-card__name");
            headerRow.Add(nameLabel);

            headerRow.Add(CreatePill("template", "pill--idempotent"));

            if (!string.IsNullOrEmpty(template.mimeType))
                headerRow.Add(CreatePill(template.mimeType, "pill--readonly"));

            card.Add(headerRow);

            if (!string.IsNullOrEmpty(template.uriTemplate))
            {
                Label uriLabel = new Label(template.uriTemplate);
                uriLabel.AddToClassList("reg-card__uri");
                card.Add(uriLabel);
            }

            if (!string.IsNullOrEmpty(template.description))
            {
                Label descLabel = new Label(template.description);
                descLabel.AddToClassList("reg-card__desc");
                card.Add(descLabel);
            }

            return card;
        }

        #endregion

        #region Prompts Content

        private bool BuildPromptsContent(string filterLower, bool hasFilter)
        {
            List<PromptDefinition> prompts = PromptRegistry.GetDefinitions().ToList();

            if (hasFilter)
            {
                prompts = prompts.Where(p =>
                    MatchesFilter(p.name, filterLower) ||
                    MatchesFilter(p.description, filterLower)
                ).ToList();
            }

            if (prompts.Count == 0) return false;

            foreach (PromptDefinition prompt in prompts)
            {
                _listContainer.Add(BuildPromptCard(prompt));
            }

            return true;
        }

        private static VisualElement BuildPromptCard(PromptDefinition prompt)
        {
            VisualElement card = new VisualElement();
            card.AddToClassList("reg-card");
            card.AddToClassList("reg-card--prompt");

            VisualElement headerRow = new VisualElement();
            headerRow.AddToClassList("reg-card__header");

            Label nameLabel = new Label(prompt.name);
            nameLabel.AddToClassList("reg-card__name");
            headerRow.Add(nameLabel);

            if (prompt.arguments != null && prompt.arguments.Count > 0)
            {
                int totalArgs = prompt.arguments.Count;
                int requiredArgs = prompt.arguments.Count(a => a.required);
                string argText = requiredArgs > 0
                    ? $"{totalArgs} args ({requiredArgs} req)"
                    : $"{totalArgs} args";

                headerRow.Add(CreatePill(argText, "pill--stat"));
            }

            card.Add(headerRow);

            if (!string.IsNullOrEmpty(prompt.description))
            {
                Label descLabel = new Label(prompt.description);
                descLabel.AddToClassList("reg-card__desc");
                card.Add(descLabel);
            }

            if (prompt.arguments != null && prompt.arguments.Count > 0)
            {
                VisualElement detailPanel = BuildPromptDetailPanel(prompt.arguments);
                detailPanel.style.display = DisplayStyle.None;
                card.Add(detailPanel);

                card.RegisterCallback<ClickEvent>(evt =>
                {
                    bool isExpanded = detailPanel.style.display == DisplayStyle.Flex;
                    detailPanel.style.display = isExpanded ? DisplayStyle.None : DisplayStyle.Flex;
                    card.EnableInClassList("reg-card--expanded", !isExpanded);
                });
                card.AddToClassList("reg-card--clickable");
            }

            return card;
        }

        private static VisualElement BuildPromptDetailPanel(List<PromptArgument> arguments)
        {
            VisualElement panel = new VisualElement();
            panel.AddToClassList("reg-detail");

            VisualElement divider = new VisualElement();
            divider.AddToClassList("reg-detail__divider");
            panel.Add(divider);

            Label headerLabel = new Label("ARGUMENTS");
            headerLabel.AddToClassList("reg-detail__header");
            panel.Add(headerLabel);

            foreach (PromptArgument arg in arguments)
            {
                VisualElement paramRow = new VisualElement();
                paramRow.AddToClassList("reg-param");

                VisualElement nameRow = new VisualElement();
                nameRow.AddToClassList("reg-param__header");

                Label nameLabel = new Label(arg.name);
                nameLabel.AddToClassList("reg-param__name");
                nameRow.Add(nameLabel);

                if (arg.required)
                {
                    Label reqLabel = new Label("required");
                    reqLabel.AddToClassList("reg-param__required");
                    nameRow.Add(reqLabel);
                }

                paramRow.Add(nameRow);

                if (!string.IsNullOrEmpty(arg.description))
                {
                    Label descLabel = new Label(arg.description);
                    descLabel.AddToClassList("reg-param__desc");
                    paramRow.Add(descLabel);
                }

                panel.Add(paramRow);
            }

            return panel;
        }

        #endregion

        #region Recipes Content

        private bool BuildRecipesContent(string filterLower, bool hasFilter)
        {
            List<RecipeInfo> recipes = RecipeRegistry.GetDefinitions().ToList();

            if (hasFilter)
            {
                recipes = recipes.Where(r =>
                    MatchesFilter(r.Name, filterLower) ||
                    MatchesFilter(r.Description, filterLower)
                ).ToList();
            }

            if (recipes.Count == 0) return false;

            foreach (RecipeInfo recipe in recipes)
            {
                _listContainer.Add(BuildRecipeCard(recipe));
            }

            return true;
        }

        private static VisualElement BuildRecipeCard(RecipeInfo recipe)
        {
            VisualElement card = new VisualElement();
            card.AddToClassList("reg-card");
            card.AddToClassList("reg-card--recipe");

            VisualElement headerRow = new VisualElement();
            headerRow.AddToClassList("reg-card__header");

            Label nameLabel = new Label(recipe.Name);
            nameLabel.AddToClassList("reg-card__name");
            headerRow.Add(nameLabel);

            List<RecipeParameterMetadata> parameters = recipe.GetParameterMetadata().ToList();
            if (parameters.Count > 0)
            {
                int requiredParams = parameters.Count(p => p.Required);
                string paramText = requiredParams > 0
                    ? $"{parameters.Count} params ({requiredParams} req)"
                    : $"{parameters.Count} params";

                headerRow.Add(CreatePill(paramText, "pill--stat"));
            }

            card.Add(headerRow);

            if (!string.IsNullOrEmpty(recipe.Description))
            {
                Label descLabel = new Label(recipe.Description);
                descLabel.AddToClassList("reg-card__desc");
                card.Add(descLabel);
            }

            // Expandable parameter detail
            if (parameters.Count > 0)
            {
                VisualElement detailPanel = BuildRecipeDetailPanel(parameters);
                detailPanel.style.display = DisplayStyle.None;
                card.Add(detailPanel);

                card.RegisterCallback<ClickEvent>(evt =>
                {
                    bool isExpanded = detailPanel.style.display == DisplayStyle.Flex;
                    detailPanel.style.display = isExpanded ? DisplayStyle.None : DisplayStyle.Flex;
                    card.EnableInClassList("reg-card--expanded", !isExpanded);
                });
                card.AddToClassList("reg-card--clickable");
            }

            return card;
        }

        private static VisualElement BuildRecipeDetailPanel(List<RecipeParameterMetadata> parameters)
        {
            VisualElement panel = new VisualElement();
            panel.AddToClassList("reg-detail");

            VisualElement divider = new VisualElement();
            divider.AddToClassList("reg-detail__divider");
            panel.Add(divider);

            Label headerLabel = new Label("PARAMETERS");
            headerLabel.AddToClassList("reg-detail__header");
            panel.Add(headerLabel);

            foreach (RecipeParameterMetadata param in parameters)
            {
                VisualElement paramRow = new VisualElement();
                paramRow.AddToClassList("reg-param");

                VisualElement nameTypeRow = new VisualElement();
                nameTypeRow.AddToClassList("reg-param__header");

                Label nameLabel = new Label(param.Name);
                nameLabel.AddToClassList("reg-param__name");
                nameTypeRow.Add(nameLabel);

                if (!string.IsNullOrEmpty(param.JsonType))
                {
                    Label typeLabel = new Label(param.JsonType);
                    typeLabel.AddToClassList("reg-param__type");
                    nameTypeRow.Add(typeLabel);
                }

                if (param.Required)
                {
                    Label reqLabel = new Label("required");
                    reqLabel.AddToClassList("reg-param__required");
                    nameTypeRow.Add(reqLabel);
                }

                paramRow.Add(nameTypeRow);

                if (!string.IsNullOrEmpty(param.Description))
                {
                    Label descLabel = new Label(param.Description);
                    descLabel.AddToClassList("reg-param__desc");
                    paramRow.Add(descLabel);
                }

                // Default value
                if (param.ParameterInfo.HasDefaultValue && param.ParameterInfo.DefaultValue != null)
                {
                    Label defaultLabel = new Label($"default: {param.ParameterInfo.DefaultValue}");
                    defaultLabel.AddToClassList("reg-param__meta");
                    paramRow.Add(defaultLabel);
                }

                panel.Add(paramRow);
            }

            return panel;
        }

        #endregion

        #region Collapsible Section Builder

        private static VisualElement BuildCollapsibleSection(string title, int count, bool startExpanded,
            Action<VisualElement> populateCards, string extraHeaderClass = null)
        {
            VisualElement sectionGroup = new VisualElement();

            VisualElement sectionHeader = new VisualElement();
            sectionHeader.AddToClassList("reg-section-header");
            sectionHeader.AddToClassList("reg-section-header--clickable");
            if (!string.IsNullOrEmpty(extraHeaderClass))
                sectionHeader.AddToClassList(extraHeaderClass);

            Label arrowLabel = new Label(startExpanded ? "\u25BC" : "\u25B6");
            arrowLabel.AddToClassList("reg-section-arrow");
            sectionHeader.Add(arrowLabel);

            Label sectionLabel = new Label(title);
            sectionLabel.AddToClassList("reg-section-label");
            sectionHeader.Add(sectionLabel);

            Label sectionCount = new Label($"{count}");
            sectionCount.AddToClassList("reg-section-count");
            sectionHeader.Add(sectionCount);

            sectionGroup.Add(sectionHeader);

            VisualElement cardsContainer = new VisualElement();
            cardsContainer.style.display = startExpanded ? DisplayStyle.Flex : DisplayStyle.None;

            populateCards(cardsContainer);

            sectionGroup.Add(cardsContainer);

            sectionHeader.RegisterCallback<ClickEvent>(evt =>
            {
                bool isExpanded = cardsContainer.style.display == DisplayStyle.Flex;
                cardsContainer.style.display = isExpanded ? DisplayStyle.None : DisplayStyle.Flex;
                arrowLabel.text = isExpanded ? "\u25B6" : "\u25BC";
            });

            return sectionGroup;
        }

        #endregion

        #region Helpers

        private static bool MatchesFilter(string value, string filterLower)
        {
            return value != null && value.ToLowerInvariant().Contains(filterLower);
        }

        private static Label CreatePill(string text, string styleClass)
        {
            Label pill = new Label(text);
            pill.AddToClassList("pill");
            pill.AddToClassList(styleClass);
            return pill;
        }

        private static void SetPlaceholder(TextField textField, string placeholder)
        {
            Label placeholderLabel = new Label(placeholder);
            placeholderLabel.AddToClassList("muted");
            placeholderLabel.style.position = Position.Absolute;
            placeholderLabel.style.left = 4;
            placeholderLabel.style.top = 2;
            placeholderLabel.pickingMode = PickingMode.Ignore;

            textField.Add(placeholderLabel);

            textField.RegisterCallback<FocusInEvent>(evt => placeholderLabel.style.display = DisplayStyle.None);
            textField.RegisterCallback<FocusOutEvent>(evt =>
            {
                if (string.IsNullOrEmpty(textField.value))
                    placeholderLabel.style.display = DisplayStyle.Flex;
            });

            if (!string.IsNullOrEmpty(textField.value))
                placeholderLabel.style.display = DisplayStyle.None;
        }

        #endregion
    }
}
