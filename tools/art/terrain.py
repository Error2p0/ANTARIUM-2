"""Terrain tiles: one tileable 32 x 32 texture per material (Art/Terrain/<material id>.png).

The terrain shader adds edges, light bands, fluids and fog on top and reuses the tile (flattened and darkened) for
the background wall, so tiles stay calm: three or four tones of one ramp, soft relief lit from the top left, and
only a few small features, because every feature repeats every four cells.
"""
import zlib

import numpy as np

from palette import RAMPS

SIZE = 32


def rng_for(name):
    return np.random.default_rng(zlib.crc32(name.encode()))


def value_noise(rng, cells):
    """Tileable value noise: a cells x cells lattice of random values, smoothly interpolated up to SIZE."""
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
    """Tileable Voronoi: per pixel the nearest seed and the gap between the nearest and second-nearest distance."""
    seeds = []
    tries = 0
    while len(seeds) < count and tries < 4000:
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
    """Random tile positions that keep a wrap-around distance from each other."""
    points = list(taken or [])
    first = len(points)
    tries = 0
    while len(points) - first < count and tries < 2000:
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
    """Topsoil: crumbly warm earth. Small clods (dark gaps with a lit rim on their lower right), a few stones."""
    rng = rng_for("dirt")
    height = value_noise(rng, 8) * 0.55 + value_noise(rng, 16) * 0.45
    gap = height < 0.4
    tone = np.full((SIZE, SIZE), 2)
    tone[gap] = 1
    tone[(value_noise(rng, 4) < 0.3) & gap] = 0
    lit = np.roll(gap, (1, 1), (0, 1)) & ~gap
    tone[lit & (rng.random((SIZE, SIZE)) < 0.75)] = 3
    grain = rng.random((SIZE, SIZE))
    tone[(grain > 0.985) & (tone == 2)] = 3
    tone[(grain < 0.02) & (tone == 2)] = 1
    img = tones("earth", 3)[tone]
    E, S = RAMPS["earth"], RAMPS["stone"]
    key = {"h": E[5], "m": E[4], "s": mix(E[5], S[5], 0.5), "g": mix(E[4], S[3], 0.5), "k": E[1]}
    spots = scatter(rng, 2, 12)
    for (x, y), rows in zip(spots, [[".s.", "sgk", ".k."], ["s", "k"]]):
        stamp(img, x, y, rows, key)
    for (x, y), rows in zip(scatter(rng, 2, 9, spots), [["h..", ".m.", "..m"], ["..m", ".h."]]):
        stamp(img, x, y, rows, key)
    return img


def strata(rng, layers, wobble=1.6):
    """Wavy horizontal layers that tile: per pixel the layer index, the row inside the layer and the layer height."""
    edges = np.sort(rng.choice(np.arange(0, SIZE, 2), layers, replace=False))
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


def facets(rng, count, stretch, crack_share=0.45, min_gap=5.0):
    """Faceted rock: Voronoi plates, each one flat tone (as if tilted to or from the light), lit on the edges that
    face the top left; some plates get a dark fracture along their lower right edge. Tones 0..4 around 2."""
    owner, _ = voronoi(rng, count, stretch, min_gap)
    n = owner.max() + 1
    tone = rng.choice([1, 2, 2, 2, 3], n)[owner]
    cracked = (rng.random(n) < crack_share)[owner]
    other_below = (owner != np.roll(owner, -1, 0)) | (owner != np.roll(owner, -1, 1))
    other_above = (owner != np.roll(owner, 1, 0)) | (owner != np.roll(owner, 1, 1))
    fracture = other_below & cracked
    rim = other_above & ~fracture & (np.roll(fracture, 1, 0) | np.roll(fracture, 1, 1))
    tone = np.where(rim, tone + 1, tone)
    tone = np.where(fracture, 0, tone)
    return np.clip(tone, 0, 4), fracture


def stone():
    """Stone strata: cool grey rock in flat, wide facets with a few dark fractures."""
    rng = rng_for("stone")
    tone, fracture = facets(rng, 13, (1.0, 1.7))
    grain = rng.random((SIZE, SIZE))
    tone = np.where((grain > 0.97) & ~fracture & (tone < 4), tone + 1, tone)
    tone = np.where((grain < 0.03) & ~fracture & (tone > 1), tone - 1, tone)
    return tones("stone", 3, 0.5)[tone]


def scatter_1d(rng, count, min_gap):
    xs = []
    tries = 0
    while len(xs) < count and tries < 500:
        tries += 1
        x = int(rng.integers(0, SIZE))
        if all(min(abs(x - q), SIZE - abs(x - q)) >= min_gap for q in xs):
            xs.append(x)
    return xs
