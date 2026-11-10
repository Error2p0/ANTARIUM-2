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

## Milestone 7 — Prestige ✅
- [x] Decisions (asked and confirmed): princesses come from a **Princess Chamber** machine and raise the memory a flight earns; Genetic Memory can be spent **any time**; the player **picks** world modifiers from the unlocked ones
- [x] Content: `GeneticDef` (tree position, cost + cost per level, max level, prerequisites, effects: stat, starting workers / food / knowledge / items, inherited research, modifier slots, unlock modifier, max princesses, reveal radius, Vein Instinct), `WorldModifierDef` (ore / cave / water / lava / gas thresholds, biome thickness, colony stats), `RecipeDef.princesses`, `BuildingDef.princessSlots`, `PlaceholderArt.PrincessChamber`, stats `GeneticGain` / `RaidSize` / `RaidFrequency` / `CreatureCount`, `GameConfig.prestige`; baked into `ContentDB` (genetics by tree position, modifiers, modifiers named by a trait start locked); `ContentDatabase.genetics` / `worldModifiers`
- [x] Sim: `Legacy` (memory, earned, flights, trait levels, records; buy / check / totals / unlocked modifiers / slots / princess limit), `Simulation.Prestige` (legacy and modifiers per colony, trait effects, starting bonuses, buying traits by command, flight estimate, `TryNuptialFlight` returns the next legacy), `WorldTweaks` threaded through world generation, `Simulation.BiomeAtDepth` with the world's biome scale (raids, creatures, rich veins), raid size / frequency and creature counts read the new stats, princess recipes in machines (pause at the limit), Vein Instinct
- [x] Princess Chamber (research *Nuptial Flight*, tier 6) with *Raise a princess* (12 fungus + 2 amber → 1 princess, 60 s); 19 genetic traits in 4 tiers; 9 world modifiers (4 behind *Strange Worlds*); `ContentSetup` creates them
- [x] Flight: confirm on the N screen, princesses fly out of the nest shaft (4.5 s, sim paused, camera follows), the next colony is founded on a new seed with the chosen modifiers and saved at once, the genetic tree opens; quitting mid-flight founds the colony first
- [x] Rendering: princess sprites (resting, two wing frames), princess chamber sprite (amber arch, three beds, royal jelly that drips while working), resting princesses on the beds, flying princesses
- [x] UI: Nuptial Flight screen (N: breakdown, modifiers with slots, locked ones, confirm), Genetic Memory tree (G: levels, costs, owned / done, hover details), toolbar buttons G Genes / N Flight (gold while princesses wait), resource entries Princesses and Memory, hover info for princess chambers and recipes, toasts (princess raised, trait bought / refused, flight, new colony); input actions View/Genetics (G), View/Flight (N), Debug/GivePrestige (F12)
- [x] *New colony* from the Esc menu keeps the legacy (no memory for the abandoned colony)
- [x] Save format 4: `COLN` 4 (princesses), `LGCY` (legacy by id + world modifiers by id); format 3 saves load with an empty legacy (checked with the M6 save)
- [x] `GameConfig.asset` re-saved: the prestige, caste, pheromone and possession sections are serialized and the unused worker / soldier fields are gone
- [x] 125 EditMode tests (12 new: content baking, buying traits (cost, levels, prerequisites, memory, max level), inherited research now and in new colonies, starting bonuses, princess chamber limit, flight estimate and the next legacy, locked modifiers / slots / princess limit, modifiers shaping world gen and stats, raid size, Vein Instinct, legacy save round trip, loading a save without `LGCY`)
- Measured in the editor: founding a colony (world, fluid settling, traits, 3 modifiers) ~630 ms, the same as a plain one; the flight shows a short hitch when the new colony appears.

## Milestone 8 — Polish ✅
- [x] Decisions (asked and confirmed): sounds **synthesized in code** (replaceable by recorded clips per cue), **OFL pixel fonts**, **key rebinding** now, **game speed** debug only
- [x] Audio: `SoundBank` (45 cues with synth patches or clips, buses, volume, pitch spread, cooldown, voices; ambience per biome + surface; music settings), `Synth` (one-shots, plucked strings, pads, seamless ambience loops), `VoiceLimiter`, `AudioDirector` (events → cues merged per frame, in-view placement: pan, edge and zoom falloff; interface sounds; ambience crossfade by the biome under the camera; mute in background), `MusicPlayer` (generative pad + pentatonic plucks on the DSP clock, in waves, key by depth); `ContentSetup` creates the bank; *Antarium → Audio → Export Sounds to WAV*
- [x] Juice: `NumberPops` (deliveries, food, crafts, knowledge; merged; unlit pixel digits), machine puffs in `DigFeedback`, dialog pop-in, toast slide-in, pressed buttons; `GameConfig.feedback` pop / puff numbers
- [x] UI: VT323 + Silkscreen font assets and a generated 9-slice skin (*Antarium → Setup → Create UI Skin*), HUD restyled (sizes on the font grid, no synthetic bold), title screen, settings dialog (Audio / Display / Gameplay / Controls), achievements dialog, controls and help (F1), Esc menu entries (settings, achievements, help, back to title), key labels from the bindings, toolbar hover tips, dark scrollbars; version 0.8.0 on the title screen
- [x] Settings: `GameSettings` + `SettingsStore` (`settings.json`, sanitized), applied live (volumes, display, UI scale on a copy of the panel settings, shake, edge pan, pops, scientific numbers, autosave); interactive rebinding (`GameInput.Rebindables`, overrides saved in the settings); Erase all progress
- [x] Platform hooks: `IPlatformServices`, `LocalPlatform` (`profile.json`), `PlatformServices.Create`; saves go to the platform's save folder; the game pauses while a store overlay is open
- [x] Achievements: `AchievementDef` (18 kinds, Steam API name, hidden, optional stat), baked into `ContentDB` (API names unique), `AchievementProgress`, `AchievementTracker`; 32 achievements in `Content/Achievements`; unlock toast and chime
- [x] Recorded music: `MusicSettings` track slots (per biome, title, raid, genetic tree, flight and discovery cues, volumes, crossfade), `MusicPlayer` plays them (biome waves with rests and nearest-biome fallback, looping sets crossfaded by `MusicMode`, cues), generative music only where no track is assigned; *Antarium → Audio → Assign Audio Files* + import rules for `Assets/_Antarium/Audio`; `docs/SUNO_PROMPTS.md` with prompts for every track, bed and stinger
- [x] Balancing: `Autopilot` + `BalanceRun` (headless colony, a row per game minute, CSV), *Antarium → Balance* window (headless runs and cost tables: research, contracts, castes, genetics, prestige), F4 game speed ×1/×2/×4/×8 in dev builds
- [x] Title flow: the saved colony waits paused behind the title; offline progress is credited on Continue (title time counts); nothing is saved while the title is up
- [x] 138 EditMode tests (13 new: music set selection, synth waves / determinism / limiting, ambience loop and pad, voice limiter, every cue and biome has a sound, settings sanitize / round trip / broken file, UI scale, scientific numbers, binding overrides round trip, local platform, achievements bake / unlock once / stats / per player, bad achievement content, balance run)
- Measured in the editor: synthesizing the sound bank takes ~1.5 s of CPU (67 one-shot variants 0.34 s, 8 ambience loops 1.1 s, music 0.12 s), so it runs on a worker thread at startup and sounds start once it is done (behind the title screen); a headless balance run takes ~1.5 s per game minute including world generation; audio, pops and puffs run outside the sim tick.

## Known limits / follow-ups
- Media capture: play mode only advances while the Unity window has focus, so a capture run (~5 min) needs the editor in front. Game toasts (cave-ins, raids) show up in HUD stills as they happen. The Pheromone Burst clip (`t13_rally`) is weak and left out of the trailer. The near-surface nest is lit by light wells because sky light only reaches ~32 open cells; deeper nests look much darker.
- **Hands-on feel pass pending**, now including M8: listening to every sound and the music (only checked as numbers and exported WAVs, never heard), clicking through the title screen and settings, rebinding keys, the feel of number pops and puffs, the UI at 1440p / 4K scale (mouse dig, painting orders/zones, pan/zoom, laying belts by dragging, placing machines; M4: clicking Claim / research nodes / ability buttons, aiming Seismic Sense, catching the beetle with the mouse, Esc / F5 / F9; M5: painting walls and gates, raid / creature cards and Go; M6: painting scents, the castes panel buttons, steering a possessed ant with WASD and biting with the mouse, aiming the Pheromone Burst; M7: clicking modifiers, the double-click Take flight, buying traits in the genetic tree). Automated checks drive the sim, tools and panels directly; tuning lives in `GameConfig.asset` and the content assets.
- Sound effects and ambience are synthesized placeholders until recorded files are assigned; music has 10 recorded tracks (title, surface, clay, stone strata, crystal caverns, fungal depths, magma zone, ancient ruins, raid, genetic tree); no stingers yet. The bank's synth patches are first-pass. Biome music changes only between tracks (moving the camera never cuts a track); there is no music for combat with cave creatures, only raids. Positional sounds ignore walls (a sound in view is heard even behind rock).
- Fixed key letters still appear in some messages and hints (e.g. *castes panel (K)*, *flight screen (N)*); buttons follow the bindings, messages don't yet. The gamepad layout can't be rebound.
- Key names follow the keyboard layout (Czech: number row + ě š č ř); a bound key with no printable label shows its Input System name.
- UI scale is whole numbers only (pixel fonts); at 1440p *Auto* picks 2×, which is big; 1× is small. Window mode and resolution apply only in builds.
- Hover tips exist only for the toolbar buttons. The debug readout (dev builds) stays visible over the title screen.
- Achievements: kept locally (`profile.json`) and merged with Steam both ways; debug keys (F6–F12, dev builds only) can unlock them, and with *Use Steam in Editor* those reach the real Steam account; *Erase all progress* can't reset Steam achievements (the next merge brings them back). Most achievements count within one colony.
- Balance autopilot never builds, researches only the cheapest node and digs one fixed pattern: it measures the hand-run early game, not a skilled player.
- Prestige balance is first-pass (score weights and the square root, princess recipe and limit, trait costs and effects, modifier numbers). A first flight after a full colony earns tens of memory; check that tier 1–2 traits don't trivialise the next start.
- Genetic Memory can't be refunded (no respec) and there is no "new bloodline" option: a fresh legacy needs the save deleted.
- Princesses are a colony counter, not ants: they don't walk or eat, a chamber shows at most 3 (more princesses with *Royal Lineage* need more chambers to be seen), and removing chambers keeps them. Chambers don't raise princesses while the game is closed (offline progress replays items, food and knowledge only).
- The flight is a cosmetic 4.5 s sequence from the nest shaft (even if it was walled up); it can't be skipped. The new colony appears with a ~0.6 s hitch (world generation and fluid settling on the main thread).
- World modifiers are picked only on the flight screen; *New colony* from the menu starts without any. Lava / gas modifiers only change biomes that have such pockets (magma zone, fungal depths).
- Inherited research counts toward the next flight's research score.
- Vein Instinct orders every revealed material that drops a non-food item (ore, coal, amber, fossils, crystals, relics) within 2 cells of a dug-out cell, including cells dug by the player and cave-ins.
- The flight screen shows decimals in the system's number format (like the resource bar rates).
- Caste, pheromone and possession balance is first-pass (caste costs and multipliers, shares, scent fade time, trail speed, priority distance, Burst numbers, possession numbers).
- Specialists only do their own jobs (by their job weights): miners stand idle without mining orders, nurses without a nursery. That is the point of the mix, but the panel only hints at it for nurses.
- Scents are on / off for the simulation; strength only sets how long they last and how they are drawn. No diffusion. Emitters keep only cells that are already scented (plus their own spot).
- Scouts only sense rock from tunnels the colony has explored; they never walk into undiscovered caves (those still open by breaching, with their creatures).
- Engineers stay by their machine even while it waits for input.
- Possession: no jumping, no building or painting while possessing, aiming bites needs the mouse (gamepad: Y possesses, A interacts, but there is no stick aim). The possessed ant's light has no shadows and shines a little into the rock.
- Pheromone Burst moves every caste in range, soldiers included (they go back to hunting afterwards).
- Pheromones do not fade while the game is closed. Possession and a running Burst are not saved.
- Debug-spawned ants of a still-locked caste don't show in the castes panel.
- Combat and hazard balance is first-pass (enemy stats, raid timing and size, soldier cost, damage per second, lake / gas / lava amounts, sand amounts).
- Fluids have no pressure: water does not rise through U-bends, and a lake surface can keep a slope of one level (of 255) per cell. Hidden lakes only settle once the colony has revealed part of their chunk, so a freshly breached lake may still be levelling.
- Ants only know "blocked or not" for fluid: they route around deep water, dense gas / smoke and lava but walk through shallow water and thin gas (which hurts), and past burning cells (which burn them).
- Gates do not hold back fluids (walls, pumps and water wheels do). Fluids have no pressure: water rises only through pumps.
- Sand only collapses when a cell below it is dug out; sand left hanging over open caves by world generation stays until disturbed.
- Workers never flee; they keep working next to enemies and bite back weakly. Soldiers can't be ordered individually (share, guard posts, Defend scent and the Pheromone Burst).
- Enemies don't dig and never leave their cave's surroundings (cave creatures) or the path to the queen (raiders). Raiders come along the surface and take the shortest way in.
- No raids, creatures or hazards while the game is closed (offline progress only replays production).
- Rebuilding a broken gate counts as building one for *Build* contracts.
- Ruin rooms are a regular grid; no story, artefacts or endgame tech yet. Crystal resonance is not in.
- Progression balance is first-pass (research costs, contract sizes and rewards, event timing, ability cooldowns, offline efficiency).
- Reserves are colony-wide counts, not per container: an unloader may empty a chest while the reserved items lie elsewhere. Building costs ignore the reserve (they are what it is for).
- Unloaders take only from the one cell behind them (a storage cell, chest or royal chamber cell). Chests don't draw their items one by one (a heap and a dot for the main item); the configure panel lists every non-food item, even ones never found.
- Upgrading a belt to iron turns it into a blueprint until workers build it: the line stops there meanwhile.
- Splitters and filters in a closed square of four can pass an item round the square a few times before it leaves.
- Configure (I) has no gamepad binding yet (Steam Deck pass).
- Storage cells show at most 8 items each (the display cap matches the default capacity; raising `storageItemsPerCell` stores more than is drawn).
- The belt step is plain C# (fine at 6,000 cells); move it to Burst when networks get much bigger.
- The flow overlay shows colours only; exact rates are in the hover panel. No numbers drawn in the world.
- Offline progress replays the last 5 minutes of net production: it does not notice mining orders running out or machines running dry, and more workers hatched offline do not raise income. Fine at 50%; revisit if players exploit it.
- One save slot (+ backup); no slot picker. The save folder depends on Player Settings → Company Name (*Elevate Studios* since 0.9; the old `DefaultCompany` folder is copied once): never change it again, it would orphan saves and Steam Cloud files.
- Research is bought instantly (no research time, no Archive requirement); there is no respec. The tree has no lines between nodes; prerequisites show in the hover text.
- Contracts can't be skipped or rerolled. Contract progress counts everything after a contract appears, including offline deliveries.
- Random events only start while the game runs; meteorites always land near the nest. The golden beetle can't be caught with a gamepad unless the pointer is moved onto it.
- Royal Decree eggs may share a floor cell (drawn on top of each other).
- Seismic Sense has no radius preview while aiming.
- Iron goes into research, iron belts / lifts / gates and the princess chamber; coal into research and smelting iron.
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

## Milestone 9 — Steam release 🟡
Steamworks integration, store page and the first build on Steam (GDD §4). Many steps need your Steamworks account, money and identity; those are marked **(you)**. Requirements and image sizes below are the ones known at the time of writing: check the current Steamworks documentation when doing each step.

**Account and app (you)**
- [x] Steamworks partner account, Steam Direct fee, app created: **App ID 4453940**.
- [x] Developer / publisher: **Elevate Studios** = Player Settings → Company Name (save folder `LocalLow/Elevate Studios/ANTARIUM 2`; the old `DefaultCompany` folder is copied over once).
- [x] Supported languages (English), tags and system requirements entered on the store page.
- [ ] Price, release date (or "Coming soon"), whether there is a demo / Next Fest.

**Steamworks integration (code)**
- [x] Steamworks.NET 2025.165.0 (UPM git package); the Gameplay assembly defines `ANTARIUM_STEAM` while it is installed (`versionDefines`). `StorePlatform` (plain C#: merge, stats, presence) over `SteamClient : IStoreClient` (init with `RestartAppIfNecessary` in builds, `InitEx`, callbacks in `Tick`, `SteamUserStats`, overlay callback → pause); `LocalPlatform` without Steam or with `-nosteam`. Editor opt-in *Antarium → Steam → Use Steam in Editor*. `steam_appid.txt` (4453940) in the project root and in development builds only.
- [x] *Antarium → Steam → Export Achievements and Icons*: `steam/achievements.md` (3 stats, 32 achievements with names, descriptions, hidden flags, progress stats) and 64 icons in `steam/achievement_icons/` (32×32 pixel art ×2: category plate, sprite or glyph, amount for tiers; grey locked twin).
- [x] **(you)** Stats and achievements entered on Steamworks (`docs/STEAM.md` §1).
- [x] Local achievements and stats are merged with Steam both ways on every Steam launch (not only the first), once Steam has the user's stats; stats only go up.
- [x] **(you)** Steam Cloud Auto-Cloud entered: `Saves/*` and `profile.json` (not `settings.json`: display settings belong to the machine), quota (`docs/STEAM.md` §3).
- [ ] Test Steam Cloud: a save syncs to a second machine, and the conflict dialog.
- [x] Rich presence (*In the main menu*, *Founding a colony, N cells deep*, *Generation 3, 420 cells deep*, *Watching a Nuptial Flight*; `steam/rich_presence_english.vdf`) and the overlay pausing the game. **(you)** upload the tokens file (`docs/STEAM.md` §2).
- [ ] Verify against the real Steam client: init, achievements, stats, presence, overlay pause.
- [ ] Steam Deck / controller pass: stick aiming for the cursor, gamepad glyphs, readable text at 1280×800, Steam Input default layout; aim for *Playable* or *Verified*.

**Release build (code)**
- [x] *Antarium → Build → Windows Release / Windows Development* (x86_64, IL2CPP when the module is installed, else Mono; one scene; release has no debug keys and no `steam_appid.txt`; Burst / IL2CPP debug folders moved to `Builds/Symbols/<version>`). Version: Player Settings → Version only (0.9.0).
- [x] **(you)** *Windows Build Support (IL2CPP)* and the VS *Desktop development with C++* workload installed.
- [ ] A first IL2CPP release build (check it starts outside the editor and `Player.log` is clean).
- [x] *Antarium → Build → Apply Release Player Settings*: Company Name, product name, bundle id `com.elevatestudios.antarium2`, fullscreen window at native resolution, resizable, single instance, no Unity splash, minimal stripping.
- [x] App icon set in Player Settings (exe); Steam client icon and shortcut icon uploaded on Steamworks.
- [ ] Check the first-run experience on a clean Windows account.
- [x] SteamPipe: `steam/upload.ps1` writes `app_build_4453940.vdf` + `depot_build_4453941.vdf` to `steam/output/` (git-ignored) and runs `steamcmd +run_app_build` (sets live on `beta`, `-Preview` for a dry run). **(you)** depot id check, `beta` branch, launch option, upload account and steamcmd in `tools/steamcmd/` (`docs/STEAM.md` §4–5).
- [x] Logging location documented in `docs/STEAM.md` (Player.log, crash dumps, symbols).
- [x] 148 EditMode tests (10 new: store merge both ways, waiting for the store's stats, giving up and resetting locally, rich presence only on change, moving the old save folder once; carving into the fog, placing without paying, research all, capture request round trip, the showcase colony builds complete).

**Store page**
- [x] **(you)** Capsule art: header, small, main, vertical, library capsule, library hero, library logo.
- [x] Media capture in the game (`docs/STEAM.md` → *Store page media and text*): `ShowcaseColony` (fixed seed; nest with light wells, storage hall, nurseries, factory hall fed by an ore room through a belt and a root lift, every machine, a shaft to the ruins with an outpost per deep biome, all castes and research) built with the instant edits in `Simulation.Showcase.cs`; scripted shots (`GameBootstrap.Shots.cs`) rendered offscreen by `CaptureRig` at 1920×1080 and a fixed 60 fps, with a drawn cursor; *Antarium → Capture* menu, `-capture` in development builds; `tools/media/make_gifs.py` and `make_trailer.py` (Pillow + imageio-ffmpeg).
- [x] Screenshots: 16 stills at 1920×1080 with the HUD in `Captures/stills` (colony, factory, flow overlay, ore room, queen and storage, the four deep biomes, raid, possession, research, castes, flight screen, genetic tree).
- [x] Trailer: `Captures/trailer/antarium2_trailer.mp4` (~75 s, 1080p60, cards in Silkscreen, `music_title.wav`). End card with the game logo and the Elevate Studios logo from `Assets/_Antarium/Icon`. Thumbnail `Captures/trailer/antarium2_trailer_thumbnail.png` (`tools/media/make_thumbnail.py`).
- [x] **(you)** Tags, system requirements and supported languages entered.
- [x] Short description (252 characters) and *About This Game* (BBCode with 7 GIF slots) in `steam/store/`; 7 GIFs (640×360, 15 fps, ~10 MB together) in `Captures/gifs`.
- [x] **(you)** Screenshots, GIFs and the trailer uploaded; store texts in place.
- [x] **(you)** Content survey completed, including the AI disclosure (pre-generated AI content: the music, made with Suno).
- [ ] Privacy policy (only needed if anything is collected: nothing is) and EULA (Steam's default is fine): confirm nothing else is asked for.
- [x] **(you)** Store page submitted for review (2026-11-08; review takes several business days).
- [ ] **(you)** After approval: make the *Coming Soon* page public. It must be public for at least 2 weeks before release; wishlists start then.

**First upload and release**
- [ ] Upload the first build to the `beta` branch, install it through Steam on a clean machine, test achievements, cloud saves, overlay and offline progress. Uploaded 0.9.0 (BuildID 26441965), but it has the startup bug fixed afterwards (`GameBootstrap` set up the UI before the `UIDocument` had a root): replace it with a new version on `beta` and `default`, then test.
- [ ] Release build log noise: `Hidden/Universal Render Pipeline/GaussianDepthOfField` "stripped from the build" is logged repeatedly (22 times in a 15 s run of the Development build), and "More than one global light" is logged during builds although `Main.unity` has one global light and no player run has logged it. Neither has a known visible effect; look into both.
- [ ] **(you)** Submit the build for review; set the release date; release (or keep it in Coming Soon / playtest).

## Milestone 10 — Content complete 🟡
Build what is still on the *Missing / not built yet* list below, while M9 waits for Steam's approvals. Branch `milestone-10` (off `milestone-9`; builds for Steam keep coming from `milestone-9` until M10 is merged, because M10 changes the save format). Each phase ends playable, verified in the editor (play mode + captures), with tests, docs (GDD / TODO / HOW_TO_PLAY) and its own commits. Save format 5 grows section by section (one version byte per section, older saves keep loading). Phases are ordered so later ones build on earlier ones; each can ship alone.

**Decisions** (asked and confirmed 2026-11-09; the rest are asked before the phase that needs them)
- *Per-ant hunger / rest* is **not built**: the colony-wide upkeep per caste already makes food matter, and per-ant needs with thousands of ants add micromanagement and cost without a visible decision for the player. Ant XP (10.4) gives individual ants their character instead.
- *Pheromone diffusion* and a *Haul to* scent stay out (decided in M6: sharp, predictable scents; storage zones do *Haul to*).
- Hand-drawn art and recorded sound effects can't come from code. M10 builds the **drop-in pipeline** (PNGs / WAVs by naming convention override the generated ones, like the terrain `textureOverride` and *Assign Audio Files*) and better generated placeholders; the art itself is **(you)** or an artist. Both are built: the pipeline *and* better generated art.
- No power network: water wheels and magma heat **boost machines next to them** instead of introducing a power grid (keeps building simple, gives fluids a use).

**10.1 Logistics completion** ✅ (GDD 2.5.3)
- [x] **Chest** (1 cell, 16 items, 4 copper ore, no research): a container like a storage cell; lines ending at it and ants standing on it put items straight in; not on storage zones; removal moves its items. **Unloader** (3 copper, *Stockkeeping*): takes its item (or any) from the storage cell / chest / royal chamber cell behind it onto the belt, lift, router or machine in front, 1.5/s, only what that output can take now.
- [x] **Storage filters**: an allowed-item set per container; **I** on a zone sets the whole connected zone (new cells painted next to it inherit it), on a chest that chest. Deliveries skip refusing containers; acceptance counts room in the containers that take the item.
- [x] **Recipe choice** per machine (any / one recipe; only that recipe's inputs are taken in) and **reserves** per item (stock view from the resource bar: machines' supply jobs and unloaders leave that many; building costs can still use them).
- [x] **Crusher** (2×2, *Ore Crushing*): 2 ore → 3 crushed ore; the smelter makes 1 bar from 2 crushed ore (+1 coal for iron): 75% more metal per ore.
- [x] **Tiers**: Iron Belt (5 cells/s) and Iron Lift (6 cells/s) behind *Reinforced Silk*, Iron Gate (450 HP) behind *Iron Bulwark*; placing another tier of the same kind over a building upgrades it in place (refund, pay, blueprint; belt items stay). Iron sprites for all three.
- [x] Splitters and filters connect to each other: items remember where they came from and are never handed back; routing masks per router and incoming side.
- [x] **Configure panel** (I, rebindable; new action *Tools/Configure*): zone / chest filters, unloader / filter item, emitter scent, machine recipe; stock view from the resource bar; hover texts name the key. Palette wraps before the ability bar (20 buildings).
- [x] Content (`ContentSetup.Logistics.cs`): items crushed copper / iron, 4 recipes, 6 buildings, 4 research nodes (row 7), 3 contracts, 2 achievements (*Quartermaster*, *Iron Highway*; icons exported, chest glyph added). **(you)** enter `ACH_CHESTS_10` and `ACH_IRON_BELTS_100` on Steamworks with the icons from `steam/achievement_icons/` (`steam/achievements.md`).
- [x] Save format 5: `COLN` 5 (zone filters, reserves; chests left out of the container list), `BLDG` 3 (chest filter + contents, recipe choice; unloaders keep "any"). The M9 save loaded and saved again in the editor.
- [x] 157 EditMode tests (9 new: chest filling and its filter, chest refused on a zone, storage filters and acceptance, unloader into a machine with a reserve, unloader item onto a belt, recipe choice, iron belt upgrade keeping items, splitters side by side never handing back, save round trip of chests / filters / reserves / recipe choice / unloader / zone growth).
- Verified in the editor: chest → unloader → iron belts → crusher → smelter → filtered chest runs (16 ore in, 8 copper in the copper-only chest after 25 s), splitter pair, iron lift and gates drawn, configure panel on a chest and on the smelter, research row 7, the wrapped palette; quitting saved format 5 and the next play loaded it intact. The belt step stays plain C#: its cost is per cell per tick, so faster tiers don't add to it.

**10.2 Surface: day, weather, aphids** ✅ (GDD 2.8.1; decisions asked and confirmed 2026-11-09: a full day/night cycle lasts **12 minutes** of game time; **birds scare first, then kill**)
- [x] **Day / night**: a saved clock in days (`SurfaceState`, `SurfaceConfig` in `GameConfig`), day = 65% of the cycle with smooth twilights, a new colony starts at 07:12. The light map's unused alpha became a **sky channel** (RGB = emitters, alpha = sky level); the shaders colour and scale it by daylight and rain, so night needs no relight. Sky gradient with dawn / dusk tints, stars, moonlight. Seeds and leaves only by day. Dawn / dusk toasts and chimes.
- [x] **Weather**: rain (water near the nest each fluid step; water within 40 cells of the surface soaks away afterwards) and drought (forage ×0.5), every 6–10 minutes after the first 10, with event cards. Offline: the clock runs on, no weather, birds or spiders.
- [x] **Plants and leaves**: seeded plants along the surface drop leaves by day (second forage, not food); the **Fungus Garden** takes 2 leaves → 4 fungus.
- [x] **Aphid Ranch** (research *Aphid Herding*): 2 leaves → 1 **honeydew**, aphids drawn walking in it. Honeydew stays a stock item; the queen eats one every 20 s (laying +50%, no upkeep of her own). **Golden aphid** event in a ranch.
- [x] **Roots** grow down from every plant through the topsoil (world generation), tough, drop **root fibre** (ranch and research cost, a contract).
- [x] **Birds** by day: every ant in the open runs underground along a new **Shelter** flow field (`AntState.Sheltering`); after 8 s the strike takes up to 4 ants still in the open near the shadow. **Wolf spiders** (`EnemyBehavior.NightHunter`) come out near the nest at night and slink off at dawn.
- [x] HUD: clock / weather entry, cards for weather, the bird and night hunters, toasts, *taken by a bird*; hover *hiding from the bird*. `SurfaceView` draws plants, aphids, rain, the bird and its shadow. Sound cues BirdCry, BirdStrike, Rain, Dusk, Dawn.
- [x] Content (`ContentSetup.Surface.cs`): items leaf, honeydew, root fibre; material root; 2 recipes; Aphid Ranch; Aphid Herding (research row 8); Golden Aphid event; Wolf Spider; 3 contracts. No new achievements.
- [x] Save: new section `SURF` 1 (format stays 5). The 10.1 save loaded (morning of day 1, clear weather) and play mode saved `SURF` on quit.
- [x] 169 EditMode tests (12 new: day / night clock and leaves only by day, the sky channel, rain soaking away, drought ending, the bird scaring and striking, no bird without ants outside, night spiders out and gone at dawn, honeydew, golden aphid, roots, save round trip, offline clock).
- Verified in the editor: the 10.1 colony at night (stars, dim surface, night hunters card), rain by day (drops, grey sky, water down the shaft and into the meteorite crater), a bird circling over far foragers and striking 4 of them, a new colony's roots under the plants, an Aphid Ranch making honeydew (queen fed, eggs laid) with the golden aphid in it, a wolf spider at night, a sunrise.
- Known limits: the rain amount and the soak rate are first guesses (rain pours down the starting shaft and pools on the royal chamber floor until it soaks away); plants are generated placeholders; the 4 a.m. sunrise on the clock is simply 65% day share; root fibre has few uses yet (more in 10.3 recipes).

**10.3 Living world: fluids and materials** ✅ (GDD 2.2.1; no open questions. Decided while building: the Smelter itself is heated (no separate *Magma Forge*); pumps reach 8 cells back and push 8 cells ahead, because without pressure a pump could neither take water from below nor lift it more than one cell; smoke is its own grey gas kind)
- [x] **Pump** (1 cell, R turns it, *Hydraulics*): 2 cells of water / gas / smoke per second from behind it to its spout (not lava); intake reaches 8 cells back, outlet pushes past full cells up to 8 ahead; fluid can't pass through it (`CellFlags.FluidBlock`), ants can. Drains a flooded shaft from above, lifts water (chain every 8 cells), fills a moat, blows smoke out.
- [x] **Water Wheel** (2×2, no floor, *Water Mills*): water resting on it falls through (2 cells/s per column); power = share of its full rate; machines within 8 cells +50% × power (best wheel counts). **Irrigation**: Fungus Garden and Aphid Ranch with water in or next to them: +50% output (an extra batch every second craft), each craft drinks 48/255 water.
- [x] **Magma heat**: the Smelter with lava, magma rock (`MaterialFlags.Hot`) or a fire in or next to it is heated: +100% speed, skips fuel (`ItemDef.fuel`: coal) and stops taking it in.
- [x] **Fire** (`FireSystem`, `FluidKind.Fire` on solid cells): coal, roots, mycelium and giant fungus burn; moving lava and meteorites light them; spreads (0.25/s per neighbour) only to seen cells touching open air; 15 s, then **ash** (falls like sand); grey **smoke** (`FluidKind.Smoke`, like gas, thins out everywhere); ants next to it take 10/s; water puts it out; digging it out ends it. **Conductive** set on copper / iron ore and glow crystals (used in 10.5).
- Fluid pressure is **not built** (decided): pumps give the player control over water, and lakes / floods stay predictable.
- [x] Presentation: fire, embers, flames licking up and smoke in the terrain shader, fire light; pump and water wheel sprites; flow overlay tints; hover texts (pump, wheel, burning cell, smoke, machine heat / irrigation / wheel boost); red **Fire!** card; fire crackle (water on fire reuses the lava hiss).
- [x] Content (`ContentSetup.Waterworks.cs`): Pump, Water Wheel, research *Hydraulics* and *Water Mills* (row 9), material Ash, irrigation / heat bonuses on the existing machines, coal as fuel, material flags, contracts *Waterworks* and *Mill Race*, sound cue *FireIgnite*. No new achievements.
- [x] Save `FLUD` 2 (fire on solid cells, smoke); `WRLD` unchanged (ash is a material by id, the fluid block is rebuilt by the buildings). The 10.2 save loaded; a save with pumps, a wheel and burning coal saved on quit and loaded again with all of them (fluid blocks restored, the fire burning on).
- [x] 184 EditMode tests (15 new: pump drains and keeps the water, pump drains a flooded shaft from above, pump leaves lava and stops at another fluid, water wheel turns and speeds up a machine and stops when the water runs out, irrigation extra output and drinking, magma and lava heat; fire spreads only along exposed cells, smokes and burns out into ash, water puts it out, moving lava lights coal, a meteorite lights its surroundings as one fire, burnt-out ceiling ash falls, ants next to a fire are burned, digging a fire out, save round trip of fires and pump blocks).
- Verified in the editor (on the 10.2 colony, restored afterwards): a water wheel in a shaft with a three-pump return loop (85% power at first, the smelter beside it +39%), an irrigated fungus garden, a smelter on magma rock smelting iron without coal, a coal seam burning along a tunnel floor into ash with grey smoke, the Fire! card, a meteorite strike; hover texts for all of them.
- Known limits: ants don't route around fires (they walk past and get burned; Avoid scent keeps them out) and enemies are not burned; lava that settled before the colony arrived sleeps and lights nothing until something wakes its chunk; water creeps slowly along long flat channels, so wheels belong right under a waterfall or a pump outlet; a pump keeping a wheel turning is free power (by design, no power grid); smoke and spore gas don't mix in one cell; the irrigation carry (half a batch) is not saved; no fire offline; pumps can't move lava; fire doesn't harm buildings; fire numbers are first guesses.

**10.7a Art and UI pass** (pulled forward from 10.7, built **before 10.4**; decided 2026-11-09)
*Why now:* this phase is done with **Claude Fable 5.2**, which the plan includes only until **2026-11-16** (the weekly limit resets on 11-15). The content phases 10.4–10.6 would use up the limit first, so the art comes now and they reuse it. Everything left in 10.7 (tooltips, overlays, stingers, sound cues, final docs) stays there; it is routine work that doesn't need the stronger model.

*Decisions (asked and confirmed):*
- **Scope**: sprites (ants, queen, eggs, items, buildings, enemies, creatures, surface), **terrain textures** (every material) and the **UI** (skin, panels, icons). Fonts stay (VT323 + Silkscreen, OFL).
- **Art for content that is planned but not built yet is drawn now** (list below), so 10.4–10.6 only wire it in.
- Art is delivered as **PNG files** in `Assets/_Antarium/Art/`, loaded by the override pipeline; the code generators (`ColonySprites`, `BuildingSprites`, `MaterialTextureBuilder`, `UiSetup`) stay as the fallback for anything without a PNG. That way the user or an artist can later edit the PNGs (e.g. in Aseprite) without touching code. How the PNGs are authored is up to you (by hand, or an editor script under `Code/Editor/` writing them from pixel maps); the PNGs are the deliverable and are committed (LFS).
- Generated art, not hand-drawn: the aim is a clearly better, coherent pixel-art style, not final commercial art.

*Instructions for the session (read this first):*
- **Branch**: work on a new branch **`milestone-10-early-art-pass`**, made from `milestone-10` (`git switch -c milestone-10-early-art-pass milestone-10`). Commit there only; it is merged back into `milestone-10` when the user says so, and 10.4 continues from the merged result.
- The usage limit may run out before the end. **Work in the priority order below, verify and commit after every step**, so each finished step stays usable even if the session stops. Update this list (`[x]`) in the same commit.
- **Step 1 is a style test and then a stop**: show the captures and wait for the user's approval or corrections before drawing the rest. Don't redo approved styles later.
- Keep captures cheap: one contact sheet / capture per batch, not per sprite. `capture_game_view --save_path X` writes under `Assets/X`; move the PNG out and delete the folder + `.meta` right after (CLAUDE.md).
- Rules that bind the art (GDD 3.0, 2.4): PPU 16, 1 cell = 8×8 px, point filtering, no compression, integer zoom only. Current sprite sizes are contracts used by the code (worker 12×7, soldier 14×8, queen 24×12, princess 14×10 with `PrincessBeds` positions, building sizes = cells × 8; see the constants in `ColonySprites` / `BuildingSprites`). Change a size only together with the code that depends on it. Colony sprites are lifted to a minimum brightness in dark tunnels, and the body colour carries the silhouette (a dark outline vanishes against dark tunnels; see the note in `ColonySprites`). Item blobs, piles and belt items are greyscale tinted per item through vertex colour; real per-item sprites may replace them, but then the tint path must be handled. Terrain slices are tileable **32×32** per material, readable, read through `TerrainMaterialDef.textureOverride` / `MaterialTextureBuilder`. The terrain shader adds edges, light, fluids, fire and the sky on top, so textures stay calm. The UI skin is 9-slice bevels drawn over the HUD's background colours (`UiSetup`, `UI/Skin/`, `Hud.uss`, `AntariumTheme.tss`), so state colours keep working; UI scale is whole numbers.
- Write **`docs/ART_STYLE.md`**: palette (per biome and for the colony), outline / shading rules, sizes per category, frame counts, animation timing, **the file naming convention**, and how to add a new sprite. 10.4–10.6 (built with another model) follow it.
- Also update GDD 3.0 (art direction, status) and `HOW_TO_PLAY.md` where the player sees something new (e.g. icons). The *Missing* list below: placeholder art lines.

*Steps, in priority order:*
- [x] **1. Style test** (built and **approved** 2026-11-10: ants, queen, terrain, UI frame, 10 × 10 icons and the smelter, all as shown): a palette (24 ramps, `tools/art/palette.py`), the worker (4 walk frames, darker gaster, eye, pale mandible), the queen (banded gaster, 2 frames: she breathes), the smelter (idle / working), terrain *dirt* and *stone*, the UI skin (panel frame, button bevels, inset) and 12 icons in the resource bar (food, ants, eggs, storage, knowledge, copper ore, iron ore, coal, copper, iron, crystal shard, amber), all in the running game.
  - *Decided while building:* the PNGs are written by **`python tools/art/make_art.py`** (Pillow + numpy, like `tools/media`), from ASCII pixel maps and painters; an **animated sprite is one strip PNG** (frames side by side), not a file per frame; a PNG's name is its path under `Art/`; item **icons are 10 × 10** (20 px on screen) and UI-only (items in the world stay tinted blobs); the import takes a material's base / shadow / highlight colours **from its tile**, so edges and walls always match the texture.
  - *Already there from step 2:* `ArtLibrary` (`GameConfig.art`), *Antarium → Art → Import Overrides*, `ArtOverrides.Apply` (strip → atlas rects, wrong size warns and falls back), overrides wired for the worker, queen and smelter, terrain through `textureOverride`, icons through `HudController.SetIcon`. Still to do in step 2: every other slot by name, the EditMode tests.
  - *Found:* `-unity-slice-scale: 4px` draws a 9-slice at two screen pixels per texel (`2px` draws one); the UI blends in linear space, so white bevel overlays need about 4% alpha and black ones 40–65%.
  - Review stills: `CaptureMenu.Run("art", false)` → `Captures/stills/art_*.png` (the stone still raises the ambient light, deep rock is dark otherwise); pieces and palette: `Captures/art/pieces.png`, `palette.png`.
  - 184 EditMode tests pass (no new ones yet).
- [x] **2. Override pipeline**: every replaceable sprite is an `ArtSlot` (PNG name → atlas rects, one per frame of the strip): `ColonySprites.Slots` (`Colony/ant_<caste>`, `queen`, `egg`, `pile`, `carry`, `marker`, `princess`, `golden_beetle`, `golden_aphid`; `Enemies/<name>`), `BuildingSprites.Slots` (`Buildings/<name>`, 27 slots) and the surface atlas (`Surface/plant_fern|bush|flower`, `bird`, `bird_dive`, `aphid`, `bird_shadow`). `ArtOverrides.Apply` copies a PNG over its rects; a missing PNG keeps the generated sprite, a wrong size warns and falls back, clear texels stay clear. Terrain goes through `textureOverride`, icons through `HudController.SetIcon`, the skin through `Hud.uss`. *Antarium → Art → Import Overrides* as in step 1. **8 EditMode tests** (`ArtTests`: slot names unique and well-formed with frames of one size, a PNG replaces only its slot, strips fill every frame in order incl. the queen's split frames, missing → generated, wrong size → warning + generated, clear texels, tile colours, library lookup); 192 pass.
- [x] **3. Terrain**: all 26 materials have a tile (`Art/Terrain/<id>.png`, painters in `tools/art/terrain.py`): soils as clods (dirt, pebbles, grub nest, packed earth), grass tufts, sand ripples, root fibres, bedded clay, rock as facets (stone, basalt columns, bedrock blocks, quartzite with glints), ores and finds as lumps in a host rock (copper, iron, coal, amber, fossil bones), glow crystal shards, obsidian, magma rock with glowing cracks, pitted meteorite, mycelium threads, ribbed giant fungus with spores, ruin masonry with moss, relic tablets with gold inlay, ash. The import set every material's base / shadow / highlight colour from its tile. Checked lit in a capture per biome (`art_biome_*.png`: topsoil, clay, stone, crystal, fungal, magma, ruins).
  - *Found:* half of the topsoil is the *pebbles* material, so its tile got few, small, warm stones (bold ones read as a dotted pattern).
- [x] **4. Colony** (`tools/art/colony.py`, `Art/Colony/`): worker, **soldier** (big square head, heavy gold mandibles, dark crimson), **miner** (front-heavy, long pale digging mandibles, dusty grey), **hauler** (big gaster carried high, golden tan), **nurse** (small, rounded, cream), **builder** (humped thorax, flat shovel jaw, olive), **scout** (slim, three rows of long legs, two long antennae, orange), **engineer** (cyan goggles and antenna tip, steel blue); every walk strip has 4 frames with an antenna twitch. Queen, **princess** (gold, wings folded / up / swept back), egg, pile and carried lump (greyscale, still tinted per item), golden beetle, golden aphid. Caste colours are ramps in the palette (`caste_*`) and match the colours in the castes panel. Sizes unchanged (the code's contracts). Checked in the colony capture.
  - Not redrawn: the 3 × 3 marker diamond (nothing to gain at that size). Stored items and belt items are in the building atlas: step 5.
- [x] **5. Buildings** (`tools/art/buildings.py`, `Art/Buildings/`, 27 PNGs): silk belt and root lift, iron belt and iron lift (4 frames each), splitter, filter, unloader, chest (closed / open with goods), smelter, crusher (idle / grinding with dust), fungus garden (idle / sprouting with spores), aphid ranch (grazing / honeydew drops), archive (idle / candle lit), nursery, pheromone emitter, princess chamber (idle / royal jelly glowing), resin gate, iron gate, resin trap (armed / sprung), guard post, earth wall, pump (2 impeller frames), water wheel (4 frames), the generic machine crate, and the tinted lumps (belt item, stored item, dot). Frame counts and sizes are the code's, unchanged: nursery cradles, princess beds, chest and emitter dots sit where the game draws eggs, princesses and item dots. Blueprints and ghosts dither from the same sprites. Checked in the factory captures.
- [x] **6. Enemies, creatures and the surface** (`tools/art/creatures.py`; `Art/Enemies/`, `Art/Surface/`): termite (pale body, amber head, dark jaws), centipede (banded copper segments, rippling legs), cave spider, fungal mite, fire beetle (glowing seams), ruin guardian (mossy stone, crystal eye), wolf spider (striped, yellow eyes), two walk frames each; fern, bush with berries and flower (two sway frames), the bird (wings up / level, dive) and the ranch aphid. Sizes unchanged. Checked in the surface and biome captures; no size warnings.
  - Left generated: the bird's dithered shadow, raindrops, dig debris and number pops (single pixels and digits: nothing a PNG would improve).
- [x] **7. UI** (`tools/art/ui.py`, `Art/UI/`): the skin (panel frame, button bevels, inset) on every panel and dialog, on event cards, toasts and alerts (bevel over their state colours) and on the scrollbars; dialog and panel titles with a rule under them. **74 icons**: every item, the fixed resource entries (food, ants, eggs, storage, knowledge, princesses, memory, sun / moon for the clock), every building in the build palette (one-cell buildings use their sprite, larger ones a 10 × 10 miniature), the toolbar buttons, the four abilities, a caste icon per caste (its first walk frame) in the castes panel, item icons in the configure panel (storage / chest filters, unloader and filter item) and in the stock view. The title screen has a crest (two soldiers and a crown) over the logo. Captures: `art_ui_*.png` (build palette + castes, stock view, research, menu, achievements, settings, genetics, title).
  - Buttons with an icon keep their text in a label next to it (`HudController.AddButtonIcon` / `SetButtonText`): a UI Toolkit button with children is no longer sized by its own text.
  - *Not done:* icons inside the **hover panel** (it is one text label; the hover code would have to say what it describes: left for the tooltip work in 10.7). The scent palette, emitter scents, machine recipes and world modifiers keep their colour swatches (the colour is the information there). The achievements dialog keeps its small squares.
  - *Antarium → Steam → Export Achievements and Icons* re-run: the 68 PNGs in `steam/achievement_icons/` now use the new sprites. **(you)** upload the changed icons to Steamworks.
- [x] **8. Art for planned content** (`tools/art/planned.py`; 36 PNGs in the library, used by nothing yet; sizes, frames and names in `ART_STYLE.md` §10 *Not used yet*): **10.4** glow ant (lantern gaster) with its dithered light halo, fire ant, `frame_queen`, six evolution icons (brood, health, regeneration, egg slots, castes, ability), six priority icons (dig, haul, build, supply, tend, nurse), four trait icons (strong, swift, lucky, lazy), level pips (empty / full); **10.5** crystal resonator (idle / resonating), `frame_codex`, tablet and lore fragment icons (found / locked), the *Ancient Engine* in 4 restoration stages, the rival ant raider, the mole cricket (walk and dig frames); **10.6** the *Queen's Throne*, `frame_slot`, the respec icon. Each still needs its slot in code when its feature is built (an `ArtSlot` with a generated fallback, or `SetIcon` / a USS rule); the file names assume the ids used here (`glow`, `fire`, `tablet`, `crystal_resonator`, `ancient_engine`, `queens_throne`): rename the PNG if an id turns out different.
- [ ] **9. Finish**: captures of every biome, the colony, buildings and the UI (before / after); all tests pass; docs; final commit.


**10.4 Colony depth** (open: queen evolution currency. Use the 10.7a art for glow / fire ants, the Queen panel, priority and trait icons, and follow `docs/ART_STYLE.md`; the same holds for 10.5 and 10.6)
- [ ] **Glow Ant** caste (research, deep): carries light. Drawn as small dynamic lights near the camera (pooled `Light2D`, GPU light splat for the rest), so dark depths can be lit by ants; finally adds **ShadowCaster2D** near the camera for these lights (GDD 3.2).
- [ ] **Fire Ant** caste: immune to lava heat and fire, takes no burn damage; works the magma zone. New hazard **heat**: ants near lava / in the magma zone lose health slowly without it.
- [ ] **Queen evolution tree** (per colony, unlike genetics): spend rare finds (honeydew, relics, star fragments, royal jelly) on bigger broods, queen health / regeneration, extra egg slots, castes unlocked earlier, a new ability. Opens from the **Queen panel** (click the queen / Q key): stats, upkeep, eggs, caste mix shortcut, evolutions.
- [ ] **Priority panel**: per job (dig, haul, build, supply, tend, nurse) a priority 1–5 that multiplies the global weights; caste weights stay.
- [ ] **Ant XP and levels** per ant in its main job (faster at it, small cap); optional **traits** at hatch (*Strong* carry +1, *Swift*, *Lucky* drops, *Lazy*), shown in the hover panel. `ANTS` 4.
- [ ] **Lane jitter**: small per-ant offsets so a column of ants on one route reads as a stream, not a single sprite.

**10.5 Deep endgame: crystals and ruins** (no open questions)
- [ ] **Crystal resonance**: a *Crystal Resonator* next to glow crystals (more crystals in its conductive cluster = stronger) produces knowledge and speeds nearby machines; mining the cluster weakens it.
- [ ] **Ruins story**: relics studied in the Archive unlock **lore fragments** (a Codex panel with a short story of the lost colony, ~12 entries); some ruin rooms hold a *tablet* (unique relic).
- [ ] **Endgame tech**: research tier 7 behind the lore (e.g. *Ancient Engine*: a big ruin machine the colony restores step by step with relics, iron, crystals; finishing it is the colony's goal and gives a large Genetic Memory bonus at the next flight).
- [ ] **Rival ant colony** (deep or surface raid variant that steals items from storage instead of food) and an **enemy that digs** (mole cricket: tunnels toward the nest through soft soil, so walls / gates alone don't stop it).
- [ ] Contracts, achievements, ruin-tier music cue hook.

**10.6 Prestige and saves** (open: respec cost)
- [ ] **Respec** genetic memory (refund at a cost, or free once per flight).
- [ ] **New bloodline**: start a fresh legacy from the menu (with confirmation), keeping achievements.
- [ ] **Prestige-only mechanics**: traits that unlock a caste from the start, biome variants (world modifiers that swap a biome's palette / creature), a building only known through flights (e.g. *Queen's Throne*).
- [ ] **Save slots**: 3 slots + backups, slot picker on the title screen (generation, depth, play time from the header), Steam Cloud keeps working (`Saves/*`).

**10.7 Presentation** (no open questions; the art override pipeline, caste silhouettes, better sprites, terrain textures, UI skin and item icons moved to **10.7a**, built before 10.4)
- [ ] Wire in any 10.7a art for planned content that 10.4–10.6 didn't use yet; new sprites for anything added since, following `docs/ART_STYLE.md`.
- [ ] **Overlays**: light overlay and a full ore-scan overlay (revealed ore by type, toggle).
- [ ] **Tooltips** on every interactive UI element (palette entries, research nodes, castes, resource entries, ability buttons), and hint texts that follow the key bindings (no fixed letters).
- [ ] Flight / discovery **stinger** slots wired (**(you)**: generate them from `docs/SUNO_PROMPTS.md`); sound cues for every new M10 feature.
- [ ] Final docs pass, HOW_TO_PLAY for everything above, version 0.10.0.

## Missing / not built yet
Things the current build is still missing, grouped by area. Many are planned for a specific milestone (in brackets).

**Food & economy**
- Food sources: seeds on the surface (by day), grubs from grub nests, the Fungus Garden (seeds or leaves → fungus), giant fungus. Honeydew from aphid ranches feeds the queen.
- Food is a colony-wide stock with upkeep; no per-ant hunger/rest needs.
- Ores are crushed and smelted into copper and iron; iron builds iron belts, lifts and gates. Magma heat replaces coal in the Smelter; water wheels speed up machines; irrigation raises farm output.

**Colony & ants**
- No late castes yet: glow ants that carry light, heat-resistant ants for the magma zone.
- No queen evolution tree (castes come from research).
- Priorities are only Dig / Gather scents and per-caste job weights; no priority panel.
- No ant XP/levels or traits.
- Ants on the same route walk in single file and overlap.

**World**
- All 7 planned biomes exist; crystal resonance and the ruins' story / endgame tech are not in yet.
- Fluids: pumps, water wheels, irrigation and magma heat exist; no fluid pressure (decided), no lava pumping.
- Material flag *Conductive* has no behaviour yet (crystal resonance, 10.5); *Flammable* = fire, *Unstable* = sand and ash collapse, *Hot* = magma heat.
- Weather is rain and drought only (no storms, no seasons).

**Gameplay layers**
- Research: no queen evolution tree. Auto-designating veins exists as the genetic trait *Vein Instinct*.
- One save slot (synced by Steam Cloud).
- Threats: termite raids, birds by day, wolf spiders at night and one cave creature per deep biome; no rival ant colonies, no enemies that dig (10.5).
- Pheromones don't diffuse or mix; no "Haul to" scent (storage zones do that).
- Prestige: no respec, no "new bloodline" reset, no prestige-only mechanics beyond Vein Instinct yet (later flights could unlock castes, biome variants, buildings).

**Presentation**
- World art is PNG now (colony, buildings, enemies, surface, terrain; 10.7a). Still generated in code: the marker diamond, the bird's shadow, rain, dig debris and number pops.
- Sound effects and ambience are synthesized placeholders; music is recorded (10 tracks), no flight / discovery stingers yet.
- UI: skin and icons done in 10.7a; the hover panel has no icons, and the achievements dialog shows plain squares instead of the achievement icons.
- No queen panel. Tooltips are limited to the hover info panel, the toolbar and the research hover details.
- Overlays: flow, pheromones (always drawn faintly) and the Seismic Sense / scout glow; no light or full ore-scan overlay.
- One dynamic light (the possessed ant's); ShadowCaster2D still not used (deferred until glow ants).
- Store page is in review, not public yet; no Steam Deck pass or public build yet (Milestone 9).
