using Antarium.Content;
using Antarium.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// Fog of war. Cells near explored open space are revealed. When digging breaks into open space that
    /// is not yet explored (a natural cave), the connected cave is flood-explored and revealed at once.
    /// </summary>
    public static class Reveal
    {
        /// <summary>Reveals every cell within <paramref name="radius"/> of <paramref name="center"/>.</summary>
        public static void Around(WorldGrid world, int2 center, int radius)
        {
            int r2 = radius * radius;
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > r2) continue;
                int x = center.x + dx, y = center.y + dy;
                if (!world.InBounds(x, y)) continue;
                int i = world.Index(x, y);
                var cell = world.Cells[i];
                if (cell.IsRevealed) continue;
                cell.Flags |= CellFlags.Revealed;
                world.Cells[i] = cell;
                world.MarkChanged(x, y, ChunkFlags.TerrainUpload);
            }
        }

        /// <summary>True if the cell is open space nobody has explored yet.</summary>
        public static bool IsUnexploredOpen(WorldGrid world, int x, int y)
        {
            if (!world.InBounds(x, y)) return false;
            var cell = world.Cells[world.Index(x, y)];
            return cell.IsOpen && (cell.Flags & CellFlags.Explored) == 0;
        }

        /// <summary>
        /// Explores all unexplored open cells connected to <paramref name="starts"/> (up to
        /// <paramref name="limit"/>) and reveals the cells around them. Returns the number explored.
        /// </summary>
        public static int Flood(WorldGrid world, NativeArray<int2> starts, int wallRadius, int limit) =>
            Flood(world, starts, wallRadius, limit, default);

        /// <summary>As <see cref="Flood(WorldGrid, NativeArray{int2}, int, int)"/>; the explored cells are also appended to <paramref name="discovered"/>.</summary>
        public static int Flood(WorldGrid world, NativeArray<int2> starts, int wallRadius, int limit, NativeList<int2> discovered)
        {
            var visited = new NativeList<int2>(256, Allocator.TempJob);
            try
            {
                new FloodJob
                {
                    Cells = world.Cells,
                    Chunks = world.Chunks,
                    Starts = starts,
                    Visited = visited,
                    ChunksX = world.ChunksX,
                    Width = world.Width,
                    Height = world.Height,
                    Radius = wallRadius,
                    Limit = limit,
                }.Run();
                if (discovered.IsCreated) discovered.AddRange(visited.AsArray());
                return visited.Length;
            }
            finally
            {
                visited.Dispose();
            }
        }

        /// <summary>Initial fog: everything connected to the open sky is explored and revealed.</summary>
        public static void Initial(WorldGrid world, NativeArray<MaterialSim> materials, int wallRadius)
        {
            var starts = new NativeList<int2>(world.Width * 64, Allocator.TempJob);
            try
            {
                for (int x = 0; x < world.Width; x++)
                for (int y = world.Height - 1; y >= 0; y--)
                {
                    var cell = world.Cells[world.Index(x, y)];
                    if (!materials[cell.Material].IsAir || !cell.IsOpen) break;
                    starts.Add(new int2(x, y));
                }
                Flood(world, starts.AsArray(), wallRadius, int.MaxValue);
            }
            finally
            {
                starts.Dispose();
            }
        }

        [BurstCompile]
        struct FloodJob : IJob
        {
            public NativeArray<Cell> Cells;
            public NativeArray<ChunkFlags> Chunks;
            [ReadOnly] public NativeArray<int2> Starts;
            public NativeList<int2> Visited; // doubles as the BFS queue

            public int ChunksX;
            public int Width;
            public int Height;
            public int Radius;
            public int Limit;

            public void Execute()
            {
                for (int i = 0; i < Starts.Length && Visited.Length < Limit; i++)
                    TryEnter(Starts[i]);

                for (int head = 0; head < Visited.Length && Visited.Length < Limit; head++)
                {
                    int2 c = Visited[head];
                    TryEnter(c + new int2(1, 0));
                    TryEnter(c + new int2(-1, 0));
                    TryEnter(c + new int2(0, 1));
                    TryEnter(c + new int2(0, -1));
                }

                int r2 = Radius * Radius;
                for (int i = 0; i < Visited.Length; i++)
                {
                    int2 c = Visited[i];
                    for (int dy = -Radius; dy <= Radius; dy++)
                    for (int dx = -Radius; dx <= Radius; dx++)
                    {
                        if (dx * dx + dy * dy > r2) continue;
                        int x = c.x + dx, y = c.y + dy;
                        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) continue;
                        int index = GridIndex.Of(x, y, ChunksX);
                        var cell = Cells[index];
                        if (cell.IsRevealed) continue;
                        cell.Flags |= CellFlags.Revealed;
                        Cells[index] = cell;
                        int chunk = (y >> WorldConstants.ChunkShift) * ChunksX + (x >> WorldConstants.ChunkShift);
                        Chunks[chunk] |= ChunkFlags.TerrainUpload;
                    }
                }
            }

            void TryEnter(int2 c)
            {
                if (Visited.Length >= Limit) return;
                if ((uint)c.x >= (uint)Width || (uint)c.y >= (uint)Height) return;
                int index = GridIndex.Of(c.x, c.y, ChunksX);
                var cell = Cells[index];
                if (!cell.IsOpen || (cell.Flags & CellFlags.Explored) != 0) return;
                cell.Flags |= CellFlags.Explored;
                Cells[index] = cell;
                Visited.Add(c);
            }
        }
    }
}
