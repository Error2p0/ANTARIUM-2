using System.Collections.Generic;
using System.Text.RegularExpressions;
using Antarium.Content;
using Antarium.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Antarium.Tests
{
    /// <summary>Milestone 10.7a: PNG art overrides (name → atlas slot, fallback to the generated sprite, size checks).</summary>
    public class ArtTests
    {
        readonly List<Object> _made = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _made)
                if (o != null) Object.DestroyImmediate(o);
            _made.Clear();
        }

        Texture2D Solid(int width, int height, Color32 color)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            _made.Add(texture);
            return texture;
        }

        /// <summary>A strip whose frame f is filled with colours[f].</summary>
        Texture2D Strip(int frameWidth, int height, params Color32[] colors)
        {
            var texture = new Texture2D(frameWidth * colors.Length, height, TextureFormat.RGBA32, false, false);
            var pixels = new Color32[texture.width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < texture.width; x++)
                pixels[y * texture.width + x] = colors[x / frameWidth];
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            _made.Add(texture);
            return texture;
        }

        ArtLibrary Library(params (string name, Texture2D texture)[] entries)
        {
            var library = ScriptableObject.CreateInstance<ArtLibrary>();
            foreach (var (name, texture) in entries)
                library.entries.Add(new ArtLibrary.Entry { name = name, texture = texture });
            library.Rebuild();
            _made.Add(library);
            return library;
        }

        /// <summary>Atlas pixels of a colony atlas built with <paramref name="art"/>.</summary>
        static Color32[] ColonyAtlas(ArtLibrary art, out int width, out int height, out RectInt[] rects)
        {
            var sprites = new ColonySprites(art);
            var pixels = sprites.Texture.GetPixels32();
            width = sprites.Texture.width;
            height = sprites.Texture.height;
            rects = new RectInt[sprites.Rects.Length];
            for (int i = 0; i < rects.Length; i++) rects[i] = ArtOverrides.PixelRect(sprites.Rects[i], width, height);
            Object.DestroyImmediate(sprites.Texture);
            sprites.Dispose();
            return pixels;
        }

        static Color32[] BuildingAtlas(ArtLibrary art, out int width, out int height, out RectInt[] rects)
        {
            var sprites = new BuildingSprites(art);
            var pixels = sprites.Texture.GetPixels32();
            width = sprites.Texture.width;
            height = sprites.Texture.height;
            rects = new RectInt[sprites.Rects.Length];
            for (int i = 0; i < rects.Length; i++) rects[i] = ArtOverrides.PixelRect(sprites.Rects[i], width, height);
            Object.DestroyImmediate(sprites.Texture);
            sprites.Dispose();
            return pixels;
        }

        static bool RectIs(Color32[] atlas, int atlasWidth, RectInt rect, Color32 color)
        {
            for (int y = rect.yMin; y < rect.yMax; y++)
            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                var c = atlas[y * atlasWidth + x];
                if (c.r != color.r || c.g != color.g || c.b != color.b || c.a != color.a) return false;
            }
            return true;
        }

        static readonly Color32 Magenta = new(255, 0, 255, 255), Cyan = new(0, 255, 255, 255), Lime = new(0, 255, 0, 255), Blue = new(0, 0, 255, 255);

        [Test]
        public void Slots_HaveUniqueNames_AndFramesOfOneSize()
        {
            ColonyAtlas(null, out int cw, out int ch, out var colony);
            BuildingAtlas(null, out int bw, out int bh, out var buildings);
            var names = new HashSet<string>();
            foreach (var (slots, rects, folders) in new[]
                     {
                         (ColonySprites.Slots, colony, new[] { "Colony/", "Enemies/" }),
                         (BuildingSprites.Slots, buildings, new[] { "Buildings/" }),
                     })
            {
                foreach (var slot in slots)
                {
                    Assert.IsTrue(names.Add(slot.Name), $"'{slot.Name}' is listed twice");
                    Assert.IsTrue(System.Array.Exists(folders, f => slot.Name.StartsWith(f)), $"'{slot.Name}' is in the wrong folder");
                    string file = slot.Name.Substring(slot.Name.IndexOf('/') + 1);
                    Assert.IsTrue(Regex.IsMatch(file, "^[a-z0-9_]+$"), $"'{slot.Name}': file names are lower case with underscores");
                    Assert.Greater(slot.Rects.Length, 0);
                    var first = rects[slot.Rects[0]];
                    Assert.Greater(first.width, 0, slot.Name);
                    foreach (int r in slot.Rects)
                        Assert.AreEqual(first.size, rects[r].size, $"'{slot.Name}': every frame has the same size");
                }
            }
            foreach (var (name, frames) in SurfaceView.SlotNames)
            {
                Assert.IsTrue(names.Add(name), $"'{name}' is listed twice");
                Assert.Greater(frames, 0);
            }
            // The names other code and the docs rely on.
            foreach (string name in new[] { "Colony/ant_worker", "Colony/ant_soldier", "Colony/ant_miner", "Colony/ant_engineer", "Colony/queen",
                         "Colony/egg", "Colony/princess", "Enemies/termite", "Enemies/wolf_spider", "Buildings/smelter", "Buildings/silk_belt",
                         "Buildings/water_wheel", "Surface/bird" })
                Assert.IsTrue(names.Contains(name), $"slot '{name}' is missing");
        }

        [Test]
        public void Png_ReplacesItsSlot_AndNothingElse()
        {
            var plain = ColonyAtlas(null, out int width, out _, out var rects);
            var egg = rects[ColonySprites.EggRect];
            var art = Library(("Colony/egg", Solid(egg.width, egg.height, Magenta)));
            var atlas = ColonyAtlas(art, out _, out _, out _);

            Assert.IsTrue(RectIs(atlas, width, egg, Magenta), "the egg PNG is in the egg rect");
            int changed = 0;
            for (int i = 0; i < atlas.Length; i++)
                if (!atlas[i].Equals(plain[i])) changed++;
            Assert.LessOrEqual(changed, egg.width * egg.height, "no pixel outside the egg rect changed");
            Assert.Greater(changed, 0);
        }

        [Test]
        public void Strip_FillsEveryFrame_InOrder()
        {
            ColonyAtlas(null, out int width, out _, out var rects);
            var art = Library(
                ("Colony/ant_worker", Strip(ColonySprites.WorkerWidth, ColonySprites.WorkerHeight, Magenta, Cyan, Lime, Blue)),
                ("Colony/queen", Strip(ColonySprites.QueenWidth, ColonySprites.QueenHeight, Lime, Blue)));
            var atlas = ColonyAtlas(art, out _, out _, out _);
            var colors = new[] { Magenta, Cyan, Lime, Blue };
            for (int f = 0; f < ColonySprites.WorkerFrames; f++)
                Assert.IsTrue(RectIs(atlas, width, rects[f], colors[f]), $"worker frame {f}");
            // The queen's two frames are not neighbours in the rect list.
            Assert.IsTrue(RectIs(atlas, width, rects[ColonySprites.QueenRect], Lime));
            Assert.IsTrue(RectIs(atlas, width, rects[ColonySprites.QueenBreathRect], Blue));

            var beltArt = Library(("Buildings/silk_belt", Strip(BuildingSprites.Cell, BuildingSprites.Cell, Magenta, Cyan, Lime, Blue)));
            var buildings = BuildingAtlas(beltArt, out int bw, out _, out var brects);
            for (int f = 0; f < BuildingSprites.AnimFrames; f++)
                Assert.IsTrue(RectIs(buildings, bw, brects[BuildingSprites.BeltRect + f], colors[f]), $"belt frame {f}");
        }

        [Test]
        public void MissingPng_KeepsTheGeneratedSprite()
        {
            var plain = ColonyAtlas(null, out _, out _, out _);
            var withEmptyLibrary = ColonyAtlas(Library(), out _, out _, out _);
            var withOtherNames = ColonyAtlas(Library(("Colony/no_such_sprite", Solid(4, 4, Magenta))), out _, out _, out _);
            CollectionAssert.AreEqual(plain, withEmptyLibrary);
            CollectionAssert.AreEqual(plain, withOtherNames);
        }

        [Test]
        public void WrongSize_Warns_AndKeepsTheGeneratedSprite()
        {
            var plain = ColonyAtlas(null, out _, out _, out _);
            // One frame instead of a strip of four, and a wrong height.
            var art = Library(
                ("Colony/ant_worker", Solid(ColonySprites.WorkerWidth, ColonySprites.WorkerHeight, Magenta)),
                ("Colony/egg", Solid(ColonySprites.EggWidth, ColonySprites.EggHeight + 1, Magenta)));
            LogAssert.Expect(LogType.Warning, new Regex("Art 'Colony/ant_worker'.*expected 4 frame\\(s\\) of 12x7 \\(48x7\\)"));
            LogAssert.Expect(LogType.Warning, new Regex("Art 'Colony/egg'.*is 4x6"));
            var atlas = ColonyAtlas(art, out _, out _, out _);
            CollectionAssert.AreEqual(plain, atlas);
        }

        [Test]
        public void ClearPixels_StayClear()
        {
            ColonyAtlas(null, out int width, out _, out var rects);
            var egg = rects[ColonySprites.EggRect];
            var texture = Solid(egg.width, egg.height, Magenta);
            texture.SetPixel(0, 0, new Color32(200, 100, 50, 0));
            texture.Apply(false, false);
            var atlas = ColonyAtlas(Library(("Colony/egg", texture)), out _, out _, out _);
            Assert.AreEqual(0, atlas[egg.y * width + egg.x].a, "a clear texel is cleared, not copied with its hidden colour");
            Assert.AreEqual(0, atlas[egg.y * width + egg.x].r);
            Assert.AreEqual(255, atlas[egg.y * width + egg.x + 1].a);
        }

        [Test]
        public void TileColors_AreTheAverage_TheDarkFifth_AndTheLightFifth()
        {
            // 10 pixels: two dark, six mid, two light.
            var pixels = new Color32[10];
            for (int i = 0; i < 10; i++)
                pixels[i] = i < 2 ? new Color32(20, 10, 0, 255) : i < 8 ? new Color32(100, 80, 60, 255) : new Color32(200, 180, 160, 255);
            ArtOverrides.TileColors(pixels, out var baseColor, out var shadow, out var highlight);
            Assert.AreEqual(new Color32(104, 86, 68, 255), baseColor);
            Assert.AreEqual(new Color32(20, 10, 0, 255), shadow);
            Assert.AreEqual(new Color32(200, 180, 160, 255), highlight);
        }

        [Test]
        public void Library_FindsByName()
        {
            var texture = Solid(2, 2, Magenta);
            var library = Library(("UI/Icons/item_coal", texture), ("", Solid(2, 2, Cyan)), ("Colony/egg", null));
            Assert.IsTrue(library.TryGet("UI/Icons/item_coal", out var found));
            Assert.AreSame(texture, found);
            Assert.IsFalse(library.TryGet("UI/Icons/item_none", out _));
            Assert.IsFalse(library.TryGet("Colony/egg", out _), "an entry without a texture counts as missing");
            Assert.IsNull(library.Get("nothing"));
        }
    }
}
