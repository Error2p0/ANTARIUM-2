using UnityEngine;

namespace Antarium.Content
{
    public enum AchievementKind : byte
    {
        /// <summary>Dig a cell at least <see cref="AchievementDef.amount"/> cells below the surface.</summary>
        ReachDepth,
        /// <summary>Cells dug out in one colony (workers and the player).</summary>
        DigCells,
        /// <summary>Ants hatched in one colony.</summary>
        HatchAnts,
        /// <summary>Ants of <see cref="AchievementDef.caste"/> hatched in one colony.</summary>
        HatchCaste,
        /// <summary>Ants alive at the same time.</summary>
        AntsAlive,
        /// <summary>Finished buildings of <see cref="AchievementDef.building"/> in one colony.</summary>
        Build,
        /// <summary><see cref="AchievementDef.item"/> delivered into the stock in one colony.</summary>
        Deliver,
        /// <summary><see cref="AchievementDef.item"/> made by machines in one colony.</summary>
        Craft,
        KillEnemies,
        RepelRaids,
        Contracts,
        /// <summary>Research nodes done in one colony.</summary>
        Research,
        KnowledgeEarned,
        PrincessesRaised,
        /// <summary>Nuptial Flights over the whole bloodline.</summary>
        Flights,
        /// <summary>Random events won in one colony.</summary>
        EventsWon,
        /// <summary>Genetic trait levels bought over the whole bloodline.</summary>
        TraitLevels,
        /// <summary>Genetic Memory earned over the whole bloodline.</summary>
        MemoryEarned,
    }

    /// <summary>
    /// A platform achievement (Steam). Checked against the colony's lifetime stats and the legacy; once unlocked it
    /// stays unlocked for the player, across colonies and saves. <see cref="apiName"/> must match the store's name.
    /// </summary>
    [CreateAssetMenu(menuName = "Antarium/Achievement", fileName = "Achievement")]
    public sealed class AchievementDef : ContentDef
    {
        [Tooltip("API name in Steamworks (Stats & Achievements). Never change it after release.")]
        public string apiName;
        [TextArea] public string description;
        public AchievementKind kind;
        public ItemDef item;
        public BuildingDef building;
        public AntCaste caste;
        [Min(1)] public long amount = 1;
        [Tooltip("Hidden on the store until unlocked; the in-game list shows '???'.")]
        public bool hidden;
        [Tooltip("Optional Steam stat that receives the progress (an INT stat with this API name). Empty = none.")]
        public string statName;
        [Tooltip("Order in the achievements list.")]
        public int order;
    }
}
