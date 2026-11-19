"""Cuts the short-form videos (TikTok, YouTube Shorts, Instagram Reels) from the vertical capture clips.

Usage (from the project root, after Antarium -> Capture -> Run Short-Form Video Shots):
    python tools/media/make_shorts.py                 # every short
    python tools/media/make_shorts.py 02 storm        # only shorts whose id contains one of these
    python tools/media/make_shorts.py --list          # the slate, with lengths
    python tools/media/make_shorts.py --preview       # a contact sheet per short instead of videos (fast)

Each short (SHORTS below) is a list of vertical clip segments (1080 x 1920 PNG frames from Captures/clips/v_*, rendered
natively in 9:16 by the game, so the pixel art is never cropped or rescaled) with hard cuts, text over the footage in
the game's fonts (kept inside the area the apps leave clear: below the top tabs, above the caption and clear of the
side buttons), and an end card (game logo, "Wishlist on Steam"). Sound: the game's own effects, mixed in sync from the
capture's per-frame sound log (clips/<clip>/sounds.tsv, samples in clips/_sounds), under one music track from
Assets/_Antarium/Audio/Music. Output: Captures/shorts/NN_name.mp4 (H.264 1080x1920 60 fps, AAC 48 kHz stereo, loudness
about -14 LUFS, which suits all three apps) and posting_guide.md / posting.csv with the caption and hashtags of each.
Needs Pillow, numpy and imageio-ffmpeg (pip install pillow numpy imageio-ffmpeg).
"""
import argparse
import bisect
import csv
import os
import subprocess
import sys
from concurrent.futures import ProcessPoolExecutor

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont
import imageio_ffmpeg

FPS = 60
W, H = 1080, 1920
RATE = 48000
FONT = os.path.join("Assets", "_Antarium", "UI", "Fonts", "Silkscreen-Regular.ttf")
STORY_FONT = os.path.join("Assets", "_Antarium", "UI", "Fonts", "VT323-Regular.ttf")
LOGO = os.path.join("Assets", "_Antarium", "Icon", "shortcut.png")
MUSIC = os.path.join("Assets", "_Antarium", "Audio", "Music")
CLIPS = os.path.join("Captures", "clips")
OUT = os.path.join("Captures", "shorts")

WHITE = (255, 255, 255)
AMBER = (247, 190, 110)
PARCHMENT = (236, 214, 176)

# Safe area on a 1080 x 1920 frame: the apps put tabs over the top ~220 px, the caption and buttons over the bottom
# ~420 px and the like / comment / share column over the right ~140 px, so text sits in this box.
TEXT_TOP = 300
TEXT_LEFT, TEXT_RIGHT = 80, W - 150
TEXT_WIDTH = TEXT_RIGHT - TEXT_LEFT
TEXT_CENTER = (TEXT_LEFT + TEXT_RIGHT) // 2

END_SECONDS = 2.6
# The ambience bed (wind, the deep's hum) sits this many dB under the music.
BED_DB = -10.0
MAX_SFX_GAIN = 12.0  # +22 dB


def active_db(x, seconds):
    """Level in dB of the loud part of a stem: RMS over the 100 ms windows within 30 dB of its loudest one."""
    x = x[: int(seconds * RATE)]
    win = RATE // 10
    k = len(x) // win
    if k == 0:
        return -120.0
    e = np.mean(x[: k * win].reshape(k, win, 2) ** 2, axis=(1, 2))
    if e.max() <= 1e-12:
        return -120.0
    loud = e[e >= e.max() * 1e-3]
    return float(10 * np.log10(np.mean(loud)))
# Interface cues of the hidden HUD: never mixed in.
SKIP_CUES = ("Toast", "UiClick", "UiHover", "UiOpen", "UiClose", "Achievement", "Refused")

# ------------------------------------------------------------------------------------------------ the slate
#
# segs:  (clip, start s, seconds[, "rev" | speed]) - seconds of the source clip; "rev" plays them backwards, a number
#        plays them that many times faster (every n-th frame; 2 halves the segment).
# texts: (from s, to s, text[, style])      - times on the short's own timeline; style "hook" (big, white, *word* in
#        amber), "cap" (amber), "story" (the lore font) or "low" (a caption lower down, over the footage). "|" breaks a line.
# counter: a clip with depth.tsv (the dive) shows a depth readout and the biome names while it plays.
# music: (track, start s); sfx: dB of the game's sound effects against the music (0 = as loud, default -6, -10 for
# music-led shorts). post: title (YouTube), caption and hashtags.

TAGS = "#indiegame #antcolony #pixelart #gamedev"


def short(id, segs, texts, music, post, sfx=-6, counter=None):
    return dict(id=id, segs=segs, texts=texts, music=music, post=post, sfx=sfx, counter=counter)


SHORTS = [
    short("01_one_ant", [("v_zoom_out", 0.0, 7.6)],
          [(0.0, 1.4, "This is *one* ant", "hook"), (5.0, 7.6, "And this is|*her colony*", "hook")],
          ("music_title", 0.0),
          dict(title="One ant queen... and her entire colony #shorts",
               caption="Every ant down there is her daughter. Antarium 2 is a living ant colony in a cross-section of the earth. Wishlist on Steam!",
               tags="#ants #antcolony #indiegame #pixelart #steam")),
    short("02_how_deep", [("v_dive", 0.0, 3.0), ("v_dive", 3.0, 6.0, 2), ("v_dive", 9.0, 6.4)],
          [(0.0, 2.2, "How deep can an|*ant colony* go?", "hook")],
          ("music_ancient_ruins_01", 0.0),
          dict(title="How deep can an ant colony go? #shorts",
               caption="From the meadow down through clay, stone, crystal caverns, fungal depths and the magma zone, to the ruins of the Old Colony.",
               tags="#ants #antcolony #indiegame #pixelart #gamedev"),
          sfx=-10, counter="v_dive"),
    short("03_dig_by_hand", [("v_dig", 0.0, 7.0), ("v_dig_haul", 0.0, 4.0)],
          [(0.0, 2.2, "Digging an ant nest|*by hand*", "hook"), (7.0, 9.6, "Then the ants|move in", "cap")],
          ("music_surface", 0.0),
          dict(title="Digging an ant nest by hand (so satisfying) #shorts",
               caption="Dig the tunnels yourself, then the colony takes over. Antarium 2, wishlist on Steam.",
               tags="#satisfying #ants #indiegame #pixelart #cozygames"),
          sfx=0),
    short("04_storm", [("v_storm", 0.0, 8.0)],
          [(0.0, 2.4, "Thunderstorm above.|*Cozy ants* below.", "hook")],
          ("music_surface", 30.0),
          dict(title="Thunderstorm above, cozy ants below #shorts",
               caption="Rain floods the meadow, lightning strikes the hills, and the colony keeps working underground.",
               tags="#cozygames #rain #ants #pixelart #indiegame"),
          sfx=-3),
    short("05_falling_stars", [("v_starfall", 0.0, 7.0)],
          [(0.0, 2.4, "My ants catch|*falling stars*", "hook")],
          ("music_title", 20.0),
          dict(title="My ants catch falling stars #shorts",
               caption="On a starfall night the sky rains star fragments, and the colony goes out to gather them.",
               tags="#pixelart #cozygames #ants #indiegame #stars")),
    short("06_under_the_meadow", [("v_sky_to_nest", 0.0, 6.4), ("v_halls", 0.0, 3.5), ("v_queen", 0.0, 3.0)],
          [(0.0, 2.0, "What's under|*this meadow*?", "hook"), (6.4, 9.0, "A whole|*ant civilization*", "hook")],
          ("music_surface", 0.0),
          dict(title="What's under this meadow? #shorts",
               caption="Under the grass: storage halls, nurseries, and a queen laying eggs. Antarium 2 on Steam.",
               tags="#ants #antcolony #pixelart #indiegame #cozygames")),
    short("07_day_to_night", [("v_dusk", 0.0, 8.0)],
          [(0.0, 2.4, "Day to night in an|*ant colony*", "hook")],
          ("music_title", 40.0),
          dict(title="Day to night in an ant colony #shorts",
               caption="A full sunset over the hills while the colony works on below.",
               tags="#cozygames #pixelart #sunset #ants #indiegame")),
    short("08_ant_factory", [("v_factory", 0.0, 9.0), ("v_flow", 0.0, 3.5)],
          [(0.0, 2.2, "My ants built|a *factory*", "hook"), (9.0, 12.4, "Ore in.|*Copper out.*", "cap")],
          ("music_genetics", 0.0),
          dict(title="My ants built a factory #shorts",
               caption="Belts, root lifts, smelters: automate your colony like a factory game, but every worker is an ant.",
               tags="#automation #factorygames #ants #indiegame #pixelart"),
          sfx=-3),
    short("09_water_wheel", [("v_waterworks", 0.0, 5.0), ("v_flow", 0.5, 3.0)],
          [(0.0, 2.4, "Ants discovered|*hydropower*", "hook")],
          ("music_crystal_caverns_01", 0.0),
          dict(title="Ants discovered hydropower #shorts",
               caption="Water flows, wheels turn, the machines run. Real fluid simulation in a pixel ant colony.",
               tags="#automation #pixelart #indiegame #ants #simulation"),
          sfx=-3),
    short("10_become_any_ant", [("v_possess", 0.0, 7.0)],
          [(0.0, 2.4, "You can become|*any ant*", "hook"), (4.2, 6.8, "and dig wherever|you want", "low")],
          ("music_stone_strata_01", 0.0),
          dict(title="You can take control of any ant #shorts",
               caption="Possess any ant in your colony and tunnel through the dark yourself.",
               tags="#ants #indiegame #pixelart #gamedev #antcolony"),
          sfx=0),
    short("11_long_live_the_queen", [("v_queen", 0.0, 5.0), ("v_halls", 1.0, 4.0)],
          [(0.0, 2.4, "Every ant here is|*her daughter*", "hook")],
          ("music_title", 60.0),
          dict(title="Every ant in the colony is her daughter #shorts",
               caption="The queen lays, the nurses carry the eggs, the workers keep the halls full.",
               tags="#ants #antqueen #antcolony #pixelart #indiegame")),
    short("12_giant_worm", [("v_worm", 0.0, 7.0)],
          [(0.0, 2.4, "A *giant worm* is|tunneling through|my colony", "hook")],
          ("music_raid", 0.0),
          dict(title="A giant worm tunneled through my ant colony #shorts",
               caption="Random events keep the colony on its toes. This one eats dirt. A lot of it.",
               tags="#ants #indiegame #pixelart #gamedev #antcolony"),
          sfx=-1),
    short("13_termite_raid", [("v_raid", 0.0, 10.5)],
          [(0.0, 2.2, "Termites attacked.|*Bad idea.*", "hook")],
          ("music_raid", 0.0),
          dict(title="Termites attacked my ant colony. Bad idea. #shorts",
               caption="Raise soldiers, build defenses, and defend the nest when the raids come.",
               tags="#ants #termites #indiegame #pixelart #strategygames"),
          sfx=-1),
    short("14_glow_ants", [("v_glow", 0.0, 6.0)],
          [(0.0, 2.4, "These ants|*glow in the dark*", "hook")],
          ("music_ancient_ruins_01", 20.0),
          dict(title="These ants glow in the dark #shorts",
               caption="Deep down there is no light, so the colony brings its own: glow ants.",
               tags="#pixelart #ants #indiegame #cozygames #gamedev")),
    short("15_fire_ants", [("v_fire", 0.0, 5.0), ("v_magma", 0.0, 3.5)],
          [(0.0, 2.4, "Fire ants.|*Actual* fire ants.", "hook")],
          ("music_magma_zone_01", 0.0),
          dict(title="Fire ants. ACTUAL fire ants. #shorts",
               caption="In the magma zone only one caste can work: fire ants that don't mind the heat.",
               tags="#ants #fireants #pixelart #indiegame #gamedev"),
          sfx=-3),
    short("16_ancient_engine", [("v_ruins", 0.0, 3.5), ("v_engine", 0.0, 7.5)],
          [(0.0, 2.4, "We found something|*ancient* down here", "hook"), (3.5, 6.0, "So we woke it up", "low")],
          ("music_ancient_ruins_01", 0.0),
          dict(title="We found something ancient deep underground #shorts",
               caption="At the bottom of the world lie the ruins of the Old Colony, and its silent Engine.",
               tags="#pixelart #indiegame #ants #mystery #gamedev"),
          sfx=-4),
    short("17_elder_tree", [("v_elder_tree", 0.0, 7.0)],
          [(0.0, 2.6, "A glowing tree...|*underground?*", "hook")],
          ("music_crystal_caverns_01", 15.0),
          dict(title="A glowing tree... underground? #shorts",
               caption="Dig deep enough and you find the Elder Tree's cavern.",
               tags="#pixelart #indiegame #cozygames #ants #gamedev")),
    short("18_every_layer", [("v_crystal", 0.0, 3.0), ("v_fungal", 0.0, 3.0), ("v_magma", 0.0, 3.0), ("v_ruins", 0.0, 3.0)],
          [(0.0, 2.0, "Every layer under|your *ant colony*", "hook"),
           (0.4, 3.0, "Crystal caverns", "low"), (3.0, 6.0, "Fungal depths", "low"),
           (6.0, 9.0, "Magma zone", "low"), (9.0, 12.0, "Ancient ruins", "low")],
          ("music_fungal_depths_01", 0.0),
          dict(title="Every layer under your ant colony #shorts",
               caption="Each biome has its own resources, creatures and music. How deep would you go?",
               tags="#pixelart #indiegame #ants #antcolony #gamedev")),
    short("19_see_through_rock", [("v_seismic", 0.0, 4.5)],
          [(0.0, 2.4, "My ants can|*see through rock*", "hook")],
          ("music_stone_strata_01", 20.0),
          dict(title="My ants can see through rock #shorts",
               caption="Seismic Sense: one pulse and every ore vein nearby lights up.",
               tags="#pixelart #indiegame #ants #gamedev #mining"),
          sfx=-3),
    short("20_nuptial_flight", [("v_flight", 0.0, 4.6)],
          [(0.0, 2.6, "This is how a|*new colony* begins", "hook")],
          ("music_title", 80.0),
          dict(title="This is how a new ant colony begins #shorts",
               caption="The Nuptial Flight: princesses leave the nest, and your next colony starts stronger.",
               tags="#ants #antcolony #pixelart #indiegame #nature")),
    short("21_the_old_colony", [("v_glow", 0.0, 4.0), ("v_ruins", 0.5, 3.0), ("v_engine", 1.0, 6.0)],
          [(0.0, 3.6, "Long ago, the Old Colony|filled these depths.", "story"),
           (3.8, 7.0, "Then the fire below|grew restless...", "story"),
           (7.4, 12.6, "\"Wake the Engine,|and remember us.\"", "story")],
          ("music_ancient_ruins_01", 40.0),
          dict(title="Long ago, the Old Colony filled these depths #shorts",
               caption="Antarium 2 has a story buried under your nest. Wishlist on Steam to dig it up.",
               tags="#pixelart #indiegame #lore #ants #gamedev")),
    short("22_pov_new_queen", [("v_queen", 0.5, 1.6), ("v_dig", 1.0, 2.0), ("v_halls", 0.0, 1.6), ("v_factory", 2.0, 1.8),
                               ("v_dive", 4.0, 2.0), ("v_raid", 3.0, 1.8), ("v_engine", 3.0, 2.2)],
          [(0.0, 1.6, "POV: you're a|*new ant queen*", "hook"), (1.6, 3.6, "Dig", "cap"), (3.6, 5.2, "Grow", "cap"),
           (5.2, 7.0, "Automate", "cap"), (7.0, 9.0, "Descend", "cap"), (9.0, 10.8, "Defend", "cap"),
           (10.8, 13.0, "Wake what sleeps below", "cap")],
          ("music_title", 0.0),
          dict(title="POV: you're a new ant queen #shorts",
               caption="Dig, grow, automate, descend, defend. Antarium 2 is coming to Steam.",
               tags="#pov #ants #indiegame #pixelart #antcolony"),
          sfx=-9),
    short("23_ant_farm", [("v_halls", 0.0, 4.0), ("v_dig_haul", 0.0, 3.0), ("v_queen", 1.0, 3.0)],
          [(0.0, 2.6, "Remember *ant farms*?", "hook"), (4.0, 7.0, "I made one|you can *play*", "hook")],
          ("music_surface", 50.0),
          dict(title="Remember ant farms? I made one you can play #shorts",
               caption="The glass ant farm from your childhood, but you dig the tunnels, run the colony and go much deeper.",
               tags="#antfarm #nostalgia #ants #indiegame #pixelart")),
    short("24_making_an_ant_game", [("v_zoom_out", 0.0, 3.0), ("v_dig", 0.5, 2.5), ("v_factory", 3.0, 2.5),
                                    ("v_storm", 1.0, 2.5), ("v_raid", 2.0, 2.5), ("v_dive", 6.0, 3.0)],
          [(0.0, 2.6, "I'm making a game about|an *ant colony*", "hook"), (3.0, 5.5, "You dig the nest", "cap"),
           (5.5, 8.0, "Automate it", "cap"), (8.0, 10.5, "Survive the weather", "cap"), (10.5, 13.0, "Defend it", "cap"),
           (13.0, 16.0, "And go *deeper*", "hook")],
          ("music_title", 10.0),
          dict(title="I'm making a game about an ant colony #shorts",
               caption="Antarium 2: dig, automate, survive storms and raids, and descend to the Old Colony. Would you play it?",
               tags="#gamedev #indiedev #indiegame #ants #pixelart"),
          sfx=-9),
    short("25_in_20_seconds", [("v_sky_to_nest", 2.5, 2.0), ("v_queen", 0.0, 1.4), ("v_dig", 2.0, 1.6), ("v_factory", 4.0, 1.6),
                               ("v_waterworks", 1.0, 1.4), ("v_storm", 2.0, 1.6), ("v_starfall", 2.0, 1.4), ("v_crystal", 1.0, 1.2),
                               ("v_fire", 1.0, 1.2), ("v_worm", 2.0, 1.6), ("v_raid", 4.0, 1.6), ("v_glow", 1.0, 1.4),
                               ("v_engine", 4.5, 2.0), ("v_flight", 1.0, 1.6)],
          [(0.0, 2.0, "Antarium 2|in *20 seconds*", "hook")],
          ("music_title", 0.0),
          dict(title="Antarium 2 in 20 seconds #shorts",
               caption="An ant colony sim in a cross-section of the earth: dig, automate, survive, descend. Wishlist on Steam!",
               tags="#indiegame #pixelart #ants #steam #gamedev"),
          sfx=-9),
]

# ------------------------------------------------------------------------------------------------ text


def font(path, size):
    return ImageFont.truetype(path, size)


STYLES = {
    # font, size, colour, outline, y (top of the block)
    "hook": (FONT, 80, WHITE, 8, TEXT_TOP),
    "cap": (FONT, 76, AMBER, 8, TEXT_TOP),
    "story": (STORY_FONT, 78, PARCHMENT, 6, 760),
    "low": (FONT, 52, WHITE, 6, 1240),
}


def parse_words(line):
    """'a *b c* d' -> [(word, amber?)]."""
    out, amber = [], False
    for word in line.split(" "):
        if not word:
            continue
        starts, ends = word.startswith("*"), word.endswith("*") and len(word) > 1
        if starts:
            amber = True
        clean = word.strip("*")
        out.append((clean, amber))
        if ends or (starts and word.count("*") == 2):
            amber = False
    return out


def text_layer(text, style):
    """The text as an RGBA layer the size of the frame, wrapped to the safe width (smaller if a line won't fit)."""
    path, size, colour, outline, top = STYLES[style]
    lines = text.split("|")
    while True:
        f = font(path, size)
        probe = ImageDraw.Draw(Image.new("RGBA", (8, 8)))
        space = probe.textlength(" ", font=f)
        widths = [sum(probe.textlength(w, font=f) for w, _ in parse_words(l)) + space * (len(parse_words(l)) - 1) for l in lines]
        if max(widths) + 2 * outline <= TEXT_WIDTH or size <= 30:
            break
        size -= 4
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    ascent, descent = f.getmetrics()
    line_h = int((ascent + descent) * 1.12)
    y = top
    for line, width in zip(lines, widths):
        x = TEXT_CENTER - width / 2
        for word, amber in parse_words(line):
            fill = AMBER if amber and colour == WHITE else colour
            # A hard drop shadow, then the outlined word.
            d.text((x + outline * 0.6, y + outline * 0.9), word, font=f, fill=(0, 0, 0, 170), stroke_width=outline, stroke_fill=(0, 0, 0, 170))
            d.text((x, y), word, font=f, fill=fill + (255,), stroke_width=outline, stroke_fill=(0, 0, 0, 255))
            x += d.textlength(word, font=f) + space
        y += line_h
    return layer


def ease_out(k):
    k = min(1.0, max(0.0, k))
    return 1 - (1 - k) ** 3


def paste_text(frame, layer, age, remaining):
    """Pops the layer in (slide up and fade, 0.15 s) and fades it out (0.15 s)."""
    k_in = ease_out(age / 0.15)
    k_out = min(1.0, remaining / 0.15)
    alpha = min(k_in, k_out)
    if alpha <= 0:
        return
    dy = int(round((1 - k_in) * 24))
    mask = layer.getchannel("A")
    if alpha < 1:
        mask = mask.point(lambda a: int(a * alpha))
    frame.paste(layer, (0, dy), mask)


# ------------------------------------------------------------------------------------------------ frames


def clip_frames(clip, start, seconds, reverse=False, speed=1):
    first, count = int(round(start * FPS)), int(round(seconds * FPS))
    frames = list(range(first, first + count, max(1, int(speed))))
    paths = [os.path.join(CLIPS, clip, f"{i:05d}.png") for i in frames]
    missing = [p for p in paths if not os.path.exists(p)]
    if missing:
        raise FileNotFoundError(f"{clip}: {len(missing)} of {count} frames missing (first {missing[0]})")
    if reverse:
        paths.reverse()
        frames.reverse()
    return list(zip(paths, frames))


def read_depths(clip):
    path = os.path.join(CLIPS, clip, "depth.tsv")
    table = {}
    if os.path.exists(path):
        for row in open(path, encoding="utf-8"):
            parts = row.rstrip("\n").split("\t")
            if len(parts) >= 3:
                table[int(parts[0])] = (int(parts[1]), parts[2])
    return table


def depth_layer(depth, biome, biome_age):
    """The depth readout (bottom left of the safe area) and the biome's name when it changes."""
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    f = font(FONT, 92)
    text = f"{depth:,}"
    y = 1080
    d.text((TEXT_LEFT, y), "DEPTH", font=font(FONT, 40), fill=AMBER + (255,), stroke_width=5, stroke_fill=(0, 0, 0, 255))
    d.text((TEXT_LEFT, y + 52), text, font=f, fill=WHITE + (255,), stroke_width=8, stroke_fill=(0, 0, 0, 255))
    if biome and biome != "Surface" and biome_age < 2.0:
        alpha = int(255 * min(1.0, biome_age / 0.12, (2.0 - biome_age) / 0.3))
        fb = font(FONT, 54)
        d.text((TEXT_LEFT, y + 170), biome.upper(), font=fb, fill=AMBER + (alpha,), stroke_width=6, stroke_fill=(0, 0, 0, alpha))
    return layer


def end_card_frames(last_path, seconds):
    """The last frame, blurred and darkened, under the game logo and the wishlist line."""
    base = Image.open(last_path).convert("RGB")
    dark = base.filter(ImageFilter.GaussianBlur(14))
    dark = Image.blend(dark, Image.new("RGB", (W, H)), 0.55)
    logo = Image.open(LOGO).convert("RGBA")
    logo = logo.crop(logo.getchannel("A").getbbox())
    scale = 820 / logo.width
    logo = logo.resize((int(logo.width * scale), int(logo.height * scale)), Image.NEAREST if scale == int(scale) else Image.LANCZOS)
    words = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(words)

    def centred(text, y, size, colour, outline):
        f = font(FONT, size)
        x = (W - d.textlength(text, font=f)) / 2
        d.text((x, y), text, font=f, fill=colour + (255,), stroke_width=outline, stroke_fill=(0, 0, 0, 255))

    centred("WISHLIST NOW", 1090, 76, WHITE, 7)
    centred("ON STEAM", 1186, 76, AMBER, 7)
    centred("link in bio", 1306, 40, WHITE, 4)
    n = int(round(seconds * FPS))
    out = []
    for k in range(n):
        t = k / FPS
        blend = min(1.0, t / 0.2)
        im = Image.blend(base, dark, blend) if blend < 1 else dark.copy()
        pop = ease_out((t - 0.05) / 0.3)
        if pop > 0:
            s = 0.85 + 0.15 * pop
            lw, lh = int(logo.width * s), int(logo.height * s)
            lg = logo.resize((lw, lh), Image.BILINEAR) if s < 1 else logo
            mask = lg.getchannel("A").point(lambda a: int(a * min(1.0, pop * 1.4)))
            im.paste(lg, ((W - lw) // 2, 330 + (logo.height - lh) // 2), mask)
        if t > 0.25:
            paste_text(im, words, t - 0.25, 99)
        out.append(im)
    return out


# ------------------------------------------------------------------------------------------------ sound


_decoded = {}


def decode(path):
    """Any audio file -> float32 stereo at RATE (cached)."""
    if path in _decoded:
        return _decoded[path]
    cmd = [imageio_ffmpeg.get_ffmpeg_exe(), "-loglevel", "error", "-i", path, "-f", "f32le", "-ac", "2", "-ar", str(RATE), "-"]
    raw = subprocess.run(cmd, check=True, capture_output=True).stdout
    data = np.frombuffer(raw, dtype=np.float32).reshape(-1, 2).copy()
    _decoded[path] = data
    return data


def sound_events(clip):
    path = os.path.join(CLIPS, clip, "sounds.tsv")
    events = []
    if os.path.exists(path):
        for row in open(path, encoding="utf-8"):
            p = row.rstrip("\n").split("\t")
            if len(p) >= 6:
                events.append((int(p[0]), p[1], p[2], float(p[3]), float(p[4]), float(p[5])))
    return events


def mix_audio(s, timeline, total_frames, levels=False):
    """The game's sounds of every segment in sync with its frames, the ambience bed, and the music under them."""
    n = int(total_frames / FPS * RATE) + RATE
    sfx = np.zeros((n, 2), np.float32)
    bed = np.zeros((n, 2), np.float32)
    for clip, src_frames, out_start in timeline:
        if not src_frames:
            continue
        events = sound_events(clip)
        reverse = src_frames[0] > src_frames[-1]
        ordered = sorted(src_frames)
        lo, hi = ordered[0], ordered[-1]

        def out_frame(f):
            """Output frame showing source frame f (the last shown frame at or before it)."""
            k = max(0, bisect.bisect_right(ordered, f) - 1)
            return out_start + k

        # The bed: the loop playing when the segment starts, then any change inside it.
        beds = [e for e in events if e[1] == "bed"]
        current = [e for e in beds if e[0] <= lo]
        changes = [current[-1]] if current else []
        changes += [e for e in beds if lo < e[0] <= hi]
        if reverse:
            changes = changes[-1:]  # backwards: one bed throughout
        for i, e in enumerate(changes):
            loop = decode(os.path.join(CLIPS, "_sounds", e[2] + ".wav"))
            a = out_start if i == 0 else out_frame(e[0])
            b = out_start + len(src_frames) if i + 1 >= len(changes) else out_frame(changes[i + 1][0])
            sa, sb = int(a / FPS * RATE), int(b / FPS * RATE)
            if sb <= sa:
                continue
            idx = (np.arange(sb - sa) + (lo * RATE // FPS)) % len(loop)
            bed[sa:sb] = loop[idx] * e[3]
        if reverse:
            continue  # backwards footage gets the bed and the music only
        for frame, kind, name, vol, pitch, pan in events:
            if kind != "sfx" or frame < lo or frame > hi or name.startswith(SKIP_CUES):
                continue
            wav = os.path.join(CLIPS, "_sounds", name + ".wav")
            if not os.path.exists(wav):
                continue
            x = decode(wav)
            if abs(pitch - 1.0) > 1e-3:
                m = int(len(x) / pitch)
                src = np.arange(m) * pitch
                x = np.stack([np.interp(src, np.arange(len(x)), x[:, c]) for c in range(2)], axis=1).astype(np.float32)
            gains = np.array([min(1.0, 1.0 - pan), min(1.0, 1.0 + pan)], np.float32) * vol
            at = int(out_frame(frame) / FPS * RATE)
            end = min(n, at + len(x))
            if end > at:
                sfx[at:end] += x[: end - at] * gains

    track, start = s["music"]
    music = decode(os.path.join(MUSIC, track + ".wav"))[int(start * RATE):]
    if len(music) < n:
        music = np.concatenate([music, np.zeros((n - len(music), 2), np.float32)])
    music = music[:n].copy()
    length = total_frames / FPS
    t = np.arange(n) / RATE
    env = np.clip(t / 0.08, 0, 1) * np.clip((length - t) / 1.2, 0, 1)
    music *= env[:, None]

    # Balance: the effects sit s["sfx"] dB from the music and the ambience bed BED_DB under it, each measured where it
    # is sounding (sparse factory clicks and constant digging would be far apart on plain RMS).
    m_db, e_db, b_db = active_db(music, length), active_db(sfx, length), active_db(bed, length)
    if levels:
        return {"music": m_db, "sfx": e_db, "bed": b_db}
    # (capped: a few faint clicks in a quiet clip are not lifted up to effect level)
    sfx_gain = min(MAX_SFX_GAIN, 10 ** ((m_db + s["sfx"] - e_db) / 20)) if e_db > -90 else 0.0
    bed_gain = 10 ** ((m_db + BED_DB - b_db) / 20) if b_db > -90 else 0.0
    out = music + sfx * sfx_gain + bed * bed_gain
    rms = np.sqrt(np.mean(out[: int(length * RATE)] ** 2)) + 1e-9
    out *= 10 ** (-14.5 / 20) / rms  # roughly -14 LUFS for music-led material
    peak = 0.89
    over = np.abs(out) > peak
    out[over] = np.sign(out[over]) * (peak + (1 - peak) * np.tanh((np.abs(out[over]) - peak) / (1 - peak)))
    out[int(length * RATE):] = 0
    return out[: int(length * RATE)]


# ------------------------------------------------------------------------------------------------ assembly


def build_frames(s):
    """[(source path or Image, timeline info)] and the (clip, source frames, output start) list for the sound."""
    items, timeline = [], []
    for seg in s["segs"]:
        clip, start, seconds = seg[:3]
        opt = seg[3] if len(seg) > 3 else None
        frames = clip_frames(clip, start, seconds, reverse=opt == "rev", speed=opt if isinstance(opt, (int, float)) else 1)
        timeline.append((clip, [f for _, f in frames], len(items)))
        for path, f in frames:
            items.append((path, clip, f))
    return items, timeline


def render_short(s, preview=False):
    items, timeline = build_frames(s)
    texts = [(a, b, text_layer(t, style[0] if style else "hook")) for a, b, t, *style in s["texts"]]
    depths = read_depths(s["counter"]) if s["counter"] else {}
    biome_seen, biome_since = None, 0.0
    body = len(items)
    end = end_card_frames(items[-1][0], END_SECONDS)
    total = body + len(end)
    os.makedirs(OUT, exist_ok=True)
    name = os.path.join(OUT, s["id"])

    def frame_at(i):
        nonlocal biome_seen, biome_since
        if i >= body:
            return end[i - body]
        path, clip, f = items[i]
        im = Image.open(path).convert("RGB")
        t = i / FPS
        if clip == s["counter"] and f in depths:
            depth, biome = depths[f]
            if biome != biome_seen:
                biome_seen, biome_since = biome, t
            im.paste(depth_layer(depth, biome, t - biome_since), (0, 0), depth_layer(depth, biome, t - biome_since))
        for a, b, layer in texts:
            if a <= t < b:
                paste_text(im, layer, t - a, b - t)
        return im

    if preview:
        picks = [int(k * (total - 1) / 7) for k in range(8)]
        thumbs = [frame_at(i).resize((270, 480), Image.BILINEAR) for i in picks]
        sheet = Image.new("RGB", (270 * 8 + 6 * 7, 480), (40, 40, 40))
        for k, th in enumerate(thumbs):
            sheet.paste(th, (k * 276, 0))
        sheet.save(name + "_preview.jpg", quality=88)
        return name + "_preview.jpg", total / FPS

    audio = mix_audio(s, timeline, body)
    audio = np.concatenate([audio, np.zeros((int(len(end) / FPS * RATE), 2), np.float32)])
    # The end card keeps the music's tail: fade it over the card instead of cutting.
    track, start = s["music"]
    tail = decode(os.path.join(MUSIC, track + ".wav"))[int(start * RATE) + int(body / FPS * RATE):][: int(len(end) / FPS * RATE)]
    if len(tail):
        rms_scale = 10 ** (-14.5 / 20) / (np.sqrt(np.mean(decode(os.path.join(MUSIC, track + ".wav")) ** 2)) + 1e-9) * 0.8
        fade = np.linspace(1, 0, len(tail))[:, None] ** 1.5
        audio[-len(end) * RATE // FPS:][: len(tail)] += tail * fade * rms_scale * 0.6
    audio = np.clip(audio, -0.95, 0.95)
    wav = name + "_audio.f32"
    audio.astype(np.float32).tofile(wav)

    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    cmd = [ffmpeg, "-y", "-loglevel", "error",
           "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}", "-r", str(FPS), "-i", "-",
           "-f", "f32le", "-ar", str(RATE), "-ac", "2", "-i", wav,
           "-map", "0:v", "-map", "1:a", "-shortest",
           "-c:v", "libx264", "-preset", "slow", "-crf", "16", "-maxrate", "24M", "-bufsize", "48M",
           "-pix_fmt", "yuv420p", "-profile:v", "high", "-level", "4.2", "-r", str(FPS),
           "-g", str(FPS), "-c:a", "aac", "-b:a", "256k", "-ar", str(RATE),
           "-movflags", "+faststart", name + ".mp4"]
    proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    try:
        for i in range(total):
            proc.stdin.write(frame_at(i).tobytes())
    finally:
        proc.stdin.close()
        proc.wait()
        os.remove(wav)
    if proc.returncode:
        raise RuntimeError(f"ffmpeg failed on {s['id']}")
    return name + ".mp4", total / FPS


def write_posting_guide(shorts):
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, "posting.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["file", "seconds", "youtube_title", "caption", "hashtags"])
        for s in shorts:
            w.writerow([s["id"] + ".mp4", f"{length(s):.1f}", s["post"]["title"], s["post"]["caption"], s["post"]["tags"]])
    lines = ["# Antarium 2 short-form videos: posting guide", "",
             "Same file for TikTok, YouTube Shorts and Instagram Reels. YouTube: the title below, the caption as the description.",
             "TikTok / Instagram: caption + hashtags. Put the Steam link in the bio (TikTok and Instagram don't link from captions;",
             "YouTube: link the Steam page as the Short's related video or in the description).", ""]
    for s in shorts:
        p = s["post"]
        lines += [f"## {s['id']}.mp4 ({length(s):.0f} s)", "", f"**Title:** {p['title']}", "", f"**Caption:** {p['caption']}", "",
                  f"**Hashtags:** {p['tags']}", ""]
    with open(os.path.join(OUT, "posting_guide.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines))


def length(s):
    return sum(seg[2] / (seg[3] if len(seg) > 3 and isinstance(seg[3], (int, float)) else 1) for seg in s["segs"]) + END_SECONDS


def job(args):
    s, preview = args
    try:
        path, seconds = render_short(s, preview)
        return f"{path}: {seconds:.1f} s" + ("" if preview else f", {os.path.getsize(path) / 1048576:.1f} MB")
    except Exception as e:  # report and carry on with the others
        return f"{s['id']}: FAILED {e}"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("only", nargs="*", help="ids (or parts of ids) to make; default every short")
    parser.add_argument("--list", action="store_true", help="print the slate and stop")
    parser.add_argument("--preview", action="store_true", help="contact sheets instead of videos")
    parser.add_argument("--jobs", type=int, default=max(1, min(4, (os.cpu_count() or 2) // 3)))
    args = parser.parse_args()
    chosen = [s for s in SHORTS if not args.only or any(o in s["id"] for o in args.only)]
    if args.list:
        for s in chosen:
            print(f"{s['id']:28s} {length(s):5.1f} s  {s['post']['title']}")
        print(f"{len(chosen)} shorts")
        return
    write_posting_guide(SHORTS)
    with ProcessPoolExecutor(max_workers=args.jobs) as pool:
        for line in pool.map(job, [(s, args.preview) for s in chosen]):
            print(line, flush=True)


if __name__ == "__main__":
    sys.exit(main())
