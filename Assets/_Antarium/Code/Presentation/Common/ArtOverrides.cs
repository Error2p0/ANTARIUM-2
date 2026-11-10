using System.Linq;
using Antarium.Content;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Antarium.Presentation
{
    /// <summary>
    /// One replaceable sprite of an atlas: the PNG called <see cref="Name"/> in the <see cref="ArtLibrary"/> (its path
    /// under <c>Art/</c> without the extension) holds the frames side by side, one per rect in <see cref="Rects"/>.
    /// </summary>
    public readonly struct ArtSlot
    {
        public readonly string Name;
        /// <summary>Atlas rect indices, one per frame, in strip order. All the same size.</summary>
        public readonly int[] Rects;

        public ArtSlot(string name, params int[] rects)
        {
            Name = name;
            Rects = rects;
        }

        /// <summary>A slot whose frames are consecutive rects.</summary>
        public static ArtSlot Strip(string name, int firstRect, int frames = 1)
        {
            var rects = new int[frames];
            for (int f = 0; f < frames; f++) rects[f] = firstRect + f;
            return new ArtSlot(name, rects);
        }
    }

    /// <summary>
    /// Copies PNG art from the <see cref="ArtLibrary"/> over the generated sprites of an atlas. A missing PNG keeps the
    /// generated sprite; a PNG of the wrong size (or not readable) is reported and skipped.
    /// </summary>
    public static class ArtOverrides
    {
        /// <summary>Applies every slot that has a PNG. Returns how many were replaced.</summary>
        public static int Apply(ArtLibrary art, ArtSlot[] slots, Color32[] atlas, int atlasWidth, int atlasHeight, NativeArray<float4> rects)
        {
            if (art == null) return 0;
            int applied = 0;
            foreach (var slot in slots)
                if (Apply(art, slot, atlas, atlasWidth, atlasHeight, rects)) applied++;
            return applied;
        }

        /// <summary>Replaces the rects of <paramref name="slot"/> with its PNG strip. Returns true if the PNG was used.</summary>
        public static bool Apply(ArtLibrary art, ArtSlot slot, Color32[] atlas, int atlasWidth, int atlasHeight, NativeArray<float4> rects)
        {
            if (art == null || !art.TryGet(slot.Name, out var texture)) return false;
            int frames = slot.Rects.Length;
            var first = PixelRect(rects[slot.Rects[0]], atlasWidth, atlasHeight);
            if (!Fits(texture, first.width * frames, first.height, out string problem))
            {
                Debug.LogWarning($"Art '{slot.Name}': {problem}; expected {frames} frame(s) of {first.width}x{first.height} " +
                                 $"({first.width * frames}x{first.height}). Using the generated sprite.");
                return false;
            }
            var pixels = texture.GetPixels32();
            for (int f = 0; f < frames; f++)
            {
                var rect = PixelRect(rects[slot.Rects[f]], atlasWidth, atlasHeight);
                if (rect.width != first.width || rect.height != first.height)
                {
                    Debug.LogWarning($"Art '{slot.Name}': frame {f} has another size in the atlas; skipped.");
                    continue;
                }
                for (int y = 0; y < rect.height; y++)
                for (int x = 0; x < rect.width; x++)
                {
                    var c = pixels[y * texture.width + f * rect.width + x];
                    // Sprites are alpha-tested: keep clear texels clean so nothing bleeds.
                    atlas[(rect.y + y) * atlasWidth + rect.x + x] = c.a < 128 ? default : new Color32(c.r, c.g, c.b, 255);
                }
            }
            return true;
        }

        /// <summary>Whether <paramref name="texture"/> can be read and has exactly the expected size.</summary>
        public static bool Fits(Texture2D texture, int width, int height, out string problem)
        {
            problem = null;
            if (texture == null) problem = "missing texture";
            else if (!texture.isReadable) problem = "not readable (run Antarium > Art > Import Overrides)";
            else if (texture.width != width || texture.height != height) problem = $"is {texture.width}x{texture.height}";
            return problem == null;
        }

        /// <summary>The pixel rect of a UV rect (xMin, yMin, xMax, yMax).</summary>
        public static RectInt PixelRect(float4 uv, int atlasWidth, int atlasHeight)
        {
            int x = (int)math.round(uv.x * atlasWidth), y = (int)math.round(uv.y * atlasHeight);
            return new RectInt(x, y, (int)math.round(uv.z * atlasWidth) - x, (int)math.round(uv.w * atlasHeight) - y);
        }

        /// <summary>
        /// The colours the terrain shader needs next to a tile: its average (background walls), and the average of its
        /// darkest and of its lightest fifth (the outline and the lit top edge are derived from those). Rounded to 8 bits.
        /// </summary>
        public static void TileColors(Color32[] pixels, out Color32 baseColor, out Color32 shadow, out Color32 highlight)
        {
            var sorted = pixels.OrderBy(c => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f).ToArray();
            int fifth = math.max(1, sorted.Length / 5);
            baseColor = Average(sorted, 0, sorted.Length);
            shadow = Average(sorted, 0, fifth);
            highlight = Average(sorted, sorted.Length - fifth, fifth);
        }

        static Color32 Average(Color32[] pixels, int start, int count)
        {
            float r = 0f, g = 0f, b = 0f;
            for (int i = start; i < start + count; i++)
            {
                r += pixels[i].r;
                g += pixels[i].g;
                b += pixels[i].b;
            }
            return new Color32((byte)math.round(r / count), (byte)math.round(g / count), (byte)math.round(b / count), 255);
        }
    }
}
