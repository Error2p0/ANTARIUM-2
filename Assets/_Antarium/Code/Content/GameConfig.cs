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
        [Tooltip("Per item type, provided by the royal chamber.")]
        [Min(0)] public int baseItemCapacity = 30;
        [Min(0f)] public float baseFoodCapacity = 100f;
        [Tooltip("Extra capacity per item type for each storage zone cell.")]
        [Min(0)] public int storageItemsPerCell = 4;
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
        [Min(0f)] public float maxShakePerHit = 0.25f;
    }
}
