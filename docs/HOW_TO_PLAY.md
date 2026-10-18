# How to play Antarium 2

This covers everything that exists in the current build (Milestones 1–2). Numbers quoted here are the defaults in `Assets/_Antarium/Content/GameConfig.asset`, so they change if that asset is tuned.

To start, open `Assets/_Antarium/Scenes/Main.unity` and press Play. If the scene is missing, run **Antarium → Setup → Create Main Scene** in the editor menu.

---

## Controls

### Camera

| Action | Keyboard / mouse | Gamepad |
|---|---|---|
| Pan | **W A S D** or **arrow keys** | Left stick |
| Drag the view | Hold **middle mouse button** and move | — |
| Zoom in / out (toward the cursor) | **Mouse wheel** | — |

Zoom works in whole steps (×1 to ×8) so the pixel art stays crisp. ×1 is the overview.

### Tools

| Key | Tool | Left mouse (hold) | Right mouse (hold) |
|---|---|---|---|
| **1** | **Dig** | Dig at the cursor yourself | — |
| **2** | **Mine** | Paint mining orders for workers | Erase mining orders |
| **3** | **Storage** | Paint a storage zone | Erase storage zone |

You can also click the tool buttons at the bottom of the screen. On a gamepad, the **right trigger** acts as the left mouse button and the **left trigger** as the right mouse button. Clicks that start on the HUD never reach the world.

### Debug (development builds and the editor)

| Key | Action |
|---|---|
| **F3** | Show/hide the debug readout (FPS, zoom, seed, the cell under the cursor, light, tool, combo, ant/pile/order counts) |
| **F6** | Spawn 50 workers in the royal chamber |

---

## The screen

- **Resource bar (top):**
  - **Food** current / capacity, **Ants**, **Eggs**.
  - One entry per resource you own, e.g. *Copper Ore 4/30*. Entries appear once you have that item, and turn orange when that item is at capacity.
- **Alert (under the bar):** shows in red while the colony is starving.
- **Toolbar (bottom):** Dig / Mine / Storage. The active tool is outlined in gold.
- **In the world:**
  - Orange **diagonal stripes** are mining orders (also visible over unexplored darkness).
  - A **teal checker** marks storage zones.
  - **Small coloured blobs** are item piles waiting to be hauled.
  - **White ovals** next to the queen are eggs.

---

## How the colony works

### Your start
You begin with a **queen** in a small chamber under the grass, connected to the surface by a shaft. She has **6 workers** and **40 food**. Everything outside the area around the nest is dark: unexplored.

### Digging yourself (Dig tool)
- Hold the left mouse button to dig at the cursor (about 10 hits per second, in a small round brush).
- Keep digging without pausing and a **combo** builds up, up to ×2.5 dig power. It resets after a short pause (about half a second).
- Harder materials take more hits: dirt is soft, stone is five times harder, and **bedrock** (the world border) can't be dug.
- You can only dig what you can see. Digging reveals the area around the hole.
- If you break into a **hidden cave**, the whole cave is revealed at once.
- Some materials drop an **item** when dug out (see *Resources*). Items fall straight down to the floor.

### Giving orders (Mine tool)
- Paint over solid ground to **order it dug out**. Workers come and mine it for you.
- Orders can reach into the darkness, so you can plan a tunnel ahead. Workers dig from the edge of what's already open and reveal the rest as they go.
- Right-click to erase orders.
- Workers dig the cells they can reach. A few workers share each spot at the digging front; the rest haul or wait, so a narrow tunnel doesn't jam.

### Hauling and storage (automatic)
- Workers **automatically pick up item piles** and carry them, one at a time, to the nearest **drop-off**:
  - the **royal chamber** (the area within 4 cells of the queen), or
  - any **storage zone** you painted (Storage tool). It must be painted on open, visible, underground space.
- Everything delivered goes into one shared colony stock, which is what the resource bar shows.
- **Capacity:**
  - The royal chamber holds **30 of each item type** and **100 food**.
  - **Each storage cell adds +4 per item type and +6 food.**
  - Workers only pick up what still fits. When an item type is full, its piles stay where they are until you add storage.

### Food and the queen
- **Food sources right now:**
  - **Seeds** appear on the grass near the nest every few seconds (up to 12 lying around). Workers walk out and bring them in: 2 food each.
  - **Grub nests** are pale-speckled soil in the topsoil. Dig them out and they drop **grubs**: 5 food each.
- **Eating:** every worker eats 0.25 food per minute and the queen 1.5 per minute.
- **Eggs:** when there is enough food, the queen lays an **egg** every 6 seconds (6 food each, at most 6 eggs at a time). After 20 seconds an egg hatches into a new worker.
- **Starving:** if food runs out, the colony is starving. Workers move and dig at half speed and the queen stops laying until food comes in again. Nobody dies.

### What workers do on their own
Each idle worker picks the best job it can reach:
1. **Mine** an order, if there's room at the digging front.
2. **Haul** an item pile to a drop-off, if it still fits.
3. Otherwise it **wanders** near the queen.

Workers crawl on floors, walls, ceilings and the back wall of dug tunnels, so they take direct routes through chambers. Outside on the surface they walk along the ground. If the ground under one is dug away in the open sky, it falls.

### Light and darkness
- Sunlight reaches into shafts and tunnels near the surface and fades deeper down.
- **Glow crystals** in the stone layer light their surroundings.
- Explored but unlit areas stay dim. Unexplored areas are black.
- Ants, the queen and items always stay visible enough to follow.

---

## A first session

1. Press **3** and paint a small **storage zone** in the starting chamber. That raises your capacity.
2. Press **2** and paint a **tunnel** sideways or downward from the chamber. Watch the workers walk over and dig it out. Copper ore, grubs and other drops get carried back automatically.
3. Keep an eye on **Food**. Workers forage seeds on their own. If food runs low, dig toward grub nests in the topsoil (pale speckles), or dig them yourself with **1**.
4. As food comes in, the queen lays eggs, and every egg is a new worker. More workers means faster mining and hauling.
5. Dig down to find the clay layer (iron) and the stone layer (coal, amber, fossils and glowing crystals).

---

## Not in the game yet

Conveyors, buildings and processing, research, other castes, pheromones, enemies, water, day/night, saving and loading. See `docs/TODO.md` for the full list of what's missing.
