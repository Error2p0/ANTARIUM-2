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
    /// Shared BFS distance maps (one per destination kind) over walkable cells. Ants descend the gradient
    /// instead of each computing its own path, so cost does not grow with the number of ants.
    /// </summary>
    public sealed class FlowFields : IDisposable
    {
        public const ushort Unreachable = ushort.MaxValue;
        public const int KindCount = 4;

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
                handles[scheduled++] = new BfsJob
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

        [BurstCompile]
        struct BfsJob : IJob
        {
            public NavGrid Nav;
            [ReadOnly] public NativeArray<int2> Sources;
            public NativeArray<ushort> Distance;

            public unsafe void Execute()
            {
                UnsafeUtility.MemSet(Distance.GetUnsafePtr(), 0xFF, (long)Distance.Length * sizeof(ushort));
                var queue = new NativeList<int2>(1024, Allocator.Temp);

                for (int i = 0; i < Sources.Length; i++)
                {
                    int2 s = Sources[i];
                    if (!Nav.IsWalkable(s)) continue;
                    int index = Nav.Index(s);
                    if (Distance[index] == 0) continue;
                    Distance[index] = 0;
                    queue.Add(s);
                }

                for (int head = 0; head < queue.Length; head++)
                {
                    int2 c = queue[head];
                    int next = Distance[Nav.Index(c)] + 1;
                    if (next >= Unreachable) continue;
                    for (int d = 0; d < NavGrid.DirectionCount; d++)
                    {
                        int2 dir = NavGrid.Direction(d);
                        int2 n = c + dir;
                        if (!Nav.InBounds(n)) continue;
                        int ni = Nav.Index(n);
                        if (Distance[ni] != Unreachable) continue;
                        if (!Nav.CanMove(c, dir)) continue;
                        Distance[ni] = (ushort)next;
                        queue.Add(n);
                    }
                }
                queue.Dispose();
            }
        }
    }
}
