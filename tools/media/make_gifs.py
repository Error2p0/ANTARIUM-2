"""Cuts the store page GIFs out of the captured clips.

Usage (from the project root, after Antarium -> Capture -> Run All Shots):
    python tools/media/make_gifs.py [--out Captures/gifs] [--only antarium_dig,antarium_raid]

Each GIF is one or more segments of 1080p60 clips in Captures/clips: the centre 1280x720 is cut out and halved with
nearest-neighbour scaling (clips that feed a GIF use an even zoom, so art pixels stay crisp), every 3rd frame is kept
(15 fps) and ffmpeg builds one palette per GIF without dithering (the game's own dither stays exact). Needs Pillow
and imageio-ffmpeg (pip install pillow imageio-ffmpeg).
"""
import argparse
import os
import shutil
import subprocess
import sys
import tempfile

from PIL import Image
import imageio_ffmpeg

SOURCE_FPS = 60
GIF_FPS = 15
CROP = (1280, 720)          # centre crop of the 1920x1080 frame, then halved
MAX_BYTES = 3 * 1024 * 1024  # Steam has no hard limit, but heavy GIFs slow the page down

# name: [(clip, start seconds, seconds), ...]
GIFS = {
    "antarium_colony": [("g_colony", 0.0, 4.5)],
    "antarium_dig": [("t03_dig", 0.3, 5.0)],
    "antarium_factory": [("g_factory", 0.0, 4.5)],
    "antarium_depths": [("g_crystal", 0.0, 2.5), ("g_magma", 0.0, 2.5)],
    "antarium_raid": [("t10_raid", 1.5, 4.5)],
    "antarium_possess": [("t11_possess", 0.5, 4.0)],
    "antarium_flight": [("t15_flight", 0.0, 4.5)],
}


def frames_of(clips_dir, clip, start, seconds):
    folder = os.path.join(clips_dir, clip)
    if not os.path.isdir(folder):
        raise FileNotFoundError(f"clip '{clip}' not captured ({folder})")
    first = int(round(start * SOURCE_FPS))
    count = int(round(seconds * SOURCE_FPS))
    step = SOURCE_FPS // GIF_FPS
    for i in range(first, first + count, step):
        path = os.path.join(folder, f"{i:05d}.png")
        if os.path.exists(path):
            yield path


def build(name, segments, clips_dir, out_dir, colors=128):
    tmp = tempfile.mkdtemp(prefix="gif_")
    try:
        n = 0
        for clip, start, seconds in segments:
            for path in frames_of(clips_dir, clip, start, seconds):
                im = Image.open(path).convert("RGB")
                w, h = im.size
                left, top = (w - CROP[0]) // 2 // 2 * 2, (h - CROP[1]) // 2 // 2 * 2
                im = im.crop((left, top, left + CROP[0], top + CROP[1]))
                im = im.resize((CROP[0] // 2, CROP[1] // 2), Image.NEAREST)
                im.save(os.path.join(tmp, f"{n:05d}.png"))
                n += 1
        if n == 0:
            print(f"{name}: no frames")
            return None
        out = os.path.join(out_dir, name + ".gif")
        ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
        graph = (f"split[a][b];[a]palettegen=max_colors={colors}:stats_mode=full[p];"
                 f"[b][p]paletteuse=dither=none:diff_mode=rectangle")
        subprocess.run([ffmpeg, "-y", "-loglevel", "error", "-framerate", str(GIF_FPS), "-i",
                        os.path.join(tmp, "%05d.png"), "-filter_complex", graph, "-loop", "0", out], check=True)
        size = os.path.getsize(out)
        if size > MAX_BYTES and colors > 32:
            print(f"{name}: {size / 1048576:.1f} MB with {colors} colours, retrying with fewer")
            return build(name, segments, clips_dir, out_dir, colors // 2)
        print(f"{name}: {n} frames, {size / 1048576:.2f} MB, {colors} colours -> {out}")
        return out
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--clips", default=os.path.join("Captures", "clips"))
    parser.add_argument("--out", default=os.path.join("Captures", "gifs"))
    parser.add_argument("--only", default="")
    args = parser.parse_args()
    os.makedirs(args.out, exist_ok=True)
    only = [s for s in args.only.split(",") if s]
    failed = False
    for name, segments in GIFS.items():
        if only and name not in only:
            continue
        try:
            build(name, segments, args.clips, args.out)
        except (FileNotFoundError, subprocess.CalledProcessError) as e:
            print(f"{name}: {e}")
            failed = True
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
