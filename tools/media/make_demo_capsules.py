"""Makes the demo app's store and library art: every capsule of the main page with a DEMO plate under the logo.

Usage (from the project root):
    python tools/media/make_demo_capsules.py [--bg <scene>.jpg] [--logo <logo>.png] [--out <dir>] [--preview]

Valve asks a demo with its own store page for capsules and library art that "clearly indicate that the app is a
demo". The main page's art is a scene (meadow over lit tunnels) with the game logo on top; this rebuilds each size
from the scene without the logo (`steam/store/demo/art/background.jpg`, the main page's 1438x810 page background)
and the logo (`Assets/_Antarium/Icon/shortcut.png`), and adds a plate in the logo's own style (slate metal, gold
bolts, the logo's yellow to orange letters) reading DEMO in the game's pixel font. The plate is drawn in art pixels
and scaled by a whole number, so it stays crisp pixel art at every size.

Writes to Captures/demo_store/ (git-ignored): header_capsule.png 920x430, small_capsule.png 462x174,
main_capsule.png 1232x706, vertical_capsule.png 748x896, library_capsule.png 600x900, library_header.png 920x430,
library_logo.png (transparent, 720 tall), screenshots/ (the store stills that show only demo content, renumbered in
upload order: DEMO_SCREENSHOTS) and with --preview a contact sheet. The library hero takes no text, so the
demo reuses the main game's. Needs Pillow.
"""
import argparse
import os
import shutil

from PIL import Image, ImageDraw, ImageFont

FONT = os.path.join("Assets", "_Antarium", "UI", "Fonts", "Silkscreen-Regular.ttf")
LOGO = os.path.join("Assets", "_Antarium", "Icon", "shortcut.png")
BACKGROUND = os.path.join("steam", "store", "demo", "art", "background.jpg")
OUT = os.path.join("Captures", "demo_store")

# The logo's colours (sampled from shortcut.png): its slate plate, the gold bolts, the letters' yellow to orange.
OUTLINE = (20, 14, 10)
SLATE_LIGHT = (120, 124, 155)
SLATE = (88, 97, 125)
SLATE_DARK = (51, 58, 79)
SLATE_DEEP = (38, 44, 63)
BOLT = (252, 190, 40)
BOLT_LIGHT = (255, 240, 150)
BOLT_DARK = (170, 90, 10)
LETTER_TOP = (255, 236, 90)
LETTER_MID = (252, 168, 4)
LETTER_BOTTOM = (249, 114, 0)
LETTER_EDGE = (60, 24, 4)

# Per capsule: size, the scene's focus (fraction of the scene that lands at the capsule's centre), the logo's visible
# width as a share of the capsule width, the logo's top as a share of the height, the plate's width as a share of the
# logo's, and where the plate goes ("below" the logo, or "right" of it for the short small capsule).
CAPSULES = {
    "header_capsule": dict(size=(920, 430), focus=(0.5, 0.36), logo_w=0.33, logo_top=0.04, plate=0.62, place="below"),
    "small_capsule": dict(size=(462, 174), focus=(0.5, 0.40), logo_w=0.44, logo_top=0.03, plate=0.80, place="right"),
    "main_capsule": dict(size=(1232, 706), focus=(0.5, 0.42), logo_w=0.34, logo_top=0.03, plate=0.62, place="below"),
    "vertical_capsule": dict(size=(748, 896), focus=(0.47, 0.5), logo_w=0.78, logo_top=0.05, plate=0.62, place="below"),
    "library_capsule": dict(size=(600, 900), focus=(0.47, 0.5), logo_w=0.82, logo_top=0.06, plate=0.62, place="below"),
    "library_header": dict(size=(920, 430), focus=(0.5, 0.36), logo_w=0.33, logo_top=0.04, plate=0.62, place="below"),
}
# The store stills (Captures/stills) that show only what the demo has, in upload order: the meadow, nest, factory,
# Elder Tree, threats and weather; none from the crystal caverns down, of fire ants, the Engine, the Codex, the flight
# or the genetic tree, nor the research / castes / queen screens (they list full game nodes as bought).
DEMO_SCREENSHOTS = [
    "01_surface_and_nest", "02_colony", "03_factory", "06_elder_tree", "12_raid", "14_possession",
    "13_thunderstorm", "04_factory_flow_overlay", "07_starfall_night", "15_giant_worm", "26_ore_room",
    "27_queen_and_storage", "17_waterworks", "18_ore_overlay", "25_sunset", "28_nest_wide",
]
STILLS = os.path.join("Captures", "stills")
LIBRARY_LOGO_HEIGHT = 720  # Valve: 1280 wide and/or 720 tall, transparent PNG
OVERLAP = 0.05  # the plate tucks this share of its height under the logo's "2" plate


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def letter_colour(row, rows):
    t = row / max(1, rows - 1)
    return lerp(LETTER_TOP, LETTER_MID, t * 2) if t < 0.5 else lerp(LETTER_MID, LETTER_BOTTOM, (t - 0.5) * 2)


def draw_plate(text="DEMO"):
    """The plate in art pixels (RGBA, one pixel = one art pixel), to be scaled up by a whole number."""
    font = ImageFont.truetype(FONT, 16)
    left, top, right, bottom = font.getbbox(text)
    tw, th = right - left, bottom - top

    # Letters: a mask, filled row by row with the gradient, a 1-pixel dark edge round them and a drop shadow.
    pad = 2
    mask = Image.new("L", (tw + pad * 2, th + pad * 2), 0)
    ImageDraw.Draw(mask).text((pad - left, pad - top), text, font=font, fill=255)
    mask = mask.point(lambda v: 255 if v > 127 else 0)
    letters = Image.new("RGBA", mask.size, (0, 0, 0, 0))
    lp, mp = letters.load(), mask.load()
    for y in range(mask.size[1]):
        colour = letter_colour(y - pad, th)
        for x in range(mask.size[0]):
            if mp[x, y]:
                lp[x, y] = colour + (255,)
    edge = Image.new("RGBA", mask.size, (0, 0, 0, 0))
    ep = edge.load()
    for y in range(mask.size[1]):
        for x in range(mask.size[0]):
            if mp[x, y]:
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, 1), (1, -1), (-1, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < mask.size[0] and 0 <= ny < mask.size[1] and mp[nx, ny]:
                    ep[x, y] = LETTER_EDGE + (255,)
                    break

    # Plate: outline, slate fill with a light top edge and a dark bottom, a gold bolt at each end.
    margin_x, margin_y, bolt_room = 5, 4, 6
    w = tw + 2 * (margin_x + bolt_room)
    h = th + 2 * margin_y + 1
    plate = Image.new("RGBA", (w + 2, h + 3), (0, 0, 0, 0))
    d = ImageDraw.Draw(plate)
    d.rounded_rectangle((0, 2, w + 1, h + 2), radius=3, fill=OUTLINE + (160,))  # soft shadow under the plate
    d.rounded_rectangle((0, 0, w + 1, h + 1), radius=3, fill=OUTLINE + (255,))
    d.rounded_rectangle((1, 1, w, h), radius=2, fill=SLATE + (255,))
    d.line((3, 1, w - 2, 1), fill=SLATE_LIGHT + (255,))
    d.line((2, 2, w - 1, 2), fill=SLATE_LIGHT + (255,))
    d.line((3, h, w - 2, h), fill=SLATE_DEEP + (255,))
    d.line((2, h - 1, w - 1, h - 1), fill=SLATE_DARK + (255,))
    # Inset panel behind the letters.
    ix0, iy0 = bolt_room + 2, 4
    ix1, iy1 = w - bolt_room - 1, h - 3
    d.rectangle((ix0, iy0, ix1, iy1), fill=SLATE_DARK + (255,))
    d.line((ix0, iy1, ix1, iy1), fill=SLATE_LIGHT + (255,))
    d.line((ix1, iy0, ix1, iy1), fill=SLATE_LIGHT + (255,))
    d.line((ix0, iy0, ix1, iy0), fill=SLATE_DEEP + (255,))
    d.line((ix0, iy0, ix0, iy1), fill=SLATE_DEEP + (255,))
    for bx in (4, w - 5):
        by = (h + 1) // 2 - 1
        d.rectangle((bx - 1, by - 1, bx + 2, by + 2), fill=OUTLINE + (255,))
        d.rectangle((bx, by, bx + 1, by + 1), fill=BOLT + (255,))
        d.point((bx, by), fill=BOLT_LIGHT + (255,))
        d.point((bx + 1, by + 1), fill=BOLT_DARK + (255,))

    ox = (w + 2 - mask.size[0]) // 2
    oy = (h + 2 - mask.size[1]) // 2
    shadow = Image.new("RGBA", mask.size, OUTLINE + (200,))
    plate.paste(shadow, (ox, oy + 1), mask)
    plate.alpha_composite(edge, (ox, oy))
    plate.alpha_composite(letters, (ox, oy))
    return plate


def scaled_plate(plate, width):
    """The plate scaled by the whole number that comes nearest to `width` pixels (at least 1)."""
    k = max(1, round(width / plate.size[0]))
    return plate.resize((plate.size[0] * k, plate.size[1] * k), Image.NEAREST)


def load_logo(path):
    logo = Image.open(path).convert("RGBA")
    alpha = logo.split()[3].point(lambda v: 255 if v >= 248 else v)  # the source is 1% see-through; make it solid
    logo.putalpha(alpha)
    return logo.crop(alpha.point(lambda v: 255 if v > 128 else 0).getbbox())


def cover(scene, size, focus):
    """The scene scaled to cover `size`, cropped round the focus point (fractions of the scene)."""
    w, h = size
    s = max(w / scene.size[0], h / scene.size[1])
    sw, sh = round(scene.size[0] * s), round(scene.size[1] * s)
    big = scene.resize((sw, sh), Image.LANCZOS)
    x = min(max(0, round(focus[0] * sw - w / 2)), sw - w)
    y = min(max(0, round(focus[1] * sh - h / 2)), sh - h)
    return big.crop((x, y, x + w, y + h))


def logo_with_plate(logo, plate_art, logo_width, plate_share, place):
    """Logo and plate on one transparent layer: the plate centred under the logo, or beside it."""
    s = logo_width / logo.size[0]
    lw, lh = round(logo.size[0] * s), round(logo.size[1] * s)
    logo_s = logo.resize((lw, lh), Image.LANCZOS)
    if place == "below":
        plate = scaled_plate(plate_art, lw * plate_share)
        overlap = round(plate.size[1] * OVERLAP)
        w = max(lw, plate.size[0])
        layer = Image.new("RGBA", (w, lh + plate.size[1] - overlap), (0, 0, 0, 0))
        layer.alpha_composite(logo_s, ((w - lw) // 2, 0))
        layer.alpha_composite(plate, ((w - plate.size[0]) // 2, lh - overlap))
    else:
        plate = scaled_plate(plate_art, lw * plate_share)
        gap = round(lw * 0.06)
        h = max(lh, plate.size[1])
        layer = Image.new("RGBA", (lw + gap + plate.size[0], h), (0, 0, 0, 0))
        layer.alpha_composite(logo_s, (0, (h - lh) // 2))
        layer.alpha_composite(plate, (lw + gap, (h - plate.size[1]) // 2 + round(lh * 0.08)))
    return layer


def make_capsule(scene, logo, plate_art, spec):
    w, h = spec["size"]
    image = cover(scene, (w, h), spec["focus"]).convert("RGBA")
    layer = logo_with_plate(logo, plate_art, w * spec["logo_w"], spec["plate"], spec["place"])
    if layer.size[0] > w * 0.96 or layer.size[1] > h * 0.94:  # keep the whole lockup inside the capsule
        s = min(w * 0.96 / layer.size[0], h * 0.94 / layer.size[1])
        layer = logo_with_plate(logo, plate_art, w * spec["logo_w"] * s, spec["plate"], spec["place"])
    top = round(h * spec["logo_top"])
    top = min(top, h - layer.size[1] - round(h * 0.03))
    image.alpha_composite(layer, ((w - layer.size[0]) // 2, max(0, top)))
    return image.convert("RGB")


def make_library_logo(logo, plate_art):
    layer = logo_with_plate(logo, plate_art, logo.size[0], 0.62, "below")
    s = LIBRARY_LOGO_HEIGHT / layer.size[1]
    # Rebuild at the final size instead of scaling the plate's pixels by a fraction.
    return logo_with_plate(logo, plate_art, logo.size[0] * s, 0.62, "below")


def contact_sheet(paths, out):
    images = [Image.open(p).convert("RGBA") for p in paths]
    width = 1900
    rows, row, x, row_h = [], [], 0, 0
    for im in images:
        s = min(1.0, 560 / im.size[1])
        im = im.resize((round(im.size[0] * s), round(im.size[1] * s)), Image.LANCZOS)
        if x + im.size[0] > width and row:
            rows.append((row, row_h))
            row, x, row_h = [], 0, 0
        row.append(im)
        x += im.size[0] + 20
        row_h = max(row_h, im.size[1])
    rows.append((row, row_h))
    sheet = Image.new("RGBA", (width, sum(r[1] + 20 for r in rows) + 20), (40, 40, 48, 255))
    y = 20
    for r, rh in rows:
        x = 20
        for im in r:
            sheet.alpha_composite(im, (x, y))
            x += im.size[0] + 20
        y += rh + 20
    sheet.convert("RGB").save(out)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--bg", default=BACKGROUND, help="the scene without the logo (main page background)")
    parser.add_argument("--logo", default=LOGO, help="the game logo, transparent PNG")
    parser.add_argument("--out", default=OUT)
    parser.add_argument("--text", default="DEMO")
    parser.add_argument("--preview", action="store_true", help="also write contact_sheet.jpg")
    args = parser.parse_args()

    scene = Image.open(args.bg).convert("RGB")
    logo = load_logo(args.logo)
    plate_art = draw_plate(args.text)
    os.makedirs(args.out, exist_ok=True)

    written = []
    for name, spec in CAPSULES.items():
        path = os.path.join(args.out, name + ".png")
        make_capsule(scene, logo, plate_art, spec).save(path)
        written.append(path)
        print(f"{path}: {spec['size'][0]}x{spec['size'][1]}")
    path = os.path.join(args.out, "library_logo.png")
    lib = make_library_logo(logo, plate_art)
    lib.save(path)
    written.append(path)
    print(f"{path}: {lib.size[0]}x{lib.size[1]} (transparent)")
    path = os.path.join(args.out, "plate_art.png")
    plate_art.save(path)
    print(f"{path}: {plate_art.size[0]}x{plate_art.size[1]} (the plate in art pixels)")
    shots = os.path.join(args.out, "screenshots")
    os.makedirs(shots, exist_ok=True)
    copied = 0
    for i, name in enumerate(DEMO_SCREENSHOTS, 1):
        src = os.path.join(STILLS, name + ".png")
        if os.path.exists(src):
            shutil.copyfile(src, os.path.join(shots, f"{i:02d}_{name[3:]}.png"))
            copied += 1
        else:
            print(f"missing still {src} (run the store shots first)")
    print(f"{shots}: {copied} of {len(DEMO_SCREENSHOTS)} demo screenshots, in upload order")
    if args.preview:
        sheet = os.path.join(args.out, "contact_sheet.jpg")
        contact_sheet(written, sheet)
        print(sheet)


if __name__ == "__main__":
    main()
