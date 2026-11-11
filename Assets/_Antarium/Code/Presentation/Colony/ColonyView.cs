using System;
using System.Collections.Generic;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Antarium.Presentation
{
    /// <summary>
    /// Draws every ant, the queen, eggs, item piles, critters and enemies as quads in one dynamic mesh (one draw
    /// call). Ant quads are built by a Burst job. Sprites snap to the art-pixel grid and rotate in 90° steps to
    /// follow the surface an ant clings to, so pixels stay crisp. Hurt ants and enemies flash and show a health bar.
    /// </summary>
    /// <summary>A princess in the air during the Nuptial Flight (world position, wing phase, facing).</summary>
    public struct Flyer
    {
        public float2 Pos;
        public float Phase;
        public int Facing;
    }

    public sealed class ColonyView : IDisposable
    {
        /// <summary>Body, carried item / egg (or idle marker), a second carried item or the possessed marker, health bar back and front.</summary>
        const int QuadsPerAnt = 5;
        const int QuadsPerEnemy = 4;
        const float FlashSeconds = 0.15f;
        const float QueenBreathsPerSecond = 0.7f;

        readonly ContentDB _content;
        readonly ColonySprites _sprites;
        readonly SpriteMesh _mesh;
        readonly NativeArray<Color32> _itemColors;
        readonly Color32 _idleTint;
        readonly Color32 _possessedTint;
        readonly BarColors _bars;
        /// <summary>Per <see cref="AntCaste"/>: 1 if it carries a light (drawn with a halo).</summary>
        readonly NativeArray<byte> _casteHalo;
        /// <summary>Lane jitter in art pixels: along the route, away from the surface, into it.</summary>
        readonly int3 _lanePixels;
        static readonly Color32 SparkleTint = new(255, 230, 120, 255);
        static readonly Color32 AlertTint = new(255, 150, 70, 255);
        static readonly Color32 White = new(255, 255, 255, 255);

        /// <summary>Princesses flying off (Nuptial Flight), drawn on top of everything. Filled by the game each frame.</summary>
        public readonly List<Flyer> Flyers = new();
        /// <summary>The princesses left on their flight: the chambers are drawn empty.</summary>
        public bool PrincessesFlown;

        struct BarColors
        {
            public Color32 Ant, Enemy, Back, Flash;
        }

        /// <param name="lanePixels">Lane jitter in art pixels: x ahead of / behind its spot, y away from the surface it walks on, z into it.</param>
        public ColonyView(ContentDB content, RenderConfig render, ArtLibrary art, Material spriteMaterial, Transform parent, float2 worldSize,
            int3 lanePixels = default)
        {
            _content = content;
            _lanePixels = lanePixels;
            _casteHalo = new NativeArray<byte>(Castes.Count, Allocator.Persistent);
            for (int c = 0; c < Castes.Count; c++) _casteHalo[c] = content.CasteDefs[c].lightRadius > 0f ? (byte)1 : (byte)0;
            _sprites = new ColonySprites(art);
            _mesh = new SpriteMesh("Colony", spriteMaterial, _sprites.Texture, parent, worldSize, 10);
            _itemColors = ItemColors(content);
            _idleTint = TerrainView.ToWorking(render.overlayIdleAntColor);
            _possessedTint = TerrainView.ToWorking(render.possessedColor);
            _bars = new BarColors
            {
                Ant = TerrainView.ToWorking(render.healthBarColor),
                Enemy = TerrainView.ToWorking(render.enemyHealthBarColor),
                Back = TerrainView.ToWorking(render.healthBarBackColor),
                Flash = TerrainView.ToWorking(render.hurtFlashColor),
            };
        }

        /// <summary>Item colours in the working colour space, for vertex tints.</summary>
        public static NativeArray<Color32> ItemColors(ContentDB content)
        {
            var colors = new NativeArray<Color32>(math.max(1, content.Items.Length), Allocator.Persistent);
            for (int i = 0; i < content.Items.Length; i++)
                colors[i] = TerrainView.ToWorking(content.Items[i].color);
            return colors;
        }

        /// <param name="overlay">Throughput overlay: idle workers get a marker above them.</param>
        public void Render(Simulation sim, float alpha, bool overlay, float time)
        {
            var colony = sim.Colony;
            var events = sim.RandomEvents;
            var enemies = sim.Enemies.Enemies;
            int antCount = sim.Ants.Count;
            // Halos first (behind every ant), then the ants.
            int quads = antCount + antCount * QuadsPerAnt + 1 + colony.Eggs.Count + colony.Piles.Count + events.Critters.Count * 2 + events.Active.Count
                        + enemies.Count * QuadsPerEnemy + colony.Princesses + Flyers.Count;
            _mesh.EnsureCapacity(quads);
            var vertices = _mesh.Vertices;

            new AntQuadJob
            {
                Ants = sim.Ants.Ants.AsArray(),
                Nav = new NavGrid(sim.World),
                Rects = _sprites.Rects,
                CasteRect = _sprites.CasteRect,
                CasteSize = _sprites.CasteSize,
                CasteHalo = _casteHalo,
                AntBase = antCount,
                LanePixels = _lanePixels,
                Castes = _content.CasteSim,
                HealthMultiplier = sim.Ants.HealthMultiplier,
                ItemColors = _itemColors,
                Vertices = vertices,
                Alpha = alpha,
                Overlay = overlay,
                Blink = ((int)(time * 3f) & 1) == 0,
                IdleTint = _idleTint,
                PossessedTint = _possessedTint,
                Bars = _bars,
            }.Schedule(antCount, 64).Complete();

            int quad = antCount + antCount * QuadsPerAnt;
            var rects = _sprites.Rects;

            // The queen breathes: her gaster swells and settles about once a second.
            int queenRect = ((int)(time * QueenBreathsPerSecond * 2f) & 1) == 0 ? ColonySprites.QueenRect : ColonySprites.QueenBreathRect;
            int throne = sim.Buildings.Throne;
            if (throne >= 0 && sim.Buildings.ThroneStanding)
            {
                // On the Queen's Throne (10.6): centred on the dais, raised to its top.
                float centerX = sim.Buildings.ThroneCenter(throne).x;
                float2 at = new float2(centerX + 0.5f, sim.Buildings.Get(throne).Origin.y + 0.5f) * WorldConstants.CellSize
                            + new float2(0f, BuildingSprites.ThroneDais / (float)WorldConstants.PixelsPerUnit);
                SpriteQuads.WriteQuad(vertices, quad++, SpriteQuads.OnSurface(at, new int2(0, 1), ColonySprites.QueenHeight),
                    new int2(ColonySprites.QueenWidth, ColonySprites.QueenHeight), new int2(0, 1), 1, rects[queenRect], White, at);
            }
            else WriteOnFloor(vertices, colony.QueenCell, ColonySprites.QueenWidth, ColonySprites.QueenHeight, rects[queenRect], White, quad++);
            foreach (var egg in colony.Eggs)
            {
                if (egg.InNursery && egg.Nursery < sim.Buildings.All.Length)
                {
                    // In a nursery cradle: two eggs side by side, lifted into the silk bowl.
                    var b = sim.Buildings.All[egg.Nursery];
                    float2 px = new float2(Simulation.NurserySlotOffset(b, egg.Slot), 2f) / WorldConstants.PixelsPerUnit;
                    float2 at = WorldGrid.CellCenterToWorld(egg.Cell) + px;
                    SpriteQuads.WriteQuad(vertices, quad++, SpriteQuads.OnSurface(at, new int2(0, 1), ColonySprites.EggHeight),
                        new int2(ColonySprites.EggWidth, ColonySprites.EggHeight), new int2(0, 1), 1, rects[ColonySprites.EggRect], White, at);
                    continue;
                }
                WriteOnFloor(vertices, egg.Cell, ColonySprites.EggWidth, ColonySprites.EggHeight, rects[ColonySprites.EggRect], White, quad++);
            }
            for (int i = 0; i < colony.Piles.Count; i++)
            {
                var pile = colony.Piles[i];
                WriteOnFloor(vertices, pile.Cell, ColonySprites.PileWidth, ColonySprites.PileHeight, rects[ColonySprites.PileRect], _itemColors[pile.Item], quad++);
            }

            // Random events: the golden beetle (with a blinking sparkle so it can be spotted) and a marker over trapped ants.
            bool blink = ((int)(time * 4f) & 1) == 0;
            var nav = new NavGrid(sim.World);
            foreach (var c in events.Critters)
            {
                float2 cellPos = math.lerp(c.PrevPos, c.Pos, alpha);
                bool aphid = events.TryGetActive(c.EventId, out var owner) && owner.Kind == RandomEventKind.GoldenAphid;
                int2 size = aphid ? new int2(ColonySprites.AphidWidth, ColonySprites.AphidHeight) : new int2(ColonySprites.BeetleWidth, ColonySprites.BeetleHeight);
                var center = Body(nav, cellPos, c.Heading, size.y, out int2 up, out int facing, out float2 world);
                int frame = aphid ? (int)(time * 2.5f) & 1 : c.IsMoving ? (int)(c.WalkPhase * 4f) & 1 : 0;
                SpriteQuads.WriteQuad(vertices, quad++, center, size, up, facing,
                    rects[(aphid ? ColonySprites.AphidRect : ColonySprites.BeetleRect) + frame], White, world);
                if (blink)
                {
                    float2 sparkle = center + new float2(up) * (6f / WorldConstants.PixelsPerUnit);
                    SpriteQuads.WriteQuad(vertices, quad++, sparkle, new int2(ColonySprites.MarkerSize), up, 1, rects[ColonySprites.MarkerRect], SparkleTint, world);
                }
                else SpriteQuads.WriteEmpty(vertices, quad++);
            }
            foreach (var e in events.Active)
            {
                if (e.Kind != RandomEventKind.TrappedAnts || !blink)
                {
                    SpriteQuads.WriteEmpty(vertices, quad++);
                    continue;
                }
                float2 above = WorldGrid.CellCenterToWorld(e.Cell + new int2(0, 2));
                SpriteQuads.WriteQuad(vertices, quad++, above, new int2(ColonySprites.MarkerSize), new int2(0, 1), 1, rects[ColonySprites.MarkerRect], AlertTint, above);
            }

            // Enemies: walk cycle, a bite wiggle, a red flash when hurt and a health bar once damaged.
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                var def = _content.Enemies[e.Def].Def;
                int art = math.clamp((int)def.art, 0, ColonySprites.EnemyArtCount - 1);
                int2 size = _sprites.EnemySize[art];
                float2 cellPos = math.lerp(e.PrevPos, e.Pos, alpha);
                var center = Body(nav, cellPos, e.Heading, size.y, out int2 up, out int facing, out float2 world);
                int frame = e.IsMoving ? (int)(e.WalkPhase * 4f) & 1 : e.Attacking ? (int)(time * 6f) & 1 : 0;
                var tint = e.SinceHurt < FlashSeconds ? _bars.Flash : White;
                // A mole cricket digging uses its dig frames.
                int rect = art == (int)EnemyArt.MoleCricket && e.State == EnemyState.Burrow && e.Attacking
                    ? ColonySprites.MoleDigRect + frame : ColonySprites.EnemyRect + art * 2 + frame;
                SpriteQuads.WriteQuad(vertices, quad++, center, size, up, facing, rects[rect], tint, world);
                // A rival ant carries its loot in its jaws, like a worker.
                if (e.LootCount > 0 && e.Loot < _itemColors.Length)
                {
                    float2 forward = new float2(up.y, -up.x) * facing;
                    float2 carry = center + forward * ((size.x * 0.5f) / WorldConstants.PixelsPerUnit) + new float2(up) * (2f / WorldConstants.PixelsPerUnit);
                    SpriteQuads.WriteQuad(vertices, quad++, carry, new int2(ColonySprites.CarryWidth, ColonySprites.CarryHeight), up, facing,
                        rects[ColonySprites.CarryRect], _itemColors[e.Loot], world);
                }
                else SpriteQuads.WriteEmpty(vertices, quad++);
                WriteBar(vertices, quad, rects[ColonySprites.PixelRect], center + new float2(0f, (size.y * 0.5f + 2f) / WorldConstants.PixelsPerUnit),
                    e.Health / math.max(1f, def.health), _bars.Back, _bars.Enemy, world);
                quad += 2;
            }
            quad = WritePrincesses(sim, vertices, quad);
            _mesh.Apply(quad);
        }

        /// <summary>
        /// Princesses rest on the silk beds of the princess chambers (in building order; any beyond the beds are not
        /// drawn), and the ones of a Nuptial Flight flap up into the sky.
        /// </summary>
        int WritePrincesses(Simulation sim, NativeArray<SpriteVertex> vertices, int quad)
        {
            var rects = _sprites.Rects;
            var size = new int2(ColonySprites.PrincessWidth, ColonySprites.PrincessHeight);
            var up = new int2(0, 1);
            int left = PrincessesFlown ? 0 : sim.Colony.Princesses;
            var all = sim.Buildings.All;
            for (int id = 0; id < all.Length && left > 0; id++)
            {
                var b = all[id];
                if (!b.Alive || !b.IsActive || b.Kind != BuildingKind.Processor) continue;
                int beds = math.min(_content.Buildings[b.Def].Def.princessSlots, BuildingSprites.PrincessBeds.Length);
                for (int s = 0; s < beds && left > 0; s++, left--)
                {
                    float2 at = WorldGrid.CellCenterToWorld(b.Origin)
                                + new float2(BuildingSprites.PrincessBeds[s].x - BuildingSprites.Cell / 2, BuildingSprites.PrincessBeds[s].y + 4) / WorldConstants.PixelsPerUnit;
                    SpriteQuads.WriteQuad(vertices, quad++, SpriteQuads.OnSurface(at, up, size.y), size, up, s == 0 ? 1 : -1,
                        rects[ColonySprites.PrincessRect], White, at);
                }
            }
            foreach (var f in Flyers)
            {
                int frame = 1 + ((int)(f.Phase * 12f) & 1);
                SpriteQuads.WriteQuad(vertices, quad++, f.Pos, size, up, f.Facing, rects[ColonySprites.PrincessRect + frame], White, f.Pos);
            }
            return quad;
        }

        /// <summary>Centre and orientation of a creature sprite clinging to the surface at <paramref name="cellPos"/>.</summary>
        static float2 Body(NavGrid nav, float2 cellPos, int2 heading, int heightPx, out int2 up, out int facing, out float2 world)
        {
            int2 cell = (int2)math.floor(cellPos);
            up = nav.TouchesSolid(cell) ? nav.SurfaceUp(cell) : new int2(0, 1);
            int2 forward = new int2(up.y, -up.x);
            int along = math.dot(heading, forward);
            facing = along != 0 ? (int)math.sign(along) : 1;
            world = cellPos * WorldConstants.CellSize;
            return SpriteQuads.OnSurface(world, up, heightPx);
        }

        /// <summary>A health bar (two quads) above a sprite; hidden at full health.</summary>
        static void WriteBar(NativeArray<SpriteVertex> v, int quad, float4 pixel, float2 center, float fraction, Color32 back, Color32 front, float2 light)
        {
            const int Width = 8;
            if (fraction >= 0.999f)
            {
                SpriteQuads.WriteEmpty(v, quad);
                SpriteQuads.WriteEmpty(v, quad + 1);
                return;
            }
            float px = 1f / WorldConstants.PixelsPerUnit;
            float2 left = math.round((center - new float2(Width * 0.5f * px, 0f)) / px) * px;
            SpriteQuads.WriteUpright(v, quad, left, new int2(Width, 1), pixel, back, light);
            int filled = math.clamp((int)math.ceil(fraction * Width), 1, Width);
            SpriteQuads.WriteUpright(v, quad + 1, left, new int2(filled, 1), pixel, front, light);
        }

        static void WriteOnFloor(NativeArray<SpriteVertex> vertices, int2 cell, int w, int h, float4 rect, Color32 color, int quad)
        {
            float2 center = WorldGrid.CellCenterToWorld(cell);
            SpriteQuads.WriteQuad(vertices, quad, SpriteQuads.OnSurface(center, new int2(0, 1), h), new int2(w, h), new int2(0, 1), 1, rect, color, center);
        }

        public void Dispose()
        {
            _mesh.Dispose();
            if (_itemColors.IsCreated) _itemColors.Dispose();
            if (_casteHalo.IsCreated) _casteHalo.Dispose();
            _sprites.Dispose();
        }

        [BurstCompile]
        struct AntQuadJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Ant> Ants;
            public NavGrid Nav;
            [ReadOnly] public NativeArray<float4> Rects;
            [ReadOnly] public NativeArray<int> CasteRect;
            [ReadOnly] public NativeArray<int2> CasteSize;
            [ReadOnly] public NativeArray<byte> CasteHalo;
            /// <summary>First ant quad (the halos come before, one per ant).</summary>
            public int AntBase;
            public int3 LanePixels;
            [ReadOnly] public NativeArray<CasteSim> Castes;
            public float HealthMultiplier;
            [ReadOnly] public NativeArray<Color32> ItemColors;
            [NativeDisableParallelForRestriction] public NativeArray<SpriteVertex> Vertices;
            public float Alpha;
            public bool Overlay;
            public bool Blink;
            public Color32 IdleTint;
            public Color32 PossessedTint;
            public BarColors Bars;

            public void Execute(int index)
            {
                var a = Ants[index];
                int quad = AntBase + index * QuadsPerAnt;
                float2 cellPos = math.lerp(a.PrevPos, a.Pos, Alpha);
                int2 cell = (int2)math.floor(cellPos);
                int2 up;
                if (a.State == AntState.Falling) up = new int2(0, 1);
                else if (Nav.TouchesSolid(cell)) up = Nav.SurfaceUp(cell);
                // On the back wall: walk upright when moving sideways, climb sideways-on when moving vertically.
                else up = math.abs(a.Heading.y) > math.abs(a.Heading.x) ? new int2(-1, 0) : new int2(0, 1);

                int2 forwardAxis = new int2(up.y, -up.x);
                int along = math.dot(a.Heading, forwardAxis);
                int facing = along != 0 ? (int)math.sign(along) : (a.Heading.x + a.Heading.y >= 0 ? 1 : -1);

                int frame;
                bool working = a.State == AntState.Digging || a.State == AntState.Building || a.State == AntState.Fighting
                               || a.State == AntState.Tending || a.ActTimer > 0f;
                if (working) frame = (int)(a.WalkPhase * 2f + (a.ActTimer > 0f ? a.ActTimer * 8f : 0f)) & 1;
                else if (a.IsMoving) frame = (int)(a.WalkPhase * 4f) & 3;
                else frame = 0;

                int caste = (int)a.Caste;
                int2 size = CasteSize[caste];
                int rect = CasteRect[caste] + frame;
                float2 world = cellPos * WorldConstants.CellSize;
                // Lane jitter: each ant is drawn a few whole art pixels ahead of or behind its spot, and above or below the
                // surface it walks on (the second offset comes from a hash of its lane, so the two vary independently).
                int ahead = (int)math.round(a.Lane / 127f * LanePixels.x);
                uint h = math.hash(new int2(a.Lane, 0x51ed));
                int side = LanePixels.y + LanePixels.z;
                int across = side > 0 ? (int)(h % (uint)(side + 1)) - LanePixels.z : 0;
                float2 laneShift = (new float2(forwardAxis) * ahead + new float2(up) * across) / WorldConstants.PixelsPerUnit;
                float2 center = SpriteQuads.OnSurface(world, up, size.y) + laneShift;
                var tint = a.SinceHurt < FlashSeconds ? Bars.Flash : new Color32(255, 255, 255, 255);
                SpriteQuads.WriteQuad(Vertices, quad, center, size, up, facing, Rects[rect], tint, world);

                // A glow ant's light: the halo, unlit, centred on its gaster.
                if (CasteHalo[caste] != 0)
                {
                    float2 back = new float2(forwardAxis) * facing * ((ColonySprites.GasterX - size.x * 0.5f) / WorldConstants.PixelsPerUnit);
                    SpriteQuads.WriteQuad(Vertices, index, center + back, new int2(ColonySprites.HaloSize), new int2(0, 1), 1,
                        Rects[ColonySprites.HaloRect], new Color32(255, 255, 255, 255), new float2(0f, -20000f)); // unlit
                }
                else SpriteQuads.WriteEmpty(Vertices, index);

                float max = Castes[caste].Health * HealthMultiplier;
                WriteBar(Vertices, quad + 3, Rects[ColonySprites.PixelRect], center + new float2(0f, (size.y * 0.5f + 2f) / WorldConstants.PixelsPerUnit),
                    a.Health / math.max(1f, max), Bars.Back, Bars.Ant, world);

                // The ant you control: a gold diamond above it (blinks so it can be found in a crowd).
                float2 above = center + new float2(up) * ((size.y * 0.5f + 3f) / WorldConstants.PixelsPerUnit);
                if (a.Possessed && Blink)
                    SpriteQuads.WriteQuad(Vertices, quad + 2, above, new int2(ColonySprites.MarkerSize), up, facing, Rects[ColonySprites.MarkerRect], PossessedTint, world);
                else SpriteQuads.WriteEmpty(Vertices, quad + 2);

                bool idle = !a.IsSoldier && !a.Possessed && (a.State == AntState.Idle || (a.State == AntState.Wander && !a.IsCarrying));
                if (Overlay && idle)
                {
                    // Idle marker: a dot floating above the ant (uses the carried-item slot, which is empty).
                    SpriteQuads.WriteQuad(Vertices, quad + 1, above, new int2(ColonySprites.MarkerSize), up, facing,
                        Rects[ColonySprites.MarkerRect], IdleTint, world);
                    return;
                }

                float2 f = new float2(forwardAxis) * facing;
                float2 carry = center + f * ((size.x * 0.5f) / WorldConstants.PixelsPerUnit) + new float2(up) * (2f / WorldConstants.PixelsPerUnit);
                if (a.CarryingEgg)
                {
                    SpriteQuads.WriteQuad(Vertices, quad + 1, carry, new int2(ColonySprites.EggWidth, ColonySprites.EggHeight), up, facing,
                        Rects[ColonySprites.EggRect], new Color32(255, 255, 255, 255), world);
                }
                else if (a.IsCarrying)
                {
                    var color = ItemColors[a.Carry];
                    SpriteQuads.WriteQuad(Vertices, quad + 1, carry, new int2(ColonySprites.CarryWidth, ColonySprites.CarryHeight), up, facing,
                        Rects[ColonySprites.CarryRect], color, world);
                    if (a.CarryCount > 1 && !a.Possessed)
                    {
                        // A hauler's stack: a second lump on top.
                        float2 stacked = carry + new float2(up) * (2f / WorldConstants.PixelsPerUnit) - f * (1f / WorldConstants.PixelsPerUnit);
                        SpriteQuads.WriteQuad(Vertices, quad + 2, stacked, new int2(ColonySprites.CarryWidth, ColonySprites.CarryHeight), up, facing,
                            Rects[ColonySprites.CarryRect], color, world);
                    }
                }
                else SpriteQuads.WriteEmpty(Vertices, quad + 1);
            }
        }
    }
}
