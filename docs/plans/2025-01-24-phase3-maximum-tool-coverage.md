# Phase 3: Maximum Tool Coverage

## Overview

Achieve full Coplay parity plus new tools that fill gaps. This phase completes all tooling and functionality before Phase 4 (production polish).

## Scope

- **27 tools** (9 ported from Coplay + 18 new)
- **14 resources** (all from Coplay)
- **Research task** for resource gaps + implementation

## Tools

### Part A: Coplay Tool Parity (9 tools)

| Tool | Category | Description |
|------|----------|-------------|
| `batch_execute` | Editor | Execute multiple MCP commands atomically |
| `execute_menu_item` | Editor | Trigger Unity menu items by path |
| `manage_editor` | Editor | Editor preferences, layout, misc state |
| `manage_material` | Asset | Material property operations |
| `manage_script` | Asset | Script file creation/modification |
| `manage_scriptable_object` | Asset | ScriptableObject CRUD |
| `manage_shader` | Asset | Shader inspection/operations |
| `manage_texture` | Asset | Texture import settings and properties |
| `manage_vfx` | VFX | Particles, lines, trails (full port) |

### Part B: New Tools (18 tools)

**PlayMode (Category: Editor)**
| Tool | Description |
|------|-------------|
| `playmode_enter` | Enter play mode |
| `playmode_exit` | Exit play mode |
| `playmode_pause` | Toggle or set pause state |
| `playmode_step` | Advance single frame |

**Profiler (Category: Profiler) - Async Job Pattern**
| Tool | Description |
|------|-------------|
| `profiler_start` | Start recording, return job_id |
| `profiler_stop` | Stop recording, finalize job |
| `profiler_get_job` | Poll job status, get captured data |

**Selection (Category: Editor)**
| Tool | Description |
|------|-------------|
| `selection_get` | Get currently selected objects |
| `selection_set` | Set selection by instance IDs or paths |

**UIToolkit (Category: UIToolkit)**
| Tool | Description |
|------|-------------|
| `uitoolkit_query` | Query VisualElements by selector/name/type |
| `uitoolkit_get_styles` | Get computed USS styles for element |

**Build (Category: Build) - Async Job Pattern**
| Tool | Description |
|------|-------------|
| `build_start` | Start build, return job_id |
| `build_get_job` | Poll build status, get result |

## Resources

### Coplay Resource Parity (14 resources)

**Editor Resources (5)**
| URI | Description |
|-----|-------------|
| `editor://active_tool` | Currently active editor tool |
| `editor://state` | Editor state snapshot (play mode, compiling, etc.) |
| `editor://prefab_stage` | Current prefab editing stage info |
| `editor://selection` | Currently selected objects |
| `editor://windows` | Open editor windows |

**Project Resources (3)**
| URI | Description |
|-----|-------------|
| `project://info` | Project path, name, Unity version |
| `project://layers` | Project layers and indices |
| `project://tags` | Project tags |

**Scene Resources (3)**
| URI | Description |
|-----|-------------|
| `scene://gameobject/{id}` | GameObject details by instance ID |
| `scene://gameobject/{id}/components` | Components on a GameObject |
| `scene://gameobject/{id}/component/{type}` | Specific component details |

**Menu Resources (1)**
| URI | Description |
|-----|-------------|
| `menu://items` | Available menu items |

**Test Resources (2)**
| URI | Description |
|-----|-------------|
| `tests://list` | Available unit tests |
| `tests://list/{mode}` | Tests filtered by mode (EditMode/PlayMode) |

### Resource Gap Research

As part of Phase 3, research valuable resource gaps:
- `console://summary` - Quick error/warning counts
- `build://settings` - Build configuration
- `scene://loaded` - All loaded scenes
- `profiler://state` - Profiler recording status
- `packages://installed` - Installed packages

Implement approved resources after research.

## Design Decisions

### Hybrid Tool Pattern

- **Bundled** (`manage_*`) for CRUD-like operations with shared parameters
- **Split** (`playmode_*`) for distinct commands without shared parameters

### Async Job Pattern

Three tool categories use async jobs:
- Tests (already implemented via `TestJobManager`)
- Profiler (new `ProfilerJobManager`)
- Build (new `BuildJobManager`)

Each has its own manager - keeps domains isolated. Refactor to shared infrastructure in Phase 4 if needed.

### Resource URI Templates

Parameterized resources use URI templates matching Coplay:
```csharp
[MCPResource("scene://gameobject/{id}", "GameObject by instance ID")]
public static object GetGameObject(
    [MCPParam("id", "Instance ID")] int id)
```

ResourceRegistry parses URI, extracts parameters, invokes method.

## VFX Tool Structure

**Actions:**
- Particles: `particle_play`, `particle_pause`, `particle_stop`, `particle_restart`, `particle_get`, `particle_set`
- Lines: `line_create`, `line_get`, `line_set`
- Trails: `trail_get`, `trail_set`, `trail_clear`

**File Structure:**
```
Editor/Tools/
├── ManageVFX.cs           # Main dispatcher
└── VFX/
    ├── VFXCommon.cs       # Shared utilities
    ├── ParticleOps.cs     # Particle operations
    ├── LineOps.cs         # Line operations
    └── TrailOps.cs        # Trail operations
```

## Updated Categories

```csharp
"Scene" => 0,
"GameObject" => 1,
"Component" => 2,
"Asset" => 3,
"VFX" => 4,           // NEW
"Console" => 5,
"Tests" => 6,
"Profiler" => 7,      // NEW
"Build" => 8,         // NEW
"UIToolkit" => 9,     // NEW
"Editor" => 10,
"Debug" => 99,
"Uncategorized" => 100,
```

## File Organization

```
Editor/
├── Tools/
│   ├── (existing 11 files)
│   ├── BatchExecute.cs
│   ├── ExecuteMenuItem.cs
│   ├── ManageEditor.cs
│   ├── ManageMaterial.cs
│   ├── ManageScript.cs
│   ├── ManageScriptableObject.cs
│   ├── ManageShader.cs
│   ├── ManageTexture.cs
│   ├── ManageVFX.cs
│   ├── VFX/
│   │   ├── VFXCommon.cs
│   │   ├── ParticleOps.cs
│   │   ├── LineOps.cs
│   │   └── TrailOps.cs
│   ├── PlayModeTools.cs
│   ├── ProfilerTools.cs
│   ├── SelectionTools.cs
│   ├── UIToolkitTools.cs
│   └── BuildTools.cs
├── Resources/
│   ├── Editor/
│   │   ├── ActiveTool.cs
│   │   ├── EditorState.cs
│   │   ├── PrefabStage.cs
│   │   ├── Selection.cs
│   │   └── Windows.cs
│   ├── Menu/
│   │   └── MenuItems.cs
│   ├── Project/
│   │   ├── ProjectInfo.cs
│   │   ├── Layers.cs
│   │   └── Tags.cs
│   ├── Scene/
│   │   └── GameObjectResource.cs
│   └── Tests/
│       └── TestList.cs
└── Services/
    ├── TestJobManager.cs (exists)
    ├── ProfilerJobManager.cs
    └── BuildJobManager.cs
```

## Helpers

Port helpers as needed when implementing tools:
- `MaterialOps` - for manage_material
- `TextureOps` - for manage_texture
- `VectorParsing` - for multiple tools
- `PropertyConversion` - for multiple tools

Don't port speculatively.

## Task Breakdown

### Part A: Coplay Tool Parity
1. `batch_execute`
2. `execute_menu_item`
3. `manage_editor`
4. `manage_material`
5. `manage_script`
6. `manage_scriptable_object`
7. `manage_shader`
8. `manage_texture`
9. `manage_vfx` (full VFX with particles, lines, trails)

### Part B: Coplay Resource Parity
10. Editor resources (5)
11. Project resources (3)
12. Scene resources (3)
13. Menu resources (1)
14. Test resources (2)

### Part C: New Tools
15. PlayMode tools (4)
16. Profiler tools (3) + ProfilerJobManager
17. Selection tools (2)
18. UIToolkit tools (2)
19. Build tools (2) + BuildJobManager

### Part D: Resource Gap Research
20. Research valuable resource gaps
21. Implement approved new resources

## Success Criteria

- All 27 tools implemented and working
- All 14 Coplay resources implemented
- Resource gap research complete with recommendations
- New valuable resources implemented
- All tools properly categorized
- Async job pattern working for profiler and build
