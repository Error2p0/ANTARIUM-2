using Antarium.Content;
using UnityEditor;

namespace Antarium.Editor
{
    public static partial class ContentSetup
    {
        /// <summary>
        /// Milestone 10.2: the surface. Leaves (from surface plants) and root fibre (from roots in the topsoil), the Fungus
        /// Garden's leaf recipe, the Aphid Ranch (leaves into honeydew, which the queen eats to lay faster), the golden aphid
        /// event, the wolf spider that hunts at night, research and contracts. Existing assets are only extended.
        /// </summary>
        static void CreateSurface(ContentDatabase database, WorldGenSettings worldGen, ItemDef copperOre, ItemDef copper, ItemDef fungus,
            ItemDef chitin)
        {
            ResearchDef LoadResearch(string id) => AssetDatabase.LoadAssetAtPath<ResearchDef>($"{ResearchFolder}/{id}.asset");
            BuildingDef LoadBuilding(string id) => AssetDatabase.LoadAssetAtPath<BuildingDef>($"{BuildingsFolder}/{id}.asset");

            var leaf = Item("leaf", "Leaf", "#7cc04a");
            var honeydew = Item("honeydew", "Honeydew", "#f2d65a");
            var fibre = Item("root_fibre", "Root Fibre", "#c8a070");

            // Roots: tough strands under every surface plant.
            var root = Mat("root", "Root", 4f, PixelPattern.Strata, "#3a2412", "#24160a", "#7a4e28", "#a8743c", 0.45f, MaterialFlags.Flammable);
            if (root.drop == null)
            {
                EnsureDrop(root, fibre);
                root.dropChance = 0.5f;
                EditorUtility.SetDirty(root);
            }
            if (worldGen.rootMaterial == null)
            {
                worldGen.rootMaterial = root;
                EditorUtility.SetDirty(worldGen);
            }

            // Fungus from leaves: a better ratio than seeds.
            var leafFungus = Recipe("grow_fungus_leaves", "Grow fungus on leaves", 10f, new[] { Amt(leaf, 2) }, new[] { Amt(fungus, 4) });
            var garden = LoadBuilding("fungus_garden");
            if (garden != null && !garden.recipes.Contains(leafFungus))
            {
                garden.recipes.Add(leafFungus);
                garden.description = "2 seeds -> 3 fungus, or 2 leaves -> 4 fungus (3 food each). Feed it what your workers forage; press I to pick one recipe.";
                EditorUtility.SetDirty(garden);
            }

            var herd = Recipe("herd_aphids", "Milk the aphids", 8f, new[] { Amt(leaf, 2) }, new[] { Amt(honeydew, 1) });
            var ranch = Building("aphid_ranch", "Aphid Ranch", BuildingKind.Processor, 16, "#9ccf5a", 7f, size: new UnityEngine.Vector2Int(4, 2),
                cost: new[] { Amt(copperOre, 8), Amt(fibre, 4) }, recipes: new[] { herd },
                description: "Aphids graze on leaves: 2 leaves -> 1 honeydew. The queen eats honeydew from the stock: she lays 50% faster and needs no food of her own while she does.",
                art: PlaceholderArt.Ranch);

            var fungiculture = LoadResearch("fungiculture");
            var herding = Research("aphid_herding", "Aphid Herding", 1, 8, 18f,
                "Unlocks the Aphid Ranch: aphids turn leaves into honeydew, a treat that makes the queen lay faster.",
                cost: new[] { Amt(fibre, 3) }, prerequisites: Prereq(fungiculture), effects: new[] { ResearchEffect.Unlock(ranch) });

            RandomEvent("golden_aphid", "Golden Aphid", RandomEventKind.GoldenAphid, 0.8f,
                "A golden aphid is grazing in one of your Aphid Ranches. Click it before it flies off!",
                duration: 40f, knowledge: 10f, food: 10f, rewards: new[] { Amt(honeydew, 3) });

            var wolfSpider = Enemy("wolf_spider", "Wolf Spider", EnemyBehavior.NightHunter, EnemyArt.WolfSpider, 45f, 7f, 0.9f, 3.2f, 12f,
                "A night hunter of the surface. It lurks near your entrances after dark and slinks off at dawn.",
                drops: chitin != null ? new[] { Amt(chitin, 1) } : null, knowledge: 3f);

            Contract("deliver_leaves", "Leaf Litter", ContractKind.Deliver, 12, item: leaf, knowledge: 5f, growth: 0.3f, weight: 0.8f);
            Contract("deliver_root_fibre", "Root Diggers", ContractKind.Deliver, 6, item: fibre, knowledge: 6f, growth: 0.3f, weight: 0.7f);
            Contract("craft_honeydew", "Sweet Harvest", ContractKind.Craft, 10, item: honeydew, knowledge: 12f, food: 15f, growth: 0.3f, requires: herding);

            bool dirty = false;
            if (!database.plantForage.Contains(leaf)) { database.plantForage.Add(leaf); dirty = true; }
            if (database.royalTreat == null) { database.royalTreat = honeydew; dirty = true; }
            if (!database.nightHunters.Contains(wolfSpider)) { database.nightHunters.Add(wolfSpider); dirty = true; }
            if (dirty) EditorUtility.SetDirty(database);
        }
    }
}
