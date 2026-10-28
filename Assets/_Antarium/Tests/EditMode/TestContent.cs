using System;
using System.Collections.Generic;
using Antarium.Content;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Antarium.Tests
{
    /// <summary>
    /// In-memory content for tests: a small world with dirt over stone, a glow crystal and bedrock; sand, obsidian
    /// and packed earth; a raider and a cave creature; gates, traps, guard posts and walls; a princess chamber,
    /// genetic traits and world modifiers.
    /// </summary>
    public sealed class TestContent : IDisposable
    {
        public readonly TerrainMaterialDef Air, Grass, Dirt, Stone, Crystal, Bedrock, Meteor, Sand, Obsidian, Packed, OreRock;
        public readonly BiomeDef Topsoil, Deep;
        public readonly ItemDef Ore, Seed, Bar, Chitin;
        public readonly BuildingDef Belt, Lift, Splitter, Filter, Smelter, Lab, Gate, Trap, Post, Wall, Nursery, Emitter, Chamber;
        public readonly RecipeDef Raise;
        /// <summary>Brood: +2 starting workers per level (3 levels). Jaws (needs Brood): +50% worker digging. Start: food, knowledge, bars, reveal.</summary>
        public readonly GeneticDef GeneBrood, GeneJaws, GeneStart, GeneInherit, GeneVein, GeneRoyal, GeneStrange, GeneGain;
        /// <summary>Rich: far more ore, raids twice as big. Shallow: biomes half as thick. Hard: locked behind GeneStrange.</summary>
        public readonly WorldModifierDef ModRich, ModShallow, ModHard;
        public readonly CasteDef WorkerCaste, SoldierCaste, MinerCaste, HaulerCaste, NurseCaste, BuilderCaste, ScoutCaste, EngineerCaste;
        /// <summary>Unlocks every specialist caste but soldiers.</summary>
        public readonly ResearchDef CasteResearch;
        public readonly AbilityDef Burst;
        public readonly EnemyDef Raider, Spider;
        public readonly ResearchDef SoldierResearch;
        public readonly RecipeDef Smelt, Study;
        public readonly ResearchDef FastBelts, LabResearch, SeismicResearch;
        public readonly AbilityDef Frenzy, Seismic, Decree;
        public readonly ContractDef DigIntro, DeliverOre;
        public readonly RandomEventDef MeteorEvent, VeinEvent, BeetleEvent, TrappedEvent;
        public readonly ProgressionContent Progression;
        /// <summary>AchDig: dig out 5 cells. AchFlight: one Nuptial Flight (pushes the stat STAT_FLIGHTS).</summary>
        public readonly AchievementDef AchDig, AchFlight;
        public readonly WorldGenSettings WorldGen;
        public readonly GameConfig Config;
        public readonly ContentDB DB;

        readonly List<Object> _objects = new();

        /// <param name="creatures">Cave creatures (<see cref="Spider"/>) live in the deep biome's caves.</param>
        /// <param name="ores">The deep biome has a deposit of <see cref="OreRock"/> (drops ore).</param>
        public TestContent(int chunksX = 4, int chunksY = 4, bool caves = true, bool creatures = false, bool ores = false)
        {
            Ore = Make<ItemDef>("ore");
            Seed = Make<ItemDef>("seed");
            Seed.foodValue = 2f;
            Bar = Make<ItemDef>("bar");
            Chitin = Make<ItemDef>("chitin");

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
            Sand = Mat("sand", 0.5f, MaterialFlags.Unstable);
            Obsidian = Mat("obsidian", 6f);
            Packed = Mat("packed", 1f);
            OreRock = Mat("ore_rock", 2f);
            OreRock.drop = Ore;

            Gate = Building("gate", BuildingKind.Gate, 6);
            Gate.maxHealth = 40f;
            Trap = Building("trap", BuildingKind.Trap, 7);
            Trap.trapDamage = 15f;
            Trap.trapHold = 3f;
            Trap.trapRearm = 5f;
            Post = Building("post", BuildingKind.GuardPost, 8);
            Wall = Building("wall", BuildingKind.Wall, 9);
            Wall.fillMaterial = Packed;
            Nursery = Building("nursery", BuildingKind.Nursery, 10);
            Nursery.size = new Vector2Int(2, 1);
            Nursery.eggCapacity = 4;
            Nursery.hatchBonus = 1f;
            Emitter = Building("emitter", BuildingKind.Emitter, 11);
            Emitter.emitterRadius = 5f;
            Raise = Make<RecipeDef>("raise");
            Raise.inputs.Add(new ItemAmount(Ore, 1));
            Raise.princesses = 1;
            Raise.duration = 1f;
            Chamber = Building("chamber", BuildingKind.Processor, 12);
            Chamber.size = new Vector2Int(2, 2);
            Chamber.recipes.Add(Raise);
            Chamber.princessSlots = 3;

            // Castes: workers and soldiers as in the game, specialists with clear numbers for the tests.
            WorkerCaste = Caste("worker", AntCaste.Worker, health: 40f, attack: 3f, upkeep: 0.25f, egg: 6f);
            SoldierCaste = Caste("soldier", AntCaste.Soldier, health: 150f, attack: 14f, upkeep: 0.6f, egg: 15f, share: 0.2f, jobs: false);
            MinerCaste = Caste("miner", AntCaste.Miner, jobs: false);
            MinerCaste.dig = 3f;
            MinerCaste.digJob = 1f;
            HaulerCaste = Caste("hauler", AntCaste.Hauler, jobs: false);
            HaulerCaste.carry = 3;
            HaulerCaste.haulJob = 1f;
            HaulerCaste.supplyJob = 1f;
            NurseCaste = Caste("nurse", AntCaste.Nurse, jobs: false);
            BuilderCaste = Caste("builder", AntCaste.Builder, jobs: false);
            BuilderCaste.build = 3f;
            BuilderCaste.buildJob = 1f;
            ScoutCaste = Caste("scout", AntCaste.Scout, jobs: false);
            ScoutCaste.senseRadius = 6;
            EngineerCaste = Caste("engineer", AntCaste.Engineer, jobs: false);
            EngineerCaste.machineBoost = 1f;

            Raider = Make<EnemyDef>("raider");
            Raider.behavior = EnemyBehavior.Raider;
            Raider.health = 20f;
            Raider.damage = 4f;
            Raider.speed = 3f;
            Raider.foodPerSecond = 2f;
            Raider.carryFood = 5f;
            Raider.drops.Add(new ItemAmount(Chitin, 1));
            Spider = Make<EnemyDef>("spider");
            Spider.behavior = EnemyBehavior.CaveDweller;
            Spider.health = 30f;
            Spider.damage = 5f;
            Spider.speed = 3f;
            Spider.aggroRadius = 10f;
            Spider.knowledge = 3f;
            Spider.drops.Add(new ItemAmount(Chitin, 2));

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
            Burst = Make<AbilityDef>("burst");
            Burst.kind = AbilityKind.PheromoneBurst;
            Burst.order = 3;
            Burst.radius = 12f;
            Burst.duration = 10f;
            Burst.magnitude = 0.5f;

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
            SoldierResearch = Make<ResearchDef>("soldier_research");
            SoldierResearch.treePosition = new Vector2Int(2, 0);
            SoldierResearch.knowledgeCost = 1f;
            SoldierResearch.effects.Add(ResearchEffect.Unlock(AntCaste.Soldier));
            CasteResearch = Make<ResearchDef>("caste_research");
            CasteResearch.treePosition = new Vector2Int(2, 1);
            CasteResearch.knowledgeCost = 1f;
            foreach (var caste in new[] { AntCaste.Miner, AntCaste.Hauler, AntCaste.Nurse, AntCaste.Builder, AntCaste.Scout, AntCaste.Engineer })
                CasteResearch.effects.Add(ResearchEffect.Unlock(caste));

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

            // Prestige: world modifiers and genetic traits.
            ModRich = Make<WorldModifierDef>("mod_rich");
            ModRich.oreThreshold = -0.5f;
            ModRich.stats.Add(new StatAmount(StatKind.RaidSize, 1f));
            ModShallow = Make<WorldModifierDef>("mod_shallow");
            ModShallow.order = 1;
            ModShallow.biomeThickness = 0.5f;
            ModHard = Make<WorldModifierDef>("mod_hard");
            ModHard.order = 2;
            ModHard.stats.Add(new StatAmount(StatKind.GeneticGain, 1f));
            ModHard.stats.Add(new StatAmount(StatKind.CreatureCount, 1f));

            GeneBrood = Genetic("gene_brood", 0, 0, 1, 1, 3, GeneticEffect.Of(GeneticEffectKind.StartWorkers, 2f));
            GeneJaws = Genetic("gene_jaws", 1, 0, 2, 0, 1, GeneticEffect.Add(StatKind.WorkerDig, 0.5f));
            GeneJaws.prerequisites.Add(GeneBrood);
            GeneStart = Genetic("gene_start", 0, 1, 1, 0, 1, GeneticEffect.Of(GeneticEffectKind.StartFood, 20f),
                GeneticEffect.Of(GeneticEffectKind.StartKnowledge, 7f), GeneticEffect.Items(Bar, 2), GeneticEffect.Of(GeneticEffectKind.RevealRadius, 10f));
            GeneInherit = Genetic("gene_inherit", 0, 2, 1, 0, 1, GeneticEffect.Inherit(FastBelts));
            GeneVein = Genetic("gene_vein", 0, 3, 1, 0, 1, GeneticEffect.Of(GeneticEffectKind.VeinInstinct));
            GeneRoyal = Genetic("gene_royal", 0, 4, 1, 0, 1, GeneticEffect.Of(GeneticEffectKind.MaxPrincesses, 1f),
                GeneticEffect.Of(GeneticEffectKind.ModifierSlots, 1f));
            GeneStrange = Genetic("gene_strange", 1, 1, 1, 0, 1, GeneticEffect.Unlock(ModHard));
            GeneGain = Genetic("gene_gain", 1, 2, 1, 0, 1, GeneticEffect.Add(StatKind.GeneticGain, 1f));

            AchDig = Make<AchievementDef>("ach_dig");
            AchDig.apiName = "ACH_DIG";
            AchDig.kind = AchievementKind.DigCells;
            AchDig.amount = 5;
            AchFlight = Make<AchievementDef>("ach_flight");
            AchFlight.apiName = "ACH_FLIGHT";
            AchFlight.kind = AchievementKind.Flights;
            AchFlight.amount = 1;
            AchFlight.statName = "STAT_FLIGHTS";
            AchFlight.order = 1;

            Progression = new ProgressionContent
            {
                Achievements = new[] { AchFlight, AchDig },
                Research = new[] { LabResearch, FastBelts, SeismicResearch, SoldierResearch, CasteResearch },
                Abilities = new[] { Decree, Frenzy, Seismic, Burst },
                Castes = new[] { EngineerCaste, WorkerCaste, SoldierCaste, MinerCaste, HaulerCaste, NurseCaste, BuilderCaste, ScoutCaste },
                Contracts = new[] { DeliverOre, DigIntro },
                Events = new[] { TrappedEvent, MeteorEvent, VeinEvent, BeetleEvent },
                Enemies = new[] { Spider, Raider },
                Raiders = new[] { Raider },
                Genetics = new[] { GeneJaws, GeneBrood, GeneStart, GeneInherit, GeneVein, GeneRoyal, GeneStrange, GeneGain },
                WorldModifiers = new[] { ModHard, ModRich, ModShallow },
            };

            Topsoil = Make<BiomeDef>("topsoil");
            Topsoil.thickness = 30;
            Topsoil.baseMaterial = Dirt;
            Topsoil.caveThreshold = caves ? 0.5f : 2f;
            Deep = Make<BiomeDef>("deep");
            Deep.baseMaterial = Stone;
            Deep.caveThreshold = caves ? 0.45f : 2f;
            Deep.deposits.Add(new BiomeDef.Deposit { material = Crystal, scale = 0.2f, threshold = 0.8f });
            if (ores) Deep.deposits.Add(new BiomeDef.Deposit { material = OreRock, scale = 0.15f, threshold = 0.6f });
            if (creatures)
            {
                Deep.creatures.Add(Spider);
                Deep.caveCellsPerCreature = 40;
                Deep.maxCreaturesPerCave = 3;
            }

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
            WorldGen.fluidReactionMaterial = Obsidian;

            Config = Make<GameConfig>("config");
            Config.progression.firstEventDelay = 1e9f; // events only when a test triggers them
            Config.threats.firstRaidSeconds = 1e9f;    // raids too

            // Deliberately not air-first, to exercise ContentDB ordering.
            DB = ContentDB.Build(new[] { Dirt, Air, Grass, Stone, Crystal, Bedrock, Meteor, Sand, Obsidian, Packed, OreRock }, WorldGen,
                new[] { Ore, Seed, Bar, Chitin }, null,
                new[] { Smelter, Belt, Lift, Splitter, Filter, Lab, Gate, Trap, Post, Wall, Nursery, Emitter, Chamber }, Progression);
        }

        CasteDef Caste(string id, AntCaste caste, float health = 40f, float attack = 3f, float upkeep = 0f, float egg = 6f, float share = 0.1f,
            bool jobs = true)
        {
            var c = Make<CasteDef>(id);
            c.caste = caste;
            c.order = (int)caste;
            c.health = health;
            c.attack = attack;
            c.upkeep = upkeep;
            c.eggFoodCost = egg;
            c.defaultShare = share;
            if (!jobs) c.digJob = c.haulJob = c.buildJob = c.supplyJob = 0f;
            return c;
        }

        /// <summary>Changes a caste's numbers after the database was built (re-reads them).</summary>
        public void SetCaste(AntCaste caste, Action<CasteDef> change)
        {
            change(DB.Caste(caste));
            DB.RefreshCastes();
        }

        /// <summary>The same content registered in a different order (indices change, ids don't). Dispose it yourself.</summary>
        public ContentDB BuildReordered() =>
            ContentDB.Build(new[] { OreRock, Packed, Obsidian, Sand, Meteor, Bedrock, Crystal, Stone, Grass, Air, Dirt }, WorldGen,
                new[] { Chitin, Bar, Seed, Ore }, null,
                new[] { Chamber, Emitter, Nursery, Wall, Post, Trap, Gate, Lab, Filter, Splitter, Lift, Belt, Smelter }, Progression);

        GeneticDef Genetic(string id, int column, int row, int cost, int costPerLevel, int maxLevel, params GeneticEffect[] effects)
        {
            var g = Make<GeneticDef>(id);
            g.treePosition = new Vector2Int(column, row);
            g.cost = cost;
            g.costPerLevel = costPerLevel;
            g.maxLevel = maxLevel;
            g.effects.AddRange(effects);
            return g;
        }

        public int GeneticIndex(GeneticDef def) => DB.TryGetGeneticIndex(def.Id, out int i) ? i : -1;
        public int ModifierIndex(WorldModifierDef def) => DB.TryGetModifierIndex(def.Id, out int i) ? i : -1;

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
