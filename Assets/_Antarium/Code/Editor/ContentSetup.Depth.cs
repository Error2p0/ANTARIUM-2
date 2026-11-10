using System.Collections.Generic;
using System.Linq;
using Antarium.Content;
using UnityEditor;
using UnityEngine;

namespace Antarium.Editor
{
    public static partial class ContentSetup
    {
        public const string EvolutionsFolder = Root + "/Evolutions";

        /// <summary>
        /// Milestone 10.4: colony depth. Glow ants and fire ants (research column 4, rows 4-5), heat in the magma zone, the
        /// queen's evolution tree (paid with rare finds from the stock) and its Quickening ability, contracts for the new
        /// castes. Existing assets are only extended, never overwritten.
        /// </summary>
        static void CreateColonyDepth(WorldGenSettings worldGen)
        {
            EnsureFolder(EvolutionsFolder);
            ItemDef LoadItem(string id) => AssetDatabase.LoadAssetAtPath<ItemDef>($"{ItemsFolder}/{id}.asset");
            ResearchDef LoadResearch(string id) => AssetDatabase.LoadAssetAtPath<ResearchDef>($"{ResearchFolder}/{id}.asset");
            var honeydew = LoadItem("honeydew");
            var amber = LoadItem("amber");
            var fossil = LoadItem("fossil");
            var star = LoadItem("star_fragment");
            var relic = LoadItem("relic");
            var chitin = LoadItem("chitin");
            var crystal = LoadItem("crystal_shard");
            var obsidian = LoadItem("obsidian");
            var iron = LoadItem("iron");

            // Castes: workers with a twist.
            var glow = Caste("glow", "Glow Ant", AntCaste.Glow, 8, "#7cf0c8",
                "A worker whose gaster glows: it lights the tunnels around it as it digs, hauls and builds, a little slower than a worker.",
                egg: 10f, upkeep: 0.35f, health: 35f, attack: 2f, dig: 0.8f, build: 0.8f);
            if (glow.lightRadius <= 0f)
            {
                glow.lightRadius = 6f;
                EditorUtility.SetDirty(glow);
            }
            var fire = Caste("fire", "Fire Ant", AntCaste.Fire, 9, "#ff7a2e",
                "A heat-proof worker for the magma zone: no harm from fire or heat, and only a quarter of the damage from lava. Tough and a strong digger.",
                egg: 12f, upkeep: 0.4f, health: 60f, attack: 6f, dig: 1.2f);
            if (!fire.heatProof)
            {
                fire.heatProof = true;
                fire.lavaDamageTaken = 0.25f;
                EditorUtility.SetDirty(fire);
            }

            // Nurses and engineers help out when their own job waits (their job and its priority usually come first):
            // nurses haul, engineers carry machine inputs. This is what the Tend and Nurse priorities weigh against.
            var nurse = AssetDatabase.LoadAssetAtPath<CasteDef>($"{CastesFolder}/nurse.asset");
            if (nurse != null && nurse.haulJob <= 0f)
            {
                nurse.haulJob = 2f;
                nurse.description = "Carries eggs from the queen to a Nursery, where they hatch faster and free the queen to lay more. " +
                                    "With no eggs to carry it hauls.";
                EditorUtility.SetDirty(nurse);
            }
            var engineer = AssetDatabase.LoadAssetAtPath<CasteDef>($"{CastesFolder}/engineer.asset");
            if (engineer != null && engineer.supplyJob <= 0f)
            {
                engineer.supplyJob = 2f;
                engineer.description = "Tends a working machine: it works 50% faster while an engineer stands by it. " +
                                       "With no machine to tend it carries machine inputs from storage.";
                EditorUtility.SetDirty(engineer);
            }

            // Heat: the magma zone itself is hot.
            var magma = worldGen.biomes.FirstOrDefault(b => b != null && b.Id == "magma_zone");
            if (magma != null && magma.heat <= 0f)
            {
                magma.heat = 0.8f;
                EditorUtility.SetDirty(magma);
            }

            // Research: the late castes in column 4.
            var scouts = LoadResearch("scout_caste");
            var engineers = LoadResearch("engineer_caste");
            var glowResearch = Research("glow_ants", "Glow Ants", 4, 4, 50f,
                "The queen can lay glow ants: workers that light up dark tunnels around them. Set their share in the castes panel (K).",
                cost: crystal != null ? new[] { Amt(crystal, 6) } : null, prerequisites: Prereq(scouts),
                effects: new[] { ResearchEffect.Unlock(AntCaste.Glow) });
            var fireResearch = Research("fire_ants", "Fire Ants", 4, 5, 60f,
                "The queen can lay fire ants: heat-proof workers for the magma zone, where heat slowly hurts every other ant.",
                cost: obsidian != null && iron != null ? new[] { Amt(obsidian, 4), Amt(iron, 2) } : null, prerequisites: Prereq(engineers),
                effects: new[] { ResearchEffect.Unlock(AntCaste.Fire) });

            // The Quickening: the ability the queen evolves.
            var quickening = Ability("quickening", "Quickening", AbilityKind.Quickening, 4, "#c8a8ff", 300f,
                "Every egg hatches at once: by the queen, in the nurseries and carried by nurses.");

            // Evolutions: two rows of three, each column needing the one before.
            var brood = Evolution("bigger_broods", "Bigger Broods", "evolution_brood", 0, 0,
                "The queen lays 15% faster per level.",
                new[] { EvolutionEffect.Add(StatKind.LayingSpeed, 0.15f) },
                Level(20f, (honeydew, 4)),
                Level(40f, (honeydew, 8), (amber, 2)),
                Level(60f, (honeydew, 12), (amber, 4), (star, 1)));
            var cradles = Evolution("royal_cradles", "Royal Cradles", "evolution_egg_slots", 0, 1,
                "2 more eggs can wait next to the queen per level.",
                new[] { EvolutionEffect.Add(StatKind.MaxEggs, 2f) },
                Level(15f, (amber, 2)),
                Level(30f, (honeydew, 6), (fossil, 3)),
                Level(45f, (star, 2), (relic, 1)));
            var hardy = Evolution("hardy_brood", "Hardy Brood", "evolution_health", 1, 0,
                "Every ant has 15% more hit points per level.",
                new[] { EvolutionEffect.Add(StatKind.AntHealth, 0.15f) },
                Level(0f, (amber, 2), (chitin, 2)),
                Level(0f, (amber, 4), (chitin, 4), (fossil, 1)),
                Level(0f, (fossil, 3), (relic, 1)));
            var mending = Evolution("quick_mending", "Quick Mending", "evolution_regeneration", 1, 1,
                "Ants heal 50% faster per level.",
                new[] { EvolutionEffect.Add(StatKind.AntRegen, 0.5f) },
                Level(0f, (honeydew, 4), (chitin, 3)),
                Level(0f, (star, 2), (chitin, 4)));
            var lineage = Evolution("deep_lineage", "Deep Lineage", "evolution_castes", 2, 0,
                "The queen lays late castes without research: level 1 glow ants, level 2 fire ants.",
                new[] { EvolutionEffect.Unlock(AntCaste.Glow, 1), EvolutionEffect.Unlock(AntCaste.Fire, 2) },
                Level(0f, (crystal, 6), (honeydew, 4)),
                Level(0f, (obsidian, 4), (relic, 2)));
            var quicken = Evolution("quickening", "Quickening", "evolution_ability", 2, 1,
                "Unlocks the Quickening ability: every egg hatches at once (5 minute recharge).",
                new[] { EvolutionEffect.Unlock(quickening) },
                Level(0f, (star, 3), (relic, 2), (honeydew, 6)));
            AddPrerequisite(hardy, brood);
            AddPrerequisite(mending, cradles);
            AddPrerequisite(lineage, hardy);
            AddPrerequisite(quicken, mending);

            Contract("hatch_glow_ants", "Lanterns", ContractKind.HatchCaste, 4, caste: AntCaste.Glow, knowledge: 15f, food: 20f, growth: 0.35f,
                requires: glowResearch, description: "Hatch glow ants to light the deep tunnels.");
            Contract("hatch_fire_ants", "Into the Fire", ContractKind.HatchCaste, 4, caste: AntCaste.Fire, knowledge: 18f, food: 20f, growth: 0.35f,
                requires: fireResearch, description: "Hatch fire ants to work the magma zone.");
        }

        static EvolutionLevel Level(float food, params (ItemDef item, int count)[] cost) => new()
        {
            foodCost = food,
            cost = cost.Where(c => c.item != null).Select(c => Amt(c.item, c.count)).ToList(),
        };

        static EvolutionDef Evolution(string id, string displayName, string icon, int column, int row, string description,
            EvolutionEffect[] effects, params EvolutionLevel[] levels)
        {
            return LoadOrCreate<EvolutionDef>($"{EvolutionsFolder}/{id}.asset", e =>
            {
                e.EditorSetIdentity(id, displayName);
                e.icon = icon;
                e.treePosition = new Vector2Int(column, row);
                e.description = description;
                e.effects = effects.ToList();
                e.levels = levels.ToList();
                e.prerequisites = new List<EvolutionDef>();
            });
        }

        static void AddPrerequisite(EvolutionDef evolution, EvolutionDef before)
        {
            if (evolution == null || before == null || evolution.prerequisites.Contains(before)) return;
            evolution.prerequisites.Add(before);
            EditorUtility.SetDirty(evolution);
        }
    }
}
