"""Assembles the Steam trailer from the captured clips.

Usage (from the project root, after Antarium -> Capture -> Run All Shots):
    python tools/media/make_trailer.py [--music Assets/_Antarium/Audio/Music/music_title.wav] [--logo game_logo.png] [--studio studio_logo.png]
                                       [--out Captures/trailer/antarium2_trailer.mp4]

The edit is the EDIT list below: clip segments (1080p60 PNG frames from Captures/clips) and title cards drawn in the
game's Silkscreen font, joined with short crossfades, with one music track faded in and out. The end card shows the
game logo (--logo, default Assets/_Antarium/Icon/shortcut.png) over the wishlist line and the studio logo (--studio,
default Assets/_Antarium/Icon/elevate_white_logo.png); a missing logo falls back to text. Output: H.264 1920x1080 60 fps,
AAC 48 kHz, which matches Steam's trailer requirements (up to 1920x1080, 30 or 60 fps, 5000+ kbps, .mp4).
Needs Pillow and imageio-ffmpeg (pip install pillow imageio-ffmpeg).
"""
import argparse
import os
import shutil
import subprocess
import tempfile

from PIL import Image, ImageDraw, ImageFont
import imageio_ffmpeg

FPS = 60
SIZE = (1920, 1080)
FADE = 0.25  # seconds of crossfade between segments
FONT = os.path.join("Assets", "_Antarium", "UI", "Fonts", "Silkscreen-Regular.ttf")
ICONS = os.path.join("Assets", "_Antarium", "Icon")
AMBER = (247, 190, 110)
DIM = (170, 150, 125)

# ("clip", name, start s, seconds) or ("card", [lines], seconds)
EDIT = [
    ("clip", "t01_sky_to_nest", 0.0, 6.5),
    ("card", ["DIG DEEP."], 1.6),
    ("clip", "t03_dig", 0.2, 5.0),
    ("clip", "t03b_dig_haul", 0.0, 2.0),
    ("clip", "t02_queen", 0.3, 3.0),
    ("card", ["BUILD THE COLONY."], 1.6),
    ("clip", "t04_ore_room", 0.5, 3.5),
    ("clip", "t05_factory", 0.5, 5.5),
    ("clip", "t05b_flow_overlay", 0.3, 2.0),
    ("card", ["DESCEND."], 1.4),
    ("clip", "t06_dive", 0.5, 6.5),
    ("clip", "t07_crystal", 0.5, 3.2),
    ("clip", "t08_fungal", 0.5, 2.8),
    ("clip", "t09_magma", 0.5, 3.2),
    ("clip", "t09b_ruins", 0.5, 2.8),
    ("card", ["DEFEND THE NEST."], 1.4),
    ("clip", "t10_raid", 0.5, 6.0),
    ("clip", "t11_possess", 0.3, 5.0),
    ("clip", "t12_seismic", 0.0, 3.0),
    ("card", ["RAISE A DYNASTY."], 1.6),
    ("clip", "t15_flight", 0.0, 4.6),
    ("clip", "t16_genetics", 0.0, 2.5),
    ("end", None, 5.0),
]


def font(size):
    return ImageFont.truetype(FONT, size)


def card(lines, size=72):
    im = Image.new("RGB", SIZE, (0, 0, 0))
    d = ImageDraw.Draw(im)
    f = font(size)
    heights = [d.textbbox((0, 0), line, font=f)[3] for line in lines]
    y = (SIZE[1] - sum(heights) - 30 * (len(lines) - 1)) // 2
    for line, h in zip(lines, heights):
        w = d.textlength(line, font=f)
        d.text(((SIZE[0] - w) // 2, y), line, font=f, fill=AMBER)
        y += h + 30
    return im


def paste_logo(im, path, box):
    """Pastes a transparent logo (trimmed to its visible pixels) centred in box = (left, top, width, height)."""
    mark = Image.open(path).convert("RGBA")
    mark = mark.crop(mark.split()[-1].getbbox())
    scale = min(box[2] / mark.width, box[3] / mark.height)
    mark = mark.resize((int(mark.width * scale), int(mark.height * scale)), Image.LANCZOS)
    im.paste(mark, (box[0] + (box[2] - mark.width) // 2, box[1] + (box[3] - mark.height) // 2), mark)


def end_card(logo, studio):
    """The game logo, the wishlist line and the studio logo (text in the game's font where a logo is missing)."""
    im = Image.new("RGB", SIZE, (0, 0, 0))
    d = ImageDraw.Draw(im)

    def centred(text, y, size, color):
        f = font(size)
        d.text(((SIZE[0] - d.textlength(text, font=f)) // 2, y), text, font=f, fill=color)

    if logo and os.path.exists(logo):
        paste_logo(im, logo, (0, 50, SIZE[0], 610))
    else:
        centred("ANTARIUM 2", 280, 150, AMBER)
    centred("WISHLIST NOW ON STEAM", 715, 52, AMBER)
    if studio and os.path.exists(studio):
        paste_logo(im, studio, (0, 850, SIZE[0], 150))
    else:
        centred("ELEVATE STUDIOS", 900, 30, DIM)
    return im


def segment_frames(kind, data, start, seconds, clips, tmp, logo, studio, index):
    """Paths of this segment's frames (clip frames are used in place; cards are written once)."""
    count = int(round(seconds * FPS))
    if kind == "clip":
        folder = os.path.join(clips, data)
        first = int(round(start * FPS))
        paths = [os.path.join(folder, f"{i:05d}.png") for i in range(first, first + count)]
        missing = [p for p in paths if not os.path.exists(p)]
        if missing:
            raise FileNotFoundError(f"{data}: {len(missing)} of {count} frames missing (first {missing[0]})")
        return paths
    im = card(data) if kind == "card" else end_card(logo, studio)
    path = os.path.join(tmp, f"card_{index:02d}.png")
    im.save(path)
    return [path] * count


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--clips", default=os.path.join("Captures", "clips"))
    parser.add_argument("--music", default=os.path.join("Assets", "_Antarium", "Audio", "Music", "music_title.wav"))
    parser.add_argument("--logo", default=os.path.join(ICONS, "shortcut.png"), help="game logo on the end card")
    parser.add_argument("--studio", default=os.path.join(ICONS, "elevate_white_logo.png"), help="studio logo under it")
    parser.add_argument("--out", default=os.path.join("Captures", "trailer", "antarium2_trailer.mp4"))
    parser.add_argument("--bitrate", default="12M", help="video bitrate; Steam asks for 5000+ kbps")
    args = parser.parse_args()

    tmp = tempfile.mkdtemp(prefix="trailer_")
    seq = os.path.join(tmp, "seq")
    os.makedirs(seq)
    try:
        segments = []
        for i, item in enumerate(EDIT):
            if item[0] == "clip":
                _, name, start, seconds = item
                segments.append(segment_frames("clip", name, start, seconds, args.clips, tmp, args.logo, args.studio, i))
            else:
                kind, data, seconds = item
                segments.append(segment_frames(kind, data, 0, seconds, args.clips, tmp, args.logo, args.studio, i))

        # Lay the segments end to end; each joint is a crossfade of FADE seconds (black at both ends).
        fade = int(round(FADE * FPS))
        n = 0
        black = os.path.join(tmp, "black.png")
        Image.new("RGB", SIZE).save(black)

        def put(path):
            nonlocal n
            dst = os.path.join(seq, f"{n:06d}.png")
            try:
                os.link(path, dst)
            except OSError:
                shutil.copyfile(path, dst)
            n += 1

        def blend(a, b, t):
            nonlocal n
            ia, ib = Image.open(a).convert("RGB"), Image.open(b).convert("RGB")
            Image.blend(ia, ib, t).save(os.path.join(seq, f"{n:06d}.png"))
            n += 1

        previous_tail = [black] * fade
        for frames in segments:
            head, body = frames[:fade], frames[fade:]
            for k in range(fade):
                blend(previous_tail[k], head[k], (k + 1) / (fade + 1))
            tail = body[-fade:] if len(body) > fade else body
            for p in body[:len(body) - len(tail)]:
                put(p)
            previous_tail = tail + [tail[-1]] * (fade - len(tail))
        for k in range(fade):
            blend(previous_tail[k], black, (k + 1) / (fade + 1))

        duration = n / FPS
        os.makedirs(os.path.dirname(args.out) or ".", exist_ok=True)
        ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
        audio = f"[1:a]atrim=0:{duration:.3f},asetpts=PTS-STARTPTS,afade=t=in:d=0.8,afade=t=out:st={duration - 3:.3f}:d=3[a]"
        cmd = [ffmpeg, "-y", "-loglevel", "error", "-stats",
               "-framerate", str(FPS), "-i", os.path.join(seq, "%06d.png"),
               "-i", args.music,
               "-filter_complex", audio, "-map", "0:v", "-map", "[a]",
               "-c:v", "libx264", "-preset", "slow", "-b:v", args.bitrate, "-maxrate", "20M", "-bufsize", "40M",
               "-pix_fmt", "yuv420p", "-r", str(FPS), "-profile:v", "high",
               "-c:a", "aac", "-b:a", "320k", "-ar", "48000",
               "-movflags", "+faststart", args.out]
        subprocess.run(cmd, check=True)
        size = os.path.getsize(args.out)
        print(f"{args.out}: {duration:.1f} s, {size / 1048576:.1f} MB, {size * 8 / duration / 1000:.0f} kbps")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    main()
