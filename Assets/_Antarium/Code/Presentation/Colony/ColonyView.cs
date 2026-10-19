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
    /// Draws every ant, the queen, eggs and item piles as quads in one dynamic mesh (one draw call).
    /// Ant quads are built by a Burst job. Sprites snap to the art-pixel grid and rotate in 90° steps to
    /// follow the surface an ant clings to, so pixels stay crisp.
    /// </summary>
    public sealed class ColonyView : IDisposable
    {
        const int QuadsPerAnt = 2; // body + carried item

        readonly ColonySprites _sprites;
        readonly SpriteMesh _mesh;
        readonly NativeArray<Color32> _itemColors;
        readonly Color32 _idleTint;

        public ColonyView(ContentDB content, RenderConfig render, Material spriteMaterial, Transform parent, float2 worldSize)
        {
            _sprites = new ColonySprites();
            _mesh = new SpriteMesh("Colony", spriteMaterial, _sprites.Texture, parent, worldSize, 10);
            _itemColors = ItemColors(content);
            _idleTint = TerrainView.ToWorking(render.overlayIdleAntColor);
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
        public void Render(Simulation sim, float alpha, bool overlay)
        {
            var colony = sim.Colony;
            int antCount = sim.Ants.Count;
            int quads = antCount * QuadsPerAnt + 1 + colony.Eggs.Count + colony.Piles.Count;
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
            }.Schedule(antCount, 64).Complete();

            int quad = antCount * QuadsPerAnt;
            var white = new Color32(255, 255, 255, 255);
            var rects = _sprites.Rects;

            WriteOnFloor(vertices, colony.QueenCell, ColonySprites.QueenWidth, ColonySprites.QueenHeight, rects[ColonySprites.QueenRect], white, quad++);
            foreach (var egg in colony.Eggs)
                WriteOnFloor(vertices, egg.Cell, ColonySprites.EggWidth, ColonySprites.EggHeight, rects[ColonySprites.EggRect], white, quad++);
            for (int i = 0; i < colony.Piles.Count; i++)
            {
                var pile = colony.Piles[i];
                WriteOnFloor(vertices, pile.Cell, ColonySprites.PileWidth, ColonySprites.PileHeight, rects[ColonySprites.PileRect], _itemColors[pile.Item], quad++);
            }
            _mesh.Apply(quads);
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

            public void Execute(int index)
            {
                var a = Ants[index];
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
                if (a.State == AntState.Digging || a.State == AntState.Building) frame = (int)(a.WalkPhase * 2f) & 1;
                else if (a.IsMoving) frame = (int)(a.WalkPhase * 4f) & 3;
                else frame = 0;

                float2 world = cellPos * WorldConstants.CellSize;
                float2 center = SpriteQuads.OnSurface(world, up, ColonySprites.WorkerHeight);
                int quad = index * QuadsPerAnt;
                SpriteQuads.WriteQuad(Vertices, quad, center, new int2(ColonySprites.WorkerWidth, ColonySprites.WorkerHeight), up, facing,
                    Rects[frame], new Color32(255, 255, 255, 255), world);

                bool idle = a.State == AntState.Idle || (a.State == AntState.Wander && !a.IsCarrying);
                if (Overlay && idle)
                {
                    // Idle marker: a dot floating above the ant (uses the carried-item slot, which is empty).
                    float2 marker = center + new float2(up) * ((ColonySprites.WorkerHeight * 0.5f + 3f) / WorldConstants.PixelsPerUnit);
                    SpriteQuads.WriteQuad(Vertices, quad + 1, marker, new int2(ColonySprites.MarkerSize), up, facing,
                        Rects[ColonySprites.MarkerRect], IdleTint, world);
                    return;
                }

                if (a.IsCarrying)
                {
                    float2 f = new float2(forwardAxis) * facing;
                    float2 carry = center + f * ((ColonySprites.WorkerWidth * 0.5f) / WorldConstants.PixelsPerUnit)
                                   + new float2(up) * (2f / WorldConstants.PixelsPerUnit);
                    SpriteQuads.WriteQuad(Vertices, quad + 1, carry, new int2(ColonySprites.CarryWidth, ColonySprites.CarryHeight), up, facing,
                        Rects[ColonySprites.CarryRect], ItemColors[a.Carry], world);
                }
                else SpriteQuads.WriteEmpty(Vertices, quad + 1);
            }
        }
    }
}
