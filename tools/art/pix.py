"""Small pixel-art helpers: ASCII pixel maps -> RGBA arrays, strips of frames, PNG output, contact sheets."""
import os

import numpy as np
from PIL import Image, ImageDraw

from palette import CLEAR, RAMPS

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))
ART = os.path.join(ROOT, "Assets", "_Antarium", "Art")
PREVIEW = os.path.join(ROOT, "Captures", "art")


def legend(**ramps):
    """Legend from ramps: legend(c="chitin") maps 'c0'..'c6' ... used through `keys` below for one-char maps."""
    return ramps


def keys(spec):
    """One-char legend from "A=chitin:3 B=chitin:4 x=#1a0e0c" (ramp:index or a hex colour)."""
    out = {".": CLEAR, " ": CLEAR}
    for part in spec.split():
        ch, value = part.split("=", 1)
        if value.startswith("#"):
            v = int(value[1:], 16)
            out[ch] = ((v >> 16) & 255, (v >> 8) & 255, v & 255, 255)
        else:
            name, index = value.split(":")
            out[ch] = RAMPS[name][int(index)]
    return out


def sprite(rows, key, width=None):
    """ASCII rows (first = top) to an RGBA array. Every row must have the same length."""
    w = width or len(rows[0])
    img = np.zeros((len(rows), w, 4), np.uint8)
    for y, row in enumerate(rows):
        if len(row) != w:
            raise ValueError(f"row {y} is {len(row)} wide, expected {w}: '{row}'")
        for x, ch in enumerate(row):
            if ch not in key:
                raise ValueError(f"unknown key '{ch}' in row {y}: '{row}'")
            img[y, x] = key[ch]
    return img


def blank(w, h, color=CLEAR):
    img = np.zeros((h, w, 4), np.uint8)
    img[:, :] = color
    return img


def strip(frames):
    """Frames side by side (the layout of every animated sprite PNG)."""
    return np.concatenate(frames, axis=1)


def paste(dst, src, x, y):
    """Alpha-keyed paste (source pixels with alpha 0 are skipped)."""
    h, w = src.shape[:2]
    region = dst[y:y + h, x:x + w]
    mask = src[:, :, 3] > 0
    region[mask] = src[mask]


def save(img, relative):
    path = os.path.join(ART, relative)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    Image.fromarray(img, "RGBA").save(path)
    return path


def upscale(img, scale):
    return np.repeat(np.repeat(img, scale, axis=0), scale, axis=1)


def sheet(items, name, scale=8, bg=(30, 21, 15), columns=4, pad=6, label=True):
    """Contact sheet for review: items = [(label, rgba array)], each over the background colour."""
    cells = []
    for text, img in items:
        big = upscale(img, scale)
        h, w = big.shape[:2]
        cell = Image.new("RGB", (w + pad * 2, h + pad * 2 + (12 if label else 0)), bg)
        cell.paste(Image.fromarray(big, "RGBA"), (pad, pad), Image.fromarray(big, "RGBA"))
        if label:
            ImageDraw.Draw(cell).text((pad, h + pad + 1), text, fill=(200, 190, 170))
        cells.append(cell)
    rows = [cells[i:i + columns] for i in range(0, len(cells), columns)]
    width = max(sum(c.width for c in row) for row in rows)
    height = sum(max(c.height for c in row) for row in rows)
    out = Image.new("RGB", (width, height), bg)
    y = 0
    for row in rows:
        x = 0
        for c in row:
            out.paste(c, (x, y))
            x += c.width
        y += max(c.height for c in row)
    os.makedirs(PREVIEW, exist_ok=True)
    path = os.path.join(PREVIEW, name + ".png")
    out.save(path)
    return path
