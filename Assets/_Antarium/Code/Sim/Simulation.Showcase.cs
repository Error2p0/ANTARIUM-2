using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// Instant edits for building showcase colonies (store media capture, editor tools, tests): carving without
    /// ants, placing finished buildings without paying, spawning ants anywhere, finishing research. They go through
    /// the same code paths as play (drops, reveals, cave breaches, creatures), only without the waiting.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>
        /// Digs out every destructible cell within <paramref name="radius"/> of <paramref name="cellPos"/> at once.
        /// The brush is revealed first, so carving into the fog works; caves broken into are discovered as usual.
        /// </summary>
        public void CarveNow(float2 cellPos, float radius)
        {
            Reveal.Around(World, (int2)math.floor(cellPos), (int)math.ceil(radius) + 1);
            ResolveDig(new DigCommand(cellPos, radius, 1e5f)); // big enough for any material, small enough not to overflow the damage maths
        }

        /// <summary>Carves a tunnel from <paramref name="from"/> to <paramref name="to"/> (cell space) with a round brush.</summary>
        public void CarveLineNow(float2 from, float2 to, float radius)
        {
            float length = math.distance(from, to);
            int steps = math.max(1, (int)math.ceil(length / math.max(0.25f, radius * 0.5f)));
            for (int i = 0; i <= steps; i++)
                CarveNow(math.lerp(from, to, i / (float)steps), radius);
        }

        /// <summary>Digs out every cell of the rectangle [<paramref name="min"/>, <paramref name="max"/>].</summary>
        public void CarveRectNow(int2 min, int2 max)
        {
            for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
                CarveNow(new float2(x + 0.5f, y + 0.5f), 0.5f);
        }

        /// <summary>Fills the rectangle with solid, revealed <paramref name="material"/> (any fluid there is removed).</summary>
        public void FillRectNow(int2 min, int2 max, ushort material)
        {
            for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
            {
                if (!World.InBounds(x, y)) continue;
                int i = World.Index(x, y);
                World.Fluid[i] = FluidCell.Empty;
                EditCell(new int2(x, y), new Cell(material, Cell.Full, CellFlags.Revealed));
            }
            LiftAntsOutOfSolid(min, max);
        }

        /// <summary>Carves an ellipse with radii <paramref name="radii"/> (cells) around <paramref name="center"/>.</summary>
        public void CarveEllipseNow(float2 center, float2 radii)
        {
            int2 min = (int2)math.floor(center - radii), max = (int2)math.ceil(center + radii);
            for (int pass = 0; pass < 2; pass++)
            for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
            {
                float2 d = (new float2(x + 0.5f, y + 0.5f) - center) / radii;
                if (math.lengthsq(d) <= 1f) CarveNow(new float2(x + 0.5f, y + 0.5f), 0.6f);
            }
        }

        /// <summary>Removes every loose item pile (carving drops a lot of them).</summary>
        public void ClearPilesNow() => Colony.Piles.Clear();

        /// <summary>Removes all fluid inside the rectangle.</summary>
        public void ClearFluidNow(int2 min, int2 max)
        {
            for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
            {
                if (!World.InBounds(x, y)) continue;
                int i = World.Index(x, y);
                if (World.Fluid[i].IsEmpty) continue;
                World.Fluid[i] = FluidCell.Empty;
                World.MarkChanged(x, y, ChunkFlags.FluidUpload | ChunkFlags.LightRecompute);
                Fluids.Wake(new int2(x, y));
            }
        }

        /// <summary>Reveals every cell within <paramref name="radius"/> of <paramref name="center"/> (no digging).</summary>
        public void RevealNow(int2 center, int radius) => Reveal.Around(World, center, radius);

        /// <summary>
        /// Places a finished building without paying, unlocking or waiting for builders. Returns the placement check
        /// (cost and lock are ignored); the building is only created when the spot itself is fine.
        /// </summary>
        public PlaceResult PlaceNow(int def, int2 origin, int2 dir, ushort filter = 0)
        {
            bool unlocked = Colony.BuildingUnlocked[def];
            Colony.BuildingUnlocked[def] = true;
            var result = Buildings.CanPlace(def, origin, dir);
            Colony.BuildingUnlocked[def] = unlocked;
            if (result != PlaceResult.Ok && result != PlaceResult.CantAfford) return result;
            Buildings.Restore(def, origin, dir, filter, BuildingState.Active, 0f);
            Buildings.MarkNetworkDirty();
            _forceFields = true;
            return PlaceResult.Ok;
        }

        /// <summary>Spawns ants of <paramref name="caste"/> on <paramref name="cell"/> (which should be open).</summary>
        public void SpawnAntsAt(int2 cell, int count, AntCaste caste)
        {
            for (int i = 0; i < count; i++)
                Ants.Spawn(cell, Seed, caste);
            Ants.CountCastes(_alive, _aliveWithEggs);
        }

        /// <summary>Completes every research node (effects applied, nothing paid).</summary>
        public void ResearchAllNow()
        {
            for (int r = 0; r < Content.Research.Length; r++)
                Research.Apply(r);
            Buildings.MarkNetworkDirty();
            _forceFields = true;
        }

        /// <summary>Lays an egg on <paramref name="cell"/> of <paramref name="caste"/> (royal chamber floor for a natural look).</summary>
        public void LayEggNow(int2 cell, AntCaste caste, float age = 0f)
        {
            Colony.Eggs.Add(new Egg { Cell = cell, Caste = caste, Age = age, Nursery = -1 });
            Ants.CountCastes(_alive, _aliveWithEggs);
        }

        /// <summary>Runs <paramref name="seconds"/> of game time without rendering (the fixed tick, as fast as it goes).</summary>
        public void FastForward(float seconds)
        {
            int ticks = (int)math.ceil(seconds / TickDuration);
            for (int t = 1; t <= ticks; t++)
            {
                Tick();
                Events.Flush();
                if (t % 20 == 0) UpdateDerived();
            }
            UpdateDerived();
        }
    }
}
