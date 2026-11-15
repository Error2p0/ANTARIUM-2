"""Enemies (Art/Enemies/, two walk frames each) and the surface layer (Art/Surface/: plants, the bird, aphids).
Everything faces right and stands on its bottom row."""
import numpy as np

from palette import RAMPS
from pix import keys, sprite, strip


def two(frames, key):
    return strip([sprite(rows, key) for rows in frames])


# ---------------------------------------------------------------- enemies

def termite():
    """Termite raider, 11 x 6: a soft pale body behind a hard amber head with dark jaws."""
    key = keys("6=silk:5 5=silk:4 4=silk:3 3=silk:1 a=silk:2 H=copper:2 h=copper:4 k=wood:0")
    body = [".........a.", ".566565.Hh.", "5655454HhHk", ".44434.HHk."]
    return two([body + [".3.3.3.3...", "..3.3.3.3.."], body + ["..3.3.3.3..", ".3.3.3.3..."]], key)


def centipede():
    """Centipede, 16 x 6: banded copper segments, a dark red head with fangs, legs rippling."""
    key = keys("5=copper:4 4=copper:3 3=copper:2 2=copper:1 1=copper:1 H=magma:2 e=#fff0a0 k=wood:0 a=magma:1")
    body = ["..............aa", ".54545454545455.", "4343434343434HeH", ".232323232323HHk"]
    return two([body + ["1.1.1.1.1.1.1...", ".1.1.1.1.1.1.1.."], body + [".1.1.1.1.1.1.1..", "1.1.1.1.1.1.1..."]], key)


def spider():
    """Cave spider, 11 x 7: a round violet body, red eyes, legs arching to both sides."""
    key = keys("6=fungus:5 5=fungus:4 4=fungus:3 3=fungus:2 2=fungus:1 v=fungus:3 E=#ff4848")
    a = ["...4554....", "..456554...", ".34555543E.", "v34444432Ev", "v.v22222v.v", "v.v.v.v.v.v", "..........."]
    b = ["...4554....", "..456554...", ".34555543E.", ".34444432E.", "vvv22222vvv", ".v.v...v.v.", "v.......v.."]
    return two([a, b], key)


def mite():
    """Fungal mite, 6 x 5: a tiny pink ball on hair legs."""
    key = keys("6=fungus:6 5=fungus:5 4=fungus:4 3=fungus:3 2=fungus:2 e=#2a0a1a")
    body = [".4554.", "456654", "34554e", ".3443."]
    return two([body + ["2.22.2"], body + [".2..2."]], key)


def fire_beetle():
    """Fire beetle, 12 x 7: a black shell with glowing seams and a burning eye."""
    key = keys("4=basalt:4 3=basalt:3 2=basalt:1 1=basalt:4 H=basalt:5 O=magma:5 o=magma:3 E=magma:6")
    body = ["...34443....", "..34O4O443..", ".344o4o4443H", ".23O3O3O32HE", "..22222222H."]
    return two([body + [".1.1..1.1...", "..1.11.1...."], body + ["..1.11.1....", ".1.1..1.1..."]], key)


def guardian():
    """Ruin guardian, 14 x 12: a mossy stone golem with a crystal eye."""
    key = keys("5=ruin:4 4=ruin:3 3=ruin:2 2=ruin:1 m=moss:2 M=moss:3 C=crystal:5 c=crystal:4")
    top = [
        "....455554....",
        "...45555543...",
        "...45CC5543...",
        "...44cC4443...",
        "..3444444433..",
        ".4555555555543",
        "45.4M4444443.4",
        "44.4mm444m43.3",
        "33.344444433.2",
    ]
    return two([top + ["...44....44...", "...44....44...", "..344....443.."],
                top + ["....44..44....", "...444..444...", ".............."]], key)


def wolf_spider():
    """Wolf spider, 12 x 7: brown and hairy with pale stripes and a row of yellow eyes; hunts at night."""
    key = keys("5=wood:5 4=wood:4 3=wood:3 v=wood:2 s=bone:3 e=#101010 E=gold:4")
    a = ["....3443....", "..345s5s43..", ".34555s543eE", "v3445s4443Ee", "v.v33333v.v.", "v.v.v.v.v.v.", "............"]
    b = ["....3443....", "..345s5s43..", ".34555s543eE", ".3445s4443Ee", "vvv33333vvv.", ".v.v...v.v..", "v.......v..."]
    return two([a, b], key)


ENEMIES = {
    "termite": termite, "centipede": centipede, "spider": spider, "mite": mite, "fire_beetle": fire_beetle,
    "guardian": guardian, "wolf_spider": wolf_spider,
}


# ---------------------------------------------------------------- surface

def sway(rows, top, key):
    """Two frames: the plant as drawn, and with its top `top` rows leaning one pixel to the right."""
    leaning = [("." + row[:-1]) if i < top else row for i, row in enumerate(rows)]
    return strip([sprite(rows, key), sprite(leaning, key)])


PLANT = "5=grass:5 4=grass:4 3=grass:3 2=grass:1 s=grass:2 "


def plant_fern():
    return sway([
        ".....5.....",
        "....454....",
        ".5..454..5.",
        ".45.343.54.",
        "..4534354..",
        "5..43434..5",
        "45.43334.54",
        ".454333454.",
        "..4433344..",
        ".....3.....",
        ".....2.....",
        ".....2.....",
    ], 5, keys(PLANT))


def plant_bush():
    return sway([
        "....45554....",
        "..455545554..",
        ".45544554554.",
        "4554b54455454",
        "4544554b54544",
        ".34454445443.",
        "..33w444w33..",
        "....w...w....",
        "....w...w....",
    ], 3, keys(PLANT + "b=red:3 w=wood:3"))


def plant_flower():
    return sway([
        "..fFf..",
        ".fFyFf.",
        ".FyYyF.",
        "..fFf..",
        "...s...",
        "..4s...",
        ".45s...",
        "...s4..",
        "...s54.",
        "...s...",
        "..4s...",
        ".45s...",
        "...s4..",
        "...s54.",
        "...s...",
        "...s...",
    ], 7, keys(PLANT + "f=petal:3 F=petal:2 y=gold:4 Y=gold:5"))


def plant_berry_bush():
    """Berry bush (Milestone 11): a bush hung with red berries; berries fall under it by day."""
    return sway([
        "....45554....",
        "..455545554..",
        ".45R44554554.",
        "4554r54455R54",
        "4R44554r54544",
        ".34454R45443.",
        "..33w444w33..",
        "....w...w....",
        "....w...w....",
    ], 3, keys(PLANT + "R=red:3 r=red:2 w=wood:3"))


BIRD = keys("4=stone:2 3=stone:1 b=stone:0 B=stone:3 e=#ffffff o=gold:3")


def bird():
    """The bird, 14 x 6: wings up, wings level. A dark shape against the sky with a yellow beak."""
    up = ["43..........34", ".443......344.", "..4433..3344..", "...43bbbb34...", ".....bBBbeo...", "......bb......"]
    level = ["..............", "..............", "444433bb334444", ".3333bBBbeo33.", "......bb......", ".............."]
    return two([up, level], BIRD)


def bird_dive():
    return sprite(["......43", ".....43.", "..433b..", ".3bBBbeo", "43bbb...", "3......."], BIRD)


def aphid():
    """Ranch aphid, 3 x 2, two frames."""
    key = keys("5=grass:5 4=grass:4 3=grass:2")
    return two([[".55", "543"], ["55.", "543"]], key)


# ---------------------------------------------------------------- trees (Milestone 11 batch 6)
# Sizes must match TreeArt.Sizes in the game (apple 48 x 56, oak 64 x 64, pine 36 x 72); two frames side by side, the crown
# swaying a pixel in the second.

def _tree_rng(name):
    import zlib
    return np.random.default_rng(zlib.crc32(name.encode()))


def _sway(img, row):
    """Second frame: everything above `row` (counted from the bottom) shifted a pixel right."""
    h = img.shape[0]
    second = img.copy()
    top = h - row
    second[:top, 1:] = img[:top, :-1]
    second[:top, 0] = 0
    return strip([img, second])


def _broadleaf(name, w, h, trunk_top, base_half, lobes, apples):
    rng = _tree_rng(name)
    G, W, R = RAMPS["grass"], RAMPS["wood"], RAMPS["red"]
    img = np.zeros((h, w, 4), np.uint8)
    cx = w / 2 - 0.5
    ys, xs = np.mgrid[0:h, 0:w]
    up = h - 1 - ys  # height above the bottom row
    # Trunk: flared foot, bark lit on the left, two limbs into the crown.
    for y in range(trunk_top + 7):
        k = min(1.0, y / trunk_top)
        half = base_half * (1 - 0.45 * k) + 2.2 * np.exp(-y / 1.6)
        for x in range(w):
            d = x - cx
            if abs(d) > half:
                continue
            t = d / half
            tone = 4 if t < -0.55 else 3 if t < 0.15 else 2 if t < 0.7 else 1
            if (x * 7 + y * 3) % 11 == 0 or rng.random() < 0.06:
                tone = max(1, tone - 1)
            img[h - 1 - y, x] = W[tone]
    for side in (-1, 1):
        for i in range(13):
            x, y = int(round(cx + side * (1 + i * 0.9))), trunk_top + 2 + i
            if 0 <= x < w - 1 and y < h:
                img[h - 1 - y, x] = W[2]
                img[h - 1 - y, x + 1] = W[1]
    # Crown: the union of the lobes with a ragged edge, made of leaf clumps lit from the top left.
    crown = np.zeros((h, w), bool)
    for lx, ly, lr in lobes:
        dist = np.hypot(xs - (cx + lx), up - ly) / lr
        crown |= dist + rng.random((h, w)) * 0.13 <= 1.0
    clumps = []
    for _ in range(400):
        x, y = rng.integers(0, w), rng.integers(0, h)
        if crown[h - 1 - y, x] and all((x - a) ** 2 + (y - b) ** 2 >= 16 for a, b, _r in clumps):
            clumps.append((x, y, rng.uniform(3.5, 5.0)))
    best = np.full((h, w), 1e9)
    nx = np.zeros((h, w))
    ny = np.zeros((h, w))
    for a, b, r in clumps:
        d = np.hypot(xs - a, up - b)
        closer = d < best
        nx = np.where(closer, (xs - a) / r, nx)
        ny = np.where(closer, (up - b) / r, ny)
        best = np.where(closer, d, best)
    lit = (-0.6 * nx + 0.8 * ny)
    gx = (xs - cx) / (w / 2)
    gy = (up - (trunk_top + (h - trunk_top) / 2)) / ((h - trunk_top) / 2)
    whole = -0.5 * gx + 0.6 * gy
    shade = lit * 0.7 + whole * 0.6
    tone = np.select([shade > 0.75, shade > 0.3, shade > -0.15, shade > -0.6], [5, 4, 3, 2], 1)
    tone = np.where(rng.random((h, w)) < 0.08, np.maximum(tone - 1, 1), tone)
    # Rims: dark where the crown ends below or to the right, light on top.
    below = np.roll(crown, -1, 0)
    below[-1] = False
    right = np.roll(crown, -1, 1)
    right[:, -1] = False
    above = np.roll(crown, 1, 0)
    above[0] = False
    tone = np.where(crown & (~below | ~right), np.minimum(tone, 1), tone)
    tone = np.where(crown & ~above & (tone >= 3), 5, tone)
    for y in range(h):
        for x in range(w):
            if crown[y, x]:
                img[y, x] = G[int(tone[y, x])]
    for ax, ay in apples:
        x, y = int(round(cx + ax)), h - 1 - ay
        if 1 <= x < w - 1 and 2 <= y < h:
            img[y - 1, x], img[y - 1, x + 1] = R[4], R[3]
            img[y, x], img[y, x + 1] = R[3], R[1]
            img[y - 2, x + 1] = W[1]
    return _sway(img, trunk_top + 6)


def tree_apple():
    """Apple tree: a round crown of leaf clumps hung with red apples, a short sturdy trunk."""
    lobes = [(0, 36, 13), (-12, 33, 9), (12, 34, 9), (-7, 45, 9), (7, 45, 9), (0, 49, 7), (-16, 38, 6), (16, 39, 6)]
    apples = [(-9, 30), (5, 28), (13, 33), (-15, 36), (-3, 38), (9, 41), (-8, 45), (3, 49), (16, 39), (-1, 31)]
    return _broadleaf("tree_apple", 48, 56, 24, 3.0, lobes, apples)


def tree_oak():
    """Oak: a broad spreading crown on a thick trunk."""
    lobes = [(0, 42, 15), (-17, 37, 11), (17, 38, 11), (-10, 51, 10), (10, 51, 10), (0, 56, 7), (-25, 40, 6), (25, 41, 6), (-5, 33, 10), (6, 33, 10)]
    return _broadleaf("tree_oak", 64, 64, 28, 4.0, lobes, [])


def tree_pine():
    """Pine: tiers of drooping dark boughs narrowing to a point, a thin trunk below."""
    rng = _tree_rng("tree_pine")
    w, h = 36, 72
    W = RAMPS["wood"]
    N = [(0x0e, 0x20, 0x18, 255), (0x16, 0x34, 0x24, 255), (0x22, 0x4c, 0x34, 255), (0x31, 0x68, 0x44, 255), (0x4a, 0x88, 0x58, 255), (0x72, 0xaa, 0x70, 255)]
    img = np.zeros((h, w, 4), np.uint8)
    cx = w / 2 - 0.5
    for y in range(h - 8):
        for x in range(w):
            d = x - cx
            if abs(d) <= 1.6 + 1.5 * np.exp(-y / 1.5):
                img[h - 1 - y, x] = W[3 if d < -0.5 else 2 if d < 0.8 else 1]
    start, tier = 9, 9
    for y in range(start, h):
        k = (y - start) / (h - start)
        t = ((y - start) % tier) / tier
        half = (w / 2 - 1) * (1 - k) * (1 - 0.42 * t) + 1
        for x in range(w):
            d = (x - cx) / half
            if abs(d) > 1:
                continue
            # Ragged bough tips.
            if abs(d) > 0.85 and rng.random() < 0.4:
                continue
            tone = 4 if d < -0.45 else 3 if d < 0.1 else 2 if d < 0.6 else 1
            if t < 0.12:
                tone = max(0, tone - 2)
            elif t > 0.8 and d < 0:
                tone = min(5, tone + 1)
            if rng.random() < 0.1:
                tone = max(0, tone - 1)
            img[h - 1 - y, x] = N[tone]
    return _sway(img, start + 4)


SURFACE = {"plant_fern": plant_fern, "plant_bush": plant_bush, "plant_flower": plant_flower, "plant_berry_bush": plant_berry_bush, "bird": bird, "bird_dive": bird_dive, "aphid": aphid,
           "tree_apple": tree_apple, "tree_oak": tree_oak, "tree_pine": tree_pine}
