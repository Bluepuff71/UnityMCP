using UnityMCP.Editor;
using UnityMCP.Editor.Core;

namespace UnityMCP.Editor.Resources.UI
{
    /// <summary>
    /// Resource provider that serves the scene preview HTML widget.
    /// </summary>
    public static class ScenePreviewApp
    {
        /// <summary>
        /// Gets the scene preview widget HTML for inline display in MCP clients.
        /// </summary>
        /// <returns>HTML content wrapped in a ResourceContent with text/html mime type.</returns>
        [MCPResource("ui://unitymcp/scene-preview.html", "Scene preview widget HTML for inline display")]
        public static ResourceContent GetScenePreview()
        {
            return ResourceContent.Text("ui://unitymcp/scene-preview.html", MCPApps.ScenePreviewHtml, "text/html");
        }
    }
}
