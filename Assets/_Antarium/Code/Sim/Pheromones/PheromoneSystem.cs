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
    /// Painted scents (<see cref="PheromoneKind"/>): one strength byte per kind and cell (0 = none, 255 = fresh). They
    /// fade once a second (a Burst job over the chunks that hold any), unless an emitter nearby keeps them fresh or,
    /// for trails, ants carrying loads walk over them. The simulation reads them as on / off; strength only decides
    /// how long a scent lasts and how strongly it is drawn. Kept sharp: painted areas do not spread.
    /// </summary>
    public sealed class PheromoneSystem : IDisposable
    {
        public const byte Full = 255;
        /// <summary>Render levels per kind (0 = none, 1..3 by strength), 2 bits each in <see cref="Render"/>.</summary>
        const int RenderBits = 2;

        readonly WorldGrid _world;
        readonly PheromoneConfig _config;
        public readonly int CellCount;
        /// <summary>Kind-major strengths: kind k, cell index i at k * CellCount + i.</summary>
        public NativeArray<byte> Strength;
        /// <summary>Per cell: 2 bits per kind of how strongly it is drawn (mirrored to the GPU).</summary>
        public NativeArray<ushort> Render;
        /// <summary>Per kind and chunk: cells with that scent (k * chunks + chunk). Chunks without any are skipped.</summary>
        NativeArray<int> _chunkCounts;
        NativeArray<int> _crossed;
        NativeList<int> _active;
        float _timer;
        float _decayCarry;

        /// <summary>Bit per kind whose set of scented cells changed (a cell gained or lost the scent). Cleared by the reader.</summary>
        public int ChangedKinds;
        /// <summary>Cells holding each kind (any strength).</summary>
        public readonly int[] Cells = new int[Pheromones.Count];

        public PheromoneSystem(WorldGrid world, PheromoneConfig config)
        {
            _world = world;
            _config = config;
            CellCount = world.Width * world.Height;
            Strength = new NativeArray<byte>(CellCount * Pheromones.Count, Allocator.Persistent);
            Render = new NativeArray<ushort>(CellCount, Allocator.Persistent);
            _chunkCounts = new NativeArray<int>(world.ChunkCount * Pheromones.Count, Allocator.Persistent);
            _crossed = new NativeArray<int>(Pheromones.Count, Allocator.Persistent);
            _active = new NativeList<int>(64, Allocator.Persistent);
        }

        /// <summary>One kind's strengths (for flow field jobs).</summary>
        public NativeArray<byte> Of(PheromoneKind kind) => Strength.GetSubArray((int)kind * CellCount, CellCount);

        public bool Has(PheromoneKind kind, int cellIndex) => Strength[(int)kind * CellCount + cellIndex] != 0;

        public bool Has(PheromoneKind kind, int2 c) => _world.InBounds(c.x, c.y) && Has(kind, _world.Index(c.x, c.y));

        public byte Get(PheromoneKind kind, int2 c) => _world.InBounds(c.x, c.y) ? Strength[(int)kind * CellCount + _world.Index(c.x, c.y)] : (byte)0;

        /// <summary>Sets one cell's strength, keeping counts, change flags and the render plane up to date.</summary>
        public void Set(PheromoneKind kind, int cellIndex, byte value)
        {
            int k = (int)kind;
            int at = k * CellCount + cellIndex;
            byte old = Strength[at];
            if (old == value) return;
            Strength[at] = value;
            if ((old == 0) != (value == 0))
            {
                int delta = value == 0 ? -1 : 1;
                _chunkCounts[k * _world.ChunkCount + (cellIndex >> WorldConstants.ChunkCellShift)] += delta;
                Cells[k] += delta;
                ChangedKinds |= 1 << k;
            }
            ushort word = Render[cellIndex];
            ushort updated = WithLevel(word, k, LevelOf(value));
            if (updated == word) return;
            Render[cellIndex] = updated;
            MarkUpload(cellIndex);
        }

        void MarkUpload(int cellIndex)
        {
            int chunk = cellIndex >> WorldConstants.ChunkCellShift;
            _world.Chunks[chunk] |= ChunkFlags.PheromoneUpload;
        }

        static int LevelOf(byte s) => s == 0 ? 0 : s < 86 ? 1 : s < 171 ? 2 : 3;

        static ushort WithLevel(ushort word, int kind, int level)
        {
            int shift = kind * RenderBits;
            return (ushort)((word & ~(3 << shift)) | (level << shift));
        }

        /// <summary>
        /// Paints (full strength) or erases a circle of <paramref name="kind"/>. Painting only reaches cells the player
        /// can see; erasing clears everything. Returns the number of cells changed.
        /// </summary>
        public int Paint(PheromoneKind kind, float2 center, float radius, bool set)
        {
            int2 min = (int2)math.floor(center - radius), max = (int2)math.ceil(center + radius);
            int changed = 0;
            for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
            {
                if (!_world.InBounds(x, y)) continue;
                if (math.distance(new float2(x + 0.5f, y + 0.5f), center) > radius) continue;
                int i = _world.Index(x, y);
                if (set && !_world.Cells[i].IsRevealed) continue;
                byte target = set ? Full : (byte)0;
                if (Strength[(int)kind * CellCount + i] == target) continue;
                Set(kind, i, target);
                changed++;
            }
            return changed;
        }

        /// <summary>Appends every cell holding <paramref name="kind"/> (only chunks that have some are scanned).</summary>
        public void AddCells(PheromoneKind kind, NativeList<int2> cells)
        {
            int k = (int)kind;
            if (Cells[k] == 0) return;
            int chunks = _world.ChunkCount;
            for (int ch = 0; ch < chunks; ch++)
            {
                if (_chunkCounts[k * chunks + ch] == 0) continue;
                int start = ch << WorldConstants.ChunkCellShift;
                for (int i = 0; i < WorldConstants.ChunkCellCount; i++)
                    if (Strength[k * CellCount + start + i] != 0) cells.Add(GridIndex.ToCell(start + i, _world.ChunksX));
            }
        }

        /// <summary>A carrying ant stepped onto <paramref name="cellIndex"/>: an existing trail there is refreshed a little.</summary>
        public void Reinforce(int cellIndex)
        {
            int at = (int)PheromoneKind.Trail * CellCount + cellIndex;
            byte s = Strength[at];
            if (s == 0 || s == Full) return;
            Set(PheromoneKind.Trail, cellIndex, (byte)math.min(Full, s + _config.trafficRefresh));
        }

        /// <summary>
        /// An emitter at <paramref name="center"/> keeps its kind fresh: every scented cell within
        /// <paramref name="radius"/> goes back to full strength, and its own cell and neighbours are always scented.
        /// </summary>
        public void Sustain(PheromoneKind kind, int2 center, float radius)
        {
            int r = (int)math.ceil(radius);
            float r2 = radius * radius;
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int x = center.x + dx, y = center.y + dy;
                if (!_world.InBounds(x, y)) continue;
                int d2 = dx * dx + dy * dy;
                if (d2 > r2) continue;
                int i = _world.Index(x, y);
                bool core = d2 <= 2;
                if (core || Strength[(int)kind * CellCount + i] != 0) Set(kind, i, Full);
            }
        }

        /// <summary>Fades every scent once per second. <paramref name="sustain"/> then refreshes what emitters keep alive.</summary>
        public void Tick(float dt, Action<PheromoneSystem> sustain)
        {
            _timer += dt;
            if (_timer < 1f) return;
            float seconds = _timer;
            _timer = 0f;

            _decayCarry += _config.decayPerSecond * seconds;
            int step = (int)_decayCarry;
            _decayCarry -= step;
            if (step > 0) Decay(math.min(step, 255));
            sustain?.Invoke(this);
        }

        void Decay(int step)
        {
            int chunks = _world.ChunkCount;
            _active.Clear();
            for (int ch = 0; ch < chunks; ch++)
            {
                for (int k = 0; k < Pheromones.Count; k++)
                {
                    if (_chunkCounts[k * chunks + ch] == 0) continue;
                    _active.Add(ch);
                    break;
                }
            }
            if (_active.Length == 0) return;
            for (int k = 0; k < Pheromones.Count; k++) _crossed[k] = 0;
            new DecayJob
            {
                Strength = Strength,
                Render = Render,
                ChunkCounts = _chunkCounts,
                Crossed = _crossed,
                Chunks = _world.Chunks,
                Active = _active.AsArray(),
                CellCount = CellCount,
                ChunkCount = chunks,
                Step = (byte)step,
            }.Run();
            for (int k = 0; k < Pheromones.Count; k++)
            {
                if (_crossed[k] == 0) continue;
                ChangedKinds |= 1 << k;
                Cells[k] -= _crossed[k];
            }
        }

        /// <summary>Loading a save: replaces all strengths and rebuilds counts and the render plane.</summary>
        internal void Restore(byte[] strengths)
        {
            Strength.CopyFrom(strengths);
            for (int i = 0; i < _chunkCounts.Length; i++) _chunkCounts[i] = 0;
            Array.Clear(Cells, 0, Cells.Length);
            for (int i = 0; i < CellCount; i++)
            {
                ushort word = 0;
                for (int k = 0; k < Pheromones.Count; k++)
                {
                    byte s = Strength[k * CellCount + i];
                    if (s == 0) continue;
                    _chunkCounts[k * _world.ChunkCount + (i >> WorldConstants.ChunkCellShift)]++;
                    Cells[k]++;
                    word = WithLevel(word, k, LevelOf(s));
                }
                Render[i] = word;
            }
            ChangedKinds = (1 << Pheromones.Count) - 1;
            _world.MarkAll(ChunkFlags.PheromoneUpload);
        }

        public void Dispose()
        {
            if (Strength.IsCreated) Strength.Dispose();
            if (Render.IsCreated) Render.Dispose();
            if (_chunkCounts.IsCreated) _chunkCounts.Dispose();
            if (_crossed.IsCreated) _crossed.Dispose();
            if (_active.IsCreated) _active.Dispose();
        }

        [BurstCompile]
        struct DecayJob : IJob
        {
            public NativeArray<byte> Strength;
            public NativeArray<ushort> Render;
            public NativeArray<int> ChunkCounts;
            public NativeArray<int> Crossed;
            public NativeArray<ChunkFlags> Chunks;
            [ReadOnly] public NativeArray<int> Active;
            public int CellCount;
            public int ChunkCount;
            public byte Step;

            public void Execute()
            {
                for (int a = 0; a < Active.Length; a++)
                {
                    int ch = Active[a];
                    int start = ch << WorldConstants.ChunkCellShift;
                    bool upload = false;
                    for (int k = 0; k < Pheromones.Count; k++)
                    {
                        if (ChunkCounts[k * ChunkCount + ch] == 0) continue;
                        int baseIndex = k * CellCount + start;
                        int shift = k * RenderBits;
                        for (int i = 0; i < WorldConstants.ChunkCellCount; i++)
                        {
                            byte s = Strength[baseIndex + i];
                            if (s == 0) continue;
                            byte n = (byte)math.max(0, s - Step);
                            Strength[baseIndex + i] = n;
                            if (n == 0)
                            {
                                ChunkCounts[k * ChunkCount + ch]--;
                                Crossed[k]++;
                            }
                            int level = n == 0 ? 0 : n < 86 ? 1 : n < 171 ? 2 : 3;
                            ushort word = Render[start + i];
                            ushort updated = (ushort)((word & ~(3 << shift)) | (level << shift));
                            if (updated == word) continue;
                            Render[start + i] = updated;
                            upload = true;
                        }
                    }
                    if (upload) Chunks[ch] |= ChunkFlags.PheromoneUpload;
                }
            }
        }
    }
}
