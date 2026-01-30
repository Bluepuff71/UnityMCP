# Component Property Inspection & Object Reference Support

**Date:** 2026-01-30
**Version:** 1.1.0
**Status:** Design Complete

## Problem Statement

When working with components via MCP, there are two gaps:
1. No way to discover available properties on a component before attempting to set them
2. Cannot set Object/Transform reference properties (e.g., assigning a Transform to a Cinemachine Follow target)

Current behavior when attempting to set a Transform reference:
```
component_manage(action: "set_property", component_type: "Unity.Cinemachine.CinemachineCamera",
                 property: "Follow", value: -1410)
// Error: "Invalid cast from 'System.String' to 'UnityEngine.Transform'"
```

## Solution Overview

Add two capabilities to `component_manage`:

1. **`inspect` action** - List all serialized properties on a component with types and current values
2. **`$ref` syntax for object references** - Enable setting Transform/GameObject/Component/Asset references

## Feature 1: The `inspect` Action

### Parameters

```json
{
  "action": "inspect",
  "target": "-1610",           // Instance ID, name, or path
  "component_type": "Unity.Cinemachine.CinemachineCamera"
}
```

### Response Format

Hybrid hierarchical with explicit paths for actionability:

```json
{
  "success": true,
  "component": "CinemachineCamera",
  "gameObject": { "name": "MainCamera", "instanceId": -1610 },
  "properties": [
    { "path": "Priority", "type": "int", "value": 10 },
    { "path": "Lens", "type": "LensSettings", "value": {
        "FieldOfView": { "type": "float", "value": 60 },
        "NearClipPlane": { "type": "float", "value": 0.1 },
        "FarClipPlane": { "type": "float", "value": 1000 }
      }
    },
    {
      "path": "Follow",
      "type": "Transform",
      "value": { "$ref": -1408, "$name": "PlayerHead", "$path": "Player/Skeleton/Head" },
      "isObjectReference": true
    },
    { "path": "LookAt", "type": "Transform", "value": null, "isObjectReference": true }
  ]
}
```

### Design Decisions

- **Serialized fields only** - Matches Unity Inspector behavior, shows only what's practically settable
- **Hierarchical with paths** - AI callers benefit from structural understanding while having direct paths for `set_property`
- **Object references include context** - Instance ID for programmatic use, name/path for understanding

### Implementation Approach

- Use `SerializedObject` and `SerializedProperty` iteration (Unity's serialization system)
- Recursively traverse nested types to build hierarchical output
- For object references, resolve instance IDs and include name/path context

## Feature 2: Object Reference Support in `set_property`

### Syntax

```json
// Scene object by instance ID (GameObject, Transform, or Component)
{ "property": "Follow", "value": { "$ref": -1410 } }

// Specific component on a scene object
{ "property": "TargetRigidbody", "value": { "$ref": -1410, "$component": "Rigidbody" } }

// Asset by path
{ "property": "Material", "value": { "$ref": "Assets/Materials/Player.mat" } }

// Component on a prefab asset
{ "property": "Template", "value": { "$ref": "Assets/Prefabs/Enemy.prefab", "$component": "EnemyController" } }

// Array of references
{ "property": "Waypoints", "value": [
    { "$ref": -1410 },
    { "$ref": -1420 }
  ]
}

// Clear a reference
{ "property": "Follow", "value": null }
```

### Resolution Logic

In `ConvertValueToType`:

1. Detect `$ref` key in value dictionary
2. If `$ref` is integer → `EditorUtility.InstanceIDToObject()`
3. If `$ref` is string starting with "Assets/" → `AssetDatabase.LoadAssetAtPath()`
4. If `$component` present → `GetComponent()` on resolved object
5. Validate resolved object is assignable to target property type
6. Fail with detailed error if type mismatch or resolution fails

### Error Handling

Strict type matching - fail immediately with detailed error:

```json
{
  "success": false,
  "error": "Cannot assign Rigidbody (instance -1410) to property 'Follow' expecting Transform"
}
```

No auto-resolution. Callers must be explicit and correct.

### Array Support

Arrays of object references are supported. Each element in the array is resolved independently using the same `$ref` syntax.

## Implementation Tasks

### Task 1: Add `inspect` action handler

Add `HandleInspect()` method to `ManageComponents.cs`:
- Accept `target` and `component_type` parameters
- Use `SerializedObject`/`SerializedProperty` to iterate serialized fields
- Build hierarchical output with paths, types, and values
- For object references, include instance ID and context (name, path)
- Handle nested types recursively

**Files:** `Package/Editor/Tools/ManageComponents.cs`
**Estimated lines:** ~150

### Task 2: Add object reference resolution

Add `$ref` syntax support to `ConvertValueToType`:
- Add `IsObjectReference()` helper to detect `$ref` syntax
- Add `ResolveObjectReference()` helper for resolution logic
- Handle scene objects (instance ID), assets (path), and component extraction
- Support arrays of references
- Return detailed errors on resolution failure

**Files:** `Package/Editor/Tools/ManageComponents.cs`
**Estimated lines:** ~100

### Task 3: Add `SerializePropertyValue` helper

Create helper method for converting `SerializedProperty` to JSON-friendly output:
- Handle all primitive types (int, float, string, bool, enum)
- Handle Unity types (Vector2, Vector3, Color, etc.)
- Handle object references with context
- Handle nested objects recursively
- Track depth to prevent infinite recursion

**Files:** `Package/Editor/Tools/ManageComponents.cs`
**Estimated lines:** ~100

### Task 4: Version bump and CI verification

- Update `Package/package.json` version to `1.1.0`
- Verify CI workflow handles minor version bumps correctly
- If workflow auto-increments, document manual tag process for minor/major bumps

**Files:** `Package/package.json`, `.github/workflows/build-release.yml` (verify only)

## Backward Compatibility

- `inspect` is a new action - no breaking changes to existing actions
- `$ref` syntax is additive - existing value formats continue to work
- Existing `set_property` calls with primitives, vectors, colors, etc. remain unchanged

## Testing Approach

- Test `inspect` action on various component types (built-in and custom)
- Test `$ref` resolution for scene objects, assets, and components
- Test arrays of references
- Test error conditions (invalid ID, type mismatch, missing asset)
- Test nested property paths in inspect output

## Use Case

Setting up Cinemachine cameras to follow skeleton bones for first-person character controllers with IK:

```json
// 1. Inspect Cinemachine camera to discover properties
{ "action": "inspect", "target": "MainCamera", "component_type": "CinemachineCamera" }

// 2. Find the player head bone
// (using gameobject_find or scene_get_hierarchy)

// 3. Set the Follow target to the head bone's Transform
{
  "action": "set_property",
  "target": "MainCamera",
  "component_type": "CinemachineCamera",
  "property": "Follow",
  "value": { "$ref": -1410 }
}
```
