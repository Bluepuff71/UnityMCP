using UnityEngine.UIElements;

namespace UnityMCP.Editor.UI.Tabs
{
    /// <summary>
    /// Interface for a tab in the MCP Server Window.
    /// Each tab owns a root VisualElement and manages its own lifecycle.
    /// </summary>
    public interface ITab
    {
        /// <summary>
        /// The root VisualElement for this tab's content.
        /// </summary>
        VisualElement Root { get; }

        /// <summary>
        /// Called when this tab becomes the active/visible tab.
        /// Use for subscribing to events and rebuilding content.
        /// </summary>
        void OnActivate();

        /// <summary>
        /// Called when this tab is hidden (another tab selected).
        /// Use for unsubscribing from events and cleaning up.
        /// </summary>
        void OnDeactivate();

        /// <summary>
        /// Called periodically (~10/sec) while the tab is active.
        /// Use for lightweight status updates (not full rebuilds).
        /// </summary>
        void Refresh();
    }
}
