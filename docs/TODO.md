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

## Known limits / follow-ups
- **Hands-on feel pass pending** (mouse dig, painting orders/zones, pan/zoom). Automated checks drive the sim directly; tuning lives in `GameConfig.asset`.
- Ants following the same flow field walk in single file and overlap ("conga line"). Add small per-ant lateral offsets or lane jitter when it starts to matter.
- `LightSystem.UpdateDirty` unions all dirty chunks into one rect. With many ants digging at distant fronts this grows; split into clusters.
- Job priorities are global weights in `GameConfig.worker`; a player-facing priority UI comes later.
- Food balance is first-pass: a large colony starves fast without fungus farms (M3+).
- Debris particles: capped per frame (`feedback.maxParticlesPerFrame`).
- Placeholder art is generated in code (`ColonySprites`, `MaterialTextureBuilder`); replace with real sprites/tiles.
- Light falloff and fog use Bayer dithering; tune `lightBands` if it reads too noisy.
- Sky has no day/night yet (GDD 2.8).

## Next: Milestone 3 — Automation
Conveyors, lifts, splitters/filters, first processing chambers and recipes, throughput overlay.

## Missing / not built yet
Things the current build is still missing, grouped by area. Many are planned for a specific milestone (in brackets).

**Food & economy**
- Only two food sources: seeds on the surface and grubs from grub nests. No real food chain yet (fungus farms, aphid ranches, leaves as fungus feed) [M3+].
- Food is a colony-wide stock with upkeep; no per-ant hunger/rest needs.
- Ores have no use yet: no processing (crusher, smelter), recipes or anything to spend them on [M3].
- Storage is one shared pool; no per-item sorting or physical stockpiles shown in storage zones.

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
- No queen panel, build menu, research UI, tooltips, settings menu, offline summary.
- No overlays (flow, light, pheromones, ore scan).
- ShadowCaster2D and dynamic lights not used yet (deferred until glow ants / possession).
- No number pops when items are delivered.
