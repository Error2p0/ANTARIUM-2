using System.Collections.Generic;
using UnityEngine;

namespace Antarium.Content
{
    public enum EnemyBehavior : byte
    {
        /// <summary>Lives in a natural cave and wakes when the colony breaks in; hunts ants within its aggro radius.</summary>
        CaveDweller,
        /// <summary>Comes from the surface in a raid, heads for the royal chamber, eats food and leaves when full.</summary>
        Raider,
    }

    /// <summary>Which generated placeholder sprite an enemy uses until real art exists.</summary>
    public enum EnemyArt : byte
    {
        Termite,
        Centipede,
        Spider,
        Mite,
        FireBeetle,
        Guardian,
    }

    /// <summary>A hostile creature: deep-cave dwellers per biome, and raiders from the surface.</summary>
    [CreateAssetMenu(menuName = "Antarium/Enemy", fileName = "Enemy")]
    public sealed class EnemyDef : ContentDef
    {
        [TextArea] public string description;
        public EnemyBehavior behavior;

        [Header("Combat")]
        [Min(1f)] public float health = 30f;
        [Tooltip("Damage per bite.")]
        [Min(0f)] public float damage = 6f;
        [Tooltip("Seconds between bites.")]
        [Min(0.1f)] public float attackInterval = 1f;
        [Tooltip("Cells per second.")]
        [Min(0.1f)] public float speed = 2.4f;
        [Tooltip("Cave dwellers start hunting ants within this many cells (path distance).")]
        [Min(1f)] public float aggroRadius = 14f;

        [Header("Hazards")]
        [Tooltip("Walks through lava unharmed.")]
        public bool lavaImmune;
        [Tooltip("Does not drown in deep water.")]
        public bool swims;
        [Tooltip("Breathes toxic gas unharmed.")]
        public bool gasImmune;

        [Header("Raiders")]
        [Tooltip("Food a raider eats per second in the royal chamber.")]
        [Min(0f)] public float foodPerSecond = 1f;
        [Tooltip("A raider leaves once it has eaten this much.")]
        [Min(0f)] public float carryFood = 10f;

        [Header("Loot")]
        public List<ItemAmount> drops = new();
        [Min(0f)] public float knowledge;

        [Header("Presentation")]
        public EnemyArt art;
    }
}
