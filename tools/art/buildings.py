"""Building sprites. Sizes are cells x 8 px; frames sit side by side (idle first, then working / animation)."""
from palette import RAMPS
from pix import blank, keys, sprite, strip

# Smelter, 24 x 24 (3 x 3 cells), frames: idle, working. A clay beehive kiln on a stone slab with a stone-lined
# mouth and a chimney. Light from the top left; while it works the mouth burns and the lintel stones glow.
KILN = keys(
    "A=clay:0 B=clay:1 C=clay:2 D=clay:3 E=clay:4 F=clay:5 "
    "1=stone:1 2=stone:2 3=stone:3 4=stone:4 5=stone:5 "
    "x=soot:0 y=soot:2 z=soot:3 "
    "r=magma:2 o=magma:3 Y=magma:4 W=magma:5 H=magma:6 "
    "g=#c98a5a G=#f2b47a"
)
SMELTER_IDLE = [
    "..............344432....",
    "..............233321....",
    "...............3432.....",
    "...............3432.....",
    "...............2321.....",
    ".........CDEEDC3432.....",
    ".......CDEFFEDD3432.....",
    "......CDEFEEDDD2321.....",
    ".....CDEFEEDDDD3432.....",
    "....CDEEEDDDDDCBBBCB....",
    "...BDDDCCCCCCCCCCBBBA...",
    "...CDEEDDDDDDDDDCCCBA...",
    "..CDEEDDD455443DDCCCBA..",
    "..CDEDDD45xxxx43DCCCBA..",
    "..BCCCCC4xxxxxx3CCBBBA..",
    ".CDEDDDD5xxxxxx3DDCCCBA.",
    ".CDEDCDD4xxxxxx3DCCCCBA.",
    ".CDDDDDD4xyyyyx2DCCCBBA.",
    ".BCCCCCC3xyzzyx2CCBBBBA.",
    ".CDDDDCC4yzzzzy2CCCCBBA.",
    "455545545222222455445432",
    "343334333433343334333221",
    "233323332333233323332211",
    "122212221222122212221111",
]
SMELTER_WORKING = [
    "..............344432....",
    "..............233321....",
    "...............3432.....",
    "...............3432.....",
    "...............2321.....",
    ".........CDEEDC3432.....",
    ".......CDEFFEDD3432.....",
    "......CDEFEEDDD2321.....",
    ".....CDEFEEDDDD3432.....",
    "....CDEEEDDDDDCBBBCB....",
    "...BDDDCCCCCCCCCCBBBA...",
    "...CDEEDDDDDDDDDCCCBA...",
    "..CDEEDDDGGGGggDDCCCBA..",
    "..CDEDDDGGroorggDCCCBA..",
    "..BCCCCCGroYYorgCCBBBA..",
    ".CDEDDDEGoYYYYogDDCCCBA.",
    ".CDEDCDEGoYWWYogDCCCCBA.",
    ".CDDDDDEGYWWWWYgDCCCBBA.",
    ".BCCCCCDGYWHHWYgCCBBBBA.",
    ".CDDDDCDGWHHHHWgCCCCBBA.",
    "45554554GYWWWWYg55445432",
    "343334334g3334g334333221",
    "233323332333233323332211",
    "122212221222122212221111",
]


def smelter():
    return strip([sprite(SMELTER_IDLE, KILN), sprite(SMELTER_WORKING, KILN)])


# ---------------------------------------------------------------- helpers

def canvas(w, h):
    return blank(w, h)


def put(img, x, y, color):
    """Sets a pixel; y counts from the top. Outside the canvas is ignored."""
    if 0 <= x < img.shape[1] and 0 <= y < img.shape[0]:
        img[y, x] = color


def stamp(img, x, y, rows, key):
    """Stamps ASCII rows with the top-left at (x, y); '.' keeps what is there."""
    for dy, row in enumerate(rows):
        for dx, ch in enumerate(row):
            if ch != ".":
                put(img, x + dx, y + dy, key[ch])


def R(name):
    return RAMPS[name]


# ---------------------------------------------------------------- transport (8 x 8, 4 frames)

def silk_belt():
    """Silk belt: a woven band along the cell floor; the stitches travel right one pixel per frame."""
    S = R("silk")
    frames = []
    for f in range(4):
        img = canvas(8, 8)
        for x in range(8):
            phase = (x - f) % 4
            put(img, x, 5, S[3] if phase == 0 else S[5])
            put(img, x, 6, S[1] if phase == 0 else S[4] if phase == 1 else S[3])
            put(img, x, 7, S[0])
        frames.append(img)
    return strip(frames)


def iron_belt():
    """Iron belt: riveted plates, one row taller than silk; the seams travel two pixels per frame."""
    I = R("iron")
    frames = []
    for f in range(4):
        img = canvas(8, 8)
        for x in range(8):
            phase = (x - f * 2) % 4
            put(img, x, 4, I[2])
            put(img, x, 5, I[3] if phase == 0 else I[5])
            put(img, x, 6, I[1] if phase == 0 else I[5] if phase == 1 else I[4])
            put(img, x, 7, I[0])
        frames.append(img)
    return strip(frames)


def lift(rail_dark, rail, rail_lit, band, rung, rung_shade):
    frames = []
    for f in range(4):
        img = canvas(8, 8)
        for y in range(8):
            up = 7 - y                         # rows from the bottom: rungs travel up one pixel per frame
            is_rung = (up - f) % 4 == 0
            below = (up - f) % 4 == 3
            put(img, 1, y, rail)
            put(img, 2, y, rail_lit)
            put(img, 5, y, rail)
            put(img, 6, y, rail_dark)
            for x in (3, 4):
                put(img, x, y, rung if is_rung else rung_shade if below else band)
            if is_rung:
                put(img, 2, y, rung)
                put(img, 5, y, rung)
        frames.append(img)
    return strip(frames)


def root_lift():
    """Root lift: two root rails with a silk strand between them and silk rungs climbing."""
    W, S = R("wood"), R("silk")
    return lift(W[1], W[3], W[4], S[2], S[5], S[1])


def iron_lift():
    I = R("iron")
    return lift(I[1], I[3], I[5], I[2], I[6], I[1])


# ---------------------------------------------------------------- logistics (8 x 8)

def splitter():
    """Splitter: a silk knot wound around a peg; items leave it in turn."""
    return sprite([
        "..5665..",
        ".565545.",
        "5653x454",
        "653xx343",
        "553xx343",
        "4543343.".replace(".", "2"),
        ".444332.",
        "..3222..",
    ], keys("6=silk:5 5=silk:4 4=silk:3 3=silk:2 2=silk:1 x=wood:0"))


def filter_gate():
    """Filter: a root frame with a silk arrowhead on its right side (the renderer turns it); the item dot sits inside."""
    return sprite([
        ".344443.",
        "3xxxxxx2",
        "3xxxxxs2",
        "3xxxxxss",
        "3xxxxxss",
        "3xxxxxs2",
        "3xxxxxx1",
        ".211111.",
    ], keys("4=wood:4 3=wood:3 2=wood:2 1=wood:1 x=wood:0 s=silk:5"))


def unloader():
    """Unloader: an iron hopper with a spout to the right (turned by the renderer); the item dot sits inside."""
    return sprite([
        "355553..",
        "3xxxx2..",
        "3xxxx24.",
        "3xxxx255",
        "3xxxx244",
        "3xxxx23.",
        "3xxxx2..",
        "211111..",
    ], keys("5=iron:5 4=iron:4 3=iron:3 2=iron:2 1=iron:1 x=iron:0"))


def chest():
    """Chest, frames: closed (empty), open (holding goods; the top item is drawn inside as a tinted lump)."""
    key = keys("5=wood:5 4=wood:4 3=wood:3 2=wood:2 1=wood:1 x=wood:0 i=iron:4 j=iron:2 k=gold:4")
    closed = [
        "........",
        ".444444.",
        "45555554",
        "4i5555i4",
        "2j2kk2j2",
        "4i4444i4",
        "3i3333i3",
        "11111111",
    ]
    opened = [
        ".544445.",
        "4xxxxxx3",
        "4xxxxxx3",
        "45555553",
        "4i4kk4i3",
        "4i4444i3",
        "3i3333i2",
        "11111111",
    ]
    return strip([sprite(closed, key), sprite(opened, key)])


# ---------------------------------------------------------------- machines

def crusher():
    """Crusher, 16 x 16, frames: idle, grinding. An iron frame with a hopper and two toothed stone rollers."""
    I, S = R("iron"), R("stone")
    key = keys("5=iron:5 4=iron:4 3=iron:3 2=iron:2 1=iron:1 x=iron:0 "
               "a=stone:2 b=stone:3 c=stone:4 d=stone:5 k=iron:1 u=sand:4 v=sand:2")
    frame = [
        ".3555555555553..",
        ".2xxxxxxxxxxx2..",
        "..2xxxxxxxxx2...",
        "...2xxxxxxx2....",
        "3444444444444442",
        "3xxxxxxxxxxxxxx2",
        "3xxxxxxxxxxxxxx2",
        "3xxxxxxxxxxxxxx2",
        "3xxxxxxxxxxxxxx2",
        "3xxxxxxxxxxxxxx2",
        "3xxxxxxxxxxxxxx2",
        "3xxxxxxxxxxxxxx2",
        "3xxxxxxxxxxxxxx2",
        "3333333333333332",
        "2212222222222122",
        "1111111111111111",
    ]
    rollers = [
        [".cddc.", "cddccb", "cdkccb", "ccccbb", "bccbba", ".bbaa."],
        [".dccd.", "dcdccb", "cckcdb", "cdccbb", "bccdba", ".abba."],
    ]
    frames = []
    for working in (False, True):
        img = sprite(frame, key)
        stamp(img, 2, 6, rollers[1 if working else 0], key)
        stamp(img, 8, 6, rollers[0 if working else 1], key)
        if working:
            for x, y, ch in ((7, 6, "u"), (8, 5, "u"), (7, 9, "v"), (8, 11, "u"), (6, 12, "v"), (9, 12, "u"), (5, 2, "u"), (9, 1, "v")):
                put(img, x, y, key[ch])
        frames.append(img)
    return strip(frames)


MUSHROOMS = {
    "big": ["..45554..", ".4566554.", "456s55543", "4555s5543", ".3322233.", "...wv....", "...wv....", "...wv...."],
    "mid": [".45554.", "4565543", "45s5543", ".32223.", "..wv...", "..wv..."],
    "small": [".454.", "45s43", ".wv..", ".wv.."],
    "sprout": ["454", ".w."],
}


def fungus_garden():
    """Fungus garden, 32 x 16, frames: idle, growing (new sprouts and drifting spores)."""
    E = R("earth")
    key = keys("6=fungus:6 5=fungus:5 4=fungus:4 3=fungus:3 2=fungus:2 s=fungus:6 w=silk:4 v=silk:2 g=spore:4 G=spore:5")
    frames = []
    for working in (False, True):
        img = canvas(32, 16)
        for x in range(32):
            put(img, x, 12, E[4] if (x * 7) % 5 else E[5])
            put(img, x, 13, E[3] if (x * 3) % 4 else E[2])
            put(img, x, 14, E[2])
            put(img, x, 15, E[1])
        for name, x in (("mid", 1), ("big", 9), ("small", 20), ("mid", 24)):
            rows = MUSHROOMS[name]
            stamp(img, x, 12 - len(rows), rows, key)
        if working:
            for x in (7, 18, 30):
                stamp(img, x - 1, 10, MUSHROOMS["sprout"], key)
            for x, y, ch in ((4, 3, "g"), (13, 1, "G"), (15, 2, "g"), (22, 5, "g"), (27, 3, "G"), (19, 0, "g")):
                put(img, x, y, key[ch])
        frames.append(img)
    return strip(frames)


LEAF_PLANT = [
    "...5...",
    "..54...",
    "..4355.",
    "...354.",
    ".553...",
    ".453...",
    "...355.",
    "...354.",
    ".553...",
    ".453...",
    "...3...",
    "...3...",
]


def aphid_ranch():
    """Aphid ranch, 32 x 16, frames: grazing, with honeydew drops on the leaves. The aphids are drawn by the game."""
    E, W = R("earth"), R("wood")
    key = keys("5=grass:5 4=grass:4 3=grass:2 h=gold:4 H=gold:5")
    frames = []
    for working in (False, True):
        img = canvas(32, 16)
        for x in range(32):
            put(img, x, 14, E[4] if (x * 5) % 7 else E[5])
            put(img, x, 15, E[2])
        for i, x in enumerate((3, 10, 18, 25)):
            rows = LEAF_PLANT if i % 2 == 0 else [r[::-1] for r in LEAF_PLANT]
            rows = rows[i % 3:]
            stamp(img, x, 14 - len(rows), rows, key)
            if working:
                put(img, x + (0 if i % 2 == 0 else 6), 14 - len(rows) + 5, key["h"])
                put(img, x + (6 if i % 2 == 0 else 0), 14 - len(rows) + 3, key["H"])
        # A twig fence: posts at both ends and a rail with gaps.
        for x in (0, 31):
            for y in range(8, 14):
                put(img, x, y, W[3] if y > 8 else W[4])
        for x in range(1, 31):
            if x % 5 != 3:
                put(img, x, 10, W[4] if x % 2 else W[3])
        frames.append(img)
    return strip(frames)


def archive():
    """Archive, 24 x 16, frames: idle, studying (the candle burns). Shelves of books above, finds below."""
    key = keys("a=wood:1 b=wood:2 c=wood:3 d=wood:4 e=wood:5 x=wood:0 R=red:2 r=red:3 B=water:1 u=water:3 G=grass:1 g=grass:3 "
               "p=silk:5 q=silk:3 n=bone:2 N=bone:4 y=gold:3 Y=gold:4 k=crystal:3 K=crystal:5 w=silk:4 f=magma:4 F=magma:6 o=magma:3 W=#ffffff")
    idle = [
        "bdddddddddddddddddddddda",
        "bcccccccccccccccccccccca",
        "bcxxxBuxxxxBuxxxxxxxxxca",
        "bcxRrBuGgxxBuxxxxxxxxxca",
        "bcxRrBuGgRrBuxxxxxxxwxca",
        "bcxRrBuGgRrBuxppqppxwxca",
        "bcxRrBuGgRrBuxpqqqpxwxca",
        "bdddddddddddddddddddddda",
        "bcbbbbbbbbbbbbbbbbbbbbca",
        "bcxxxxxxxxxxxxxxxxxxxxca",
        "bcxxxxxxxxxxxxxxxxxKxxca",
        "bcxxxxxxxxxxxYYyxxxKkxca",
        "bcxNxxxxxxNxYYYyyxxKkxca",
        "bcxNNnnnnNNxyYyyyxykkxca",
        "bdddddddddddddddddddddda",
        "aaaaaaaaaaaaaaaaaaaaaaaa",
    ]
    working = list(idle)
    working[2] = "bcxxxBuxxxxBuxxxxxxxfxca"
    working[3] = "bcxRrBuGgxxBuxxxxxxoFoca"
    working[5] = "bcxRrBuGgRrBuxpWqpWxwxca"
    working[10] = "bcxxxxxxxxxxxxxxxxxWxxca"
    return strip([sprite(idle, key), sprite(working, key)])


def nursery():
    """Nursery, 24 x 16: three silk cradles hanging from a root beam over a clay ledge. The eggs are drawn by the game
    (two per cradle, resting 2 px above the floor), so the bowls stay open."""
    W, S, C = R("wood"), R("silk"), R("clay")
    img = canvas(24, 16)
    for x in range(24):
        put(img, x, 0, W[4] if x % 6 else W[5])
        put(img, x, 1, W[2] if x % 5 else W[1])
        put(img, x, 14, C[3] if (x * 5) % 7 else C[4])
        put(img, x, 15, C[1])
    for c in range(3):
        cx = 4 + c * 8
        for y in range(2, 10):
            put(img, cx - 3, y, S[3] if y % 2 else S[2])
            put(img, cx + 3, y, S[2] if y % 2 else S[3])
        put(img, cx - 3, 10, S[4])
        put(img, cx + 3, 10, S[3])
        put(img, cx - 3, 11, S[5])
        put(img, cx - 2, 11, S[4])
        put(img, cx + 2, 11, S[4])
        put(img, cx + 3, 11, S[3])
        for dx in range(-3, 4):
            put(img, cx + dx, 12, S[4] if dx < 2 else S[2])
        put(img, cx - 2, 12, S[5])
        for dx in range(-2, 3):
            put(img, cx + dx, 13, S[1])
    return img


def pheromone_emitter():
    """Pheromone emitter, 8 x 8: a gland pod on a root base; the scent shows as a tinted dot in its socket."""
    return sprite([
        "...vv...",
        "..5665..",
        ".56xx54.",
        ".5xxxx4.",
        ".5xxxx3.",
        ".45xx43.",
        "..4332..",
        ".111111.",
    ], keys("6=silk:5 5=silk:4 4=silk:3 3=silk:2 2=silk:1 1=wood:1 v=wood:3 x=soot:1"))


def princess_chamber():
    """Princess chamber, 32 x 24, frames: idle, raising a princess (royal jelly glows and drips). An amber arch over
    a clay floor with three silk beds: two on the floor (x 8 and 24) and one on a ledge (x 16, 9 px up)."""
    G, C, S = R("gold"), R("clay"), R("silk")
    frames = []
    for working in (False, True):
        img = canvas(32, 24)
        for y in range(24):
            for x in range(32):
                dx, dy = (x + 0.5 - 16) / 16.0, (23.5 - y - 1) / 23.0
                r = (dx * dx + dy * dy) ** 0.5
                if r > 1.0 or 23 - y < 2:
                    continue
                if r > 0.90:
                    tone = 4 if dx < -0.2 else 3 if dx < 0.45 else 2            # outer rim, lit from the left
                    if dy > 0.85:
                        tone = 4
                    put(img, x, y, G[tone])
                elif r > 0.80:
                    put(img, x, y, G[1] if (x + y) % 5 else G[2])
                elif r > 0.74:
                    put(img, x, y, G[0])
        for x in range(32):
            put(img, x, 22, C[3] if (x * 5) % 7 else C[4])
            put(img, x, 23, C[1])
        for bx, by in ((8, 0), (24, 0), (16, 9)):
            base = 21 - by                                                       # top row of what the bed stands on
            if by > 0:
                for dx in range(-5, 6):
                    put(img, bx + dx, base + 1, C[3] if dx % 3 else C[4])
                    put(img, bx + dx, base + 2, C[1])
            for dx in range(-3, 4):
                put(img, bx + dx, base, S[2])
                put(img, bx + dx, base - 1, S[5] if abs(dx) < 3 else S[3])
        # Royal jelly under the crown of the arch.
        jelly, hot = (S[4], S[5]) if not working else (G[5], (255, 255, 255, 255))
        for dx in range(-2, 2):
            put(img, 16 + dx, 5, jelly)
        put(img, 15, 6, hot if working else jelly)
        if working:
            for x, y in ((15, 8), (16, 11), (9, 12), (22, 13)):
                put(img, x, y, G[4])
        frames.append(img)
    return strip(frames)


# ---------------------------------------------------------------- defence (8 x 8)

def resin_gate():
    return sprite([
        "23444432",
        "25151512",
        "24141412",
        "23444432",
        "25151512",
        "24141412",
        "23333332",
        "11111111",
    ], keys("5=gold:4 4=gold:3 3=gold:2 2=gold:1 1=gold:0"))


def iron_gate():
    return sprite([
        "24555542",
        "25151512",
        "24141412",
        "2r4444r2",
        "25151512",
        "24141412",
        "23333332",
        "11111111",
    ], keys("5=iron:5 4=iron:4 3=iron:3 2=iron:2 1=iron:0 r=iron:6"))


def resin_trap():
    """Resin trap, frames: armed (jaws open), sprung (jaws shut)."""
    key = keys("t=bone:4 s=bone:2 4=gold:3 3=gold:2 2=gold:1 1=gold:0")
    armed = ["........", "........", "........", "t......t", "ts....st", "4t3..3t4", "34433443", "11111111"]
    sprung = ["........", "........", "........", "........", "..t..t..", ".4tsst4.", "34433443", "11111111"]
    return strip([sprite(armed, key), sprite(sprung, key)])


def guard_post():
    return sprite([
        ".e......",
        ".dRRRr..",
        ".dRwRRr.",
        ".dRRRr..",
        ".dQR.Q..",
        ".c......",
        ".c......",
        "bbb.....",
    ], keys("e=wood:5 d=wood:4 c=wood:3 b=wood:1 R=red:2 r=red:3 Q=red:1 w=gold:4"))


def earth_wall():
    return sprite([
        "45555554",
        "54444443",
        "44343443",
        "43444343",
        "44434443",
        "43443433",
        "33333332",
        "21111112",
    ], keys("5=earth:5 4=earth:4 3=earth:3 2=earth:2 1=earth:1"))


# ---------------------------------------------------------------- waterworks

def pump():
    """Pump, frames: the impeller in two positions. Spout to the right (the renderer turns it)."""
    key = keys("5=iron:5 4=iron:4 3=iron:3 2=iron:2 1=iron:1 x=iron:0 u=water:3 U=water:4")
    a = ["355553..", "3xxxx2..", "3x5xx24.", "3555x2uU", "3x5xx2uu", "3xxxx23.", "3xxxx2..", "211111.."]
    b = ["355553..", "3xxxx2..", "35x5x24.", "3x5xx2Uu", "35x5x2uu", "3xxxx23.", "3xxxx2..", "211111.."]
    return strip([sprite(a, key), sprite(b, key)])


def water_wheel():
    """Water wheel, 16 x 16, 4 frames: eight paddles on a wooden rim, turning a quarter of a paddle gap per frame."""
    import math
    W, I, A = R("wood"), R("iron"), R("water")
    frames = []
    for f in range(4):
        img = canvas(16, 16)
        for y in range(16):
            for x in range(16):
                dx, dy = x + 0.5 - 8, y + 0.5 - 8
                r = math.hypot(dx, dy)
                if 6.6 < r <= 7.6:
                    put(img, x, y, W[4] if dx + dy < -4 else W[2] if dx + dy > 4 else W[3])
                elif 4.2 < r <= 4.9:
                    put(img, x, y, W[1])
        for k in range(8):
            a = (k + f / 4.0) * math.pi / 4.0
            t = 1.6
            while t <= 7.4:
                qx, qy = 8 + math.cos(a) * t, 8 - math.sin(a) * t
                color = (A[4] if k % 2 == 0 else A[3]) if t > 6.2 else W[5] if t > 4.9 else W[3]
                put(img, int(math.floor(qx)), int(math.floor(qy)), color)
                t += 0.35
        for y in range(6, 10):
            for x in range(6, 10):
                r = math.hypot(x + 0.5 - 8, y + 0.5 - 8)
                if r < 1.7:
                    put(img, x, y, I[1] if r < 0.8 else I[4] if x + y < 15 else I[2])
        frames.append(img)
    return strip(frames)


def generic():
    """A plain machine crate for buildings without their own sprite."""
    return sprite([
        "2444444444444442",
        "4i555555555555i3",
        "45xxxxxxxxxxxx43",
        "45x..........x43".replace(".", "3"),
        "45x3334443333x43",
        "45x3345k54333x43",
        "45x334k5k4333x43",
        "45x3345k54333x43",
        "45x3334443333x43",
        "45x3333333333x43",
        "45x3333333333x43",
        "45x3333333333x43",
        "45xxxxxxxxxxxx43",
        "4i444444444444i3",
        "3333333333333332",
        "1111111111111111",
    ], keys("5=wood:5 4=wood:4 3=wood:3 2=wood:2 1=wood:1 x=wood:1 i=iron:4 k=iron:5"))


# Greyscale lumps and dots, tinted with the item (or scent) colour by the renderer.
GREY = keys("w=#ffffff g=#c8c8c8 k=#8c8c8c d=#5a5a5a")


def belt_item():
    return sprite([".wg", "wgk", "gkd"], GREY)


def dot():
    return sprite([".ww.", "wwwg", "wwgk", ".gk."], GREY)


def stored_item():
    return sprite(["wgg", "gkd"], GREY)


SPRITES = {
    "silk_belt": silk_belt, "root_lift": root_lift, "iron_belt": iron_belt, "iron_lift": iron_lift, "splitter": splitter,
    "filter": filter_gate, "unloader": unloader, "chest": chest, "smelter": smelter, "crusher": crusher,
    "fungus_garden": fungus_garden, "aphid_ranch": aphid_ranch, "archive": archive, "nursery": nursery,
    "pheromone_emitter": pheromone_emitter, "princess_chamber": princess_chamber, "resin_gate": resin_gate,
    "iron_gate": iron_gate, "resin_trap": resin_trap, "guard_post": guard_post, "earth_wall": earth_wall, "pump": pump,
    "water_wheel": water_wheel, "generic": generic, "belt_item": belt_item, "dot": dot, "stored_item": stored_item,
}
