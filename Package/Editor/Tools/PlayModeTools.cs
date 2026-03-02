using System;
using UnityEditor;
using UnityEngine;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Manage play mode state: enter, exit, pause, step.
    /// </summary>
    [MCPTool("playmode", "Manage play mode state", Category = "Editor")]
    public static class PlayModeTools
    {
        [MCPAction("enter", Description = "Enter play mode")]
        public static object Enter()
        {
            try
            {
                if (EditorApplication.isPlaying)
                {
                    return new
                    {
                        success = true,
                        message = "Already in play mode.",
                        isPlaying = true,
                        isPaused = EditorApplication.isPaused
                    };
                }

                if (EditorApplication.isCompiling)
                {
                    return new
                    {
                        success = false,
                        error = "Cannot enter play mode while scripts are compiling.",
                        isPlaying = false,
                        isPaused = false
                    };
                }

                if (EditorApplication.isUpdating)
                {
                    return new
                    {
                        success = false,
                        error = "Cannot enter play mode while assets are importing.",
                        isPlaying = false,
                        isPaused = false
                    };
                }

                EditorApplication.isPlaying = true;

                return new
                {
                    success = true,
                    message = "Entering play mode.",
                    isPlaying = true,
                    isPaused = false
                };
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[PlayModeTools] Error entering play mode: {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error entering play mode: {exception.Message}"
                };
            }
        }

        [MCPAction("exit", Description = "Exit play mode")]
        public static object Exit()
        {
            try
            {
                if (!EditorApplication.isPlaying)
                {
                    return new
                    {
                        success = true,
                        message = "Already stopped (not in play mode).",
                        isPlaying = false,
                        isPaused = false
                    };
                }

                EditorApplication.isPlaying = false;

                return new
                {
                    success = true,
                    message = "Exiting play mode.",
                    isPlaying = false,
                    isPaused = false
                };
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[PlayModeTools] Error exiting play mode: {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error exiting play mode: {exception.Message}"
                };
            }
        }

        [MCPAction("pause", Description = "Toggle or set pause state")]
        public static object Pause(
            [MCPParam("paused", "Set pause state (true/false), omit to toggle")] bool? paused = null)
        {
            try
            {
                if (!EditorApplication.isPlaying)
                {
                    return new
                    {
                        success = false,
                        error = "Cannot pause when not in play mode. Use playmode with action='enter' first.",
                        isPlaying = false,
                        isPaused = false
                    };
                }

                bool newPauseState;
                string actionDescription;

                if (paused.HasValue)
                {
                    newPauseState = paused.Value;
                    if (EditorApplication.isPaused == newPauseState)
                    {
                        actionDescription = newPauseState ? "Already paused." : "Already running.";
                    }
                    else
                    {
                        EditorApplication.isPaused = newPauseState;
                        actionDescription = newPauseState ? "Play mode paused." : "Play mode resumed.";
                    }
                }
                else
                {
                    newPauseState = !EditorApplication.isPaused;
                    EditorApplication.isPaused = newPauseState;
                    actionDescription = newPauseState ? "Play mode paused." : "Play mode resumed.";
                }

                return new
                {
                    success = true,
                    message = actionDescription,
                    isPlaying = true,
                    isPaused = newPauseState
                };
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[PlayModeTools] Error toggling pause: {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error toggling pause: {exception.Message}"
                };
            }
        }

        [MCPAction("step", Description = "Advance single frame")]
        public static object Step()
        {
            try
            {
                if (!EditorApplication.isPlaying)
                {
                    return new
                    {
                        success = false,
                        error = "Cannot step when not in play mode. Use playmode with action='enter' first.",
                        isPlaying = false,
                        isPaused = false
                    };
                }

                EditorApplication.Step();

                return new
                {
                    success = true,
                    message = "Advanced one frame.",
                    isPlaying = true,
                    isPaused = true
                };
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[PlayModeTools] Error stepping frame: {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error stepping frame: {exception.Message}"
                };
            }
        }
    }
}
