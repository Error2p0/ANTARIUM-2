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
        public CasteConfig castes = new();
        public PheromoneConfig pheromones = new();
        public PossessionConfig possession = new();
        public PrestigeConfig prestige = new();
        public SurfaceConfig surface = new();
        public WaterworksConfig waterworks = new();
        public FireConfig fire = new();
        /// <summary>10.4: job priorities, ant XP and traits, lane jitter.</summary>
        public AntGrowthConfig growth = new();
        /// <summary>10.4: the lights glow ants carry and the shadows near them.</summary>
        public GlowConfig glow = new();
        [Tooltip("PNG art that replaces the sprites generated in code (Antarium > Art > Import Overrides fills it).")]
        public ArtLibrary art;
    }

    /// <summary>Pumps, water wheels, irrigation and magma heat (10.3). Per-building numbers are on the building assets.</summary>
    [Serializable]
    public sealed class AntGrowthConfig
    {
        [Header("Job priorities (priority panel)")]
        [Tooltip("Distance multiplier per priority 1..5 (lower = preferred). Priority 3 is normal.")]
        public float[] priorityWeights = { 4f, 2f, 1f, 0.5f, 0.25f };
        [Tooltip("Tend and Nurse: distance multiplier of a specialist's own job against the shared jobs at priority 3 (low = it nearly always comes first).")]
        [Min(0.01f)] public float roleWeight = 0.1f;

        [Header("Experience")]
        [Tooltip("XP for levels 2, 3, 4, 5 (the last entry is the top level).")]
        public float[] levelXp = { 15f, 45f, 110f, 240f };
        [Tooltip("Faster at its skill per level above 1 (0.05 = +5% per level).")]
        [Range(0f, 0.5f)] public float levelBonus = 0.05f;
        [Tooltip("Prefers its skill: the job's distance is multiplied by 1 - this per level above 1.")]
        [Range(0f, 0.2f)] public float levelPreference = 0.05f;
        [Tooltip("XP per hardness unit dug / per unit of build work.")]
        [Min(0f)] public float digXp = 1f;
        [Min(0f)] public float buildXp = 1f;
        [Tooltip("XP per item delivered to the stock / to a machine.")]
        [Min(0f)] public float haulXp = 1f;
        [Min(0f)] public float supplyXp = 1f;
        [Tooltip("XP per second an engineer tends a working machine.")]
        [Min(0f)] public float tendXp = 0.1f;
        [Tooltip("XP per egg a nurse lays into a nursery.")]
        [Min(0f)] public float nurseXp = 2f;

        [Header("Traits at hatch")]
        [Tooltip("Chance a new ant hatches with a trait (Strong, Swift, Lucky or Lazy, equally likely).")]
        [Range(0f, 1f)] public float traitChance = 0.3f;
        [Tooltip("Swift: walking speed multiplier.")]
        [Min(1f)] public float swiftSpeed = 1.2f;
        [Tooltip("Lucky: chance that a cell it breaks drops one more item (if the cell drops anything).")]
        [Range(0f, 1f)] public float luckyChance = 0.25f;
        [Tooltip("Lazy: work speed multiplier (dig, build, walking).")]
        [Range(0.1f, 1f)] public float lazyWork = 0.8f;

        [Header("Lane jitter")]
        [Tooltip("Walking speed varies per ant by up to this share (0.07 = 93%..107%), so ants on one route spread out.")]
        [Range(0f, 0.3f)] public float speedJitter = 0.07f;
        [Tooltip("Ants are drawn up to this many art pixels ahead of / behind their spot on the route.")]
        [Range(0, 6)] public int laneOffsetPixels = 3;
        [Tooltip("...and up to this many art pixels away from the surface they walk on (up from a floor, out from a wall).")]
        [Range(0, 6)] public int laneUpPixels = 3;
        [Tooltip("...or up to this many into it (down into a floor).")]
        [Range(0, 6)] public int laneDownPixels = 2;
    }

    [Serializable]
    public sealed class GlowConfig
    {
        [Tooltip("Most glow ants that get a real light (the ones nearest the view centre). The others only show their halo.")]
        [Range(0, 128)] public int maxLights = 24;
        public Color color = new(0.62f, 1f, 0.55f);
        [Range(0f, 2f)] public float intensity = 0.9f;
        [Tooltip("Inner radius as a share of the outer radius (full brightness inside).")]
        [Range(0f, 1f)] public float innerShare = 0.25f;
        [Tooltip("Rock blocks the glow ants' light: shadow casters are made from rock edges near the lights.")]
        public bool shadows = true;
        [Range(0f, 1f)] public float shadowIntensity = 0.85f;
        [Tooltip("Most shadow caster rectangles at once (pooled).")]
        [Range(0, 4096)] public int maxCasters = 768;
        [Tooltip("Seconds between rebuilding the shadow casters (also rebuilt when the terrain changes).")]
        [Min(0.02f)] public float casterRefresh = 0.25f;
    }

    [Serializable]
    public sealed class WaterworksConfig
    {
        [Tooltip("A pump draws from the first cell holding fluid up to this many cells behind it (a hose), so a pump above a flooded " +
                 "shaft drains it.")]
        [Min(1)] public int pumpReach = 8;
        [Tooltip("Seconds between checks of which machines are irrigated, heated or near a turning water wheel.")]
        [Min(0.05f)] public float checkSeconds = 0.5f;
        [Tooltip("Share of the new value a water wheel's power takes each fluid step (smooths it over about a second).")]
        [Range(0.01f, 1f)] public float wheelSmoothing = 0.1f;
        [Tooltip("Water (of 255) a cell in or next to a machine needs for the machine to count as irrigated.")]
        [Range(1, 255)] public int irrigationMinLevel = 24;
        [Tooltip("Water (of 255) an irrigated machine drinks per craft.")]
        [Range(0, 255)] public int irrigationWater = 48;
    }

    /// <summary>Burning flammable cells (10.3).</summary>
    [Serializable]
    public sealed class FireConfig
    {
        [Tooltip("Seconds a cell burns before it turns to ash.")]
        [Min(0.5f)] public float burnSeconds = 15f;
        [Tooltip("Chance per second that a burning cell lights each flammable neighbour (if it touches open air).")]
        [Range(0f, 1f)] public float spreadPerSecond = 0.25f;
        [Tooltip("Chance per lava step that moving lava lights a flammable cell next to it.")]
        [Range(0f, 1f)] public float lavaIgniteChance = 0.05f;
        [Tooltip("Smoke (gas, of 255) a burning cell gives off per fluid step into the open cell above or beside it.")]
        [Range(0, 255)] public int smokePerStep = 4;
        [Tooltip("Water (of 255) next to a burning cell that puts it out.")]
        [Range(1, 255)] public int extinguishLevel = 32;
        [Tooltip("Water lost as steam when it puts out a fire.")]
        [Range(0, 255)] public int extinguishWater = 16;
        [Tooltip("At most this many cells burn at once (new fires wait).")]
        [Min(1)] public int maxBurning = 2000;
        [Tooltip("Meteorite impacts light flammable cells this many cells around the crater.")]
        [Min(0f)] public float meteoriteIgniteRadius = 3f;
        [Tooltip("Colour of the light fire gives off.")]
        [ColorUsage(false)] public Color light = new(1f, 0.55f, 0.2f);
        [Tooltip("Light emitted by a burning cell, as a fraction of the maximum light level.")]
        [Range(0f, 1f)] public float lightStrength = 0.6f;
    }

    [Serializable]
    public sealed class SurfaceConfig
    {
        [Header("Day and night (time of day: 0 = midnight, 0.5 = noon)")]
        [Tooltip("Seconds of game time for a full day and night.")]
        [Min(10f)] public float dayLengthSeconds = 720f;
        [Tooltip("Share of the cycle from sunrise to sunset.")]
        [Range(0.2f, 0.95f)] public float dayShare = 0.65f;
        [Tooltip("Share of the cycle a sunrise or sunset takes.")]
        [Range(0.005f, 0.2f)] public float twilight = 0.06f;
        [Tooltip("Time of day a new colony starts at.")]
        [Range(0f, 1f)] public float startTime = 0.3f;

        [Header("Weather")]
        [Tooltip("No weather before this many seconds into a colony.")]
        [Min(0f)] public float firstWeatherSeconds = 600f;
        [Min(1f)] public float weatherIntervalMin = 360f;
        [Min(1f)] public float weatherIntervalMax = 600f;
        [Tooltip("Chance that the next weather is rain (else a drought).")]
        [Range(0f, 1f)] public float rainChance = 0.6f;
        [Tooltip("Chance that a rain is a thunderstorm: heavier rain and lightning.")]
        [Range(0f, 1f)] public float thunderstormChance = 0.35f;
        [Tooltip("A thunderstorm drops this many times as much rain.")]
        [Min(1f)] public float stormRainFactor = 1.5f;
        [Tooltip("Seconds between lightning strikes in a thunderstorm (random in this range).")]
        public Vector2 lightningInterval = new(3f, 9f);
        public Vector2 rainSeconds = new(60f, 100f);
        public Vector2 droughtSeconds = new(180f, 300f);
        [Tooltip("Forage spawns this much as often during a drought.")]
        [Range(0f, 1f)] public float droughtForage = 0.5f;
        [Tooltip("Rain falls this many cells either side of the nest.")]
        [Min(8)] public int rainRange = 150;
        [Tooltip("Columns that get a raindrop per fluid step.")]
        [Min(0)] public int rainDropsPerStep = 10;
        [Tooltip("Water (of 255) one raindrop adds.")]
        [Range(1, 255)] public int rainDropLevel = 24;
        [Tooltip("Water this close to the surface (cells) slowly soaks into the ground, so rain floods drain again.")]
        [Min(0)] public int soakDepth = 40;
        [Tooltip("Chance per fluid step that a wet cell near the surface loses one level.")]
        [Range(0f, 1f)] public float soakChance = 0.3f;

        [Header("Leaves")]
        [Tooltip("Seconds between leaves falling from a surface plant (by day; forage research and droughts change it).")]
        [Min(0.1f)] public float leafInterval = 6f;
        [Tooltip("No new leaves while this many lie on the surface.")]
        [Min(0)] public int maxSurfaceLeaves = 10;

        [Header("Honeydew")]
        [Tooltip("The queen eats one honeydew from the stock every this many seconds (while there is any).")]
        [Min(1f)] public float honeydewSeconds = 20f;
        [Tooltip("While fed honeydew she lays this much faster (0.5 = +50%) and her own upkeep is free.")]
        [Min(0f)] public float honeydewLaying = 0.5f;

        [Header("Birds (by day)")]
        [Min(0f)] public float firstBirdSeconds = 480f;
        [Min(1f)] public float birdIntervalMin = 150f;
        [Min(1f)] public float birdIntervalMax = 300f;
        [Tooltip("Seconds between the bird's shadow appearing and its strike: ants on the surface run for cover.")]
        [Min(1f)] public float birdWarningSeconds = 8f;
        [Tooltip("The strike kills exposed ants within this many cells of the bird.")]
        [Min(0.5f)] public float birdStrikeRadius = 3f;
        [Min(1)] public int birdMaxKills = 4;
        [Tooltip("Cells per second the bird's shadow follows its prey.")]
        [Min(0.5f)] public float birdSpeed = 6f;
        [Tooltip("Sheltering ants stay inside this many seconds after the strike.")]
        [Min(0f)] public float birdShelterSeconds = 4f;

        [Header("Surface spiders (at night)")]
        [Tooltip("No spiders before this many seconds into a colony (the first night is quiet).")]
        [Min(0f)] public float firstSpiderSeconds = 900f;
        [Tooltip("No spiders while the colony has fewer ants than this.")]
        [Min(0)] public int spiderMinAnts = 15;
        [Min(0f)] public float spidersBase = 1f;
        [Tooltip("Extra spiders per ant in the colony.")]
        [Min(0f)] public float spidersPerAnt = 0.02f;
        [Min(0)] public int spidersMax = 4;
        [Tooltip("Spiders appear on the surface this far (cells) from the nest.")]
        public Vector2Int spiderDistance = new(12, 40);
        [Tooltip("Spiders come out over this many seconds after nightfall.")]
        [Min(0f)] public float spiderSpawnWindow = 60f;

        [Header("Starfall nights")]
        [Tooltip("Chance at dusk that the night is a starfall: shooting stars all night, no night hunters, and seeds and leaves keep coming.")]
        [Range(0f, 1f)] public float starfallChance = 0.15f;
        [Tooltip("No starfall before this many seconds into a colony.")]
        [Min(0f)] public float firstStarfallSeconds = 900f;
    }

    [Serializable]
    public sealed class PrestigeConfig
    {
        [Header("Genetic Memory from a flight: floor((score / divisor) ^ exponent x princess factor x gain)")]
        [Tooltip("Score per cell of the deepest dig.")]
        [Min(0f)] public float depthScore = 0.1f;
        [Tooltip("Score per point of knowledge earned (studied, rewards, events).")]
        [Min(0f)] public float knowledgeScore = 0.1f;
        [Tooltip("Score per research node done (inherited ones too).")]
        [Min(0f)] public float researchScore = 2f;
        [Tooltip("Score per contract completed.")]
        [Min(0f)] public float contractScore = 1f;
        [Min(0.01f)] public float scoreDivisor = 4f;
        [Range(0.1f, 1f)] public float scoreExponent = 0.5f;

        [Header("Princesses")]
        [Tooltip("A flight needs at least this many princesses.")]
        [Min(1)] public int minPrincesses = 1;
        [Tooltip("Princesses that count toward a flight (genetic traits add more). Princess chambers stop at this many.")]
        [Min(1)] public int maxPrincesses = 5;
        [Tooltip("Each princess after the first adds this much to the princess factor (1 + bonus x (princesses - 1)).")]
        [Min(0f)] public float princessBonus = 0.25f;

        [Header("World modifiers & traits")]
        [Tooltip("World modifiers that can be chosen for a new colony (genetic traits add more).")]
        [Min(0)] public int modifierSlots = 1;
        [Tooltip("Vein Instinct: ore within this many cells of a dug-out cell is ordered for mining.")]
        [Min(1)] public int veinRadius = 2;

        [Header("Flight (presentation)")]
        [Tooltip("Seconds the princesses are shown flying before the new colony is founded.")]
        [Min(0.5f)] public float flightSeconds = 4.5f;
        [Tooltip("Princesses climb this many cells per second.")]
        [Min(0.5f)] public float flightSpeed = 9f;

        [Header("Ancient Engine (10.5)")]
        [Tooltip("A colony whose Ancient Engine runs earns this much more Genetic Memory from its flight (1 = twice as much).")]
        [Min(0f)] public float engineMemoryBonus = 1f;

        [Header("Respec (10.6)")]
        [Tooltip("Respecs per colony that refund every spent point of Genetic Memory.")]
        [Min(0)] public int freeRespecs = 1;
        [Tooltip("Share of the refund lost by every respec after the free ones (rounded up, at least 1).")]
        [Range(0f, 1f)] public float respecFee = 0.25f;
    }

    [Serializable]
    public sealed class CasteConfig
    {
        [Tooltip("Step of the caste shares the player sets (0.05 = 0%, 5%, 10%...).")]
        [Range(0.01f, 0.5f)] public float shareStep = 0.05f;
        [Tooltip("Most of the colony one specialist caste may be.")]
        [Range(0f, 1f)] public float maxShare = 0.5f;
        [Tooltip("Most of the colony all specialist castes together may be (workers keep the rest).")]
        [Range(0f, 1f)] public float maxTotalShare = 0.8f;
        [Tooltip("Engineers that may tend one machine at once.")]
        [Min(1)] public int engineersPerMachine = 1;
        [Tooltip("Scouts re-plan where the edge of the known tunnels is this often (seconds).")]
        [Min(0.5f)] public float scoutFieldSeconds = 3f;
    }

    [Serializable]
    public sealed class PheromoneConfig
    {
        [Tooltip("Strength (of 255) a painted cell loses per second; 1 = it fades in about 4 minutes.")]
        [Min(0f)] public float decayPerSecond = 1f;
        [Tooltip("Strength a trail cell regains each time a carrying ant steps onto it.")]
        [Range(0, 255)] public int trafficRefresh = 6;
        [Tooltip("Walking speed on trail cells.")]
        [Min(1f)] public float trailSpeed = 1.5f;
        [Tooltip("Jobs inside Dig / Gather scent are preferred over jobs up to this many cells closer.")]
        [Min(0)] public int priorityCells = 40;
        [Tooltip("Brush radius (cells) for painting pheromones.")]
        [Min(0.5f)] public float brushRadius = 1.6f;
        [Tooltip("Ants answering a Pheromone Burst stop once they are this close (cells) to its spot.")]
        [Min(1)] public int rallyArriveCells = 3;
    }

    [Serializable]
    public sealed class PossessionConfig
    {
        [Tooltip("The possessed ant walks this much faster than its caste.")]
        [Min(0.1f)] public float speed = 1.6f;
        [Tooltip("Dig power of the possessed ant's bite, per hit (like the Dig tool's power).")]
        [Min(0f)] public float digPower = 0.9f;
        [Tooltip("Damage of one bite at an enemy.")]
        [Min(0f)] public float biteDamage = 6f;
        [Tooltip("How far (cells) from the possessed ant you can dig or bite.")]
        [Min(1f)] public float reach = 2.5f;
        [Tooltip("Bites (digging or fighting) per second while the button is held.")]
        [Min(0.5f)] public float bitesPerSecond = 6f;
        [Tooltip("The possessed ant takes this share of damage (0.5 = half).")]
        [Range(0.05f, 1f)] public float damageTaken = 0.5f;
        [Tooltip("Radius (cells) and intensity of the light that follows the possessed ant.")]
        [Min(0f)] public float lightRadius = 6f;
        [Range(0f, 2f)] public float lightIntensity = 0.6f;
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
        [Header("Ants (hit points and bite per caste are in the Caste assets)")]
        [Tooltip("Hit points regained per second after a while without damage.")]
        [Min(0f)] public float regenPerSecond = 2f;
        [Min(0f)] public float regenDelay = 6f;

        [Header("Hazards (damage per second)")]
        [Min(0f)] public float drownDamage = 6f;
        [Min(0f)] public float lavaDamage = 40f;
        [Min(0f)] public float gasDamage = 4f;
        [Tooltip("Damage per second to an ant next to a burning cell.")]
        [Min(0f)] public float fireDamage = 10f;
        [Tooltip("Heat: damage per second to an ant next to lava, magma rock or a fire, unless heat-proof (fire ants). Biomes add their own heat.")]
        [Min(0f)] public float heatDamage = 3f;
        [Tooltip("Damage (once) to an ant buried by falling sand.")]
        [Min(0f)] public float caveInDamage = 25f;
        [Tooltip("Ants swim at this share of their walking speed.")]
        [Range(0.1f, 1f)] public float swimSpeedFactor = 0.55f;

        [Header("Soldiers (egg cost, upkeep and share are in the Caste assets)")]
        [Tooltip("Soldiers go after enemies within this path distance (cells).")]
        [Min(1f)] public float soldierRange = 60f;
        [Tooltip("Idle soldiers stay within this many cells of their guard post.")]
        [Min(1f)] public float guardRadius = 3f;

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

        [Header("Rival ants (10.5)")]
        [Tooltip("No rival ant raids before this many seconds into a colony (before that every raid is termites).")]
        [Min(0f)] public float rivalFirstSeconds = 1500f;
        [Tooltip("Chance that a raid is rival ants (thieves) instead of termites, once they come.")]
        [Range(0f, 1f)] public float rivalRaidChance = 0.4f;

        [Header("Mole crickets (10.5)")]
        [Tooltip("No mole crickets before this many seconds into a colony.")]
        [Min(0f)] public float moleFirstSeconds = 1080f;
        [Min(1f)] public float moleIntervalMin = 480f;
        [Min(1f)] public float moleIntervalMax = 840f;
        [Tooltip("No mole crickets while the colony has fewer ants than this.")]
        [Min(0)] public int moleMinAnts = 15;
        [Tooltip("A mole cricket starts this far (cells) from the queen, in soft ground.")]
        public Vector2Int moleDistance = new(26, 42);
        [Tooltip("A mole cricket starts at least this many cells from the colony's tunnels.")]
        [Min(1)] public int moleClearance = 8;
        [Tooltip("A mole cricket that can't get closer for this many seconds gives up and digs away (it is removed).")]
        [Min(1f)] public float moleGiveUpSeconds = 30f;
    }

    [Serializable]
    public sealed class ProgressionConfig
    {
        [Header("Contracts")]
        [Min(1)] public int contractSlots = 3;

        [Header("Random events")]
        [Tooltip("Seconds into a new colony before the first random event.")]
        [Min(0f)] public float firstEventDelay = 225f;
        [Min(1f)] public float eventIntervalMin = 270f;
        [Min(1f)] public float eventIntervalMax = 540f;
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

        [Header("Queen & brood (egg cost per caste is in the Caste assets)")]
        [Min(0.1f)] public float eggInterval = 6f;
        [Min(0)] public int maxEggs = 6;
        [Min(0.1f)] public float hatchTime = 20f;
        [Tooltip("Cells around the queen that count as the royal chamber for deliveries and eggs.")]
        [Min(1)] public int royalRadius = 4;

        [Header("Upkeep (food per minute; ants per caste in the Caste assets)")]
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
        [Tooltip("Ground runs in large flat patches that are lighter or darker than the tile: the strongest step (0 = none). They are placed in the world, so they never repeat like the tile does.")]
        [Range(0f, 0.6f)] public float patchContrast = 0.26f;
        [Tooltip("The same patches lean warmer or cooler by this much (0 = none).")]
        [Range(0f, 0.3f)] public float patchTint = 0.07f;
        [Tooltip("Intensity of the global Light2D: the minimum brightness of revealed, unlit areas.")]
        [Range(0f, 1f)] public float ambient = 0.025f;
        public Color skyTop = new(0.36f, 0.62f, 0.86f);
        public Color skyHorizon = new(0.72f, 0.86f, 0.93f);
        [Header("Day, night and weather")]
        public Color nightSkyTop = new(0.03f, 0.04f, 0.12f);
        public Color nightSkyHorizon = new(0.1f, 0.12f, 0.26f);
        [Tooltip("Horizon at sunrise and sunset.")]
        public Color duskSkyHorizon = new(0.95f, 0.55f, 0.35f);
        [Tooltip("Sky colour mixed in while it rains.")]
        public Color rainSky = new(0.42f, 0.46f, 0.52f);
        [Tooltip("Colour and strength of the sky light at night (moonlight).")]
        [ColorUsage(false)] public Color nightLight = new(0.55f, 0.65f, 1f);
        [Range(0f, 1f)] public float nightLightStrength = 0.3f;
        [Tooltip("Sky light while it rains (fraction of daylight).")]
        [Range(0f, 1f)] public float rainLight = 0.7f;
        public Color starColor = new(0.9f, 0.92f, 1f);
        public Color rainColor = new(0.65f, 0.75f, 0.95f, 0.7f);
        [Tooltip("Seconds between shooting stars on a clear night (random in this range).")]
        public Vector2 shootingStarInterval = new(6f, 20f);
        [Tooltip("Seconds between shooting stars on a starfall night.")]
        public Vector2 starfallInterval = new(0.04f, 0.18f);
        [Tooltip("Lightning bolts and the flash they light the sky with.")]
        public Color lightningColor = new(0.92f, 0.95f, 1f);
        [Tooltip("How much a lightning flash brightens the sky and the sky light (0 = none).")]
        [Range(0f, 2f)] public float lightningFlash = 0.9f;
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

        [Header("Light and ore overlays (10.7)")]
        [Tooltip("Light overlay: one flat colour per band of light, darkest first (at most 8).")]
        public Color[] lightOverlayRamp =
        {
            new(0.07f, 0.07f, 0.2f), new(0.2f, 0.13f, 0.45f), new(0.47f, 0.16f, 0.5f), new(0.75f, 0.24f, 0.24f),
            new(0.9f, 0.55f, 0.2f), new(0.98f, 0.86f, 0.35f), new(1f, 0.98f, 0.9f),
        };
        [Tooltip("Ore overlay: brightness of everything that is not a find (greyscale).")]
        [Range(0f, 1f)] public float oreOverlayDim = 0.45f;
        [Tooltip("Seconds between counts of the known finds for the ore overlay's legend.")]
        [Min(0.1f)] public float oreSurveySeconds = 1f;

        [Header("Fluids")]
        [Tooltip("Water tint; alpha = how strongly it covers the wall behind it.")]
        public Color waterColor = new(0.22f, 0.48f, 0.9f, 0.62f);
        public Color waterSurfaceColor = new(0.62f, 0.85f, 1f, 1f);
        public Color lavaColor = new(0.95f, 0.36f, 0.08f, 1f);
        public Color lavaHotColor = new(1f, 0.85f, 0.35f, 1f);
        [Tooltip("Gas tint; alpha = cover at full density.")]
        public Color gasColor = new(0.62f, 0.85f, 0.3f, 0.55f);
        [Tooltip("Smoke tint; alpha = cover at full density.")]
        public Color smokeColor = new(0.42f, 0.4f, 0.4f, 0.6f);
        [Tooltip("Burning cells: flames on their edge (and the hot flicker), embers inside.")]
        public Color fireColor = new(1f, 0.5f, 0.12f, 1f);
        public Color fireHotColor = new(1f, 0.9f, 0.45f, 1f);
        public Color emberColor = new(0.55f, 0.12f, 0.04f, 1f);

        [Header("Combat")]
        public Color healthBarColor = new(0.45f, 0.95f, 0.4f);
        public Color healthBarBackColor = new(0.15f, 0.05f, 0.05f);
        public Color enemyHealthBarColor = new(1f, 0.35f, 0.25f);
        [Tooltip("Sprites flash this colour for a moment when hurt.")]
        public Color hurtFlashColor = new(1f, 0.45f, 0.4f);

        [Header("Pheromones (in PheromoneKind order: trail, dig, gather, defend, avoid)")]
        public Color[] pheromoneColors =
        {
            new(1f, 0.88f, 0.4f), new(1f, 0.55f, 0.2f), new(0.45f, 0.95f, 0.4f), new(1f, 0.3f, 0.3f), new(0.75f, 0.5f, 1f),
        };
        [Tooltip("Share of pixels a full-strength pheromone tints, normally / while the Pheromone tool is active.")]
        [Range(0f, 1f)] public float pheromoneCover = 0.16f;
        [Range(0f, 1f)] public float pheromoneToolCover = 0.35f;
        [Tooltip("Marker over the ant you control.")]
        public Color possessedColor = new(1f, 0.85f, 0.3f);
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
        [Tooltip("How quickly the camera catches up with the ant you control (higher = tighter).")]
        [Min(0.1f)] public float followSharpness = 8f;
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

        [Header("Number pops and machine puffs (Milestone 8)")]
        [Tooltip("Seconds a number pop stays up.")]
        [Min(0.1f)] public float popSeconds = 1.1f;
        [Tooltip("Cells a number pop rises over its life.")]
        [Min(0f)] public float popRise = 1.6f;
        [Tooltip("Deliveries of the same kind close together within this many seconds merge into one pop.")]
        [Min(0f)] public float popMergeSeconds = 0.4f;
        [Tooltip("Deliveries within this many cells of each other merge.")]
        [Min(0)] public int popMergeCells = 3;
        [Min(1)] public int maxPops = 48;
        [Tooltip("Seconds between puffs from working machines (0 = none).")]
        [Min(0f)] public float machinePuffSeconds = 0.35f;
    }
}
