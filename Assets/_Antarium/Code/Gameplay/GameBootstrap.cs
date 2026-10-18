using Antarium.Content;
using Antarium.Core;
using Antarium.Presentation;
using Antarium.Sim;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Composition root. Builds the simulation and its views from assets and drives the frame loop.
    /// This is the only MonoBehaviour in the game loop; everything else is plain C# wired here.
    /// Run "Antarium/Setup/Create Main Scene" to create a scene with this wired up.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] ContentDatabase content;
        [SerializeField] GameConfig config;
        [SerializeField] InputActionAsset inputActions;

        [Header("Scene")]
        [SerializeField] Camera worldCamera;
        [SerializeField] Light2D globalLight;
        [SerializeField] UIDocument hudDocument;

        [Header("Rendering")]
        [SerializeField] Material terrainMaterial;
        [SerializeField] Material particleMaterial;
        [SerializeField] Material spriteMaterial;

        [Header("World")]
        [SerializeField] bool randomSeed;
        [SerializeField, Tooltip("0 = use the WorldGenSettings default seed.")] uint seedOverride;

        ContentDB _content;
        Simulation _sim;
        TerrainView _terrain;
        DigFeedback _feedback;
        PixelCamera _camera;
        GameInput _input;
        CameraController _cameraController;
        ToolController _tools;
        HudController _hud;
        DebugHud _debugHud;
        ColonyView _colonyView;
        float2 _worldSize;

        void Awake()
        {
            if (content == null || config == null || inputActions == null || worldCamera == null || terrainMaterial == null || spriteMaterial == null)
            {
                Debug.LogError("GameBootstrap is missing references. Run 'Antarium/Setup/Create Main Scene'.", this);
                enabled = false;
                return;
            }

            _content = ContentDB.Build(content);
            uint seed = randomSeed ? (uint)System.Environment.TickCount | 1u
                : seedOverride != 0 ? seedOverride
                : _content.WorldGen.defaultSeed;
            _sim = new Simulation(_content, config, seed);

            if (globalLight != null) globalLight.intensity = config.render.ambient;

            _terrain = new TerrainView(_sim.World, _sim.Light, _content, config.render, terrainMaterial, transform);
            _terrain.Upload();
            _feedback = new DigFeedback(_content, config.feedback, config.render.ambient, _sim.World, _sim.Light, particleMaterial, transform);

            _worldSize = new float2(_sim.World.Width, _sim.World.Height) * WorldConstants.CellSize;
            _colonyView = new ColonyView(_content, spriteMaterial, transform, _worldSize);
            _camera = new PixelCamera(worldCamera, config.camera);
            int startX = _sim.World.Width / 2;
            _camera.Position = WorldGrid.CellCenterToWorld(new int2(startX, _sim.World.SurfaceY[startX] - _content.WorldGen.nestDepth / 2));

            _input = new GameInput(inputActions);
            _cameraController = new CameraController(_camera, _input);
            _tools = new ToolController(config.dig);
            _hud = new HudController(hudDocument, _content);
            _hud.ToolClicked += _tools.SetMode;
            _tools.ModeChanged += _hud.SetActiveTool;
            _debugHud = new DebugHud(_hud.DebugLabel) { Visible = Debug.isDebugBuild };
        }

        void Update()
        {
            float dt = Time.deltaTime;
            double now = Time.timeAsDouble;

            _cameraController.Update(dt);
            _camera.Apply(dt, _worldSize);
            float2 pointerWorld = _camera.ScreenToWorld(_input.Pointer);

            _tools.Update(_sim, _input, pointerWorld, _hud.IsPointerOverUi(_input.Pointer), dt, now);
            if (_input.ToggleHud.WasPressedThisFrame()) _debugHud.Visible = !_debugHud.Visible;
            if (Debug.isDebugBuild && _input.SpawnAnts.WasPressedThisFrame()) _sim.SpawnWorkers(50);

            _sim.Advance(dt);
            _sim.UpdateDerived();
            _terrain.Upload();
            _colonyView.Render(_sim, _sim.Clock.Alpha);

            var events = _sim.Events;
            _camera.AddTrauma(_feedback.Consume(events));
            _tools.Consume(events, now);
            _hud.Update(dt, _sim);
            _debugHud.Update(dt, _sim, pointerWorld, _camera.Zoom, _tools);
            events.Flush();
        }

        void OnDestroy()
        {
            _input?.Dispose();
            _feedback?.Dispose();
            _colonyView?.Dispose();
            _terrain?.Dispose();
            _sim?.Dispose();
            _content?.Dispose();
        }
    }
}
