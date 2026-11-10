using System.Collections.Generic;
using Antarium.Content;
using Antarium.Core;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// Buildings that work with fluids and heat (10.3). Pumps move water or gas from the cell behind them to the cell their
    /// arrow points at; water wheels let water from above fall through them and speed up machines nearby by how much
    /// passes. Both block fluid in their own cells (<see cref="CellFlags.FluidBlock"/>; ants walk through). Machines with an
    /// irrigation bonus give extra output while water stands in or next to them (and drink some per craft); machines with a
    /// heat bonus work faster and need no fuel while lava, magma rock or fire touches them.
    /// </summary>
    public sealed partial class BuildingSystem
    {
        readonly WaterworksConfig _water;
        /// <summary>Finished pumps and water wheels (rebuilt with the network).</summary>
        readonly List<int> _pumps = new();
        readonly List<int> _wheels = new();
        /// <summary>Cells that started or stopped blocking fluid; the fluids push fluid out of / wake them on the next step.</summary>
        readonly List<int2> _fluidCellsChanged = new();
        float _climateTimer;
        FluidSystem _fluids;

        public IReadOnlyList<int> Pumps => _pumps;
        public IReadOnlyList<int> Wheels => _wheels;

        /// <summary>The fluid system irrigated machines wake when they drink (set once by the simulation).</summary>
        public void SetFluids(FluidSystem fluids) => _fluids = fluids;

        void SetFluidBlock(in Building b, bool block)
        {
            for (int y = 0; y < b.Size.y; y++)
            for (int x = 0; x < b.Size.x; x++)
            {
                var c = b.Origin + new int2(x, y);
                int index = _world.Index(c.x, c.y);
                var cell = _world.Cells[index];
                cell.Flags = block ? cell.Flags | CellFlags.FluidBlock : cell.Flags & ~CellFlags.FluidBlock;
                _world.Cells[index] = cell;
                _fluidCellsChanged.Add(c);
            }
        }

        /// <summary>
        /// Before a fluid step: fluid in cells that just became blocked is pushed up out of them, and the fluids around
        /// cells that changed wake up.
        /// </summary>
        public void ApplyFluidBlocks(FluidSystem fluids)
        {
            if (_fluidCellsChanged.Count == 0) return;
            foreach (var c in _fluidCellsChanged)
            {
                if (fluids == null) continue;
                if (!_world[c.x, c.y].HoldsFluid) fluids.Displace(c);
                fluids.Wake(c);
            }
            _fluidCellsChanged.Clear();
        }

        /// <summary>
        /// After each fluid step: pumps move fluid and water wheels pass water down. Returns true if a cell started or
        /// stopped blocking ant paths.
        /// </summary>
        public bool StepFluids(FluidSystem fluids, float stepSeconds)
        {
            if (_networkDirty) Compile();
            ApplyFluidBlocks(fluids);
            bool nav = false;
            foreach (int id in _pumps) nav |= StepPump(id, fluids, stepSeconds);
            foreach (int id in _wheels) nav |= StepWheel(id, fluids, stepSeconds);
            return nav;
        }

        bool HoldsFluid(int2 c) => _world.InBounds(c.x, c.y) && _world[c.x, c.y].HoldsFluid;

        bool Blocks(FluidCell f) => f.Level > 0 && (f.Kind == FluidKind.Lava || f.Level >= _world.DeepFluid);

        /// <summary>Levels of fluid per fluid step for a building moving <paramref name="cellsPerSecond"/> cells a second.</summary>
        static int PerStep(float cellsPerSecond, float stepSeconds) => math.max(1, (int)math.round(cellsPerSecond * FluidCell.Full * stepSeconds));

        /// <summary>Moves <paramref name="amount"/> of <paramref name="kind"/> from <paramref name="from"/> to <paramref name="to"/> (both holding cells).</summary>
        bool MoveFluid(int2 from, int2 to, FluidKind kind, int amount, FluidSystem fluids)
        {
            int fi = _world.Index(from.x, from.y), ti = _world.Index(to.x, to.y);
            var a = _world.Fluid[fi];
            var b = _world.Fluid[ti];
            var na = new FluidCell(kind, (byte)(a.Level - amount));
            var nb = new FluidCell(kind, (byte)(b.Level + amount));
            _world.Fluid[fi] = na.Level == 0 ? FluidCell.Empty : na;
            _world.Fluid[ti] = nb;
            _world.MarkChanged(from.x, from.y, ChunkFlags.FluidUpload);
            _world.MarkChanged(to.x, to.y, ChunkFlags.FluidUpload);
            fluids?.Wake(from);
            fluids?.Wake(to);
            return Blocks(a) != Blocks(na) || Blocks(b) != Blocks(nb);
        }

        /// <summary>
        /// Where a pump draws from: the first cell holding fluid behind it, up to <see cref="WaterworksConfig.pumpReach"/> cells
        /// back through open cells (its hose), else the cell right behind it.
        /// </summary>
        public int2 PumpIntake(in Building b)
        {
            int2 intake = b.Origin - b.Dir;
            for (int k = 1; k < _water.pumpReach; k++)
            {
                if (!HoldsFluid(intake) || _world.Fluid[_world.Index(intake.x, intake.y)].Level > 0) break;
                int2 next = intake - b.Dir;
                if (!HoldsFluid(next)) break;
                intake = next;
            }
            return intake;
        }

        /// <summary>
        /// Where a pump pushes <paramref name="kind"/> to: the cell in front of it, or, while that is full of the same fluid, on
        /// along its arrow (up to <see cref="WaterworksConfig.pumpReach"/> cells) to the first cell with room. Fluids have no
        /// pressure, so this is what lets a pump lift water up a shaft.
        /// </summary>
        public int2 PumpOutlet(in Building b, FluidKind kind)
        {
            int2 outlet = b.Origin + b.Dir;
            for (int k = 1; k < _water.pumpReach; k++)
            {
                if (!HoldsFluid(outlet)) break;
                var f = _world.Fluid[_world.Index(outlet.x, outlet.y)];
                if (f.Level < FluidCell.Full || f.Kind != kind) break;
                int2 next = outlet + b.Dir;
                if (!HoldsFluid(next)) break;
                outlet = next;
            }
            return outlet;
        }

        /// <summary>
        /// A pump moves water or gas (not lava: too thick) from behind it (<see cref="PumpIntake"/>) out in front of it
        /// (<see cref="PumpOutlet"/>), as fast as its speed allows and only while the outlet has room for that fluid.
        /// <see cref="Building.Craft"/> = smoothed pumping (0..1).
        /// </summary>
        bool StepPump(int id, FluidSystem fluids, float stepSeconds)
        {
            ref var b = ref Get(id);
            if (!b.IsActive) return false;
            int rate = PerStep(_content.Buildings[b.Def].Def.speed, stepSeconds);
            int2 intake = PumpIntake(b);
            int moved = 0;
            bool nav = false;
            if (!HoldsFluid(intake) || !HoldsFluid(b.Origin + b.Dir)) b.Status = MachineStatus.Idle;
            else
            {
                var fi = _world.Fluid[_world.Index(intake.x, intake.y)];
                int2 outlet = PumpOutlet(b, fi.Kind);
                var fo = _world.Fluid[_world.Index(outlet.x, outlet.y)];
                if (fi.Level == 0 || fi.Kind == FluidKind.Lava) b.Status = MachineStatus.Idle;
                else if (fo.Level > 0 && fo.Kind != fi.Kind) b.Status = MachineStatus.OutputBlocked;
                else
                {
                    moved = math.min(math.min(fi.Level, rate), FluidCell.Full - fo.Level);
                    if (moved <= 0) b.Status = MachineStatus.OutputBlocked;
                    else
                    {
                        nav = MoveFluid(intake, outlet, fi.Kind, moved, fluids);
                        b.Status = MachineStatus.Working;
                    }
                }
            }
            b.Passed += moved;
            b.Craft = Smooth(b.Craft, moved / (float)rate);
            return nav;
        }

        /// <summary>
        /// Water resting on a wheel falls through it, each column up to the wheel's speed, into the cells right below (if
        /// they have room). <see cref="Building.Craft"/> = its power (0..1): the share of its full rate that passed, smoothed.
        /// </summary>
        bool StepWheel(int id, FluidSystem fluids, float stepSeconds)
        {
            ref var b = ref Get(id);
            if (!b.IsActive) return false;
            int perColumn = PerStep(_content.Buildings[b.Def].Def.speed, stepSeconds);
            int moved = 0;
            bool nav = false;
            for (int x = 0; x < b.Size.x; x++)
            {
                var above = new int2(b.Origin.x + x, b.Origin.y + b.Size.y);
                var below = new int2(b.Origin.x + x, b.Origin.y - 1);
                if (!HoldsFluid(above) || !HoldsFluid(below)) continue;
                var fa = _world.Fluid[_world.Index(above.x, above.y)];
                var fb = _world.Fluid[_world.Index(below.x, below.y)];
                if (fa.Level == 0 || fa.Kind != FluidKind.Water || (fb.Level > 0 && fb.Kind != FluidKind.Water)) continue;
                int move = math.min(math.min(fa.Level, perColumn), FluidCell.Full - fb.Level);
                if (move <= 0) continue;
                nav |= MoveFluid(above, below, FluidKind.Water, move, fluids);
                moved += move;
            }
            b.Passed += moved;
            b.Craft = Smooth(b.Craft, moved / (float)(perColumn * math.max(1, b.Size.x)));
            b.Status = b.Craft > 0.05f ? MachineStatus.Working : MachineStatus.Idle;
            return nav;
        }

        float Smooth(float current, float target)
        {
            float v = math.lerp(current, math.saturate(target), _water.wheelSmoothing);
            return v < 0.005f ? 0f : v;
        }

        // ---------------------------------------------------------------- heat, irrigation, wheels near machines

        /// <summary>Which machines are heated, irrigated or near a turning wheel (every <see cref="WaterworksConfig.checkSeconds"/>).</summary>
        void UpdateClimate()
        {
            UpdateResonators();
            ulong fuel = _content.FuelMask;
            foreach (var m in _machines)
            {
                if (m == null) continue;
                var b = All[m.Building];
                if (!b.IsActive) continue;
                var def = m.Data.Def;
                if (def.heatBonus > 0f && m.SetHeated(TouchesHeat(b), fuel))
                {
                    // Fuel stops (or starts) being taken in: belts, workers and routing masks follow.
                    ConsumersChanged |= m.Data.InputMask;
                    _networkDirty = true;
                }
                m.Irrigated = def.irrigationBonus > 0f && WettestNear(b) >= 0;
                float boost = 0f;
                foreach (int w in _wheels)
                {
                    var wb = All[w];
                    if (wb.Craft <= 0f) continue;
                    var wdef = _content.Buildings[wb.Def].Def;
                    if (RectDistance(b, wb) <= wdef.boostRadius) boost = math.max(boost, wb.Craft * wdef.machineBoost);
                }
                m.WheelBoost = boost;
                m.ResonanceBoost = ResonatorBoost(b);
            }
        }

        /// <summary>Cells gap between two footprints (0 = touching or overlapping), Chebyshev.</summary>
        static int RectDistance(in Building a, in Building b)
        {
            int dx = math.max(0, math.max(a.Origin.x - (b.Origin.x + b.Size.x - 1), b.Origin.x - (a.Origin.x + a.Size.x - 1)));
            int dy = math.max(0, math.max(a.Origin.y - (b.Origin.y + b.Size.y - 1), b.Origin.y - (a.Origin.y + a.Size.y - 1)));
            return math.max(dx, dy);
        }

        /// <summary>Lava, a hot material (magma rock) or a fire in or right around the footprint.</summary>
        public bool TouchesHeat(in Building b)
        {
            for (int y = -1; y <= b.Size.y; y++)
            for (int x = -1; x <= b.Size.x; x++)
            {
                var c = b.Origin + new int2(x, y);
                if (!_world.InBounds(c.x, c.y)) continue;
                int i = _world.Index(c.x, c.y);
                var cell = _world.Cells[i];
                var f = _world.Fluid[i];
                if (f.Level > 0 && (f.Kind == FluidKind.Lava || f.Kind == FluidKind.Fire)) return true;
                if (!cell.IsOpen && _content.MaterialSim[cell.Material].IsHot) return true;
            }
            return false;
        }

        /// <summary>The wettest open cell in or right around the footprint with enough water to irrigate, or -1.</summary>
        int WettestNear(in Building b)
        {
            int best = -1, bestLevel = _water.irrigationMinLevel - 1;
            for (int y = -1; y <= b.Size.y; y++)
            for (int x = -1; x <= b.Size.x; x++)
            {
                var c = b.Origin + new int2(x, y);
                if (!_world.InBounds(c.x, c.y)) continue;
                int i = _world.Index(c.x, c.y);
                var f = _world.Fluid[i];
                if (f.Kind != FluidKind.Water || f.Level <= bestLevel) continue;
                best = i;
                bestLevel = f.Level;
            }
            return best;
        }

        /// <summary>An irrigated machine finished a craft: it drinks from the wettest water next to it. False if there is none.</summary>
        bool Irrigate(in Building b)
        {
            int i = WettestNear(b);
            if (i < 0) return false;
            var f = _world.Fluid[i];
            int left = math.max(0, f.Level - _water.irrigationWater);
            _world.Fluid[i] = left > 0 ? new FluidCell(FluidKind.Water, (byte)left) : FluidCell.Empty;
            var c = GridIndex.ToCell(i, _world.ChunksX);
            _world.MarkChanged(c.x, c.y, ChunkFlags.FluidUpload);
            _fluids?.Wake(c);
            return true;
        }

        /// <summary>Machines a turning water wheel speeds up now (hover text).</summary>
        public int MachinesBoostedBy(int wheel)
        {
            var wb = All[wheel];
            if (wb.Craft <= 0f) return 0;
            float radius = _content.Buildings[wb.Def].Def.boostRadius;
            int n = 0;
            foreach (var m in _machines)
                if (m != null && All[m.Building].IsActive && RectDistance(All[m.Building], wb) <= radius) n++;
            return n;
        }
    }
}
