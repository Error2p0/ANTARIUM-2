using System;
using System.IO;
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
    /// A session is one colony: starting a new colony or loading a save rebuilds the simulation and the
    /// world views, while camera, input and HUD stay. Run "Antarium/Setup/Create Main Scene" to create a
    /// scene with this wired up.
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

        [Header("Saving")]
        [SerializeField, Tooltip("Continue the saved colony on start (with offline progress). Off = always start a new colony.")]
        bool continueLastSave = true;

        ContentDB _content;
        SaveStore _store;
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
        BuildingView _buildingView;
        HoverInfo _hoverInfo;
        string[] _targetingHints;
        Light2D _possessLight;
        bool _overlay;
        float2 _worldSize;
        int2 _pointerCell;
        float _autosaveTimer;
        DateTime? _lastSaved;
        bool _quitSaved;
        FlightSequence _flight;

        /// <summary>The Nuptial Flight being shown: princesses fly up from the nest before the next colony is founded.</summary>
        sealed class FlightSequence
        {
            public Legacy Next;
            public int Earned;
            public int[] Modifiers;
            public float2 Origin;
            public int Count;
            public float Time;
        }

        void Awake()
        {
            if (content == null || config == null || inputActions == null || worldCamera == null || terrainMaterial == null || spriteMaterial == null)
            {
                Debug.LogError("GameBootstrap is missing references. Run 'Antarium/Setup/Create Main Scene'.", this);
                enabled = false;
                return;
            }

            _content = ContentDB.Build(content);
            _store = new SaveStore(Path.Combine(Application.persistentDataPath, "Saves"));
            if (globalLight != null) globalLight.intensity = config.render.ambient;

            _camera = new PixelCamera(worldCamera, config.camera);
            _input = new GameInput(inputActions);
            _cameraController = new CameraController(_camera, _input);
            _hud = new HudController(hudDocument, _content, config.render, config.save.autosaveSeconds);
            _hud.ToolClicked += mode => _tools?.SetMode(mode);
            _hud.BuildingClicked += def => _tools?.SelectBuilding(def);
            _hud.ScentClicked += kind => _tools?.SelectScent(kind);
            _hud.OverlayClicked += ToggleOverlay;
            _hud.ResearchToggled += ToggleResearch;
            _hud.PossessClicked += () => { if (_sim != null) _tools.ClickPossess(_sim); };
            _hud.CastesClicked += () => _hud.Castes?.Toggle();
            if (_hud.Castes != null)
                _hud.Castes.ShareStep += (caste, step) =>
                {
                    if (_sim != null) _sim.EnqueueCasteShare(caste, _sim.Colony.CasteShare[(int)caste] + step * _sim.Colony.ShareStep);
                };
            if (_hud.Contracts != null)
            {
                _hud.Contracts.ClaimClicked += slot => _sim?.EnqueueClaim(slot);
                _hud.EventFeed.GoClicked += cell => { if (_sim != null) LookAt(cell); };
                _hud.Abilities.Clicked += ability => { if (_sim != null) _tools.ClickAbility(_sim, ability, _pointerCell); };
                _hud.Research.ResearchClicked += node => _sim?.EnqueueResearch(node);
                _hud.Research.CloseClicked += _hud.HideModal;
                _hud.WelcomeBack.Closed += _hud.HideModal;
                _hud.Menu.Resume += _hud.HideModal;
                _hud.Menu.Save += () => Save(background: true, announce: true);
                _hud.Menu.Load += () => { if (!TryLoad(applyOffline: false)) _hud.ShowToast("There is no save to load."); };
                _hud.Menu.NewGame += () => StartNewColony(NewSeed(), _sim?.Legacy.Clone());
                _hud.Menu.Quit += Quit;
                _hud.Genetics.BuyClicked += trait => _sim?.EnqueueGenetic(trait);
                _hud.Genetics.CloseClicked += _hud.HideModal;
                _hud.Flight.FlyClicked += BeginFlight;
                _hud.Flight.GeneticsClicked += () => _hud.ShowModal(_hud.Genetics.Root);
                _hud.Flight.CloseClicked += _hud.HideModal;
            }
            _hud.GeneticsToggled += ToggleGenetics;
            _hud.FlightToggled += ToggleFlight;
            _hoverInfo = new HoverInfo(_content, _hud);
            _debugHud = new DebugHud(_hud.DebugLabel) { Visible = Debug.isDebugBuild };
            _targetingHints = Array.ConvertAll(_content.Abilities, a => $"Click where to use {a.DisplayName}. Right click or Esc cancels.");
            _possessLight = CreatePossessLight();

            if (!(continueLastSave && TryLoad(applyOffline: true)))
                StartNewColony(randomSeed ? (uint)Environment.TickCount | 1u : seedOverride != 0 ? seedOverride : _content.WorldGen.defaultSeed);
        }

        // ---------------------------------------------------------------- sessions

        /// <summary>A new colony that inherits <paramref name="legacy"/> (Genetic Memory and traits; none = a fresh bloodline).</summary>
        void StartNewColony(uint seed, Legacy legacy = null, int[] modifiers = null)
        {
            BeginSession(new Simulation(_content, config, seed, legacy ?? new Legacy(_content), modifiers));
            _lastSaved = null;
            _hud.ShowToast("A new colony begins. Contracts on the right show what to do next.");
        }

        static uint NewSeed() => (uint)(DateTime.UtcNow.Ticks ^ Environment.TickCount) | 1u;

        /// <summary>Loads the save. With <paramref name="applyOffline"/> the time since it was written is credited as offline progress.</summary>
        bool TryLoad(bool applyOffline)
        {
            if (!_store.HasSave) return false;
            Simulation sim;
            SaveHeader header;
            try
            {
                sim = _store.Load(_content, config, out header);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not load the save: {e}");
                _hud.ShowToast("The save could not be loaded: " + e.Message);
                return false;
            }
            BeginSession(sim);
            _lastSaved = header.SavedUtc;
            double away = (DateTime.UtcNow - header.SavedUtc).TotalSeconds;
            if (applyOffline && away >= config.save.offlineMinSeconds)
            {
                _hud.WelcomeBack?.Show(OfflineProgress.Apply(sim, away));
                _hud.ShowModal(_hud.WelcomeBack?.Root);
            }
            else _hud.ShowToast($"Loaded the colony saved {HudText.Duration(away)} ago.");
            return true;
        }

        void BeginSession(Simulation sim)
        {
            EndSession();
            _sim = sim;
            _worldSize = new float2(sim.World.Width, sim.World.Height) * WorldConstants.CellSize;
            _terrain = new TerrainView(sim.World, sim.Light, sim.Pheromones, _content, config.render, terrainMaterial, transform);
            _terrain.Upload();
            _feedback = new DigFeedback(_content, config.feedback, config.render.ambient, sim.World, sim.Light, particleMaterial, transform);
            _buildingView = new BuildingView(_content, config.render, spriteMaterial, transform, _worldSize);
            _colonyView = new ColonyView(_content, config.render, spriteMaterial, transform, _worldSize);
            _tools = new ToolController(config, _content, _buildingView.Ghosts);
            _tools.ModeChanged += _hud.SetActiveTool;
            _tools.ModeChanged += mode => _terrain?.SetScentCover(mode == ToolMode.Pheromone);
            _tools.Build.SelectionChanged += _hud.SetSelectedBuilding;
            _tools.ScentChanged += _hud.SetSelectedScent;
            _hud.SetActiveTool(_tools.Mode);
            _hud.SetSelectedBuilding(_tools.Build.Selected);
            _hud.SetSelectedScent(_tools.Scent);
            _hud.ResetSession(sim);
            LookAt(sim.Colony.QueenCell + new int2(0, 4));
            _autosaveTimer = config.save.autosaveSeconds;
        }

        void EndSession()
        {
            _feedback?.Dispose();
            _colonyView?.Dispose();
            _buildingView?.Dispose();
            _terrain?.Dispose();
            _sim?.Dispose();
            _feedback = null;
            _colonyView = null;
            _buildingView = null;
            _terrain = null;
            _sim = null;
        }

        void Save(bool background, bool announce)
        {
            if (_sim == null) return;
            try
            {
                var snapshot = SaveGame.Capture(_sim, DateTime.UtcNow);
                if (background)
                {
                    if (!_store.SaveInBackground(snapshot)) return; // the previous save is still being written
                }
                else _store.Save(snapshot);
                _lastSaved = snapshot.Header.SavedUtc;
                _autosaveTimer = config.save.autosaveSeconds;
                if (announce) _hud.ShowToast("Game saved.");
            }
            catch (Exception e)
            {
                Debug.LogError($"Saving failed: {e}");
                _hud.ShowToast("Saving failed: " + e.Message);
            }
        }

        void Quit()
        {
            Save(background: false, announce: false);
            _quitSaved = true;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnApplicationQuit()
        {
            if (_flight != null) FinishFlight(); // the flight was confirmed: found the new colony before saving
            if (_quitSaved || _sim == null) return;
            Save(background: false, announce: false);
            _quitSaved = true;
        }

        // ---------------------------------------------------------------- frame loop

        void Update()
        {
            if (_sim == null) return;
            float dt = Time.deltaTime;
            double now = Time.timeAsDouble;
            if (_flight != null)
            {
                UpdateFlight(dt);
                return;
            }

            HandleSystemKeys();
            bool paused = _hud.IsShowing(_hud.Menu?.Root);

            bool possessing = _tools.Possessing;
            _cameraController.Update(dt, allowPan: !possessing);
            if (possessing) FollowPossessed(dt);
            _camera.Apply(dt, _worldSize);
            float2 pointerWorld = _camera.ScreenToWorld(_input.Pointer);
            _pointerCell = WorldGrid.WorldToCell(pointerWorld);

            bool overUi = _hud.IsPointerOverUi(_input.Pointer);
            if (!paused) _tools.Update(_sim, _input, pointerWorld, overUi, dt, now);
            if (_input.Overlay.WasPressedThisFrame()) ToggleOverlay();
            if (_input.ToggleHud.WasPressedThisFrame()) _debugHud.Visible = !_debugHud.Visible;
            if (Debug.isDebugBuild)
            {
                if (_input.SpawnAnts.WasPressedThisFrame()) _sim.SpawnWorkers(50);
                if (_input.FinishBuilds.WasPressedThisFrame()) _sim.Buildings.CompleteAllBlueprints();
                if (_input.GiveItems.WasPressedThisFrame())
                {
                    _sim.GiveResources(20, 50f);
                    _sim.Colony.Inventory.AddKnowledge(50f, studied: false);
                }
                if (_input.TriggerEvent.WasPressedThisFrame()) _sim.EnqueueTriggerEvent(-1);
                if (_input.TriggerRaid.WasPressedThisFrame()) _sim.EnqueueTriggerRaid();
                if (_input.GivePrestige.WasPressedThisFrame()) _sim.DebugGivePrestige(2, 10);
            }

            if (!paused) _sim.Advance(dt);
            _sim.UpdateDerived();
            _terrain.Upload();
            float alpha = _sim.Clock.Alpha;
            _buildingView.Render(_sim, alpha, Time.time, _overlay);
            _colonyView.Render(_sim, alpha, _overlay, Time.time);

            var events = _sim.Events;
            _camera.AddTrauma(_feedback.Consume(events) + EventShake(events));
            _tools.Consume(events, now);
            _hud.Consume(events, _sim);
            _hud.Update(dt, _sim, _tools.TargetingAbility, _hud.IsShowing(_hud.Menu?.Root) ? MenuStatus() : null, _tools.Possessing, _tools.TargetingPossess);
            _hoverInfo.Update(dt, _sim, WorldGrid.WorldToCell(pointerWorld), _tools, overUi);
            _hud.SetInfo(_tools.TargetingAbility >= 0 ? _targetingHints[_tools.TargetingAbility]
                : _tools.TargetingPossess ? "Click an ant to take control of it. Right click or Esc cancels."
                : _hoverInfo.Text);
            UpdatePossessLight(alpha);
            _debugHud.Update(dt, _sim, pointerWorld, _camera.Zoom, _tools);
            events.Flush();

            if (!paused && config.save.autosaveSeconds > 0f)
            {
                _autosaveTimer -= dt;
                if (_autosaveTimer <= 0f) Save(background: true, announce: false);
            }
            string error = _store.TakeError();
            if (error != null) _hud.ShowToast("Saving failed: " + error);
        }

        void HandleSystemKeys()
        {
            if (_input.Menu.WasPressedThisFrame())
            {
                if (_tools.CancelTargeting()) { }
                else if (_hud.ModalOpen) _hud.HideModal();
                else if (_tools.Possessing) _sim.EnqueueRelease();
                else _hud.ShowModal(_hud.Menu?.Root);
            }
            if (_input.Research.WasPressedThisFrame()) ToggleResearch();
            if (_input.Genetics.WasPressedThisFrame()) ToggleGenetics();
            if (_input.Flight.WasPressedThisFrame()) ToggleFlight();
            if (_input.Castes.WasPressedThisFrame() && _hud.Castes != null && CastesPanel.HasSpecialists(_sim)) _hud.Castes.Toggle();
            if (_input.QuickSave.WasPressedThisFrame()) Save(background: true, announce: true);
            if (_input.QuickLoad.WasPressedThisFrame() && !TryLoad(applyOffline: false)) _hud.ShowToast("There is no save to load.");
        }

        /// <summary>Camera shake for big moments: Seismic Sense, meteorite impacts.</summary>
        float EventShake(EventBus events)
        {
            float trauma = 0f;
            foreach (var e in events.Read<AbilityUsedEvent>())
                if (_content.Abilities[e.Ability].kind == AbilityKind.SeismicSense) trauma += config.feedback.seismicShake;
            foreach (var e in events.Read<RandomEventEvent>())
                if (e.Phase == RandomEventPhase.Started && _content.Events[e.Event].Def.kind == RandomEventKind.Meteorite)
                    trauma += config.feedback.meteoriteShake;
            int caveIns = events.Read<CaveInEvent>().Length;
            if (caveIns > 0) trauma += Mathf.Min(config.feedback.maxShakePerHit, caveIns * config.feedback.caveInShake);
            if (events.Read<GateBrokenEvent>().Length > 0) trauma += config.feedback.maxShakePerHit;
            return trauma;
        }

        string MenuStatus()
        {
            string saved = _lastSaved.HasValue ? $"saved {HudText.Duration((DateTime.UtcNow - _lastSaved.Value).TotalSeconds)} ago" : "not saved yet";
            return $"Seed {_sim.Seed}   played {HudText.Duration(_sim.PlayTime)}   {saved}";
        }

        void ToggleOverlay()
        {
            _overlay = !_overlay;
            _hud.SetOverlay(_overlay);
        }

        void ToggleResearch()
        {
            if (_hud.Research == null) return;
            if (_hud.IsShowing(_hud.Research.Root)) _hud.HideModal();
            else if (!_hud.IsShowing(_hud.Menu.Root)) _hud.ShowModal(_hud.Research.Root);
        }

        void ToggleGenetics()
        {
            if (_hud.Genetics == null || _sim == null) return;
            if (_hud.IsShowing(_hud.Genetics.Root)) _hud.HideModal();
            else if (!_hud.IsShowing(_hud.Menu.Root) && (_sim.Legacy.Awakened || _hud.FlightKnown(_sim))) _hud.ShowModal(_hud.Genetics.Root);
        }

        void ToggleFlight()
        {
            if (_hud.Flight == null || _sim == null) return;
            if (_hud.IsShowing(_hud.Flight.Root)) _hud.HideModal();
            else if (!_hud.IsShowing(_hud.Menu.Root) && _hud.FlightKnown(_sim))
            {
                _hud.Flight.Open(_sim);
                _hud.ShowModal(_hud.Flight.Root);
            }
        }

        // ---------------------------------------------------------------- Nuptial Flight

        /// <summary>The player confirmed the flight: the princesses take off, then the next colony is founded.</summary>
        void BeginFlight(int[] modifiers)
        {
            if (_sim == null || _flight != null) return;
            if (!_sim.TryNuptialFlight(out var next, out int earned))
            {
                _hud.ShowToast("The princesses are not ready to fly yet.");
                return;
            }
            var chosen = new System.Collections.Generic.List<int>();
            int slots = next.ModifierSlots(config.prestige);
            foreach (int m in modifiers)
                if (chosen.Count < slots && next.ModifierUnlocked(m) && !chosen.Contains(m)) chosen.Add(m);

            int2 queen = _sim.Colony.QueenCell;
            _flight = new FlightSequence
            {
                Next = next,
                Earned = earned,
                Modifiers = chosen.ToArray(),
                Origin = new float2(queen.x + 0.5f, _sim.World.SurfaceY[queen.x] + 1f),
                Count = math.clamp(_sim.Colony.Princesses, 1, 12),
            };
            _tools.CancelTargeting();
            _hud.HideModal();
            _hud.SetInfo(null);
            if (_possessLight != null) _possessLight.gameObject.SetActive(false);
            _colonyView.PrincessesFlown = true;
            _hud.ShowToast("The Nuptial Flight! The princesses take to the sky to found a new colony...");
        }

        /// <summary>Princesses climb from the nest entrance in a loose, wobbling swarm; the camera rises with them.</summary>
        void UpdateFlight(float dt)
        {
            var f = _flight;
            var cfg = config.prestige;
            f.Time += dt;
            var flyers = _colonyView.Flyers;
            flyers.Clear();
            float2 sum = f.Origin;
            int counted = 1;
            for (int i = 0; i < f.Count; i++)
            {
                float t = f.Time - i * 0.3f;
                if (t < 0f) continue;
                int side = (i & 1) == 0 ? 1 : -1;
                float drift = side * (0.6f + 0.35f * (i >> 1));
                float climb = cfg.flightSpeed * (0.85f + 0.1f * (i % 3));
                var p = f.Origin + new float2(drift * t + math.sin(t * 2.6f + i * 1.7f) * 0.8f, climb * t + math.sin(t * 7f + i) * 0.3f);
                flyers.Add(new Flyer { Pos = p * WorldConstants.CellSize, Phase = t + i * 0.13f, Facing = side });
                sum += p;
                counted++;
            }
            float2 target = sum / counted * WorldConstants.CellSize;
            _camera.Position = math.lerp(_camera.Position, target, 1f - math.exp(-3f * dt));
            _camera.Apply(dt, _worldSize);
            _terrain.Upload();
            _buildingView.Render(_sim, 1f, Time.time, false);
            _colonyView.Render(_sim, 1f, false, Time.time);
            _hud.Update(dt, _sim, -1, null);
            _sim.Events.Flush();
            if (f.Time >= cfg.flightSeconds) FinishFlight();
        }

        void FinishFlight()
        {
            var f = _flight;
            _flight = null;
            BeginSession(new Simulation(_content, config, NewSeed(), f.Next, f.Modifiers));
            _lastSaved = null;
            Save(background: false, announce: false); // the flight is final once the new colony exists
            _hud.ShowToast($"The princesses founded a new colony. +{f.Earned} Genetic Memory: spend it in the genetic tree (G).");
            _hud.ShowModal(_hud.Genetics?.Root);
        }

        void LookAt(int2 cell) => _camera.Position = WorldGrid.CellCenterToWorld(cell);

        /// <summary>The camera eases toward the ant the player controls.</summary>
        void FollowPossessed(float dt)
        {
            int i = _sim.PossessedAnt;
            if (i < 0) return;
            float2 target = _sim.Ants.Ants[i].Pos * WorldConstants.CellSize;
            float t = 1f - math.exp(-config.camera.followSharpness * dt);
            _camera.Position = math.lerp(_camera.Position, target, t);
        }

        Light2D CreatePossessLight()
        {
            var go = new GameObject("PossessLight");
            go.transform.SetParent(transform, false);
            var light = go.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.pointLightInnerRadius = 0f;
            light.pointLightOuterRadius = config.possession.lightRadius * WorldConstants.CellSize;
            light.intensity = config.possession.lightIntensity;
            light.color = config.render.possessedColor;
            light.shadowsEnabled = false;
            go.SetActive(false);
            return light;
        }

        /// <summary>A warm light follows the possessed ant (no shadows: see GDD 3.2).</summary>
        void UpdatePossessLight(float alpha)
        {
            if (_possessLight == null) return;
            int i = _sim.PossessedAnt;
            bool on = i >= 0 && config.possession.lightIntensity > 0f;
            if (_possessLight.gameObject.activeSelf != on) _possessLight.gameObject.SetActive(on);
            if (!on) return;
            var a = _sim.Ants.Ants[i];
            float2 p = math.lerp(a.PrevPos, a.Pos, alpha) * WorldConstants.CellSize;
            _possessLight.transform.position = new Vector3(p.x, p.y, 0f);
        }

        void OnDestroy()
        {
            _store?.Wait();
            _input?.Dispose();
            EndSession();
            _content?.Dispose();
        }
    }
}
