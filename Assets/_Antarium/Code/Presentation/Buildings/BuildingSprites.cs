using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Antarium.Presentation
{
    /// <summary>
    /// Placeholder pixel-art atlas for buildings, generated in code until real art exists: animated silk
    /// belts and root lifts, splitter knot, filter frame, smelter kiln and fungus garden, plus a small item
    /// blob and dot that are tinted per item through vertex colour.
    /// </summary>
    public sealed class BuildingSprites : System.IDisposable
    {
        public const int Cell = 8;
        public const int AnimFrames = 4;
        public const int ItemSize = 3, DotSize = 4;
        public const int SmelterWidth = 24, SmelterHeight = 24;
        public const int GardenWidth = 32, GardenHeight = 16;

        const int AtlasWidth = 128, AtlasHeight = 64;

        // Rect indices.
        public const int BeltRect = 0;                      // 4 frames
        public const int LiftRect = BeltRect + AnimFrames;  // 4 frames
        public const int SplitterRect = LiftRect + AnimFrames;
        public const int FilterRect = SplitterRect + 1;
        public const int ItemRect = FilterRect + 1;
        public const int DotRect = ItemRect + 1;
        public const int SmelterRect = DotRect + 1;         // 2 frames: idle, working
        public const int GardenRect = SmelterRect + 2;      // 2 frames: idle, working
        public const int GenericRect = GardenRect + 2;      // plain machine block for unknown processors
        const int RectCount = GenericRect + 1;

        public readonly Texture2D Texture;
        /// <summary>UV rects (xMin, yMin, xMax, yMax).</summary>
        public NativeArray<float4> Rects;

        static readonly Color32 SilkHigh = Hex(0xfff4dc), SilkMid = Hex(0xe2d2ae), SilkDark = Hex(0xa8916a), SilkShadow = Hex(0x6f5d44);
        static readonly Color32 Root = Hex(0x6b4a2c), RootHigh = Hex(0x93693f);
        static readonly Color32 Clay = Hex(0xb8562c), ClayDark = Hex(0x7a3418), ClayHigh = Hex(0xe08048), Brick = Hex(0x6c5a4c), BrickDark = Hex(0x4a3c32);
        static readonly Color32 Mouth = Hex(0x2a1610), Fire = Hex(0xffb040), FireHot = Hex(0xfff0a0), Ember = Hex(0xe0602a);
        static readonly Color32 Soil = Hex(0x4a321f), SoilHigh = Hex(0x6b4a2f), Stem = Hex(0xefe2d0), Cap = Hex(0xb98ad0), CapDark = Hex(0x7d5596), CapSpot = Hex(0xf6ecff);
        static readonly Color32 White = new(255, 255, 255, 255), Grey = new(190, 190, 190, 255), DarkGrey = new(120, 120, 120, 255);

        readonly Color32[] _px = new Color32[AtlasWidth * AtlasHeight];

        public BuildingSprites()
        {
            Rects = new NativeArray<float4>(RectCount, Allocator.Persistent);

            for (int f = 0; f < AnimFrames; f++)
            {
                PaintBelt(f * Cell, 0, f);
                Rects[BeltRect + f] = Rect(f * Cell, 0, Cell, Cell);
                PaintLift(32 + f * Cell, 0, f);
                Rects[LiftRect + f] = Rect(32 + f * Cell, 0, Cell, Cell);
            }
            Blit(64, 0, new[] { "...hh...", "..hmmh..", ".hmddms.", "hmdhhdms", "smdhhdms", ".smddms.", "..smms..", "...ss..." });
            Rects[SplitterRect] = Rect(64, 0, Cell, Cell);
            Blit(72, 0, new[] { ".ssssss.", "s......s", "s.....hs", "s.....hh", "s.....hh", "s.....hs", "s......s", ".ssssss." });
            Rects[FilterRect] = Rect(72, 0, Cell, Cell);
            Blit(80, 0, new[] { "wg.", "ggk", ".k." });
            Rects[ItemRect] = Rect(80, 0, ItemSize, ItemSize);
            Blit(88, 0, new[] { ".ww.", "wwww", "wwww", ".ww." });
            Rects[DotRect] = Rect(88, 0, DotSize, DotSize);

            PaintSmelter(0, 8, working: false);
            PaintSmelter(24, 8, working: true);
            Rects[SmelterRect] = Rect(0, 8, SmelterWidth, SmelterHeight);
            Rects[SmelterRect + 1] = Rect(24, 8, SmelterWidth, SmelterHeight);
            PaintGarden(48, 8, working: false);
            PaintGarden(80, 8, working: true);
            Rects[GardenRect] = Rect(48, 8, GardenWidth, GardenHeight);
            Rects[GardenRect + 1] = Rect(80, 8, GardenWidth, GardenHeight);
            PaintGeneric(112, 8);
            Rects[GenericRect] = Rect(112, 8, 16, 16);

            Texture = new Texture2D(AtlasWidth, AtlasHeight, TextureFormat.RGBA32, false, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "BuildingAtlas",
            };
            Texture.SetPixels32(_px);
            Texture.Apply(false, false);
        }

        static float4 Rect(int x, int y, int w, int h) =>
            new float4(x / (float)AtlasWidth, y / (float)AtlasHeight, (x + w) / (float)AtlasWidth, (y + h) / (float)AtlasHeight);

        void Set(int x, int y, Color32 c)
        {
            if ((uint)x < AtlasWidth && (uint)y < AtlasHeight) _px[y * AtlasWidth + x] = c;
        }

        /// <summary>Silk band along the bottom of the cell; dashes travel right one pixel per frame.</summary>
        void PaintBelt(int ox, int oy, int frame)
        {
            for (int x = 0; x < Cell; x++)
            {
                Set(ox + x, oy + 2, SilkHigh);
                Set(ox + x, oy + 1, ((x - frame) & 3) == 0 ? SilkDark : SilkMid);
                Set(ox + x, oy + 0, SilkShadow);
            }
        }

        /// <summary>Two root rails with a silk strand between them; rungs travel up one pixel per frame.</summary>
        void PaintLift(int ox, int oy, int frame)
        {
            for (int y = 0; y < Cell; y++)
            {
                Set(ox + 1, oy + y, Root);
                Set(ox + 6, oy + y, Root);
                Set(ox + 2, oy + y, RootHigh);
                bool rung = ((y - frame) & 3) == 0;
                for (int x = 3; x <= 5; x++) Set(ox + x, oy + y, rung ? SilkDark : SilkMid);
                Set(ox + 4, oy + y, rung ? SilkDark : SilkHigh);
            }
        }

        /// <summary>Clay kiln: brick base, shaded dome, chimney and an arched mouth that glows while working.</summary>
        void PaintSmelter(int ox, int oy, bool working)
        {
            // Dome.
            for (int y = 4; y < 20; y++)
            for (int x = 1; x < 23; x++)
            {
                float2 d = (new float2(x + 0.5f, y + 0.5f) - new float2(12f, 4f)) / new float2(11f, 15.5f);
                float r = math.lengthsq(d);
                if (r > 1f) continue;
                Color32 c = r > 0.8f ? ClayDark : Clay;
                if (r < 0.8f && d.x < -0.15f && d.y > 0.35f && r > 0.3f) c = ClayHigh;
                Set(ox + x, oy + y, c);
            }
            // Chimney.
            for (int y = 16; y < 24; y++)
            for (int x = 15; x < 19; x++)
                Set(ox + x, oy + y, x == 15 || y == 23 ? BrickDark : Brick);
            // Brick base with mortar lines.
            for (int y = 0; y < 5; y++)
            for (int x = 0; x < 24; x++)
            {
                bool mortar = y == 2 || (y < 2 ? (x + 1) % 6 == 0 : (x + 4) % 6 == 0);
                Set(ox + x, oy + y, y == 4 ? Brick : mortar ? BrickDark : Brick);
            }
            // Mouth.
            for (int y = 2; y < 11; y++)
            for (int x = 8; x < 16; x++)
            {
                float2 d = new float2(x + 0.5f - 12f, math.max(0f, y + 0.5f - 7f)) / new float2(4f, 4f);
                if (math.lengthsq(d) > 1f) continue;
                Color32 c = Mouth;
                if (working)
                {
                    c = y < 4 ? FireHot : y < 6 ? Fire : (x + y) % 3 == 0 ? Fire : Ember;
                }
                Set(ox + x, oy + y, c);
            }
        }

        /// <summary>Soil bed with mushrooms; the working frame has extra sprouts and lit spots.</summary>
        void PaintGarden(int ox, int oy, bool working)
        {
            for (int y = 0; y < 5; y++)
            for (int x = 0; x < GardenWidth; x++)
                Set(ox + x, oy + y, y == 4 ? SoilHigh : (x * 7 + y * 3) % 5 == 0 ? SoilHigh : Soil);

            void Mushroom(int cx, int stem, int capR)
            {
                for (int y = 5; y < 5 + stem; y++)
                {
                    Set(ox + cx, oy + y, Stem);
                    Set(ox + cx + 1, oy + y, Stem);
                }
                int top = 5 + stem;
                for (int y = -1; y <= capR; y++)
                for (int x = -capR; x <= capR + 1; x++)
                {
                    float2 d = new float2(x - 0.5f, y) / new float2(capR + 0.5f, capR * 0.75f + 0.5f);
                    if (y < 0 || math.lengthsq(d) > 1f) continue;
                    Color32 c = y == 0 ? CapDark : Cap;
                    if (y > 0 && (x * 3 + y * 5) % 7 == 0) c = CapSpot;
                    Set(ox + cx + x, oy + top + y, c);
                }
            }
            Mushroom(4, 3, 3);
            Mushroom(12, 5, 4);
            Mushroom(21, 4, 3);
            Mushroom(27, 2, 2);
            if (working)
            {
                Mushroom(8, 1, 1);
                Mushroom(17, 1, 1);
                Mushroom(25, 1, 1);
            }
        }

        void PaintGeneric(int ox, int oy)
        {
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                Set(ox + x, oy + y, x == 0 || y == 0 || x == 15 || y == 15 ? DarkGrey : (x + y) % 4 == 0 ? Grey : White);
        }

        /// <summary>Draws ASCII rows (first row = top of the sprite) with the bottom-left at (ox, oy).</summary>
        void Blit(int ox, int oy, string[] rows)
        {
            int h = rows.Length;
            for (int r = 0; r < h; r++)
            {
                string row = rows[r];
                int y = oy + (h - 1 - r);
                for (int x = 0; x < row.Length; x++)
                {
                    Color32 c;
                    switch (row[x])
                    {
                        case 'h': c = SilkHigh; break;
                        case 'm': c = SilkMid; break;
                        case 'd': c = SilkDark; break;
                        case 's': c = SilkShadow; break;
                        case 'w': c = White; break;
                        case 'g': c = Grey; break;
                        case 'k': c = DarkGrey; break;
                        default: continue;
                    }
                    Set(ox + x, y, c);
                }
            }
        }

        static Color32 Hex(int rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        public void Dispose()
        {
            if (Rects.IsCreated) Rects.Dispose();
            if (Texture != null) Object.Destroy(Texture);
        }
    }
}
