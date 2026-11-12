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
            /// <summary>Colour of the find it drops for the ore overlay; w = 1 if it is a find.</summary>
            public Vector4 OreColor;
        }

        /// <summary>What the terrain shader draws over the world (10.7); the flow overlay is drawn by the building and colony views.</summary>
        public enum Overlay { None = 0, Light = 1, Ores = 2 }

        static readonly int CellsId = Shader.PropertyToID("_AntCells");
        static readonly int LightId = Shader.PropertyToID("_AntLight");
        static readonly int MaterialsId = Shader.PropertyToID("_AntMaterials");
        static readonly int MaterialTexId = Shader.PropertyToID("_AntMaterialTex");
        static readonly int WorldId = Shader.PropertyToID("_AntWorld");
        static readonly int PixelId = Shader.PropertyToID("_AntPixel");
        static readonly int WallId = Shader.PropertyToID("_AntWall");
        static readonly int TileId = Shader.PropertyToID("_AntTile");
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
        static readonly int SmokeId = Shader.PropertyToID("_AntSmoke");
        static readonly int FireId = Shader.PropertyToID("_AntFire");
        static readonly int FireHotId = Shader.PropertyToID("_AntFireHot");
        static readonly int EmberId = Shader.PropertyToID("_AntEmber");
        static readonly int GasId = Shader.PropertyToID("_AntGas");
        static readonly int TimeId = Shader.PropertyToID("_AntTime");
        static readonly int ScentId = Shader.PropertyToID("_AntScent");
        static readonly int ScentColorsId = Shader.PropertyToID("_AntScentColors");
        static readonly int ScentCoverId = Shader.PropertyToID("_AntScentCover");
        static readonly int SkyLightId = Shader.PropertyToID("_AntSkyLight");
        static readonly int StarsId = Shader.PropertyToID("_AntStars");
        static readonly int ViewId = Shader.PropertyToID("_AntView");
        static readonly int LightRampId = Shader.PropertyToID("_AntLightRamp");
        float _rain;

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
                    OreColor = OreColorOf(content, (ushort)i),
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
            Shader.SetGlobalColor(SmokeId, ToWorkingAlpha(render.smokeColor));
            Shader.SetGlobalColor(FireId, ToWorkingAlpha(render.fireColor));
            Shader.SetGlobalColor(FireHotId, ToWorkingAlpha(render.fireHotColor));
            Shader.SetGlobalColor(EmberId, ToWorkingAlpha(render.emberColor));
            Shader.SetGlobalColor(GasId, ToWorkingAlpha(render.gasColor));
            Shader.SetGlobalBuffer(MaterialsId, _materialBuffer);
            Shader.SetGlobalTexture(MaterialTexId, _materialTextures);
            Shader.SetGlobalVector(WorldId, new Vector4(_world.Width, _world.Height, _world.ChunksX, 0f));
            Shader.SetGlobalVector(PixelId, new Vector4(WorldConstants.PixelsPerUnit, WorldConstants.PixelsPerCell, render.lightBands, render.edgeNoise));
            float gamma = IsLinear ? 2.2f : 1f;
            Shader.SetGlobalVector(WallId, new Vector4(render.wallBrightness, render.wallOcclusion, gamma, render.wallContrast));
            Shader.SetGlobalVector(TileId, new Vector4(WorldConstants.MaterialTextureSize - 1, render.patchContrast, render.patchTint, 0f));
            Shader.SetGlobalVector(SpriteMinLightId, new Vector4(render.spriteMinLight, 0f, 0f, 0f));
            Shader.SetGlobalColor(OverlayId, render.miningOrderColor);
            Shader.SetGlobalColor(StorageColorId, render.storageColor);
            Shader.SetGlobalColor(SkyTopId, render.skyTop);
            Shader.SetGlobalColor(SkyHorizonId, render.skyHorizon);
            Shader.SetGlobalVector(SkyLightId, Vector4.one);
            Shader.SetGlobalVector(StarsId, Vector4.zero);
            var ramp = new Vector4[8];
            int bands = render.lightOverlayRamp != null ? Mathf.Min(render.lightOverlayRamp.Length, ramp.Length) : 0;
            for (int i = 0; i < bands; i++) ramp[i] = ToWorking(render.lightOverlayRamp[i]);
            Shader.SetGlobalVectorArray(LightRampId, ramp);
            _rampSize = Mathf.Max(1, bands);
            SetOverlay(Overlay.None);
            float horizon = (_world.Height - content.WorldGen.skyHeight) * WorldConstants.CellSize;
            Shader.SetGlobalVector(SkyRangeId, new Vector4(horizon, _world.Height * WorldConstants.CellSize, 0f, 0f));
        }

        /// <summary>
        /// Sky colours, sky light and stars for the time of day and the weather (rain greys the sky and dims the light; it
        /// fades in and out over a few seconds); <paramref name="flash"/> (0..1, a lightning bolt) lights sky and ground up
        /// for a moment. Also sets the light map's sky scale for particles.
        /// </summary>
        public void SetSky(SurfaceState surface, Color daySkyLight, float dt, float flash = 0f)
        {
            float d = surface.Daylight;
            float target = surface.Weather == WeatherKind.Rain ? 1f : 0f;
            _rain = Mathf.MoveTowards(_rain, target, dt / 4f);
            var r = _render;
            float twilight = 1f - Mathf.Abs(2f * d - 1f); // 1 halfway through a sunrise or sunset
            var top = Color.Lerp(r.nightSkyTop, r.skyTop, d);
            var horizon = Color.Lerp(Color.Lerp(r.nightSkyHorizon, r.skyHorizon, d), r.duskSkyHorizon, twilight * 0.85f);
            top = Color.Lerp(top, r.duskSkyHorizon * 0.6f, twilight * 0.25f);
            float grey = _rain * 0.65f;
            top = Color.Lerp(top, r.rainSky * Mathf.Lerp(0.25f, 1f, d), grey);
            horizon = Color.Lerp(horizon, r.rainSky * Mathf.Lerp(0.3f, 1.1f, d), grey);
            float bolt = Mathf.Clamp01(flash * r.lightningFlash);
            top = Color.Lerp(top, r.lightningColor * 0.75f, bolt * 0.55f);
            horizon = Color.Lerp(horizon, r.lightningColor * 0.9f, bolt * 0.65f);
            Shader.SetGlobalColor(SkyTopId, top);
            Shader.SetGlobalColor(SkyHorizonId, horizon);

            var night = r.nightLight * r.nightLightStrength;
            var sky = Color.Lerp(night, daySkyLight, d) * Mathf.Lerp(1f, r.rainLight, _rain);
            sky = Color.Lerp(sky, r.lightningColor, bolt * 0.7f);
            Shader.SetGlobalVector(SkyLightId, new Vector4(sky.r, sky.g, sky.b, 1f));
            float stars = Mathf.Clamp01(1f - d * 2f) * (1f - _rain);
            Shader.SetGlobalVector(StarsId, new Vector4(r.starColor.r, r.starColor.g, r.starColor.b, stars));
            _light.SkyScale = Mathf.Lerp(Mathf.Lerp(r.nightLightStrength, 1f, d) * Mathf.Lerp(1f, r.rainLight, _rain), 1f, bolt * 0.7f);
        }

        int _rampSize = 1;

        /// <summary>The light or ore overlay over the terrain, or none.</summary>
        public void SetOverlay(Overlay overlay) =>
            Shader.SetGlobalVector(ViewId, new Vector4((int)overlay, _render.oreOverlayDim, _rampSize, 0f));

        /// <summary>The item colour of a find (a material dropping a non-food item), w = 1; zero for anything else.</summary>
        static Vector4 OreColorOf(ContentDB content, ushort material)
        {
            ushort item = OreSurvey.FindItem(content, material);
            if (item == ContentDB.NoItem) return Vector4.zero;
            var c = ToWorking(content.Items[item].MapColor);
            return new Vector4(c.r, c.g, c.b, 1f);
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
