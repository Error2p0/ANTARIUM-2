using Antarium.Content;
using UnityEditor;
using UnityEngine;

namespace Antarium.Editor
{
    public static partial class ContentSetup
    {
        public const string HelpFolder = Root + "/Help";
        public const string TutorialFolder = Root + "/Tutorial";

        /// <summary>
        /// The help topics (F1) and the first-colony tutorial. Existing assets keep their text (hand edits survive);
        /// <i>Antarium → Setup → Rewrite Help and Tutorial Text</i> writes the text below over them.
        /// </summary>
        static void CreateHelpAndTutorial() => WriteHelpAndTutorial(overwrite: false);

        [MenuItem("Antarium/Setup/Rewrite Help and Tutorial Text")]
        static void RewriteHelpAndTutorial()
        {
            WriteHelpAndTutorial(overwrite: true);
            RebuildDatabase();
            AssetDatabase.SaveAssets();
        }

        static void WriteHelpAndTutorial(bool overwrite)
        {
            EnsureFolder(HelpFolder);
            EnsureFolder(TutorialFolder);
            BiomeDef Biome(string id) => AssetDatabase.LoadAssetAtPath<BiomeDef>($"{BiomesFolder}/{id}.asset");
            ResearchDef Research(string id) => AssetDatabase.LoadAssetAtPath<ResearchDef>($"{ResearchFolder}/{id}.asset");

            HelpTopicDef Topic(string id, string title, int order, string category, string icon, string body,
                HelpReveal reveal = HelpReveal.Always, BiomeDef biome = null, ResearchDef research = null)
            {
                string path = $"{HelpFolder}/{id}.asset";
                var topic = AssetDatabase.LoadAssetAtPath<HelpTopicDef>(path);
                bool created = topic == null;
                if (!created && !overwrite) return topic;
                if (created) topic = ScriptableObject.CreateInstance<HelpTopicDef>();
                topic.EditorSetIdentity(id, title);
                topic.order = order;
                topic.category = category;
                topic.icon = icon;
                topic.body = body;
                topic.reveal = reveal;
                topic.revealBiome = biome;
                topic.revealResearch = research;
                if (created) AssetDatabase.CreateAsset(topic, path);
                else EditorUtility.SetDirty(topic);
                return topic;
            }

            // ---------------------------------------------------------------- Basics
            const string basics = "Basics";
            var colony = Topic("help_colony", "Your colony", 100, basics, "ants",
                "You are the mind of an ant colony. The queen lives in the royal chamber just under the grass, with a handful of workers.\n\n" +
                "Workers dig and bring home what they find. Food lets the queen lay eggs, and the eggs hatch into more ants. Ore and other finds pay for " +
                "buildings and research. The deeper you dig, the harder, stranger and richer the rock gets.\n\n" +
                "You don't steer ants one by one. You give orders (mining orders, storage zones, buildings, scents) and the ants take up the work on their own.\n\n" +
                "Nothing ends the game: a starving colony slows down and ants can die, but the colony goes on. When it stops growing, a Nuptial Flight starts a new, stronger one.");
            var camera = Topic("help_camera", "Looking around", 110, basics, "sun",
                "Pan with {Pan} or the arrow keys, or hold the middle mouse button and drag. The mouse wheel zooms toward the pointer, in whole steps so the pixel art stays sharp.\n\n" +
                "Everything away from your tunnels is dark until your ants dig near it. Breaking into a cave reveals the whole cave at once.\n\n" +
                "Point at anything, in the world or on the screen, and the info panel at the bottom says what it is: an ant's job and health, what a building does " +
                "and why it waits, what a cell of rock gives when it is dug.");
            Topic("help_screen", "The screen", 120, basics, "level_pips",
                "Top: the time of day and the weather, food, ants, eggs, storage, knowledge, and every item you own with how much arrives per minute. " +
                "Click an item to choose how many to keep for building.\n\n" +
                "Right: the contracts, your current goals. Left: cards for events and threats; Go moves the view there.\n\n" +
                "Bottom: the tools and panels with their keys, the info panel and messages. Bottom right: your abilities.\n\n" +
                "A red line under the top bar warns you while the colony is starving, raiders are in the royal chamber or a bird is circling.");
            var digging = Topic("help_digging", "Digging yourself", 130, basics, "tool_dig",
                "Press {Dig} for the Dig tool and hold the left mouse on rock you can see. Keep digging without a pause and a combo builds up: you dig faster and faster.\n\n" +
                "The Strength bar above the toolbar drains while you dig and fills up again while you rest. Your workers dig without limit, " +
                "so use the Dig tool for quick jobs and give orders for the rest.\n\n" +
                "{Brush} changes the brush size (1, 2, 3). Hard rock takes more hits; the bedrock at the edge of the world can't be dug.\n\n" +
                "Some rock drops an item when it breaks: ore, coal, amber, grubs and more fall to the floor, and workers carry them home.");
            var mining = Topic("help_mining", "Mining orders", 140, basics, "tool_mine",
                "Press {Mine} and paint over rock: orange stripes mark it for digging, and workers come and dig it out. Right drag erases orders.\n\n" +
                "Orders reach into the dark, so you can plan a tunnel before you can see where it goes. Workers dig from the open side and reveal the rest as they go. " +
                "They can't reach orders that no tunnel leads to.\n\n" +
                "A few workers share each spot at the digging front; the others haul or wait, so a narrow tunnel doesn't jam.\n\n" +
                "Later the research Vein Instinct orders the ore next to every dug-out cell by itself.");
            var storage = Topic("help_storage", "Storage and hauling", 150, basics, "storage",
                "Workers pick up whatever lies around and carry it to the nearest place with room: the royal chamber around the queen, or a storage zone. " +
                "A machine that needs an item gets it first.\n\n" +
                "Press {Storage} and paint storage on dug-out cells underground. Every storage cell adds room for items and a little food. Chests (Build tool) hold more in one cell.\n\n" +
                "Stored items are really there: you see them stacked in the cells. When storage is full, piles wait on the floor until there is room; " +
                "Storage in the top bar turns orange.\n\n" +
                "Point at a storage zone or a chest and press {Configure} to choose which items it takes.");
            Topic("help_food", "Food", 160, basics, "food",
                "Every ant eats, and the queen most of all. Food in the top bar shows what you have, how much fits, and what you gain or lose per minute.\n\n" +
                "Food comes from:\n" +
                "- seeds, berries, apples and toadstools on the surface, which workers forage by themselves (seeds, berries and apples only by day);\n" +
                "- grub nests in the topsoil: pale speckled soil that drops grubs when it is dug;\n" +
                "- mushrooms in caves, which grow back after they are picked;\n" +
                "- the Fungus Garden (research Fungiculture), which turns seeds or leaves into much more food.\n\n" +
                "When food runs out the colony starves: everyone works at half speed and the queen stops laying. No ant starves to death, and it ends as soon as food comes in.");
            var queen = Topic("help_queen", "The queen and eggs", 170, basics, "eggs",
                "While there is food, the queen lays an egg every few seconds. Each egg costs food and hatches into an ant. Only a few eggs can wait next to her at once.\n\n" +
                "Click the queen or press {Queen} for her panel: how fast she lays, her eggs, what the colony eats, and her evolutions, which you buy with rare finds " +
                "such as honeydew and amber.\n\n" +
                "Stop laying in her panel tells her to rest, for when the colony has as many ants as your food can feed. Resume laying starts her again.\n\n" +
                "Raiders in the royal chamber stop her laying too. Honeydew from an Aphid Ranch makes her lay faster.");
            var contracts = Topic("help_contracts", "Contracts", 180, basics, "knowledge",
                "The contracts on the right are your goals: dig so many cells, bring in some ore, build a machine. The first ones walk you through the start, " +
                "one at a time; later ones are drawn at random and ask for more each time.\n\n" +
                "When a contract is done it turns gold: click Claim reward for knowledge, food and sometimes items. A new contract takes its place.");
            Topic("help_saving", "Saving and time away", 190, basics, "memory",
                "The game saves by itself every few minutes (Settings > Gameplay), when you quit and when you press {Save}. {Load} goes back to the last save. " +
                "There are three save slots, each with its own bloodline.\n\n" +
                "While the game is closed the colony keeps working at half its recent pace, for up to 8 hours (research and traits raise both): " +
                "what came into the stock, what it ate, eggs hatching. A Welcome back panel tells you what happened. Raids, weather and other dangers wait until you are back.\n\n" +
                "Build up steady production (foraging, belts, machines) and the colony earns while you're away.");

            // ---------------------------------------------------------------- Building
            const string building = "Building";
            Topic("help_building", "Building", 200, building, "tool_build",
                "Press {Build} and pick a building in the palette ({Previous} / {Next} go through it, {Rotate} turns it). A green preview means it fits; " +
                "red means it doesn't, and the info panel says why.\n\n" +
                "The cost comes out of your stock when you place it, and a see-through blueprint appears. Workers walk over and build it. " +
                "Removing a building (right mouse) gives its cost back.\n\n" +
                "Belts, lifts and other small buildings need open space ants can walk on. Machines need dug-out space underground with a flat, solid floor under their whole width.\n\n" +
                "Locked buildings are faded in the palette: research unlocks them. Point at one to see what it does and costs.");
            Topic("help_belts", "Belts and lifts", 210, building, "building_silk_belt",
                "Silk Belts carry items left or right, Root Lifts up or down. Drag to lay a line: it flows the way you drag. Dragging over a line turns it around.\n\n" +
                "A line delivers where it ends: into a machine, into a chest, or into the stock when it ends in or next to the royal chamber or a storage zone. " +
                "A line that leads nowhere stops, and the info panel warns you.\n\n" +
                "Workers drop their loads onto the nearest belt that can deliver them and go straight back to work, and what you dig falls onto belts below it. " +
                "A full line backs up; a jammed one shows red in the flow overlay ({Flow}).\n\n" +
                "Splitters share items out between lines and filters send one item one way (research Silk Routing). Iron belts and lifts are twice as fast.");
            var machines = Topic("help_machines", "Machines", 220, building, "building_smelter",
                "Machines turn items into better ones: the Smelter makes copper from copper ore (and iron from iron ore and coal), the Archive studies finds into knowledge, " +
                "the Fungus Garden grows food, and research brings more.\n\n" +
                "A machine starts as soon as it has what it needs. Workers bring fresh finds straight to it and fetch its inputs from storage; belts can feed it too. " +
                "Its products go onto a belt leading away, or wait on its floor for workers. With too many waiting it pauses.\n\n" +
                "Point at a machine to see its inputs and progress. {Configure} makes it run one recipe only, or switches it off. " +
                "In the flow overlay ({Flow}) machines are green while working, yellow while waiting for inputs and red when their products pile up.");
            Topic("help_stock", "Stock, filters and chests", 230, building, "building_chest",
                "Click an item in the top bar for the stock view and set how many of each item to keep for building. Machines and unloaders never take the stock below that.\n\n" +
                "{Configure} on a storage zone or a chest chooses which items it takes; everything else goes elsewhere. A chest holds many items in one cell, " +
                "and a belt that ends at a chest fills it.\n\n" +
                "Unloaders (research Stockkeeping) take items out of the storage cell, chest or royal chamber behind them and put them onto the belt or machine their arrow points at: " +
                "a chest of ore next to a smelter feeds it directly.");
            Topic("help_overlays", "Overlays", 240, building, "tool_flow",
                "Overlays show what is hard to see, one at a time, with a legend above the toolbar:\n" +
                "- Flow ({Flow}): belts from grey (idle) to green (busy) and red where jammed, machines by what they are doing, and a blue dot over idle workers.\n" +
                "- Light ({Light}): how much light reaches each spot. Light is only for your eyes: ants work just as well in the dark.\n" +
                "- Ores ({Ores}): every find you know of in its own colour, with how many cells of each.\n\n" +
                "The Overlay button goes through them.");

            // ---------------------------------------------------------------- Colony
            const string colonyCategory = "Colony";
            var research = Topic("help_research", "Knowledge and research", 300, colonyCategory, "tool_research",
                "Knowledge (violet in the top bar) buys research. It comes from claiming contracts, from events, and from the Archive, " +
                "which studies copper, crystals, amber, fossils and rarer finds.\n\n" +
                "Press {Research} for the research tree. A node you can buy now glows violet; grey ones need other research first. " +
                "Point at a node to see what it does and costs, and click to buy it. Research is instant and lasts for the whole colony.\n\n" +
                "Research unlocks buildings, castes and abilities, and makes the colony faster.");
            Topic("help_abilities", "Abilities", 310, colonyCategory, "ability_frenzy",
                "Your abilities sit at the bottom right with their keys, and recharge after each use.\n" +
                "- Frenzy ({Ability1}): every worker is faster for a short while. You have it from the start.\n" +
                "- Seismic Sense ({Ability2}): reveals the ore and other finds hidden in the dark around a spot.\n" +
                "- Royal Decree ({Ability3}): the queen lays a batch of eggs at once, for free.\n" +
                "- Pheromone Burst ({Ability4}): every ant nearby rushes to a spot.\n" +
                "- Quickening ({Ability5}): every egg hatches at once (a queen evolution).\n\n" +
                "Abilities that need a place wait for a click in the world; right click or {Menu} cancels.");
            Topic("help_castes", "Castes", 320, colonyCategory, "tool_castes",
                "At first the queen lays only workers, who do a bit of everything. Research unlocks castes that each do one thing better: soldiers fight, miners dig, " +
                "haulers carry three at a time, builders build, nurses carry eggs to nurseries, scouts reveal the rock around your tunnels and engineers speed up machines. " +
                "Deeper finds bring glow ants that light the dark and fire ants that don't mind the heat.\n\n" +
                "Press {Castes} to set each caste's share of the colony. Workers fill the rest, and the queen lays whichever caste is furthest below its share.\n\n" +
                "Specialists only do their own work: miners stand idle without mining orders, builders without blueprints. Keep their shares to what you need.");
            Topic("help_priorities", "Job priorities", 330, colonyCategory, "priority_dig",
                "Press {Priorities} to set which jobs ants pick first: dig, haul, build, supply (fetching machine inputs from storage), tend (engineers) and nurse, " +
                "each from 1 (lowest) to 5 (highest).\n\n" +
                "A higher priority makes ants walk farther for that job. Raise hauling when finds pile up while ants dig, or building when blueprints wait. " +
                "Each caste still only does its own kinds of work.");
            Topic("help_scents", "Pheromones", 340, colonyCategory, "tool_scent",
                "Press {Scent} to paint scents; {Previous} / {Next} pick one, right drag erases.\n" +
                "- Trail: ants walk faster along it and prefer it. Lay it along busy tunnels.\n" +
                "- Dig here: mining orders under it are dug first.\n" +
                "- Gather: piles on it are picked up first.\n" +
                "- Defend: soldiers patrol it.\n" +
                "- Avoid: ants keep out of it and take no jobs inside.\n\n" +
                "Scents fade after a few minutes. A Pheromone Emitter (research Scent Glands) keeps one scent fresh around it: paint the area first, then place the emitter.");
            Topic("help_possession", "Controlling an ant", 350, colonyCategory, "tool_possess",
                "Point at an ant and press {Possess} to take control of it. {Pan} moves it; it climbs walls and ceilings like any ant.\n\n" +
                "Hold the left mouse to bite toward the pointer: an enemy there gets bitten, rock gets dug. Right mouse or {Interact} picks up a pile, " +
                "and delivers or puts down what the ant carries.\n\n" +
                "The ant you control is faster and tougher than the others, and the view follows it. {Possess} or {Menu} lets go.");
            Topic("help_growth", "Experience and traits", 360, colonyCategory, "trait_strong",
                "An ant's first job becomes its skill. Working at it gives experience, and every level makes it a little faster at that job. " +
                "Point at an ant to see its skill and level.\n\n" +
                "About one ant in three hatches with a trait: Strong ones carry more, Swift ones walk faster, Lucky ones find more, and Lazy ones take their time.\n\n" +
                "The queen's evolutions ({Queen}) make every ant tougher, quicker to heal, and more.");

            // ---------------------------------------------------------------- World
            const string world = "World";
            Topic("help_surface", "Day, night and weather", 400, world, "sun",
                "A day passes in a few minutes of play. Seeds, berries and leaves only appear by day.\n\n" +
                "Birds hunt by day: when one circles, every ant in the open runs for the nearest way underground, and the bird takes the ones still outside when it strikes. " +
                "Dig entrances close to where your ants forage. At night wolf spiders prowl near the nest.\n\n" +
                "Rain pours into shafts and tunnels near the surface and soaks away afterwards; a thunderstorm brings more. A drought halves forage. " +
                "An Earth Wall closes a shaft while it rains.\n\n" +
                "Lakes and rivers can't be crossed on the surface: tunnel under them, and don't dig into their beds.");
            Topic("help_events", "Random events", 410, world, "item_star_fragment",
                "Every few minutes something happens, with a card on the left (Go looks at it):\n" +
                "- Meteorite: dig out the lump for star fragments.\n" +
                "- Rich vein: a blob of ore next to your tunnels glows. Order it mined.\n" +
                "- Golden beetle: click it before it gets away.\n" +
                "- Trapped ants: dig through to them in time and they join you.\n" +
                "- Golden aphid: click it in your Aphid Ranch.\n" +
                "- Giant earthworm: kill it for a feast; the Dig tool hurts it too.\n\n" +
                "Events are chances, not threats: missing one costs nothing.");
            var clay = Biome("clay");
            var stone = Biome("stone_strata");
            Topic("help_layers", "The deep layers", 420, world, "item_iron_ore",
                "The world changes layer by layer as you dig down, starting with the topsoil, the clay and the stone. Every layer has its own rock, finds and dangers, " +
                "and its rock is harder to dig.\n\n" +
                "Point at a cell to see what it is; the ore overlay ({Ores}) shows every find you know of.\n\n" +
                "Hauling from deep down means long walks. Lifts and belts bring finds up a shaft, and storage near the digging saves trips.",
                HelpReveal.Biome, clay);
            Topic("help_fluids", "Water, lava and gas", 430, world, "building_earth_wall",
                "Some caves hold water, lava or gas. They stay put until you dig into them, then flow into your tunnels.\n" +
                "- Water falls and spreads. Deep water blocks ants, and ants caught in it can drown.\n" +
                "- Lava flows slowly, burns ants and lights things up. Where it meets water it turns to obsidian.\n" +
                "- Gas rises, spreads under ceilings and hurts ants inside it.\n" +
                "- Sand falls into any gap dug under it; fallen sand is ordered for mining by itself.\n\n" +
                "Seal off what you don't want with Earth Walls (Build tool, drag to paint): they become solid packed earth. A pit dug below a flood drains it.",
                HelpReveal.Biome, clay);
            Topic("help_waterworks", "Pumps, wheels and farms", 440, world, "building_water_wheel",
                "Water never rises by itself: a Pump moves water or gas from behind it out of its spout. Chain pumps to lift water higher, drain a flooded shaft or blow gas out.\n\n" +
                "A Water Wheel turns while water pours through it and speeds up the machines around it. Put it under a waterfall or a pump's outlet.\n\n" +
                "Fungus Gardens and Aphid Ranches with water next to them make more. A Smelter touching lava, magma rock or fire works faster and needs no coal.",
                HelpReveal.Research, research: Research("hydraulics"));
            Topic("help_fire", "Fire", 450, world, "item_coal",
                "Coal, roots, mycelium and giant fungus burn. Lava flowing next to them, or a meteorite, can set them alight. Fire spreads to whatever burns next to it, " +
                "gives off smoke and burns ants close by; burnt cells turn to ash, which falls like sand.\n\n" +
                "Water next to a fire puts it out at once, and digging a burning cell out ends it. A Fire card shows how much is burning.",
                HelpReveal.Biome, stone);
            Topic("help_light", "Light and darkness", 460, world, "item_glow_mushroom",
                "Sunlight reaches into tunnels near the surface and fades deeper down. Glowing crystals, mushrooms, magma and lava light their surroundings, " +
                "and glow ants carry light with them.\n\n" +
                "Light only matters for your eyes: ants work just as well in the dark. Unexplored rock is black; explored tunnels without light are dim. " +
                "The research Guiding Light puts a light at your pointer.");

            // ---------------------------------------------------------------- Dangers
            const string dangers = "Dangers";
            Topic("help_raids", "Raids and defence", 500, dangers, "caste_soldier",
                "Once the colony has grown a little, termites from a rival mound raid it from the surface every several minutes. A red card warns you before they arrive.\n\n" +
                "Raiders head for the royal chamber and eat your food while the queen stops laying, then leave. Kill them all before they eat and the raid is repelled. " +
                "Later, rival ants come for your stock instead and carry items away.\n\n" +
                "Every ant next to an enemy bites back, but workers are weak. Research Soldier Caste: soldiers hunt enemies and guard Guard Posts and Defend scent. " +
                "Gates and traps (research Fortification) hold raiders at a narrow tunnel, and a Pheromone Burst throws every ant nearby at them.");
            Topic("help_creatures", "Cave creatures", 510, dangers, "item_chitin",
                "From the stone layer down, creatures live in the caves. They wake when you break into their cave (a card says how many) and hunt ants that come near.\n\n" +
                "Send soldiers first, wall off a cave you're not ready for, or paint Avoid over it. Killed creatures drop chitin and other finds, and give a little knowledge.\n\n" +
                "Mole crickets dig toward the nest through soft ground; their tunnel shows through the dark as they come.",
                HelpReveal.Biome, stone);

            // ---------------------------------------------------------------- Late game
            const string late = "Late game";
            Topic("help_resonance", "Crystal resonance", 600, late, "building_crystal_resonator",
                "Glow crystals that grow together hum. A Crystal Resonator (research Crystal Resonance) built touching a crystal cluster, or copper or iron ore leading to it, " +
                "turns the hum into knowledge and speeds up the machines around it. The more crystals in the cluster, the stronger it is.\n\n" +
                "Mining the crystals weakens it: leave the cluster standing and build around it.",
                HelpReveal.Biome, Biome("crystal_caverns"));
            Topic("help_ruins", "The ruins and the Codex", 610, late, "lore_fragment",
                "The deepest layer holds the rooms of a lost ant colony, still guarded. The relics and ancient tablets found there tell its story: " +
                "study them in an Archive and fragments of the story appear in the Codex ({Codex}).\n\n" +
                "The ancient research needs fragments found. Its last node unlocks the Ancient Engine: build it in the ruins and restore it step by step ({Configure} on it). " +
                "A running Engine makes knowledge and doubles the Genetic Memory of this colony's Nuptial Flight.",
                HelpReveal.Biome, Biome("ancient_ruins"));
            var nuptial = Research("nuptial_flight");
            Topic("help_flight", "The Nuptial Flight", 620, late, "tool_flight",
                "When a colony slows down, send princesses to found a new one. Build a Princess Chamber and feed it fungus and amber: it raises princesses.\n\n" +
                "Press {Flight} to see what a flight would earn: Genetic Memory, more for a deeper, more developed colony and for more princesses. " +
                "Pick world modifiers for the next colony there; harder worlds earn more.\n\n" +
                "Taking flight ends this colony. The next one starts on a new world, and the Genetic Memory is yours to spend.",
                HelpReveal.Flight, research: nuptial);
            Topic("help_genetics", "Genetic Memory", 630, late, "memory",
                "Genetic Memory buys traits in the genetic tree ({Genetics}): more starting workers and food, faster digging, research that every colony knows from the start, " +
                "more world modifiers and much more. Traits are kept forever, in every colony of your bloodline.\n\n" +
                "Respec gives back what you spent (free once per colony). Each save slot holds its own bloodline; New bloodline in the menu starts over without Genetic Memory.",
                HelpReveal.Flight, research: nuptial);

            // ---------------------------------------------------------------- the tutorial
            var smelter = AssetDatabase.LoadAssetAtPath<BuildingDef>($"{BuildingsFolder}/smelter.asset");

            void Step(string id, string title, int order, TutorialGoal goal, int amount, TutorialHighlight highlight, HelpTopicDef topic, string body,
                BuildingDef target = null)
            {
                string path = $"{TutorialFolder}/{id}.asset";
                var step = AssetDatabase.LoadAssetAtPath<TutorialStepDef>(path);
                bool created = step == null;
                if (!created && !overwrite) return;
                if (created) step = ScriptableObject.CreateInstance<TutorialStepDef>();
                step.EditorSetIdentity(id, title);
                step.order = order;
                step.goal = goal;
                step.amount = amount;
                step.highlight = highlight;
                step.helpTopic = topic;
                step.building = target;
                step.body = body;
                if (created) AssetDatabase.CreateAsset(step, path);
                else EditorUtility.SetDirty(step);
            }

            Step("tutorial_welcome", "Welcome to your colony", 10, TutorialGoal.None, 1, TutorialHighlight.None, colony,
                "You are the mind of an ant colony. The queen sits in the royal chamber just under the grass, with a few workers. " +
                "Food becomes eggs, eggs become ants, and ants dig, gather and build.\n\n" +
                "This tutorial shows you the basics. Skip a step or end it whenever you like; you can start it again from the help ({Help}).");
            Step("tutorial_look_around", "Look around", 20, TutorialGoal.LookAround, 8, TutorialHighlight.None, camera,
                "Move the view with {Pan} or the arrow keys (or drag with the middle mouse button). Zoom with the mouse wheel: zoom in to watch your ants up close.");
            Step("tutorial_dig", "Dig with your own jaws", 30, TutorialGoal.DigYourself, 6, TutorialHighlight.DigTool, digging,
                "Press {Dig} for the Dig tool and hold the left mouse on the soil next to the chamber. Keep digging without a pause to build up a combo.\n\n" +
                "The Strength bar drains while you dig and fills up while you rest. Your workers never tire.");
            Step("tutorial_orders", "Give orders", 40, TutorialGoal.MiningOrders, 12, TutorialHighlight.MineTool, mining,
                "Press {Mine} and paint over the soil where you want a tunnel. The orange stripes are mining orders: your workers dig them out for you.\n\n" +
                "Orders can reach into the dark, so you can plan ahead. Right drag erases them.");
            Step("tutorial_storage", "Make room", 50, TutorialGoal.StorageZone, 4, TutorialHighlight.StorageTool, storage,
                "What your workers dig up is carried to the royal chamber, and it fills up fast. Press {Storage} and paint a storage zone " +
                "on dug-out cells underground: in the chamber or along a tunnel.");
            Step("tutorial_hauling", "Workers carry it home", 60, TutorialGoal.ItemsStored, 5, TutorialHighlight.Resources, storage,
                "You never carry anything yourself: workers pick up whatever lies around and bring it in. Watch the top bar as it arrives.\n\n" +
                "Point at an ant, a pile or a cell: the panel at the bottom tells you what it is and what it's doing.");
            Step("tutorial_queen", "The queen", 70, TutorialGoal.OpenQueen, 1, TutorialHighlight.QueenButton, queen,
                "Every ant eats. Workers forage seeds on the surface by day and dig up grubs in the soil; Food in the top bar shows how much you have. " +
                "While there is food, the queen lays eggs that hatch into more workers.\n\n" +
                "Click the queen or press {Queen} to see her.");
            Step("tutorial_contracts", "Goals", 80, TutorialGoal.ClaimContract, 1, TutorialHighlight.Contracts, contracts,
                "The contracts on the right always show a next goal. When one is done, click Claim reward for knowledge, food or items.\n\n" +
                "The first ones walk you through the start of a colony.");
            Step("tutorial_research", "Research", 90, TutorialGoal.Research, 1, TutorialHighlight.ResearchButton, research,
                "Knowledge (violet, in the top bar) buys research. Press {Research}, then click a node that glows. Fungiculture is a good first one: " +
                "its Fungus Garden makes much more food out of seeds.\n\n" +
                "More knowledge comes from contracts and from an Archive studying your finds.");
            Step("tutorial_build", "Build a machine", 100, TutorialGoal.PlaceBuilding, 1, TutorialHighlight.BuildTool, machines,
                "Press {Build} for the buildings. A Smelter costs {Cost}: once you have that, place one on a flat floor in a tunnel. " +
                "Workers build it, bring it copper ore and carry the copper home.\n\n" +
                "Copper pays for better buildings, and an Archive studies it into knowledge.", smelter);
            Step("tutorial_help", "When in doubt", 110, TutorialGoal.OpenHelp, 1, TutorialHighlight.None, null,
                "Press {Help} now. The help explains every part of the game, and new pages appear in it as your colony discovers more.");
            Step("tutorial_done", "Over to you", 120, TutorialGoal.None, 1, TutorialHighlight.None, null,
                "That's the basics. Keep digging: deeper layers hide iron, coal and stranger things, and the cards on the left tell you when something happens.\n\n" +
                "Good luck, mind of the colony.");
        }
    }
}
