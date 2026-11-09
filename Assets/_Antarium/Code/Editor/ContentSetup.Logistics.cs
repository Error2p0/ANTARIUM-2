using System.Linq;
using Antarium.Content;
using UnityEditor;
using UnityEngine;

namespace Antarium.Editor
{
    public static partial class ContentSetup
    {
        /// <summary>
        /// Milestone 10.1: chests, unloaders, the crusher (crushed ore smelts at a better ratio), iron belts / lifts / gates,
        /// and the research, contracts and achievements for them. Existing assets are only extended, never overwritten.
        /// </summary>
        static void CreateLogistics(ItemDef copperOre, ItemDef ironOre, ItemDef coal, ItemDef copper, ItemDef iron)
        {
            ResearchDef LoadResearch(string id) => AssetDatabase.LoadAssetAtPath<ResearchDef>($"{ResearchFolder}/{id}.asset");
            BuildingDef LoadBuilding(string id) => AssetDatabase.LoadAssetAtPath<BuildingDef>($"{BuildingsFolder}/{id}.asset");

            var crushedCopper = Item("crushed_copper", "Crushed Copper", "#e8a070");
            var crushedIron = Item("crushed_iron", "Crushed Iron", "#d6d2cc");

            // Crushing 2 ore gives 3 crushed ore, and 2 crushed ore smelt into 1 bar: 75% more metal per ore than smelting it raw.
            var crushCopper = Recipe("crush_copper", "Crush copper ore", 3f, new[] { Amt(copperOre, 2) }, new[] { Amt(crushedCopper, 3) });
            var crushIron = Recipe("crush_iron", "Crush iron ore", 4f, new[] { Amt(ironOre, 2) }, new[] { Amt(crushedIron, 3) });
            var smeltCrushedCopper = Recipe("smelt_crushed_copper", "Smelt crushed copper", 3f, new[] { Amt(crushedCopper, 2) }, new[] { Amt(copper, 1) });
            var smeltCrushedIron = Recipe("smelt_crushed_iron", "Smelt crushed iron", 5f, new[] { Amt(crushedIron, 2), Amt(coal, 1) }, new[] { Amt(iron, 1) });
            var smelter = LoadBuilding("smelter");
            if (smelter != null && !smelter.recipes.Contains(smeltCrushedCopper))
            {
                smelter.recipes.Add(smeltCrushedCopper);
                smelter.recipes.Add(smeltCrushedIron);
                smelter.description = "2 copper ore -> 1 copper. 2 iron ore + 1 coal -> 1 iron. Crushed ore smelts better: " +
                                      "2 crushed copper -> 1 copper, 2 crushed iron + 1 coal -> 1 iron. Click it with I to pick one recipe.";
                EditorUtility.SetDirty(smelter);
            }

            var chest = Building("chest", "Chest", BuildingKind.Chest, 7, "#8a6a3c", 1.5f, cost: new[] { Amt(copperOre, 4) },
                description: "Stores 16 items in one cell. Press I on it to choose which items it takes. Belts ending at it fill it.");
            if (chest.capacity != 16)
            {
                chest.capacity = 16;
                EditorUtility.SetDirty(chest);
            }
            var unloader = Building("unloader", "Unloader", BuildingKind.Unloader, 8, "#9aa4b0", 1.5f, speed: 1.5f, cost: new[] { Amt(copper, 3) },
                description: "Takes items out of the storage, chest or royal chamber behind it and puts them onto the belt or machine its arrow points at. " +
                             "R turns it; press I on it to pick one item (or any).");
            var crusher = Building("crusher", "Crusher", BuildingKind.Processor, 15, "#8b8f99", 6f, size: new Vector2Int(2, 2),
                cost: new[] { Amt(copperOre, 10), Amt(copper, 4) }, recipes: new[] { crushCopper, crushIron },
                description: "2 copper ore -> 3 crushed copper. 2 iron ore -> 3 crushed iron. Crushed ore smelts into more metal.",
                art: PlaceholderArt.Crusher);
            var ironBelt = Building("iron_belt", "Iron Belt", BuildingKind.Belt, 5, "#aeb8c6", 0.4f, speed: 5f, cost: new[] { Amt(iron, 1) },
                description: "Twice as fast as a silk belt. Drag it over silk belts to upgrade them in place.");
            var ironLift = Building("iron_lift", "Iron Lift", BuildingKind.Lift, 6, "#8e98a6", 0.6f, speed: 6f, cost: new[] { Amt(iron, 1), Amt(copperOre, 1) },
                description: "Twice as fast as a root lift. Drag it over root lifts to upgrade them in place.");
            var ironGate = Building("iron_gate", "Iron Gate", BuildingKind.Gate, 24, "#aeb8c6", 4f, cost: new[] { Amt(iron, 3), Amt(copper, 2) },
                description: "Like a resin gate with three times the hit points. Place it over a resin gate to upgrade it.",
                art: PlaceholderArt.Gate, maxHealth: 450f);
            foreach (var tiered in new[] { ironBelt, ironLift, ironGate })
            {
                if (tiered.tier == 1) continue;
                tiered.tier = 1;
                EditorUtility.SetDirty(tiered);
            }

            // Research row 7: logistics.
            var routing = LoadResearch("silk_routing");
            var weaving = LoadResearch("silk_weaving");
            var fortification = LoadResearch("fortification");
            var stockkeeping = Research("stockkeeping", "Stockkeeping", 1, 7, 15f,
                "Unlocks the Unloader: stock can finally go back onto belts, straight into a machine or along a production line.",
                cost: new[] { Amt(copper, 2) }, prerequisites: Prereq(routing), effects: new[] { ResearchEffect.Unlock(unloader) });
            var crushing = Research("ore_crushing", "Ore Crushing", 2, 7, 30f,
                "Unlocks the Crusher: crushed ore smelts into 75% more copper and iron.",
                cost: new[] { Amt(copper, 4) }, prerequisites: Prereq(stockkeeping), effects: new[] { ResearchEffect.Unlock(crusher) });
            var reinforced = Research("reinforced_silk", "Reinforced Silk", 3, 7, 45f,
                "Unlocks Iron Belts and Iron Lifts: twice as fast, laid over silk ones to upgrade them.",
                cost: new[] { Amt(iron, 6) }, prerequisites: new[] { crushing, weaving }.Where(r => r != null).ToArray(),
                effects: new[] { ResearchEffect.Unlock(ironBelt), ResearchEffect.Unlock(ironLift) });
            Research("iron_bulwark", "Iron Bulwark", 4, 7, 55f,
                "Unlocks the Iron Gate: three times the hit points of a resin gate.",
                cost: new[] { Amt(iron, 8) }, prerequisites: new[] { reinforced, fortification }.Where(r => r != null).ToArray(),
                effects: new[] { ResearchEffect.Unlock(ironGate) });

            Contract("build_chests", "Orderly Stores", ContractKind.Build, 3, building: chest, knowledge: 6f, growth: 0.3f, weight: 0.7f,
                description: "Build chests to sort your stock.");
            Contract("craft_crushed_copper", "Rock Crusher", ContractKind.Craft, 20, item: crushedCopper, knowledge: 10f, growth: 0.3f,
                requires: crushing);
            Contract("build_iron_belts", "Iron Lines", ContractKind.Build, 20, building: ironBelt, knowledge: 14f, growth: 0.35f, requires: reinforced);

            int order = AssetDatabase.FindAssets("t:" + nameof(AchievementDef)).Length;
            Achievement("quartermaster", "ACH_CHESTS_10", "Quartermaster", "Build 10 chests in one colony.", AchievementKind.Build, 10, order++, building: chest);
            Achievement("iron_highway", "ACH_IRON_BELTS_100", "Iron Highway", "Build 100 iron belts in one colony.", AchievementKind.Build, 100, order++, building: ironBelt);
        }
    }
}
