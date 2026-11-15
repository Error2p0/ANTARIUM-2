using System.Collections.Generic;
using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>A rare structure on the surface (Milestone 11) across columns <see cref="X0"/>..<see cref="X1"/>.</summary>
    public struct SurfaceFeature
    {
        public SurfaceFeatureKind Kind;
        /// <summary>Index of its rule in <c>WorldGenSettings.surfaceFeatures</c> (material, finds).</summary>
        public int Rule;
        public int X0, X1;
        /// <summary>Random variation (shape details), from the seed.</summary>
        public uint Variant;

        public int Mid => (X0 + X1) / 2;
        public bool Contains(int x, int margin = 0) => x >= X0 - margin && x <= X1 + margin;
    }

    /// <summary>
    /// Where the rare surface structures stand: derived from the seed and the world size only (like <see cref="SurfaceWater"/>),
    /// so world generation and the plants agree. Kept <c>featureClearance</c> columns from the nest, clear of lakes and rivers
    /// and apart from each other. World generation builds them (<see cref="WorldGenerator"/>).
    /// </summary>
    public static class SurfaceFeatures
    {
        const int Spacing = 10, WaterMargin = 6;

        public static List<SurfaceFeature> Place(int width, WorldGenSettings gen, uint seed) =>
            Place(width, gen, seed, SurfaceWater.Place(width, gen, seed));

        public static List<SurfaceFeature> Place(int width, WorldGenSettings gen, uint seed, List<WaterBody> water)
        {
            var list = new List<SurfaceFeature>();
            if (gen.surfaceFeatures == null) return list;
            var rng = new Random(math.hash(new uint2(seed, 0xFEA7u)) | 1u);
            int nest = width / 2, clear = gen.featureClearance + gen.shaftHalfWidth;
            int edge = gen.bedrockThickness + 6;
            for (int r = 0; r < gen.surfaceFeatures.Count; r++)
            {
                var rule = gen.surfaceFeatures[r];
                if (rule == null || rule.material == null) continue;
                int lo = math.max(0, rule.count.x);
                int count = rng.NextInt(lo, math.max(lo, rule.count.y) + 1);
                int wLo = math.max(3, rule.width.x), wHi = math.max(wLo, rule.width.y);
                for (int n = 0; n < count; n++)
                for (int attempt = 0; attempt < 32; attempt++)
                {
                    int size = rng.NextInt(wLo, wHi + 1);
                    int x0 = rng.NextInt(edge, math.max(edge + 1, width - edge - size));
                    int x1 = x0 + size - 1;
                    if (x1 >= width - edge || x1 >= nest - clear && x0 <= nest + clear) continue;
                    bool free = true;
                    foreach (var b in water)
                        if (x0 <= b.X1 + WaterMargin && x1 >= b.X0 - WaterMargin) { free = false; break; }
                    foreach (var f in list)
                        if (x0 <= f.X1 + Spacing && x1 >= f.X0 - Spacing) { free = false; break; }
                    if (!free) continue;
                    list.Add(new SurfaceFeature { Kind = rule.kind, Rule = r, X0 = x0, X1 = x1, Variant = rng.NextUInt() });
                    break;
                }
            }
            list.Sort((a, b) => a.X0.CompareTo(b.X0));
            return list;
        }

        /// <summary>Index of the structure covering column <paramref name="x"/> (with <paramref name="margin"/>), or -1.</summary>
        public static int At(List<SurfaceFeature> features, int x, int margin = 0)
        {
            for (int i = 0; i < features.Count; i++)
                if (features[i].Contains(x, margin)) return i;
            return -1;
        }

        public static string Name(SurfaceFeatureKind kind) => kind switch
        {
            SurfaceFeatureKind.Boulder => "Boulder",
            SurfaceFeatureKind.Overhang => "Rock Overhang",
            SurfaceFeatureKind.HollowLog => "Hollow Log",
            SurfaceFeatureKind.Anthill => "Abandoned Anthill",
            _ => "Standing Stones",
        };
    }
}
