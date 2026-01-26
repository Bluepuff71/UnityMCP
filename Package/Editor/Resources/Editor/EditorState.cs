using UnityEditor;
using UnityEngine;

namespace UnityMCP.Editor.Resources.Editor
{
    /// <summary>
    /// Resource provider for the current editor state.
    /// </summary>
    public static class EditorState
    {
        /// <summary>
        /// Gets comprehensive information about the current editor state.
        /// </summary>
        /// <returns>Object containing editor state information.</returns>
        [MCPResource("editor://state", "Current editor state (play mode, compiling, focus, etc.)")]
        public static object GetEditorState()
        {
            return new
            {
                playMode = new
                {
                    isPlaying = EditorApplication.isPlaying,
                    isPaused = EditorApplication.isPaused,
                    isPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode
                },
                compilation = new
                {
                    isCompiling = EditorApplication.isCompiling,
                    isUpdating = EditorApplication.isUpdating
                },
                focus = new
                {
                    applicationHasFocus = UnityEditorInternal.InternalEditorUtility.isApplicationActive,
                    isRemoteConnected = EditorApplication.isRemoteConnected
                },
                time = new
                {
                    timeSinceStartup = EditorApplication.timeSinceStartup
                },
                project = new
                {
                    applicationPath = EditorApplication.applicationPath,
                    applicationContentsPath = EditorApplication.applicationContentsPath,
                    isTemporaryProject = EditorApplication.isTemporaryProject,
                    unityVersion = Application.unityVersion,
                    platform = Application.platform.ToString()
                }
            };
        }
    }
}
