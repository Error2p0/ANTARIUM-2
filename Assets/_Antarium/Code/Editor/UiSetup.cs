using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;

namespace Antarium.Editor
{
    /// <summary>
    /// Creates the pixel-art UI skin (9-slice bevel overlays drawn over the HUD's background colours, so every state
    /// colour keeps working) and the pixel font assets: VT323 for text, Silkscreen (capitals) for titles. Both are
    /// strict-grid fonts under the SIL Open Font License. Safe to re-run: textures are rewritten, font assets are kept.
    /// </summary>
    public static class UiSetup
    {
        const string SkinFolder = "Assets/_Antarium/UI/Skin";
        const string FontFolder = "Assets/_Antarium/UI/Fonts";
        /// <summary>UI Toolkit draws text from a signed distance field, sampled large so the pixel corners stay sharp.</summary>
        const int SamplingSize = 64;
        const int Padding = 6;

        [MenuItem("Antarium/Setup/Create UI Skin")]
        public static void CreateUiSkin()
        {
            ContentSetup.EnsureFolder(SkinFolder);
            // Raised: light top / left, dark bottom / right. Pressed: the reverse. Frame: a raised edge with a dark inner line.
            Write("bevel", 6, (x, y, s) => Edge(x, y, s, light: 0.16f, dark: 0.38f));
            Write("bevel_down", 6, (x, y, s) => Edge(x, y, s, light: -0.38f, dark: -0.12f));
            Write("frame", 8, (x, y, s) =>
            {
                var c = Edge(x, y, s, light: 0.14f, dark: 0.45f);
                bool inner = (x == 1 || y == 1 || x == s - 2 || y == s - 2) && x > 0 && y > 0 && x < s - 1 && y < s - 1;
                return inner && c.a == 0f ? new Color(0f, 0f, 0f, 0.35f) : c;
            });
            Write("inset", 6, (x, y, s) => Edge(x, y, s, light: -0.4f, dark: -0.1f));
            AssetDatabase.Refresh();
            foreach (var name in new[] { "bevel", "bevel_down", "frame", "inset" })
                ConfigureTexture($"{SkinFolder}/{name}.png");
            CreateFontAsset("VT323-Regular");
            CreateFontAsset("Silkscreen-Regular");
            AssetDatabase.SaveAssets();
            Debug.Log("UI skin and pixel font created.");
        }

        delegate Color Pixel(int x, int y, int size);

        /// <summary>One art pixel of edge: positive light = white highlight on top / left, dark = black shade bottom / right; negative swaps sides.</summary>
        static Color Edge(int x, int y, int size, float light, float dark)
        {
            // y = 0 is the bottom row of the texture.
            bool top = y == size - 1, bottom = y == 0, left = x == 0, right = x == size - 1;
            bool corner = (top || bottom) && (left || right);
            if (corner) return new Color(0f, 0f, 0f, 0.55f);
            float hi = light >= 0f ? light : 0f, lo = light < 0f ? -light : 0f;
            float shade = dark >= 0f ? dark : 0f, glow = dark < 0f ? -dark : 0f;
            if (top || left) return hi > 0f ? new Color(1f, 0.95f, 0.85f, hi) : new Color(0f, 0f, 0f, lo);
            if (bottom || right) return shade > 0f ? new Color(0f, 0f, 0f, shade) : new Color(1f, 0.95f, 0.85f, glow);
            return new Color(0f, 0f, 0f, 0f);
        }

        static void Write(string name, int size, Pixel pixel)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, pixel(x, y, size));
            tex.Apply();
            File.WriteAllBytes($"{SkinFolder}/{name}.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        static void ConfigureTexture(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) return;
            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
        }

        static void CreateFontAsset(string name)
        {
            string path = $"{FontFolder}/{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<FontAsset>(path) != null) return;
            var font = AssetDatabase.LoadAssetAtPath<Font>($"{FontFolder}/{name}.ttf");
            if (font == null)
            {
                Debug.LogError($"Missing {FontFolder}/{name}.ttf.");
                return;
            }
            var asset = FontAsset.CreateFontAsset(font, SamplingSize, Padding, GlyphRenderMode.SDFAA_HINTED, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            asset.name = name;
            AssetDatabase.CreateAsset(asset, path);
            var atlas = asset.atlasTexture;
            atlas.name = name + " Atlas";
            AssetDatabase.AddObjectToAsset(atlas, asset);
            asset.material.name = name + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            // Latin-1 up front so the common glyphs never pop in at runtime.
            var chars = new System.Text.StringBuilder();
            for (int c = 32; c < 127; c++) chars.Append((char)c);
            for (int c = 160; c < 256; c++) chars.Append((char)c);
            chars.Append("–—‘’“”•…×→←↑↓∞");
            asset.TryAddCharacters(chars.ToString(), out _);
            EditorUtility.SetDirty(asset);
        }
    }
}
