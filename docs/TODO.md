# TODO

## Done
- Project check: Unity 6000.6.5f1, URP 17.6 2D Renderer, new Input System only, Linear colour space.
- Package cleanup (removed tilemap, tilemap.extras, visual scripting, collab proxy, tutorial framework); Burst/Collections/Mathematics are direct dependencies.
- `docs/GDD.md`, `CLAUDE.md`.
- Decisions: stay on Unity 6.6; UI Toolkit; pixel art; GPU per-pixel terrain; no terrain colliders; ShadowCaster2D only near the camera.
- Player Settings: Run In Background on (idle game keeps simulating when unfocused).

## Milestone 1 — Foundation ✅
- [x] Assemblies: Core / Content / Sim / Presentation / Gameplay / Editor / Tests.EditMode
- [x] Core: EventBus (pull-based struct events), SimClock (fixed tick, hitch-safe), WorldConstants
- [x] Content: TerrainMaterialDef, BiomeDef, WorldGenSettings, GameConfig, ContentDatabase, baked ContentDB (Air = index 0)
- [x] World grid: chunk-major 32x32 chunks, 4-byte cells, chunk dirty flags
- [x] World generation (Burst): surface, 3 biomes with warped borders, deposits, caves, bedrock, starting nest
- [x] Terrain renderer: GPU buffers uploaded per dirty chunk + per-pixel "pixel marching squares" shader
- [x] Procedural 32x32 pixel textures per material (override with hand-drawn tiles later)
- [x] Pixel-perfect camera: integer zoom, pixel-snapped position, pan, zoom at cursor, pixel shake
- [x] Click/hold to dig: circular brush, hardness, combo multiplier, debris particles
- [x] Light map: Burst bucket-queue flood fill (8-way, RGB), sky + emissive materials, exact local updates
- [x] Fog of war: reveal radius around dug cells; breaching an unexplored cave reveals the whole cave
- [x] Editor setup menus
- [ ] ShadowCaster2D near camera — deferred until the first dynamic Light2D exists. URP 17.6 has no public shape setter: use a Collider2D shape provider or reflection on `m_ShapePath`.

## Milestone 2 — Living colony ✅
- [x] Items (`ItemDef`) and material drops; piles fall to the floor
- [x] Colony inventory with capacity (royal chamber + storage zones), items in transit reserved
- [x] Queen in the nest: food upkeep, eggs, hatching into workers; starvation slows work and stops laying
- [x] Food: grub nests in topsoil, seeds spawning on the surface (foraging)
- [x] Surface-crawling navigation rules (`NavGrid`) and BFS flow fields per destination kind (parallel Burst jobs)
- [x] Workers: Burst think/move job + deterministic intent resolution; dig, haul, deliver, wander, fall
- [x] Job openings: diggers per frontier cell, haulers per item (no crowds queueing behind a tunnel front)
- [x] Tools: Dig (1), Mine orders (2), Storage zones (3); right mouse erases; orders reach into the fog
- [x] Rendering: one dynamic sprite mesh (Burst), surface-oriented pixel sprites, procedural placeholder atlas, sprite minimum brightness
- [x] Terrain overlays: mining-order stripes (also over fog), storage tint; calmer low-contrast background walls
- [x] UI Toolkit HUD (`UI/Hud.uxml` + `Hud.uss`): resource bar, starving alert, toolbar; debug readout (F3); F6 spawns 50 ants (dev builds)
- [x] Big-number formatting (`NumberFormat`)
- [x] Invariant: cells are either solid (eroding from 255 to 128) or fully dug; no half-open cells
- [x] 35 EditMode tests (incl. end-to-end hauling from the surface and mining orders)
- [x] Playtest fixes: ants crawl on the back wall of dug tunnels and use octile path costs (direct routes instead of hugging walls); background wall gets a rounded edge kept inside the terrain edge (no square or peeking pixels at the shaft top); material pockets render as blobs instead of 8×8 squares
- [x] Docs: `docs/HOW_TO_PLAY.md` (controls + how the colony works), GDD status markers
- Measured with 1,050 ants: ~0.1–0.15 ms per sim tick, 0.09 ms sprite mesh per frame, ~1.4 ms per local light update.

## Milestone 3 — Automation ✅
- [x] Content: `BuildingDef` (kind, footprint, cost, build work, speed, recipes, buffers, placeholder art) and `RecipeDef`, baked into `ContentDB` (dense indices, per-item consumer slots). Items: copper, iron, fungus. `ContentSetup` creates the defaults.
- [x] `BuildingSystem`: placement checks (open/revealed/floor/occupied/cost), blueprints paid on placement, refunds on removal, re-orienting a transport cell by placing over it
- [x] Transport network: Silk Belt (left/right), Root Lift (up/down), Splitter (round-robin), Filter (one item one way); 2 items per cell, downstream-first stepping, merging, back-pressure and jam detection, head-on belts never connect
- [x] Sinks: end of a line delivers into a machine or into the stock (royal chamber / storage zone); per-cell routing masks so items only enter lines that can deliver them
- [x] Machines: input buffers, round-robin recipe choice, products onto an output belt or left as a pile (pause at `outputLimit`); Smelter (copper, iron) and Fungus Garden (seeds → fungus)
- [x] Workers: build job (Build flow field, slots per site), deliveries to machines (consumer fields per item, sticky machine-vs-stock choice with `consumerBias`), loading belts, dropping undeliverable loads as piles; falling drops land on belts
- [x] Flow fields: one shared field-major array (base kinds + consumer slots) read by the Burst ant job
- [x] Inventory: machine demand as extra room (stock room clamped at zero), build costs/refunds, income meters per item and net food rate
- [x] Rendering: shared `SpriteMesh`/`SpriteQuads`; `BuildingView` Burst job (sprites, animated belts/lifts, riding items, filter item dot); procedural `BuildingSprites` atlas; dithered blueprints and placement ghosts (vertex alpha → ordered dither in `ColonySprite.shader`)
- [x] Throughput overlay (O): belt flow colours, red jams, machine status tints, idle-worker markers; resource bar income rates
- [x] UI: Build tool (4) with palette (costs, red when unaffordable), Q/E cycle, R rotate, drag to lay belts/lifts (axis-locked, drag direction = flow), right-drag removes; hover info panel; toasts for refused placements
- [x] Debug: F7 finishes all blueprints, F8 adds 20 of every item and 50 food
- [x] Physical storage: royal chamber (40) and storage cells (8 each) hold items of any type, deliveries go to the nearest container with room (sorted by type), erased cells move or drop their items, refunds that don't fit drop as piles; every stored item is drawn (stock mesh behind buildings); hover shows a cell's contents; HUD shows Storage used/total
- [x] Supply job: workers fetch items machines need from the container holding them (per-item supply flow fields, demand = machine room minus items already on the way)
- [x] 50 EditMode tests (15 new, incl. physical storage and feeding a machine from the stock: belts, jams, splitter, filter, machines, placement, removal, build tool drag, workers building and feeding a machine, drops onto belts, dropping undeliverable loads)
- Measured in the editor: 6,000 loaded belt cells → belt step ~0.55 ms per tick, full sim tick ~0.8 ms, building render ~0.45 ms per frame.

## Known limits / follow-ups
- **Hands-on feel pass pending** (mouse dig, painting orders/zones, pan/zoom, laying belts by dragging, placing machines). Automated checks drive the sim and tools directly; tuning lives in `GameConfig.asset`.
- Machines and buildings draw from the same stock; there is no way to reserve items for construction or to limit what machines may take.
- No unloader building yet: stored items reach machines only by workers, never onto belts.
- Storage cells show at most 8 items each (the display cap matches the default capacity; raising `storageItemsPerCell` stores more than is drawn).
- A machine runs whichever recipe it can, in turn; there is no per-machine recipe choice yet.
- Splitters and filters cannot connect directly to each other (put a belt cell between them).
- The belt step is plain C# (fine at 6,000 cells); move it to Burst when networks get much bigger.
- The flow overlay shows colours only; exact rates are in the hover panel. No numbers drawn in the world.
- Iron has no use yet; copper is only used for filters. Spending them comes with research / more buildings (M4).
- Belts need no floor (they can be strung across a chamber). Machines need a flat floor; if it is dug away later the machine keeps floating.
- Placeholder sprites for buildings are generated in code (`BuildingSprites`).
- Ants following the same flow field walk in single file and overlap ("conga line"). Add small per-ant lateral offsets or lane jitter when it starts to matter.
- `LightSystem.UpdateDirty` unions all dirty chunks into one rect. With many ants digging at distant fronts this grows; split into clusters.
- Job priorities are global weights in `GameConfig.worker` (dig, haul) and `GameConfig.logistics` (build, machine preference); a player-facing priority UI comes later.
- Food balance is first-pass: the Fungus Garden roughly doubles what foraged seeds are worth, but supply is still limited by seed spawns.
- Debris particles: capped per frame (`feedback.maxParticlesPerFrame`).
- Placeholder art is generated in code (`ColonySprites`, `MaterialTextureBuilder`); replace with real sprites/tiles.
- Light falloff and fog use Bayer dithering; tune `lightBands` if it reads too noisy.
- Sky has no day/night yet (GDD 2.8).

## Next: Milestone 4 — Incremental layer
Contracts, active abilities, random events, research tree v1, offline progress, save/load.

## Missing / not built yet
Things the current build is still missing, grouped by area. Many are planned for a specific milestone (in brackets).

**Food & economy**
- Food sources: seeds on the surface, grubs from grub nests, and the Fungus Garden (seeds → fungus). No aphid ranches or leaves as fungus feed yet.
- Food is a colony-wide stock with upkeep; no per-ant hunger/rest needs.
- Ores are spent on buildings and smelted into copper and iron, but iron has nothing to be spent on yet, and there is no crusher or other processing [M4+].
- Storage cells accept any item; no per-zone item filters yet.
- No buffers / chests, no unloader that puts stock items onto belts.

**Colony & ants**
- Only the Worker caste; no miners, haulers, nurses, builders, soldiers, scouts, engineers [M6].
- Eggs hatch where they are laid; no nurses carrying brood, no brood chambers [M6].
- No player-facing job priorities (weights are only in `GameConfig.worker`).
- No ant XP/levels or traits.
- No ant possession mode [M6].
- Ants on the same route walk in single file and overlap.

**World**
- Only 3 of the 7 planned biomes (topsoil, clay, stone); no crystal caverns, fungal depths, magma, ruins [M5].
- No fluids (water/lava), flooding, gas, cave-ins or unstable materials [M5].
- Material flags *Flammable*, *Conductive*, *Unstable* have no behaviour yet.
- No day/night cycle or weather (GDD 2.8).
- No roots, underground lakes or other cave features beyond open caverns.

**Gameplay layers**
- No contracts/goals, active abilities, random events [M4].
- No research tree [M4].
- No save/load, autosave or offline progress [M4].
- No enemies, combat or defense [M5].
- No pheromones [M6].
- No prestige (Nuptial Flight) [M7].

**Presentation**
- All art is placeholder generated in code (ant/queen/egg sprites, terrain textures).
- No audio.
- No pixel font or pixel-art UI skin; default font.
- No queen panel, research UI, settings menu, offline summary. Tooltips are limited to the hover info panel.
- Only the flow overlay; no light, pheromone or ore-scan overlays.
- ShadowCaster2D and dynamic lights not used yet (deferred until glow ants / possession).
- No number pops when items are delivered or crafted.
- Buildings have no sounds or particles (smelter smoke, garden spores).
