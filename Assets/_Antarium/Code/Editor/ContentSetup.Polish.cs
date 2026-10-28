using Antarium.Content;
using UnityEditor;

namespace Antarium.Editor
{
    public static partial class ContentSetup
    {
        /// <summary>Milestone 8: achievements (Steam API names are final once the store page exists).</summary>
        static void CreateAchievements(ItemDef copper, ItemDef iron, ItemDef fungus)
        {
            T Load<T>(string folder, string id) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>($"{folder}/{id}.asset");
            var belt = Load<BuildingDef>(BuildingsFolder, "silk_belt");
            var relic = Load<ItemDef>(ItemsFolder, "relic");
            var star = Load<ItemDef>(ItemsFolder, "star_fragment");

            int order = 0;
            Achievement("breaking_ground", "ACH_BREAKING_GROUND", "Breaking Ground", "Dig out 25 cells.", AchievementKind.DigCells, 25, order++);
            Achievement("excavator", "ACH_EXCAVATOR", "Excavator", "Dig out 5,000 cells in one colony.", AchievementKind.DigCells, 5000, order++);
            Achievement("below_the_roots", "ACH_DEPTH_100", "Below the Roots", "Dig 100 cells deep.", AchievementKind.ReachDepth, 100, order++, stat: "STAT_MAX_DEPTH");
            Achievement("into_the_stone", "ACH_DEPTH_300", "Into the Stone", "Reach the stone strata, 300 cells deep.", AchievementKind.ReachDepth, 300, order++, stat: "STAT_MAX_DEPTH");
            Achievement("crystal_light", "ACH_DEPTH_700", "Crystal Light", "Reach the crystal caverns, 700 cells deep.", AchievementKind.ReachDepth, 700, order++, stat: "STAT_MAX_DEPTH");
            Achievement("heart_of_fire", "ACH_DEPTH_1050", "Heart of Fire", "Reach the magma zone, 1,050 cells deep.", AchievementKind.ReachDepth, 1050, order++, stat: "STAT_MAX_DEPTH");
            Achievement("lost_civilization", "ACH_DEPTH_1250", "Lost Civilization", "Reach the ancient ruins, 1,250 cells deep.", AchievementKind.ReachDepth, 1250, order++, hidden: true, stat: "STAT_MAX_DEPTH");
            Achievement("a_proper_colony", "ACH_ANTS_50", "A Proper Colony", "Have 50 ants at once.", AchievementKind.AntsAlive, 50, order++, stat: "STAT_MAX_ANTS");
            Achievement("bustling_nest", "ACH_ANTS_250", "Bustling Nest", "Have 250 ants at once.", AchievementKind.AntsAlive, 250, order++, stat: "STAT_MAX_ANTS");
            Achievement("superorganism", "ACH_ANTS_1000", "Superorganism", "Have 1,000 ants at once.", AchievementKind.AntsAlive, 1000, order++, stat: "STAT_MAX_ANTS");
            if (belt != null)
            {
                Achievement("silk_road", "ACH_FIRST_BELT", "Silk Road", "Build a silk belt.", AchievementKind.Build, 1, order++, building: belt);
                Achievement("factory_floor", "ACH_BELTS_200", "Factory Floor", "Build 200 silk belts in one colony.", AchievementKind.Build, 200, order++, building: belt);
            }
            Achievement("smelting", "ACH_FIRST_COPPER", "Smelting", "Smelt your first copper.", AchievementKind.Craft, 1, order++, item: copper);
            Achievement("iron_age", "ACH_IRON_50", "Iron Age", "Smelt 50 iron in one colony.", AchievementKind.Craft, 50, order++, item: iron);
            Achievement("fungal_farmer", "ACH_FUNGUS_200", "Fungal Farmer", "Grow 200 fungus in one colony.", AchievementKind.Craft, 200, order++, item: fungus);
            Achievement("curious_minds", "ACH_RESEARCH_5", "Curious Minds", "Finish 5 research nodes.", AchievementKind.Research, 5, order++);
            Achievement("scholar", "ACH_RESEARCH_20", "Scholar", "Finish 20 research nodes in one colony.", AchievementKind.Research, 20, order++);
            Achievement("archivist", "ACH_KNOWLEDGE_1000", "Archivist", "Earn 1,000 knowledge in one colony.", AchievementKind.KnowledgeEarned, 1000, order++);
            Achievement("contractor", "ACH_CONTRACTS_10", "Contractor", "Complete 10 contracts in one colony.", AchievementKind.Contracts, 10, order++);
            Achievement("first_blood", "ACH_FIRST_KILL", "First Blood", "Defeat an enemy.", AchievementKind.KillEnemies, 1, order++);
            Achievement("exterminator", "ACH_KILLS_200", "Exterminator", "Defeat 200 enemies in one colony.", AchievementKind.KillEnemies, 200, order++);
            Achievement("hold_the_gate", "ACH_RAID_REPELLED", "Hold the Gate", "Repel a raid before the raiders eat anything.", AchievementKind.RepelRaids, 1, order++);
            Achievement("fortress", "ACH_RAIDS_10", "Fortress", "Repel 10 raids in one colony.", AchievementKind.RepelRaids, 10, order++);
            Achievement("standing_army", "ACH_SOLDIERS_25", "Standing Army", "Hatch 25 soldiers in one colony.", AchievementKind.HatchCaste, 25, order++, caste: AntCaste.Soldier);
            Achievement("lucky_colony", "ACH_EVENTS_5", "Lucky Colony", "Win 5 random events in one colony.", AchievementKind.EventsWon, 5, order++);
            if (relic != null)
                Achievement("archaeologist", "ACH_RELICS_5", "Archaeologist", "Bring 5 relics into the stock.", AchievementKind.Deliver, 5, order++, item: relic, hidden: true);
            if (star != null)
                Achievement("stardust", "ACH_STAR_FRAGMENT", "Stardust", "Bring a star fragment into the stock.", AchievementKind.Deliver, 1, order++, item: star);
            Achievement("royal_heir", "ACH_FIRST_PRINCESS", "Royal Heir", "Raise a princess.", AchievementKind.PrincessesRaised, 1, order++);
            Achievement("nuptial_flight", "ACH_FIRST_FLIGHT", "Nuptial Flight", "Send princesses on a Nuptial Flight.", AchievementKind.Flights, 1, order++, stat: "STAT_FLIGHTS");
            Achievement("dynasty", "ACH_FLIGHTS_5", "Dynasty", "Found 5 colonies by Nuptial Flight.", AchievementKind.Flights, 5, order++, stat: "STAT_FLIGHTS");
            Achievement("bloodline", "ACH_TRAITS_10", "Bloodline", "Buy 10 levels of genetic traits.", AchievementKind.TraitLevels, 10, order++);
            Achievement("ancestral_memory", "ACH_MEMORY_500", "Ancestral Memory", "Earn 500 Genetic Memory over your bloodline.", AchievementKind.MemoryEarned, 500, order++, hidden: true);
        }

        static AchievementDef Achievement(string id, string apiName, string displayName, string description, AchievementKind kind, long amount,
            int order, ItemDef item = null, BuildingDef building = null, AntCaste caste = AntCaste.Worker, bool hidden = false, string stat = null)
        {
            return LoadOrCreate<AchievementDef>($"{AchievementsFolder}/{id}.asset", a =>
            {
                a.EditorSetIdentity(id, displayName);
                a.apiName = apiName;
                a.description = description;
                a.kind = kind;
                a.amount = amount;
                a.order = order;
                a.item = item;
                a.building = building;
                a.caste = caste;
                a.hidden = hidden;
                a.statName = stat ?? "";
            });
        }
    }
}
