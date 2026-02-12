# Unity MCP - AI Assistant Integration for Unity Editor

![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity)
![Unity 6 Compatible](https://img.shields.io/badge/Unity%206-Compatible-brightgreen?logo=unity)
![Release](https://img.shields.io/github/v/tag/Bluepuff71/UnityMCP?label=version)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-blue)

Model Context Protocol (MCP) server that enables AI assistants like Claude, Codex, and Cursor to automate Unity Editor tasks and game development workflows.

## Features

- **Zero telemetry** - Completely private Unity automation. No data collection.
- **40+ built-in tools** - Create GameObjects, run tests, build projects, manipulate scenes through AI.
- **Simple extension API** - Add custom AI tools with a single attribute.

## Requirements

- Unity 2022.3 or later
- Any MCP client: Claude Code, Claude Desktop, Codex, Cursor, or other AI assistants

## Installation

1. Open Unity Package Manager (Window → Package Manager)
2. Click `+` → "Add package from git URL"
3. Enter the URL of whichever version you want

### Latest Version (Recommended)

To install the latest version:
```
https://github.com/Bluepuff71/UnityMCP.git?path=/Package
```

### Specific Version (If I messed something up)

To install a specific version, append `#version` tag:
```
https://github.com/Bluepuff71/UnityMCP.git?path=/Package#1.0.0
```

See [Releases](https://github.com/Bluepuff71/UnityMCP/releases) for available versions.

## Setup

### Claude Code

```bash
claude mcp add unity-mcp --transport http http://localhost:8080/
```

### Claude Desktop

Add this to your Claude Desktop configuration file:

**macOS**: `~/Library/Application Support/Claude/claude_desktop_config.json`

**Windows**: `%APPDATA%\Claude\claude_desktop_config.json`

```json
{
  "mcpServers": {
    "unity-mcp": {
      "command": "npx",
      "args": ["-y", "mcp-remote", "http://localhost:8080/"]
    }
  }
}
```

Restart Claude Desktop after saving.

### Other MCP Clients (Codex, Cursor, etc.)

Unity MCP runs an HTTP server at `http://localhost:8080/`. Any MCP client with HTTP transport support can connect directly. For stdio-only clients, use the mcp-remote bridge as shown above.

*Note: Configurations for clients other than Claude Code have not been tested. Open a PR!*

## Architecture

Unity MCP runs an HTTP server on a background thread using a C plugin that persists across Unity domain reloads. This ensures the AI assistant connection stays active even while Unity recompiles scripts.

```
┌─────────────────┐
│  MCP Client     │
│  (Claude, etc.) │
└────────┬────────┘
         │ HTTP POST (JSON-RPC)
         ▼
┌─────────────────────────────────────┐
│  Proxy Plugin (C)                   │
│  - HTTP server on background thread │
│  - Persists across domain reloads   │
│  - Buffers request, waits for       │
│    response from C#                 │
└────────┬────────────────────────────┘
         │ Polling (EditorApplication.update)
         ▼
┌─────────────────────────────────────┐
│  Unity C# (main thread)            │
│  - Polls for pending requests       │
│  - Routes to MCPServer handlers     │
│  - Executes tools, reads resources  │
└─────────────────────────────────────┘
```

**During script recompilation**, the C# polling stops and the plugin holds incoming requests until the domain reload completes. The AI assistant sees a brief delay rather than a disconnection.

## Available MCP Tools

Unity MCP provides over 40 built-in tools organized by category:

### GameObject Management
- **gameobject_manage** - Create, modify, delete, duplicate GameObjects, or move them relative to other objects
- **gameobject_find** - Find GameObjects by name, tag, layer, component type, path, or instance ID with pagination

### Component Management
- **component_manage** - Add, remove, set properties, or inspect components attached to GameObjects

### Scene Management
- **scene_create** - Create a new empty scene at a specified path
- **scene_load** - Load a scene by path or build index
- **scene_save** - Save the current scene, optionally to a new path
- **scene_get_active** - Get information about the currently active scene
- **scene_get_hierarchy** - Get the hierarchy of GameObjects in the current scene with pagination
- **scene_screenshot** - Capture a screenshot of the Game View

### Asset Management
- **asset_manage** - Create, delete, move, rename, duplicate, import, search, or get info about assets
- **prefab_manage** - Open/close prefab stage, save prefabs, or create prefabs from GameObjects
- **manage_material** - Create materials, set properties, assign to renderers, or set colors
- **manage_texture** - Modify texture import settings (format, compression, size, etc.)
- **manage_shader** - Create and manage shader assets
- **manage_script** - Create C# scripts from templates
- **manage_scriptableobject** - Create and manage ScriptableObject assets
- **manage_vfx** - Create and configure particle systems, trail renderers, and line renderers

### Build & Testing
- **build_start** - Start a player build asynchronously, returns job_id for polling
- **build_get_job** - Get build job status and results
- **tests_run** - Start Unity Test Runner asynchronously (EditMode or PlayMode)
- **tests_get_job** - Get test run job status and results

### Editor Control
- **playmode_enter** - Enter play mode
- **playmode_exit** - Exit play mode
- **playmode_pause** - Toggle or set pause state
- **playmode_step** - Advance a single frame while paused
- **selection_get** - Get currently selected objects in the Unity Editor
- **selection_set** - Set selection by instance IDs or asset paths
- **execute_menu_item** - Execute Unity Editor menu items by path (with safety blacklist)
- **manage_editor** - Manage editor state, tags, layers, and tools
- **unity_refresh** - Refresh Unity asset database and optionally request script compilation

### Console & Profiling
- **console_read** - Read Unity Console log entries with filtering and pagination
- **profiler_start** - Start profiler recording, returns job_id for polling
- **profiler_stop** - Stop profiler recording and finalize job
- **profiler_get_job** - Poll profiler job status and get captured data

### UI Toolkit
- **uitoolkit_query** - Query VisualElements in EditorWindows with compact overview and drill-down refs
- **uitoolkit_get_styles** - Get computed USS styles for a VisualElement
- **uitoolkit_click** - Click a button, toggle, or clickable element in an EditorWindow
- **uitoolkit_get_value** - Get the current value from an input field or control
- **uitoolkit_set_value** - Set the value of an input field or control
- **uitoolkit_navigate** - Expand/collapse foldouts or select tabs in an EditorWindow

### Batch Operations
- **batch_execute** - Execute multiple MCP commands in a single operation with fail-fast support

### Debug & Testing
- **test_echo** - Echo back input message (connectivity test)
- **test_add** - Add two numbers (parameter handling test)
- **test_unity_info** - Get basic Unity editor information
- **test_list_scenes** - List all scenes in build settings

## Available MCP Resources

Resources provide read-only access to Unity Editor state via URI patterns:

### Scene Resources
- **scene://gameobject/{id}** - GameObject details by instance ID
- **scene://gameobject/{id}/components** - List components on a GameObject
- **scene://gameobject/{id}/component/{type}** - Specific component details
- **scene://loaded** - List of all loaded scenes

### Editor Resources
- **editor://state** - Current editor state (play mode, compiling, focus, etc.)
- **editor://selection** - Currently selected objects
- **editor://windows** - List of open EditorWindows
- **editor://prefab_stage** - Current prefab editing stage information
- **editor://active_tool** - Currently active editor tool (Move, Rotate, Scale, etc.)

### Project Resources
- **project://info** - Project path, name, and Unity version
- **project://tags** - Project tags
- **project://layers** - Project layers and their indices
- **project://player_settings** - Comprehensive player settings including icons, resolution, and platform settings
- **project://quality** - Quality settings including all quality levels and their configurations
- **project://physics** - Physics settings including gravity, solver iterations, and layer collision matrix
- **project://audio** - Audio settings including speaker mode, DSP buffer, and sample rate
- **project://input** - Input system actions, bindings, or legacy input axes
- **project://rendering** - Rendering settings including render pipeline, ambient lighting, and fog

### Build & Package Resources
- **build://settings** - Build target, scenes, and configuration
- **packages://installed** - List of installed packages and their versions

### Console & Tests
- **console://summary** - Quick error/warning/info counts from the console
- **console://errors** - Detailed compilation/runtime errors with file paths and line numbers
- **tests://list** - Available unit tests
- **tests://list/{mode}** - Tests filtered by mode (EditMode or PlayMode)
- **profiler://state** - Profiler recording status and configuration

### Animation & Menu
- **animation://controller/{path}** - AnimatorController details including layers, parameters, and state machines
- **menu://items** - Available Unity Editor menu items

### Asset Resources
- **assets://dependencies/{path}** - Asset dependencies - what an asset uses and what uses it

## Adding Custom AI Tools

Create a static method and mark it with `[MCPTool]`:

```csharp
using UnityMCP.Editor;
using UnityEngine;

public static class MyCustomTools
{
    [MCPTool("hello_world", "Says hello to the specified name")]
    public static string SayHello(
        [MCPParam("name", "Name to greet", required: true)] string name)
    {
        return $"Hello, {name}!";
    }

    [MCPTool("create_cube_at", "Creates a cube at specified position")]
    public static object CreateCube(
        [MCPParam("x", "X coordinate", required: true)] float x,
        [MCPParam("y", "Y coordinate", required: true)] float y,
        [MCPParam("z", "Z coordinate", required: true)] float z)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.position = new Vector3(x, y, z);
        cube.name = "Custom Cube";

        return new
        {
            success = true,
            message = $"Created cube at ({x}, {y}, {z})",
            instanceID = cube.GetInstanceID()
        };
    }
}
```

Tools are automatically discovered on domain reload. No registration needed.

## Adding Custom Resources

Resources expose read-only data to AI assistants. Use `[MCPResource]`:

```csharp
using UnityMCP.Editor;
using UnityEngine;

public static class MyCustomResources
{
    [MCPResource("unity://player/stats", "Current player statistics")]
    public static object GetPlayerStats()
    {
        var player = GameObject.Find("Player");
        if (player == null)
            return new { error = "Player not found" };

        return new
        {
            position = player.transform.position,
            health = player.GetComponent<Health>()?.CurrentHealth ?? 0,
            isGrounded = player.GetComponent<CharacterController>()?.isGrounded ?? false
        };
    }
}
```

Resources use URI patterns (e.g., `unity://player/stats`) and are read via `resources/read`.

## License

GPLv3 - see LICENSE file for details.
