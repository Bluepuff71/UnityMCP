# Unity MCP - Design Document

Pure C# MCP server for Unity 6 with attribute-based tool extensions.

## Overview

A fork/rewrite of [Coplay's unity-mcp](https://github.com/CoplayDev/unity-mcp) that:
- Removes Python dependency entirely
- Native Unity 6 support
- Attribute-based extension API for custom tools
- Fills gaps: profiler, play mode control, UI Toolkit inspection

## Architecture

```
┌─────────────────────────────────────────────────────┐
│  External Packages (UICore, etc.)                   │
│  [MCPTool] attributed methods                       │
└─────────────────────┬───────────────────────────────┘
                      │ registers tools
                      ▼
┌─────────────────────────────────────────────────────┐
│  UnityMCP Package                                   │
│  ┌───────────────────────────────────────────────┐  │
│  │  ToolRegistry (discovers [MCPTool] methods)   │  │
│  └───────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────┐  │
│  │  MCPServer (HTTP + JSON-RPC)                  │  │
│  │  localhost:8080/mcp                           │  │
│  └───────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────┐  │
│  │  Built-in Tools                               │  │
│  │  - Scene/GameObject                           │  │
│  │  - Console                                    │  │
│  │  - Play Mode                                  │  │
│  │  - Profiler                                   │  │
│  └───────────────────────────────────────────────┘  │
└─────────────────────┬───────────────────────────────┘
                      │ HTTP
                      ▼
               Claude Code

```

## Package Structure

```
D:\Unity Packages\UnityMCP\
├── package.json
├── README.md
├── CHANGELOG.md
├── LICENSE                           # MIT
├── docs/
│   └── DESIGN.md                     # This file
├── Runtime/
│   └── UnityMCP.Runtime.asmdef       # Minimal - MCP is editor-only
├── Editor/
│   ├── UnityMCP.Editor.asmdef
│   ├── Core/
│   │   ├── MCPServer.cs              # HTTP server + MCP protocol
│   │   ├── MCPProtocol.cs            # JSON-RPC message types
│   │   ├── ToolRegistry.cs           # Discovers and invokes tools
│   │   └── ResourceRegistry.cs       # Discovers and serves resources
│   ├── Attributes/
│   │   ├── MCPToolAttribute.cs       # [MCPTool("name", "description")]
│   │   ├── MCPResourceAttribute.cs   # [MCPResource("uri", "description")]
│   │   └── MCPParamAttribute.cs      # [MCPParam("name", required: true)]
│   ├── Tools/                        # Built-in tools
│   │   ├── SceneTools.cs
│   │   ├── GameObjectTools.cs
│   │   ├── ComponentTools.cs
│   │   ├── AssetTools.cs
│   │   ├── ConsoleTools.cs
│   │   ├── PlayModeTools.cs
│   │   ├── ProfilerTools.cs
│   │   ├── SelectionTools.cs
│   │   ├── TestTools.cs
│   │   └── UIToolkitTools.cs
│   ├── Resources/                    # Built-in resources
│   │   ├── HierarchyResource.cs
│   │   ├── SelectionResource.cs
│   │   ├── ProjectInfoResource.cs
│   │   └── ConsoleResource.cs
│   └── UI/
│       └── MCPServerWindow.cs        # Editor window
```

## Core Components

### MCPServer.cs

HTTP server using `System.Net.HttpListener`. Handles MCP protocol methods:

- `initialize` - Handshake with client
- `tools/list` - Return registered tools
- `tools/call` - Invoke a tool by name
- `resources/list` - Return registered resources
- `resources/read` - Read a resource by URI

```csharp
public class MCPServer
{
    private HttpListener _listener;
    private const int Port = 8080;

    public bool IsRunning => _listener?.IsListening ?? false;

    public void Start()
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();
        ListenAsync();
    }

    public void Stop()
    {
        _listener?.Stop();
        _listener?.Close();
    }

    private async void ListenAsync()
    {
        while (_listener.IsListening)
        {
            var context = await _listener.GetContextAsync();
            _ = HandleRequestAsync(context);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = await ReadRequest(context);

        var response = request.method switch
        {
            "initialize" => HandleInitialize(request),
            "tools/list" => HandleToolsList(request),
            "tools/call" => HandleToolsCall(request),
            "resources/list" => HandleResourcesList(request),
            "resources/read" => HandleResourcesRead(request),
            _ => CreateError(-32601, "Method not found")
        };

        await WriteResponse(context, response);
    }
}
```

### MCPProtocol.cs

JSON-RPC 2.0 message types:

```csharp
[Serializable]
public class MCPRequest
{
    public string jsonrpc = "2.0";
    public string method;
    public JsonElement @params;
    public string id;
}

[Serializable]
public class MCPResponse
{
    public string jsonrpc = "2.0";
    public object result;
    public MCPError error;
    public string id;
}

[Serializable]
public class MCPError
{
    public int code;
    public string message;
    public object data;
}

[Serializable]
public class ToolDefinition
{
    public string name;
    public string description;
    public JsonSchema inputSchema;
}
```

### Attribute System

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class MCPToolAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }

    public MCPToolAttribute(string name, string description)
    {
        Name = name;
        Description = description;
    }
}

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public class MCPParamAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }
    public bool Required { get; }

    public MCPParamAttribute(string name, string description = null, bool required = false)
    {
        Name = name;
        Description = description;
        Required = required;
    }
}
```

### ToolRegistry.cs

Discovers tools at editor startup:

```csharp
public static class ToolRegistry
{
    private static Dictionary<string, ToolInfo> _tools = new();

    public static int Count => _tools.Count;

    [InitializeOnLoadMethod]
    private static void DiscoverTools()
    {
        _tools.Clear();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            // Skip system assemblies for performance
            if (assembly.FullName.StartsWith("System") ||
                assembly.FullName.StartsWith("Unity."))
                continue;

            foreach (var type in assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public))
                {
                    var attr = method.GetCustomAttribute<MCPToolAttribute>();
                    if (attr != null)
                    {
                        _tools[attr.Name] = new ToolInfo(attr, method);
                    }
                }
            }
        }
    }

    public static IEnumerable<ToolDefinition> GetDefinitions()
    {
        return _tools.Values.Select(t => t.ToDefinition());
    }

    public static object Invoke(string name, Dictionary<string, JsonElement> args)
    {
        if (!_tools.TryGetValue(name, out var tool))
            throw new MCPException(-32602, $"Unknown tool: {name}");

        return tool.Invoke(args);
    }
}
```

## Built-in Tools

### Complete Parity with Coplay (19 tools)

All tools from Coplay's MCP will be ported with identical functionality:

| Tool | Description | Actions |
|------|-------------|---------|
| `batch_execute` | Execute multiple operations atomically | batch operations |
| `execute_menu_item` | Trigger any Unity menu item | execute |
| `find_gameobjects` | Search for GameObjects | by_name, by_tag, by_component, by_path |
| `get_test_job` | Get test job status | get status |
| `manage_asset` | Asset operations | find, import, create, delete, move, copy |
| `manage_components` | Component operations | add, remove, set_property |
| `manage_editor` | Editor state control | get_state, set_state |
| `manage_gameobject` | GameObject operations | create, modify, delete, duplicate, move_relative |
| `manage_material` | Material operations | create, modify, get, list |
| `manage_prefabs` | Prefab operations | instantiate, create, apply, unpack |
| `manage_scene` | Scene operations | create, load, save, get_hierarchy, get_active, screenshot |
| `manage_script` | Script operations | create, modify, validate, delete |
| `manage_scriptable_object` | ScriptableObject operations | create, modify, get, list |
| `manage_shader` | Shader operations | get, list, create |
| `manage_texture` | Texture operations | get, modify, import |
| `manage_vfx` | VFX operations | particle, line, trail control |
| `read_console` | Read console logs | get logs with filtering |
| `refresh_unity` | Refresh asset database | refresh, reimport |
| `run_tests` | Run Unity tests | run, list |

### New Tools (Gaps Filled)

| Tool | Description |
|------|-------------|
| `playmode_enter` | Enter play mode |
| `playmode_exit` | Exit play mode |
| `playmode_pause` | Pause/unpause |
| `playmode_step` | Advance single frame |
| `profiler_capture` | Get CPU/memory snapshot |
| `profiler_get_frame` | Detailed frame breakdown |
| `profiler_start` | Start recording |
| `profiler_stop` | Stop recording |
| `selection_get` | Currently selected objects |
| `selection_set` | Select objects programmatically |
| `uitoolkit_query` | Query VisualElement tree |
| `uitoolkit_get_styles` | Get computed USS styles |
| `build_check` | Verify compilation status |
| `build_trigger` | Trigger a build |

## Built-in Resources

### Complete Parity with Coplay (14 resources)

| Resource | Description |
|----------|-------------|
| `get_windows` | Open editor windows |
| `get_tests` | Test list |
| `get_tests_for_mode` | Tests by mode (Edit/Play) |
| `get_selection` | Current selection |
| `get_active_tool` | Active editor tool |
| `get_editor_state` | Editor state (play mode, etc.) |
| `get_prefab_stage` | Prefab editing stage info |
| `get_gameobject` | GameObject details by ID |
| `get_gameobject_components` | Components on a GameObject |
| `get_gameobject_component` | Specific component details |
| `get_menu_items` | Available menu items |
| `get_project_info` | Project information |
| `get_tags` | Project tags |
| `get_layers` | Project layers |

## Extension Example

How UICore would register tools:

```csharp
// In UICore: Editor/MCP/UICoreMCPTools.cs
using UnityMCP;

public static class UICoreMCPTools
{
    [MCPTool("uicore_get_widget_hierarchy", "Returns the widget tree managed by UIComposer")]
    public static object GetWidgetHierarchy(
        [MCPParam("composer_name", "Name of UIComposer GameObject")] string composerName = null)
    {
        var composer = composerName != null
            ? GameObject.Find(composerName)?.GetComponent<UIComposer>()
            : Object.FindFirstObjectByType<UIComposer>();

        if (composer == null)
            return new { error = "No UIComposer found" };

        var doc = composer.GetComponent<UIDocument>();
        return SerializeVisualElement(doc.rootVisualElement);
    }

    [MCPTool("uicore_get_dialogue_state", "Returns current Yarn Spinner dialogue state")]
    public static object GetDialogueState()
    {
        var presenter = Object.FindFirstObjectByType<LinePresenter>();
        if (presenter == null)
            return new { active = false };

        return new {
            active = true,
            currentLine = presenter.CurrentLine,
            isTypewriting = presenter.IsTypewriting
        };
    }

    [MCPTool("uicore_trigger_fade", "Triggers fade on a widget")]
    public static object TriggerFade(
        [MCPParam("widget_id", required: true)] string widgetId,
        [MCPParam("fade_in", required: true)] bool fadeIn,
        [MCPParam("duration")] float duration = 0.3f)
    {
        // Implementation
    }
}
```

UICore adds reference to `UnityMCP.Editor.asmdef` - tools discovered automatically.

## Editor Window

```csharp
public class MCPServerWindow : EditorWindow
{
    [MenuItem("Window/Unity MCP")]
    public static void ShowWindow() => GetWindow<MCPServerWindow>("Unity MCP");

    private void OnGUI()
    {
        // Status
        bool isRunning = MCPServer.Instance?.IsRunning ?? false;

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUI.color = isRunning ? Color.green : Color.gray;
        GUILayout.Label(isRunning ? "● Running" : "○ Stopped", EditorStyles.boldLabel);
        GUI.color = Color.white;
        GUILayout.FlexibleSpace();

        if (GUILayout.Button(isRunning ? "Stop" : "Start", EditorStyles.toolbarButton))
            if (isRunning) MCPServer.Instance.Stop();
            else MCPServer.Instance.Start();
        EditorGUILayout.EndHorizontal();

        // Info
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Endpoint", "http://localhost:8080/mcp");
        EditorGUILayout.LabelField("Tools", ToolRegistry.Count.ToString());
        EditorGUILayout.LabelField("Resources", ResourceRegistry.Count.ToString());

        // Tool list
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Registered Tools", EditorStyles.boldLabel);
        foreach (var tool in ToolRegistry.GetDefinitions())
        {
            EditorGUILayout.LabelField($"  {tool.name}", tool.description);
        }
    }
}
```

## Implementation Phases

### Phase 1: Foundation
1. Create package structure
2. Implement MCPServer (HTTP + JSON-RPC)
3. Implement MCPProtocol (message types)
4. Implement attribute system
5. Implement ToolRegistry (discovery)
6. Create MCPServerWindow (start/stop)
7. Add one test tool to verify end-to-end

### Phase 2: Port Coplay's Tools
1. Port scene/GameObject tools
2. Port console tools
3. Port asset/prefab tools
4. Port test runner tools

### Phase 3: New Tools
1. Play mode control
2. Profiler capture
3. UI Toolkit query
4. Compilation status

### Phase 4: UICore Integration
1. Add UnityMCP.Editor reference to UICore
2. Create UICoreMCPTools.cs
3. Test full workflow

## What We're NOT Including

- Python server (replaced with pure C#)
- Telemetry
- Backwards compatibility for pre-Unity 6
- Complex configuration UI
- Multiple transport modes (HTTP only)

## Claude Code Configuration

```bash
claude mcp add --transport http --scope user unity-mcp http://localhost:8080/mcp
```

## License

MIT (same as Coplay's original)
