"""Building sprites. Sizes are cells x 8 px; frames sit side by side (idle first, then working / animation)."""
from pix import keys, sprite, strip

# Smelter, 24 x 24 (3 x 3 cells), frames: idle, working. A clay beehive kiln on a stone slab with a stone-lined
# mouth and a chimney. Light from the top left; while it works the mouth burns and the lintel stones glow.
KILN = keys(
    "A=clay:0 B=clay:1 C=clay:2 D=clay:3 E=clay:4 F=clay:5 "
    "1=stone:1 2=stone:2 3=stone:3 4=stone:4 5=stone:5 "
    "x=soot:0 y=soot:2 z=soot:3 "
    "r=magma:2 o=magma:3 Y=magma:4 W=magma:5 H=magma:6 "
    "g=#c98a5a G=#f2b47a"
)
SMELTER_IDLE = [
    "..............344432....",
    "..............233321....",
    "...............3432.....",
    "...............3432.....",
    "...............2321.....",
    ".........CDEEDC3432.....",
    ".......CDEFFEDD3432.....",
    "......CDEFEEDDD2321.....",
    ".....CDEFEEDDDD3432.....",
    "....CDEEEDDDDDCBBBCB....",
    "...BDDDCCCCCCCCCCBBBA...",
    "...CDEEDDDDDDDDDCCCBA...",
    "..CDEEDDD455443DDCCCBA..",
    "..CDEDDD45xxxx43DCCCBA..",
    "..BCCCCC4xxxxxx3CCBBBA..",
    ".CDEDDDD5xxxxxx3DDCCCBA.",
    ".CDEDCDD4xxxxxx3DCCCCBA.",
    ".CDDDDDD4xyyyyx2DCCCBBA.",
    ".BCCCCCC3xyzzyx2CCBBBBA.",
    ".CDDDDCC4yzzzzy2CCCCBBA.",
    "455545545222222455445432",
    "343334333433343334333221",
    "233323332333233323332211",
    "122212221222122212221111",
]
SMELTER_WORKING = [
    "..............344432....",
    "..............233321....",
    "...............3432.....",
    "...............3432.....",
    "...............2321.....",
    ".........CDEEDC3432.....",
    ".......CDEFFEDD3432.....",
    "......CDEFEEDDD2321.....",
    ".....CDEFEEDDDD3432.....",
    "....CDEEEDDDDDCBBBCB....",
    "...BDDDCCCCCCCCCCBBBA...",
    "...CDEEDDDDDDDDDCCCBA...",
    "..CDEEDDDGGGGggDDCCCBA..",
    "..CDEDDDGGroorggDCCCBA..",
    "..BCCCCCGroYYorgCCBBBA..",
    ".CDEDDDEGoYYYYogDDCCCBA.",
    ".CDEDCDEGoYWWYogDCCCCBA.",
    ".CDDDDDEGYWWWWYgDCCCBBA.",
    ".BCCCCCDGYWHHWYgCCBBBBA.",
    ".CDDDDCDGWHHHHWgCCCCBBA.",
    "45554554GYWWWWYg55445432",
    "343334334g3334g334333221",
    "233323332333233323332211",
    "122212221222122212221111",
]


def smelter():
    return strip([sprite(SMELTER_IDLE, KILN), sprite(SMELTER_WORKING, KILN)])
