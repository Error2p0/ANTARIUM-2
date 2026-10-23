using Antarium.Content;
using Antarium.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Antarium.Presentation
{
    /// <summary>
    /// Builds the terrain Texture2DArray: one tileable 32x32 pixel-art slice per material. Uses the
    /// material's texture override when present, otherwise paints a deterministic procedural pattern
    /// from its palette, so new materials look reasonable before any art exists.
    /// </summary>
    public static class MaterialTextureBuilder
    {
        const int Size = WorldConstants.MaterialTextureSize;

        public static Texture2DArray Build(TerrainMaterialDef[] materials)
        {
            var array = new Texture2DArray(Size, Size, materials.Length, TextureFormat.RGBA32, false, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                name = "TerrainMaterials",
            };
            var pixels = new Color32[Size * Size];
            for (int i = 0; i < materials.Length; i++)
            {
                if (!TryCopyOverride(materials[i], pixels))
                    Paint(materials[i], pixels);
                array.SetPixels32(pixels, i);
            }
            array.Apply(false, true);
            return array;
        }

        static bool TryCopyOverride(TerrainMaterialDef def, Color32[] pixels)
        {
            var tex = def.textureOverride;
            if (tex == null) return false;
            if (tex.width != Size || tex.height != Size || !tex.isReadable)
            {
                Debug.LogWarning($"Texture override of '{def.Id}' must be a readable {Size}x{Size} texture; using the procedural pattern.");
                return false;
            }
            tex.GetPixels32().CopyTo(pixels, 0);
            return true;
        }

        public static void Paint(TerrainMaterialDef def, Color32[] pixels)
        {
            uint seed = StableHash(def.Id) | 1u;
            var rng = new Unity.Mathematics.Random(seed);
            float2 offset = rng.NextFloat2(0f, 100f);

            Color dark = def.shadowColor, mid = def.baseColor, light = def.highlightColor;

            // Base: three tones from tileable noise plus a little single-pixel grain.
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float2 p = new float2(x, y);
                float n = noise.pnoise(p / 8f + offset, new float2(Size / 8f)) * 0.7f
                          + noise.pnoise(p / 4f + offset * 1.7f, new float2(Size / 4f)) * 0.3f;
                if (def.pattern == PixelPattern.Strata)
                    n = noise.pnoise(new float2(x / 16f, y / 3f) + offset, new float2(Size / 16f, Size / 3f)) * 0.8f + n * 0.3f;

                Color c = n < -0.18f ? dark : n > 0.22f ? light : mid;
                float grain = rng.NextFloat();
                if (grain < 0.04f) c = dark;
                else if (grain > 0.97f) c = light;
                pixels[y * Size + x] = c;
            }

            switch (def.pattern)
            {
                case PixelPattern.Pebbles:
                    Blobs(pixels, ref rng, (int)(10 * def.accentAmount + 4), 1, 2, def.accentColor, Lighten(def.accentColor), Darken(def.accentColor));
                    break;
                case PixelPattern.Rock:
                    Blobs(pixels, ref rng, 7, 3, 5, mid, light, dark);
                    break;
                case PixelPattern.Ore:
                    Blobs(pixels, ref rng, (int)(8 * def.accentAmount + 3), 1, 3, def.accentColor, Lighten(def.accentColor), Darken(def.accentColor));
                    break;
                case PixelPattern.Crystal:
                    Shards(pixels, ref rng, (int)(6 * def.accentAmount + 3), def.accentColor, def.highlightColor);
                    break;
                case PixelPattern.Brick:
                    Bricks(pixels, ref rng, mid, light, dark, def.accentColor, def.accentAmount);
                    break;
                case PixelPattern.Mycelium:
                    Threads(pixels, ref rng, (int)(10 * def.accentAmount + 4), def.accentColor, Lighten(def.accentColor));
                    break;
                case PixelPattern.Sand:
                    Speckle(pixels, ref rng, (int)(60 * def.accentAmount + 20), def.accentColor, dark);
                    break;
            }
        }

        /// <summary>Running-bond blocks 8 px wide and 4 px tall: mortar lines, a lit top-left edge, a few cracked or accent blocks.</summary>
        static void Bricks(Color32[] px, ref Unity.Mathematics.Random rng, Color mid, Color light, Color dark, Color accent, float accentAmount)
        {
            const int W = 8, H = 4;
            for (int row = 0; row < Size / H; row++)
            {
                int shift = (row & 1) * (W / 2);
                for (int b = 0; b < Size / W; b++)
                {
                    Color face = rng.NextFloat() < accentAmount * 0.3f ? accent : rng.NextFloat() < 0.3f ? Color.Lerp(mid, dark, 0.35f) : mid;
                    for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        Color c = face;
                        if (y == 0 || x == 0) c = dark;                 // mortar
                        else if (y == H - 1 || x == 1) c = Color.Lerp(face, light, 0.5f); // lit edge
                        if (c != dark && rng.NextFloat() < 0.05f) c = Color.Lerp(c, dark, 0.5f);
                        Set(px, b * W + x + shift, row * H + y, c);
                    }
                }
            }
        }

        /// <summary>Wandering pale threads (fungal mycelium) with brighter knots.</summary>
        static void Threads(Color32[] px, ref Unity.Mathematics.Random rng, int count, Color thread, Color knot)
        {
            for (int t = 0; t < count; t++)
            {
                int2 p = rng.NextInt2(0, Size);
                int len = rng.NextInt(5, 14);
                for (int i = 0; i < len; i++)
                {
                    Set(px, p.x, p.y, thread);
                    p += new int2(rng.NextInt(-1, 2), rng.NextInt(-1, 2));
                }
                Set(px, p.x, p.y, knot);
            }
        }

        /// <summary>Scattered single grains in two colours (sand).</summary>
        static void Speckle(Color32[] px, ref Unity.Mathematics.Random rng, int count, Color grain, Color shade)
        {
            for (int i = 0; i < count; i++)
            {
                int2 p = rng.NextInt2(0, Size);
                Set(px, p.x, p.y, rng.NextBool() ? grain : shade);
            }
        }

        /// <summary>Round clusters with a highlight on the top-left and a shadow on the bottom-right.</summary>
        static void Blobs(Color32[] px, ref Unity.Mathematics.Random rng, int count, int minR, int maxR, Color fill, Color hi, Color lo)
        {
            for (int b = 0; b < count; b++)
            {
                int2 c = rng.NextInt2(0, Size);
                int r = rng.NextInt(minR, maxR + 1);
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    float d = math.length(new float2(dx, dy));
                    if (d > r + 0.25f) continue;
                    Color col = fill;
                    bool rim = d > r - 0.9f;
                    if (rim && (dx < 0 || dy > 0)) col = hi;
                    else if (rim) col = lo;
                    Set(px, c.x + dx, c.y + dy, col);
                }
            }
        }

        /// <summary>Diagonal crystal shards with a bright core line.</summary>
        static void Shards(Color32[] px, ref Unity.Mathematics.Random rng, int count, Color fill, Color hi)
        {
            for (int s = 0; s < count; s++)
            {
                int2 c = rng.NextInt2(0, Size);
                int len = rng.NextInt(4, 9);
                int dir = rng.NextBool() ? 1 : -1;
                for (int i = 0; i < len; i++)
                {
                    int x = c.x + i * dir, y = c.y + i;
                    Set(px, x, y, hi);
                    Set(px, x + 1, y, fill);
                    Set(px, x - 1, y, fill);
                }
            }
        }

        static void Set(Color32[] px, int x, int y, Color c)
        {
            x = ((x % Size) + Size) % Size;
            y = ((y % Size) + Size) % Size;
            px[y * Size + x] = c;
        }

        /// <summary>FNV-1a; string.GetHashCode is not stable across runtimes.</summary>
        static uint StableHash(string s)
        {
            uint h = 2166136261u;
            foreach (char ch in s)
                h = (h ^ ch) * 16777619u;
            return h;
        }

        static Color Lighten(Color c) => Color.Lerp(c, Color.white, 0.35f);
        static Color Darken(Color c) => Color.Lerp(c, Color.black, 0.35f);
    }
}
