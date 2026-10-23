using System.Collections.Generic;
using UnityEngine;

namespace Antarium.Content
{
    public enum ContractKind : byte
    {
        /// <summary>Deliver <see cref="ContractDef.item"/> into the stock (food items count too).</summary>
        Deliver,
        /// <summary>Machines make <see cref="ContractDef.item"/>.</summary>
        Craft,
        /// <summary>Dig a cell at least this deep (absolute depth below the surface).</summary>
        ReachDepth,
        /// <summary>Dig this many cells deeper than the deepest point when the contract started.</summary>
        DigDeeper,
        HatchAnts,
        /// <summary>Finish building <see cref="ContractDef.building"/>.</summary>
        Build,
        /// <summary>Dig out any cells (workers and the player).</summary>
        DigCells,
        /// <summary>Defeat this many enemies (creatures and raiders).</summary>
        KillEnemies,
        /// <summary>Hatch ants of <see cref="ContractDef.caste"/>.</summary>
        HatchCaste,
    }

    /// <summary>
    /// A goal on the contract board. Intro contracts (<see cref="introOrder"/> &gt; 0) are offered once, in
    /// order, one at a time; the other slots draw repeatable contracts at random, growing each time.
    /// </summary>
    [CreateAssetMenu(menuName = "Antarium/Contract", fileName = "Contract")]
    public sealed class ContractDef : ContentDef
    {
        [Tooltip("Shown under the title. Empty = generated from the goal.")]
        [TextArea] public string description;
        public ContractKind kind;
        public ItemDef item;
        public BuildingDef building;
        [Tooltip("Hatch caste: which caste counts.")]
        public AntCaste caste;
        [Min(1)] public int amount = 10;

        [Header("Offering")]
        [Tooltip("> 0: part of the intro chain, offered in this order and only once.")]
        [Min(0)] public int introOrder;
        public bool repeatable = true;
        [Tooltip("Repeatable: the amount and rewards grow by this factor every time it is completed (0.3 = +30%).")]
        [Min(0f)] public float growth = 0.3f;
        [Min(0f)] public float weight = 1f;
        [Tooltip("Only offered once the colony has dug this deep.")]
        [Min(0)] public int minDepth;
        [Tooltip("Only offered once this research is done.")]
        public ResearchDef requires;

        [Header("Rewards")]
        [Min(0f)] public float rewardKnowledge;
        [Min(0f)] public float rewardFood;
        public List<ItemAmount> rewards = new();
    }
}
