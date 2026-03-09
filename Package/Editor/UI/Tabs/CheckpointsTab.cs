using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityMCP.Editor.Services;

namespace UnityMCP.Editor.UI.Tabs
{
    /// <summary>
    /// Checkpoints tab: save, restore, and diff scene checkpoints.
    /// Displays bucket metadata including frozen/active status and tracked assets.
    /// </summary>
    public class CheckpointsTab : ITab
    {
        public VisualElement Root { get; }

        private readonly TextField _nameField;
        private readonly ScrollView _listScrollView;
        private readonly VisualElement _listContainer;
        private readonly VisualElement _emptyState;
        private readonly Label _pendingIndicatorLabel;
        private readonly VisualElement _pendingIndicator;

        private List<CheckpointMetadata> _cachedCheckpoints;
        private bool _isActive;

        public CheckpointsTab()
        {
            Root = new VisualElement();
            Root.style.flexGrow = 1;

            // Toolbar row: name field + save button
            VisualElement toolbar = new VisualElement();
            toolbar.AddToClassList("row");
            toolbar.style.marginBottom = 4;

            _nameField = new TextField();
            _nameField.style.flexGrow = 1;
            _nameField.style.marginRight = 4;
            toolbar.Add(_nameField);

            // Set placeholder
            SetPlaceholder(_nameField, "Checkpoint name (optional)");

            Button saveButton = new Button(OnSave) { text = "Save Checkpoint" };
            saveButton.AddToClassList("button--accent");
            toolbar.Add(saveButton);

            Root.Add(toolbar);

            // Pending tracked assets indicator
            _pendingIndicator = new VisualElement();
            _pendingIndicator.AddToClassList("checkpoint-pending");
            _pendingIndicator.style.display = DisplayStyle.None;

            _pendingIndicatorLabel = new Label();
            _pendingIndicatorLabel.AddToClassList("checkpoint-pending__label");
            _pendingIndicator.Add(_pendingIndicatorLabel);

            Root.Add(_pendingIndicator);

            // List scroll view
            _listScrollView = new ScrollView(ScrollViewMode.Vertical);
            _listScrollView.AddToClassList("scroll-view");
            Root.Add(_listScrollView);

            _listContainer = _listScrollView.contentContainer;

            // Empty state
            _emptyState = new VisualElement();
            _emptyState.AddToClassList("empty-state");
            Label emptyLabel = new Label("No checkpoints saved.\nUse the button above or call manage_checkpoint from an AI client.");
            emptyLabel.AddToClassList("empty-state-text");
            _emptyState.Add(emptyLabel);
        }

        public void OnActivate()
        {
            _isActive = true;
            RebuildList();
            UpdatePendingIndicator();
        }

        public void OnDeactivate()
        {
            _isActive = false;
        }

        public void Refresh()
        {
            if (!_isActive) return;
            UpdatePendingIndicator();
        }

        private void OnSave()
        {
            string checkpointName = string.IsNullOrWhiteSpace(_nameField.value) ? null : _nameField.value.Trim();

            try
            {
                CheckpointMetadata metadata = CheckpointManager.SaveCheckpoint(checkpointName);
                if (metadata == CheckpointManager.NothingToSave)
                {
                    Debug.Log("[MCPServerWindow] Nothing to save — scene is clean and no assets were tracked.");
                }
                else if (metadata != null)
                {
                    _nameField.value = "";
                    RebuildList();
                    UpdatePendingIndicator();
                }
                else
                {
                    Debug.LogWarning("[MCPServerWindow] Failed to save checkpoint. Ensure the scene is saved.");
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"[MCPServerWindow] Error saving checkpoint: {exception.Message}");
            }
        }

        #region Pending Indicator

        private void UpdatePendingIndicator()
        {
            IReadOnlyCollection<string> pendingTracks = CheckpointManager.PendingTracks;
            int pendingCount = pendingTracks.Count;

            if (pendingCount > 0)
            {
                _pendingIndicator.style.display = DisplayStyle.Flex;
                _pendingIndicatorLabel.text = $"pending: {pendingCount} asset{(pendingCount != 1 ? "s" : "")} tracked";
            }
            else
            {
                _pendingIndicator.style.display = DisplayStyle.None;
            }
        }

        #endregion

        #region List Building

        private void RebuildList()
        {
            _listContainer.Clear();
            _emptyState.RemoveFromHierarchy();

            try
            {
                _cachedCheckpoints = CheckpointManager.ListCheckpoints();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[MCPServerWindow] Error listing checkpoints: {exception.Message}");
                _cachedCheckpoints = new List<CheckpointMetadata>();
            }

            if (_cachedCheckpoints.Count == 0)
            {
                _listScrollView.Add(_emptyState);
                return;
            }

            foreach (CheckpointMetadata checkpoint in _cachedCheckpoints)
            {
                _listContainer.Add(BuildCheckpointEntry(checkpoint));
            }
        }

        private VisualElement BuildCheckpointEntry(CheckpointMetadata checkpoint)
        {
            VisualElement entry = new VisualElement();
            entry.AddToClassList("checkpoint-entry");

            // Main row
            VisualElement mainRow = new VisualElement();
            mainRow.AddToClassList("row--spaced");

            // Left: name + scene + badges
            VisualElement leftColumn = new VisualElement();
            leftColumn.style.flexGrow = 1;
            leftColumn.style.overflow = Overflow.Hidden;

            // Name row with status badge
            VisualElement nameRow = new VisualElement();
            nameRow.AddToClassList("row");

            Label nameLabel = new Label(checkpoint.name);
            nameLabel.AddToClassList("checkpoint-name");
            nameRow.Add(nameLabel);

            // Active/Frozen badge
            Label statusBadge = new Label(checkpoint.isFrozen ? "Frozen" : "Active");
            statusBadge.AddToClassList("pill");
            statusBadge.AddToClassList(checkpoint.isFrozen ? "pill--stat" : "pill--success");
            statusBadge.style.marginLeft = 6;
            nameRow.Add(statusBadge);

            // Tracked asset count badge
            int trackedCount = checkpoint.trackedAssetPaths?.Count ?? 0;
            if (trackedCount > 0)
            {
                Label assetCountBadge = new Label($"+{trackedCount} asset{(trackedCount != 1 ? "s" : "")}");
                assetCountBadge.AddToClassList("pill");
                assetCountBadge.AddToClassList("pill--readonly");
                assetCountBadge.style.marginLeft = 4;
                nameRow.Add(assetCountBadge);
            }

            leftColumn.Add(nameRow);

            Label sceneLabel = new Label(checkpoint.sceneName);
            sceneLabel.AddToClassList("checkpoint-scene");
            leftColumn.Add(sceneLabel);

            Label idLabel = new Label(checkpoint.id);
            idLabel.AddToClassList("checkpoint-id");
            idLabel.AddToClassList("mono");
            leftColumn.Add(idLabel);

            mainRow.Add(leftColumn);

            // Center: timestamp
            VisualElement centerColumn = new VisualElement();
            centerColumn.style.flexShrink = 0;
            centerColumn.style.marginLeft = 8;
            centerColumn.style.marginRight = 8;

            Label timeLabel = new Label(checkpoint.timestamp.ToString("MMM dd, h:mm tt"));
            timeLabel.AddToClassList("checkpoint-time");
            centerColumn.Add(timeLabel);

            string relativeTime = GetRelativeTime(checkpoint.timestamp);
            Label relativeLabel = new Label(relativeTime);
            relativeLabel.AddToClassList("muted");
            centerColumn.Add(relativeLabel);

            mainRow.Add(centerColumn);

            // Right: action buttons
            VisualElement actionColumn = new VisualElement();
            actionColumn.AddToClassList("checkpoint-actions");

            string capturedId = checkpoint.id;

            // Assets button (only if there are tracked assets)
            if (trackedCount > 0)
            {
                Button assetsButton = new Button(() => OnToggleAssets(entry, checkpoint)) { text = "Assets" };
                assetsButton.AddToClassList("button--small");
                assetsButton.style.marginRight = 2;
                actionColumn.Add(assetsButton);
            }

            Button diffButton = new Button(() => OnDiff(entry, capturedId)) { text = "Diff" };
            diffButton.AddToClassList("button--small");
            diffButton.AddToClassList("button--accent");
            actionColumn.Add(diffButton);

            Button restoreButton = new Button(() => OnRestoreClicked(entry, capturedId)) { text = "Restore" };
            restoreButton.AddToClassList("button--small");
            restoreButton.AddToClassList("button--warning");
            actionColumn.Add(restoreButton);

            mainRow.Add(actionColumn);
            entry.Add(mainRow);

            return entry;
        }

        #endregion

        #region Tracked Assets Panel

        private void OnToggleAssets(VisualElement entry, CheckpointMetadata checkpoint)
        {
            // Toggle: remove if already showing
            VisualElement existing = entry.Q(name: "assets-panel");
            if (existing != null)
            {
                existing.RemoveFromHierarchy();
                return;
            }

            if (checkpoint.trackedAssetPaths == null || checkpoint.trackedAssetPaths.Count == 0)
            {
                return;
            }

            VisualElement assetsPanel = new VisualElement();
            assetsPanel.name = "assets-panel";
            assetsPanel.AddToClassList("checkpoint-assets");

            Label headerLabel = new Label("TRACKED ASSETS");
            headerLabel.AddToClassList("checkpoint-assets__header");
            assetsPanel.Add(headerLabel);

            foreach (string assetPath in checkpoint.trackedAssetPaths)
            {
                Label pathLabel = new Label(assetPath);
                pathLabel.AddToClassList("checkpoint-assets__path");
                assetsPanel.Add(pathLabel);
            }

            entry.Add(assetsPanel);
        }

        #endregion

        #region Restore Flow

        private void OnRestoreClicked(VisualElement entry, string checkpointId)
        {
            // Remove any existing confirmation panel
            RemoveExpandablePanel(entry, "restore-confirm");

            VisualElement confirmPanel = new VisualElement();
            confirmPanel.name = "restore-confirm";
            confirmPanel.AddToClassList("checkpoint-confirm");

            Label confirmText = new Label("Restore to this checkpoint? An auto-checkpoint is saved first.");
            confirmText.AddToClassList("muted");
            confirmText.style.whiteSpace = WhiteSpace.Normal;
            confirmText.style.marginBottom = 4;
            confirmPanel.Add(confirmText);

            VisualElement buttonRow = new VisualElement();
            buttonRow.AddToClassList("row");

            Button confirmButton = new Button(() =>
            {
                try
                {
                    // Save auto-checkpoint before restoring (safety net)
                    CheckpointMetadata beforeMetadata = CheckpointManager.SaveCheckpoint("Before restore (auto)", newBucket: true);
                    if (beforeMetadata != null && beforeMetadata != CheckpointManager.NothingToSave)
                    {
                        CheckpointManager.FreezeCheckpoint(beforeMetadata.id);
                    }

                    CheckpointMetadata restored = CheckpointManager.RestoreCheckpoint(checkpointId);
                    if (restored != null)
                    {
                        RebuildList();
                        UpdatePendingIndicator();
                    }
                    else
                    {
                        Debug.LogWarning("[MCPServerWindow] Failed to restore checkpoint.");
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[MCPServerWindow] Error restoring checkpoint: {exception.Message}");
                }
            }) { text = "Confirm" };
            confirmButton.AddToClassList("button--small");
            confirmButton.AddToClassList("button--warning");
            buttonRow.Add(confirmButton);

            Button cancelButton = new Button(() => RemoveExpandablePanel(entry, "restore-confirm")) { text = "Cancel" };
            cancelButton.AddToClassList("button--small");
            cancelButton.style.marginLeft = 4;
            buttonRow.Add(cancelButton);

            confirmPanel.Add(buttonRow);
            entry.Add(confirmPanel);
        }

        #endregion

        #region Diff Flow

        private void OnDiff(VisualElement entry, string checkpointId)
        {
            // Toggle: remove if already showing
            VisualElement existing = entry.Q(name: "diff-panel");
            if (existing != null)
            {
                existing.RemoveFromHierarchy();
                return;
            }

            try
            {
                CheckpointDiff diff = CheckpointManager.GetDiff(checkpointId, "current");
                if (diff == null)
                {
                    Debug.LogWarning("[MCPServerWindow] Failed to compute diff.");
                    return;
                }

                VisualElement diffPanel = new VisualElement();
                diffPanel.name = "diff-panel";
                diffPanel.AddToClassList("checkpoint-diff");

                // Count changes
                Label countLabel = new Label($"Root objects: {diff.rootCountA} \u2192 {diff.rootCountB}");
                countLabel.AddToClassList("diff-changed");
                diffPanel.Add(countLabel);

                // Added objects
                if (diff.addedObjects != null && diff.addedObjects.Count > 0)
                {
                    foreach (string added in diff.addedObjects)
                    {
                        Label addedLabel = new Label($"+ {added}");
                        addedLabel.AddToClassList("diff-added");
                        diffPanel.Add(addedLabel);
                    }
                }

                // Removed objects
                if (diff.removedObjects != null && diff.removedObjects.Count > 0)
                {
                    foreach (string removed in diff.removedObjects)
                    {
                        Label removedLabel = new Label($"- {removed}");
                        removedLabel.AddToClassList("diff-removed");
                        diffPanel.Add(removedLabel);
                    }
                }

                // Tracked asset differences
                bool hasTrackedA = diff.trackedAssetsA != null && diff.trackedAssetsA.Count > 0;
                bool hasTrackedB = diff.trackedAssetsB != null && diff.trackedAssetsB.Count > 0;

                if (hasTrackedA || hasTrackedB)
                {
                    VisualElement assetDiffDivider = new VisualElement();
                    assetDiffDivider.AddToClassList("checkpoint-assets__divider");
                    diffPanel.Add(assetDiffDivider);

                    Label assetDiffHeader = new Label("TRACKED ASSETS");
                    assetDiffHeader.AddToClassList("checkpoint-assets__header");
                    diffPanel.Add(assetDiffHeader);

                    if (hasTrackedA)
                    {
                        Label checkpointAssetsLabel = new Label($"Checkpoint: {diff.trackedAssetsA.Count} asset{(diff.trackedAssetsA.Count != 1 ? "s" : "")}");
                        checkpointAssetsLabel.AddToClassList("muted");
                        diffPanel.Add(checkpointAssetsLabel);
                    }

                    if (hasTrackedB)
                    {
                        Label currentAssetsLabel = new Label($"Current: {diff.trackedAssetsB.Count} asset{(diff.trackedAssetsB.Count != 1 ? "s" : "")}");
                        currentAssetsLabel.AddToClassList("muted");
                        diffPanel.Add(currentAssetsLabel);
                    }
                }

                // No changes
                if ((diff.addedObjects == null || diff.addedObjects.Count == 0) &&
                    (diff.removedObjects == null || diff.removedObjects.Count == 0) &&
                    !hasTrackedA && !hasTrackedB)
                {
                    Label noChanges = new Label("No changes detected.");
                    noChanges.AddToClassList("muted");
                    diffPanel.Add(noChanges);
                }

                entry.Add(diffPanel);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[MCPServerWindow] Error computing diff: {exception.Message}");
            }
        }

        #endregion

        #region Helpers

        private static void RemoveExpandablePanel(VisualElement parent, string panelName)
        {
            VisualElement existing = parent.Q(name: panelName);
            existing?.RemoveFromHierarchy();
        }

        private static string GetRelativeTime(DateTime timestamp)
        {
            TimeSpan elapsed = DateTime.Now - timestamp;

            if (elapsed.TotalSeconds < 60) return "just now";
            if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes} min ago";
            if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
            if (elapsed.TotalDays < 7) return $"{(int)elapsed.TotalDays}d ago";
            return timestamp.ToString("MMM dd");
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
        }

        #endregion
    }
}
