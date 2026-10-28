using System;
using System.Text;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Binds the Hud.uxml layout: resource bar with income rates, alerts, toolbar, build palette, the hover
    /// info panel and short toasts. Text refreshes a few times per second to avoid per-frame garbage.
    /// </summary>
    public sealed class HudController
    {
        const float RefreshInterval = 0.2f;
        const float ToastSeconds = 2.5f;

        readonly UIDocument _document;
        readonly ContentDB _content;
        readonly RenderConfig _render;
        readonly VisualElement _resources;
        readonly VisualElement _palette;
        readonly Label _alert, _info, _toast;
        readonly Button[] _toolButtons = new Button[5];
        readonly VisualElement _hudRoot;
        readonly GameInput _input;
        /// <summary>Buttons whose label starts with a hotkey: refreshed when bindings change.</summary>
        readonly System.Collections.Generic.List<(Button Button, InputAction Action, string Name)> _keyed = new();
        int _keysVersion = -1;
        Button _hovered;
        readonly VisualElement _scentPalette;
        readonly Button[] _scentButtons = new Button[Pheromones.Count];
        readonly Button _possessButton, _castesButton;
        readonly Button _overlayButton;
        readonly Label _food, _foodRate, _ants, _eggs, _storage;
        readonly VisualElement _storageEntry;
        readonly VisualElement[] _itemEntries;
        readonly Label[] _itemLabels, _itemRates;
        readonly Button[] _buildingButtons;
        readonly Label[] _buildingCosts;
        readonly bool[] _buildingLockShown;
        readonly StringBuilder _sb = new();
        readonly Label _knowledge, _knowledgeRate;
        readonly Label _princesses, _memory;
        readonly VisualElement _princessEntry, _memoryEntry;
        readonly VisualElement _modal;
        readonly Button _researchButton, _geneticsButton, _flightButton;
        /// <summary>Buildings whose recipes raise princesses.</summary>
        readonly bool[] _raisesPrincesses;
        /// <summary>Per building: name of the research that unlocks it (null if it starts unlocked).</summary>
        readonly string[] _unlockedBy;
        float _caveInToastCooldown;

        public CastesPanel Castes { get; }
        public ContractsPanel Contracts { get; }
        public EventFeed EventFeed { get; }
        public AbilityBar Abilities { get; }
        public ResearchPanel Research { get; }
        public MenuDialog Menu { get; }
        public WelcomeBackDialog WelcomeBack { get; }
        public GeneticPanel Genetics { get; }
        public FlightDialog Flight { get; }
        public SettingsDialog Settings { get; }
        public AchievementsDialog AchievementsPanel { get; }
        public HelpDialog Help { get; }
        public TitleScreen Title { get; }
        /// <summary>A button was clicked / hovered, a dialog opened or closed, a toast appeared (for interface sounds).</summary>
        public event Action UiClicked, UiHovered, ModalOpened, ModalClosed, ToastShown;
        /// <summary>Help text of the HUD element under the pointer, or null.</summary>
        public string HoverTip { get; private set; }
        public event Action ResearchToggled;
        public event Action GeneticsToggled;
        public event Action FlightToggled;
        float _timer;
        float _toastTimer;

        public Label DebugLabel { get; }
        public event Action<ToolMode> ToolClicked;
        public event Action<int> BuildingClicked;
        public event Action OverlayClicked;
        public event Action<PheromoneKind> ScentClicked;
        public event Action PossessClicked;
        public event Action CastesClicked;

        public HudController(UIDocument document, ContentDB content, RenderConfig render, float autosaveSeconds, GameInput input)
        {
            _document = document;
            _content = content;
            _render = render;
            _input = input;
            if (document == null || document.rootVisualElement == null) return;

            var root = document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            _resources = root.Q<VisualElement>("resources");
            _alert = root.Q<Label>("alert");
            _info = root.Q<Label>("info");
            _toast = root.Q<Label>("toast");
            _palette = root.Q<VisualElement>("build-palette");
            _scentPalette = root.Q<VisualElement>("scent-palette");
            var castes = root.Q<VisualElement>("castes");
            DebugLabel = root.Q<Label>("debug");
            var toolbar = root.Q<VisualElement>("toolbar");
            var contracts = root.Q<VisualElement>("contracts");
            var events = root.Q<VisualElement>("events");
            var abilities = root.Q<VisualElement>("abilities");
            _modal = root.Q<VisualElement>("modal");
            _hudRoot = root.Q<VisualElement>("hud");
            if (_resources == null || toolbar == null || _palette == null || _info == null || _toast == null
                || contracts == null || events == null || abilities == null || _modal == null || _scentPalette == null || castes == null)
            {
                Debug.LogError("HUD layout is missing elements; assign Hud.uxml to the UIDocument.");
                return;
            }

            _food = AddEntry(new Color(0.55f, 0.85f, 0.35f), out _, out _foodRate);
            _ants = AddEntry(new Color(0.65f, 0.3f, 0.2f), out _, out _);
            _eggs = AddEntry(new Color(0.94f, 0.9f, 0.8f), out _, out _);
            _storage = AddEntry(new Color(0.3f, 0.75f, 0.85f), out _storageEntry, out _);
            _knowledge = AddEntry(new Color(0.8f, 0.74f, 1f), out var knowledgeEntry, out _knowledgeRate);
            knowledgeEntry.AddToClassList("resource--knowledge");
            _princesses = AddEntry(new Color(1f, 0.75f, 0.4f), out _princessEntry, out _);
            _princessEntry.AddToClassList("hidden");
            _memory = AddEntry(new Color(1f, 0.85f, 0.45f), out _memoryEntry, out _);
            _memoryEntry.AddToClassList("resource--memory");
            _memoryEntry.AddToClassList("hidden");
            _itemEntries = new VisualElement[content.Items.Length];
            _itemLabels = new Label[content.Items.Length];
            _itemRates = new Label[content.Items.Length];
            for (int i = 0; i < content.Items.Length; i++)
            {
                if (content.Items[i].IsFood) continue;
                _itemLabels[i] = AddEntry(content.Items[i].color, out _itemEntries[i], out _itemRates[i]);
                _itemEntries[i].AddToClassList("hidden");
            }

            AddTool(toolbar, ToolMode.Dig, "Dig", input.ToolDig, "Dig: hold the mouse on rock to dig it out yourself. Combos dig faster.");
            AddTool(toolbar, ToolMode.Mine, "Mine", input.ToolMine, "Mine orders: paint rock for workers to dig. Right drag erases.");
            AddTool(toolbar, ToolMode.Storage, "Storage", input.ToolStorage, "Storage zones: paint dug-out cells where workers store items.");
            AddTool(toolbar, ToolMode.Build, "Build", input.ToolBuild, "Build: belts, lifts, machines and defenses. Q / E pick, R rotates.");
            AddTool(toolbar, ToolMode.Pheromone, "Scent", input.ToolPheromone, "Scent: paint pheromones that steer the ants. They fade unless an emitter keeps them.");
            _possessButton = Toggle(toolbar, "Possess", () => PossessClicked?.Invoke(), input.Possess, "Possess: take control of an ant and dig, carry and fight yourself.");
            _castesButton = Toggle(toolbar, "Castes", () => CastesClicked?.Invoke(), input.Castes, "Castes: choose which kinds of ants the queen lays.");
            _castesButton.AddToClassList("hidden");
            _researchButton = Toggle(toolbar, "Research", () => ResearchToggled?.Invoke(), input.Research, "Research: spend knowledge on new buildings, castes, abilities and upgrades.");
            _overlayButton = Toggle(toolbar, "Flow", () => OverlayClicked?.Invoke(), input.Overlay, "Flow overlay: belt traffic, jams, machine status and idle workers.");
            _geneticsButton = Toggle(toolbar, "Genes", () => GeneticsToggled?.Invoke(), input.Genetics, "Genetic Memory: permanent traits for every colony of your bloodline.");
            _geneticsButton.AddToClassList("hidden");
            _flightButton = Toggle(toolbar, "Flight", () => FlightToggled?.Invoke(), input.Flight, "Nuptial Flight: send princesses to found a new colony and earn Genetic Memory.");
            _flightButton.AddToClassList("hidden");
            _raisesPrincesses = new bool[content.Buildings.Length];
            for (int i = 0; i < content.Buildings.Length; i++)
                foreach (int r in content.Buildings[i].Recipes)
                    if (content.Recipes[r].Princesses > 0) _raisesPrincesses[i] = true;
            Abilities = new AbilityBar(abilities, content, input.Abilities);
            for (int k = 0; k < Pheromones.Count; k++) _scentButtons[k] = AddScent((PheromoneKind)k);
            Castes = new CastesPanel(castes, content);

            _unlockedBy = new string[content.Buildings.Length];
            foreach (var r in content.Research)
                foreach (int b in r.UnlockBuildings) _unlockedBy[b] = r.Def.DisplayName;
            _buildingButtons = new Button[content.Buildings.Length];
            _buildingCosts = new Label[content.Buildings.Length];
            _buildingLockShown = new bool[content.Buildings.Length];
            for (int i = 0; i < content.Buildings.Length; i++)
                _buildingButtons[i] = AddBuilding(i);

            Contracts = new ContractsPanel(contracts, content);
            EventFeed = new EventFeed(events, content);
            Research = new ResearchPanel(content);
            Menu = new MenuDialog(autosaveSeconds);
            WelcomeBack = new WelcomeBackDialog(content);
            Genetics = new GeneticPanel(content);
            Flight = new FlightDialog(content);
            Settings = new SettingsDialog(input);
            AchievementsPanel = new AchievementsDialog(content);
            Help = new HelpDialog(input);
            Title = new TitleScreen();
            Title.Root.AddToClassList("hidden");
            _hudRoot?.Insert(_hudRoot.IndexOf(_modal), Title.Root);

            // Interface sounds: every button click and hover, wherever it is.
            root.RegisterCallback<ClickEvent>(e =>
            {
                if (e.target is VisualElement v && v.GetFirstOfType<Button>() != null) UiClicked?.Invoke();
            }, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerOverEvent>(e =>
            {
                var button = (e.target as VisualElement)?.GetFirstOfType<Button>();
                if (button == _hovered) return;
                _hovered = button;
                if (button != null && button.enabledInHierarchy) UiHovered?.Invoke();
            }, TrickleDown.TrickleDown);
            SetActiveTool(ToolMode.Dig);
            SetSelectedBuilding(0);
        }

        Label AddEntry(Color swatchColor, out VisualElement entry, out Label rate)
        {
            entry = new VisualElement();
            entry.AddToClassList("resource");
            var swatch = new VisualElement();
            swatch.AddToClassList("resource__swatch");
            swatch.style.backgroundColor = swatchColor;
            var label = new Label();
            label.AddToClassList("resource__label");
            rate = new Label();
            rate.AddToClassList("resource__rate");
            entry.Add(swatch);
            entry.Add(label);
            entry.Add(rate);
            _resources.Add(entry);
            return label;
        }

        void AddTool(VisualElement toolbar, ToolMode mode, string name, InputAction action, string tip)
        {
            var button = new Button(() => ToolClicked?.Invoke(mode)) { focusable = false };
            button.AddToClassList("tool");
            button.AddToClassList("tool--narrow");
            toolbar.Add(button);
            _toolButtons[(int)mode] = button;
            _keyed.Add((button, action, name));
            Tip(button, tip);
        }

        Button Toggle(VisualElement toolbar, string name, Action clicked, InputAction action, string tip)
        {
            var button = new Button(clicked) { focusable = false };
            button.AddToClassList("tool");
            button.AddToClassList("tool--narrow");
            button.AddToClassList("tool--toggle");
            toolbar.Add(button);
            _keyed.Add((button, action, name));
            Tip(button, tip);
            return button;
        }

        /// <summary>Shows <paramref name="tip"/> in the info panel while the pointer is over <paramref name="element"/>.</summary>
        public void Tip(VisualElement element, string tip)
        {
            element.RegisterCallback<PointerEnterEvent>(_ => HoverTip = tip);
            element.RegisterCallback<PointerLeaveEvent>(_ => { if (HoverTip == tip) HoverTip = null; });
        }

        /// <summary>"[key] Name" labels follow the current key bindings.</summary>
        void RefreshKeyLabels()
        {
            if (_input == null || _keysVersion == _input.BindingsVersion) return;
            _keysVersion = _input.BindingsVersion;
            foreach (var (button, action, name) in _keyed)
                button.text = KeyLabel(action, name);
            Abilities?.RefreshKeys();
        }

        /// <summary>The key in a muted colour (rich text), then the name.</summary>
        public static string KeyLabel(InputAction action, string name)
        {
            string key = GameInput.KeyName(action);
            return string.IsNullOrEmpty(key) ? name : $"<color=#a89070>{key}</color> {name}";
        }

        static readonly string[] ScentNames = { "Trail", "Dig here", "Gather", "Defend", "Avoid" };
        static readonly string[] ScentHints = { "faster, preferred paths", "these orders first", "these piles first", "soldiers patrol", "ants keep out" };

        /// <summary>Display name of a pheromone.</summary>
        public static string ScentName(PheromoneKind kind) => ScentNames[(int)kind];

        Button AddScent(PheromoneKind kind)
        {
            var button = new Button(() => ScentClicked?.Invoke(kind)) { focusable = false };
            button.AddToClassList("building");
            button.AddToClassList("scent");
            var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
            swatch.AddToClassList("building__swatch");
            var colors = _render.pheromoneColors;
            swatch.style.backgroundColor = colors != null && (int)kind < colors.Length ? colors[(int)kind] : Color.white;
            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("building__text");
            var name = new Label(ScentNames[(int)kind]) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("building__name");
            var hint = new Label(ScentHints[(int)kind]) { pickingMode = PickingMode.Ignore };
            hint.AddToClassList("building__cost");
            text.Add(name);
            text.Add(hint);
            button.Add(swatch);
            button.Add(text);
            _scentPalette.Add(button);
            return button;
        }

        public void SetSelectedScent(PheromoneKind kind)
        {
            for (int k = 0; k < _scentButtons.Length; k++)
                _scentButtons[k]?.EnableInClassList("building--active", k == (int)kind);
        }

        Button AddBuilding(int index)
        {
            var def = _content.Buildings[index].Def;
            var button = new Button(() => BuildingClicked?.Invoke(index)) { focusable = false };
            button.AddToClassList("building");
            var swatch = new VisualElement();
            swatch.AddToClassList("building__swatch");
            swatch.style.backgroundColor = def.color;
            var text = new VisualElement();
            text.AddToClassList("building__text");
            text.pickingMode = PickingMode.Ignore;
            var name = new Label(def.DisplayName) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("building__name");
            var cost = new Label(CostText(_content.Buildings[index])) { pickingMode = PickingMode.Ignore };
            cost.AddToClassList("building__cost");
            _buildingCosts[index] = cost;
            text.Add(name);
            text.Add(cost);
            swatch.pickingMode = PickingMode.Ignore;
            button.Add(swatch);
            button.Add(text);
            _palette.Add(button);
            return button;
        }

        public string CostText(BuildingData data)
        {
            _sb.Clear();
            for (int i = 0; i < data.CostItems.Length; i++)
            {
                if (_sb.Length > 0) _sb.Append(", ");
                _sb.Append(data.CostCounts[i]).Append(' ').Append(_content.Items[data.CostItems[i]].DisplayName);
            }
            if (data.Def.foodCost > 0f)
            {
                if (_sb.Length > 0) _sb.Append(", ");
                _sb.Append(NumberFormat.Format(data.Def.foodCost)).Append(" Food");
            }
            return _sb.Length > 0 ? _sb.ToString() : "Free";
        }

        public void SetActiveTool(ToolMode mode)
        {
            for (int i = 0; i < _toolButtons.Length; i++)
                _toolButtons[i]?.EnableInClassList("tool--active", i == (int)mode);
            _palette?.EnableInClassList("hidden", mode != ToolMode.Build);
            _scentPalette?.EnableInClassList("hidden", mode != ToolMode.Pheromone);
        }

        public void SetSelectedBuilding(int index)
        {
            if (_buildingButtons == null) return;
            for (int i = 0; i < _buildingButtons.Length; i++)
                _buildingButtons[i].EnableInClassList("building--active", i == index);
        }

        public void SetOverlay(bool on) => _overlayButton?.EnableInClassList("tool--on", on);

        // ---------------------------------------------------------------- modal layer (menu, research, welcome back)

        public bool ModalOpen => _modal != null && !_modal.ClassListContains("hidden");

        public bool IsShowing(VisualElement panel) => panel != null && ModalOpen && panel.parent == _modal;

        public void ShowModal(VisualElement panel)
        {
            if (_modal == null || panel == null) return;
            bool was = ModalOpen;
            _modal.Clear();
            _modal.Add(panel);
            _modal.RemoveFromClassList("hidden");
            // Pop in: start small and transparent, let the USS transition run from the next frame.
            panel.AddToClassList("pop-enter");
            panel.schedule.Execute(() => panel.RemoveFromClassList("pop-enter")).StartingIn(16);
            HoverTip = null;
            if (!was) ModalOpened?.Invoke();
        }

        public void HideModal()
        {
            if (_modal == null) return;
            bool was = ModalOpen;
            Settings?.CancelRebind();
            _modal.Clear();
            _modal.AddToClassList("hidden");
            if (was) ModalClosed?.Invoke();
        }

        public bool TitleShowing => Title != null && !Title.Root.ClassListContains("hidden");

        /// <summary>Shows the title screen (the HUD hides behind it) or hides it.</summary>
        public void SetTitle(bool show)
        {
            if (Title == null) return;
            Title.Root.EnableInClassList("hidden", !show);
            _hudRoot?.EnableInClassList("hud--title", show);
            HoverTip = null;
            if (show) HideModal();
        }

        /// <summary>A new colony or a loaded save: show its own event cards, close dialogs.</summary>
        public void ResetSession(Simulation sim)
        {
            EventFeed?.Reset(sim);
            HideModal();
        }

        /// <summary>Hover / selection details; null hides the panel.</summary>
        public void SetInfo(string text)
        {
            if (_info == null) return;
            _info.EnableInClassList("hidden", string.IsNullOrEmpty(text));
            if (!string.IsNullOrEmpty(text)) _info.text = text;
        }

        public void ShowToast(string text)
        {
            if (_toast == null) return;
            _toast.text = text;
            if (_toast.ClassListContains("hidden"))
            {
                _toast.RemoveFromClassList("hidden");
                _toast.AddToClassList("toast--enter");
                _toast.schedule.Execute(() => _toast.RemoveFromClassList("toast--enter")).StartingIn(16);
            }
            _toastTimer = ToastSeconds;
            ToastShown?.Invoke();
        }

        /// <summary>True when the pointer is over an interactive HUD element.</summary>
        public bool IsPointerOverUi(Vector2 screenPosition)
        {
            var panel = _document != null ? _document.rootVisualElement?.panel : null;
            if (panel == null) return false;
            var panelPos = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            return panel.Pick(panelPos) != null;
        }

        /// <summary>Turns refused placements, research, abilities, contracts and random events into toasts and cards.</summary>
        public void Consume(EventBus events, Simulation sim)
        {
            if (_resources == null) return;
            var failed = events.Read<PlaceFailedEvent>();
            if (failed.Length > 0) ShowToast(PlaceMessage(failed[failed.Length - 1].Result, failed[failed.Length - 1].Def, sim));

            foreach (var e in events.Read<ResearchCompletedEvent>())
            {
                var data = _content.Research[e.Research];
                _sb.Clear().Append("Researched ").Append(data.Def.DisplayName).Append('.');
                foreach (int b in data.UnlockBuildings) _sb.Append(' ').Append(_content.Buildings[b].Def.DisplayName).Append(" unlocked (4).");
                foreach (int a in data.UnlockAbilities) _sb.Append(' ').Append(_content.Abilities[a].DisplayName).Append(" unlocked.");
                foreach (var caste in data.UnlockCastes)
                    _sb.Append(" The queen now lays ").Append(_content.Caste(caste).DisplayName.ToLowerInvariant())
                        .Append("s: set their share in the castes panel (K).");
                ShowToast(_sb.ToString());
            }
            foreach (var e in events.Read<ResearchFailedEvent>())
            {
                if (e.Research < 0 || e.Research >= _content.Research.Length) continue;
                string name = _content.Research[e.Research].Def.DisplayName;
                ShowToast(e.Result switch
                {
                    ResearchResult.NeedsPrerequisites => $"{name} needs the research before it first.",
                    ResearchResult.CantAfford => $"Not enough for {name} yet.",
                    _ => $"{name} is already researched.",
                });
            }
            foreach (var e in events.Read<AbilityUsedEvent>())
            {
                var def = _content.Abilities[e.Ability];
                ShowToast(def.kind switch
                {
                    AbilityKind.Frenzy => $"Frenzy! Workers are {Mathf.RoundToInt(def.magnitude * 100f)}% faster for {Mathf.RoundToInt(def.duration)} seconds.",
                    AbilityKind.SeismicSense => e.Amount > 0 ? $"Seismic Sense revealed {e.Amount} hidden finds." : "Seismic Sense found nothing new here.",
                    AbilityKind.RoyalDecree => $"Royal Decree: the queen laid {e.Amount} eggs.",
                    AbilityKind.PheromoneBurst => $"Pheromone Burst: {e.Amount} {(e.Amount == 1 ? "ant rushes" : "ants rush")} to the spot.",
                    _ => def.DisplayName,
                });
            }
            foreach (var e in events.Read<AbilityFailedEvent>())
            {
                string name = e.Ability >= 0 && e.Ability < _content.Abilities.Length ? _content.Abilities[e.Ability].DisplayName : "That ability";
                ShowToast(e.Result switch
                {
                    AbilityResult.Locked => $"{name} is locked: unlock it with research (T).",
                    AbilityResult.Cooling => $"{name} is still recharging ({HudText.Duration(sim.Abilities.Cooldown[e.Ability])}).",
                    AbilityResult.CantAfford => $"Not enough food for {name}.",
                    AbilityResult.NoEffect when e.Ability < _content.Abilities.Length && _content.Abilities[e.Ability].kind == AbilityKind.PheromoneBurst =>
                        $"No ants close enough to answer the {name}.",
                    AbilityResult.NoEffect => $"{name} has no effect right now.",
                    _ => "Can't use that.",
                });
            }
            foreach (var e in events.Read<GeneticBoughtEvent>())
            {
                var data = _content.Genetics[e.Trait];
                _sb.Clear().Append(data.Def.DisplayName);
                if (data.MaxLevel > 1) _sb.Append(" level ").Append(e.Level);
                _sb.Append(" is part of your bloodline now.");
                bool startOnly = true;
                foreach (var effect in data.Effects)
                    startOnly &= effect.kind is GeneticEffectKind.StartWorkers or GeneticEffectKind.StartFood or GeneticEffectKind.StartKnowledge
                        or GeneticEffectKind.StartItems or GeneticEffectKind.RevealRadius;
                if (startOnly) _sb.Append(" It takes effect in the next colony.");
                ShowToast(_sb.ToString());
            }
            foreach (var e in events.Read<GeneticFailedEvent>())
            {
                if (e.Trait < 0 || e.Trait >= _content.Genetics.Length) continue;
                string name = _content.Genetics[e.Trait].Def.DisplayName;
                ShowToast(e.Result switch
                {
                    GeneticResult.NeedsPrerequisites => $"{name} needs the trait before it first.",
                    GeneticResult.CantAfford => $"Not enough Genetic Memory for {name}.",
                    _ => $"{name} is already at its highest level.",
                });
            }
            foreach (var e in events.Read<PrincessRaisedEvent>())
                ShowToast(e.Total >= sim.Colony.PrincessCap
                    ? $"A princess is ready: {e.Total} wait for the Nuptial Flight, as many as count. Open the flight screen (N)."
                    : $"A princess is ready ({e.Total}/{sim.Colony.PrincessCap}). Each one raises the Genetic Memory of a Nuptial Flight (N).");
            foreach (var e in events.Read<ContractClaimedEvent>())
                ShowToast($"Contract complete: {_content.Contracts[e.Contract].Def.DisplayName}.");
            if (events.Read<GateBrokenEvent>().Length > 0) ShowToast("Enemies broke a gate! Workers will repair it once it is safe.");
            if (events.Read<CaveInEvent>().Length > 0 && _caveInToastCooldown <= 0f)
            {
                ShowToast("Cave-in! Fallen sand is marked for digging.");
                _caveInToastCooldown = 15f;
            }
            string deaths = DeathToast(events.Read<AntDiedEvent>());
            if (deaths != null) ShowToast(deaths);
            foreach (var e in events.Read<PossessionEvent>())
            {
                string caste = _content.Caste(e.Caste).DisplayName.ToLowerInvariant();
                ShowToast(e.Phase switch
                {
                    PossessionPhase.Started => $"You control a {caste}. WASD: move. Left mouse: dig / bite. Right mouse or Space: pick up / drop. F: let go.",
                    PossessionPhase.Died => $"Your {caste} died. You are the colony's mind again.",
                    PossessionPhase.Missed => "No ant there. Point at an ant and press F.",
                    _ => $"You let go of the {caste}.",
                });
            }
            string eventToast = EventFeed.Consume(events);
            if (eventToast != null) ShowToast(eventToast);
        }

        /// <summary>"A worker drowned." / "3 ants were killed by Cave Spiders." for this frame's deaths, or null.</summary>
        string DeathToast(ReadOnlySpan<AntDiedEvent> deaths)
        {
            if (deaths.Length == 0) return null;
            var first = deaths[0];
            int same = 0;
            bool mixedCaste = false;
            foreach (var d in deaths)
            {
                if (d.Cause != first.Cause || d.Killer != first.Killer) continue;
                same++;
                mixedCaste |= d.Caste != first.Caste;
            }
            string who = mixedCaste ? "ant" : _content.Caste(first.Caste).DisplayName.ToLowerInvariant();
            string subject = same == 1 ? "A " + who : $"{same} {who}s";
            string verb = same == 1 ? "was" : "were";
            string text = first.Cause switch
            {
                DeathCause.Drowned => $"{subject} drowned.",
                DeathCause.Burned => $"{subject} burned in lava.",
                DeathCause.Gassed => $"{subject} choked on gas.",
                DeathCause.Crushed => $"{subject} {verb} buried by a cave-in.",
                DeathCause.Killed when first.Killer >= 0 && first.Killer < _content.Enemies.Length =>
                    $"{subject} {verb} killed by {(same == 1 ? HudText.Plural(1, _content.Enemies[first.Killer].Def.DisplayName) : "enemies")}.",
                _ => $"{subject} died.",
            };
            if (deaths.Length > same) text += $" ({deaths.Length - same} more lost.)";
            return text;
        }

        public string PlaceMessage(PlaceResult result, int def, Simulation sim)
        {
            var data = def >= 0 && def < _content.Buildings.Length ? _content.Buildings[def] : null;
            switch (result)
            {
                case PlaceResult.Hidden: return "You can only build where you can see.";
                case PlaceResult.Flooded: return "Can't build in water, gas or lava.";
                case PlaceResult.Protected: return "Walls can't go into the royal chamber or a storage zone.";
                case PlaceResult.NotOpen:
                    if (data != null && data.Def.IsChamber) return "Machines and nurseries need dug-out space underground.";
                    return data != null && data.Def.IsDefense ? "Needs open space ants can walk on." : "Belts need dug-out space or the ground surface.";
                case PlaceResult.Occupied: return "Something is already built there.";
                case PlaceResult.NeedsFloor:
                    return data != null && data.Def.kind == BuildingKind.Trap ? "Traps need a solid floor under them." : "Machines need a flat, solid floor under their whole width.";
                case PlaceResult.OutOfBounds: return "That is outside the world.";
                case PlaceResult.Locked:
                    return data != null ? $"{data.Def.DisplayName} is locked: research {_unlockedBy[def] ?? "it"} first (T)." : "Locked.";
                case PlaceResult.CantAfford:
                {
                    if (data == null) return "Not enough resources.";
                    var inv = sim.Colony.Inventory;
                    for (int i = 0; i < data.CostItems.Length; i++)
                        if (inv.Items[data.CostItems[i]] < data.CostCounts[i])
                            return $"Not enough {_content.Items[data.CostItems[i]].DisplayName} ({inv.Items[data.CostItems[i]]}/{data.CostCounts[i]}).";
                    return "Not enough food.";
                }
                default: return "Can't build there.";
            }
        }

        /// <param name="targeting">Ability waiting for a click in the world, or -1.</param>
        /// <param name="menuStatus">Line shown in the Esc menu (seed, play time, last save).</param>
        public void Update(float dt, Simulation sim, int targeting, string menuStatus, bool possessing = false, bool targetingPossess = false)
        {
            RefreshKeyLabels();
            if (TitleShowing) Title.Update(dt);
            if (Settings != null && IsShowing(Settings.Root)) Settings.Update(dt);
            if (_possessButton != null)
            {
                _possessButton.EnableInClassList("tool--possessing", possessing);
                _possessButton.EnableInClassList("tool--targeting", targetingPossess);
                string label = KeyLabel(_input.Possess, possessing ? "Let go" : "Possess");
                if (_possessButton.text != label) _possessButton.text = label;
            }
            if (_resources == null) return;
            if (_toastTimer > 0f)
            {
                _toastTimer -= dt;
                if (_toastTimer <= 0f) _toast.AddToClassList("hidden");
            }
            if (_caveInToastCooldown > 0f) _caveInToastCooldown -= dt;
            _timer -= dt;
            if (_timer > 0f) return;
            _timer = RefreshInterval;

            var colony = sim.Colony;
            var inv = colony.Inventory;
            _food.text = $"Food {NumberFormat.Format(inv.Food)}/{NumberFormat.Format(inv.FoodCapacity)}";
            SetRate(_foodRate, inv.FoodIncomePerMinute - colony.UpkeepPerMinute(sim.AliveByCaste));
            int specialists = sim.Ants.Count - sim.Workers;
            _ants.text = specialists > 0
                ? $"Ants {NumberFormat.Format(sim.Ants.Count)} ({NumberFormat.Format(sim.Workers)} workers)"
                : $"Ants {NumberFormat.Format(sim.Ants.Count)}";
            bool castes = CastesPanel.HasSpecialists(sim);
            _castesButton.EnableInClassList("hidden", !castes);
            _castesButton.EnableInClassList("tool--on", Castes.Visible);
            if (!castes) Castes.Hide();
            Castes.Refresh(sim);
            for (int k = 0; k < _scentButtons.Length; k++)
                _scentButtons[k].EnableInClassList("building--locked", !ToolController.ScentAvailable(sim, (PheromoneKind)k));
            _eggs.text = $"Eggs {colony.Eggs.Count}";
            _storage.text = $"Storage {NumberFormat.Format(inv.Stored)}/{NumberFormat.Format(inv.Capacity)}";
            _storageEntry.EnableInClassList("resource--full", inv.Stored >= inv.Capacity);
            _knowledge.text = $"Knowledge {NumberFormat.Format(inv.Knowledge)}";
            SetRate(_knowledgeRate, colony.Meter.KnowledgePerMinute);

            Contracts.Refresh(sim);
            EventFeed.Update(RefreshInterval, sim);
            Abilities.Refresh(sim, targeting);
            bool researchOpen = IsShowing(Research.Root);
            _researchButton.EnableInClassList("tool--on", researchOpen);
            if (researchOpen) Research.Refresh(sim);
            bool flightKnown = FlightKnown(sim);
            _flightButton.EnableInClassList("hidden", !flightKnown);
            _geneticsButton.EnableInClassList("hidden", !sim.Legacy.Awakened);
            bool geneticsOpen = IsShowing(Genetics.Root), flightOpen = IsShowing(Flight.Root);
            _geneticsButton.EnableInClassList("tool--on", geneticsOpen);
            _flightButton.EnableInClassList("tool--on", flightOpen);
            _flightButton.EnableInClassList("tool--ready", sim.Colony.Princesses > 0 && !flightOpen);
            if (geneticsOpen) Genetics.Refresh(sim);
            if (flightOpen) Flight.Update(RefreshInterval, sim);
            _princessEntry.EnableInClassList("hidden", !flightKnown);
            if (flightKnown) _princesses.text = $"Princesses {colony.Princesses}/{colony.PrincessCap}";
            _memoryEntry.EnableInClassList("hidden", !sim.Legacy.Awakened);
            if (sim.Legacy.Awakened) _memory.text = $"Memory {sim.Legacy.Memory}";
            if (IsShowing(Menu.Root)) Menu.Update(RefreshInterval, menuStatus);

            for (int i = 0; i < _itemLabels.Length; i++)
            {
                if (_itemLabels[i] == null) continue;
                int count = inv.Items[i];
                bool show = count > 0 || inv.InTransit[i] > 0;
                _itemEntries[i].EnableInClassList("hidden", !show);
                if (!show) continue;
                _itemLabels[i].text = $"{_content.Items[i].DisplayName} {NumberFormat.Format(count)}";
                SetRate(_itemRates[i], inv.IncomePerMinute[i]);
            }

            for (int i = 0; i < _buildingButtons.Length; i++)
            {
                bool locked = !colony.BuildingUnlocked[i];
                _buildingButtons[i].EnableInClassList("building--poor", !locked && !inv.CanAfford(_content.Buildings[i]));
                _buildingButtons[i].EnableInClassList("building--locked", locked);
                if (locked == _buildingLockShown[i]) continue;
                _buildingLockShown[i] = locked;
                _buildingCosts[i].text = locked ? "Research: " + (_unlockedBy[i] ?? "?") : CostText(_content.Buildings[i]);
            }

            _sb.Clear();
            if (colony.QueenDisturbed) _sb.Append("Raiders are in the royal chamber! They eat your food and the queen stopped laying.");
            if (colony.Starving)
            {
                if (_sb.Length > 0) _sb.Append('\n');
                _sb.Append("The colony is starving: work is slowed and the queen stopped laying.");
            }
            _alert.EnableInClassList("hidden", _sb.Length == 0);
            if (_sb.Length > 0) _alert.text = _sb.ToString();
        }

        /// <summary>The Nuptial Flight is known: a princess chamber is unlocked, princesses exist, or the colony has flown before.</summary>
        public bool FlightKnown(Simulation sim)
        {
            if (sim.Legacy.Awakened || sim.Colony.Princesses > 0) return true;
            for (int i = 0; i < _raisesPrincesses.Length; i++)
                if (_raisesPrincesses[i] && sim.Colony.BuildingUnlocked[i]) return true;
            return false;
        }

        static void SetRate(Label label, float perMinute)
        {
            bool show = Mathf.Abs(perMinute) >= 0.05f;
            label.EnableInClassList("hidden", !show);
            if (!show) return;
            label.EnableInClassList("resource__rate--negative", perMinute < 0f);
            string value = Mathf.Abs(perMinute) < 10f ? Mathf.Abs(perMinute).ToString("0.0") : NumberFormat.Format(Mathf.Abs(perMinute));
            label.text = (perMinute < 0f ? "-" : "+") + value + "/min";
        }
    }
}
