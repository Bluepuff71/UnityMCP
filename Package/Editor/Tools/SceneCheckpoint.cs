using System;
using System.Collections.Generic;
using System.Linq;
using UnityMCP.Editor;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Services;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// MCP tools for saving, restoring, and comparing scene checkpoints.
    /// </summary>
    [MCPTool("scene_checkpoint", "Save, restore, or compare scene checkpoints for undo/restore. Call with action='save' before destructive operations (deleting objects, editing scripts, bulk modifications).", Category = "Scene")]
    public static class SceneCheckpoint
    {
        #region Save

        /// <summary>
        /// Saves the current scene state as a checkpoint.
        /// </summary>
        [MCPAction("save", Description = "Save a new scene checkpoint")]
        public static object Save(
            [MCPParam("name", "Optional name for the checkpoint")] string name = null,
            [MCPParam("new_bucket", "Start a new checkpoint bucket (true) or fold into current (false)")] bool newBucket = true)
        {
            try
            {
                // Check preconditions to provide specific error messages
                var activeScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
                if (!activeScene.IsValid())
                {
                    return new
                    {
                        success = false,
                        error = "Cannot save checkpoint: no valid active scene."
                    };
                }

                if (string.IsNullOrEmpty(activeScene.path))
                {
                    return new
                    {
                        success = false,
                        error = "Cannot save checkpoint: scene has no path. Save the scene first."
                    };
                }

                CheckpointMetadata metadata = CheckpointManager.SaveCheckpoint(name, newBucket);

                if (metadata == CheckpointManager.NothingToSave)
                {
                    return new
                    {
                        success = true,
                        message = "No changes to save — scene is clean and no assets were tracked."
                    };
                }

                if (metadata == null)
                {
                    return new
                    {
                        success = false,
                        error = "Failed to save checkpoint due to an unexpected error."
                    };
                }

                return new
                {
                    success = true,
                    message = $"Checkpoint '{metadata.name}' saved.",
                    checkpoint = metadata.ToSerializable()
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error saving checkpoint: {exception.Message}"
                };
            }
        }

        #endregion

        #region List

        /// <summary>
        /// Lists all existing checkpoints sorted by timestamp descending.
        /// </summary>
        [MCPAction("list", Description = "List all existing checkpoints", ReadOnlyHint = true)]
        public static object List()
        {
            try
            {
                List<CheckpointMetadata> checkpoints = CheckpointManager.ListCheckpoints();

                return new
                {
                    success = true,
                    count = checkpoints.Count,
                    checkpoints = checkpoints.Select(checkpoint => checkpoint.ToSerializable()).ToList()
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error listing checkpoints: {exception.Message}"
                };
            }
        }

        #endregion

        #region Restore

        /// <summary>
        /// Restores a previously saved scene checkpoint.
        /// Automatically creates a "before restore" checkpoint before restoring.
        /// </summary>
        [MCPAction("restore", Description = "Restore a previously saved scene checkpoint, including tracked asset snapshots. Automatically saves a safety checkpoint before restoring.", DestructiveHint = true)]
        public static object Restore(
            [MCPParam("checkpoint_id", "ID of the checkpoint to restore", required: true)] string checkpointId)
        {
            if (string.IsNullOrWhiteSpace(checkpointId))
            {
                throw MCPException.InvalidParams("checkpoint_id is required.");
            }

            try
            {
                // Get the current scene state for diff comparison
                string beforeSnapshotId = null;
                CheckpointMetadata beforeMetadata = CheckpointManager.SaveCheckpoint("Before restore (auto)", newBucket: true);
                if (beforeMetadata != null && beforeMetadata != CheckpointManager.NothingToSave)
                {
                    beforeSnapshotId = beforeMetadata.id;
                    // Freeze immediately to preserve as immutable safety net
                    CheckpointManager.FreezeCheckpoint(beforeMetadata.id);
                }

                // Restore the requested checkpoint
                CheckpointMetadata restoredMetadata = CheckpointManager.RestoreCheckpoint(checkpointId);
                if (restoredMetadata == null)
                {
                    return new
                    {
                        success = false,
                        error = $"Failed to restore checkpoint '{checkpointId}'. Checkpoint may not exist or scene file may be missing."
                    };
                }

                // Compute diff showing what changed
                object diffResult = null;
                if (beforeSnapshotId != null)
                {
                    CheckpointDiff diff = CheckpointManager.GetDiff(beforeSnapshotId, checkpointId);
                    if (diff != null)
                    {
                        diffResult = diff.ToSerializable();
                    }
                }

                var result = new Dictionary<string, object>
                {
                    { "success", true },
                    { "message", $"Restored checkpoint '{restoredMetadata.name}'." },
                    { "restored", restoredMetadata.ToSerializable() }
                };

                if (beforeSnapshotId != null)
                {
                    result["before_restore_id"] = beforeSnapshotId;
                }

                if (diffResult != null)
                {
                    result["diff"] = diffResult;
                }

                return result;
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error restoring checkpoint: {exception.Message}"
                };
            }
        }

        #endregion

        #region Diff

        /// <summary>
        /// Compares two checkpoints or the current scene against a checkpoint.
        /// Reports added and removed root objects and count changes.
        /// </summary>
        [MCPAction("diff", Description = "Compare two checkpoints or current scene vs a checkpoint. Use checkpoint_a='current' to compare live scene against a saved checkpoint. Reports root object and tracked asset differences.", ReadOnlyHint = true)]
        public static object Diff(
            [MCPParam("checkpoint_a", "First checkpoint ID (or 'current' for active scene)", required: true)] string checkpointA,
            [MCPParam("checkpoint_b", "Second checkpoint ID (or 'current' for active scene)")] string checkpointB = "current")
        {
            if (string.IsNullOrWhiteSpace(checkpointA))
            {
                throw MCPException.InvalidParams("checkpoint_a is required.");
            }

            try
            {
                CheckpointDiff diff = CheckpointManager.GetDiff(checkpointA, checkpointB);
                if (diff == null)
                {
                    return new
                    {
                        success = false,
                        error = "Failed to compute diff. One or both checkpoint IDs may be invalid."
                    };
                }

                return new
                {
                    success = true,
                    checkpoint_a = checkpointA,
                    checkpoint_b = checkpointB ?? "current",
                    diff = diff.ToSerializable()
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error computing diff: {exception.Message}"
                };
            }
        }

        #endregion
    }
}
