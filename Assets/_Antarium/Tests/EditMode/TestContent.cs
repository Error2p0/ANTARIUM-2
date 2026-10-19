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
        public readonly TerrainMaterialDef Air, Grass, Dirt, Stone, Crystal, Bedrock, Meteor;
        public readonly BiomeDef Topsoil, Deep;
        public readonly ItemDef Ore, Seed, Bar;
        public readonly BuildingDef Belt, Lift, Splitter, Filter, Smelter, Lab;
        public readonly RecipeDef Smelt, Study;
        public readonly ResearchDef FastBelts, LabResearch, SeismicResearch;
        public readonly AbilityDef Frenzy, Seismic, Decree;
        public readonly ContractDef DigIntro, DeliverOre;
        public readonly RandomEventDef MeteorEvent, VeinEvent, BeetleEvent, TrappedEvent;
        public readonly ProgressionContent Progression;
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
            Study = Make<RecipeDef>("study");
            Study.inputs.Add(new ItemAmount(Ore, 1));
            Study.knowledge = 3f;
            Study.duration = 1f;
            Lab = Building("lab", BuildingKind.Processor, 5);
            Lab.size = new Vector2Int(2, 2);
            Lab.recipes.Add(Study);

            Air = Mat("air", 1f, MaterialFlags.Air);
            Grass = Mat("grass", 1f);
            Dirt = Mat("dirt", 1f);
            Stone = Mat("stone", 4f);
            Stone.drop = Ore;
            Crystal = Mat("crystal", 4f, MaterialFlags.LightEmitting);
            Crystal.emissionColor = Color.white;
            Crystal.emissionStrength = 1f;
            Bedrock = Mat("bedrock", 1f, MaterialFlags.Indestructible);
            Meteor = Mat("meteor", 2f);
            Meteor.drop = Bar;

            // Progression: three research nodes, three abilities, two contracts, one event of each kind.
            Frenzy = Make<AbilityDef>("frenzy");
            Frenzy.kind = AbilityKind.Frenzy;
            Frenzy.cooldown = 30f;
            Frenzy.duration = 5f;
            Frenzy.magnitude = 0.75f;
            Seismic = Make<AbilityDef>("seismic");
            Seismic.kind = AbilityKind.SeismicSense;
            Seismic.order = 1;
            Seismic.radius = 10f;
            Decree = Make<AbilityDef>("decree");
            Decree.kind = AbilityKind.RoyalDecree;
            Decree.order = 2;
            Decree.count = 3;

            FastBelts = Make<ResearchDef>("fast_belts");
            FastBelts.knowledgeCost = 5f;
            FastBelts.effects.Add(ResearchEffect.Add(StatKind.BeltSpeed, 1f));
            LabResearch = Make<ResearchDef>("lab_research");
            LabResearch.treePosition = new Vector2Int(1, 0);
            LabResearch.knowledgeCost = 5f;
            LabResearch.cost.Add(new ItemAmount(Bar, 1));
            LabResearch.prerequisites.Add(FastBelts);
            LabResearch.effects.Add(ResearchEffect.Unlock(Lab));
            SeismicResearch = Make<ResearchDef>("seismic_research");
            SeismicResearch.treePosition = new Vector2Int(0, 1);
            SeismicResearch.knowledgeCost = 1f;
            SeismicResearch.effects.Add(ResearchEffect.Unlock(Seismic));

            DigIntro = Make<ContractDef>("dig_intro");
            DigIntro.kind = ContractKind.DigCells;
            DigIntro.amount = 5;
            DigIntro.introOrder = 1;
            DigIntro.repeatable = false;
            DigIntro.rewardKnowledge = 2f;
            DeliverOre = Make<ContractDef>("deliver_ore");
            DeliverOre.kind = ContractKind.Deliver;
            DeliverOre.item = Ore;
            DeliverOre.amount = 3;
            DeliverOre.growth = 1f;
            DeliverOre.rewardKnowledge = 4f;
            DeliverOre.rewards.Add(new ItemAmount(Bar, 1));

            MeteorEvent = Make<RandomEventDef>("meteor");
            MeteorEvent.kind = RandomEventKind.Meteorite;
            MeteorEvent.radius = 2f;
            MeteorEvent.materials.Add(Meteor);
            VeinEvent = Make<RandomEventDef>("vein");
            VeinEvent.kind = RandomEventKind.RichVein;
            VeinEvent.radius = 2.5f;
            VeinEvent.materials.Add(Crystal);
            BeetleEvent = Make<RandomEventDef>("beetle");
            BeetleEvent.kind = RandomEventKind.GoldenBeetle;
            BeetleEvent.duration = 30f;
            BeetleEvent.rewardKnowledge = 5f;
            BeetleEvent.rewards.Add(new ItemAmount(Bar, 2));
            TrappedEvent = Make<RandomEventDef>("trapped");
            TrappedEvent.kind = RandomEventKind.TrappedAnts;
            TrappedEvent.duration = 60f;
            TrappedEvent.radius = 4f;
            TrappedEvent.count = 3;

            Progression = new ProgressionContent
            {
                Research = new[] { LabResearch, FastBelts, SeismicResearch },
                Abilities = new[] { Decree, Frenzy, Seismic },
                Contracts = new[] { DeliverOre, DigIntro },
                Events = new[] { TrappedEvent, MeteorEvent, VeinEvent, BeetleEvent },
            };

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
            Config.progression.firstEventDelay = 1e9f; // events only when a test triggers them

            // Deliberately not air-first, to exercise ContentDB ordering.
            DB = ContentDB.Build(new[] { Dirt, Air, Grass, Stone, Crystal, Bedrock, Meteor }, WorldGen, new[] { Ore, Seed, Bar }, null,
                new[] { Smelter, Belt, Lift, Splitter, Filter, Lab }, Progression);
        }

        /// <summary>The same content registered in a different order (indices change, ids don't). Dispose it yourself.</summary>
        public ContentDB BuildReordered() =>
            ContentDB.Build(new[] { Meteor, Bedrock, Crystal, Stone, Grass, Air, Dirt }, WorldGen, new[] { Bar, Seed, Ore }, null,
                new[] { Lab, Filter, Splitter, Lift, Belt, Smelter }, Progression);

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
