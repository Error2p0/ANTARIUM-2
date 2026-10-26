# How to play Antarium 2

This covers everything that exists in the current build (Milestones 1–7). Numbers quoted here are the defaults in `Assets/_Antarium/Content/GameConfig.asset` and the content assets in `Assets/_Antarium/Content` (Biomes, Buildings, Recipes, Research, Abilities, Contracts, Events, Enemies, Castes, Genetics, WorldModifiers), so they change if those assets are tuned.

To start, open `Assets/_Antarium/Scenes/Main.unity` and press Play. If the scene is missing, run **Antarium → Setup → Create Main Scene** in the editor menu. The game continues your saved colony if there is one (see *Saving and offline progress*); to always start fresh, untick **Continue Last Save** on the `GameBootstrap` object.

---

## Controls

### Camera

| Action | Keyboard / mouse | Gamepad |
|---|---|---|
| Pan | **W A S D** or **arrow keys** | Left stick |
| Drag the view | Hold **middle mouse button** and move | — |
| Zoom in / out (toward the cursor) | **Mouse wheel** | — |

Zoom works in whole steps (×1 to ×8) so the pixel art stays crisp. ×1 is the overview. While you control an ant (see *Controlling an ant*), the pan keys move the ant instead and the camera follows it.

### Tools

| Key | Tool | Left mouse (hold) | Right mouse (hold) |
|---|---|---|---|
| **1** | **Dig** | Dig at the cursor yourself | — |
| **2** | **Mine** | Paint mining orders for workers | Erase mining orders |
| **3** | **Storage** | Paint a storage zone | Erase storage zone |
| **4** | **Build** | Place the selected building (drag for belts and lifts) | Remove buildings (hold and drag) |
| **5** | **Scent** | Paint the selected pheromone | Erase that pheromone |

You can also click the tool buttons at the bottom of the screen. On a gamepad, the **right trigger** acts as the left mouse button and the **left trigger** as the right mouse button. Clicks that start on the HUD never reach the world.

Clicking a **golden beetle** catches it, whatever tool is active (see *Random events*).

### Abilities

| Key | Ability | What it does |
|---|---|---|
| **Z** | **Frenzy** | Used right away: all workers move, dig and build 75% faster for 20 seconds. Recharges in 2 minutes. Available from the start. |
| **X** | **Seismic Sense** | Used **at the cursor**: reveals every ore cell, grub nest and crystal within 24 cells, even in unexplored darkness. Revealed finds glow faintly so you can spot them. Recharges in 90 seconds. Unlocked by research. |
| **C** | **Royal Decree** | Used right away: the queen lays 4 eggs at once, free of food (beyond the usual egg limit). Recharges in 4 minutes. Unlocked by research. |
| **V** | **Pheromone Burst** | Used **at the cursor**: every ant within 20 cells drops what it is doing and rushes to that spot, 50% faster, for 15 seconds. Good for swarming raiders or crowding a new dig site. Recharges in 2.5 minutes (no ants in range: nothing happens and it doesn't recharge). Unlocked by research. |

The ability buttons sit at the bottom right (only unlocked ones are shown) with the time left to recharge. Clicking **Seismic Sense** or **Pheromone Burst** there waits for you to click a spot in the world; right click or **Esc** cancels.

### Scent tool (pheromones)

| Key / mouse | Action |
|---|---|
| Click a scent in the **palette** (above the toolbar) | Select it (also switches to the Scent tool) |
| **Q** / **E** | Previous / next scent |
| **Left mouse** (hold) | Paint the scent (only where you can see) |
| **Right mouse** (hold) | Erase that scent |

### Controlling an ant (possession)

| Key / mouse | Action |
|---|---|
| **F** with the cursor on an ant (or the **F Possess** button, then click an ant) | Take control of that ant |
| **W A S D** / arrows / left stick | Move it (it climbs walls and ceilings like any ant) |
| **Left mouse** (hold) | Bite toward the cursor, up to 2.5 cells away: an enemy there gets bitten, otherwise the rock is dug (with the dig combo) |
| **Right mouse** or **Space** (gamepad **A**) | Pick up a pile at or next to the ant; when carrying: deliver it (machine, royal chamber or storage, belt) or put it down |
| **F** or **Esc** (or the **F Let go** button) | Let go |

Gamepad **Y** possesses the ant under the pointer / lets go.

### Build tool

| Key / mouse | Action |
|---|---|
| Click a building in the **palette** (above the toolbar) | Select it (also switches to the Build tool) |
| **Q** / **E** | Previous / next building |
| **R** | Rotate: belts flip left/right, lifts flip up/down, filters turn a quarter clockwise; with the Pheromone Emitter selected: the next scent for new emitters |
| **Left drag** with Silk Belt or Root Lift | Lay a line. It stays on the row (belt) or column (lift) where you started, and **flows in the direction you drag**. Dragging over an existing belt or lift turns it to the new direction. |
| **Left drag** with Earth Wall or Resin Gate | Paint them on every cell you drag over |
| **Left click** with any other building | Place it (machines: the cursor marks the bottom middle) |
| **Left click on a placed filter** with the Filter selected | Change the filter's item (cycles through all items) |
| **Left click on a placed emitter** with the Pheromone Emitter selected | Change its scent (cycles through the five) |
| **Right mouse** (hold) | Remove whatever building is under the cursor |

A preview follows the cursor: **green** = can be built there, **red** = can't. The info panel says why.

### View

| Key | Action |
|---|---|
| **O** (or the **O Flow** button) | Toggle the flow overlay (see *The screen*) |
| **T** (or the **T Research** button) | Open / close the research tree |
| **K** (or the **K Castes** button) | Open / close the castes panel (once the queen can lay anything but workers) |
| **N** (or the **N Flight** button) | Open / close the Nuptial Flight screen (once *Nuptial Flight* is researched, or after your first flight) |
| **G** (or the **G Genes** button) | Open / close the Genetic Memory tree (once you have had Genetic Memory, or from the flight screen) |

### Game

| Key | Action |
|---|---|
| **Esc** (gamepad **Start**) | Cancel an ability or possess target, close the open panel, let go of the ant you control, or open the menu (Resume, Save, Load last save, New colony, Save and quit). The game is **paused** while the menu is open. |
| **F5** | Save now |
| **F9** | Load the last save (no offline progress) |

### Debug (development builds and the editor)

| Key | Action |
|---|---|
| **F3** | Show/hide the debug readout (FPS, zoom, seed, the cell under the cursor, light, tool, combo, ant/pile/order counts) |
| **F6** | Spawn 50 workers in the royal chamber |
| **F7** | Finish every blueprint immediately |
| **F8** | Add up to 20 of every resource (as far as storage has room), 50 food and 50 knowledge |
| **F10** | Start a random event now |
| **F11** | A raid arrives right now (no warning) |
| **F12** | Add 2 princesses (up to the limit) and 10 Genetic Memory |

The F3 readout also shows soldiers, enemies, the time to the next raid and its size, how many fluid chunks are moving, ants lost, ants per caste (worker / soldier / miner / hauler / nurse / builder / scout / engineer), scented cells per pheromone, cells scouted, the possessed ant, princesses, what a flight would earn now, unspent Genetic Memory, flights so far and the colony's world modifiers.

---

## The screen

- **Resource bar (top):**
  - **Food** current / capacity, with the **net food rate** per minute (food delivered minus what the colony eats; red when negative). **Ants** (with how many of them are workers once you have other castes), **Eggs**.
  - **Storage** used / total: how many items (of any type) the royal chamber and your storage cells hold. Turns orange when full.
  - **Knowledge** (violet): the research currency, with the knowledge your Archives study per minute.
  - **Princesses** waiting / how many count (once the Nuptial Flight is known) and **Memory** (gold): your unspent Genetic Memory (once you have had any).
  - One entry per resource you own, e.g. *Copper Ore 4 +12/min*. Entries appear once you have that item. The green number is how many arrived in the stock per minute recently.
- **Alert (under the bar):** shows in red while the colony is starving, and while raiders are in the royal chamber.
- **Contracts (right):** your current goals with a progress bar and the reward. A finished contract turns gold with a **Claim reward** button.
- **Event cards (left):** random events with the time left to act, a **Go** button that moves the camera there and **x** to dismiss the card. When an event ends, its card says how it went and fades after a few seconds.
  - **Raid** cards are red: first a warning with the time until the raiders arrive, then how many are left. **Go** jumps to the raiders. The card turns green if the raid is repelled.
  - A card also appears when you break into a cave where **creatures** live (which kind and how many).
- **Toolbar (bottom):** Dig / Mine / Storage / Build / Scent, **F Possess** (gold **F Let go** while you control an ant), **K Castes** (once you have more than workers), **T Research**, **O Flow** for the overlay, **G Genes** (once you have had Genetic Memory) and **N Flight** (once the Nuptial Flight is known; outlined in gold while princesses are waiting). The active tool is outlined in gold; the toggle buttons turn green while open / on.
- **Castes panel** (bottom left, **K**): one row per caste the queen can lay: how many there are (and eggs), its **share** of the colony with **-** / **+** (5% steps), and *rest* for workers, who fill whatever the others leave. See *Castes*.
- **Abilities (bottom right):** one button per unlocked ability with its hotkey; orange while running, greyed with a timer while recharging, blue while waiting for you to pick a spot.
- **Build palette** (above the toolbar, Build tool only): every building with its cost. The cost turns red while you can't afford it. Buildings still locked behind research are faded and say which research unlocks them.
- **Scent palette** (above the toolbar, Scent tool only): the five pheromones with what each does. *Defend* is faded until you have soldiers.
- **Info panel** (above the palette): details of the building under the cursor (flow per minute, what it carries, why it is jammed, a machine's inputs, progress and recipes, whether an engineer tends it, a gate's hit points, whether a trap is armed, a nursery's eggs, an emitter's scent, a princess chamber's princesses), an **ant** under the cursor (caste, hit points, what it is doing, *F: take control*), an **enemy** under the cursor (hit points, what it is doing), **water, lava or gas** under the cursor, what a storage cell or the royal chamber holds, the **scents** on the cell (*Scent: Trail 80%*), or, in the Build tool, the selected building's cost, what it does and why it can't go where the cursor is. With the Scent tool it explains the selected scent. While you control an ant it shows its hit points and the controls.
- **Messages** (above the info panel): short notes, e.g. a refused placement (*Not enough Copper Ore (3/12)*), finished research, an ability that is still recharging, a claimed contract, a random event, an ant lost (*A worker drowned.*), a cave-in, a broken gate, or taking / losing control of an ant.
- **Research tree (T):** see *Research*.
- **Nuptial Flight screen (N)** and **Genetic Memory tree (G):** see *Nuptial Flight and Genetic Memory*.
- **Welcome back** (after the game was closed): what the colony did while you were away.
- **In the world:**
  - Orange **diagonal stripes** are mining orders (also visible over unexplored darkness).
  - A **teal checker** marks storage zones.
  - **Stacks of small bricks** on the back wall of storage cells, and on the floor of the royal chamber beside the queen, are your stored items: one brick per item, coloured like the item.
  - **Small coloured blobs** are item piles waiting to be hauled.
  - **White ovals** next to the queen are eggs.
  - **Cream silk bands** along a tunnel floor are belts; **brown root rails** in a shaft are lifts. Small coloured dots riding them are items.
  - A **knot** is a splitter; a **framed square with a coloured dot** is a filter (the dot is its item, the bright edge is its arrow side).
  - The **clay kiln** is a smelter (its mouth glows while it works); the **mushroom bed** is a fungus garden; the **wooden shelves** with bones and books are an Archive (its candle burns while it studies).
  - A **golden beetle** with a blinking sparkle wanders the tunnels during its event. A blinking **orange dot** marks ants trapped in the rock.
  - A **purple crystalline lump** in a crater on the surface is a meteorite.
  - **See-through (dithered) buildings** are blueprints still being built; they fill in as workers work on them.
  - **Water** is blue with a pale surface line; **lava** is bright churning orange and lights its surroundings; **spore gas** is a speckled green haze. **Sand** is pale and grainy.
  - **Soldiers** are darker and bigger than workers, with a heavy head and golden mandibles. **Miners** are grey with a heavy head and pale jaws, **haulers** tan with a big rear (a second lump on top when they carry more than one), **nurses** pale cream, **builders** olive green, **scouts** bright orange with long antennae, **engineers** steel blue with a cyan eye.
  - A **nurse carrying an egg** holds a little white oval. The ant you control has a **blinking gold diamond** over it and a warm light around it.
  - A **clay ledge with three silk cradles** is a nursery; the eggs lie in the cradles. A **pale pod** with a coloured dot is a pheromone emitter (the dot is its scent).
  - An **amber arch** over three silk beds is a princess chamber: royal jelly glows and drips under the arch while a princess is being raised, and **golden winged princesses** rest on the beds (two on the floor, one on a raised ledge).
  - **Pheromones** are scattered speckles in the scent's colour: yellow **Trail**, orange **Dig here**, green **Gather**, red **Defend**, violet **Avoid**. They thin out as they fade and show more clearly while the Scent tool is active. **Enemies**: pale termites with brown heads (raiders), long red-brown centipedes, purple cave spiders, small pink fungal mites, black fire beetles with glowing stripes, grey stone ruin guardians with a cyan eye.
  - A short **bar** above an ant (green) or enemy (red) shows its health once it is hurt; a sprite **flashes red** when it is hit.
  - A **resin lattice** across a tunnel is a gate (it turns redder as it is damaged); **resin with bone teeth** on the floor is a trap (jaws open = armed); a **red banner** is a guard post; a **packed earth block** is a wall blueprint.
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
  - **Giant fungus** deep down in the fungal depths: every cell dug out drops 2 fungus.
- **Eating:** every worker eats 0.25 food per minute, every soldier 0.6, the other castes 0.2–0.35 (see *Castes*) and the queen 1.5 per minute.
- **Eggs:** when there is enough food, the queen lays an **egg** every 6 seconds (6 food for a worker egg, other castes cost more; at most 6 eggs waiting next to her). After 20 seconds an egg hatches into an ant of its caste. Eggs that nurses carry to a **Nursery** don't count toward the 6 and hatch faster.
- **Starving:** if food runs out, the colony is starving. Workers move and dig at half speed and the queen stops laying until food comes in again. Nobody starves to death.
- While **raiders are in the royal chamber** the queen doesn't lay either.

### What workers do on their own
Each idle worker picks the best job it can reach (nearer jobs first; jobs inside **Dig here** / **Gather** scent come first, jobs inside **Avoid** scent are never taken):
1. **Build** a blueprint, if fewer than 2 workers are on it.
2. **Mine** an order, if there's room at the digging front.
3. **Fetch** an item from storage for a machine that is waiting for it.
4. **Haul** an item pile to a drop-off or a machine, if it still fits.
5. Otherwise it **wanders** near the queen.

Workers crawl on floors, walls, ceilings and the back wall of dug tunnels, so they take direct routes through chambers. Outside on the surface they walk along the ground. If the ground under one is dug away in the open sky, it falls. They walk around deep water, thick gas and lava (see *Water, lava, gas and cave-ins*), prefer **Trail** scent and steer clear of **Avoid** scent.

Other castes only do their own kind of work (see *Castes*); soldiers never take jobs.

### Castes
The queen lays **workers** at first. Research unlocks more castes (see *Research*); each one you unlock starts at a **10% share** of the colony (soldiers 20%). Open the **castes panel (K)** to change the shares with **-** / **+** in 5% steps: one caste can be at most 50%, all of them together at most 80%, and **workers fill the rest**. The queen always lays whichever caste is furthest below its share.

| Caste | Egg (food) | Eats /min | Hit points | What it does |
|---|---|---|---|---|
| **Worker** | 6 | 0.25 | 40 | Digs, hauls, builds and fetches for machines |
| **Soldier** | 15 | 0.6 | 150 | Fights (bites hard), guards posts and Defend scent; never takes jobs |
| **Miner** | 9 | 0.35 | 50 | Digs **3 times as fast**, but only digs |
| **Hauler** | 8 | 0.3 | 45 | Carries **3 items** of a kind at once and walks 20% faster; only hauls and fetches for machines |
| **Nurse** | 6 | 0.2 | 30 | Carries eggs from the queen to a **Nursery** |
| **Builder** | 8 | 0.3 | 45 | Builds **3 times as fast**, but only builds |
| **Scout** | 8 | 0.3 | 35 | Walks 50% faster to the edge of your tunnels and **senses the rock up to 10 cells around**: hidden cells there are revealed and ore found glows. Chases and catches **golden beetles** |
| **Engineer** | 10 | 0.35 | 40 | Walks to a machine nobody tends and stands by it: the machine works **50% faster** |

- Specialists stand idle when there is none of their work (miners without mining orders, nurses without a nursery). Keep their shares to what you need.
- **Nursery** (Build tool, unlocked with *Nurse Caste*): nurses bring eggs here. Eggs in a nursery hatch **50% faster** and free the queen's egg slots, so she keeps laying. A nursery holds 6 eggs. Without nurses or a nursery, eggs simply hatch next to the queen as before.
- One engineer per machine; it stays until the machine is removed.

### Pheromones (Scent tool, 5)
Paint scents onto the map to steer the colony. Each scent is shown as coloured speckles.

| Scent | Effect |
|---|---|
| **Trail** (yellow) | Ants walk **50% faster** along it and prefer it as a route (a step along a trail counts as half the distance). Ants carrying loads keep the trail fresh as they walk it. |
| **Dig here** (orange) | Mining orders inside are dug **before all others** (unless another order is more than 40 cells closer). |
| **Gather** (green) | Item piles inside are hauled **before all others** (same rule). |
| **Defend** (red) | Idle soldiers patrol here, like at a guard post. Available once you have soldiers. |
| **Avoid** (violet) | Ants route around it (crossing it counts as 4 times the distance), take **no jobs inside** and never wander in. Enemies ignore it. |

- You can only paint where you can see. Right mouse erases the selected scent.
- Scents **fade in about 4 minutes**. A **Pheromone Emitter** (unlocked by *Scent Glands*) keeps its scent fresh within 10 cells: paint the area first, then place the emitter in it. Press **R** before placing to choose its scent, or click a placed emitter (with the emitter selected) to change it.
- Scents don't fade while the game is closed.

### Controlling an ant
Point at any ant and press **F** (or click **F Possess** and then the ant). It stops thinking for itself and you steer it:
- **WASD** moves it to the next cell in that direction: it climbs walls and ceilings in tunnels like every ant and walks on the ground outside. The camera follows it.
- **Hold left mouse** to bite toward the cursor (up to 2.5 cells away): enemies there take 6 damage per bite; otherwise you dig the rock like with the Dig tool (with the combo). You can only dig what you can see.
- **Right mouse** or **Space** picks up a pile at or next to the ant (any item, even when storage is full). When it carries something, the same button delivers it (to a machine, the royal chamber or a storage cell, or onto a belt) or, if nothing takes it there, puts it down.
- The ant you control walks 60% faster than its caste and takes only half damage.
- **F** or **Esc** lets go; the ant goes back to work. If it dies you are let go too.

### Building (Build tool)
- Choose a building in the palette and place it. The **cost is paid from your stock right away** and a see-through **blueprint** appears. Workers walk over and build it (up to 2 workers per building). You can't place what you can't afford.
- **Removing** a building (right mouse) gives back its full cost. Items riding a removed belt, and a removed machine's stored inputs, drop to the floor as piles.
- You can only build on cells you can see, and not in deep water, thick gas or lava. Belts, lifts, splitters, filters, walls, gates and guard posts need open space ants can walk on (dug-out space or the ground surface outside). Traps also need a solid floor under them. Machines need dug-out space underground **and a flat, solid floor under their whole width**.

| Building | Cost | Size | Build time (1 worker) | What it does |
|---|---|---|---|---|
| **Silk Belt** | 1 Copper Ore | 1 cell | 0.3 s | Carries items **left or right**, 2.5 cells per second |
| **Root Lift** | 2 Copper Ore | 1 cell | 0.5 s | Carries items **up or down**, 3 cells per second |
| **Splitter** | 3 Copper Ore | 1 cell | 1 s | Hands items in turn to every belt, lift or machine around it |
| **Filter** | 2 Copper | 1 cell | 1.5 s | Its item leaves through the arrow side; everything else through the other sides |
| **Smelter** | 12 Copper Ore | 3 × 3 | 8 s | 2 Copper Ore → 1 Copper (4 s); 2 Iron Ore + 1 Coal → 1 Iron (6 s) |
| **Fungus Garden** | 8 Copper Ore | 4 × 2 | 6 s | 2 Seeds → 3 Fungus (10 s). Fungus is food worth 3 each. |
| **Archive** | 10 Copper Ore | 3 × 2 | 10 s | Studies finds into **Knowledge**: 1 Copper → 1 (8 s), 1 Crystal Shard → 4 (6 s), 1 Amber → 5 (8 s), 1 Fossil → 6 (8 s), 1 Star Fragment → 12 (10 s), 1 Relic → 25 (12 s) |
| **Earth Wall** | Free | 1 cell | 1.5 s | When finished the cell becomes **solid packed earth** (soft, easy to mine again). Seals off water, gas, lava and caves. Not in the royal chamber or a storage zone. |
| **Resin Gate** | 4 Copper | 1 cell | 3 s | Ants walk through; **enemies must break it** (150 hit points, mends 1 per second). A broken gate becomes a blueprint again and workers rebuild it for free. |
| **Resin Trap** | 2 Copper, 2 Chitin | 1 cell, on a floor | 2 s | The first enemy that steps on it takes **30 damage** and is **stuck for 4 seconds**. Re-arms after 8 seconds. |
| **Guard Post** | 2 Copper Ore | 1 cell | 1 s | Idle soldiers wait at the nearest guard post and defend the area around it. |
| **Nursery** | 10 Copper Ore | 3 × 2 | 6 s | Nurses bring eggs here; they hatch 50% faster and the queen can lay more meanwhile. Holds 6 eggs. Needs a flat floor. |
| **Pheromone Emitter** | 3 Copper, 1 Iron | 1 cell | 2 s | Keeps one scent from fading within 10 cells. |
| **Princess Chamber** | 10 Copper, 4 Iron | 4 × 3 | 12 s | 12 Fungus + 2 Amber → **1 princess** (60 s). Stops while the colony has as many princesses as count (5). Needs a flat floor. |

The **Splitter** and **Filter** are locked until you research *Silk Routing*, the **Fungus Garden** until *Fungiculture*, the **Resin Gate** and **Resin Trap** until *Fortification*, the **Guard Post** until *Soldier Caste*, the **Nursery** until *Nurse Caste*, the **Pheromone Emitter** until *Scent Glands* and the **Princess Chamber** until *Nuptial Flight*. Locked buildings are faded in the palette.

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

### Knowledge and research
- **Knowledge** is the research currency. You get it from:
  - the **Archive**, which studies items workers bring it (copper bars, crystal shards, amber, fossils, star fragments; see the building table). Workers fetch them from storage like for any machine;
  - **contracts** and the **golden beetle**.
- Press **T** to open the research tree. Each node costs knowledge, sometimes items from the stock or food, and is bought instantly with a click. A node can be bought once every node it needs is done.
- Node colours: **green** = researched; **bright violet** = you can buy it now; **violet with red cost** = available, but you don't have enough yet; **grey** = needs other research first. Hover a node to see exactly what it does and what it still needs.
- Research is permanent for this colony (genetic traits can make a research node part of every colony: see *Nuptial Flight and Genetic Memory*).

| Tier | Research | Cost | Needs | Effect |
|---|---|---|---|---|
| 1 | **Fungiculture** | 8 Knowledge | — | Unlocks the Fungus Garden |
| 1 | **Strong Mandibles** | 10 Knowledge, 6 Copper Ore | — | Workers and you dig 25% faster |
| 1 | **Silk Routing** | 12 Knowledge, 4 Copper | — | Unlocks the Splitter and the Filter |
| 1 | **Seismic Sense** | 12 Knowledge | — | Unlocks the Seismic Sense ability (X) |
| 2 | **Royal Jelly** | 20 Knowledge, 20 Food | Fungiculture | The queen lays 25% faster; 2 more eggs can wait at once |
| 2 | **Quick Legs** | 20 Knowledge | Strong Mandibles | Workers walk 15% faster |
| 2 | **Silk Weaving** | 20 Knowledge, 6 Copper | Silk Routing | Belts and lifts 35% faster |
| 2 | **Royal Vault** | 16 Knowledge, 10 Copper Ore | Strong Mandibles | Royal chamber holds 40 more items; +60 food capacity |
| 3 | **Royal Decree** | 35 Knowledge, 40 Food | Royal Jelly | Unlocks the Royal Decree ability (C) |
| 3 | **Iron Mandibles** | 40 Knowledge, 8 Iron | Quick Legs | Workers dig 40% and build 30% faster |
| 3 | **Hot Kilns** | 35 Knowledge, 6 Iron, 6 Coal | Silk Weaving | Machines work 30% faster |
| 3 | **Foraging Trails** | 25 Knowledge | Royal Vault | Seeds appear on the surface 50% more often (and more may lie around) |
| 4 | **Dormancy** | 50 Knowledge | Royal Decree | Offline efficiency 75% instead of 50%, and 4 more offline hours |
| 4 | **Ancient Script** | 40 Knowledge, 2 Fossils | Iron Mandibles | The Archive gives 50% more knowledge |
| 4 | **Contract Network** | 45 Knowledge, 10 Copper | Hot Kilns | One more contract slot |
| 4 | **Instinct** | 40 Knowledge | Foraging Trails, Seismic Sense | Abilities recharge 25% faster |
| 5 | **Soldier Caste** | 40 Knowledge, 30 Food | Royal Jelly | The queen can lay **soldiers**; unlocks the Guard Post |
| 5 | **Fortification** | 45 Knowledge, 6 Iron | Iron Mandibles | Unlocks the Resin Gate and the Resin Trap |
| 5 | **Air Sacs** | 30 Knowledge | Foraging Trails | Ants take half damage from drowning, lava, gas and cave-ins |
| 6 | **Hardened Carapace** | 60 Knowledge, 8 Chitin | Soldier Caste | Every ant has 50% more hit points |
| 6 | **Venom Glands** | 70 Knowledge, 10 Chitin | Soldier Caste | Ants bite 50% harder |
| 6 | **Obsidian Plating** | 60 Knowledge, 6 Obsidian | Fortification | Gates have twice the hit points, traps hit twice as hard |
| 6 | **Nuptial Flight** | 80 Knowledge, 6 Iron | Fungiculture, Royal Jelly | Unlocks the **Princess Chamber** and the Nuptial Flight (see *Nuptial Flight and Genetic Memory*) |
| 2 | **Miner Caste** | 25 Knowledge, 10 Copper Ore | Strong Mandibles | The queen can lay **miners** |
| 2 | **Hauler Caste** | 25 Knowledge, 4 Copper | Silk Routing | The queen can lay **haulers** |
| 2 | **Scent Glands** | 20 Knowledge | Quick Legs | Unlocks the **Pheromone Emitter** |
| 3 | **Builder Caste** | 30 Knowledge, 4 Iron | Miner Caste | The queen can lay **builders** |
| 3 | **Nurse Caste** | 30 Knowledge, 30 Food | Royal Jelly | The queen can lay **nurses**; unlocks the **Nursery** |
| 3 | **Pheromone Burst** | 30 Knowledge, 20 Food | Scent Glands | Unlocks the Pheromone Burst ability (V) |
| 4 | **Scout Caste** | 35 Knowledge | Seismic Sense, Miner Caste | The queen can lay **scouts** |
| 4 | **Engineer Caste** | 40 Knowledge, 6 Iron | Hot Kilns, Hauler Caste | The queen can lay **engineers** |

### Contracts
- The board on the right always shows **3 contracts** (4 with *Contract Network*). Progress counts from the moment a contract appears.
- The first ones are a short **introduction** (one at a time, in order): dig 40 cells, bring in 10 copper ore, dig 40 deep, build a Smelter, hatch 10 workers, build an Archive.
- The others are drawn at random: deliver items into the stock (copper ore, grubs, seeds; iron ore once you are 110 deep, coal, fossils and chitin at 290, crystal shards at 690, obsidian at 1050, relics at 1250), make copper or iron, dig out cells, dig deeper, hatch workers, **defeat enemies** (from 290 deep), **hatch miners, haulers or nurses** (once that caste is researched). Each time you finish one, the next of that kind asks for about **30% more** and pays more (defeating enemies and hatching castes: 35–40%).
- When a contract is done, click **Claim reward** to collect it: knowledge, food and sometimes items (items that don't fit drop next to the queen). A new contract takes its place.

### Random events
Every **3 to 6 minutes** (the first after about 2.5 minutes) something happens. At most 2 events wait for you at once.

| Event | What happens | What to do |
|---|---|---|
| **Meteorite Strike** | A meteorite crashes onto the surface near the nest and leaves a glowing lump in a crater (the camera shakes). | Dig it out (Dig tool or Mine orders). Every meteorite cell drops a **Star Fragment**, worth 12 knowledge in the Archive. |
| **Rich Vein** | A blob of ore next to your tunnels is revealed: copper in the topsoil, iron in the clay, coal deeper down. It glows faintly. | Order it mined. |
| **Golden Beetle** | A golden beetle wanders your tunnels for **45 seconds**. | Click it (it sparkles) to catch it: +8 Knowledge, +20 Food, +2 Amber. If you don't, it gets away. |
| **Trapped Ants** | A cave-in traps **5 ants** in a pocket in the rock near your tunnels (only once you are 10 deep). | Dig a tunnel to them within **2.5 minutes** and they join your colony. Otherwise they are lost. |

### The deep layers
The world gets stranger the deeper you dig. Depths are cells below the surface; the borders between layers are wavy.

| Layer | Depth | Rock | Finds | Dangers |
|---|---|---|---|---|
| **Topsoil** | 0–110 | dirt, pebbles | copper ore, grub nests | **sand** pockets |
| **Clay** | 110–290 | clay | iron ore, copper ore | sand, **underground lakes** |
| **Stone strata** | 290–690 | stone | coal, iron, amber, fossils, glow crystals | lakes, **centipedes** |
| **Crystal Caverns** | 690–870 | quartzite (hard) | lots of glow crystals, iron, amber | big caves, lakes, **cave spiders** |
| **Fungal Depths** | 870–1050 | mycelium (soft) | **giant fungus** (food, glows), coal | **spore gas**, lakes, swarms of **fungal mites** |
| **Magma Zone** | 1050–1250 | basalt (very hard) | **obsidian**, iron, coal, glowing magma rock | **lava lakes**, gas, **fire beetles** |
| **Ancient Ruins** | 1250–bottom | ruin brick (very hard) | **relics** (25 knowledge each in the Archive), glow crystals | rooms guarded by **ruin guardians** |

The ruins are a grid of rectangular rooms joined by doors and hatches, built by a lost ant civilization.

### Water, lava, gas and cave-ins
- Some caves hold **water**, **lava** or **spore gas**. They stay put until you dig into them, then they flow into your tunnels:
  - **Water** falls and spreads out. Thin puddles slowly dry up in your tunnels. Water at least **half a cell deep** can't be walked through; an ant caught in it swims (slowly) to the nearest dry spot and **drowns** if it stays under too long (6 damage per second).
  - **Lava** flows slowly and heaps up. It burns ants fast (40 per second), blocks their paths and lights up its surroundings. Where lava meets water it hardens into **obsidian** (with a puff of steam).
  - **Spore gas** rises and spreads under ceilings; it slowly thins out once it is in your tunnels. Ants in it take 4 damage per second; thick gas can't be walked through.
- To deal with a flood: let it drain into a pit dug below it, wait for puddles to dry, or seal it off with **Earth Walls** (painted by dragging). Walls also seal gas and lava.
- **Sand** collapses: when you dig out the cell under sand, the sand falls into the gap (and slides off to the side of piles), one cell at a time. An ant it lands on takes 25 damage and is pushed out. Fallen sand is automatically **ordered for mining**, so workers clear it. Sand never falls into buildings, blueprints, storage zones or the royal chamber; they hold it up.
- *Air Sacs* research halves all of this damage.

### Health
- Workers have **40 hit points**, soldiers **150**. Ants heal **2 per second** after 6 seconds without damage.
- An ant that dies drops whatever it carried. A message says what happened. Losing ants is a setback, not the end.

### Enemies and raids
- **Cave creatures** live in the caves of the deeper layers (stone strata and below). They appear when you break into their cave (a card and a message tell you how many), stay put until an ant comes near, then hunt ants. When they lose their prey they wander near their cave.
- **Raids**: once the colony is **12 minutes** old and has **20 ants**, a rival termite mound raids you every **7 to 10 minutes**. A red card warns you **30 seconds** before they arrive on the surface, 60–100 cells from the nest. Each raid brings 3 termites, +1 for every 12 ants, +2 for every layer you have reached below the topsoil (at most 40).
- Raiders head for the **royal chamber**. There each one eats **1 food per second** and the queen stops laying; when a raider has eaten 8 food (or the food is gone) it leaves the way it came. Kill every raider before they eat anything and the raid is **repelled**: +10 knowledge.
- Enemies bite any ant next to them. **Every ant next to an enemy bites back**: workers weakly (3 damage per second), soldiers hard (14).
- Killed enemies drop **chitin** (fire beetles also obsidian, ruin guardians a relic) and give a little knowledge.

| Enemy | Lives in | Hit points | Bite | Notes |
|---|---|---|---|---|
| **Termite Raider** | raids from the surface | 22 | 5 every second | eats your food |
| **Centipede** | stone strata | 60 | 8 every second | up to 3 per cave |
| **Cave Spider** | crystal caverns | 55 | 10 every 0.8 s | fast, notices ants from far away |
| **Fungal Mite** | fungal depths | 14 | 3 every 0.7 s | swarms of up to 8; gas doesn't hurt it |
| **Fire Beetle** | magma zone | 160 | 15 every 1.2 s | walks through lava |
| **Ruin Guardian** | ancient ruins | 320 | 24 every 1.5 s | very tough |

### Soldiers
- Research **Soldier Caste** and the queen starts laying soldiers. Their share of the colony starts at **20%**; change it in the **castes panel (K)** like any caste (see *Castes*). Soldier eggs cost 15 food.
- Soldiers don't dig, haul or build. They **go after any enemy within 60 cells** and bite it, and otherwise wait around the nearest **Guard Post** or **Defend** scent (or near the queen if there is neither). Place guard posts or paint Defend where trouble comes from: the nest entrance, near cave openings.
- **Pheromone Burst** (V) calls every ant near the cursor to one spot: a quick way to throw everyone at a raid. You can also take control of a soldier yourself (F) and bite raiders directly.
- Make **chokepoints**: a few gates across a tunnel stop raiders while soldiers and traps work on them.

### Saving and offline progress
- The game **saves automatically every 2 minutes**, when you quit, and when you press **F5** or use *Save* in the menu. There is one save slot; the previous save is kept as a backup and used if the main file is damaged. Saves live in the game's data folder (`%USERPROFILE%/AppData/LocalLow/<company>/ANTARIUM 2/Saves` on Windows).
- When you start the game, it **continues** the saved colony. If it has been closed for **at least a minute**, the colony gets **offline progress**:
  - It runs at **50% of its recent pace** (75% with *Dormancy*) for up to **8 hours** (12 with *Dormancy*).
  - "Recent pace" is what the stock gained over the last 5 minutes you played: items delivered minus items taken out for machines, food brought in, and knowledge studied. Items only arrive while storage has room.
  - The colony also eats at that pace. The queen keeps laying while there is food to spare (she keeps 10 minutes of upkeep in reserve), and the eggs hatch into workers.
  - Waiting eggs (also those nurses carry) hatch, abilities recharge, and timed events (beetle, trapped ants) run out. New eggs follow your caste shares.
  - No raids, creatures or hazards happen while the game is closed.
  - A **Welcome back** panel lists what changed.
- Saves keep castes, eggs in nurseries, what haulers and nurses carry, every painted scent, princesses, your Genetic Memory and traits, and the colony's world modifiers. The ant you control is let go when you load. Saves from earlier versions load (with no Genetic Memory yet).
- **Load last save** (menu or **F9**) goes back to the last save without offline progress. **New colony** (click twice to confirm) abandons the current colony and starts on a new random world without world modifiers; you **keep your Genetic Memory and traits** but earn nothing for the abandoned colony. The next save overwrites the old colony.

### Nuptial Flight and Genetic Memory
When a colony slows down, the queen can send winged **princesses** to found a new one. The colony ends, but its **Genetic Memory** lives on: you spend it on **genetic traits** that are kept forever, in every colony after.

- **Princesses**: research **Nuptial Flight** (tier 6) and build a **Princess Chamber**. Workers and belts feed it like any machine: **12 Fungus + 2 Amber** raise **1 princess** in 60 seconds. Up to **5** princesses count (traits add more); the chamber rests while you have that many.
- **The flight screen (N)** shows what a flight would earn and why:
  - **Colony score** = deepest dig × 0.1 + knowledge earned × 0.1 + research nodes done × 2 + contracts completed × 1 (starting knowledge from traits doesn't count as earned).
  - **Genetic Memory** = √(score ÷ 4) × **princess factor** × **gain**, rounded down. The princess factor is 1 for one princess and +0.25 for each one after it (2 with five). Gain comes from traits and world modifiers.
  - You need at least **1 princess** and at least 1 Genetic Memory to fly.
- **World modifiers** for the next colony are picked on the same screen: click one to choose it (1 slot; the trait *Wanderlust* adds more). Harder worlds raise the next flight's Genetic Memory.

| Modifier | Effect | Unlocked |
|---|---|---|
| **Rich Veins** | Much more ore in every layer; raids 25% bigger | from the start |
| **Lush Meadow** | Seeds appear 50% more often; ore a little rarer | from the start |
| **Shallow World** | Every layer 25% thinner (the deep biomes come sooner); +10% Genetic Memory | from the start |
| **Hollow Earth** | Far more caves, 50% more creatures in them; +15% Genetic Memory | from the start |
| **Quiet Surface** | Raids 40% less often and 25% smaller; −25% Genetic Memory | from the start |
| **Flooded Depths** | Far more lakes in the caves; +25% Genetic Memory | *Strange Worlds* |
| **Volcanic** | More lava lakes and gas pockets in the deep; +30% Genetic Memory | *Strange Worlds* |
| **Famine** | Seeds appear half as often; +35% Genetic Memory | *Strange Worlds* |
| **Swarming** | Raids 50% bigger and 30% more often, twice the cave creatures; +50% Genetic Memory | *Strange Worlds* |

- **Take flight** (click twice to confirm): the princesses fly up out of the nest into the sky, then the new colony is founded on a new random world with your modifiers, and the game saves at once. The genetic tree opens so you can spend what you earned.
- **What carries over**: unspent Genetic Memory, every trait, the number of flights and your records (deepest colony, best flight). Everything else (ants, buildings, research, items, princesses) starts over.
- **The genetic tree (G)**: click a trait to buy its next level. Colony bonuses and inherited research work **right away**; starting bonuses (workers, food, knowledge, items, revealed map) from the **next colony** on. Node colours: **gold** = you can buy it now; **dim gold** = available but not enough memory; **grey** = needs another trait first; a **green edge** = some levels bought; **green** = fully bought. Hover a trait for details.

| Tier | Trait | Cost (memory) | Levels | Needs | Effect (per level) |
|---|---|---|---|---|---|
| 1 | **Big Brood** | 1, +1 per level | 5 | — | +2 starting workers |
| 1 | **Fat Reserves** | 1, +1 | 3 | — | +40 starting food and +40 food capacity |
| 1 | **Strong Jaws** | 1, +1 | 5 | — | Workers and you dig 10% faster |
| 1 | **Long Legs** | 1, +1 | 5 | — | Workers walk 8% faster |
| 1 | **Ancestral Map** | 2, +1 | 3 | — | New colonies start with 15 more cells around the nest revealed |
| 2 | **Fertile Queen** | 2, +2 | 3 | Big Brood | The queen lays 15% faster; +1 waiting egg |
| 2 | **Inherited Gardens** | 3 | 1 | Fat Reserves | Every colony knows *Fungiculture* |
| 2 | **Inherited Silk** | 3 | 1 | Long Legs | Every colony knows *Silk Routing* |
| 2 | **Starter Cache** | 2, +1 | 3 | Strong Jaws | Start with 10 Copper Ore and 3 Copper |
| 2 | **Wanderlust** | 3, +3 | 2 | Ancestral Map | +1 world modifier slot |
| 3 | **Born Miners** | 5 | 1 | Fertile Queen | Every colony knows *Miner Caste* |
| 3 | **Born Soldiers** | 5 | 1 | Fertile Queen | Every colony knows *Soldier Caste* |
| 3 | **Vein Instinct** | 6 | 1 | Inherited Silk | Ore within 2 cells of every dug-out cell is ordered for mining automatically, so ants follow veins on their own |
| 3 | **Royal Lineage** | 4, +2 | 3 | Inherited Gardens | +1 princess counts toward a flight |
| 3 | **Strange Worlds** | 5 | 1 | Wanderlust | Unlocks Flooded Depths, Volcanic, Famine and Swarming |
| 4 | **Ancient Knowledge** | 4, +2 | 3 | Born Miners | +25 starting knowledge |
| 4 | **Deep Memory** | 5, +3 | 5 | Royal Lineage | +10% Genetic Memory from flights |
| 4 | **Dormant Wisdom** | 4, +2 | 3 | Vein Instinct | +10 percentage points offline efficiency |
| 4 | **Silk Mastery** | 4, +2 | 3 | Vein Instinct | Belts 15% and machines 10% faster |

### Light and darkness
- Sunlight reaches into shafts and tunnels near the surface and fades deeper down.
- **Glow crystals**, **giant fungi**, **magma rock** and **lava** light their surroundings, and a small warm light goes with the ant you control.
- Explored but unlit areas stay dim. Unexplored areas are black.
- Ants, the queen and items always stay visible enough to follow.

---

## A first session

The **contracts** on the right walk you through the first steps; this is the same path with a little more detail.

1. Press **3** and paint a small **storage zone** in the starting chamber. That raises your capacity.
2. Press **2** and paint a **tunnel** sideways or downward from the chamber. Watch the workers walk over and dig it out. Copper ore, grubs and other drops get carried back automatically.
3. Keep an eye on **Food**. Workers forage seeds on their own. If food runs low, dig toward grub nests in the topsoil (pale speckles), or dig them yourself with **1**.
4. As food comes in, the queen lays eggs, and every egg is a new worker. More workers means faster mining and hauling.
5. Dig down to find the clay layer (iron) and the stone layer (coal, amber, fossils and glowing crystals).
6. Once you have **12 Copper Ore**, press **4**, pick the **Smelter** (Q/E or click it) and place it on a flat floor in a tunnel. Fresh copper ore now goes to the smelter, and the copper it makes is hauled to the stock.
7. When workers walk far from the digging front, **drag a Silk Belt** from near the front to the royal chamber (drag toward the chamber so it flows that way). Workers drop their loads on it and go straight back. Use a **Root Lift** to bring items up a shaft.
8. Build an **Archive** and keep copper (or crystals, amber, fossils) in storage: workers bring them over and it turns them into knowledge. Claim contract rewards for more.
9. Press **T** and research **Fungiculture**, then build a **Fungus Garden** so foraged seeds become more food. *Silk Routing* unlocks splitters and filters.
10. Press **Z** (Frenzy) when there's a lot to dig or build. Watch the left side for **random events**: catch the golden beetle, dig out trapped ants and meteorites.
11. Press **O** to see where belts are jammed and which workers are idle.
12. Before the first **raid** (around minute 12, once you have 20 ants), research **Soldier Caste** (after *Fungiculture* and *Royal Jelly*) and put a **Guard Post** near the nest shaft. Later *Fortification* gives you gates and traps for the entrance.
13. Dig carefully near **lakes** and **sand** in the clay layer: flooded tunnels block your workers. **Earth Walls** seal off what you don't want.
14. Press **5** and paint a **Trail** along your busiest tunnel: haulers go faster and keep it fresh. Paint **Dig here** over the mining orders you want first and **Avoid** over a dangerous cave.
15. Research **Miner Caste** and **Hauler Caste**, then open the castes panel (**K**) and give them a share. Later **Nurses** with a **Nursery** keep the queen laying, **Scouts** find ore in the dark, **Engineers** speed up your machines.
16. Point at an ant and press **F** to play it yourself: dig a shortcut, carry a find home, or bite a raider. **F** again lets go.
17. When progress slows, research **Nuptial Flight**, build a **Princess Chamber** and feed it fungus and amber. Press **N** to see what a flight would earn, pick a world modifier and **take flight**. Spend the Genetic Memory in the tree (**G**): *Big Brood* and *Inherited Gardens* make the next start much faster.

---

## Not in the game yet

Glow ants and heat-resistant ants, a queen evolution tree, uses for water and lava (pumps, magma heat), day/night and weather, surface threats other than raids, a settings menu, several save slots, and a way to put stored items onto belts. See `docs/TODO.md` for the full list of what's missing.
