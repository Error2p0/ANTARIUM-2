using System.Collections.Generic;
using Antarium.Content;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Antarium.Presentation
{
    /// <summary>
    /// The colony atlas: worker and soldier walk cycles, queen, egg, item blobs, the golden beetle and the enemies.
    /// Every sprite is first generated in code (the fallback) and then replaced by its PNG from the
    /// <see cref="ArtLibrary"/> when there is one (<c>Art/Colony/...</c>, see <c>docs/ART_STYLE.md</c>). Item blobs are
    /// greyscale and tinted per item through vertex colour.
    /// </summary>
    public sealed class ColonySprites : System.IDisposable
    {
        public const int WorkerWidth = 12, WorkerHeight = 7, WorkerFrames = 4;
        public const int SoldierWidth = 14, SoldierHeight = 8;
        public const int QueenWidth = 24, QueenHeight = 12;
        public const int EggWidth = 4, EggHeight = 5;
        public const int PileWidth = 6, PileHeight = 4;
        public const int CarryWidth = 3, CarryHeight = 3;

        const int AtlasWidth = 128, AtlasHeight = 128;

        public readonly Texture2D Texture;
        /// <summary>UV rects (xMin, yMin, xMax, yMax); see the *Rect constants.</summary>
        public NativeArray<float4> Rects;
        /// <summary>Per <see cref="EnemyArt"/>: sprite size in pixels (both frames share it).</summary>
        public readonly int2[] EnemySize;

        public const int EggRect = WorkerFrames, PileRect = WorkerFrames + 1, CarryRect = WorkerFrames + 2, QueenRect = WorkerFrames + 3;
        /// <summary>Small white diamond (tinted) floating over ants, e.g. idle workers in the overlay.</summary>
        public const int MarkerRect = QueenRect + 1;
        public const int MarkerSize = 3;
        /// <summary>Golden beetle, 2 walk frames.</summary>
        public const int BeetleRect = MarkerRect + 1;
        public const int BeetleWidth = 10, BeetleHeight = 6;
        /// <summary>Soldier walk cycle, 4 frames.</summary>
        public const int SoldierRect = BeetleRect + 2;
        /// <summary>One white pixel (stretched and tinted for health bars).</summary>
        public const int PixelRect = SoldierRect + WorkerFrames;
        /// <summary>Enemies: 2 walk frames per <see cref="EnemyArt"/>, in enum order.</summary>
        public const int EnemyRect = PixelRect + 1;
        public const int EnemyArtCount = 9;
        /// <summary>The specialist castes (miner, hauler, nurse, builder, scout, engineer, glow, fire): 4 walk frames each, in caste order.</summary>
        public const int SpecialistRect = EnemyRect + EnemyArtCount * 2;
        const int SpecialistCount = 8;
        /// <summary>Winged princess: resting (wings folded), then two flight frames (wings up, wings back).</summary>
        public const int PrincessRect = SpecialistRect + SpecialistCount * WorkerFrames;
        public const int PrincessWidth = 14, PrincessHeight = 10, PrincessFrames = 3;
        /// <summary>Golden aphid (sits in an Aphid Ranch), 2 frames.</summary>
        public const int AphidRect = PrincessRect + PrincessFrames;
        public const int AphidWidth = 6, AphidHeight = 5;
        /// <summary>The queen's second frame (her gaster swells as she breathes); the first is <see cref="QueenRect"/>.</summary>
        public const int QueenBreathRect = AphidRect + 2;
        /// <summary>The light a glow ant carries: a dithered disc drawn unlit around its gaster (10.4).</summary>
        public const int HaloRect = QueenBreathRect + 1;
        public const int HaloSize = 13;
        /// <summary>Pixels from the sprite's left edge to the middle of the glow ant's gaster (the halo's centre).</summary>
        public const float GasterX = 2f;
        /// <summary>The mole cricket digging: 2 frames, same size as its walk frames (10.5).</summary>
        public const int MoleDigRect = HaloRect + 1;
        const int RectCount = MoleDigRect + 2;

        /// <summary>Per <see cref="AntCaste"/>: first walk frame and sprite size.</summary>
        public NativeArray<int> CasteRect;
        public NativeArray<int2> CasteSize;

        // Side view facing right: gaster (x0-3), waist (x4), thorax (x5-6), neck (x7), head (x8-10), mandible (x11).
        // The body colour carries the silhouette (a dark outline would vanish against dark tunnels):
        // highlight on top, mid body, shadow row underneath, thin waist and neck in leg colour.
        static readonly string[] WorkerBody =
        {
            "..........L.",
            ".HH.....HL..",
            "HMMM.HH.HMM.",
            "MMMMLMMLMMML",
            ".DMD.DD.DD..",
        };

        // Three legs from the thorax, alternating tripod gait.
        static readonly string[][] WorkerLegs =
        {
            new[] { "....L.L.L...", "...L..L..L.." },
            new[] { ".....LL.L...", "....L.L...L." },
            new[] { "....L.L.L...", "....L.L..L.." },
            new[] { "....LL..L...", "...L..L...L." },
        };

        // Soldier: darker, with a big square head and heavy mandibles.
        static readonly string[] SoldierBody =
        {
            "..........L...",
            ".hh......hhL..",
            "hmmm.hh.hmmmm.",
            "mmmmLmmLmmmmmK",
            ".dmd.dd.dmmmK.",
            ".........dd...",
        };

        static readonly string[][] SoldierLegs =
        {
            new[] { "....L.L.L.....", "...L..L..L...." },
            new[] { ".....LL.L.....", "....L.L...L..." },
            new[] { "....L.L.L.....", "....L.L..L...." },
            new[] { "....LL..L.....", "...L..L...L..." },
        };

        // Princess: a slim golden young queen (long gaster) with two pairs of wings on the thorax.
        static readonly string[][] Princess =
        {
            new[] // resting: wings folded flat along the back
            {
                "..............", "..............", "..............", "vwwwwwv....L..", ".HHH..HH..HL..", "HMMMM.MM.HMM..",
                "MMMMMLMMLMMMK.", ".DMMD.DD.DD...", ".....L.L.L....", "....L..L..L...",
            },
            new[] // flying, wings up
            {
                "..vww..vw.....", "...vwww.ww....", "....vwwwww....", "......ww...L..", ".HHH..HH..HL..", "HMMMM.MM.HMM..",
                "MMMMMLMMLMMMK.", ".DMMD.DD.DD...", ".....L.L.L....", ".....L.L.L....",
            },
            new[] // flying, wings swept back
            {
                "..............", "..............", "vwwwwwv.......", ".vvwwwwww..L..", ".HHH..HH..HL..", "HMMMM.MM.HMM..",
                "MMMMMLMMLMMMK.", ".DMMD.DD.DD...", ".....L.L.L....", ".....L.L.L....",
            },
        };

        static readonly string[] Egg = { ".EE.", "EWEE", "EEEE", "ESSE", ".SS." };
        static readonly string[] Pile = { ".wwg..", "wwggg.", "ggggkk", ".kkkk." };
        static readonly string[] Carry = { "wg.", "ggk", ".k." };
        static readonly string[] Marker = { ".w.", "www", ".w." };
        static readonly string[] BeetleBody = { "...YYYY...", "..YOOOOOB.", ".OOOOOOOBB", ".oOOOOOOBB", "..oooooo.." };
        static readonly string[] BeetleLegs = { ".l.l..l.l.", "..l.ll.l.." };

        // Enemies, two frames each (only the leg rows differ). Facing right.
        static readonly string[][] Termite =
        {
            new[] { "........c..", ".tttttttHH.", "tTTTTTTTHHc", ".ttttttHHH.", ".r.r.r.r...", "..r.r.r.r.." },
            new[] { "........c..", ".tttttttHH.", "tTTTTTTTHHc", ".ttttttHHH.", "..r.r.r.r..", ".r.r.r.r..." },
        };
        static readonly string[][] Centipede =
        {
            new[] { "..............aa", ".ssssssssssssHH.", "sSsSsSsSsSsSsHHH", ".ssssssssssssHH.", "r.r.r.r.r.r.r.r.", "................" },
            new[] { "..............aa", ".ssssssssssssHH.", "sSsSsSsSsSsSsHHH", ".ssssssssssssHH.", ".r.r.r.r.r.r.r.r", "................" },
        };
        static readonly string[][] Spider =
        {
            new[] { "...pppp....", "..pPPPPp...", ".pPPPPPPpE.", "vpPPPPPPpEv", "v.vpppppv.v", "v.v.v.v.v.v", "..........." },
            new[] { "...pppp....", "..pPPPPp...", ".pPPPPPPpE.", ".pPPPPPPpE.", "vvvpppppvvv", ".v.v...v.v.", "v.......v.." },
        };
        static readonly string[][] Mite =
        {
            new[] { ".mmmm.", "mMMMMm", "mMMMMe", ".mmmm.", "i.ii.i" },
            new[] { ".mmmm.", "mMMMMm", "mMMMMe", ".mmmm.", ".i..i." },
        };
        // Rival ant (10.5): a worker-sized ant in cold steel blue with pale mandibles.
        static readonly string[][] RivalAnt =
        {
            new[] { "..........L.", ".HH.....HL..", "HMMM.HH.HMM.", "MMMMLMMLMMMK", ".DMD.DD.DD..", "....L.L.L...", "...L..L..L.." },
            new[] { "..........L.", ".HH.....HL..", "HMMM.HH.HMM.", "MMMMLMMLMMMK", ".DMD.DD.DD..", ".....LL.L...", "....L.L...L." },
        };
        // Mole cricket (10.5): a fat brown body, a shield on its back and big digging claws in front.
        static readonly string[][] MoleCricket =
        {
            new[] { "......SSSS......", "..bbbSssssSb....", ".bBBBBBBBBBBbh..", "bBBBBBBBBBBBbhhC", "bbbbbbbbbbbbbhCC", ".dddddddddddd.C.", "..l..l..l.......", ".l..l..l........" },
            new[] { "......SSSS......", "..bbbSssssSb....", ".bBBBBBBBBBBbh..", "bBBBBBBBBBBBbhhC", "bbbbbbbbbbbbbhCC", ".dddddddddddd.C.", ".l..l..l........", "..l..l..l......." },
        };
        static readonly string[][] MoleCricketDig =
        {
            new[] { "......SSSS......", "..bbbSssssSb....", ".bBBBBBBBBBBbhCC", "bBBBBBBBBBBBbhC.", "bbbbbbbbbbbbbh..", ".dddddddddddd...", "..l..l..l.......", ".l..l..l........" },
            new[] { "......SSSS......", "..bbbSssssSb....", ".bBBBBBBBBBBbh..", "bBBBBBBBBBBBbh..", "bbbbbbbbbbbbbhC.", ".dddddddddddd.CC", "..l..l..l.....C.", ".l..l..l........" },
        };
        static readonly string[][] FireBeetle =
        {
            new[] { "...KKKKK....", "..KOKOKOKK..", ".KKKKKKKKKHH", ".kkkkkkkkkHH", "..kkkkkkkk..", ".j.j..j.j...", "..j.jj.j...." },
            new[] { "...KKKKK....", "..KOKOKOKK..", ".KKKKKKKKKHH", ".kkkkkkkkkHH", "..kkkkkkkk..", "..j.jj.j....", ".j.j..j.j..." },
        };
        static readonly string[][] Guardian =
        {
            new[]
            {
                "....GGGGGG....", "...GggggggGG..", "...GgCCgggggG.", "...GgCCggggG..", "..GGGGGGGGGG..", ".GgggggggggggG",
                "Gg.ggggggggg.G", "Gg.gggggggggGg", "...gggggggg...", "...gg....gg...", "...gg....gg...", "..ggg....ggg..",
            },
            new[]
            {
                "....GGGGGG....", "...GggggggGG..", "...GgCCgggggG.", "...GgCCggggG..", "..GGGGGGGGGG..", ".GgggggggggggG",
                "Gg.ggggggggg.G", "Gg.gggggggggGg", "...gggggggg...", "....gg..gg....", "...ggg..ggg...", "..............",
            },
        };

        // Wolf spider: a hairy brown-grey hunter with pale stripes and many eyes.
        static readonly string[][] WolfSpider =
        {
            new[] { "....bbbb....", "..bBsBBsBb..", ".bBBBBBBBbeE", "vbBBsBBBBbEe", "v.vbbbbbv.v.", "v.v.v.v.v.v.", "............" },
            new[] { "....bbbb....", "..bBsBBsBb..", ".bBBBBBBBbeE", ".bBBsBBBBbEe", "vvvbbbbbvvv.", ".v.v...v.v..", "v.......v..." },
        };
        static readonly string[][] GoldenAphid =
        {
            new[] { "..a...", ".YYYa.", "YOOOOa", "OOOOOo", ".l.l.." },
            new[] { "...a..", ".YYYa.", "YOOOOa", "OOOOOo", "..l.l." },
        };

        static readonly Color32 Dark = Hex(0x5a1a0c), Mid = Hex(0xb8401e), High = Hex(0xf08048), Leg = Hex(0x4a160a);
        static readonly Color32 SoldierDark = Hex(0x3a0c06), SoldierMid = Hex(0x862612), SoldierHigh = Hex(0xc05a2a), Mandible = Hex(0xe0b050);
        static readonly Color32 EggColor = Hex(0xefe6cf), EggShade = Hex(0xc9bc9c);

        /// <param name="art">PNG overrides; null = generated sprites only.</param>
        public ColonySprites(ArtLibrary art = null)
        {
            var pixels = new Color32[AtlasWidth * AtlasHeight];
            Rects = new NativeArray<float4>(RectCount, Allocator.Persistent);
            EnemySize = new int2[EnemyArtCount];

            for (int f = 0; f < WorkerFrames; f++)
            {
                int ox = f * WorkerWidth;
                var rows = new string[WorkerHeight];
                WorkerBody.CopyTo(rows, 0);
                rows[5] = WorkerLegs[f][0];
                rows[6] = WorkerLegs[f][1];
                Blit(pixels, ox, 0, rows);
                Rects[f] = Rect(ox, 0, WorkerWidth, WorkerHeight);

                int sx = f * SoldierWidth;
                var srows = new string[SoldierHeight];
                SoldierBody.CopyTo(srows, 0);
                srows[6] = SoldierLegs[f][0];
                srows[7] = SoldierLegs[f][1];
                Blit(pixels, sx, 20, srows);
                Rects[SoldierRect + f] = Rect(sx, 20, SoldierWidth, SoldierHeight);
            }
            Blit(pixels, 48, 0, Egg);
            Rects[EggRect] = Rect(48, 0, EggWidth, EggHeight);
            Blit(pixels, 52, 0, Pile);
            Rects[PileRect] = Rect(52, 0, PileWidth, PileHeight);
            Blit(pixels, 58, 0, Carry);
            Rects[CarryRect] = Rect(58, 0, CarryWidth, CarryHeight);
            Blit(pixels, 61, 0, Marker);
            Rects[MarkerRect] = Rect(61, 0, MarkerSize, MarkerSize);
            PaintQueen(pixels, 0, 8);
            Rects[QueenRect] = Rect(0, 8, QueenWidth, QueenHeight);
            PaintQueen(pixels, 30, 30);
            Rects[QueenBreathRect] = Rect(30, 30, QueenWidth, QueenHeight);
            for (int f = 0; f < 2; f++)
            {
                var rows = new string[BeetleHeight];
                BeetleBody.CopyTo(rows, 0);
                rows[5] = BeetleLegs[f];
                int ox = 24 + f * BeetleWidth;
                Blit(pixels, ox, 8, rows);
                Rects[BeetleRect + f] = Rect(ox, 8, BeetleWidth, BeetleHeight);
            }
            pixels[(AtlasHeight - 1) * AtlasWidth + AtlasWidth - 1] = new Color32(255, 255, 255, 255);
            Rects[PixelRect] = Rect(AtlasWidth - 1, AtlasHeight - 1, 1, 1);

            AddEnemy(pixels, EnemyArt.Termite, Termite, 64, 0, new Dictionary<char, Color32>
            {
                ['t'] = Hex(0xd8ccb0), ['T'] = Hex(0xf0e6cc), ['H'] = Hex(0xb0602a), ['c'] = Hex(0x5a2a10), ['r'] = Hex(0x8a6a4a),
            });
            AddEnemy(pixels, EnemyArt.Mite, Mite, 88, 0, new Dictionary<char, Color32>
            {
                ['m'] = Hex(0xb06a8a), ['M'] = Hex(0xe8a8c8), ['e'] = Hex(0x2a0a1a), ['i'] = Hex(0x6a3a5a),
            });
            AddEnemy(pixels, EnemyArt.Centipede, Centipede, 64, 8, new Dictionary<char, Color32>
            {
                ['s'] = Hex(0x8a3a1a), ['S'] = Hex(0xd0782e), ['H'] = Hex(0x5a2010), ['a'] = Hex(0x3a1a0a), ['r'] = Hex(0x6a2a10),
            });
            AddEnemy(pixels, EnemyArt.Spider, Spider, 96, 8, new Dictionary<char, Color32>
            {
                ['p'] = Hex(0x3e2c52), ['P'] = Hex(0x74529a), ['E'] = Hex(0xff4848), ['v'] = Hex(0x2a1a3a),
            });
            AddEnemy(pixels, EnemyArt.FireBeetle, FireBeetle, 64, 16, new Dictionary<char, Color32>
            {
                ['K'] = Hex(0x2e2824), ['O'] = Hex(0xff8a24), ['k'] = Hex(0x16120f), ['H'] = Hex(0x3e342c), ['j'] = Hex(0x1c1612),
            });
            AddEnemy(pixels, EnemyArt.Guardian, Guardian, 0, 30, new Dictionary<char, Color32>
            {
                ['G'] = Hex(0x6a6878), ['g'] = Hex(0x8e8ca0), ['C'] = Hex(0x70f0ff),
            });

            AddEnemy(pixels, EnemyArt.WolfSpider, WolfSpider, 64, 74, new Dictionary<char, Color32>
            {
                ['b'] = Hex(0x4a3c2e), ['B'] = Hex(0x7a6650), ['s'] = Hex(0xc8b89a), ['e'] = Hex(0x101010), ['E'] = Hex(0xffd060), ['v'] = Hex(0x2e241a),
            });
            var rival = new Dictionary<char, Color32>
            {
                ['D'] = Hex(0x2e3a5a), ['M'] = Hex(0x5a6e9a), ['H'] = Hex(0x9ab0d8), ['L'] = Hex(0x1e2638), ['K'] = Hex(0xe8e0c8),
            };
            AddEnemy(pixels, EnemyArt.RivalAnt, RivalAnt, 70, 112, rival);
            var mole = new Dictionary<char, Color32>
            {
                ['b'] = Hex(0x6a4a2a), ['B'] = Hex(0x8a6438), ['d'] = Hex(0x4a3018), ['S'] = Hex(0xb08a58), ['s'] = Hex(0x8a6a40),
                ['h'] = Hex(0x5a3a20), ['C'] = Hex(0xd8c090), ['l'] = Hex(0x3a2614),
            };
            AddEnemy(pixels, EnemyArt.MoleCricket, MoleCricket, 0, 112, mole);
            AddFrames(pixels, MoleCricketDig, 32, 112, MoleDigRect, mole);
            AddFrames(pixels, GoldenAphid, 90, 82, AphidRect, new Dictionary<char, Color32>
            {
                ['Y'] = Hex(0xfff6b0), ['O'] = Hex(0xf0c040), ['o'] = Hex(0xb08020), ['a'] = Hex(0x8a6a20), ['l'] = Hex(0x5a3a10),
            });
            PaintCastes(pixels);
            PaintHalo(pixels, 112, 98);
            Rects[HaloRect] = Rect(112, 98, HaloSize, HaloSize);
            AddFrames(pixels, Princess, 0, 74, PrincessRect, new Dictionary<char, Color32>
            {
                ['D'] = Hex(0x7a4a10), ['M'] = Hex(0xe0a032), ['H'] = Hex(0xffe48a), ['L'] = Hex(0x5a3008), ['K'] = Hex(0xfff4c8),
                ['w'] = Hex(0xf0f8ff), ['v'] = Hex(0x7896c8),
            });

            ArtOverrides.Apply(art, Slots, pixels, AtlasWidth, AtlasHeight, Rects);

            Texture = new Texture2D(AtlasWidth, AtlasHeight, TextureFormat.RGBA32, false, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "ColonyAtlas",
            };
            Texture.SetPixels32(pixels);
            Texture.Apply(false, false); // small; kept readable for debugging
        }

        static readonly string[] EnemyNames = { "termite", "centipede", "spider", "mite", "fire_beetle", "guardian", "wolf_spider", "rival_ant", "mole_cricket" };

        /// <summary>
        /// Every sprite a PNG can replace (<c>Art/Colony/</c>, <c>Art/Enemies/</c>): ants are <c>Colony/ant_&lt;caste&gt;</c>,
        /// enemies <c>Enemies/&lt;name&gt;</c> in <see cref="EnemyArt"/> order. Sizes: see <c>docs/ART_STYLE.md</c>.
        /// </summary>
        public static ArtSlot[] Slots => _slots ??= BuildSlots();
        static ArtSlot[] _slots;

        static ArtSlot[] BuildSlots()
        {
            var slots = new List<ArtSlot>
            {
                ArtSlot.Strip("Colony/ant_worker", 0, WorkerFrames),
                ArtSlot.Strip("Colony/ant_soldier", SoldierRect, WorkerFrames),
                new("Colony/queen", QueenRect, QueenBreathRect),
                ArtSlot.Strip("Colony/egg", EggRect),
                ArtSlot.Strip("Colony/pile", PileRect),
                ArtSlot.Strip("Colony/carry", CarryRect),
                ArtSlot.Strip("Colony/marker", MarkerRect),
                ArtSlot.Strip("Colony/princess", PrincessRect, PrincessFrames),
                ArtSlot.Strip("Colony/golden_beetle", BeetleRect, 2),
                ArtSlot.Strip("Colony/golden_aphid", AphidRect, 2),
                ArtSlot.Strip("Colony/ant_glow_halo", HaloRect),
            };
            for (int s = 0; s < Specialists.Length; s++)
                slots.Add(ArtSlot.Strip("Colony/ant_" + Specialists[s].Caste.ToString().ToLowerInvariant(), SpecialistRect + s * WorkerFrames, WorkerFrames));
            for (int e = 0; e < EnemyArtCount; e++)
                slots.Add(ArtSlot.Strip("Enemies/" + EnemyNames[e], EnemyRect + e * 2, 2));
            slots.Add(ArtSlot.Strip("Enemies/mole_cricket_dig", MoleDigRect, 2));
            return slots.ToArray();
        }

        /// <summary>One specialist caste: body rows (first = top), legs and palette (D dark, M mid, H high, L leg, K accent).</summary>
        readonly struct CasteArt
        {
            public readonly AntCaste Caste;
            public readonly string[] Body;
            public readonly int LegShift;
            public readonly Color32 Dark, Mid, High, Leg, Accent;

            public CasteArt(AntCaste caste, string[] body, int legShift, int dark, int mid, int high, int leg, int accent)
            {
                Caste = caste;
                Body = body;
                LegShift = legShift;
                Dark = Hex(dark);
                Mid = Hex(mid);
                High = Hex(high);
                Leg = Hex(leg);
                Accent = Hex(accent);
            }
        }

        // Miners: a heavy head with pale digging mandibles. Haulers: a big gaster. Scouts: slim with long antennae.
        static readonly CasteArt[] Specialists =
        {
            new(AntCaste.Miner, new[] { "...........L.", ".HH......HHL.", "HMMM.HH.HMMM.", "MMMMLMMLMMMMK", ".DMD.DD.DDDK." }, 0,
                0x2e2a2a, 0x5e5350, 0x8e8078, 0x221c1a, 0xd8c8a0),
            new(AntCaste.Hauler, new[] { "...........L.", ".HHH.....HL..", "HMMMM.HH.HMM.", "MMMMMLMMLMMML", "DDMMD.DD.DD.." }, 1,
                0x6a4a1c, 0xb88a3a, 0xe8c070, 0x4a3210, 0xe8c070),
            new(AntCaste.Nurse, new[] { "..........L.", ".HH.....HL..", "HMMM.HH.HMM.", "MMMMLMMLMMML", ".DMD.DD.DD.." }, 0,
                0x8a7a5a, 0xd8c8a0, 0xfff4d8, 0x6a5a40, 0xfff4d8),
            new(AntCaste.Builder, new[] { "..........L.", ".HH.....HL..", "HMKM.HH.HMM.", "MMMMLMMLMMMK", ".DMD.DD.DD.." }, 0,
                0x34401c, 0x6a7a34, 0x9ab050, 0x283010, 0xc8d890),
            new(AntCaste.Scout, new[] { "........LLL.", ".H......HL..", "HMM..HH.HMM.", "MMMLLMMLMMML", ".DD..DD.DD.." }, 0,
                0x8a4a10, 0xe08a28, 0xffc060, 0x5a3008, 0xffe0a0),
            new(AntCaste.Engineer, new[] { "..........L.", ".HH.....HL..", "HMMM.HH.HMK.", "MMMMLMMLMMML", ".DMD.DD.DD.." }, 0,
                0x1e3448, 0x3e6a8e, 0x7aaad0, 0x162432, 0xa0f0ff),
            // 10.4: the glow ant's gaster is a pale lantern; the fire ant is ember red with a bright tip.
            new(AntCaste.Glow, new[] { "..........L.", ".KK.....HL..", "KKKK.HH.HMM.", "KKKKLMMLMMML", ".DMD.DD.DD.." }, 0,
                0x0e3238, 0x24706e, 0x6cc8b0, 0x081c20, 0xd4fff0),
            new(AntCaste.Fire, new[] { "K.........L.", ".HH.....HL..", "HMMM.HH.HMM.", "MMMMLMMLMMML", ".DMD.DD.DD.." }, 0,
                0x5a180c, 0xd4501c, 0xffc860, 0x2a0c08, 0xffe6a0),
        };

        /// <summary>Walk cycles of the specialist castes (palette swaps and small silhouette changes of the worker).</summary>
        void PaintCastes(Color32[] pixels)
        {
            CasteRect = new NativeArray<int>(Castes.Count, Allocator.Persistent);
            CasteSize = new NativeArray<int2>(Castes.Count, Allocator.Persistent);
            CasteRect[(int)AntCaste.Worker] = 0;
            CasteSize[(int)AntCaste.Worker] = new int2(WorkerWidth, WorkerHeight);
            CasteRect[(int)AntCaste.Soldier] = SoldierRect;
            CasteSize[(int)AntCaste.Soldier] = new int2(SoldierWidth, SoldierHeight);
            for (int s = 0; s < Specialists.Length; s++)
            {
                var art = Specialists[s];
                int w = art.Body[0].Length, h = art.Body.Length + 2;
                // The first six fill rows 44-70; the 10.4 castes sit in the rows added below the old atlas.
                int ox = (s % 2) * 56, oy = s < 6 ? 44 + (s / 2) * 9 : 98 + ((s - 6) / 2) * 9;
                for (int f = 0; f < WorkerFrames; f++)
                {
                    var rows = new string[h];
                    art.Body.CopyTo(rows, 0);
                    rows[h - 2] = (new string('.', art.LegShift) + WorkerLegs[f][0]).PadRight(w, '.').Substring(0, w);
                    rows[h - 1] = (new string('.', art.LegShift) + WorkerLegs[f][1]).PadRight(w, '.').Substring(0, w);
                    for (int r = 0; r < h; r++)
                    {
                        int y = oy + (h - 1 - r);
                        for (int x = 0; x < w; x++)
                        {
                            Color32 c;
                            switch (rows[r][x])
                            {
                                case 'D': c = art.Dark; break;
                                case 'M': c = art.Mid; break;
                                case 'H': c = art.High; break;
                                case 'L': c = art.Leg; break;
                                case 'K': c = art.Accent; break;
                                default: continue;
                            }
                            pixels[y * AtlasWidth + ox + f * (w + 1) + x] = c;
                        }
                    }
                    Rects[SpecialistRect + s * WorkerFrames + f] = Rect(ox + f * (w + 1), oy, w, h);
                }
                CasteRect[(int)art.Caste] = SpecialistRect + s * WorkerFrames;
                CasteSize[(int)art.Caste] = new int2(w, h);
            }
        }

        /// <summary>The glow ant's light: a bright core, then two dithered rings (green-white, fading out).</summary>
        static void PaintHalo(Color32[] px, int ox, int oy)
        {
            Color32 core = Hex(0xd8ffe8), ring = Hex(0x8ee8b0), rim = Hex(0x4aa878);
            for (int y = 0; y < HaloSize; y++)
            for (int x = 0; x < HaloSize; x++)
            {
                float r = math.length(new float2(x - 6, y - 6));
                Color32 c;
                if (r < 2.5f) c = core;
                else if (r < 4.2f && (x + y) % 2 == 0) c = ring;
                else if (r < 6.3f && x % 2 == 0 && y % 2 == 0) c = rim;
                else continue;
                px[(oy + y) * AtlasWidth + ox + x] = c;
            }
        }

        /// <summary>Frames side by side (one pixel apart) from (ox, oy) with their own palette, into consecutive rects.</summary>
        void AddFrames(Color32[] pixels, string[][] frames, int ox, int oy, int firstRect, Dictionary<char, Color32> palette)
        {
            int w = frames[0][0].Length, h = frames[0].Length;
            for (int f = 0; f < frames.Length; f++)
            {
                int fx = ox + f * (w + 1);
                for (int r = 0; r < h; r++)
                {
                    int y = oy + (h - 1 - r);
                    for (int x = 0; x < frames[f][r].Length; x++)
                        if (palette.TryGetValue(frames[f][r][x], out var c)) pixels[y * AtlasWidth + fx + x] = c;
                }
                Rects[firstRect + f] = Rect(fx, oy, w, h);
            }
        }

        /// <summary>Two frames of an enemy side by side at (ox, oy), drawn with the enemy's own palette.</summary>
        void AddEnemy(Color32[] pixels, EnemyArt art, string[][] frames, int ox, int oy, Dictionary<char, Color32> palette)
        {
            int w = frames[0][0].Length, h = frames[0].Length;
            EnemySize[(int)art] = new int2(w, h);
            for (int f = 0; f < 2; f++)
            {
                var rows = frames[f];
                for (int r = 0; r < h; r++)
                {
                    int y = oy + (h - 1 - r);
                    for (int x = 0; x < rows[r].Length; x++)
                        if (palette.TryGetValue(rows[r][x], out var c)) pixels[y * AtlasWidth + ox + f * w + x] = c;
                }
                Rects[EnemyRect + (int)art * 2 + f] = Rect(ox + f * w, oy, w, h);
            }
        }

        static float4 Rect(int x, int y, int w, int h) =>
            new float4(x / (float)AtlasWidth, y / (float)AtlasHeight, (x + w) / (float)AtlasWidth, (y + h) / (float)AtlasHeight);

        /// <summary>Draws ASCII rows (first row = top of the sprite) with the bottom-left at (ox, oy).</summary>
        static void Blit(Color32[] px, int ox, int oy, string[] rows)
        {
            int h = rows.Length;
            for (int r = 0; r < h; r++)
            {
                string row = rows[r];
                int y = oy + (h - 1 - r);
                for (int x = 0; x < row.Length; x++)
                {
                    if (!TryColor(row[x], out var c)) continue;
                    px[y * AtlasWidth + ox + x] = c;
                }
            }
        }

        static bool TryColor(char ch, out Color32 c)
        {
            switch (ch)
            {
                case 'D': c = Dark; return true;
                case 'M': c = Mid; return true;
                case 'H': c = High; return true;
                case 'L': c = Leg; return true;
                case 'E': c = EggColor; return true;
                case 'S': c = EggShade; return true;
                case 'W': c = new Color32(255, 250, 240, 255); return true;
                // Soldier.
                case 'd': c = SoldierDark; return true;
                case 'm': c = SoldierMid; return true;
                case 'h': c = SoldierHigh; return true;
                case 'K': c = Mandible; return true;
                // Golden beetle.
                case 'Y': c = Hex(0xfff0a0); return true;
                case 'O': c = Hex(0xf0c040); return true;
                case 'o': c = Hex(0xb08020); return true;
                case 'B': c = Hex(0x5a3a10); return true;
                case 'l': c = Hex(0x4a2a08); return true;
                // Greyscale for item blobs (tinted by item colour).
                case 'w': c = new Color32(255, 255, 255, 255); return true;
                case 'g': c = new Color32(190, 190, 190, 255); return true;
                case 'k': c = new Color32(120, 120, 120, 255); return true;
                default: c = default; return false;
            }
        }

        /// <summary>Queen: large striped gaster, thorax and head built from shaded ellipses.</summary>
        static void PaintQueen(Color32[] px, int ox, int oy)
        {
            var shape = new byte[QueenWidth * QueenHeight]; // 0 empty, 1 gaster, 2 thorax, 3 head
            void Ellipse(float cx, float cy, float rx, float ry, byte part)
            {
                for (int y = 0; y < QueenHeight; y++)
                for (int x = 0; x < QueenWidth; x++)
                {
                    float2 d = (new float2(x + 0.5f, y + 0.5f) - new float2(cx, cy)) / new float2(rx, ry);
                    if (math.lengthsq(d) <= 1f) shape[y * QueenWidth + x] = part;
                }
            }
            Ellipse(7.5f, 6f, 7.5f, 4.5f, 1);
            Ellipse(15.5f, 6.5f, 3f, 2.6f, 2);
            Ellipse(20.5f, 7f, 3f, 2.4f, 3);

            for (int y = 0; y < QueenHeight; y++)
            for (int x = 0; x < QueenWidth; x++)
            {
                byte part = shape[y * QueenWidth + x];
                if (part == 0) continue;
                bool edge = x == 0 || y == 0 || x == QueenWidth - 1 || y == QueenHeight - 1
                            || shape[y * QueenWidth + x - 1] == 0 || shape[y * QueenWidth + x + 1] == 0
                            || shape[(y - 1) * QueenWidth + x] == 0 || shape[(y + 1) * QueenWidth + x] == 0;
                Color32 c = edge ? Dark : Mid;
                if (!edge && part == 1 && x % 3 == 0) c = Dark;          // gaster bands
                if (!edge && y >= 7 && (x + y) % 5 == 0) c = High;        // sheen
                px[(oy + y) * AtlasWidth + ox + x] = c;
            }
            // Legs and antennae.
            int[] legX = { 12, 14, 16, 18 };
            foreach (int lx in legX)
            {
                px[(oy + 1) * AtlasWidth + ox + lx] = Leg;
                px[(oy + 0) * AtlasWidth + ox + lx - 1] = Leg;
            }
            px[(oy + 10) * AtlasWidth + ox + 22] = Leg;
            px[(oy + 11) * AtlasWidth + ox + 23] = Leg;
            px[(oy + 7) * AtlasWidth + ox + 21] = new Color32(10, 5, 4, 255); // eye
        }

        static Color32 Hex(int rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        public void Dispose()
        {
            if (Rects.IsCreated) Rects.Dispose();
            if (CasteRect.IsCreated) CasteRect.Dispose();
            if (CasteSize.IsCreated) CasteSize.Dispose();
            if (Texture != null) Object.Destroy(Texture);
        }
    }
}
