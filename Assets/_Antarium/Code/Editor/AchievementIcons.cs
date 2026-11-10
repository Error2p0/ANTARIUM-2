using System.Collections.Generic;
using Antarium.Content;
using Antarium.Presentation;
using Unity.Mathematics;
using UnityEngine;

namespace Antarium.Editor
{
    /// <summary>
    /// Draws Steam achievement icons (64×64, unlocked + grey locked) from the game's pixel art: a 32×32 art-pixel
    /// canvas (bevelled plate in the achievement's category colour, the subject with a dark outline, the amount for
    /// tiered achievements) scaled up 2×. Subjects are the colony sprites (ants, castes, princesses, enemies, the golden
    /// beetle) or small glyphs drawn here for things without a sprite (depth, digging, items, belts, contracts, ...).
    /// </summary>
    public sealed class AchievementIcons : System.IDisposable
    {
        public const int Canvas = 32, Scale = 2, Size = Canvas * Scale;

        readonly ColonySprites _sprites = new(UnityEditor.AssetDatabase.LoadAssetAtPath<ArtLibrary>(ArtImport.LibraryPath));
        readonly Color32[] _atlas;
        readonly int _atlasWidth;

        public AchievementIcons()
        {
            _atlas = _sprites.Texture.GetPixels32();
            _atlasWidth = _sprites.Texture.width;
        }

        public void Dispose()
        {
            Object.DestroyImmediate(_sprites.Texture);
            _sprites.Dispose();
        }

        /// <summary>A pixel image: (0,0) bottom-left, transparent = alpha 0.</summary>
        sealed class Img
        {
            public readonly int W, H;
            public readonly Color32[] Px;
            public Img(int w, int h) { W = w; H = h; Px = new Color32[w * h]; }
            public Color32 this[int x, int y] { get => Px[y * W + x]; set => Px[y * W + x] = value; }
            public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
        }

        // ---------------------------------------------------------------- public

        /// <summary>The unlocked icon and its grey locked twin, both <see cref="Size"/>² RGBA.</summary>
        public (Texture2D Unlocked, Texture2D Locked) Draw(AchievementDef def)
        {
            var canvas = Compose(def);
            return (ToTexture(canvas, locked: false), ToTexture(canvas, locked: true));
        }

        Img Compose(AchievementDef def)
        {
            var (bg, subject) = Pick(def);
            var c = new Img(Canvas, Canvas);
            Plate(c, bg);
            string amount = AmountText(def);
            bool plate = amount != null;
            // The subject (plus its outline) fills the plate inside its rim, above the amount plate when there is one.
            int areaW = Canvas - 4, areaH = Canvas - (plate ? 12 : 6);
            int scale = math.max(1, math.min(areaW / subject.W, areaH / subject.H));
            var big = Outline(Upscale(subject, scale), Hex(0x120c0a));
            int ox = (Canvas - big.W) / 2;
            int oy = plate ? 8 + (Canvas - 10 - big.H) / 2 : (Canvas - big.H) / 2;
            Paste(c, big, ox, oy);
            if (plate) Number(c, amount);
            return c;
        }

        static Texture2D ToTexture(Img c, bool locked)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                var p = c[x / Scale, y / Scale];
                if (locked)
                {
                    byte g = (byte)((p.r * 30 + p.g * 59 + p.b * 11) / 100 * 55 / 100 + 14);
                    p = new Color32(g, g, g, p.a);
                }
                px[y * Size + x] = p;
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        // ---------------------------------------------------------------- subjects

        static readonly Color32 Earth = Hex(0x5a3a22), Colony = Hex(0x2e5a34), Combat = Hex(0x6a2222), Industry = Hex(0x2e4a66),
            Mind = Hex(0x4e3270), Royal = Hex(0x7a5a14);

        (Color32 Background, Img Subject) Pick(AchievementDef def) => def.kind switch
        {
            AchievementKind.ReachDepth => (Depth(def.amount), Glyph(DownArrow, Pal('W', 0xf0e6d0, 'S', 0xb0a080))),
            AchievementKind.DigCells => (Earth, Glyph(Pickaxe, Pal('S', 0x6a7484, 'H', 0xd0d8e4, 'w', 0xb07a40, 'd', 0x6a4220))),
            AchievementKind.HatchAnts => (Colony, Atlas(ColonySprites.EggRect)),
            AchievementKind.HatchCaste => (def.caste == AntCaste.Soldier ? Combat : Colony, Caste(def.caste)),
            AchievementKind.AntsAlive => (Colony, Crowd()),
            AchievementKind.Build when def.building != null && def.building.kind == BuildingKind.Chest =>
                (Industry, Glyph(Chest, Pal('o', 0x2a1a10, 'W', 0x6b4a2c, 'P', 0x93693f, 'h', 0xe0c070, 'n', 0xd47a3c, 'N', 0xaeb8c6))),
            AchievementKind.Build => (Industry, Glyph(Belt, Pal('o', 0x1c2228, 'H', 0xa8b4c0, 'M', 0x6a7684, 'D', 0x3a4450, 'n', ItemHex(def), 'N', Lighter(ItemHex(def))))),
            AchievementKind.Deliver or AchievementKind.Craft => (def.kind == AchievementKind.Craft ? Industry : Earth, Nugget(def.item)),
            AchievementKind.KillEnemies => (Combat, Atlas(ColonySprites.SoldierRect)),
            AchievementKind.RepelRaids => (Combat, Atlas(ColonySprites.EnemyRect + (int)EnemyArt.Termite * 2)),
            AchievementKind.Contracts => (Earth, Glyph(Scroll, Pal('P', 0xf0e0b0, 'p', 0xc0a070, 'l', 0x8a6a3a, 'r', 0xc03a2a))),
            AchievementKind.Research => (Mind, Glyph(Flask, Pal('G', 0xa06a3a, 'g', 0xd8f0f8, 'L', 0x58e0c0, 'H', 0xe8fff8))),
            AchievementKind.KnowledgeEarned => (Mind, Glyph(Sparkle, Pal('Y', 0xf0c040, 'W', 0xfff8e0))),
            AchievementKind.PrincessesRaised => (Royal, Atlas(ColonySprites.PrincessRect)),
            AchievementKind.Flights => (Royal, Atlas(ColonySprites.PrincessRect + 1)),
            AchievementKind.EventsWon => (Royal, Atlas(ColonySprites.BeetleRect)),
            AchievementKind.TraitLevels => (Mind, Glyph(Dna, Pal('A', 0xf06a8a, 'B', 0x6ac0f0, '-', 0xe8e0f0))),
            AchievementKind.MemoryEarned => (Royal, Glyph(Dna, Pal('A', 0xffe070, 'B', 0xf0a030, '-', 0xfff4d0))),
            _ => (Earth, Atlas(0)),
        };

        /// <summary>Deeper achievements get a darker, colder plate (the biomes go from earth to crystal to magma).</summary>
        static Color32 Depth(long amount) => amount switch
        {
            < 200 => Earth,
            < 500 => Hex(0x4a4a52),
            < 900 => Hex(0x3a3a7a),
            < 1200 => Hex(0x7a2a14),
            _ => Hex(0x3a5a5a),
        };

        static int ItemHex(AchievementDef def)
        {
            var c = def.building != null ? (Color32)def.building.color : (Color32)Color.white;
            return (c.r << 16) | (c.g << 8) | c.b;
        }

        Img Atlas(int rect)
        {
            float4 uv = _sprites.Rects[rect];
            int x0 = (int)math.round(uv.x * _atlasWidth), y0 = (int)math.round(uv.y * _sprites.Texture.height);
            int x1 = (int)math.round(uv.z * _atlasWidth), y1 = (int)math.round(uv.w * _sprites.Texture.height);
            var img = new Img(x1 - x0, y1 - y0);
            for (int y = 0; y < img.H; y++)
            for (int x = 0; x < img.W; x++)
                img[x, y] = _atlas[(y0 + y) * _atlasWidth + x0 + x];
            return Trim(img);
        }

        Img Caste(AntCaste caste) => caste switch
        {
            AntCaste.Worker => Atlas(0),
            AntCaste.Soldier => Atlas(ColonySprites.SoldierRect),
            _ => Atlas(_sprites.CasteRect[(int)caste]),
        };

        /// <summary>Three workers, staggered.</summary>
        Img Crowd()
        {
            var ant = Atlas(0);
            var img = new Img(ant.W + 6, ant.H * 2 + 2);
            Paste(img, ant, 0, ant.H + 2);
            Paste(img, ant, 6, ant.H + 2 - 1);
            Paste(img, ant, 3, 0);
            return img;
        }

        static Img Nugget(ItemDef item)
        {
            var c = item != null ? (Color32)item.color : new Color32(200, 160, 80, 255);
            int hex = (c.r << 16) | (c.g << 8) | c.b;
            return Glyph(NuggetArt, Pal('C', hex, 'H', Lighter(hex), 'd', Darker(hex)));
        }

        // ---------------------------------------------------------------- glyphs (first row = top)

        static readonly string[] DownArrow =
        {
            "...WWW...", "...WSW...", "...WSW...", "...WSW...", "WWWWSWWWW", ".WSSSSSW.", "..WSSSW..", "...WSW...", "....W....",
        };

        static readonly string[] Pickaxe =
        {
            "...SSSSSSS...", ".SSHHHHHHHSS.", "SS....w....SS", "S.....w.....S", "......w......", "......w......",
            "......w......", "......w......", "......w......", "......d......",
        };

        static readonly string[] NuggetArt =
        {
            "...dddd...", ".ddCCCCdd.", ".dCHHCCCd.", "dCHHCCCCCd", "dCCCCCCCCd", "dCCCCCCCdd", ".dCCCCCdd.", "..dddddd..",
        };

        static readonly string[] Belt =
        {
            "...nnN.......", "...nnn.......", "ooooooooooooo", "oHHHHHHHHHHHo", "oMDMMDMMDMMDo", "oDDDDDDDDDDDo", ".ooo.....ooo.",
        };

        /// <summary>A wooden chest with goods heaped on its lid.</summary>
        static readonly string[] Chest =
        {
            "...nnNN.n...", "..nnnNNnnn..", "oooooooooooo", "oPPPPPPPPPPo", "oWWWWWWWWWWo", "oPPPPhhPPPPo", "oPPPPhhPPPPo",
            "oPPPPPPPPPPo", "oooooooooooo",
        };

        static readonly string[] Scroll =
        {
            "pPPPPPPPPPp", "pPPPPPPPPPp", ".PlllllPPP.", ".PPPPPPPPP.", ".PllllllPP.", ".PPPPPPPPP.", ".PlllPPrrP.",
            ".PPPPPPrrP.", "pPPPPPPPPPp", "pPPPPPPPPPp",
        };

        static readonly string[] Flask =
        {
            "...GGGG...", "....gg....", "....gg....", "....gg....", "...g..g...", "..g.LL.g..", ".gLLLLLLg.", "gLLHLLLLLg",
            "gLLLLLLHLg", ".gggggggg.",
        };

        static readonly string[] Sparkle =
        {
            ".....Y.....", ".....Y.....", "....YWY....", "....YWY....", "YYYYWWWYYYY", "....YWY....", "....YWY....",
            ".....Y.....", ".....Y.....",
        };

        static readonly string[] Dna =
        {
            "A......B", ".A....B.", "..A--B..", "...AB...", "...BA...", "..B--A..", ".B....A.", "B......A", "B......A",
        };

        static Dictionary<char, Color32> Pal(params object[] pairs)
        {
            var d = new Dictionary<char, Color32>();
            for (int i = 0; i + 1 < pairs.Length; i += 2) d[(char)pairs[i]] = Hex((int)pairs[i + 1]);
            return d;
        }

        static Img Glyph(string[] rows, Dictionary<char, Color32> palette)
        {
            int w = 0;
            foreach (var r in rows) w = math.max(w, r.Length);
            var img = new Img(w, rows.Length);
            for (int r = 0; r < rows.Length; r++)
            for (int x = 0; x < rows[r].Length; x++)
                if (palette.TryGetValue(rows[r][x], out var c))
                    img[x, rows.Length - 1 - r] = c;
            return img;
        }

        // ---------------------------------------------------------------- drawing

        /// <summary>A bevelled plate: dark rim, light top-left edge, shaded bottom-right edge.</summary>
        static void Plate(Img c, Color32 bg)
        {
            var rim = Hex(0x120c0a);
            var light = Mix(bg, new Color32(255, 255, 255, 255), 0.25f);
            var shade = Mix(bg, rim, 0.45f);
            for (int y = 0; y < c.H; y++)
            for (int x = 0; x < c.W; x++)
            {
                bool edge = x == 0 || y == 0 || x == c.W - 1 || y == c.H - 1;
                // Rounded corners: the four corner pixels stay transparent.
                if ((x == 0 || x == c.W - 1) && (y == 0 || y == c.H - 1)) continue;
                Color32 p = edge ? rim
                    : x == 1 || y == c.H - 2 ? light
                    : x == c.W - 2 || y == 1 ? shade
                    // A soft vertical gradient: lighter at the top.
                    : Mix(bg, light, (y - 2) / (float)(c.H - 4) * 0.35f);
                c[x, y] = p;
            }
        }

        static Img Upscale(Img s, int k)
        {
            var o = new Img(s.W * k, s.H * k);
            for (int y = 0; y < o.H; y++)
            for (int x = 0; x < o.W; x++)
                o[x, y] = s[x / k, y / k];
            return o;
        }

        /// <summary>Adds a one-pixel outline around opaque pixels (grows the image by one on each side).</summary>
        static Img Outline(Img s, Color32 color)
        {
            var o = new Img(s.W + 2, s.H + 2);
            for (int y = 0; y < s.H; y++)
            for (int x = 0; x < s.W; x++)
                o[x + 1, y + 1] = s[x, y];
            var copy = (Color32[])o.Px.Clone();
            for (int y = 0; y < o.H; y++)
            for (int x = 0; x < o.W; x++)
            {
                if (copy[y * o.W + x].a != 0) continue;
                bool near = false;
                for (int d = 0; d < 4 && !near; d++)
                {
                    int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    near = o.Inside(nx, ny) && copy[ny * o.W + nx].a != 0;
                }
                if (near) o[x, y] = color;
            }
            return o;
        }

        static Img Trim(Img s)
        {
            int x0 = s.W, y0 = s.H, x1 = -1, y1 = -1;
            for (int y = 0; y < s.H; y++)
            for (int x = 0; x < s.W; x++)
                if (s[x, y].a != 0)
                {
                    x0 = math.min(x0, x); y0 = math.min(y0, y);
                    x1 = math.max(x1, x); y1 = math.max(y1, y);
                }
            if (x1 < 0) return s;
            var o = new Img(x1 - x0 + 1, y1 - y0 + 1);
            for (int y = 0; y < o.H; y++)
            for (int x = 0; x < o.W; x++)
                o[x, y] = s[x0 + x, y0 + y];
            return o;
        }

        static void Paste(Img dst, Img src, int ox, int oy)
        {
            for (int y = 0; y < src.H; y++)
            for (int x = 0; x < src.W; x++)
            {
                var p = src[x, y];
                if (p.a != 0 && dst.Inside(ox + x, oy + y)) dst[ox + x, oy + y] = p;
            }
        }

        // ---------------------------------------------------------------- amount

        /// <summary>Tiered achievements show their amount ("50", "1K"); one-off ones (amount 1) show nothing.</summary>
        static string AmountText(AchievementDef def)
        {
            if (def.amount <= 1) return null;
            if (def.kind == AchievementKind.ReachDepth || def.amount < 1000) return def.amount.ToString();
            return def.amount % 1000 == 0 ? (def.amount / 1000) + "K" : def.amount.ToString();
        }

        static readonly Dictionary<char, string[]> Digits = new()
        {
            ['0'] = new[] { "###", "#.#", "#.#", "#.#", "###" },
            ['1'] = new[] { ".#.", "##.", ".#.", ".#.", "###" },
            ['2'] = new[] { "###", "..#", "###", "#..", "###" },
            ['3'] = new[] { "###", "..#", ".##", "..#", "###" },
            ['4'] = new[] { "#.#", "#.#", "###", "..#", "..#" },
            ['5'] = new[] { "###", "#..", "###", "..#", "###" },
            ['6'] = new[] { "###", "#..", "###", "#.#", "###" },
            ['7'] = new[] { "###", "..#", "..#", ".#.", ".#." },
            ['8'] = new[] { "###", "#.#", "###", "#.#", "###" },
            ['9'] = new[] { "###", "#.#", "###", "..#", "###" },
            ['K'] = new[] { "#.#", "#.#", "##.", "#.#", "#.#" },
        };

        /// <summary>White 3×5 digits on a dark plate in the bottom-right corner.</summary>
        static void Number(Img c, string text)
        {
            int w = text.Length * 4 - 1;
            int x0 = c.W - 3 - w, y0 = 3;
            var plate = Hex(0x120c0a);
            for (int y = y0 - 1; y <= y0 + 5; y++)
            for (int x = x0 - 1; x <= x0 + w; x++)
                c[x, y] = plate;
            var ink = Hex(0xfff4d8);
            for (int i = 0; i < text.Length; i++)
            {
                var rows = Digits[text[i]];
                for (int r = 0; r < 5; r++)
                for (int x = 0; x < 3; x++)
                    if (rows[r][x] == '#') c[x0 + i * 4 + x, y0 + 4 - r] = ink;
            }
        }

        // ---------------------------------------------------------------- colour

        static Color32 Hex(int rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
        static int Lighter(int rgb) => Scale3(rgb, 1.35f, 40);
        static int Darker(int rgb) => Scale3(rgb, 0.55f, 0);

        static int Scale3(int rgb, float k, int add)
        {
            int r = math.min(255, (int)(((rgb >> 16) & 255) * k) + add);
            int g = math.min(255, (int)(((rgb >> 8) & 255) * k) + add);
            int b = math.min(255, (int)((rgb & 255) * k) + add);
            return (r << 16) | (g << 8) | b;
        }

        static Color32 Mix(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, t);
    }
}
