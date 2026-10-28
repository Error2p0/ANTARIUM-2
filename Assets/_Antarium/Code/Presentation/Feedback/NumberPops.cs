using System;
using System.Collections.Generic;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using Unity.Mathematics;
using UnityEngine;

namespace Antarium.Presentation
{
    /// <summary>
    /// Floating "+3" numbers over deliveries (item colour, food value for food), crafts and knowledge, drawn in the
    /// world with a tiny 3x5 pixel digit font (baked dark outline) in one sprite mesh. Deliveries of the same kind
    /// close together are merged so a busy stockpile shows one growing number instead of a cloud.
    /// </summary>
    public sealed class NumberPops : IDisposable
    {
        const string Glyphs = "0123456789+-.KMBTe";
        const int GlyphW = 5, GlyphH = 7, Advance = 4;
        /// <summary>Light point that tells the sprite shader to draw at full brightness.</summary>
        static readonly float2 Unlit = new(0f, -20000f);
        static readonly Color KnowledgeColor = new(0.8f, 0.74f, 1f);
        static readonly Color FoodColor = new(0.55f, 0.85f, 0.35f);

        // 3x5 glyphs, rows top to bottom.
        static readonly string[] Shapes =
        {
            "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
            "111100111001111", "111100111101111", "111001001010010", "111101111101111", "111101111001111",
            "000010111010000", "000000111000000", "000000000000010", "101101110101101", "101111111101101",
            "110101110101110", "111010010010010", "000011111100011",
        };

        struct Pending
        {
            public int2 Cell;
            /// <summary>Item index, or -1 for knowledge.</summary>
            public int Kind;
            public double Amount;
            public float Age;
        }

        struct Pop
        {
            public float2 Pos;
            public string Text;
            public Color32 Color;
            public float Age;
        }

        readonly ContentDB _content;
        readonly FeedbackConfig _config;
        readonly SpriteMesh _mesh;
        readonly Texture2D _atlas;
        readonly List<Pending> _pending = new();
        readonly List<Pop> _pops = new();

        /// <summary>Player setting (Settings → Gameplay → Number pops).</summary>
        public bool Enabled = true;

        public NumberPops(ContentDB content, FeedbackConfig config, Material spriteMaterial, Transform parent, float2 worldSize)
        {
            _content = content;
            _config = config;
            _atlas = BuildAtlas();
            _mesh = new SpriteMesh("NumberPops", spriteMaterial, _atlas, parent, worldSize, 80);
        }

        static Texture2D BuildAtlas()
        {
            int w = Glyphs.Length * GlyphW;
            var tex = new Texture2D(w, GlyphH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "NumberPopsAtlas" };
            var pixels = new Color32[w * GlyphH];
            var outline = new Color32(28, 16, 10, 255);
            var fill = new Color32(255, 255, 255, 255);
            for (int g = 0; g < Shapes.Length; g++)
            {
                string shape = Shapes[g];
                bool On(int x, int y) => x >= 0 && x < 3 && y >= 0 && y < 5 && shape[y * 3 + x] == '1';
                for (int y = 0; y < GlyphH; y++)
                for (int x = 0; x < GlyphW; x++)
                {
                    int gx = x - 1, gy = (GlyphH - 1 - y) - 1; // glyph row 0 at the top, texture row 0 at the bottom
                    Color32 c = default;
                    if (On(gx, gy)) c = fill;
                    else
                    {
                        for (int dy = -1; dy <= 1 && c.a == 0; dy++)
                        for (int dx = -1; dx <= 1 && c.a == 0; dx++)
                            if (On(gx + dx, gy + dy)) c = outline;
                    }
                    pixels[y * w + g * GlyphW + x] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>Collects this frame's deliveries and crafts.</summary>
        public void Consume(EventBus events, Simulation sim)
        {
            if (!Enabled) return;
            foreach (var e in events.Read<ItemDeliveredEvent>())
            {
                var item = _content.Items[e.Item];
                Add(e.Cell, e.Item, item.IsFood ? item.foodValue : 1f);
            }
            foreach (var e in events.Read<ItemCraftedEvent>())
            {
                if (e.Item != ContentDB.NoItem) Add(e.Origin + new int2(0, 2), e.Item, 1f);
                if (e.Knowledge > 0f) Add(e.Origin + new int2(0, 4), -1, e.Knowledge * sim.Colony.Mods.Mult(StatKind.KnowledgeGain));
            }
        }

        void Add(int2 cell, int kind, double amount)
        {
            int merge = math.max(0, _config.popMergeCells);
            for (int i = 0; i < _pending.Count; i++)
            {
                var p = _pending[i];
                if (p.Kind != kind || math.any(math.abs(p.Cell - cell) > merge)) continue;
                p.Amount += amount;
                _pending[i] = p;
                return;
            }
            _pending.Add(new Pending { Cell = cell, Kind = kind, Amount = amount });
        }

        public void Render(Simulation sim, float dt)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                p.Age += dt;
                if (p.Age < _config.popMergeSeconds && Enabled)
                {
                    _pending[i] = p;
                    continue;
                }
                _pending.RemoveAt(i);
                if (!Enabled || p.Amount < 0.5) continue;
                Color color = p.Kind < 0 ? KnowledgeColor : _content.Items[p.Kind].IsFood ? FoodColor : _content.Items[p.Kind].color;
                if (_pops.Count >= _config.maxPops) _pops.RemoveAt(0);
                _pops.Add(new Pop
                {
                    Pos = WorldGrid.CellCenterToWorld(p.Cell),
                    Text = "+" + NumberFormat.Format(p.Amount),
                    Color = Brighten(color),
                });
            }

            int quads = 0;
            for (int i = _pops.Count - 1; i >= 0; i--)
            {
                var pop = _pops[i];
                pop.Age += dt;
                if (pop.Age >= _config.popSeconds || !Enabled) _pops.RemoveAt(i);
                else
                {
                    _pops[i] = pop;
                    quads += pop.Text.Length;
                }
            }
            _mesh.EnsureCapacity(math.max(1, quads));
            var v = _mesh.Vertices;
            int q = 0;
            float px = 1f / WorldConstants.PixelsPerUnit;
            foreach (var pop in _pops)
            {
                float t = pop.Age / _config.popSeconds;
                // Ease out: a quick jump that settles, then hangs.
                float rise = (1f - (1f - t) * (1f - t)) * _config.popRise * WorldConstants.CellSize;
                var color = pop.Color;
                color.a = (byte)(255 * math.saturate((1f - t) / 0.3f));
                int width = pop.Text.Length * Advance + 1;
                float2 origin = math.round((pop.Pos + new float2(0f, rise)) / px - new float2(width * 0.5f, 0f)) * px;
                for (int c = 0; c < pop.Text.Length; c++)
                {
                    int g = Glyphs.IndexOf(pop.Text[c]);
                    if (g < 0)
                    {
                        SpriteQuads.WriteEmpty(v, q++);
                        continue;
                    }
                    float4 rect = new(g * GlyphW / (float)_atlas.width, 0f, (g + 1) * GlyphW / (float)_atlas.width, 1f);
                    SpriteQuads.WriteUpright(v, q++, origin + new float2(c * Advance * px, 0f), new int2(GlyphW, GlyphH), rect, color, Unlit);
                }
            }
            _mesh.Apply(q);
        }

        static Color32 Brighten(Color c)
        {
            var b = Color.Lerp(c, Color.white, 0.25f);
            return new Color32((byte)(b.r * 255), (byte)(b.g * 255), (byte)(b.b * 255), 255);
        }

        public void Dispose()
        {
            _mesh.Dispose();
            if (_atlas != null) UnityEngine.Object.Destroy(_atlas);
        }
    }
}
