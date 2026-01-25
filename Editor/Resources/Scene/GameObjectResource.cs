using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#pragma warning disable CS0618 // EditorUtility.InstanceIDToObject is deprecated but still functional

namespace UnityMCP.Editor.Resources.Scene
{
    /// <summary>
    /// Resource provider for GameObject details accessed by instance ID.
    /// Supports parameterized URIs for querying specific GameObjects and their components.
    /// </summary>
    public static class GameObjectResource
    {
        /// <summary>
        /// Gets detailed information about a GameObject by its instance ID.
        /// </summary>
        /// <param name="instanceId">The instance ID of the GameObject.</param>
        /// <returns>Object containing GameObject details or error information.</returns>
        [MCPResource("scene://gameobject/{id}", "GameObject details by instance ID")]
        public static object GetGameObject([MCPParam("id", "Instance ID of the GameObject")] int instanceId)
        {
            var unityObject = EditorUtility.InstanceIDToObject(instanceId);

            if (unityObject == null)
            {
                return new
                {
                    error = true,
                    message = $"GameObject with instance ID {instanceId} not found"
                };
            }

            if (unityObject is not GameObject gameObject)
            {
                return new
                {
                    error = true,
                    message = $"Object with instance ID {instanceId} is not a GameObject, it is a {unityObject.GetType().Name}"
                };
            }

            return BuildGameObjectInfo(gameObject, includeComponentDetails: false);
        }

        /// <summary>
        /// Gets the list of components attached to a GameObject.
        /// </summary>
        /// <param name="instanceId">The instance ID of the GameObject.</param>
        /// <returns>Object containing component list or error information.</returns>
        [MCPResource("scene://gameobject/{id}/components", "List of components on a GameObject")]
        public static object GetGameObjectComponents([MCPParam("id", "Instance ID of the GameObject")] int instanceId)
        {
            var unityObject = EditorUtility.InstanceIDToObject(instanceId);

            if (unityObject == null)
            {
                return new
                {
                    error = true,
                    message = $"GameObject with instance ID {instanceId} not found"
                };
            }

            if (unityObject is not GameObject gameObject)
            {
                return new
                {
                    error = true,
                    message = $"Object with instance ID {instanceId} is not a GameObject, it is a {unityObject.GetType().Name}"
                };
            }

            var components = gameObject.GetComponents<Component>();
            var componentInfoList = new List<object>();

            foreach (var component in components)
            {
                if (component == null)
                {
                    componentInfoList.Add(new
                    {
                        type = "Missing",
                        instanceId = -1,
                        isMissing = true
                    });
                    continue;
                }

                componentInfoList.Add(new
                {
                    type = component.GetType().Name,
                    fullType = component.GetType().FullName,
                    instanceId = component.GetInstanceID(),
                    enabled = IsComponentEnabled(component),
                    isMissing = false
                });
            }

            return new
            {
                gameObjectName = gameObject.name,
                gameObjectInstanceId = instanceId,
                componentCount = components.Length,
                components = componentInfoList.ToArray()
            };
        }

        /// <summary>
        /// Gets detailed information about a specific component on a GameObject.
        /// </summary>
        /// <param name="instanceId">The instance ID of the GameObject.</param>
        /// <param name="componentType">The type name of the component.</param>
        /// <returns>Object containing component details or error information.</returns>
        [MCPResource("scene://gameobject/{id}/component/{type}", "Specific component details on a GameObject")]
        public static object GetGameObjectComponent(
            [MCPParam("id", "Instance ID of the GameObject")] int instanceId,
            [MCPParam("type", "Type name of the component")] string componentType)
        {
            var unityObject = EditorUtility.InstanceIDToObject(instanceId);

            if (unityObject == null)
            {
                return new
                {
                    error = true,
                    message = $"GameObject with instance ID {instanceId} not found"
                };
            }

            if (unityObject is not GameObject gameObject)
            {
                return new
                {
                    error = true,
                    message = $"Object with instance ID {instanceId} is not a GameObject, it is a {unityObject.GetType().Name}"
                };
            }

            // Find the component by type name
            var components = gameObject.GetComponents<Component>();
            Component targetComponent = null;

            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }

                // Match by short type name or full type name
                if (component.GetType().Name.Equals(componentType, StringComparison.OrdinalIgnoreCase) ||
                    component.GetType().FullName.Equals(componentType, StringComparison.OrdinalIgnoreCase))
                {
                    targetComponent = component;
                    break;
                }
            }

            if (targetComponent == null)
            {
                // List available components in error message
                var availableTypes = components
                    .Where(c => c != null)
                    .Select(c => c.GetType().Name)
                    .Distinct()
                    .ToArray();

                return new
                {
                    error = true,
                    message = $"Component of type '{componentType}' not found on GameObject '{gameObject.name}'",
                    availableComponents = availableTypes
                };
            }

            return BuildComponentDetails(targetComponent, gameObject);
        }

        /// <summary>
        /// Builds detailed information about a GameObject.
        /// </summary>
        private static object BuildGameObjectInfo(GameObject gameObject, bool includeComponentDetails)
        {
            var transform = gameObject.transform;
            var components = gameObject.GetComponents<Component>();

            var componentSummary = components
                .Where(c => c != null)
                .Select(c => new
                {
                    type = c.GetType().Name,
                    instanceId = c.GetInstanceID(),
                    enabled = IsComponentEnabled(c)
                })
                .ToArray();

            var childrenInfo = new List<object>();
            for (int childIndex = 0; childIndex < transform.childCount; childIndex++)
            {
                var child = transform.GetChild(childIndex);
                childrenInfo.Add(new
                {
                    name = child.name,
                    instanceId = child.gameObject.GetInstanceID(),
                    isActive = child.gameObject.activeSelf
                });
            }

            return new
            {
                name = gameObject.name,
                instanceId = gameObject.GetInstanceID(),
                tag = gameObject.tag,
                layer = gameObject.layer,
                layerName = LayerMask.LayerToName(gameObject.layer),
                isActive = gameObject.activeSelf,
                isActiveInHierarchy = gameObject.activeInHierarchy,
                isStatic = gameObject.isStatic,
                isPrefab = PrefabUtility.IsPartOfAnyPrefab(gameObject),
                prefabStatus = GetPrefabStatus(gameObject),
                transform = new
                {
                    position = FormatVector3(transform.position),
                    localPosition = FormatVector3(transform.localPosition),
                    rotation = FormatVector3(transform.eulerAngles),
                    localRotation = FormatVector3(transform.localEulerAngles),
                    localScale = FormatVector3(transform.localScale),
                    lossyScale = FormatVector3(transform.lossyScale)
                },
                hierarchy = new
                {
                    parent = transform.parent != null ? new
                    {
                        name = transform.parent.name,
                        instanceId = transform.parent.gameObject.GetInstanceID()
                    } : null,
                    childCount = transform.childCount,
                    children = childrenInfo.ToArray(),
                    siblingIndex = transform.GetSiblingIndex(),
                    hierarchyPath = GetHierarchyPath(transform)
                },
                componentCount = components.Length,
                components = componentSummary
            };
        }

        /// <summary>
        /// Builds detailed information about a specific component.
        /// </summary>
        private static object BuildComponentDetails(Component component, GameObject gameObject)
        {
            var componentType = component.GetType();
            var properties = new Dictionary<string, object>();

            // Get serialized properties for better Unity integration
            var serializedObject = new SerializedObject(component);
            var serializedProperty = serializedObject.GetIterator();

            if (serializedProperty.NextVisible(true))
            {
                do
                {
                    // Skip script reference
                    if (serializedProperty.name == "m_Script")
                    {
                        continue;
                    }

                    properties[serializedProperty.name] = GetSerializedPropertyValue(serializedProperty);
                }
                while (serializedProperty.NextVisible(false));
            }

            // Also include some key public properties via reflection for common components
            var reflectedProperties = GetReflectedProperties(component);

            return new
            {
                type = componentType.Name,
                fullType = componentType.FullName,
                instanceId = component.GetInstanceID(),
                gameObject = new
                {
                    name = gameObject.name,
                    instanceId = gameObject.GetInstanceID()
                },
                enabled = IsComponentEnabled(component),
                serializedProperties = properties,
                publicProperties = reflectedProperties
            };
        }

        /// <summary>
        /// Gets the value of a serialized property in a safe, readable format.
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
                case SerializedPropertyType.Color:
                    var color = property.colorValue;
                    return new { r = color.r, g = color.g, b = color.b, a = color.a };
                case SerializedPropertyType.ObjectReference:
                    var objRef = property.objectReferenceValue;
                    return objRef != null ? new
                    {
                        name = objRef.name,
                        type = objRef.GetType().Name,
                        instanceId = objRef.GetInstanceID()
                    } : null;
                case SerializedPropertyType.LayerMask:
                    return property.intValue;
                case SerializedPropertyType.Enum:
                    return property.enumNames.Length > property.enumValueIndex && property.enumValueIndex >= 0
                        ? property.enumNames[property.enumValueIndex]
                        : property.intValue.ToString();
                case SerializedPropertyType.Vector2:
                    var vec2 = property.vector2Value;
                    return new { x = vec2.x, y = vec2.y };
                case SerializedPropertyType.Vector3:
                    var vec3 = property.vector3Value;
                    return new { x = vec3.x, y = vec3.y, z = vec3.z };
                case SerializedPropertyType.Vector4:
                    var vec4 = property.vector4Value;
                    return new { x = vec4.x, y = vec4.y, z = vec4.z, w = vec4.w };
                case SerializedPropertyType.Rect:
                    var rect = property.rectValue;
                    return new { x = rect.x, y = rect.y, width = rect.width, height = rect.height };
                case SerializedPropertyType.Bounds:
                    var bounds = property.boundsValue;
                    return new
                    {
                        center = new { x = bounds.center.x, y = bounds.center.y, z = bounds.center.z },
                        size = new { x = bounds.size.x, y = bounds.size.y, z = bounds.size.z }
                    };
                case SerializedPropertyType.Quaternion:
                    var quat = property.quaternionValue;
                    return new { x = quat.x, y = quat.y, z = quat.z, w = quat.w };
                case SerializedPropertyType.Vector2Int:
                    var vec2Int = property.vector2IntValue;
                    return new { x = vec2Int.x, y = vec2Int.y };
                case SerializedPropertyType.Vector3Int:
                    var vec3Int = property.vector3IntValue;
                    return new { x = vec3Int.x, y = vec3Int.y, z = vec3Int.z };
                case SerializedPropertyType.RectInt:
                    var rectInt = property.rectIntValue;
                    return new { x = rectInt.x, y = rectInt.y, width = rectInt.width, height = rectInt.height };
                case SerializedPropertyType.BoundsInt:
                    var boundsInt = property.boundsIntValue;
                    return new
                    {
                        position = new { x = boundsInt.position.x, y = boundsInt.position.y, z = boundsInt.position.z },
                        size = new { x = boundsInt.size.x, y = boundsInt.size.y, z = boundsInt.size.z }
                    };
                case SerializedPropertyType.ArraySize:
                    return property.intValue;
                default:
                    return $"<{property.propertyType}>";
            }
        }

        /// <summary>
        /// Gets reflected public properties that are commonly useful.
        /// </summary>
        private static Dictionary<string, object> GetReflectedProperties(Component component)
        {
            var result = new Dictionary<string, object>();
            var componentType = component.GetType();

            // Get commonly useful properties based on component type
            if (component is Renderer renderer)
            {
                result["isVisible"] = renderer.isVisible;
                result["bounds"] = new
                {
                    center = FormatVector3(renderer.bounds.center),
                    size = FormatVector3(renderer.bounds.size)
                };
                result["materialCount"] = renderer.sharedMaterials.Length;
                result["materials"] = renderer.sharedMaterials
                    .Where(m => m != null)
                    .Select(m => new { name = m.name, instanceId = m.GetInstanceID() })
                    .ToArray();
            }
            else if (component is Collider collider)
            {
                result["enabled"] = collider.enabled;
                result["isTrigger"] = collider.isTrigger;
                result["bounds"] = new
                {
                    center = FormatVector3(collider.bounds.center),
                    size = FormatVector3(collider.bounds.size)
                };
            }
            else if (component is Rigidbody rigidbody)
            {
                result["mass"] = rigidbody.mass;
                result["drag"] = rigidbody.linearDamping;
                result["angularDrag"] = rigidbody.angularDamping;
                result["useGravity"] = rigidbody.useGravity;
                result["isKinematic"] = rigidbody.isKinematic;
                result["velocity"] = FormatVector3(rigidbody.linearVelocity);
                result["angularVelocity"] = FormatVector3(rigidbody.angularVelocity);
            }
            else if (component is Camera camera)
            {
                result["fieldOfView"] = camera.fieldOfView;
                result["nearClipPlane"] = camera.nearClipPlane;
                result["farClipPlane"] = camera.farClipPlane;
                result["depth"] = camera.depth;
                result["orthographic"] = camera.orthographic;
                result["orthographicSize"] = camera.orthographicSize;
                result["cullingMask"] = camera.cullingMask;
            }
            else if (component is Light light)
            {
                result["lightType"] = light.type.ToString();
                result["color"] = new { r = light.color.r, g = light.color.g, b = light.color.b, a = light.color.a };
                result["intensity"] = light.intensity;
                result["range"] = light.range;
                result["spotAngle"] = light.spotAngle;
                result["shadows"] = light.shadows.ToString();
            }
            else if (component is AudioSource audioSource)
            {
                result["clip"] = audioSource.clip != null ? new { name = audioSource.clip.name, instanceId = audioSource.clip.GetInstanceID() } : null;
                result["volume"] = audioSource.volume;
                result["pitch"] = audioSource.pitch;
                result["loop"] = audioSource.loop;
                result["playOnAwake"] = audioSource.playOnAwake;
                result["isPlaying"] = audioSource.isPlaying;
            }
            else if (component is Animator animator)
            {
                result["hasRuntimeController"] = animator.runtimeAnimatorController != null;
                result["speed"] = animator.speed;
                result["applyRootMotion"] = animator.applyRootMotion;
                result["updateMode"] = animator.updateMode.ToString();
                result["cullingMode"] = animator.cullingMode.ToString();
            }

            return result;
        }

        /// <summary>
        /// Checks if a component is enabled (for components that support enabling/disabling).
        /// </summary>
        private static bool IsComponentEnabled(Component component)
        {
            if (component is Behaviour behaviour)
            {
                return behaviour.enabled;
            }

            if (component is Renderer renderer)
            {
                return renderer.enabled;
            }

            if (component is Collider collider)
            {
                return collider.enabled;
            }

            // Transform and other components don't have enabled state
            return true;
        }

        /// <summary>
        /// Gets the prefab status of a GameObject.
        /// </summary>
        private static string GetPrefabStatus(GameObject gameObject)
        {
            var prefabStatus = PrefabUtility.GetPrefabInstanceStatus(gameObject);
            return prefabStatus.ToString();
        }

        /// <summary>
        /// Gets the full hierarchy path of a transform.
        /// </summary>
        private static string GetHierarchyPath(Transform transform)
        {
            var path = transform.name;
            var current = transform.parent;

            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        /// <summary>
        /// Formats a Vector3 for JSON output.
        /// </summary>
        private static object FormatVector3(Vector3 vector)
        {
            return new { x = vector.x, y = vector.y, z = vector.z };
        }
    }
}
