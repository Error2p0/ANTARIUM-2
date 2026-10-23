using System;
using System.Runtime.InteropServices;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Antarium.Presentation
{
    /// <summary>
    /// Mirrors the world grid, fluid plane and light map to GPU buffers (one 1024-cell range per dirty chunk)
    /// and draws the whole world as a single quad with the per-pixel terrain shader. No meshes per chunk.
    /// </summary>
    public sealed class TerrainView : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MaterialGpu
        {
            public Vector4 TopRim;
            public Vector4 EdgeLight;
            public Vector4 EdgeDark;
            public Vector4 Params;
            public Vector4 BaseColor;
        }

        static readonly int CellsId = Shader.PropertyToID("_AntCells");
        static readonly int LightId = Shader.PropertyToID("_AntLight");
        static readonly int MaterialsId = Shader.PropertyToID("_AntMaterials");
        static readonly int MaterialTexId = Shader.PropertyToID("_AntMaterialTex");
        static readonly int WorldId = Shader.PropertyToID("_AntWorld");
        static readonly int PixelId = Shader.PropertyToID("_AntPixel");
        static readonly int WallId = Shader.PropertyToID("_AntWall");
        static readonly int SkyTopId = Shader.PropertyToID("_AntSkyTop");
        static readonly int SkyHorizonId = Shader.PropertyToID("_AntSkyHorizon");
        static readonly int SkyRangeId = Shader.PropertyToID("_AntSkyRange");
        static readonly int OverlayId = Shader.PropertyToID("_AntOverlay");
        static readonly int SpriteMinLightId = Shader.PropertyToID("_AntSpriteMinLight");
        static readonly int StorageColorId = Shader.PropertyToID("_AntStorageColor");
        static readonly int FluidId = Shader.PropertyToID("_AntFluid");
        static readonly int WaterId = Shader.PropertyToID("_AntWater");
        static readonly int WaterSurfaceId = Shader.PropertyToID("_AntWaterSurface");
        static readonly int LavaId = Shader.PropertyToID("_AntLava");
        static readonly int LavaHotId = Shader.PropertyToID("_AntLavaHot");
        static readonly int GasId = Shader.PropertyToID("_AntGas");
        static readonly int TimeId = Shader.PropertyToID("_AntTime");
        static readonly int ScentId = Shader.PropertyToID("_AntScent");
        static readonly int ScentColorsId = Shader.PropertyToID("_AntScentColors");
        static readonly int ScentCoverId = Shader.PropertyToID("_AntScentCover");

        readonly WorldGrid _world;
        readonly LightSystem _light;
        readonly GraphicsBuffer _cellBuffer;
        readonly GraphicsBuffer _lightBuffer;
        /// <summary>Two cells per uint (each: level | kind &lt;&lt; 8), the fluid plane reinterpreted.</summary>
        readonly GraphicsBuffer _fluidBuffer;
        /// <summary>Two cells per uint (each: 2 bits per pheromone kind), the render plane of <see cref="PheromoneSystem"/>.</summary>
        readonly GraphicsBuffer _scentBuffer;
        readonly PheromoneSystem _pheromones;
        readonly RenderConfig _render;
        readonly GraphicsBuffer _materialBuffer;
        readonly Texture2DArray _materialTextures;
        readonly GameObject _quad;
        readonly Mesh _mesh;

        public TerrainView(WorldGrid world, LightSystem light, PheromoneSystem pheromones, ContentDB content, RenderConfig render,
            Material terrainMaterial, Transform parent)
        {
            _world = world;
            _light = light;
            _pheromones = pheromones;
            _render = render;
            int count = world.Width * world.Height;
            _cellBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, sizeof(uint)) { name = "AntCells" };
            _lightBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, sizeof(uint)) { name = "AntLight" };
            _fluidBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count / 2, sizeof(uint)) { name = "AntFluid" };
            _scentBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count / 2, sizeof(uint)) { name = "AntScent" };
            // New GPU buffers hold whatever memory they were given (often a previous session's): fill them all once.
            // After this only dirty chunks are uploaded.
            _cellBuffer.SetData(world.Cells);
            _lightBuffer.SetData(light.Light);
            _fluidBuffer.SetData(world.Fluid.Reinterpret<uint>(UnsafeFluidSize));
            _scentBuffer.SetData(pheromones.Render.Reinterpret<uint>(sizeof(ushort)));

            // Colours are authored in sRGB; buffers are raw floats, so convert to the working colour space here.
            var materials = content.Materials;
            var gpu = new MaterialGpu[materials.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                var m = materials[i];
                gpu[i] = new MaterialGpu
                {
                    TopRim = m.topRimColor.a > 0f ? ToWorking(new Color(m.topRimColor.r, m.topRimColor.g, m.topRimColor.b, 1f)) : Vector4.zero,
                    EdgeLight = ToWorking(Color.Lerp(m.highlightColor, Color.white, 0.15f)),
                    EdgeDark = ToWorking(Color.Lerp(m.shadowColor, Color.black, 0.3f)),
                    Params = new Vector4(m.IsAir ? 1f : 0f, m.selfGlow, 0f, 0f),
                    BaseColor = ToWorking(m.baseColor),
                };
            }
            _materialBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, gpu.Length, Marshal.SizeOf<MaterialGpu>()) { name = "AntMaterials" };
            _materialBuffer.SetData(gpu);
            _materialTextures = MaterialTextureBuilder.Build(materials);

            ApplyGlobals(content, render);

            _mesh = BuildQuad(world.Width * WorldConstants.CellSize, world.Height * WorldConstants.CellSize);
            _quad = new GameObject("Terrain");
            _quad.transform.SetParent(parent, false);
            _quad.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = _quad.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = terrainMaterial;
            renderer.sortingOrder = -100;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        void ApplyGlobals(ContentDB content, RenderConfig render)
        {
            Shader.SetGlobalBuffer(CellsId, _cellBuffer);
            Shader.SetGlobalBuffer(LightId, _lightBuffer);
            Shader.SetGlobalBuffer(FluidId, _fluidBuffer);
            Shader.SetGlobalBuffer(ScentId, _scentBuffer);
            var scentColors = new Vector4[8];
            for (int i = 0; i < Pheromones.Count && render.pheromoneColors != null && i < render.pheromoneColors.Length; i++)
                scentColors[i] = ToWorking(render.pheromoneColors[i]);
            Shader.SetGlobalVectorArray(ScentColorsId, scentColors);
            SetScentCover(false);
            Shader.SetGlobalColor(WaterId, ToWorkingAlpha(render.waterColor));
            Shader.SetGlobalColor(WaterSurfaceId, ToWorkingAlpha(render.waterSurfaceColor));
            Shader.SetGlobalColor(LavaId, ToWorkingAlpha(render.lavaColor));
            Shader.SetGlobalColor(LavaHotId, ToWorkingAlpha(render.lavaHotColor));
            Shader.SetGlobalColor(GasId, ToWorkingAlpha(render.gasColor));
            Shader.SetGlobalBuffer(MaterialsId, _materialBuffer);
            Shader.SetGlobalTexture(MaterialTexId, _materialTextures);
            Shader.SetGlobalVector(WorldId, new Vector4(_world.Width, _world.Height, _world.ChunksX, 0f));
            Shader.SetGlobalVector(PixelId, new Vector4(WorldConstants.PixelsPerUnit, WorldConstants.PixelsPerCell, render.lightBands, render.edgeNoise));
            float gamma = IsLinear ? 2.2f : 1f;
            Shader.SetGlobalVector(WallId, new Vector4(render.wallBrightness, render.wallOcclusion, gamma, render.wallContrast));
            Shader.SetGlobalVector(SpriteMinLightId, new Vector4(render.spriteMinLight, 0f, 0f, 0f));
            Shader.SetGlobalColor(OverlayId, render.miningOrderColor);
            Shader.SetGlobalColor(StorageColorId, render.storageColor);
            Shader.SetGlobalColor(SkyTopId, render.skyTop);
            Shader.SetGlobalColor(SkyHorizonId, render.skyHorizon);
            float horizon = (_world.Height - content.WorldGen.skyHeight) * WorldConstants.CellSize;
            Shader.SetGlobalVector(SkyRangeId, new Vector4(horizon, _world.Height * WorldConstants.CellSize, 0f, 0f));
        }

        /// <summary>Pheromones are drawn faintly, or clearly while the Pheromone tool is active.</summary>
        public void SetScentCover(bool toolActive) =>
            Shader.SetGlobalFloat(ScentCoverId, toolActive ? _render.pheromoneToolCover : _render.pheromoneCover);

        /// <summary>Uploads chunks whose cells, fluid, pheromones or light changed since the last call.</summary>
        public int Upload()
        {
            const ChunkFlags any = ChunkFlags.TerrainUpload | ChunkFlags.LightUpload | ChunkFlags.FluidUpload | ChunkFlags.PheromoneUpload;
            Shader.SetGlobalFloat(TimeId, Time.time);
            int uploaded = 0;
            var chunks = _world.Chunks;
            var fluid = _world.Fluid.Reinterpret<uint>(UnsafeFluidSize);
            var scent = _pheromones.Render.Reinterpret<uint>(sizeof(ushort));
            for (int i = 0; i < chunks.Length; i++)
            {
                var flags = chunks[i];
                if ((flags & any) == 0) continue;
                int start = i * WorldConstants.ChunkCellCount;
                if ((flags & ChunkFlags.TerrainUpload) != 0)
                    _cellBuffer.SetData(_world.Cells, start, start, WorldConstants.ChunkCellCount);
                if ((flags & ChunkFlags.LightUpload) != 0)
                    _lightBuffer.SetData(_light.Light, start, start, WorldConstants.ChunkCellCount);
                if ((flags & ChunkFlags.FluidUpload) != 0)
                    _fluidBuffer.SetData(fluid, start / 2, start / 2, WorldConstants.ChunkCellCount / 2);
                if ((flags & ChunkFlags.PheromoneUpload) != 0)
                    _scentBuffer.SetData(scent, start / 2, start / 2, WorldConstants.ChunkCellCount / 2);
                chunks[i] = flags & ~any;
                uploaded++;
            }
            return uploaded;
        }

        /// <summary>Size of <see cref="FluidCell"/> in bytes (two of them pack into one GPU uint).</summary>
        const int UnsafeFluidSize = 2;

        static Color ToWorkingAlpha(Color c)
        {
            var w = ToWorking(new Color(c.r, c.g, c.b, 1f));
            w.a = c.a;
            return w;
        }

        static bool IsLinear => QualitySettings.activeColorSpace == ColorSpace.Linear;

        /// <summary>sRGB colour to the working colour space, for data written to buffers or vertex colours.</summary>
        public static Color ToWorking(Color c) => IsLinear ? c.linear : c;

        static Mesh BuildQuad(float width, float height)
        {
            var mesh = new Mesh { name = "WorldQuad" };
            mesh.SetVertices(new[] { new Vector3(0, 0), new Vector3(width, 0), new Vector3(0, height), new Vector3(width, height) });
            mesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            mesh.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            mesh.bounds = new Bounds(new Vector3(width / 2, height / 2), new Vector3(width, height, 1f));
            return mesh;
        }

        public void Dispose()
        {
            _cellBuffer?.Release();
            _lightBuffer?.Release();
            _fluidBuffer?.Release();
            _scentBuffer?.Release();
            _materialBuffer?.Release();
            if (_materialTextures != null) UnityEngine.Object.Destroy(_materialTextures);
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_quad != null) UnityEngine.Object.Destroy(_quad);
        }
    }
}
