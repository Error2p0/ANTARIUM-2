using System;
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
    public sealed class ColonyView : IDisposable
    {
        /// <summary>Body, carried item (or idle marker), health bar back and front.</summary>
        const int QuadsPerAnt = 4;
        const int QuadsPerEnemy = 3;
        const float FlashSeconds = 0.15f;

        readonly ContentDB _content;
        readonly ColonySprites _sprites;
        readonly SpriteMesh _mesh;
        readonly NativeArray<Color32> _itemColors;
        readonly Color32 _idleTint;
        readonly BarColors _bars;
        static readonly Color32 SparkleTint = new(255, 230, 120, 255);
        static readonly Color32 AlertTint = new(255, 150, 70, 255);
        static readonly Color32 White = new(255, 255, 255, 255);

        struct BarColors
        {
            public Color32 Ant, Enemy, Back, Flash;
        }

        public ColonyView(ContentDB content, RenderConfig render, Material spriteMaterial, Transform parent, float2 worldSize)
        {
            _content = content;
            _sprites = new ColonySprites();
            _mesh = new SpriteMesh("Colony", spriteMaterial, _sprites.Texture, parent, worldSize, 10);
            _itemColors = ItemColors(content);
            _idleTint = TerrainView.ToWorking(render.overlayIdleAntColor);
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
            int quads = antCount * QuadsPerAnt + 1 + colony.Eggs.Count + colony.Piles.Count + events.Critters.Count * 2 + events.Active.Count
                        + enemies.Count * QuadsPerEnemy;
            _mesh.EnsureCapacity(quads);
            var vertices = _mesh.Vertices;

            new AntQuadJob
            {
                Ants = sim.Ants.Ants.AsArray(),
                Nav = new NavGrid(sim.World),
                Rects = _sprites.Rects,
                ItemColors = _itemColors,
                Vertices = vertices,
                Alpha = alpha,
                Overlay = overlay,
                IdleTint = _idleTint,
                Bars = _bars,
                WorkerMax = sim.Ants.MaxHealth(AntCaste.Worker),
                SoldierMax = sim.Ants.MaxHealth(AntCaste.Soldier),
            }.Schedule(antCount, 64).Complete();

            int quad = antCount * QuadsPerAnt;
            var rects = _sprites.Rects;

            WriteOnFloor(vertices, colony.QueenCell, ColonySprites.QueenWidth, ColonySprites.QueenHeight, rects[ColonySprites.QueenRect], White, quad++);
            foreach (var egg in colony.Eggs)
                WriteOnFloor(vertices, egg.Cell, ColonySprites.EggWidth, ColonySprites.EggHeight, rects[ColonySprites.EggRect], White, quad++);
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
                var center = Body(nav, cellPos, c.Heading, ColonySprites.BeetleHeight, out int2 up, out int facing, out float2 world);
                int frame = c.IsMoving ? (int)(c.WalkPhase * 4f) & 1 : 0;
                SpriteQuads.WriteQuad(vertices, quad++, center, new int2(ColonySprites.BeetleWidth, ColonySprites.BeetleHeight), up, facing,
                    rects[ColonySprites.BeetleRect + frame], White, world);
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
                SpriteQuads.WriteQuad(vertices, quad++, center, size, up, facing, rects[ColonySprites.EnemyRect + art * 2 + frame], tint, world);
                WriteBar(vertices, quad, rects[ColonySprites.PixelRect], center + new float2(0f, (size.y * 0.5f + 2f) / WorldConstants.PixelsPerUnit),
                    e.Health / math.max(1f, def.health), _bars.Back, _bars.Enemy, world);
                quad += 2;
            }
            _mesh.Apply(quad);
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
            _sprites.Dispose();
        }

        [BurstCompile]
        struct AntQuadJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Ant> Ants;
            public NavGrid Nav;
            [ReadOnly] public NativeArray<float4> Rects;
            [ReadOnly] public NativeArray<Color32> ItemColors;
            [NativeDisableParallelForRestriction] public NativeArray<SpriteVertex> Vertices;
            public float Alpha;
            public bool Overlay;
            public Color32 IdleTint;
            public BarColors Bars;
            public float WorkerMax;
            public float SoldierMax;

            public void Execute(int index)
            {
                var a = Ants[index];
                int quad = index * QuadsPerAnt;
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
                if (a.State == AntState.Digging || a.State == AntState.Building || a.State == AntState.Fighting) frame = (int)(a.WalkPhase * 2f) & 1;
                else if (a.IsMoving) frame = (int)(a.WalkPhase * 4f) & 3;
                else frame = 0;

                bool soldier = a.IsSoldier;
                int2 size = soldier ? new int2(ColonySprites.SoldierWidth, ColonySprites.SoldierHeight) : new int2(ColonySprites.WorkerWidth, ColonySprites.WorkerHeight);
                int rect = soldier ? ColonySprites.SoldierRect + frame : frame;
                float2 world = cellPos * WorldConstants.CellSize;
                float2 center = SpriteQuads.OnSurface(world, up, size.y);
                var tint = a.SinceHurt < FlashSeconds ? Bars.Flash : new Color32(255, 255, 255, 255);
                SpriteQuads.WriteQuad(Vertices, quad, center, size, up, facing, Rects[rect], tint, world);

                float max = soldier ? SoldierMax : WorkerMax;
                WriteBar(Vertices, quad + 2, Rects[ColonySprites.PixelRect], center + new float2(0f, (size.y * 0.5f + 2f) / WorldConstants.PixelsPerUnit),
                    a.Health / math.max(1f, max), Bars.Back, Bars.Ant, world);

                bool idle = !soldier && (a.State == AntState.Idle || (a.State == AntState.Wander && !a.IsCarrying));
                if (Overlay && idle)
                {
                    // Idle marker: a dot floating above the ant (uses the carried-item slot, which is empty).
                    float2 marker = center + new float2(up) * ((size.y * 0.5f + 3f) / WorldConstants.PixelsPerUnit);
                    SpriteQuads.WriteQuad(Vertices, quad + 1, marker, new int2(ColonySprites.MarkerSize), up, facing,
                        Rects[ColonySprites.MarkerRect], IdleTint, world);
                    return;
                }

                if (a.IsCarrying)
                {
                    float2 f = new float2(forwardAxis) * facing;
                    float2 carry = center + f * ((size.x * 0.5f) / WorldConstants.PixelsPerUnit)
                                   + new float2(up) * (2f / WorldConstants.PixelsPerUnit);
                    SpriteQuads.WriteQuad(Vertices, quad + 1, carry, new int2(ColonySprites.CarryWidth, ColonySprites.CarryHeight), up, facing,
                        Rects[ColonySprites.CarryRect], ItemColors[a.Carry], world);
                }
                else SpriteQuads.WriteEmpty(Vertices, quad + 1);
            }
        }
    }
}
