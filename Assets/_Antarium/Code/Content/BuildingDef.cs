using System.Collections.Generic;
using UnityEngine;

namespace Antarium.Content
{
    public enum BuildingKind : byte
    {
        /// <summary>Horizontal transport cell (left / right).</summary>
        Belt,
        /// <summary>Vertical transport cell (up / down).</summary>
        Lift,
        /// <summary>Hands items round-robin to the transport cells and machines around it.</summary>
        Splitter,
        /// <summary>Like a splitter, but items of the filter type only leave in its arrow direction.</summary>
        Filter,
        /// <summary>Multi-cell machine that runs recipes.</summary>
        Processor,
        /// <summary>Ants walk through; enemies must break it. Destroyed gates turn back into blueprints for repair.</summary>
        Gate,
        /// <summary>On a floor: hurts and holds the first enemy that steps on it, then re-arms.</summary>
        Trap,
        /// <summary>Soldiers wait at the nearest guard post when there is nothing to fight.</summary>
        GuardPost,
        /// <summary>Workers pack the cell back into solid soil (<see cref="BuildingDef.fillMaterial"/>); the building then disappears.</summary>
        Wall,
        /// <summary>Multi-cell brood chamber on a floor: nurses bring eggs here, where they hatch faster.</summary>
        Nursery,
        /// <summary>Keeps the pheromone of its kind (a <see cref="PheromoneKind"/> in the filter slot) from fading around it.</summary>
        Emitter,
    }

    /// <summary>Which generated placeholder sprite a machine uses until real art exists.</summary>
    public enum PlaceholderArt : byte
    {
        Block,
        Kiln,
        Garden,
        Archive,
        Gate,
        Trap,
        GuardPost,
        Wall,
        Nursery,
        Emitter,
        PrincessChamber,
    }

    /// <summary>Anything the player can place with the Build tool: transport cells and processing chambers.</summary>
    [CreateAssetMenu(menuName = "Antarium/Building", fileName = "Building")]
    public sealed class BuildingDef : ContentDef
    {
        public BuildingKind kind;
        [Tooltip("Position in the build palette (lower first).")]
        public int paletteOrder;
        [TextArea] public string description;

        [Header("Placement & construction")]
        [Tooltip("Footprint in cells. Transport cells are always 1x1.")]
        public Vector2Int size = Vector2Int.one;
        [Tooltip("Paid from the colony stock when placed; refunded when removed.")]
        public List<ItemAmount> cost = new();
        [Min(0f)] public float foodCost;
        [Tooltip("Seconds of work for one worker to build it.")]
        [Min(0.05f)] public float buildWork = 1f;

        [Header("Transport")]
        [Tooltip("Cells per second items move along this cell.")]
        [Min(0.1f)] public float speed = 2f;

        [Header("Processing")]
        public List<RecipeDef> recipes = new();
        [Tooltip("Input buffer per item type, in crafts' worth of that input.")]
        [Min(1)] public int inputBatches = 2;
        [Tooltip("Production pauses while this many products wait at the machine (no output belt).")]
        [Min(1)] public int outputLimit = 6;

        [Header("Defense")]
        [Tooltip("Gate: hit points before enemies break through.")]
        [Min(1f)] public float maxHealth = 120f;
        [Tooltip("Trap: damage dealt to the enemy that triggers it.")]
        [Min(0f)] public float trapDamage = 25f;
        [Tooltip("Trap: seconds the enemy is held in place.")]
        [Min(0f)] public float trapHold = 4f;
        [Tooltip("Trap: seconds to re-arm after triggering.")]
        [Min(0.1f)] public float trapRearm = 8f;
        [Tooltip("Wall: the solid material the cell becomes.")]
        public TerrainMaterialDef fillMaterial;

        [Header("Brood & pheromones")]
        [Tooltip("Nursery: eggs it holds.")]
        [Min(1)] public int eggCapacity = 6;
        [Tooltip("Nursery: eggs inside hatch this much faster (0.5 = +50%).")]
        [Min(0f)] public float hatchBonus = 0.5f;
        [Tooltip("Emitter: pheromone of its kind within this many cells never fades.")]
        [Min(1f)] public float emitterRadius = 10f;
        [Tooltip("Princess chamber: princesses shown resting in it.")]
        [Min(0)] public int princessSlots;

        [Header("Presentation")]
        [Tooltip("Colour of the palette swatch.")]
        public Color color = Color.white;
        [Tooltip("Generated sprite for machines (transport cells have their own).")]
        public PlaceholderArt art;

        /// <summary>Belts, lifts, splitters and filters: one cell that carries items.</summary>
        public bool IsTransport => kind <= BuildingKind.Filter;
        /// <summary>Gates, traps, guard posts and walls: one cell, no items.</summary>
        public bool IsDefense => kind >= BuildingKind.Gate && kind <= BuildingKind.Wall;
        /// <summary>Machines and nurseries: several cells on a flat floor.</summary>
        public bool IsChamber => kind == BuildingKind.Processor || kind == BuildingKind.Nursery;
        public Vector2Int Footprint => IsChamber ? new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y)) : Vector2Int.one;
    }
}
