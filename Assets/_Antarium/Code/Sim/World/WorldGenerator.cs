using Antarium.Content;
using Antarium.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// Deterministic, seeded world generation. Same seed + content = identical world. Biomes are depth bands
    /// with their own base material, deposits, noise caves (or ruin rooms) and fluid pockets; liquids are
    /// dropped to the cave floors and gas to the ceilings so a fresh world settles quickly.
    /// </summary>
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
            public int PocketStart;
            public int PocketCount;
            public bool Rooms;
            public int2 RoomSize;
            public float RoomChance;
            public uint RoomSeed;
        }

        struct PocketGen
        {
            public FluidKind Kind;
            public float Scale;
            public float Threshold;
            public float2 Offset;
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

            int depositTotal = 0, pocketTotal = 0;
            foreach (var b in gen.biomes)
            {
                depositTotal += b.deposits.Count;
                pocketTotal += b.fluidPockets.Count;
            }

            var biomes = new NativeArray<BiomeGen>(gen.biomes.Count, Allocator.TempJob);
            var deposits = new NativeArray<DepositGen>(depositTotal, Allocator.TempJob);
            var pockets = new NativeArray<PocketGen>(math.max(1, pocketTotal), Allocator.TempJob);
            var surface = new NativeArray<int>(world.SurfaceY, Allocator.TempJob);
            try
            {
                float top = 0f;
                int depositIndex = 0, pocketIndex = 0;
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
                        PocketStart = pocketIndex,
                        PocketCount = b.fluidPockets.Count,
                        Rooms = b.rooms,
                        RoomSize = math.max(new int2(b.roomSize.x, b.roomSize.y), new int2(6, 6)),
                        RoomChance = b.roomChance,
                        RoomSeed = rng.NextUInt() | 1u,
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
                    foreach (var p in b.fluidPockets)
                    {
                        pockets[pocketIndex++] = new PocketGen
                        {
                            Kind = p.kind,
                            Scale = p.scale,
                            Threshold = p.threshold,
                            Offset = NextOffset(),
                        };
                    }
                }

                new GenerateJob
                {
                    Cells = world.Cells,
                    Fluid = world.Fluid,
                    SurfaceY = surface,
                    Biomes = biomes,
                    Deposits = deposits,
                    Pockets = pockets,
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

                new DropFluidsJob
                {
                    Cells = world.Cells,
                    Fluid = world.Fluid,
                    ChunksX = world.ChunksX,
                    Height = world.Height,
                }.Schedule(world.Width, 16).Complete();
            }
            finally
            {
                biomes.Dispose();
                deposits.Dispose();
                pockets.Dispose();
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
            world.Fluid[i] = FluidCell.Empty;
        }

        /// <summary>
        /// Per column: in every vertical run of open cells, liquids drop to the bottom and gas rises to the top
        /// (amounts kept), so lakes start nearly settled and the fluid simulation only evens them out sideways.
        /// </summary>
        [BurstCompile]
        struct DropFluidsJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<Cell> Cells;
            [NativeDisableParallelForRestriction] public NativeArray<FluidCell> Fluid;
            public int ChunksX;
            public int Height;

            public void Execute(int x)
            {
                int y = 0;
                while (y < Height)
                {
                    if (!Cells[GridIndex.Of(x, y, ChunksX)].IsOpen) { y++; continue; }
                    int start = y;
                    int liquid = 0, gas = 0;
                    FluidKind liquidKind = FluidKind.None;
                    for (; y < Height; y++)
                    {
                        int i = GridIndex.Of(x, y, ChunksX);
                        if (!Cells[i].IsOpen) break;
                        var f = Fluid[i];
                        if (f.Level == 0) continue;
                        if (f.Kind == FluidKind.Gas) gas += f.Level;
                        else
                        {
                            if (liquidKind == FluidKind.None) liquidKind = f.Kind;
                            if (f.Kind == liquidKind) liquid += f.Level;
                        }
                        Fluid[i] = FluidCell.Empty;
                    }
                    int end = y; // exclusive
                    for (int k = start; k < end && liquid > 0; k++)
                    {
                        int amount = math.min(liquid, FluidCell.Full);
                        Fluid[GridIndex.Of(x, k, ChunksX)] = new FluidCell(liquidKind, (byte)amount);
                        liquid -= amount;
                    }
                    for (int k = end - 1; k >= start && gas > 0; k--)
                    {
                        int i = GridIndex.Of(x, k, ChunksX);
                        if (Fluid[i].Level != 0) break;
                        int amount = math.min(gas, FluidCell.Full);
                        Fluid[i] = new FluidCell(FluidKind.Gas, (byte)amount);
                        gas -= amount;
                    }
                }
            }
        }

        [BurstCompile]
        struct GenerateJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<Cell> Cells;
            [NativeDisableParallelForRestriction] public NativeArray<FluidCell> Fluid;
            [ReadOnly] public NativeArray<int> SurfaceY;
            [ReadOnly] public NativeArray<BiomeGen> Biomes;
            [ReadOnly] public NativeArray<DepositGen> Deposits;
            [ReadOnly] public NativeArray<PocketGen> Pockets;

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
                {
                    int i = baseIndex | (ly << WorldConstants.ChunkShift) | lx;
                    Cells[i] = Generate(ox + lx, oy + ly, out var fluid);
                    Fluid[i] = fluid;
                }
            }

            Cell Generate(int x, int y, out FluidCell fluid)
            {
                fluid = FluidCell.Empty;
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
                    if (biome.Rooms)
                    {
                        if (IsRoomSpace(x, y, biome)) density = 0;
                    }
                    else
                    {
                        // Caves are stretched horizontally; the threshold is raised near the top so they fade in.
                        float2 cp = p * biome.CaveScale * new float2(1f, 1.7f) + CaveOffset;
                        float v = noise.snoise(cp) * 0.7f + noise.snoise(cp * 2.3f + 17.3f) * 0.3f;
                        float fade = math.saturate((CaveMinDepth + CaveFadeDepth - depth) / CaveFadeDepth);
                        if (v > biome.CaveThreshold + fade * 0.4f)
                            density = 0;
                    }
                }

                // Fluid pockets fill parts of the caves (dropped to the floor / ceiling afterwards).
                if (density == 0)
                {
                    for (int k = 0; k < biome.PocketCount; k++)
                    {
                        var pocket = Pockets[biome.PocketStart + k];
                        if (noise.snoise(p * pocket.Scale + pocket.Offset) > pocket.Threshold)
                            fluid = new FluidCell(pocket.Kind, FluidCell.Full);
                    }
                }
                return new Cell(material, density);
            }

            /// <summary>
            /// Ancient ruins: a grid of rooms with 2-cell walls. Some grid slots hold a room; doors join rooms
            /// side by side at floor level, hatches join rooms above each other.
            /// </summary>
            bool IsRoomSpace(int x, int y, in BiomeGen b)
            {
                int2 size = b.RoomSize;
                int2 g = new int2((int)math.floor(x / (float)size.x), (int)math.floor(y / (float)size.y));
                int2 l = new int2(x, y) - g * size;
                bool here = Room(g, b);
                if (l.x >= 2 && l.y >= 2) return here;
                if (l.x < 2 && l.y >= 2 && l.y <= 3)
                    return here && Room(g - new int2(1, 0), b) && Link(g, 1u, b) < 0.75f; // door to the room on the left
                if (l.y < 2 && l.x >= size.x / 2 && l.x <= size.x / 2 + 1)
                    return here && Room(g - new int2(0, 1), b) && Link(g, 2u, b) < 0.5f; // hatch to the room below
                return false;
            }

            static bool Room(int2 g, in BiomeGen b) =>
                (math.hash(new uint3((uint)g.x, (uint)g.y, b.RoomSeed)) & 0xFFFF) / 65535f < b.RoomChance;

            static float Link(int2 g, uint kind, in BiomeGen b) =>
                (math.hash(new uint4((uint)g.x, (uint)g.y, b.RoomSeed, kind)) & 0xFFFF) / 65535f;

            int FindBiome(float depth)
            {
                for (int i = 0; i < Biomes.Length; i++)
                    if (depth < Biomes[i].Bottom) return i;
                return Biomes.Length - 1;
            }
        }
    }
}
