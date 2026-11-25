"""Assembles the Steam trailers from the captured clips.

Usage (from the project root, after Antarium -> Capture -> Run All Store Shots):
    python tools/media/make_trailer.py [--cut general|gameplay] [--music Assets/_Antarium/Audio/Music/music_title.wav]
                                       [--logo game_logo.png] [--studio studio_logo.png] [--out <file>.mp4]

Three cuts (CUTS below): "general" (story-led, Steam category General / Cinematic; the default), "gameplay" (straight
into play, short captions over the footage instead of cards, Steam category Gameplay; list it first on the store
page) and "demo" (the demo app's page: the gameplay style with only what the demo has, ending on the free demo). Each edit is a list of clip segments (1080p60 PNG frames from Captures/clips) and title cards drawn in the
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
STORY_FONT = os.path.join("Assets", "_Antarium", "UI", "Fonts", "VT323-Regular.ttf")
ICONS = os.path.join("Assets", "_Antarium", "Icon")
AMBER = (247, 190, 110)
PARCHMENT = (230, 205, 165)
DIM = (170, 150, 125)

CAPTION_SECONDS = 2.4  # how long a caption stays on its clip (it fades in and out)

# ("clip", name, start s, seconds[, caption]), ("card", [lines], seconds) = a beat in Silkscreen capitals,
# ("story", [lines], seconds) = the Old Colony's story (the lore fragments) in the game's body font.
# A clip's caption is drawn over its first CAPTION_SECONDS, at the bottom of the frame.
GENERAL = [
    ("story", ["Long ago, the Old Colony filled these depths.", "Its queens lived for a hundred summers."], 3.4),
    ("clip", "s05b_glow_ruins_pan", 0.0, 4.2),
    ("story", ["Then the fire below grew restless,", "and its Engine fell silent."], 3.0),
    ("clip", "t01_sky_to_nest", 0.0, 6.5),
    ("story", ["Now a new queen begins to dig."], 2.4),
    ("clip", "t02_queen", 0.3, 3.0),
    ("card", ["DIG DEEP."], 1.4),
    ("clip", "t03_dig", 0.2, 4.6),
    ("clip", "t03b_dig_haul", 0.0, 1.6),
    ("card", ["BUILD A LIVING MACHINE."], 1.6),
    ("clip", "t04_ore_room", 0.5, 3.0),
    ("clip", "t05_factory", 0.5, 4.5),
    ("clip", "t05b_flow_overlay", 0.3, 2.0),
    ("clip", "s09_waterworks", 0.5, 3.0),
    ("card", ["DAY, NIGHT AND STORM."], 1.6),
    ("clip", "s01_dusk", 0.0, 5.5),
    ("clip", "s03_storm", 0.3, 3.6),
    ("clip", "s02_starfall", 0.5, 3.6),
    ("card", ["DESCEND."], 1.4),
    ("clip", "t06_dive", 0.5, 6.0),
    ("clip", "s04_elder_tree", 0.5, 4.2),
    ("clip", "t07_crystal", 0.5, 2.6),
    ("clip", "t08_fungal", 0.5, 2.4),
    ("clip", "s08_fire_ants", 0.5, 3.0),
    ("card", ["DEFEND THE NEST."], 1.4),
    ("clip", "t10_raid", 0.5, 4.5),
    ("clip", "s07_worm", 0.5, 3.6),
    ("clip", "t11_possess", 0.3, 4.0),
    ("story", ["\"To whoever digs this deep: you are our daughters' daughters.", "Wake the Engine, and remember us.\""], 3.8),
    ("clip", "s06_engine", 0.4, 5.4),
    ("card", ["RAISE A DYNASTY."], 1.4),
    ("clip", "t15_flight", 0.0, 4.6),
    ("clip", "t16_genetics", 0.0, 2.5),
    ("end", None, 5.0),
]

# Opens on the player digging and never leaves the game until the end card: every clip is the showcase colony
# playing, seen from the player's side-view camera.
GAMEPLAY = [
    ("clip", "t03_dig", 0.2, 4.0, "DIG YOUR NEST"),
    ("clip", "t03b_dig_haul", 0.0, 2.0),
    ("clip", "t02_queen", 0.3, 2.6, "GROW A COLONY"),
    ("clip", "g_colony", 0.0, 3.0),
    ("clip", "t04_ore_room", 0.5, 2.6, "BUILD A LIVING MACHINE"),
    ("clip", "t05_factory", 0.5, 4.0),
    ("clip", "t05b_flow_overlay", 0.3, 2.0),
    ("clip", "s09_waterworks", 0.5, 2.6),
    ("clip", "t14_research", 0.0, 2.5),
    ("clip", "s03_storm", 0.3, 3.0, "DAY, NIGHT AND STORM"),
    ("clip", "s02_starfall", 0.5, 2.6),
    ("clip", "t06_dive", 0.5, 5.0, "DESCEND"),
    ("clip", "t07_crystal", 0.5, 2.4),
    ("clip", "t08_fungal", 0.5, 2.2),
    ("clip", "s08_fire_ants", 0.5, 2.6),
    ("clip", "s04_elder_tree", 0.5, 3.0),
    ("clip", "t10_raid", 0.5, 4.5, "DEFEND THE NEST"),
    ("clip", "s07_worm", 0.5, 3.0),
    ("clip", "t11_possess", 0.3, 4.0, "TAKE CONTROL OF ANY ANT"),
    ("clip", "s06_engine", 0.4, 4.0, "WAKE THE ANCIENT ENGINE"),
    ("clip", "t15_flight", 0.0, 4.0, "RAISE A DYNASTY"),
    ("clip", "t16_genetics", 0.0, 2.5),
    ("end", None, 4.0),
]

# The demo app's own page (Valve asks a demo page for a trailer of the demo itself): only what the demo has, from the
# meadow down to the stone strata and the Elder Tree; no crystal caverns, magma, ruins, fire ants, Engine or flight.
# Its last beat is the Ancient Seal (d_seal, from the demo capture set: Antarium -> Capture -> Run Demo Store Shots), and
# it ends on the free demo and the carry-over instead of the wishlist line.
DEMO = [
    ("clip", "t03_dig", 0.2, 4.0, "DIG YOUR NEST"),
    ("clip", "t03b_dig_haul", 0.0, 2.0),
    ("clip", "t02_queen", 0.3, 2.6, "GROW A COLONY"),
    ("clip", "g_colony", 0.0, 3.0),
    ("clip", "t04_ore_room", 0.5, 2.6, "BUILD A LIVING MACHINE"),
    ("clip", "t05_factory", 0.5, 4.0),
    ("clip", "t05b_flow_overlay", 0.3, 2.0),
    ("clip", "s09_waterworks", 0.5, 2.6),
    ("clip", "s03_storm", 0.3, 3.0, "DAY, NIGHT AND STORM"),
    ("clip", "s01_dusk", 1.0, 3.0),
    ("clip", "s02_starfall", 0.5, 2.6),
    ("clip", "t12_seismic", 0.5, 2.6, "DIG DEEPER"),
    ("clip", "s04_elder_tree", 0.5, 3.6, "FIND THE ELDER TREE"),
    ("clip", "t10_raid", 0.5, 4.5, "DEFEND THE NEST"),
    ("clip", "s07_worm", 0.5, 3.0),
    ("clip", "t11_possess", 0.3, 4.0, "TAKE CONTROL OF ANY ANT"),
    ("clip", "d_seal", 2.0, 5.5, "REACH THE ANCIENT SEAL"),
    ("end", None, 4.5),
]

CUTS = {"general": GENERAL, "gameplay": GAMEPLAY, "demo": DEMO}
OUTPUTS = {"general": "antarium2_trailer.mp4", "gameplay": "antarium2_gameplay_trailer.mp4",
           "demo": "antarium2_demo_trailer.mp4"}
# The end card's lines under the logo: the first large, the rest smaller.
END_LINES = {"general": ["WISHLIST NOW ON STEAM"], "gameplay": ["WISHLIST NOW ON STEAM"],
             "demo": ["FREE DEMO OUT NOW", "YOUR COLONY CARRIES OVER TO THE FULL GAME"]}
end_lines = END_LINES["general"]


def font(size, path=FONT):
    return ImageFont.truetype(path, size)


def card(lines, size=72, path=FONT, color=AMBER, gap=30):
    im = Image.new("RGB", SIZE, (0, 0, 0))
    d = ImageDraw.Draw(im)
    f = font(size, path)
    heights = [d.textbbox((0, 0), line, font=f)[3] for line in lines]
    y = (SIZE[1] - sum(heights) - gap * (len(lines) - 1)) // 2
    for line, h in zip(lines, heights):
        w = d.textlength(line, font=f)
        d.text(((SIZE[0] - w) // 2, y), line, font=f, fill=color)
        y += h + gap
    return im


def story_card(lines):
    return card(lines, size=64, path=STORY_FONT, color=PARCHMENT, gap=18)


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
    centred(end_lines[0], 700 if len(end_lines) > 1 else 715, 52, AMBER)
    for k, line in enumerate(end_lines[1:]):
        centred(line, 785 + k * 44, 28, PARCHMENT)
    if studio and os.path.exists(studio):
        paste_logo(im, studio, (0, 850, SIZE[0], 150))
    else:
        centred("ELEVATE STUDIOS", 900, 30, DIM)
    return im


def caption_layer(text):
    """The caption in amber Silkscreen with a black outline, bottom centre, on a transparent 1080p layer."""
    layer = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    f = font(56)
    x, y = (SIZE[0] - d.textlength(text, font=f)) // 2, SIZE[1] - 150
    for dx, dy in ((-4, 0), (4, 0), (0, -4), (0, 4), (-3, -3), (3, 3), (-3, 3), (3, -3)):
        d.text((x + dx, y + dy), text, font=f, fill=(0, 0, 0, 255))
    d.text((x, y), text, font=f, fill=AMBER + (255,))
    return layer


def clip_frame_paths(clips, name, start, seconds):
    """The PNG frames of clip `name` from `start` for `seconds` (raises if any are missing)."""
    count = int(round(seconds * FPS))
    first = int(round(start * FPS))
    paths = [os.path.join(clips, name, f"{i:05d}.png") for i in range(first, first + count)]
    missing = [p for p in paths if not os.path.exists(p)]
    if missing:
        raise FileNotFoundError(f"{name}: {len(missing)} of {count} frames missing (first {missing[0]})")
    return paths


def segment_frames(kind, data, start, seconds, clips, tmp, logo, studio, index, caption=None):
    """Paths of this segment's frames (clip frames used in place, captioned ones redrawn; cards written once)."""
    count = int(round(seconds * FPS))
    if kind == "clip":
        paths = clip_frame_paths(clips, data, start, seconds)
        if caption:
            layer = caption_layer(caption)
            alpha = layer.split()[-1]
            shown = min(len(paths), int(round(CAPTION_SECONDS * FPS)))
            fade_in, fade_out = int(0.25 * FPS), int(0.35 * FPS)
            for k in range(shown):
                t = min(1.0, (k + 1) / fade_in, (shown - k) / fade_out)
                im = Image.open(paths[k]).convert("RGB")
                im.paste(layer, (0, 0), alpha.point(lambda a, t=t: int(a * t)))
                out = os.path.join(tmp, f"cap_{index:02d}_{k:04d}.png")
                im.save(out, compress_level=1)
                paths[k] = out
        return paths
    im = card(data) if kind == "card" else story_card(data) if kind == "story" else end_card(logo, studio)
    path = os.path.join(tmp, f"card_{index:02d}.png")
    im.save(path)
    return [path] * count


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--cut", choices=sorted(CUTS), default="general",
                        help="general = story-led (Steam: General / Cinematic), gameplay = straight into play (Steam: Gameplay), "
                             "demo = the demo's own page (Gameplay, demo content only)")
    parser.add_argument("--clips", default=os.path.join("Captures", "clips"))
    parser.add_argument("--music", default=os.path.join("Assets", "_Antarium", "Audio", "Music", "music_title.wav"))
    parser.add_argument("--logo", default=os.path.join(ICONS, "shortcut.png"), help="game logo on the end card")
    parser.add_argument("--studio", default=os.path.join(ICONS, "elevate_white_logo.png"), help="studio logo under it")
    parser.add_argument("--out", help="default Captures/trailer/antarium2_trailer.mp4 (general) or antarium2_gameplay_trailer.mp4")
    parser.add_argument("--bitrate", default="12M", help="video bitrate; Steam asks for 5000+ kbps")
    args = parser.parse_args()
    args.out = args.out or os.path.join("Captures", "trailer", OUTPUTS[args.cut])
    global end_lines
    end_lines = END_LINES[args.cut]

    tmp = tempfile.mkdtemp(prefix="trailer_")
    seq = os.path.join(tmp, "seq")
    os.makedirs(seq)
    try:
        segments = []
        for i, item in enumerate(CUTS[args.cut]):
            if item[0] == "clip":
                name, start, seconds = item[1:4]
                caption = item[4] if len(item) > 4 else None
                segments.append(segment_frames("clip", name, start, seconds, args.clips, tmp, args.logo, args.studio, i,
                                               caption))
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
