"""Makes the trailer thumbnail (1920x1080 PNG) from captured frames and the game logo.

Usage (from the project root, after a capture run):
    python tools/media/make_thumbnail.py [--deep t07_crystal/00150] [--out Captures/trailer/antarium2_trailer_thumbnail.png]

A cross-section like the game: the sunlit nest (sky, grass, the factory floor) on top, fading into a deep layer
below, with the game logo large in the middle and a small caption under it. Frames are used at 1:1, never scaled,
so the pixel art stays crisp. Needs Pillow.
"""
import argparse
import os

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

SIZE = (1920, 1080)
CLIPS = os.path.join("Captures", "clips")
ICONS = os.path.join("Assets", "_Antarium", "Icon")
FONT = os.path.join("Assets", "_Antarium", "UI", "Fonts", "Silkscreen-Regular.ttf")
AMBER = (247, 190, 110)


def frame(name):
    return Image.open(os.path.join(CLIPS, name + ".png")).convert("RGB")


def vertical_mask(height, start, end):
    """0 above start, 255 below end, a smooth ramp between."""
    mask = Image.new("L", (1, height))
    for y in range(height):
        t = min(1.0, max(0.0, (y - start) / max(1, end - start)))
        mask.putpixel((0, y), int(255 * t * t * (3 - 2 * t)))
    return mask.resize((SIZE[0], height))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--top", default="t05_factory/00300", help="clip/frame for the nest (sky at the top)")
    parser.add_argument("--top-offset", type=int, default=60, help="frame row shown at the top of the image")
    parser.add_argument("--deep", default="t07_crystal/00150", help="clip/frame for the deep layer")
    parser.add_argument("--deep-offset", type=int, default=-470, help="frame row at the top of the image (negative = pushed down)")
    parser.add_argument("--seam", type=int, default=660, help="where the nest fades into the deep layer")
    parser.add_argument("--logo", default=os.path.join(ICONS, "shortcut.png"))
    parser.add_argument("--caption", default="GAMEPLAY TRAILER")
    parser.add_argument("--out", default=os.path.join("Captures", "trailer", "antarium2_trailer_thumbnail.png"))
    args = parser.parse_args()

    top = frame(args.top).crop((0, args.top_offset, SIZE[0], args.top_offset + SIZE[1]))
    deep = frame(args.deep).crop((0, args.deep_offset, SIZE[0], args.deep_offset + SIZE[1]))
    image = Image.composite(deep, top, vertical_mask(SIZE[1], args.seam - 90, args.seam + 90))

    # Darken the middle a little so the logo stands out; keep the edges bright.
    shade = Image.new("L", SIZE, 0)
    ImageDraw.Draw(shade).ellipse((SIZE[0] * 0.18, SIZE[1] * 0.02, SIZE[0] * 0.82, SIZE[1] * 1.0), fill=120)
    shade = shade.filter(ImageFilter.GaussianBlur(140))
    image = Image.composite(Image.new("RGB", SIZE, (0, 0, 0)), image, shade)

    # The logo with a soft dark halo.
    logo = Image.open(args.logo).convert("RGBA")
    logo = logo.crop(logo.split()[-1].getbbox())
    height = 760
    logo = logo.resize((int(logo.width * height / logo.height), height), Image.LANCZOS)
    x, y = (SIZE[0] - logo.width) // 2, 40
    halo = Image.new("L", SIZE, 0)
    halo.paste(logo.split()[-1], (x, y))
    halo = halo.filter(ImageFilter.MaxFilter(15)).filter(ImageFilter.GaussianBlur(28))
    halo = ImageChops.multiply(halo, Image.new("L", SIZE, 200))
    image = Image.composite(Image.new("RGB", SIZE, (0, 0, 0)), image, halo)
    image.paste(logo, (x, y), logo)

    if args.caption:
        d = ImageDraw.Draw(image)
        f = ImageFont.truetype(FONT, 54)
        w = d.textlength(args.caption, font=f)
        tx, ty = (SIZE[0] - w) // 2, y + height + 40
        for dx, dy in ((-4, 0), (4, 0), (0, -4), (0, 4), (-3, -3), (3, 3), (-3, 3), (3, -3)):
            d.text((tx + dx, ty + dy), args.caption, font=f, fill=(0, 0, 0))
        d.text((tx, ty), args.caption, font=f, fill=AMBER)

    os.makedirs(os.path.dirname(args.out) or ".", exist_ok=True)
    image.save(args.out)
    print(f"{args.out}: {SIZE[0]}x{SIZE[1]}")


if __name__ == "__main__":
    main()
