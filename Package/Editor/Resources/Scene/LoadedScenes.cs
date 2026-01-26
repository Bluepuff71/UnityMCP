using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace UnityMCP.Editor.Resources.Scene
{
    /// <summary>
    /// Resource provider for information about currently loaded scenes.
    /// </summary>
    public static class LoadedScenes
    {
        /// <summary>
        /// Gets information about all currently loaded scenes in the editor.
        /// </summary>
        /// <returns>Object containing loaded scene information.</returns>
        [MCPResource("scene://loaded", "All currently loaded scenes and their status")]
        public static object GetLoadedScenes()
        {
            int sceneCount = SceneManager.sceneCount;
            var scenesList = new List<object>();

            for (int sceneIndex = 0; sceneIndex < sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);

                scenesList.Add(new
                {
                    name = scene.name,
                    path = scene.path,
                    buildIndex = scene.buildIndex,
                    isLoaded = scene.isLoaded,
                    isDirty = scene.isDirty,
                    isValid = scene.IsValid(),
                    rootCount = scene.isLoaded ? scene.rootCount : 0,
                    handle = scene.handle
                });
            }

            // Get active scene info
            var activeScene = SceneManager.GetActiveScene();

            // Get prefab stage info (if editing a prefab)
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            object prefabStageInfo = null;

            if (prefabStage != null)
            {
                prefabStageInfo = new
                {
                    isOpen = true,
                    prefabAssetPath = prefabStage.assetPath,
                    scenePath = prefabStage.scene.path,
                    mode = prefabStage.mode.ToString()
                };
            }

            return new
            {
                sceneCount = sceneCount,
                activeScene = new
                {
                    name = activeScene.name,
                    path = activeScene.path,
                    buildIndex = activeScene.buildIndex,
                    isDirty = activeScene.isDirty
                },
                scenes = scenesList.ToArray(),
                prefabStage = prefabStageInfo,
                sceneSetup = GetSceneSetupInfo()
            };
        }

        /// <summary>
        /// Gets the current scene setup for saving/restoring scene states.
        /// </summary>
        private static object GetSceneSetupInfo()
        {
            var sceneSetups = EditorSceneManager.GetSceneManagerSetup();
            var setupList = new List<object>();

            foreach (var setup in sceneSetups)
            {
                setupList.Add(new
                {
                    path = setup.path,
                    isLoaded = setup.isLoaded,
                    isActive = setup.isActive
                });
            }

            return new
            {
                count = setupList.Count,
                setups = setupList.ToArray()
            };
        }
    }
}
