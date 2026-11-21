using System;
using System.Collections.Generic;
using UnityEngine;

namespace Antarium.Content
{
    /// <summary>
    /// The Steam Next Fest demo (GDD 2.16): the first colony down to the bottom of <see cref="lastBiome"/>, where an
    /// unbreakable Ancient Seal ends the world. Research, evolutions and contracts that need what only lies below the seal
    /// are marked "full game". The same build of the game is a demo only when it is built with <c>ANTARIUM_DEMO</c> (or the
    /// editor opts in); without that, this asset only lets the full game break the seal of a colony carried over from the demo.
    /// </summary>
    [CreateAssetMenu(menuName = "Antarium/Demo Config", fileName = "DemoConfig")]
    public sealed class DemoConfig : ScriptableObject
    {
        [Header("The seal")]
        [Tooltip("The demo ends at the bottom of this biome: the seal follows its (warped) lower border.")]
        public BiomeDef lastBiome;
        [Tooltip("The unbreakable material of the seal (Indestructible).")]
        public TerrainMaterialDef sealMaterial;
        [Tooltip("Rows of seal in every column (more where the border is steep, so no tunnel can slip past it).")]
        [Min(2)] public int sealThickness = 4;
        [Tooltip("When the colony first reaches the seal, cells within this radius beyond it are revealed (a look into the depths).")]
        [Min(0)] public int glimpseRadius = 16;
        [Tooltip("The revealed window is centred this many cells below the seal.")]
        [Min(0)] public int glimpseDepth = 10;

        [Header("Full game only")]
        [Tooltip("Items that only lie below the seal (or not enough of them above it). Research, evolution levels and contracts " +
                 "that need them are full game only.")]
        public List<ItemDef> fullGameItems = new();
        [Tooltip("Research that is full game only whatever it costs (and everything that needs it first).")]
        public List<ResearchDef> fullGameResearch = new();

        [Header("End of the demo")]
        [Tooltip("Minutes of play after which the end screen is offered once at a calm moment, if the colony never reached the seal.")]
        [Min(0f)] public float endScreenMinutes = 60f;
        public string endTitle = "The Ancient Seal";
        [TextArea] public string endBodySeal = "Your colony has dug down to the Ancient Seal. Long ago the Old Colony sealed the deep here, and this is where the demo ends.";
        [TextArea] public string endBodyTime = "You have played the demo for a good while. Thank you! Far below, at the bottom of the stone strata, the Old Colony sealed the deep: that is where the demo ends.";
        [Tooltip("What the full game adds, one line each.")]
        public List<string> fullGameFeatures = new();
        [TextArea] public string carryOver = "Your demo colony carries over: the full game offers to continue it, and the seal opens.";
        [Tooltip("The Nuptial Flight / Genetic Memory teaser (Flight and Genes buttons in the demo).")]
        public string flightTitle = "Nuptial Flight";
        [TextArea] public string flightBody = "When a colony has grown, its princesses fly off to found the next one. Each flight earns Genetic Memory for permanent traits.";
        [Tooltip("Hover text of the seal.")]
        [TextArea] public string sealHover = "The Old Colony sealed the deep here. It can't be broken in the demo: in the full game the depths go on.";

        [Header("Steam")]
        [Tooltip("The full game on Steam (the Wishlist buttons open its store page).")]
        public uint fullGameAppId = 4453940;
        [Tooltip("The demo's own Steam App ID (Steamworks → the app → Create demo). 0 = the demo runs without Steam.")]
        public uint demoAppId;
    }

    /// <summary>
    /// <see cref="DemoConfig"/> baked to dense indices: which items, research nodes, evolution levels and contracts are full game
    /// only, and where the seal lies.
    /// </summary>
    public sealed class DemoData
    {
        public DemoConfig Def;
        /// <summary>Index into <see cref="WorldGenSettings.biomes"/> of the last biome of the demo.</summary>
        public int LastBiome;
        public ushort SealMaterial;
        /// <summary>Per item.</summary>
        public bool[] FullGameItem;
        /// <summary>Per research node: needs a full game item, is listed, or needs such a node first.</summary>
        public bool[] FullGameResearch;
        /// <summary>Per evolution, per level (0 = the first): the level costs a full game item.</summary>
        public bool[][] FullGameEvolution;
        /// <summary>Per contract: never offered in the demo (asks for full game items, research, buildings or depths).</summary>
        public bool[] FullGameContract;
        /// <summary>Per building: no research the demo allows unlocks it (and it does not start unlocked).</summary>
        public bool[] FullGameBuilding;
        /// <summary>Per caste: only full game research unlocks it.</summary>
        public bool[] FullGameCaste;

        /// <summary>Depth (cells below the surface) where the biome after <see cref="LastBiome"/> starts, before border warp.</summary>
        public int SealDepth(WorldGenSettings gen, float thicknessScale = 1f)
        {
            int depth = 0;
            for (int i = 0; i <= LastBiome && i < gen.biomes.Count; i++) depth += ContentDB.BiomeThickness(gen.biomes[i], thicknessScale);
            return depth;
        }

        /// <summary>Some research node, evolution level or contract is full game only because of <paramref name="cost"/>.</summary>
        bool NeedsFullGameItem(ushort[] cost)
        {
            if (cost == null) return false;
            foreach (ushort item in cost)
                if (item < FullGameItem.Length && FullGameItem[item]) return true;
            return false;
        }

        public static DemoData Bake(ContentDB content, DemoConfig def)
        {
            if (def == null) return null;
            if (def.sealMaterial == null) throw new ArgumentException("DemoConfig has no seal material.");
            if (!def.sealMaterial.flags.HasFlag(MaterialFlags.Indestructible))
                throw new ArgumentException($"The demo's seal material '{def.sealMaterial.Id}' must be Indestructible.");
            int last = content.WorldGen.biomes.IndexOf(def.lastBiome);
            if (last < 0) throw new ArgumentException("DemoConfig.lastBiome is not one of the world's biomes.");
            if (last >= content.WorldGen.biomes.Count - 1) throw new ArgumentException("DemoConfig.lastBiome is the deepest biome: nothing would be left for the full game.");

            var data = new DemoData
            {
                Def = def,
                LastBiome = last,
                SealMaterial = content.GetMaterialIndex(def.sealMaterial),
                FullGameItem = new bool[content.Items.Length],
                FullGameResearch = new bool[content.Research.Length],
                FullGameEvolution = new bool[content.Evolutions.Length][],
                FullGameContract = new bool[content.Contracts.Length],
                FullGameBuilding = new bool[content.Buildings.Length],
                FullGameCaste = new bool[Castes.Count],
            };
            foreach (var item in def.fullGameItems)
                if (item != null && content.TryGetItemIndex(item.Id, out ushort i)) data.FullGameItem[i] = true;

            // Research: listed, or a full game item in the cost; then everything that needs such a node (until nothing changes).
            for (int r = 0; r < content.Research.Length; r++)
            {
                var node = content.Research[r];
                data.FullGameResearch[r] = def.fullGameResearch.Contains(node.Def) || data.NeedsFullGameItem(node.CostItems);
            }
            for (bool changed = true; changed;)
            {
                changed = false;
                for (int r = 0; r < content.Research.Length; r++)
                {
                    if (data.FullGameResearch[r]) continue;
                    foreach (int p in content.Research[r].Prerequisites)
                        if (data.FullGameResearch[p])
                        {
                            data.FullGameResearch[r] = changed = true;
                            break;
                        }
                }
            }

            for (int e = 0; e < content.Evolutions.Length; e++)
            {
                var evo = content.Evolutions[e];
                var levels = new bool[evo.MaxLevel];
                for (int l = 0; l < levels.Length; l++)
                    levels[l] = data.NeedsFullGameItem(evo.CostItems[l]) || l > 0 && levels[l - 1];
                data.FullGameEvolution[e] = levels;
            }

            // Buildings and castes: locked ones that only full game research unlocks.
            var buildingOpen = new bool[content.Buildings.Length];
            for (int b = 0; b < buildingOpen.Length; b++) buildingOpen[b] = !content.BuildingStartsLocked[b];
            var casteOpen = new bool[Castes.Count];
            for (int c = 0; c < casteOpen.Length; c++) casteOpen[c] = !content.CasteStartsLocked[c];
            for (int r = 0; r < content.Research.Length; r++)
            {
                if (data.FullGameResearch[r]) continue;
                foreach (int b in content.Research[r].UnlockBuildings) buildingOpen[b] = true;
                foreach (var c in content.Research[r].UnlockCastes) casteOpen[(int)c] = true;
            }
            for (int e = 0; e < content.Evolutions.Length; e++)
                foreach (var effect in content.Evolutions[e].Effects)
                    if (effect.kind == EvolutionEffectKind.UnlockCaste && !data.FullGameEvolution[e][Math.Clamp(effect.level, 1, data.FullGameEvolution[e].Length) - 1])
                        casteOpen[(int)effect.caste] = true;
            for (int b = 0; b < buildingOpen.Length; b++) data.FullGameBuilding[b] = !buildingOpen[b];
            for (int c = 0; c < casteOpen.Length; c++) data.FullGameCaste[c] = !casteOpen[c];

            int sealDepth = data.SealDepth(content.WorldGen);
            for (int k = 0; k < content.Contracts.Length; k++)
            {
                var c = content.Contracts[k];
                var cd = c.Def;
                data.FullGameContract[k] =
                    c.Item != ContentDB.NoItem && data.FullGameItem[c.Item]
                    || c.Requires >= 0 && data.FullGameResearch[c.Requires]
                    || c.Building >= 0 && data.FullGameBuilding[c.Building]
                    || cd.kind == ContractKind.HatchCaste && data.FullGameCaste[(int)cd.caste]
                    || cd.minDepth >= sealDepth
                    || cd.kind == ContractKind.ReachDepth && cd.amount >= sealDepth;
            }
            return data;
        }
    }
}
