using System.Collections.Generic;
using System.Linq;
using Antarium.Content;
using UnityEditor;
using UnityEngine;

namespace Antarium.Editor
{
    public static partial class ContentSetup
    {
        public const string LoreFolder = Root + "/Lore";

        /// <summary>
        /// Milestone 10.5: the deep endgame. Crystal resonance (the Crystal Resonator, glow crystals resonate), the ruins' story
        /// (12 lore fragments found by studying relics and tablets; tablets are set into ruin floors), the ancient research tier
        /// behind the lore and the Ancient Engine, rival ants that steal from the stock, the mole cricket that digs, contracts
        /// and achievements. Existing assets are only extended, never overwritten.
        /// </summary>
        static void CreateRuins(ContentDatabase database, WorldGenSettings worldGen)
        {
            EnsureFolder(LoreFolder);
            ItemDef LoadItem(string id) => AssetDatabase.LoadAssetAtPath<ItemDef>($"{ItemsFolder}/{id}.asset");
            ResearchDef LoadResearch(string id) => AssetDatabase.LoadAssetAtPath<ResearchDef>($"{ResearchFolder}/{id}.asset");
            TerrainMaterialDef LoadMaterial(string id) => AssetDatabase.LoadAssetAtPath<TerrainMaterialDef>($"{MaterialsFolder}/{id}.asset");
            var relic = LoadItem("relic");
            var crystal = LoadItem("crystal_shard");
            var copper = LoadItem("copper");
            var iron = LoadItem("iron");
            var amber = LoadItem("amber");
            var obsidian = LoadItem("obsidian");
            var chitin = LoadItem("chitin");

            // Crystal resonance: glow crystals resonate; copper and iron ore only carry it through the rock.
            var glowCrystal = LoadMaterial("glow_crystal");
            if (glowCrystal != null && glowCrystal.resonance <= 0f)
            {
                glowCrystal.resonance = 1f;
                EditorUtility.SetDirty(glowCrystal);
            }

            // Tablets: a unique find set into the floors of ruin rooms.
            var tabletItem = Item("tablet", "Ancient Tablet", "#f0c850");
            var tablet = Mat("tablet", "Ancient Tablet", 6f, PixelPattern.Brick, "#6e5a3a", "#4a3a24", "#f0d070", "#ffe7a0", 0.6f,
                MaterialFlags.LightEmitting, value: 60, emission: "#ffd070", emissionStrength: 0.45f, glow: 0.5f);
            EnsureDrop(tablet, tabletItem);
            if (worldGen.tabletMaterial == null)
            {
                worldGen.tabletMaterial = tablet;
                EditorUtility.SetDirty(worldGen);
            }

            // The Archive studies tablets (and relics, as before) and finds the lore that way.
            var studyTablet = Recipe("study_tablet", "Study a tablet", 20f, new[] { Amt(tabletItem, 1) }, new ItemAmount[0], knowledge: 60f);
            var archive = AssetDatabase.LoadAssetAtPath<BuildingDef>($"{BuildingsFolder}/archive.asset");
            if (archive != null && !archive.recipes.Contains(studyTablet))
            {
                archive.recipes.Add(studyTablet);
                archive.description = "Studies finds into Knowledge for research: copper 1, crystal shard 4, amber 5, fossil 6, star fragment 12, " +
                                      "relic 25, tablet 60. Relics and tablets also reveal the ruins' story (the Codex).";
                EditorUtility.SetDirty(archive);
            }

            // Buildings.
            var resonator = Building("crystal_resonator", "Crystal Resonator", BuildingKind.Resonator, 18, "#7fd8ff", 8f,
                size: new Vector2Int(2, 2), cost: new[] { Amt(crystal, 8), Amt(copper, 4), Amt(iron, 2) }, art: PlaceholderArt.Resonator,
                description: "Build it on a floor touching glow crystals. The crystals it reaches through conductive rock (glow crystals; copper " +
                             "and iron ore carry the resonance) make knowledge and speed up machines within 8 cells by up to 30%. " +
                             "24 crystals give full power; mining them weakens it.");
            if (resonator.knowledgePerSecond <= 0f)
            {
                resonator.knowledgePerSecond = 0.25f;
                resonator.fullResonance = 24f;
                resonator.maxClusterCells = 400;
                resonator.machineBoost = 0.3f;
                resonator.boostRadius = 8f;
                EditorUtility.SetDirty(resonator);
            }
            var ruinsBiome = worldGen.biomes.FirstOrDefault(b => b != null && b.rooms);
            var engine = Building("ancient_engine", "Ancient Engine", BuildingKind.Engine, 19, "#e8c860", 30f,
                size: new Vector2Int(4, 3), cost: new[] { Amt(relic, 4), Amt(copper, 10) }, art: PlaceholderArt.Engine,
                description: "The great machine of the lost colony, rebuilt on a ruin floor. Restore it step by step (I or click it): iron gears, " +
                             "a crystal heart, then the waking. Running, it makes knowledge, and the colony's Nuptial Flight earns twice the " +
                             "Genetic Memory. One per colony; it can't be taken down.");
            if (engine.stages.Count == 0)
            {
                engine.unique = true;
                engine.requiredBiome = ruinsBiome;
                engine.knowledgePerSecond = 1f;
                engine.stages = new List<BuildingStage>
                {
                    Stage("Iron Gears", "Three rings of iron, each driving the next.", 40f, 0f, Amt(iron, 30), Amt(copper, 10)),
                    Stage("Crystal Heart", "Crystals set in amber, where the gears can hear them sing.", 40f, 0f, Amt(crystal, 30), Amt(amber, 6)),
                    Stage("The Waking", "Relics for its memory, obsidian for its skin, food for the hands that turn it.", 60f, 150f,
                        Amt(relic, 8), Amt(obsidian, 12)),
                };
                EditorUtility.SetDirty(engine);
            }

            // Research: Crystal Resonance (tier 6) and the ancient tier 7, which asks for lore.
            var glowAnts = LoadResearch("glow_ants");
            var carapace = LoadResearch("hardened_carapace");
            var resonance = Research("crystal_resonance", "Crystal Resonance", 5, 4, 70f,
                "Unlocks the Crystal Resonator: glow crystals that hum together make knowledge and speed up the machines around them.",
                cost: new[] { Amt(crystal, 10), Amt(copper, 4) }, prerequisites: Prereq(glowAnts), effects: new[] { ResearchEffect.Unlock(resonator) });
            var masonry = Research("ancient_masonry", "Ancient Masonry", 6, 0, 120f,
                "What the old builders knew: ants build 30% faster, gates and traps are 25% stronger. Needs 3 lore fragments.",
                cost: new[] { Amt(relic, 4) }, prerequisites: Prereq(carapace),
                effects: new[] { ResearchEffect.Add(StatKind.BuildSpeed, 0.3f), ResearchEffect.Add(StatKind.DefenseStrength, 0.25f) });
            var lattice = Research("resonant_lattice", "Resonant Lattice", 6, 4, 150f,
                "Machines tuned to the crystals' hum: every machine works 25% faster. Needs 5 lore fragments.",
                cost: new[] { Amt(relic, 6), Amt(crystal, 12) }, prerequisites: Prereq(resonance),
                effects: new[] { ResearchEffect.Add(StatKind.MachineSpeed, 0.25f) });
            var engineResearch = Research("ancient_engine", "The Ancient Engine", 6, 2, 200f,
                "Unlocks the Ancient Engine, the lost colony's great machine. Build it in the ruins and restore it: running, it doubles the " +
                "Genetic Memory of this colony's Nuptial Flight. Needs 8 lore fragments.",
                cost: new[] { Amt(relic, 8) }, prerequisites: new[] { masonry, lattice },
                effects: new[] { ResearchEffect.Unlock(engine) });
            SetLore(masonry, 3);
            SetLore(lattice, 5);
            SetLore(engineResearch, 8);

            // The ruins' story: nine fragments from relics, three from tablets.
            int order = 0;
            Lore("old_marks", "A Marked Stone", order++, relic, 1,
                "The relic is covered in tiny scratches, row after row, like the trails ants leave in soft clay. Someone wrote here. Ants wrote " +
                "here, long before your queen's grandmother hatched.");
            Lore("old_colony", "The Old Colony", order++, relic, 2,
                "The marks name a colony that once filled these depths: the Old Colony. Its tunnels ran from the roots down to the fire, and its " +
                "queens lived for a hundred summers.");
            Lore("singing_stone", "Singing Stone", order++, relic, 3,
                "The Old Colony listened to the crystals. Where many grew together the stone hummed, and the hum carried thoughts from chamber " +
                "to chamber. They built their halls around the loudest clusters.");
            Lore("hollow_halls", "The Hollow Halls", order++, relic, 5,
                "Their chambers were square and their walls were brick, baked hard by the heat from below. Every hall had a door at the floor " +
                "and a hatch in the roof, so no ant was ever more than a short climb from home.");
            Lore("guardians", "The Guardians", order++, relic, 7,
                "When the soldiers grew too few, the engineers shaped guardians out of stone and crystal to keep the relic halls. They were told " +
                "to wait. They are still waiting.");
            Lore("the_engine", "The Engine", order++, relic, 9,
                "At the bottom of everything the Old Colony built the Engine: iron gears turned by the warmth of the deep, and a heart of " +
                "crystal that sang to every hall at once. While it turned, the colony thought as one.");
            Lore("long_quiet", "The Long Quiet", order++, relic, 12,
                "Then the fire below grew restless. Lava filled the lowest halls, the crystals cracked and the Engine's song faltered. Without " +
                "it the colony's thoughts drifted apart, chamber by chamber.");
            Lore("last_flight", "The Last Flight", order++, relic, 15,
                "The last queen raised her daughters in the dark and sent them up toward a sky none of them had ever seen. The marks do not say " +
                "whether any of them reached it.");
            Lore("who_reads", "Who Reads This", order++, relic, 18,
                "The last relic is newer than the rest, its marks quick and uneven: \"To whoever digs this deep: you are our daughters' " +
                "daughters. Wake the Engine, and remember us.\"");
            Lore("tablet_gears", "Tablet: The Gears", order++, tabletItem, 1,
                "A plan inlaid in gold: three rings of iron, each driving the next, set on a bed of stone where the deep warmth rises. " +
                "\"Iron for the bones of the Engine.\"");
            Lore("tablet_heart", "Tablet: The Heart", order++, tabletItem, 2,
                "A cluster of crystals set in a ring of amber. \"A heart that sings must be fed with light. Set the crystals where the gears " +
                "can hear them.\"");
            Lore("tablet_waking", "Tablet: The Waking", order++, tabletItem, 3,
                "The whole Engine, and around it ants without number. \"It wakes when the colony is strong enough to feed it: relics for its " +
                "memory, obsidian for its skin, food for the hands that turn it.\"");

            // Threats: rival ants raid the stock; mole crickets dig toward the nest.
            Enemy("rival_ant", "Rival Ant", EnemyBehavior.Thief, EnemyArt.RivalAnt, 28f, 5f, 1f, 3.2f, 6f,
                "An ant from a rival colony on the surface. Rival raids go for your stock, not your food: each thief carries off up to 3 items " +
                "of what you have most of. Kill it and it drops them.",
                drops: chitin != null ? new[] { Amt(chitin, 1) } : null, knowledge: 2f);
            var rival = AssetDatabase.LoadAssetAtPath<EnemyDef>($"{EnemiesFolder}/rival_ant.asset");
            if (rival != null && rival.stealSeconds == 1f && rival.carryItems == 3)
            {
                rival.stealSeconds = 1.2f;
                EditorUtility.SetDirty(rival);
            }
            var mole = Enemy("mole_cricket", "Mole Cricket", EnemyBehavior.Digger, EnemyArt.MoleCricket, 140f, 12f, 1.2f, 2f, 18f,
                "A big burrower that tunnels toward the nest through soft ground (dirt, clay, sand, earth walls); it goes around rock. " +
                "Once it breaks into your tunnels it hunts ants.",
                drops: chitin != null ? new[] { Amt(chitin, 3) } : null, knowledge: 12f);
            if (mole.digPower == 2f)
            {
                mole.digPower = 1f;
                mole.maxDigHardness = 2f;
                EditorUtility.SetDirty(mole);
            }

            Contract("build_resonators", "Singing Stone", ContractKind.Build, 1, building: resonator, knowledge: 20f, growth: 0.4f, weight: 0.7f,
                requires: resonance, description: "Build Crystal Resonators where glow crystals grow thick.");

            int achievementOrder = AssetDatabase.FindAssets("t:" + nameof(AchievementDef)).Length;
            Achievement("crystal_choir", "ACH_RESONATORS_5", "Crystal Choir", "Build 5 Crystal Resonators in one colony.", AchievementKind.Build, 5,
                achievementOrder++, building: resonator);
            Achievement("keeper_of_the_codex", "ACH_CODEX", "Keeper of the Codex", "Find every lore fragment of the ruins in one colony.",
                AchievementKind.LoreFound, 12, achievementOrder++);
            Achievement("the_engine_wakes", "ACH_ANCIENT_ENGINE", "The Engine Wakes", "Restore the Ancient Engine.", AchievementKind.EngineRestored, 1,
                achievementOrder++, hidden: true);
            Achievement("not_one_crumb", "ACH_RIVALS_3", "Not One Crumb", "Beat 3 raids of rival ants without losing an item.",
                AchievementKind.RivalsRepelled, 3, achievementOrder++);
        }

        static BuildingStage Stage(string name, string description, float work, float food, params ItemAmount[] cost) => new()
        {
            name = name,
            description = description,
            buildWork = work,
            foodCost = food,
            cost = cost.Where(c => c.item != null).ToList(),
        };

        static void SetLore(ResearchDef research, int lore)
        {
            if (research == null || research.loreRequired > 0) return;
            research.loreRequired = lore;
            EditorUtility.SetDirty(research);
        }

        static LoreDef Lore(string id, string title, int order, ItemDef item, int count, string text)
        {
            return LoadOrCreate<LoreDef>($"{LoreFolder}/{id}.asset", l =>
            {
                l.EditorSetIdentity(id, title);
                l.order = order;
                l.item = item;
                l.count = count;
                l.text = text;
            });
        }
    }
}
