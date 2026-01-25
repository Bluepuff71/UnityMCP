# Tool Categories Design

## Overview

Add category support to MCP tools for better organization in both the Unity Editor window and MCP protocol responses.

## Goals

1. Organize tools into collapsible foldouts in the MCPServerWindow
2. Expose categories in the MCP `tools/list` response for AI client discoverability

## Design

### 1. MCPToolAttribute Changes

Add optional `Category` property:

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public class MCPToolAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }
    public string Category { get; set; } = "Uncategorized";

    public MCPToolAttribute(string name, string description)
    {
        Name = name;
        Description = description;
    }
}
```

Usage:
```csharp
[MCPTool("scene_load", "Loads a scene", Category = "Scene")]
public static object Load(...) { }
```

### 2. ToolDefinition Changes

Add `category` field to protocol response:

```csharp
public class ToolDefinition
{
    public string name;
    public string description;
    public string category;  // NEW
    public InputSchema inputSchema;
}
```

### 3. ToolRegistry Changes

- Expose `Category` property on `ToolInfo`
- Add `GetDefinitionsByCategory()` method for UI grouping
- Add `GetCategoryOrder()` for consistent sort order
- Update `ToDefinition()` to include category

### 4. MCPServerWindow Changes

- Replace flat tool list with collapsible foldouts per category
- Track foldout state in `Dictionary<string, bool>`
- Show tool count per category: "Scene (6)"
- Categories default to expanded

### 5. Category Assignments

| Category | Tools |
|----------|-------|
| Scene | scene_create, scene_load, scene_save, scene_get_active, scene_get_hierarchy, scene_screenshot |
| GameObject | gameobject_find, gameobject_manage |
| Component | component_manage |
| Asset | asset_manage, prefab_manage |
| Console | console_read |
| Tests | tests_run, tests_get_job |
| Editor | unity_refresh |
| Debug | test_echo, test_add, test_unity_info, test_list_scenes |

### 6. Sort Order

Categories appear in this order:
1. Scene
2. GameObject
3. Component
4. Asset
5. Console
6. Tests
7. Editor
8. Debug
9. Uncategorized (tools without explicit category)

## Files to Modify

1. `Editor/Attributes/MCPToolAttribute.cs` - Add Category property
2. `Editor/Core/MCPProtocol.cs` - Add category to ToolDefinition
3. `Editor/Core/ToolRegistry.cs` - Expose category, add grouping method
4. `Editor/UI/MCPServerWindow.cs` - Collapsible foldouts UI
5. `Editor/Tools/*.cs` - Add Category to all existing tools

## API Response Example

```json
{
  "tools": [
    {
      "name": "scene_load",
      "description": "Loads a scene by path or build index",
      "category": "Scene",
      "inputSchema": { ... }
    },
    {
      "name": "gameobject_find",
      "description": "Finds GameObjects by name, tag, layer...",
      "category": "GameObject",
      "inputSchema": { ... }
    }
  ]
}
```

## Notes

- The MCP spec doesn't define `category`, but extra fields are allowed
- AI clients can use categories to group tools when presenting options
- Non-breaking additive change to the protocol
