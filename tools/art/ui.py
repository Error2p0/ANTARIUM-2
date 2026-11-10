"""UI art: the 9-slice skin (Art/UI/Skin) and the 10 x 10 icons (Art/UI/Icons). The UI draws art pixels at 2 screen
pixels (x UI scale), so an icon is 20 px on screen and a 4 px frame border is 8 px."""
import numpy as np

from palette import RAMPS
from pix import blank, keys, sprite


def rgba(color, alpha):
    return (color[0], color[1], color[2], int(round(alpha * 255)))


# ---------------------------------------------------------------- skin

def frame():
    """Panel frame, slice 4: dark outline with cut corners, a raised two-tone band (lit from the top left) with a
    stud in each corner, an inner shadow line, and the panel fill baked in (the element's own background colour and
    border are switched off in the stylesheet)."""
    U = RAMPS["ui"]
    key = {
        ".": (0, 0, 0, 0), "0": U[0], "6": U[6], "5": U[5], "4": U[4], "3": U[3],
        "S": U[7], "f": rgba(U[1], 0.93),
    }
    rows = [
        ".0000000000.",
        "066666666650",
        "06S555555S30",
        "065000000430",
        "0650ffff0430",
        "0650ffff0430",
        "0650ffff0430",
        "0650ffff0430",
        "065000000430",
        "06S444444S30",
        "053333333330",
        ".0000000000.",
    ]
    return sprite(rows, key)


def overlay(rows):
    """Bevel overlays are translucent warm white / black, drawn over an element's own background colour inside its
    CSS border (so every state colour keeps working). The UI blends in linear space, where a little white goes a
    long way and black needs a lot: hence the tiny highlight alphas and the heavy shadow alphas."""
    key = {
        ".": (0, 0, 0, 0), "c": (0, 0, 0, 190), "H": (255, 240, 210, 11), "h": (255, 240, 210, 5),
        "S": (0, 0, 0, 165), "s": (0, 0, 0, 110), "t": (0, 0, 0, 70),
    }
    return sprite(rows, key)


def button():
    """Raised button, slice 2: a lit line along the top, a two-row shadow lip along the bottom, dark inner corners."""
    return overlay([
        "cHHHHHc",
        ".......",
        ".......",
        ".......",
        ".......",
        "ttttttt",
        "cSSSSSc",
    ])


def button_down():
    """Pressed button: shadow along the top, a faint light along the bottom."""
    return overlay([
        "cSSSSSc",
        "ttttttt",
        ".......",
        ".......",
        ".......",
        ".......",
        "chhhhhc",
    ])


def inset():
    """Sunken slot (bars, value boxes), slice 1: shaded top and left, a faint light on the bottom and right."""
    return overlay([
        "SSSS",
        "t..h",
        "t..h",
        "thhh",
    ])


# ---------------------------------------------------------------- icons (10 x 10)

ROCK = "1=stone:1 2=stone:2 3=stone:3 4=stone:4 5=stone:5 "

ICONS = {
    # Fixed entries of the resource bar.
    "food": (keys("5=grass:5 4=grass:4 3=grass:3 2=grass:2 H=sand:5 S=sand:4 s=sand:3 d=sand:1"), [
        "......45..",
        ".....454..",
        "....5443..",
        "....32....",
        "...sSSs...",
        "..sSHHSs..",
        "..sSHSSs..",
        "..dsSSsd..",
        "...dssd...",
        "..........",
    ]),
    "ants": (keys("a=chitin:2 2=chitin:2 3=chitin:3 4=chitin:4 5=chitin:5 6=chitin:6 e=#1c0a0a k=#f0d49c"), [
        ".a......a.",
        "..a....a..",
        "...a56a...",
        "..455654..",
        ".4e5555e4.",
        ".34555543.",
        "..344443..",
        "..k3223k..",
        "...kk.kk..",
        "..........",
    ]),
    "eggs": (keys("6=silk:5 5=silk:4 4=silk:3 3=silk:2 2=silk:1"), [
        "..........",
        "....55....",
        "...5665...",
        "..556655..",
        "..566555..",
        "..455554..",
        "..445544..",
        "...3443...",
        "....33....",
        "..........",
    ]),
    "storage": (keys("5=wood:5 4=wood:4 3=wood:3 2=wood:2 1=wood:1 i=iron:4 j=iron:2 k=gold:4"), [
        "..........",
        "..444444..",
        ".45555554.",
        ".4i5555i4.",
        ".2j2222j2.",
        ".4i44k4i4.",
        ".4i4444i4.",
        ".3j3333j3.",
        ".11111111.",
        "..........",
    ]),
    "knowledge": (keys("3=void:3 4=void:4 5=void:5 6=#ffffff"), [
        "....4.....",
        "....5.....",
        "...454....",
        "..35653...",
        "455666554.",
        "..35653...",
        "...454....",
        "....5.....",
        "....4.....",
        "..........",
    ]),
    # Items.
    "item_copper_ore": (keys(ROCK + "o=copper:2 O=copper:3 P=copper:4"), [
        "..........",
        "...3443...",
        "..3455O43.",
        ".345OP432.",
        ".34Oo4332.",
        ".2433OP32.",
        ".223oO322.",
        "..2222o1..",
        "...1111...",
        "..........",
    ]),
    "item_iron_ore": (keys(ROCK + "o=iron:4 O=iron:5 P=iron:6 r=clay:2 R=clay:3"), [
        "..........",
        "...RRR3...",
        "..RR5O43..",
        ".3R5OP432.",
        ".34OorR32.",
        ".2433OP32.",
        ".2r3oO3r2.",
        "..2r22o1..",
        "...1111...",
        "..........",
    ]),
    "item_coal": (keys("0=basalt:0 1=basalt:1 2=basalt:2 3=basalt:3 4=basalt:4 5=basalt:5 w=iron:5"), [
        "..........",
        "...2332...",
        "..24w432..",
        ".23w43221.",
        ".2343221..",
        ".12322321.",
        ".11221w21.",
        "..111211..",
        "...0000...",
        "..........",
    ]),
    "item_copper": (keys("1=copper:0 2=copper:1 3=copper:2 4=copper:3 5=copper:4 6=copper:5"), [
        "..........",
        "..........",
        "...555555.",
        "..5666665.",
        ".55555554.",
        ".44444443.",
        ".33333332.",
        ".2222222..",
        "..........",
        "..........",
    ]),
    "item_iron": (keys("1=iron:1 2=iron:2 3=iron:3 4=iron:4 5=iron:5 6=iron:6"), [
        "..........",
        "..........",
        "...555555.",
        "..5666665.",
        ".55555554.",
        ".44444443.",
        ".33333332.",
        ".2222222..",
        "..........",
        "..........",
    ]),
    "item_crystal_shard": (keys("1=crystal:1 2=crystal:2 3=crystal:3 4=crystal:4 5=crystal:5 6=crystal:6"), [
        ".....6....",
        "....564...",
        "....5643..",
        "...56443..",
        "...56433..",
        "..5564332.",
        "..5544322.",
        "...54332..",
        "....432...",
        ".....2....",
    ]),
    "item_amber": (keys("1=gold:0 2=gold:1 3=gold:2 4=gold:3 5=gold:4 6=gold:5 k=#3a1c08"), [
        "..........",
        "...4554...",
        "..456654..",
        ".44566544.",
        ".34565443.",
        ".3445k443.",
        ".23444k32.",
        "..233332..",
        "...2222...",
        "..........",
    ]),
}


def icon(name):
    key, rows = ICONS[name]
    return lambda: sprite(rows, key)
