using Antarium.Content;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Antarium.Presentation
{
    /// <summary>
    /// Copies PNG art from the <see cref="ArtLibrary"/> over the generated sprites of an atlas. A sprite's PNG is a
    /// strip: its frames side by side, each the size of the atlas rect it replaces. A missing PNG keeps the generated
    /// sprite; a PNG of the wrong size (or not readable) is reported and skipped.
    /// </summary>
    public static class ArtOverrides
    {
        /// <summary>
        /// Replaces <paramref name="frames"/> consecutive rects starting at <paramref name="firstRect"/> with the strip
        /// called <paramref name="name"/>. Returns true if the PNG was used.
        /// </summary>
        public static bool Apply(ArtLibrary art, string name, Color32[] atlas, int atlasWidth, int atlasHeight,
            NativeArray<float4> rects, int firstRect, int frames)
        {
            if (art == null || !art.TryGet(name, out var texture)) return false;
            var first = PixelRect(rects[firstRect], atlasWidth, atlasHeight);
            if (!Fits(texture, first.width * frames, first.height, out string problem))
            {
                Debug.LogWarning($"Art '{name}': {problem}; expected {frames} frame(s) of {first.width}x{first.height} " +
                                 $"({first.width * frames}x{first.height}). Using the generated sprite.");
                return false;
            }
            var pixels = texture.GetPixels32();
            for (int f = 0; f < frames; f++)
            {
                var rect = PixelRect(rects[firstRect + f], atlasWidth, atlasHeight);
                if (rect.width != first.width || rect.height != first.height)
                {
                    Debug.LogWarning($"Art '{name}': frame {f} has another size in the atlas; skipped.");
                    continue;
                }
                for (int y = 0; y < rect.height; y++)
                for (int x = 0; x < rect.width; x++)
                {
                    var c = pixels[y * texture.width + f * rect.width + x];
                    // Sprites are alpha-tested: keep fully clear texels clean so nothing bleeds.
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
    }
}
