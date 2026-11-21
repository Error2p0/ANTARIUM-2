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

        public static WorldGrid Create(ContentDB content, uint seed) => Create(content, seed, WorldTweaks.None);

        /// <summary>Generates a world changed by world modifiers (<paramref name="tweaks"/>).</summary>
        public static WorldGrid Create(ContentDB content, uint seed, in WorldTweaks tweaks)
        {
            var gen = content.WorldGen;
            var world = new WorldGrid(gen.widthInChunks, gen.heightInChunks);
            Generate(world, content, seed, tweaks);
            return world;
        }

        public static void Generate(WorldGrid world, ContentDB content, uint seed) => Generate(world, content, seed, WorldTweaks.None);

        public static void Generate(WorldGrid world, ContentDB content, uint seed, in WorldTweaks tweaks)
        {
            float scale = tweaks.BiomeScale > 0f ? tweaks.BiomeScale : 1f;
            var gen = content.WorldGen;
            var rng = new Random(seed == 0 ? 0x9E3779B9u : seed);
            float2 NextOffset() => rng.NextFloat2(-5000f, 5000f);

            float2 surfaceOffset = NextOffset();
            int baseSurface = world.Height - 1 - gen.skyHeight;
            ShapeSurface(world, gen, seed, surfaceOffset, baseSurface);
            // Lakes and rivers (Milestone 11): their basins are dug into the surface line before the terrain is filled in.
            var water = SurfaceWater.Place(world.Width, gen, seed);
            var features = SurfaceFeatures.Place(world.Width, gen, seed, water);
            // The ground is levelled under structures (not boulders) and lakes, blending back into the hills beside them.
            foreach (var f in features)
                if (f.Kind != SurfaceFeatureKind.Boulder) Level(world, f.X0 - 1, f.X1 + 1, math.max(4, gen.waterFlatBlend / 2));
            foreach (var b in water) Level(world, b.X0 - 2, b.X1 + 2, gen.waterFlatBlend);
            var rims = new int[water.Count];
            for (int k = 0; k < water.Count; k++)
            {
                var b = water[k];
                int rim = math.min(world.SurfaceY[b.X0 - 1], world.SurfaceY[b.X1 + 1]);
                rims[k] = rim;
                for (int x = b.X0; x <= b.X1; x++)
                    world.SurfaceY[x] = math.min(world.SurfaceY[x], rim) - SurfaceWater.BedDepth(b, x);
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
                    int thickness = ContentDB.BiomeThickness(b, scale);
                    biomes[i] = new BiomeGen
                    {
                        Top = top,
                        Bottom = last ? float.MaxValue : top + thickness,
                        BaseMaterial = tweaks.BaseMaterial(i, content.GetMaterialIndex(b.baseMaterial)),
                        CaveScale = b.caveScale,
                        CaveThreshold = b.caveThreshold + tweaks.CaveThreshold,
                        DepositStart = depositIndex,
                        DepositCount = b.deposits.Count,
                        PocketStart = pocketIndex,
                        PocketCount = b.fluidPockets.Count,
                        Rooms = b.rooms,
                        RoomSize = math.max(new int2(b.roomSize.x, b.roomSize.y), new int2(6, 6)),
                        RoomChance = b.roomChance,
                        RoomSeed = rng.NextUInt() | 1u,
                    };
                    top += thickness;
                    foreach (var d in b.deposits)
                    {
                        ushort material = content.GetMaterialIndex(d.material);
                        deposits[depositIndex++] = new DepositGen
                        {
                            Material = material,
                            Scale = d.scale,
                            Threshold = d.threshold + (IsOre(content, material) ? tweaks.OreThreshold : 0f),
                            MinDepth = d.minDepth * scale,
                            MaxDepth = d.maxDepth > 0 ? d.maxDepth * scale : float.MaxValue,
                            Offset = NextOffset(),
                        };
                    }
                    foreach (var p in b.fluidPockets)
                    {
                        pockets[pocketIndex++] = new PocketGen
                        {
                            Kind = p.kind,
                            Scale = p.scale,
                            Threshold = p.threshold + tweaks.PocketThreshold(p.kind),
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

            FillWater(world, content, water, rims);
            if (ElderTree.Locate(world.Width, world.Height, gen, seed, scale, out var elder))
                ElderTree.Build(world, content, elder, seed);
            GrowRoots(world, content, seed);
            BuildFeatures(world, content, features, seed);
            PlaceTablets(world, content, seed, scale);
            CarveNest(world, gen);
            world.MarkAll(ChunkFlags.TerrainUpload | ChunkFlags.LightRecompute);
        }

        /// <summary>
        /// The surface line: small waves (<c>surfaceAmplitude</c>), and since Milestone 11 big rolling hills, small bumps and
        /// terraces (flat steps with cliffs) where a slow noise mask allows; flat around the nest shaft, blending into the
        /// hills. New noise offsets come from the seed by hash, so the rest of world generation keeps its random stream.
        /// </summary>
        static void ShapeSurface(WorldGrid world, WorldGenSettings gen, uint seed, float2 offset, int baseSurface)
        {
            float2 Off(uint k) => new float2(math.hash(new uint2(seed, k)) % 20000u, math.hash(new uint2(seed, k + 1)) % 20000u) * 0.25f;
            float2 hillOff = Off(0x4111u), bumpOff = Off(0xB0B0u), terraceOff = Off(0x7E77u);
            int nest = world.Width / 2;
            var h = new float[world.Width];
            for (int x = 0; x < world.Width; x++)
            {
                float n = noise.snoise(new float2(x * gen.surfaceFrequency, 0f) + offset) * 0.75f
                          + noise.snoise(new float2(x * gen.surfaceFrequency * 3.1f, 7f) + offset) * 0.25f;
                float v = n * gen.surfaceAmplitude;
                if (gen.hillAmplitude > 0f)
                {
                    // Hills: a slow wave with a sharper second octave, so some summits are rounded and some are pointed.
                    float hill = noise.snoise(new float2(x * gen.hillFrequency, 0f) + hillOff);
                    float ridge = 1f - math.abs(noise.snoise(new float2(x * gen.hillFrequency * 2.3f, 3f) + hillOff));
                    v += (hill * 0.75f + (ridge - 0.5f) * 0.5f) * gen.hillAmplitude;
                }
                if (gen.bumpAmplitude > 0f) v += noise.snoise(new float2(x * gen.bumpFrequency, 1f) + bumpOff) * gen.bumpAmplitude;
                if (gen.terraceThreshold < 1f)
                {
                    float mask = noise.snoise(new float2(x * gen.terraceFrequency, 5f) + terraceOff);
                    float k = math.saturate((mask - gen.terraceThreshold) / 0.06f);
                    float step = math.max(1, gen.terraceStep);
                    v = math.lerp(v, math.floor(v / step) * step, k);
                }
                h[x] = v;
            }
            if (gen.nestFlatRadius > 0)
            {
                float flat = h[nest];
                for (int x = 0; x < world.Width; x++)
                {
                    float t = math.saturate((math.abs(x - nest) - gen.nestFlatRadius) / (float)math.max(1, gen.nestFlatBlend));
                    h[x] = math.lerp(flat, h[x], t * t * (3f - 2f * t));
                }
            }
            for (int x = 0; x < world.Width; x++) world.SurfaceY[x] = baseSurface + (int)math.round(h[x]);
        }

        /// <summary>Levels the surface line over columns <paramref name="x0"/>..<paramref name="x1"/> to their average height, blending back over <paramref name="blend"/> columns either side.</summary>
        static void Level(WorldGrid world, int x0, int x1, int blend)
        {
            x0 = math.max(0, x0);
            x1 = math.min(world.Width - 1, x1);
            if (x1 < x0) return;
            float sum = 0f;
            for (int x = x0; x <= x1; x++) sum += world.SurfaceY[x];
            int level = (int)math.round(sum / (x1 - x0 + 1));
            for (int x = math.max(0, x0 - blend); x <= math.min(world.Width - 1, x1 + blend); x++)
            {
                int d = math.max(x0 - x, x - x1);
                float t = d <= 0 || blend <= 0 ? 0f : math.saturate(d / (float)(blend + 1));
                t = t * t * (3f - 2f * t);
                world.SurfaceY[x] = (int)math.round(math.lerp(level, world.SurfaceY[x], t));
            }
        }

        /// <summary>
        /// Rare surface structures (Milestone 11), built on the finished ground: boulders, a rock overhang, a hollow log, an
        /// abandoned anthill, standing stones. Afterwards each column's surface line is its topmost solid cell, so ants under
        /// the overhang or inside a log count as under cover.
        /// </summary>
        static void BuildFeatures(WorldGrid world, ContentDB content, System.Collections.Generic.List<SurfaceFeature> features, uint seed)
        {
            var gen = content.WorldGen;
            ushort bedrock = content.GetMaterialIndex(gen.bedrock);
            foreach (var f in features)
            {
                var rule = gen.surfaceFeatures[f.Rule];
                ushort material = content.GetMaterialIndex(rule.material);
                ushort find = rule.find != null ? content.GetMaterialIndex(rule.find) : ContentDB.AirIndex;
                var rng = new Random(math.hash(new uint2(seed, f.Variant)) | 1u);
                int ground = int.MaxValue, groundTop = int.MinValue;
                for (int x = f.X0; x <= f.X1; x++)
                {
                    ground = math.min(ground, world.SurfaceY[x]);
                    groundTop = math.max(groundTop, world.SurfaceY[x]);
                }
                var finds = new System.Collections.Generic.List<int2>();

                // Solid cell of the structure; below the ground only plain ground is replaced (never finds or bedrock).
                void Solid(int x, int y, ushort m)
                {
                    if (!world.InBounds(x, y)) return;
                    int i = world.Index(x, y);
                    var cell = world.Cells[i];
                    if (cell.Material == bedrock) return;
                    if (y <= world.SurfaceY[x] && !cell.IsOpen && content.MaterialSim[cell.Material].DropItem != ContentDB.NoItem) return;
                    world.Cells[i] = new Cell(m, Cell.Full, cell.Flags);
                    world.Fluid[i] = FluidCell.Empty;
                }

                // Open cell inside the structure (a hollow, a tunnel): it keeps the structure's material as its back wall.
                void Hollow(int x, int y, ushort m)
                {
                    if (!world.InBounds(x, y)) return;
                    int i = world.Index(x, y);
                    if (world.Cells[i].Material == bedrock) return;
                    world.Cells[i] = new Cell(m, 0, world.Cells[i].Flags);
                    world.Fluid[i] = FluidCell.Empty;
                }

                int w = f.X1 - f.X0 + 1, mid = f.Mid;
                float half = (w - 1) * 0.5f, cx = f.X0 + half;
                switch (f.Kind)
                {
                    case SurfaceFeatureKind.Boulder:
                    {
                        // A lumpy rock, about half of it sunk into the ground.
                        float rx = half + 0.5f, ry = math.max(2f, w * 0.42f);
                        float2 o = rng.NextFloat2(-100f, 100f);
                        int cy = ground + 1;
                        for (int y = cy - (int)ry - 1; y <= cy + (int)ry + 1; y++)
                        for (int x = f.X0 - 1; x <= f.X1 + 1; x++)
                        {
                            float2 d = (new float2(x, y) - new float2(cx, cy)) / new float2(rx, ry);
                            if (math.lengthsq(d) + noise.snoise(new float2(x, y) * 0.4f + o) * 0.25f <= 1f) Solid(x, y, material);
                        }
                        break;
                    }
                    case SurfaceFeatureKind.Overhang:
                    {
                        // A crag on one side and a ledge reaching out from its top over open ground, thinning to a lip.
                        // (An arch would enclose a sealed bubble of air in a side view; a ledge leaves the space under it open.)
                        int height = rng.NextInt(6, 9);
                        int top = groundTop + height;
                        int dir = (f.Variant & 1) == 0 ? 1 : -1;
                        int crag = math.max(4, w * 2 / 5);
                        for (int i = 0; i < w; i++)
                        {
                            int x = dir > 0 ? f.X0 + i : f.X1 - i;
                            // A rough top over the crag; the ledge is smooth so its thin lip stays joined to it.
                            int crown = i < crag ? top - (noise.snoise(new float2(x * 0.45f, f.Variant % 977)) < -0.3f ? 1 : 0) - (i == 0 ? 1 : 0)
                                : top - (i == w - 1 ? 1 : 0);
                            int bottom;
                            if (i < crag) bottom = world.SurfaceY[x] + 1;
                            else if (i < crag + 2) bottom = world.SurfaceY[x] + 1 + (i - crag + 1) * 2; // its foot slopes back under the ledge
                            else
                            {
                                float k = (i - crag) / (float)math.max(1, w - crag);
                                int thick = (int)math.round(math.lerp(3.4f, 2f, k));
                                bottom = crown - thick + 1;
                            }
                            for (int y = bottom; y <= crown; y++) Solid(x, y, material);
                        }
                        break;
                    }
                    case SurfaceFeatureKind.HollowLog:
                    {
                        // A fallen trunk resting on the high ground: a bark shell around a hollow, open at both ends.
                        int d = rng.NextInt(4, 6);
                        int bottom = groundTop + 1, topRow = bottom + d - 1;
                        for (int x = f.X0; x <= f.X1; x++)
                        for (int y = bottom; y <= topRow; y++)
                        {
                            bool end = x == f.X0 || x == f.X1;
                            bool shell = y == bottom || y == topRow;
                            if (end && shell) continue; // rounded ends
                            if (shell) Solid(x, y, material);
                            else Hollow(x, y, material);
                        }
                        // Fill under the log where the ground dips, so it rests on something.
                        for (int x = f.X0 + 1; x < f.X1; x++)
                        for (int y = world.SurfaceY[x] + 1; y < bottom; y++)
                            Solid(x, y, material);
                        for (int n = 0; n < rule.finds; n++)
                            finds.Add(new int2(rng.NextInt(f.X0 + 2, f.X1 - 1), rng.NextBool() ? bottom : topRow));
                        break;
                    }
                    case SurfaceFeatureKind.Anthill:
                    {
                        // A mound of tamped earth over an old nest: a winding entrance from the top down to a chamber under the ground.
                        int height = math.max(4, (int)math.round(w * 0.33f));
                        float2 o = rng.NextFloat2(-100f, 100f);
                        var tops = new int[w];
                        for (int x = f.X0; x <= f.X1; x++)
                        {
                            float t = (x - cx) / math.max(1f, half);
                            int top = ground + (int)math.round(height * math.pow(math.max(0f, 1f - t * t), 0.8f) + noise.snoise(new float2(x * 0.3f, 0f) + o) * 0.8f);
                            tops[x - f.X0] = top;
                            for (int y = world.SurfaceY[x] - 3; y <= top; y++) Solid(x, y, material);
                        }
                        int2 chamber = new int2(mid, ground - 6);
                        for (int y = chamber.y - 2; y <= chamber.y + 2; y++)
                        for (int x = chamber.x - 6; x <= chamber.x + 6; x++)
                            if (math.lengthsq((new float2(x, y) - chamber) / new float2(5.5f, 2.2f)) <= 1f) Hollow(x, y, material);
                        // The entrance: a 1-cell tunnel winding down from beside the summit.
                        int ex = mid + (rng.NextBool() ? 2 : -2);
                        int ey = tops[ex - f.X0];
                        int px = ex;
                        for (int y = ey; y >= chamber.y; y--)
                        {
                            Hollow(px, y, material);
                            if (rng.NextFloat() < 0.35f)
                            {
                                px += px < mid ? 1 : px > mid ? -1 : (rng.NextBool() ? 1 : -1);
                                Hollow(px, y, material);
                            }
                        }
                        // A side gallery off the chamber.
                        int dir = ex < mid ? 1 : -1;
                        for (int k = 0; k < 7; k++) Hollow(chamber.x + dir * (6 + k), chamber.y - 1 - k / 3, material);
                        // Finds in the chamber walls.
                        for (int n = 0, tries = 0; n < rule.finds && tries < 200; tries++)
                        {
                            int2 p = chamber + new int2(rng.NextInt(-7, 8), rng.NextInt(-4, 4));
                            if (!world.InBounds(p.x, p.y) || world[p.x, p.y].IsOpen) continue;
                            bool wall = world[p.x + 1, p.y].IsOpen || world[p.x - 1, p.y].IsOpen || world[p.x, p.y + 1].IsOpen || world[p.x, p.y - 1].IsOpen;
                            if (!wall || finds.Contains(p)) continue;
                            finds.Add(p);
                            n++;
                        }
                        break;
                    }
                    case SurfaceFeatureKind.StandingStones:
                    {
                        // Three or four slabs two cells wide, sunk two cells deep, the middle ones tallest; a find under the middle one.
                        int stones = w >= 13 ? 4 : 3;
                        float gap = (w - 2) / (float)(stones - 1);
                        for (int s = 0; s < stones; s++)
                        {
                            int x0 = f.X0 + (int)math.round(s * gap);
                            float centre = 1f - math.abs(s - (stones - 1) * 0.5f) / stones;
                            int height = (int)math.round(3 + centre * 4) + rng.NextInt(0, 2);
                            for (int x = x0; x <= x0 + 1; x++)
                            for (int y = world.SurfaceY[x] - 2; y <= groundTop + height - (x == x0 + 1 && rng.NextBool() ? 1 : 0); y++)
                                Solid(x, y, material);
                        }
                        int m0 = f.X0 + (int)math.round((stones / 2) * gap);
                        for (int n = 0; n < rule.finds; n++) finds.Add(new int2(m0 + (n & 1), world.SurfaceY[m0] - 3 - n / 2));
                        break;
                    }
                }

                if (find != ContentDB.AirIndex)
                    foreach (var p in finds)
                        if (world.InBounds(p.x, p.y) && !world[p.x, p.y].IsOpen && world[p.x, p.y].Material != bedrock)
                            world.Cells[world.Index(p.x, p.y)] = new Cell(find, Cell.Full, world[p.x, p.y].Flags);

                // The surface line is now the topmost solid cell of each column the structure touches.
                for (int x = math.max(0, f.X0 - 1); x <= math.min(world.Width - 1, f.X1 + 1); x++)
                    for (int y = world.Height - 1; y > world.SurfaceY[x]; y--)
                        if (!world[x, y].IsOpen) { world.SurfaceY[x] = y; break; }
            }
        }

        /// <summary>
        /// Tablets (10.5): a few unique finds set into the floors of ruin rooms (the first biome with rooms), far apart. A tablet
        /// replaces the solid cell under an open room cell whose neighbours on the floor are solid too.
        /// </summary>
        static void PlaceTablets(WorldGrid world, ContentDB content, uint seed, float scale)
        {
            var gen = content.WorldGen;
            ushort tablet = content.TabletMaterial;
            if (tablet == ContentDB.AirIndex || gen.tabletCount <= 0) return;
            int top = 0, ruins = -1;
            for (int i = 0; i < gen.biomes.Count; i++)
            {
                if (gen.biomes[i] != null && gen.biomes[i].rooms) { ruins = i; break; }
                top += ContentDB.BiomeThickness(gen.biomes[i], scale);
            }
            if (ruins < 0) return;

            var rng = new Random(math.hash(new uint2(seed, 0x7AB1E7u)) | 1u);
            var placed = new System.Collections.Generic.List<int2>();
            int margin = gen.bedrockThickness + 2;
            int minDepth = top + 8;
            float spacing2 = (float)gen.tabletSpacing * gen.tabletSpacing;
            for (int attempt = 0; attempt < 4000 && placed.Count < gen.tabletCount; attempt++)
            {
                int x = rng.NextInt(margin, world.Width - margin);
                int maxDepth = world.SurfaceY[x] - margin;
                if (maxDepth <= minDepth) continue;
                int y = world.SurfaceY[x] - rng.NextInt(minDepth, maxDepth);
                // Walk down to the floor of the room this cell is in (if it is one).
                if (!world[x, y].IsOpen) continue;
                while (y > margin && world[x, y - 1].IsOpen) y--;
                int2 floor = new int2(x, y - 1);
                if (floor.y <= margin || world[floor.x, floor.y].IsOpen) continue;
                if (world.Depth(floor.x, floor.y) < minDepth) continue;
                if (world[floor.x - 1, floor.y].IsOpen || world[floor.x + 1, floor.y].IsOpen) continue;
                if (!world[floor.x - 1, floor.y + 1].IsOpen || !world[floor.x + 1, floor.y + 1].IsOpen) continue;
                bool far = true;
                foreach (var p in placed)
                    if (math.distancesq(new float2(p), new float2(floor)) < spacing2) { far = false; break; }
                if (!far) continue;
                int index = world.Index(floor.x, floor.y);
                world.Cells[index] = new Cell(tablet, Cell.Full);
                placed.Add(floor);
            }
        }

        /// <summary>
        /// Lakes and rivers (Milestone 11): each basin is filled with water up to its lower bank, its open cells are flagged
        /// <see cref="CellFlags.Lake"/> (that water never soaks away), and the ground under it and on its banks becomes the
        /// water bed material (sand). Only plain ground is replaced, never finds or bedrock.
        /// </summary>
        static void FillWater(WorldGrid world, ContentDB content, System.Collections.Generic.List<WaterBody> water, int[] rims)
        {
            var gen = content.WorldGen;
            ushort bed = gen.waterBedMaterial != null ? content.GetMaterialIndex(gen.waterBedMaterial) : ContentDB.AirIndex;
            ushort bedrock = content.GetMaterialIndex(gen.bedrock);
            for (int k = 0; k < water.Count; k++)
            {
                var b = water[k];
                for (int x = b.X0; x <= b.X1; x++)
                for (int y = world.SurfaceY[x] + 1; y <= rims[k] && y < world.Height; y++)
                {
                    int i = world.Index(x, y);
                    var cell = world.Cells[i];
                    if (!cell.IsOpen) continue;
                    cell.Flags |= CellFlags.Lake;
                    world.Cells[i] = cell;
                    world.Fluid[i] = new FluidCell(FluidKind.Water, FluidCell.Full);
                }
                if (bed == ContentDB.AirIndex) continue;
                for (int x = b.X0 - 2; x <= b.X1 + 2; x++)
                {
                    if (x < 0 || x >= world.Width) continue;
                    if (!b.Contains(x) && world.SurfaceY[x] > rims[k] + 1) continue; // a bank on a hill keeps its grass
                    int rows = b.Contains(x) ? math.max(1, gen.waterBedDepth) : math.max(1, gen.grassDepth);
                    for (int y = world.SurfaceY[x]; y > world.SurfaceY[x] - rows && y > 0; y--)
                    {
                        int i = world.Index(x, y);
                        var cell = world.Cells[i];
                        if (cell.IsOpen || cell.Material == bedrock || content.MaterialSim[cell.Material].DropItem != ContentDB.NoItem) continue;
                        world.Cells[i] = new Cell(bed, cell.Density, cell.Flags);
                    }
                }
            }
        }

        /// <summary>Ore: a material that drops an item that is not food (world modifiers make these richer or rarer).</summary>
        public static bool IsOre(ContentDB content, ushort material)
        {
            ushort drop = content.MaterialSim[material].DropItem;
            return drop != ContentDB.NoItem && !content.Items[drop].IsFood;
        }

        /// <summary>
        /// Roots: from every surface plant a tough strand grows down through the topsoil (a random walk with a few side
        /// branches). It replaces plain soil only (not ore or other finds) and stops at caves.
        /// </summary>
        static void GrowRoots(WorldGrid world, ContentDB content, uint seed)
        {
            var gen = content.WorldGen;
            if (gen.rootMaterial == null) return;
            ushort root = content.GetMaterialIndex(gen.rootMaterial);
            ushort bedrock = content.GetMaterialIndex(gen.bedrock);
            var rng = new Random(math.hash(new uint2(seed, 0x2007u)) | 1u);
            var tips = new System.Collections.Generic.List<int4>(); // x, y, cells left, sideways drift
            ushort bigRoot = gen.treeRootMaterial != null ? content.GetMaterialIndex(gen.treeRootMaterial) : ContentDB.AirIndex;
            var treeRng = new Random(math.hash(new uint2(seed, 0x7EE7u)) | 1u);
            foreach (var plant in SurfacePlants.Place(world.Width, gen, seed))
            {
                if (plant.IsTree && bigRoot != ContentDB.AirIndex)
                {
                    GrowTreeRoots(world, content, plant.X, bigRoot, root, ref treeRng);
                    continue;
                }
                int lo = math.max(1, gen.rootLength.x), hi = math.max(lo, gen.rootLength.y);
                tips.Clear();
                tips.Add(new int4(plant.X, world.SurfaceY[plant.X] - gen.grassDepth, rng.NextInt(lo, hi + 1), 0));
                for (int t = 0; t < tips.Count && t < 8; t++)
                {
                    var tip = tips[t];
                    int x = tip.x, y = tip.y;
                    for (int n = 0; n < tip.z; n++)
                    {
                        if (!world.InBounds(x, y)) break;
                        int depth = world.SurfaceY[x] - y;
                        if (depth > gen.rootMaxDepth) break;
                        int i = world.Index(x, y);
                        var cell = world.Cells[i];
                        if (cell.IsOpen) break; // reached a cave
                        if (depth >= gen.grassDepth && cell.Material != bedrock && content.MaterialSim[cell.Material].DropItem == ContentDB.NoItem)
                            world.Cells[i] = new Cell(root, Cell.Full, cell.Flags);
                        if (tip.w == 0 && n > 3 && rng.NextFloat() < 0.07f)
                            tips.Add(new int4(x, y, math.max(3, (tip.z - n) / 2), rng.NextBool() ? 1 : -1));
                        // Mostly down; branches lean to their side.
                        float r = rng.NextFloat();
                        if (tip.w != 0 && r < 0.5f) x += tip.w;
                        else if (r < 0.15f) x--;
                        else if (r > 0.85f) x++;
                        else y--;
                        if (tip.w != 0 && rng.NextFloat() < 0.5f) y--;
                    }
                }
            }
        }

        /// <summary>
        /// Big roots of a tree (Milestone 11): a taproot straight down and a few thick roots spreading out and curving down,
        /// two cells wide for most of their length, with thin plain roots off them. They replace plain ground only and stop
        /// at caves and at <c>treeRootMaxDepth</c>.
        /// </summary>
        static void GrowTreeRoots(WorldGrid world, ContentDB content, int treeX, ushort bigRoot, ushort plainRoot, ref Random rng)
        {
            var gen = content.WorldGen;
            ushort bedrock = content.GetMaterialIndex(gen.bedrock);
            int count = rng.NextInt(math.max(1, gen.treeRoots.x), math.max(gen.treeRoots.x, gen.treeRoots.y) + 1);
            int lo = math.max(4, gen.treeRootLength.x), hi = math.max(lo, gen.treeRootLength.y);

            bool Grow(int x, int y, ushort material)
            {
                if (!world.InBounds(x, y)) return false;
                int depth = world.SurfaceY[x] - y;
                if (depth > gen.treeRootMaxDepth) return false;
                int i = world.Index(x, y);
                var cell = world.Cells[i];
                if (cell.IsOpen) return false; // a cave
                if (depth >= gen.grassDepth && cell.Material != bedrock && cell.Material != bigRoot
                    && content.MaterialSim[cell.Material].DropItem == ContentDB.NoItem || cell.Material == plainRoot && material == bigRoot)
                    world.Cells[i] = new Cell(material, Cell.Full, cell.Flags);
                return true;
            }

            for (int r = 0; r < count; r++)
            {
                // Root 0 is the taproot; the others leave the trunk to alternate sides, flatter at first, then diving.
                int side = r == 0 ? 0 : (r & 1) == 1 ? -1 : 1;
                float spread = r == 0 ? 0.1f : rng.NextFloat(0.55f, 0.8f);
                int length = r == 0 ? hi : rng.NextInt(lo, hi + 1);
                int x = treeX + side, y = world.SurfaceY[treeX] - gen.grassDepth;
                for (int n = 0; n < length; n++)
                {
                    bool thick = n < length * 0.65f;
                    if (!Grow(x, y, bigRoot)) break;
                    if (thick && !Grow(side >= 0 ? x + 1 : x - 1, y, bigRoot)) break;
                    // A thin plain root off to the side now and then.
                    if (n > 4 && rng.NextFloat() < 0.08f)
                    {
                        int bx = x, by = y, bs = rng.NextBool() ? 1 : -1;
                        for (int m = 0, len = rng.NextInt(4, 12); m < len; m++)
                        {
                            if (!Grow(bx, by, plainRoot)) break;
                            if (rng.NextFloat() < 0.5f) bx += bs;
                            else by--;
                        }
                    }
                    float out_ = spread * (1f - n / (float)length * 0.8f);
                    float roll = rng.NextFloat();
                    if (side != 0 && roll < out_) x += side;
                    else if (side == 0 && roll < 0.12f) x += rng.NextBool() ? 1 : -1;
                    else y--;
                }
            }
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
