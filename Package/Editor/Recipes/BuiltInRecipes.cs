using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if UNITY_MCP_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using UnityMCP.Editor;

namespace UnityMCP.Editor.Recipes
{
    /// <summary>
    /// Built-in scene recipes that create common scene setups.
    /// </summary>
    public static class BuiltInRecipes
    {
        /// <summary>
        /// Creates a basic FPS prototype scene with player, ground, and lighting.
        /// </summary>
        [MCPRecipe("fps_prototype", "Creates a basic FPS prototype scene with player, ground, and lighting")]
        public static object FpsPrototype(
            [MCPParam("ground_size", "Ground plane size", Minimum = 10, Maximum = 1000)] float groundSize = 100)
        {
            var createdObjects = new List<string>();

            // Ground plane
            var groundGameObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
            groundGameObject.name = "Ground";
            float planeScaleFactor = groundSize / 10f;
            groundGameObject.transform.localScale = new Vector3(planeScaleFactor, 1, planeScaleFactor);
            Undo.RegisterCreatedObjectUndo(groundGameObject, "Create Ground");
            createdObjects.Add("Ground");

            // Directional Light
            var lightGameObject = new GameObject("Directional Light");
            var lightComponent = lightGameObject.AddComponent<Light>();
            lightComponent.type = LightType.Directional;
            lightGameObject.transform.rotation = Quaternion.Euler(50, -30, 0);
            Undo.RegisterCreatedObjectUndo(lightGameObject, "Create Directional Light");
            createdObjects.Add("Directional Light");

            // Player capsule
            var playerGameObject = new GameObject("Player");
            playerGameObject.transform.position = new Vector3(0, 1, 0);

            var playerRigidbody = playerGameObject.AddComponent<Rigidbody>();
            playerRigidbody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            var playerCollider = playerGameObject.AddComponent<CapsuleCollider>();
            playerCollider.height = 2f;
            playerCollider.center = new Vector3(0, 0, 0);

            Undo.RegisterCreatedObjectUndo(playerGameObject, "Create Player");
            createdObjects.Add("Player");

            // Camera as child of player at eye height
            var cameraGameObject = new GameObject("Main Camera");
            cameraGameObject.tag = "MainCamera";
            cameraGameObject.AddComponent<Camera>();
            cameraGameObject.AddComponent<AudioListener>();
            cameraGameObject.transform.SetParent(playerGameObject.transform);
            cameraGameObject.transform.localPosition = new Vector3(0, 0.8f, 0);
            cameraGameObject.transform.localRotation = Quaternion.identity;
            Undo.RegisterCreatedObjectUndo(cameraGameObject, "Create Main Camera");
            createdObjects.Add("Main Camera (child of Player)");

            return new
            {
                success = true,
                recipe = "fps_prototype",
                created = createdObjects,
                summary = $"FPS prototype created with {groundSize}x{groundSize} ground, player capsule with rigidbody, and camera at eye height"
            };
        }

        /// <summary>
        /// Creates a Canvas with EventSystem and basic layout panels (header, content, footer).
        /// </summary>
        [MCPRecipe("ui_canvas", "Creates a Canvas with EventSystem and basic layout panels")]
        public static object UiCanvas()
        {
            var createdObjects = new List<string>();

            // Canvas (ScreenSpace-Overlay)
            var canvasGameObject = new GameObject("Canvas");
            var canvasComponent = canvasGameObject.AddComponent<Canvas>();
            canvasComponent.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGameObject.AddComponent<CanvasScaler>();
            canvasGameObject.AddComponent<GraphicRaycaster>();
            Undo.RegisterCreatedObjectUndo(canvasGameObject, "Create Canvas");
            createdObjects.Add("Canvas");

            // EventSystem
            var eventSystemGameObject = new GameObject("EventSystem");
            eventSystemGameObject.AddComponent<EventSystem>();
#if UNITY_MCP_INPUT_SYSTEM
            eventSystemGameObject.AddComponent<InputSystemUIInputModule>();
#else
            eventSystemGameObject.AddComponent<StandaloneInputModule>();
#endif
            Undo.RegisterCreatedObjectUndo(eventSystemGameObject, "Create EventSystem");
            createdObjects.Add("EventSystem");

            // Header panel (top, 60px height)
            var headerGameObject = new GameObject("Header");
            headerGameObject.transform.SetParent(canvasGameObject.transform, false);
            var headerImage = headerGameObject.AddComponent<Image>();
            headerImage.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            var headerRectTransform = headerGameObject.GetComponent<RectTransform>();
            headerRectTransform.anchorMin = new Vector2(0, 1);
            headerRectTransform.anchorMax = new Vector2(1, 1);
            headerRectTransform.pivot = new Vector2(0.5f, 1);
            headerRectTransform.sizeDelta = new Vector2(0, 60);
            headerRectTransform.anchoredPosition = Vector2.zero;
            Undo.RegisterCreatedObjectUndo(headerGameObject, "Create Header Panel");
            createdObjects.Add("Header Panel");

            // Content panel (fill remaining space between header and footer)
            var contentGameObject = new GameObject("Content");
            contentGameObject.transform.SetParent(canvasGameObject.transform, false);
            var contentImage = contentGameObject.AddComponent<Image>();
            contentImage.color = new Color(0.9f, 0.9f, 0.9f, 1f);
            var contentRectTransform = contentGameObject.GetComponent<RectTransform>();
            contentRectTransform.anchorMin = new Vector2(0, 0);
            contentRectTransform.anchorMax = new Vector2(1, 1);
            contentRectTransform.offsetMin = new Vector2(0, 40);
            contentRectTransform.offsetMax = new Vector2(0, -60);
            Undo.RegisterCreatedObjectUndo(contentGameObject, "Create Content Panel");
            createdObjects.Add("Content Panel");

            // Footer panel (bottom, 40px height)
            var footerGameObject = new GameObject("Footer");
            footerGameObject.transform.SetParent(canvasGameObject.transform, false);
            var footerImage = footerGameObject.AddComponent<Image>();
            footerImage.color = new Color(0.25f, 0.25f, 0.25f, 1f);
            var footerRectTransform = footerGameObject.GetComponent<RectTransform>();
            footerRectTransform.anchorMin = new Vector2(0, 0);
            footerRectTransform.anchorMax = new Vector2(1, 0);
            footerRectTransform.pivot = new Vector2(0.5f, 0);
            footerRectTransform.sizeDelta = new Vector2(0, 40);
            footerRectTransform.anchoredPosition = Vector2.zero;
            Undo.RegisterCreatedObjectUndo(footerGameObject, "Create Footer Panel");
            createdObjects.Add("Footer Panel");

            return new
            {
                success = true,
                recipe = "ui_canvas",
                created = createdObjects,
                summary = "UI Canvas created with EventSystem, Header (60px), Content (fill), and Footer (40px) panels"
            };
        }

        /// <summary>
        /// Creates a basic 3D scene with lighting, ground, and camera.
        /// </summary>
        [MCPRecipe("3d_scene_template", "Creates a basic 3D scene with lighting, ground, and camera")]
        public static object SceneTemplate()
        {
            var createdObjects = new List<string>();

            // Directional Light rotated 50/30/0
            var lightGameObject = new GameObject("Directional Light");
            var lightComponent = lightGameObject.AddComponent<Light>();
            lightComponent.type = LightType.Directional;
            lightGameObject.transform.rotation = Quaternion.Euler(50, 30, 0);
            Undo.RegisterCreatedObjectUndo(lightGameObject, "Create Directional Light");
            createdObjects.Add("Directional Light");

            // Ground Plane at origin
            var groundGameObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
            groundGameObject.name = "Ground";
            groundGameObject.transform.position = Vector3.zero;
            groundGameObject.transform.localScale = new Vector3(10, 1, 10);
            Undo.RegisterCreatedObjectUndo(groundGameObject, "Create Ground");
            createdObjects.Add("Ground");

            // Main Camera at (0,5,-10) looking at origin
            var cameraGameObject = new GameObject("Main Camera");
            cameraGameObject.tag = "MainCamera";
            cameraGameObject.AddComponent<Camera>();
            cameraGameObject.AddComponent<AudioListener>();
            cameraGameObject.transform.position = new Vector3(0, 5, -10);
            cameraGameObject.transform.LookAt(Vector3.zero);
            Undo.RegisterCreatedObjectUndo(cameraGameObject, "Create Main Camera");
            createdObjects.Add("Main Camera");

            return new
            {
                success = true,
                recipe = "3d_scene_template",
                created = createdObjects,
                summary = "3D scene template created with directional light, ground plane, and camera at (0,5,-10)"
            };
        }

        /// <summary>
        /// Creates a physics playground with ramps, spheres, and cubes.
        /// </summary>
        [MCPRecipe("physics_playground", "Creates a physics playground with ramps, spheres, and cubes")]
        public static object PhysicsPlayground()
        {
            var createdObjects = new List<string>();

            // Ground Plane
            var groundGameObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
            groundGameObject.name = "Ground";
            groundGameObject.transform.position = Vector3.zero;
            groundGameObject.transform.localScale = new Vector3(10, 1, 10);
            Undo.RegisterCreatedObjectUndo(groundGameObject, "Create Ground");
            createdObjects.Add("Ground");

            // 3 ramps at varying angles
            float[] rampAngles = { 15f, 30f, 45f };
            for (int rampIndex = 0; rampIndex < rampAngles.Length; rampIndex++)
            {
                var rampGameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rampGameObject.name = $"Ramp_{rampAngles[rampIndex]}deg";
                rampGameObject.transform.localScale = new Vector3(2, 0.2f, 4);
                float xPosition = (rampIndex - 1) * 5f;
                rampGameObject.transform.position = new Vector3(xPosition, 1.5f, -3);
                rampGameObject.transform.rotation = Quaternion.Euler(rampAngles[rampIndex], 0, 0);
                Undo.RegisterCreatedObjectUndo(rampGameObject, $"Create Ramp {rampAngles[rampIndex]}deg");
                createdObjects.Add(rampGameObject.name);
            }

            // 5 spheres with Rigidbodies at height
            for (int sphereIndex = 0; sphereIndex < 5; sphereIndex++)
            {
                var sphereGameObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphereGameObject.name = $"Sphere_{sphereIndex + 1}";
                float xPosition = (sphereIndex - 2) * 2f;
                sphereGameObject.transform.position = new Vector3(xPosition, 8, 0);
                sphereGameObject.AddComponent<Rigidbody>();
                Undo.RegisterCreatedObjectUndo(sphereGameObject, $"Create Sphere {sphereIndex + 1}");
                createdObjects.Add(sphereGameObject.name);
            }

            // 3 cubes with Rigidbodies
            for (int cubeIndex = 0; cubeIndex < 3; cubeIndex++)
            {
                var cubeGameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cubeGameObject.name = $"Cube_{cubeIndex + 1}";
                float xPosition = (cubeIndex - 1) * 3f;
                cubeGameObject.transform.position = new Vector3(xPosition, 6, 3);
                cubeGameObject.AddComponent<Rigidbody>();
                Undo.RegisterCreatedObjectUndo(cubeGameObject, $"Create Cube {cubeIndex + 1}");
                createdObjects.Add(cubeGameObject.name);
            }

            return new
            {
                success = true,
                recipe = "physics_playground",
                created = createdObjects,
                summary = "Physics playground created with ground, 3 ramps (15/30/45 deg), 5 spheres, and 3 cubes with rigidbodies"
            };
        }
    }
}
