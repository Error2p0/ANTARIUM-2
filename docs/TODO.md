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
- **Hands-on feel pass pending**, now including M8: listening to every sound and the music (only checked as numbers and exported WAVs, never heard), clicking through the title screen and settings, rebinding keys, the feel of number pops and puffs, the UI at 1440p / 4K scale (mouse dig, painting orders/zones, pan/zoom, laying belts by dragging, placing machines; M4: clicking Claim / research nodes / ability buttons, aiming Seismic Sense, catching the beetle with the mouse, Esc / F5 / F9; M5: painting walls and gates, raid / creature cards and Go; M6: painting scents, the castes panel buttons, steering a possessed ant with WASD and biting with the mouse, aiming the Pheromone Burst; M7: clicking modifiers, the double-click Take flight, buying traits in the genetic tree). Automated checks drive the sim, tools and panels directly; tuning lives in `GameConfig.asset` and the content assets.
- Sounds and music are synthesized placeholders until recorded files are assigned (the pipeline is ready, no files yet). The bank's synth patches are first-pass. Biome music changes only between tracks (moving the camera never cuts a track); there is no music for combat with cave creatures, only raids. Positional sounds ignore walls (a sound in view is heard even behind rock).
- Fixed key letters still appear in some messages and hints (e.g. *castes panel (K)*, *flight screen (N)*); buttons follow the bindings, messages don't yet. The gamepad layout can't be rebound.
- Key names follow the keyboard layout (Czech: number row + ě š č ř); a bound key with no printable label shows its Input System name.
- UI scale is whole numbers only (pixel fonts); at 1440p *Auto* picks 2×, which is big; 1× is small. Window mode and resolution apply only in builds.
- Hover tips exist only for the toolbar buttons. The debug readout (dev builds) stays visible over the title screen.
- Achievements: kept locally (`profile.json`) until Steam; debug keys (F6–F12, dev builds only) can unlock them; *Erase all progress* can't reset Steam achievements. Most achievements count within one colony.
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
- One save slot (+ backup); no slot picker, no Steam cloud yet. The save folder (and `settings.json` / `profile.json`) depends on Player Settings → Company Name (still `DefaultCompany`): set it in Milestone 9 before the first public build, since changing it later orphans saves.
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

## Next: Milestone 9 — Steam release
Steamworks integration, store page and the first build on Steam (GDD §4). Many steps need your Steamworks account, money and identity; those are marked **(you)**. Requirements and image sizes below are the ones known at the time of writing: check the current Steamworks documentation when doing each step.

**Account and app (you)**
- [ ] Steamworks partner account (partner.steamgames.com): legal entity / name, identity verification, tax interview, bank details. Verification can take days, so start first.
- [ ] Pay the Steam Direct fee (USD 100 per app, recouped after USD 1,000 in revenue) and create the app: note the **App ID**.
- [ ] Decide the developer / publisher name. It also becomes Player Settings → **Company Name**, which sets the save folder (see Known limits).
- [ ] Price, release date (or "Coming soon"), languages (English), whether there is a demo / Next Fest.

**Steamworks integration (code)**
- [ ] Add Steamworks.NET (UPM git package) behind a scripting define (`ANTARIUM_STEAM`); `SteamPlatform : IPlatformServices` (init with `RestartAppIfNecessary`, run callbacks in `Tick`, achievements and stats via `SteamUserStats`, `StoreStats` in `Flush`, overlay callback → pause); fall back to `LocalPlatform` without Steam. `steam_appid.txt` for development only.
- [ ] Achievements and stats on Steamworks: the 32 API names from `Content/Achievements` and the stats `STAT_MAX_DEPTH`, `STAT_MAX_ANTS`, `STAT_FLIGHTS`; an editor tool that lists them (names, descriptions, hidden flags) for entering in the Steamworks UI; 64×64 achievement icons (unlocked + grey locked), generated from the pixel art.
- [ ] Copy local achievements to Steam on the first Steam launch.
- [ ] Steam Cloud: Auto-Cloud for the save folder (`LocalLow/<Company>/ANTARIUM 2/Saves`, `colony.sav` + `.bak`, and `settings.json` / `profile.json`), with quota; test the conflict dialog with two machines.
- [ ] Rich presence (*Generation 3, 420 cells deep*) and the overlay pausing the game.
- [ ] Steam Deck / controller pass: stick aiming for the cursor, gamepad glyphs, readable text at 1280×800, Steam Input default layout; aim for *Playable* or *Verified*.

**Release build (code)**
- [ ] *Antarium → Build → Windows Release* (x86_64, IL2CPP if it holds up, else Mono; non-development, no debug keys), version from a single place, build into `Builds/` (git-ignored).
- [ ] Icon, splash (Unity Personal shows the Unity splash), product name, Company Name, default window mode.
- [ ] Crash-safe logging location documented; check the first-run experience on a clean Windows account.
- [ ] SteamPipe: `app_build_<appid>.vdf` + `depot_build_<depotid>.vdf` and an `upload` script using `steamcmd` with a builder account (you add the account and Steam Guard); `beta` branch first, then `default`.

**Store page**
- [ ] Capsule art (from in-game captures and the logo): header 920×430, small 462×174, main 1232×706, vertical 748×896; library capsule 600×900, library hero 3840×1240, library logo 1280×720 (transparent), page background (optional). Rendered at integer scale from the pixel art.
- [ ] At least 5 screenshots at 1920×1080 (colony, automation, a deep biome, a raid, the genetic tree).
- [ ] Trailer (30–90 s): capture sequences with a scripted camera; you edit / approve it.
- [ ] Short description (up to 300 characters), long description with feature list and GIFs, tags (Colony Sim, Automation, Incremental, Pixel Graphics, Base Building, Idler, Strategy, Simulation, 2D, Singleplayer), system requirements (Windows 10 64-bit, a DX11 GPU, 4 GB RAM, ~500 MB disk; confirm after measuring a build), supported languages.
- [ ] Content survey / age rating questionnaire; privacy policy (only needed if anything is collected: nothing is); EULA (Steam's default is fine).
- [ ] **(you)** Submit the store page for review (several business days). The *Coming Soon* page must be public for at least 2 weeks before release; wishlists start then.

**First upload and release**
- [ ] Upload the first build to the `beta` branch, install it through Steam on a clean machine, test achievements, cloud saves, overlay and offline progress.
- [ ] **(you)** Submit the build for review; set the release date; release (or keep it in Coming Soon / playtest).

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
- Research: no conveyor tiers; no queen evolution tree. Auto-designating veins exists as the genetic trait *Vein Instinct*.
- One save slot; no Steam cloud saves yet (Milestone 9).
- Threats: termite raids and one cave creature per deep biome; no birds, surface spiders or rival ant colonies, no enemies that dig.
- Pheromones don't diffuse or mix; no "Haul to" scent (storage zones do that).
- Prestige: no respec, no "new bloodline" reset, no prestige-only mechanics beyond Vein Instinct yet (later flights could unlock castes, biome variants, buildings).

**Presentation**
- All art is placeholder generated in code (ant/queen/egg sprites, terrain textures).
- Sounds and music are synthesized placeholders; no recorded audio or composed music.
- UI skin is generated bevels over flat colours; no hand-drawn UI art or icons.
- No queen panel. Tooltips are limited to the hover info panel, the toolbar and the research hover details.
- Overlays: flow, pheromones (always drawn faintly) and the Seismic Sense / scout glow; no light or full ore-scan overlay.
- One dynamic light (the possessed ant's); ShadowCaster2D still not used (deferred until glow ants).
- Caste sprites are palette swaps of the worker; nursery and emitter sprites are generated placeholders.
- No Steam integration, store page or release build yet (Milestone 9).
