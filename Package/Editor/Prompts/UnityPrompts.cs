using System.Collections.Generic;
using UnityMCP.Editor;
using UnityMCP.Editor.Core;

namespace UnityMCP.Editor.Prompts
{
    /// <summary>
    /// Built-in MCP prompts for common Unity workflows.
    /// </summary>
    public static class UnityPrompts
    {
        [MCPPrompt("read_gameobject", "Read detailed info about a GameObject")]
        public static PromptResult ReadGameObject(
            [MCPParam("name", "Name of the GameObject to inspect", required: true)] string name,
            [MCPParam("include_children", "Whether to include children hierarchy (true/false)")] string includeChildren = "false")
        {
            bool showChildren = includeChildren?.ToLower() == "true";

            string instructions = $@"Use the following MCP tools to read detailed info about the GameObject named ""{name}"":

1. Use `gameobject_find` with search_term=""{name}"" to find the GameObject and get its instance ID
2. Use `component_manage` with action=""inspect"" and target=""{name}"" and component_type=""Transform"" to see its position, rotation, and scale
3. Use `gameobject_find` with search_term=""{name}"" to see what components are attached";

            if (showChildren)
            {
                instructions += $@"
4. Use `scene_get_hierarchy` with parent=""{name}"" and max_depth=3 to see the children hierarchy";
            }

            return new PromptResult
            {
                description = $"Instructions to inspect GameObject '{name}'",
                messages = new List<PromptMessage>
                {
                    new PromptMessage
                    {
                        role = "user",
                        content = new PromptMessageContent
                        {
                            type = "text",
                            text = instructions
                        }
                    }
                }
            };
        }

        [MCPPrompt("inspect_prefab", "Inspect a prefab asset")]
        public static PromptResult InspectPrefab(
            [MCPParam("path", "Path to the prefab asset (e.g., Assets/Prefabs/Player.prefab)", required: true)] string path)
        {
            string instructions = $@"Use the following MCP tools to inspect the prefab at ""{path}"":

1. Use `asset_manage` with action=""get_info"" and path=""{path}"" to get the prefab asset details
2. Use `prefab_manage` with action=""open_stage"" and prefab_path=""{path}"" to open the prefab for editing
3. Use `scene_get_hierarchy` with max_depth=3 to see the prefab's internal hierarchy
4. For each important GameObject in the hierarchy, use `component_manage` with action=""inspect"" to see its components
5. Use `prefab_manage` with action=""close_stage"" to close the prefab stage when done";

            return new PromptResult
            {
                description = $"Instructions to inspect prefab at '{path}'",
                messages = new List<PromptMessage>
                {
                    new PromptMessage
                    {
                        role = "user",
                        content = new PromptMessageContent
                        {
                            type = "text",
                            text = instructions
                        }
                    }
                }
            };
        }

        [MCPPrompt("modify_component", "Modify a component on a GameObject")]
        public static PromptResult ModifyComponent(
            [MCPParam("target", "Name or path of the target GameObject", required: true)] string target,
            [MCPParam("component", "Component type to modify (e.g., Rigidbody, BoxCollider)", required: true)] string component,
            [MCPParam("property", "Property to modify (e.g., mass, isTrigger)", required: true)] string property)
        {
            string instructions = $@"Use the following MCP tools to modify the {component}.{property} on ""{target}"":

1. Use `gameobject_find` with search_term=""{target}"" to verify the GameObject exists
2. Use `component_manage` with action=""inspect"", target=""{target}"", component_type=""{component}"" to see current property values and verify the property name
3. Use `component_manage` with action=""set_property"", target=""{target}"", component_type=""{component}"", property=""{property}"", and value=<new_value> to set the property
4. Use `component_manage` with action=""inspect"", target=""{target}"", component_type=""{component}"" again to verify the change took effect";

            return new PromptResult
            {
                description = $"Instructions to modify {component}.{property} on '{target}'",
                messages = new List<PromptMessage>
                {
                    new PromptMessage
                    {
                        role = "user",
                        content = new PromptMessageContent
                        {
                            type = "text",
                            text = instructions
                        }
                    }
                }
            };
        }

        [MCPPrompt("setup_scene", "Set up a new scene")]
        public static PromptResult SetupScene(
            [MCPParam("scene_type", "Type of scene: 3d, 2d, or ui (default: 3d)")] string sceneType = "3d")
        {
            string typeNormalized = sceneType?.ToLower() ?? "3d";

            string instructions;

            switch (typeNormalized)
            {
                case "2d":
                    instructions = @"Use the following MCP tools to set up a new 2D scene:

1. Use `scene_create` with name=""NewScene2D"" to create a new scene
2. Use `scene_load` with name=""NewScene2D"" to load the scene
3. Use `gameobject_find` with search_term=""Main Camera"" to find the default camera
4. Use `component_manage` with action=""set_property"", target=""Main Camera"", component_type=""Camera"", property=""orthographic"", value=true to set it to orthographic
5. Use `gameobject_manage` with action=""create"", name=""Background"", primitiveType=""Quad"", position=[0,0,1], scale=[20,20,1] to create a background
6. Use `scene_save` to save the scene";
                    break;

                case "ui":
                    instructions = @"Use the following MCP tools to set up a new UI scene:

1. Use `scene_create` with name=""NewSceneUI"" to create a new scene
2. Use `scene_load` with name=""NewSceneUI"" to load the scene
3. Use `gameobject_manage` with action=""create"", name=""Canvas"" to create a Canvas (add Canvas, CanvasScaler, and GraphicRaycaster components)
4. Use `component_manage` with action=""add"", target=""Canvas"", component_type=""Canvas""
5. Use `component_manage` with action=""add"", target=""Canvas"", component_type=""UnityEngine.UI.CanvasScaler""
6. Use `component_manage` with action=""add"", target=""Canvas"", component_type=""UnityEngine.UI.GraphicRaycaster""
7. Use `gameobject_manage` with action=""create"", name=""EventSystem""
8. Use `component_manage` with action=""add"", target=""EventSystem"", component_type=""UnityEngine.EventSystems.EventSystem""
9. Use `scene_save` to save the scene";
                    break;

                default: // "3d"
                    instructions = @"Use the following MCP tools to set up a new 3D scene:

1. Use `scene_create` with name=""NewScene3D"" to create a new scene
2. Use `scene_load` with name=""NewScene3D"" to load the scene
3. Use `gameobject_manage` with action=""create"", name=""Ground"", primitiveType=""Plane"", position=[0,0,0], scale=[10,1,10] to create a ground plane
4. Use `gameobject_manage` with action=""create"", name=""Directional Light"" and then use `component_manage` to add a Light component with type=Directional
5. Use `gameobject_find` with search_term=""Main Camera"" to find the camera and reposition with `gameobject_manage` action=""modify"" position=[0,5,-10] rotation=[30,0,0]
6. Use `scene_save` to save the scene";
                    break;
            }

            return new PromptResult
            {
                description = $"Instructions to set up a new {typeNormalized} scene",
                messages = new List<PromptMessage>
                {
                    new PromptMessage
                    {
                        role = "user",
                        content = new PromptMessageContent
                        {
                            type = "text",
                            text = instructions
                        }
                    }
                }
            };
        }
    }
}
