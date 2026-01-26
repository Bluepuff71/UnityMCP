using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_MCP_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace UnityMCP.Editor.Resources.Project
{
    /// <summary>
    /// Resource provider for input system settings.
    /// </summary>
    public static class InputSettings
    {
        /// <summary>
        /// Gets input system configuration including action maps, actions, and bindings
        /// for the new Input System, or input axes for the legacy Input Manager.
        /// </summary>
        /// <returns>Object containing input settings information.</returns>
        [MCPResource("project://input", "Input system actions, bindings, or legacy input axes")]
        public static object Get()
        {
#if UNITY_MCP_INPUT_SYSTEM
            return GetNewInputSystemSettings();
#else
            return GetLegacyInputSettings();
#endif
        }

#if UNITY_MCP_INPUT_SYSTEM
        private static object GetNewInputSystemSettings()
        {
            // Find all InputActionAsset files in the project
            var inputActionAssetGuids = AssetDatabase.FindAssets("t:InputActionAsset");
            var inputActionAssets = new List<object>();

            foreach (var guid in inputActionAssetGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var inputActionAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(assetPath);

                if (inputActionAsset != null)
                {
                    var actionMaps = new List<object>();

                    foreach (var actionMap in inputActionAsset.actionMaps)
                    {
                        var actions = new List<object>();

                        foreach (var action in actionMap.actions)
                        {
                            var bindings = action.bindings
                                .Select(binding => new
                                {
                                    path = binding.path,
                                    interactions = binding.interactions,
                                    processors = binding.processors,
                                    groups = binding.groups,
                                    isComposite = binding.isComposite,
                                    isPartOfComposite = binding.isPartOfComposite,
                                    name = binding.name
                                })
                                .ToArray();

                            actions.Add(new
                            {
                                name = action.name,
                                type = action.type.ToString(),
                                expectedControlType = action.expectedControlType,
                                interactions = action.interactions,
                                processors = action.processors,
                                bindingCount = bindings.Length,
                                bindings = bindings
                            });
                        }

                        actionMaps.Add(new
                        {
                            name = actionMap.name,
                            actionCount = actions.Count,
                            actions = actions.ToArray()
                        });
                    }

                    inputActionAssets.Add(new
                    {
                        name = inputActionAsset.name,
                        path = assetPath,
                        actionMapCount = actionMaps.Count,
                        actionMaps = actionMaps.ToArray()
                    });
                }
            }

            // Get Input System settings
            var inputSettings = InputSystem.settings;

            return new
            {
                inputSystem = "New Input System",
                settings = new
                {
                    updateMode = inputSettings.updateMode.ToString(),
                    compensateForScreenOrientation = inputSettings.compensateForScreenOrientation,
                    defaultDeadzoneMin = inputSettings.defaultDeadzoneMin,
                    defaultDeadzoneMax = inputSettings.defaultDeadzoneMax,
                    defaultButtonPressPoint = inputSettings.defaultButtonPressPoint,
                    defaultTapTime = inputSettings.defaultTapTime,
                    defaultSlowTapTime = inputSettings.defaultSlowTapTime,
                    defaultHoldTime = inputSettings.defaultHoldTime,
                    tapRadius = inputSettings.tapRadius,
                    multiTapDelayTime = inputSettings.multiTapDelayTime
                },
                assetCount = inputActionAssets.Count,
                assets = inputActionAssets.ToArray()
            };
        }
#endif

        private static object GetLegacyInputSettings()
        {
            // Load the InputManager asset to read legacy input axes
            var inputManagerAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset");
            var serializedObject = new SerializedObject(inputManagerAsset[0]);
            var axesProperty = serializedObject.FindProperty("m_Axes");

            var axes = new List<object>();

            for (int i = 0; i < axesProperty.arraySize; i++)
            {
                var axisProperty = axesProperty.GetArrayElementAtIndex(i);

                var axisName = axisProperty.FindPropertyRelative("m_Name")?.stringValue ?? "";
                var descriptiveName = axisProperty.FindPropertyRelative("descriptiveName")?.stringValue ?? "";
                var descriptiveNegativeName = axisProperty.FindPropertyRelative("descriptiveNegativeName")?.stringValue ?? "";
                var negativeButton = axisProperty.FindPropertyRelative("negativeButton")?.stringValue ?? "";
                var positiveButton = axisProperty.FindPropertyRelative("positiveButton")?.stringValue ?? "";
                var altNegativeButton = axisProperty.FindPropertyRelative("altNegativeButton")?.stringValue ?? "";
                var altPositiveButton = axisProperty.FindPropertyRelative("altPositiveButton")?.stringValue ?? "";
                var gravity = axisProperty.FindPropertyRelative("gravity")?.floatValue ?? 0f;
                var dead = axisProperty.FindPropertyRelative("dead")?.floatValue ?? 0f;
                var sensitivity = axisProperty.FindPropertyRelative("sensitivity")?.floatValue ?? 0f;
                var snap = axisProperty.FindPropertyRelative("snap")?.boolValue ?? false;
                var invert = axisProperty.FindPropertyRelative("invert")?.boolValue ?? false;
                var axisType = axisProperty.FindPropertyRelative("type")?.intValue ?? 0;
                var axisNumber = axisProperty.FindPropertyRelative("axis")?.intValue ?? 0;
                var joyNum = axisProperty.FindPropertyRelative("joyNum")?.intValue ?? 0;

                axes.Add(new
                {
                    name = axisName,
                    descriptiveName = descriptiveName,
                    descriptiveNegativeName = descriptiveNegativeName,
                    negativeButton = negativeButton,
                    positiveButton = positiveButton,
                    altNegativeButton = altNegativeButton,
                    altPositiveButton = altPositiveButton,
                    gravity = gravity,
                    deadZone = dead,
                    sensitivity = sensitivity,
                    snap = snap,
                    invert = invert,
                    type = GetAxisTypeName(axisType),
                    axis = GetAxisName(axisNumber),
                    joyNum = joyNum
                });
            }

            return new
            {
                inputSystem = "Legacy Input Manager",
                axisCount = axes.Count,
                axes = axes.ToArray()
            };
        }

        private static string GetAxisTypeName(int type)
        {
            return type switch
            {
                0 => "KeyOrMouseButton",
                1 => "MouseMovement",
                2 => "JoystickAxis",
                _ => $"Unknown ({type})"
            };
        }

        private static string GetAxisName(int axis)
        {
            return axis switch
            {
                0 => "X axis",
                1 => "Y axis",
                2 => "3rd axis (Joysticks and Scrollwheel)",
                3 => "4th axis (Joysticks)",
                4 => "5th axis (Joysticks)",
                5 => "6th axis (Joysticks)",
                6 => "7th axis (Joysticks)",
                7 => "8th axis (Joysticks)",
                8 => "9th axis (Joysticks)",
                9 => "10th axis (Joysticks)",
                10 => "11th axis (Joysticks)",
                11 => "12th axis (Joysticks)",
                12 => "13th axis (Joysticks)",
                13 => "14th axis (Joysticks)",
                14 => "15th axis (Joysticks)",
                15 => "16th axis (Joysticks)",
                16 => "17th axis (Joysticks)",
                17 => "18th axis (Joysticks)",
                18 => "19th axis (Joysticks)",
                19 => "20th axis (Joysticks)",
                20 => "21st axis (Joysticks)",
                21 => "22nd axis (Joysticks)",
                22 => "23rd axis (Joysticks)",
                23 => "24th axis (Joysticks)",
                24 => "25th axis (Joysticks)",
                25 => "26th axis (Joysticks)",
                26 => "27th axis (Joysticks)",
                27 => "28th axis (Joysticks)",
                _ => $"Unknown axis ({axis})"
            };
        }
    }
}
