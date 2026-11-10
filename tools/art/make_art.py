"""Writes the game's pixel art as PNG files into Assets/_Antarium/Art/ (see docs/ART_STYLE.md).

Usage (from the project root):
    python tools/art/make_art.py            # everything
    python tools/art/make_art.py worker     # only the pieces whose name contains "worker"

Each PNG is drawn from the pixel maps and painters in this folder with the palette in palette.py. The PNGs are the
deliverable: after a run, use Antarium -> Art -> Import Overrides in Unity. A PNG edited by hand (Aseprite etc.)
is overwritten by the next run of its piece, so remove the piece from PIECES once it is hand-edited.
Preview sheets go to Captures/art/ (not committed). Needs Pillow and numpy.
"""
import sys

import numpy as np

import buildings
import colony
import creatures
import planned
import terrain
import ui
from palette import RAMPS
from pix import save, sheet

# name -> (path under Art/, painter). The path without ".png" is the name the game looks the piece up by.
PIECES = {
    "ant_worker": ("Colony/ant_worker.png", colony.worker),
    "queen": ("Colony/queen.png", colony.queen),
    "ant_soldier": ("Colony/ant_soldier.png", colony.soldier),
    "ant_miner": ("Colony/ant_miner.png", colony.miner),
    "ant_hauler": ("Colony/ant_hauler.png", colony.hauler),
    "ant_nurse": ("Colony/ant_nurse.png", colony.nurse),
    "ant_builder": ("Colony/ant_builder.png", colony.builder),
    "ant_scout": ("Colony/ant_scout.png", colony.scout),
    "ant_engineer": ("Colony/ant_engineer.png", colony.engineer),
    "princess": ("Colony/princess.png", colony.princess),
    "egg": ("Colony/egg.png", colony.egg),
    "pile": ("Colony/pile.png", colony.pile),
    "carry": ("Colony/carry.png", colony.carry),
    "golden_beetle": ("Colony/golden_beetle.png", colony.golden_beetle),
    "golden_aphid": ("Colony/golden_aphid.png", colony.golden_aphid),
    "skin_frame": ("UI/Skin/frame.png", ui.frame),
    "skin_button": ("UI/Skin/button.png", ui.button),
    "skin_button_down": ("UI/Skin/button_down.png", ui.button_down),
    "skin_inset": ("UI/Skin/inset.png", ui.inset),
}
PIECES.update({"icon_" + name: (f"UI/Icons/{name}.png", ui.icon(name)) for name in ui.ICONS})
ONE_CELL = ["silk_belt", "root_lift", "iron_belt", "iron_lift", "splitter", "filter", "unloader", "chest", "pheromone_emitter",
            "resin_gate", "iron_gate", "resin_trap", "guard_post", "earth_wall", "pump"]
PIECES.update({"icon_building_" + n: (f"UI/Icons/building_{n}.png", ui.building_icon(buildings.SPRITES[n])) for n in ONE_CELL})
CASTES = {"worker": 12, "soldier": 14, "miner": 13, "hauler": 13, "nurse": 12, "builder": 12, "scout": 12, "engineer": 12}
PIECES.update({"icon_caste_" + n: (f"UI/Icons/caste_{n}.png", ui.caste_icon(getattr(colony, n), w)) for n, w in CASTES.items()})
PIECES["title_emblem"] = ("UI/title_emblem.png", ui.title_emblem(colony.soldier, ui.icon("princesses")))
PIECES.update({"building_" + name: (f"Buildings/{name}.png", painter) for name, painter in buildings.SPRITES.items()})
PIECES.update({"enemy_" + name: (f"Enemies/{name}.png", painter) for name, painter in creatures.ENEMIES.items()})
PIECES.update({"surface_" + name: (f"Surface/{name}.png", painter) for name, painter in creatures.SURFACE.items()})
PIECES.update({"terrain_" + name: (f"Terrain/{name}.png", painter) for name, painter in terrain.TILES.items()})
# Art for planned content (10.4 - 10.6): written and imported like the rest, used by nothing yet.
PIECES.update({"planned_" + name: piece for name, piece in planned.PIECES.items()})


def palette_sheet():
    """Every ramp as a row of swatches (dark to light), for review: Captures/art/palette.png."""
    rows = []
    for name, ramp in RAMPS.items():
        row = np.zeros((2, 8, 4), np.uint8)
        for i, color in enumerate(ramp):
            row[:, i] = color
        rows.append((name, row))
    return sheet(rows, "palette", scale=14, columns=4)


def main(filters):
    palette_sheet()
    done = []
    for name, (path, painter) in PIECES.items():
        if filters and not any(f in name for f in filters):
            continue
        img = painter()
        save(img, path)
        done.append((name, img))
    if done:
        print("wrote", len(done), "pieces; preview:", sheet(done, "pieces", scale=6, columns=5))


if __name__ == "__main__":
    main(sys.argv[1:])
