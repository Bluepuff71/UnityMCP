using UnityEditor;
using UnityEditor.SceneManagement;

namespace UnityMCP.Editor.Resources.Editor
{
    /// <summary>
    /// Resource provider for the current prefab editing stage.
    /// </summary>
    public static class PrefabStage
    {
        /// <summary>
        /// Gets information about the current prefab editing stage, if any.
        /// </summary>
        /// <returns>Object containing prefab stage information or null state.</returns>
        [MCPResource("editor://prefab_stage", "Current prefab editing stage information")]
        public static object GetPrefabStage()
        {
            var currentStage = PrefabStageUtility.GetCurrentPrefabStage();

            if (currentStage == null)
            {
                return new
                {
                    isInPrefabMode = false,
                    message = "Not currently editing a prefab"
                };
            }

            var prefabRoot = currentStage.prefabContentsRoot;

            return new
            {
                isInPrefabMode = true,
                prefabAssetPath = currentStage.assetPath,
                mode = currentStage.mode.ToString(),
                stageHandle = currentStage.stageHandle.GetHashCode(),
                prefabRoot = prefabRoot != null ? new
                {
                    name = prefabRoot.name,
                    instanceId = prefabRoot.GetInstanceID(),
                    childCount = prefabRoot.transform.childCount,
                    componentCount = prefabRoot.GetComponents<UnityEngine.Component>().Length
                } : null,
                scene = new
                {
                    name = currentStage.scene.name,
                    path = currentStage.scene.path,
                    isLoaded = currentStage.scene.isLoaded,
                    isDirty = currentStage.scene.isDirty
                }
            };
        }
    }
}
