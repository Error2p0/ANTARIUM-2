# Antarium 2 — Art style

How the game's pixel art is made, named and wired in. Follow it for every new sprite, tile and UI piece
(milestones 10.4–10.7 included). The style was approved on 2026-11-10 (10.7a step 1). Pieces not yet listed
under *What exists* are still generated in code.

## 1. How the art gets into the game

- The art is **PNG files** under `Assets/_Antarium/Art/`. A PNG's **name** is its path under that folder without
  the extension (`Colony/ant_worker`, `Terrain/dirt`, `UI/Icons/item_coal`).
- **Antarium → Art → Import Overrides** (run it after adding or renaming a PNG): sets the import settings
  (point filter, no compression, readable, no mip maps), lists every PNG in `Art/ArtLibrary.asset`
  (referenced by `GameConfig.art`), assigns `Terrain/<material id>.png` to that material's *texture override*
  and takes the material's base / shadow / highlight colours from the tile (average, darkest fifth, lightest
  fifth: the shader uses them for background walls and terrain edges).
- At run time a PNG **replaces the generated sprite** of the same slot; a missing PNG keeps the generated one
  (`ColonySprites`, `BuildingSprites`, `MaterialTextureBuilder` stay as the fallback). A PNG of the wrong size is
  reported in the console and skipped.
- The PNGs are written by **`python tools/art/make_art.py`** (Pillow + numpy) from pixel maps and painters in
  `tools/art/`, with the palette in `tools/art/palette.py`. `make_art.py worker` redraws only the pieces whose
  name contains "worker". Preview sheets go to `Captures/art/` (not committed).
- A PNG can also be **edited by hand** (Aseprite etc.). The next run of its piece would overwrite it, so remove
  that piece from `PIECES` in `make_art.py` once it is hand-edited.

## 2. Rules that bind the art

- **PPU 16, 1 cell = 8×8 px.** Point filtering, no compression, integer zoom. Never scale or rotate a sprite by
  anything but 90° steps and mirroring (the renderer does that).
- **Sizes are contracts** with the code (see the table). Change a size only together with the constant in
  `ColonySprites` / `BuildingSprites` and whatever depends on it.
- **Animated sprites are strips**: the frames side by side in one PNG, left to right, each frame the sprite's
  size (`ant_worker.png` is 4 × 12 = 48 px wide).
- Sprites **face right** and are mirrored for left. Creatures cling to floors, walls and ceilings: draw them
  standing on the bottom row.
- **Alpha is on or off** (sprites are alpha-tested): no soft edges, no partial transparency in world sprites.
- World sprites are lit as a whole by the light map and never drawn darker than `spriteMinLight` in dark
  tunnels. So **the body colours carry the silhouette**: no black outlines (they vanish on dark walls); the
  darkest tone used on a body is index 1–2 of its ramp.

## 3. Palette

All colours come from the ramps in `tools/art/palette.py` (dark → light). Shadows lean cool / red, highlights
lean warm / yellow. A sprite uses **one main ramp** (3–5 tones) plus small accents from another.
`make_art.py` writes the swatches to `Captures/art/palette.png`.

| Ramp | Used for | Tones (dark → light) |
|---|---|---|
| chitin | ants, the queen | `2a0d0c 4c1610 7a2414 a8381a d05a26 ee8a44 ffc078` |
| silk | silk, eggs, pale mandibles | `5a4a3a 8a7558 b8a27a dcc9a0 f2e6c4 fffaea` |
| gold | royal bands, amber, honeydew, rewards | `5a300c 8a5212 c07a1c eaa82e ffd45a fff2a8` |
| earth | topsoil, packed earth | `24150f 382116 4f3020 6b432b 875a38 a67848 c59a62` |
| clay | clay biome, kilns, bricks | `3e1a14 5e2c1e 84412a a85a36 c97a4a e4a06a` |
| sand | sand, seeds | `6a5434 8e7446 b39558 d2b672 e8d294 f8ecbc` |
| stone | stone strata, masonry, rocks | `1c1b24 2c2b36 41404c 595966 767682 96979f bcbdc2` |
| basalt | basalt, coal, obsidian base | `121014 1e1b20 2e2a30 403b42 565058 6e6870` |
| grass | grass, leaves, plants | `18321a 265022 3a742a 56a038 84c84c b8e676` |
| wood | roots, lifts, chests, shelves | `22140c 3a2414 563820 76502e 986c40 ba8e58` |
| fungus | fungal depths, mushrooms | `1e1024 32203a 4e3058 744a80 a070aa cca4d4 f0d8f4` |
| spore | fungal glow, gas | `0c2e30 145650 248a78 4cc8a4 8ef0d0 d4fff0` |
| crystal | crystal caverns, shards | `101a34 1a3260 2a5a9c 4494d4 7fd0f8 c4f2ff f2feff` |
| magma | magma zone, fire, embers | `240a08 4e160e 8c2a14 cc4e1c f4882c ffc45a fff2b0` |
| ruin | ancient ruins, relics | `2a2620 443e34 625a4c 827866 a49a84 c8c0a8` |
| moss | ruin moss | `1c2a1e 2e4430 486446 688a5e` |
| void | meteorite, star fragments, knowledge | `1a1426 2a2040 443466 6a5496 9a84c8 cdbcf4` |
| iron | iron buildings, ingots | `1e222a 343a46 525c6a 7a8696 a6b2c0 d2dae2 f4f7fa` |
| copper | copper ore and ingots | `4a2010 7a3a18 b05c24 d98440 f4b070 ffdcb0` |
| water | water, pumps | `12244e 1c3e7c 2c62b4 4a90dc 84c0f4 c8e8ff` |
| bone | fossils, chitin plates | `5e5444 8c8068 b8ac90 dcd2b8 f4eedc` |
| soot | kiln mouths, burnt things | `0c0808 1a100e 2e1c16 48302a` |
| red | banners, danger | `3a0c10 6e1618 a82420 dc4430 f8785a` |
| ui | UI frames and fills | `0e0806 1a100a 2a1b12 3e2a1c 58402a 7a5c3c a07e54 c8a878` |

## 4. Shading

- **Light comes from the top left.** Top and left faces take the lighter tones, the underside and the right the
  darker ones; one or two pixels of the lightest tone as a shine.
- **Flat tones, no gradients, no dithering inside sprites.** Dither only where the game already does (light
  bands, blueprints).
- **Selective edges**: a darker tone of the same ramp along the lower right, a lighter one along the upper left.
- **Glow** (fire, crystals, eyes) is the top of its ramp surrounded by the next tones down; the stones or
  walls right next to a fire take a warm tint.
- Enemies must not read as ants: other body plans (long and banded, round on arching legs, a domed shell, a
  stone biped), their own ramps, and one hot accent each (red or yellow eyes, glowing seams, a crystal eye).
- Colony: ants are three clear segments with one-pixel gaps (waist, neck) in the leg tone, an eye and a pale
  mandible. The **gaster is one tone darker** than head and thorax, so the facing direction reads at a glance.
  Castes differ by **silhouette** first and colour second: soldier = big square head and heavy mandibles;
  miner = front-heavy with long pale mandibles; hauler = big gaster carried high; nurse = small and round;
  builder = humped thorax, flat shovel jaw; scout = slim on long legs with two long antennae; engineer = cyan
  goggles and antenna tip. A new caste (10.4: glow ant, fire ant) gets its own `caste_<name>` ramp of 6 tones
  and one feature no other caste has.
- A walk strip is 4 frames: the legs alternate in a tripod gait, and the antennae move in frames 2–3.
- **Tinted sprites** (pile, carried lump, belt item, stored item, marker, dots) are greyscale: the renderer
  multiplies them with the item's colour. Keep them white-to-mid-grey.

## 5. Sizes, frames and timing

| Piece | Size (px) | Frames | Timing | Name |
|---|---|---|---|---|
| Worker, nurse, builder, scout, engineer | 12 × 7 | 4 walk | 4 frames per walk phase; frames 0–1 alternate while working | `Colony/ant_<caste>` |
| Miner, hauler | 13 × 7 | 4 walk | same | `Colony/ant_miner`, `Colony/ant_hauler` |
| Soldier | 14 × 8 | 4 walk | same | `Colony/ant_soldier` |
| Queen | 24 × 12 | 2 (rest, gaster swollen) | 0.7 breaths per second | `Colony/queen` |
| Princess | 14 × 10 | 3 (resting, wings up, wings back) | flight: frames 1–2 at 12 per second | `Colony/princess` |
| Egg | 4 × 5 | 1 | | `Colony/egg` |
| Pile / carried lump | 6 × 4 / 3 × 3 | 1 | greyscale, tinted with the item colour | `Colony/pile`, `Colony/carry` |
| Golden beetle | 10 × 6 | 2 walk | | `Colony/golden_beetle` |
| Golden aphid | 6 × 5 | 2 | 2.5 per second | `Colony/golden_aphid` |
| Marker | 3 × 3 | 1 | white, tinted | `Colony/marker` (generated) |
| Termite / centipede | 11 × 6 / 16 × 6 | 2 walk | 4 per walk phase | `Enemies/termite`, `Enemies/centipede` |
| Cave spider / wolf spider | 11 × 7 / 12 × 7 | 2 walk | | `Enemies/spider`, `Enemies/wolf_spider` |
| Fungal mite / fire beetle | 6 × 5 / 12 × 7 | 2 walk | | `Enemies/mite`, `Enemies/fire_beetle` |
| Ruin guardian | 14 × 12 | 2 walk | | `Enemies/guardian` |
| Fern / bush / flower | 11 × 12 / 13 × 9 / 7 × 16 | 2 sway (the top leans 1 px) | slow, per plant | `Surface/plant_fern`, `plant_bush`, `plant_flower` |
| Bird / dive | 14 × 6 / 8 × 6 | 2 flap / 1 | 6–8 per second | `Surface/bird`, `Surface/bird_dive` |
| Ranch aphid | 3 × 2 | 2 | | `Surface/aphid` |
| Bird shadow | 16 × 4 | 1 | dithered | `Surface/bird_shadow` (generated) |
| Belts, lifts | 8 × 8 | 4 | the pattern moves 1 px per frame (iron belt: 2 px); speed from the building | `Buildings/silk_belt`, `root_lift`, `iron_belt`, `iron_lift` |
| Splitter, filter, unloader | 8 × 8 | 1 | filter and unloader point right and are turned by the game | `Buildings/splitter`, `filter`, `unloader` |
| Chest | 8 × 8 | 2 (closed, open with goods) | | `Buildings/chest` |
| Smelter | 24 × 24 | 2 (idle, working) | by machine state | `Buildings/smelter` |
| Crusher | 16 × 16 | 2 (idle, working) | | `Buildings/crusher` |
| Fungus garden, aphid ranch | 32 × 16 | 2 (idle, working) | | `Buildings/fungus_garden`, `aphid_ranch` |
| Archive | 24 × 16 | 2 (idle, working) | | `Buildings/archive` |
| Nursery | 24 × 16 | 1 | cradles centred at x 4, 12, 20; eggs rest 2 px above the floor | `Buildings/nursery` |
| Princess chamber | 32 × 24 | 2 (idle, working) | beds at (8, 0), (24, 0), (16, 9) from the bottom left | `Buildings/princess_chamber` |
| Emitter, gates, guard post, wall | 8 × 8 | 1 | the emitter's scent dot is drawn in its socket | `Buildings/pheromone_emitter`, `resin_gate`, `iron_gate`, `guard_post`, `earth_wall` |
| Resin trap | 8 × 8 | 2 (armed, sprung) | | `Buildings/resin_trap` |
| Pump | 8 × 8 | 2 (impeller) | 8 per second while pumping; spout right, turned by the game | `Buildings/pump` |
| Water wheel | 16 × 16 | 4 | by water power | `Buildings/water_wheel` |
| Generic machine | 16 × 16 | 1 | | `Buildings/generic` |
| Belt item / stored item / dot | 3 × 3 / 3 × 2 / 4 × 4 | 1 | greyscale, tinted | `Buildings/belt_item`, `stored_item`, `dot` |
| Terrain tile | 64 × 64, tileable | 1 | | `Terrain/<material id>` |
| Icon | 10 × 10 | 1 | | `UI/Icons/<name>` |
| Panel frame | 12 × 12, slice 4 | | | `UI/Skin/frame` |
| Button bevels | 7 × 7, slice 2 | | | `UI/Skin/button`, `UI/Skin/button_down` |
| Inset | 4 × 4, slice 1 | | | `UI/Skin/inset` |

Buildings are always cells × 8 px and stand on their bottom row. A machine's **working frame** changes only
what works (fire, dust, sprouts, a candle), never the outline. Where the game draws something into a building
(eggs, princesses, the top item, a scent dot), the sprite leaves a dark socket or an open bowl there.

## 6. Terrain tiles

- One tileable **64 × 64** PNG per material, named by the material id (`WorldConstants.MaterialTextureSize`). It
  repeats every 8 cells.
- **Lively, but flat-toned**: five tones of a ramp (wide steps: `tones(ramp, mid, 0.7)`), ground that runs in
  lighter and darker patches, warmer and cooler patches (`tint_patches`), and shapes of mixed sizes. Soil is
  clods of varying coarseness with earth lumps (`clods`, `boulders(soft=True)`); rock is facets of mixed tones
  with boulders set in and a few dark fractures (`rock`, `boulders`); bedded materials use `strata`.
- **Ore is big**: an ore body is a few lumps of 5–7 px with smaller ones between (`veins`), lit top left with a
  shadow pixel under each. It has to read from far out.
- A feature must still not be so bold and regular that the tile shows as a grid (rings, rows of equal dots). A
  material that covers large areas (pebbles: half the topsoil) gets fewer and smaller features than a rare one.
- The painters were tuned on a 32 px tile; `SCALE` / `AREA` in `terrain.py` keep features at their pixel size
  (counts passed to `scatter`, `voronoi`, `strata`, `value_noise` are *per 32 px tile*).
- The shader adds the outline and lit top edge, light bands, fluids, fire and fog, draws the same tile flattened
  and darkened as the background wall, and lays **world-space patches** over everything (`PatchShade` in
  `TerrainCommon.hlsl`: five flat brightness steps and a warm / cool lean; strength in `RenderConfig.patchContrast`
  and `patchTint`). Those never repeat, which is what hides the tile grid. Do not draw edges or lighting into
  the tile.
- Glowing materials (crystal, magma rock, giant fungus, meteorite) are drawn bright; the game adds the glow
  (`selfGlow`, emission) on top.

## 7. UI

- One art pixel is **2 screen pixels** (times the UI scale). Icons are 10 × 10, shown at 20 × 20 px.
- 9-slices use `-unity-slice-scale: 4px` (measured: that draws two screen pixels per texel; `2px` draws one).
- **Panels** (`.panel`, `.resources`, `.palette`, `.info`, `.castes`) use `UI/Skin/frame`: the fill is part of
  the image, the element's own background colour and border are off.
- **Buttons and cards** keep their CSS background and border colours (they carry the state: active, done,
  danger…). The skin only lays a bevel over them, inside the border. The UI blends in **linear** colour space:
  white overlays need a tiny alpha (about 4%), black ones a large one (40–65%).
- Fonts stay: VT323 (text), Silkscreen (titles).
- An icon is looked up by name under `UI/Icons/` (`HudController.SetIcon`); without one the old colour swatch
  is shown, so new content works before its icon exists:
  - `item_<item id>`: resource bar, stock view, configure panel;
  - `building_<building id>`: build palette (a one-cell building's icon is its sprite on a 10 × 10 canvas,
    made by `ui.building_icon`; larger buildings get a hand-drawn miniature);
  - `caste_<caste>` (14 × 8, the first walk frame, shown at 28 × 16): castes panel;
  - `ability_<ability id>`: ability bar;
  - `tool_dig`, `tool_mine`, `tool_build`, `tool_scent`, `tool_possess`, `tool_castes`, `tool_research`,
    `tool_flow`, `tool_flight`: toolbar (Storage uses `storage`, Genes uses `memory`);
  - `food`, `ants`, `eggs`, `storage`, `knowledge`, `princesses`, `memory`, `sun`, `moon`: fixed entries.
- A button with an icon holds its text in a label beside the icon: add the icon with
  `HudController.AddButtonIcon` and set the text with `HudController.SetButtonText`.
- Cards that carry a state colour (events, toasts, alerts, research nodes, contracts) keep it and only get the
  bevel. `UI/title_emblem` (44 × 12, shown at 4 px per art pixel) is the crest on the title screen.

## 8. Adding a sprite

1. Draw it in `tools/art/` (an ASCII pixel map with a key from the ramps, or a painter) and add it to `PIECES`
   in `make_art.py` — or draw the PNG by hand with the palette — at the size and name the tables give.
2. `python tools/art/make_art.py <name>`, look at `Captures/art/pieces.png`.
3. In Unity: **Antarium → Art → Import Overrides**.
4. A new *slot* (a sprite the code does not know yet) also needs: its rect in the atlas class, a generated
   fallback there, and an `ArtSlot` in that class's `Slots` list (name → rects). `ArtTests` checks the list.
5. Check it in the running game: `CaptureMenu.Run("art", false)` writes review stills to `Captures/stills/art_*.png`.

## 9. What exists

198 PNGs (195 in use since 10.5; the three 10.6 pieces wait). Drawn in the new style: **the whole colony** (10 castes with the glow ant's halo, queen, princess, egg,
item lumps, golden beetle and aphid), **every building** (29 PNGs, with the Crystal Resonator and the Ancient Engine), **the 9 enemies**
(with the rival ant and the mole cricket's walk and dig frames), **the surface** (plants, bird, aphid), **all 27
terrain tiles** (with the ancient tablet), the UI skin (frame, button bevels, inset; `frame_queen`, `frame_codex`), **the icons** (items, buildings, castes, tools,
abilities, fixed entries, evolutions, priorities, traits, lore fragments) and the title crest. The building atlas is 128 × 128 since 10.5
(the engine's four 32 × 24 frames fill its top rows).

## 10. Not used yet

Art drawn ahead for milestones 10.4–10.6 (`tools/art/planned.py`). The PNGs are in the library. **The 10.4 rows are in
use since 10.4** (the glow and fire ants and the halo in the colony atlas, which grew to 128 × 128 for them; `frame_queen`
on the Queen panel; the evolution, priority, trait and pip icons; 10.4 also added `caste_glow`, `caste_fire` and
`ability_quickening` to `planned.py`). **The 10.5 rows are in use since 10.5** (resonator and engine in the building atlas, rival
ant and mole cricket in the colony atlas with `ColonySprites.MoleDigRect` for the dig frames, `frame_codex` and the lore / tablet icons
on the Codex; 10.5 also added the palette icons `building_crystal_resonator` and `building_ancient_engine` to `planned.py` and the
`tablet` tile to `terrain.py`). The 10.6 rows are not used yet. When the feature is built: give a world sprite a rect, a generated fallback and an `ArtSlot` in its
atlas class (§8 step 4); show an icon with `HudController.SetIcon`; use a frame in `Hud.uss` like
`UI/Skin/frame`. If the content gets another id than assumed here, rename the PNG.

| For | PNG | Size | Frames |
|---|---|---|---|
| 10.4 Glow Ant | `Colony/ant_glow` | 12 × 7 | 4 walk (a caste: add it to `ColonySprites` like the specialists) |
| 10.4 its light | `Colony/ant_glow_halo` | 13 × 13 | 1, dithered; draw it unlit, centred on the gaster (x 1–2 of the sprite) |
| 10.4 Fire Ant | `Colony/ant_fire` | 12 × 7 | 4 walk |
| 10.4 Queen panel | `UI/Skin/frame_queen` | 12 × 12, slice 4 | gold band |
| 10.4 Evolutions | `UI/Icons/evolution_brood`, `_health`, `_regeneration`, `_egg_slots`, `_castes`, `_ability` | 10 × 10 | |
| 10.4 Priority panel | `UI/Icons/priority_dig`, `_haul`, `_build`, `_supply`, `_tend`, `_nurse` | 10 × 10 | |
| 10.4 Traits | `UI/Icons/trait_strong`, `_swift`, `_lucky`, `_lazy` | 10 × 10 | |
| 10.4 Levels | `UI/Icons/level_pips` | 5 × 5 | 2 (empty, full): a strip, show half of it per pip |
| 10.5 Crystal Resonator | `Buildings/crystal_resonator` | 16 × 16 | 2 (idle, resonating) |
| 10.5 Ancient Engine | `Buildings/ancient_engine` | 32 × 24 | 4 stages (ruin, rebuilt with gear, crystals set, running) |
| 10.5 Rival ant | `Enemies/rival_ant` | 12 × 7 | 2 walk (an `EnemyArt`) |
| 10.5 Mole cricket | `Enemies/mole_cricket`, `Enemies/mole_cricket_dig` | 16 × 8 | 2 walk, 2 dig |
| 10.5 Codex | `UI/Skin/frame_codex`; `UI/Icons/item_tablet`, `lore_fragment`, `lore_fragment_locked` | 12 × 12 slice 4; 10 × 10 | |
| 10.6 Queen's Throne | `Buildings/queens_throne` | 32 × 16 | 1 (the queen sprite rests on the dais, between the pillars) |
| 10.6 Save slots | `UI/Skin/frame_slot` | 12 × 12, slice 4 | iron band |
| 10.6 Respec | `UI/Icons/respec` | 10 × 10 | |

Ramps drawn for them: `caste_glow`, `caste_fire`, `rival` (in `palette.py`).
