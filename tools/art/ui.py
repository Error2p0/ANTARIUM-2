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


# ---------------------------------------------------------------- more icons (step 7)

ICONS.update({
    # Items.
    "item_chitin": (keys("5=silk:3 4=silk:2 3=silk:1 2=silk:0 s=silk:0"), [
        "..........",
        "...4554...",
        "..455554..",
        ".45555443.",
        ".4s4444s3.",
        ".34444433.",
        ".3s3333s2.",
        "..233322..",
        "...2222...",
        "..........",
    ]),
    "item_crushed_copper": (keys("5=copper:4 4=copper:3 3=copper:2 2=copper:1"), [
        "..........",
        "..........",
        "....45....",
        "...4543...",
        "..453354..",
        "..454354..",
        ".45434543.",
        ".34343432.",
        "..222222..",
        "..........",
    ]),
    "item_crushed_iron": (keys("5=iron:5 4=iron:4 3=iron:3 2=iron:2"), [
        "..........",
        "..........",
        "....45....",
        "...4543...",
        "..453354..",
        "..454354..",
        ".45434543.",
        ".34343432.",
        "..222222..",
        "..........",
    ]),
    "item_fossil": (keys("5=bone:4 4=bone:3 3=bone:2 1=stone:2"), [
        "..........",
        "...5555...",
        "..541145..",
        ".54155145.",
        ".51541515.",
        ".51514515.",
        ".54155514.",
        "..5411143.",
        "...44443..",
        "..........",
    ]),
    "item_fungus": (keys("6=fungus:6 5=fungus:5 4=fungus:4 3=fungus:2 s=fungus:6 w=silk:4 v=silk:2"), [
        "..........",
        "...4554...",
        "..456654..",
        ".45s555s4.",
        ".45555543.",
        "..333333..",
        "....wv....",
        "....wv....",
        "...wwv....",
        "..........",
    ]),
    "item_grub": (keys("6=silk:5 5=silk:4 4=silk:2 h=copper:3"), [
        "..........",
        "...5665...",
        "..56665h..",
        ".5655.....",
        ".565......",
        ".5655..4..",
        ".4565554..",
        "..445554..",
        "...4444...",
        "..........",
    ]),
    "item_honeydew": (keys("6=gold:5 5=gold:4 4=gold:3 w=#ffffff"), [
        "..........",
        "....5.....",
        "....56....",
        "...5665...",
        "..5w6654..",
        "..566554..",
        "..565544..",
        "...5544...",
        "....44....",
        "..........",
    ]),
    "item_leaf": (keys("5=grass:5 4=grass:4 3=grass:2"), [
        ".......45.",
        ".....4554.",
        "....45534.",
        "...455354.",
        "..4553554.",
        "..453554..",
        ".4535544..",
        ".435544...",
        ".3444.....",
        "3.........",
    ]),
    "item_obsidian": (keys("5=void:4 4=void:2 3=void:1 2=void:0"), [
        "..........",
        "....45....",
        "...4543...",
        "..345432..",
        "..3454322.",
        ".33443222.",
        ".2334222..",
        "..22322...",
        "...222....",
        "..........",
    ]),
    "item_relic": (keys("5=ruin:4 4=ruin:3 3=ruin:1 k=gold:4"), [
        "..........",
        "..444444..",
        ".45555553.",
        ".45k55k53.",
        ".455kk553.",
        ".45k55k53.",
        ".45555553.",
        ".34444443.",
        "..333333..",
        "..........",
    ]),
    "item_root_fibre": (keys("5=wood:5 4=wood:4 s=silk:4"), [
        "..........",
        ".4.5.4.5..",
        ".4.5.4.5..",
        "..45.45...",
        "..4sss5...",
        "..45.45...",
        ".4.5.4.5..",
        ".4.5.4.54.",
        "4..5.4..5.",
        "..........",
    ]),
    "item_seed": (keys("6=sand:5 5=sand:4 4=sand:3 3=sand:1"), [
        "..........",
        "....45....",
        "...4554...",
        "..455654..",
        "..456554..",
        "..455554..",
        "..345543..",
        "...3443...",
        "....33....",
        "..........",
    ]),
    "item_star_fragment": (keys("6=void:5 5=void:4 4=void:3 w=#ffffff"), [
        "....5.....",
        "....65....",
        ".5.5665.5.",
        "..566665..",
        ".566w6654.",
        "..566654..",
        ".5.5654.4.",
        "....54....",
        "....4.....",
        "..........",
    ]),
    # Fixed entries.
    "sun": (keys("6=gold:5 5=gold:4 4=gold:3"), [
        "....5.....",
        ".5..5..5..",
        "..5...5...",
        "...4554...",
        "55.5665.55",
        "...5665...",
        "...4554...",
        "..5...5...",
        ".5..5..5..",
        "....5.....",
    ]),
    "moon": (keys("6=#f2feff 5=crystal:5 4=crystal:4"), [
        "..........",
        "...5665...",
        "..566.....",
        ".5665.....",
        ".5665.....",
        ".5665.....",
        ".56665....",
        "..56665.5.",
        "...5555...",
        "..........",
    ]),
    "princesses": (keys("6=gold:5 5=gold:4 4=gold:3 3=gold:2 k=red:3"), [
        "..........",
        ".5..5..5..",
        ".5.565.5..",
        ".56565654.",
        ".56666654.",
        ".55555554.",
        ".4k4k4k43.",
        ".44444443.",
        "..........",
        "..........",
    ]),
    "memory": (keys("6=gold:5 5=gold:4 4=gold:2"), [
        "..5...4...",
        "...5.4....",
        "....6.....",
        "...4.5....",
        "..4...5...",
        "..4...5...",
        "...4.5....",
        "....6.....",
        "...5.4....",
        "..5...4...",
    ]),
    # Toolbar.
    "tool_dig": (keys("5=iron:5 4=iron:4 w=wood:4"), [
        "..........",
        "..44444...",
        ".4....54..",
        "......554.",
        ".....w.54.",
        "....w...4.",
        "...w......",
        "..w.......",
        ".w........",
        "..........",
    ]),
    "tool_mine": (keys("5=stone:5 4=stone:4 3=stone:3 2=stone:2 o=#ffb040"), [
        "..........",
        "...3443...",
        "..3o4543..",
        ".3o45o432.",
        ".o45o4332.",
        ".24o43o22.",
        "..o332o2..",
        "...2222...",
        "..........",
        "..........",
    ]),
    "tool_build": (keys("5=iron:5 4=iron:4 w=wood:4"), [
        "..........",
        "......45..",
        ".....4555.",
        "....45554.",
        ".....w54..",
        "....w.4...",
        "...w......",
        "..w.......",
        ".w........",
        "..........",
    ]),
    "tool_scent": (keys("6=fungus:6 5=fungus:4 4=fungus:3"), [
        "..........",
        "....5.....",
        "...565..4.",
        "..56665..4",
        "..56665..4",
        "..56655.4.",
        "...555....",
        "..........",
        "..........",
        "..........",
    ]),
    "tool_possess": (keys("5=gold:4 6=gold:5 k=#1c0a0a"), [
        "..........",
        "..........",
        "...5555...",
        "..5....5..",
        ".5..66..5.",
        ".5.6kk6.5.",
        ".5..66..5.",
        "..5....5..",
        "...5555...",
        "..........",
    ]),
    "tool_castes": (keys("3=chitin:3 4=chitin:5 5=caste_miner:2 8=caste_miner:4 6=caste_hauler:2 7=caste_hauler:4"), [
        "..........",
        "..33......",
        ".3443..66.",
        ".3443.6776",
        "..33..6776",
        "....55.66.",
        "...5885...",
        "...5885...",
        "....55....",
        "..........",
    ]),
    "tool_research": (keys("5=iron:5 4=iron:4 v=void:3 V=void:5"), [
        "..........",
        "...4444...",
        "....45....",
        "....45....",
        "...4..5...",
        "..4.vv.5..",
        ".4.vVvv.5.",
        ".4vvvVvv5.",
        "..444455..",
        "..........",
    ]),
    "tool_flow": (keys("5=grass:5 4=grass:3"), [
        "..........",
        ".5...5....",
        "..5...5...",
        "...5...5..",
        "....5...5.",
        "...4...4..",
        "..4...4...",
        ".4...4....",
        "..........",
        "..........",
    ]),
    "tool_flight": (keys("6=#f4faff 5=#b4cce8 4=#7c98c4"), [
        "..........",
        ".....5555.",
        "...556664.",
        "..5666654.",
        ".56665544.",
        ".5665544..",
        ".55444....",
        "..44......",
        "..........",
        "..........",
    ]),
    # Abilities (by ability id).
    "ability_frenzy": (keys("6=magma:5 5=magma:4"), [
        ".....55...",
        "....565...",
        "...5665...",
        "..56665...",
        "..5666555.",
        "...55665..",
        "....5665..",
        "....565...",
        "...565....",
        "...55.....",
    ]),
    "ability_seismic_sense": (keys("6=crystal:6 5=crystal:4"), [
        "..........",
        "..555555..",
        ".5......5.",
        "5..5555..5",
        "..5....5..",
        ".....6....",
        "....666...",
        ".....6....",
        "..........",
        "..........",
    ]),
    "ability_royal_decree": (keys("p=silk:5 q=silk:2 5=gold:3 k=red:3"), [
        "..........",
        ".5555555..",
        ".5pppppp5.",
        "..pqqqqp..",
        "..pppppp..",
        "..pqqqpp..",
        "..pppppp..",
        ".5pppkpp5.",
        ".5555555..",
        "..........",
    ]),
    "ability_pheromone_burst": (keys("6=#ffffff 5=petal:3 4=petal:2"), [
        "....5.....",
        ".5..5..5..",
        "..5...5...",
        "...4554...",
        "55.5665.55",
        "...5665...",
        "...4554...",
        "..5...5...",
        ".5..5..5..",
        "....5.....",
    ]),
    # Buildings larger than a cell (the 8 x 8 ones use their sprite, see building_icon).
    "building_smelter": (keys("B=clay:1 C=clay:2 D=clay:3 E=clay:4 4=stone:4 3=stone:3 2=stone:2 1=stone:1 Y=magma:4 W=magma:5"), [
        "......33..",
        "......34..",
        "...CDD34..",
        "..CDEED3..",
        ".CDEEDDCB.",
        ".CD4443CB.",
        ".CD4YW3CB.",
        ".CD4WW3BB.",
        "3444YY4432",
        "2222222221",
    ]),
    "building_crusher": (keys("5=iron:5 4=iron:4 3=iron:3 2=iron:2 1=iron:1 x=iron:0 b=stone:3 c=stone:4 d=stone:5"), [
        ".35555553.",
        "..2xxxx2..",
        "3444444442",
        "3bccxxbcc2",
        "3cdcxxcdc2",
        "3ccbxxccb2",
        "3xxxxxxxx2",
        "3333333332",
        "2222222221",
        "..........",
    ]),
    "building_fungus_garden": (keys("5=fungus:5 4=fungus:4 3=fungus:2 s=fungus:6 w=silk:4 e=earth:4 d=earth:2"), [
        "..........",
        "..4554....",
        ".45s554.45",
        ".4555544s5",
        "..33w3.455",
        "....w..3w3",
        "....w...w.",
        "eeeeeeeeee",
        "dddddddddd",
        "..........",
    ]),
    "building_aphid_ranch": (keys("5=grass:5 4=grass:4 3=grass:2 g=grass:5 G=grass:2 w=wood:4 e=earth:4"), [
        "..........",
        "....5.....",
        "...54.55..",
        "...4355...",
        ".55.3.....",
        ".453..gg..",
        "...3.ggG..",
        "w..3.....w",
        "wwwwwwwwww",
        "eeeeeeeeee",
    ]),
    "building_archive": (keys("a=wood:1 b=wood:2 d=wood:4 x=wood:0 R=red:2 r=red:3 B=water:1 u=water:3 G=grass:1 g=grass:3 N=bone:4 n=bone:2 Y=gold:4 y=gold:3"), [
        "bddddddddb",
        "bRrBuGgRrb",
        "bRrBuGgRrb",
        "bddddddddb",
        "bxxxxxxxxb",
        "bxNNnxYyxb",
        "bddddddddb",
        "aaaaaaaaaa",
        "..........",
        "..........",
    ]),
    "building_nursery": (keys("d=wood:4 s=silk:2 6=silk:5 5=silk:4 4=silk:1 c=clay:3"), [
        "dddddddddd",
        ".s......s.",
        ".s......s.",
        ".s.5665.s.",
        ".s.5665.s.",
        ".55.66.55.",
        "..555555..",
        "...4444...",
        "cccccccccc",
        "..........",
    ]),
    "building_princess_chamber": (keys("5=gold:4 4=gold:3 3=gold:2 2=gold:1 w=silk:5 s=silk:2 c=clay:3"), [
        "...4554...",
        "..45..54..",
        ".45....53.",
        ".4......3.",
        "45......32",
        "4...ww...2",
        "4..wwww..2",
        "4.ssssss.2",
        "cccccccccc",
        "..........",
    ]),
    "building_water_wheel": (keys("4=wood:3 w=wood:5 k=iron:3 u=water:4"), [
        "...u44u...",
        "..4.w..4..",
        ".4..w...4.",
        "u...w....u",
        "4wwwkwwww4",
        "4...w....4",
        "u...w....u",
        ".4..w...4.",
        "..4.w..4..",
        "...u44u...",
    ]),
})


def padded(img, width, height):
    """An image centred on a clear canvas (bottom-aligned when the spare rows are odd)."""
    out = blank(width, height)
    h, w = img.shape[:2]
    x, y = (width - w) // 2, height - h - (height - h) // 2
    out[y:y + h, x:x + w] = img
    return out


def building_icon(painter):
    """The icon of a one-cell building: the first frame of its sprite on a 10 x 10 canvas."""
    return lambda: padded(painter()[:, :8], 10, 10)


def caste_icon(painter, width):
    """A caste's icon: the first walk frame on a 14 x 8 canvas (shown at 28 x 16)."""
    return lambda: padded(painter()[:, :width], 14, 8)


def title_emblem(soldier, crown):
    """The title screen's crest, 44 x 12: two soldiers facing a crown."""
    def paint():
        img = blank(44, 12)
        left = soldier()[:, :14]
        img[4:12, 1:15] = left
        img[4:12, 29:43] = left[:, ::-1]
        img[1:11, 17:27] = crown()
        return img
    return paint
