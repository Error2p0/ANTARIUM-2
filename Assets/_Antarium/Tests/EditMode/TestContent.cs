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
        public readonly TerrainMaterialDef Air, Grass, Dirt, Stone, Crystal, Bedrock, Meteor, Sand, Obsidian, Packed, OreRock, Root;
        /// <summary>10.3: CoalRock burns (drops coal), Magma is hot, Ash is what burnt cells become (falls like sand).</summary>
        public readonly TerrainMaterialDef CoalRock, Magma, Ash;
        /// <summary>Coal is fuel: a heated Forge runs without it.</summary>
        public readonly ItemDef Coal;
        /// <summary>
        /// Pump: 2 cells of fluid a second. Wheel: 2×2, 2 cells a second per column, +50% within 6 cells. Forge: 2×2, 2 ore + 1 coal
        /// -> 1 bar (2 s), +100% when heated. Farm: 2×1, 1 seed -> 2 leaves (1 s), +50% output when irrigated.
        /// </summary>
        public readonly BuildingDef Pump, Wheel, Forge, Farm;
        public readonly BiomeDef Topsoil, Deep;
        public readonly ItemDef Ore, Seed, Bar, Chitin;
        /// <summary>Surface (10.2): leaves fall from plants, honeydew is the queen's treat, roots drop fibre.</summary>
        public readonly ItemDef Leaf, Honeydew, Fibre;
        /// <summary>Ranch: 2 leaves -> 1 honeydew (art Ranch, so the golden aphid can appear in it).</summary>
        public readonly BuildingDef Ranch;
        public readonly EnemyDef WolfSpider;
        public readonly RandomEventDef AphidEvent;
        public readonly BuildingDef Belt, Lift, Splitter, Filter, Smelter, Lab, Gate, Trap, Post, Wall, Nursery, Emitter, Chamber;
        /// <summary>Chest: holds 4. Unloader: 10 items per second. IronBelt: a faster belt costing a bar. Mixer: Smelt or Press (chitin -> bar).</summary>
        public readonly BuildingDef Chest, Unloader, IronBelt, Mixer;
        public readonly RecipeDef Raise, Press;
        /// <summary>Brood: +2 starting workers per level (3 levels). Jaws (needs Brood): +50% worker digging. Start: food, knowledge, bars, reveal.</summary>
        public readonly GeneticDef GeneBrood, GeneJaws, GeneStart, GeneInherit, GeneVein, GeneRoyal, GeneStrange, GeneGain;
        /// <summary>Rich: far more ore, raids twice as big. Shallow: biomes half as thick. Hard: locked behind GeneStrange.</summary>
        public readonly WorldModifierDef ModRich, ModShallow, ModHard;
        public readonly CasteDef WorkerCaste, SoldierCaste, MinerCaste, HaulerCaste, NurseCaste, BuilderCaste, ScoutCaste, EngineerCaste;
        /// <summary>10.4: glow ants (light radius 6) and fire ants (heat-proof, a quarter of lava damage), locked behind EvoLineage.</summary>
        public readonly CasteDef GlowCaste, FireCaste;
        /// <summary>10.4 evolutions. Brood: laying +15% per level (L1 2 honeydew, L2 2 honeydew + 1 chitin). Lineage (needs Brood):
        /// glow ants at level 1, fire ants at level 2 (1 chitin each). Quicken: unlocks the Quickening (1 honeydew).</summary>
        public readonly EvolutionDef EvoBrood, EvoLineage, EvoQuicken;
        public readonly AbilityDef Quickening;
        /// <summary>
        /// 10.5. Resonator: 2x2 on a floor touching conductive rock, 1 knowledge a second and +50% machine speed within 6 cells at
        /// full power (4 resonance; Crystal resonates 1, OreRock conducts). Engine: 2x2, unique, only in the Deep biome, 2 stages
        /// (1 bar, then 1 relic and 5 food), 2 knowledge a second running. Scriptorium: studies relics (5 knowledge) and tablets (9).
        /// </summary>
        public readonly BuildingDef Resonator, Engine, Scriptorium;
        public readonly ItemDef Relic, Tablet;
        public readonly TerrainMaterialDef TabletRock;
        /// <summary>Lore: Marks (1 relic), Story (2 relics), Plan (1 tablet). AncientResearch needs 2 fragments.</summary>
        public readonly LoreDef LoreMarks, LoreStory, LorePlan;
        public readonly ResearchDef AncientResearch;
        /// <summary>Thief: a rival ant (carries 2, one every 0.5 s). Mole: digs (power 4) through hardness up to 1.5.</summary>
        public readonly EnemyDef Thief, Mole;
        /// <summary>
        /// 10.6. Throne: 4x2, unique, built around the queen, only through GeneThrone; +50% laying, +2 eggs, ants within 6 cells
        /// work twice as fast. ModVariant turns the Deep biome into VariantRock ("Red Deep") with CaveBeetles in its caves.
        /// </summary>
        public readonly BuildingDef Throne;
        public readonly GeneticDef GeneThrone;
        public readonly WorldModifierDef ModVariant;
        public readonly TerrainMaterialDef VariantRock;
        public readonly EnemyDef CaveBeetle;
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
            Leaf = Make<ItemDef>("leaf");
            Honeydew = Make<ItemDef>("honeydew");
            Fibre = Make<ItemDef>("fibre");
            Coal = Make<ItemDef>("coal");
            Coal.fuel = true;
            Relic = Make<ItemDef>("relic");
            Tablet = Make<ItemDef>("tablet");

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
            Crystal = Mat("crystal", 4f, MaterialFlags.LightEmitting | MaterialFlags.Conductive);
            Crystal.resonance = 1f;
            Crystal.emissionColor = Color.white;
            Crystal.emissionStrength = 1f;
            Bedrock = Mat("bedrock", 1f, MaterialFlags.Indestructible);
            Meteor = Mat("meteor", 2f);
            Meteor.drop = Bar;
            Sand = Mat("sand", 0.5f, MaterialFlags.Unstable);
            Obsidian = Mat("obsidian", 6f);
            Packed = Mat("packed", 1f);
            OreRock = Mat("ore_rock", 2f, MaterialFlags.Conductive);
            OreRock.drop = Ore;
            Root = Mat("root", 4f, MaterialFlags.Flammable);
            Root.drop = Fibre;
            CoalRock = Mat("coal_rock", 3f, MaterialFlags.Flammable);
            CoalRock.drop = Coal;
            Magma = Mat("magma", 6f, MaterialFlags.Hot);
            VariantRock = Mat("variant_rock", 4f);
            Ash = Mat("ash", 0.3f, MaterialFlags.Unstable);
            TabletRock = Mat("tablet_rock", 5f, MaterialFlags.LightEmitting);
            TabletRock.drop = Tablet;

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
            Chest = Building("chest", BuildingKind.Chest, 13);
            Chest.capacity = 4;
            Unloader = Building("unloader", BuildingKind.Unloader, 14);
            Unloader.speed = 10f;
            IronBelt = Building("iron_belt", BuildingKind.Belt, 15);
            IronBelt.speed = 8f;
            IronBelt.tier = 1;
            IronBelt.cost.Add(new ItemAmount(Bar, 1));
            Press = Make<RecipeDef>("press");
            Press.inputs.Add(new ItemAmount(Chitin, 1));
            Press.outputs.Add(new ItemAmount(Bar, 1));
            Press.duration = 1f;
            Mixer = Building("mixer", BuildingKind.Processor, 16);
            Mixer.size = new Vector2Int(2, 2);
            Mixer.recipes.Add(Smelt);
            Mixer.recipes.Add(Press);
            var herd = Make<RecipeDef>("herd");
            herd.inputs.Add(new ItemAmount(Leaf, 2));
            herd.outputs.Add(new ItemAmount(Honeydew, 1));
            herd.duration = 1f;
            Ranch = Building("ranch", BuildingKind.Processor, 17);
            Ranch.size = new Vector2Int(2, 1);
            Ranch.recipes.Add(herd);
            Ranch.art = PlaceholderArt.Ranch;

            Pump = Building("pump", BuildingKind.Pump, 18);
            Pump.speed = 2f;
            Wheel = Building("wheel", BuildingKind.WaterWheel, 19);
            Wheel.size = new Vector2Int(2, 2);
            Wheel.speed = 2f;
            Wheel.machineBoost = 0.5f;
            Wheel.boostRadius = 6f;
            var fire = Make<RecipeDef>("fire_smelt");
            fire.inputs.Add(new ItemAmount(Ore, 2));
            fire.inputs.Add(new ItemAmount(Coal, 1));
            fire.outputs.Add(new ItemAmount(Bar, 1));
            fire.duration = 2f;
            Forge = Building("forge", BuildingKind.Processor, 20);
            Forge.size = new Vector2Int(2, 2);
            Forge.recipes.Add(fire);
            Forge.heatBonus = 1f;
            var grow = Make<RecipeDef>("grow");
            grow.inputs.Add(new ItemAmount(Seed, 1));
            grow.outputs.Add(new ItemAmount(Leaf, 2));
            grow.duration = 1f;
            Farm = Building("farm", BuildingKind.Processor, 21);
            Farm.size = new Vector2Int(2, 1);
            Farm.recipes.Add(grow);
            Farm.irrigationBonus = 0.5f;
            Farm.inputBatches = 10;
            Farm.outputLimit = 50;

            Resonator = Building("resonator", BuildingKind.Resonator, 22);
            Resonator.size = new Vector2Int(2, 2);
            Resonator.knowledgePerSecond = 1f;
            Resonator.fullResonance = 4f;
            Resonator.machineBoost = 0.5f;
            Resonator.boostRadius = 6f;
            Engine = Building("engine", BuildingKind.Engine, 23);
            Engine.size = new Vector2Int(2, 2);
            Engine.unique = true;
            Engine.knowledgePerSecond = 2f;
            Engine.cost.Add(new ItemAmount(Bar, 1));
            Engine.stages.Add(new BuildingStage { name = "gears", buildWork = 0.5f, cost = { new ItemAmount(Bar, 1) } });
            Engine.stages.Add(new BuildingStage { name = "waking", buildWork = 0.5f, foodCost = 5f, cost = { new ItemAmount(Relic, 1) } });
            var studyRelic = Make<RecipeDef>("study_relic");
            studyRelic.inputs.Add(new ItemAmount(Relic, 1));
            studyRelic.knowledge = 5f;
            studyRelic.duration = 0.5f;
            var studyTablet = Make<RecipeDef>("study_tablet");
            studyTablet.inputs.Add(new ItemAmount(Tablet, 1));
            studyTablet.knowledge = 9f;
            studyTablet.duration = 0.5f;
            Scriptorium = Building("scriptorium", BuildingKind.Processor, 24);
            Scriptorium.size = new Vector2Int(2, 2);
            Scriptorium.recipes.Add(studyRelic);
            Scriptorium.recipes.Add(studyTablet);
            Throne = Building("throne", BuildingKind.Throne, 25);
            Throne.size = new Vector2Int(4, 2);
            Throne.unique = true;
            Throne.cost.Add(new ItemAmount(Bar, 1));
            Throne.stats.Add(new StatAmount(StatKind.LayingSpeed, 0.5f));
            Throne.stats.Add(new StatAmount(StatKind.MaxEggs, 2f));
            Throne.auraRadius = 6f;
            Throne.auraWork = 1f;

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
            GlowCaste = Caste("glow", AntCaste.Glow, health: 35f);
            GlowCaste.lightRadius = 6f;
            FireCaste = Caste("fire", AntCaste.Fire, health: 60f);
            FireCaste.heatProof = true;
            FireCaste.lavaDamageTaken = 0.25f;
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
            WolfSpider = Make<EnemyDef>("wolf_spider");
            WolfSpider.behavior = EnemyBehavior.NightHunter;
            WolfSpider.health = 30f;
            WolfSpider.damage = 5f;
            WolfSpider.speed = 3f;
            WolfSpider.aggroRadius = 8f;
            Thief = Make<EnemyDef>("thief");
            Thief.behavior = EnemyBehavior.Thief;
            Thief.health = 20f;
            Thief.damage = 2f;
            Thief.speed = 3f;
            Thief.carryItems = 2;
            Thief.stealSeconds = 0.5f;
            Thief.drops.Add(new ItemAmount(Chitin, 1));
            Mole = Make<EnemyDef>("mole");
            Mole.behavior = EnemyBehavior.Digger;
            Mole.health = 50f;
            Mole.damage = 5f;
            Mole.speed = 3f;
            Mole.aggroRadius = 10f;
            Mole.digPower = 4f;
            Mole.maxDigHardness = 1.5f;

            LoreMarks = Make<LoreDef>("lore_marks");
            LoreMarks.item = Relic;
            LoreMarks.count = 1;
            LoreStory = Make<LoreDef>("lore_story");
            LoreStory.item = Relic;
            LoreStory.count = 2;
            LoreStory.order = 1;
            LorePlan = Make<LoreDef>("lore_plan");
            LorePlan.item = Tablet;
            LorePlan.count = 1;
            LorePlan.order = 2;

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
            Quickening = Make<AbilityDef>("quickening");
            Quickening.kind = AbilityKind.Quickening;
            Quickening.order = 4;
            Quickening.cooldown = 60f;

            EvoBrood = Evolution("evo_brood", 0, 0, new[] { EvolutionEffect.Add(StatKind.LayingSpeed, 0.15f) },
                Level(0f, (Honeydew, 2)), Level(0f, (Honeydew, 2), (Chitin, 1)));
            EvoLineage = Evolution("evo_lineage", 1, 0, new[] { EvolutionEffect.Unlock(AntCaste.Glow, 1), EvolutionEffect.Unlock(AntCaste.Fire, 2) },
                Level(0f, (Chitin, 1)), Level(0f, (Chitin, 1)));
            EvoLineage.prerequisites.Add(EvoBrood);
            EvoQuicken = Evolution("evo_quicken", 0, 1, new[] { EvolutionEffect.Unlock(Quickening) }, Level(0f, (Honeydew, 1)));

            AncientResearch = Make<ResearchDef>("ancient_research");
            AncientResearch.treePosition = new Vector2Int(3, 0);
            AncientResearch.knowledgeCost = 1f;
            AncientResearch.loreRequired = 2;
            AncientResearch.effects.Add(ResearchEffect.Unlock(Engine));

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
            AphidEvent = Make<RandomEventDef>("golden_aphid");
            AphidEvent.kind = RandomEventKind.GoldenAphid;
            AphidEvent.duration = 30f;
            AphidEvent.rewardKnowledge = 4f;
            AphidEvent.rewards.Add(new ItemAmount(Honeydew, 3));

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
            GeneThrone = Genetic("gene_throne", 1, 3, 2, 0, 1, GeneticEffect.Unlock(Throne));
            CaveBeetle = Make<EnemyDef>("cave_beetle");
            CaveBeetle.behavior = EnemyBehavior.CaveDweller;
            CaveBeetle.health = 20f;
            CaveBeetle.speed = 2f;
            ModVariant = Make<WorldModifierDef>("mod_variant");
            ModVariant.order = 3;
            ModVariant.variants.Add(new BiomeVariant { displayName = "Red Deep", baseMaterial = VariantRock, creatures = { CaveBeetle } });

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
                Research = new[] { LabResearch, FastBelts, SeismicResearch, SoldierResearch, CasteResearch, AncientResearch },
                Abilities = new[] { Decree, Frenzy, Seismic, Burst, Quickening },
                Castes = new[] { EngineerCaste, WorkerCaste, SoldierCaste, MinerCaste, HaulerCaste, NurseCaste, BuilderCaste, ScoutCaste, GlowCaste, FireCaste },
                Evolutions = new[] { EvoLineage, EvoQuicken, EvoBrood },
                Contracts = new[] { DeliverOre, DigIntro },
                Events = new[] { TrappedEvent, MeteorEvent, VeinEvent, BeetleEvent, AphidEvent },
                Enemies = new[] { Spider, Raider, Thief, Mole },
                Lore = new[] { LorePlan, LoreMarks, LoreStory },
                Raiders = new[] { Raider },
                Genetics = new[] { GeneJaws, GeneBrood, GeneStart, GeneInherit, GeneVein, GeneRoyal, GeneStrange, GeneGain, GeneThrone },
                WorldModifiers = new[] { ModHard, ModRich, ModShallow, ModVariant },
                PlantForage = new[] { Leaf },
                RoyalTreat = Honeydew,
                NightHunters = new[] { WolfSpider },
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
            WorldGen.ashMaterial = Ash;
            WorldGen.tabletMaterial = TabletRock;
            Engine.requiredBiome = Deep;
            ModVariant.variants[0].biome = Deep;

            Config = Make<GameConfig>("config");
            Config.progression.firstEventDelay = 1e9f; // events only when a test triggers them
            Config.threats.firstRaidSeconds = 1e9f;    // raids too
            Config.threats.rivalFirstSeconds = 1e9f;   // rival ants and mole crickets only when a test asks (10.5)
            Config.threats.moleFirstSeconds = 1e9f;
            // The surface stays quiet unless a test asks: no leaves, weather, birds or spiders.
            Config.surface.leafInterval = 1e9f;
            Config.surface.firstWeatherSeconds = 1e9f;
            Config.surface.firstBirdSeconds = 1e9f;
            Config.surface.firstSpiderSeconds = 1e9f;
            // Ants hatch without traits and walk at one speed unless a test asks (10.4).
            Config.growth.traitChance = 0f;
            Config.growth.speedJitter = 0f;

            // Deliberately not air-first, to exercise ContentDB ordering.
            DB = ContentDB.Build(new[] { Dirt, Air, Grass, Stone, Crystal, Bedrock, Meteor, Sand, Obsidian, Packed, OreRock, Root, CoalRock, Magma, Ash, TabletRock,
                    VariantRock },
                WorldGen, new[] { Ore, Seed, Bar, Chitin, Leaf, Honeydew, Fibre, Coal, Relic, Tablet }, null,
                new[] { Smelter, Belt, Lift, Splitter, Filter, Lab, Gate, Trap, Post, Wall, Nursery, Emitter, Chamber, Chest, Unloader, IronBelt, Mixer, Ranch,
                    Pump, Wheel, Forge, Farm, Resonator, Engine, Scriptorium, Throne },
                Progression);
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
            ContentDB.Build(new[] { VariantRock, TabletRock, Ash, Magma, CoalRock, Root, OreRock, Packed, Obsidian, Sand, Meteor, Bedrock, Crystal, Stone, Grass, Air, Dirt }, WorldGen,
                new[] { Tablet, Relic, Coal, Fibre, Honeydew, Leaf, Chitin, Bar, Seed, Ore }, null,
                new[] { Throne, Scriptorium, Engine, Resonator, Farm, Forge, Wheel, Pump, Ranch, Mixer, IronBelt, Unloader, Chest, Chamber, Emitter, Nursery, Wall, Post, Trap,
                    Gate, Lab, Filter, Splitter, Lift, Belt, Smelter },
                Progression);

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

        EvolutionDef Evolution(string id, int column, int row, EvolutionEffect[] effects, params EvolutionLevel[] levels)
        {
            var e = Make<EvolutionDef>(id);
            e.treePosition = new Vector2Int(column, row);
            e.effects.AddRange(effects);
            e.levels.AddRange(levels);
            return e;
        }

        static EvolutionLevel Level(float food, params (ItemDef item, int count)[] cost)
        {
            var level = new EvolutionLevel { foodCost = food };
            foreach (var (item, count) in cost) level.cost.Add(new ItemAmount(item, count));
            return level;
        }

        public int EvolutionIndex(EvolutionDef def) => DB.TryGetEvolutionIndex(def.Id, out int i) ? i : -1;

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
