using System.Collections.Generic;
using UnityEngine.UIElements;
using UnityMCP.Editor.Core;

namespace UnityMCP.Editor.UI.Tabs
{
    /// <summary>
    /// Activity tab: live feed of tool calls with filtering and expandable detail.
    /// </summary>
    public class ActivityTab : ITab
    {
        private enum FilterMode { All, Success, Failed }

        public VisualElement Root { get; }

        private readonly ScrollView _feedScrollView;
        private readonly VisualElement _feedContainer;
        private readonly VisualElement _emptyState;
        private readonly Button _filterAll;
        private readonly Button _filterSuccess;
        private readonly Button _filterFailed;

        private FilterMode _currentFilter = FilterMode.All;
        private bool _isActive;
        private int _lastEntryCount;

        // Cache tool categories to avoid repeated lookups
        private readonly Dictionary<string, string> _categoryCache = new Dictionary<string, string>();

        public ActivityTab()
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1;

            // Toolbar row
            VisualElement toolbar = new VisualElement();
            toolbar.AddToClassList("row--spaced");
            toolbar.style.marginBottom = 6;

            // Filter pills (left)
            VisualElement filterBar = new VisualElement();
            filterBar.AddToClassList("filter-bar");

            _filterAll = CreateFilterPill("All", FilterMode.All);
            _filterSuccess = CreateFilterPill("Success", FilterMode.Success);
            _filterFailed = CreateFilterPill("Failed", FilterMode.Failed);

            filterBar.Add(_filterAll);
            filterBar.Add(_filterSuccess);
            filterBar.Add(_filterFailed);
            toolbar.Add(filterBar);

            // Right side: clear button
            Button clearButton = new Button(OnClear) { text = "Clear" };
            clearButton.AddToClassList("button--small");
            toolbar.Add(clearButton);

            Root.Add(toolbar);

            // Feed scroll view
            _feedScrollView = new ScrollView(ScrollViewMode.Vertical);
            _feedScrollView.AddToClassList("scroll-view");
            Root.Add(_feedScrollView);

            _feedContainer = _feedScrollView.contentContainer;

            // Empty state
            _emptyState = new VisualElement();
            _emptyState.AddToClassList("empty-state");
            Label emptyLabel = new Label("No tool calls recorded yet.\nActivity appears here when an AI client sends requests.");
            emptyLabel.AddToClassList("empty-state-text");
            _emptyState.Add(emptyLabel);

            // Set initial filter visual state (don't rebuild feed yet -- not in hierarchy)
            _filterAll.AddToClassList("filter-pill--active");
        }

        public void OnActivate()
        {
            _isActive = true;
            ActivityLog.OnEntryAdded += OnEntryAdded;
            RebuildFeed();
        }

        public void OnDeactivate()
        {
            _isActive = false;
            ActivityLog.OnEntryAdded -= OnEntryAdded;
        }

        public void Refresh()
        {
            // Only rebuild if entry count changed (handles clears from outside)
            if (ActivityLog.Entries.Count != _lastEntryCount)
            {
                RebuildFeed();
            }
        }

        private void OnEntryAdded()
        {
            if (!_isActive) return;
            RebuildFeed();
        }

        private void OnClear()
        {
            ActivityLog.Clear();
            RebuildFeed();
        }

        #region Category Lookup

        private string GetToolCategory(string toolName)
        {
            if (string.IsNullOrEmpty(toolName)) return null;

            if (_categoryCache.TryGetValue(toolName, out string cached))
                return cached;

            ToolDefinition definition = ToolRegistry.GetDefinition(toolName);
            string category = definition?.category;
            _categoryCache[toolName] = category;
            return category;
        }

        #endregion

        #region Filtering

        private Button CreateFilterPill(string label, FilterMode mode)
        {
            Button pill = new Button(() => SetFilter(mode)) { text = label };
            pill.AddToClassList("filter-pill");
            return pill;
        }

        private void SetFilter(FilterMode mode)
        {
            _currentFilter = mode;

            _filterAll.EnableInClassList("filter-pill--active", mode == FilterMode.All);
            _filterSuccess.EnableInClassList("filter-pill--active", mode == FilterMode.Success);
            _filterFailed.EnableInClassList("filter-pill--active", mode == FilterMode.Failed);

            RebuildFeed();
        }

        #endregion

        #region Feed Building

        private void RebuildFeed()
        {
            _feedContainer.Clear();
            _emptyState.RemoveFromHierarchy();

            IReadOnlyList<ActivityLog.Entry> entries = ActivityLog.Entries;
            _lastEntryCount = entries.Count;

            if (entries.Count == 0)
            {
                _feedScrollView.Add(_emptyState);
                return;
            }

            // Build newest first
            bool hasVisibleEntries = false;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                ActivityLog.Entry entry = entries[i];

                // Apply filter
                if (_currentFilter == FilterMode.Success && !entry.success) continue;
                if (_currentFilter == FilterMode.Failed && entry.success) continue;

                _feedContainer.Add(BuildEntryRow(entry));
                hasVisibleEntries = true;
            }

            if (!hasVisibleEntries)
            {
                VisualElement noResults = new VisualElement();
                noResults.AddToClassList("empty-state");
                string filterName = _currentFilter == FilterMode.Success ? "successful" : "failed";
                Label noResultsLabel = new Label($"No {filterName} calls recorded.");
                noResultsLabel.AddToClassList("empty-state-text");
                noResults.Add(noResultsLabel);
                _feedContainer.Add(noResults);
            }
        }

        private VisualElement BuildEntryRow(ActivityLog.Entry entry)
        {
            bool hasDetail = !string.IsNullOrEmpty(entry.detail);
            bool hasArguments = !string.IsNullOrEmpty(entry.argumentsSummary);
            bool isExpandable = hasDetail || hasArguments || entry.responseBytes > 0;

            VisualElement wrapper = new VisualElement();

            // === Line 1: Main row ===
            VisualElement row = new VisualElement();
            row.AddToClassList("activity-entry");
            if (!entry.success)
                row.AddToClassList("activity-entry--failed");

            // Disclosure arrow (only if expandable)
            if (isExpandable)
            {
                Label arrow = new Label("\u25B6");
                arrow.AddToClassList("activity-arrow");
                row.Add(arrow);
            }

            // Timestamp
            Label timestamp = new Label(entry.timestamp.ToString("HH:mm:ss"));
            timestamp.AddToClassList("activity-timestamp");
            timestamp.AddToClassList("mono");
            row.Add(timestamp);

            // Tool name + category + inline args
            VisualElement nameGroup = new VisualElement();
            nameGroup.AddToClassList("row");
            nameGroup.style.flexGrow = 1;
            nameGroup.style.overflow = Overflow.Hidden;

            Label toolName = new Label(entry.toolName);
            toolName.AddToClassList("activity-tool-name");
            nameGroup.Add(toolName);

            // Inline arguments summary (truncated, same line as tool name)
            if (hasArguments)
            {
                Label argsLabel = new Label(entry.argumentsSummary);
                argsLabel.AddToClassList("activity-args-inline");
                nameGroup.Add(argsLabel);
            }

            row.Add(nameGroup);

            // Status pill
            Label statusPill = new Label(entry.success ? "OK" : "FAIL");
            statusPill.AddToClassList("pill");
            statusPill.AddToClassList(entry.success ? "pill--success" : "pill--error");
            row.Add(statusPill);

            wrapper.Add(row);

            // === Expandable detail panel ===
            if (isExpandable)
            {
                VisualElement detailPanel = new VisualElement();
                detailPanel.AddToClassList("activity-detail-panel");
                detailPanel.style.display = DisplayStyle.None;

                // Detail metadata row: category + duration + response size
                VisualElement metaRow = new VisualElement();
                metaRow.AddToClassList("row");
                metaRow.style.marginBottom = 4;

                string category = GetToolCategory(entry.toolName);
                if (!string.IsNullOrEmpty(category))
                {
                    Label categoryPill = new Label(category);
                    categoryPill.AddToClassList("pill");
                    categoryPill.AddToClassList("pill--readonly");
                    metaRow.Add(categoryPill);
                }

                if (entry.durationMs > 0)
                {
                    string durationText = entry.durationMs >= 1000
                        ? $"{entry.durationMs / 1000.0:F1}s"
                        : $"{entry.durationMs}ms";
                    Label durationPill = new Label(durationText);
                    durationPill.AddToClassList("pill");
                    durationPill.AddToClassList("pill--duration");
                    metaRow.Add(durationPill);
                }

                if (entry.responseBytes > 0)
                {
                    string sizeText = entry.responseBytes >= 1024
                        ? $"{entry.responseBytes / 1024.0:F1} KB"
                        : $"{entry.responseBytes} B";
                    Label sizePill = new Label(sizeText);
                    sizePill.AddToClassList("pill");
                    sizePill.AddToClassList("pill--duration");
                    metaRow.Add(sizePill);
                }

                detailPanel.Add(metaRow);

                // Full arguments (wrapping, not truncated by CSS)
                if (hasArguments)
                {
                    Label fullArgsLabel = new Label(entry.argumentsSummary);
                    fullArgsLabel.AddToClassList("activity-detail-text");
                    fullArgsLabel.AddToClassList("mono");
                    detailPanel.Add(fullArgsLabel);
                }

                if (hasDetail)
                {
                    Label detailLabel = new Label(entry.detail);
                    detailLabel.AddToClassList("activity-detail-text");
                    detailPanel.Add(detailLabel);
                }

                wrapper.Add(detailPanel);

                // Click row to toggle detail
                row.RegisterCallback<ClickEvent>(evt =>
                {
                    bool isExpanded = detailPanel.style.display == DisplayStyle.Flex;
                    detailPanel.style.display = isExpanded ? DisplayStyle.None : DisplayStyle.Flex;

                    Label arrowLabel = row.Q<Label>(className: "activity-arrow");
                    if (arrowLabel != null)
                        arrowLabel.text = isExpanded ? "\u25B6" : "\u25BC";
                });

                row.style.cursor = StyleKeyword.Initial;
                row.AddToClassList("activity-entry--expandable");
            }

            return wrapper;
        }

        #endregion
    }
}
