using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityMCP.Editor;
using UnityMCP.Editor.Core;
using UnityMCP.Editor.Utilities;

namespace UnityMCP.Editor.Tools
{
    /// <summary>
    /// Handles asset management operations including create, delete, move, duplicate, import, and search.
    /// </summary>
    public static class ManageAsset
    {
        private const int DefaultPageSize = 50;
        private const int MaxPageSize = 500;

        #region Main Tool Entry Point

        /// <summary>
        /// Manages assets in the Unity project with various operations.
        /// </summary>
        [MCPTool("asset_manage", "Manages assets: create, delete, move, rename, duplicate, import, search, get_info, create_folder", Category = "Asset")]
        public static object Manage(
            [MCPParam("action", "Action to perform: create, delete, move, rename, duplicate, import, search, get_info, create_folder", required: true)] string action,
            [MCPParam("path", "Asset path (e.g., 'Assets/Materials/New.mat')")] string path = null,
            [MCPParam("destination", "Destination path for move/duplicate operations")] string destination = null,
            [MCPParam("asset_type", "Asset type for create: folder, material, physicsmaterial")] string assetType = null,
            [MCPParam("properties", "Properties for create/modify operations (e.g., shader, friction, bounciness)")] Dictionary<string, object> properties = null,
            [MCPParam("search_pattern", "Search pattern for search operation")] string searchPattern = null,
            [MCPParam("filter_type", "Filter by asset type for search (e.g., 'Material', 'Prefab', 'Texture2D')")] string filterType = null,
            [MCPParam("page_size", "Number of results per page for search (default: 50, max: 500)")] int pageSize = DefaultPageSize,
            [MCPParam("page_number", "Page number for search results (1-based, default: 1)")] int pageNumber = 1)
        {
            if (string.IsNullOrEmpty(action))
            {
                throw MCPException.InvalidParams("Action parameter is required.");
            }

            string normalizedAction = action.ToLowerInvariant().Trim();

            try
            {
                return normalizedAction switch
                {
                    "create" => HandleCreate(path, assetType, properties),
                    "create_folder" => HandleCreateFolder(path),
                    "delete" => HandleDelete(path),
                    "move" => HandleMove(path, destination),
                    "rename" => HandleRename(path, destination),
                    "duplicate" => HandleDuplicate(path, destination),
                    "import" => HandleImport(path),
                    "search" => HandleSearch(searchPattern, filterType, pageSize, pageNumber),
                    "get_info" => HandleGetInfo(path),
                    _ => throw MCPException.InvalidParams($"Unknown action: '{action}'. Valid actions: create, delete, move, rename, duplicate, import, search, get_info, create_folder")
                };
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error executing action '{action}': {exception.Message}"
                };
            }
        }

        #endregion

        #region Action Handlers

        /// <summary>
        /// Handles the create action - creates a new asset.
        /// </summary>
        private static object HandleCreate(string path, string assetType, Dictionary<string, object> properties)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw MCPException.InvalidParams("'path' parameter is required for create action.");
            }

            if (string.IsNullOrEmpty(assetType))
            {
                throw MCPException.InvalidParams("'asset_type' parameter is required for create action.");
            }

            string normalizedPath = PathUtilities.NormalizePath(path);
            string normalizedAssetType = assetType.ToLowerInvariant().Trim();

            // Check if asset already exists
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(normalizedPath) != null)
            {
                return new
                {
                    success = false,
                    error = $"Asset already exists at '{normalizedPath}'."
                };
            }

            // Ensure parent directory exists
            string parentDirectory = Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parentDirectory) && !AssetDatabase.IsValidFolder(parentDirectory))
            {
                if (!PathUtilities.EnsureFolderExists(parentDirectory, out string folderError))
                {
                    return new { success = false, error = folderError };
                }
            }

            try
            {
                return normalizedAssetType switch
                {
                    "folder" => CreateFolder(normalizedPath),
                    "material" or "mat" => CreateMaterial(normalizedPath, properties),
                    "physicsmaterial" or "physics_material" or "physic_material" => CreatePhysicsMaterial(normalizedPath, properties),
                    _ => throw MCPException.InvalidParams($"Unsupported asset type: '{assetType}'. Supported types: folder, material, physicsmaterial")
                };
            }
            catch (MCPException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error creating asset: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Handles the create_folder action - creates a new folder.
        /// </summary>
        private static object HandleCreateFolder(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw MCPException.InvalidParams("'path' parameter is required for create_folder action.");
            }

            string normalizedPath = PathUtilities.NormalizePath(path);
            return CreateFolderRecursive(normalizedPath);
        }

        /// <summary>
        /// Handles the delete action - deletes an asset.
        /// </summary>
        private static object HandleDelete(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw MCPException.InvalidParams("'path' parameter is required for delete action.");
            }

            string normalizedPath = PathUtilities.NormalizePath(path);

            if (!AssetExists(normalizedPath))
            {
                return new
                {
                    success = false,
                    error = $"Asset not found at '{normalizedPath}'."
                };
            }

            string guid = AssetDatabase.AssetPathToGUID(normalizedPath);
            string assetName = Path.GetFileName(normalizedPath);

            try
            {
                bool deleted = AssetDatabase.DeleteAsset(normalizedPath);

                if (deleted)
                {
                    return new
                    {
                        success = true,
                        message = $"Asset '{assetName}' deleted successfully.",
                        deletedPath = normalizedPath,
                        deletedGuid = guid
                    };
                }
                else
                {
                    return new
                    {
                        success = false,
                        error = $"Failed to delete asset at '{normalizedPath}'."
                    };
                }
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error deleting asset: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Handles the move action - moves an asset to a new location.
        /// </summary>
        private static object HandleMove(string path, string destination)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw MCPException.InvalidParams("'path' parameter is required for move action.");
            }

            if (string.IsNullOrEmpty(destination))
            {
                throw MCPException.InvalidParams("'destination' parameter is required for move action.");
            }

            string normalizedPath = PathUtilities.NormalizePath(path);
            string normalizedDestination = PathUtilities.NormalizePath(destination);

            if (!AssetExists(normalizedPath))
            {
                return new
                {
                    success = false,
                    error = $"Source asset not found at '{normalizedPath}'."
                };
            }

            // If destination is a folder, move asset into that folder
            if (AssetDatabase.IsValidFolder(normalizedDestination))
            {
                string fileName = Path.GetFileName(normalizedPath);
                normalizedDestination = Path.Combine(normalizedDestination, fileName).Replace('\\', '/');
            }

            // Ensure parent directory exists
            string parentDirectory = Path.GetDirectoryName(normalizedDestination)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parentDirectory) && !AssetDatabase.IsValidFolder(parentDirectory))
            {
                if (!PathUtilities.EnsureFolderExists(parentDirectory, out string folderError))
                {
                    return new { success = false, error = folderError };
                }
            }

            try
            {
                string moveResult = AssetDatabase.MoveAsset(normalizedPath, normalizedDestination);

                if (string.IsNullOrEmpty(moveResult))
                {
                    return new
                    {
                        success = true,
                        message = $"Asset moved successfully.",
                        originalPath = normalizedPath,
                        newPath = normalizedDestination,
                        asset = BuildAssetInfo(normalizedDestination)
                    };
                }
                else
                {
                    return new
                    {
                        success = false,
                        error = $"Failed to move asset: {moveResult}"
                    };
                }
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error moving asset: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Handles the rename action - renames an asset.
        /// </summary>
        private static object HandleRename(string path, string destination)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw MCPException.InvalidParams("'path' parameter is required for rename action.");
            }

            if (string.IsNullOrEmpty(destination))
            {
                throw MCPException.InvalidParams("'destination' parameter is required for rename action. Provide the new name or full path.");
            }

            string normalizedPath = PathUtilities.NormalizePath(path);

            if (!AssetExists(normalizedPath))
            {
                return new
                {
                    success = false,
                    error = $"Asset not found at '{normalizedPath}'."
                };
            }

            // Determine the new name
            string newName;
            if (destination.Contains("/") || destination.Contains("\\"))
            {
                // Destination is a full path
                newName = Path.GetFileNameWithoutExtension(destination);
            }
            else
            {
                // Destination is just a name
                newName = Path.GetFileNameWithoutExtension(destination);
            }

            try
            {
                string renameResult = AssetDatabase.RenameAsset(normalizedPath, newName);

                if (string.IsNullOrEmpty(renameResult))
                {
                    // Build the new path
                    string directory = Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/');
                    string extension = Path.GetExtension(normalizedPath);
                    string newPath = string.IsNullOrEmpty(directory)
                        ? $"{newName}{extension}"
                        : $"{directory}/{newName}{extension}";

                    return new
                    {
                        success = true,
                        message = $"Asset renamed successfully.",
                        originalPath = normalizedPath,
                        newPath,
                        asset = BuildAssetInfo(newPath)
                    };
                }
                else
                {
                    return new
                    {
                        success = false,
                        error = $"Failed to rename asset: {renameResult}"
                    };
                }
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error renaming asset: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Handles the duplicate action - duplicates an asset.
        /// </summary>
        private static object HandleDuplicate(string path, string destination)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw MCPException.InvalidParams("'path' parameter is required for duplicate action.");
            }

            string normalizedPath = PathUtilities.NormalizePath(path);

            if (!AssetExists(normalizedPath))
            {
                return new
                {
                    success = false,
                    error = $"Source asset not found at '{normalizedPath}'."
                };
            }

            string normalizedDestination;

            if (string.IsNullOrEmpty(destination))
            {
                // Generate a unique name in the same directory
                string directory = Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/');
                string fileName = Path.GetFileNameWithoutExtension(normalizedPath);
                string extension = Path.GetExtension(normalizedPath);
                normalizedDestination = AssetDatabase.GenerateUniqueAssetPath(
                    string.IsNullOrEmpty(directory)
                        ? $"{fileName}_Copy{extension}"
                        : $"{directory}/{fileName}_Copy{extension}");
            }
            else
            {
                normalizedDestination = PathUtilities.NormalizePath(destination);
            }

            // Ensure parent directory exists
            string parentDirectory = Path.GetDirectoryName(normalizedDestination)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parentDirectory) && !AssetDatabase.IsValidFolder(parentDirectory))
            {
                if (!PathUtilities.EnsureFolderExists(parentDirectory, out string folderError))
                {
                    return new { success = false, error = folderError };
                }
            }

            try
            {
                bool copied = AssetDatabase.CopyAsset(normalizedPath, normalizedDestination);

                if (copied)
                {
                    return new
                    {
                        success = true,
                        message = $"Asset duplicated successfully.",
                        originalPath = normalizedPath,
                        duplicatePath = normalizedDestination,
                        asset = BuildAssetInfo(normalizedDestination)
                    };
                }
                else
                {
                    return new
                    {
                        success = false,
                        error = $"Failed to duplicate asset to '{normalizedDestination}'."
                    };
                }
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error duplicating asset: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Handles the import action - reimports an asset.
        /// </summary>
        private static object HandleImport(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw MCPException.InvalidParams("'path' parameter is required for import action.");
            }

            string normalizedPath = PathUtilities.NormalizePath(path);

            if (!AssetExists(normalizedPath))
            {
                return new
                {
                    success = false,
                    error = $"Asset not found at '{normalizedPath}'."
                };
            }

            try
            {
                AssetDatabase.ImportAsset(normalizedPath, ImportAssetOptions.ForceUpdate);

                return new
                {
                    success = true,
                    message = $"Asset reimported successfully.",
                    path = normalizedPath,
                    asset = BuildAssetInfo(normalizedPath)
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error importing asset: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Handles the search action - searches for assets.
        /// </summary>
        private static object HandleSearch(string searchPattern, string filterType, int pageSize, int pageNumber)
        {
            // Build the search filter
            string filter = string.Empty;

            if (!string.IsNullOrEmpty(filterType))
            {
                filter = $"t:{filterType}";
            }

            if (!string.IsNullOrEmpty(searchPattern))
            {
                filter = string.IsNullOrEmpty(filter)
                    ? searchPattern
                    : $"{filter} {searchPattern}";
            }

            // Clamp pagination values
            int resolvedPageSize = Mathf.Clamp(pageSize, 1, MaxPageSize);
            int resolvedPageNumber = Mathf.Max(1, pageNumber);

            try
            {
                string[] guids = string.IsNullOrEmpty(filter)
                    ? AssetDatabase.FindAssets("")
                    : AssetDatabase.FindAssets(filter);

                int totalCount = guids.Length;
                int startIndex = (resolvedPageNumber - 1) * resolvedPageSize;
                int totalPages = (int)Math.Ceiling((double)totalCount / resolvedPageSize);

                if (startIndex >= totalCount && totalCount > 0)
                {
                    return new
                    {
                        success = false,
                        error = $"Page {resolvedPageNumber} exceeds total pages ({totalPages})."
                    };
                }

                int endIndex = Mathf.Min(startIndex + resolvedPageSize, totalCount);
                var assets = new List<object>();

                for (int i = startIndex; i < endIndex; i++)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        assets.Add(new
                        {
                            path = assetPath,
                            guid = guids[i],
                            name = Path.GetFileName(assetPath),
                            type = AssetDatabase.GetMainAssetTypeAtPath(assetPath)?.Name ?? "Unknown"
                        });
                    }
                }

                return new
                {
                    success = true,
                    filter = string.IsNullOrEmpty(filter) ? "(all assets)" : filter,
                    assets,
                    pagination = new
                    {
                        pageNumber = resolvedPageNumber,
                        pageSize = resolvedPageSize,
                        totalCount,
                        totalPages,
                        hasNextPage = resolvedPageNumber < totalPages,
                        hasPreviousPage = resolvedPageNumber > 1
                    }
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error searching assets: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Handles the get_info action - gets detailed information about an asset.
        /// </summary>
        private static object HandleGetInfo(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw MCPException.InvalidParams("'path' parameter is required for get_info action.");
            }

            string normalizedPath = PathUtilities.NormalizePath(path);

            if (!AssetExists(normalizedPath))
            {
                return new
                {
                    success = false,
                    error = $"Asset not found at '{normalizedPath}'."
                };
            }

            try
            {
                var assetInfo = BuildDetailedAssetInfo(normalizedPath);
                return new
                {
                    success = true,
                    asset = assetInfo
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error getting asset info: {exception.Message}"
                };
            }
        }

        #endregion

        #region Asset Creation Methods

        /// <summary>
        /// Creates a folder at the specified path.
        /// </summary>
        private static object CreateFolder(string path)
        {
            return CreateFolderRecursive(path);
        }

        /// <summary>
        /// Creates folders recursively to ensure the full path exists.
        /// </summary>
        private static object CreateFolderRecursive(string path)
        {
            string normalizedPath = path.Replace('\\', '/').TrimEnd('/');

            if (AssetDatabase.IsValidFolder(normalizedPath))
            {
                return new
                {
                    success = true,
                    message = $"Folder already exists at '{normalizedPath}'.",
                    path = normalizedPath,
                    guid = AssetDatabase.AssetPathToGUID(normalizedPath)
                };
            }

            // Split path into parts
            string[] parts = normalizedPath.Split('/');
            string currentPath = string.Empty;

            for (int i = 0; i < parts.Length; i++)
            {
                if (i == 0)
                {
                    currentPath = parts[i];
                    // First part should be "Assets"
                    if (!currentPath.Equals("Assets", StringComparison.OrdinalIgnoreCase))
                    {
                        currentPath = "Assets";
                    }
                    continue;
                }

                string parentPath = currentPath;
                currentPath = $"{currentPath}/{parts[i]}";

                if (!AssetDatabase.IsValidFolder(currentPath))
                {
                    string guid = AssetDatabase.CreateFolder(parentPath, parts[i]);
                    if (string.IsNullOrEmpty(guid))
                    {
                        return new
                        {
                            success = false,
                            error = $"Failed to create folder at '{currentPath}'."
                        };
                    }
                }
            }

            return new
            {
                success = true,
                message = $"Folder created successfully.",
                path = normalizedPath,
                guid = AssetDatabase.AssetPathToGUID(normalizedPath)
            };
        }

        /// <summary>
        /// Creates a new Material asset.
        /// </summary>
        private static object CreateMaterial(string path, Dictionary<string, object> properties)
        {
            // Ensure path has .mat extension
            if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
            {
                path = Path.ChangeExtension(path, ".mat");
            }

            // Determine shader
            Shader shader = Shader.Find("Standard");

            if (properties != null && properties.TryGetValue("shader", out object shaderValue))
            {
                string shaderName = shaderValue?.ToString();
                if (!string.IsNullOrEmpty(shaderName))
                {
                    Shader foundShader = Shader.Find(shaderName);
                    if (foundShader != null)
                    {
                        shader = foundShader;
                    }
                    else
                    {
                        Debug.LogWarning($"[ManageAsset] Shader '{shaderName}' not found. Using 'Standard' shader.");
                    }
                }
            }

            try
            {
                Material material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
                AssetDatabase.SaveAssets();

                return new
                {
                    success = true,
                    message = $"Material created successfully.",
                    asset = BuildAssetInfo(path)
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error creating material: {exception.Message}"
                };
            }
        }

        /// <summary>
        /// Creates a new PhysicMaterial asset.
        /// </summary>
        private static object CreatePhysicsMaterial(string path, Dictionary<string, object> properties)
        {
            // Ensure path has .physicMaterial extension
            if (!path.EndsWith(".physicMaterial", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".physicsMaterial", StringComparison.OrdinalIgnoreCase))
            {
                path = Path.ChangeExtension(path, ".physicMaterial");
            }

            try
            {
                PhysicsMaterial physicMaterial = new PhysicsMaterial();

                // Apply properties
                if (properties != null)
                {
                    if (properties.TryGetValue("friction", out object frictionValue) ||
                        properties.TryGetValue("dynamic_friction", out frictionValue))
                    {
                        if (TryConvertToFloat(frictionValue, out float friction))
                        {
                            physicMaterial.dynamicFriction = friction;
                            physicMaterial.staticFriction = friction;
                        }
                    }

                    if (properties.TryGetValue("static_friction", out object staticFrictionValue))
                    {
                        if (TryConvertToFloat(staticFrictionValue, out float staticFriction))
                        {
                            physicMaterial.staticFriction = staticFriction;
                        }
                    }

                    if (properties.TryGetValue("bounciness", out object bouncinessValue))
                    {
                        if (TryConvertToFloat(bouncinessValue, out float bounciness))
                        {
                            physicMaterial.bounciness = Mathf.Clamp01(bounciness);
                        }
                    }
                }

                AssetDatabase.CreateAsset(physicMaterial, path);
                AssetDatabase.SaveAssets();

                return new
                {
                    success = true,
                    message = $"Physics material created successfully.",
                    asset = BuildAssetInfo(path)
                };
            }
            catch (Exception exception)
            {
                return new
                {
                    success = false,
                    error = $"Error creating physics material: {exception.Message}"
                };
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Checks if an asset exists at the given path.
        /// </summary>
        private static bool AssetExists(string path)
        {
            // Check if it's a folder
            if (AssetDatabase.IsValidFolder(path))
            {
                return true;
            }

            // Check if it's an asset
            return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null;
        }

        /// <summary>
        /// Builds basic asset information.
        /// </summary>
        private static object BuildAssetInfo(string path)
        {
            string guid = AssetDatabase.AssetPathToGUID(path);
            Type assetType = AssetDatabase.GetMainAssetTypeAtPath(path);

            return new
            {
                path,
                guid,
                name = Path.GetFileName(path),
                type = assetType?.Name ?? "Unknown"
            };
        }

        /// <summary>
        /// Builds detailed asset information.
        /// </summary>
        private static object BuildDetailedAssetInfo(string path)
        {
            string guid = AssetDatabase.AssetPathToGUID(path);
            Type assetType = AssetDatabase.GetMainAssetTypeAtPath(path);
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);

            bool isFolder = AssetDatabase.IsValidFolder(path);

            var info = new Dictionary<string, object>
            {
                { "path", path },
                { "guid", guid },
                { "name", Path.GetFileName(path) },
                { "type", assetType?.Name ?? "Unknown" },
                { "isFolder", isFolder }
            };

            if (!isFolder && asset != null)
            {
                info["instanceID"] = asset.GetInstanceID();

                // Get file info
                string fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    FileInfo fileInfo = new FileInfo(fullPath);
                    info["fileSize"] = fileInfo.Length;
                    info["lastModified"] = fileInfo.LastWriteTimeUtc.ToString("o");
                }

                // Get dependencies
                string[] dependencies = AssetDatabase.GetDependencies(path, false);
                info["dependencies"] = dependencies.Where(d => d != path).ToArray();

                // Get labels
                string[] labels = AssetDatabase.GetLabels(asset);
                if (labels.Length > 0)
                {
                    info["labels"] = labels;
                }

                // Add type-specific information
                if (asset is Material material)
                {
                    info["shader"] = material.shader?.name ?? "None";
                }
                else if (asset is Texture2D texture)
                {
                    info["width"] = texture.width;
                    info["height"] = texture.height;
                    info["format"] = texture.format.ToString();
                }
                else if (asset is AudioClip audioClip)
                {
                    info["length"] = audioClip.length;
                    info["channels"] = audioClip.channels;
                    info["frequency"] = audioClip.frequency;
                }
                else if (asset is PhysicsMaterial physicMaterial)
                {
                    info["dynamicFriction"] = physicMaterial.dynamicFriction;
                    info["staticFriction"] = physicMaterial.staticFriction;
                    info["bounciness"] = physicMaterial.bounciness;
                }
            }
            else if (isFolder)
            {
                // Get folder contents count
                string[] subFolders = AssetDatabase.GetSubFolders(path);
                string[] allAssets = AssetDatabase.FindAssets("", new[] { path });

                info["subFolderCount"] = subFolders.Length;
                info["assetCount"] = allAssets.Length - subFolders.Length;
            }

            return info;
        }

        /// <summary>
        /// Tries to convert an object to a float value.
        /// </summary>
        private static bool TryConvertToFloat(object value, out float result)
        {
            result = 0f;

            if (value == null)
            {
                return false;
            }

            try
            {
                result = Convert.ToSingle(value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
