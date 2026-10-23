using UnityEngine;

namespace Antarium.Content
{
    /// <summary>
    /// Kinds of ant. The value is stored in saves: append new castes, never reorder. Behaviour is code per caste;
    /// the numbers live in a <see cref="CasteDef"/>.
    /// </summary>
    public enum AntCaste : byte
    {
        /// <summary>Generalist: digs, hauls, builds and supplies machines.</summary>
        Worker,
        /// <summary>Guards and fights; never takes jobs.</summary>
        Soldier,
        /// <summary>Digs only, much faster.</summary>
        Miner,
        /// <summary>Hauls and supplies machines, carrying several items at once.</summary>
        Hauler,
        /// <summary>Carries eggs from the royal chamber to nurseries.</summary>
        Nurse,
        /// <summary>Builds only, much faster.</summary>
        Builder,
        /// <summary>Explores the edge of the known tunnels, sensing ore through the fog; catches golden beetles.</summary>
        Scout,
        /// <summary>Tends working machines, which then work faster.</summary>
        Engineer,
    }

    public static class Castes
    {
        public const int Count = 8;
    }

    /// <summary>
    /// Numbers of one ant caste. Workers start unlocked; the others are unlocked by research and laid by the queen
    /// according to the caste mix the player sets. Job weights scale the distance to a job (lower = preferred);
    /// 0 means the caste never takes that job.
    /// </summary>
    [CreateAssetMenu(menuName = "Antarium/Caste", fileName = "Caste")]
    public sealed class CasteDef : ContentDef
    {
        public AntCaste caste;
        [TextArea] public string description;
        [Tooltip("Position in the castes panel (lower first).")]
        public int order;

        [Header("Brood")]
        [Tooltip("Food the queen spends on one egg of this caste.")]
        [Min(0f)] public float eggFoodCost = 6f;
        [Tooltip("Food per minute one ant of this caste eats.")]
        [Min(0f)] public float upkeep = 0.25f;
        [Tooltip("Share of the colony set when the caste is unlocked (workers fill the rest).")]
        [Range(0f, 1f)] public float defaultShare = 0.1f;

        [Header("Body")]
        [Min(1f)] public float health = 40f;
        [Tooltip("Damage per second dealt to an enemy next to it.")]
        [Min(0f)] public float attack = 3f;
        [Tooltip("Multiplies the base walking speed (GameConfig.worker.moveSpeed).")]
        [Min(0.1f)] public float speed = 1f;
        [Tooltip("Multiplies the base dig power (GameConfig.worker.digPower).")]
        [Min(0f)] public float dig = 1f;
        [Tooltip("Multiplies the base build power (GameConfig.logistics.buildPower).")]
        [Min(0f)] public float build = 1f;
        [Tooltip("Items of one kind carried at once.")]
        [Range(1, 8)] public int carry = 1;

        [Header("Jobs (distance multipliers, 0 = never)")]
        [Min(0f)] public float digJob = 1f;
        [Min(0f)] public float haulJob = 1f;
        [Min(0f)] public float buildJob = 1f;
        [Min(0f)] public float supplyJob = 1f;

        [Header("Specialists")]
        [Tooltip("Scout: cells around it revealed as it walks (ore found there glows).")]
        [Min(0)] public int senseRadius = 10;
        [Tooltip("Engineer: extra machine speed while it tends a machine (0.5 = +50%).")]
        [Min(0f)] public float machineBoost = 0.5f;

        [Header("Presentation")]
        [Tooltip("Swatch colour in the castes panel.")]
        public Color color = Color.white;
    }
}
