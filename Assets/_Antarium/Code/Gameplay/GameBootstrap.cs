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
    /// world views, while camera, input, HUD and audio stay. The game opens on the title screen over the saved
    /// colony (paused); offline progress is credited when the player continues. Run "Antarium/Setup/Create Main
    /// Scene" to create a scene with this wired up.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        static readonly float[] DebugSpeeds = { 1f, 2f, 4f, 8f };

        [Header("Data")]
        [SerializeField] ContentDatabase content;
        [SerializeField] GameConfig config;
        [SerializeField] InputActionAsset inputActions;
        [SerializeField] SoundBank sounds;

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
        [SerializeField, Tooltip("Open on the title screen. Off = straight into the colony (handy while developing).")]
        bool showTitle = true;

        ContentDB _content;
        IPlatformServices _platform;
        SaveStore _store;
        SettingsStore _settingsStore;
        GameSettings _settings;
        Simulation _sim;
        TerrainView _terrain;
        DigFeedback _feedback;
        AudioDirector _audio;
        AchievementTracker _achievements;
        PixelCamera _camera;
        GameInput _input;
        CameraController _cameraController;
        ToolController _tools;
        HudController _hud;
        DebugHud _debugHud;
        ColonyView _colonyView;
        BuildingView _buildingView;
        NumberPops _pops;
        HoverInfo _hoverInfo;
        PanelSettings _panelSettings;
        string[] _targetingHints;
        Light2D _possessLight;
        bool _overlay;
        float2 _worldSize;
        int2 _pointerCell;
        float _autosaveTimer;
        DateTime? _lastSaved;
        bool _quitSaved;
        FlightSequence _flight;
        /// <summary>The colony was never saved: leaving the title screen just starts playing it.</summary>
        bool _freshColony;
        /// <summary>A system dialog was opened from the Esc menu: closing it goes back to the menu.</summary>
        bool _backToMenu;
        bool _focused = true, _overlayOpen;
        int _speed;
        float _titleTime;
        /// <summary>Deepest biome the colony has dug into (for the discovery cue); -1 until a session starts.</summary>
        int _biomeReached = -1;
        (FullScreenMode Mode, int W, int H, bool Vsync, int Cap) _display = (FullScreenMode.MaximizedWindow, -1, -1, false, -1);

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
            _platform = PlatformServices.Create(Application.persistentDataPath);
            _platform.OverlayChanged += open => _overlayOpen = open;
            _store = new SaveStore(_platform.SaveDirectory);
            _settingsStore = new SettingsStore(Application.persistentDataPath);
            _settings = _settingsStore.Load();
            if (globalLight != null) globalLight.intensity = config.render.ambient;
            if (hudDocument != null && hudDocument.panelSettings != null)
            {
                // A copy, so the UI scale setting never edits the asset.
                _panelSettings = Instantiate(hudDocument.panelSettings);
                hudDocument.panelSettings = _panelSettings;
            }

            _camera = new PixelCamera(worldCamera, config.camera);
            _input = new GameInput(inputActions);
            _input.LoadOverrides(_settings.bindingOverrides);
            _cameraController = new CameraController(_camera, _input);
            _audio = sounds != null ? new AudioDirector(sounds, _content, transform) : null;
            _achievements = new AchievementTracker(_content, _platform);
            _achievements.Unlocked += a =>
            {
                _hud.ShowToast($"Achievement unlocked: {a.Def.DisplayName}! {a.Def.description}");
                _audio?.Play(SoundCue.Achievement);
            };
            _hud = new HudController(hudDocument, _content, config.render, config.save.autosaveSeconds, _input);
            WireHud();
            _hoverInfo = new HoverInfo(_content, _hud);
            _debugHud = new DebugHud(_hud.DebugLabel) { Visible = Debug.isDebugBuild };
            _targetingHints = Array.ConvertAll(_content.Abilities, a => $"Click where to use {a.DisplayName}. Right click or Esc cancels.");
            _possessLight = CreatePossessLight();
            ApplySettings(_settings);

            if (!(continueLastSave && TryLoad(applyOffline: !showTitle)))
                StartNewColony(randomSeed ? (uint)Environment.TickCount | 1u : seedOverride != 0 ? seedOverride : _content.WorldGen.defaultSeed, announce: !showTitle);
            if (showTitle) ShowTitle();
        }

        void WireHud()
        {
            _hud.ToolClicked += mode => _tools?.SetMode(mode);
            _hud.BuildingClicked += def => _tools?.SelectBuilding(def);
            _hud.ScentClicked += kind => _tools?.SelectScent(kind);
            _hud.OverlayClicked += ToggleOverlay;
            _hud.ResearchToggled += ToggleResearch;
            _hud.PossessClicked += () => { if (_sim != null) _tools.ClickPossess(_sim); };
            _hud.CastesClicked += () => _hud.Castes?.Toggle();
            _hud.GeneticsToggled += ToggleGenetics;
            _hud.FlightToggled += ToggleFlight;
            _hud.UiClicked += () => _audio?.Play(SoundCue.UiClick);
            _hud.UiHovered += () => _audio?.Play(SoundCue.UiHover);
            _hud.ModalOpened += () => _audio?.Play(SoundCue.UiOpen);
            _hud.ModalClosed += () => _audio?.Play(SoundCue.UiClose);
            _hud.ToastShown += () => _audio?.Play(SoundCue.Toast);
            if (_hud.Castes != null)
                _hud.Castes.ShareStep += (caste, step) =>
                {
                    if (_sim != null) _sim.EnqueueCasteShare(caste, _sim.Colony.CasteShare[(int)caste] + step * _sim.Colony.ShareStep);
                };
            if (_hud.Contracts == null) return;
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
            _hud.Menu.Settings += () => OpenSettings(fromMenu: true);
            _hud.Menu.Achievements += () => OpenAchievements(fromMenu: true);
            _hud.Menu.Help += () => OpenHelp(fromMenu: true);
            _hud.Menu.MainMenu += () =>
            {
                Save(background: false, announce: false);
                ShowTitle();
            };
            _hud.Menu.Quit += Quit;
            _hud.Genetics.BuyClicked += trait => _sim?.EnqueueGenetic(trait);
            _hud.Genetics.CloseClicked += _hud.HideModal;
            _hud.Flight.FlyClicked += BeginFlight;
            _hud.Flight.GeneticsClicked += () => _hud.ShowModal(_hud.Genetics.Root);
            _hud.Flight.CloseClicked += _hud.HideModal;
            _hud.Settings.Changed += ApplySettings;
            _hud.Settings.Closed += CloseSystemDialog;
            _hud.Settings.EraseProgress += EraseProgress;
            _hud.AchievementsPanel.Closed += CloseSystemDialog;
            _hud.Help.Closed += CloseSystemDialog;
            _hud.Title.Continue += LeaveTitle;
            _hud.Title.NewColony += () =>
            {
                if (!_freshColony) StartNewColony(NewSeed(), _sim?.Legacy.Clone());
                LeaveTitle();
            };
            _hud.Title.Settings += () => OpenSettings(fromMenu: false);
            _hud.Title.Achievements += () => OpenAchievements(fromMenu: false);
            _hud.Title.Help += () => OpenHelp(fromMenu: false);
            _hud.Title.Quit += Quit;
        }

        // ---------------------------------------------------------------- sessions

        /// <summary>A new colony that inherits <paramref name="legacy"/> (Genetic Memory and traits; none = a fresh bloodline).</summary>
        void StartNewColony(uint seed, Legacy legacy = null, int[] modifiers = null, bool announce = true)
        {
            BeginSession(new Simulation(_content, config, seed, legacy ?? new Legacy(_content), modifiers));
            _lastSaved = null;
            _freshColony = true;
            if (announce) _hud.ShowToast("A new colony begins. Contracts on the right show what to do next.");
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
            _freshColony = false;
            if (applyOffline) CreditOffline(announceLoad: true);
            return true;
        }

        /// <summary>Credits the time since the last save as offline progress (with the Welcome back summary).</summary>
        void CreditOffline(bool announceLoad)
        {
            if (_lastSaved == null) return;
            double away = (DateTime.UtcNow - _lastSaved.Value).TotalSeconds;
            if (away >= config.save.offlineMinSeconds)
            {
                _hud.WelcomeBack?.Show(OfflineProgress.Apply(_sim, away));
                _hud.ShowModal(_hud.WelcomeBack?.Root);
            }
            else if (announceLoad) _hud.ShowToast($"Loaded the colony saved {HudText.Duration(away)} ago.");
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
            _pops = new NumberPops(_content, config.feedback, spriteMaterial, transform, _worldSize) { Enabled = _settings.numberPops };
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
            _autosaveTimer = AutosaveSeconds;
            _speed = 0;
            _biomeReached = sim.BiomeAtDepth(sim.Colony.Stats.MaxDepth);
        }

        void EndSession()
        {
            _feedback?.Dispose();
            _pops?.Dispose();
            _colonyView?.Dispose();
            _buildingView?.Dispose();
            _terrain?.Dispose();
            _sim?.Dispose();
            _feedback = null;
            _pops = null;
            _colonyView = null;
            _buildingView = null;
            _terrain = null;
            _sim = null;
        }

        float AutosaveSeconds => _settings.autosaveMinutes * 60f;

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
                _freshColony = false;
                _autosaveTimer = AutosaveSeconds;
                if (announce) _hud.ShowToast("Game saved.");
            }
            catch (Exception e)
            {
                Debug.LogError($"Saving failed: {e}");
                _hud.ShowToast("Saving failed: " + e.Message);
            }
        }

        /// <summary>
        /// Nothing is saved while the title screen is up: a loaded colony has not been credited its offline time yet
        /// (a save would throw it away), and a fresh one has not been played.
        /// </summary>
        bool CanSave => _sim != null && !_hud.TitleShowing;

        void Quit()
        {
            if (CanSave) Save(background: false, announce: false);
            _quitSaved = true;
            _settingsStore.Save(_settings);
            _platform.Flush();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnApplicationQuit()
        {
            if (_flight != null) FinishFlight(); // the flight was confirmed: found the new colony before saving
            _platform?.Flush();
            if (_quitSaved || _sim == null || !CanSave) return;
            Save(background: false, announce: false);
            _quitSaved = true;
        }

        void OnApplicationFocus(bool focus) => _focused = focus;

        /// <summary>Settings → Erase all progress: no save, no bloodline, no local achievements; a fresh colony behind the title.</summary>
        void EraseProgress()
        {
            try
            {
                _store.Delete();
            }
            catch (Exception e)
            {
                _hud.ShowToast("Could not delete the save: " + e.Message);
                return;
            }
            if (_platform is LocalPlatform local) local.Reset();
            _achievements.Refresh();
            StartNewColony(NewSeed(), announce: false);
            ShowTitle();
            _hud.ShowToast("All progress was erased.");
        }

        // ---------------------------------------------------------------- title screen and system dialogs

        void ShowTitle()
        {
            _tools?.CancelTargeting();
            if (_sim != null && _sim.PossessedAnt >= 0) _sim.EnqueueRelease();
            string status = null;
            if (!_freshColony && _sim != null)
            {
                string saved = _lastSaved.HasValue ? $"saved {HudText.Duration((DateTime.UtcNow - _lastSaved.Value).TotalSeconds)} ago" : "";
                status = $"{NumberFormat.Format(_sim.Ants.Count)} ants, {_sim.Colony.Stats.MaxDepth} cells deep, played {HudText.Duration(_sim.PlayTime)}, {saved}";
                if (_sim.Legacy.Flights > 0) status += $". Generation {_sim.Legacy.Flights + 1}";
            }
            _hud.Title.SetSave(status);
            _hud.SetTitle(true);
            _titleTime = 0f;
        }

        void LeaveTitle()
        {
            _hud.SetTitle(false);
            if (_sim == null) return;
            LookAt(_sim.Colony.QueenCell + new int2(0, 4));
            if (_freshColony) _hud.ShowToast("A new colony begins. Contracts on the right show what to do next.");
            else CreditOffline(announceLoad: true);
        }

        void OpenSettings(bool fromMenu)
        {
            _backToMenu = fromMenu;
            _hud.Settings.Open(_settings);
            _hud.ShowModal(_hud.Settings.Root);
        }

        void OpenAchievements(bool fromMenu)
        {
            _backToMenu = fromMenu;
            _achievements.Update(0f, _sim, force: _sim != null && !_hud.TitleShowing);
            _hud.AchievementsPanel.Refresh(_achievements, _hud.TitleShowing ? null : _sim);
            _hud.ShowModal(_hud.AchievementsPanel.Root);
        }

        void OpenHelp(bool fromMenu)
        {
            _backToMenu = fromMenu;
            _hud.Help.Refresh();
            _hud.ShowModal(_hud.Help.Root);
        }

        bool SystemDialogOpen => _hud.IsShowing(_hud.Settings?.Root) || _hud.IsShowing(_hud.AchievementsPanel?.Root) || _hud.IsShowing(_hud.Help?.Root);

        void CloseSystemDialog()
        {
            if (_hud.IsShowing(_hud.Settings.Root)) _settingsStore.Save(_settings);
            if (_backToMenu && !_hud.TitleShowing) _hud.ShowModal(_hud.Menu.Root);
            else _hud.HideModal();
            _backToMenu = false;
        }

        /// <summary>Applies every setting (called on load and on each change in the settings dialog).</summary>
        void ApplySettings(GameSettings s)
        {
            _audio?.SetVolumes(s.masterVolume, s.musicVolume, s.sfxVolume, s.ambienceVolume, s.uiVolume);
            NumberFormat.PreferScientific = s.scientificNumbers;
            _camera.ShakeScale = s.screenShake;
            _cameraController.EdgePan = s.edgePan;
            if (_pops != null) _pops.Enabled = s.numberPops;
            _autosaveTimer = math.min(_autosaveTimer, AutosaveSeconds);
            ApplyDisplay(s);
            ApplyUiScale();
        }

        void ApplyDisplay(GameSettings s)
        {
            var wanted = (s.windowMode, s.width, s.height, s.vsync, s.frameCap);
            if (wanted == _display) return;
            _display = wanted;
            QualitySettings.vSyncCount = s.vsync ? 1 : 0;
            Application.targetFrameRate = s.vsync || s.frameCap <= 0 ? -1 : s.frameCap;
#if !UNITY_EDITOR
            int w = s.width > 0 ? s.width : Screen.currentResolution.width;
            int h = s.height > 0 ? s.height : Screen.currentResolution.height;
            if (s.windowMode != FullScreenMode.Windowed && s.width <= 0)
            {
                w = Display.main.systemWidth;
                h = Display.main.systemHeight;
            }
            Screen.SetResolution(w, h, s.windowMode);
#endif
        }

        void ApplyUiScale()
        {
            if (_panelSettings == null) return;
            float scale = _settings.EffectiveUiScale(Screen.height);
            if (!Mathf.Approximately(_panelSettings.scale, scale)) _panelSettings.scale = scale;
        }

        // ---------------------------------------------------------------- frame loop

        void Update()
        {
            _platform.Tick();
            if (_sim == null) return;
            float dt = Time.deltaTime;
            double now = Time.timeAsDouble;
            ApplyUiScale();
            if (_flight != null)
            {
                UpdateFlight(dt);
                UpdateAudio(dt, MusicMode.Flight);
                return;
            }

            HandleSystemKeys();
            bool title = _hud.TitleShowing;
            bool paused = title || _overlayOpen || _hud.IsShowing(_hud.Menu?.Root) || SystemDialogOpen;

            bool possessing = _tools.Possessing;
            if (title) DriftTitleCamera(dt);
            else
            {
                _cameraController.Update(dt, allowPan: !possessing);
                if (possessing) FollowPossessed(dt);
            }
            _camera.Apply(dt, _worldSize);
            float2 pointerWorld = _camera.ScreenToWorld(_input.Pointer);
            _pointerCell = WorldGrid.WorldToCell(pointerWorld);

            bool overUi = _hud.IsPointerOverUi(_input.Pointer);
            if (!paused) _tools.Update(_sim, _input, pointerWorld, overUi, dt, now);
            if (!title)
            {
                if (_input.Overlay.WasPressedThisFrame()) ToggleOverlay();
                if (_input.ToggleHud.WasPressedThisFrame()) _debugHud.Visible = !_debugHud.Visible;
                if (Debug.isDebugBuild) DebugKeys();
            }

            if (!paused) _sim.Advance(dt * DebugSpeeds[_speed]);
            _sim.UpdateDerived();
            _terrain.Upload();
            float alpha = _sim.Clock.Alpha;
            _buildingView.Render(_sim, alpha, Time.time, _overlay);
            _colonyView.Render(_sim, alpha, _overlay, Time.time);

            var events = _sim.Events;
            _camera.AddTrauma(_feedback.Consume(events) + EventShake(events));
            _feedback.Machines(_sim, dt, paused);
            _pops.Consume(events, _sim);
            _pops.Render(_sim, dt);
            _audio?.Consume(events);
            _tools.Consume(events, now);
            _hud.Consume(events, _sim);
            _hud.Update(dt, _sim, _tools.TargetingAbility, _hud.IsShowing(_hud.Menu?.Root) ? MenuStatus() : null, _tools.Possessing, _tools.TargetingPossess);
            if (_hud.IsShowing(_hud.AchievementsPanel.Root)) _hud.AchievementsPanel.Refresh(_achievements, title ? null : _sim);
            _hoverInfo.Update(dt, _sim, WorldGrid.WorldToCell(pointerWorld), _tools, overUi);
            _hud.SetInfo(title ? null
                : _tools.TargetingAbility >= 0 ? _targetingHints[_tools.TargetingAbility]
                : _tools.TargetingPossess ? "Click an ant to take control of it. Right click or Esc cancels."
                : overUi && _hud.HoverTip != null ? _hud.HoverTip
                : _hoverInfo.Text);
            UpdatePossessLight(alpha);
            _debugHud.Update(dt, _sim, pointerWorld, _camera.Zoom, _tools);
            UpdateAudio(dt, title ? MusicMode.Title
                : _hud.IsShowing(_hud.Genetics?.Root) ? MusicMode.Genetics
                : _sim.Enemies.ActiveRaid != 0 ? MusicMode.Raid
                : MusicMode.Play);
            events.Flush();
            if (!title) _achievements.Update(dt, _sim);

            if (!paused && AutosaveSeconds > 0f)
            {
                _autosaveTimer -= dt;
                if (_autosaveTimer <= 0f) Save(background: true, announce: false);
            }
            string error = _store.TakeError();
            if (error != null) _hud.ShowToast("Saving failed: " + error);
        }

        void UpdateAudio(float dt, MusicMode music)
        {
            if (_audio == null) return;
            _audio.SetView(_camera.Position, _camera.HalfViewSize, _camera.StepsOut);
            int2 cell = WorldGrid.WorldToCell(_camera.Position);
            int depth = _sim.World.Depth(cell.x, cell.y);
            _audio.Update(dt, depth < 0 ? -1 : _sim.BiomeAtDepth(depth), _settings.muteInBackground && !_focused, music);

            // The first dig into a deeper biome gets the discovery cue.
            int reached = _sim.BiomeAtDepth(_sim.Colony.Stats.MaxDepth);
            if (reached > _biomeReached)
            {
                if (_biomeReached >= 0 && sounds != null) _audio.PlayMusicCue(sounds.music.discoveryCue);
                _biomeReached = reached;
            }
        }

        void DebugKeys()
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
            if (_input.GameSpeed.WasPressedThisFrame())
            {
                _speed = (_speed + 1) % DebugSpeeds.Length;
                _hud.ShowToast($"Game speed x{DebugSpeeds[_speed]} (dev builds only, F4).");
            }
        }

        /// <summary>Behind the title the camera drifts slowly over the nest.</summary>
        void DriftTitleCamera(float dt)
        {
            _titleTime += dt;
            var home = WorldGrid.CellCenterToWorld(_sim.Colony.QueenCell + new int2(0, 6));
            _camera.Position = home + new float2(math.sin(_titleTime * 0.05f) * 14f, math.sin(_titleTime * 0.031f) * 3f);
        }

        void HandleSystemKeys()
        {
            if (_hud.Settings.Rebinding) return;
            if (_input.Menu.WasPressedThisFrame())
            {
                if (_tools.CancelTargeting()) { }
                else if (SystemDialogOpen) CloseSystemDialog();
                else if (_hud.ModalOpen) _hud.HideModal();
                else if (_hud.TitleShowing) { }
                else if (_tools.Possessing) _sim.EnqueueRelease();
                else _hud.ShowModal(_hud.Menu?.Root);
            }
            if (_input.Help.WasPressedThisFrame())
            {
                if (_hud.IsShowing(_hud.Help.Root)) CloseSystemDialog();
                else if (!_hud.ModalOpen || _hud.IsShowing(_hud.Menu.Root)) OpenHelp(fromMenu: _hud.IsShowing(_hud.Menu.Root));
            }
            if (_hud.TitleShowing) return;
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
            else if (!_hud.IsShowing(_hud.Menu.Root) && !SystemDialogOpen) _hud.ShowModal(_hud.Research.Root);
        }

        void ToggleGenetics()
        {
            if (_hud.Genetics == null || _sim == null) return;
            if (_hud.IsShowing(_hud.Genetics.Root)) _hud.HideModal();
            else if (!_hud.IsShowing(_hud.Menu.Root) && !SystemDialogOpen && (_sim.Legacy.Awakened || _hud.FlightKnown(_sim))) _hud.ShowModal(_hud.Genetics.Root);
        }

        void ToggleFlight()
        {
            if (_hud.Flight == null || _sim == null) return;
            if (_hud.IsShowing(_hud.Flight.Root)) _hud.HideModal();
            else if (!_hud.IsShowing(_hud.Menu.Root) && !SystemDialogOpen && _hud.FlightKnown(_sim))
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
            _audio?.Play(SoundCue.Flight);
            if (sounds != null) _audio?.PlayMusicCue(sounds.music.flightCue);
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
            _achievements.Update(0f, _sim, force: true);
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
            _audio?.Dispose();
            _platform?.Dispose();
            if (_panelSettings != null) Destroy(_panelSettings);
            _content?.Dispose();
        }
    }
}
