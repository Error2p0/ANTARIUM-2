using System;
using Antarium.Content;
using Antarium.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// Grid light map. Light spreads from the sky (open Air cells) and from emissive materials, losing
    /// <c>openCost</c> per open cell and <c>solidCost</c> per solid cell. Stored chunk-major, one packed
    /// RGBA8 uint per cell (alpha unused), so it uploads to the GPU the same way as the cells.
    ///
    /// Because light reach is bounded (maxLevel / openCost cells, measured as Chebyshev distance since every
    /// step, straight or diagonal, costs at least openCost), a change only affects cells within that radius,
    /// and only sources within twice that radius can contribute, so updates are exact and local.
    /// </summary>
    public sealed class LightSystem : IDisposable
    {
        readonly LightConfig _config;
        public NativeArray<uint> Light;

        public LightSystem(WorldGrid world, LightConfig config)
        {
            _config = config;
            Light = new NativeArray<uint>(world.Width * world.Height, Allocator.Persistent);
        }

        public int Reach => (_config.maxLevel + _config.openCost - 1) / _config.openCost;

        public void RecomputeAll(WorldGrid world, NativeArray<MaterialSim> materials)
        {
            var all = new int4(0, 0, world.Width, world.Height);
            Compute(world, materials, all, all);
            for (int i = 0; i < world.Chunks.Length; i++)
                world.Chunks[i] = (world.Chunks[i] & ~ChunkFlags.LightRecompute) | ChunkFlags.LightUpload;
        }

        /// <summary>Recomputes light around chunks flagged <see cref="ChunkFlags.LightRecompute"/>. Returns true if anything ran.</summary>
        public bool UpdateDirty(WorldGrid world, NativeArray<MaterialSim> materials)
        {
            int2 min = int.MaxValue, max = int.MinValue;
            for (int i = 0; i < world.Chunks.Length; i++)
            {
                if ((world.Chunks[i] & ChunkFlags.LightRecompute) == 0) continue;
                world.Chunks[i] &= ~ChunkFlags.LightRecompute;
                var c = new int2(i % world.ChunksX, i / world.ChunksX);
                min = math.min(min, c);
                max = math.max(max, c);
            }
            if (min.x == int.MaxValue) return false;

            int reach = Reach;
            int2 worldMax = new int2(world.Width, world.Height);
            int2 dstMin = math.max(min * WorldConstants.ChunkSize - reach, 0);
            int2 dstMax = math.min((max + 1) * WorldConstants.ChunkSize + reach, worldMax);
            int2 srcMin = math.max(dstMin - reach, 0);
            int2 srcMax = math.min(dstMax + reach, worldMax);
            Compute(world, materials, new int4(srcMin, srcMax), new int4(dstMin, dstMax));

            int2 cMin = dstMin >> WorldConstants.ChunkShift;
            int2 cMax = (dstMax - 1) >> WorldConstants.ChunkShift;
            for (int cy = cMin.y; cy <= cMax.y; cy++)
            for (int cx = cMin.x; cx <= cMax.x; cx++)
                world.Chunks[cy * world.ChunksX + cx] |= ChunkFlags.LightUpload;
            return true;
        }

        /// <summary>Light at a cell as 0..1 RGB.</summary>
        public float3 Sample(WorldGrid world, int x, int y)
        {
            if (!world.InBounds(x, y)) return 0f;
            uint v = Light[world.Index(x, y)];
            return new float3(v & 0xFF, (v >> 8) & 0xFF, (v >> 16) & 0xFF) / 255f;
        }

        void Compute(WorldGrid world, NativeArray<MaterialSim> materials, int4 src, int4 dst)
        {
            int count = (src.z - src.x) * (src.w - src.y);
            var level = new NativeArray<byte>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var cost = new NativeArray<byte>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var heads = new NativeArray<int>(_config.maxLevel + 1, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var nodes = new NativeList<int2>(count / 4 + 64, Allocator.TempJob);
            try
            {
                var sky = _config.skyLight;
                new PropagateJob
                {
                    Cells = world.Cells,
                    Materials = materials,
                    Light = Light,
                    ChunksX = world.ChunksX,
                    Src = src,
                    Dst = dst,
                    MaxLevel = _config.maxLevel,
                    OpenCost = (byte)math.min(_config.openCost, 255),
                    SolidCost = (byte)math.min(_config.solidCost, 255),
                    Sky = new float3(sky.r, sky.g, sky.b),
                    Level = level,
                    Cost = cost,
                    Heads = heads,
                    Nodes = nodes,
                }.Run();
            }
            finally
            {
                level.Dispose();
                cost.Dispose();
                heads.Dispose();
                nodes.Dispose();
            }
        }

        public void Dispose()
        {
            if (Light.IsCreated) Light.Dispose();
        }

        /// <summary>
        /// Bucket-queue flood fill per colour channel over the source rect; results are written only
        /// inside the destination rect (cells near the source border may be incomplete).
        /// </summary>
        [BurstCompile]
        struct PropagateJob : IJob
        {
            [ReadOnly] public NativeArray<Cell> Cells;
            [ReadOnly] public NativeArray<MaterialSim> Materials;
            public NativeArray<uint> Light;

            public int ChunksX;
            public int4 Src; // xMin, yMin, xMax (exclusive), yMax (exclusive)
            public int4 Dst;
            public int MaxLevel;
            public byte OpenCost;
            public byte SolidCost;
            public float3 Sky;

            public NativeArray<byte> Level;
            public NativeArray<byte> Cost;
            public NativeArray<int> Heads;
            public NativeList<int2> Nodes; // x = local cell index, y = next node in the same bucket

            public void Execute()
            {
                int w = Src.z - Src.x;
                int h = Src.w - Src.y;

                for (int ly = 0; ly < h; ly++)
                for (int lx = 0; lx < w; lx++)
                {
                    var cell = Cells[GridIndex.Of(Src.x + lx, Src.y + ly, ChunksX)];
                    Cost[ly * w + lx] = cell.IsOpen ? OpenCost : SolidCost;
                }

                for (int channel = 0; channel < 3; channel++)
                {
                    Nodes.Clear();
                    for (int i = 0; i < Heads.Length; i++) Heads[i] = -1;

                    for (int ly = 0; ly < h; ly++)
                    for (int lx = 0; lx < w; lx++)
                    {
                        int local = ly * w + lx;
                        byte e = Emission(Cells[GridIndex.Of(Src.x + lx, Src.y + ly, ChunksX)], channel);
                        Level[local] = e;
                        if (e > 0) Push(e, local);
                    }

                    for (int l = MaxLevel; l > 0; l--)
                    {
                        int node = Heads[l];
                        while (node != -1)
                        {
                            int2 n = Nodes[node];
                            node = n.y;
                            int i = n.x;
                            if (Level[i] != l) continue; // stale entry; a brighter path already won
                            int lx = i % w;
                            int ly = i / w;
                            bool left = lx > 0, right = lx < w - 1, down = ly > 0, up = ly < h - 1;
                            if (left) Relax(i - 1, l, false);
                            if (right) Relax(i + 1, l, false);
                            if (down) Relax(i - w, l, false);
                            if (up) Relax(i + w, l, false);
                            // Diagonals at ~1.5x cost make falloff octagonal instead of a diamond.
                            if (left && down) Relax(i - w - 1, l, true);
                            if (right && down) Relax(i - w + 1, l, true);
                            if (left && up) Relax(i + w - 1, l, true);
                            if (right && up) Relax(i + w + 1, l, true);
                        }
                    }

                    int shift = channel * 8;
                    uint mask = ~(0xFFu << shift);
                    for (int y = Dst.y; y < Dst.w; y++)
                    for (int x = Dst.x; x < Dst.z; x++)
                    {
                        int local = (y - Src.y) * w + (x - Src.x);
                        uint v = (uint)(Level[local] * 255 / MaxLevel);
                        int index = GridIndex.Of(x, y, ChunksX);
                        Light[index] = (Light[index] & mask) | (v << shift);
                    }
                }
            }

            byte Emission(Cell cell, int channel)
            {
                var m = Materials[cell.Material];
                float e;
                if (m.IsAir) e = cell.IsOpen ? Sky[channel] : 0f;
                else e = cell.IsEmpty ? 0f : m.Emission[channel];
                return (byte)math.clamp((int)math.round(e * MaxLevel), 0, MaxLevel);
            }

            void Relax(int neighbour, int level, bool diagonal)
            {
                int cost = Cost[neighbour];
                int next = level - (diagonal ? (cost * 3 + 1) / 2 : cost);
                if (next <= Level[neighbour]) return;
                Level[neighbour] = (byte)next;
                Push(next, neighbour);
            }

            void Push(int level, int local)
            {
                Nodes.Add(new int2(local, Heads[level]));
                Heads[level] = Nodes.Length - 1;
            }
        }
    }
}
