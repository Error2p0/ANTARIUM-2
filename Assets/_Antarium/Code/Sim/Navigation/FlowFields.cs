using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Antarium.Sim
{
    public enum FieldKind
    {
        /// <summary>Walkable cells next to a cell designated for mining.</summary>
        Dig,
        /// <summary>Cells holding item piles the colony can accept.</summary>
        Items,
        /// <summary>Royal chamber and storage zones.</summary>
        Dropoff,
        /// <summary>Around the queen; idle ants stay close to it. Ignores pheromones (raiders use it too).</summary>
        Home,
        /// <summary>Construction sites (blueprint cells).</summary>
        Build,
        /// <summary>Enemies (creatures and raiders): soldiers hunt down this field.</summary>
        Enemies,
        /// <summary>Guard posts and Defend scent: idle soldiers wait here.</summary>
        Guard,
        /// <summary>Every ant: hunting creatures descend this field.</summary>
        Ants,
        /// <summary>Where the current raid came from: raiders leave along this field.</summary>
        Exit,
        /// <summary>Eggs nurses can carry to a nursery.</summary>
        Brood,
        /// <summary>Nurseries with room for more eggs.</summary>
        Nursery,
        /// <summary>The edge of the known tunnels: scouts walk here to sense the rock beyond.</summary>
        Frontier,
        /// <summary>Machines that want an engineer.</summary>
        Machines,
        /// <summary>Golden beetles (scouts chase them).</summary>
        Critters,
        /// <summary>Where a Pheromone Burst calls the ants.</summary>
        Rally,
    }

    /// <summary>
    /// Shared distance maps over walkable cells: one per destination kind, plus per item that some building
    /// consumes a "consumer" field (machines needing it, belts that can carry it there) and a "supply" field
    /// (stock containers holding it). Ants descend the gradient instead of each computing its own path, so cost
    /// does not grow with the number of ants. All fields live in one array (field-major) so a Burst job can read
    /// any of them.
    ///
    /// Pheromones change step costs for the fields ants use (not those enemies follow): a step along a trail costs
    /// about half, a step into or out of an Avoid area four times as much. "Late" sources start at an offset
    /// instead of 0, so jobs inside Dig / Gather scent are preferred over plain ones up to that far away.
    /// </summary>
    public sealed class FlowFields : IDisposable
    {
        public const ushort Unreachable = ushort.MaxValue;
        /// <summary>Number of <see cref="FieldKind"/> fields; consumer fields follow them.</summary>
        public const int KindCount = 15;
        /// <summary>Distance units per straight step; a diagonal step costs <see cref="DiagonalCost"/> (≈ √2 ratio).</summary>
        public const int StraightCost = 2;
        public const int DiagonalCost = 3;
        /// <summary>Step costs along a trail (both cells scented).</summary>
        public const int TrailStraight = 1, TrailDiagonal = 2;
        /// <summary>Steps touching an Avoid cell cost this many times as much.</summary>
        public const int AvoidFactor = 4;
        /// <summary>Bucket ring size: larger than the most expensive step.</summary>
        const int Buckets = 16;

        public readonly int FieldCount;
        public readonly int CellCount;

        NativeArray<ushort> _all;
        readonly NativeList<int2>[] _sources;
        readonly NativeList<int2>[] _late;
        readonly int[] _lateOffset;
        readonly bool[] _dirty;
        readonly bool[] _usePheromones;
        NativeArray<byte> _noScent;

        public FlowFields(WorldGrid world, int consumerSlots = 0)
        {
            CellCount = world.Width * world.Height;
            FieldCount = KindCount + math.max(0, consumerSlots);
            _all = new NativeArray<ushort>(CellCount * FieldCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _sources = new NativeList<int2>[FieldCount];
            _late = new NativeList<int2>[FieldCount];
            _lateOffset = new int[FieldCount];
            _dirty = new bool[FieldCount];
            _usePheromones = new bool[FieldCount];
            for (int i = 0; i < FieldCount; i++)
            {
                _sources[i] = new NativeList<int2>(256, Allocator.Persistent);
                _late[i] = new NativeList<int2>(16, Allocator.Persistent);
                _dirty[i] = true;
                _usePheromones[i] = true;
            }
            // Fields enemies follow (and soldiers' "next to an enemy" test) keep plain costs.
            _usePheromones[(int)FieldKind.Home] = false;
            _usePheromones[(int)FieldKind.Enemies] = false;
            _usePheromones[(int)FieldKind.Ants] = false;
            _usePheromones[(int)FieldKind.Exit] = false;
            _noScent = new NativeArray<byte>(1, Allocator.Persistent);
            Fill(_all, Unreachable);
        }

        /// <summary>Field index of the consumer field for a consumer slot (see <c>ContentDB.ConsumerSlot</c>).</summary>
        public static int ConsumerField(int slot) => KindCount + slot;

        /// <summary>Field index of the supply field (stock holding the item) for a consumer slot; follows all consumer fields.</summary>
        public static int SupplyField(int slot, int slotCount) => KindCount + slotCount + slot;

        /// <summary>All fields, field-major: field f, cell index i is at f * CellCount + i.</summary>
        public NativeArray<ushort> All => _all;

        public NativeArray<ushort> this[FieldKind kind] => Field((int)kind);
        public NativeArray<ushort> Field(int field) => _all.GetSubArray(field * CellCount, CellCount);

        public void MarkDirty(FieldKind kind) => _dirty[(int)kind] = true;
        public void MarkDirty(int field) => _dirty[field] = true;

        public void MarkAllDirty()
        {
            for (int i = 0; i < FieldCount; i++) _dirty[i] = true;
        }

        /// <summary>Every field whose costs depend on pheromones (painted trails or Avoid areas changed).</summary>
        public void MarkScentedDirty()
        {
            for (int i = 0; i < FieldCount; i++)
                if (_usePheromones[i]) _dirty[i] = true;
        }

        public bool IsDirty(FieldKind kind) => _dirty[(int)kind];
        public bool IsDirty(int field) => _dirty[field];

        /// <summary>Source list to refill before <see cref="Rebuild"/>; only used for dirty fields.</summary>
        public NativeList<int2> Sources(FieldKind kind) => _sources[(int)kind];
        public NativeList<int2> Sources(int field) => _sources[field];

        /// <summary>
        /// Sources that start at <paramref name="offset"/> instead of 0 (cleared with the field's sources each rebuild
        /// by the caller). Used to rank plain jobs behind prioritised ones.
        /// </summary>
        public NativeList<int2> LateSources(FieldKind kind, int offset)
        {
            _lateOffset[(int)kind] = math.clamp(offset, 0, Unreachable - 1);
            return _late[(int)kind];
        }

        /// <summary>Rebuilds all dirty fields in parallel from their current source lists.</summary>
        public void Rebuild(WorldGrid world, PheromoneSystem pheromones = null)
        {
            var nav = new NavGrid(world);
            bool scent = pheromones != null;
            var trail = scent ? pheromones.Of(Antarium.Content.PheromoneKind.Trail) : _noScent;
            var avoid = scent ? pheromones.Of(Antarium.Content.PheromoneKind.Avoid) : _noScent;
            var handles = new NativeArray<JobHandle>(FieldCount, Allocator.Temp);
            int scheduled = 0;
            for (int i = 0; i < FieldCount; i++)
            {
                if (!_dirty[i]) continue;
                _dirty[i] = false;
                handles[scheduled++] = new FieldJob
                {
                    Nav = nav,
                    Sources = _sources[i].AsArray(),
                    Late = _late[i].AsArray(),
                    LateOffset = _lateOffset[i],
                    Distance = Field(i),
                    Trail = trail,
                    Avoid = avoid,
                    UsePheromones = scent && _usePheromones[i],
                }.Schedule();
            }
            JobHandle.CompleteAll(handles.GetSubArray(0, scheduled));
            handles.Dispose();
        }

        public ushort Get(FieldKind kind, WorldGrid world, int2 cell) => Get((int)kind, world, cell);

        public ushort Get(int field, WorldGrid world, int2 cell) =>
            world.InBounds(cell.x, cell.y) ? _all[field * CellCount + world.Index(cell.x, cell.y)] : Unreachable;

        static unsafe void Fill(NativeArray<ushort> array, ushort value)
        {
            UnsafeUtility.MemSet(array.GetUnsafePtr(), (byte)(value & 0xFF), (long)array.Length * sizeof(ushort));
        }

        public void Dispose()
        {
            if (_all.IsCreated) _all.Dispose();
            if (_noScent.IsCreated) _noScent.Dispose();
            for (int i = 0; i < FieldCount; i++)
            {
                if (_sources[i].IsCreated) _sources[i].Dispose();
                if (_late[i].IsCreated) _late[i].Dispose();
            }
        }

        /// <summary>
        /// Dial's algorithm (bucket queue). Plain steps cost 2 / 3 (straight / diagonal); with pheromones a step along
        /// a trail costs 1 / 2 and a step touching an Avoid cell 4x. The most expensive step (12) is below the ring
        /// size (16), so a ring of buckets is enough.
        /// </summary>
        [BurstCompile]
        struct FieldJob : IJob
        {
            public NavGrid Nav;
            [ReadOnly] public NativeArray<int2> Sources;
            [ReadOnly] public NativeArray<int2> Late;
            public int LateOffset;
            // Each job writes its own slice of the shared field array.
            [NativeDisableContainerSafetyRestriction] public NativeArray<ushort> Distance;
            [ReadOnly] public NativeArray<byte> Trail;
            [ReadOnly] public NativeArray<byte> Avoid;
            public bool UsePheromones;

            public unsafe void Execute()
            {
                UnsafeUtility.MemSet(Distance.GetUnsafePtr(), 0xFF, (long)Distance.Length * sizeof(ushort));
                var buckets = new NativeArray<UnsafeList<int2>>(Buckets, Allocator.Temp);
                for (int b = 0; b < Buckets; b++) buckets[b] = new UnsafeList<int2>(256, Allocator.Temp);

                int pending = 0;
                for (int i = 0; i < Sources.Length; i++) pending += Seed(Sources[i], 0, buckets);
                bool lateDone = Late.Length == 0;

                for (int d = 0; (pending > 0 || !lateDone) && d < Unreachable; d++)
                {
                    if (!lateDone && d == LateOffset)
                    {
                        for (int i = 0; i < Late.Length; i++) pending += Seed(Late[i], d, buckets);
                        lateDone = true;
                    }
                    var bucket = buckets[d & (Buckets - 1)];
                    // Pushes from distance d land in d+1 .. d+12, never in this bucket, so it does not grow here.
                    for (int i = 0; i < bucket.Length; i++)
                    {
                        pending--;
                        int2 c = bucket[i];
                        int ci = Nav.Index(c);
                        if (Distance[ci] != d) continue; // stale: reached cheaper later
                        for (int k = 0; k < NavGrid.DirectionCount; k++)
                        {
                            int2 dir = NavGrid.Direction(k);
                            int2 n = c + dir;
                            if (!Nav.InBounds(n)) continue;
                            int ni = Nav.Index(n);
                            int next = d + StepCost(ci, ni, k < 4);
                            if (next >= Unreachable) continue;
                            if (Distance[ni] <= next) continue;
                            if (!Nav.CanMove(c, dir)) continue;
                            Distance[ni] = (ushort)next;
                            var target = buckets[next & (Buckets - 1)];
                            target.Add(n);
                            buckets[next & (Buckets - 1)] = target;
                            pending++;
                        }
                    }
                    bucket.Clear();
                    buckets[d & (Buckets - 1)] = bucket;
                }

                for (int b = 0; b < Buckets; b++) buckets[b].Dispose();
                buckets.Dispose();
            }

            int Seed(int2 s, int d, NativeArray<UnsafeList<int2>> buckets)
            {
                if (!Nav.IsWalkable(s)) return 0;
                int index = Nav.Index(s);
                if (Distance[index] <= d) return 0;
                Distance[index] = (ushort)d;
                var list = buckets[d & (Buckets - 1)];
                list.Add(s);
                buckets[d & (Buckets - 1)] = list;
                return 1;
            }

            int StepCost(int a, int b, bool straight)
            {
                if (!UsePheromones) return straight ? StraightCost : DiagonalCost;
                if (Avoid[a] != 0 || Avoid[b] != 0) return (straight ? StraightCost : DiagonalCost) * AvoidFactor;
                if (Trail[a] != 0 && Trail[b] != 0) return straight ? TrailStraight : TrailDiagonal;
                return straight ? StraightCost : DiagonalCost;
            }
        }
    }
}
