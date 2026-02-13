using System;
using System.Threading;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityMCP.Editor.Services;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Tool for refreshing the Unity asset database and optionally requesting script compilation.
    /// </summary>
    public static class RefreshUnity
    {
        private const int RetryAfterMs = 5000;
        private const int WaitTimeoutMs = 60000;
        private const int WaitPollIntervalMs = 100;

        /// <summary>
        /// Refreshes the Unity asset database and optionally requests script compilation.
        /// </summary>
        [MCPTool("unity_refresh", "Refreshes Unity asset database and optionally requests script compilation", Category = "Editor", DestructiveHint = true)]
        public static object Refresh(
            [MCPParam("mode", "Refresh mode: force or if_dirty (default: if_dirty)")] string mode = "if_dirty",
            [MCPParam("scope", "What to refresh: all or scripts (default: all)")] string scope = "all",
            [MCPParam("compile", "Compilation request: none or request (default: none)")] string compile = "none",
            [MCPParam("wait_for_ready", "Wait for Unity to finish compiling/importing (default: false)")] bool waitForReady = false)
        {
            // Validate mode parameter
            string normalizedMode = (mode ?? "if_dirty").ToLowerInvariant().Trim();
            if (normalizedMode != "force" && normalizedMode != "if_dirty")
            {
                return new
                {
                    success = false,
                    error = $"Invalid mode: '{mode}'. Valid values are 'force' or 'if_dirty'."
                };
            }

            // Validate scope parameter
            string normalizedScope = (scope ?? "all").ToLowerInvariant().Trim();
            if (normalizedScope != "all" && normalizedScope != "scripts")
            {
                return new
                {
                    success = false,
                    error = $"Invalid scope: '{scope}'. Valid values are 'all' or 'scripts'."
                };
            }

            // Validate compile parameter
            string normalizedCompile = (compile ?? "none").ToLowerInvariant().Trim();
            if (normalizedCompile != "none" && normalizedCompile != "request")
            {
                return new
                {
                    success = false,
                    error = $"Invalid compile: '{compile}'. Valid values are 'none' or 'request'."
                };
            }

            // Check if tests are running
            if (TestJobManager.IsRunning)
            {
                return new
                {
                    success = false,
                    error = "Tests are currently running",
                    retry_after_ms = RetryAfterMs
                };
            }

            bool refreshTriggered = false;
            bool compileRequested = false;
            string hint = string.Empty;

            try
            {
                // Step 1: Asset database refresh for non-scripts scope
                if (normalizedScope != "scripts")
                {
                    if (normalizedMode == "force")
                    {
                        // Force refresh with synchronous import
                        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                        refreshTriggered = true;
                    }
                    else
                    {
                        // Only refresh if dirty (default behavior)
                        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                        refreshTriggered = true;
                    }
                }

                // Step 2: Request script compilation if requested
                if (normalizedCompile == "request")
                {
                    CompilationPipeline.RequestScriptCompilation();
                    compileRequested = true;
                }

                // Step 3: Wait for Unity to be ready if requested
                // Note: For Unity 6+, skip waiting if compile was requested due to domain reload issues
                bool shouldWait = waitForReady;

#if UNITY_6000_0_OR_NEWER
                // Unity 6+ has issues with waiting after compilation request due to domain reload
                if (compileRequested && waitForReady)
                {
                    shouldWait = false;
                    hint = "wait_for_ready skipped on Unity 6+ when compile is requested (domain reload issues). Poll unity_refresh with wait_for_ready=false to check state.";
                }
#endif

                if (shouldWait)
                {
                    bool isReady = WaitForUnityReady();
                    if (!isReady)
                    {
                        hint = "Timed out waiting for Unity to be ready. Unity may still be compiling or importing.";
                    }
                }

                // Determine resulting state
                string resultingState = GetCurrentUnityState();

                // Build success message
                string message;
                if (refreshTriggered && compileRequested)
                {
                    message = "Asset refresh and script compilation requested.";
                }
                else if (compileRequested)
                {
                    message = "Script compilation requested.";
                }
                else if (refreshTriggered)
                {
                    message = "Asset refresh completed.";
                }
                else
                {
                    message = "No action taken (scope was 'scripts' without compile='request').";
                }

                // Add state-specific hints
                if (string.IsNullOrEmpty(hint))
                {
                    hint = resultingState switch
                    {
                        "compiling" => "Unity is compiling scripts. Poll again to check when complete.",
                        "asset_import" => "Unity is importing assets. Poll again to check when complete.",
                        "idle" => "Unity refresh completed and editor is idle.",
                        _ => "Unity refresh completed."
                    };
                }

                return new
                {
                    success = true,
                    message,
                    refresh_triggered = refreshTriggered,
                    compile_requested = compileRequested,
                    resulting_state = resultingState,
                    hint
                };
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[RefreshUnity] Error during refresh: {exception.Message}");
                return new
                {
                    success = false,
                    error = $"Error during refresh: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Waits for Unity to be ready (not compiling, not importing, not playing, tests not running).
        /// </summary>
        /// <returns>True if Unity became ready within the timeout, false otherwise.</returns>
        private static bool WaitForUnityReady()
        {
            int elapsedMs = 0;

            while (elapsedMs < WaitTimeoutMs)
            {
                if (IsUnityReady())
                {
                    return true;
                }

                // Sleep briefly and pump the editor update
                Thread.Sleep(WaitPollIntervalMs);
                elapsedMs += WaitPollIntervalMs;

                // Allow editor to process pending operations
                // Note: This is synchronous waiting - in a real async scenario,
                // we'd use EditorApplication.update callbacks
            }

            return false;
        }

        /// <summary>
        /// Checks if Unity is ready (not busy with any operations).
        /// </summary>
        private static bool IsUnityReady()
        {
            // Check if compiling
            if (EditorApplication.isCompiling)
            {
                return false;
            }

            // Check if updating (importing assets)
            if (EditorApplication.isUpdating)
            {
                return false;
            }

            // Check if tests are running
            if (TestJobManager.IsRunning)
            {
                return false;
            }

            // Check if playing or about to change play mode
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets the current Unity editor state.
        /// </summary>
        private static string GetCurrentUnityState()
        {
            if (EditorApplication.isCompiling)
            {
                return "compiling";
            }

            if (EditorApplication.isUpdating)
            {
                return "asset_import";
            }

            return "idle";
        }
    }
}
