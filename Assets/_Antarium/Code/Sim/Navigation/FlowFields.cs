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
        /// <summary>Around the queen; idle ants stay close to it.</summary>
        Home,
    }

    /// <summary>
    /// Shared distance maps (one per destination kind) over walkable cells. Ants descend the gradient
    /// instead of each computing its own path, so cost does not grow with the number of ants.
    /// </summary>
    public sealed class FlowFields : IDisposable
    {
        public const ushort Unreachable = ushort.MaxValue;
        public const int KindCount = 4;
        /// <summary>Distance units per straight step; a diagonal step costs <see cref="DiagonalCost"/> (≈ √2 ratio).</summary>
        public const int StraightCost = 2;
        public const int DiagonalCost = 3;

        readonly NativeArray<ushort>[] _fields = new NativeArray<ushort>[KindCount];
        readonly NativeList<int2>[] _sources = new NativeList<int2>[KindCount];
        readonly bool[] _dirty = new bool[KindCount];

        public FlowFields(WorldGrid world)
        {
            int count = world.Width * world.Height;
            for (int i = 0; i < KindCount; i++)
            {
                _fields[i] = new NativeArray<ushort>(count, Allocator.Persistent);
                _sources[i] = new NativeList<int2>(256, Allocator.Persistent);
                _dirty[i] = true;
            }
            for (int i = 0; i < KindCount; i++)
                Fill(_fields[i], Unreachable);
        }

        public NativeArray<ushort> this[FieldKind kind] => _fields[(int)kind];

        public void MarkDirty(FieldKind kind) => _dirty[(int)kind] = true;

        public void MarkAllDirty()
        {
            for (int i = 0; i < KindCount; i++) _dirty[i] = true;
        }

        public bool IsDirty(FieldKind kind) => _dirty[(int)kind];

        /// <summary>Source list to refill before <see cref="Rebuild"/>; only used for dirty fields.</summary>
        public NativeList<int2> Sources(FieldKind kind) => _sources[(int)kind];

        /// <summary>Rebuilds all dirty fields in parallel from their current source lists.</summary>
        public void Rebuild(WorldGrid world)
        {
            var nav = new NavGrid(world);
            var handles = new NativeArray<JobHandle>(KindCount, Allocator.Temp);
            int scheduled = 0;
            for (int i = 0; i < KindCount; i++)
            {
                if (!_dirty[i]) continue;
                _dirty[i] = false;
                handles[scheduled++] = new FieldJob
                {
                    Nav = nav,
                    Sources = _sources[i].AsArray(),
                    Distance = _fields[i],
                }.Schedule();
            }
            JobHandle.CompleteAll(handles.GetSubArray(0, scheduled));
            handles.Dispose();
        }

        public ushort Get(FieldKind kind, WorldGrid world, int2 cell) =>
            world.InBounds(cell.x, cell.y) ? _fields[(int)kind][world.Index(cell.x, cell.y)] : Unreachable;

        static unsafe void Fill(NativeArray<ushort> array, ushort value)
        {
            UnsafeUtility.MemSet(array.GetUnsafePtr(), (byte)(value & 0xFF), (long)array.Length * sizeof(ushort));
        }

        public void Dispose()
        {
            for (int i = 0; i < KindCount; i++)
            {
                if (_fields[i].IsCreated) _fields[i].Dispose();
                if (_sources[i].IsCreated) _sources[i].Dispose();
            }
        }

        /// <summary>
        /// Dial's algorithm (bucket queue) with octile step costs: straight = 2, diagonal = 3. Max edge
        /// cost 3 < 4 buckets, so a ring of 4 lists is enough.
        /// </summary>
        [BurstCompile]
        struct FieldJob : IJob
        {
            public NavGrid Nav;
            [ReadOnly] public NativeArray<int2> Sources;
            public NativeArray<ushort> Distance;

            static NativeList<int2> Bucket(int d, NativeList<int2> b0, NativeList<int2> b1, NativeList<int2> b2, NativeList<int2> b3) =>
                (d & 3) switch { 0 => b0, 1 => b1, 2 => b2, _ => b3 };

            public unsafe void Execute()
            {
                UnsafeUtility.MemSet(Distance.GetUnsafePtr(), 0xFF, (long)Distance.Length * sizeof(ushort));
                var b0 = new NativeList<int2>(1024, Allocator.Temp);
                var b1 = new NativeList<int2>(1024, Allocator.Temp);
                var b2 = new NativeList<int2>(1024, Allocator.Temp);
                var b3 = new NativeList<int2>(1024, Allocator.Temp);

                int pending = 0;
                for (int i = 0; i < Sources.Length; i++)
                {
                    int2 s = Sources[i];
                    if (!Nav.IsWalkable(s)) continue;
                    int index = Nav.Index(s);
                    if (Distance[index] == 0) continue;
                    Distance[index] = 0;
                    b0.Add(s);
                    pending++;
                }

                for (int d = 0; pending > 0 && d < Unreachable; d++)
                {
                    var bucket = Bucket(d, b0, b1, b2, b3);
                    // Pushes from distance d land in d+2 / d+3, never in this bucket, so it does not grow here.
                    for (int i = 0; i < bucket.Length; i++)
                    {
                        pending--;
                        int2 c = bucket[i];
                        if (Distance[Nav.Index(c)] != d) continue; // stale: reached cheaper later
                        for (int k = 0; k < NavGrid.DirectionCount; k++)
                        {
                            int2 dir = NavGrid.Direction(k);
                            int2 n = c + dir;
                            if (!Nav.InBounds(n)) continue;
                            int next = d + (k < 4 ? StraightCost : DiagonalCost);
                            if (next >= Unreachable) continue;
                            int ni = Nav.Index(n);
                            if (Distance[ni] <= next) continue;
                            if (!Nav.CanMove(c, dir)) continue;
                            Distance[ni] = (ushort)next;
                            Bucket(next, b0, b1, b2, b3).Add(n);
                            pending++;
                        }
                    }
                    bucket.Clear();
                }

                b0.Dispose();
                b1.Dispose();
                b2.Dispose();
                b3.Dispose();
            }
        }
    }
}
