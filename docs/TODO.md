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

## Milestone 4 — Incremental layer ✅
- [x] Content: `ResearchDef` (stat effects via `StatKind`, building / ability unlocks), `AbilityDef`, `ContractDef`, `RandomEventDef`, recipe knowledge output; baked into `ContentDB` with id lookups; buildings / abilities named by research start locked
- [x] Sim: `Modifiers` (stat totals read by ants, belts, machines, capacity, eggs, forage, offline, contracts, abilities), `ColonyStats` (lifetime counters), `ProductionMeter` (rolling 5-minute net production), knowledge in the inventory
- [x] Research tree v1 (16 nodes, 4 tiers), Archive building (studies copper / crystal / amber / fossil / star fragment into knowledge), iron and coal spent on research
- [x] Contracts: intro chain (6) + 11 repeatables that grow; claim for rewards; board of 3 slots (+1 by research)
- [x] Abilities: Frenzy, Seismic Sense (scanned ore glows: `CellFlags.Scanned`), Royal Decree; cooldowns, cast at the cursor (Z / X / C) or from the bar
- [x] Random events: meteorite (crater + lump of meteorite → star fragments), rich vein, golden beetle (critter, click to catch), trapped ants (pocket, rescue in time)
- [x] Save / load: versioned, sectioned, deflate-compressed binary; content by id; world as planes; header readable alone; backup file; autosave in the background; save on quit; F5 / F9; migrations hook
- [x] Offline progress: rate replay at 50% for up to 8 h (research raises both), storage-limited, upkeep, eggs with a food reserve; Welcome back panel
- [x] UI: contracts panel, event cards with Go, ability column, research tree panel with hover details, Esc menu (pauses), welcome back, Knowledge in the resource bar, locked buildings in the palette, toasts for research / abilities / contracts / events; golden beetle sprite with sparkle, trapped-ants marker, Archive sprite
- [x] Debug: F8 also gives 50 knowledge, F10 starts a random event; F3 shows knowledge, research and the next event timer
- [x] 71 EditMode tests (21 new: research, locks, studying, contracts, abilities, all four events, offline progress, save round trip, id remapping, damaged / newer saves)
- Measured in the editor: save capture on the main thread ~1.5 ms, background compress + write ~120 ms, load ~170 ms (incl. rebuilding views); a young 1024×1536 world saves to ~270 KB. 65 ants with mining orders: ~0.11 ms per sim tick.

## Milestone 5 — Depth & danger ✅
- [x] Content: `EnemyDef` (behaviour, combat, hazard immunities, raider appetite, loot, placeholder art), `BiomeDef` fluid pockets / ruin rooms / creature lists, defense `BuildingKind`s (Gate, Trap, GuardPost, Wall with a fill material), `AntCaste` and the *unlock caste* research effect, stats `AntHealth` / `AntAttack` / `HazardResistance` / `DefenseStrength`, contract kind *defeat enemies*, `GameConfig` sections `fluids`, `combat`, `threats`; baked into `ContentDB` (enemies, raiders, biome creatures)
- [x] Four new biomes (crystal caverns, fungal depths, magma zone, ancient ruins) with 10 new materials (sand, quartzite, mycelium, giant fungus, basalt, obsidian, magma rock, ruin brick, relic, packed earth) and new pixel patterns (sand, brick, mycelium); sand in the topsoil and clay, lakes in the clay / stone / caverns / fungal depths, gas and lava pockets, ruin rooms
- [x] Fluids: separate 2-byte fluid plane; Burst cellular automaton for water, lava (thick, creeps) and gas (rises), awake chunks only (hidden chunks sleep while playing); lava + water → obsidian; evaporation / thinning in explored tunnels; settled at world generation (pockets dropped to floors / ceilings first); fluid in the terrain shader (water with a surface row, churning self-lit lava, dithered gas); lava lights the light map
- [x] Hazards: deep water / dense gas / lava block paths; ants swim out; drowning, burning, choking; cave-ins of sand (supported by buildings, storage, blueprints, the royal chamber), fallen sand ordered for mining; cells turning solid push fluid up and ants, enemies and piles out
- [x] Ant health and regeneration; deaths drop carried items; death toasts and particles
- [x] Enemies: cave creatures spawn when a cave is breached (per biome, dormant until ants come close, hunt along an ants field, leash home); escalating termite raids from the surface with a warning, eating in the royal chamber (queen stops laying), leaving along an exit field; combat both ways; loot (chitin, obsidian, relics) and knowledge; repelled raids pay knowledge
- [x] Soldiers: research unlock, soldier share setting (HUD), soldier eggs, hunting within range, guard posts / queen when idle; offline progress hatches by caste
- [x] Defense buildings: Earth Wall (drag to paint; becomes solid packed earth), Resin Gate (blocks enemies, mends, broken → free rebuild), Resin Trap (damage + hold, re-arm), Guard Post; their sprites; gate damage tint
- [x] Research tiers 5–6 (6 nodes), 5 contracts, Archive studies relics
- [x] Rendering: soldier walk cycle and six enemy sprites (own palettes), health bars, hurt flashes; HUD soldier share, ants (soldiers) count, raid and creature cards, raider alert, toasts (deaths, cave-ins, broken gates), hover info for enemies, fluids, gates and traps; research tree with six tiers
- [x] Save format 2: versioned sections, `FLUD` and `THRT`; format 1 saves load (checked with the M4 save)
- [x] Debug: F11 starts a raid; F3 shows soldiers, enemies, raid timer and size, awake fluid chunks, ants lost
- [x] 93 EditMode tests (22 new: fluid falling / levelling / sleeping, gas rising, lava creeping and turning to obsidian, paths and deep water, displacement, world-gen pockets and ruin rooms, swimming out, drowning, sand collapse / supports / burial, walls, soldier share and eggs, offline soldiers, creatures on breach, soldiers vs creature with loot, creature kills, raids eating and leaving, gates holding until broken, traps, save round trip of the new state)
- Measured in the editor: a new 1024×1536 world generates in ~25 ms and settles its fluids in ~0.6 s; 1,000 ants 0.09 ms per tick (worst 0.6 ms); with 30 cave spiders, 60 soldiers and a 40-termite raid 0.24 ms per tick (worst 1.2 ms).

## Milestone 6 — Pheromones & castes ✅
- [x] Decisions (asked and confirmed): **Trail** replaces "Haul to" (storage zones already do that); all six castes; nurses are a boost, not a requirement; the Pheromone tool is available from the start (Defend once soldiers exist), the Emitter and Pheromone Burst are research
- [x] Content: `CasteDef` (egg cost, upkeep, HP, bite, speed / dig / build multipliers, carry, job weights, scout sense radius, engineer boost), `AntCaste` Miner / Hauler / Nurse / Builder / Scout / Engineer, `ContentDB` caste table (+ Burst copy, defaults for missing castes, `RefreshCastes`), `BuildingKind` Nursery / Emitter (+ `IsChamber`), `AbilityKind.PheromoneBurst`, `ContractKind.HatchCaste`, `PheromoneKind`, `GameConfig` sections `castes`, `pheromones`, `possession` (+ camera follow, pheromone colours); worker / soldier numbers moved out of `GameConfig` into the caste assets
- [x] Caste mix: per-caste shares (5% steps, ≤50% each, ≤80% together), queen lays the caste furthest below its share, upkeep and egg cost per caste, research unlocks castes; castes panel (K) replaces the soldier box
- [x] Specialists: miners (dig ×3, dig only), haulers (carry 3, pick up / withdraw / deliver in stacks), builders (build ×3), nurses (eggs → Nursery, +50% hatch, eggs there don't count toward the queen's limit, carried eggs age and hatch), scouts (frontier field by a parallel Burst job, sense 10 cells twice a second, ore glows, chase and catch golden beetles), engineers (tend one machine each, +50%)
- [x] Pheromones: `PheromoneSystem` (byte per kind and cell, per-chunk counts, decay Burst job once a second, emitters sustain, traffic refreshes trails, render plane 2 bits per kind); flow fields with pheromone step costs (trail 1/2, Avoid ×4, Dial ring of 16) and late sources for Dig / Gather priority; Defend joins the guard field; no jobs inside Avoid; enemy fields ignore scents; trail speed ×1.5
- [x] Pheromone Emitter building (R / click picks the scent), Nursery building, their sprites; Pheromone Burst ability (V): rally field, ants within 20 cells rush there 50% faster for 15 s
- [x] Possession: F / toolbar button + click; pan keys steer through the crawling rules; left mouse bites (enemy or rock, dig combo), right mouse / Space picks up / delivers / drops; faster, half damage; camera follow, Light2D, gold marker; F / Esc lets go; death releases it
- [x] Rendering: caste walk cycles (palette swaps with small silhouette changes), carried eggs, stacked loads, possessed marker, eggs in nursery cradles; pheromone speckles in the terrain shader (faint, clearer with the Scent tool); emitter scent dot
- [x] UI: Scent tool (5) with palette and Q/E, F Possess / Let go button, K Castes button and panel, V on the ability bar, hover info for ants, scents, nurseries, emitters and tended machines, possession help, toasts (possession, caste unlocks, Burst); input actions ToolPheromone, Possess, Interact, Ability4, Castes
- [x] Research rows 4–6 (8 nodes), 3 caste contracts, `ContentSetup` creates the castes and M6 content and fixes the soldier research text
- [x] Save format 3: `COLN` 3, `ANTS` 3, `PHER`; emitter kind kept raw; format 2 saves load (checked with the M5 save)
- [x] Offline progress hatches by caste share (incl. eggs carried by nurses)
- [x] 113 EditMode tests (20 new: scent painting / fading / erasing, emitters, trails shorten paths and traffic refreshes them, Avoid, Dig and Gather priority, Defend, Pheromone Burst, pheromone save; caste shares and limits, miner, hauler, builder, nurse + nursery, scout, engineer, possession (move, dig, carry, deliver, release, death), caste save, offline castes)
- Measured in the editor: 2,000 ants of all castes, scents painted: 0.32 ms per tick on average (worst 2.8 ms); frontier scan 0.8 ms (every 3 s at most, scouts only); a full rebuild of all 35 flow fields 8 ms.

## Known limits / follow-ups
- **Hands-on feel pass pending** (mouse dig, painting orders/zones, pan/zoom, laying belts by dragging, placing machines; M4: clicking Claim / research nodes / ability buttons, aiming Seismic Sense, catching the beetle with the mouse, Esc / F5 / F9; M5: painting walls and gates, raid / creature cards and Go; M6: painting scents, the castes panel buttons, steering a possessed ant with WASD and biting with the mouse, aiming the Pheromone Burst). Automated checks drive the sim, tools and panels directly; tuning lives in `GameConfig.asset` and the content assets.
- Caste, pheromone and possession balance is first-pass (caste costs and multipliers, shares, scent fade time, trail speed, priority distance, Burst numbers, possession numbers).
- Specialists only do their own jobs (by their job weights): miners stand idle without mining orders, nurses without a nursery. That is the point of the mix, but the panel only hints at it for nurses.
- Scents are on / off for the simulation; strength only sets how long they last and how they are drawn. No diffusion. Emitters keep only cells that are already scented (plus their own spot).
- Scouts only sense rock from tunnels the colony has explored; they never walk into undiscovered caves (those still open by breaching, with their creatures).
- Engineers stay by their machine even while it waits for input.
- Possession: no jumping, no building or painting while possessing, aiming bites needs the mouse (gamepad: Y possesses, A interacts, but there is no stick aim). The possessed ant's light has no shadows and shines a little into the rock.
- Pheromone Burst moves every caste in range, soldiers included (they go back to hunting afterwards).
- Pheromones do not fade while the game is closed. Possession and a running Burst are not saved.
- Debug-spawned ants of a still-locked caste don't show in the castes panel.
- The worker and soldier numbers that used to be in `GameConfig.asset` are still in that file as unused fields until it is saved again.
- Combat and hazard balance is first-pass (enemy stats, raid timing and size, soldier cost, damage per second, lake / gas / lava amounts, sand amounts).
- Fluids have no pressure: water does not rise through U-bends, and a lake surface can keep a slope of one level (of 255) per cell. Hidden lakes only settle once the colony has revealed part of their chunk, so a freshly breached lake may still be levelling.
- Ants only know "blocked or not" for fluid: they route around deep water, dense gas and lava but walk through shallow water and thin gas (which hurts).
- Fluids are only a hazard: no pumps, water wheels, irrigation, or magma heat for smelting yet. Gates do not hold back fluids (walls do).
- Sand only collapses when a cell below it is dug out; sand left hanging over open caves by world generation stays until disturbed.
- Workers never flee; they keep working next to enemies and bite back weakly. Soldiers can't be ordered individually (share, guard posts, Defend scent and the Pheromone Burst).
- Enemies don't dig and never leave their cave's surroundings (cave creatures) or the path to the queen (raiders). Raiders come along the surface and take the shortest way in.
- No raids, creatures or hazards while the game is closed (offline progress only replays production).
- Rebuilding a broken gate counts as building one for *Build* contracts.
- Ruin rooms are a regular grid; no story, artefacts or endgame tech yet. Crystal resonance is not in.
- Progression balance is first-pass (research costs, contract sizes and rewards, event timing, ability cooldowns, offline efficiency).
- Machines and buildings draw from the same stock; there is no way to reserve items for construction or to limit what machines may take.
- No unloader building yet: stored items reach machines only by workers, never onto belts.
- Storage cells show at most 8 items each (the display cap matches the default capacity; raising `storageItemsPerCell` stores more than is drawn).
- A machine runs whichever recipe it can, in turn; there is no per-machine recipe choice yet.
- Splitters and filters cannot connect directly to each other (put a belt cell between them).
- The belt step is plain C# (fine at 6,000 cells); move it to Burst when networks get much bigger.
- The flow overlay shows colours only; exact rates are in the hover panel. No numbers drawn in the world.
- Offline progress replays the last 5 minutes of net production: it does not notice mining orders running out or machines running dry, and more workers hatched offline do not raise income. Fine at 50%; revisit if players exploit it.
- One save slot (+ backup); no slot picker, no Steam cloud yet. The save folder depends on Player Settings → Company Name (still `DefaultCompany`): set it before the first public build, since changing it later orphans saves.
- Research is bought instantly (no research time, no Archive requirement); there is no respec. The tree has no lines between nodes; prerequisites show in the hover text.
- Contracts can't be skipped or rerolled. Contract progress counts everything after a contract appears, including offline deliveries.
- Random events only start while the game runs; meteorites always land near the nest. The golden beetle can't be caught with a gamepad unless the pointer is moved onto it.
- Royal Decree eggs may share a floor cell (drawn on top of each other).
- Seismic Sense has no radius preview while aiming.
- Iron and coal are only spent on research for now; no iron buildings / reinforced belts yet.
- Belts need no floor (they can be strung across a chamber). Machines need a flat floor; if it is dug away later the machine keeps floating.
- Placeholder sprites for buildings are generated in code (`BuildingSprites`).
- Ants following the same flow field walk in single file and overlap ("conga line"); trails make it more visible. Add small per-ant lateral offsets or lane jitter when it starts to matter.
- `LightSystem.UpdateDirty` unions all dirty chunks into one rect. With many ants digging at distant fronts this grows; split into clusters.
- Job priorities are global weights in `GameConfig.worker` (dig, haul) and `GameConfig.logistics` (build, machine preference), times each caste's job weights; the player steers them with Dig / Gather scents. A priority panel comes later if scents aren't enough.
- Food balance is first-pass: the Fungus Garden roughly doubles what foraged seeds are worth, but supply is still limited by seed spawns.
- Debris particles: capped per frame (`feedback.maxParticlesPerFrame`).
- Placeholder art is generated in code (`ColonySprites`, `MaterialTextureBuilder`); replace with real sprites/tiles.
- Light falloff and fog use Bayer dithering; tune `lightBands` if it reads too noisy.
- Sky has no day/night yet (GDD 2.8).

## Next: Milestone 7 — Prestige
Nuptial Flight, Genetic Memory tree, world modifiers (GDD 2.12).

## Missing / not built yet
Things the current build is still missing, grouped by area. Many are planned for a specific milestone (in brackets).

**Food & economy**
- Food sources: seeds on the surface, grubs from grub nests, and the Fungus Garden (seeds → fungus). No aphid ranches or leaves as fungus feed yet.
- Food is a colony-wide stock with upkeep; no per-ant hunger/rest needs.
- Ores are spent on buildings and smelted into copper and iron; iron and coal are spent on research. No crusher, iron buildings or other processing yet.
- Storage cells accept any item; no per-zone item filters yet.
- No buffers / chests, no unloader that puts stock items onto belts.

**Colony & ants**
- No late castes yet: glow ants that carry light, heat-resistant ants for the magma zone.
- No queen evolution tree (castes come from research).
- Priorities are only Dig / Gather scents and per-caste job weights; no priority panel.
- No ant XP/levels or traits.
- Ants on the same route walk in single file and overlap.

**World**
- All 7 planned biomes exist; crystal resonance, magma heat for smelting and the ruins' story / endgame tech are not in yet.
- Fluids are a hazard only: no pumps, water wheels, irrigation or other uses; no fluid pressure.
- Material flags *Flammable* and *Conductive* have no behaviour yet (*Unstable* = sand collapses).
- No day/night cycle or weather (GDD 2.8).
- No roots, underground lakes or other cave features beyond open caverns.

**Gameplay layers**
- Events: no golden aphid (needs aphids).
- Research: no conveyor tiers or QoL automation (auto-designate veins); no queen evolution tree.
- One save slot; no settings menu, no Steam cloud saves.
- Threats: termite raids and one cave creature per deep biome; no birds, surface spiders or rival ant colonies, no enemies that dig.
- Pheromones don't diffuse or mix; no "Haul to" scent (storage zones do that).
- No prestige (Nuptial Flight) [M7].

**Presentation**
- All art is placeholder generated in code (ant/queen/egg sprites, terrain textures).
- No audio.
- No pixel font or pixel-art UI skin; default font.
- No queen panel or settings menu. Tooltips are limited to the hover info panel (and research hover details).
- Overlays: flow, pheromones (always drawn faintly) and the Seismic Sense / scout glow; no light or full ore-scan overlay.
- One dynamic light (the possessed ant's); ShadowCaster2D still not used (deferred until glow ants).
- Caste sprites are palette swaps of the worker; nursery and emitter sprites are generated placeholders.
- No number pops when items are delivered or crafted.
- Buildings have no sounds or particles (smelter smoke, garden spores).
