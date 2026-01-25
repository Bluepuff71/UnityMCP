using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMCP.Editor.Tools.VFX
{
    /// <summary>
    /// Shared utilities for VFX operations (particles, lines, trails).
    /// </summary>
    public static class VFXCommon
    {
        #region GameObject Finding

        /// <summary>
        /// Finds a GameObject by instance ID, name, or path.
        /// </summary>
        public static GameObject FindGameObject(string target, bool searchInactive = true)
        {
            if (string.IsNullOrEmpty(target))
            {
                return null;
            }

            Scene activeScene = GetActiveScene();

            // Try instance ID first
            if (int.TryParse(target, out int instanceId))
            {
                var obj = EditorUtility.InstanceIDToObject(instanceId);
                if (obj is GameObject gameObject)
                {
                    return gameObject;
                }
                if (obj is Component component)
                {
                    return component.gameObject;
                }
            }

            // Try path-based lookup
            if (target.Contains("/"))
            {
                var roots = activeScene.GetRootGameObjects();
                foreach (var root in roots)
                {
                    if (root == null)
                    {
                        continue;
                    }

                    string rootPath = root.name;
                    if (target.Equals(rootPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return root;
                    }

                    if (target.StartsWith(rootPath + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        var found = root.transform.Find(target.Substring(rootPath.Length + 1));
                        if (found != null)
                        {
                            return found.gameObject;
                        }
                    }
                }
            }

            // Try name-based lookup
            var allObjects = GetAllSceneObjects(searchInactive);
            foreach (var gameObject in allObjects)
            {
                if (gameObject != null && gameObject.name.Equals(target, StringComparison.OrdinalIgnoreCase))
                {
                    return gameObject;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the active scene, handling prefab stage.
        /// </summary>
        public static Scene GetActiveScene()
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                return prefabStage.scene;
            }
            return EditorSceneManager.GetActiveScene();
        }

        /// <summary>
        /// Gets all GameObjects in the active scene.
        /// </summary>
        private static IEnumerable<GameObject> GetAllSceneObjects(bool includeInactive)
        {
            Scene activeScene = GetActiveScene();
            var roots = activeScene.GetRootGameObjects();
            var allObjects = new List<GameObject>();

            foreach (var root in roots)
            {
                if (root == null)
                {
                    continue;
                }

                if (includeInactive || root.activeInHierarchy)
                {
                    allObjects.Add(root);
                }

                var transforms = root.GetComponentsInChildren<Transform>(includeInactive);
                foreach (var transform in transforms)
                {
                    if (transform != null && transform.gameObject != null && transform.gameObject != root)
                    {
                        allObjects.Add(transform.gameObject);
                    }
                }
            }

            return allObjects;
        }

        /// <summary>
        /// Gets the full hierarchy path of a GameObject.
        /// </summary>
        public static string GetGameObjectPath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return string.Empty;
            }

            try
            {
                var names = new Stack<string>();
                Transform transform = gameObject.transform;
                while (transform != null)
                {
                    names.Push(transform.name);
                    transform = transform.parent;
                }
                return string.Join("/", names);
            }
            catch
            {
                return gameObject.name;
            }
        }

        #endregion

        #region Value Parsing

        /// <summary>
        /// Parses a Vector3 from various input formats.
        /// </summary>
        public static Vector3? ParseVector3(object input)
        {
            if (input == null)
            {
                return null;
            }

            try
            {
                // Handle List<object> (from JSON array)
                if (input is List<object> list && list.Count >= 3)
                {
                    return new Vector3(
                        Convert.ToSingle(list[0]),
                        Convert.ToSingle(list[1]),
                        Convert.ToSingle(list[2])
                    );
                }

                // Handle Dictionary<string, object> (from JSON object)
                if (input is Dictionary<string, object> dict)
                {
                    if (dict.TryGetValue("x", out object xValue) &&
                        dict.TryGetValue("y", out object yValue) &&
                        dict.TryGetValue("z", out object zValue))
                    {
                        return new Vector3(
                            Convert.ToSingle(xValue),
                            Convert.ToSingle(yValue),
                            Convert.ToSingle(zValue)
                        );
                    }
                }

                // Handle array types
                if (input is object[] array && array.Length >= 3)
                {
                    return new Vector3(
                        Convert.ToSingle(array[0]),
                        Convert.ToSingle(array[1]),
                        Convert.ToSingle(array[2])
                    );
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[VFXCommon] Failed to parse Vector3: {exception.Message}");
            }

            return null;
        }

        /// <summary>
        /// Parses a Color from various input formats.
        /// </summary>
        public static Color? ParseColor(object input)
        {
            if (input == null)
            {
                return null;
            }

            try
            {
                // Handle List<object> (from JSON array [r,g,b] or [r,g,b,a])
                if (input is List<object> list && list.Count >= 3)
                {
                    float red = Convert.ToSingle(list[0]);
                    float green = Convert.ToSingle(list[1]);
                    float blue = Convert.ToSingle(list[2]);
                    float alpha = list.Count >= 4 ? Convert.ToSingle(list[3]) : 1f;

                    // Normalize if values are in 0-255 range
                    if (red > 1f || green > 1f || blue > 1f)
                    {
                        red /= 255f;
                        green /= 255f;
                        blue /= 255f;
                        if (alpha > 1f) alpha /= 255f;
                    }

                    return new Color(red, green, blue, alpha);
                }

                // Handle Dictionary<string, object> (from JSON object {r,g,b,a})
                if (input is Dictionary<string, object> dict)
                {
                    if (dict.TryGetValue("r", out object rValue) &&
                        dict.TryGetValue("g", out object gValue) &&
                        dict.TryGetValue("b", out object bValue))
                    {
                        float red = Convert.ToSingle(rValue);
                        float green = Convert.ToSingle(gValue);
                        float blue = Convert.ToSingle(bValue);
                        float alpha = dict.TryGetValue("a", out object aValue) ? Convert.ToSingle(aValue) : 1f;

                        if (red > 1f || green > 1f || blue > 1f)
                        {
                            red /= 255f;
                            green /= 255f;
                            blue /= 255f;
                            if (alpha > 1f) alpha /= 255f;
                        }

                        return new Color(red, green, blue, alpha);
                    }
                }

                // Handle string color names or hex
                if (input is string colorString)
                {
                    if (!colorString.StartsWith("#"))
                    {
                        colorString = "#" + colorString;
                    }
                    if (ColorUtility.TryParseHtmlString(colorString, out Color parsedColor))
                    {
                        return parsedColor;
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[VFXCommon] Failed to parse Color: {exception.Message}");
            }

            return null;
        }

        /// <summary>
        /// Parses a Gradient from various input formats.
        /// </summary>
        public static Gradient ParseGradient(object input)
        {
            if (input == null)
            {
                return null;
            }

            try
            {
                // Handle single color
                Color? singleColor = ParseColor(input);
                if (singleColor.HasValue)
                {
                    var gradient = new Gradient();
                    gradient.SetKeys(
                        new[] { new GradientColorKey(singleColor.Value, 0f), new GradientColorKey(singleColor.Value, 1f) },
                        new[] { new GradientAlphaKey(singleColor.Value.a, 0f), new GradientAlphaKey(singleColor.Value.a, 1f) }
                    );
                    return gradient;
                }

                // Handle gradient definition with colorKeys and alphaKeys
                if (input is Dictionary<string, object> dict)
                {
                    var gradient = new Gradient();
                    var colorKeys = new List<GradientColorKey>();
                    var alphaKeys = new List<GradientAlphaKey>();

                    if (dict.TryGetValue("colorKeys", out object colorKeysObj) && colorKeysObj is List<object> colorKeysList)
                    {
                        foreach (var keyObj in colorKeysList)
                        {
                            if (keyObj is Dictionary<string, object> keyDict)
                            {
                                float time = keyDict.TryGetValue("time", out object timeValue) ? Convert.ToSingle(timeValue) : 0f;
                                Color color = Color.white;
                                if (keyDict.TryGetValue("color", out object colorValue))
                                {
                                    color = ParseColor(colorValue) ?? Color.white;
                                }
                                colorKeys.Add(new GradientColorKey(color, time));
                            }
                        }
                    }

                    if (dict.TryGetValue("alphaKeys", out object alphaKeysObj) && alphaKeysObj is List<object> alphaKeysList)
                    {
                        foreach (var keyObj in alphaKeysList)
                        {
                            if (keyObj is Dictionary<string, object> keyDict)
                            {
                                float time = keyDict.TryGetValue("time", out object timeValue) ? Convert.ToSingle(timeValue) : 0f;
                                float alpha = keyDict.TryGetValue("alpha", out object alphaValue) ? Convert.ToSingle(alphaValue) : 1f;
                                alphaKeys.Add(new GradientAlphaKey(alpha, time));
                            }
                        }
                    }

                    // Set default keys if none provided
                    if (colorKeys.Count == 0)
                    {
                        colorKeys.Add(new GradientColorKey(Color.white, 0f));
                        colorKeys.Add(new GradientColorKey(Color.white, 1f));
                    }
                    if (alphaKeys.Count == 0)
                    {
                        alphaKeys.Add(new GradientAlphaKey(1f, 0f));
                        alphaKeys.Add(new GradientAlphaKey(1f, 1f));
                    }

                    gradient.SetKeys(colorKeys.ToArray(), alphaKeys.ToArray());
                    return gradient;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[VFXCommon] Failed to parse Gradient: {exception.Message}");
            }

            return null;
        }

        /// <summary>
        /// Parses an AnimationCurve from various input formats.
        /// </summary>
        public static AnimationCurve ParseAnimationCurve(object input)
        {
            if (input == null)
            {
                return null;
            }

            try
            {
                // Handle single float value (constant curve)
                if (input is double doubleValue)
                {
                    return AnimationCurve.Constant(0f, 1f, (float)doubleValue);
                }
                if (input is float floatValue)
                {
                    return AnimationCurve.Constant(0f, 1f, floatValue);
                }
                if (input is int intValue)
                {
                    return AnimationCurve.Constant(0f, 1f, intValue);
                }
                if (input is long longValue)
                {
                    return AnimationCurve.Constant(0f, 1f, longValue);
                }

                // Handle preset strings
                if (input is string presetString)
                {
                    return presetString.ToLowerInvariant() switch
                    {
                        "linear" => AnimationCurve.Linear(0, 0, 1, 1),
                        "ease_in" or "easein" => AnimationCurve.EaseInOut(0, 0, 1, 1),
                        "ease_out" or "easeout" => AnimationCurve.EaseInOut(0, 0, 1, 1),
                        "ease_in_out" or "easeinout" => AnimationCurve.EaseInOut(0, 0, 1, 1),
                        "constant" => AnimationCurve.Constant(0, 1, 1),
                        _ => AnimationCurve.Linear(0, 0, 1, 1)
                    };
                }

                // Handle dictionary with keys or preset
                if (input is Dictionary<string, object> dict)
                {
                    if (dict.TryGetValue("preset", out object presetValue))
                    {
                        return ParseAnimationCurve(presetValue);
                    }

                    if (dict.TryGetValue("keys", out object keysValue) && keysValue is List<object> keysList)
                    {
                        var keyframes = new List<Keyframe>();
                        foreach (var keyObj in keysList)
                        {
                            if (keyObj is Dictionary<string, object> keyDict)
                            {
                                float time = keyDict.TryGetValue("time", out object t) ? Convert.ToSingle(t) : 0f;
                                float value = keyDict.TryGetValue("value", out object v) ? Convert.ToSingle(v) : 0f;
                                float inTangent = keyDict.TryGetValue("inTangent", out object inT) ? Convert.ToSingle(inT) : 0f;
                                float outTangent = keyDict.TryGetValue("outTangent", out object outT) ? Convert.ToSingle(outT) : 0f;
                                keyframes.Add(new Keyframe(time, value, inTangent, outTangent));
                            }
                            else if (keyObj is List<object> keyArray && keyArray.Count >= 2)
                            {
                                float time = Convert.ToSingle(keyArray[0]);
                                float value = Convert.ToSingle(keyArray[1]);
                                keyframes.Add(new Keyframe(time, value));
                            }
                        }

                        if (keyframes.Count > 0)
                        {
                            return new AnimationCurve(keyframes.ToArray());
                        }
                    }
                }

                // Handle array of [time, value] pairs
                if (input is List<object> list)
                {
                    var keyframes = new List<Keyframe>();
                    foreach (var item in list)
                    {
                        if (item is List<object> pair && pair.Count >= 2)
                        {
                            float time = Convert.ToSingle(pair[0]);
                            float value = Convert.ToSingle(pair[1]);
                            keyframes.Add(new Keyframe(time, value));
                        }
                    }
                    if (keyframes.Count > 0)
                    {
                        return new AnimationCurve(keyframes.ToArray());
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[VFXCommon] Failed to parse AnimationCurve: {exception.Message}");
            }

            return null;
        }

        /// <summary>
        /// Parses a list of Vector3 positions.
        /// </summary>
        public static List<Vector3> ParsePositions(object input)
        {
            var positions = new List<Vector3>();

            if (input == null)
            {
                return positions;
            }

            try
            {
                if (input is List<object> list)
                {
                    foreach (var item in list)
                    {
                        Vector3? parsed = ParseVector3(item);
                        if (parsed.HasValue)
                        {
                            positions.Add(parsed.Value);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[VFXCommon] Failed to parse positions: {exception.Message}");
            }

            return positions;
        }

        #endregion

        #region Serialization Helpers

        /// <summary>
        /// Serializes a Gradient to a dictionary.
        /// </summary>
        public static object SerializeGradient(Gradient gradient)
        {
            if (gradient == null)
            {
                return null;
            }

            var colorKeys = gradient.colorKeys.Select(k => new
            {
                time = k.time,
                color = new { r = k.color.r, g = k.color.g, b = k.color.b }
            }).ToList();

            var alphaKeys = gradient.alphaKeys.Select(k => new
            {
                time = k.time,
                alpha = k.alpha
            }).ToList();

            return new
            {
                mode = gradient.mode.ToString(),
                colorKeyCount = colorKeys.Count,
                colorKeys,
                alphaKeyCount = alphaKeys.Count,
                alphaKeys
            };
        }

        /// <summary>
        /// Serializes an AnimationCurve to a dictionary.
        /// </summary>
        public static object SerializeAnimationCurve(AnimationCurve curve)
        {
            if (curve == null || curve.keys.Length == 0)
            {
                return null;
            }

            var keys = curve.keys.Select(k => new
            {
                time = k.time,
                value = k.value,
                inTangent = k.inTangent,
                outTangent = k.outTangent
            }).ToList();

            return new
            {
                keyCount = keys.Count,
                keys,
                preWrapMode = curve.preWrapMode.ToString(),
                postWrapMode = curve.postWrapMode.ToString()
            };
        }

        /// <summary>
        /// Serializes a Color to a dictionary.
        /// </summary>
        public static object SerializeColor(Color color)
        {
            return new
            {
                r = color.r,
                g = color.g,
                b = color.b,
                a = color.a,
                hex = ColorUtility.ToHtmlStringRGBA(color)
            };
        }

        /// <summary>
        /// Serializes a Vector3 to an array.
        /// </summary>
        public static float[] SerializeVector3(Vector3 vector)
        {
            return new[] { vector.x, vector.y, vector.z };
        }

        #endregion

        #region Dictionary Helpers

        /// <summary>
        /// Converts input to a properties dictionary.
        /// </summary>
        public static Dictionary<string, object> ConvertToDictionary(object input)
        {
            if (input == null)
            {
                return null;
            }

            if (input is Dictionary<string, object> dict)
            {
                return dict;
            }

            if (input is System.Collections.IDictionary iDict)
            {
                var result = new Dictionary<string, object>();
                foreach (var key in iDict.Keys)
                {
                    result[key.ToString()] = iDict[key];
                }
                return result;
            }

            return null;
        }

        #endregion
    }
}
