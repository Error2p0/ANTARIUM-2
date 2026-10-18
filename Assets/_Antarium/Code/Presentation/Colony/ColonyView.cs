using System;
using System.Runtime.InteropServices;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Antarium.Presentation
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SpriteVertex
    {
        // Field order must follow the VertexAttribute enum order (Position, Color, TexCoord0, TexCoord1).
        public float3 Position;
        public Color32 Color;
        public float2 UV;
        public float2 LightPos;
    }

    /// <summary>
    /// Draws every ant, the queen, eggs and item piles as quads in one dynamic mesh (one draw call).
    /// Ant quads are built by a Burst job. Sprites snap to the art-pixel grid and rotate in 90° steps to
    /// follow the surface an ant clings to, so pixels stay crisp.
    /// </summary>
    public sealed class ColonyView : IDisposable
    {
        const int QuadsPerAnt = 2; // body + carried item

        static readonly VertexAttributeDescriptor[] Layout =
        {
            new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2),
        };

        readonly ColonySprites _sprites;
        readonly Material _material;
        readonly Mesh _mesh;
        readonly GameObject _go;
        readonly Bounds _bounds;
        readonly NativeArray<Color32> _itemColors;
        NativeArray<SpriteVertex> _vertices;
        int _quadCapacity;

        public ColonyView(ContentDB content, Material spriteMaterial, Transform parent, float2 worldSize)
        {
            _sprites = new ColonySprites();
            _material = new Material(spriteMaterial) { mainTexture = _sprites.Texture };

            _itemColors = new NativeArray<Color32>(math.max(1, content.Items.Length), Allocator.Persistent);
            for (int i = 0; i < content.Items.Length; i++)
                _itemColors[i] = TerrainView.ToWorking(content.Items[i].color);

            _mesh = new Mesh { name = "ColonySprites" };
            _mesh.MarkDynamic();
            _bounds = new Bounds(new Vector3(worldSize.x / 2, worldSize.y / 2), new Vector3(worldSize.x, worldSize.y, 1f));

            _go = new GameObject("Colony");
            _go.transform.SetParent(parent, false);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = _go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.sortingOrder = 10;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            EnsureCapacity(256);
        }

        public void Render(Simulation sim, float alpha)
        {
            var colony = sim.Colony;
            int antCount = sim.Ants.Count;
            int quads = antCount * QuadsPerAnt + 1 + colony.Eggs.Count + colony.Piles.Count;
            EnsureCapacity(quads);

            new AntQuadJob
            {
                Ants = sim.Ants.Ants.AsArray(),
                Nav = new NavGrid(sim.World),
                Rects = _sprites.Rects,
                ItemColors = _itemColors,
                Vertices = _vertices,
                Alpha = alpha,
            }.Schedule(antCount, 64).Complete();

            int quad = antCount * QuadsPerAnt;
            var white = new Color32(255, 255, 255, 255);
            var rects = _sprites.Rects;

            WriteOnFloor(colony.QueenCell, ColonySprites.QueenWidth, ColonySprites.QueenHeight, rects[ColonySprites.QueenRect], white, quad++);
            foreach (var egg in colony.Eggs)
                WriteOnFloor(egg.Cell, ColonySprites.EggWidth, ColonySprites.EggHeight, rects[ColonySprites.EggRect], white, quad++);
            for (int i = 0; i < colony.Piles.Count; i++)
            {
                var pile = colony.Piles[i];
                WriteOnFloor(pile.Cell, ColonySprites.PileWidth, ColonySprites.PileHeight, rects[ColonySprites.PileRect], _itemColors[pile.Item], quad++);
            }

            _mesh.SetVertexBufferData(_vertices, 0, 0, quads * 4, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            _mesh.SetSubMesh(0, new SubMeshDescriptor(0, quads * 6) { bounds = _bounds, vertexCount = quads * 4 },
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            _mesh.bounds = _bounds;
        }

        void WriteOnFloor(int2 cell, int w, int h, float4 rect, Color32 color, int quad)
        {
            float2 center = WorldGrid.CellCenterToWorld(cell);
            Quads.WriteQuad(_vertices, quad, Quads.OnSurface(center, new int2(0, 1), h), new int2(w, h), new int2(0, 1), 1, rect, color, center);
        }

        void EnsureCapacity(int quads)
        {
            if (quads <= _quadCapacity) return;
            int capacity = math.max(quads, _quadCapacity * 2);
            if (_vertices.IsCreated) _vertices.Dispose();
            _vertices = new NativeArray<SpriteVertex>(capacity * 4, Allocator.Persistent);

            var indices = new NativeArray<uint>(capacity * 6, Allocator.Temp);
            for (int q = 0; q < capacity; q++)
            {
                uint v = (uint)(q * 4);
                indices[q * 6 + 0] = v;
                indices[q * 6 + 1] = v + 2;
                indices[q * 6 + 2] = v + 1;
                indices[q * 6 + 3] = v + 1;
                indices[q * 6 + 4] = v + 2;
                indices[q * 6 + 5] = v + 3;
            }
            _mesh.SetVertexBufferParams(capacity * 4, Layout);
            _mesh.SetIndexBufferParams(capacity * 6, IndexFormat.UInt32);
            _mesh.SetIndexBufferData(indices, 0, 0, indices.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            _mesh.subMeshCount = 1;
            indices.Dispose();
            _quadCapacity = capacity;
        }

        public void Dispose()
        {
            if (_vertices.IsCreated) _vertices.Dispose();
            if (_itemColors.IsCreated) _itemColors.Dispose();
            _sprites.Dispose();
            if (_material != null) UnityEngine.Object.Destroy(_material);
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_go != null) UnityEngine.Object.Destroy(_go);
        }

        /// <summary>Pixel-snapped, surface-oriented quad helpers shared by the job and the main thread.</summary>
        static class Quads
        {
            const float Ppu = WorldConstants.PixelsPerUnit;

            /// <summary>Centre of a sprite of <paramref name="heightPx"/> standing on the surface below <paramref name="up"/>.</summary>
            public static float2 OnSurface(float2 cellCenter, int2 up, int heightPx) =>
                cellCenter - new float2(up) * (WorldConstants.CellSize * 0.5f - heightPx * 0.5f / Ppu);

            /// <summary>
            /// Writes a quad whose sprite "up" points along <paramref name="up"/> and "forward" along
            /// <paramref name="facing"/> × perpendicular(up). The covered rect is snapped to whole art pixels.
            /// </summary>
            public static void WriteQuad(NativeArray<SpriteVertex> v, int quad, float2 center, int2 sizePx, int2 up, int facing,
                float4 rect, Color32 color, float2 lightPos)
            {
                float2 u = up;
                float2 f = new float2(up.y, -up.x) * facing;
                float2 ext = math.abs(f) * sizePx.x + math.abs(u) * sizePx.y;
                float2 min = math.round(center * Ppu - ext * 0.5f);
                float2 c = (min + ext * 0.5f) / Ppu;
                float2 hw = f * (sizePx.x * 0.5f / Ppu);
                float2 hh = u * (sizePx.y * 0.5f / Ppu);
                int i = quad * 4;
                v[i + 0] = Vertex(c - hw - hh, new float2(rect.x, rect.y), color, lightPos);
                v[i + 1] = Vertex(c + hw - hh, new float2(rect.z, rect.y), color, lightPos);
                v[i + 2] = Vertex(c - hw + hh, new float2(rect.x, rect.w), color, lightPos);
                v[i + 3] = Vertex(c + hw + hh, new float2(rect.z, rect.w), color, lightPos);
            }

            public static void WriteEmpty(NativeArray<SpriteVertex> v, int quad)
            {
                int i = quad * 4;
                v[i] = v[i + 1] = v[i + 2] = v[i + 3] = default;
            }

            static SpriteVertex Vertex(float2 p, float2 uv, Color32 color, float2 lightPos) =>
                new() { Position = new float3(p, 0f), UV = uv, Color = color, LightPos = lightPos };
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
                if (a.State == AntState.Digging) frame = (int)(a.WalkPhase * 2f) & 1;
                else if (a.IsMoving) frame = (int)(a.WalkPhase * 4f) & 3;
                else frame = 0;

                float2 world = cellPos * WorldConstants.CellSize;
                float2 center = Quads.OnSurface(world, up, ColonySprites.WorkerHeight);
                var white = new Color32(255, 255, 255, 255);
                int quad = index * QuadsPerAnt;
                Quads.WriteQuad(Vertices, quad, center, new int2(ColonySprites.WorkerWidth, ColonySprites.WorkerHeight), up, facing,
                    Rects[frame], white, world);

                if (a.IsCarrying)
                {
                    float2 f = new float2(forwardAxis) * facing;
                    float2 carry = center + f * ((ColonySprites.WorkerWidth * 0.5f) / WorldConstants.PixelsPerUnit)
                                   + new float2(up) * (2f / WorldConstants.PixelsPerUnit);
                    Quads.WriteQuad(Vertices, quad + 1, carry, new int2(ColonySprites.CarryWidth, ColonySprites.CarryHeight), up, facing,
                        Rects[ColonySprites.CarryRect], ItemColors[a.Carry], world);
                }
                else Quads.WriteEmpty(Vertices, quad + 1);
            }
        }
    }
}
