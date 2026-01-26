# Unity MCP - AI Assistant Integration for Unity Editor

![Unity 6+](https://img.shields.io/badge/Unity-6%2B-black?logo=unity)
![License](https://img.shields.io/github/license/Bluepuff71/UnityMCP)
![Release](https://img.shields.io/github/v/tag/Bluepuff71/UnityMCP?label=version)
![Build](https://img.shields.io/github/actions/workflow/status/Bluepuff71/UnityMCP/build-release.yml?branch=main)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-blue)

Model Context Protocol (MCP) server that enables AI assistants like Claude, Codex, and Cursor to automate Unity Editor tasks and game development workflows.

## Features

- **Zero telemetry** - Completely private Unity automation. No data collection.
- **40+ built-in tools** - Create GameObjects, run tests, build projects, manipulate scenes through AI.
- **Simple extension API** - Add custom AI tools with a single attribute.

## Requirements

- Unity 6 or later
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

## How Unity Editor Automation Works

Unity MCP uses a native C plugin to maintain an HTTP server on a background thread, independent of Unity's C# scripting domain. This architecture ensures the AI assistant connection remains active during script recompilation.

```
┌─────────────────┐
│  MCP Client     │
│  (Claude, etc.) │
└────────┬────────┘
         │ HTTP
         ▼
┌─────────────────────────────────────┐
│  Native Plugin (C)                  │
│  - HTTP server on background thread │
│  - Survives domain reloads          │
└────────┬────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────┐
│  Unity C# (main thread)             │
│  - Executes tools when available    │
│  - Returns "recompiling" during     │
│    domain reload                    │
└─────────────────────────────────────┘
```

When Unity recompiles scripts, the C# domain unloads temporarily. During this time, the native plugin continues accepting HTTP requests but returns a "Unity is recompiling" error instead of disconnecting. Once recompilation completes, requests are forwarded to Unity's main thread for execution.

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
