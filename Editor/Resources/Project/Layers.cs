using System.Linq;
using UnityEditorInternal;
using UnityEngine;

namespace UnityMCP.Editor.Resources.Project
{
    /// <summary>
    /// Resource provider for project layers.
    /// </summary>
    public static class Layers
    {
        /// <summary>
        /// Gets all layers defined in the project with their indices.
        /// </summary>
        /// <returns>Object containing layer information.</returns>
        [MCPResource("project://layers", "Project layers and their indices")]
        public static object Get()
        {
            // Get all non-empty layers
            string[] definedLayers = InternalEditorUtility.layers;

            // Build a complete list of all 32 layer slots with their names
            var allLayers = Enumerable.Range(0, 32)
                .Select(layerIndex => new
                {
                    index = layerIndex,
                    name = LayerMask.LayerToName(layerIndex),
                    isDefined = !string.IsNullOrEmpty(LayerMask.LayerToName(layerIndex)),
                    mask = 1 << layerIndex
                })
                .ToArray();

            // Get only defined layers for convenience
            var definedLayersList = allLayers
                .Where(layer => layer.isDefined)
                .Select(layer => new
                {
                    layer.index,
                    layer.name,
                    layer.mask
                })
                .ToArray();

            return new
            {
                totalSlots = 32,
                definedCount = definedLayers.Length,
                layers = definedLayersList,
                allSlots = allLayers
            };
        }
    }
}
