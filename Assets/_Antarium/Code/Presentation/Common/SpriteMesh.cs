using System;
using System.Runtime.InteropServices;
using Antarium.Core;
using Unity.Collections;
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
    /// One dynamic mesh of pixel-sprite quads drawn with the ColonySprite shader (one draw call). Callers
    /// fill <see cref="Vertices"/> (4 per quad, see <see cref="SpriteQuads"/>) and then call <see cref="Apply"/>.
    /// </summary>
    public sealed class SpriteMesh : IDisposable
    {
        static readonly VertexAttributeDescriptor[] Layout =
        {
            new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2),
        };

        readonly Mesh _mesh;
        readonly GameObject _go;
        readonly Material _material;
        readonly Bounds _bounds;
        public NativeArray<SpriteVertex> Vertices;
        int _quadCapacity;

        public SpriteMesh(string name, Material spriteMaterial, Texture atlas, Transform parent, float2 worldSize, int sortingOrder)
        {
            _material = new Material(spriteMaterial) { mainTexture = atlas };
            _mesh = new Mesh { name = name };
            _mesh.MarkDynamic();
            _bounds = new Bounds(new Vector3(worldSize.x / 2, worldSize.y / 2), new Vector3(worldSize.x, worldSize.y, 1f));

            _go = new GameObject(name);
            _go.transform.SetParent(parent, false);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = _go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.sortingOrder = sortingOrder;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            EnsureCapacity(256);
        }

        /// <summary>Grows the vertex buffer to hold at least <paramref name="quads"/> quads. Existing vertices are discarded.</summary>
        public void EnsureCapacity(int quads)
        {
            if (quads <= _quadCapacity) return;
            int capacity = math.max(quads, _quadCapacity * 2);
            if (Vertices.IsCreated) Vertices.Dispose();
            Vertices = new NativeArray<SpriteVertex>(capacity * 4, Allocator.Persistent);

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

        /// <summary>Uploads the first <paramref name="quads"/> quads.</summary>
        public void Apply(int quads)
        {
            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            _mesh.SetVertexBufferData(Vertices, 0, 0, quads * 4, 0, flags);
            _mesh.SetSubMesh(0, new SubMeshDescriptor(0, quads * 6) { bounds = _bounds, vertexCount = quads * 4 }, flags);
            _mesh.bounds = _bounds;
        }

        public void Dispose()
        {
            if (Vertices.IsCreated) Vertices.Dispose();
            if (_material != null) UnityEngine.Object.Destroy(_material);
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_go != null) UnityEngine.Object.Destroy(_go);
        }
    }

    /// <summary>Pixel-snapped, surface-oriented quad helpers, usable from Burst jobs and the main thread.</summary>
    public static class SpriteQuads
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

        /// <summary>Upright quad with its bottom-left corner at a world position (already on the pixel grid).</summary>
        public static void WriteUpright(NativeArray<SpriteVertex> v, int quad, float2 bottomLeft, int2 sizePx, float4 rect, Color32 color, float2 lightPos)
        {
            float2 size = new float2(sizePx) / Ppu;
            WriteQuad(v, quad, bottomLeft + size * 0.5f, sizePx, new int2(0, 1), 1, rect, color, lightPos);
        }

        public static void WriteEmpty(NativeArray<SpriteVertex> v, int quad)
        {
            int i = quad * 4;
            v[i] = v[i + 1] = v[i + 2] = v[i + 3] = default;
        }

        static SpriteVertex Vertex(float2 p, float2 uv, Color32 color, float2 lightPos) =>
            new() { Position = new float3(p, 0f), UV = uv, Color = color, LightPos = lightPos };
    }
}
