using System;
using System.Collections.Generic;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Antarium.Presentation
{
    /// <summary>
    /// The surface layer (Milestone 10.2): plants swaying along the ground, aphids grazing in Aphid Ranches, the bird's
    /// shadow (behind the colony) and the bird itself and the rain (in front). Its own small atlas, generated in code and
    /// replaced by the PNGs in <c>Art/Surface/</c> where they exist; drawn by two sprite meshes, only what is in view.
    /// Milestone 11: shooting stars at night (a shower of them on a starfall night) and lightning bolts in thunderstorms,
    /// drawn unlit pixel by pixel behind the colony; <see cref="Flash"/> tells the sky how bright the last bolt still is.
    /// </summary>
    public sealed class SurfaceView : IDisposable
    {
        const int AtlasWidth = 96, AtlasHeight = 64;
        const float Ppu = WorldConstants.PixelsPerUnit;
        const int RainDrops = 400;
        const int MaxStreaks = 48;
        /// <summary>Art pixels per cell.</summary>
        const float CellPx = WorldConstants.PixelsPerCell;
        /// <summary>Seconds a lightning bolt is drawn, with a short dark flicker in it.</summary>
        const float BoltSeconds = 0.32f, BoltGapStart = 0.07f, BoltGapEnd = 0.11f, FlashSeconds = 0.5f;
        static readonly float2 Unlit = new(0f, -20000f);

        // Plants (2 sway frames each): fern, bush, tall stalk with a flower. First row = top. Facing does not matter.
        static readonly string[][][] Plants =
        {
            new[]
            {
                new[] { "....l......", "...lL.l....", ".l.lLlL..l.", ".LllLLl.lL.", "..LLlLLlL..", "l.lLLsLLl.l", "LlL.LsL.LlL", ".LL.lsl.LL.", "..L..s..L..", ".....s.....", ".....s.....", ".....s....." },
                new[] { ".....l.....", "....lL.l...", "..l.lLlL.l.", "..LllLLl.L.", "..LLlLLlL..", ".l.lLsLLl.l", "LlL.LsL.LlL", ".LL.lsl.LL.", "..L..s..L..", ".....s.....", ".....s.....", ".....s....." },
            },
            new[]
            {
                new[] { "....lll......", "..llLLLll.l..", ".lLLLlLLLlLl.", "lLLlLLLLlLLLl", "LLLLLLlLLLLLL", ".LLlLLLLLlLL.", "..LLsLLLsLL..", "....s...s....", "....s...s...." },
                new[] { ".....lll.....", "..llLLLll.l..", ".lLLLlLLLlLl.", "lLLlLLLLlLLLl", "LLLLLLlLLLLLL", ".LLlLLLLLlLL.", "..LLsLLLsLL..", "....s...s....", "....s...s...." },
            },
            new[]
            {
                new[] { "..fff..", ".fFyFf.", "..fff..", "...s...", "..ls...", ".lLs...", "...sl..", "...sLl.", "...s...", "..ls...", ".lLs...", "...s...", "...sl..", "...sLl.", "...s...", "...s..." },
                new[] { "...fff.", "..fFyFf", "...fff.", "...s...", "..ls...", ".lLs...", "...sl..", "...sLl.", "...s...", "..ls...", ".lLs...", "...s...", "...sl..", "...sLl.", "...s...", "...s..." },
            },
        };

        static readonly string[][] Bird =
        {
            new[] { "kk..........kk", ".kkk......kkk.", "..kkkk..kkkk..", "...kkkbbkkk...", ".....bbbbo....", "......bb......" },
            new[] { "..............", "..............", "kkkkkkbbkkkkkk", ".kkkkbbbbokkk.", "......bb......", ".............." },
        };
        static readonly string[] Dive = { "......kk", ".....kk.", "..kkkb..", ".kbbbbo.", "kkkbb...", "kk......" };
        static readonly string[] Aphid = { ".gg", "ggG" };
        static readonly string[] AphidStep = { "gg.", "Ggg" };

        readonly SpriteMesh _back;
        readonly SpriteMesh _front;
        readonly Texture2D _texture;
        readonly NativeArray<float4> _rects;
        readonly int[] _plantRect = new int[3];
        readonly int2[] _plantSize = new int2[3];
        readonly int _birdRect, _diveRect, _aphidRect, _shadowRect, _dropRect, _pixelRect;
        readonly int2 _birdSize, _diveSize;
        readonly Color32 _rainColor;
        readonly float2[] _drops = new float2[RainDrops];
        readonly ContentDB _content;
        readonly RenderConfig _render;
        float _rain;
        bool _dropsPlaced;

        struct Streak
        {
            public float2 Head;
            public float2 Dir;
            public float Speed, Age, Life;
            public int Length;
        }

        readonly Streak[] _streaks = new Streak[MaxStreaks];
        int _streakCount;
        float _streakTimer = -1f;
        Unity.Mathematics.Random _rng = new(0x5EED57A2u);
        /// <summary>Art-pixel positions of the current bolt (the main channel first, <see cref="_boltMain"/> of them).</summary>
        readonly List<int2> _bolt = new();
        int _boltMain;
        float _boltAge = 99f;
        int _seenStrikes = -1;

        /// <summary>How bright the last lightning flash still is (0..1), for the sky.</summary>
        public float Flash { get; private set; }
        static readonly Color32 White = new(255, 255, 255, 255);

        static readonly string[] PlantNames = { "plant_fern", "plant_bush", "plant_flower" };

        /// <summary>The PNG names this view looks up, with their frame counts (for the art list and tests).</summary>
        public static readonly (string Name, int Frames)[] SlotNames =
        {
            ("Surface/plant_fern", 2), ("Surface/plant_bush", 2), ("Surface/plant_flower", 2), ("Surface/bird", 2),
            ("Surface/bird_dive", 1), ("Surface/aphid", 2), ("Surface/bird_shadow", 1),
        };

        public SurfaceView(ContentDB content, RenderConfig render, ArtLibrary art, Material spriteMaterial, Transform parent, float2 worldSize)
        {
            _content = content;
            _render = render;
            var slots = new List<ArtSlot>();
            var px = new Color32[AtlasWidth * AtlasHeight];
            var rects = new List<float4>();
            var palette = new Dictionary<char, Color32>
            {
                ['l'] = Hex(0x9ad65a), ['L'] = Hex(0x5a9a34), ['s'] = Hex(0x4a6a24), ['f'] = Hex(0xf2a0c8), ['F'] = Hex(0xe060a0), ['y'] = Hex(0xffe060),
                ['k'] = Hex(0x2a2430), ['b'] = Hex(0x4a3e48), ['o'] = Hex(0xf0b040),
                ['g'] = Hex(0x8ad04a), ['G'] = Hex(0x3a6a20),
            };

            int x = 0;
            for (int k = 0; k < Plants.Length; k++)
            {
                _plantRect[k] = rects.Count;
                slots.Add(ArtSlot.Strip("Surface/" + PlantNames[k], rects.Count, 2));
                int w = Plants[k][0][0].Length, h = Plants[k][0].Length;
                _plantSize[k] = new int2(w, h);
                for (int f = 0; f < 2; f++)
                {
                    Blit(px, x, 0, Plants[k][f], palette);
                    rects.Add(Rect(x, 0, w, h));
                    x += w + 1;
                }
            }

            _birdRect = rects.Count;
            slots.Add(ArtSlot.Strip("Surface/bird", rects.Count, 2));
            _birdSize = new int2(Bird[0][0].Length, Bird[0].Length);
            for (int f = 0; f < 2; f++)
            {
                Blit(px, f * (_birdSize.x + 1), 20, Bird[f], palette);
                rects.Add(Rect(f * (_birdSize.x + 1), 20, _birdSize.x, _birdSize.y));
            }
            _diveRect = rects.Count;
            slots.Add(ArtSlot.Strip("Surface/bird_dive", rects.Count));
            _diveSize = new int2(Dive[0].Length, Dive.Length);
            Blit(px, 32, 20, Dive, palette);
            rects.Add(Rect(32, 20, _diveSize.x, _diveSize.y));

            _aphidRect = rects.Count;
            slots.Add(ArtSlot.Strip("Surface/aphid", rects.Count, 2));
            Blit(px, 42, 20, Aphid, palette);
            rects.Add(Rect(42, 20, 3, 2));
            Blit(px, 46, 20, AphidStep, palette);
            rects.Add(Rect(46, 20, 3, 2));

            // Shadow: a dithered dark ellipse (every other pixel), so the ground shows through.
            _shadowRect = rects.Count;
            slots.Add(ArtSlot.Strip("Surface/bird_shadow", rects.Count));
            for (int sy = 0; sy < 4; sy++)
            for (int sx = 0; sx < 16; sx++)
            {
                float2 d = (new float2(sx + 0.5f, sy + 0.5f) - new float2(8f, 2f)) / new float2(8f, 2f);
                if (math.lengthsq(d) <= 1f && ((sx + sy) & 1) == 0) px[(30 + sy) * AtlasWidth + sx] = new Color32(12, 8, 16, 255);
            }
            rects.Add(Rect(0, 30, 16, 4));

            _dropRect = rects.Count;
            for (int dy = 0; dy < 3; dy++) px[(30 + dy) * AtlasWidth + 20] = White;
            rects.Add(Rect(20, 30, 1, 3));
            _pixelRect = rects.Count;
            px[(AtlasHeight - 1) * AtlasWidth + AtlasWidth - 1] = White;
            rects.Add(Rect(AtlasWidth - 1, AtlasHeight - 1, 1, 1));

            _rects = new NativeArray<float4>(rects.ToArray(), Allocator.Persistent);
            ArtOverrides.Apply(art, slots.ToArray(), px, AtlasWidth, AtlasHeight, _rects);
            _texture = new Texture2D(AtlasWidth, AtlasHeight, TextureFormat.RGBA32, false, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "SurfaceAtlas",
            };
            _texture.SetPixels32(px);
            _texture.Apply(false, false);
            _rainColor = TerrainView.ToWorking(render.rainColor);
            _back = new SpriteMesh("SurfaceBack", spriteMaterial, _texture, parent, worldSize, 6);
            _front = new SpriteMesh("SurfaceFront", spriteMaterial, _texture, parent, worldSize, 20);
        }

        /// <param name="viewCenter">Camera position (world units).</param>
        /// <param name="viewHalf">Half the view size (world units).</param>
        public void Render(Simulation sim, float2 viewCenter, float2 viewHalf, float dt, float time)
        {
            var world = sim.World;
            var surface = sim.Surface;
            float2 min = (viewCenter - viewHalf) / WorldConstants.CellSize - 2f;
            float2 max = (viewCenter + viewHalf) / WorldConstants.CellSize + 2f;

            // Back: plants, aphids, the bird's shadow.
            // Sprites never render darker than the colony's minimum light, so the plants (and rain) are tinted by daylight.
            byte shade = (byte)(255f * math.lerp(0.42f, 1f, surface.Daylight));
            var dusk = new Color32(shade, shade, (byte)math.min(255, shade + 20), 255);
            var plants = surface.Plants;
            var buildings = sim.Buildings.All;
            // Shooting stars and lightning (drawn unlit, behind the plants).
            UpdateStreaks(world, surface, min, max, dt);
            UpdateBolt(world, surface, max, dt);
            int sky = 0;
            for (int s = 0; s < _streakCount; s++) sky += _streaks[s].Length;
            if (_boltAge < BoltSeconds) sky += _bolt.Count + _boltMain;
            _back.EnsureCapacity(plants.Count + buildings.Length * 4 + 1 + sky);
            var v = _back.Vertices;
            int quad = 0;
            foreach (var p in plants)
            {
                if (p.X < min.x || p.X > max.x || !SurfacePlants.IsAlive(world, p)) continue;
                int ground = world.SurfaceY[p.X] + 1;
                if (ground < min.y || ground > max.y + 4) continue;
                int kind = math.min(p.Kind, Plants.Length - 1);
                int frame = math.sin(time * 1.3f + p.Variant * 0.37f + p.X * 0.21f) > 0.35f ? 1 : 0;
                float2 at = new float2(p.X + 0.5f, ground) * WorldConstants.CellSize;
                var size = _plantSize[kind];
                SpriteQuads.WriteUpright(v, quad++, Snap(at - new float2(size.x * 0.5f, 0f) / Ppu), size, _rects[_plantRect[kind] + frame], dusk, at);
            }
            for (int id = 0; id < buildings.Length; id++)
            {
                var b = buildings[id];
                if (!b.IsActive || _content.Buildings[b.Def].Def.art != PlaceholderArt.Ranch) continue;
                if (b.Origin.x > max.x || b.Origin.x + b.Size.x < min.x || b.Origin.y > max.y || b.Origin.y + b.Size.y < min.y) continue;
                // Four aphids pace along the pasture floor, each at its own speed.
                for (int a = 0; a < 4; a++)
                {
                    float width = b.Size.x * 8f - 6f;
                    float phase = time * (2.5f + a * 0.7f) + a * 9.1f + id;
                    float tri = math.abs(math.frac(phase / (2f * width)) * 2f - 1f) * width;
                    int facing = math.frac(phase / (2f * width)) < 0.5f ? 1 : -1;
                    float2 at = WorldGrid.CellCenterToWorld(b.Origin) + new float2((tri - 1f) / Ppu, (a % 2 == 0 ? 2f : 6f + a) / Ppu - WorldConstants.CellSize * 0.5f);
                    int frame = ((int)(phase * 3f) & 1);
                    SpriteQuads.WriteQuad(v, quad++, Snap(at), new int2(3, 2), new int2(0, 1), facing, _rects[_aphidRect + frame], White, at);
                }
            }
            quad = WriteStreaks(world, v, quad);
            quad = WriteBolt(v, quad);

            bool circling = surface.Bird == BirdPhase.Circling;
            if (circling)
            {
                float2 c = surface.BirdPos;
                int gx = math.clamp((int)c.x, 0, world.Width - 1);
                float2 at = new float2(c.x, world.SurfaceY[gx] + 1f) * WorldConstants.CellSize;
                SpriteQuads.WriteQuad(v, quad++, Snap(at + new float2(0f, 2f / Ppu)), new int2(16, 4), new int2(0, 1), 1, _rects[_shadowRect], White, at);
            }
            _back.Apply(quad);

            // Front: the bird and the rain.
            _rain = Mathf.MoveTowards(_rain, surface.Weather == WeatherKind.Rain ? 1f : 0f, dt / 3f);
            _front.EnsureCapacity(RainDrops + 1);
            v = _front.Vertices;
            quad = 0;
            if (surface.Bird != BirdPhase.None) quad = WriteBird(sim, v, quad, time);
            if (_rain > 0f) quad = WriteRain(world, v, quad, min, max, dt, surface.Daylight);
            else _dropsPlaced = false;
            _front.Apply(quad);
        }

        int WriteBird(Simulation sim, NativeArray<SpriteVertex> v, int quad, float time)
        {
            var s = sim.Surface;
            var cfg = sim.Config.surface;
            var world = sim.World;
            float2 target = s.BirdPos;
            int gx = math.clamp((int)target.x, 0, world.Width - 1);
            float ground = world.SurfaceY[gx] + 1.5f;
            float2 pos;
            int rect, facing;
            int2 size;
            if (s.Bird == BirdPhase.Circling && s.BirdTimer < 0.6f)
            {
                // Dive onto the prey.
                float t = 1f - s.BirdTimer / 0.6f;
                pos = math.lerp(new float2(target.x - 6f, ground + 14f), new float2(target.x, ground), t * t);
                rect = _diveRect;
                size = _diveSize;
                facing = 1;
            }
            else if (s.Bird == BirdPhase.Circling)
            {
                float a = time * 1.6f;
                pos = new float2(target.x + math.cos(a) * 7f, ground + 14f + math.sin(a * 2f) * 1.5f);
                rect = _birdRect + ((int)(time * 6f) & 1);
                size = _birdSize;
                facing = math.sin(a) > 0f ? -1 : 1;
            }
            else
            {
                float t = math.max(0f, cfg.birdShelterSeconds - s.BirdTimer);
                pos = new float2(target.x + t * 18f, ground + 1f + t * 12f);
                rect = _birdRect + ((int)(time * 8f) & 1);
                size = _birdSize;
                facing = 1;
            }
            float2 at = pos * WorldConstants.CellSize;
            SpriteQuads.WriteQuad(v, quad++, Snap(at), size, new int2(0, 1), facing, _rects[rect], White, at);
            return quad;
        }

        /// <summary>Raindrops fall slanted through the view and stop at the ground (never underground).</summary>
        int WriteRain(WorldGrid world, NativeArray<SpriteVertex> v, int quad, float2 min, float2 max, float dt, float daylight)
        {
            float k = math.lerp(0.45f, 1f, daylight);
            var color = new Color32((byte)(_rainColor.r * k), (byte)(_rainColor.g * k), (byte)(_rainColor.b * k), _rainColor.a);
            float2 span = max - min;
            var rng = new Unity.Mathematics.Random((uint)(Time.frameCount * 747796405) | 1u);
            if (!_dropsPlaced)
            {
                for (int i = 0; i < RainDrops; i++) _drops[i] = min + rng.NextFloat2() * span;
                _dropsPlaced = true;
            }
            int visible = (int)(RainDrops * _rain);
            float2 fall = new float2(-6f, -40f) * dt;
            for (int i = 0; i < RainDrops; i++)
            {
                var d = _drops[i] + fall;
                int x = (int)math.floor(d.x);
                bool landed = x < 0 || x >= world.Width || d.y <= world.SurfaceY[x] + 1 || d.x < min.x || d.y < min.y;
                if (landed || d.x > max.x || d.y > max.y + 4f) d = new float2(min.x + rng.NextFloat() * (span.x + 8f), max.y + rng.NextFloat(4f));
                _drops[i] = d;
                if (i >= visible) continue;
                x = (int)math.floor(d.x);
                if (x < 0 || x >= world.Width || d.y <= world.SurfaceY[x] + 1) continue;
                float2 at = d * WorldConstants.CellSize;
                SpriteQuads.WriteUpright(v, quad++, Snap(at), new int2(1, 3), _rects[_dropRect], color, at);
            }
            return quad;
        }

        // ---------------------------------------------------------------- shooting stars

        /// <summary>
        /// At night under a clear sky a shooting star crosses the view every <c>shootingStarInterval</c> seconds; on a starfall
        /// night every <c>starfallInterval</c>, all streaking the same way (away from the shower's radiant).
        /// </summary>
        void UpdateStreaks(WorldGrid world, SurfaceState surface, float2 min, float2 max, float dt)
        {
            for (int i = _streakCount - 1; i >= 0; i--)
            {
                ref var s = ref _streaks[i];
                s.Age += dt;
                s.Head += s.Dir * s.Speed * dt;
                if (s.Age < s.Life) continue;
                _streaks[i] = _streaks[--_streakCount];
            }
            if (dt <= 0f) return;
            float night = math.saturate(1f - surface.Daylight * 2f) * (1f - _rain);
            if (night < 0.3f)
            {
                _streakTimer = -1f;
                return;
            }
            var range = surface.Starfall ? _render.starfallInterval : _render.shootingStarInterval;
            float lo = math.max(0.02f, range.x), hi = math.max(lo, range.y) + 1e-3f;
            if (_streakTimer < 0f || _streakTimer > hi) _streakTimer = _rng.NextFloat(lo, hi);
            _streakTimer -= dt;
            if (_streakTimer > 0f) return;
            _streakTimer = _rng.NextFloat(lo, hi);
            if (_streakCount >= MaxStreaks) return;

            // Only in the sky in view: above the highest ground under the view.
            int x0 = math.clamp((int)min.x, 0, world.Width - 1), x1 = math.clamp((int)max.x, 0, world.Width - 1);
            int ground = 0;
            for (int x = x0; x <= x1; x++) ground = math.max(ground, world.SurfaceY[x]);
            float low = math.max(ground + 6f, min.y + 2f), high = math.min(max.y - 2f, world.Height - 1f);
            if (high - low < 4f) return;

            bool shower = surface.Starfall;
            float side = shower ? ((surface.Day & 1) == 0 ? 1f : -1f) : (_rng.NextBool() ? 1f : -1f);
            float angle = math.radians(shower ? _rng.NextFloat(22f, 34f) : _rng.NextFloat(12f, 42f));
            _streaks[_streakCount++] = new Streak
            {
                Head = new float2(_rng.NextFloat(min.x, max.x), _rng.NextFloat(math.lerp(low, high, 0.3f), high)),
                Dir = new float2(math.cos(angle) * side, -math.sin(angle)),
                Speed = _rng.NextFloat(12f, 24f),
                Life = _rng.NextFloat(0.45f, 1f),
                Length = _rng.NextInt(10, shower && _rng.NextFloat() < 0.25f ? 40 : 26),
            };
        }

        /// <summary>A bright head and a tail that thins out (dithered), fading in and out over the streak's life.</summary>
        int WriteStreaks(WorldGrid world, NativeArray<SpriteVertex> v, int quad)
        {
            Color32 star = TerrainView.ToWorking(_render.starColor);
            for (int i = 0; i < _streakCount; i++)
            {
                var s = _streaks[i];
                float env = math.saturate(s.Age / 0.08f) * math.saturate((s.Life - s.Age) / (s.Life * 0.45f));
                for (int k = 0; k < s.Length; k++)
                {
                    float f = k / (float)s.Length;
                    float a = env * (1f - f * f); // solid near the head, thinning (dithered) toward the tail
                    if (a < 0.06f) break;
                    float2 p = s.Head - s.Dir * (k / CellPx);
                    int cx = (int)math.floor(p.x);
                    if (cx < 0 || cx >= world.Width || p.y <= world.SurfaceY[cx] + 1.5f) continue;
                    var c = k < 2 ? White : star;
                    float2 at = math.floor(p * CellPx) / Ppu;
                    SpriteQuads.WriteUpright(v, quad++, at, new int2(1, 1), _rects[_pixelRect], new Color32(c.r, c.g, c.b, (byte)(255f * math.min(1f, a))), Unlit);
                }
            }
            return quad;
        }

        // ---------------------------------------------------------------- lightning

        /// <summary>A new strike in the simulation starts a jagged bolt from above the view down to where it hit.</summary>
        void UpdateBolt(WorldGrid world, SurfaceState surface, float2 max, float dt)
        {
            int strikes = surface.LightningStrikes;
            if (_seenStrikes >= 0 && strikes > _seenStrikes) StartBolt(world, surface.LightningCell, max);
            _seenStrikes = strikes;
            _boltAge += dt;
            bool gap = _boltAge > BoltGapStart && _boltAge < BoltGapEnd;
            Flash = math.saturate(1f - _boltAge / FlashSeconds) * (gap ? 0.35f : 1f);
        }

        void StartBolt(WorldGrid world, int2 cell, float2 max)
        {
            _bolt.Clear();
            _boltAge = 0f;
            int gx = (int)((cell.x + 0.5f) * CellPx), groundPx = (int)(cell.y * CellPx);
            int topPx = (int)(math.min(world.Height, max.y + 2f) * CellPx);
            if (topPx - groundPx < 8) return; // the sky is not in view: only the flash
            topPx = math.min(topPx, groundPx + 70 * (int)CellPx);

            // Main channel: segments of a few pixels, each leaning left, right or straight down, kept near the strike.
            var forks = new List<int2>(4);
            int x = gx + _rng.NextInt(-16, 17), dx = 0, run = 0;
            for (int y = topPx; y >= groundPx; y--)
            {
                if (run-- <= 0)
                {
                    run = _rng.NextInt(2, 7);
                    dx = _rng.NextInt(-1, 2);
                    if (x - gx > 10) dx = -1;
                    else if (gx - x > 10) dx = 1;
                    if (forks.Count < 3 && y < topPx - 12 && y > groundPx + 24 && _rng.NextFloat() < 0.12f) forks.Add(new int2(x, y));
                }
                x += dx;
                _bolt.Add(new int2(x, y));
            }
            _boltMain = _bolt.Count;
            // Forks: thinner side channels that peter out in the air.
            foreach (var f in forks)
            {
                int side = _rng.NextBool() ? 1 : -1, fx = f.x, length = _rng.NextInt(12, 36);
                for (int k = 0, y = f.y; k < length && y > groundPx + 4; k++, y--)
                {
                    if (_rng.NextFloat() < 0.7f) fx += side;
                    _bolt.Add(new int2(fx, y));
                }
            }
        }

        /// <summary>The bolt, two pixels wide along its main channel, one along its forks; dark for a moment in between.</summary>
        int WriteBolt(NativeArray<SpriteVertex> v, int quad)
        {
            if (_boltAge >= BoltSeconds || (_boltAge > BoltGapStart && _boltAge < BoltGapEnd) || _bolt.Count == 0) return quad;
            Color32 c = TerrainView.ToWorking(_render.lightningColor);
            var core = new Color32(c.r, c.g, c.b, 255);
            var fork = new Color32(c.r, c.g, c.b, (byte)(_boltAge < BoltGapStart ? 255 : 170));
            var rect = _rects[_pixelRect];
            for (int i = 0; i < _bolt.Count; i++)
            {
                bool main = i < _boltMain;
                float2 at = new float2(_bolt[i]) / Ppu;
                SpriteQuads.WriteUpright(v, quad++, at, new int2(1, 1), rect, main ? core : fork, Unlit);
                if (main) SpriteQuads.WriteUpright(v, quad++, at + new float2(1f / Ppu, 0f), new int2(1, 1), rect, fork, Unlit);
            }
            return quad;
        }

        static float2 Snap(float2 world) => math.round(world * Ppu) / Ppu;

        static float4 Rect(int x, int y, int w, int h) =>
            new(x / (float)AtlasWidth, y / (float)AtlasHeight, (x + w) / (float)AtlasWidth, (y + h) / (float)AtlasHeight);

        static void Blit(Color32[] px, int ox, int oy, string[] rows, Dictionary<char, Color32> palette)
        {
            int h = rows.Length;
            for (int r = 0; r < h; r++)
            {
                int y = oy + (h - 1 - r);
                for (int x = 0; x < rows[r].Length; x++)
                    if (palette.TryGetValue(rows[r][x], out var c)) px[y * AtlasWidth + ox + x] = c;
            }
        }

        static Color32 Hex(int rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        public void Dispose()
        {
            _back.Dispose();
            _front.Dispose();
            if (_rects.IsCreated) _rects.Dispose();
            if (_texture != null) UnityEngine.Object.Destroy(_texture);
        }
    }
}
