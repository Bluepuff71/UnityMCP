using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMCP.Editor.Services
{
    /// <summary>
    /// Metadata for a scene checkpoint bucket, stored alongside the saved scene file.
    /// Each bucket captures a scene snapshot and optionally tracked asset snapshots.
    /// </summary>
    [Serializable]
    public class CheckpointMetadata
    {
        public string id;
        public string name;
        public DateTime timestamp;
        public string sceneName;
        public string scenePath;
        public int rootObjectCount;
        public int totalObjectCount;
        public List<string> rootObjectNames = new List<string>();
        public List<string> trackedAssetPaths = new List<string>();
        public bool isFrozen;

        /// <summary>
        /// Converts metadata to a serializable format for JSON output.
        /// Omits null/empty fields to minimize token usage.
        /// </summary>
        public object ToSerializable()
        {
            var result = new Dictionary<string, object>
            {
                { "id", id },
                { "name", name },
                { "timestamp", timestamp.ToString("o") },
                { "scene_name", sceneName },
                { "scene_path", scenePath },
                { "root_object_count", rootObjectCount },
                { "total_object_count", totalObjectCount }
            };

            if (trackedAssetPaths != null && trackedAssetPaths.Count > 0)
            {
                result["tracked_asset_count"] = trackedAssetPaths.Count;
            }

            result["is_frozen"] = isFrozen;

            return result;
        }
    }

    /// <summary>
    /// Result of comparing two checkpoint states, reporting added and removed root objects
    /// and tracked asset differences.
    /// </summary>
    public class CheckpointDiff
    {
        public List<string> addedObjects = new List<string>();
        public List<string> removedObjects = new List<string>();
        public int rootCountA;
        public int rootCountB;
        public int totalCountA;
        public int totalCountB;
        public List<string> trackedAssetsA = new List<string>();
        public List<string> trackedAssetsB = new List<string>();

        /// <summary>
        /// Converts the diff result to a serializable format for JSON output.
        /// Omits empty collections to minimize token usage.
        /// </summary>
        public object ToSerializable()
        {
            var result = new Dictionary<string, object>
            {
                { "root_count_a", rootCountA },
                { "root_count_b", rootCountB },
                { "root_count_diff", rootCountB - rootCountA },
                { "total_count_a", totalCountA },
                { "total_count_b", totalCountB },
                { "total_count_diff", totalCountB - totalCountA }
            };

            if (addedObjects.Count > 0)
            {
                result["added"] = addedObjects;
            }

            if (removedObjects.Count > 0)
            {
                result["removed"] = removedObjects;
            }

            if (trackedAssetsA.Count > 0)
            {
                const int maxAssets = 30;
                result["tracked_assets_a"] = trackedAssetsA.Count <= maxAssets
                    ? trackedAssetsA
                    : trackedAssetsA.Take(maxAssets).ToList();
                if (trackedAssetsA.Count > maxAssets)
                    result["tracked_assets_a_total"] = trackedAssetsA.Count;
            }

            if (trackedAssetsB.Count > 0)
            {
                const int maxAssets = 30;
                result["tracked_assets_b"] = trackedAssetsB.Count <= maxAssets
                    ? trackedAssetsB
                    : trackedAssetsB.Take(maxAssets).ToList();
                if (trackedAssetsB.Count > maxAssets)
                    result["tracked_assets_b_total"] = trackedAssetsB.Count;
            }

            return result;
        }
    }

    /// <summary>
    /// Manages scene checkpoint storage using a bucket model for save/restore/diff operations.
    /// Each bucket captures a scene file copy, metadata JSON, and optionally tracked asset snapshots.
    /// Buckets are either active (mutable, accepting new tracked assets) or frozen (immutable).
    /// File-based storage in a temp directory survives domain reloads.
    /// </summary>
    public static class CheckpointManager
    {
        #region Sentinels

        /// <summary>
        /// Sentinel returned by SaveCheckpoint when there is nothing to save
        /// (no pending tracks and scene is clean). Distinguishes this from null,
        /// which indicates an actual error.
        /// </summary>
        public static readonly CheckpointMetadata NothingToSave = new CheckpointMetadata();

        #endregion

        #region Constants

        /// <summary>
        /// Maximum number of buckets to keep. Oldest frozen buckets are evicted when exceeded.
        /// </summary>
        private const int MaxBuckets = 20;

        /// <summary>
        /// Maximum age for frozen buckets before they are eligible for age-based eviction.
        /// </summary>
        private static readonly TimeSpan MaxBucketAge = TimeSpan.FromHours(24);

        private static readonly object _lock = new object();

        #endregion

        #region Track API

        /// <summary>
        /// Paths of assets pending inclusion in the next checkpoint save.
        /// Populated via Track() calls; consumed by SaveCheckpoint().
        /// </summary>
        private static readonly HashSet<string> _pendingTrackedPaths = new HashSet<string>();

        /// <summary>
        /// Registers a Unity object's asset path for inclusion in the next checkpoint.
        /// Must be called from the main thread (uses AssetDatabase.GetAssetPath).
        /// For off-main-thread callers, use the <see cref="Track(string)"/> overload instead.
        /// </summary>
        /// <param name="obj">A Unity object that has an asset path (e.g., material, prefab).</param>
        public static void Track(UnityEngine.Object obj)
        {
            if (obj == null) return;
            string assetPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(assetPath))
                lock (_lock) { _pendingTrackedPaths.Add(assetPath); }
        }

        /// <summary>
        /// Registers an asset path for inclusion in the next checkpoint.
        /// Thread-safe; can be called from any thread.
        /// </summary>
        /// <param name="assetPath">An asset path relative to the project root (e.g., "Assets/Materials/Foo.mat").</param>
        public static void Track(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            lock (_lock) { _pendingTrackedPaths.Add(assetPath); }
        }

        /// <summary>
        /// Returns true if there are any pending tracked asset paths awaiting checkpoint save.
        /// </summary>
        public static bool HasPendingTracks
        {
            get { lock (_lock) { return _pendingTrackedPaths.Count > 0; } }
        }

        /// <summary>
        /// Returns a snapshot copy of all pending tracked asset paths.
        /// </summary>
        public static IReadOnlyCollection<string> PendingTracks
        {
            get { lock (_lock) { return new List<string>(_pendingTrackedPaths); } }
        }

        /// <summary>
        /// Atomically consumes and returns the current set of pending tracked paths, clearing the internal set.
        /// Caller must hold _lock.
        /// </summary>
        private static HashSet<string> ConsumePendingTracks()
        {
            Debug.Assert(Monitor.IsEntered(_lock), "ConsumePendingTracks must be called with _lock held");
            var consumed = new HashSet<string>(_pendingTrackedPaths);
            _pendingTrackedPaths.Clear();
            return consumed;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the directory where checkpoint buckets are stored.
        /// Creates the directory if it does not exist.
        /// </summary>
        private static string CheckpointDirectory
        {
            get
            {
                string directory = Path.Combine(Path.GetTempPath(), "UnityMCP", "Checkpoints");
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                return directory;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Saves a checkpoint bucket of the current scene and tracked assets.
        /// If newBucket is false and an active (non-frozen) bucket exists, merges into it.
        /// If newBucket is true or no active bucket exists, creates a new bucket and freezes the old one.
        /// Returns null if there are no changes to save (no pending tracks and scene is not dirty).
        /// </summary>
        /// <param name="name">Optional human-readable name for the checkpoint.</param>
        /// <param name="newBucket">If true, always creates a new bucket (freezing the current active one).</param>
        /// <returns>The metadata for the saved/updated checkpoint, or null if nothing to save.</returns>
        public static CheckpointMetadata SaveCheckpoint(string name = null, bool newBucket = false)
        {
            lock (_lock)
            {
                Scene activeScene = EditorSceneManager.GetActiveScene();
                if (!activeScene.IsValid())
                {
                    Debug.LogWarning("[CheckpointManager] Cannot save checkpoint: no valid active scene.");
                    return null;
                }

                if (string.IsNullOrEmpty(activeScene.path))
                {
                    Debug.LogWarning("[CheckpointManager] Cannot save checkpoint: scene has no path. Save the scene first.");
                    return null;
                }

                // Step 1: Consume pending tracks
                HashSet<string> consumedTracks = ConsumePendingTracks();

                // Step 2: Remove the active scene path from tracked assets (scene is always handled separately)
                consumedTracks.Remove(activeScene.path);

                // Step 3: Check if there is anything to save
                bool sceneIsDirty = activeScene.isDirty;
                if (consumedTracks.Count == 0 && !sceneIsDirty)
                {
                    return NothingToSave;
                }

                // Step 4: Capture root info BEFORE SaveScene to avoid phantom duplicates
                // caused by Unity's post-save instance ID reconciliation
                var rootInfo = CaptureRootInfo(activeScene);

                // Save current scene state to disk before copying (only if scene has unsaved changes)
                if (sceneIsDirty)
                {
                    EditorSceneManager.SaveScene(activeScene);
                }

                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                string fullScenePath = Path.Combine(projectRoot, activeScene.path);

                // Find the current active (non-frozen) bucket, if any
                CheckpointMetadata activeBucket = FindActiveBucket();

                if (!newBucket && activeBucket != null)
                {
                    // Step 5: Merge into existing active bucket
                    return MergeIntoActiveBucket(activeBucket, activeScene, fullScenePath, consumedTracks, rootInfo);
                }
                else
                {
                    // Step 6: Freeze ALL unfrozen buckets and create a new one
                    FreezeAllUnfrozenBuckets();

                    CheckpointMetadata newMetadata = CreateNewBucket(name, activeScene, fullScenePath, consumedTracks, rootInfo);

                    if (newMetadata != null)
                    {
                        Debug.Log($"[CheckpointManager] Created new bucket '{newMetadata.id}' (name='{newMetadata.name}', trackedAssets={newMetadata.trackedAssetPaths?.Count ?? 0}, sceneIsDirty={sceneIsDirty})");
                        // Step 7: Enforce retention limits
                        EnforceRetentionLimits();
                    }
                    else
                    {
                        Debug.LogWarning($"[CheckpointManager] CreateNewBucket returned null (name='{name}', trackedCount={consumedTracks.Count}, sceneIsDirty={sceneIsDirty})");
                    }

                    return newMetadata;
                }
            }
        }

        /// <summary>
        /// Lists all available checkpoints sorted by timestamp descending (newest first).
        /// </summary>
        /// <returns>List of checkpoint metadata objects.</returns>
        public static List<CheckpointMetadata> ListCheckpoints()
        {
            lock (_lock)
            {
                return ListCheckpointsInternal();
            }
        }

        /// <summary>
        /// Restores a previously saved checkpoint by copying its scene file and tracked assets back.
        /// </summary>
        /// <param name="checkpointId">The ID of the checkpoint to restore.</param>
        /// <returns>The metadata of the restored checkpoint, or null on failure.</returns>
        public static CheckpointMetadata RestoreCheckpoint(string checkpointId)
        {
            lock (_lock)
            {
                CheckpointMetadata metadata = GetCheckpointMetadata(checkpointId);
                if (metadata == null)
                {
                    Debug.LogWarning($"[CheckpointManager] Checkpoint not found: {checkpointId}");
                    return null;
                }

                string checkpointScenePath = Path.Combine(CheckpointDirectory, $"{checkpointId}.unity");
                if (!File.Exists(checkpointScenePath))
                {
                    Debug.LogWarning($"[CheckpointManager] Checkpoint scene file not found: {checkpointScenePath}");
                    return null;
                }

                // Resolve the original scene path
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                string originalFullPath = Path.Combine(projectRoot, metadata.scenePath);

                try
                {
                    // Copy checkpoint scene file back to the original location
                    File.Copy(checkpointScenePath, originalFullPath, overwrite: true);

                    // Restore tracked asset snapshots
                    RestoreAssetSnapshots(metadata);

                    // Refresh the asset database and reopen the scene
                    AssetDatabase.Refresh();
                    EditorSceneManager.OpenScene(metadata.scenePath, OpenSceneMode.Single);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[CheckpointManager] Failed to restore checkpoint: {exception.Message}");
                    return null;
                }

                return metadata;
            }
        }

        /// <summary>
        /// Compares two checkpoints by their root object names and tracked assets.
        /// If checkpointIdB is null, compares against the current scene state.
        /// </summary>
        /// <param name="checkpointIdA">First checkpoint ID, or "current" for the active scene.</param>
        /// <param name="checkpointIdB">Second checkpoint ID, or "current" for the active scene. Defaults to current scene.</param>
        /// <returns>A diff result showing added/removed root objects and tracked assets, or null on failure.</returns>
        public static CheckpointDiff GetDiff(string checkpointIdA, string checkpointIdB = null)
        {
            lock (_lock)
            {
                // Resolve checkpoint A root object names
                List<string> rootNamesA;
                int totalCountA;
                int rootCountA;
                List<string> trackedAssetsA;

                if (string.Equals(checkpointIdA, "current", StringComparison.OrdinalIgnoreCase))
                {
                    var currentInfo = GetCurrentSceneRootInfo();
                    rootNamesA = currentInfo.rootNames;
                    rootCountA = currentInfo.rootCount;
                    totalCountA = currentInfo.totalCount;
                    trackedAssetsA = new List<string>();
                }
                else
                {
                    CheckpointMetadata metadataA = GetCheckpointMetadata(checkpointIdA);
                    if (metadataA == null)
                    {
                        Debug.LogWarning($"[CheckpointManager] Checkpoint A not found: {checkpointIdA}");
                        return null;
                    }
                    rootNamesA = metadataA.rootObjectNames ?? new List<string>();
                    rootCountA = metadataA.rootObjectCount;
                    totalCountA = metadataA.totalObjectCount;
                    trackedAssetsA = metadataA.trackedAssetPaths ?? new List<string>();
                }

                // Resolve checkpoint B root object names (default to current scene)
                List<string> rootNamesB;
                int totalCountB;
                int rootCountB;
                List<string> trackedAssetsB;

                bool useCurrentForB = string.IsNullOrEmpty(checkpointIdB) ||
                                      string.Equals(checkpointIdB, "current", StringComparison.OrdinalIgnoreCase);

                if (useCurrentForB)
                {
                    var currentInfo = GetCurrentSceneRootInfo();
                    rootNamesB = currentInfo.rootNames;
                    rootCountB = currentInfo.rootCount;
                    totalCountB = currentInfo.totalCount;
                    trackedAssetsB = new List<string>();
                }
                else
                {
                    CheckpointMetadata metadataB = GetCheckpointMetadata(checkpointIdB);
                    if (metadataB == null)
                    {
                        Debug.LogWarning($"[CheckpointManager] Checkpoint B not found: {checkpointIdB}");
                        return null;
                    }
                    rootNamesB = metadataB.rootObjectNames ?? new List<string>();
                    rootCountB = metadataB.rootObjectCount;
                    totalCountB = metadataB.totalObjectCount;
                    trackedAssetsB = metadataB.trackedAssetPaths ?? new List<string>();
                }

                // Compute diff using count-aware comparison (handles duplicate names)
                Dictionary<string, int> countsA = BuildNameCounts(rootNamesA);
                Dictionary<string, int> countsB = BuildNameCounts(rootNamesB);

                var addedObjects = new List<string>();
                var removedObjects = new List<string>();

                // Find added objects (in B but not enough in A)
                foreach (var kvp in countsB)
                {
                    countsA.TryGetValue(kvp.Key, out int countInA);
                    int addedCount = kvp.Value - countInA;
                    for (int i = 0; i < addedCount; i++)
                    {
                        addedObjects.Add(kvp.Key);
                    }
                }

                // Find removed objects (in A but not enough in B)
                foreach (var kvp in countsA)
                {
                    countsB.TryGetValue(kvp.Key, out int countInB);
                    int removedCount = kvp.Value - countInB;
                    for (int i = 0; i < removedCount; i++)
                    {
                        removedObjects.Add(kvp.Key);
                    }
                }

                return new CheckpointDiff
                {
                    addedObjects = addedObjects,
                    removedObjects = removedObjects,
                    rootCountA = rootCountA,
                    rootCountB = rootCountB,
                    totalCountA = totalCountA,
                    totalCountB = totalCountB,
                    trackedAssetsA = trackedAssetsA,
                    trackedAssetsB = trackedAssetsB
                };
            }
        }

        /// <summary>
        /// Deletes a checkpoint and all its associated files (scene copy, metadata, asset snapshots).
        /// </summary>
        /// <param name="checkpointId">The ID of the checkpoint to delete.</param>
        /// <returns>True if the checkpoint was deleted, false if not found or failed.</returns>
        public static bool DeleteCheckpoint(string checkpointId)
        {
            lock (_lock)
            {
                return DeleteCheckpointFiles(checkpointId);
            }
        }

        /// <summary>
        /// Freezes a checkpoint by ID, making it immutable.
        /// Used to preserve safety-net checkpoints (e.g., "Before restore (auto)").
        /// </summary>
        /// <param name="checkpointId">The ID of the checkpoint to freeze.</param>
        /// <returns>True if frozen successfully, false if not found or write failed.</returns>
        public static bool FreezeCheckpoint(string checkpointId)
        {
            lock (_lock)
            {
                CheckpointMetadata metadata = GetCheckpointMetadata(checkpointId);
                if (metadata == null) return false;
                if (metadata.isFrozen) return true; // Already frozen
                return FreezeBucket(metadata);
            }
        }

        #endregion

        #region Bucket Operations

        /// <summary>
        /// Finds the active (non-frozen) bucket from existing checkpoints.
        /// Returns null if no active bucket exists. Caller must hold _lock.
        /// </summary>
        private static CheckpointMetadata FindActiveBucket()
        {
            List<CheckpointMetadata> checkpoints = ListCheckpointsInternal();
            foreach (CheckpointMetadata checkpoint in checkpoints)
            {
                if (!checkpoint.isFrozen)
                {
                    return checkpoint;
                }
            }
            return null;
        }

        /// <summary>
        /// Freezes a bucket by setting isFrozen to true and writing updated metadata.
        /// Returns true on success, false if metadata write failed (reverts in-memory state).
        /// Caller must hold _lock.
        /// </summary>
        private static bool FreezeBucket(CheckpointMetadata bucket)
        {
            bucket.isFrozen = true;
            if (!WriteMetadata(bucket))
            {
                Debug.LogWarning($"[CheckpointManager] Failed to freeze bucket '{bucket.id}'");
                bucket.isFrozen = false;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Freezes ALL non-frozen buckets to maintain the single-active-bucket invariant.
        /// Caller must hold _lock.
        /// </summary>
        private static void FreezeAllUnfrozenBuckets()
        {
            List<CheckpointMetadata> checkpoints = ListCheckpointsInternal();
            foreach (CheckpointMetadata checkpoint in checkpoints)
            {
                if (!checkpoint.isFrozen)
                {
                    FreezeBucket(checkpoint);
                }
            }
        }

        /// <summary>
        /// Merges new changes into an existing active (non-frozen) bucket.
        /// Re-copies the scene file and merges tracked assets.
        /// Caller must hold _lock.
        /// </summary>
        private static CheckpointMetadata MergeIntoActiveBucket(
            CheckpointMetadata activeBucket,
            Scene activeScene,
            string fullScenePath,
            HashSet<string> newTrackedPaths,
            (List<string> rootNames, int rootCount, int totalCount) rootInfo)
        {
            string checkpointScenePath = Path.Combine(CheckpointDirectory, $"{activeBucket.id}.unity");

            try
            {
                // Re-copy the scene file over the existing bucket's scene copy
                File.Copy(fullScenePath, checkpointScenePath, overwrite: true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[CheckpointManager] Failed to update scene file for bucket '{activeBucket.id}': {exception.Message}");
                return null;
            }

            // Merge new tracked paths into the bucket's existing tracked asset paths
            var existingTrackedPaths = new HashSet<string>(activeBucket.trackedAssetPaths ?? new List<string>());
            existingTrackedPaths.UnionWith(newTrackedPaths);
            activeBucket.trackedAssetPaths = existingTrackedPaths.ToList();

            // Re-copy all tracked asset files to the bucket's asset snapshot directory
            SaveAssetSnapshots(activeBucket.id, existingTrackedPaths);

            // Update scene root info from pre-captured data (captured before SaveScene)
            activeBucket.rootObjectNames = rootInfo.rootNames;
            activeBucket.rootObjectCount = rootInfo.rootCount;
            activeBucket.totalObjectCount = rootInfo.totalCount;

            // Update timestamp
            activeBucket.timestamp = DateTime.Now;

            // Write updated metadata
            WriteMetadata(activeBucket);

            return activeBucket;
        }

        /// <summary>
        /// Creates a new checkpoint bucket with a fresh ID, scene copy, and asset snapshots.
        /// Caller must hold _lock.
        /// </summary>
        private static CheckpointMetadata CreateNewBucket(
            string checkpointName,
            Scene activeScene,
            string fullScenePath,
            HashSet<string> trackedPaths,
            (List<string> rootNames, int rootCount, int totalCount) rootInfo)
        {
            // Generate checkpoint ID and paths
            string checkpointId = Guid.NewGuid().ToString("N").Substring(0, 12);
            string checkpointScenePath = Path.Combine(CheckpointDirectory, $"{checkpointId}.unity");

            // Copy scene file to checkpoint location
            try
            {
                File.Copy(fullScenePath, checkpointScenePath, overwrite: true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[CheckpointManager] Failed to copy scene file: {exception.Message}");
                return null;
            }

            // Copy tracked asset snapshots
            SaveAssetSnapshots(checkpointId, trackedPaths);

            // Build metadata using pre-captured root info (captured before SaveScene)
            CheckpointMetadata metadata = new CheckpointMetadata
            {
                id = checkpointId,
                name = checkpointName ?? $"Checkpoint {DateTime.Now:HH:mm:ss}",
                timestamp = DateTime.Now,
                sceneName = activeScene.name,
                scenePath = activeScene.path,
                rootObjectCount = rootInfo.rootCount,
                totalObjectCount = rootInfo.totalCount,
                rootObjectNames = rootInfo.rootNames,
                trackedAssetPaths = trackedPaths.ToList(),
                isFrozen = false
            };

            // Write metadata JSON
            if (!WriteMetadata(metadata))
            {
                // Clean up the scene file since metadata write failed
                TryDeleteFile(checkpointScenePath);
                TryDeleteDirectory(Path.Combine(CheckpointDirectory, $"{checkpointId}_assets"));
                return null;
            }

            return metadata;
        }

        #endregion

        #region Asset Snapshot Storage

        /// <summary>
        /// Copies tracked asset files into the bucket's asset snapshot directory.
        /// Asset filenames use path separators replaced with underscores.
        /// Caller must hold _lock.
        /// </summary>
        /// <param name="checkpointId">The bucket ID to save snapshots for.</param>
        /// <param name="trackedPaths">Asset paths relative to the project root (e.g., "Assets/Materials/Foo.mat").</param>
        private static void SaveAssetSnapshots(string checkpointId, IEnumerable<string> trackedPaths)
        {
            if (trackedPaths == null) return;

            string assetSnapshotDirectory = Path.Combine(CheckpointDirectory, $"{checkpointId}_assets");
            string projectRoot = Path.GetDirectoryName(Application.dataPath);

            // Collect paths first to check if any exist
            var pathsList = trackedPaths.ToList();
            if (pathsList.Count == 0) return;

            // Ensure the asset snapshot directory exists
            if (!Directory.Exists(assetSnapshotDirectory))
            {
                Directory.CreateDirectory(assetSnapshotDirectory);
            }

            foreach (string assetPath in pathsList)
            {
                if (string.IsNullOrEmpty(assetPath)) continue;

                string fullAssetPath = Path.Combine(projectRoot, assetPath);
                if (!File.Exists(fullAssetPath))
                {
                    Debug.LogWarning($"[CheckpointManager] Tracked asset not found on disk: {assetPath}");
                    continue;
                }

                string sanitizedFilename = SanitizeAssetPathToFilename(assetPath);
                string destinationPath = Path.Combine(assetSnapshotDirectory, sanitizedFilename);

                try
                {
                    File.Copy(fullAssetPath, destinationPath, overwrite: true);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[CheckpointManager] Failed to snapshot asset '{assetPath}': {exception.Message}");
                }
            }
        }

        /// <summary>
        /// Restores tracked asset files from a bucket's asset snapshot directory back to the project.
        /// Caller must hold _lock.
        /// </summary>
        /// <param name="metadata">The checkpoint metadata containing tracked asset paths.</param>
        private static void RestoreAssetSnapshots(CheckpointMetadata metadata)
        {
            if (metadata.trackedAssetPaths == null || metadata.trackedAssetPaths.Count == 0) return;

            string assetSnapshotDirectory = Path.Combine(CheckpointDirectory, $"{metadata.id}_assets");
            if (!Directory.Exists(assetSnapshotDirectory)) return;

            string projectRoot = Path.GetDirectoryName(Application.dataPath);

            foreach (string assetPath in metadata.trackedAssetPaths)
            {
                if (string.IsNullOrEmpty(assetPath)) continue;

                string sanitizedFilename = SanitizeAssetPathToFilename(assetPath);
                string snapshotFilePath = Path.Combine(assetSnapshotDirectory, sanitizedFilename);

                if (!File.Exists(snapshotFilePath))
                {
                    Debug.LogWarning($"[CheckpointManager] Asset snapshot not found: {sanitizedFilename}");
                    continue;
                }

                string fullAssetPath = Path.Combine(projectRoot, assetPath);

                try
                {
                    // Ensure the target directory exists
                    string targetDirectory = Path.GetDirectoryName(fullAssetPath);
                    if (!string.IsNullOrEmpty(targetDirectory) && !Directory.Exists(targetDirectory))
                    {
                        Directory.CreateDirectory(targetDirectory);
                    }

                    File.Copy(snapshotFilePath, fullAssetPath, overwrite: true);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[CheckpointManager] Failed to restore asset '{assetPath}': {exception.Message}");
                }
            }
        }

        /// <summary>
        /// Converts an asset path to a safe filename by replacing path separators with underscores.
        /// E.g., "Assets/Materials/Foo.mat" becomes "Assets_Materials_Foo.mat".
        /// </summary>
        private static string SanitizeAssetPathToFilename(string assetPath)
        {
            return assetPath.Replace('/', '_').Replace('\\', '_');
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Reads a checkpoint metadata file by ID.
        /// Caller must hold _lock or be called from within a locked context.
        /// </summary>
        private static CheckpointMetadata GetCheckpointMetadata(string checkpointId)
        {
            string metadataPath = Path.Combine(CheckpointDirectory, $"{checkpointId}.json");
            if (!File.Exists(metadataPath))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(metadataPath);
                return JsonConvert.DeserializeObject<CheckpointMetadata>(json);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[CheckpointManager] Failed to read metadata for '{checkpointId}': {exception.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets root object information from the current active scene.
        /// </summary>
        private static (List<string> rootNames, int rootCount, int totalCount) GetCurrentSceneRootInfo()
        {
            Scene activeScene = EditorSceneManager.GetActiveScene();
            if (!activeScene.IsValid())
            {
                return (new List<string>(), 0, 0);
            }

            return CaptureRootInfo(activeScene);
        }

        /// <summary>
        /// Captures root object names and counts from the given scene.
        /// Must be called BEFORE EditorSceneManager.SaveScene() to avoid phantom
        /// duplicates caused by Unity's post-save instance ID reconciliation.
        /// </summary>
        private static (List<string> rootNames, int rootCount, int totalCount) CaptureRootInfo(Scene scene)
        {
            GameObject[] rootObjects = scene.GetRootGameObjects();
            List<string> rootNames = rootObjects
                .Where(go => go != null)
                .Select(go => go.name)
                .ToList();

            int totalCount = 0;
            foreach (GameObject rootObject in rootObjects)
            {
                if (rootObject != null)
                {
                    totalCount += rootObject.GetComponentsInChildren<Transform>(includeInactive: true).Length;
                }
            }

            return (rootNames, rootObjects.Length, totalCount);
        }

        /// <summary>
        /// Builds a dictionary of name occurrence counts from a list of names.
        /// Used for count-aware diff comparisons that handle duplicate names.
        /// </summary>
        private static Dictionary<string, int> BuildNameCounts(List<string> names)
        {
            var counts = new Dictionary<string, int>();
            foreach (string objectName in names)
            {
                if (counts.ContainsKey(objectName))
                {
                    counts[objectName]++;
                }
                else
                {
                    counts[objectName] = 1;
                }
            }
            return counts;
        }

        /// <summary>
        /// Writes checkpoint metadata to its JSON file.
        /// Returns true on success, false on failure.
        /// Caller must hold _lock.
        /// </summary>
        private static bool WriteMetadata(CheckpointMetadata metadata)
        {
            string metadataPath = Path.Combine(CheckpointDirectory, $"{metadata.id}.json");
            try
            {
                string metadataJson = JsonConvert.SerializeObject(metadata, Formatting.Indented);
                File.WriteAllText(metadataPath, metadataJson);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[CheckpointManager] Failed to write metadata for '{metadata.id}': {exception.Message}");
                return false;
            }
        }

        /// <summary>
        /// Enforces retention limits: age-based expiry and count-based FIFO eviction.
        /// Never evicts the active (non-frozen) bucket.
        /// Caller must hold _lock.
        /// </summary>
        private static void EnforceRetentionLimits()
        {
            List<CheckpointMetadata> checkpoints = ListCheckpointsInternal();
            DateTime now = DateTime.Now;

            // Age-based: delete frozen buckets older than MaxBucketAge
            for (int i = checkpoints.Count - 1; i >= 0; i--)
            {
                CheckpointMetadata checkpoint = checkpoints[i];
                if (checkpoint.isFrozen && (now - checkpoint.timestamp) > MaxBucketAge)
                {
                    DeleteCheckpointFiles(checkpoint.id);
                    checkpoints.RemoveAt(i);
                }
            }

            // Count-based FIFO: delete oldest frozen buckets until under cap
            while (checkpoints.Count > MaxBuckets)
            {
                // Find the oldest frozen bucket (list is sorted newest-first, so search from the end)
                int oldestFrozenIndex = -1;
                for (int i = checkpoints.Count - 1; i >= 0; i--)
                {
                    if (checkpoints[i].isFrozen)
                    {
                        oldestFrozenIndex = i;
                        break;
                    }
                }

                if (oldestFrozenIndex < 0)
                {
                    // No frozen buckets left to evict
                    break;
                }

                DeleteCheckpointFiles(checkpoints[oldestFrozenIndex].id);
                checkpoints.RemoveAt(oldestFrozenIndex);
            }
        }

        /// <summary>
        /// Internal version of ListCheckpoints that does not acquire the lock (caller must hold it).
        /// Returns checkpoints sorted by timestamp descending (newest first).
        /// </summary>
        private static List<CheckpointMetadata> ListCheckpointsInternal()
        {
            var checkpoints = new List<CheckpointMetadata>();
            string checkpointDirectory = CheckpointDirectory;

            string[] metadataFiles;
            try
            {
                metadataFiles = Directory.GetFiles(checkpointDirectory, "*.json");
            }
            catch (Exception)
            {
                return checkpoints;
            }

            foreach (string metadataFilePath in metadataFiles)
            {
                try
                {
                    string json = File.ReadAllText(metadataFilePath);
                    CheckpointMetadata metadata = JsonConvert.DeserializeObject<CheckpointMetadata>(json);
                    if (metadata != null)
                    {
                        string sceneFilePath = Path.Combine(checkpointDirectory, $"{metadata.id}.unity");
                        if (File.Exists(sceneFilePath))
                        {
                            checkpoints.Add(metadata);
                        }
                    }
                }
                catch (Exception)
                {
                    // Skip unreadable metadata files
                }
            }

            // Sort by timestamp descending (newest first)
            checkpoints.Sort((a, b) => b.timestamp.CompareTo(a.timestamp));

            return checkpoints;
        }

        /// <summary>
        /// Deletes checkpoint files and asset snapshot directory without acquiring the lock.
        /// Caller must hold _lock.
        /// </summary>
        /// <returns>True if any files were deleted, false if nothing was found to delete.</returns>
        private static bool DeleteCheckpointFiles(string checkpointId)
        {
            string checkpointScenePath = Path.Combine(CheckpointDirectory, $"{checkpointId}.unity");
            string checkpointMetadataPath = Path.Combine(CheckpointDirectory, $"{checkpointId}.json");
            string assetSnapshotDirectory = Path.Combine(CheckpointDirectory, $"{checkpointId}_assets");

            bool sceneDeleted = TryDeleteFile(checkpointScenePath);
            bool metadataDeleted = TryDeleteFile(checkpointMetadataPath);
            bool assetsDeleted = TryDeleteDirectory(assetSnapshotDirectory);

            return sceneDeleted || metadataDeleted || assetsDeleted;
        }

        /// <summary>
        /// Attempts to delete a file, returning true if successful or if the file does not exist.
        /// </summary>
        private static bool TryDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    return true;
                }
                return false;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[CheckpointManager] Failed to delete file '{filePath}': {exception.Message}");
                return false;
            }
        }

        /// <summary>
        /// Attempts to recursively delete a directory and all its contents.
        /// Returns true if successfully deleted, false otherwise.
        /// </summary>
        private static bool TryDeleteDirectory(string directoryPath)
        {
            try
            {
                if (Directory.Exists(directoryPath))
                {
                    Directory.Delete(directoryPath, recursive: true);
                    return true;
                }
                return false;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[CheckpointManager] Failed to delete directory '{directoryPath}': {exception.Message}");
                return false;
            }
        }

        #endregion
    }
}
