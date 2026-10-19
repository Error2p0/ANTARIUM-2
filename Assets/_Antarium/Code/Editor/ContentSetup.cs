using System.Collections.Generic;
using System.IO;
using System.Linq;
using Antarium.Content;
using UnityEditor;
using UnityEngine;

namespace Antarium.Editor
{
    /// <summary>Creates the default content (terrain, items, buildings, recipes) and keeps the ContentDatabase in sync.</summary>
    public static class ContentSetup
    {
        public const string Root = "Assets/_Antarium/Content";
        public const string MaterialsFolder = Root + "/Materials";
        public const string BiomesFolder = Root + "/Biomes";
        public const string ItemsFolder = Root + "/Items";
        public const string RecipesFolder = Root + "/Recipes";
        public const string BuildingsFolder = Root + "/Buildings";
        public const string ResearchFolder = Root + "/Research";
        public const string AbilitiesFolder = Root + "/Abilities";
        public const string ContractsFolder = Root + "/Contracts";
        public const string EventsFolder = Root + "/Events";
        public const string DatabasePath = Root + "/ContentDatabase.asset";
        public const string WorldGenPath = Root + "/WorldGenSettings.asset";
        public const string ConfigPath = Root + "/GameConfig.asset";

        /// <summary>
        /// Creates any missing default assets. Existing assets are left untouched so hand-tuned values survive.
        /// </summary>
        [MenuItem("Antarium/Setup/Create Default Content")]
        public static ContentDatabase CreateDefaultContent()
        {
            EnsureFolder(MaterialsFolder);
            EnsureFolder(BiomesFolder);
            EnsureFolder(ItemsFolder);
            EnsureFolder(RecipesFolder);
            EnsureFolder(BuildingsFolder);
            EnsureFolder(ResearchFolder);
            EnsureFolder(AbilitiesFolder);
            EnsureFolder(ContractsFolder);
            EnsureFolder(EventsFolder);

            var copperItem = Item("copper_ore", "Copper Ore", "#d47a3c");
            var ironItem = Item("iron_ore", "Iron Ore", "#cfc8c0");
            var coalItem = Item("coal", "Coal", "#2a282e");
            var amberItem = Item("amber", "Amber", "#e39b2b");
            var fossilItem = Item("fossil", "Fossil", "#e8dcc0");
            var crystalItem = Item("crystal_shard", "Crystal Shard", "#7fd8ff");
            var seedItem = Item("seed", "Seed", "#c8a85a", food: 2f);
            var grubItem = Item("grub", "Grub", "#f0e2c0", food: 5f);
            var copperBar = Item("copper", "Copper", "#f09a5a");
            var ironBar = Item("iron", "Iron", "#aeb8c6");
            var fungusItem = Item("fungus", "Fungus", "#c9a2d8", food: 3f);

            var air = Mat("air", "Air", 1f, PixelPattern.Soil, "#000000", "#000000", "#000000", "#000000", 0f, MaterialFlags.Air);
            var grass = Mat("grass", "Grass", 0.8f, PixelPattern.Soil, "#4c7a2e", "#33561f", "#6aa63c", "#2d4a1a", 0.2f, top: "#86c94e");
            var dirt = Mat("dirt", "Dirt", 1f, PixelPattern.Soil, "#6b4a2f", "#4a321f", "#86603d", "#3b2717", 0.2f);
            var pebbles = Mat("pebbles", "Pebbly Dirt", 2f, PixelPattern.Pebbles, "#6b4a2f", "#4a321f", "#86603d", "#9b948a", 0.6f);
            var copper = Mat("copper_ore", "Copper Ore", 3f, PixelPattern.Ore, "#5e4330", "#3f2c1e", "#7a573c", "#d47a3c", 0.7f, value: 2, flags: MaterialFlags.Conductive);
            var clay = Mat("clay", "Clay", 1.8f, PixelPattern.Strata, "#9a5a3c", "#74402a", "#b8714c", "#5e3322", 0.2f);
            var iron = Mat("iron_ore", "Iron Ore", 4f, PixelPattern.Ore, "#8a4e35", "#643622", "#a8654a", "#cfc8c0", 0.6f, value: 4, flags: MaterialFlags.Conductive);
            var stone = Mat("stone", "Stone", 5f, PixelPattern.Rock, "#6c6f78", "#4b4d55", "#8b8f99", "#3a3b41", 0.3f);
            var coal = Mat("coal", "Coal", 3f, PixelPattern.Ore, "#5d6068", "#41434a", "#7a7e87", "#1f1d22", 0.8f, value: 3, flags: MaterialFlags.Flammable);
            var amber = Mat("amber", "Amber", 3f, PixelPattern.Ore, "#5d6068", "#41434a", "#7a7e87", "#e39b2b", 0.5f, value: 10);
            var fossil = Mat("fossil", "Fossil", 4f, PixelPattern.Ore, "#5d6068", "#41434a", "#7a7e87", "#e8dcc0", 0.5f, value: 8);
            var crystal = Mat("glow_crystal", "Glow Crystal", 4f, PixelPattern.Crystal, "#28344f", "#1b2338", "#e0fbff", "#7fd8ff", 0.8f,
                value: 12, flags: MaterialFlags.LightEmitting, emission: "#7fd0ff", emissionStrength: 0.85f, glow: 0.6f);
            var grubNest = Mat("grub_nest", "Grub Nest", 0.8f, PixelPattern.Pebbles, "#6b4a2f", "#4a321f", "#86603d", "#f0e2c0", 0.7f);
            var bedrock = Mat("bedrock", "Bedrock", 1f, PixelPattern.Rock, "#2b2a30", "#1a191e", "#3d3c44", "#121115", 0.3f, MaterialFlags.Indestructible);

            var topsoil = Biome("topsoil", "Topsoil & Roots", 110, dirt, 0.028f, 0.62f,
                Dep(pebbles, 0.09f, 0.5f), Dep(copper, 0.14f, 0.72f, minDepth: 10), Dep(grubNest, 0.16f, 0.78f, minDepth: 6));
            EnsureDeposit(topsoil, grubNest, 0.16f, 0.78f, 6);

            // Fill drops on materials that have none (keeps hand-edited assets intact).
            EnsureDrop(copper, copperItem);
            EnsureDrop(iron, ironItem);
            EnsureDrop(coal, coalItem);
            EnsureDrop(amber, amberItem);
            EnsureDrop(fossil, fossilItem);
            EnsureDrop(crystal, crystalItem);
            EnsureDrop(grubNest, grubItem);
            var clayBiome = Biome("clay", "Clay & Groundwater", 180, clay, 0.022f, 0.56f,
                Dep(dirt, 0.05f, 0.6f), Dep(iron, 0.13f, 0.72f), Dep(copper, 0.15f, 0.8f));
            var strata = Biome("stone_strata", "Stone Strata", 400, stone, 0.018f, 0.5f,
                Dep(coal, 0.1f, 0.66f), Dep(iron, 0.14f, 0.78f), Dep(amber, 0.18f, 0.82f), Dep(fossil, 0.16f, 0.84f), Dep(crystal, 0.11f, 0.9f));

            var worldGen = LoadOrCreate<WorldGenSettings>(WorldGenPath, g =>
            {
                g.air = air;
                g.surfaceMaterial = grass;
                g.bedrock = bedrock;
                g.biomes = new List<BiomeDef> { topsoil, clayBiome, strata };
            });
            LoadOrCreate<GameConfig>(ConfigPath, _ => { });

            // Milestone 3: transport, machines and their recipes.
            var smeltCopper = Recipe("smelt_copper", "Smelt copper", 4f, new[] { Amt(copperItem, 2) }, new[] { Amt(copperBar, 1) });
            var smeltIron = Recipe("smelt_iron", "Smelt iron", 6f, new[] { Amt(ironItem, 2), Amt(coalItem, 1) }, new[] { Amt(ironBar, 1) });
            var growFungus = Recipe("grow_fungus", "Grow fungus", 10f, new[] { Amt(seedItem, 2) }, new[] { Amt(fungusItem, 3) });
            Building("silk_belt", "Silk Belt", BuildingKind.Belt, 0, "#efe6cc", 0.3f, speed: 2.5f, cost: new[] { Amt(copperItem, 1) },
                description: "Carries items left or right. Drag to lay a line; the drag direction sets the flow.");
            Building("root_lift", "Root Lift", BuildingKind.Lift, 1, "#9a7448", 0.5f, speed: 3f, cost: new[] { Amt(copperItem, 2) },
                description: "Carries items up or down a shaft.");
            Building("splitter", "Splitter", BuildingKind.Splitter, 2, "#e6d39a", 1f, speed: 3f, cost: new[] { Amt(copperItem, 3) },
                description: "Hands items in turn to every belt, lift or machine around it.");
            Building("filter", "Filter", BuildingKind.Filter, 3, "#7fc3e8", 1.5f, speed: 3f, cost: new[] { Amt(copperBar, 2) },
                description: "The chosen item leaves in the arrow direction; everything else goes to the other sides. Click it to change the item.");
            Building("smelter", "Smelter", BuildingKind.Processor, 10, "#b8562c", 8f, size: new Vector2Int(3, 3),
                cost: new[] { Amt(copperItem, 12) }, recipes: new[] { smeltCopper, smeltIron },
                description: "2 copper ore -> 1 copper. 2 iron ore + 1 coal -> 1 iron.", art: PlaceholderArt.Kiln);
            var fungusGarden = Building("fungus_garden", "Fungus Garden", BuildingKind.Processor, 11, "#9b7cb4", 6f, size: new Vector2Int(4, 2),
                cost: new[] { Amt(copperItem, 8) }, recipes: new[] { growFungus },
                description: "2 seeds -> 3 fungus (3 food each). Feed it the seeds your workers forage.", art: PlaceholderArt.Garden);

            CreateProgression(copperItem, ironItem, coalItem, amberItem, fossilItem, crystalItem, seedItem, grubItem, copperBar, ironBar,
                copper, iron, coal, fungusGarden);

            var database = LoadOrCreate<ContentDatabase>(DatabasePath, db => db.worldGen = worldGen);
            if (database.surfaceForage.Count == 0)
            {
                database.surfaceForage.Add(seedItem);
                EditorUtility.SetDirty(database);
            }
            RebuildDatabase();
            AssetDatabase.SaveAssets();
            return database;
        }

        /// <summary>Milestone 4: knowledge (Archive), research tree, abilities, contracts and random events.</summary>
        static void CreateProgression(ItemDef copperOre, ItemDef ironOre, ItemDef coal, ItemDef amber, ItemDef fossil, ItemDef crystal,
            ItemDef seed, ItemDef grub, ItemDef copper, ItemDef iron, TerrainMaterialDef copperMat, TerrainMaterialDef ironMat,
            TerrainMaterialDef coalMat, BuildingDef fungusGarden)
        {
            var star = Item("star_fragment", "Star Fragment", "#b9a7ff");
            var meteorite = Mat("meteorite", "Meteorite", 6f, PixelPattern.Crystal, "#3b3446", "#26212e", "#8a7fa0", "#b9a7ff", 0.6f,
                value: 15, emission: "#9d8cff", emissionStrength: 0.35f, glow: 0.35f);
            EnsureDrop(meteorite, star);

            var studyCopper = Recipe("study_copper", "Study metalwork", 8f, new[] { Amt(copper, 1) }, new ItemAmount[0], knowledge: 1f);
            var studyCrystal = Recipe("study_crystal", "Study a crystal shard", 6f, new[] { Amt(crystal, 1) }, new ItemAmount[0], knowledge: 4f);
            var studyAmber = Recipe("study_amber", "Study amber", 8f, new[] { Amt(amber, 1) }, new ItemAmount[0], knowledge: 5f);
            var studyFossil = Recipe("study_fossil", "Study a fossil", 8f, new[] { Amt(fossil, 1) }, new ItemAmount[0], knowledge: 6f);
            var studyStar = Recipe("study_star", "Study a star fragment", 10f, new[] { Amt(star, 1) }, new ItemAmount[0], knowledge: 12f);
            var archive = Building("archive", "Archive", BuildingKind.Processor, 12, "#c9b27c", 10f, size: new Vector2Int(3, 2),
                cost: new[] { Amt(copperOre, 10) }, recipes: new[] { studyCopper, studyCrystal, studyAmber, studyFossil, studyStar },
                description: "Studies finds into Knowledge for research: copper 1, crystal shard 4, amber 5, fossil 6, star fragment 12.",
                art: PlaceholderArt.Archive);
            var splitter = AssetDatabase.LoadAssetAtPath<BuildingDef>($"{BuildingsFolder}/splitter.asset");
            var filter = AssetDatabase.LoadAssetAtPath<BuildingDef>($"{BuildingsFolder}/filter.asset");

            // Abilities.
            var frenzy = Ability("frenzy", "Frenzy", AbilityKind.Frenzy, 0, "#ff9a3c", 120f,
                "All workers move, dig and build 75% faster for 20 seconds.", duration: 20f, magnitude: 0.75f);
            var seismic = Ability("seismic_sense", "Seismic Sense", AbilityKind.SeismicSense, 1, "#7fd8ff", 90f,
                "Reveals every ore, grub nest and crystal within 24 cells of the cursor, even in unexplored darkness.", radius: 24f);
            var decree = Ability("royal_decree", "Royal Decree", AbilityKind.RoyalDecree, 2, "#f0d060", 240f,
                "The queen lays 4 eggs at once, free of food.", count: 4);

            // Research tree: columns are tiers, rows are branches (brood, digging, logistics, colony).
            var fungiculture = Research("fungiculture", "Fungiculture", 0, 0, 8f, "Unlocks the Fungus Garden: seeds become fungus worth more food.",
                effects: new[] { ResearchEffect.Unlock(fungusGarden) });
            var mandibles = Research("strong_mandibles", "Strong Mandibles", 0, 1, 10f, "Workers and you dig 25% faster.",
                cost: new[] { Amt(copperOre, 6) },
                effects: new[] { ResearchEffect.Add(StatKind.WorkerDig, 0.25f), ResearchEffect.Add(StatKind.PlayerDig, 0.25f) });
            var routing = Research("silk_routing", "Silk Routing", 0, 2, 12f, "Unlocks the Splitter and the Filter.",
                cost: new[] { Amt(copper, 4) },
                effects: new[] { ResearchEffect.Unlock(splitter), ResearchEffect.Unlock(filter) });
            var seismicNode = Research("seismic_sense", "Seismic Sense", 0, 3, 12f, "Unlocks the Seismic Sense ability (X).",
                effects: new[] { ResearchEffect.Unlock(seismic) });

            var jelly = Research("royal_jelly", "Royal Jelly", 1, 0, 20f, "The queen lays 25% faster and up to 2 more eggs wait at once.",
                foodCost: 20f, prerequisites: new[] { fungiculture },
                effects: new[] { ResearchEffect.Add(StatKind.LayingSpeed, 0.25f), ResearchEffect.Add(StatKind.MaxEggs, 2f) });
            var legs = Research("quick_legs", "Quick Legs", 1, 1, 20f, "Workers walk 15% faster.", prerequisites: new[] { mandibles },
                effects: new[] { ResearchEffect.Add(StatKind.WorkerSpeed, 0.15f) });
            var weaving = Research("silk_weaving", "Silk Weaving", 1, 2, 20f, "Belts and lifts move items 35% faster.",
                cost: new[] { Amt(copper, 6) }, prerequisites: new[] { routing },
                effects: new[] { ResearchEffect.Add(StatKind.BeltSpeed, 0.35f) });
            var vault = Research("royal_vault", "Royal Vault", 1, 3, 16f, "The royal chamber holds 40 more items and the colony 60 more food.",
                cost: new[] { Amt(copperOre, 10) }, prerequisites: new[] { mandibles },
                effects: new[] { ResearchEffect.Add(StatKind.RoyalCapacity, 40f), ResearchEffect.Add(StatKind.FoodCapacity, 60f) });

            var decreeNode = Research("royal_decree", "Royal Decree", 2, 0, 35f, "Unlocks the Royal Decree ability (C).",
                foodCost: 40f, prerequisites: new[] { jelly }, effects: new[] { ResearchEffect.Unlock(decree) });
            var ironJaws = Research("iron_mandibles", "Iron Mandibles", 2, 1, 40f, "Workers dig 40% faster and build 30% faster.",
                cost: new[] { Amt(iron, 8) }, prerequisites: new[] { legs },
                effects: new[] { ResearchEffect.Add(StatKind.WorkerDig, 0.4f), ResearchEffect.Add(StatKind.BuildSpeed, 0.3f) });
            var kilns = Research("hot_kilns", "Hot Kilns", 2, 2, 35f, "Machines work 30% faster.",
                cost: new[] { Amt(iron, 6), Amt(coal, 6) }, prerequisites: new[] { weaving },
                effects: new[] { ResearchEffect.Add(StatKind.MachineSpeed, 0.3f) });
            var trails = Research("foraging_trails", "Foraging Trails", 2, 3, 25f, "Seeds appear on the surface 50% more often.",
                prerequisites: new[] { vault }, effects: new[] { ResearchEffect.Add(StatKind.ForageRate, 0.5f) });

            Research("dormancy", "Dormancy", 3, 0, 50f, "While the game is closed the colony earns 25% more (75% instead of 50%), for up to 4 more hours.",
                prerequisites: new[] { decreeNode },
                effects: new[] { ResearchEffect.Add(StatKind.OfflineEfficiency, 0.25f), ResearchEffect.Add(StatKind.OfflineHours, 4f) });
            Research("ancient_script", "Ancient Script", 3, 1, 40f, "Studying in the Archive gives 50% more knowledge.",
                cost: new[] { Amt(fossil, 2) }, prerequisites: new[] { ironJaws },
                effects: new[] { ResearchEffect.Add(StatKind.KnowledgeGain, 0.5f) });
            Research("contract_network", "Contract Network", 3, 2, 45f, "One more contract on the board.",
                cost: new[] { Amt(copper, 10) }, prerequisites: new[] { kilns },
                effects: new[] { ResearchEffect.Add(StatKind.ContractSlots, 1f) });
            Research("instinct", "Instinct", 3, 3, 40f, "Abilities recharge 25% faster.", prerequisites: new[] { trails, seismicNode },
                effects: new[] { ResearchEffect.Add(StatKind.AbilityRecharge, 0.25f) });

            // Contracts: a short intro chain, then repeatables.
            var smelter = AssetDatabase.LoadAssetAtPath<BuildingDef>($"{BuildingsFolder}/smelter.asset");
            Contract("intro_dig", "Breaking Ground", ContractKind.DigCells, 40, intro: 1, knowledge: 4f, food: 10f,
                description: "Dig out 40 cells: yourself (1) or with mining orders (2).");
            Contract("intro_copper", "First Stockpile", ContractKind.Deliver, 10, intro: 2, item: copperOre, knowledge: 5f,
                description: "Bring 10 copper ore into the stock.");
            Contract("intro_depth", "Going Deeper", ContractKind.ReachDepth, 40, intro: 3, knowledge: 6f, food: 15f,
                description: "Dig a cell 40 deep.");
            Contract("intro_smelter", "Fire in the Hole", ContractKind.Build, 1, intro: 4, building: smelter, knowledge: 6f,
                rewards: new[] { Amt(copperOre, 6) }, description: "Build a Smelter (4).");
            Contract("intro_brood", "A Growing Brood", ContractKind.HatchAnts, 10, intro: 5, knowledge: 8f, food: 20f,
                description: "Hatch 10 new workers.");
            Contract("intro_archive", "Keepers of Knowledge", ContractKind.Build, 1, intro: 6, building: archive, knowledge: 10f,
                description: "Build an Archive and study your finds for research (T).");

            Contract("deliver_copper_ore", "Copper Haul", ContractKind.Deliver, 30, item: copperOre, knowledge: 6f, growth: 0.35f);
            Contract("deliver_grubs", "Grub Feast", ContractKind.Deliver, 8, item: grub, knowledge: 5f, food: 10f, growth: 0.3f);
            Contract("deliver_seeds", "Seed Gathering", ContractKind.Deliver, 15, item: seed, knowledge: 4f, growth: 0.3f, weight: 0.8f);
            Contract("craft_copper", "Copper Smithing", ContractKind.Craft, 10, item: copper, knowledge: 8f, growth: 0.3f, building: null);
            Contract("dig_cells", "Tunnel Works", ContractKind.DigCells, 150, knowledge: 5f, food: 15f, growth: 0.3f);
            Contract("dig_deeper", "Delve", ContractKind.DigDeeper, 30, knowledge: 6f, growth: 0.2f, weight: 1.2f);
            Contract("hatch_ants", "Brood Boom", ContractKind.HatchAnts, 15, knowledge: 8f, food: 20f, growth: 0.3f);
            Contract("deliver_iron_ore", "Iron Haul", ContractKind.Deliver, 15, item: ironOre, knowledge: 8f, growth: 0.3f, minDepth: 110);
            Contract("craft_iron", "Ironworks", ContractKind.Craft, 6, item: iron, knowledge: 12f, growth: 0.3f, minDepth: 110);
            Contract("deliver_coal", "Coal Run", ContractKind.Deliver, 10, item: coal, knowledge: 8f, growth: 0.3f, minDepth: 290);
            Contract("deliver_fossils", "Bone Collectors", ContractKind.Deliver, 3, item: fossil, knowledge: 15f, growth: 0.25f, minDepth: 290);

            // Random events.
            RandomEvent("meteorite_strike", "Meteorite Strike", RandomEventKind.Meteorite, 1f,
                "A meteorite crashed near the nest. Dig it out for star fragments (study them in the Archive).",
                radius: 2.5f, materials: new[] { meteorite });
            RandomEvent("rich_vein", "Rich Vein", RandomEventKind.RichVein, 1.2f,
                "The ground shifted and exposed a rich vein next to your tunnels. Order it mined!",
                radius: 3.5f, materials: new[] { copperMat, ironMat, coalMat });
            RandomEvent("golden_beetle", "Golden Beetle", RandomEventKind.GoldenBeetle, 1f,
                "A golden beetle is wandering your tunnels. Click it before it leaves!",
                duration: 45f, knowledge: 8f, food: 20f, rewards: new[] { Amt(amber, 2) });
            RandomEvent("trapped_ants", "Trapped Ants", RandomEventKind.TrappedAnts, 0.8f,
                "A cave-in trapped a group of ants nearby. Dig them out before their air runs out!",
                duration: 150f, radius: 6f, count: 5, minDepth: 10);
        }

        static AbilityDef Ability(string id, string displayName, AbilityKind kind, int order, string colorHex, float cooldown, string description,
            float duration = 0f, float magnitude = 0f, float radius = 1f, int count = 1)
        {
            return LoadOrCreate<AbilityDef>($"{AbilitiesFolder}/{id}.asset", a =>
            {
                a.EditorSetIdentity(id, displayName);
                a.kind = kind;
                a.order = order;
                a.color = Hex(colorHex);
                a.cooldown = cooldown;
                a.description = description;
                a.duration = duration;
                a.magnitude = magnitude;
                a.radius = radius;
                a.count = count;
            });
        }

        static ResearchDef Research(string id, string displayName, int column, int row, float knowledge, string description,
            ItemAmount[] cost = null, float foodCost = 0f, ResearchDef[] prerequisites = null, ResearchEffect[] effects = null)
        {
            return LoadOrCreate<ResearchDef>($"{ResearchFolder}/{id}.asset", r =>
            {
                r.EditorSetIdentity(id, displayName);
                r.treePosition = new Vector2Int(column, row);
                r.knowledgeCost = knowledge;
                r.description = description;
                r.cost = cost?.ToList() ?? new List<ItemAmount>();
                r.foodCost = foodCost;
                r.prerequisites = prerequisites?.ToList() ?? new List<ResearchDef>();
                r.effects = effects?.ToList() ?? new List<ResearchEffect>();
            });
        }

        static ContractDef Contract(string id, string displayName, ContractKind kind, int amount, int intro = 0, ItemDef item = null,
            BuildingDef building = null, float knowledge = 0f, float food = 0f, ItemAmount[] rewards = null, float growth = 0.3f,
            float weight = 1f, int minDepth = 0, string description = null)
        {
            return LoadOrCreate<ContractDef>($"{ContractsFolder}/{id}.asset", c =>
            {
                c.EditorSetIdentity(id, displayName);
                c.kind = kind;
                c.amount = amount;
                c.introOrder = intro;
                c.repeatable = intro == 0;
                c.item = item;
                c.building = building;
                c.rewardKnowledge = knowledge;
                c.rewardFood = food;
                c.rewards = rewards?.ToList() ?? new List<ItemAmount>();
                c.growth = growth;
                c.weight = weight;
                c.minDepth = minDepth;
                c.description = description;
            });
        }

        static RandomEventDef RandomEvent(string id, string displayName, RandomEventKind kind, float weight, string description,
            float duration = 0f, float radius = 3f, int count = 1, int minDepth = 0, TerrainMaterialDef[] materials = null,
            float knowledge = 0f, float food = 0f, ItemAmount[] rewards = null)
        {
            return LoadOrCreate<RandomEventDef>($"{EventsFolder}/{id}.asset", e =>
            {
                e.EditorSetIdentity(id, displayName);
                e.kind = kind;
                e.weight = weight;
                e.description = description;
                e.duration = duration;
                e.radius = radius;
                e.count = count;
                e.minDepth = minDepth;
                e.materials = materials?.ToList() ?? new List<TerrainMaterialDef>();
                e.rewardKnowledge = knowledge;
                e.rewardFood = food;
                e.rewards = rewards?.ToList() ?? new List<ItemAmount>();
            });
        }

        /// <summary>Collects every TerrainMaterialDef in the project into the ContentDatabase.</summary>
        [MenuItem("Antarium/Content/Rebuild Database")]
        public static void RebuildDatabase()
        {
            var database = AssetDatabase.LoadAssetAtPath<ContentDatabase>(DatabasePath);
            if (database == null)
            {
                Debug.LogError($"No ContentDatabase at {DatabasePath}. Run Antarium/Setup/Create Default Content.");
                return;
            }
            database.materials = AssetDatabase.FindAssets("t:" + nameof(TerrainMaterialDef))
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<TerrainMaterialDef>)
                .ToList();
            database.items = FindAll<ItemDef>();
            database.recipes = FindAll<RecipeDef>();
            database.buildings = FindAll<BuildingDef>();
            database.research = FindAll<ResearchDef>();
            database.abilities = FindAll<AbilityDef>();
            database.contracts = FindAll<ContractDef>();
            database.events = FindAll<RandomEventDef>();
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();
            Debug.Log($"ContentDatabase rebuilt: {database.materials.Count} materials, {database.items.Count} items, " +
                      $"{database.buildings.Count} buildings, {database.recipes.Count} recipes, {database.research.Count} research, " +
                      $"{database.abilities.Count} abilities, {database.contracts.Count} contracts, {database.events.Count} events.");
        }

        static List<T> FindAll<T>() where T : Object
        {
            return AssetDatabase.FindAssets("t:" + typeof(T).Name)
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .ToList();
        }

        static TerrainMaterialDef Mat(string id, string displayName, float hardness, PixelPattern pattern,
            string baseHex, string shadowHex, string highlightHex, string accentHex, float accentAmount,
            MaterialFlags flags = MaterialFlags.None, string top = null, int value = 0,
            string emission = null, float emissionStrength = 0f, float glow = 0f)
        {
            return LoadOrCreate<TerrainMaterialDef>($"{MaterialsFolder}/{id}.asset", m =>
            {
                m.EditorSetIdentity(id, displayName);
                m.hardness = hardness;
                m.flags = flags;
                m.value = value;
                m.pattern = pattern;
                m.baseColor = Hex(baseHex);
                m.shadowColor = Hex(shadowHex);
                m.highlightColor = Hex(highlightHex);
                m.accentColor = Hex(accentHex);
                m.accentAmount = accentAmount;
                m.topRimColor = top != null ? Hex(top) : new Color(0, 0, 0, 0);
                m.emissionColor = emission != null ? Hex(emission) : Color.black;
                m.emissionStrength = emissionStrength;
                m.selfGlow = glow;
            });
        }

        static BiomeDef Biome(string id, string displayName, int thickness, TerrainMaterialDef baseMaterial,
            float caveScale, float caveThreshold, params BiomeDef.Deposit[] deposits)
        {
            return LoadOrCreate<BiomeDef>($"{BiomesFolder}/{id}.asset", b =>
            {
                b.EditorSetIdentity(id, displayName);
                b.thickness = thickness;
                b.baseMaterial = baseMaterial;
                b.caveScale = caveScale;
                b.caveThreshold = caveThreshold;
                b.deposits = deposits.ToList();
            });
        }

        static ItemDef Item(string id, string displayName, string colorHex, float food = 0f)
        {
            return LoadOrCreate<ItemDef>($"{ItemsFolder}/{id}.asset", i =>
            {
                i.EditorSetIdentity(id, displayName);
                i.color = Hex(colorHex);
                i.foodValue = food;
            });
        }

        static ItemAmount Amt(ItemDef item, int count) => new(item, count);

        static RecipeDef Recipe(string id, string displayName, float duration, ItemAmount[] inputs, ItemAmount[] outputs, float knowledge = 0f)
        {
            return LoadOrCreate<RecipeDef>($"{RecipesFolder}/{id}.asset", r =>
            {
                r.EditorSetIdentity(id, displayName);
                r.duration = duration;
                r.knowledge = knowledge;
                r.inputs = inputs.ToList();
                r.outputs = outputs.ToList();
            });
        }

        static BuildingDef Building(string id, string displayName, BuildingKind kind, int order, string colorHex, float work,
            float speed = 2f, Vector2Int? size = null, ItemAmount[] cost = null, RecipeDef[] recipes = null, string description = null,
            PlaceholderArt art = PlaceholderArt.Block)
        {
            return LoadOrCreate<BuildingDef>($"{BuildingsFolder}/{id}.asset", b =>
            {
                b.EditorSetIdentity(id, displayName);
                b.kind = kind;
                b.paletteOrder = order;
                b.color = Hex(colorHex);
                b.buildWork = work;
                b.speed = speed;
                b.size = size ?? Vector2Int.one;
                b.cost = cost?.ToList() ?? new List<ItemAmount>();
                b.recipes = recipes?.ToList() ?? new List<RecipeDef>();
                b.description = description;
                b.art = art;
            });
        }

        static void EnsureDrop(TerrainMaterialDef material, ItemDef item)
        {
            if (material.drop != null) return;
            material.drop = item;
            EditorUtility.SetDirty(material);
        }

        static void EnsureDeposit(BiomeDef biome, TerrainMaterialDef material, float scale, float threshold, int minDepth)
        {
            if (biome.deposits.Any(d => d.material == material)) return;
            biome.deposits.Add(Dep(material, scale, threshold, minDepth));
            EditorUtility.SetDirty(biome);
        }

        static BiomeDef.Deposit Dep(TerrainMaterialDef material, float scale, float threshold, int minDepth = 0, int maxDepth = 0) =>
            new() { material = material, scale = scale, threshold = threshold, minDepth = minDepth, maxDepth = maxDepth };

        static T LoadOrCreate<T>(string path, System.Action<T> init) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            init(asset);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
