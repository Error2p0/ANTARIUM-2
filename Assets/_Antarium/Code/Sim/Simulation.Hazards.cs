using System.Collections.Generic;
using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// Hazards and terrain that changes by itself: fluid steps (and lava turning to stone where it meets water),
    /// cave-ins of unstable material, finished walls, and removing ants that died.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>World indices of unstable cells that may fall (checked on the next fluid step).</summary>
        readonly HashSet<int> _unstable = new();
        readonly List<int2> _unstableBatch = new();
        /// <summary>Cells that became solid this step: sand landed (crushes) / walls and stone (no damage).</summary>
        readonly HashSet<int> _crushed = new();
        readonly HashSet<int> _filled = new();
        bool _fluidNavDirty;
        float _sinceFluidNav;
        int _fluidTick;

        /// <summary>Unstable cells waiting to be checked (tests, debug).</summary>
        public int PendingCaveIns => _unstable.Count;

        void StepHazards(float dt)
        {
            _sinceFluidNav += dt;
            if (++_fluidTick >= Config.fluids.ticksPerStep)
            {
                _fluidTick = 0;
                var result = Fluids.Step();
                if (result.NavChanged) _fluidNavDirty = true;
                var reactions = Fluids.Reactions;
                for (int i = 0; i < reactions.Length; i++)
                {
                    var c = reactions[i];
                    if (!World[c.x, c.y].IsOpen) continue;
                    Solidify(c, Fluids.ReactionMaterial, _filled);
                    Events.Publish(new FluidReactionEvent(c));
                }
                StepCaveIns();
                FlushSolidified();
            }
            // Moving fluid reroutes ants, but rebuilding every path field each fluid step would be wasteful.
            if (_fluidNavDirty && _sinceFluidNav >= Config.fluids.pathRefreshSeconds)
            {
                _fluidNavDirty = false;
                _sinceFluidNav = 0f;
                _terrainChanged = true;
            }
        }

        // ---------------------------------------------------------------- cave-ins

        /// <summary>A cell opened: unstable cells above it (and diagonally) may fall.</summary>
        internal void CheckUnstableAbove(int2 c)
        {
            for (int dx = -1; dx <= 1; dx++)
                AddUnstable(c + new int2(dx, 1));
        }

        void AddUnstable(int2 c)
        {
            if (!World.InBounds(c.x, c.y)) return;
            int i = World.Index(c.x, c.y);
            var cell = World.Cells[i];
            if (!cell.IsOpen && Content.MaterialSim[cell.Material].IsUnstable) _unstable.Add(i);
        }

        /// <summary>Every waiting unstable cell falls one cell (straight down, else sliding diagonally), lowest first.</summary>
        void StepCaveIns()
        {
            if (_unstable.Count == 0) return;
            _unstableBatch.Clear();
            foreach (int i in _unstable) _unstableBatch.Add(GridIndex.ToCell(i, World.ChunksX));
            _unstable.Clear();
            _unstableBatch.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));

            foreach (var c in _unstableBatch)
            {
                var cell = World[c.x, c.y];
                if (cell.IsOpen || !Content.MaterialSim[cell.Material].IsUnstable) continue;
                int2 down = c + new int2(0, -1);
                int2 dst;
                if (CanSandFallInto(down)) dst = down;
                else
                {
                    int s = _rng.NextBool() ? 1 : -1;
                    if (IsOpenCell(c + new int2(s, 0)) && CanSandFallInto(c + new int2(s, -1))) dst = c + new int2(s, -1);
                    else if (IsOpenCell(c + new int2(-s, 0)) && CanSandFallInto(c + new int2(-s, -1))) dst = c + new int2(-s, -1);
                    else continue;
                }
                MoveSand(c, dst);
                AddUnstable(dst);
                CheckUnstableAbove(c);
            }
        }

        bool IsOpenCell(int2 c) => World.InBounds(c.x, c.y) && World[c.x, c.y].IsOpen;

        /// <summary>Sand never falls into buildings, blueprints, storage or the royal chamber: they act as supports.</summary>
        bool CanSandFallInto(int2 c)
        {
            if (!World.InBounds(c.x, c.y)) return false;
            var cell = World[c.x, c.y];
            if (!cell.IsOpen) return false;
            if ((cell.Flags & (CellFlags.Storage | CellFlags.Blueprint)) != 0) return false;
            return Buildings.At(c) < 0 && !Colony.IsRoyalCell(c);
        }

        /// <summary>
        /// Moves an unstable cell from <paramref name="src"/> into the open cell <paramref name="dst"/>. The fallen
        /// cell is ordered for mining so workers clear it; fluid in <paramref name="dst"/> is pushed up into the gap.
        /// </summary>
        void MoveSand(int2 src, int2 dst)
        {
            int si = World.Index(src.x, src.y), di = World.Index(dst.x, dst.y);
            var s = World.Cells[si];
            var d = World.Cells[di];
            var revealed = (s.Flags | d.Flags) & CellFlags.Revealed;
            World.Cells[di] = new Cell(s.Material, s.Density, revealed | CellFlags.DigDesignated);
            World.Cells[si] = new Cell(s.Material, 0, (s.Flags & CellFlags.Revealed) | (d.Flags & CellFlags.Explored));
            World.MarkChanged(src.x, src.y, ChunkFlags.TerrainUpload | ChunkFlags.LightRecompute | ChunkFlags.FluidUpload);
            World.MarkChanged(dst.x, dst.y, ChunkFlags.TerrainUpload | ChunkFlags.LightRecompute | ChunkFlags.FluidUpload);
            World.Fluid[si] = World.Fluid[di];
            World.Fluid[di] = FluidCell.Empty;
            Fluids.Wake(dst);

            if (Colony.Designated.Remove(si)) _designationsChanged = true;
            Colony.Designated.Add(di);
            _designationsChanged = true;
            _terrainChanged = true;
            _crushed.Add(di);
            Events.Publish(new CaveInEvent(dst, s.Material));
        }

        // ---------------------------------------------------------------- cells turning solid

        /// <summary>
        /// Turns an open cell solid (finished wall, lava meeting water). A building there is removed and refunded,
        /// a storage cell is erased, fluid is pushed up; ants, enemies and piles inside are moved out afterwards.
        /// </summary>
        void Solidify(int2 c, ushort material, HashSet<int> into)
        {
            int index = World.Index(c.x, c.y);
            if (Buildings.At(c) >= 0) Buildings.Remove(c);
            var cell = World.Cells[index];
            if ((cell.Flags & CellFlags.Storage) != 0)
            {
                Colony.StorageCells.Remove(index);
                EvictStorage(c, index);
                _storageChanged = true;
                Buildings.MarkNetworkDirty();
            }
            if (Colony.Designated.Remove(index)) _designationsChanged = true;
            World.Cells[index] = new Cell(material, Cell.Full, cell.Flags & CellFlags.Revealed);
            World.MarkChanged(c.x, c.y, ChunkFlags.TerrainUpload | ChunkFlags.LightRecompute);
            _terrainChanged = true;
            Fluids.Displace(c);
            into.Add(index);
        }

        /// <summary>Finished walls become solid soil; the wall building is used up.</summary>
        void ProcessCompletedWalls()
        {
            var walls = Buildings.CompletedWalls;
            if (walls.Count == 0) return;
            foreach (int id in walls)
            {
                var b = Buildings.Get(id);
                if (!b.Alive || b.Kind != BuildingKind.Wall) continue;
                ushort fill = Content.Buildings[b.Def].FillMaterial;
                Buildings.Remove(b.Origin, refund: false);
                Solidify(b.Origin, fill, _filled);
                Events.Publish(new WallBuiltEvent(b.Origin));
            }
            walls.Clear();
            FlushSolidified();
        }

        /// <summary>Moves ants, enemies and piles out of cells that just turned solid; falling sand hurts what it buries.</summary>
        void FlushSolidified()
        {
            if (_crushed.Count > 0)
            {
                float damage = Config.combat.caveInDamage / math.max(0.01f, Colony.Mods.Mult(StatKind.HazardResistance));
                LiftOut(_crushed, damage);
                _crushed.Clear();
            }
            if (_filled.Count > 0)
            {
                LiftOut(_filled, 0f);
                _filled.Clear();
            }
        }

        void LiftOut(HashSet<int> cells, float damage)
        {
            var ants = Ants.Ants;
            for (int i = 0; i < ants.Length; i++)
            {
                var a = ants[i];
                if (a.IsDead || !cells.Contains(World.Index(a.Cell.x, a.Cell.y))) continue;
                var c = World.OpenAbove(a.Cell);
                a.Cell = a.From = c;
                a.Progress = 0f;
                a.Pos = a.PrevPos = new float2(c) + 0.5f;
                a.State = AntState.Idle;
                if (damage > 0f)
                {
                    a.Health -= damage;
                    a.SinceHurt = 0f;
                    if (a.Health <= 0f)
                    {
                        a.State = AntState.Dead;
                        a.Cause = DeathCause.Crushed;
                    }
                }
                ants[i] = a;
            }
            Enemies.LiftOutOfSolid(cells, damage);
            foreach (int index in cells)
            {
                var c = GridIndex.ToCell(index, World.ChunksX);
                Colony.Piles.Relocate(c, World.OpenAbove(c));
            }
        }

        // ---------------------------------------------------------------- deaths

        /// <summary>Removes ants that died this tick; what they carried drops where they fell.</summary>
        void RemoveDeadAnts()
        {
            var ants = Ants.Ants;
            for (int i = ants.Length - 1; i >= 0; i--)
            {
                var a = ants[i];
                if (!a.IsDead) continue;
                DropCarried(ref a, a.Cell);
                PutEggDown(ref a, a.Cell);
                Colony.Stats.AntsLost++;
                _alive[(int)a.Caste] = math.max(0, _alive[(int)a.Caste] - 1);
                if (a.Possessed)
                {
                    PossessInput = int2.zero;
                    Events.Publish(new PossessionEvent(PossessionPhase.Died, a.Caste, a.Cell));
                }
                Events.Publish(new AntDiedEvent(a.Cell, a.Caste, a.Cause, a.Cause == DeathCause.Killed ? a.Killer : -1));
                ants.RemoveAtSwapBack(i);
            }
        }
    }
}
