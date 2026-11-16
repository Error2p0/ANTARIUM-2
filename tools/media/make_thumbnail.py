"""Makes a trailer's custom thumbnail (1920x1080 PNG): one frame of the trailer itself, unchanged.

Usage (from the project root, after a capture run):
    python tools/media/make_thumbnail.py [--cut gameplay|general] [--frame g_colony/00090] [--out <file>.png]

Steam only accepts a frame from the video itself (1920x1080 .jpg or .png) and advises against text, so this takes a
captured clip frame as it is (no logo, no caption) and checks that the trailer's edit really shows it: the frame
must lie inside one of the cut's clip segments, outside the crossfades at its ends. Needs Pillow.
"""
import argparse
import os
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_trailer import CUTS, FADE, FPS, SIZE  # noqa: E402

CLIPS = os.path.join("Captures", "clips")
# The colony in cross-section (meadow on top, the busy nest below) for the gameplay cut; the storm over the hills and the nest for the general one.
DEFAULT_FRAME = {"gameplay": "g_colony/00090", "general": "s03_storm/00120"}
OUTPUTS = {"gameplay": "antarium2_gameplay_trailer_thumbnail.png", "general": "antarium2_trailer_thumbnail.png"}


def shown_in_cut(cut, clip, index):
    """True when frame `index` of `clip` plays at full strength somewhere in the cut."""
    fade = int(round(FADE * FPS))
    for item in CUTS[cut]:
        if item[0] != "clip" or item[1] != clip:
            continue
        first = int(round(item[2] * FPS))
        count = int(round(item[3] * FPS))
        if first + fade <= index < first + count - fade:
            return True
    return False


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--cut", choices=sorted(CUTS), default="gameplay")
    parser.add_argument("--frame", help="clip/frame, e.g. g_colony/00090 (default depends on the cut)")
    parser.add_argument("--clips", default=CLIPS)
    parser.add_argument("--out")
    args = parser.parse_args()
    frame = args.frame or DEFAULT_FRAME[args.cut]
    out = args.out or os.path.join("Captures", "trailer", OUTPUTS[args.cut])

    clip, number = frame.split("/")
    if not shown_in_cut(args.cut, clip, int(number)):
        sys.exit(f"{frame} is not shown in the {args.cut} trailer (see CUTS in make_trailer.py); Steam needs a frame "
                 f"from the video itself")
    image = Image.open(os.path.join(args.clips, clip, number + ".png")).convert("RGB")
    if image.size != SIZE:
        sys.exit(f"{frame} is {image.size[0]}x{image.size[1]}, Steam needs {SIZE[0]}x{SIZE[1]}")

    os.makedirs(os.path.dirname(out) or ".", exist_ok=True)
    image.save(out)
    print(f"{out}: {frame} of the {args.cut} trailer, {SIZE[0]}x{SIZE[1]}")


if __name__ == "__main__":
    main()
