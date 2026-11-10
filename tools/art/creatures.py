"""Enemies (Art/Enemies/, two walk frames each) and the surface layer (Art/Surface/: plants, the bird, aphids).
Everything faces right and stands on its bottom row."""
from pix import keys, sprite, strip


def two(frames, key):
    return strip([sprite(rows, key) for rows in frames])


# ---------------------------------------------------------------- enemies

def termite():
    """Termite raider, 11 x 6: a soft pale body behind a hard amber head with dark jaws."""
    key = keys("6=silk:5 5=silk:4 4=silk:3 3=silk:1 a=silk:2 H=copper:2 h=copper:4 k=wood:0")
    body = [".........a.", ".566565.Hh.", "5655454HhHk", ".44434.HHk."]
    return two([body + [".3.3.3.3...", "..3.3.3.3.."], body + ["..3.3.3.3..", ".3.3.3.3..."]], key)


def centipede():
    """Centipede, 16 x 6: banded copper segments, a dark red head with fangs, legs rippling."""
    key = keys("5=copper:4 4=copper:3 3=copper:2 2=copper:1 1=copper:1 H=magma:2 e=#fff0a0 k=wood:0 a=magma:1")
    body = ["..............aa", ".54545454545455.", "4343434343434HeH", ".232323232323HHk"]
    return two([body + ["1.1.1.1.1.1.1...", ".1.1.1.1.1.1.1.."], body + [".1.1.1.1.1.1.1..", "1.1.1.1.1.1.1..."]], key)


def spider():
    """Cave spider, 11 x 7: a round violet body, red eyes, legs arching to both sides."""
    key = keys("6=fungus:5 5=fungus:4 4=fungus:3 3=fungus:2 2=fungus:1 v=fungus:3 E=#ff4848")
    a = ["...4554....", "..456554...", ".34555543E.", "v34444432Ev", "v.v22222v.v", "v.v.v.v.v.v", "..........."]
    b = ["...4554....", "..456554...", ".34555543E.", ".34444432E.", "vvv22222vvv", ".v.v...v.v.", "v.......v.."]
    return two([a, b], key)


def mite():
    """Fungal mite, 6 x 5: a tiny pink ball on hair legs."""
    key = keys("6=fungus:6 5=fungus:5 4=fungus:4 3=fungus:3 2=fungus:2 e=#2a0a1a")
    body = [".4554.", "456654", "34554e", ".3443."]
    return two([body + ["2.22.2"], body + [".2..2."]], key)


def fire_beetle():
    """Fire beetle, 12 x 7: a black shell with glowing seams and a burning eye."""
    key = keys("4=basalt:4 3=basalt:3 2=basalt:1 1=basalt:4 H=basalt:5 O=magma:5 o=magma:3 E=magma:6")
    body = ["...34443....", "..34O4O443..", ".344o4o4443H", ".23O3O3O32HE", "..22222222H."]
    return two([body + [".1.1..1.1...", "..1.11.1...."], body + ["..1.11.1....", ".1.1..1.1..."]], key)


def guardian():
    """Ruin guardian, 14 x 12: a mossy stone golem with a crystal eye."""
    key = keys("5=ruin:4 4=ruin:3 3=ruin:2 2=ruin:1 m=moss:2 M=moss:3 C=crystal:5 c=crystal:4")
    top = [
        "....455554....",
        "...45555543...",
        "...45CC5543...",
        "...44cC4443...",
        "..3444444433..",
        ".4555555555543",
        "45.4M4444443.4",
        "44.4mm444m43.3",
        "33.344444433.2",
    ]
    return two([top + ["...44....44...", "...44....44...", "..344....443.."],
                top + ["....44..44....", "...444..444...", ".............."]], key)


def wolf_spider():
    """Wolf spider, 12 x 7: brown and hairy with pale stripes and a row of yellow eyes; hunts at night."""
    key = keys("5=wood:5 4=wood:4 3=wood:3 v=wood:2 s=bone:3 e=#101010 E=gold:4")
    a = ["....3443....", "..345s5s43..", ".34555s543eE", "v3445s4443Ee", "v.v33333v.v.", "v.v.v.v.v.v.", "............"]
    b = ["....3443....", "..345s5s43..", ".34555s543eE", ".3445s4443Ee", "vvv33333vvv.", ".v.v...v.v..", "v.......v..."]
    return two([a, b], key)


ENEMIES = {
    "termite": termite, "centipede": centipede, "spider": spider, "mite": mite, "fire_beetle": fire_beetle,
    "guardian": guardian, "wolf_spider": wolf_spider,
}


# ---------------------------------------------------------------- surface

def sway(rows, top, key):
    """Two frames: the plant as drawn, and with its top `top` rows leaning one pixel to the right."""
    leaning = [("." + row[:-1]) if i < top else row for i, row in enumerate(rows)]
    return strip([sprite(rows, key), sprite(leaning, key)])


PLANT = "5=grass:5 4=grass:4 3=grass:3 2=grass:1 s=grass:2 "


def plant_fern():
    return sway([
        ".....5.....",
        "....454....",
        ".5..454..5.",
        ".45.343.54.",
        "..4534354..",
        "5..43434..5",
        "45.43334.54",
        ".454333454.",
        "..4433344..",
        ".....3.....",
        ".....2.....",
        ".....2.....",
    ], 5, keys(PLANT))


def plant_bush():
    return sway([
        "....45554....",
        "..455545554..",
        ".45544554554.",
        "4554b54455454",
        "4544554b54544",
        ".34454445443.",
        "..33w444w33..",
        "....w...w....",
        "....w...w....",
    ], 3, keys(PLANT + "b=red:3 w=wood:3"))


def plant_flower():
    return sway([
        "..fFf..",
        ".fFyFf.",
        ".FyYyF.",
        "..fFf..",
        "...s...",
        "..4s...",
        ".45s...",
        "...s4..",
        "...s54.",
        "...s...",
        "..4s...",
        ".45s...",
        "...s4..",
        "...s54.",
        "...s...",
        "...s...",
    ], 7, keys(PLANT + "f=petal:3 F=petal:2 y=gold:4 Y=gold:5"))


BIRD = keys("4=stone:2 3=stone:1 b=stone:0 B=stone:3 e=#ffffff o=gold:3")


def bird():
    """The bird, 14 x 6: wings up, wings level. A dark shape against the sky with a yellow beak."""
    up = ["43..........34", ".443......344.", "..4433..3344..", "...43bbbb34...", ".....bBBbeo...", "......bb......"]
    level = ["..............", "..............", "444433bb334444", ".3333bBBbeo33.", "......bb......", ".............."]
    return two([up, level], BIRD)


def bird_dive():
    return sprite(["......43", ".....43.", "..433b..", ".3bBBbeo", "43bbb...", "3......."], BIRD)


def aphid():
    """Ranch aphid, 3 x 2, two frames."""
    key = keys("5=grass:5 4=grass:4 3=grass:2")
    return two([[".55", "543"], ["55.", "543"]], key)


SURFACE = {"plant_fern": plant_fern, "plant_bush": plant_bush, "plant_flower": plant_flower, "bird": bird, "bird_dive": bird_dive, "aphid": aphid}
