using System;
using System.Collections.Generic;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Provides contextual guidance for AI agents on using Unity tools and conventions effectively.
    /// </summary>
    public static class UnityGuide
    {
        #region Guide Content

        private static readonly Dictionary<string, string> TopicGuides = new Dictionary<string, string>
        {
            ["scene"] = @"# Scene Management Guide

## Coordinate System
- Unity uses a left-handed Y-up coordinate system: X=right, Y=up, Z=forward.
- Rotations are in degrees (Euler angles), applied in Z-X-Y order.
- World units are unitless but 1 unit = 1 meter by convention.

## Scene Composition Order
1. Use `scene_get_active` to identify the current scene before making changes.
2. Use `scene_get_hierarchy` to inspect existing objects and understand structure.
3. Create foundational objects first: ground/terrain, lighting, camera.
4. Add structural objects (walls, floors, platforms), then detail objects (props, decorations).
5. Add interactive objects last (characters, triggers, collectibles).
6. Use `scene_save` after significant changes to avoid losing work.

## Hierarchy Best Practices
- Group related objects under empty parent GameObjects for organization.
- Use descriptive names: 'Environment/Lighting', 'Gameplay/Enemies', 'UI/HUD'.
- Keep hierarchy depth shallow (3-4 levels max) for performance and clarity.
- Static environment objects should be separate from dynamic/moving objects.

## Lighting Setup
- Every scene needs at least one light source. Unity scenes start with a Directional Light.
- For outdoor scenes: one Directional Light (sun) + Skybox material on Camera.
- For indoor scenes: multiple Point Lights or Spot Lights placed at logical positions.
- Use `gameobject_manage` with action 'create' and type 'light' to add lights.
- Configure light properties via `component_manage` on the Light component.

## Key Tools
- `scene_create` / `scene_load` / `scene_save` - Scene lifecycle management.
- `scene_get_hierarchy` - Inspect full object tree (use max_depth to limit output).
- `scene_screenshot` - Capture the current Game or Scene view for visual verification.",

            ["gameobjects"] = @"# GameObject Management Guide

## Creation Patterns
- Use `gameobject_manage` with action='create' for all object creation.
- Built-in types: 'empty', 'cube', 'sphere', 'capsule', 'cylinder', 'plane', 'quad', 'light', 'camera'.
- Always provide a descriptive name via the 'name' parameter.
- After creation, the response includes the instanceId - save this for subsequent operations.

## Transform Conventions
- Position: world-space coordinates as [x, y, z]. Ground level is typically y=0.
- Rotation: Euler angles in degrees as [x, y, z]. Identity rotation is [0, 0, 0].
- Scale: [1, 1, 1] is default. Uniform scaling (same x/y/z) prevents mesh distortion.
- Use `gameobject_manage` with action='modify' to set transform properties.
- Use action='move_relative' for incremental position/rotation changes.

## Parenting Rules
- Set parent via `gameobject_manage` action='modify' with parent_path or parent_id.
- Child transforms become relative to parent (local space).
- Moving a parent moves all children. Scaling a parent scales all children.
- To unparent, set parent_path to an empty string.
- Use `scene_get_hierarchy` to verify parent-child relationships.

## Prefab Workflow
1. Create and configure a GameObject in the scene.
2. Use `prefab_manage` with action='create' to save it as a prefab asset.
3. Use `prefab_manage` with action='instantiate' to spawn copies from the prefab.
4. Changes to the prefab asset propagate to all instances (unless overridden).

## Duplication and Deletion
- `gameobject_manage` action='duplicate' clones an object with all components and children.
- `gameobject_manage` action='delete' removes an object and all its children permanently.
- Always verify with `scene_get_hierarchy` after bulk operations.

## Key Tools
- `gameobject_manage` - Create, modify, delete, duplicate, move objects.
- `component_manage` - Add/remove/modify components on GameObjects.
- `gameobject_find` - Search for objects by name, tag, layer, or component type.
- `selection` action='get' / action='set' - Track which objects are selected in the Editor.",

            ["scripting"] = @"# Scripting Guide

## Script Creation Workflow
1. Use `manage_script` with action='create' to generate a new C# script file.
2. Call `unity_refresh` with compile='request' to trigger compilation.
3. Check `console_read` for compilation errors - fix any issues in the script.
4. Once compiled, attach the script using `component_manage` action='add' with the script's class name.
5. Configure exposed fields via `component_manage` action='set_property'.

## Script Editing Workflow
1. Use `manage_script` action='read' to view current script contents.
2. Use `manage_script` action='update' to update the script.
3. Call `unity_refresh` with compile='request' after every edit.
4. Always check `console_read` for errors before proceeding.

## Common Component Patterns
- Rigidbody: Required for physics-driven movement. Add before colliders.
- Colliders (BoxCollider, SphereCollider, MeshCollider): Required for physics interactions and triggers.
- AudioSource: Needs an AudioClip assigned to play sounds.
- Animator: Requires an AnimatorController asset for animation state machines.
- Use `component_manage` action='inspect' to inspect component properties and available fields.

## MonoBehaviour Lifecycle (execution order)
- Awake() -> OnEnable() -> Start() [initialization phase]
- FixedUpdate() [physics, runs at fixed intervals]
- Update() [every frame, main game logic]
- LateUpdate() [after all Update calls, good for camera follow]
- OnDisable() -> OnDestroy() [cleanup phase]

## Key Conventions
- Class name must match the filename exactly (case-sensitive).
- Scripts must be inside a folder recognized by Unity (Assets/ or Packages/).
- Use `[SerializeField]` to expose private fields in the Inspector.
- Use `[RequireComponent(typeof(...))]` to enforce component dependencies.

## Key Tools
- `manage_script` - Create, read, update, delete, and validate scripts.
- `component_manage` - Attach scripts and configure serialized fields.
- `console_read` - Check for compilation and runtime errors.
- `unity_refresh` - Trigger compilation after script changes.",

            ["materials"] = @"# Materials and Shaders Guide

## Material Setup Pipeline
1. Use `manage_material` action='create' to create a new material.
2. Specify a shader (default is 'Standard' for Built-in RP, or 'Universal Render Pipeline/Lit' for URP).
3. Use `manage_material` action='set_property' or action='set_color' to set properties.
4. Assign the material to a renderer via `manage_material` action='assign_to_renderer'.

## Common Shader Properties (Standard/URP Lit)
- _Color / _BaseColor: Main tint color as [r, g, b, a] with values 0-1.
- _MainTex / _BaseMap: Albedo/diffuse texture (asset path).
- _Metallic: Metalness value 0-1 (0=non-metal, 1=full metal).
- _Smoothness / _Glossiness: Surface smoothness 0-1.
- _BumpMap / _NormalMap: Normal map texture for surface detail.
- _EmissionColor: Emission color (set to non-black to enable emission).
- Use `manage_shader` action='get' to discover all properties for any shader.

## Texture Import Settings
- Use `manage_texture` action='get' to check current import settings.
- Use `manage_texture` action='set_import_settings' to configure:
  - textureType: 'Default', 'NormalMap', 'Sprite', 'Cursor', 'Lightmap'.
  - maxSize: Power of 2 (256, 512, 1024, 2048, 4096). Smaller = less memory.
  - compression: 'None', 'LowQuality', 'NormalQuality', 'HighQuality'.
  - filterMode: 'Point' (pixel art), 'Bilinear' (smooth), 'Trilinear' (mipmapped smooth).
- Normal maps: Set textureType to 'NormalMap' before assigning to _BumpMap.

## Material Inspection
- `manage_material` action='get_info' returns all current property values.
- `manage_shader` action='get' lists every property the shader exposes with types and defaults.
- Use `asset_manage` action='search' with type filter 'Material' to find existing materials.

## Key Tools
- `manage_material` - Create, modify, inspect, and assign materials.
- `manage_shader` - Get shader info, list/find shaders, manage keywords.
- `manage_texture` - Import settings and texture configuration.
- `asset_manage` - Search and manage material/texture assets.",

            ["debugging"] = @"# Debugging Guide

## Console Reading Workflow
1. Start with `console_read` using types='error,warning' to check for problems.
2. If errors exist, read with include_stacktrace=true for source file and line info.
3. Use filter_text to narrow down specific error patterns.
4. Common error types:
   - Compilation errors: Fix scripts, then `unity_refresh` with compile='request'.
   - NullReferenceException: A required reference is missing - check component fields.
   - MissingComponentException: A GetComponent call failed - verify component exists.
   - Shader errors: Check material/shader compatibility with current render pipeline.

## Diagnostic Workflow
1. Use `scene_get_hierarchy` to verify scene structure is correct.
2. Use `component_manage` action='inspect' to inspect component state and field values.
3. Use `gameobject_find` to verify objects exist with expected names/tags/components.
4. Use `scene_screenshot` to visually verify the scene state.
5. Use `selection` action='set' then action='get' to focus on specific objects.

## Profiler Workflow (Performance)
1. Start a capture with `profiler` action='start' specifying duration.
2. This returns a job_id immediately (async operation).
3. Poll `profiler` action='get_job' with the job_id until status is 'completed'.
4. Results include frame times, CPU/GPU usage, memory stats, and top functions.
5. Look for frames exceeding 16.67ms (60fps target) or 33.33ms (30fps target).

## Common Error Patterns and Fixes
- 'Can't add component because class doesn't exist': Script has compilation errors or class name doesn't match filename. Check `console_read` for compile errors.
- 'Material doesn't have property': Wrong property name for the shader. Use `manage_shader` action='get' to find correct names.
- 'Object reference not set': A serialized field is null. Use `component_manage` action='inspect' to check, then action='set_property' to assign.
- 'Script class cannot be found': Call `unity_refresh` with compile='request' first.

## Key Tools
- `console_read` - Read and filter Unity Console log entries.
- `profiler` action='start' / action='get_job' - Performance profiling (async).
- `scene_screenshot` - Visual verification of scene state.
- `scene_get_hierarchy` - Structural verification of scene objects.",

            ["building"] = @"# Build Pipeline Guide

## Build Workflow
1. Verify there are no compilation errors: `console_read` with types='error'.
2. Ensure the scene is saved: `scene_save`.
3. Start a build with `build` action='start', specifying target platform and scenes.
4. This returns a job_id immediately (async operation).
5. Poll `build` action='get_job' with the job_id until status is 'completed' or 'failed'.
6. Check build result for errors in the response.

## Test Running
1. Use `tests` action='run' to start test execution (EditMode or PlayMode).
2. This returns a job_id immediately (async operation).
3. Poll `tests` action='get_job' with the job_id until status is 'completed'.
4. Results include pass/fail counts and individual test results with messages.
5. Fix failing tests, recompile with `unity_refresh`, then re-run.

## Platform Targeting
- Common targets: 'StandaloneWindows64', 'StandaloneOSX', 'StandaloneLinux64', 'Android', 'iOS', 'WebGL'.
- Each platform may require specific settings and SDK installations.
- Use `manage_editor` to check current build target and editor settings.

## Async Job Polling Pattern
All long-running operations (build, test, profiler) follow the same pattern:
1. Call the start tool -> returns { job_id: ""..."" }.
2. Poll the get/status tool with that job_id.
3. Check the 'status' field: 'running', 'completed', or 'failed'.
4. If 'running', wait a few seconds and poll again.
5. If 'completed', the result payload contains the output data.
6. If 'failed', the result payload contains error details.

## Key Tools
- `build` action='start' / action='get_job' - Build pipeline (async).
- `tests` action='run' / action='get_job' - Test execution (async).
- `console_read` - Check for build/compile errors before building.
- `scene_save` - Save scenes before building.
- `manage_editor` - Check and configure editor/build settings.",

            ["ui"] = @"# UI Development Guide

## UI Toolkit (Recommended for Unity 6+)
- UI Toolkit uses UXML (layout) and USS (styling), similar to HTML/CSS.
- Use `uitoolkit_query` to inspect UI Toolkit panels and elements at runtime.
- Query by element name, USS class, or element type.
- Common element types: Button, Label, TextField, ScrollView, VisualElement, ListView.
- USS classes use dot notation: '.my-class'. Names use hash: '#my-element'.

## Canvas-Based UI (Legacy/uGUI)
- Requires a Canvas GameObject in the scene. Create via `gameobject_manage` action='create'.
- Canvas must have an EventSystem sibling for input handling.
- Canvas render modes: 'Screen Space - Overlay' (HUD), 'Screen Space - Camera' (3D UI), 'World Space' (in-game UI).
- Add UI elements as children of the Canvas: Button, Text (TextMeshPro), Image, Panel.

## EventSystem Requirements
- Every scene with UI interaction needs exactly one EventSystem object.
- If UI clicks aren't working, check for a missing EventSystem.
- Use `gameobject_find` with component_type='EventSystem' to verify.
- Create one via `execute_menu_item` with 'GameObject/UI/Event System'.

## UI Toolkit Querying
1. Use `uitoolkit_query` to list all open UI Toolkit panels.
2. Query specific panels by name to inspect their element trees.
3. Filter by element type or USS class to find specific controls.
4. Useful for debugging Editor extensions and runtime UI.

## Key Tools
- `uitoolkit_query` - Inspect UI Toolkit panels and elements.
- `gameobject_manage` - Create Canvas and UI GameObjects.
- `component_manage` - Configure UI components (Canvas, Image, Button, Text).
- `gameobject_find` - Verify EventSystem and Canvas existence.
- `execute_menu_item` - Access Unity's built-in UI creation menu items.",

            ["workflows"] = @"# Multi-Step Workflow Recipes

## Create a Prefab from Scratch
1. `gameobject_manage` action='create' -> create the base object (e.g., cube, empty).
2. `component_manage` action='add' -> add required components (Rigidbody, Collider, scripts).
3. `component_manage` action='set_property' -> configure component properties.
4. `manage_material` action='create' -> create a material for appearance.
5. `manage_material` action='assign_to_renderer' -> assign material to MeshRenderer.
6. `prefab_manage` action='create_from_gameobject' -> save as prefab asset at desired path.

## Set Up a Complete Scene
1. `scene_create` -> create a new empty scene.
2. `gameobject_manage` action='create' type='light' -> add directional light.
3. `gameobject_manage` action='create' type='camera' -> add main camera (if not present).
4. `gameobject_manage` action='create' type='plane' -> add ground plane.
5. Build scene content (objects, lighting, prefab instances).
6. `scene_save` -> save the scene to disk.

## Debug a Runtime Error
1. `console_read` types='error' include_stacktrace=true -> identify the error.
2. `manage_script` action='read' -> read the problematic script.
3. `component_manage` action='inspect' -> inspect the failing component's field values.
4. `manage_script` action='update' -> fix the script.
5. `unity_refresh` compile='request' -> recompile.
6. `console_read` types='error' -> verify the error is resolved.

## Create and Apply a Custom Material
1. `manage_shader` action='get' -> inspect available shader properties.
2. `manage_material` action='create' -> create material with chosen shader.
3. `manage_material` action='set_property' / action='set_color' -> set color, texture, and value properties.
4. `gameobject_find` -> find target objects.
5. `manage_material` action='assign_to_renderer' -> assign material to each object's renderer.
6. `scene_screenshot` -> visually verify the result.

## Iterative Script Development
1. `manage_script` action='create' -> create initial script.
2. `unity_refresh` compile='request' -> compile.
3. `console_read` types='error' -> check for compilation errors.
4. If errors: `manage_script` action='update' -> fix, then repeat steps 2-3.
5. `component_manage` action='add' -> attach compiled script to a GameObject.
6. `component_manage` action='set_property' -> configure serialized fields.
7. `playmode` action='enter' -> test in play mode.
8. `console_read` types='error,warning,log' -> check runtime behavior.

## Performance Investigation
1. `profiler` action='start' -> start profiling session.
2. `profiler` action='get_job' -> poll until complete.
3. Analyze frame times and hotspots in the results.
4. `scene_get_hierarchy` -> check for excessive object counts.
5. `component_manage` action='inspect' -> inspect suspected expensive components.
6. Make optimizations, then re-profile to verify improvement.

## Tool Chaining Tips
- Always check `console_read` after `unity_refresh` to catch compilation errors.
- Use `scene_get_hierarchy` before and after bulk operations to verify changes.
- Use `scene_screenshot` after visual changes to confirm the result.
- Save frequently with `scene_save` to avoid losing work.
- Use `gameobject_find` to locate objects by name/tag instead of hardcoding instance IDs.
- For async operations (build, test, profiler), always use the poll pattern: start -> get_job -> check status.

## Checkpoint & Asset Tracking
- Checkpoints use a **bucket model**: an active (mutable) bucket accumulates changes until frozen.
- `scene_checkpoint` action='save' with new_bucket=false merges into the active bucket; new_bucket=true freezes it and starts fresh.
- All destructive tools (create, modify, delete, etc.) automatically call `CheckpointManager.Track()` to register modified assets.
- When a checkpoint is saved, all pending tracked assets are snapshotted alongside the scene file.
- Restoring a checkpoint restores both the scene and all tracked asset files.
- Use `scene_checkpoint` action='diff' to compare checkpoints, including tracked asset differences.

### Tool Author Convention for Track()
When writing new tools that modify **project assets** (materials, scripts, prefabs, textures, ScriptableObjects), add a one-liner after each mutation:
- `CheckpointManager.Track(unityObject)` for Unity asset objects (materials, prefabs, ScriptableObjects).
- `CheckpointManager.Track(assetPath)` for string-based asset paths.
- Place Track() **after** the modification is complete (after SetDirty, SaveAssets, etc.).
- For **delete** operations, Track() goes **before** the deletion (the object is destroyed after).
- **Do NOT** call Track() on scene GameObjects or components — they have no asset path and the call is a no-op. Scene changes are captured by the scene file copy.
- Import `using UnityMCP.Editor.Services;` to access CheckpointManager.",

            ["getting_started"] = @"# Getting Started with UnityMCP

## First Steps
1. Call `scene_get_active` to see what scene is loaded and whether it has unsaved changes.
2. Call `scene_describe` for an overview of the scene (camera, lighting, objects, issues).
3. Call `scene_checkpoint` action='save' to create a safety checkpoint before making changes.

## Core Workflow: Find -> Inspect -> Modify -> Verify
- `gameobject_find` or `scene_get_hierarchy` to locate objects (returns instance IDs).
- `component_manage` action='inspect' to read component properties.
- `gameobject_manage` or `component_manage` to make changes.
- `scene_screenshot` or `component_manage` action='inspect' to verify results.

## Safety Practices
- Save checkpoints before destructive operations (delete, bulk modify, script edits).
- Check `console_read` after `unity_refresh` or script changes for compile errors.
- Use `scene_save` periodically to persist changes to disk.

## Discovering More Tools
- `search_tools` with no args for a full category listing.
- `unity_guide` with a topic for detailed guidance: scene, gameobjects, scripting, materials, debugging, building, ui, workflows.
- `diagnose` to scan for missing references, shader issues, and build problems."
        };

        private const string OverviewContent = @"# Unity MCP Tool Guide

## Available Topics
Call unity_guide with a topic parameter for detailed guidance on each area:

- **getting_started** - First steps, core workflow pattern, safety practices, and tool discovery.
- **scene** - Scene composition, hierarchy structure, lighting setup, and coordinate conventions.
- **gameobjects** - Object creation, parenting, transform conventions, and prefab workflow.
- **scripting** - Script lifecycle, creation-to-attachment workflow, and common component patterns.
- **materials** - Material/shader pipeline, texture import settings, and common property names.
- **debugging** - Console reading, diagnostic workflows, profiler usage, and common error fixes.
- **building** - Build pipeline, test execution, platform targeting, and async job polling.
- **ui** - UI Toolkit querying, Canvas setup, and EventSystem requirements.
- **workflows** - Multi-step tool chaining recipes for common tasks, plus checkpoint and asset tracking conventions.

## Universal Conventions
- **Coordinate System:** Left-handed Y-up. X=right, Y=up, Z=forward. 1 unit = 1 meter. Rotations in degrees.
- **Scale Norms:** Default scale is [1,1,1]. Standard humanoid is ~1.8 units tall. Keep uniform scale to avoid distortion.
- **Naming Conventions:** Use PascalCase for GameObjects ('PlayerCharacter'), paths with forward slashes ('Environment/Trees/Oak01'), and descriptive names that indicate purpose.
- **Async Pattern:** Long operations (build, test, profiler) return a job_id. Poll the same tool with action='get_job' until status is 'completed' or 'failed'.";

        #endregion

        #region Tool Entry Point

        /// <summary>
        /// Returns guidance for AI agents on using Unity tools and conventions effectively.
        /// When called without a topic, returns an overview of all available topics.
        /// When called with a topic, returns detailed guidance for that area.
        /// </summary>
        [MCPTool("unity_guide", "Returns guidance on Unity tools, conventions, and workflow recipes. New session? Start with topic='getting_started'. Use topic='workflows' for tool chaining recipes.", Category = "Guide", ReadOnlyHint = true)]
        public static object Guide(
            [MCPParam("topic", "Topic to get guidance on", Enum = new[] { "getting_started", "scene", "gameobjects", "scripting", "materials", "debugging", "building", "ui", "workflows" })] string topic = null)
        {
            try
            {
                if (string.IsNullOrEmpty(topic))
                {
                    return new
                    {
                        success = true,
                        guide = OverviewContent
                    };
                }

                string normalizedTopic = topic.ToLowerInvariant().Trim();

                if (TopicGuides.TryGetValue(normalizedTopic, out string guideContent))
                {
                    return new
                    {
                        success = true,
                        topic = normalizedTopic,
                        guide = guideContent
                    };
                }

                string validTopics = string.Join(", ", TopicGuides.Keys);
                return new
                {
                    success = false,
                    error = $"Unknown topic: '{topic}'. Valid topics: {validTopics}"
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error retrieving guide: {exception.Message}"
                };
            }
        }

        #endregion
    }
}
