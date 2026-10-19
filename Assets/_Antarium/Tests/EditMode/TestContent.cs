using System;
using System.Collections.Generic;
using Antarium.Content;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Antarium.Tests
{
    /// <summary>In-memory content for tests: a small world with dirt over stone, a glow crystal and bedrock.</summary>
    public sealed class TestContent : IDisposable
    {
        public readonly TerrainMaterialDef Air, Grass, Dirt, Stone, Crystal, Bedrock;
        public readonly BiomeDef Topsoil, Deep;
        public readonly ItemDef Ore, Seed, Bar;
        public readonly BuildingDef Belt, Lift, Splitter, Filter, Smelter;
        public readonly RecipeDef Smelt;
        public readonly WorldGenSettings WorldGen;
        public readonly GameConfig Config;
        public readonly ContentDB DB;

        readonly List<Object> _objects = new();

        public TestContent(int chunksX = 4, int chunksY = 4, bool caves = true)
        {
            Ore = Make<ItemDef>("ore");
            Seed = Make<ItemDef>("seed");
            Seed.foodValue = 2f;
            Bar = Make<ItemDef>("bar");

            Smelt = Make<RecipeDef>("smelt");
            Smelt.inputs.Add(new ItemAmount(Ore, 2));
            Smelt.outputs.Add(new ItemAmount(Bar, 1));
            Smelt.duration = 1f;
            Belt = Building("belt", BuildingKind.Belt, 0);
            Lift = Building("lift", BuildingKind.Lift, 1);
            Splitter = Building("splitter", BuildingKind.Splitter, 2);
            Splitter.cost.Add(new ItemAmount(Bar, 2));
            Filter = Building("filter", BuildingKind.Filter, 3);
            Smelter = Building("smelter", BuildingKind.Processor, 4);
            Smelter.size = new Vector2Int(2, 2);
            Smelter.recipes.Add(Smelt);
            Smelter.buildWork = 1f;

            Air = Mat("air", 1f, MaterialFlags.Air);
            Grass = Mat("grass", 1f);
            Dirt = Mat("dirt", 1f);
            Stone = Mat("stone", 4f);
            Stone.drop = Ore;
            Crystal = Mat("crystal", 4f, MaterialFlags.LightEmitting);
            Crystal.emissionColor = Color.white;
            Crystal.emissionStrength = 1f;
            Bedrock = Mat("bedrock", 1f, MaterialFlags.Indestructible);

            Topsoil = Make<BiomeDef>("topsoil");
            Topsoil.thickness = 30;
            Topsoil.baseMaterial = Dirt;
            Topsoil.caveThreshold = caves ? 0.5f : 2f;
            Deep = Make<BiomeDef>("deep");
            Deep.baseMaterial = Stone;
            Deep.caveThreshold = caves ? 0.45f : 2f;
            Deep.deposits.Add(new BiomeDef.Deposit { material = Crystal, scale = 0.2f, threshold = 0.8f });

            WorldGen = Make<WorldGenSettings>("worldgen");
            WorldGen.widthInChunks = chunksX;
            WorldGen.heightInChunks = chunksY;
            WorldGen.skyHeight = 16;
            WorldGen.air = Air;
            WorldGen.surfaceMaterial = Grass;
            WorldGen.bedrock = Bedrock;
            WorldGen.biomes = new List<BiomeDef> { Topsoil, Deep };
            WorldGen.caveMinDepth = 12;
            WorldGen.nestDepth = 8;
            WorldGen.nestRadius = new Vector2(4, 2);

            Config = Make<GameConfig>("config");

            // Deliberately not air-first, to exercise ContentDB ordering.
            DB = ContentDB.Build(new[] { Dirt, Air, Grass, Stone, Crystal, Bedrock }, WorldGen, new[] { Ore, Seed, Bar }, null,
                new[] { Smelter, Belt, Lift, Splitter, Filter });
        }

        BuildingDef Building(string id, BuildingKind kind, int order)
        {
            var b = Make<BuildingDef>(id);
            b.kind = kind;
            b.paletteOrder = order;
            b.speed = 4f;
            b.buildWork = 0.2f;
            return b;
        }

        public int BuildingIndex(BuildingDef def) => DB.GetBuildingIndex(def);
        public ushort Item(ItemDef def) => DB.GetItemIndex(def);

        TerrainMaterialDef Mat(string id, float hardness, MaterialFlags flags = MaterialFlags.None)
        {
            var m = Make<TerrainMaterialDef>(id);
            m.hardness = hardness;
            m.flags = flags;
            return m;
        }

        T Make<T>(string id) where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            o.name = id;
            if (o is ContentDef def) def.EditorSetIdentity(id, id);
            _objects.Add(o);
            return o;
        }

        public ushort Index(TerrainMaterialDef def) => DB.GetMaterialIndex(def);

        public void Dispose()
        {
            DB.Dispose();
            foreach (var o in _objects) Object.DestroyImmediate(o);
        }
    }
}
