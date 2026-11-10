"""Terrain tiles: one tileable 64 x 64 texture per material (Art/Terrain/<material id>.png).

The terrain shader adds edges, light bands, fluids and fog on top and reuses the tile (flattened and darkened) for
the background wall. Tiles are lively rather than flat: five tones of a ramp, patches that run lighter, darker,
warmer or cooler, shapes of mixed sizes (clods, boulders, facets), and ore in big lumps. They stay flat-toned and
lit from the top left; every feature repeats every eight cells, so nothing is so bold that it reads as a grid.
The shader adds large patches of lighter / darker and warmer / cooler ground in world space, which never repeat.

The painters were tuned on a 32 px tile: SCALE and AREA keep every feature at its pixel size on the larger tile
(noise lattices and layer counts times SCALE, scattered features times AREA).
"""
import math
import zlib

import numpy as np

from palette import RAMPS

SIZE = 64
SCALE = SIZE // 32
AREA = SCALE * SCALE


def rng_for(name):
    return np.random.default_rng(zlib.crc32(name.encode()))


def value_noise(rng, cells):
    """Tileable value noise: a lattice of random values, smoothly interpolated up to SIZE. `cells` is the lattice
    size on a 32 px tile."""
    cells *= SCALE
    lattice = rng.random((cells, cells))
    step = SIZE / cells
    out = np.zeros((SIZE, SIZE))
    for y in range(SIZE):
        for x in range(SIZE):
            fx, fy = x / step, y / step
            x0, y0 = int(fx) % cells, int(fy) % cells
            x1, y1 = (x0 + 1) % cells, (y0 + 1) % cells
            tx, ty = fx - int(fx), fy - int(fy)
            tx, ty = tx * tx * (3 - 2 * tx), ty * ty * (3 - 2 * ty)
            a = lattice[y0, x0] * (1 - tx) + lattice[y0, x1] * tx
            b = lattice[y1, x0] * (1 - tx) + lattice[y1, x1] * tx
            out[y, x] = a * (1 - ty) + b * ty
    return out


def relief(height, strength=1.0):
    """Slope of a height field toward the top-left light: positive = lit face, negative = shaded face."""
    lit = np.roll(height, (1, 1), (0, 1)) - np.roll(height, (-1, -1), (0, 1))
    return -lit * strength


def voronoi(rng, count, stretch=(1.0, 1.0), min_gap=4.0):
    """Tileable Voronoi: per pixel the nearest seed and the gap between the nearest and second-nearest distance.
    `count` is per 32 px tile."""
    count *= AREA
    seeds = []
    tries = 0
    while len(seeds) < count and tries < 4000 * AREA:
        tries += 1
        p = rng.random(2) * SIZE
        ok = True
        for q in seeds:
            d = np.abs(p - q)
            d = np.minimum(d, SIZE - d) * np.array(stretch)
            if np.hypot(*d) < min_gap:
                ok = False
                break
        if ok:
            seeds.append(p)
    seeds = np.array(seeds)
    ys, xs = np.mgrid[0:SIZE, 0:SIZE]
    best = np.full((SIZE, SIZE), 1e9)
    second = np.full((SIZE, SIZE), 1e9)
    owner = np.zeros((SIZE, SIZE), int)
    for i, (sx, sy) in enumerate(seeds):
        dx = np.abs(xs + 0.5 - sx)
        dy = np.abs(ys + 0.5 - sy)
        dx = np.minimum(dx, SIZE - dx) * stretch[0]
        dy = np.minimum(dy, SIZE - dy) * stretch[1]
        d = np.hypot(dx, dy)
        closer = d < best
        second = np.where(closer, best, np.minimum(second, d))
        owner = np.where(closer, i, owner)
        best = np.where(closer, d, best)
    return owner, second - best


def paint(index_map, ramp):
    """Tone indices -> RGBA through a ramp."""
    colors = np.array(RAMPS[ramp], np.uint8)
    return colors[np.clip(index_map, 0, len(colors) - 1)]


def stamp(img, x, y, rows, key):
    """Stamps a small ASCII feature with wrap-around ('.' = keep)."""
    for dy, row in enumerate(rows):
        for dx, ch in enumerate(row):
            if ch != ".":
                img[(y + dy) % SIZE, (x + dx) % SIZE] = key[ch]


def scatter(rng, count, min_gap, taken=None):
    """Random tile positions that keep a wrap-around distance from each other. `count` is per 32 px tile."""
    count *= AREA
    points = list(taken or [])
    first = len(points)
    tries = 0
    while len(points) - first < count and tries < 2000 * AREA:
        tries += 1
        p = rng.integers(0, SIZE, 2)
        if all(np.hypot(*np.minimum(np.abs(p - q), SIZE - np.abs(p - q))) >= min_gap for q in points):
            points.append(p)
    return points[first:]


def mix(a, b, t):
    return tuple(int(round(a[i] * (1 - t) + b[i] * t)) for i in range(3)) + (255,)


def tones(ramp, mid, spread=0.55):
    """Five close tones around ramp[mid] (deep, shade, mid, light, bright): terrain uses half steps of a ramp."""
    r = RAMPS[ramp]
    lo2, lo, m, hi, hi2 = r[max(mid - 2, 0)], r[mid - 1], r[mid], r[min(mid + 1, len(r) - 1)], r[min(mid + 2, len(r) - 1)]
    return np.array([mix(lo, lo2, 0.35), mix(m, lo, spread), m, mix(m, hi, spread), mix(hi, hi2, 0.35)], np.uint8)


def dirt():
    """Topsoil: crumbly warm earth in clods of mixed sizes, with lighter and darker patches, earth lumps and stones."""
    rng = rng_for("dirt")
    tone = clods(rng)
    tone = boulders(tone, rng, 3, 2, 4, min_gap=11, fills=(3, 2, 2), soft=True)
    img = tint_patches(EARTH()[tone], rng)
    E, S = RAMPS["earth"], RAMPS["stone"]
    key = {"h": mix(S[5], E[5], 0.4), "m": mix(S[4], E[4], 0.4), "d": mix(S[3], E[2], 0.4), "w": S[5], "k": E[1]}
    embed(img, rng, 3, key, [[".hm.", "hmmd", ".mdk"], ["hm", "md"], ["h", "d"]], min_gap=11)
    return img


def strata(rng, layers, wobble=1.6):
    """Wavy horizontal layers that tile: per pixel the layer index, the row inside the layer and the layer height.
    `layers` is per 32 px of height."""
    layers *= SCALE
    # Evenly spaced with a pixel of jitter: layers that start too close would cross where they wobble.
    edges = np.array([int(k * SIZE / layers) + int(rng.integers(-1, 2)) for k in range(layers)]) % SIZE
    layer = np.zeros((SIZE, SIZE), int)
    row = np.zeros((SIZE, SIZE), int)
    height = np.zeros((SIZE, SIZE), int)
    waves = [(value_noise(rng, 4)[0] - 0.5) * 2 * wobble for _ in range(layers)]
    for x in range(SIZE):
        tops = sorted((int(round(edges[k] + waves[k][x])) % SIZE, k) for k in range(layers))
        for i, (top, k) in enumerate(tops):
            nxt = tops[(i + 1) % layers][0]
            h = (nxt - top) % SIZE or SIZE
            for d in range(h):
                y = (top + d) % SIZE
                layer[y, x], row[y, x], height[y, x] = k, d, h
    return layer, row, height


def facets(rng, count, stretch, crack_share=0.45, min_gap=5.0, choices=(1, 1, 2, 2, 2, 3, 3)):
    """Faceted rock: Voronoi plates, each one flat tone (as if tilted to or from the light), lit on the edges that
    face the top left; some plates get a dark fracture along their lower right edge. Tones 0..4 around 2."""
    owner, _ = voronoi(rng, count, stretch, min_gap)
    n = owner.max() + 1
    tone = rng.choice(list(choices), n)[owner]
    cracked = (rng.random(n) < crack_share)[owner]
    other_below = (owner != np.roll(owner, -1, 0)) | (owner != np.roll(owner, -1, 1))
    other_above = (owner != np.roll(owner, 1, 0)) | (owner != np.roll(owner, 1, 1))
    fracture = other_below & cracked
    rim = other_above & ~fracture & (np.roll(fracture, 1, 0) | np.roll(fracture, 1, 1))
    tone = np.where(rim, tone + 1, tone)
    tone = np.where(fracture, 0, tone)
    return np.clip(tone, 0, 4), fracture


def boulders(tone, rng, count, rmin=3, rmax=5, min_gap=10, fills=(2, 3), soft=False):
    """Rounded lumps set into a tone map: a flat face, a lit rim on the upper left, a shaded rim on the lower right
    (`soft`: a clod of earth, without the dark crevice under it)."""
    tone = tone.copy()
    for cx, cy in scatter(rng, count, min_gap):
        ry = int(rng.integers(rmin, rmax + 1))
        rx = ry + int(rng.integers(0, 2))
        fill = int(rng.choice(list(fills)))
        for dy in range(-ry, ry + 1):
            for dx in range(-rx, rx + 1):
                d = math.hypot(dx / (rx + 0.3), dy / (ry + 0.3))
                if d > 1.0:
                    continue
                t = fill
                if d > 0.66:
                    if dx + dy < 0:
                        t = fill + 1
                    elif dx + dy > 0:
                        t = fill - 2 if d > 0.86 and not soft else fill - 1
                tone[(cy + dy) % SIZE, (cx + dx) % SIZE] = max(0, min(4, t))
    return tone


def tint_patches(img, rng, strength=0.07):
    """Colour variety: large soft patches of the tile lean a little warmer or cooler (flat within a patch)."""
    n = value_noise(rng, 4)
    out = img.astype(float)
    warm, cool = n > 0.6, n < 0.38
    out[warm, 0] *= 1 + strength
    out[warm, 2] *= 1 - strength
    out[cool, 0] *= 1 - strength
    out[cool, 2] *= 1 + strength * 0.8
    return np.clip(out, 0, 255).astype(np.uint8)


def stone():
    """Stone strata: cool grey rock in facets of mixed tones with boulders set in, and a few dark fractures."""
    rng = rng_for("stone")
    tone, fracture = rock(rng, 15, (1.0, 1.6), 0.45, min_gap=4.0)
    tone = boulders(tone, rng, 3, 3, 5, min_gap=12)
    return tint_patches(STONE()[tone], rng)


def scatter_1d(rng, count, min_gap):
    count *= SCALE
    xs = []
    tries = 0
    while len(xs) < count and tries < 500 * SCALE:
        tries += 1
        x = int(rng.integers(0, SIZE))
        if all(min(abs(x - q), SIZE - abs(x - q)) >= min_gap for q in xs):
            xs.append(x)
    return xs


# ---------------------------------------------------------------- shared pieces

def tones5(*hexes):
    """Five explicit tones (deep, shade, mid, light, bright) from hex colours."""
    return np.array([((h >> 16) & 255, (h >> 8) & 255, h & 255, 255) for h in hexes], np.uint8)


def speckle(rng, tone, light=0.02, dark=0.02, keep=None):
    """Single lighter and darker grains on mid-tone pixels."""
    grain = rng.random((SIZE, SIZE))
    ok = (tone == 2) if keep is None else keep
    tone = np.where((grain > 1 - light) & ok, tone + 1, tone)
    return np.where((grain < dark) & ok, tone - 1, tone)


def clods(rng, gap_level=0.4, deep_level=0.3, patches=True):
    """Soil relief as tones 0..4: dark gaps between clods with a lit rim on their lower right. With `patches`
    the ground runs in lighter (drier) and darker (damper) areas and the clods are finer in some and coarser
    in others."""
    height = value_noise(rng, 8) * 0.55 + value_noise(rng, 16) * 0.45
    big = value_noise(rng, 4)
    level = gap_level + ((big - 0.5) * 0.3 if patches else 0.0)
    gap = height < level
    base = np.full((SIZE, SIZE), 2)
    if patches:
        base[big > 0.6] = 3
        base[big < 0.3] = 1
    tone = np.where(gap, base - 1, base)
    tone[(value_noise(rng, 4) < deep_level) & gap] -= 1
    lit = np.roll(gap, (1, 1), (0, 1)) & ~gap
    tone = np.where(lit & (rng.random((SIZE, SIZE)) < 0.75), base + 1, tone)
    grain = rng.random((SIZE, SIZE))
    tone = np.where((grain > 0.985) & ~gap, tone + 1, tone)
    tone = np.where((grain < 0.02) & ~gap, tone - 1, tone)
    return np.clip(tone, 0, 4)


def rock(rng, count=13, stretch=(1.0, 1.7), crack_share=0.45, min_gap=5.0):
    tone, fracture = facets(rng, count, stretch, crack_share, min_gap)
    grain = rng.random((SIZE, SIZE))
    tone = np.where((grain > 0.97) & ~fracture & (tone < 4), tone + 1, tone)
    tone = np.where((grain < 0.03) & ~fracture & (tone > 1), tone - 1, tone)
    return tone, fracture


# Lumps embedded in a host: h lit, m body, d shade, w shine, k the host's deep tone (a shadow under the lump).
LUMPS = [
    ["hm", "md"],
    [".hm.", "hmmd", ".mdk"],
    [".hh.", "hwmd", "mmdd", ".ddk"],
    ["hm.", "mmd", ".dk"],
    [".hmm.", "hwmmd", "hmmdd", ".mddk"],
    ["h", "d"],
    ["hmd"],
]


# Ore comes in big lumps (the old generated ore was readable from far out: keep that).
BIG = [
    ["..hhm..", ".hwhmm.", "hhmmmmd", "hmmmmdd", ".mmdddk", "..ddkk."],
    [".hhm..", "hwmmm.", "hmmmdd", ".mmddk", "..ddk."],
    [".hh..", "hwhm.", "hmmmd", "hmmdd", ".mddk", "..dk."],
    ["..hm..", ".hwmm.", "hhmmmd", "hmmmdd", ".mmddk", "..ddk."],
]


def veins(img, rng, key, big=4, small=5, shapes=LUMPS):
    """An ore body: a few big lumps and smaller ones between them."""
    spots = embed(img, rng, big, key, BIG, min_gap=11)
    embed(img, rng, small, key, shapes, min_gap=8, taken=spots)


def embed(img, rng, count, key, shapes=LUMPS, min_gap=6, taken=None):
    """Stamps `count` lumps at spread-out positions; returns the positions."""
    spots = scatter(rng, count, min_gap, taken)
    for i, (x, y) in enumerate(spots):
        stamp(img, int(x), int(y), shapes[i % len(shapes)], key)
    return spots


def lump_key(ramp, hi, deep, shine=None):
    r = RAMPS[ramp]
    return {"h": r[hi], "m": r[hi - 1], "d": r[hi - 2], "w": r[shine if shine is not None else min(hi + 1, len(r) - 1)], "k": deep}


EARTH = lambda: tones("earth", 3, 0.7)
STONE = lambda: tones("stone", 3, 0.7)


# ---------------------------------------------------------------- topsoil

def grass():
    """Turf: mid green with darker tufts and short lit blades."""
    rng = rng_for("grass")
    tone = np.full((SIZE, SIZE), 2)
    tone[value_noise(rng, 8) < 0.38] = 1
    tone[(value_noise(rng, 4) < 0.28) & (tone == 1)] = 0
    for x, y in scatter(rng, 26, 4):
        h = int(rng.integers(2, 4))
        for d in range(h):
            tone[(y + d) % SIZE, x] = 3
        tone[y % SIZE, x] = 4 if rng.random() < 0.5 else 3
        tone[(y + h) % SIZE, (x + 1) % SIZE] = 1
    return tint_patches(tones("grass", 2, 0.6)[tone], rng, 0.09)


def pebbles():
    """Topsoil full of small grey stones."""
    rng = rng_for("pebbles")
    img = tint_patches(EARTH()[clods(rng)], rng)
    S, E = RAMPS["stone"], RAMPS["earth"]
    # Half the topsoil is this material, so the stones stay few, small and warm: they must not read as a pattern.
    key = {"h": mix(S[5], E[5], 0.35), "m": mix(S[4], E[4], 0.35), "d": mix(S[3], E[2], 0.4), "w": S[5], "k": E[1]}
    embed(img, rng, 6, key, [["hm", "md"], [".hm.", "hmmd", ".mdk"], ["h", "d"], ["hm.", "mmd", ".dk"], ["hm"], ["h", "d"]], min_gap=9)
    return img


def grub_nest():
    """Topsoil with pale grubs curled in small dark pockets."""
    rng = rng_for("grub_nest")
    img = tint_patches(EARTH()[clods(rng)], rng)
    K, E = RAMPS["silk"], RAMPS["earth"]
    key = {"w": K[5], "c": K[4], "s": K[3], "d": K[2], "k": mix(E[1], E[2], 0.5)}
    grubs = [
        [".kk...", "kwck..", "kccckk", ".kscdk", "..kkk."],
        ["..kkk.", ".kwcck", "kccskk", "kdsk..", ".kk..."],
        [".kkk.", "kwcck", "kscdk", ".kkk."],
    ]
    embed(img, rng, 5, key, grubs, min_gap=10)
    return img


def sand():
    """Fine pale sand with soft ripples."""
    rng = rng_for("sand")
    tone = np.full((SIZE, SIZE), 2)
    for base in range(3, SIZE, 8):
        wave = (value_noise(rng, 4)[0] - 0.5) * 5
        for x in range(SIZE):
            if rng.random() < 0.2:
                continue
            y = int(round(base + wave[x])) % SIZE
            tone[y, x] = 3
            tone[(y + 1) % SIZE, x] = 1
    tone = speckle(rng, tone, 0.05, 0.05)
    grain = rng.random((SIZE, SIZE))
    tone = np.where(grain > 0.992, 4, tone)
    tone = np.where((value_noise(rng, 4) > 0.62) & (tone == 2), 3, tone)
    return tint_patches(tones("sand", 3, 0.5)[tone], rng, 0.05)


def root():
    """Root wood: fibres running down, darker seams between the strands, a knot or two."""
    rng = rng_for("root")
    tone = np.zeros((SIZE, SIZE), int)
    edges = sorted(scatter_1d(rng, 9, 3))
    drift = (value_noise(rng, 4)[:, 0] - 0.5) * 7
    for y in range(SIZE):
        shift = int(round(drift[y]))
        for i, e in enumerate(edges):
            nxt = edges[(i + 1) % len(edges)]
            width = (nxt - e) % SIZE or SIZE
            base = 2 if i % 3 else 1
            for d in range(width):
                x = (e + d + shift) % SIZE
                tone[y, x] = 0 if d == 0 and rng.random() < 0.5 else base + (1 if d == 1 else 0) - (1 if d == width - 1 and width > 2 else 0)
    tone = speckle(rng, tone, 0.02, 0.03)
    img = tones("wood", 3, 0.5)[np.clip(tone, 0, 4)]
    W = RAMPS["wood"]
    key = {"h": W[5], "m": W[3], "d": W[1], "k": W[0]}
    for (x, y), rows in zip(scatter(rng, 2, 14), [[".hh.", "hkkd", "hkkd", ".dd."], ["hh", "kd"]]):
        stamp(img, int(x), int(y), rows, key)
    return img


def packed_earth():
    """Tamped earth: flat dark layers pressed by the builders, small stones set in."""
    rng = rng_for("packed_earth")
    layer, row, height = strata(rng, 6, wobble=0.7)
    tone = np.array([2, 2, 1, 2, 2, 1])[layer % 6]
    tone = np.where(clods(rng, 0.3, 0.0, patches=False) == 1, np.maximum(tone - 1, 1), tone)
    seam = row == 0
    dashes = value_noise(rng, 8) > 0.45
    tone = np.where((row == 1) & dashes & (rng.random((SIZE, SIZE)) < 0.5), tone + 1, tone)
    tone = np.where(seam & dashes, 0, tone)
    tone = speckle(rng, tone, 0.02, 0.03)
    E, S = RAMPS["earth"], RAMPS["stone"]
    img = np.array([E[0], mix(E[1], E[2], 0.4), mix(E[2], E[3], 0.35), mix(E[3], E[4], 0.25), E[4]], np.uint8)[np.clip(tone, 0, 4)]
    key = {"h": mix(E[5], S[5], 0.5), "m": mix(E[4], S[3], 0.5), "d": E[1], "w": E[5], "k": E[0]}
    embed(img, rng, 3, key, [["hm", "dk"], ["h", "d"], ["hmd"]], min_gap=11)
    return img


# ---------------------------------------------------------------- clay

def clay():
    """Clay: smooth bedded layers in close warm tones, thin broken seams, a few drying cracks."""
    rng = rng_for("clay")
    layer, row, height = strata(rng, 5, wobble=1.6)
    tone = np.array([2, 1, 2, 3, 2, 2, 3, 1, 2, 2])[layer % 10]
    seam = row == 0
    tone = np.where((row == 1) & (rng.random((SIZE, SIZE)) < 0.5) & (tone < 3), tone + 1, tone)
    tone = np.where(seam & (rng.random((SIZE, SIZE)) < 0.55), np.maximum(tone - 2, 0), tone)
    for x in scatter_1d(rng, 3, 9):
        y0, length = int(rng.integers(0, SIZE)), int(rng.integers(3, 6))
        for d in range(length):
            tone[(y0 + d) % SIZE, (x + (d // 2) % 2) % SIZE] = 0
    tone = speckle(rng, tone, 0.012, 0.02)
    tone = boulders(np.clip(tone, 0, 4), rng, 2, 2, 3, min_gap=14, fills=(3,), soft=True)
    return tint_patches(tones("clay", 3, 0.6)[tone], rng)


# ---------------------------------------------------------------- ores and finds

def copper_ore():
    """Copper ore: brown host rock with bright copper nuggets."""
    rng = rng_for("copper_ore")
    tone, _ = rock(rng, 12, (1.0, 1.3), 0.2)
    img = tint_patches(tones5(0x33200f, 0x4a3222, 0x5e4330, 0x745640, 0x8c6a4c)[tone], rng)
    veins(img, rng, lump_key("copper", 4, (0x2e, 0x1e, 0x14, 255)))
    return img


def iron_ore():
    """Iron ore: rusty red rock with pale metallic flecks."""
    rng = rng_for("iron_ore")
    tone, _ = rock(rng, 12, (1.3, 1.0), 0.2)
    img = tones5(0x582a1c, 0x723a27, 0x84462f, 0x97563a, 0xae6a4a)[tone]
    I = RAMPS["iron"]
    key = {"h": I[5], "m": I[4], "d": I[3], "w": I[6], "k": (0x4a, 0x22, 0x18, 255)}
    flecks = [["hm", "md"], [".h.", "hmd", ".d."], ["hmd", ".dk"], ["h", "m"], ["hm"], [".hm", "hmd", "md."]]
    veins(img, rng, key, 4, 5, flecks)
    return img


def coal():
    """Coal seam: stone crowded with black, glinting lumps."""
    rng = rng_for("coal")
    tone, _ = rock(rng, 10, (1.0, 1.5), 0.3)
    img = tint_patches(STONE()[tone], rng)
    B, S = RAMPS["basalt"], RAMPS["stone"]
    key = {"h": B[3], "m": B[1], "d": B[0], "w": RAMPS["iron"][4], "k": S[1]}
    lumps = [
        [".hmm.", "hwmmd", "mmmdd", "mmddd", ".dddk"],
        [".hm.", "hmmd", "mmdd", ".ddk"],
        ["hmm.", "mwmd", "mmdd", ".ddk"],
        ["hm", "md"],
        [".hmmm.", "hmwmmd", "mmmddd", ".ddddk"],
    ]
    veins(img, rng, key, 5, 5, lumps)
    return img


def amber():
    """Amber: stone with honey-gold drops."""
    rng = rng_for("amber")
    tone, _ = rock(rng, 11, (1.0, 1.5), 0.35)
    img = STONE()[tone]
    drops = [[".hm.", "hwmd", "mmmd", ".ddk"], ["hm", "md"], [".h.", "hwd", "mmd", ".dk"], ["h", "d"]]
    veins(img, rng, lump_key("gold", 4, RAMPS["stone"][1]), 3, 4, drops)
    return img


def fossil():
    """Fossil bed: stone with a coiled shell, ribs and bone ends."""
    rng = rng_for("fossil")
    tone, _ = rock(rng, 11, (1.0, 1.5), 0.3)
    img = STONE()[tone]
    N, S = RAMPS["bone"], RAMPS["stone"]
    key = {"h": N[4], "m": N[3], "d": N[2], "s": N[1], "k": S[1]}
    bones = [
        [".hhhh.", "hmssmh", "hs.hms", "hsmmds", "hmsss.", ".dmmd."],        # coiled shell
        ["h.....", "mh....", ".mhhh.", "..dmmh", ".....d"],                 # rib
        ["hm.....mh", ".dmmmmmd.", "hd.....dm"],                           # long bone
        ["h.h.h", "mdmdm"],                                                 # vertebrae
        ["hm", "d."],
    ]
    embed(img, rng, 5, key, bones, min_gap=11)
    return img


def relic():
    """Relic wall: ruin masonry with a gold-inlaid tablet."""
    rng = rng_for("relic")
    img = bricks(rng, moss=0.0, block=(16, 8))
    G, R = RAMPS["gold"], RAMPS["ruin"]
    key = {"h": G[5], "m": G[4], "d": G[2], "k": R[0], "b": R[1]}
    glyphs = [
        ["kkkkkkkkk", "kmmmhmmmk", "kmkkkkkmk", "kmkhmkkmk", "kmkkdkkdk", "kmkkkkkdk", "kdmmdmddk", "kkkkkkkkk"],
        [".kkkkk.", "kmhkhmk", "kkmhmkk", "kmkdkmk", "kdkkkdk", ".kkkkk."],
    ]
    for oy in range(0, SIZE, 32):
        for ox in range(0, SIZE, 32):
            flip = (ox + oy) // 32 % 2
            for (x, y), rows in zip([(4, 3), (20, 18)], glyphs[::-1] if flip else glyphs):
                stamp(img, ox + x, oy + y, rows, key)
    return img


def tablet():
    """Ancient tablet (10.5): a gold-inlaid slab set into ruin masonry, brighter than a relic wall."""
    rng = rng_for("tablet")
    img = bricks(rng, moss=0.0, block=(16, 8))
    G, R = RAMPS["gold"], RAMPS["ruin"]
    key = {"w": (0xff, 0xf4, 0xc0, 255), "h": G[5], "m": G[4], "d": G[2], "k": R[0], "s": R[1]}
    slab = [
        "kkkkkkkkkkkk",
        "khhhhhhhhhmk",
        "khmmmmmmmmdk",
        "khmwhkkhmmdk",
        "khmkkmmkkmdk",
        "khmhkwwkhmdk",
        "khmkkmmkkmdk",
        "khmwhkkhwmdk",
        "khmmmmmmmmdk",
        "kmddddddddsk",
        "kkkkkkkkkkkk",
    ]
    for oy in range(0, SIZE, 32):
        for ox in range(0, SIZE, 32):
            stamp(img, ox + 10, oy + 10, slab, key)
    return img


# ---------------------------------------------------------------- deep rock

def basalt():
    """Basalt: near-black columns, taller than wide."""
    rng = rng_for("basalt")
    tone, _ = rock(rng, 14, (1.9, 1.0), 0.55, min_gap=4.0)
    return tint_patches(tones("basalt", 3, 0.75)[tone], rng, 0.06)


def bedrock():
    """Bedrock: the darkest, heaviest rock; big blocks, deep cracks. Cannot be dug."""
    rng = rng_for("bedrock")
    tone, _ = rock(rng, 7, (1.0, 1.0), 0.85, min_gap=8.0)
    return tint_patches(tones5(0x0c0b10, 0x1a191e, 0x2b2a30, 0x3a3940, 0x4a4954)[tone], rng, 0.06)


def quartzite():
    """Quartzite: pale lavender-grey, angular, with glints."""
    rng = rng_for("quartzite")
    tone, fracture = rock(rng, 18, (1.0, 1.0), 0.4, min_gap=4.5)
    tone = boulders(tone, rng, 2, 3, 4, min_gap=14)
    img = tint_patches(tones5(0x55526a, 0x737089, 0x8e8a9a, 0xa9a5b9, 0xc8c4da)[tone], rng, 0.05)
    glint = (rng.random((SIZE, SIZE)) > 0.988) & ~fracture
    img[glint] = (0xee, 0xea, 0xff, 255)
    return img


def glow_crystal():
    """Glow crystals: deep blue rock set with bright shards (the tile glows in the game)."""
    rng = rng_for("glow_crystal")
    tone, _ = rock(rng, 10, (1.0, 1.0), 0.4)
    C = RAMPS["crystal"]
    img = np.array([C[0], mix(C[0], C[1], 0.5), C[1], mix(C[1], C[2], 0.5), C[2]], np.uint8)[tone]
    key = {"w": C[6], "h": C[5], "m": C[4], "d": C[3], "g": C[2]}
    shards = [
        ["..w..", ".ghg.", "ghmdg", "ghmdg", "ghmdg", ".gmd.", "..d.."],
        ["...gw", "..ghm", ".ghmd", "ghmdg", "gmdg.", ".dg.."],
        [".w.", "hmd", "hmd", ".d."],
        ["gw...", "ghmg.", ".gmdg", "..gdg"],
        ["w", "m", "d"],
        [".gwg.", "ghhmg", "gmmdg", ".gdg."],
    ]
    big = [
        ["...w...", "..ghg..", ".ghhmg.", ".ghmdg.", "gghmdg.", ".ghmdg.", ".gmddg.", "..gdg..", "...g..."],
        ["....gw.", "...ghmg", "..ghmdg", ".ghmdg.", "ghmdg..", "gmdg...", ".gg...."],
    ]
    spots = embed(img, rng, 3, key, big, min_gap=12)
    embed(img, rng, 5, key, shards, min_gap=8, taken=spots)
    return img


def obsidian():
    """Obsidian: black-violet glass with a few sharp glancing lights."""
    rng = rng_for("obsidian")
    tone, _ = rock(rng, 8, (1.0, 1.0), 0.5, min_gap=7.0)
    V = RAMPS["void"]
    img = np.array([(0x0e, 0x0a, 0x16, 255), mix(V[0], V[1], 0.3), mix(V[0], V[1], 0.75), mix(V[1], V[2], 0.5), V[2]], np.uint8)[tone]
    key = {"w": V[5], "h": V[4], "m": V[3]}
    streaks = [["...w", "..h.", ".h..", "m..."], ["..h", ".h.", "m.."], ["w", "h"], [".....h", "...hw.", ".mh...", "m....."]]
    embed(img, rng, 5, key, streaks, min_gap=10)
    return img


def magma_rock():
    """Magma rock: dark crust split by glowing cracks."""
    rng = rng_for("magma_rock")
    owner, _ = voronoi(rng, 11, (1.0, 1.0), 6.0)
    n = owner.max() + 1
    tone = rng.choice([1, 2, 2, 3], n)[owner]
    below = (owner != np.roll(owner, -1, 0)) | (owner != np.roll(owner, -1, 1))
    crack = below & (rng.random(n) < 0.55)[owner]
    tone = speckle(rng, tone, 0.02, 0.03, keep=~crack & (tone == 2))
    M = RAMPS["magma"]
    img = tones5(0x1c0c0a, 0x2e1612, 0x42211a, 0x5a3024, 0x6e3e2e)[np.clip(tone, 0, 4)]
    heat = value_noise(rng, 4)
    img[crack & (heat <= 0.45)] = M[3]
    img[crack & (heat > 0.45)] = M[4]
    img[crack & (heat > 0.68)] = M[5]
    # A dull red rim next to the hottest cracks.
    hot = crack & (heat > 0.45)
    rim = (np.roll(hot, 1, 0) | np.roll(hot, 1, 1)) & ~crack
    img[rim] = M[2]
    return img


def meteorite():
    """Meteorite: dark violet iron, pitted, with glowing specks."""
    rng = rng_for("meteorite")
    V = RAMPS["void"]
    tone = np.full((SIZE, SIZE), 2)
    tone[value_noise(rng, 8) < 0.33] = 1
    tone = speckle(rng, tone, 0.02, 0.03)
    img = np.array([V[0], mix(V[0], V[1], 0.6), mix(V[1], V[2], 0.35), mix(V[1], V[2], 0.85), V[3]], np.uint8)[tone]
    key = {"k": V[0], "d": mix(V[0], V[1], 0.5), "h": V[3], "w": V[5], "g": V[4]}
    pits = [[".kk.", "kkdh", "kdhh", ".hh."], ["kk.", "kdh", ".hh"], ["kd", "dh"], [".kkk.", "kkddh", "kdddh", ".dhh."]]
    spots = embed(img, rng, 7, key, pits, min_gap=7)
    embed(img, rng, 6, key, [["w"], ["gw"], ["w", "g"]], min_gap=6, taken=spots)
    return img


# ---------------------------------------------------------------- fungal depths

def mycelium():
    """Mycelium: dark violet rot laced with pale threads."""
    rng = rng_for("mycelium")
    tone = clods(rng, 0.42, 0.32)
    F = RAMPS["fungus"]
    img = tint_patches(np.array([F[0], mix(F[0], F[1], 0.6), mix(F[1], F[2], 0.35), mix(F[1], F[2], 0.8), F[2]], np.uint8)[tone], rng, 0.08)
    thread, knot = F[4], F[5]
    for x, y in scatter(rng, 7, 8):
        for branch in range(2):
            p = np.array([x, y])
            step = np.array([rng.choice([-1, 1]), rng.choice([-1, 0, 1])])
            for i in range(int(rng.integers(5, 10))):
                img[p[1] % SIZE, p[0] % SIZE] = thread
                # Wander: usually keep going, often bend, never run straight for long.
                if rng.random() < 0.6:
                    step = np.array([step[0] if rng.random() < 0.7 else 0, rng.choice([-1, 0, 1])])
                    if not step.any():
                        step = np.array([rng.choice([-1, 1]), 0])
                p = p + step
        img[y % SIZE, x % SIZE] = knot
    return img


def giant_fungus():
    """Giant fungus flesh: soft violet, ribbed like gills, with glowing teal spores."""
    rng = rng_for("giant_fungus")
    tone = np.full((SIZE, SIZE), 2)
    drift = (value_noise(rng, 4)[:, 0] - 0.5) * 4
    for x0 in sorted(scatter_1d(rng, 8, 3)):
        for y in range(SIZE):
            x = (x0 + int(round(drift[y]))) % SIZE
            if rng.random() < 0.85:
                tone[y, x] = 1
                tone[y, (x + 1) % SIZE] = 3
    tone[(value_noise(rng, 4) < 0.3) & (tone == 2)] = 1
    tone = speckle(rng, tone, 0.02, 0.02)
    img = tones("fungus", 3, 0.5)[np.clip(tone, 0, 4)]
    P, F = RAMPS["spore"], RAMPS["fungus"]
    key = {"w": P[5], "h": P[4], "m": P[3], "d": P[2], "k": F[1]}
    spores = [[".h.", "hwm", ".md"], ["hw", "md"], ["w"], [".hh.", "hwwm", "hwmm", ".md."], ["h", "m"]]
    embed(img, rng, 8, key, spores, min_gap=7)
    return img


# ---------------------------------------------------------------- ruins, ash

def bricks(rng, moss=0.2, block=(8, 4)):
    """Ruin masonry in running bond: worn blocks, dark joints, a lit top-left edge, moss in some joints."""
    W, H = block
    R, M = RAMPS["ruin"], RAMPS["moss"]
    pal = np.array([R[0], R[1], mix(R[1], R[2], 0.6), mix(R[2], R[3], 0.4), mix(R[2], R[3], 0.9), R[3], R[4]], np.uint8)
    img = np.zeros((SIZE, SIZE, 4), np.uint8)
    for row in range(SIZE // H):
        shift = (row % 2) * (W // 2)
        for b in range(SIZE // W):
            face = int(rng.choice([2, 3, 3, 3, 4]))
            worn = rng.random() < 0.3
            for y in range(H):
                for x in range(W):
                    t = face
                    if y == 0 or x == 0:
                        t = 0 if rng.random() < 0.85 else 1                    # joint
                    elif y == 1 or x == 1:
                        t = face + 1                                            # lit edge
                    elif y == H - 1 or x == W - 1:
                        t = face - 1
                    elif rng.random() < 0.05:
                        t = face - 1
                    if worn and (x, y) in ((1, 1), (W - 1, H - 1)):
                        t = 1                                                   # chipped corner
                    px, py = (b * W + x + shift) % SIZE, row * H + y
                    img[py, px] = pal[max(0, min(t, 6))]
                    if (y == 0 or x == 0) and rng.random() < moss:
                        img[py, px] = M[int(rng.choice([1, 2, 2, 3]))]
    return img


def ruin_brick():
    """Ancient ruins: worn sandstone blocks with moss in the joints."""
    return bricks(rng_for("ruin_brick"))


def ash():
    """Ash: soft grey powder with charcoal crumbs."""
    rng = rng_for("ash")
    tone = np.full((SIZE, SIZE), 2)
    tone[value_noise(rng, 8) < 0.36] = 1
    tone[value_noise(rng, 8) > 0.68] = 3
    tone = speckle(rng, tone, 0.04, 0.04)
    img = tones5(0x2a2726, 0x4a4644, 0x5a5654, 0x6b6663, 0x827c78)[tone]
    key = {"k": (0x1e, 0x1b, 0x1a, 255), "d": (0x2a, 0x27, 0x26, 255), "h": (0x96, 0x90, 0x8a, 255)}
    embed(img, rng, 6, key, [["kd"], ["k", "d"], ["h"], ["dk", "k."], ["h"], ["k"]], min_gap=8)
    return img


# Every material with a tile, by material id (dirt and stone above).
TILES = {
    "dirt": dirt, "grass": grass, "pebbles": pebbles, "grub_nest": grub_nest, "sand": sand, "root": root,
    "packed_earth": packed_earth, "clay": clay, "copper_ore": copper_ore, "iron_ore": iron_ore, "coal": coal,
    "amber": amber, "fossil": fossil, "stone": stone, "basalt": basalt, "bedrock": bedrock, "quartzite": quartzite,
    "glow_crystal": glow_crystal, "obsidian": obsidian, "magma_rock": magma_rock, "meteorite": meteorite,
    "mycelium": mycelium, "giant_fungus": giant_fungus, "ruin_brick": ruin_brick, "relic": relic, "ash": ash,
    "tablet": tablet,
}
