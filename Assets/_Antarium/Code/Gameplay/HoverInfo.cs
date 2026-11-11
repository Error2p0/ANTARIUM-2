using System.Text;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using Unity.Mathematics;
using UnityEngine;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Text for the info panel: details of the building under the cursor (flow, jams, machine inputs and
    /// progress), or the selected building's cost and rules while the Build tool is active.
    /// Rebuilt a few times per second.
    /// </summary>
    public sealed class HoverInfo
    {
        const float RefreshInterval = 0.2f;

        readonly ContentDB _content;
        readonly HudController _hud;
        readonly StringBuilder _sb = new();
        float _timer;

        public HoverInfo(ContentDB content, HudController hud)
        {
            _content = content;
            _hud = hud;
        }

        /// <summary>Current panel text, or null when there is nothing to show.</summary>
        public string Text { get; private set; }

        /// <summary>Icon for <see cref="Text"/> (art name under <c>UI/Icons/</c>: the caste, building, item...), or null (10.7).</summary>
        public string Icon { get; private set; }

        public void Update(float dt, Simulation sim, int2 cell, ToolController tools, bool pointerOverUi)
        {
            _timer -= dt;
            if (_timer > 0f) return;
            _timer = RefreshInterval;
            Icon = null;
            Text = Build(sim, cell, tools, pointerOverUi);
            if (Text == null) Icon = null;
        }

        string Build(Simulation sim, int2 cell, ToolController tools, bool pointerOverUi)
        {
            _sb.Clear();
            if (tools.Possessing)
            {
                DescribePossessed(sim);
                return _sb.ToString();
            }
            if (!pointerOverUi && sim.RandomEvents.CritterAt(new float2(cell) + 0.5f) >= 0)
            {
                _sb.Append("Golden beetle! Click it to catch it before it leaves.");
                return _sb.ToString();
            }
            int enemy = pointerOverUi ? -1 : sim.Enemies.EnemyAt(new float2(cell) + 0.5f, 1.2f);
            if (enemy >= 0 && IsVisible(sim, cell))
            {
                DescribeEnemy(sim, enemy);
                return _sb.ToString();
            }
            if (tools.Mode == ToolMode.Pheromone)
            {
                DescribeScentTool(sim, cell, tools, pointerOverUi);
                return _sb.ToString();
            }
            int ant = pointerOverUi ? -1 : sim.Ants.AntAt(new float2(cell) + 0.5f, 0.9f);
            if (ant >= 0 && tools.Mode != ToolMode.Build)
            {
                DescribeAnt(sim, ant);
                return _sb.ToString();
            }
            int id = pointerOverUi ? BuildingSystem.None : sim.Buildings.At(cell);
            if (id >= 0)
            {
                DescribeBuilding(sim, id);
                Icon = "building_" + _content.Buildings[sim.Buildings.Get(id).Def].Def.Id;
            }
            else if (tools.Mode == ToolMode.Build && tools.Build.SelectedDef != null)
            {
                DescribeSelection(sim, cell, tools);
                Icon = "building_" + tools.Build.SelectedDef.Id;
            }
            else if (!pointerOverUi)
            {
                DescribeStock(sim, cell);
                if (_sb.Length == 0) DescribeFind(sim, cell);
                if (_sb.Length == 0) DescribeFluid(sim, cell);
                if (_sb.Length == 0) DescribeFire(sim, cell);
            }
            if (!pointerOverUi) AppendScents(sim, cell);
            return _sb.Length > 0 ? _sb.ToString() : null;
        }

        static string StateText(in Ant a) => a.State switch
        {
            AntState.SeekDig => "going to dig",
            AntState.Digging => "digging",
            AntState.SeekItem => "going to pick something up",
            AntState.Carrying => "carrying",
            AntState.SeekBuild => "going to build",
            AntState.Building => "building",
            AntState.SeekSupply => "fetching for a machine",
            AntState.Hunting => "hunting",
            AntState.Fighting => "fighting",
            AntState.Swimming => "swimming out",
            AntState.Falling => "falling",
            AntState.SeekEgg => "going for an egg",
            AntState.CarryEgg => "carrying an egg to a nursery",
            AntState.Scouting => "scouting",
            AntState.SeekCritter => "chasing a golden critter",
            AntState.SeekMachine => "going to tend a machine",
            AntState.Tending => "tending a machine",
            AntState.Rallying => "answering the Pheromone Burst",
            AntState.Sheltering => "hiding from the bird",
            _ => "idle",
        };

        void DescribeAnt(Simulation sim, int index)
        {
            var a = sim.Ants.Ants[index];
            var def = _content.Caste(a.Caste);
            Icon = CasteIcon(a.Caste);
            _sb.Append(def.DisplayName).Append("  ").Append(math.max(1, (int)math.ceil(a.Health))).Append('/')
                .Append((int)sim.Ants.MaxHealth(a.Caste)).Append(" HP  -  ").Append(StateText(a));
            if (a.IsCarrying)
                _sb.Append(" (").Append(a.CarryCount > 1 ? $"{a.CarryCount} " : "").Append(_content.Items[a.Carry].DisplayName).Append(')');
            if (a.Skill != Jobs.NoSkill || a.Trait != AntTrait.None) _sb.Append('\n');
            if (a.Skill != Jobs.NoSkill)
            {
                int level = sim.AntLevel(a);
                _sb.Append("Level ").Append(level).Append(' ').Append(HudText.SkillName((JobKind)a.Skill));
                if (level > 1) _sb.Append(" (+").Append(Mathf.RoundToInt((level - 1) * sim.Config.growth.levelBonus * 100f)).Append("% at it)");
                var xp = sim.Config.growth.levelXp;
                if (xp != null && level - 1 < xp.Length) _sb.Append(", ").Append((int)a.Xp).Append('/').Append((int)xp[level - 1]).Append(" XP");
            }
            if (a.Trait != AntTrait.None)
            {
                if (a.Skill != Jobs.NoSkill) _sb.Append("   ");
                _sb.Append(HudText.TraitName(a.Trait)).Append(": ").Append(HudText.TraitText(a.Trait));
            }
            if (!string.IsNullOrEmpty(def.description)) _sb.Append('\n').Append(def.description);
            _sb.Append('\n').Append(Keys.Possess).Append(": take control of this ant.");
        }

        void DescribePossessed(Simulation sim)
        {
            int i = sim.PossessedAnt;
            if (i < 0) return;
            var a = sim.Ants.Ants[i];
            Icon = CasteIcon(a.Caste);
            _sb.Append("You control a ").Append(_content.Caste(a.Caste).DisplayName.ToLowerInvariant()).Append("  ")
                .Append(math.max(1, (int)math.ceil(a.Health))).Append('/').Append((int)sim.Ants.MaxHealth(a.Caste)).Append(" HP");
            if (a.CarryingEgg) _sb.Append("  -  carrying an egg");
            else if (a.IsCarrying) _sb.Append("  -  carrying ").Append(_content.Items[a.Carry].DisplayName);
            _sb.Append("\nCamera keys: move   left mouse: dig or bite (within reach)   right mouse / ").Append(Keys.Interact)
                .Append(": pick up, deliver, drop   ").Append(Keys.Possess).Append(" / ").Append(Keys.Menu).Append(": let go");
        }

        void DescribeScentTool(Simulation sim, int2 cell, ToolController tools, bool pointerOverUi)
        {
            var kind = tools.Scent;
            _sb.Append(HudController.ScentName(kind)).Append(" scent  -  ").Append(kind switch
            {
                PheromoneKind.Trail => "ants walk faster along it and prefer it as a route. Ants carrying loads keep it fresh.",
                PheromoneKind.Dig => "mining orders inside are dug before all others.",
                PheromoneKind.Gather => "item piles inside are hauled before all others.",
                PheromoneKind.Defend => "idle soldiers patrol here.",
                _ => "ants route around it and take no jobs inside.",
            });
            _sb.Append("\nLeft mouse: paint   right mouse: erase   ").Append(Keys.Previous).Append(" / ").Append(Keys.Next)
                .Append(": other scent. Scents fade after about 4 minutes unless an Emitter keeps them.");
            if (!pointerOverUi) AppendScents(sim, cell);
        }

        /// <summary>"Scent: Trail 80%, Dig here 40%" for the cell under the cursor.</summary>
        void AppendScents(Simulation sim, int2 cell)
        {
            bool first = true;
            for (int k = 0; k < Pheromones.Count; k++)
            {
                int s = sim.Pheromones.Get((PheromoneKind)k, cell);
                if (s == 0) continue;
                _sb.Append(first ? (_sb.Length > 0 ? "\nScent: " : "Scent: ") : ", ")
                    .Append(HudController.ScentName((PheromoneKind)k)).Append(' ').Append(s * 100 / PheromoneSystem.Full).Append('%');
                first = false;
            }
        }

        static bool IsVisible(Simulation sim, int2 cell) => sim.World.InBounds(cell.x, cell.y) && sim.World[cell.x, cell.y].IsRevealed;

        void DescribeEnemy(Simulation sim, int index)
        {
            var e = sim.Enemies.Enemies[index];
            var def = _content.Enemies[e.Def].Def;
            _sb.Append(def.DisplayName).Append("  ").Append(math.max(1, (int)math.ceil(e.Health))).Append('/').Append((int)def.health).Append(" HP  -  ");
            _sb.Append(e.State switch
            {
                EnemyState.Dormant => "lurking",
                EnemyState.Wander => "wandering",
                EnemyState.Hunt => "hunting ants",
                EnemyState.Raid => def.behavior == EnemyBehavior.Thief ? "raiding the stock" : "raiding the colony",
                EnemyState.Burrow => "digging toward the nest",
                _ when def.behavior == EnemyBehavior.Thief => e.LootCount > 0 ? "running off with your items" : "leaving",
                _ => "leaving with stolen food",
            });
            if (e.LootCount > 0 && e.Loot < _content.Items.Length)
                _sb.Append(" (").Append(e.LootCount).Append(' ').Append(_content.Items[e.Loot].DisplayName).Append(')');
            if (e.Held > 0f) _sb.Append(", stuck in a trap");
            if (!string.IsNullOrEmpty(def.description)) _sb.Append('\n').Append(def.description);
            _sb.Append("\nBites for ").Append(def.damage.ToString("0")).Append(". Soldiers (and workers, weakly) fight back.");
        }

        /// <summary>A find in the rock (ore, coal, amber...; 10.7) or a tablet in a ruin floor (10.5): what it gives.</summary>
        void DescribeFind(Simulation sim, int2 cell)
        {
            if (!IsVisible(sim, cell)) return;
            var c = sim.World[cell.x, cell.y];
            if (c.IsOpen) return;
            ushort item = OreSurvey.FindItem(_content, c.Material);
            if (item == ContentDB.NoItem) return;
            Icon = "item_" + _content.Items[item].Id;
            if (c.Material == _content.TabletMaterial && _content.TabletMaterial != ContentDB.AirIndex)
            {
                _sb.Append(_content.Materials[c.Material].DisplayName)
                    .Append("  -  a unique find of the lost colony. Dig it out and study it in an Archive: a lot of knowledge and a fragment of their story.");
                return;
            }
            _sb.Append(_content.Materials[c.Material].DisplayName).Append("  -  dig it out for ").Append(_content.Items[item].DisplayName)
                .Append(". Paint mining orders (").Append(Keys.Mine).Append(") and workers bring it to storage. ")
                .Append(Keys.Ores).Append(": the ore overlay.");
        }

        static string CasteIcon(AntCaste caste) => "caste_" + caste.ToString().ToLowerInvariant();

        void DescribeFluid(Simulation sim, int2 cell)
        {
            if (!IsVisible(sim, cell) || !sim.World[cell.x, cell.y].IsOpen) return;
            var f = sim.World.Fluid[sim.World.Index(cell.x, cell.y)];
            if (f.Level == 0) return;
            int percent = (int)math.round(f.Level * 100f / FluidCell.Full);
            bool deep = f.Level >= sim.World.DeepFluid;
            switch (f.Kind)
            {
                case FluidKind.Water:
                    _sb.Append("Water ").Append(percent).Append('%');
                    _sb.Append(deep ? "  -  too deep to walk: ants avoid it and drown if caught inside." : "  -  shallow; it slowly dries up in your tunnels.");
                    _sb.Append("\nDig a pit below it to drain it, or seal it off with an Earth Wall.");
                    break;
                case FluidKind.Lava:
                    _sb.Append("Lava  -  burns ants and blocks their paths. Where it meets water it turns into obsidian.");
                    break;
                case FluidKind.Smoke:
                    _sb.Append("Smoke ").Append(percent).Append('%');
                    _sb.Append("  -  from a fire. It rises, hurts ants when thick and clears away on its own; a pump can blow it out.");
                    break;
                case FluidKind.Gas:
                    _sb.Append("Spore gas ").Append(percent).Append('%');
                    _sb.Append(deep ? "  -  too thick to walk through; it hurts ants inside." : "  -  hurts ants; it thins out over time in your tunnels.");
                    break;
            }
        }

        /// <summary>A burning solid cell.</summary>
        void DescribeFire(Simulation sim, int2 cell)
        {
            if (!IsVisible(sim, cell)) return;
            var c = sim.World[cell.x, cell.y];
            var f = sim.World.Fluid[sim.World.Index(cell.x, cell.y)];
            if (c.IsOpen || f.Kind != FluidKind.Fire || f.Level == 0) return;
            float left = f.Level * sim.TickDuration * Mathf.Max(1, sim.Config.fluids.ticksPerStep);
            _sb.Append(_content.Materials[c.Material].DisplayName).Append("  -  burning (").Append(Mathf.CeilToInt(left)).Append(" s left)")
                .Append("\nIt spreads to what burns next to it, gives off smoke and burns ants beside it, then turns to ash.")
                .Append("\nWater next to it puts it out (pump some in); digging it out ends it too.");
        }

        void DescribeBuilding(Simulation sim, int id)
        {
            var buildings = sim.Buildings;
            var b = buildings.Get(id);
            var data = _content.Buildings[b.Def];
            _sb.Append(data.Def.DisplayName);
            if (b.State == BuildingState.Blueprint)
            {
                float progress = b.WorkTotal > 0f ? 1f - b.WorkLeft / b.WorkTotal : 0f;
                if (b.Restoring && b.Stage < data.StageCount)
                    _sb.Append("  -  restoring \"").Append(data.Def.stages[b.Stage].name).Append("\" ").Append((int)(progress * 100f)).Append("%. Workers are building it.");
                else _sb.Append("  -  under construction ").Append((int)(progress * 100f)).Append("%. Workers will build it.");
                return;
            }

            switch (b.Kind)
            {
                case BuildingKind.Gate:
                    _sb.Append("  -  ").Append((int)math.ceil(b.Health)).Append('/').Append((int)buildings.GateMaxHealth(b.Def))
                        .Append(" HP\nAnts walk through; enemies have to break it first. It mends slowly; broken gates are rebuilt for free.");
                    return;
                case BuildingKind.Trap:
                    _sb.Append(b.Rearm > 0f ? $"  -  re-arming ({b.Rearm:0}s)" : "  -  armed")
                        .Append("\nHurts and holds the first enemy that steps on it.");
                    return;
                case BuildingKind.GuardPost:
                    _sb.Append("\nIdle soldiers wait at the nearest guard post and defend the area around it.");
                    return;
                case BuildingKind.Nursery:
                {
                    int eggs = 0;
                    foreach (var e in sim.Colony.Eggs)
                        if (e.Nursery == id) eggs++;
                    _sb.Append("  -  ").Append(eggs).Append('/').Append(data.Def.eggCapacity).Append(" eggs\nNurses bring eggs here from the queen; they hatch ")
                        .Append(Mathf.RoundToInt(data.Def.hatchBonus * 100f)).Append("% faster and the queen can lay more meanwhile.");
                    return;
                }
                case BuildingKind.Emitter:
                    _sb.Append("  -  ").Append(HudController.ScentName((PheromoneKind)math.min(b.Filter, Pheromones.Count - 1)))
                        .Append("\nKeeps that scent from fading within ").Append(data.Def.emitterRadius.ToString("0")).Append(" cells (paint it first).")
                        .Append(' ').Append(_hud.ConfigureKey).Append(": change the scent; R picks the scent of new ones.");
                    return;
                case BuildingKind.Chest:
                {
                    var chest = sim.Colony.Inventory.ContainerAt(sim.World.Index(b.Origin.x, b.Origin.y));
                    if (chest == null) return;
                    _sb.Append("  -  ");
                    AppendContents(chest);
                    AppendAllowed(chest);
                    _sb.Append("\n").Append(_hud.ConfigureKey).Append(": choose what it takes. Belts that end at it fill it.");
                    return;
                }
                case BuildingKind.Unloader:
                {
                    var source = buildings.UnloaderSource(id);
                    _sb.Append("  -  takes ").Append(b.Filter < _content.Items.Length ? _content.Items[b.Filter].DisplayName : "any item")
                        .Append(" from ").Append(source == null ? "nothing (put storage, a chest or the royal chamber behind it)"
                            : source.IsRoyal ? "the royal chamber" : source.IsChest ? "the chest behind it" : "the storage behind it");
                    _sb.Append("\nFlow ").Append(Rate(b.Rate)).Append(" items/min  -  ").Append(b.Outputs.x < 0
                        ? "nothing in front of its arrow: point it at a belt, lift or machine"
                        : b.Status == MachineStatus.Working ? "unloading"
                        : b.Status == MachineStatus.OutputBlocked ? "waiting: what it feeds is full or doesn't use the item"
                        : "nothing to take (or only what is kept for building)");
                    _sb.Append("\n").Append(_hud.ConfigureKey).Append(": pick the item. ").Append(Keys.Rotate).Append(" turns new ones.");
                    return;
                }
                case BuildingKind.Pump:
                {
                    var intake = buildings.PumpIntake(b);
                    var fi = sim.World.Fluid[sim.World.Index(intake.x, intake.y)];
                    _sb.Append("  -  ").Append(b.Status switch
                    {
                        MachineStatus.Working => "pumping " + (fi.Kind == FluidKind.Smoke ? "smoke" : fi.Kind == FluidKind.Gas ? "gas" : "water"),
                        MachineStatus.OutputBlocked => "stopped: no room in front (full, or another fluid there)",
                        _ when fi.Level > 0 && fi.Kind == FluidKind.Lava => "lava is too thick to pump",
                        _ => "nothing to pump behind it",
                    });
                    _sb.Append("\nMoves ").Append(Rate(b.Rate / FluidCell.Full)).Append(" cells of fluid/min from behind it to the way its spout points. ")
                        .Append("It reaches ").Append(sim.Config.waterworks.pumpReach).Append(" cells back and pushes up to ")
                        .Append(sim.Config.waterworks.pumpReach).Append(" cells ahead.")
                        .Append("\nFluid can't pass through it; ants can. ").Append(Keys.Rotate).Append(" turns new ones; chain pumps to lift water higher.");
                    return;
                }
                case BuildingKind.Resonator:
                {
                    var def = data.Def;
                    var r = buildings.ResonanceOf(id);
                    int boosted = buildings.MachinesNear(id);
                    _sb.Append("  -  ").Append(b.Craft > 0f ? $"resonating ({Mathf.RoundToInt(b.Craft * 100f)}% power)" : "silent");
                    _sb.Append("\nCluster: ").Append(r.Crystals).Append(r.Crystals == 1 ? " crystal" : " crystals").Append(" in ").Append(r.Cells)
                        .Append(" conductive cells (").Append(def.fullResonance.ToString("0")).Append(" crystals for full power)");
                    _sb.Append("\nMakes ").Append(Rate(b.Craft * def.knowledgePerSecond * 60f * sim.Colony.Mods.Mult(StatKind.KnowledgeGain)))
                        .Append(" knowledge/min");
                    if (b.Craft > 0f)
                    {
                        _sb.Append(boosted > 0 ? $", speeds up {boosted} {(boosted == 1 ? "machine" : "machines")}" : ", machines")
                            .Append(" within ").Append(def.boostRadius.ToString("0")).Append(" cells ").Append(boosted > 0 ? "by +" : "would work +")
                            .Append(Mathf.RoundToInt(b.Craft * def.machineBoost * 100f)).Append(boosted > 0 ? "%" : "% faster");
                    }
                    _sb.Append("\nGlow crystals touching it (or joined to it through copper or iron ore) feed it; mining them weakens it.");
                    return;
                }
                case BuildingKind.Engine:
                {
                    var def = data.Def;
                    int stages = data.StageCount;
                    if (b.Stage >= stages)
                        _sb.Append("  -  running\nMakes ").Append(Rate(def.knowledgePerSecond * 60f * sim.Colony.Mods.Mult(StatKind.KnowledgeGain)))
                            .Append(" knowledge/min; this colony's Nuptial Flight earns ")
                            .Append(Mathf.RoundToInt((1f + sim.Config.prestige.engineMemoryBonus) * 100f)).Append("% Genetic Memory.");
                    else
                        _sb.Append("  -  ").Append(b.Stage).Append('/').Append(stages).Append(" restored. Next: ").Append(def.stages[b.Stage].name)
                            .Append("\n").Append(_hud.ConfigureKey).Append(" (or click it): restore the next step.");
                    return;
                }
                case BuildingKind.Throne:
                {
                    var def = data.Def;
                    _sb.Append("  -  the queen holds court");
                    foreach (var s in def.stats)
                    {
                        _sb.Append("\n").Append(s.value >= 0f ? "+" : "");
                        if (HudText.IsPercent(s.stat)) _sb.Append(Mathf.RoundToInt(s.value * 100f)).Append('%');
                        else _sb.Append(s.value.ToString("0.#"));
                        _sb.Append(' ').Append(HudText.StatName(s.stat));
                    }
                    if (def.auraRadius > 0f && def.auraWork > 0f)
                        _sb.Append("\nAnts within ").Append(def.auraRadius.ToString("0")).Append(" cells dig and build +")
                            .Append(Mathf.RoundToInt(def.auraWork * 100f)).Append("% faster.");
                    return;
                }
                case BuildingKind.WaterWheel:
                {
                    var def = data.Def;
                    int boosted = buildings.MachinesBoostedBy(id);
                    _sb.Append("  -  ").Append(b.Craft > 0.05f ? $"turning ({Mathf.RoundToInt(b.Craft * 100f)}% power)" : "still");
                    if (b.Craft > 0.05f)
                        _sb.Append("\nSpeeds up ").Append(boosted).Append(boosted == 1 ? " machine" : " machines").Append(" within ")
                            .Append(def.boostRadius.ToString("0")).Append(" cells by +").Append(Mathf.RoundToInt(b.Craft * def.machineBoost * 100f)).Append('%');
                    _sb.Append("\nWater has to pour onto it from above and drain away below (up to +")
                        .Append(Mathf.RoundToInt(def.machineBoost * 100f)).Append("% at full flow). A pump can keep it turning.");
                    return;
                }
            }

            if (b.IsTransport)
            {
                if (b.Kind == BuildingKind.Filter)
                    _sb.Append("  -  filter: ").Append(_content.Items[b.Filter].DisplayName).Append(" (").Append(_hud.ConfigureKey).Append(" to change)");
                _sb.Append("\nFlow ").Append(Rate(b.Rate)).Append(" items/min");
                if (!b.Front.IsEmpty)
                {
                    _sb.Append("   carrying ").Append(_content.Items[b.Front.Item].DisplayName);
                    if (!b.Back.IsEmpty) _sb.Append(", ").Append(_content.Items[b.Back.Item].DisplayName);
                }
                if (b.Mask == 0)
                    _sb.Append("\nLeads nowhere: end the line in the royal chamber, a storage zone or a machine.");
                else if (b.Jammed)
                    _sb.Append("\nJammed: the line is full. Its end is blocked (stock full, machine busy) or leads nowhere for this item.");
                else if (b.Mask != buildings.AllItems)
                    _sb.Append("\nOnly carries what the machines at its end use.");
                return;
            }

            var m = buildings.MachineOf(id);
            if (m == null) return;
            bool raises = false;
            foreach (int r in m.Data.Recipes) raises |= _content.Recipes[r].Princesses > 0;
            _sb.Append("  -  ").Append(b.Status switch
            {
                MachineStatus.Working => "working",
                MachineStatus.OutputBlocked when raises => "resting: the colony has as many princesses as count",
                MachineStatus.OutputBlocked => "output blocked (products are not being taken away)",
                _ => "waiting for input",
            });
            if (raises)
                _sb.Append("\nPrincesses ").Append(sim.Colony.Princesses).Append('/').Append(sim.Colony.PrincessCap)
                    .Append("   they wait here for the Nuptial Flight (").Append(Keys.Flight).Append(')');
            if (m.Tenders > 0)
                _sb.Append(", tended by an engineer (+").Append(Mathf.RoundToInt(buildings.EngineerBoost * m.TendPower * 100f)).Append("%)");
            var mdef = m.Data.Def;
            if (mdef.heatBonus > 0f)
                _sb.Append(m.Heated
                    ? $"\nHeated: +{Mathf.RoundToInt(mdef.heatBonus * 100f)}% speed and no fuel needed"
                    : "\nNot heated: build it next to lava or magma rock to skip the coal and work faster");
            if (mdef.irrigationBonus > 0f)
                _sb.Append(m.Irrigated
                    ? $"\nIrrigated: +{Mathf.RoundToInt(mdef.irrigationBonus * 100f)}% output (it drinks a little water per batch)"
                    : $"\nDry: water in or next to it gives +{Mathf.RoundToInt(mdef.irrigationBonus * 100f)}% output");
            if (m.WheelBoost > 0f)
                _sb.Append("\nWater wheel nearby: +").Append(Mathf.RoundToInt(m.WheelBoost * 100f)).Append("% speed");
            if (m.ResonanceBoost > 0f)
                _sb.Append("\nCrystal Resonator nearby: +").Append(Mathf.RoundToInt(m.ResonanceBoost * 100f)).Append("% speed");
            if (m.Recipe >= 0)
            {
                var recipe = _content.Recipes[m.Recipe];
                _sb.Append("\n").Append(recipe.Def.DisplayName).Append(' ').Append((int)(b.Craft * 100f)).Append('%');
            }
            if (m.Data.Recipes.Length > 1)
                _sb.Append("\nRuns ").Append(m.Choice >= 0 ? "only: " + _content.Recipes[m.Data.Recipes[m.Choice]].Def.DisplayName : "any recipe it can")
                    .Append("  (").Append(_hud.ConfigureKey).Append(" to choose)");
            _sb.Append("\nInputs: ");
            for (int i = 0; i < m.Inputs.Length; i++)
            {
                if (i > 0) _sb.Append(", ");
                _sb.Append(_content.Items[m.Data.InputItems[i]].DisplayName).Append(' ').Append(m.Inputs[i]).Append('/').Append(m.Data.InputCaps[i]);
            }
            _sb.Append("\nMade ").Append(m.Crafted).Append(m.OutputBelt >= 0 ? "   products go onto the belt beside it" : "   products are left for workers to haul");
            _sb.Append("\nRecipes: ");
            for (int i = 0; i < m.Data.Recipes.Length; i++)
            {
                if (i > 0) _sb.Append("   ");
                AppendRecipe(_content.Recipes[m.Data.Recipes[i]]);
            }
        }

        /// <summary>Contents of the storage cell (or the royal chamber) under the cursor.</summary>
        void DescribeStock(Simulation sim, int2 cell)
        {
            if (!sim.World.InBounds(cell.x, cell.y)) return;
            var inv = sim.Colony.Inventory;
            var container = inv.ContainerAt(sim.World.Index(cell.x, cell.y));
            Icon = "storage";
            if (container != null) _sb.Append("Storage cell ");
            else if (sim.Colony.IsRoyalCell(cell) && sim.World[cell.x, cell.y].IsOpen)
            {
                container = inv.Royal;
                _sb.Append("Royal chamber store ");
            }
            else
            {
                Icon = null;
                return;
            }
            AppendContents(container);
            AppendAllowed(container);
            _sb.Append("\nWhole stock ").Append(inv.Stored).Append('/').Append(inv.Capacity).Append(". Workers fetch from here for machines that need it.");
            if (!container.IsRoyal) _sb.Append("\n").Append(_hud.ConfigureKey).Append(": choose which items this storage zone takes.");
            else _sb.Append("\nThe queen: click her or press ").Append(_hud.QueenKey).Append(" for her panel (evolutions).");
        }

        void AppendContents(StockContainer container)
        {
            _sb.Append(container.Total).Append('/').Append(container.Capacity);
            bool first = true;
            for (int i = 0; i < container.Counts.Length; i++)
            {
                if (container.Counts[i] == 0) continue;
                _sb.Append(first ? ":  " : ", ").Append(_content.Items[i].DisplayName).Append(' ').Append(container.Counts[i]);
                first = false;
            }
        }

        /// <summary>"Takes only: ..." for a filtered container.</summary>
        void AppendAllowed(StockContainer container)
        {
            if (!container.Filtered) return;
            _sb.Append("\nTakes only: ");
            bool first = true;
            for (int i = 0; i < _content.Items.Length && i < 64; i++)
            {
                if (_content.Items[i].IsFood || !container.Accepts((ushort)i)) continue;
                _sb.Append(first ? "" : ", ").Append(_content.Items[i].DisplayName);
                first = false;
            }
            if (first) _sb.Append("nothing");
        }

        void DescribeSelection(Simulation sim, int2 cell, ToolController tools)
        {
            var build = tools.Build;
            var data = _content.Buildings[build.Selected];
            _sb.Append(data.Def.DisplayName).Append("  -  ").Append(_hud.CostText(data));
            if (!string.IsNullOrEmpty(data.Def.description)) _sb.Append('\n').Append(data.Def.description);
            var result = sim.Buildings.CanPlace(build.Selected, build.OriginFor(cell), build.Dir);
            if (result != PlaceResult.Ok && result != PlaceResult.Rotated && result != PlaceResult.Unchanged)
                _sb.Append('\n').Append(_hud.PlaceMessage(result, build.Selected, sim));
            _sb.Append('\n').Append(Keys.Previous).Append(" / ").Append(Keys.Next).Append(": other building   ").Append(Keys.Rotate)
                .Append(": rotate   right mouse: remove");
        }

        void AppendRecipe(RecipeData recipe)
        {
            for (int i = 0; i < recipe.InputItems.Length; i++)
            {
                if (i > 0) _sb.Append(" + ");
                _sb.Append(recipe.InputCounts[i]).Append(' ').Append(_content.Items[recipe.InputItems[i]].DisplayName);
            }
            _sb.Append(" -> ");
            for (int i = 0; i < recipe.OutputItems.Length; i++)
            {
                if (i > 0) _sb.Append(" + ");
                _sb.Append(recipe.OutputCounts[i]).Append(' ').Append(_content.Items[recipe.OutputItems[i]].DisplayName);
            }
            if (recipe.Knowledge > 0f)
            {
                if (recipe.OutputItems.Length > 0) _sb.Append(" + ");
                _sb.Append(recipe.Knowledge.ToString("0.#")).Append(" Knowledge");
            }
            if (recipe.Princesses > 0)
            {
                if (recipe.OutputItems.Length > 0 || recipe.Knowledge > 0f) _sb.Append(" + ");
                _sb.Append(recipe.Princesses).Append(recipe.Princesses == 1 ? " Princess" : " Princesses");
            }
            _sb.Append(" (").Append(recipe.Duration.ToString("0.#")).Append("s)");
        }

        static string Rate(float perMinute) => perMinute < 10f ? perMinute.ToString("0.0") : NumberFormat.Format(perMinute);
    }
}
