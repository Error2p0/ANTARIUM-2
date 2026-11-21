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
        /// <summary>One cell of storage (<see cref="BuildingDef.capacity"/> items) that can be limited to chosen items.</summary>
        Chest,
        /// <summary>Takes items (its filter item, or any) out of the storage behind it and puts them onto the belt or machine its arrow points at.</summary>
        Unloader,
        /// <summary>Moves water or gas from the cell behind it to the cell its arrow points at. Fluid can't pass through it; ants can.</summary>
        Pump,
        /// <summary>Multi-cell, no floor needed: water from above falls through it, and the flow speeds up machines nearby.</summary>
        WaterWheel,
        /// <summary>Multi-cell, on a floor, touching conductive rock: its crystal cluster makes knowledge and speeds up machines nearby (10.5).</summary>
        Resonator,
        /// <summary>The Ancient Engine (10.5): one per colony, built in the ruins and restored stage by stage; running, it is the colony's goal.</summary>
        Engine,
        /// <summary>The Queen's Throne (10.6): one per colony, built around the queen; colony stats and a work aura while it stands.</summary>
        Throne,
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
        Crusher,
        /// <summary>Aphid Ranch: a fenced leaf pasture (aphids are drawn walking in it).</summary>
        Ranch,
        /// <summary>Water wheel (its own animated sprite).</summary>
        Wheel,
        /// <summary>Crystal resonator: idle / resonating.</summary>
        Resonator,
        /// <summary>Ancient Engine: one frame per restoration stage.</summary>
        Engine,
        /// <summary>Queen's Throne: a dais between two pillars (the queen is drawn on it).</summary>
        Throne,
    }

    /// <summary>One restoration step of a staged building (the Ancient Engine): paid from the stock, then built by workers.</summary>
    [System.Serializable]
    public sealed class BuildingStage
    {
        public string name;
        [TextArea] public string description;
        public List<ItemAmount> cost = new();
        [Min(0f)] public float foodCost;
        [Tooltip("Seconds of work for one worker.")]
        [Min(0.05f)] public float buildWork = 10f;
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
        [Tooltip("Cells per second items move along this cell. Unloader: items per second it puts out. Pump: cells of fluid per second it moves. " +
                 "Water wheel: cells of water per second each of its columns lets through.")]
        [Min(0.1f)] public float speed = 2f;

        [Tooltip("Belts, lifts and gates: 0 = silk / resin, 1 = iron (picks the sprite). Placing a building of the same kind " +
                 "over another one upgrades it in place.")]
        [Min(0)] public int tier;

        [Header("Storage")]
        [Tooltip("Chest: items of any type it holds.")]
        [Min(1)] public int capacity = 16;

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

        [Header("Water and heat")]
        [Tooltip("Water wheel: extra speed (0.5 = +50%) for machines near it when water pours through at full rate.")]
        [Min(0f)] public float machineBoost;
        [Tooltip("Water wheel: machines within this many cells of it are sped up.")]
        [Min(0f)] public float boostRadius = 8f;
        [Tooltip("Machines: extra output (0.5 = +50%) while water stands in or next to them (irrigation). 0 = not irrigated.")]
        [Min(0f)] public float irrigationBonus;
        [Tooltip("Machines: extra speed (1 = +100%) while lava, magma rock or fire touches them; they then need no fuel. 0 = no heat.")]
        [Min(0f)] public float heatBonus;

        [Header("Crystals and ruins (10.5)")]
        [Tooltip("Resonator / Engine: knowledge per second at full power (knowledge gain applies). 0 = none.")]
        [Min(0f)] public float knowledgePerSecond;
        [Tooltip("Resonator: resonance (sum of the cluster's material resonance) for full power.")]
        [Min(0.1f)] public float fullResonance = 24f;
        [Tooltip("Resonator: conductive cells it follows at most when measuring its cluster.")]
        [Min(1)] public int maxClusterCells = 400;
        [Tooltip("Can only be built in this biome (the Ancient Engine: the ruins). None = anywhere.")]
        public BiomeDef requiredBiome;
        [Tooltip("Only one per colony.")]
        public bool unique;
        [Tooltip("Engine: restoration steps after placing it, each paid and built in turn. The last one finishes it.")]
        public List<BuildingStage> stages = new();

        [Header("Queen's Throne (10.6)")]
        [Tooltip("Colony stats while the building stands (built, not a blueprint).")]
        public List<StatAmount> stats = new();
        [Tooltip("Throne: ants within this many cells of it work faster. 0 = no aura.")]
        [Min(0f)] public float auraRadius;
        [Tooltip("Throne: extra dig and build speed (0.15 = +15%) for ants within the aura.")]
        [Min(0f)] public float auraWork;

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
        public bool IsChamber => kind == BuildingKind.Processor || kind == BuildingKind.Nursery || kind == BuildingKind.Resonator
                                 || kind == BuildingKind.Engine || kind == BuildingKind.Throne;
        /// <summary>Several cells: machines, nurseries, resonators, the engine and water wheels (the only ones that use <see cref="size"/>).</summary>
        public bool IsMultiCell => IsChamber || kind == BuildingKind.WaterWheel;
        /// <summary>Pumps and water wheels: fluid can't flow through their cells.</summary>
        public bool BlocksFluid => kind == BuildingKind.Pump || kind == BuildingKind.WaterWheel;
        public Vector2Int Footprint => IsMultiCell ? new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y)) : Vector2Int.one;
    }
}
