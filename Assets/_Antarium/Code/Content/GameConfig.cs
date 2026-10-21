using System;
using UnityEngine;

namespace Antarium.Content
{
    /// <summary>Tuning values for systems. Content (materials, biomes...) lives in <see cref="ContentDatabase"/>.</summary>
    [CreateAssetMenu(menuName = "Antarium/Game Config", fileName = "GameConfig")]
    public sealed class GameConfig : ScriptableObject
    {
        public SimConfig sim = new();
        public DigConfig dig = new();
        public LightConfig light = new();
        public RenderConfig render = new();
        public CameraConfig camera = new();
        public FeedbackConfig feedback = new();
        public ColonyConfig colony = new();
        public WorkerConfig worker = new();
        public LogisticsConfig logistics = new();
        public ProgressionConfig progression = new();
        public SaveConfig save = new();
        public FluidConfig fluids = new();
        public CombatConfig combat = new();
        public ThreatConfig threats = new();
    }

    [Serializable]
    public sealed class FluidConfig
    {
        [Tooltip("Fluids update once every N simulation ticks (2 = 10 times a second).")]
        [Min(1)] public int ticksPerStep = 2;
        [Tooltip("Water or gas at or above this level (of 255) blocks ant paths; deep water drowns.")]
        [Range(1, 255)] public int deepLevel = 128;
        [Tooltip("Gas at or above this level hurts ants inside it.")]
        [Range(1, 255)] public int gasHarmLevel = 48;
        [Tooltip("Lava moves only every N fluid steps (it is thick).")]
        [Min(1)] public int lavaStepEvery = 4;
        [Tooltip("Lava only spreads sideways where the level difference is at least this (it heaps up).")]
        [Range(2, 128)] public int lavaMinDiff = 24;
        [Tooltip("Lava's sideways flow is divided by this (it creeps).")]
        [Range(1, 8)] public int lavaThickness = 2;
        [Tooltip("Water films this thin (or thinner) evaporate over time in explored tunnels.")]
        [Range(0, 64)] public int evaporateBelow = 8;
        [Range(0f, 1f)] public float evaporateChance = 0.06f;
        [Tooltip("Chance per step that gas in explored tunnels thins by one level.")]
        [Range(0f, 1f)] public float gasDecayChance = 0.35f;
        [Tooltip("Water lost as steam where water and lava meet (and the lava turns to obsidian).")]
        [Range(0, 255)] public int steamLoss = 96;
        [Tooltip("Fluid steps a chunk stays awake after its last change.")]
        [Min(1)] public int awakeSteps = 12;
        [Tooltip("Fluid steps run during world generation so lakes start settled.")]
        [Min(0)] public int settleSteps = 400;
        [Tooltip("Ant paths are rebuilt for moving fluids (and falling sand) at most this often, in seconds.")]
        [Min(0.05f)] public float pathRefreshSeconds = 1f;
        [Tooltip("Colour of the light lava gives off.")]
        [ColorUsage(false)] public Color lavaLight = new(1f, 0.5f, 0.15f);
        [Tooltip("Light emitted by lava, as a fraction of the maximum light level.")]
        [Range(0f, 1f)] public float lavaLightStrength = 0.75f;
    }

    [Serializable]
    public sealed class CombatConfig
    {
        [Header("Ants")]
        [Min(1f)] public float workerHealth = 40f;
        [Min(1f)] public float soldierHealth = 150f;
        [Tooltip("Hit points regained per second after a while without damage.")]
        [Min(0f)] public float regenPerSecond = 2f;
        [Min(0f)] public float regenDelay = 6f;
        [Tooltip("Damage per second an ant deals to an enemy next to it.")]
        [Min(0f)] public float workerAttack = 3f;
        [Min(0f)] public float soldierAttack = 14f;

        [Header("Hazards (damage per second)")]
        [Min(0f)] public float drownDamage = 6f;
        [Min(0f)] public float lavaDamage = 40f;
        [Min(0f)] public float gasDamage = 4f;
        [Tooltip("Damage (once) to an ant buried by falling sand.")]
        [Min(0f)] public float caveInDamage = 25f;
        [Tooltip("Ants swim at this share of their walking speed.")]
        [Range(0.1f, 1f)] public float swimSpeedFactor = 0.55f;

        [Header("Soldiers")]
        [Tooltip("Food for a soldier egg (worker eggs use colony.eggFoodCost).")]
        [Min(0f)] public float soldierEggFoodCost = 15f;
        [Tooltip("Food per minute per soldier.")]
        [Min(0f)] public float soldierUpkeep = 0.6f;
        [Tooltip("Soldiers go after enemies within this path distance (cells).")]
        [Min(1f)] public float soldierRange = 60f;
        [Tooltip("Idle soldiers stay within this many cells of their guard post.")]
        [Min(1f)] public float guardRadius = 3f;
        [Tooltip("Step of the soldier share the player can set (0.1 = 0%, 10%, 20%...).")]
        [Range(0.05f, 0.5f)] public float soldierShareStep = 0.1f;
        [Range(0f, 1f)] public float maxSoldierShare = 0.5f;
        [Tooltip("Soldier share set when soldiers are unlocked.")]
        [Range(0f, 1f)] public float defaultSoldierShare = 0.2f;

        [Header("Defense")]
        [Tooltip("Hit points an intact gate mends per second.")]
        [Min(0f)] public float gateRegenPerSecond = 1f;
    }

    [Serializable]
    public sealed class ThreatConfig
    {
        [Header("Cave creatures")]
        [Tooltip("Creatures spawn at least this far (cells) from where the colony broke into their cave.")]
        [Min(0)] public int minSpawnDistance = 8;
        [Tooltip("Awake cave creatures without prey wander within this many cells of where they spawned.")]
        [Min(1)] public int leashRadius = 12;

        [Header("Raids")]
        [Tooltip("No raids before this many seconds into a colony.")]
        [Min(0f)] public float firstRaidSeconds = 720f;
        [Tooltip("No raids while the colony has fewer ants than this.")]
        [Min(0)] public int raidMinAnts = 20;
        [Min(1f)] public float raidIntervalMin = 420f;
        [Min(1f)] public float raidIntervalMax = 600f;
        [Tooltip("Seconds of warning before raiders arrive.")]
        [Min(0f)] public float raidWarningSeconds = 30f;
        [Min(1)] public int raidBaseSize = 3;
        [Tooltip("Extra raiders per ant in the colony.")]
        [Min(0f)] public float raidPerAnt = 0.08f;
        [Tooltip("Extra raiders per biome the colony has reached below the first.")]
        [Min(0f)] public float raidPerBiome = 2f;
        [Min(1)] public int raidMaxSize = 40;
        [Tooltip("Raiders arrive on the surface this far (cells) from the nest.")]
        public Vector2Int raidDistance = new(60, 100);
        [Tooltip("Knowledge for beating a raid (every raider killed or chased off).")]
        [Min(0f)] public float raidKnowledge = 10f;
    }

    [Serializable]
    public sealed class ProgressionConfig
    {
        [Header("Contracts")]
        [Min(1)] public int contractSlots = 3;

        [Header("Random events")]
        [Tooltip("Seconds into a new colony before the first random event.")]
        [Min(0f)] public float firstEventDelay = 150f;
        [Min(1f)] public float eventIntervalMin = 180f;
        [Min(1f)] public float eventIntervalMax = 360f;
        [Tooltip("No new event starts while this many are waiting for the player.")]
        [Min(1)] public int maxActiveEvents = 2;
        [Tooltip("Golden beetle walking speed, cells per second.")]
        [Min(0.1f)] public float critterSpeed = 2.2f;
        [Tooltip("How close (cells) a click must be to catch a critter.")]
        [Min(0.5f)] public float catchRadius = 1.5f;
    }

    [Serializable]
    public sealed class SaveConfig
    {
        [Tooltip("Seconds between autosaves. 0 = off (still saves on quit).")]
        [Min(0f)] public float autosaveSeconds = 120f;

        [Header("Offline progress")]
        [Tooltip("Share of the recent production rates earned while the game is closed.")]
        [Range(0f, 1f)] public float offlineEfficiency = 0.5f;
        [Min(0f)] public float offlineMaxHours = 8f;
        [Tooltip("Shorter absences are ignored (no summary).")]
        [Min(0f)] public float offlineMinSeconds = 60f;
        [Tooltip("While away the queen only lays when this many minutes of upkeep stay in store afterwards.")]
        [Min(0f)] public float offlineFoodReserveMinutes = 10f;
    }

    [Serializable]
    public sealed class LogisticsConfig
    {
        [Header("Transport")]
        [Tooltip("Minimum distance between two items on a belt, in cells. A cell holds at most 1 / spacing items.")]
        [Range(0.25f, 1f)] public float itemSpacing = 0.5f;
        [Tooltip("A full belt cell whose front item has not moved for this long is jammed; ants stop loading it.")]
        [Min(0.1f)] public float jamSeconds = 2f;
        [Tooltip("Smoothing of the per-belt and per-item rate meters (fraction of the new sample per second).")]
        [Range(0.01f, 1f)] public float rateSmoothing = 0.15f;

        [Header("Hauling")]
        [Tooltip("Haulers compare distance to a machine that needs the item, multiplied by this, with distance to the stock. " +
                 "0.1 = a machine wins unless it is more than 10x farther away than the stock.")]
        [Range(0.02f, 1f)] public float consumerBias = 0.1f;

        [Header("Construction")]
        [Tooltip("Build work per second for one worker (a building's buildWork is in seconds of one worker).")]
        [Min(0.01f)] public float buildPower = 1f;
        [Tooltip("Job choice: distance to a construction site is multiplied by this. Lower = preferred.")]
        [Min(0.01f)] public float buildWeight = 0.7f;
        [Tooltip("Job choice: distance to stock holding an item a machine waits for is multiplied by this. Lower = preferred.")]
        [Min(0.01f)] public float supplyWeight = 0.9f;
        [Tooltip("Workers that may work on one construction site at a time.")]
        [Min(1)] public int buildersPerSite = 2;
        [Tooltip("Share of the cost returned when a finished building is removed (blueprints always refund fully).")]
        [Range(0f, 1f)] public float refundFraction = 1f;
    }

    [Serializable]
    public sealed class ColonyConfig
    {
        [Min(0)] public int startingWorkers = 6;
        [Min(0f)] public float startingFood = 40f;

        [Header("Capacity")]
        [Tooltip("Items (any type) the royal chamber holds. Food has its own capacity.")]
        [Min(0)] public int baseItemCapacity = 40;
        [Min(0f)] public float baseFoodCapacity = 100f;
        [Tooltip("Items (any type) one storage zone cell holds. The stock view draws up to 8 per cell.")]
        [Min(0)] public int storageItemsPerCell = 8;
        [Min(0f)] public float storageFoodPerCell = 6f;

        [Header("Queen & brood")]
        [Min(0f)] public float eggFoodCost = 6f;
        [Min(0.1f)] public float eggInterval = 6f;
        [Min(0)] public int maxEggs = 6;
        [Min(0.1f)] public float hatchTime = 20f;
        [Tooltip("Cells around the queen that count as the royal chamber for deliveries and eggs.")]
        [Min(1)] public int royalRadius = 4;

        [Header("Upkeep (food per minute)")]
        [Min(0f)] public float workerUpkeep = 0.25f;
        [Min(0f)] public float queenUpkeep = 1.5f;
        [Range(0.05f, 1f)] public float starvingSpeedFactor = 0.5f;

        [Header("Surface foraging")]
        [Min(0.1f)] public float forageInterval = 5f;
        [Min(0)] public int maxSurfaceFood = 12;
        [Tooltip("Max horizontal distance from the nest (cells) where forage spawns.")]
        [Min(4)] public int forageRange = 120;

        [Header("Navigation")]
        [Tooltip("Flow fields are rebuilt at most once every N ticks when something changed.")]
        [Min(1)] public int fieldRefreshTicks = 4;
    }

    [Serializable]
    public sealed class WorkerConfig
    {
        [Tooltip("Cells per second.")]
        [Min(0.1f)] public float moveSpeed = 3f;
        [Min(0.1f)] public float fallSpeed = 14f;
        [Tooltip("Hardness units dug per second.")]
        [Min(0f)] public float digPower = 0.5f;
        [Tooltip("Job choice: distance is multiplied by this. Lower = preferred.")]
        [Min(0.01f)] public float digWeight = 1f;
        [Min(0.01f)] public float haulWeight = 0.8f;
        [Tooltip("How many workers may target each walkable cell at the mining front; the rest haul or wait.")]
        [Min(1)] public int diggersPerFrontierCell = 4;
        [Tooltip("Idle ants wander within this many cells (path distance) of the queen.")]
        [Min(1)] public int wanderRadius = 16;
    }

    [Serializable]
    public sealed class SimConfig
    {
        [Min(1)] public int ticksPerSecond = 20;
        [Min(1)] public int maxTicksPerFrame = 5;
    }

    [Serializable]
    public sealed class DigConfig
    {
        [Tooltip("Hardness units removed from a cell per hit at the brush centre.")]
        [Min(0f)] public float power = 0.6f;
        [Min(0.5f)] public float brushRadius = 1.6f;
        [Tooltip("Damage multiplier at the brush edge.")]
        [Range(0f, 1f)] public float brushEdgeFactor = 0.35f;
        [Min(0.5f)] public float hitsPerSecond = 10f;
        [Tooltip("Brush radius (cells) for painting mining orders and storage zones.")]
        [Min(0.5f)] public float orderBrushRadius = 1.6f;

        [Header("Combo")]
        [Min(0.05f)] public float comboWindow = 0.45f;
        [Min(0f)] public float comboPerHit = 0.04f;
        [Min(1f)] public float maxCombo = 2.5f;

        [Header("Reveal")]
        [Min(1)] public int revealRadius = 4;
        [Tooltip("Max cells of a newly breached cave revealed at once.")]
        [Min(1)] public int caveRevealLimit = 20000;
    }

    [Serializable]
    public sealed class LightConfig
    {
        [Range(8, 255)] public int maxLevel = 64;
        [Tooltip("Light lost per open cell. Also sets the max reach: maxLevel / openCost cells.")]
        [Min(1)] public int openCost = 2;
        [Min(1)] public int solidCost = 8;
        [ColorUsage(false)] public Color skyLight = Color.white;
    }

    [Serializable]
    public sealed class RenderConfig
    {
        [Tooltip("Number of brightness steps the grid light is quantised into.")]
        [Range(2, 32)] public int lightBands = 7;
        [Tooltip("Per-pixel jitter on terrain edges. Higher = crumblier.")]
        [Range(0f, 0.5f)] public float edgeNoise = 0.16f;
        [Range(0f, 1f)] public float wallBrightness = 0.42f;
        [Range(0f, 1f)] public float wallOcclusion = 0.55f;
        [Tooltip("Texture contrast of dug-out background walls (0 = flat base colour, 1 = full texture).")]
        [Range(0f, 1f)] public float wallContrast = 0.35f;
        [Tooltip("Intensity of the global Light2D: the minimum brightness of revealed, unlit areas.")]
        [Range(0f, 1f)] public float ambient = 0.025f;
        public Color skyTop = new(0.36f, 0.62f, 0.86f);
        public Color skyHorizon = new(0.72f, 0.86f, 0.93f);
        [Tooltip("Ants, queen and items never render darker than this, so the colony stays watchable in dark tunnels.")]
        [Range(0f, 1f)] public float spriteMinLight = 0.85f;
        [Tooltip("Stripes on cells ordered for mining.")]
        public Color miningOrderColor = new(1f, 0.69f, 0.25f);
        [Tooltip("Tint on storage zone cells.")]
        public Color storageColor = new(0.3f, 0.75f, 0.85f);

        [Header("Buildings & overlay")]
        [Tooltip("Opacity of blueprints and placement previews (dithered).")]
        [Range(0.1f, 0.9f)] public float ghostAlpha = 0.55f;
        public Color ghostValidColor = new(0.55f, 1f, 0.6f);
        public Color ghostInvalidColor = new(1f, 0.35f, 0.3f);
        [Tooltip("Throughput overlay: belt colour at zero and at high flow, and for jammed belts.")]
        public Color overlayIdleColor = new(0.35f, 0.4f, 0.55f);
        public Color overlayFlowColor = new(0.4f, 1f, 0.45f);
        public Color overlayJamColor = new(1f, 0.25f, 0.2f);
        [Tooltip("Items per minute shown as full flow colour.")]
        [Min(1f)] public float overlayFullRate = 120f;
        public Color overlayWorkingColor = new(0.5f, 1f, 0.5f);
        public Color overlayStarvedColor = new(1f, 0.85f, 0.3f);
        public Color overlayIdleAntColor = new(0.45f, 0.7f, 1f);
        [Tooltip("Brightness of items kept in storage relative to loose items (they sit on the back wall).")]
        [Range(0.3f, 1f)] public float stockBrightness = 0.8f;

        [Header("Fluids")]
        [Tooltip("Water tint; alpha = how strongly it covers the wall behind it.")]
        public Color waterColor = new(0.22f, 0.48f, 0.9f, 0.62f);
        public Color waterSurfaceColor = new(0.62f, 0.85f, 1f, 1f);
        public Color lavaColor = new(0.95f, 0.36f, 0.08f, 1f);
        public Color lavaHotColor = new(1f, 0.85f, 0.35f, 1f);
        [Tooltip("Gas tint; alpha = cover at full density.")]
        public Color gasColor = new(0.62f, 0.85f, 0.3f, 0.55f);

        [Header("Combat")]
        public Color healthBarColor = new(0.45f, 0.95f, 0.4f);
        public Color healthBarBackColor = new(0.15f, 0.05f, 0.05f);
        public Color enemyHealthBarColor = new(1f, 0.35f, 0.25f);
        [Tooltip("Sprites flash this colour for a moment when hurt.")]
        public Color hurtFlashColor = new(1f, 0.45f, 0.4f);
    }

    [Serializable]
    public sealed class CameraConfig
    {
        [Min(1)] public int minZoom = 1;
        [Min(1)] public int maxZoom = 8;
        [Min(1)] public int defaultZoom = 3;
        [Tooltip("Keyboard pan speed in world units per second at zoom 1.")]
        [Min(1f)] public float panSpeed = 60f;
        [Tooltip("Max shake offset in art pixels at full trauma.")]
        [Min(0f)] public float maxShakePixels = 3f;
        [Min(0f)] public float shakeDecay = 2.5f;
    }

    [Serializable]
    public sealed class FeedbackConfig
    {
        [Min(0)] public int particlesPerDamage = 1;
        [Min(0)] public int particlesPerBreak = 6;
        [Tooltip("Upper bound on debris particles spawned per frame (many ants digging at once).")]
        [Min(0)] public int maxParticlesPerFrame = 150;
        [Min(0f)] public float particleSpeed = 4f;
        [Min(0f)] public float particleLifetime = 0.6f;
        [Min(0f)] public float shakePerBreak = 0.06f;
        [Tooltip("Camera shake per cell of sand that falls (cave-ins); a frame adds at most maxShakePerHit.")]
        [Min(0f)] public float caveInShake = 0.08f;
        [Tooltip("Particles when an ant or enemy dies.")]
        [Min(0)] public int particlesPerDeath = 8;
        [Tooltip("Camera shake when Seismic Sense is used / a meteorite lands.")]
        [Min(0f)] public float seismicShake = 0.5f;
        [Min(0f)] public float meteoriteShake = 0.8f;
        [Min(0f)] public float maxShakePerHit = 0.25f;
    }
}
