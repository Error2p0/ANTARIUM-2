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
    /// RGBA8 uint per cell, so it uploads to the GPU the same way as the cells: RGB is the light of emissive
    /// materials and lava, alpha the sky light (a level, coloured and scaled by the time of day in the shaders,
    /// so day and night never need a relight).
    ///
    /// Because light reach is bounded (maxLevel / openCost cells, measured as Chebyshev distance since every
    /// step, straight or diagonal, costs at least openCost), a change only affects cells within that radius,
    /// and only sources within twice that radius can contribute, so updates are exact and local.
    /// </summary>
    public sealed class LightSystem : IDisposable
    {
        readonly LightConfig _config;
        public NativeArray<uint> Light;
        /// <summary>Light emitted by a cell holding lava (fraction of the max level per channel).</summary>
        public float3 LavaEmission;
        /// <summary>Light emitted by a burning cell (fraction of the max level per channel).</summary>
        public float3 FireEmission;

        /// <summary>
        /// Light given off by things in open cells that are not terrain (glowing mushrooms, Milestone 11): cell index to its
        /// cell and RGB (a fraction of the max level). Kept by whoever owns those things; not saved. Mirrored into two native
        /// lists for the job whenever it changes.
        /// </summary>
        readonly System.Collections.Generic.Dictionary<int, (int2 Cell, float3 Rgb)> _extra = new();
        NativeList<int2> _extraCells;
        NativeList<float3> _extraRgb;
        bool _extraDirty;

        public LightSystem(WorldGrid world, LightConfig config)
        {
            _config = config;
            Light = new NativeArray<uint>(world.Width * world.Height, Allocator.Persistent);
            _extraCells = new NativeList<int2>(64, Allocator.Persistent);
            _extraRgb = new NativeList<float3>(64, Allocator.Persistent);
        }

        /// <summary>Number of extra emitters (glowing mushrooms).</summary>
        public int EmitterCount => _extra.Count;

        /// <summary>
        /// Sets (or with a black <paramref name="rgb"/>, clears) the extra light emitted at <paramref name="cell"/>; the light
        /// around it is recomputed with the next <see cref="UpdateDirty"/>.
        /// </summary>
        public void SetEmitter(WorldGrid world, int2 cell, float3 rgb)
        {
            if (!world.InBounds(cell.x, cell.y)) return;
            int index = world.Index(cell.x, cell.y);
            if (math.cmax(rgb) > 0f)
            {
                if (_extra.TryGetValue(index, out var old) && old.Rgb.Equals(rgb)) return;
                _extra[index] = (cell, rgb);
            }
            else if (!_extra.Remove(index)) return;
            _extraDirty = true;
            world.MarkChanged(cell.x, cell.y, ChunkFlags.LightRecompute);
        }

        /// <summary>Removes every extra emitter (without relighting; a new colony or a load follows).</summary>
        public void ClearEmitters()
        {
            _extra.Clear();
            _extraDirty = true;
        }

        public int Reach => (_config.maxLevel + _config.openCost - 1) / _config.openCost;

        public void RecomputeAll(WorldGrid world, NativeArray<MaterialSim> materials)
        {
            var all = new int4(0, 0, world.Width, world.Height);
            Compute(world, materials, all, all);
            for (int i = 0; i < world.Chunks.Length; i++)
                world.Chunks[i] = (world.Chunks[i] & ~ChunkFlags.LightRecompute) | ChunkFlags.LightUpload;
        }

        readonly System.Collections.Generic.List<int4> _clusters = new();

        /// <summary>
        /// Recomputes light around chunks flagged <see cref="ChunkFlags.LightRecompute"/>. Returns true if anything ran.
        /// Dirty chunks are grouped into clusters (chunks within two chunks of each other's box) and each cluster is
        /// relit on its own, so changes far apart (a mushroom regrowing in another cave while you dig) don't relight
        /// everything between them. Each region is computed from scratch, so the result is exact whatever the grouping.
        /// </summary>
        public bool UpdateDirty(WorldGrid world, NativeArray<MaterialSim> materials)
        {
            _clusters.Clear();
            for (int i = 0; i < world.Chunks.Length; i++)
            {
                if ((world.Chunks[i] & ChunkFlags.LightRecompute) == 0) continue;
                world.Chunks[i] &= ~ChunkFlags.LightRecompute;
                var c = new int2(i % world.ChunksX, i / world.ChunksX);
                _clusters.Add(new int4(c, c));
            }
            if (_clusters.Count == 0) return false;

            // Merge clusters whose boxes come within MergeGap chunks of each other until none do.
            const int MergeGap = 2;
            for (bool merged = true; merged;)
            {
                merged = false;
                for (int a = 0; a < _clusters.Count; a++)
                for (int b = _clusters.Count - 1; b > a; b--)
                {
                    int4 p = _clusters[a], q = _clusters[b];
                    if (q.x > p.z + MergeGap || p.x > q.z + MergeGap || q.y > p.w + MergeGap || p.y > q.w + MergeGap) continue;
                    _clusters[a] = new int4(math.min(p.xy, q.xy), math.max(p.zw, q.zw));
                    _clusters.RemoveAt(b);
                    merged = true;
                }
            }

            int reach = Reach;
            int2 worldMax = new int2(world.Width, world.Height);
            // Many scattered clusters (all the mushrooms of a new world): one box over them all is cheaper.
            int4 all = _clusters[0];
            long separate = 0;
            foreach (var box in _clusters)
            {
                all = new int4(math.min(all.xy, box.xy), math.max(all.zw, box.zw));
                separate += SourceArea(box, reach, worldMax);
            }
            if (_clusters.Count > 1 && separate >= SourceArea(all, reach, worldMax))
            {
                _clusters.Clear();
                _clusters.Add(all);
            }
            foreach (var box in _clusters)
            {
                int2 dstMin = math.max(box.xy * WorldConstants.ChunkSize - reach, 0);
                int2 dstMax = math.min((box.zw + 1) * WorldConstants.ChunkSize + reach, worldMax);
                int2 srcMin = math.max(dstMin - reach, 0);
                int2 srcMax = math.min(dstMax + reach, worldMax);
                Compute(world, materials, new int4(srcMin, srcMax), new int4(dstMin, dstMax));

                int2 cMin = dstMin >> WorldConstants.ChunkShift;
                int2 cMax = (dstMax - 1) >> WorldConstants.ChunkShift;
                for (int cy = cMin.y; cy <= cMax.y; cy++)
                for (int cx = cMin.x; cx <= cMax.x; cx++)
                    world.Chunks[cy * world.ChunksX + cx] |= ChunkFlags.LightUpload;
            }
            return true;
        }

        /// <summary>Cells the flood fill reads to relight a box of chunks (the box plus twice the light's reach around it).</summary>
        static long SourceArea(int4 box, int reach, int2 worldMax)
        {
            int2 lo = math.max(box.xy * WorldConstants.ChunkSize - 2 * reach, 0);
            int2 hi = math.min((box.zw + 1) * WorldConstants.ChunkSize + 2 * reach, worldMax);
            return (long)(hi.x - lo.x) * (hi.y - lo.y);
        }

        /// <summary>Sky light scale used by <see cref="Sample(WorldGrid, int, int)"/> (the presentation sets it from the time of day).</summary>
        public float SkyScale = 1f;

        /// <summary>Light at a cell as 0..1 RGB, with the sky at <see cref="SkyScale"/>.</summary>
        public float3 Sample(WorldGrid world, int x, int y) => Sample(world, x, y, SkyScale);

        /// <summary>Light at a cell as 0..1 RGB, with the sky at <paramref name="daylight"/> (1 = full sky light).</summary>
        public float3 Sample(WorldGrid world, int x, int y, float daylight)
        {
            if (!world.InBounds(x, y)) return 0f;
            uint v = Light[world.Index(x, y)];
            var sky = _config.skyLight;
            float3 emitted = new float3(v & 0xFF, (v >> 8) & 0xFF, (v >> 16) & 0xFF) / 255f;
            return math.max(emitted, ((v >> 24) & 0xFF) / 255f * daylight * new float3(sky.r, sky.g, sky.b));
        }

        /// <summary>Sky light level at a cell (0..1), before the time of day.</summary>
        public float SkyAt(WorldGrid world, int x, int y) =>
            world.InBounds(x, y) ? ((Light[world.Index(x, y)] >> 24) & 0xFF) / 255f : 0f;

        void Compute(WorldGrid world, NativeArray<MaterialSim> materials, int4 src, int4 dst)
        {
            if (_extraDirty)
            {
                _extraDirty = false;
                _extraCells.Clear();
                _extraRgb.Clear();
                foreach (var e in _extra.Values)
                {
                    _extraCells.Add(e.Cell);
                    _extraRgb.Add(e.Rgb);
                }
            }
            int count = (src.z - src.x) * (src.w - src.y);
            var level = new NativeArray<byte>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var cost = new NativeArray<byte>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var heads = new NativeArray<int>(_config.maxLevel + 1, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var nodes = new NativeList<int2>(count / 4 + 64, Allocator.TempJob);
            try
            {
                new PropagateJob
                {
                    Cells = world.Cells,
                    Fluid = world.Fluid,
                    Lava = LavaEmission,
                    Fire = FireEmission,
                    ExtraCells = _extraCells.AsArray(),
                    ExtraRgb = _extraRgb.AsArray(),
                    Materials = materials,
                    Light = Light,
                    ChunksX = world.ChunksX,
                    Src = src,
                    Dst = dst,
                    MaxLevel = _config.maxLevel,
                    OpenCost = (byte)math.min(_config.openCost, 255),
                    SolidCost = (byte)math.min(_config.solidCost, 255),
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
            if (_extraCells.IsCreated) _extraCells.Dispose();
            if (_extraRgb.IsCreated) _extraRgb.Dispose();
        }

        /// <summary>
        /// Bucket-queue flood fill per colour channel over the source rect; results are written only
        /// inside the destination rect (cells near the source border may be incomplete).
        /// </summary>
        [BurstCompile]
        struct PropagateJob : IJob
        {
            [ReadOnly] public NativeArray<Cell> Cells;
            [ReadOnly] public NativeArray<FluidCell> Fluid;
            [ReadOnly] public NativeArray<MaterialSim> Materials;
            public NativeArray<uint> Light;
            public float3 Lava;
            public float3 Fire;
            /// <summary>Extra emitters in open cells (glowing mushrooms) and their light.</summary>
            [ReadOnly] public NativeArray<int2> ExtraCells;
            [ReadOnly] public NativeArray<float3> ExtraRgb;

            public int ChunksX;
            public int4 Src; // xMin, yMin, xMax (exclusive), yMax (exclusive)
            public int4 Dst;
            public int MaxLevel;
            public byte OpenCost;
            public byte SolidCost;

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

                for (int channel = 0; channel < 4; channel++)
                {
                    Nodes.Clear();
                    for (int i = 0; i < Heads.Length; i++) Heads[i] = -1;

                    for (int ly = 0; ly < h; ly++)
                    for (int lx = 0; lx < w; lx++)
                    {
                        int local = ly * w + lx;
                        byte e = Emission(GridIndex.Of(Src.x + lx, Src.y + ly, ChunksX), channel);
                        Level[local] = e;
                        if (e > 0) Push(e, local);
                    }
                    if (channel < 3)
                        for (int k = 0; k < ExtraCells.Length; k++)
                        {
                            int2 c = ExtraCells[k];
                            if (c.x < Src.x || c.y < Src.y || c.x >= Src.z || c.y >= Src.w) continue;
                            if (!Cells[GridIndex.Of(c.x, c.y, ChunksX)].IsOpen) continue;
                            byte e = (byte)math.clamp((int)math.round(ExtraRgb[k][channel] * MaxLevel), 0, MaxLevel);
                            int local = (c.y - Src.y) * w + (c.x - Src.x);
                            if (e <= Level[local]) continue;
                            Level[local] = e;
                            Push(e, local);
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

            byte Emission(int index, int channel)
            {
                var cell = Cells[index];
                var m = Materials[cell.Material];
                // Channel 3 is the sky: open air cells at full level. 0-2 are emissive materials and lava.
                if (channel == 3) return m.IsAir && cell.IsOpen ? (byte)MaxLevel : (byte)0;
                float e = m.IsAir || cell.IsEmpty ? 0f : m.Emission[channel];
                var f = Fluid[index];
                if (f.Kind == FluidKind.Lava && f.Level > 0) e = math.max(e, Lava[channel]);
                else if (f.Kind == FluidKind.Fire && f.Level > 0) e = math.max(e, Fire[channel]);
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
