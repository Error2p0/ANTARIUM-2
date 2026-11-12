using System;
using Antarium.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Antarium.Presentation
{
    /// <summary>
    /// Outlines the cells a painting tool's round brush covers at the cursor (Milestone 11): a solid one-pixel line along
    /// every edge between a covered and an uncovered cell, unlit, in the tool's colour. Covered means the
    /// cell's centre is within the radius, the same rule the simulation uses for digging and painting.
    /// </summary>
    public sealed class BrushCursorView : IDisposable
    {
        const float Ppu = WorldConstants.PixelsPerUnit;
        const int CellPx = WorldConstants.PixelsPerCell;
        static readonly float2 Unlit = new(0f, -20000f);
        static readonly float4 Pixel = new(0f, 0f, 1f, 1f);

        readonly SpriteMesh _mesh;
        readonly Texture2D _texture;

        public BrushCursorView(Material spriteMaterial, Transform parent, float2 worldSize)
        {
            _texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Point, name = "BrushCursor" };
            _texture.SetPixel(0, 0, Color.white);
            _texture.Apply(false, false);
            _mesh = new SpriteMesh("BrushCursor", spriteMaterial, _texture, parent, worldSize, 22);
        }

        public void Hide() => _mesh.Apply(0);

        /// <param name="center">Brush centre in cell space.</param>
        public void Render(float2 center, float radius, Color color)
        {
            Color32 c = TerrainView.ToWorking(color);
            c.a = 255; // a solid one-pixel line (a dithered one turned dotted or solid depending on where it fell)
            int r = (int)math.ceil(radius) + 1;
            int2 min = (int2)math.floor(center) - r, max = (int2)math.floor(center) + r;
            _mesh.EnsureCapacity((2 * r + 1) * (2 * r + 1) * 4);
            var v = _mesh.Vertices;
            int quad = 0;
            float cell = WorldConstants.CellSize, px = 1f / Ppu;
            for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
            {
                if (!Covered(x, y, center, radius)) continue;
                float2 at = new float2(x, y) * cell;
                if (!Covered(x - 1, y, center, radius)) SpriteQuads.WriteUpright(v, quad++, at, new int2(1, CellPx), Pixel, c, Unlit);
                if (!Covered(x + 1, y, center, radius)) SpriteQuads.WriteUpright(v, quad++, at + new float2(cell - px, 0f), new int2(1, CellPx), Pixel, c, Unlit);
                if (!Covered(x, y - 1, center, radius)) SpriteQuads.WriteUpright(v, quad++, at, new int2(CellPx, 1), Pixel, c, Unlit);
                if (!Covered(x, y + 1, center, radius)) SpriteQuads.WriteUpright(v, quad++, at + new float2(0f, cell - px), new int2(CellPx, 1), Pixel, c, Unlit);
            }
            _mesh.Apply(quad);
        }

        static bool Covered(int x, int y, float2 center, float radius) => math.distance(new float2(x + 0.5f, y + 0.5f), center) <= radius;

        public void Dispose()
        {
            _mesh.Dispose();
            if (_texture != null) UnityEngine.Object.Destroy(_texture);
        }
    }
}
