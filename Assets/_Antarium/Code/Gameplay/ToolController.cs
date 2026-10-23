using System;
using System.Collections.Generic;
using Antarium.Content;
using Antarium.Presentation;
using Antarium.Core;
using Antarium.Sim;
using Unity.Mathematics;
using UnityEngine;

namespace Antarium.Gameplay
{
    public enum ToolMode
    {
        /// <summary>Dig personally at the cursor.</summary>
        Dig,
        /// <summary>Paint mining orders for workers (right button erases).</summary>
        Mine,
        /// <summary>Paint storage zones (right button erases).</summary>
        Storage,
        /// <summary>Place buildings from the palette (right button removes).</summary>
        Build,
        /// <summary>Paint pheromones of the selected kind (right button erases that kind).</summary>
        Pheromone,
    }

    /// <summary>
    /// Routes pointer input to the active tool. A press that starts over the UI never reaches the world. While the
    /// player controls an ant, the pointer and the pan keys drive that ant instead of the tools.
    /// </summary>
    public sealed class ToolController
    {
        readonly DigConfig _config;
        readonly PheromoneConfig _scent;
        readonly PossessionConfig _possession;
        readonly ContentDB _content;
        readonly DigTool _dig;
        readonly BuildTool _build;
        readonly List<BuildGhost> _ghosts;
        bool _strokeBlocked;
        bool _hasLast;
        float2 _lastCellPos;
        float _biteCooldown;

        public ToolMode Mode { get; private set; } = ToolMode.Dig;
        public DigTool Dig => _dig;
        public BuildTool Build => _build;
        public event Action<ToolMode> ModeChanged;
        /// <summary>Ability waiting for the player to click where to use it, or -1.</summary>
        public int TargetingAbility { get; private set; } = -1;
        /// <summary>The Possess button was clicked: the next click in the world picks the ant.</summary>
        public bool TargetingPossess { get; private set; }
        /// <summary>Pheromone the Pheromone tool paints.</summary>
        public PheromoneKind Scent { get; private set; } = PheromoneKind.Trail;
        public event Action<PheromoneKind> ScentChanged;
        /// <summary>The player controls an ant (refreshed every frame).</summary>
        public bool Possessing { get; private set; }

        /// <param name="ghosts">Placement previews to draw; filled while the Build tool is active.</param>
        public ToolController(GameConfig config, ContentDB content, List<BuildGhost> ghosts)
        {
            _config = config.dig;
            _scent = config.pheromones;
            _possession = config.possession;
            _content = content;
            _dig = new DigTool(config.dig);
            _build = new BuildTool(content);
            _ghosts = ghosts;
        }

        public void SetMode(ToolMode mode)
        {
            if (mode == Mode) return;
            Mode = mode;
            _hasLast = false;
            _build.Cancel();
            _ghosts.Clear();
            ModeChanged?.Invoke(mode);
        }

        /// <summary>
        /// Ability bar click: abilities that act on a place (Seismic Sense, Pheromone Burst) wait for a click in the
        /// world; the others are used right away.
        /// </summary>
        public void ClickAbility(Simulation sim, int ability, int2 cursorCell)
        {
            if (ability < 0 || ability >= _content.Abilities.Length) return;
            var kind = _content.Abilities[ability].kind;
            if (kind == AbilityKind.SeismicSense || kind == AbilityKind.PheromoneBurst)
            {
                TargetingAbility = TargetingAbility == ability ? -1 : ability;
                TargetingPossess = false;
                return;
            }
            sim.EnqueueAbility(ability, cursorCell);
        }

        /// <summary>The Possess button: let go of the controlled ant, or wait for a click on one.</summary>
        public void ClickPossess(Simulation sim)
        {
            if (Possessing)
            {
                sim.EnqueueRelease();
                return;
            }
            TargetingPossess = !TargetingPossess;
            TargetingAbility = -1;
        }

        /// <summary>Cancels ability / possess targeting. Returns true if there was something to cancel (Esc).</summary>
        public bool CancelTargeting()
        {
            if (TargetingAbility < 0 && !TargetingPossess) return false;
            TargetingAbility = -1;
            TargetingPossess = false;
            return true;
        }

        /// <summary>Selects a building from the palette (and switches to the Build tool).</summary>
        public void SelectBuilding(int def)
        {
            _build.Select(def);
            SetMode(ToolMode.Build);
        }

        /// <summary>Selects a pheromone from the scent palette (and switches to the Pheromone tool).</summary>
        public void SelectScent(PheromoneKind kind)
        {
            SetMode(ToolMode.Pheromone);
            if (kind == Scent) return;
            Scent = kind;
            ScentChanged?.Invoke(kind);
        }

        /// <summary>Defend scent only matters once there are soldiers.</summary>
        public static bool ScentAvailable(Simulation sim, PheromoneKind kind) => kind != PheromoneKind.Defend || sim.Colony.SoldiersUnlocked;

        void CycleScent(Simulation sim, int delta)
        {
            var kind = Scent;
            for (int i = 0; i < Pheromones.Count; i++)
            {
                kind = (PheromoneKind)(((int)kind + delta + Pheromones.Count) % Pheromones.Count);
                if (ScentAvailable(sim, kind)) break;
            }
            SelectScent(kind);
        }

        public void Update(Simulation sim, GameInput input, float2 pointerWorld, bool pointerOverUi, float dt, double now)
        {
            Possessing = sim.PossessedAnt >= 0;
            if (input.ToolDig.WasPressedThisFrame()) SetMode(ToolMode.Dig);
            if (input.ToolMine.WasPressedThisFrame()) SetMode(ToolMode.Mine);
            if (input.ToolStorage.WasPressedThisFrame()) SetMode(ToolMode.Storage);
            if (input.ToolBuild.WasPressedThisFrame()) SetMode(ToolMode.Build);
            if (input.ToolPheromone.WasPressedThisFrame()) SetMode(ToolMode.Pheromone);
            if (Mode == ToolMode.Build)
            {
                if (input.NextBuilding.WasPressedThisFrame()) _build.Cycle(1);
                if (input.PrevBuilding.WasPressedThisFrame()) _build.Cycle(-1);
                if (input.Rotate.WasPressedThisFrame()) _build.Rotate();
            }
            else if (Mode == ToolMode.Pheromone)
            {
                if (input.NextBuilding.WasPressedThisFrame()) CycleScent(sim, 1);
                if (input.PrevBuilding.WasPressedThisFrame()) CycleScent(sim, -1);
            }
            if (!ScentAvailable(sim, Scent)) SelectScent(PheromoneKind.Trail);

            float2 cellPos = WorldGrid.WorldToCellSpace(pointerWorld);
            int2 cursorCell = (int2)math.floor(cellPos);
            for (int i = 0; i < input.Abilities.Length && i < _content.Abilities.Length; i++)
            {
                if (!input.Abilities[i].WasPressedThisFrame()) continue;
                TargetingAbility = -1;
                sim.EnqueueAbility(i, cursorCell); // hotkeys act at the cursor right away
            }
            if (input.Possess.WasPressedThisFrame() && !pointerOverUi)
            {
                TargetingPossess = false;
                if (Possessing) sim.EnqueueRelease();
                else sim.EnqueuePossess(cellPos);
            }

            bool primary = input.Primary.IsPressed();
            bool secondary = input.Secondary.IsPressed();
            bool pressed = input.Primary.WasPressedThisFrame() || input.Secondary.WasPressedThisFrame();
            if (pressed) _strokeBlocked = pointerOverUi;
            if (!primary && !secondary) _strokeBlocked = false;

            if (pressed && !pointerOverUi)
            {
                // A click aimed at an ability target, an ant to possess, or a critter is not a tool stroke.
                if (TargetingAbility >= 0)
                {
                    if (input.Primary.WasPressedThisFrame()) sim.EnqueueAbility(TargetingAbility, cursorCell);
                    TargetingAbility = -1;
                    _strokeBlocked = true;
                }
                else if (TargetingPossess)
                {
                    if (input.Primary.WasPressedThisFrame()) sim.EnqueuePossess(cellPos);
                    TargetingPossess = false;
                    _strokeBlocked = true;
                }
                else if (input.Primary.WasPressedThisFrame() && sim.RandomEvents.CritterAt(cellPos) >= 0)
                {
                    sim.EnqueueCatch(cellPos);
                    _strokeBlocked = true;
                }
            }
            if (_strokeBlocked) { primary = secondary = false; }

            if (Possessing)
            {
                UpdatePossessed(sim, input, primary, input.Primary.WasPressedThisFrame() && !_strokeBlocked,
                    input.Secondary.WasPressedThisFrame() && !_strokeBlocked, cellPos, pointerWorld, dt, now);
                _ghosts.Clear();
                return;
            }
            sim.PossessInput = int2.zero;

            switch (Mode)
            {
                case ToolMode.Dig:
                    _dig.Update(sim, primary, pressed && !_strokeBlocked, pointerWorld, dt, now);
                    break;
                case ToolMode.Mine:
                case ToolMode.Storage:
                case ToolMode.Pheromone:
                    Paint(sim, primary, secondary, cellPos);
                    break;
                case ToolMode.Build:
                    _build.Update(sim, primary, input.Primary.WasPressedThisFrame() && !_strokeBlocked, secondary, cellPos, _ghosts);
                    if (pointerOverUi && !primary && !secondary) _ghosts.Clear();
                    break;
            }
        }

        /// <summary>
        /// The controlled ant: the pan keys / stick push it, the left button bites toward the cursor (rock or enemy,
        /// building the dig combo), the right button or Space picks up, delivers or drops.
        /// </summary>
        void UpdatePossessed(Simulation sim, GameInput input, bool primary, bool primaryPressed, bool secondaryPressed,
            float2 cellPos, float2 pointerWorld, float dt, double now)
        {
            Vector2 pan = input.Pan.ReadValue<Vector2>();
            const float dead = 0.35f;
            sim.PossessInput = new int2(pan.x > dead ? 1 : pan.x < -dead ? -1 : 0, pan.y > dead ? 1 : pan.y < -dead ? -1 : 0);

            _dig.Update(sim, false, false, pointerWorld, dt, now); // keeps the combo timing
            _biteCooldown -= dt;
            if (primaryPressed) _biteCooldown = 0f;
            if (primary && _biteCooldown <= 0f)
            {
                _biteCooldown += 1f / _possession.bitesPerSecond;
                if (_biteCooldown < 0f) _biteCooldown = 0f;
                sim.EnqueuePossessAct(cellPos, _dig.ComboMultiplier);
            }
            if (secondaryPressed || input.Interact.WasPressedThisFrame()) sim.EnqueuePossessInteract();
        }

        void Paint(Simulation sim, bool primary, bool secondary, float2 cellPos)
        {
            if (!primary && !secondary)
            {
                _hasLast = false;
                return;
            }
            bool set = primary;
            float radius = Mode == ToolMode.Pheromone ? _scent.brushRadius : _config.orderBrushRadius;

            // Fill the gap since last frame so fast strokes stay continuous.
            float2 from = _hasLast ? _lastCellPos : cellPos;
            float length = math.distance(from, cellPos);
            int steps = math.max(1, (int)math.ceil(length / (radius * 0.5f)));
            for (int i = 1; i <= steps; i++)
            {
                float2 p = math.lerp(from, cellPos, i / (float)steps);
                if (Mode == ToolMode.Mine) sim.EnqueueDesignate(p, radius, set);
                else if (Mode == ToolMode.Storage) sim.EnqueueStorageZone(p, radius, set);
                else sim.EnqueuePheromone(Scent, p, radius, set);
            }
            _hasLast = true;
            _lastCellPos = cellPos;
        }

        public void Consume(EventBus events, double now) => _dig.Consume(events, now);
    }
}
