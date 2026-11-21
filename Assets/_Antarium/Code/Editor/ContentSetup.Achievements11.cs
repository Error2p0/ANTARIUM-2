using Antarium.Content;
using UnityEditor;

namespace Antarium.Editor
{
    public static partial class ContentSetup
    {
        /// <summary>
        /// Milestone 11 achievements: wild food (apples, berries, cave and glowing mushrooms, wild food in all), the Elder Tree and
        /// its golden apples (hidden: they give the tree away), the giant earthworm and starfall nights. Existing ones are left as
        /// they are; new ones go after the last.
        /// </summary>
        static void CreateMilestone11Achievements()
        {
            ItemDef Item(string id) => AssetDatabase.LoadAssetAtPath<ItemDef>($"{ItemsFolder}/{id}.asset");
            int order = AssetDatabase.FindAssets("t:" + nameof(AchievementDef)).Length;

            void Add(string id, string api, string name, string description, AchievementKind kind, long amount, ItemDef item = null, bool hidden = false)
            {
                if (AssetDatabase.LoadAssetAtPath<AchievementDef>($"{AchievementsFolder}/{id}.asset") != null) return;
                if ((kind == AchievementKind.Deliver) && item == null) return; // the item doesn't exist (yet)
                Achievement(id, api, name, description, kind, amount, order++, item: item, hidden: hidden);
            }

            Add("berry_season", "ACH_BERRIES_50", "Berry Season", "Bring 50 berries home in one colony.", AchievementKind.Deliver, 50, Item("berry"));
            Add("an_apple_a_day", "ACH_APPLES_25", "An Apple a Day", "Bring 25 apples home in one colony.", AchievementKind.Deliver, 25, Item("apple"));
            Add("mushroom_hunter", "ACH_MUSHROOMS_50", "Mushroom Hunter", "Bring 50 cave mushrooms home in one colony.", AchievementKind.Deliver, 50, Item("mushroom"));
            Add("glow_in_the_dark", "ACH_GLOW_MUSHROOMS_20", "Glow in the Dark", "Bring 20 glowing mushrooms home in one colony.", AchievementKind.Deliver, 20,
                Item("glow_mushroom"));
            Add("living_off_the_land", "ACH_WILD_FOOD_300", "Living off the Land",
                "Bring 300 wild foods home in one colony: berries, apples, toadstools and mushrooms.", AchievementKind.WildFood, 300);
            Add("starfall", "ACH_STARFALL", "Starfall", "See a starfall night.", AchievementKind.StarfallNights, 1);
            Add("worm_hunter", "ACH_GIANT_WORM", "Worm Hunter", "Slay a giant earthworm.", AchievementKind.WormsSlain, 1);
            Add("the_elder_tree", "ACH_ELDER_TREE", "The Elder Tree", "Find the ancient tree that grows deep in the stone.", AchievementKind.ElderTreeFound, 1,
                hidden: true);
            Add("golden_harvest", "ACH_GOLDEN_APPLES_10", "Golden Harvest", "Bring 10 golden apples home in one colony.", AchievementKind.Deliver, 10,
                Item("golden_apple"), hidden: true);
        }
    }
}
