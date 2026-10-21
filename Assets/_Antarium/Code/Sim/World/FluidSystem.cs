using System;
using Antarium.Content;
using Antarium.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>What one fluid step changed, for the simulation to react to.</summary>
    public struct FluidStepResult
    {
        /// <summary>Cells whose fluid changed.</summary>
        public int Changed;
        /// <summary>Some cell started or stopped blocking ant paths (deep water, dense gas, lava).</summary>
        public bool NavChanged;
        /// <summary>Cells where lava met water this step; they must turn solid (see <see cref="FluidSystem.Reactions"/>).</summary>
        public int Reactions;
    }

    /// <summary>
    /// Cellular-automaton fluids: water and lava fall and spread sideways, gas rises and spreads. One kind per
    /// cell, level 0..255. Only "awake" chunks are processed (a chunk sleeps a few steps after its last change
    /// and wakes when terrain near it opens), bottom to top, so settled lakes cost nothing. Lava is thick
    /// (moves every few steps and heaps up); where it meets water it turns into
    /// <see cref="ContentDB.FluidReactionMaterial"/> (the simulation does that from <see cref="Reactions"/>).
    /// Thin water films and gas evaporate once they reach explored tunnels; untouched pockets stay forever.
    /// </summary>
    public sealed class FluidSystem : IDisposable
    {
        readonly FluidConfig _config;
        readonly WorldGrid _world;
        NativeArray<byte> _awake;
        NativeArray<byte> _stamp;
        NativeList<int> _active;
        NativeList<int2> _reactions;
        NativeArray<int> _counters;
        uint _step;
        byte _stampValue;

        public FluidSystem(WorldGrid world, FluidConfig config)
        {
            _world = world;
            _config = config;
            world.DeepFluid = (byte)math.clamp(config.deepLevel, 1, 255);
            _awake = new NativeArray<byte>(world.ChunkCount, Allocator.Persistent);
            _stamp = new NativeArray<byte>(world.Cells.Length, Allocator.Persistent);
            _active = new NativeList<int>(64, Allocator.Persistent);
            _reactions = new NativeList<int2>(16, Allocator.Persistent);
            _counters = new NativeArray<int>(3, Allocator.Persistent);
        }

        /// <summary>Chunks processed by the last step.</summary>
        public int ActiveChunks => _active.Length;

        /// <summary>Cells where lava met water in the last step (they hold no fluid now and must turn solid).</summary>
        public NativeList<int2> Reactions => _reactions;

        /// <summary>Wakes the chunk holding <paramref name="cell"/> and its eight neighbours (terrain changed there).</summary>
        public void Wake(int2 cell)
        {
            int cx = cell.x >> WorldConstants.ChunkShift, cy = cell.y >> WorldConstants.ChunkShift;
            byte steps = (byte)math.clamp(_config.awakeSteps, 1, 255);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if ((uint)x >= (uint)_world.ChunksX || (uint)y >= (uint)_world.ChunksY) continue;
                _awake[y * _world.ChunksX + x] = steps;
            }
        }

        /// <summary>Wakes every chunk that holds any fluid (new world, loaded save).</summary>
        public void WakeAllWithFluid()
        {
            byte steps = (byte)math.clamp(_config.awakeSteps, 1, 255);
            var fluid = _world.Fluid;
            for (int chunk = 0; chunk < _world.ChunkCount; chunk++)
            {
                int start = chunk << WorldConstants.ChunkCellShift;
                for (int i = 0; i < WorldConstants.ChunkCellCount; i++)
                {
                    if (fluid[start + i].Level == 0) continue;
                    _awake[chunk] = steps;
                    break;
                }
            }
        }

        /// <summary>Runs fluid steps until everything is settled or <paramref name="maxSteps"/> ran (world generation).</summary>
        public int Settle(int maxSteps)
        {
            WakeAllWithFluid();
            int steps = 0;
            for (; steps < maxSteps; steps++)
            {
                Step();
                for (int i = 0; i < _reactions.Length; i++) SolidifyRaw(_reactions[i]);
                if (_active.Length == 0) break;
            }
            return steps;
        }

        /// <summary>World generation only: lava met water before anyone could see it; the cell simply turns solid.</summary>
        void SolidifyRaw(int2 c)
        {
            int i = _world.Index(c.x, c.y);
            var cell = _world.Cells[i];
            _world.Cells[i] = new Cell(_reactionMaterial, Cell.Full, cell.Flags & CellFlags.Revealed);
            Displace(c);
        }

        ushort _reactionMaterial;

        /// <summary>Material lava turns into where it meets water.</summary>
        public ushort ReactionMaterial => _reactionMaterial;
        public void SetReactionMaterial(ushort material) => _reactionMaterial = material;

        /// <summary>Advances all awake chunks by one step.</summary>
        public FluidStepResult Step()
        {
            _active.Clear();
            for (int i = 0; i < _awake.Length; i++)
            {
                if (_awake[i] == 0) continue;
                _active.Add(i);
                _awake[i]--;
            }
            _reactions.Clear();
            if (_active.Length == 0) return default;

            _step++;
            _stampValue = (byte)(_stampValue == 255 ? 1 : _stampValue + 1);
            for (int i = 0; i < _counters.Length; i++) _counters[i] = 0;
            new StepJob
            {
                Cells = _world.Cells,
                Fluid = _world.Fluid,
                Chunks = _world.Chunks,
                Awake = _awake,
                Stamp = _stamp,
                Active = _active.AsArray(),
                Reactions = _reactions,
                Counters = _counters,
                ChunksX = _world.ChunksX,
                ChunksY = _world.ChunksY,
                Width = _world.Width,
                Height = _world.Height,
                Step = _step,
                StampValue = _stampValue,
                DeepLevel = _world.DeepFluid,
                LavaMoves = _step % (uint)math.max(1, _config.lavaStepEvery) == 0,
                LavaMinDiff = math.max(2, _config.lavaMinDiff),
                LavaThickness = math.max(1, _config.lavaThickness),
                EvaporateBelow = _config.evaporateBelow,
                EvaporateChance = (uint)(math.saturate(_config.evaporateChance) * 65535f),
                GasDecayChance = (uint)(math.saturate(_config.gasDecayChance) * 65535f),
                SteamLoss = _config.steamLoss,
                AwakeSteps = (byte)math.clamp(_config.awakeSteps, 1, 255),
            }.Run();
            return new FluidStepResult { Changed = _counters[0], NavChanged = _counters[1] > 0, Reactions = _reactions.Length };
        }

        /// <summary>
        /// The cell at <paramref name="c"/> became solid: its fluid moves into the open cell above (if it has room
        /// and holds the same kind or nothing), otherwise it is lost.
        /// </summary>
        public void Displace(int2 c)
        {
            if (!_world.InBounds(c.x, c.y)) return;
            int i = _world.Index(c.x, c.y);
            var f = _world.Fluid[i];
            if (f.Level == 0) return;
            _world.Fluid[i] = FluidCell.Empty;
            _world.MarkChanged(c.x, c.y, ChunkFlags.FluidUpload | (f.Kind == FluidKind.Lava ? ChunkFlags.LightRecompute : 0));
            for (int y = c.y + 1; y < _world.Height && f.Level > 0; y++)
            {
                int ai = _world.Index(c.x, y);
                if (!_world.Cells[ai].IsOpen) break;
                var a = _world.Fluid[ai];
                if (a.Level != 0 && a.Kind != f.Kind) break;
                int move = math.min(f.Level, FluidCell.Full - a.Level);
                if (move <= 0) continue;
                _world.Fluid[ai] = new FluidCell(f.Kind, (byte)(a.Level + move));
                f.Level = (byte)(f.Level - move);
                _world.MarkChanged(c.x, y, ChunkFlags.FluidUpload | (f.Kind == FluidKind.Lava ? ChunkFlags.LightRecompute : 0));
            }
            Wake(c);
        }

        /// <summary>Puts fluid into an open cell (tests, events, debug).</summary>
        public void Set(int2 c, FluidKind kind, byte level)
        {
            if (!_world.InBounds(c.x, c.y)) return;
            _world.Fluid[_world.Index(c.x, c.y)] = new FluidCell(kind, level);
            _world.MarkChanged(c.x, c.y, ChunkFlags.FluidUpload | ChunkFlags.LightRecompute);
            Wake(c);
        }

        /// <summary>Total fluid of a kind in the world (tests, debug).</summary>
        public long Total(FluidKind kind)
        {
            long total = 0;
            foreach (var f in _world.Fluid)
                if (f.Kind == kind) total += f.Level;
            return total;
        }

        public void Dispose()
        {
            if (_awake.IsCreated) _awake.Dispose();
            if (_stamp.IsCreated) _stamp.Dispose();
            if (_active.IsCreated) _active.Dispose();
            if (_reactions.IsCreated) _reactions.Dispose();
            if (_counters.IsCreated) _counters.Dispose();
        }

        [BurstCompile]
        struct StepJob : IJob
        {
            [ReadOnly] public NativeArray<Cell> Cells;
            public NativeArray<FluidCell> Fluid;
            public NativeArray<ChunkFlags> Chunks;
            public NativeArray<byte> Awake;
            public NativeArray<byte> Stamp;
            [ReadOnly] public NativeArray<int> Active;
            public NativeList<int2> Reactions;
            /// <summary>0 = cells changed, 1 = nav-blocking flips, 2 = lava appeared / vanished.</summary>
            public NativeArray<int> Counters;

            public int ChunksX, ChunksY, Width, Height;
            public uint Step;
            public byte StampValue;
            public byte DeepLevel;
            public bool LavaMoves;
            public int LavaMinDiff;
            public int LavaThickness;
            public int EvaporateBelow;
            public uint EvaporateChance;
            public uint GasDecayChance;
            public int SteamLoss;
            public byte AwakeSteps;

            const int Full = FluidCell.Full;

            int Idx(int x, int y) => GridIndex.Of(x, y, ChunksX);
            bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;
            bool Blocking(FluidCell f) => f.Level > 0 && (f.Kind == FluidKind.Lava || f.Level >= DeepLevel);
            bool Roll(int i, uint chance) => (math.hash(new uint2((uint)i, Step)) & 0xFFFF) < chance;

            public void Execute()
            {
                for (int k = 0; k < Active.Length; k++)
                {
                    int chunk = Active[k];
                    int ox = (chunk % ChunksX) << WorldConstants.ChunkShift;
                    int oy = (chunk / ChunksX) << WorldConstants.ChunkShift;
                    int baseIndex = chunk << WorldConstants.ChunkCellShift;
                    for (int ly = 0; ly < WorldConstants.ChunkSize; ly++)
                    {
                        // Alternate the sweep direction per row and step so nothing drifts one way.
                        bool leftToRight = ((Step + (uint)ly) & 1) == 0;
                        for (int t = 0; t < WorldConstants.ChunkSize; t++)
                        {
                            int lx = leftToRight ? t : WorldConstants.ChunkMask - t;
                            int i = baseIndex | (ly << WorldConstants.ChunkShift) | lx;
                            var f = Fluid[i];
                            if (f.Level == 0 || Stamp[i] == StampValue) continue;
                            int x = ox + lx, y = oy + ly;
                            if (!Cells[i].IsOpen)
                            {
                                Set(i, x, y, FluidCell.Empty); // the cell turned solid without displacing (should not happen)
                                continue;
                            }
                            switch (f.Kind)
                            {
                                case FluidKind.Water:
                                    UpdateLiquid(i, x, y, f, 2, 1, leftToRight);
                                    break;
                                case FluidKind.Lava:
                                    if (LavaMoves) UpdateLiquid(i, x, y, f, LavaMinDiff, LavaThickness, leftToRight);
                                    break;
                                case FluidKind.Gas:
                                    UpdateGas(i, x, y, f, leftToRight);
                                    break;
                            }
                        }
                    }
                }
            }

            /// <param name="minDiff">Smallest level difference that still flows sideways.</param>
            /// <param name="thickness">Sideways flow is divided by this (lava creeps).</param>
            void UpdateLiquid(int i, int x, int y, FluidCell f, int minDiff, int thickness, bool leftToRight)
            {
                int level = f.Level;
                // 1. Fall into the cell below.
                if (y > 0)
                {
                    int bi = Idx(x, y - 1);
                    if (Cells[bi].IsOpen)
                    {
                        var b = Fluid[bi];
                        if (b.Level == 0 || b.Kind == f.Kind)
                        {
                            int move = math.min(level, Full - b.Level);
                            if (move > 0)
                            {
                                Set(bi, x, y - 1, new FluidCell(f.Kind, (byte)(b.Level + move)));
                                level -= move;
                            }
                        }
                        else if (b.Kind == FluidKind.Gas)
                        {
                            // Liquid sinks through gas: swap.
                            Set(bi, x, y - 1, new FluidCell(f.Kind, (byte)level));
                            Set(i, x, y, b);
                            Stamp[i] = StampValue;
                            return;
                        }
                        else
                        {
                            React(i, x, y, f.Kind, level, bi, x, y - 1, b);
                            return;
                        }
                    }
                }

                // 2. Spread sideways toward lower neighbours (thirds when both are lower, halves when one is).
                if (level > 0)
                {
                    int dx0 = leftToRight ? 1 : -1;
                    int na = -1, nb = -1, la = 0, lb = 0;
                    for (int s = 0; s < 2; s++)
                    {
                        int nx = x + (s == 0 ? dx0 : -dx0);
                        if (!InBounds(nx, y)) continue;
                        int ni = Idx(nx, y);
                        if (!Cells[ni].IsOpen) continue;
                        var n = Fluid[ni];
                        if (n.Level != 0 && n.Kind != f.Kind)
                        {
                            if (n.Kind != FluidKind.Gas)
                            {
                                React(i, x, y, f.Kind, level, ni, nx, y, n);
                                return;
                            }
                            continue;
                        }
                        if (level - n.Level < minDiff) continue;
                        if (na < 0) { na = ni; la = n.Level; }
                        else { nb = ni; lb = n.Level; }
                    }
                    // Only the difference above minDiff flows, so thick lava heaps up instead of levelling out.
                    int floor = minDiff > 2 ? minDiff : 0;
                    if (na >= 0 && nb >= 0)
                    {
                        int ma = (level - la - floor) / (3 * thickness), mb = (level - lb - floor) / (3 * thickness);
                        Give(na, ma, f.Kind, la, false);
                        Give(nb, mb, f.Kind, lb, false);
                        level -= ma + mb;
                    }
                    else if (na >= 0)
                    {
                        int m = (level - la - floor) / (2 * thickness);
                        Give(na, m, f.Kind, la, false);
                        level -= m;
                    }
                }

                // 3. Thin water films in explored tunnels evaporate.
                if (f.Kind == FluidKind.Water && level > 0 && level <= EvaporateBelow
                    && (Cells[i].Flags & CellFlags.Explored) != 0 && Roll(i, EvaporateChance))
                    level--;

                if (level != f.Level) Set(i, x, y, new FluidCell(f.Kind, (byte)level));
            }

            void UpdateGas(int i, int x, int y, FluidCell f, bool leftToRight)
            {
                int level = f.Level;
                // 1. Rise into the cell above.
                if (y < Height - 1)
                {
                    int ai = Idx(x, y + 1);
                    if (Cells[ai].IsOpen)
                    {
                        var a = Fluid[ai];
                        if (a.Level == 0 || a.Kind == FluidKind.Gas)
                        {
                            int move = math.min(level, Full - a.Level);
                            if (move > 0)
                            {
                                Set(ai, x, y + 1, new FluidCell(FluidKind.Gas, (byte)(a.Level + move)));
                                Stamp[ai] = StampValue;
                                level -= move;
                            }
                        }
                    }
                }

                // 2. Spread sideways.
                if (level > 0)
                {
                    int dx0 = leftToRight ? 1 : -1;
                    int na = -1, nb = -1, la = 0, lb = 0;
                    for (int s = 0; s < 2; s++)
                    {
                        int nx = x + (s == 0 ? dx0 : -dx0);
                        if (!InBounds(nx, y)) continue;
                        int ni = Idx(nx, y);
                        if (!Cells[ni].IsOpen) continue;
                        var n = Fluid[ni];
                        if (n.Level != 0 && n.Kind != FluidKind.Gas) continue;
                        if (level - n.Level < 2) continue;
                        if (na < 0) { na = ni; la = n.Level; }
                        else { nb = ni; lb = n.Level; }
                    }
                    if (na >= 0 && nb >= 0)
                    {
                        int ma = (level - la) / 3, mb = (level - lb) / 3;
                        Give(na, ma, FluidKind.Gas, la, false);
                        Give(nb, mb, FluidKind.Gas, lb, false);
                        level -= ma + mb;
                    }
                    else if (na >= 0)
                    {
                        int m = (level - la) / 2;
                        Give(na, m, FluidKind.Gas, la, false);
                        level -= m;
                    }
                }

                // 3. Gas that reached the colony's tunnels slowly thins out.
                if (level > 0 && (Cells[i].Flags & CellFlags.Explored) != 0 && Roll(i, GasDecayChance))
                    level--;

                if (level != f.Level) Set(i, x, y, new FluidCell(FluidKind.Gas, (byte)level));
            }

            /// <summary>
            /// Adds fluid to a neighbour. Sideways gifts do not stamp it: a cell further along the sweep may pass some
            /// on in the same step (a fast, decaying ripple), which levels lakes much quicker than one cell per step.
            /// </summary>
            void Give(int ni, int amount, FluidKind kind, int current, bool stamp)
            {
                if (amount <= 0) return;
                var c = GridIndex.ToCell(ni, ChunksX);
                Set(ni, c.x, c.y, new FluidCell(kind, (byte)(current + amount)));
                if (stamp) Stamp[ni] = StampValue;
            }

            /// <summary>Lava and water meet: the lava cell empties (and turns solid on the main thread), the water loses steam.</summary>
            void React(int i, int x, int y, FluidKind kind, int level, int ni, int nx, int ny, FluidCell other)
            {
                bool selfIsLava = kind == FluidKind.Lava;
                int lavaIndex = selfIsLava ? i : ni;
                int2 lavaCell = selfIsLava ? new int2(x, y) : new int2(nx, ny);
                int waterIndex = selfIsLava ? ni : i;
                int2 waterCell = selfIsLava ? new int2(nx, ny) : new int2(x, y);
                int waterLevel = selfIsLava ? other.Level : level;

                Set(lavaIndex, lavaCell.x, lavaCell.y, FluidCell.Empty);
                Set(waterIndex, waterCell.x, waterCell.y, new FluidCell(FluidKind.Water, (byte)math.max(0, waterLevel - SteamLoss)));
                Stamp[lavaIndex] = StampValue;
                Stamp[waterIndex] = StampValue;
                Reactions.Add(lavaCell);
            }

            void Set(int i, int x, int y, FluidCell value)
            {
                if (value.Level == 0) value = FluidCell.Empty;
                var old = Fluid[i];
                Fluid[i] = value;
                Counters[0]++;
                if (Blocking(old) != Blocking(value)) Counters[1]++;

                int cx = x >> WorldConstants.ChunkShift, cy = y >> WorldConstants.ChunkShift;
                int chunk = cy * ChunksX + cx;
                var flags = ChunkFlags.FluidUpload;
                if ((old.Kind == FluidKind.Lava && old.Level > 0) != (value.Kind == FluidKind.Lava))
                {
                    flags |= ChunkFlags.LightRecompute;
                    Counters[2]++;
                }
                Chunks[chunk] |= flags;
                Awake[chunk] = AwakeSteps;
                int lx = x & WorldConstants.ChunkMask, ly = y & WorldConstants.ChunkMask;
                if (lx == 0 && cx > 0) Awake[chunk - 1] = AwakeSteps;
                if (lx == WorldConstants.ChunkMask && cx < ChunksX - 1) Awake[chunk + 1] = AwakeSteps;
                if (ly == 0 && cy > 0) Awake[chunk - ChunksX] = AwakeSteps;
                if (ly == WorldConstants.ChunkMask && cy < ChunksY - 1) Awake[chunk + ChunksX] = AwakeSteps;
            }
        }
    }
}
