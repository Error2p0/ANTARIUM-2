"""Colony sprites: ants per caste (walk strips), the queen, eggs, princess, items. Side view, facing right."""
import numpy as np

from pix import keys, sprite, strip

# Chitin ramp as digits: 1 legs / deepest shade ... 6 specular. e = eye, k = mandible, a = antenna.
ANT = keys("1=chitin:1 2=chitin:2 3=chitin:3 4=chitin:4 5=chitin:5 6=chitin:6 e=#1c0a0a k=#f0d49c a=chitin:2")

# Worker, 12 x 7: gaster x0-3, petiole x4, thorax x5-6, neck x7, head x8-10, mandible x11.
# Two body poses (the antenna flicks) and four leg poses (alternating tripod gait).
# The gaster is a tone darker than the head and thorax, so the direction an ant faces reads at a glance.
WORKER_BODY = [
    [
        "...........a",
        ".344....45a.",
        "3432.44.45e3",
        "23221331344k",
        ".222.22.222.",
    ],
    [
        "..........a.",
        ".344....45a.",
        "3432.44.45e3",
        "23221331344k",
        ".222.22.222.",
    ],
]
WORKER_LEGS = [
    ["....2.2.2...", "...2..2..2.."],
    [".....22.2...", "....2.2...2."],
    ["....2.2.2...", "....2.2..2.."],
    ["....22..2...", "...2..2...2."],
]


def walk(bodies, legs, key):
    frames = []
    for f, leg in enumerate(legs):
        frames.append(sprite(bodies[(f // 2) % len(bodies)] + leg, key))
    return strip(frames)


def worker():
    return walk(WORKER_BODY, WORKER_LEGS, ANT)


# Queen, 24 x 12, two frames (her gaster swells as she breathes). A deep red gaster with golden bands, a heavy
# thorax and head; she rests on the floor, so the sprite's bottom row is ground level.
QUEEN = keys("1=chitin:1 2=chitin:2 3=chitin:3 4=chitin:4 5=chitin:5 6=chitin:6 e=#1c0a0a k=#f0d49c a=chitin:2 "
             "G=gold:4 g=gold:3 h=gold:2")
QUEEN_FRONT = [
    "........................",
    "........................",
    "........................",
    ".......................a",
    "...............455....a.",
    "...............4554.45..",
    "...............454345e3.",
    "...............34333443k",
    "...............222.222k.",
    "...............2.2..2...",
    "..............2..2...2..",
    ".............2...2....2.",
]
QUEEN_SPANS = [
    {3: (4, 10), 4: (2, 12), 5: (1, 13), 6: (0, 13), 7: (0, 14), 8: (0, 13), 9: (1, 13), 10: (2, 12), 11: (4, 10)},
    {2: (5, 10), 3: (3, 12), 4: (1, 13), 5: (0, 13), 6: (0, 13), 7: (0, 14), 8: (0, 13), 9: (1, 13), 10: (2, 12), 11: (4, 10)},
]


def queen_frame(spans):
    rows = [list(r) for r in QUEEN_FRONT]
    top = min(spans)
    tone = {0: "5", 1: "4", 2: "4", 3: "3", 4: "3", 5: "3", 6: "2", 7: "2", 8: "1", 9: "1"}
    for r, (a, b) in spans.items():
        depth = r - top + (0 if top == 2 else 0)
        for x in range(a, b + 1):
            # Bands bend forward at the top and bottom, like hoops around a barrel.
            bend = 1 if (r <= top or r >= 10) else 0
            if (x - bend) in (3, 6, 9, 12) and x < b:
                ch = "G" if depth <= 2 else "g" if depth <= 5 else "h"
            else:
                ch = tone[min(depth, 9)] if top == 2 else tone[min(depth + 1, 9)] if depth > 0 else "5"
                if (x - bend - 1) in (3, 6, 9) and depth in (1, 2):
                    ch = "6" if x < 8 and depth == 1 else "5"
                if x == a and depth >= 2:
                    ch = str(max(1, int(ch) - 1))
                if x == b and r != 7:
                    ch = str(max(1, int(ch) - 1))
            rows[r][x] = ch
    rows[7][14] = "1"
    return sprite(["".join(r) for r in rows], QUEEN)


def queen():
    return strip([queen_frame(s) for s in QUEEN_SPANS])


# ---------------------------------------------------------------- castes
# Every caste keeps the worker's build (three segments, eye, pale mandible, gaster a tone darker) and differs by
# silhouette first, colour second. Keys: 1 legs / deepest ... 5 light, 6 shine; e eye, k mandible, a antenna.

def caste_key(ramp, mandible="#f0d49c", extra=""):
    return keys(f"1={ramp}:0 2={ramp}:1 3={ramp}:2 4={ramp}:3 5={ramp}:4 6={ramp}:5 a={ramp}:1 e=#1c0a0a k={mandible} " + extra)


def shifted(legs, shift, width):
    """The worker's leg rows moved right by `shift` and cut to `width`."""
    return [[("." * shift + row).ljust(width, ".")[:width] for row in pose] for pose in legs]


def caste(body, flick, legs, key):
    """Four walk frames: `flick` replaces body rows (by index) in frames 2 and 3, so the antennae twitch."""
    alt = [flick.get(i, row) for i, row in enumerate(body)]
    return strip([sprite((body if f < 2 else alt) + legs[f], key) for f in range(4)])


# Soldier, 14 x 8: a big square head with heavy crossed mandibles, dark crimson.
def soldier():
    body = [
        "............a.",
        ".344....4554a.",
        "3432.44.455e4.",
        "2322133134443k",
        ".222.22.2332kk",
        ".........22...",
    ]
    return caste(body, {0: "...........a..", 1: ".344....4554a."}, shifted(WORKER_LEGS, 0, 14), caste_key("caste_soldier", "#f0c060"))


# Miner, 13 x 7: front-heavy, a boxy head with long pale digging mandibles, small gaster, dusty grey-brown.
def miner():
    body = [
        "..........a..",
        ".34.....455a.",
        "343.444.45e4k",
        "23213331" + "3444" + "k",
        ".22.222.222kk",
    ]
    return caste(body, {0: "...........a.", 1: ".34.....455a."}, shifted(WORKER_LEGS, 0, 13), caste_key("caste_miner", "#eee2c4"))


# Hauler, 13 x 7: a big swollen gaster carried high, small thorax and head, golden tan.
def hauler():
    body = [
        "..455.......a",
        ".45543....45.",
        "454333.44.4e3",
        "3433221331" + "34k",
        ".22222.22.22.",
    ]
    return caste(body, {0: "..455......a."}, shifted(WORKER_LEGS, 2, 13), caste_key("caste_hauler"))


# Nurse, 12 x 7: small and rounded, short antennae, pale cream.
def nurse():
    body = [
        ".........a..",
        "..45....45..",
        ".4543.4445e.",
        ".3433133344k",
        "..222.22.22.",
    ]
    return caste(body, {0: "..........a."}, shifted(WORKER_LEGS, 1, 12), caste_key("caste_nurse", "#ffffff"))


# Builder, 12 x 7: a humped thorax and a flat shovel jaw, olive green.
def builder():
    body = [
        "..........a.",
        ".344.45.45a.",
        "3432.44.45e3",
        "23221331344.",
        ".222.22.22kk",
    ]
    return caste(body, {0: "...........a"}, shifted(WORKER_LEGS, 0, 12), caste_key("caste_builder", "#dce6a8"))


# Scout, 12 x 7: slim, on long legs (three leg rows), two long antennae swept forward, bright orange.
SCOUT_LEGS = [
    ["....2.2.2...", "...2..2..2..", "..2...2...2."],
    [".....22.2...", "....2.2...2.", "....2.2....2"],
    ["....2.2.2...", "....2.2..2..", "...2..2..2.."],
    ["....22..2...", "...2..2...2.", "..2...2....2"],
]


def scout():
    body = [
        "..........aa",
        ".34.....45a.",
        "3432.44.4e3.",
        ".2211331.2k.",
    ]
    return caste(body, {0: ".........a.a"}, SCOUT_LEGS, caste_key("caste_scout"))


# Engineer, 12 x 7: goggle eyes and an antenna tip that glow cyan, steel blue.
def engineer():
    body = [
        "..........c.",
        ".344....45a.",
        "3432.44.4cc3",
        "23221331344k",
        ".222.22.222.",
    ]
    return caste(body, {0: "...........c"}, shifted(WORKER_LEGS, 0, 12), caste_key("caste_engineer", "#d2dae2", "c=#a0f0ff"))


# ---------------------------------------------------------------- princess, brood, critters

# Princess, 14 x 10, frames: resting (wings folded along the back), flying with wings up, wings swept back.
PRINCESS = keys("1=gold:0 2=gold:1 3=gold:2 4=gold:3 5=gold:4 6=gold:5 a=gold:1 e=#1c0a0a k=#fff4c8 "
                "w=#f4faff v=#b4cce8 u=#7c98c4")
PRINCESS_BODY = [
    ".455.....45...",
    "45543.55.45e3.",
    "3444314413" + "44k.",
    ".2332.22.222..",
]


def princess():
    rest = [
        "..............",
        "..............",
        "............a.",
        ".uvwwwwv...a..",
        "vwwwwwwvv.45..",
    ] + PRINCESS_BODY[1:] + [".....2.2.2....", "....2..2..2..."]
    up = [
        "..vww..vw.....",
        "...vwww.ww....",
        "....vwwwww..a.",
        "......uww..a..",
    ] + PRINCESS_BODY + [".....2.2.2....", ".....2.2.2...."]
    back = [
        "..............",
        "..............",
        "vwwwwwv.....a.",
        ".uvwwwwww..a..",
    ] + PRINCESS_BODY + [".....2.2.2....", ".....2.2.2...."]
    return strip([sprite(rest, PRINCESS), sprite(up, PRINCESS), sprite(back, PRINCESS)])


def egg():
    return sprite([".56.", "5665", "5654", "4543", ".33."], keys("6=silk:5 5=silk:4 4=silk:3 3=silk:2"))


# Item lumps are greyscale and tinted with the item's colour by the renderer.
GREY = keys("w=#ffffff g=#c8c8c8 k=#8c8c8c d=#5a5a5a")


def pile():
    return sprite(["..wg..", ".wggk.", "wgggkk", "gkkkdd"], GREY)


def carry():
    return sprite([".wg", "wgk", "gkd"], GREY)


def golden_beetle():
    key = keys("2=gold:1 3=gold:2 4=gold:3 5=gold:4 6=gold:5 b=wood:1 e=#fff2a8 l=wood:1")
    body = [
        "...4554...",
        "..456554b.",
        ".34565543b",
        ".23444432e",
        "..233322b.",
    ]
    return strip([sprite(body + [legs], key) for legs in (".l.l..l.l.", "..l.ll.l..")])


def golden_aphid():
    key = keys("2=gold:1 3=gold:2 4=gold:3 5=gold:4 6=gold:5 a=gold:1 l=wood:1")
    body = [".4554.", "456543", "344432"]
    return strip([sprite(["..a.a."] + body + [".l.l.."], key), sprite(["...aa."] + body + ["..l.l."], key)])
