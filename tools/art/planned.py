"""Art for content that is planned but not built yet (milestones 10.4 - 10.6). Drawn with the rest so the later
milestones only wire it in; nothing in the game uses these PNGs yet. docs/ART_STYLE.md lists them under
"Not used yet" with the size and frames each one was drawn for."""
import math

import numpy as np

import colony
import ui
from palette import RAMPS
from pix import blank, keys, sprite, strip


def put(img, x, y, color):
    if 0 <= x < img.shape[1] and 0 <= y < img.shape[0]:
        img[y, x] = color


def stamp(img, x, y, rows, key):
    for dy, row in enumerate(rows):
        for dx, ch in enumerate(row):
            if ch != ".":
                put(img, x + dx, y + dy, key[ch])


# ---------------------------------------------------------------- 10.4: glow ant, fire ant

def ant_glow():
    """Glow ant, 12 x 7, 4 walk frames: a dark teal ant whose gaster is a lantern."""
    key = colony.caste_key("caste_glow", "#d4fff0", "G=spore:4 W=spore:5 g=spore:3")
    body = [
        "...........a",
        ".GWG....45a.",
        "GWWG.44.45e3",
        "gGGg1331344k",
        ".ggg.22.222.",
    ]
    return colony.caste(body, {0: "..........a."}, colony.shifted(colony.WORKER_LEGS, 0, 12), key)


def ant_glow_halo():
    """The glow ant's light, 13 x 13: a dithered disc to draw unlit around its gaster."""
    S = RAMPS["spore"]
    img = blank(13, 13)
    for y in range(13):
        for x in range(13):
            r = math.hypot(x - 6, y - 6)
            if r < 2.5:
                img[y, x] = S[5]
            elif r < 4.2 and (x + y) % 2 == 0:
                img[y, x] = S[4]
            elif r < 6.3 and (x % 2 == 0 and y % 2 == 0):
                img[y, x] = S[3]
    return img


def ant_fire():
    """Fire ant, 12 x 7, 4 walk frames: ember red with a glowing gaster tip; a spark rises from it."""
    key = colony.caste_key("caste_fire", "#ffe6a0", "Y=magma:5 W=magma:6")
    body = [
        "Y..........a",
        "W55.....45a.",
        "6543.44.45e3",
        "54321331344k",
        ".322.22.222.",
    ]
    return colony.caste(body, {0: ".Y........a."}, colony.shifted(colony.WORKER_LEGS, 0, 12), key)


# ---------------------------------------------------------------- 10.5: rival ants, mole cricket, resonator, engine

def rival_ant():
    """Rival ant raider (an enemy: 2 frames), 12 x 7: the worker's build in blue-black, with a red eye."""
    key = colony.caste_key("rival", "#c8d0e8").copy()
    key["e"] = (255, 72, 72, 255)
    body = [
        "...........a",
        ".344....45a.",
        "3432.44.45e3",
        "23221331344k",
        ".222.22.222.",
    ]
    legs = colony.shifted(colony.WORKER_LEGS, 0, 12)
    return strip([sprite(body + legs[0], key), sprite(body + legs[1], key)])


MOLE = keys("6=wood:5 5=wood:4 4=wood:3 3=wood:2 2=wood:1 k=bone:3 K=bone:4 e=#101010 a=wood:2")
MOLE_BODY = [
    ".............a..",
    "...3444433455a..",
    ".3445555445665e.",
    "3445555544566543",
    "2344444433455433",
    ".23333332234433.",
]


def mole_cricket():
    """Mole cricket, 16 x 8, 2 walk frames: a heavy brown digger with pale shovel claws."""
    a = MOLE_BODY + ["..2..2..2...kK..", ".2..2..2....kk.."]
    b = MOLE_BODY + ["...2..2..2..Kk..", "..2..2..2....kk."]
    return strip([sprite(a, MOLE), sprite(b, MOLE)])


def mole_cricket_dig():
    """Mole cricket digging, 16 x 8, 2 frames: the claws strike forward and down in turn."""
    a = list(MOLE_BODY) + ["..2..2..2.....kK", ".2..2..2........"]
    a[3] = "34455555445665kK"
    b = list(MOLE_BODY) + ["..2..2..2....kK.", ".2..2..2.....kkK"]
    return strip([sprite(a, MOLE), sprite(b, MOLE)])


def crystal_resonator():
    """Crystal resonator, 16 x 16 (2 x 2 cells), frames: idle, resonating (light arcs between the prongs)."""
    C, I = RAMPS["crystal"], RAMPS["iron"]
    frames = []
    for working in (False, True):
        img = blank(16, 16)
        for px in (3, 10):
            for y in range(0, 10):
                tone = 5 if y < 3 else 4 if y < 6 else 3
                put(img, px, y + (1 if y == 0 else 0), C[tone])
                put(img, px + 1, y, C[min(tone + 1, 6)] if working else C[tone + (1 if y < 5 else 0)])
                put(img, px + 2, y + (1 if y == 0 else 0), C[tone - 1])
        for x in range(3, 13):
            put(img, x, 10, I[4])
            put(img, x, 11, I[2])
        for y in range(12, 14):
            for x in range(6, 10):
                put(img, x, y, I[3] if x < 9 else I[2])
        for x in range(2, 14):
            put(img, x, 14, I[4] if x % 4 else I[5])
            put(img, x, 15, I[1])
        if working:
            for x, y in ((6, 2), (7, 3), (8, 2), (9, 3), (6, 6), (7, 5), (8, 6), (9, 5), (7, 8), (8, 8)):
                put(img, x, y, C[6] if (x + y) % 2 else C[5])
        frames.append(img)
    return strip(frames)


def ancient_engine():
    """Ancient Engine, 32 x 24 (4 x 3 cells), 4 restoration stages: a ruin, the housing rebuilt with its gear, the
    crystals set, and running (the core and the crystals alight)."""
    Ru, Mo, I, G, C = RAMPS["ruin"], RAMPS["moss"], RAMPS["iron"], RAMPS["gold"], RAMPS["crystal"]
    rng = np.random.default_rng(7)
    damage = rng.random((24, 32))
    frames = []
    for stage in range(4):
        img = blank(32, 24)
        # Plinth.
        for y in range(20, 24):
            for x in range(32):
                joint = (x + (4 if y % 2 else 0)) % 8 == 0
                put(img, x, y, Ru[1] if joint or y == 23 else Ru[4] if y == 20 else Ru[3])
        # Housing: an arch of blocks, lit from the top left.
        for y in range(3, 20):
            for x in range(2, 30):
                dx, dy = (x + 0.5 - 16) / 14.0, (19.5 - y) / 17.0
                if dx * dx + dy * dy > 1.0:
                    continue
                if stage == 0 and damage[y, x] > 0.80 and y < 14:
                    continue                                                    # fallen blocks
                joint = y % 4 == 3 or (x + (3 if (y // 4) % 2 else 0)) % 6 == 0
                tone = 1 if joint else 3
                if not joint and (dx + dy * -1) < -0.5:
                    tone = 4
                if not joint and dx > 0.55:
                    tone = 2
                put(img, x, y, Ru[tone])
                if stage == 0 and damage[y, x] < 0.10:
                    put(img, x, y, Mo[2 if damage[y, x] < 0.05 else 1])
        # The gear well.
        for y in range(5, 20):
            for x in range(8, 24):
                r = math.hypot(x + 0.5 - 16, y + 0.5 - 12)
                if r < 7.2:
                    put(img, x, y, Ru[0])
        if stage >= 1:
            metal = G if stage == 3 else I
            for y in range(5, 20):
                for x in range(8, 24):
                    dx, dy = x + 0.5 - 16, y + 0.5 - 12
                    r, ang = math.hypot(dx, dy), math.atan2(dy, dx)
                    tooth = math.cos(ang * 8 + (0.4 if stage == 3 else 0.0)) > 0.2
                    if 4.2 <= r < 5.6 or (5.6 <= r < 6.6 and tooth):
                        put(img, x, y, metal[4] if dx + dy < -2 else metal[2] if dx + dy > 3 else metal[3])
                    elif r < 4.2 and (abs(dx) < 0.8 or abs(dy) < 0.8):
                        put(img, x, y, metal[2])
                    elif r < 1.8:
                        put(img, x, y, metal[4])
        # Crystal sockets left and right, and the core.
        for sx in (4, 26):
            for y in range(9, 16):
                put(img, sx, y, Ru[0])
                put(img, sx + 1, y, Ru[0])
            if stage >= 2:
                lit = stage == 3
                shard = ["hw", "hm", "hm", "md", "md", ".d"] if sx == 4 else ["wh", "mh", "mh", "dm", "dm", "d."]
                key = {"w": C[6] if lit else C[4], "h": C[5] if lit else C[3], "m": C[4] if lit else C[2], "d": C[3] if lit else C[1]}
                stamp(img, sx, 9, shard, key)
        if stage == 3:
            for x, y in ((15, 11), (16, 11), (15, 12), (16, 12)):
                put(img, x, y, C[6])
            for x, y in ((14, 11), (17, 12), (15, 10), (16, 13)):
                put(img, x, y, C[5])
            for x, y in ((7, 7), (24, 6), (12, 2), (19, 3)):
                put(img, x, y, G[5])
        frames.append(img)
    return strip(frames)


# ---------------------------------------------------------------- 10.6: the Queen's Throne

def queens_throne():
    """Queen's Throne, 32 x 16 (4 x 2 cells): a gold-trimmed dais between two lamp pillars; the queen rests on it."""
    Ru, G, C = RAMPS["ruin"], RAMPS["gold"], RAMPS["crystal"]
    img = blank(32, 16)
    for x in range(32):
        put(img, x, 15, Ru[1])
    for x in range(1, 31):
        put(img, x, 14, Ru[3] if x % 6 else Ru[1])
        put(img, x, 13, G[4] if x % 4 else G[5])
    for x in range(3, 29):
        put(img, x, 12, Ru[4] if x % 5 else Ru[2])
        put(img, x, 11, G[3] if x % 3 else G[4])
    for px in (0, 29):
        for y in range(3, 13):
            put(img, px, y, Ru[3])
            put(img, px + 1, y, Ru[4] if y % 4 else Ru[2])
            put(img, px + 2, y, Ru[2])
        for x in range(3):
            put(img, px + x, 2, G[4])
            put(img, px + x, 3, G[2])
        put(img, px + 1, 0, C[6])
        put(img, px + 1, 1, C[5])
        put(img, px, 1, C[4])
        put(img, px + 2, 1, C[3])
    return img


# ---------------------------------------------------------------- UI: frames and icons

def frame_variant(light, mid, dark, stud):
    """A panel frame like UI/Skin/frame with another band colour (slice 4)."""
    def paint():
        U = RAMPS["ui"]
        key = {".": (0, 0, 0, 0), "0": U[0], "6": light, "5": mid, "4": mid, "3": dark, "S": stud, "f": ui.rgba(U[1], 0.93)}
        return sprite([
            ".0000000000.", "066666666650", "06S555555S30", "065000000430", "0650ffff0430", "0650ffff0430",
            "0650ffff0430", "0650ffff0430", "065000000430", "06S444444S30", "053333333330", ".0000000000.",
        ], key)
    return paint


G, R, C, I = RAMPS["gold"], RAMPS["ruin"], RAMPS["crystal"], RAMPS["iron"]
FRAMES = {
    "frame_queen": frame_variant(G[4], G[3], G[1], G[5]),          # the Queen panel (10.4)
    "frame_codex": frame_variant(R[4], R[3], R[1], C[4]),          # the Codex (10.5)
    "frame_slot": frame_variant(I[4], I[3], I[1], I[6]),           # save slot cards (10.6)
}

ICONS = {
    # Queen evolution (10.4).
    "evolution_brood": (keys("6=silk:5 5=silk:4 4=silk:2"), [
        "..........", "....56....", "...5665...", ".56.565.56", "5665.4.566", "5665..5665", ".54....54.", "..........", "..........", "..........",
    ]),
    "evolution_health": (keys("5=red:4 4=red:3 3=red:2 2=red:1"), [
        "..........", ".44...44..", "4554.4554.", "455544554.", "455555543.", ".4555543..", "..45543...", "...343....", "....2.....", "..........",
    ]),
    "evolution_regeneration": (keys("5=grass:5 4=grass:4 3=grass:2"), [
        "..........", "...4554...", "...4554...", ".44455444.", ".55555555.", ".55555555.", ".33455433.", "...4554...", "...3443...", "..........",
    ]),
    "evolution_egg_slots": (keys("6=silk:5 5=silk:4 4=silk:2 g=gold:4"), [
        "..........", "..56...g..", ".5665.ggg.", ".5665..g..", ".5665.....", ".4554.....", "..44......", "..........", "..........", "..........",
    ]),
    "evolution_castes": (keys("4=chitin:4 5=chitin:5 3=chitin:3 e=#1c0a0a g=gold:4"), [
        ".g........", "ggg.3..3..", ".g...55...", "...455554.", "..4e555e4.", "..3455543.", "...34443..", "..........", "..........", "..........",
    ]),
    "evolution_ability": (keys("6=#ffffff 5=void:5 4=void:4"), [
        "....5.....", ".5..5..5..", "..5...5...", "...4554...", "55.5665.55", "...5665...", "...4554...", "..5...5...", ".5..5..5..", "....5.....",
    ]),
    # Priority panel (10.4): one per job.
    "priority_haul": (keys("5=sand:4 4=sand:3 3=sand:1 a=grass:4"), [
        "..........", "...4554...", "..455554..", "..455544..", "..344443..", "...3333...", "..........", ".a..a..a..", "..a.a.a...", "...aaa....",
    ]),
    "priority_supply": (keys("5=wood:5 4=wood:4 3=wood:2 a=grass:4"), [
        "....a.....", "....a.....", "..a.a.a...", "...aaa....", "....a.....", ".4......4.", ".45555554.", ".45555554.", ".33333333.", "..........",
    ]),
    "priority_tend": (keys("5=iron:5 4=iron:4 3=iron:2"), [
        "..........", "...4.4....", "..45454...", ".4555554..", "..55.55...", ".4555554..", "..45454...", "...4.4....", "..........", "..........",
    ]),
    # Ant traits (10.4).
    "trait_strong": (keys("5=iron:5 4=iron:3 3=iron:2"), [
        "..........", "..........", "44......44", "45......54", "4555555554", "4533333354", "45......54", "33......33", "..........", "..........",
    ]),
    "trait_swift": (keys("5=crystal:5 4=crystal:4"), [
        "..........", "5...5.....", ".5...5....", "..5...5...", "...5...5..", "..4...4...", ".4...4....", "4...4.....", "..........", "..........",
    ]),
    "trait_lucky": (keys("5=grass:5 4=grass:4 3=grass:2"), [
        "..........", "..45.45...", ".4554554..", ".4555554..", "..45554...", ".4555554..", ".4554554..", "..45345...", ".....3....", "....3.....",
    ]),
    "trait_lazy": (keys("5=void:5 4=void:4"), [
        "..........", ".5555.....", "...5......", "..5...444.", ".5555..4..", "......4...", ".....444..", "..........", "..........", "..........",
    ]),
    # Codex (10.5).
    "item_tablet": (keys("5=ruin:4 4=ruin:3 3=ruin:1 k=crystal:4 K=crystal:5"), [
        "..........", "..444444..", ".45555553.", ".45kKk553.", ".4555k553.", ".45kKk553.", ".45k55553.", ".45kKk553.", ".34444443.", "..333333..",
    ]),
    "lore_fragment": (keys("p=silk:5 q=silk:2 s=silk:3 k=crystal:4"), [
        "..........", ".ppppp....", ".pqqqpp...", ".pppppps..", ".pqqkqpp..", "..ppppps..", "..pqqqp...", "...pppps..", "....sss...", "..........",
    ]),
    "lore_fragment_locked": (keys("p=ui:5 q=ui:3 s=ui:4"), [
        "..........", ".ppppp....", ".pqqqpp...", ".pppppps..", ".pqqqqpp..", "..ppppps..", "..pqqqp...", "...pppps..", "....sss...", "..........",
    ]),
    # Respec (10.6).
    "respec": (keys("5=gold:4 4=gold:3"), [
        "..........", "...5555.5.", "..5....55.", ".5....555.", ".5........", "........4.", ".444....4.", ".44....4..", ".4.4444...", "..........",
    ]),
}


def level_pips():
    """Level pips, 5 x 5, frames: empty, full."""
    key = keys("0=ui:1 1=ui:4 g=gold:4 G=gold:5 d=gold:2")
    empty = [".111.", "10001", "10001", "10001", ".111."]
    full = [".ggg.", "gGGgd", "gGggd", "ggggd", ".ddd."]
    return strip([sprite(empty, key), sprite(full, key)])


def copy_of(name):
    return ui.icon(name)


def icon(name):
    key, rows = ICONS[name]
    return lambda: sprite(rows, key)


# name -> (path under Art/, painter)
PIECES = {
    "ant_glow": ("Colony/ant_glow.png", ant_glow),
    "ant_glow_halo": ("Colony/ant_glow_halo.png", ant_glow_halo),
    "ant_fire": ("Colony/ant_fire.png", ant_fire),
    "rival_ant": ("Enemies/rival_ant.png", rival_ant),
    "mole_cricket": ("Enemies/mole_cricket.png", mole_cricket),
    "mole_cricket_dig": ("Enemies/mole_cricket_dig.png", mole_cricket_dig),
    "crystal_resonator": ("Buildings/crystal_resonator.png", crystal_resonator),
    "ancient_engine": ("Buildings/ancient_engine.png", ancient_engine),
    "queens_throne": ("Buildings/queens_throne.png", queens_throne),
    "level_pips": ("UI/Icons/level_pips.png", level_pips),
    "priority_dig": ("UI/Icons/priority_dig.png", copy_of("tool_dig")),
    "priority_build": ("UI/Icons/priority_build.png", copy_of("tool_build")),
    "priority_nurse": ("UI/Icons/priority_nurse.png", copy_of("eggs")),
    # Added when 10.4 was built: the castes panel icons and the Quickening ability (the evolution that unlocks it).
    "caste_glow": ("UI/Icons/caste_glow.png", ui.caste_icon(ant_glow, 12)),
    "caste_fire": ("UI/Icons/caste_fire.png", ui.caste_icon(ant_fire, 12)),
    "ability_quickening": ("UI/Icons/ability_quickening.png", lambda: icon("evolution_ability")()),
}
PIECES.update({name: (f"UI/Skin/{name}.png", painter) for name, painter in FRAMES.items()})
PIECES.update({name: (f"UI/Icons/{name}.png", icon(name)) for name in ICONS})
