using System.Text;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using Unity.Mathematics;

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

        public void Update(float dt, Simulation sim, int2 cell, ToolController tools, bool pointerOverUi)
        {
            _timer -= dt;
            if (_timer > 0f) return;
            _timer = RefreshInterval;
            Text = Build(sim, cell, tools, pointerOverUi);
        }

        string Build(Simulation sim, int2 cell, ToolController tools, bool pointerOverUi)
        {
            _sb.Clear();
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
            int id = pointerOverUi ? BuildingSystem.None : sim.Buildings.At(cell);
            if (id >= 0) DescribeBuilding(sim, id);
            else if (tools.Mode == ToolMode.Build && tools.Build.SelectedDef != null) DescribeSelection(sim, cell, tools);
            else if (!pointerOverUi)
            {
                DescribeStock(sim, cell);
                if (_sb.Length == 0) DescribeFluid(sim, cell);
            }
            return _sb.Length > 0 ? _sb.ToString() : null;
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
                EnemyState.Raid => "raiding the colony",
                _ => "leaving with stolen food",
            });
            if (e.Held > 0f) _sb.Append(", stuck in a trap");
            if (!string.IsNullOrEmpty(def.description)) _sb.Append('\n').Append(def.description);
            _sb.Append("\nBites for ").Append(def.damage.ToString("0")).Append(". Soldiers (and workers, weakly) fight back.");
        }

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
                case FluidKind.Gas:
                    _sb.Append("Spore gas ").Append(percent).Append('%');
                    _sb.Append(deep ? "  -  too thick to walk through; it hurts ants inside." : "  -  hurts ants; it thins out over time in your tunnels.");
                    break;
            }
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
                _sb.Append("  -  under construction ").Append((int)(progress * 100f)).Append("%. Workers will build it.");
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
            }

            if (b.IsTransport)
            {
                if (b.Kind == BuildingKind.Filter)
                    _sb.Append("  -  filter: ").Append(_content.Items[b.Filter].DisplayName).Append(" (click with the Filter tool to change)");
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
            _sb.Append("  -  ").Append(b.Status switch
            {
                MachineStatus.Working => "working",
                MachineStatus.OutputBlocked => "output blocked (products are not being taken away)",
                _ => "waiting for input",
            });
            if (m.Recipe >= 0)
            {
                var recipe = _content.Recipes[m.Recipe];
                _sb.Append("\n").Append(recipe.Def.DisplayName).Append(' ').Append((int)(b.Craft * 100f)).Append('%');
            }
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
            if (container != null) _sb.Append("Storage cell ");
            else if (sim.Colony.IsRoyalCell(cell) && sim.World[cell.x, cell.y].IsOpen)
            {
                container = inv.Royal;
                _sb.Append("Royal chamber store ");
            }
            else return;
            _sb.Append(container.Total).Append('/').Append(container.Capacity);
            bool first = true;
            for (int i = 0; i < container.Counts.Length; i++)
            {
                if (container.Counts[i] == 0) continue;
                _sb.Append(first ? ":  " : ", ").Append(_content.Items[i].DisplayName).Append(' ').Append(container.Counts[i]);
                first = false;
            }
            _sb.Append("\nWhole stock ").Append(inv.Stored).Append('/').Append(inv.Capacity).Append(". Workers fetch from here for machines that need it.");
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
            _sb.Append("\nQ / E: other building   R: rotate   right mouse: remove");
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
            _sb.Append(" (").Append(recipe.Duration.ToString("0.#")).Append("s)");
        }

        static string Rate(float perMinute) => perMinute < 10f ? perMinute.ToString("0.0") : NumberFormat.Format(perMinute);
    }
}
