# How to play Antarium 2

This covers everything that exists in the current build (Milestones 1–3). Numbers quoted here are the defaults in `Assets/_Antarium/Content/GameConfig.asset` and the building / recipe assets in `Assets/_Antarium/Content/Buildings` and `Recipes`, so they change if those assets are tuned.

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
| **4** | **Build** | Place the selected building (drag for belts and lifts) | Remove buildings (hold and drag) |

You can also click the tool buttons at the bottom of the screen. On a gamepad, the **right trigger** acts as the left mouse button and the **left trigger** as the right mouse button. Clicks that start on the HUD never reach the world.

### Build tool

| Key / mouse | Action |
|---|---|
| Click a building in the **palette** (above the toolbar) | Select it (also switches to the Build tool) |
| **Q** / **E** | Previous / next building |
| **R** | Rotate: belts flip left/right, lifts flip up/down, filters turn a quarter clockwise |
| **Left drag** with Silk Belt or Root Lift | Lay a line. It stays on the row (belt) or column (lift) where you started, and **flows in the direction you drag**. Dragging over an existing belt or lift turns it to the new direction. |
| **Left click** with any other building | Place it (machines: the cursor marks the bottom middle) |
| **Left click on a placed filter** with the Filter selected | Change the filter's item (cycles through all items) |
| **Right mouse** (hold) | Remove whatever building is under the cursor |

A preview follows the cursor: **green** = can be built there, **red** = can't. The info panel says why.

### View

| Key | Action |
|---|---|
| **O** (or the **O Flow** button) | Toggle the flow overlay (see *The screen*) |

### Debug (development builds and the editor)

| Key | Action |
|---|---|
| **F3** | Show/hide the debug readout (FPS, zoom, seed, the cell under the cursor, light, tool, combo, ant/pile/order counts) |
| **F6** | Spawn 50 workers in the royal chamber |
| **F7** | Finish every blueprint immediately |
| **F8** | Add up to 20 of every resource (as far as storage has room) and 50 food |

---

## The screen

- **Resource bar (top):**
  - **Food** current / capacity, with the **net food rate** per minute (food delivered minus what the colony eats; red when negative). **Ants**, **Eggs**.
  - **Storage** used / total: how many items (of any type) the royal chamber and your storage cells hold. Turns orange when full.
  - One entry per resource you own, e.g. *Copper Ore 4 +12/min*. Entries appear once you have that item. The green number is how many arrived in the stock per minute recently.
- **Alert (under the bar):** shows in red while the colony is starving.
- **Toolbar (bottom):** Dig / Mine / Storage / Build, and **O Flow** for the overlay. The active tool is outlined in gold; the overlay button turns green while it is on.
- **Build palette** (above the toolbar, Build tool only): every building with its cost. The cost turns red while you can't afford it.
- **Info panel** (above the palette): details of the building under the cursor (flow per minute, what it carries, why it is jammed, a machine's inputs, progress and recipes), what a storage cell or the royal chamber holds, or, in the Build tool, the selected building's cost, what it does and why it can't go where the cursor is.
- **Messages** (above the info panel): short notes when a placement is refused, e.g. *Not enough Copper Ore (3/12)*.
- **In the world:**
  - Orange **diagonal stripes** are mining orders (also visible over unexplored darkness).
  - A **teal checker** marks storage zones.
  - **Stacks of small bricks** on the back wall of storage cells, and on the floor of the royal chamber beside the queen, are your stored items: one brick per item, coloured like the item.
  - **Small coloured blobs** are item piles waiting to be hauled.
  - **White ovals** next to the queen are eggs.
  - **Cream silk bands** along a tunnel floor are belts; **brown root rails** in a shaft are lifts. Small coloured dots riding them are items.
  - A **knot** is a splitter; a **framed square with a coloured dot** is a filter (the dot is its item, the bright edge is its arrow side).
  - The **clay kiln** is a smelter (its mouth glows while it works); the **mushroom bed** is a fungus garden.
  - **See-through (dithered) buildings** are blueprints still being built; they fill in as workers work on them.
- **Flow overlay (O):**
  - Belts and lifts go from **grey** (nothing moving) to **green** (busy). **Red** = jammed: full and not moving.
  - Machines are **green** while working, **yellow** while waiting for input, **red** when their products are not being taken away.
  - Idle workers get a **blue dot** above them.

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
  - Machines that need the item and belts come first (see *How workers use belts and machines*).
- Delivered items are **kept physically**: each goes into the nearest place with room (the royal chamber or a storage cell), preferring one that already holds the same item, so storage stays sorted. You can see every stored item (see *The screen*). Together they are the colony stock shown in the resource bar; building costs are paid from it.
- **Capacity** (items of any type):
  - The royal chamber holds **40 items** and **100 food**.
  - **Each storage cell holds 8 items and adds +6 food.**
  - Food is kept as food points, not as items.
  - Workers only pick up what still fits in the stock or in a machine that needs it. When storage is full, piles stay where they are until you add storage (or a machine wants them).
- **Erasing** a storage cell moves its items to other storage with room; whatever doesn't fit drops to the floor as piles.

### Food and the queen
- **Food sources right now:**
  - **Seeds** appear on the grass near the nest every few seconds (up to 12 lying around). Workers walk out and bring them in: 2 food each.
  - **Grub nests** are pale-speckled soil in the topsoil. Dig them out and they drop **grubs**: 5 food each.
  - **Fungus Garden** (Build tool): turns 2 seeds into 3 fungus worth 3 food each, so seeds are worth more than twice as much. Workers bring seeds to a garden before the queen.
- **Eating:** every worker eats 0.25 food per minute and the queen 1.5 per minute.
- **Eggs:** when there is enough food, the queen lays an **egg** every 6 seconds (6 food each, at most 6 eggs at a time). After 20 seconds an egg hatches into a new worker.
- **Starving:** if food runs out, the colony is starving. Workers move and dig at half speed and the queen stops laying until food comes in again. Nobody dies.

### What workers do on their own
Each idle worker picks the best job it can reach (nearer jobs first):
1. **Build** a blueprint, if fewer than 2 workers are on it.
2. **Mine** an order, if there's room at the digging front.
3. **Fetch** an item from storage for a machine that is waiting for it.
4. **Haul** an item pile to a drop-off or a machine, if it still fits.
5. Otherwise it **wanders** near the queen.

Workers crawl on floors, walls, ceilings and the back wall of dug tunnels, so they take direct routes through chambers. Outside on the surface they walk along the ground. If the ground under one is dug away in the open sky, it falls.

### Building (Build tool)
- Choose a building in the palette and place it. The **cost is paid from your stock right away** and a see-through **blueprint** appears. Workers walk over and build it (up to 2 workers per building). You can't place what you can't afford.
- **Removing** a building (right mouse) gives back its full cost. Items riding a removed belt, and a removed machine's stored inputs, drop to the floor as piles.
- You can only build on cells you can see. Belts, lifts, splitters and filters need dug-out space (or the ground surface outside). Machines need dug-out space underground **and a flat, solid floor under their whole width**.

| Building | Cost | Size | Build time (1 worker) | What it does |
|---|---|---|---|---|
| **Silk Belt** | 1 Copper Ore | 1 cell | 0.3 s | Carries items **left or right**, 2.5 cells per second |
| **Root Lift** | 2 Copper Ore | 1 cell | 0.5 s | Carries items **up or down**, 3 cells per second |
| **Splitter** | 3 Copper Ore | 1 cell | 1 s | Hands items in turn to every belt, lift or machine around it |
| **Filter** | 2 Copper | 1 cell | 1.5 s | Its item leaves through the arrow side; everything else through the other sides |
| **Smelter** | 12 Copper Ore | 3 × 3 | 8 s | 2 Copper Ore → 1 Copper (4 s); 2 Iron Ore + 1 Coal → 1 Iron (6 s) |
| **Fungus Garden** | 8 Copper Ore | 4 × 2 | 6 s | 2 Seeds → 3 Fungus (10 s). Fungus is food worth 3 each. |

### Belts and lifts
- Each belt or lift cell holds **2 items**. Items pass from cell to cell; if the next cell is full they wait, so a blocked line **backs up**.
- Lines **merge** when one belt or lift points into another. Two belts pointing straight at each other don't connect.
- **Where a line ends** (the last cell has no belt or lift after it):
  - into a **machine** → the machine takes the items it uses;
  - in or next to the **royal chamber** or a **storage zone** → items go into the stock;
  - anywhere else → the line **leads nowhere** and items stop at its end. The info panel warns about this.
- Belts only take items they can deliver. A line that ends in a smelter only takes smelter ingredients.
- **Splitter:** passes items in turn to every neighbouring belt, lift or machine that isn't feeding into it, skipping outputs that can't use the item. **Filter:** the chosen item only leaves through its arrow side; everything else goes to the other sides. Put at least one belt cell between two splitters or filters, since they don't connect directly.
- A line that is **full and has not moved for 2 seconds** counts as jammed: it shows red in the overlay and workers stop loading it until it moves again.

### Machines
- A machine stores up to **two crafts' worth** of each ingredient (a smelter holds 4 Copper Ore, 4 Iron Ore and 2 Coal) and starts a recipe as soon as it has the ingredients, taking turns between recipes.
- **Products** go onto a belt or lift next to the machine that leads away from it. With no such belt, products are left as a pile on the machine floor for workers to haul; at **6 waiting products** the machine pauses.
- Machines get their ingredients from **belts**, from **workers bringing fresh piles**, and from **storage**: when a machine has room for an ingredient that is in stock, a worker walks to the storage cell (or royal chamber) holding it, takes it out and carries it over. Keep ingredients in storage near your machines to keep them busy.
- Taking items out for machines and paying for buildings use the same stock, so a busy smelter can eat the copper ore you were saving for a building.

### How workers use belts and machines
- A worker carrying an item takes it to a **machine that needs it** rather than the stock, unless the machine is more than about **10 times farther** away.
- Workers **fetch from storage** for machines only as much as the machines still have room for (counting what is already on its way).
- On the way, a worker drops the item onto the **nearest belt that can deliver it** instead of walking all the way.
- Mining drops that fall past a belt or lift **land on it** if it can deliver them.
- A worker carrying something that nothing will take (stock full, no machine needs it) **puts it down** as a pile and goes back to work.

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
6. Once you have **12 Copper Ore**, press **4**, pick the **Smelter** (Q/E or click it) and place it on a flat floor in a tunnel. Fresh copper ore now goes to the smelter, and the copper it makes is hauled to the stock.
7. When workers walk far from the digging front, **drag a Silk Belt** from near the front to the royal chamber (drag toward the chamber so it flows that way). Workers drop their loads on it and go straight back. Use a **Root Lift** to bring items up a shaft.
8. Build a **Fungus Garden** so foraged seeds become more food.
9. Press **O** to see where belts are jammed and which workers are idle.

---

## Not in the game yet

Research, other castes, pheromones, enemies, water, day/night, saving and loading, and a way to put stored items onto belts. Iron has no use yet. See `docs/TODO.md` for the full list of what's missing.
