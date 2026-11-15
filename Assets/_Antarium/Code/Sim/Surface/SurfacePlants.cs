using System.Collections.Generic;
using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>A plant or tree on the ground surface: leaves fall under it by day, its roots reach into the topsoil.</summary>
    public struct SurfacePlant
    {
        /// <summary>Column it grows in (on the ground cell at <see cref="WorldGrid.SurfaceY"/>).</summary>
        public int X;
        /// <summary>
        /// Look: 0 fern, 1 bush, 2 tall stalk, 3 berry bush (<see cref="SurfacePlants.BerryBush"/>: berries fall under it), and
        /// the trees (Milestone 11): 4 apple tree (<see cref="SurfacePlants.AppleTree"/>: apples fall under it), 5 oak, 6 pine.
        /// </summary>
        public byte Kind;
        /// <summary>Random variation for the sprite (presentation).</summary>
        public byte Variant;

        public bool IsTree => Kind >= SurfacePlants.AppleTree;
    }

    /// <summary>
    /// Where surface plants and trees grow: derived from the seed and the world size only (not saved), so world generation
    /// (roots) and the running colony (leaves, berries, apples, drawing) agree.
    /// </summary>
    public static class SurfacePlants
    {
        /// <summary><see cref="SurfacePlant.Kind"/> of a berry bush.</summary>
        public const byte BerryBush = 3;
        /// <summary><see cref="SurfacePlant.Kind"/> of an apple tree, the first tree kind (Milestone 11).</summary>
        public const byte AppleTree = 4;
        public const byte Oak = 5;
        public const byte Pine = 6;
        /// <summary>Plants keep this many columns from a tree's trunk.</summary>
        public const int TreeSpread = 3;

        /// <summary>
        /// Plants every <c>plantGap</c> columns, not by the nest shaft and not in or right beside lakes, rivers and structures.
        /// A share (<c>berryBushShare</c>, by its own hash so the other plants keep their looks) are berry bushes. Trees every
        /// <c>treeGap</c> columns (their own random stream), plants keep clear of them. Sorted by column.
        /// </summary>
        public static List<SurfacePlant> Place(int width, WorldGenSettings gen, uint seed) =>
            Place(width, gen, seed, SurfaceWater.Place(width, gen, seed));

        /// <summary>The same, around the given lakes and rivers (a world saved before Milestone 11 has none).</summary>
        public static List<SurfacePlant> Place(int width, WorldGenSettings gen, uint seed, List<WaterBody> water) =>
            Place(width, gen, seed, water, SurfaceFeatures.Place(width, gen, seed, water));

        /// <summary>The same, around the given lakes, rivers and structures.</summary>
        public static List<SurfacePlant> Place(int width, WorldGenSettings gen, uint seed, List<WaterBody> water, List<SurfaceFeature> features)
        {
            var trees = PlaceTrees(width, gen, seed, water, features);
            var plants = new List<SurfacePlant>();
            var rng = new Random(math.hash(new uint2(seed, 0x91A47u)) | 1u);
            int lo = math.max(2, gen.plantGap.x), hi = math.max(lo, gen.plantGap.y);
            int nest = width / 2, clear = gen.plantClearance + gen.shaftHalfWidth;
            int edge = gen.bedrockThickness + 2;
            for (int x = edge + rng.NextInt(lo); x < width - edge; x += rng.NextInt(lo, hi + 1))
            {
                if (math.abs(x - nest) <= clear) continue;
                var plant = new SurfacePlant { X = x, Kind = (byte)rng.NextInt(3), Variant = (byte)rng.NextInt(256) };
                if (SurfaceWater.At(water, x, 2) >= 0) continue;
                if (features != null && SurfaceFeatures.At(features, x, 1) >= 0) continue;
                if (NearTree(trees, x)) continue;
                if ((math.hash(new uint2(seed, (uint)x * 0x9E37u + 0xBE77u)) & 0xFFFF) < gen.berryBushShare * 65536f) plant.Kind = BerryBush;
                plants.Add(plant);
            }
            if (trees.Count == 0) return plants;
            plants.AddRange(trees);
            plants.Sort((a, b) => a.X.CompareTo(b.X));
            return plants;
        }

        static bool NearTree(List<SurfacePlant> trees, int x)
        {
            foreach (var t in trees)
                if (math.abs(t.X - x) <= TreeSpread) return true;
            return false;
        }

        /// <summary>Trees (Milestone 11): every <c>treeGap</c> columns, clear of the nest, the water and the structures.</summary>
        static List<SurfacePlant> PlaceTrees(int width, WorldGenSettings gen, uint seed, List<WaterBody> water, List<SurfaceFeature> features)
        {
            var trees = new List<SurfacePlant>();
            if (gen.treeGap.x <= 0) return trees;
            var rng = new Random(math.hash(new uint2(seed, 0x7EE5u)) | 1u);
            int lo = math.max(8, gen.treeGap.x), hi = math.max(lo, gen.treeGap.y);
            int nest = width / 2, clear = gen.treeClearance + gen.shaftHalfWidth;
            int edge = gen.bedrockThickness + 8;
            for (int x = edge + rng.NextInt(lo); x < width - edge; x += rng.NextInt(lo, hi + 1))
            {
                float kind = rng.NextFloat();
                byte variant = (byte)rng.NextInt(256);
                if (math.abs(x - nest) <= clear) continue;
                if (SurfaceWater.At(water, x, 4) >= 0) continue;
                if (features != null && SurfaceFeatures.At(features, x, 4) >= 0) continue;
                byte k = kind < gen.appleTreeShare ? AppleTree : kind < gen.appleTreeShare + (1f - gen.appleTreeShare) * 0.5f ? Oak : Pine;
                trees.Add(new SurfacePlant { X = x, Kind = k, Variant = variant });
            }
            return trees;
        }

        /// <summary>A plant lives while the ground it stands on is intact and the cell above it is open sky.</summary>
        public static bool IsAlive(WorldGrid world, in SurfacePlant plant)
        {
            int x = plant.X, y = world.SurfaceY[x];
            if (!world.InBounds(x, y + 1)) return false;
            var ground = world[x, y];
            var above = world[x, y + 1];
            return !ground.IsOpen && above.IsOpen && above.Material == ContentDB.AirIndex;
        }
    }
}
