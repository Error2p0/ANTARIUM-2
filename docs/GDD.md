# Antarium 2 — Game Design Document

> Living design doc. Update when the design changes. Engineering rules live in `/CLAUDE.md`, progress and gaps in `docs/TODO.md`, player-facing controls in `docs/HOW_TO_PLAY.md`.
>
> **Status legend:** ✅ done · 🟡 partly done (what's missing is noted) · no mark = not started yet.

## 1. Vision

You are the mind of an ant colony. You start with a queen and a handful of workers in a small hole under the grass. You dig down through increasingly strange layers of earth, discover ores and secrets, and turn your colony into a living machine: ants and silk conveyors carry resources through tunnels to processing chambers and to the **Queen**, who turns them into eggs, new castes, and evolutions.

The fun comes from three layers stacked on top of each other:

- 🟡 **Active play** — you personally dig, click, use abilities, react to events, and fight threats. It feels juicy and responsive. *(Click-to-dig with combo and debris done; abilities, events, combat not yet.)*
- 🟡 **Automation** — you design tunnels, conveyors, lifts and production chains so the colony runs itself. Watching resources flow to the queen should be satisfying, like Factorio in an ant farm. *(Ants mine, haul and build; silk belts, root lifts, splitters, filters, a smelter and a fungus garden exist. Longer chains, buffers and pheromone routing not yet.)*
- **Incremental progression** — numbers still go up, but through *unlocks, new mechanics, new biomes and prestige*, not just "+10% mining speed".

Design pillars:
1. **Always something to do** — a new goal is always visible on screen.
2. 🟡 **Watchable** — the colony is alive; hundreds of ants climbing walls, carrying things, reacting. *(Climbing, carrying, foraging, eggs done; reactions and more behaviours to come.)*
3. **Depth = discovery** — every layer down introduces new materials, mechanics and dangers.
4. **Automate what you've mastered** — anything you did by hand early becomes automatable later.

---

## 2. Game design

### 2.1 Core loop
🟡 Dig → discover resources → haul them (ants, then conveyors) → process them in chambers → feed the Queen and build/research → get more ants, castes and tech → dig deeper → hit a wall → **prestige** (Nuptial Flight) → start a new colony stronger and with new options.
*(Built so far: dig → discover → ants and belts haul → smelt / grow → feed the queen and build → more ants. Research, castes and prestige come later.)*

### 2.2 The world (side cross-section)
- ✅ Procedurally generated from a seed, wide horizontally (1024 cells), deep vertically (1536 cells), with a **surface** at the top (grass and sky).
  - Day/night and weather are not done yet (see 2.8).
- 🟡 **Depth layers / biomes**, each with its own materials, hazards, enemies and visual palette. Example progression:
  1. ✅ Topsoil & roots — dirt, pebbles, grubs, copper *(roots as a feature not yet)*
  2. 🟡 Clay & groundwater — clay, iron ✅; water pockets that can flood tunnels (needs fluids)
  3. 🟡 Stone strata — hard rock, coal, fossils, amber ✅; amber's ancient bugs as research/collectibles not yet
  4. Crystal caverns — crystals that emit light, resonance mechanics *(glow crystals already appear rarely in the stone strata)*
  5. Fungal depths — giant fungi, spores, a hostile underground species
  6. Magma zone — heat (powers smelting), gas pockets, lava
  7. Ancient ruins — remains of a lost ant civilization; story, relics, endgame tech
- 🟡 Materials have hardness, value, and special properties (light-emitting, flammable, conductive, unstable). *(Hardness, value, light emission work; flammable/conductive/unstable are flags without behaviour yet.)*
- 🟡 **Cave features**: natural caverns ✅; underground lakes, gas pockets, collapsing sand, roots not yet.
- **Fluids**: simple cellular water (and later lava) that flows into tunnels. Flooding is a hazard, and later a resource (water wheels / irrigation for fungus farms).
- ✅ **Fog of war / darkness**: unexplored areas are dark; breaking into a hidden cave reveals it. Light matters (see 3.2).

### 2.3 Ants and castes
- ✅ Ants are individual agents that **climb walls and ceilings** — they crawl on any surface of a dug tunnel, including the back wall (like an ant farm), so they take direct routes through chambers. Above ground they must touch the ground.
- 🟡 Castes unlocked over time: **Worker** (dig/haul) ✅, **Miner** (fast digging, hard materials), **Hauler** (carries more), **Nurse** (brood care), **Builder** (constructs chambers/conveyors), **Soldier** (defense), **Scout** (explores, reveals map, finds events), **Engineer** (maintains machines), plus late castes (e.g. glow ants that carry light, heat-resistant ants for magma).
- Ants have simple **needs** (food, rest) and gain **XP/levels** in their job. Optional: small randomized traits ("Strong", "Lazy", "Lucky"). *(Today food is a colony-wide upkeep, not a per-ant need.)*
- 🟡 **Job system** with priorities. The player gives orders mainly through **designations** (dig here, build this) and **pheromone painting** (see 2.6), not by micromanaging individuals. *(Mining orders, storage zones, blueprints, dig/haul/build jobs with weights and openings done; player-facing priorities and pheromones not yet.)*
- The player can **possess** any ant (click to take control) for direct active play: run, dig, carry, fight. This is the main "active" mode and should feel good to control.

### 2.4 The Queen
- ✅ The heart of the colony and the main resource sink. Lives in the Royal Chamber (the starting nest).
- 🟡 Consumes food and special resources → lays eggs → eggs need nurses and brood chambers to hatch. *(Food → eggs → workers done; special resources, nurses and brood chambers not yet.)*
- **Queen Evolution tree**: spend rare resources to unlock castes, bigger broods, passive bonuses, new abilities.
- 🟡 Visually central: conveyors and haulers feeding her, eggs being carried away. If she starves, production slows dramatically (clear feedback, not instant game over). *(Haulers feeding her, eggs next to her and the starving slowdown + alert are done; eggs being carried away needs nurses.)*

### 2.5 Logistics and automation
- ✅ **Early**: ants carry everything by hand. Visible bottlenecks motivate automation.
- ✅ **Silk conveyors**: belts laid along tunnels (left/right); items visibly ride them.
- ✅ **Root lifts / elevators**: vertical transport (up/down) for deep mining.
- 🟡 **Splitters, filters, mergers, buffers** for routing. *(Splitters, filters and merging (belts feeding into one) done; buffers / chests not yet.)*
- 🟡 **Storage chambers** with capacity ✅; sorting by item type not yet (storage is one shared pool).
- **Pheromone highways**: painted trails that make ants travel faster and prefer routes.
- 🟡 Throughput stats and overlays (show flow rates, blocked belts, idle ants) so optimizing feels like a puzzle. *(Flow overlay, income rates per item and a hover panel with per-belt flow done; numbers drawn in the world not yet.)*

### 2.5.1 Colony basics (as built in Milestone 2) ✅
- **Resources**: digging a cell may drop an item (`TerrainMaterialDef.drop`): ores, coal, amber, fossils, crystal shards, grubs. Plain dirt/clay/stone drop nothing. Drops fall straight down to the floor as piles.
- **Food**: grub nests in the topsoil (dig them → grubs) and seeds that appear on the surface over time (`ContentDatabase.surfaceForage`), so workers go outside to forage. Food items turn into Food points on delivery.
- **Delivery**: workers haul items to the royal chamber (around the queen) or to **Storage zones** the player paints on open cells. Everything delivered goes into one colony inventory. Capacity per item type = royal chamber base + per storage cell. Ants only pick up what can still be delivered.
- **Orders**: the player paints **Mine** orders on solid cells (also into the fog, to plan tunnels ahead) and Storage zones; right mouse erases. Personal click-dig stays as the **Dig** tool.
- **Queen**: consumes food to lay eggs on the chamber floor; eggs hatch into workers. Workers and queen have food upkeep. At zero food the colony is **starving**: work slows and laying stops (no game over).
- **Jobs**: idle workers choose dig or haul by path distance and priority weights, limited by open slots (a few diggers per frontier cell, one hauler per item) so crowds don't queue behind a narrow tunnel front.
- **Watchability**: colony sprites never render darker than a minimum brightness, so the colony stays readable in dark tunnels while terrain darkness still shows where light is.

### 2.5.2 Logistics (as built in Milestone 3) ✅
- **Building**: the Build tool places **blueprints**. The cost is paid from the stock when placed; workers walk over and build it (a build job next to dig and haul). Removing a building refunds its cost (blueprints always in full; finished buildings by `refundFraction`, default 100%).
- **Transport cells** (1 cell, on open walkable cells, underground or on the ground surface): **Silk Belt** (left/right), **Root Lift** (up/down), **Splitter**, **Filter**. A cell holds up to 2 items. Items move along the cell and pass to the next transport cell; when the next one is full they wait, so jams back up the line. Belts feeding into one cell merge. Two belts pointing at each other never connect.
- **End of a line**: a belt or lift whose next cell has no transport delivers into a **machine** there, or into the **stock** if the line ends in (or next to) the royal chamber or a storage zone. Otherwise the line leads nowhere and items stop at its end.
- **Splitter**: hands items in turn to every neighbouring belt, lift or machine that is not feeding into it. **Filter**: items of its chosen type leave only in its arrow direction; everything else goes to its other sides. Splitters and filters do not connect directly to each other.
- **Routing masks**: every transport cell knows which items can reach a sink from it. A splitter only sends an item where it can be delivered; workers only load belts that can deliver what they carry; falling drops only land on such belts.
- **Workers and belts**: a carrying worker picks once between the stock (royal chamber, storage zones and belts leading there) and a machine that needs the item (or a belt feeding one), preferring machines unless they are much farther away (`consumerBias`). Workers drop items onto the nearest suitable belt instead of walking all the way. Drops from digging fall onto a belt or lift they pass. A worker with nowhere to deliver puts its load down as a pile.
- **Machines** (multi-cell, on a flat floor): input buffer per item (two crafts' worth); they pick the first craftable recipe in turn. Products go onto a transport cell next to the machine that leads away from it, or are left as a pile on the machine floor for workers (production pauses at `outputLimit`). Machines are fed by workers and belts, not from the stock.
- **Content**: Smelter (3×3): 2 copper ore → 1 copper; 2 iron ore + 1 coal → 1 iron. Fungus Garden (4×2): 2 seeds → 3 fungus (3 food each), the first food multiplier. Copper is used for filters; iron has no use yet.
- **Overlay (O)**: belts coloured from grey (idle) to green (flowing), red when jammed; machines green (working), yellow (waiting for input), red (output blocked); idle workers get a blue marker. The resource bar shows income per minute (net for food).

### 2.6 Pheromones (signature mechanic)
The player paints pheromone fields onto the map: *Dig*, *Gather*, *Haul to*, *Defend*, *Avoid*. Ants follow gradients. This is the main way to direct the colony and should be thematic and fun. Pheromones fade over time unless sustained by a **Pheromone Emitter** building (automation of orders).

### 2.7 Chambers and buildings
🟡 Dig out a space, designate it as a chamber type. Chamber size and decoration affect efficiency. *(Royal chamber (fixed) and Storage zones done; machines are placed as buildings on a dug-out floor; size/decoration bonuses not yet.)*
Examples: Royal Chamber ✅, Brood Nursery, Storage ✅, Fungus Farm (food) ✅ *(as the Fungus Garden, fed with seeds instead of leaves)*, Aphid Ranch (honeydew), Crusher, Smelter ✅ *(coal is an ingredient; heat from magma not yet)*, Crystal Resonator, Research Lab ("Archive"), Barracks, Fossil Museum (collection bonuses), Pheromone Emitter.
🟡 Production chains should grow from simple (dirt → nothing; copper ore → copper) to multi-step (iron ore + coal heat → iron → reinforced conveyors). *(Copper ore → copper and iron ore + coal → iron done; nothing consumes iron yet.)*

### 2.8 Surface
- Day/night cycle and weather (rain floods upper tunnels, drought reduces food).
- 🟡 **Foraging expeditions** to the surface for food ✅ (seeds), leaves (fungus feed) and other forage not yet.
- Surface threats: birds, spiders, rival ant colonies.

### 2.9 Threats and combat
- Raids from rival colonies, termites, centipedes, beetles; deep layers have their own creatures.
- Defense: soldier ants, gates, traps, chokepoint design. Threats escalate with depth and colony size.
- Hazards: cave-ins in unstable materials, flooding, gas, heat.
- Losing ants is a setback, not a disaster. The tone is cozy-but-challenging.

### 2.10 Active incremental layer
- ✅ **Click/hold to dig** with combo multipliers and satisfying feedback (debris, screen shake).
- **Active abilities** with cooldowns (e.g. *Frenzy* — all ants work faster; *Pheromone Burst*; *Earthquake* — reveals ore in radius; *Royal Decree* — queen lays a burst of eggs).
- **Random events** that reward attention: rich ore vein exposed, trapped ant rescue, a wandering beetle carrying loot, meteorite strike on the surface, golden aphid.
- **Contracts / goals**: a rotating list of objectives ("Deliver 500 iron to the Queen", "Reach depth 200") with rewards. There must always be a next goal.
- **Offline progress** (calculated on load, with a summary screen), since this is an incremental game on Steam.

### 2.11 Research
A tech tree in the Archive. Research uses resources + "knowledge" (from fossils, amber, ruins). Unlocks buildings, conveyor tiers, castes, abilities, QoL automation (e.g. auto-designate ore veins).

### 2.12 Prestige: Nuptial Flight
When progress slows, the queen sends winged princesses on a **Nuptial Flight** to found a new colony. You reset the world (new seed, possibly new world modifiers) and keep **Genetic Memory** points to spend on a permanent tree (starting bonuses, new starting castes, auto-unlocks of early automation, new biome variants). Later prestiges unlock new mechanics, not just bigger multipliers.

### 2.13 Juice and feel
🟡 Particles when digging (material-colored debris) ✅, screen shake ✅ (on digging; cave-ins don't exist yet), number pops, chunky sounds, ants' little legs animating ✅, items visibly carried ✅. Ambient sound by depth. The world should feel physical.

---

## 3. Technical design

### 3.0 Platform, art direction and scale ✅
- **Engine**: Unity 6.6 (6000.6.x, "Supported" stream), URP 17 with the 2D Renderer. Decision: stay on 6.6 and move to the next LTS when it ships rather than downgrading to 6.3 LTS.
- **Art direction: pixel art.** Everything (terrain, ants, items, buildings, UI accents) is drawn on a single pixel grid.
- **Scale**: 16 art pixels per world unit (PPU 16). **1 cell = 8×8 px = 0.5 world units.** An ant is 12×7 px (~1.5 cells long). World 1024 × 1536 cells.
- **Pixel-perfect camera**: zoom levels are integer screen-pixels-per-art-pixel (×1…×8); the camera position snaps to the art-pixel grid. At ×1 on 1080p the view shows ~240 × 135 cells, which is the "overview" zoom. A strategic map beyond that is a separate render, not fractional zoom.

### 3.1 No Tilemaps ✅
The original used Unity Tilemaps and they were a headache (no proper shadows, hard to make look good). **Do not use Tilemaps.** Instead:

- ✅ **World data**: a chunked grid (32×32 cells per chunk, chunk-major so a chunk is one contiguous range) of 4-byte cells: material, density (dig progress), flags (revealed, explored, mining order, storage). Fluid level comes with fluids. This is the single source of truth.
  - A cell is either solid (density eroding from 255 down to 128) or fully dug (0). There is no half-open state.
- ✅ **Terrain rendering: GPU per-pixel "pixel marching squares".** *(Changed from per-chunk procedural meshes because of the pixel-art direction: smooth mesh contours would cut through art pixels.)*
  - Cell data and the light map are mirrored into GPU structured buffers. Only dirty chunks are uploaded, one 1024-cell range each, so digging never re-uploads the whole world.
  - A terrain shader on one world-sized quad snaps every fragment to the art-pixel grid, interpolates the densities of the surrounding cells (the marching-squares field) and thresholds it with smooth value noise. The result is organic, rounded tunnel edges that are still crisp pixel art.
  - Material boundaries are sampled at a smoothly jittered point, so pockets of another material read as blobs, not squares.
  - Surface pixels come from per-material **pixel textures in a texture array**, tiled in world pixel space. Edge pixels get outline/rim colors. **Normals are derived from the density gradient** (a bevel) so `Light2D` shades the terrain without hand-drawn normal maps.
  - A darker, low-contrast **background wall** is drawn behind dug areas so tunnels have depth. Its edge against the sky uses the same rounded contour, kept just inside the terrain edge.
  - No per-chunk meshes or GameObjects for terrain.
- ✅ **Colliders: none for terrain.** *(Changed: the possessed ant will use the same grid-based crawling controller as AI ants, so control is consistent and no physics colliders are needed. Revisit only if a feature truly needs Box2D, e.g. physics debris.)*

### 3.2 Lighting and shadows
- ✅ URP **2D Renderer** with `Light2D` (a global light provides the minimum ambient).
- ✅ **Primary darkness comes from a grid light map**: light propagates from the surface (sky light) and from light sources (crystals now; glow ants, torches/fungi later) using a Burst flood fill (8-way, RGB), sampled by the terrain and sprite shaders. Light is quantized into a few bands per pixel to match the pixel-art look. Updates are exact and local around changes.
- **ShadowCaster2D only near the camera.** *(Changed from per-chunk everywhere: underground, solid terrain is already dark via the light map, so thousands of casters would cost a lot for almost no visible gain.)* Shadow shapes are generated in code from the cell grid only for chunks that are both in view and inside the radius of a dynamic `Light2D`, and are pooled. *(Deferred until the first dynamic light exists.)*
- Dynamic `Light2D`s add local light (possessed ant, glow ants, abilities, explosions) on top of the light map.
- ✅ Fog of war: unrevealed cells rendered black (a "revealed" bit in the cell flags).
- ✅ Performance budget: lighting must hold 60 FPS with a large, partially dug world *(full recompute once at start, local updates ~1.4 ms)*.

### 3.3 Simulation
- ✅ **Fixed-tick simulation** (20 ticks/sec) fully separated from rendering, which interpolates. This also makes offline progress and saving sane.
- ✅ Ants: target **several hundred to a few thousand** active ants. Store ant state in struct arrays (data-oriented), update with Burst jobs where hot. *(Rendering as built: one dynamic mesh for all colony sprites, written each frame by a Burst job, one draw call. Same cost class as instancing, but it sorts and receives 2D lights like any renderer.)* Sprites snap to the art-pixel grid and rotate in 90° steps to follow the surface. Do not use one GameObject per ant. (The possessed ant can be a normal GameObject.) *(Measured: 1,050 ants ≈ 0.1–0.15 ms per tick.)*
- ✅ **Ant update**: a parallel Burst job moves ants and decides, emitting *intents* for anything touching shared state (dig damage, pickup, delivery); a serial pass applies intents in ant order, so results are deterministic.
- ✅ **Navigation**: ants crawl through dug cells (floor, walls, ceiling and back wall) and, above ground, along the ground. **Flow fields** (distance maps with octile costs: straight 2, diagonal 3) per destination kind: mining front, haulable items, drop-off (royal chamber + storage + belts leading there), home, construction sites, plus one "consumer" field per item that a machine uses (machines needing it, belts feeding them). All fields share one array so the Burst ant job can read any of them. Rebuilt in parallel jobs at most every few ticks when something changed. A* for one-off paths is not needed yet.
- **Pheromones**: float grid fields per pheromone type with diffusion/decay, updated in jobs.
- **Fluids**: simple cellular automaton, updated at a lower rate, only in active regions.
- ✅ **Items on conveyors**: simulated as data (up to two item slots per transport cell, not physics objects), stepped downstream-first so lines move without gaps, and rendered by a Burst job into one mesh with the buildings. *(The belt step is plain C# for now: ~0.55 ms per tick for 6,000 loaded belt cells in the editor.)*

### 3.4 Data-driven content
🟡 All materials, ores, castes, buildings, recipes, research nodes, events, biomes and upgrades are **ScriptableObjects** (or JSON) so I can add content without touching code. Build a simple pattern for IDs and lookups. *(Pattern done: stable string ids → dense runtime indices in `ContentDB`. Materials, items, biomes, buildings, recipes, world gen and tuning are ScriptableObjects; castes, research, events, upgrades come with their features.)*

### 3.5 Saving
Versioned save format (binary or compressed JSON) including the full world grid, ants, buildings, conveyors, research and prestige. Autosave. Save migration support from day one. Offline progress computed from the save timestamp using a simplified rate-based simulation (not full ant sim).

### 3.6 UI
🟡 **Unity UI Toolkit** for all menus, panels and HUD (decided). World-space feedback such as number pops and item icons is drawn by the game's own instanced renderer, not the UI system. UI uses a pixel font and pixel-art 9-slices with integer scaling to match the art direction. Needed: resource bar ✅ (with income rates), build menu ✅ (palette), designation tools ✅ (Dig / Mine / Storage / Build), pheromone tools, queen panel, research tree, contracts list, overlays (flow ✅, light, pheromones, ore scan), tooltips 🟡 (hover info panel), offline summary, settings. Big number formatting (K, M, B... and scientific option) ✅. *(Pixel font and 9-slices not yet; default font for now.)*

### 3.7 Steam
Leave hooks for Steamworks (achievements, cloud saves, stats) behind an interface, but don't integrate the SDK until I ask.

---

## 4. Milestones

Each milestone must be playable and committed before the next.

1. ✅ **Foundation** — project setup, chunked world grid, procedural generation of a few layers, pixel-art terrain renderer (GPU pixel marching squares), pixel-perfect camera (pan/integer zoom), click-to-dig with particles, 2D lighting + darkness/fog of war. *Goal: digging already feels good and looks good.* *(ShadowCaster2D deferred, see 3.2.)*
2. ✅ **Living colony** — ants, climbing movement, flow-field navigation, job system (dig/haul), queen + eggs + workers, storage zones, basic resources. *Goal: watch ants carry ore to the queen.*
3. ✅ **Automation** — conveyors, lifts, splitters/filters, first processing chambers and recipes, throughput overlay. *(Buffers/chests and machines fed from the stock moved to later, see TODO.)*
4. **Incremental layer** — contracts, active abilities, random events, research tree v1, offline progress, save/load.
5. **Depth & danger** — more biomes, fluids, hazards, enemies, soldiers, defense.
6. **Pheromones & castes** — pheromone painting/emitters, remaining castes, ant possession mode.
7. **Prestige** — Nuptial Flight, Genetic Memory tree, world modifiers.
8. **Polish** — audio, juice, UI polish, balancing tools, settings, Steam hooks.
