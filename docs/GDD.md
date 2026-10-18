# Antarium 2 — Game Design Document

> Living design doc. Update when the design changes. Engineering rules live in `/CLAUDE.md`; progress in `docs/TODO.md`.

## 1. Vision

You are the mind of an ant colony. You start with a queen and a handful of workers in a small hole under the grass. You dig down through increasingly strange layers of earth, discover ores and secrets, and turn your colony into a living machine: ants and silk conveyors carry resources through tunnels to processing chambers and to the **Queen**, who turns them into eggs, new castes, and evolutions.

The fun comes from three layers stacked on top of each other:

- **Active play** — you personally dig, click, use abilities, react to events, and fight threats. It feels juicy and responsive.
- **Automation** — you design tunnels, conveyors, lifts and production chains so the colony runs itself. Watching resources flow to the queen should be satisfying, like Factorio in an ant farm.
- **Incremental progression** — numbers still go up, but through *unlocks, new mechanics, new biomes and prestige*, not just "+10% mining speed".

Design pillars:
1. **Always something to do** — a new goal is always visible on screen.
2. **Watchable** — the colony is alive; hundreds of ants climbing walls, carrying things, reacting.
3. **Depth = discovery** — every layer down introduces new materials, mechanics and dangers.
4. **Automate what you've mastered** — anything you did by hand early becomes automatable later.

---

## 2. Game design

### 2.1 Core loop
Dig → discover resources → haul them (ants, then conveyors) → process them in chambers → feed the Queen and build/research → get more ants, castes and tech → dig deeper → hit a wall → **prestige** (Nuptial Flight) → start a new colony stronger and with new options.

### 2.2 The world (side cross-section)
- Procedurally generated from a seed, wide horizontally (e.g. ~1000 cells), deep vertically, with a **surface** at the top (grass, sky, day/night, weather).
- **Depth layers / biomes**, each with its own materials, hazards, enemies and visual palette. Example progression:
  1. Topsoil & roots — dirt, pebbles, grubs, copper
  2. Clay & groundwater — clay, iron, water pockets that can flood tunnels
  3. Stone strata — hard rock, coal, fossils, amber (amber contains ancient bugs = research/collectibles)
  4. Crystal caverns — crystals that emit light, resonance mechanics
  5. Fungal depths — giant fungi, spores, a hostile underground species
  6. Magma zone — heat (powers smelting), gas pockets, lava
  7. Ancient ruins — remains of a lost ant civilization; story, relics, endgame tech
- Materials have hardness, value, and special properties (light-emitting, flammable, conductive, unstable).
- **Cave features**: natural caverns, underground lakes, gas pockets, collapsing sand, roots.
- **Fluids**: simple cellular water (and later lava) that flows into tunnels. Flooding is a hazard, and later a resource (water wheels / irrigation for fungus farms).
- **Fog of war / darkness**: unexplored areas are dark. Light matters (see 3.2).

### 2.3 Ants and castes
- Ants are individual agents that **climb walls and ceilings** (they can walk on any surface of a dug tunnel). This should look great.
- Castes unlocked over time: **Worker** (dig/haul), **Miner** (fast digging, hard materials), **Hauler** (carries more), **Nurse** (brood care), **Builder** (constructs chambers/conveyors), **Soldier** (defense), **Scout** (explores, reveals map, finds events), **Engineer** (maintains machines), plus late castes (e.g. glow ants that carry light, heat-resistant ants for magma).
- Ants have simple **needs** (food, rest) and gain **XP/levels** in their job. Optional: small randomized traits ("Strong", "Lazy", "Lucky").
- **Job system** with priorities. The player gives orders mainly through **designations** (dig here, build this) and **pheromone painting** (see 2.6), not by micromanaging individuals.
- The player can **possess** any ant (click to take control) for direct active play: run, dig, carry, fight. This is the main "active" mode and should feel good to control.

### 2.4 The Queen
- The heart of the colony and the main resource sink. Lives in the Royal Chamber.
- Consumes food and special resources → lays eggs → eggs need nurses and brood chambers to hatch.
- **Queen Evolution tree**: spend rare resources to unlock castes, bigger broods, passive bonuses, new abilities.
- Visually central: conveyors and haulers feeding her, eggs being carried away. If she starves, production slows dramatically (clear feedback, not instant game over).

### 2.5 Logistics and automation
- **Early**: ants carry everything by hand. Visible bottlenecks motivate automation.
- **Silk conveyors**: belts laid along tunnel floors/walls; items visibly ride them.
- **Root lifts / elevators**: vertical transport for deep mining.
- **Splitters, filters, mergers, buffers** for routing.
- **Storage chambers** with capacity; sorting by item type.
- **Pheromone highways**: painted trails that make ants travel faster and prefer routes.
- Throughput stats and overlays (show flow rates, blocked belts, idle ants) so optimizing feels like a puzzle.

### 2.6 Pheromones (signature mechanic)
The player paints pheromone fields onto the map: *Dig*, *Gather*, *Haul to*, *Defend*, *Avoid*. Ants follow gradients. This is the main way to direct the colony and should be thematic and fun. Pheromones fade over time unless sustained by a **Pheromone Emitter** building (automation of orders).

### 2.7 Chambers and buildings
Dig out a space, designate it as a chamber type. Chamber size and decoration affect efficiency.
Examples: Royal Chamber, Brood Nursery, Storage, Fungus Farm (food), Aphid Ranch (honeydew), Crusher, Smelter (needs heat from magma or burning coal), Crystal Resonator, Research Lab ("Archive"), Barracks, Fossil Museum (collection bonuses), Pheromone Emitter.
Production chains should grow from simple (dirt → nothing; copper ore → copper) to multi-step (iron ore + coal heat → iron → reinforced conveyors).

### 2.8 Surface
- Day/night cycle and weather (rain floods upper tunnels, drought reduces food).
- **Foraging expeditions** to the surface for food, leaves (fungus feed), seeds.
- Surface threats: birds, spiders, rival ant colonies.

### 2.9 Threats and combat
- Raids from rival colonies, termites, centipedes, beetles; deep layers have their own creatures.
- Defense: soldier ants, gates, traps, chokepoint design. Threats escalate with depth and colony size.
- Hazards: cave-ins in unstable materials, flooding, gas, heat.
- Losing ants is a setback, not a disaster. The tone is cozy-but-challenging.

### 2.10 Active incremental layer
- **Click/hold to dig** with combo multipliers and satisfying feedback.
- **Active abilities** with cooldowns (e.g. *Frenzy* — all ants work faster; *Pheromone Burst*; *Earthquake* — reveals ore in radius; *Royal Decree* — queen lays a burst of eggs).
- **Random events** that reward attention: rich ore vein exposed, trapped ant rescue, a wandering beetle carrying loot, meteorite strike on the surface, golden aphid.
- **Contracts / goals**: a rotating list of objectives ("Deliver 500 iron to the Queen", "Reach depth 200") with rewards. There must always be a next goal.
- **Offline progress** (calculated on load, with a summary screen), since this is an incremental game on Steam.

### 2.11 Research
A tech tree in the Archive. Research uses resources + "knowledge" (from fossils, amber, ruins). Unlocks buildings, conveyor tiers, castes, abilities, QoL automation (e.g. auto-designate ore veins).

### 2.12 Prestige: Nuptial Flight
When progress slows, the queen sends winged princesses on a **Nuptial Flight** to found a new colony. You reset the world (new seed, possibly new world modifiers) and keep **Genetic Memory** points to spend on a permanent tree (starting bonuses, new starting castes, auto-unlocks of early automation, new biome variants). Later prestiges unlock new mechanics, not just bigger multipliers.

### 2.13 Juice and feel
Particles when digging (material-colored debris), screen shake on cave-ins, number pops, chunky sounds, ants' little legs animating, items visibly carried. Ambient sound by depth. The world should feel physical.

---

## 3. Technical design

### 3.0 Platform, art direction and scale
- **Engine**: Unity 6.6 (6000.6.x, "Supported" stream), URP 17 with the 2D Renderer. Decision: stay on 6.6 and move to the next LTS when it ships rather than downgrading to 6.3 LTS.
- **Art direction: pixel art.** Everything (terrain, ants, items, buildings, UI accents) is drawn on a single pixel grid.
- **Scale**: 16 art pixels per world unit (PPU 16). **1 cell = 8×8 px = 0.5 world units.** An ant is ~12–16 px long (~1.5–2 cells); tunnels are ≥ 3 cells wide. Starting world ~1024 × 1536 cells.
- **Pixel-perfect camera**: zoom levels are integer screen-pixels-per-art-pixel (e.g. ×1…×8, clamped by screen size); the camera position snaps to the art-pixel grid. At ×1 on 1080p the view shows ~240 × 135 cells, which is the "overview" zoom. A strategic map beyond that is a separate render, not fractional zoom.

### 3.1 No Tilemaps
The original used Unity Tilemaps and they were a headache (no proper shadows, hard to make look good). **Do not use Tilemaps.** Instead:

- **World data**: a chunked grid (32×32 cells per chunk) stored as plain arrays of structs: material ID, density (dig progress), flags (revealed, background wall, etc.), fluid level. This is the single source of truth.
- **Terrain rendering: GPU per-pixel "pixel marching squares".** *(Changed from per-chunk procedural meshes because of the pixel-art direction: smooth mesh contours would cut through art pixels.)*
  - Cell data (material + density) is mirrored into a world-sized **data texture** on the GPU. Dirty chunks are uploaded as 32×32 regions (`Graphics.CopyTexture` from a small staging texture), so digging never re-uploads the whole world.
  - A terrain shader on a camera-sized quad snaps every fragment to the art-pixel grid, interpolates the corner densities of its cell (the marching-squares field) and thresholds it. The result is organic, rounded tunnel edges that are still crisp pixel art, and partly dug cells erode smoothly.
  - Surface pixels come from per-material **pixel textures in a texture array**, tiled in world pixel space. Edge pixels get outline/rim colors. **Normals are derived from the density gradient** (a bevel) so `Light2D` shades the terrain without hand-drawn normal maps.
  - A separate darker **background wall** is drawn behind dug areas so tunnels have depth.
  - No per-chunk meshes or GameObjects for terrain.
- **Colliders: none for terrain.** *(Changed: the possessed ant uses the same grid-based surface-crawling controller as AI ants, so control is consistent and no physics colliders are needed. Revisit only if a feature truly needs Box2D, e.g. physics debris.)*

### 3.2 Lighting and shadows
- URP **2D Renderer** with `Light2D` (global, point, freeform).
- **Primary darkness/shadows come from a grid light map**: light propagates from the surface (sky light) and from light sources (crystals, glow ants, torches/fungi) using a BFS flood fill (Burst job), written to a low-res texture that's sampled by the terrain/sprite shaders. Light is quantized into a few bands per pixel to match the pixel-art look.
- **ShadowCaster2D only near the camera.** *(Changed from per-chunk everywhere: underground, solid terrain is already dark via the light map, so thousands of casters would cost a lot for almost no visible gain.)* Shadow shapes are generated in code from the cell grid only for chunks that are both in view and inside the radius of a dynamic `Light2D`, and are pooled.
- Dynamic `Light2D`s add local light (possessed ant, glow ants, abilities, explosions) on top of the light map.
- Fog of war: unrevealed cells rendered black (a "revealed" bit stored alongside the light map).
- Performance budget: lighting must hold 60 FPS with a large, partially dug world.

### 3.3 Simulation
- **Fixed-tick simulation** (e.g. 20 ticks/sec) fully separated from rendering, which interpolates. This also makes offline progress and saving sane.
- Ants: target **several hundred to a few thousand** active ants. Store ant state in struct arrays (data-oriented), update with Burst jobs where hot, render with **GPU instancing** (`Graphics.RenderMeshInstanced` or similar) and sprite-sheet animation in the shader. Do not use one GameObject per ant. (The possessed ant can be a normal GameObject.)
- **Navigation**: ants move through dug cells and cling to surfaces. Use **flow fields** toward common destinations (queen, storage, designated jobs) and A* for individual one-off paths; update incrementally when terrain changes.
- **Pheromones**: float grid fields per pheromone type with diffusion/decay, updated in jobs.
- **Fluids**: simple cellular automaton, updated at a lower rate, only in active regions.
- **Items on conveyors**: simulated as data along conveyor segments (not physics objects), rendered instanced.

### 3.4 Data-driven content
All materials, ores, castes, buildings, recipes, research nodes, events, biomes and upgrades are **ScriptableObjects** (or JSON) so I can add content without touching code. Build a simple pattern for IDs and lookups.

### 3.5 Saving
Versioned save format (binary or compressed JSON) including the full world grid, ants, buildings, conveyors, research and prestige. Autosave. Save migration support from day one. Offline progress computed from the save timestamp using a simplified rate-based simulation (not full ant sim).

### 3.6 UI
**Unity UI Toolkit** for all menus, panels and HUD (decided). World-space feedback such as number pops and item icons is drawn by the game's own instanced renderer, not the UI system. UI uses a pixel font and pixel-art 9-slices with integer scaling to match the art direction. Needed: resource bar, build menu, designation/pheromone tools, queen panel, research tree, contracts list, overlays (flow, light, pheromones, ore scan), tooltips, offline summary, settings. Big number formatting (K, M, B... and scientific option).

### 3.7 Steam
Leave hooks for Steamworks (achievements, cloud saves, stats) behind an interface, but don't integrate the SDK until I ask.

---

## 4. Milestones

Each milestone must be playable and committed before the next.

1. **Foundation** — project setup, chunked world grid, procedural generation of a few layers, pixel-art terrain renderer (GPU pixel marching squares), pixel-perfect camera (pan/integer zoom), click-to-dig with particles, 2D lighting + shadows + darkness/fog of war. *Goal: digging already feels good and looks good.*
2. **Living colony** — instanced ants, climbing movement, flow-field navigation, job system (dig/haul), queen + eggs + workers, storage chamber, basic resources. *Goal: watch ants carry ore to the queen.*
3. **Automation** — conveyors, lifts, splitters/filters, first processing chambers and recipes, throughput overlay.
4. **Incremental layer** — contracts, active abilities, random events, research tree v1, offline progress, save/load.
5. **Depth & danger** — more biomes, fluids, hazards, enemies, soldiers, defense.
6. **Pheromones & castes** — pheromone painting/emitters, remaining castes, ant possession mode.
7. **Prestige** — Nuptial Flight, Genetic Memory tree, world modifiers.
8. **Polish** — audio, juice, UI polish, balancing tools, settings, Steam hooks.

Start with Milestone 1 after I approve the architecture plan.

