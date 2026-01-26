using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityMCP.Editor.Utilities;

namespace UnityMCP.Editor.Resources.Project
{
    /// <summary>
    /// Resource provider for Unity physics settings.
    /// </summary>
    public static class PhysicsSettings
    {
        /// <summary>
        /// Gets the current physics settings for the project.
        /// </summary>
        /// <returns>Object containing physics configuration including gravity, solver iterations, and collision matrix.</returns>
        [MCPResource("project://physics", "Physics settings including gravity, solver iterations, and layer collision matrix")]
        public static object Get()
        {
            // Get PhysicsManager via SerializedObject for additional settings
            var physicsManager = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/DynamicsManager.asset");
            SerializedObject serializedPhysicsManager = null;

            if (physicsManager != null)
            {
                serializedPhysicsManager = new SerializedObject(physicsManager);
            }

            // Build layer collision matrix (32x32)
            var collisionMatrix = BuildLayerCollisionMatrix();

            return new
            {
                gravity = new
                {
                    x = Physics.gravity.x,
                    y = Physics.gravity.y,
                    z = Physics.gravity.z
                },
                defaultSolverIterations = Physics.defaultSolverIterations,
                defaultSolverVelocityIterations = Physics.defaultSolverVelocityIterations,
                bounceThreshold = Physics.bounceThreshold,
                sleepThreshold = Physics.sleepThreshold,
                defaultContactOffset = Physics.defaultContactOffset,
                defaultMaxAngularSpeed = Physics.defaultMaxAngularSpeed,
                queriesHitTriggers = Physics.queriesHitTriggers,
                queriesHitBackfaces = Physics.queriesHitBackfaces,
                autoSimulation = Physics.simulationMode.ToString(),
                reuseCollisionCallbacks = Physics.reuseCollisionCallbacks,
                interCollisionDistance = Physics.interCollisionDistance,
                interCollisionStiffness = Physics.interCollisionStiffness,
                clothGravity = new
                {
                    x = Physics.clothGravity.x,
                    y = Physics.clothGravity.y,
                    z = Physics.clothGravity.z
                },
                layerCollisionMatrix = collisionMatrix,
                additionalSettings = GetAdditionalSettings(serializedPhysicsManager)
            };
        }

        /// <summary>
        /// Builds the 32x32 layer collision matrix showing which layers can collide with each other.
        /// </summary>
        private static object BuildLayerCollisionMatrix()
        {
            var layerNames = new Dictionary<int, string>();
            var enabledLayers = new List<int>();

            // Get all defined layer names
            for (int layerIndex = 0; layerIndex < UnityConstants.TotalLayerCount; layerIndex++)
            {
                string layerName = LayerMask.LayerToName(layerIndex);
                if (!string.IsNullOrEmpty(layerName))
                {
                    layerNames[layerIndex] = layerName;
                    enabledLayers.Add(layerIndex);
                }
            }

            // Build collision pairs - only include layers that can collide
            var collisionPairs = new List<object>();
            var ignoredPairs = new List<object>();

            for (int layerA = 0; layerA < UnityConstants.TotalLayerCount; layerA++)
            {
                for (int layerB = layerA; layerB < UnityConstants.TotalLayerCount; layerB++)
                {
                    // Only include pairs where at least one layer is defined
                    bool layerADefined = layerNames.ContainsKey(layerA);
                    bool layerBDefined = layerNames.ContainsKey(layerB);

                    if (!layerADefined && !layerBDefined)
                    {
                        continue;
                    }

                    bool canCollide = !Physics.GetIgnoreLayerCollision(layerA, layerB);

                    if (!canCollide)
                    {
                        ignoredPairs.Add(new
                        {
                            layerA = layerA,
                            layerAName = layerADefined ? layerNames[layerA] : $"Layer {layerA}",
                            layerB = layerB,
                            layerBName = layerBDefined ? layerNames[layerB] : $"Layer {layerB}"
                        });
                    }
                }
            }

            return new
            {
                definedLayers = layerNames,
                ignoredCollisionPairs = ignoredPairs.ToArray(),
                ignoredPairCount = ignoredPairs.Count,
                note = "By default, all layer pairs can collide. Only ignored pairs are listed above."
            };
        }

        /// <summary>
        /// Gets additional physics settings from the serialized PhysicsManager.
        /// </summary>
        private static object GetAdditionalSettings(SerializedObject serializedPhysicsManager)
        {
            if (serializedPhysicsManager == null)
            {
                return null;
            }

            var settings = new Dictionary<string, object>();

            // Try to get additional serialized properties
            var properties = new[]
            {
                "m_DefaultMaterial",
                "m_BounceThreshold",
                "m_DefaultMaxDepenetrationVelocity",
                "m_EnableEnhancedDeterminism",
                "m_EnableUnifiedHeightmaps",
                "m_SolverType",
                "m_FrictionType",
                "m_ClothInterCollisionDistance",
                "m_ClothInterCollisionStiffness"
            };

            foreach (var propertyName in properties)
            {
                var property = serializedPhysicsManager.FindProperty(propertyName);
                if (property != null)
                {
                    settings[propertyName] = GetSerializedPropertyValue(property);
                }
            }

            // Get default physics material if set
            var defaultMaterialProperty = serializedPhysicsManager.FindProperty("m_DefaultMaterial");
            if (defaultMaterialProperty != null && defaultMaterialProperty.objectReferenceValue != null)
            {
                var material = defaultMaterialProperty.objectReferenceValue as PhysicsMaterial;
                if (material != null)
                {
                    settings["defaultMaterial"] = new
                    {
                        name = material.name,
                        dynamicFriction = material.dynamicFriction,
                        staticFriction = material.staticFriction,
                        bounciness = material.bounciness,
                        frictionCombine = material.frictionCombine.ToString(),
                        bounceCombine = material.bounceCombine.ToString()
                    };
                }
            }

            return settings.Count > 0 ? settings : null;
        }

        /// <summary>
        /// Gets the value of a serialized property.
        /// </summary>
        private static object GetSerializedPropertyValue(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return property.intValue;
                case SerializedPropertyType.Boolean:
                    return property.boolValue;
                case SerializedPropertyType.Float:
                    return property.floatValue;
                case SerializedPropertyType.String:
                    return property.stringValue;
                case SerializedPropertyType.Enum:
                    return property.enumNames.Length > property.enumValueIndex && property.enumValueIndex >= 0
                        ? property.enumNames[property.enumValueIndex]
                        : property.intValue.ToString();
                case SerializedPropertyType.ObjectReference:
                    var objectReference = property.objectReferenceValue;
                    return objectReference != null
                        ? new { name = objectReference.name, type = objectReference.GetType().Name }
                        : null;
                case SerializedPropertyType.Vector3:
                    var vector3Value = property.vector3Value;
                    return new { x = vector3Value.x, y = vector3Value.y, z = vector3Value.z };
                default:
                    return $"<{property.propertyType}>";
            }
        }
    }
}
