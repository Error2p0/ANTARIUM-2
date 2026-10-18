using System.IO;
using System.Linq;
using Antarium.Content;
using Antarium.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Antarium.Editor
{
    /// <summary>Builds the main game scene with every reference wired. Safe to re-run; it recreates the scene.</summary>
    public static class SceneSetup
    {
        const string ScenePath = "Assets/_Antarium/Scenes/Main.unity";
        const string RenderingFolder = "Assets/_Antarium/Rendering";
        const string UIFolder = "Assets/_Antarium/UI";
        const string TerrainMaterialPath = RenderingFolder + "/Terrain.mat";
        const string ParticleMaterialPath = RenderingFolder + "/PixelParticle.mat";
        const string ThemePath = UIFolder + "/AntariumTheme.tss";
        const string PanelSettingsPath = UIFolder + "/HudPanelSettings.asset";
        const string InputPath = "Assets/_Antarium/Input/AntariumInput.inputactions";

        [MenuItem("Antarium/Setup/Create Main Scene")]
        public static void CreateMainScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            ContentSetup.CreateDefaultContent();
            EnsureMaterial(TerrainMaterialPath, "Antarium/Terrain");
            EnsureMaterial(ParticleMaterialPath, "Antarium/PixelParticle");
            EnsurePanelSettings();
            AssetDatabase.SaveAssets();

            ContentSetup.EnsureFolder(Path.GetDirectoryName(ScenePath)!.Replace('\\', '/'));
            // NewScene unloads unused assets, so load references only after it.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var content = Load<ContentDatabase>(ContentSetup.DatabasePath);
            var config = Load<GameConfig>(ContentSetup.ConfigPath);
            var input = Load<InputActionAsset>(InputPath);
            var terrainMaterial = Load<Material>(TerrainMaterialPath);
            var particleMaterial = Load<Material>(ParticleMaterialPath);
            var panelSettings = Load<PanelSettings>(PanelSettingsPath);

            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            cameraGo.transform.position = new Vector3(0f, 0f, -10f);
            cameraGo.AddComponent<UniversalAdditionalCameraData>();

            var lightGo = new GameObject("Global Light 2D");
            var globalLight = lightGo.AddComponent<Light2D>();
            globalLight.lightType = Light2D.LightType.Global;
            globalLight.color = Color.white;
            globalLight.intensity = config.render.ambient;

            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<UIDocument>();
            hud.panelSettings = panelSettings;

            var gameGo = new GameObject("Game");
            var bootstrap = gameGo.AddComponent<GameBootstrap>();
            var so = new SerializedObject(bootstrap);
            so.FindProperty("content").objectReferenceValue = content;
            so.FindProperty("config").objectReferenceValue = config;
            so.FindProperty("inputActions").objectReferenceValue = input;
            so.FindProperty("worldCamera").objectReferenceValue = camera;
            so.FindProperty("globalLight").objectReferenceValue = globalLight;
            so.FindProperty("hudDocument").objectReferenceValue = hud;
            so.FindProperty("terrainMaterial").objectReferenceValue = terrainMaterial;
            so.FindProperty("particleMaterial").objectReferenceValue = particleMaterial;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            Debug.Log($"Created {ScenePath}. Press Play to dig.");
        }

        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new System.InvalidOperationException($"Missing {typeof(T).Name} at {path}.");
            return asset;
        }

        static Material EnsureMaterial(string path, string shaderName)
        {
            ContentSetup.EnsureFolder(RenderingFolder);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new System.InvalidOperationException($"Shader '{shaderName}' not found.");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static PanelSettings EnsurePanelSettings()
        {
            ContentSetup.EnsureFolder(UIFolder);
            if (!File.Exists(ThemePath))
            {
                File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(ThemePath);
            }
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings != null) return settings;
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1f;
            AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            return settings;
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
