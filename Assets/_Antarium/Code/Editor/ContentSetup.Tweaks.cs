using System.Collections.Generic;
using Antarium.Content;
using UnityEditor;

namespace Antarium.Editor
{
    public static partial class ContentSetup
    {
        const string RivalAntText = "An ant from a rival colony on the surface. Rival raids go for your stores, not your food: each thief robs " +
                                    "the nearest storage cell, chest or royal chamber it can reach and carries off up to 3 items of what that store " +
                                    "holds most of. Kill it and it drops them.";

        /// <summary>
        /// Milestone 11 batch 3: the Guiding Light and Vein Instinct research, the giant worm (enemy and random event) and
        /// the hidden shooting star achievement. Existing assets are left as they are (the Vein Instinct trait's text is
        /// updated once, since its meaning changed).
        /// </summary>
        static void CreateTweaks()
        {
            T Load<T>(string folder, string id) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>($"{folder}/{id}.asset");
            var seismic = Load<ResearchDef>(ResearchFolder, "seismic_sense");
            var copperOre = Load<ItemDef>(ItemsFolder, "copper_ore");
            var prereq = seismic != null ? new[] { seismic } : null;

            Research("guiding_light", "Guiding Light", 0, 4, 15f,
                "A soft light follows your pointer and lights up the rock around it, like a glow ant's lantern but wider.",
                prerequisites: prereq, effects: new[] { ResearchEffect.Enable(ResearchFeature.CursorLight) });
            Research("vein_instinct", "Vein Instinct", 0, 5, 25f,
                "Ore and other finds next to every cell your colony digs out are ordered for mining automatically: ants follow veins on their own. " +
                "Switch it off and on with the Mine tool.",
                cost: copperOre != null ? new[] { Amt(copperOre, 10) } : null, prerequisites: prereq,
                effects: new[] { ResearchEffect.Enable(ResearchFeature.VeinInstinct) });

            const string veinTraitText = "Every colony knows Vein Instinct from the start, as if researched: ore next to every cell your colony digs out " +
                                         "is ordered for mining automatically.";
            var trait = Load<GeneticDef>(GeneticsFolder, "vein_instinct");
            if (trait != null && trait.description != veinTraitText)
            {
                trait.description = veinTraitText;
                EditorUtility.SetDirty(trait);
            }

            var rival = Load<EnemyDef>(EnemiesFolder, "rival_ant");
            if (rival != null && rival.description != RivalAntText)
            {
                rival.description = RivalAntText;
                EditorUtility.SetDirty(rival);
            }

            var worm = LoadOrCreate<EnemyDef>($"{EnemiesFolder}/giant_worm.asset", e =>
            {
                e.EditorSetIdentity("giant_worm", "Giant Earthworm");
                e.behavior = EnemyBehavior.Worm;
                e.art = EnemyArt.Worm;
                e.health = 400f;
                e.damage = 4f;
                e.attackInterval = 1.2f;
                e.speed = 1.6f;
                e.aggroRadius = 1f;
                e.digPower = 1.5f;
                e.maxDigHardness = 2f;
                e.swims = true;
                e.description = "A huge earthworm churning through the soft soil near your tunnels, leaving a tunnel behind it. It doesn't hunt, " +
                                "but thrashes at ants touching its head. Bite it anywhere along its body: soldiers, traps, a possessed ant, " +
                                "or click it with the Dig tool.";
                e.drops = new List<ItemAmount>();
                e.knowledge = 0f;
            });
            var wormEvent = RandomEvent("giant_worm", "Giant Earthworm", RandomEventKind.GiantWorm, 0.8f,
                "A giant earthworm is churning through the soil near your tunnels! Kill it before it burrows away: it's a feast " +
                "(food and knowledge), and its tunnels stay. Click it with the Dig tool, send soldiers, or possess an ant.",
                duration: 180f, minDepth: 15, knowledge: 25f, food: 80f);
            if (wormEvent.creature == null)
            {
                wormEvent.creature = worm;
                EditorUtility.SetDirty(wormEvent);
            }

            int order = AssetDatabase.FindAssets("t:" + nameof(AchievementDef)).Length;
            if (Load<AchievementDef>(AchievementsFolder, "wish_upon_a_star") == null)
                Achievement("wish_upon_a_star", "ACH_SHOOTING_STAR", "Wish Upon a Star", "Catch a shooting star.", AchievementKind.ShootingStars, 1,
                    order, hidden: true);
        }
    }
}
