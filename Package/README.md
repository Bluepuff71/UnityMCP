# Unity MCP - AI Assistant Integration for Unity Editor

![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity)
![Unity 6 Compatible](https://img.shields.io/badge/Unity%206-Compatible-brightgreen?logo=unity)
![Release](https://img.shields.io/github/v/tag/Bluepuff71/UnityMCP?label=version)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-blue)

Unity-native [Model Context Protocol](https://modelcontextprotocol.io/) (MCP) server for AI-powered game development. Connect Claude, Codex, Cursor, and other AI assistants directly to the Unity Editor — no Node.js, Python, or external scripts required. Install the package and start automating.

## Features

- **100% Unity-native** - Runs entirely inside the Unity Editor as a single package. No Node.js, Python, or external runtimes to install or maintain.
- **Zero telemetry** - Completely private Unity automation. No data collection.
- **45 built-in tools** - Create GameObjects, run tests, build projects, manipulate scenes, and more.
- **28 built-in resources** - Read-only access to project settings, scene state, console output, and more.
- **4 workflow prompts** - Pre-built prompt templates for common Unity workflows.
- **Remote access** - Connect from other devices on your network with TLS encryption and API key authentication.
- **Activity log** - Monitor MCP requests and responses in real time from the editor window.
- **Progressive discovery** - Use `search_tools` to explore available tools by category or keyword.
- **Tool annotations** - Safety hints (readOnlyHint, destructiveHint) help AI assistants make better decisions.
- **Simple extension API** - Add custom tools, resources, and prompts with a single C# attribute.

## Requirements

- Unity 2022.3 or later (including Unity 6)
- Any MCP-compatible AI client: Claude Code, Claude Desktop, Codex, Cursor, or others

## Installation

1. Open Unity Package Manager (Window > Package Manager)
2. Click `+` > "Add package from git URL"
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

Unity MCP runs a built-in HTTP server at `http://localhost:8080/`. Any MCP-compatible client with HTTP transport support can connect directly. For stdio-only clients, use the mcp-remote bridge as shown above.

*Note: Configurations for clients other than Claude Code have not been tested. Open a PR!*

## Configuration

### Editor Window

Open the Unity MCP control panel from **Window > Unity MCP**:

<img src=".github/editor-window.png" alt="Unity MCP Editor Window" width="400">

- **Start/Stop** the MCP server
- **View registered tools** organized by category with foldout groups
- **Copy endpoint URL** to clipboard for easy client configuration
- **View activity log** showing recent MCP requests and responses

### Port

The default port is `8080`. To change it:

1. Stop the server
2. Enter a new port number in the editor window
3. Click **Apply**
4. Start the server

### Verbose Logging

Toggle **Verbose Logging** in the editor window to enable detailed debug output in the Unity Console. Useful for troubleshooting connection or tool execution issues.

### Remote Access

Enable remote access to allow AI assistants to connect to Unity MCP from other devices on your network:

- **Toggle remote access** in the editor window to enable binding to all network interfaces (0.0.0.0)
- **Requires TLS** - Unity MCP automatically generates a self-signed certificate for secure connections
- **API key authentication** - An API key (prefix `umcp_`) is auto-generated on first enable and required for all requests
- **Copy or regenerate** the API key directly from the editor window
- **Endpoint changes** to `https://<LAN_IP>:<port>/` when remote access is enabled
- **Certificate storage** - Certificate is stored in `LocalApplicationData/UnityMCP/` and auto-regenerates if your LAN IP changes
- **Firewall configuration** is the user's responsibility - you may need to allow incoming connections on the configured port

#### Claude Code Remote Setup

When remote access is enabled, configure Claude Code on another device using:

```bash
claude mcp add unity-mcp --transport http --header "Authorization: Bearer <API_KEY>" https://<LAN_IP>:8080/
```

Replace `<API_KEY>` with your generated API key and `<LAN_IP>` with your Unity machine's IP address.

## Available MCP Tools

45 built-in tools organized by category:

> **Tip:** Use `search_tools` with no arguments for a quick category overview, or pass a `query` or `category` to explore further.

<details>
<summary>View all 45 built-in tools (click to expand)</summary>

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
- **manage_scriptable_object** - Create and manage ScriptableObject assets
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
- **search_tools** - Search available tools by name, description, or category

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

### Debug & Testing
- **test_echo** - Echo back input message (connectivity test)
- **test_add** - Add two numbers (parameter handling test)
- **test_unity_info** - Get basic Unity editor information
- **test_list_scenes** - List all scenes in build settings

</details>

## Available MCP Resources

28 built-in resources provide read-only access to Unity Editor state via URI patterns:

<details>
<summary>View all 28 built-in resources (click to expand)</summary>

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

</details>

## Available MCP Prompts

4 built-in prompt templates for common Unity workflows:

<details>
<summary>View all 4 built-in prompts (click to expand)</summary>

- **read_gameobject** - Inspect a GameObject's transform, components, and optionally its children hierarchy
  - `name` (required) - Name of the GameObject to inspect
  - `include_children` - Whether to include children hierarchy (true/false)

- **inspect_prefab** - Examine a prefab asset by opening the prefab stage and reading its hierarchy
  - `path` (required) - Path to the prefab asset (e.g., Assets/Prefabs/Player.prefab)

- **modify_component** - Step-by-step guide to safely change a component property with verification
  - `target` (required) - Name or path of the target GameObject
  - `component` (required) - Component type to modify (e.g., Rigidbody, BoxCollider)
  - `property` (required) - Property to modify (e.g., mass, isTrigger)

- **setup_scene** - Set up a new scene with appropriate defaults for 3D, 2D, or UI
  - `scene_type` - Type of scene: 3d, 2d, or ui (default: 3d)

</details>

## Architecture

Unity MCP is entirely self-contained within the Unity Editor. A native C plugin runs the HTTP server on a background thread and persists across Unity domain reloads, so the AI assistant connection stays active even while Unity recompiles scripts. No external processes, runtimes, or sidecar applications are needed.

```
┌─────────────────┐
│  MCP Client     │
│  (Claude, etc.) │
└────────┬────────┘
         │ HTTP(S) POST (JSON-RPC)
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

## Extending Unity MCP

### Adding Custom Tools

<details>
<summary>Click to expand</summary>

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

    [MCPTool("spawn_enemy", "Spawn an enemy at a position with difficulty scaling",
        Category = "Gameplay", DestructiveHint = true)]
    public static object SpawnEnemy(
        [MCPParam("enemy_type", "Type of enemy to spawn", required: true,
            Enum = new[] { "goblin", "skeleton", "dragon" })] string enemyType,
        [MCPParam("x", "X position", required: true)] float x,
        [MCPParam("y", "Y position", required: true)] float y,
        [MCPParam("z", "Z position", required: true)] float z,
        [MCPParam("difficulty", "Difficulty multiplier (1-10)",
            Minimum = 1, Maximum = 10)] float difficulty = 5)
    {
        GameObject enemy = new GameObject($"Enemy_{enemyType}");
        enemy.transform.position = new Vector3(x, y, z);

        return new
        {
            instanceID = enemy.GetInstanceID(),
            type = enemyType,
            difficulty
        };
    }
}
```

Tools are automatically discovered on domain reload. No registration needed.

#### Tool Annotations

Annotations provide hints to AI assistants about tool behavior:

| Property | Type | Default | Description |
|---|---|---|---|
| `Category` | string | `"Uncategorized"` | Groups related tools in `search_tools` results |
| `ReadOnlyHint` | bool | `false` | Tool does not modify any state |
| `DestructiveHint` | bool | `false` | Tool may perform irreversible operations |
| `IdempotentHint` | bool | `false` | Same arguments always yield the same result |
| `OpenWorldHint` | bool | `false` | Tool interacts with systems beyond Unity |
| `Title` | string | `null` | Human-readable display title |

#### Parameter Constraints

Constraints are included in the JSON Schema sent to AI assistants:

| Property | Type | Description |
|---|---|---|
| `Enum` | string[] | Valid values for string parameters |
| `Minimum` | double | Minimum value for numeric parameters |
| `Maximum` | double | Maximum value for numeric parameters |

</details>

### Adding Custom Resources

<details>
<summary>Click to expand</summary>

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

</details>

### Adding Custom Prompts

<details>
<summary>Click to expand</summary>

Prompts provide reusable workflow templates for AI assistants. Use `[MCPPrompt]`:

```csharp
using System.Collections.Generic;
using UnityMCP.Editor;
using UnityMCP.Editor.Core;

public static class MyCustomPrompts
{
    [MCPPrompt("debug_gameobject", "Debug a GameObject by inspecting its state")]
    public static PromptResult DebugGameObject(
        [MCPParam("name", "Name of the GameObject to debug", required: true)] string name,
        [MCPParam("verbose", "Include full component details (true/false)")] string verbose = "false")
    {
        bool isVerbose = verbose?.ToLower() == "true";

        string instructions = $@"Debug the GameObject ""{name}"" using these steps:

1. Use `gameobject_find` with search_term=""{name}"" to locate it
2. Use `component_manage` with action=""inspect"" to check each component";

        if (isVerbose)
        {
            instructions += $@"
3. Use `console_read` with filter=""{name}"" to check for related log messages";
        }

        return new PromptResult
        {
            description = $"Debug instructions for '{name}'",
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
```

Prompts are automatically discovered on domain reload, just like tools and resources.

</details>

## License

GPLv3 - see LICENSE file for details.
