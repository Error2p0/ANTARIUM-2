using Unity.Mathematics;
using UnityEngine;

namespace Antarium.Presentation
{
    /// <summary>
    /// Generated tree sprites (Milestone 11 batch 6), used where no PNG override (<c>Art/Surface/tree_*.png</c>) exists: an apple
    /// tree, an oak and a pine, two frames each (the crown sways a pixel). Drawn into an atlas with row 0 at the bottom.
    /// The PNGs from <c>tools/art</c> must have exactly these sizes (two frames side by side).
    /// </summary>
    public static class TreeArt
    {
        /// <summary>Sprite size in art pixels: apple tree, oak, pine.</summary>
        public static readonly int2[] Sizes = { new(48, 56), new(64, 64), new(36, 72) };
        public static readonly string[] Names = { "tree_apple", "tree_oak", "tree_pine" };

        static readonly Color32[] Wood = { Hex(0x22140c), Hex(0x3a2414), Hex(0x563820), Hex(0x76502e), Hex(0x986c40) };
        static readonly Color32[] Leaf = { Hex(0x18321a), Hex(0x265022), Hex(0x3a742a), Hex(0x56a038), Hex(0x84c84c), Hex(0xb8e676) };
        static readonly Color32[] Needle = { Hex(0x0e2018), Hex(0x163424), Hex(0x224c34), Hex(0x316844), Hex(0x4a8858), Hex(0x72aa70) };
        static readonly Color32[] Apple = { Hex(0x6e1618), Hex(0xa82420), Hex(0xdc4430), Hex(0xf8785a) };

        /// <summary>Paints both frames of tree <paramref name="kind"/> (0 apple, 1 oak, 2 pine) side by side at (<paramref name="ox"/>, <paramref name="oy"/>), a pixel apart.</summary>
        public static void Paint(Color32[] atlas, int atlasWidth, int ox, int oy, int kind)
        {
            int2 size = Sizes[kind];
            var frame = new Color32[size.x * size.y];
            int sway;
            if (kind == 2) sway = PaintPine(frame, size);
            else sway = PaintBroadleaf(frame, size, kind == 0);
            for (int f = 0; f < 2; f++)
            for (int y = 0; y < size.y; y++)
            for (int x = 0; x < size.x; x++)
            {
                int sx = f == 1 && y >= sway ? x - 1 : x;
                if (sx < 0) continue;
                var c = frame[y * size.x + sx];
                if (c.a == 0) continue;
                atlas[(oy + y) * atlasWidth + ox + f * (size.x + 1) + x] = c;
            }
        }

        static int PaintBroadleaf(Color32[] px, int2 size, bool apple)
        {
            int w = size.x, h = size.y;
            float cx = w * 0.5f - 0.5f;
            int trunkTop = apple ? 24 : 28;
            float baseHalf = apple ? 3f : 4f;
            // Crown lobes (x offset from the centre, y, radius).
            float3[] lobes = apple
                ? new[] { new float3(0, 36, 13), new float3(-12, 33, 9), new float3(12, 34, 9), new float3(-7, 45, 9), new float3(7, 45, 9), new float3(0, 49, 7), new float3(-16, 38, 6), new float3(16, 39, 6) }
                : new[] { new float3(0, 42, 15), new float3(-17, 37, 11), new float3(17, 38, 11), new float3(-10, 51, 10), new float3(10, 51, 10), new float3(0, 56, 7), new float3(-25, 40, 6), new float3(25, 41, 6), new float3(-5, 33, 10), new float3(6, 33, 10) };

            // Trunk with a flared foot and two limbs into the crown.
            for (int y = 0; y <= trunkTop + 6; y++)
            {
                float k = math.saturate(y / (float)trunkTop);
                float half = math.lerp(baseHalf, baseHalf * 0.55f, k) + 2.2f * math.exp(-y / 1.6f);
                for (int x = 0; x < w; x++)
                {
                    float d = x - cx;
                    if (math.abs(d) > half) continue;
                    float t = d / half;
                    int tone = t < -0.55f ? 4 : t < 0.15f ? 3 : t < 0.7f ? 2 : 1;
                    if (Hash(x, y, 3) % 9 == 0) tone = math.max(1, tone - 1);
                    px[y * w + x] = Wood[tone];
                }
            }
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 12; i++)
                {
                    int x = (int)math.round(cx + s * (1 + i * 0.9f)), y = trunkTop + 2 + i;
                    if (x >= 0 && x < w && y < h) { px[y * w + x] = Wood[2]; if (x + 1 < w) px[y * w + x + 1] = Wood[1]; }
                }

            // Leaves: the union of the lobes, lit from the top left, a darker rim at the bottom right, leafy speckle.
            var inside = new bool[w * h];
            var tones = new int[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float best = 99f;
                float2 n = default;
                foreach (var l in lobes)
                {
                    float2 d = (new float2(x - cx - l.x, y - l.y)) / l.z;
                    float q = math.length(d);
                    float jag = (Hash(x, y, 7) % 100) / 100f * 0.12f;
                    if (q - jag < best) { best = q - jag; n = d; }
                }
                if (best > 1f) continue;
                float lit = math.dot(n, math.normalize(new float2(-0.6f, 0.8f)));
                int tone = lit > 0.55f ? 4 : lit > 0.1f ? 3 : lit > -0.45f ? 2 : 1;
                if (lit > 0.75f && Hash(x, y, 11) % 3 == 0) tone = 5;
                uint r = Hash(x, y, 13) % 100;
                if (r < 12) tone = math.max(1, tone - 1);
                else if (r < 18) tone = math.min(5, tone + 1);
                inside[y * w + x] = true;
                tones[y * w + x] = tone;
            }
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!inside[y * w + x]) continue;
                bool rim = y == 0 || x == w - 1 || !inside[(y - 1) * w + x] || !inside[y * w + x + 1];
                int tone = rim ? math.min(tones[y * w + x], 1) : tones[y * w + x];
                px[y * w + x] = Leaf[tone];
            }

            if (apple)
            {
                int2[] fruit = { new(-9, 30), new(5, 28), new(13, 33), new(-15, 36), new(-3, 38), new(9, 41), new(-8, 45), new(3, 49), new(16, 39), new(-1, 31) };
                foreach (var a in fruit)
                {
                    int x = (int)math.round(cx + a.x), y = a.y;
                    if (x < 1 || x + 1 >= w || y + 1 >= h) continue;
                    px[y * w + x] = Apple[1];
                    px[y * w + x + 1] = Apple[0];
                    px[(y + 1) * w + x] = Apple[3];
                    px[(y + 1) * w + x + 1] = Apple[2];
                }
            }
            return trunkTop + 6;
        }

        static int PaintPine(Color32[] px, int2 size)
        {
            int w = size.x, h = size.y;
            float cx = w * 0.5f - 0.5f;
            for (int y = 0; y < h - 8; y++)
            for (int x = 0; x < w; x++)
            {
                float d = x - cx;
                if (math.abs(d) > 1.6f + 1.5f * math.exp(-y / 1.5f)) continue;
                px[y * w + x] = Wood[d < -0.5f ? 3 : d < 0.8f ? 2 : 1];
            }
            // Tiers of drooping boughs, narrowing to the tip.
            const int start = 9, tier = 9;
            for (int y = start; y < h; y++)
            {
                float k = (y - start) / (float)(h - start);
                float t = ((y - start) % tier) / (float)tier;
                float half = (w * 0.5f - 1f) * (1f - k) * (1f - 0.42f * t) + 1f;
                for (int x = 0; x < w; x++)
                {
                    float d = (x - cx) / half;
                    if (math.abs(d) > 1f) continue;
                    int tone = d < -0.45f ? 4 : d < 0.1f ? 3 : d < 0.6f ? 2 : 1;
                    if (t < 0.12f) tone = math.max(0, tone - 2);              // the shaded underside of a tier
                    else if (t > 0.8f && d < 0f) tone = math.min(5, tone + 1); // snow-free lit tops
                    uint r = Hash(x, y, 17) % 100;
                    if (r < 10) tone = math.max(0, tone - 1);
                    px[y * w + x] = Needle[tone];
                }
            }
            return start + 4;
        }

        static uint Hash(int x, int y, uint salt) => math.hash(new uint3((uint)x, (uint)y, salt));

        static Color32 Hex(int rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
    }
}
