using System.Collections.Generic;
using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>Where the Elder Tree's cavern lies (Milestone 11): its centre and half size in cells.</summary>
    public struct ElderSite
    {
        public int2 Center;
        public int2 Half;

        public bool Contains(int2 c) => math.all(math.abs(c - Center) <= Half);
    }

    /// <summary>
    /// The Elder Tree (Milestone 11): one great cavern per world in the middle of <c>WorldGenSettings.elderBiome</c>, a dome with
    /// a flat floor, and in it an ancient tree of solid cells: a flared trunk of elderwood, branches reaching up and out, glowing
    /// leaf canopies at their ends and big roots running along the floor and down into the rock. Its fruit (golden apples)
    /// grows on top of the canopy and branches as growth spots (<see cref="Simulation"/>, like cave mushrooms). Where it lies
    /// comes from the seed, the world size and the biome scale only, so the running colony finds it again after a load.
    /// </summary>
    public static class ElderTree
    {
        public static bool Locate(int width, int height, WorldGenSettings gen, uint seed, float scale, out ElderSite site)
        {
            site = default;
            if (gen.elderBiome == null || gen.elderWood == null || gen.biomes == null) return false;
            int index = gen.biomes.IndexOf(gen.elderBiome);
            if (index < 0) return false;
            if (scale <= 0f) scale = 1f;
            int top = 0;
            for (int i = 0; i < index; i++) top += ContentDB.BiomeThickness(gen.biomes[i], scale);
            int thickness = ContentDB.BiomeThickness(gen.elderBiome, scale);
            int2 size = math.max(new int2(gen.elderCaveSize.x, gen.elderCaveSize.y), new int2(40, 30));
            int2 half = size / 2;
            var rng = new Random(math.hash(new uint2(seed, 0xE1DE5u)) | 1u);
            int baseSurface = height - 1 - gen.skyHeight;
            int cy = baseSurface - (top + (int)math.round(thickness * gen.elderDepthShare));
            cy = math.clamp(cy, gen.bedrockThickness + half.y + 6, baseSurface - gen.caveMinDepth - half.y - 4);
            int margin = gen.bedrockThickness + half.x + 6;
            int far = width / 2 - margin;
            if (far <= 0 || cy - half.y <= gen.bedrockThickness) return false;
            int near = math.min(gen.elderMinDistance, far);
            int side = rng.NextBool() ? 1 : -1;
            int cx = width / 2 + side * rng.NextInt(near, far + 1);
            site = new ElderSite { Center = new int2(cx, cy), Half = half };
            return true;
        }

        /// <summary>Carves the cavern and grows the tree into it.</summary>
        public static void Build(WorldGrid world, ContentDB content, in ElderSite site, uint seed)
        {
            var gen = content.WorldGen;
            ushort wood = content.GetMaterialIndex(gen.elderWood);
            ushort leaves = gen.elderLeaves != null ? content.GetMaterialIndex(gen.elderLeaves) : wood;
            ushort root = gen.treeRootMaterial != null ? content.GetMaterialIndex(gen.treeRootMaterial) : wood;
            ushort bedrock = content.GetMaterialIndex(gen.bedrock);
            var rng = new Random(math.hash(new uint2(seed, 0xE1DE7u)) | 1u);
            int2 c = site.Center, half = site.Half;
            float2 noiseAt = rng.NextFloat2(-500f, 500f);
            int edge = gen.bedrockThickness + 1;

            // The cavern: a flat floor (a gentle mound under the trunk) and a dome that pinches out at both ends.
            int floorBase = c.y - half.y + 4;
            var floor = new int[half.x * 2 + 1];
            var ceiling = new int[half.x * 2 + 1];
            for (int dx = -half.x; dx <= half.x; dx++)
            {
                float t = dx / (float)half.x;
                float n = noise.snoise(new float2(dx * 0.09f, 0f) + noiseAt);
                int f = floorBase + (int)math.round(n * 1.5f + 3f * math.exp(-(dx * dx) / 90f));
                int h = (int)math.round((half.y * 2 - 6) * math.pow(math.max(0f, 1f - t * t), 0.55f)
                                        + noise.snoise(new float2(dx * 0.13f, 9f) + noiseAt) * 2.5f);
                floor[dx + half.x] = f;
                ceiling[dx + half.x] = f + math.max(0, h);
            }
            for (int dx = -half.x; dx <= half.x; dx++)
            {
                int x = c.x + dx;
                if (x < edge || x >= world.Width - edge) continue;
                for (int y = floor[dx + half.x] + 1; y <= ceiling[dx + half.x]; y++)
                {
                    if (y < edge || y >= world.Height) continue;
                    int i = world.Index(x, y);
                    var cell = world.Cells[i];
                    if (cell.Material == bedrock) continue;
                    cell.Density = 0;
                    world.Cells[i] = cell;
                    world.Fluid[i] = FluidCell.Empty;
                }
            }

            int FloorAt(int x) => floor[math.clamp(x - c.x, -half.x, half.x) + half.x];
            int CeilingAt(int x) => ceiling[math.clamp(x - c.x, -half.x, half.x) + half.x];
            bool Inside(int x, int y) => math.abs(x - c.x) <= half.x && y > FloorAt(x) && y <= CeilingAt(x);

            void Set(int x, int y, ushort material, bool intoRock)
            {
                if (x < edge || x >= world.Width - edge || y < edge || y >= world.Height) return;
                int i = world.Index(x, y);
                var cell = world.Cells[i];
                if (cell.Material == bedrock || cell.Material == wood && material != wood) return;
                if (cell.IsOpen)
                {
                    if (!Inside(x, y)) return; // never fill another cave
                }
                else if (!intoRock || content.MaterialSim[cell.Material].DropItem != ContentDB.NoItem && cell.Material != leaves) return;
                world.Cells[i] = new Cell(material, Cell.Full, cell.Flags);
                world.Fluid[i] = FluidCell.Empty;
            }

            // Trunk: flared at the foot, leaning a little, as tall as about half the cavern.
            int baseY = FloorAt(c.x);
            int trunk = (int)(half.y * 0.8f);
            float lean = rng.NextFloat(0f, 6.28f);
            int2 crown = c;
            for (int h = -2; h <= trunk; h++)
            {
                float k = math.saturate(h / (float)trunk);
                float w = math.lerp(4.4f, 2.2f, k) + 3.6f * math.exp(-math.max(0, h) / 2.4f);
                int offset = (int)math.round(math.sin(h * 0.11f + lean) * 1.3f);
                int y = baseY + h;
                for (int x = (int)math.floor(c.x + offset - w); x <= (int)math.ceil(c.x + offset + w); x++)
                    if (math.abs(x - (c.x + offset)) <= w) Set(x, y, wood, true);
                crown = new int2(c.x + offset, y);
            }

            // Branches up and out from the upper trunk; each ends in a canopy.
            var tips = new List<float2> { new float2(crown.x, crown.y + 2) };
            int branches = rng.NextInt(5, 9);
            for (int b = 0; b < branches; b++)
            {
                int side = (b & 1) == 0 ? 1 : -1;
                float startH = math.lerp(0.45f, 1f, (b + rng.NextFloat()) / branches);
                var p = new float2(c.x + (int)math.round(math.sin(startH * trunk * 0.11f + lean) * 1.3f), baseY + startH * trunk);
                float angle = math.radians(rng.NextFloat(4f, 30f));
                var dir = new float2(side * math.cos(angle), math.sin(angle));
                float length = rng.NextFloat(half.x * 0.45f, half.x * 0.8f);
                float thick = rng.NextFloat(1.8f, 2.6f);
                for (float s = 0f; s < length; s += 0.5f)
                {
                    float r = math.lerp(thick, 0.6f, s / length);
                    if (p.y + 7 > CeilingAt((int)p.x) || math.abs(p.x - c.x) > half.x - 9) break;
                    if (math.abs(s - length * 0.55f) < 0.25f) tips.Add(p + new float2(0f, 2f)); // a smaller canopy midway
                    for (int y = (int)math.floor(p.y - r); y <= (int)math.ceil(p.y + r); y++)
                    for (int x = (int)math.floor(p.x - r); x <= (int)math.ceil(p.x + r); x++)
                        if (math.lengthsq(new float2(x, y) - p) <= r * r + 0.25f) Set(x, y, wood, false);
                    dir = math.normalize(dir + new float2(0f, 0.01f)); // curling upward
                    p += dir * 0.5f;
                }
                tips.Add(p);
            }

            // Canopies: lumpy flattened blobs of glowing leaves, kept two cells under the cavern roof.
            foreach (var tip in tips)
            {
                float rx = rng.NextFloat(8f, 13f), ry = rng.NextFloat(4.5f, 7f);
                float2 o = rng.NextFloat2(-100f, 100f);
                for (int y = (int)(tip.y - ry - 1); y <= (int)(tip.y + ry + 1); y++)
                for (int x = (int)(tip.x - rx - 1); x <= (int)(tip.x + rx + 1); x++)
                {
                    float2 d = (new float2(x, y) - tip) / new float2(rx, y < tip.y ? ry * 0.7f : ry);
                    float lump = noise.snoise(new float2(x, y) * 0.35f + o) * 0.3f;
                    if (math.lengthsq(d) + lump > 1f || y > CeilingAt(x) - 2) continue;
                    int i = world.InBounds(x, y) ? world.Index(x, y) : -1;
                    if (i >= 0 && world.Cells[i].IsOpen) Set(x, y, leaves, false);
                }
            }

            // Big roots: along the floor (humps over it) then down into the rock, each with a side branch.
            int roots = rng.NextInt(5, 8);
            for (int r = 0; r < roots; r++)
            {
                int side = (r & 1) == 0 ? 1 : -1;
                int x = c.x + side * rng.NextInt(1, 4), y = baseY + 1;
                int length = rng.NextInt(half.x / 2, half.x);
                float down = rng.NextFloat(0.2f, 0.45f);
                for (int n = 0; n < length; n++)
                {
                    int rows = n < length * 0.35f ? 3 : n < length * 0.75f ? 2 : 1;
                    for (int k = 0; k < rows; k++) Set(x, y - k, root, true);
                    if (rng.NextFloat() < down + n * 0.006f) y--;
                    else x += side;
                    if (y <= edge + 1 || math.abs(x - c.x) > half.x + 20) break;
                    if (n == length / 2 && rng.NextBool())
                    {
                        int bx = x, by = y;
                        for (int m = 0; m < length / 3; m++)
                        {
                            Set(bx, by, root, true);
                            if (rng.NextFloat() < 0.6f) by--;
                            else bx += side;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Where the fruit grows: open cells in the cavern standing on canopy or elderwood with open space above, by chance
        /// (<c>elderFruitChance</c>, from the seed), at least 3 cells apart, at most <c>elderFruitMax</c>.
        /// </summary>
        public static List<int2> FruitSpots(WorldGrid world, ContentDB content, in ElderSite site, uint seed)
        {
            var gen = content.WorldGen;
            var spots = new List<int2>();
            if (gen.elderFruitMax <= 0 || gen.elderWood == null) return spots;
            ushort wood = content.GetMaterialIndex(gen.elderWood);
            ushort leaves = gen.elderLeaves != null ? content.GetMaterialIndex(gen.elderLeaves) : wood;
            int2 lo = site.Center - site.Half, hi = site.Center + site.Half;
            for (int y = hi.y; y >= lo.y && spots.Count < gen.elderFruitMax; y--)
            for (int x = lo.x; x <= hi.x && spots.Count < gen.elderFruitMax; x++)
            {
                if (!world.InBounds(x, y + 1) || !world.InBounds(x, y - 1)) continue;
                var cell = world[x, y];
                var below = world[x, y - 1];
                if (!cell.IsOpen || below.IsOpen || !world[x, y + 1].IsOpen) continue;
                if (below.Material != leaves && below.Material != wood) continue;
                if ((math.hash(new uint3((uint)x, (uint)y, seed ^ 0xA991Eu)) & 0xFFFF) >= gen.elderFruitChance * 65536f) continue;
                bool far = true;
                foreach (var s in spots)
                    if (math.distancesq(s, new int2(x, y)) < 9) { far = false; break; }
                if (far) spots.Add(new int2(x, y));
            }
            return spots;
        }

        /// <summary>
        /// Reveals the tree's own cells (trunk, branches, canopy, and its roots near the cavern): the fog only shows cells next
        /// to explored space, which would leave the thick trunk and canopy as hollow outlines.
        /// </summary>
        public static void RevealTree(WorldGrid world, ContentDB content, in ElderSite site)
        {
            var gen = content.WorldGen;
            if (gen.elderWood == null) return;
            ushort wood = content.GetMaterialIndex(gen.elderWood);
            ushort leaves = gen.elderLeaves != null ? content.GetMaterialIndex(gen.elderLeaves) : wood;
            ushort root = gen.treeRootMaterial != null ? content.GetMaterialIndex(gen.treeRootMaterial) : wood;
            int2 lo = site.Center - site.Half - new int2(20, 30), hi = site.Center + site.Half + new int2(20, 0);
            for (int y = math.max(0, lo.y); y <= math.min(world.Height - 1, hi.y); y++)
            for (int x = math.max(0, lo.x); x <= math.min(world.Width - 1, hi.x); x++)
            {
                int i = world.Index(x, y);
                var cell = world.Cells[i];
                if (cell.IsRevealed || cell.Material != wood && cell.Material != leaves && cell.Material != root) continue;
                cell.Flags |= CellFlags.Revealed;
                world.Cells[i] = cell;
                world.MarkChanged(x, y, ChunkFlags.TerrainUpload);
            }
        }

        /// <summary>True if the world holds the tree (its trunk foot is elderwood): a world made before Milestone 11 batch 6 has none.</summary>
        public static bool IsBuilt(WorldGrid world, ContentDB content, in ElderSite site)
        {
            var gen = content.WorldGen;
            if (gen.elderWood == null) return false;
            ushort wood = content.GetMaterialIndex(gen.elderWood);
            int2 lo = site.Center - new int2(4, site.Half.y), hi = site.Center + new int2(4, site.Half.y);
            for (int y = lo.y; y <= hi.y; y++)
            for (int x = lo.x; x <= hi.x; x++)
                if (world.InBounds(x, y) && world[x, y].Material == wood) return true;
            return false;
        }
    }
}
