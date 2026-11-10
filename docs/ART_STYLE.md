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
- Colony: ants are three clear segments with one-pixel gaps (waist, neck) in the leg tone, an eye and a pale
  mandible. The **gaster is one tone darker** than head and thorax, so the facing direction reads at a glance.
  Castes differ by **silhouette** first and colour second.

## 5. Sizes, frames and timing

| Piece | Size (px) | Frames | Timing | Name |
|---|---|---|---|---|
| Worker | 12 × 7 | 4 walk | 4 frames per walk phase; 2 frames alternate while working | `Colony/ant_worker` |
| Queen | 24 × 12 | 2 (rest, gaster swollen) | 0.7 breaths per second | `Colony/queen` |
| Smelter | 24 × 24 | 2 (idle, working) | by machine state | `Buildings/smelter` |
| Terrain tile | 32 × 32, tileable | 1 | | `Terrain/<material id>` |
| Icon | 10 × 10 | 1 | | `UI/Icons/<name>` |
| Panel frame | 12 × 12, slice 4 | | | `UI/Skin/frame` |
| Button bevels | 7 × 7, slice 2 | | | `UI/Skin/button`, `UI/Skin/button_down` |
| Inset | 4 × 4, slice 1 | | | `UI/Skin/inset` |

Buildings are always cells × 8 px. More rows are added to this table as each category is drawn (steps 3–8).

## 6. Terrain tiles

- One tileable **32 × 32** PNG per material, named by the material id. It repeats every 4 cells, so **keep it
  calm**: close tones (half steps of a ramp: see `tones()` in `terrain.py`), small soft shapes, and only two or
  three distinct features per tile. Anything bold shows up as a grid.
- The shader adds the outline and lit top edge, light bands, fluids, fire and fog, and draws the same tile
  flattened and darkened as the background wall. Do not draw edges or lighting into the tile.
- Soil is clods (dark gaps with a lit rim on their lower right); rock is flat facets with a few dark fractures
  (`clods()`, `rock()`); bedded materials use `strata()`; ores and finds are lumps stamped into a host rock
  (`embed()`), lit top left with a shadow pixel under them. A material that covers large areas (pebbles: half the
  topsoil) gets fewer and smaller features than a rare one (relic).
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
- An icon is looked up by name: `UI/Icons/item_<item id>` for items; `food`, `ants`, `eggs`, `storage`,
  `knowledge` for the fixed entries. Without an icon the old colour swatch is shown
  (`HudController.SetIcon`).

## 8. Adding a sprite

1. Draw it in `tools/art/` (an ASCII pixel map with a key from the ramps, or a painter) and add it to `PIECES`
   in `make_art.py` — or draw the PNG by hand with the palette — at the size and name the tables give.
2. `python tools/art/make_art.py <name>`, look at `Captures/art/pieces.png`.
3. In Unity: **Antarium → Art → Import Overrides**.
4. A new *slot* (a sprite the code does not know yet) also needs: its rect in the atlas class, a generated
   fallback there, and an `ArtSlot` in that class's `Slots` list (name → rects). `ArtTests` checks the list.
5. Check it in the running game: `CaptureMenu.Run("art", false)` writes review stills to `Captures/stills/art_*.png`.

## 9. What exists

Drawn in the new style: worker, queen, smelter, **all 26 terrain tiles**, the UI skin (frame, button
bevels, inset) and the icons *food, ants, eggs, storage, knowledge, copper ore, iron ore, coal, copper, iron,
crystal shard, amber*.

Not used yet: nothing (art for planned content comes in step 8 and is listed here then).
