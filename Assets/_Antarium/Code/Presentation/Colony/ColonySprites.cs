using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Antarium.Presentation
{
    /// <summary>
    /// Placeholder pixel-art atlas generated in code until real art exists: worker walk cycle, queen, egg
    /// and item blobs. Item blobs are greyscale and tinted per item through vertex colour.
    /// </summary>
    public sealed class ColonySprites : System.IDisposable
    {
        public const int WorkerWidth = 12, WorkerHeight = 7, WorkerFrames = 4;
        public const int QueenWidth = 24, QueenHeight = 12;
        public const int EggWidth = 4, EggHeight = 5;
        public const int PileWidth = 6, PileHeight = 4;
        public const int CarryWidth = 3, CarryHeight = 3;

        const int AtlasWidth = 64, AtlasHeight = 32;

        public readonly Texture2D Texture;
        /// <summary>UV rects (xMin, yMin, xMax, yMax): worker frames 0-3, then egg, pile, carry, queen, marker.</summary>
        public NativeArray<float4> Rects;

        public const int EggRect = WorkerFrames, PileRect = WorkerFrames + 1, CarryRect = WorkerFrames + 2, QueenRect = WorkerFrames + 3;
        /// <summary>Small white diamond (tinted) floating over ants, e.g. idle workers in the overlay.</summary>
        public const int MarkerRect = QueenRect + 1;
        public const int MarkerSize = 3;

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

        static readonly string[] Egg = { ".EE.", "EWEE", "EEEE", "ESSE", ".SS." };
        static readonly string[] Pile = { ".wwg..", "wwggg.", "ggggkk", ".kkkk." };
        static readonly string[] Carry = { "wg.", "ggk", ".k." };
        static readonly string[] Marker = { ".w.", "www", ".w." };

        static readonly Color32 Dark = Hex(0x5a1a0c), Mid = Hex(0xb8401e), High = Hex(0xf08048), Leg = Hex(0x4a160a);
        static readonly Color32 EggColor = Hex(0xefe6cf), EggShade = Hex(0xc9bc9c);

        public ColonySprites()
        {
            var pixels = new Color32[AtlasWidth * AtlasHeight];
            Rects = new NativeArray<float4>(MarkerRect + 1, Allocator.Persistent);

            for (int f = 0; f < WorkerFrames; f++)
            {
                int ox = f * WorkerWidth;
                var rows = new string[WorkerHeight];
                WorkerBody.CopyTo(rows, 0);
                rows[5] = WorkerLegs[f][0];
                rows[6] = WorkerLegs[f][1];
                Blit(pixels, ox, 0, rows);
                Rects[f] = Rect(ox, 0, WorkerWidth, WorkerHeight);
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

            Texture = new Texture2D(AtlasWidth, AtlasHeight, TextureFormat.RGBA32, false, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "ColonyAtlas",
            };
            Texture.SetPixels32(pixels);
            Texture.Apply(false, false); // small; kept readable for debugging
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
            if (Texture != null) Object.Destroy(Texture);
        }
    }
}
