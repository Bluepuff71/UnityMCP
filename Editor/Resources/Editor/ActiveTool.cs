using UnityEditor;

namespace UnityMCP.Editor.Resources.Editor
{
    /// <summary>
    /// Resource provider for the currently active editor tool.
    /// </summary>
    public static class ActiveTool
    {
        /// <summary>
        /// Gets information about the currently active editor tool.
        /// </summary>
        /// <returns>Object containing the current tool and view tool information.</returns>
        [MCPResource("editor://active_tool", "Currently active editor tool (Move, Rotate, Scale, etc.)")]
        public static object GetActiveTool()
        {
            return new
            {
                tool = UnityEditor.Tools.current.ToString(),
                viewTool = UnityEditor.Tools.viewTool.ToString(),
                pivotMode = UnityEditor.Tools.pivotMode.ToString(),
                pivotRotation = UnityEditor.Tools.pivotRotation.ToString(),
                handlePosition = new
                {
                    x = UnityEditor.Tools.handlePosition.x,
                    y = UnityEditor.Tools.handlePosition.y,
                    z = UnityEditor.Tools.handlePosition.z
                },
                handleRotation = new
                {
                    x = UnityEditor.Tools.handleRotation.eulerAngles.x,
                    y = UnityEditor.Tools.handleRotation.eulerAngles.y,
                    z = UnityEditor.Tools.handleRotation.eulerAngles.z
                }
            };
        }
    }
}
