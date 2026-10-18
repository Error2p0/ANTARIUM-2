using Antarium.Content;
using Antarium.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>Deterministic, seeded world generation. Same seed + content = identical world.</summary>
    public static class WorldGenerator
    {
        struct BiomeGen
        {
            public float Top;
            public float Bottom;
            public ushort BaseMaterial;
            public float CaveScale;
            public float CaveThreshold;
            public int DepositStart;
            public int DepositCount;
        }

        struct DepositGen
        {
            public ushort Material;
            public float Scale;
            public float Threshold;
            public float MinDepth;
            public float MaxDepth;
            public float2 Offset;
        }

        public static WorldGrid Create(ContentDB content, uint seed)
        {
            var gen = content.WorldGen;
            var world = new WorldGrid(gen.widthInChunks, gen.heightInChunks);
            Generate(world, content, seed);
            return world;
        }

        public static void Generate(WorldGrid world, ContentDB content, uint seed)
        {
            var gen = content.WorldGen;
            var rng = new Random(seed == 0 ? 0x9E3779B9u : seed);
            float2 NextOffset() => rng.NextFloat2(-5000f, 5000f);

            float2 surfaceOffset = NextOffset();
            int baseSurface = world.Height - 1 - gen.skyHeight;
            for (int x = 0; x < world.Width; x++)
            {
                float n = noise.snoise(new float2(x * gen.surfaceFrequency, 0f) + surfaceOffset) * 0.75f
                          + noise.snoise(new float2(x * gen.surfaceFrequency * 3.1f, 7f) + surfaceOffset) * 0.25f;
                world.SurfaceY[x] = baseSurface + (int)math.round(n * gen.surfaceAmplitude);
            }

            int depositTotal = 0;
            foreach (var b in gen.biomes) depositTotal += b.deposits.Count;

            var biomes = new NativeArray<BiomeGen>(gen.biomes.Count, Allocator.TempJob);
            var deposits = new NativeArray<DepositGen>(depositTotal, Allocator.TempJob);
            var surface = new NativeArray<int>(world.SurfaceY, Allocator.TempJob);
            try
            {
                float top = 0f;
                int depositIndex = 0;
                for (int i = 0; i < gen.biomes.Count; i++)
                {
                    var b = gen.biomes[i];
                    bool last = i == gen.biomes.Count - 1;
                    biomes[i] = new BiomeGen
                    {
                        Top = top,
                        Bottom = last ? float.MaxValue : top + b.thickness,
                        BaseMaterial = content.GetMaterialIndex(b.baseMaterial),
                        CaveScale = b.caveScale,
                        CaveThreshold = b.caveThreshold,
                        DepositStart = depositIndex,
                        DepositCount = b.deposits.Count,
                    };
                    top += b.thickness;
                    foreach (var d in b.deposits)
                    {
                        deposits[depositIndex++] = new DepositGen
                        {
                            Material = content.GetMaterialIndex(d.material),
                            Scale = d.scale,
                            Threshold = d.threshold,
                            MinDepth = d.minDepth,
                            MaxDepth = d.maxDepth > 0 ? d.maxDepth : float.MaxValue,
                            Offset = NextOffset(),
                        };
                    }
                }

                new GenerateJob
                {
                    Cells = world.Cells,
                    SurfaceY = surface,
                    Biomes = biomes,
                    Deposits = deposits,
                    ChunksX = world.ChunksX,
                    Width = world.Width,
                    Air = ContentDB.AirIndex,
                    SurfaceMaterial = content.GetMaterialIndex(gen.surfaceMaterial),
                    Bedrock = content.GetMaterialIndex(gen.bedrock),
                    GrassDepth = gen.grassDepth,
                    BedrockThickness = gen.bedrockThickness,
                    CaveMinDepth = gen.caveMinDepth,
                    CaveFadeDepth = gen.caveFadeDepth,
                    WarpAmplitude = gen.biomeWarpAmplitude,
                    WarpFrequency = gen.biomeWarpFrequency,
                    WarpOffset = NextOffset(),
                    CaveOffset = NextOffset(),
                }.Schedule(world.ChunkCount, 4).Complete();
            }
            finally
            {
                biomes.Dispose();
                deposits.Dispose();
                surface.Dispose();
            }

            CarveNest(world, gen);
            world.MarkAll(ChunkFlags.TerrainUpload | ChunkFlags.LightRecompute);
        }

        /// <summary>Starting hole: a shaft from the surface down into a small oval chamber at the world centre.</summary>
        static void CarveNest(WorldGrid world, WorldGenSettings gen)
        {
            int cx = world.Width / 2;
            int surface = world.SurfaceY[cx];
            int cy = surface - gen.nestDepth;
            float2 r = gen.nestRadius;

            for (int y = cy; y <= surface + 1; y++)
            for (int x = cx - gen.shaftHalfWidth; x <= cx + gen.shaftHalfWidth; x++)
                CarveCell(world, x, y);

            for (int y = (int)(cy - r.y); y <= (int)(cy + r.y); y++)
            for (int x = (int)(cx - r.x); x <= (int)(cx + r.x); x++)
            {
                float2 d = (new float2(x, y) - new float2(cx, cy)) / r;
                if (math.lengthsq(d) <= 1f) CarveCell(world, x, y);
            }
        }

        static void CarveCell(WorldGrid world, int x, int y)
        {
            if (!world.InBounds(x, y)) return;
            int i = world.Index(x, y);
            var cell = world.Cells[i];
            cell.Density = 0;
            world.Cells[i] = cell;
        }

        [BurstCompile]
        struct GenerateJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<Cell> Cells;
            [ReadOnly] public NativeArray<int> SurfaceY;
            [ReadOnly] public NativeArray<BiomeGen> Biomes;
            [ReadOnly] public NativeArray<DepositGen> Deposits;

            public int ChunksX;
            public int Width;
            public ushort Air;
            public ushort SurfaceMaterial;
            public ushort Bedrock;
            public int GrassDepth;
            public int BedrockThickness;
            public float CaveMinDepth;
            public float CaveFadeDepth;
            public float WarpAmplitude;
            public float WarpFrequency;
            public float2 WarpOffset;
            public float2 CaveOffset;

            public void Execute(int chunk)
            {
                int ox = (chunk % ChunksX) * WorldConstants.ChunkSize;
                int oy = (chunk / ChunksX) * WorldConstants.ChunkSize;
                int baseIndex = chunk << WorldConstants.ChunkCellShift;
                for (int ly = 0; ly < WorldConstants.ChunkSize; ly++)
                for (int lx = 0; lx < WorldConstants.ChunkSize; lx++)
                    Cells[baseIndex | (ly << WorldConstants.ChunkShift) | lx] = Generate(ox + lx, oy + ly);
            }

            Cell Generate(int x, int y)
            {
                if (x < BedrockThickness || x >= Width - BedrockThickness || y < BedrockThickness)
                    return new Cell(Bedrock, Cell.Full);

                float depth = SurfaceY[x] - y;
                if (depth < 0f) return new Cell(Air, 0);
                if (depth < GrassDepth) return new Cell(SurfaceMaterial, Cell.Full);

                float2 p = new float2(x, y);
                float warped = depth + noise.snoise(p * WarpFrequency + WarpOffset) * WarpAmplitude;
                var biome = Biomes[FindBiome(warped)];

                ushort material = biome.BaseMaterial;
                for (int i = 0; i < biome.DepositCount; i++)
                {
                    var d = Deposits[biome.DepositStart + i];
                    if (depth < d.MinDepth || depth >= d.MaxDepth) continue;
                    if (noise.snoise(p * d.Scale + d.Offset) > d.Threshold)
                        material = d.Material;
                }

                byte density = Cell.Full;
                if (depth > CaveMinDepth)
                {
                    // Caves are stretched horizontally; the threshold is raised near the top so they fade in.
                    float2 cp = p * biome.CaveScale * new float2(1f, 1.7f) + CaveOffset;
                    float v = noise.snoise(cp) * 0.7f + noise.snoise(cp * 2.3f + 17.3f) * 0.3f;
                    float fade = math.saturate((CaveMinDepth + CaveFadeDepth - depth) / CaveFadeDepth);
                    if (v > biome.CaveThreshold + fade * 0.4f)
                        density = 0;
                }
                return new Cell(material, density);
            }

            int FindBiome(float depth)
            {
                for (int i = 0; i < Biomes.Length; i++)
                    if (depth < Biomes[i].Bottom) return i;
                return Biomes.Length - 1;
            }
        }
    }
}
