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
