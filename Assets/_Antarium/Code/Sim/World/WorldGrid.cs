using System;
using System.Runtime.CompilerServices;
using Antarium.Core;
using Unity.Collections;
using Unity.Mathematics;

namespace Antarium.Sim
{
    [Flags]
    public enum ChunkFlags : byte
    {
        None = 0,
        /// <summary>Cell data changed and must be re-uploaded to the GPU.</summary>
        TerrainUpload = 1 << 0,
        /// <summary>Opacity or emitters changed; light around this chunk must be recomputed.</summary>
        LightRecompute = 1 << 1,
        /// <summary>Light values changed and must be re-uploaded to the GPU.</summary>
        LightUpload = 1 << 2,
    }

    /// <summary>
    /// Chunk-major cell index math. Each 32x32 chunk is a contiguous block of 1024 cells, so a chunk can be
    /// uploaded or copied as one range. Cell (0,0) is the bottom-left of the world; y grows upward.
    /// </summary>
    public static class GridIndex
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Of(int x, int y, int chunksX)
        {
            int chunk = (y >> WorldConstants.ChunkShift) * chunksX + (x >> WorldConstants.ChunkShift);
            return (chunk << WorldConstants.ChunkCellShift)
                   | ((y & WorldConstants.ChunkMask) << WorldConstants.ChunkShift)
                   | (x & WorldConstants.ChunkMask);
        }

        public static int2 ToCell(int index, int chunksX)
        {
            int chunk = index >> WorldConstants.ChunkCellShift;
            int local = index & (WorldConstants.ChunkCellCount - 1);
            return new int2(
                (chunk % chunksX) * WorldConstants.ChunkSize + (local & WorldConstants.ChunkMask),
                (chunk / chunksX) * WorldConstants.ChunkSize + (local >> WorldConstants.ChunkShift));
        }
    }

    /// <summary>The world grid: single source of truth for terrain.</summary>
    public sealed class WorldGrid : IDisposable
    {
        public readonly int ChunksX;
        public readonly int ChunksY;
        public readonly int Width;
        public readonly int Height;

        public NativeArray<Cell> Cells;
        public NativeArray<ChunkFlags> Chunks;

        /// <summary>Per column: y of the topmost generated ground cell. Depth = SurfaceY[x] - y.</summary>
        public readonly int[] SurfaceY;

        public WorldGrid(int chunksX, int chunksY)
        {
            if (chunksX <= 0 || chunksY <= 0) throw new ArgumentOutOfRangeException();
            ChunksX = chunksX;
            ChunksY = chunksY;
            Width = chunksX * WorldConstants.ChunkSize;
            Height = chunksY * WorldConstants.ChunkSize;
            Cells = new NativeArray<Cell>(Width * Height, Allocator.Persistent);
            Chunks = new NativeArray<ChunkFlags>(chunksX * chunksY, Allocator.Persistent);
            SurfaceY = new int[Width];
        }

        public int ChunkCount => ChunksX * ChunksY;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Index(int x, int y) => GridIndex.Of(x, y, ChunksX);

        public Cell this[int x, int y]
        {
            get => Cells[Index(x, y)];
            set => Cells[Index(x, y)] = value;
        }

        public int Depth(int x, int y) => SurfaceY[math.clamp(x, 0, Width - 1)] - y;

        public void MarkChanged(int x, int y, ChunkFlags flags)
        {
            int chunk = (y >> WorldConstants.ChunkShift) * ChunksX + (x >> WorldConstants.ChunkShift);
            Chunks[chunk] |= flags;
        }

        public void MarkAll(ChunkFlags flags)
        {
            for (int i = 0; i < Chunks.Length; i++)
                Chunks[i] |= flags;
        }

        public static float2 CellCenterToWorld(int2 cell) => (new float2(cell) + 0.5f) * WorldConstants.CellSize;
        public static float2 WorldToCellSpace(float2 world) => world / WorldConstants.CellSize;
        public static int2 WorldToCell(float2 world) => (int2)math.floor(world / WorldConstants.CellSize);

        public void Dispose()
        {
            if (Cells.IsCreated) Cells.Dispose();
            if (Chunks.IsCreated) Chunks.Dispose();
        }
    }
}
