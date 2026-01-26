using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMCP.Editor.Resources.Editor
{
    /// <summary>
    /// Resource provider for the current editor selection.
    /// </summary>
    public static class SelectionResource
    {
        /// <summary>
        /// Gets information about the currently selected objects in the editor.
        /// </summary>
        /// <returns>Object containing selection information.</returns>
        [MCPResource("editor://selection", "Currently selected objects in the editor")]
        public static object GetSelection()
        {
            var selectedGameObjects = Selection.gameObjects;
            var selectedObjects = Selection.objects;
            var activeObject = Selection.activeObject;
            var activeGameObject = Selection.activeGameObject;
            var activeTransform = Selection.activeTransform;

            return new
            {
                count = selectedObjects.Length,
                gameObjectCount = selectedGameObjects.Length,
                activeObject = activeObject != null ? new
                {
                    name = activeObject.name,
                    instanceId = activeObject.GetInstanceID(),
                    type = activeObject.GetType().Name
                } : null,
                activeGameObject = activeGameObject != null ? new
                {
                    name = activeGameObject.name,
                    instanceId = activeGameObject.GetInstanceID(),
                    tag = activeGameObject.tag,
                    layer = LayerMask.LayerToName(activeGameObject.layer),
                    isActive = activeGameObject.activeInHierarchy,
                    isPrefab = PrefabUtility.IsPartOfAnyPrefab(activeGameObject)
                } : null,
                activeTransform = activeTransform != null ? new
                {
                    position = new { x = activeTransform.position.x, y = activeTransform.position.y, z = activeTransform.position.z },
                    rotation = new { x = activeTransform.eulerAngles.x, y = activeTransform.eulerAngles.y, z = activeTransform.eulerAngles.z },
                    localScale = new { x = activeTransform.localScale.x, y = activeTransform.localScale.y, z = activeTransform.localScale.z }
                } : null,
                selectedGameObjects = selectedGameObjects.Select(gameObject => new
                {
                    name = gameObject.name,
                    instanceId = gameObject.GetInstanceID(),
                    tag = gameObject.tag,
                    layer = LayerMask.LayerToName(gameObject.layer),
                    isActive = gameObject.activeInHierarchy
                }).ToArray(),
                selectedAssets = selectedObjects
                    .Where(obj => AssetDatabase.Contains(obj))
                    .Select(asset => new
                    {
                        name = asset.name,
                        instanceId = asset.GetInstanceID(),
                        type = asset.GetType().Name,
                        assetPath = AssetDatabase.GetAssetPath(asset)
                    }).ToArray(),
                selectionMode = new
                {
                    containsGameObjects = selectedGameObjects.Length > 0,
                    containsAssets = selectedObjects.Any(obj => AssetDatabase.Contains(obj)),
                    isMixed = selectedGameObjects.Length > 0 && selectedObjects.Any(obj => AssetDatabase.Contains(obj))
                }
            };
        }
    }
}
