"""Generate in-map textures for TSC check-spot buildings.

Flat-shaded sprites with dark outlines, drawn at 4x and downscaled, in the
same hand as the world-map icon generator. Output 128x128 RGBA into
Textures/Things/Building/.
"""
import os
import math
from PIL import Image, ImageDraw

SS = 4
SIZE = 128
CANVAS = SIZE * SS
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "Textures", "Things", "Building")

OUTLINE = (44, 38, 32, 255)
STONE = (138, 134, 126, 255)
STONE_DARK = (108, 104, 97, 255)
MARBLE = (216, 212, 202, 255)
MARBLE_DARK = (184, 179, 168, 255)
RUNE = (72, 60, 48, 255)
EMBER = (214, 120, 52, 255)
DIRT = (122, 96, 66, 255)

def canvas():
    img = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)

def W(f):
    return int(f * CANVAS)

LW = W(0.02)

def save(img, name, out_dir=None):
    img = img.resize((SIZE, SIZE), Image.LANCZOS)
    target = out_dir or OUT_DIR
    os.makedirs(target, exist_ok=True)
    path = os.path.join(target, name + ".png")
    img.save(path)
    print("wrote", path)


ITEM_DIR = os.path.join(os.path.dirname(__file__), "..", "Textures", "Things", "Item")


# ------------------------------------------------------------- graven stone
def make_graven_stone():
    img, d = canvas()
    # ground shadow
    d.ellipse([W(0.18), W(0.74), W(0.82), W(0.92)], fill=(0, 0, 0, 60))
    # standing slab, rounded top, slightly tapered
    d.polygon([(W(0.30), W(0.82)), (W(0.33), W(0.30)), (W(0.40), W(0.18)),
               (W(0.60), W(0.18)), (W(0.67), W(0.30)), (W(0.70), W(0.82))],
              fill=STONE, outline=OUTLINE, width=LW)
    # left face shading
    d.polygon([(W(0.30), W(0.82)), (W(0.33), W(0.30)), (W(0.40), W(0.18)),
               (W(0.44), W(0.18)), (W(0.40), W(0.30)), (W(0.38), W(0.82))],
              fill=STONE_DARK)
    # road-script: stacked carved lines with tick marks
    for i, y in enumerate((0.32, 0.42, 0.52, 0.62)):
        d.line([(W(0.42), W(y)), (W(0.62), W(y))], fill=RUNE, width=LW)
        for tx in (0.46, 0.52, 0.58):
            d.line([(W(tx), W(y - 0.03)), (W(tx), W(y))], fill=RUNE, width=int(LW * 0.8))
    save(img, "TSC_GravenStone")


# ------------------------------------------------------------- shrine altar
def make_shrine_altar():
    img, d = canvas()
    d.ellipse([W(0.14), W(0.70), W(0.86), W(0.94)], fill=(0, 0, 0, 60))
    # plinth
    d.polygon([(W(0.30), W(0.84)), (W(0.34), W(0.56), ), (W(0.66), W(0.56)), (W(0.70), W(0.84))],
              fill=MARBLE_DARK, outline=OUTLINE, width=LW)
    # basin bowl
    d.ellipse([W(0.16), W(0.34), W(0.84), W(0.62)], fill=MARBLE, outline=OUTLINE, width=LW)
    # bowl interior (dry, clean, faintly warm)
    d.ellipse([W(0.24), W(0.40), W(0.76), W(0.58)], fill=MARBLE_DARK, outline=OUTLINE, width=int(LW * 0.8))
    d.ellipse([W(0.34), W(0.45), W(0.66), W(0.55)], fill=(96, 84, 74, 255))
    # rim rite: carved marks around the front lip
    for i in range(7):
        a = math.pi * (0.15 + 0.7 * i / 6)
        x = 0.5 + 0.31 * math.cos(a)
        y = 0.52 + 0.11 * math.sin(a)
        d.line([(W(x), W(y)), (W(x), W(y + 0.04))], fill=RUNE, width=int(LW * 0.9))
    save(img, "TSC_ShrineAltar")


# ------------------------------------------------------------- beast tracks
def make_beast_tracks():
    img, d = canvas()
    # worn dirt patch
    d.ellipse([W(0.10), W(0.20), W(0.90), W(0.90)], fill=(122, 96, 66, 90))
    d.ellipse([W(0.18), W(0.28), W(0.82), W(0.82)], fill=(122, 96, 66, 60))

    def paw(cx, cy, s, ang):
        # pad
        d.ellipse([W(cx - 0.055 * s), W(cy - 0.045 * s), W(cx + 0.055 * s), W(cy + 0.055 * s)],
                  fill=DIRT, outline=OUTLINE, width=int(LW * 0.6))
        # toes
        for i in (-1.5, -0.5, 0.5, 1.5):
            a = ang + i * 0.42
            tx = cx + 0.085 * s * math.sin(a)
            ty = cy - 0.085 * s * math.cos(a)
            d.ellipse([W(tx - 0.022 * s), W(ty - 0.028 * s), W(tx + 0.022 * s), W(ty + 0.028 * s)],
                      fill=DIRT, outline=OUTLINE, width=int(LW * 0.5))

    paw(0.34, 0.68, 1.0, -0.2)
    paw(0.58, 0.52, 1.05, 0.1)
    paw(0.40, 0.36, 0.9, -0.1)
    paw(0.66, 0.24, 0.85, 0.15)
    save(img, "TSC_BeastTracks")


# ---------------------------------------------------------------- rune ward
def make_rune_ward():
    img, d = canvas()
    d.ellipse([W(0.16), W(0.72), W(0.84), W(0.94)], fill=(0, 0, 0, 60))
    # squat warding stone
    d.polygon([(W(0.26), W(0.84)), (W(0.28), W(0.34)), (W(0.38), W(0.22)),
               (W(0.62), W(0.22)), (W(0.72), W(0.34)), (W(0.74), W(0.84))],
              fill=STONE_DARK, outline=OUTLINE, width=LW)
    # the ward: ember sigil, a bound circle with a cross-stroke
    d.ellipse([W(0.37), W(0.34), W(0.63), W(0.60)], outline=EMBER, width=int(LW * 1.4))
    d.line([(W(0.50), W(0.28)), (W(0.50), W(0.66))], fill=EMBER, width=int(LW * 1.2))
    d.line([(W(0.40), W(0.52)), (W(0.60), W(0.42))], fill=EMBER, width=int(LW * 1.2))
    # heat shimmer ticks
    for x in (0.33, 0.50, 0.67):
        d.line([(W(x), W(0.72)), (W(x + 0.02), W(0.68))], fill=EMBER, width=int(LW * 0.8))
    save(img, "TSC_RuneWard")


# --------------------------------------------------------- collapsed passage
def make_collapsed_passage():
    img, d = canvas()
    d.ellipse([W(0.06), W(0.66), W(0.94), W(0.96)], fill=(0, 0, 0, 60))
    # boulder heap: big stones overlapping
    stones = [
        (0.14, 0.52, 0.48, 0.86, STONE_DARK),
        (0.40, 0.42, 0.78, 0.80, STONE),
        (0.62, 0.56, 0.92, 0.88, STONE_DARK),
        (0.26, 0.30, 0.60, 0.62, STONE),
        (0.06, 0.62, 0.32, 0.88, STONE),
        (0.52, 0.24, 0.80, 0.52, STONE_DARK),
    ]
    for x0, y0, x1, y1, fill in stones:
        d.ellipse([W(x0), W(y0), W(x1), W(y1)], fill=fill, outline=OUTLINE, width=LW)
    # dust/rubble bits
    for x, y in ((0.12, 0.90), (0.50, 0.92), (0.86, 0.92), (0.70, 0.88)):
        d.ellipse([W(x - 0.03), W(y - 0.02), W(x + 0.03), W(y + 0.02)],
                  fill=STONE_DARK, outline=OUTLINE, width=int(LW * 0.5))
    save(img, "TSC_CollapsedPassage")


# ===================================================================== round 2
# Twelve more spots, added so every proficiency has somewhere to matter in
# Adventure Mode. Same flat-shaded hand as above.

BONE = (226, 220, 200, 255)
BONE_DARK = (196, 188, 166, 255)
WOOD = (128, 98, 62, 255)
WOOD_DARK = (98, 74, 46, 255)
CLOTH = (150, 62, 58, 255)
CLOTH_DARK = (114, 44, 42, 255)
IRON = (128, 132, 140, 255)
INK = (58, 52, 44, 255)
GRASS = (96, 124, 62, 255)
WATER = (78, 108, 132, 255)
GOLD = (206, 170, 84, 255)


def _shadow(d):
    d.ellipse([W(0.08), W(0.70), W(0.92), W(0.96)], fill=(0, 0, 0, 60))


def make_grisly_remains():
    img, d = canvas()
    _shadow(d)
    for x in (0.30, 0.42, 0.54, 0.66):
        d.arc([W(x - 0.04), W(0.34), W(x + 0.16), W(0.76)], 250, 470,
              fill=BONE, width=int(LW * 1.6))
    d.ellipse([W(0.16), W(0.56), W(0.34), W(0.72)], fill=BONE, outline=OUTLINE, width=LW)
    d.ellipse([W(0.21), W(0.61), W(0.25), W(0.65)], fill=OUTLINE)
    for x, y in ((0.74, 0.78), (0.60, 0.86), (0.40, 0.84)):
        d.line([W(x), W(y), W(x + 0.12), W(y - 0.04)], fill=BONE_DARK, width=int(LW * 1.4))
    save(img, "TSC_GrislyRemains")


def make_watch_post():
    img, d = canvas()
    _shadow(d)
    d.polygon([(W(0.30), W(0.62)), (W(0.62), W(0.62)), (W(0.58), W(0.70)), (W(0.34), W(0.70))],
              fill=WOOD, outline=OUTLINE, width=LW)
    for x in (0.34, 0.56):
        d.line([W(x), W(0.70), W(x + 0.02), W(0.88)], fill=WOOD_DARK, width=int(LW * 1.6))
    d.line([W(0.74), W(0.14), W(0.68), W(0.88)], fill=WOOD_DARK, width=int(LW * 1.5))
    d.polygon([(W(0.74), W(0.10)), (W(0.79), W(0.24)), (W(0.70), W(0.24))],
              fill=IRON, outline=OUTLINE, width=LW)
    d.polygon([(W(0.24), W(0.40)), (W(0.42), W(0.36)), (W(0.44), W(0.64)), (W(0.22), W(0.66))],
              fill=CLOTH, outline=OUTLINE, width=LW)
    save(img, "TSC_WatchPost")


def make_bandit_ledger():
    img, d = canvas()
    _shadow(d)
    d.polygon([(W(0.20), W(0.30)), (W(0.80), W(0.26)), (W(0.84), W(0.78)), (W(0.24), W(0.82))],
              fill=(216, 202, 172, 255), outline=OUTLINE, width=LW)
    for i in range(5):
        y = 0.38 + i * 0.09
        d.line([W(0.30), W(y), W(0.70 - i * 0.04), W(y - 0.01)], fill=INK, width=int(LW * 0.9))
    d.line([W(0.20), W(0.30), W(0.24), W(0.82)], fill=WOOD_DARK, width=int(LW * 2))
    save(img, "TSC_BanditLedger")


def make_grave_mound():
    img, d = canvas()
    _shadow(d)
    d.chord([W(0.10), W(0.44), W(0.90), W(0.94)], 180, 360, fill=DIRT, outline=OUTLINE, width=LW)
    for x in (0.24, 0.40, 0.58, 0.74):
        d.ellipse([W(x), W(0.52), W(x + 0.07), W(0.60)], fill=GRASS)
    d.polygon([(W(0.44), W(0.16)), (W(0.56), W(0.16)), (W(0.56), W(0.56)), (W(0.44), W(0.56))],
              fill=STONE, outline=OUTLINE, width=LW)
    d.polygon([(W(0.30), W(0.26)), (W(0.70), W(0.26)), (W(0.70), W(0.36)), (W(0.30), W(0.36))],
              fill=STONE_DARK, outline=OUTLINE, width=LW)
    save(img, "TSC_GraveMound")


def make_old_well():
    img, d = canvas()
    _shadow(d)
    d.ellipse([W(0.18), W(0.44), W(0.82), W(0.88)], fill=STONE, outline=OUTLINE, width=LW)
    d.ellipse([W(0.28), W(0.52), W(0.72), W(0.80)], fill=(20, 24, 30, 255), outline=OUTLINE, width=LW)
    d.ellipse([W(0.34), W(0.58), W(0.66), W(0.76)], fill=WATER)
    for x in (0.22, 0.78):
        d.line([W(x), W(0.50), W(x), W(0.14)], fill=WOOD_DARK, width=int(LW * 1.6))
    d.line([W(0.18), W(0.16), W(0.82), W(0.16)], fill=WOOD, width=int(LW * 1.8))
    save(img, "TSC_OldWell")


def make_sealed_door():
    img, d = canvas()
    _shadow(d)
    d.polygon([(W(0.22), W(0.16)), (W(0.78), W(0.16)), (W(0.78), W(0.90)), (W(0.22), W(0.90))],
              fill=STONE_DARK, outline=OUTLINE, width=LW)
    d.line([W(0.50), W(0.16), W(0.50), W(0.90)], fill=OUTLINE, width=LW)
    for y in (0.34, 0.62):
        d.line([W(0.22), W(y), W(0.78), W(y)], fill=IRON, width=int(LW * 1.8))
    d.ellipse([W(0.42), W(0.44), W(0.58), W(0.60)], fill=GOLD, outline=OUTLINE, width=LW)
    save(img, "TSC_SealedDoor")


def make_prisoners_marks():
    img, d = canvas()
    _shadow(d)
    d.polygon([(W(0.10), W(0.12)), (W(0.90), W(0.12)), (W(0.90), W(0.88)), (W(0.10), W(0.88))],
              fill=STONE_DARK, outline=OUTLINE, width=LW)
    n = 0
    for row in range(3):
        for col in range(7):
            if n >= 19:
                break
            x = 0.18 + col * 0.10
            y = 0.26 + row * 0.22
            d.line([W(x), W(y), W(x - 0.02), W(y + 0.14)], fill=MARBLE, width=int(LW * 1.1))
            n += 1
        if row < 2:
            d.line([W(0.16), W(0.28 + row * 0.22), W(0.62), W(0.42 + row * 0.22)],
                   fill=MARBLE, width=int(LW * 1.1))
    save(img, "TSC_PrisonersMarks")


def make_minstrels_grave():
    img, d = canvas()
    _shadow(d)
    d.chord([W(0.12), W(0.50), W(0.88), W(0.94)], 180, 360, fill=DIRT, outline=OUTLINE, width=LW)
    d.ellipse([W(0.30), W(0.40), W(0.62), W(0.72)], fill=WOOD, outline=OUTLINE, width=LW)
    d.ellipse([W(0.42), W(0.52), W(0.50), W(0.60)], fill=OUTLINE)
    d.line([W(0.58), W(0.52), W(0.86), W(0.22)], fill=WOOD_DARK, width=int(LW * 2))
    d.polygon([(W(0.82), W(0.26)), (W(0.90), W(0.16)), (W(0.94), W(0.22)), (W(0.86), W(0.32))],
              fill=WOOD, outline=OUTLINE, width=LW)
    for i in range(3):
        d.line([W(0.36 + i * 0.02), W(0.44), W(0.84 + i * 0.02), W(0.24)],
               fill=(230, 226, 214, 200), width=max(1, int(LW * 0.5)))
    save(img, "TSC_MinstrelsGrave")


def make_campfire_circle():
    img, d = canvas()
    _shadow(d)
    for i in range(9):
        a = math.radians(40 * i)
        x = 0.50 + 0.32 * math.cos(a)
        y = 0.66 + 0.22 * math.sin(a)
        d.ellipse([W(x - 0.06), W(y - 0.045), W(x + 0.06), W(y + 0.045)],
                  fill=STONE if i % 2 else STONE_DARK, outline=OUTLINE, width=max(1, int(LW * 0.8)))
    d.ellipse([W(0.34), W(0.56), W(0.66), W(0.76)], fill=(48, 42, 38, 255))
    for x0, y0, x1, y1 in ((0.38, 0.72, 0.62, 0.58), (0.40, 0.58, 0.64, 0.72)):
        d.line([W(x0), W(y0), W(x1), W(y1)], fill=WOOD_DARK, width=int(LW * 1.6))
    d.ellipse([W(0.46), W(0.62), W(0.54), W(0.70)], fill=(80, 70, 62, 255))
    save(img, "TSC_CampfireCircle")


def make_parley_stone():
    img, d = canvas()
    _shadow(d)
    d.polygon([(W(0.28), W(0.86)), (W(0.34), W(0.24)), (W(0.66), W(0.20)), (W(0.74), W(0.86))],
              fill=MARBLE, outline=OUTLINE, width=LW)
    d.arc([W(0.34), W(0.40), W(0.58), W(0.66)], 300, 120, fill=RUNE, width=int(LW * 1.8))
    d.arc([W(0.44), W(0.44), W(0.68), W(0.70)], 120, 300, fill=RUNE, width=int(LW * 1.8))
    save(img, "TSC_ParleyStone")


def make_hunters_blind():
    img, d = canvas()
    _shadow(d)
    for x in (0.22, 0.50, 0.78):
        d.line([W(x), W(0.30), W(x), W(0.88)], fill=WOOD_DARK, width=int(LW * 1.6))
    for i in range(6):
        y = 0.34 + i * 0.09
        d.line([W(0.16), W(y), W(0.84), W(y - 0.02)],
               fill=GRASS if i % 2 else (78, 102, 52, 255), width=int(LW * 1.5))
    d.polygon([(W(0.40), W(0.14)), (W(0.60), W(0.14)), (W(0.50), W(0.30))],
              fill=GRASS, outline=OUTLINE, width=LW)
    save(img, "TSC_HuntersBlind")


def make_strewn_baggage():
    img, d = canvas()
    _shadow(d)
    d.polygon([(W(0.14), W(0.52)), (W(0.48), W(0.46)), (W(0.52), W(0.82)), (W(0.18), W(0.88))],
              fill=CLOTH_DARK, outline=OUTLINE, width=LW)
    d.polygon([(W(0.46), W(0.56)), (W(0.86), W(0.50)), (W(0.88), W(0.84)), (W(0.50), W(0.88))],
              fill=WOOD, outline=OUTLINE, width=LW)
    d.line([W(0.46), W(0.68), W(0.88), W(0.64)], fill=WOOD_DARK, width=int(LW * 1.4))
    for x, y in ((0.30, 0.30), (0.62, 0.26), (0.78, 0.36)):
        d.ellipse([W(x - 0.05), W(y - 0.04), W(x + 0.05), W(y + 0.04)],
                  fill=GOLD, outline=OUTLINE, width=max(1, int(LW * 0.8)))
    save(img, "TSC_StrewnBaggage")


def make_guild_strongbox():
    """
    The delve objective. It kept getting lost against dirt and cave floor
    because it reused the generic loot-chest sprite, small and earth-toned.
    This one is built to POP on brown and on grey: near-black iron body,
    bright brass banding, and a red wax seal that exists in neither
    environment.
    """
    img, d = canvas()
    IRON = (58, 60, 66, 255)
    IRON_HI = (86, 89, 97, 255)
    BRASS = (226, 176, 68, 255)
    BRASS_HI = (250, 216, 130, 255)
    WAX = (176, 38, 40, 255)
    WAX_HI = (214, 74, 70, 255)

    d.ellipse([W(0.08), W(0.74), W(0.92), W(0.96)], fill=(0, 0, 0, 80))

    body = [W(0.14), W(0.34), W(0.86), W(0.86)]
    d.rectangle(body, fill=IRON, outline=OUTLINE, width=int(LW * 1.4))
    # domed lid
    d.chord([W(0.14), W(0.16), W(0.86), W(0.52)], 180, 360, fill=IRON_HI,
            outline=OUTLINE, width=int(LW * 1.4))
    d.line([W(0.14), W(0.34), W(0.86), W(0.34)], fill=OUTLINE, width=int(LW * 1.2))

    # brass banding: the part that catches the eye at a distance
    for x in (0.28, 0.72):
        d.line([W(x), W(0.20), W(x), W(0.86)], fill=BRASS, width=int(LW * 2.6))
        d.line([W(x - 0.008), W(0.20), W(x - 0.008), W(0.86)], fill=BRASS_HI, width=int(LW * 0.9))
    d.rectangle([W(0.12), W(0.80), W(0.88), W(0.88)], fill=BRASS, outline=OUTLINE, width=int(LW * 0.9))
    for cx in (0.17, 0.83):
        d.ellipse([W(cx - 0.045), W(0.30), W(cx + 0.045), W(0.40)], fill=BRASS,
                  outline=OUTLINE, width=int(LW * 0.8))

    # wax seal over the seam
    d.ellipse([W(0.40), W(0.42), W(0.60), W(0.62)], fill=WAX, outline=OUTLINE, width=int(LW * 1.1))
    d.ellipse([W(0.435), W(0.455), W(0.53), W(0.545)], fill=WAX_HI)
    # the guild mark: a rough crown, three points
    for px in (0.455, 0.50, 0.545):
        d.polygon([(W(px - 0.018), W(0.545)), (W(px), W(0.485)), (W(px + 0.018), W(0.545))],
                  fill=BRASS_HI)
    d.line([W(0.44), W(0.552), W(0.56), W(0.552)], fill=BRASS_HI, width=int(LW * 1.1))

    save(img, "TSC_GuildStrongbox", ITEM_DIR)


def make_guild_coin():
    """
    Guild scrip. Carries the strongbox's brass and its three-point crown on
    purpose: the mark is the guild's, and a player who has seen one coffer
    should read the coin as the same institution. Struck rather than
    stacked - a single disc, milled edge, so it never reads as silver.
    """
    img, d = canvas()
    BRASS = (204, 152, 54, 255)
    BRASS_HI = (246, 206, 116, 255)
    BRASS_LO = (150, 106, 34, 255)

    d.ellipse([W(0.12), W(0.16), W(0.90), W(0.94)], fill=(0, 0, 0, 70))

    face = [W(0.10), W(0.10), W(0.88), W(0.88)]
    d.ellipse(face, fill=BRASS, outline=OUTLINE, width=int(LW * 1.6))
    # milled edge: short ticks around the rim
    cx, cy, r = W(0.49), W(0.49), W(0.39)
    for i in range(28):
        a = i * (2 * math.pi / 28)
        d.line([cx + math.cos(a) * r * 0.90, cy + math.sin(a) * r * 0.90,
                cx + math.cos(a) * r, cy + math.sin(a) * r],
               fill=BRASS_LO, width=int(LW * 0.9))
    d.ellipse([W(0.18), W(0.18), W(0.80), W(0.80)], outline=BRASS_LO, width=int(LW * 1.1))
    # struck highlight, upper left, so it reads as metal not cardboard
    d.chord([W(0.16), W(0.16), W(0.82), W(0.82)], 190, 290, fill=BRASS_HI)
    d.ellipse([W(0.22), W(0.22), W(0.76), W(0.76)], fill=BRASS)

    # the guild mark: the strongbox crown, three points on a band
    for px in (0.40, 0.49, 0.58):
        d.polygon([(W(px - 0.055), W(0.60)), (W(px), W(0.34)), (W(px + 0.055), W(0.60))],
                  fill=BRASS_LO)
    d.rectangle([W(0.33), W(0.60), W(0.65), W(0.66)], fill=BRASS_LO)

    save(img, "TSC_GuildCoin", ITEM_DIR)




def make_choir_bones():
    """
    The hollow choir: a gallery wall of stacked skulls, rank on rank. Reads
    as masonry-of-bone rather than an altar - rows of pale rounds with dark
    eye sockets, mortared with shadow.
    """
    img, d = canvas()
    BONE = (214, 204, 178, 255)
    BONE_DK = (176, 164, 136, 255)
    SOCKET = (52, 44, 36, 255)

    rows = [(0.78, 7), (0.63, 8), (0.48, 7), (0.33, 8), (0.20, 6)]
    for ri, (cy, n) in enumerate(rows):
        for i in range(n):
            cx = 0.10 + (0.80 / max(1, n - 1)) * i + (0.02 if ri % 2 else -0.01)
            r = 0.055 + (0.008 if (i + ri) % 3 == 0 else 0)
            col = BONE if (i + ri) % 2 == 0 else BONE_DK
            d.ellipse([W(cx - r), W(cy - r), W(cx + r), W(cy + r)],
                      fill=col, outline=OUTLINE, width=int(LW * 0.7))
            for ex in (-0.022, 0.022):
                d.ellipse([W(cx + ex - 0.012), W(cy - 0.018), W(cx + ex + 0.012), W(cy + 0.004)],
                          fill=SOCKET)
            d.rectangle([W(cx - 0.018), W(cy + 0.024), W(cx + 0.018), W(cy + r)], fill=BONE_DK)
    save(img, "TSC_ChoirBones")


if __name__ == "__main__":
    make_choir_bones()
    make_guild_coin()
    make_guild_strongbox()
    make_graven_stone()
    make_shrine_altar()
    make_beast_tracks()
    make_rune_ward()
    make_collapsed_passage()
    make_grisly_remains()
    make_watch_post()
    make_bandit_ledger()
    make_grave_mound()
    make_old_well()
    make_sealed_door()
    make_prisoners_marks()
    make_minstrels_grave()
    make_campfire_circle()
    make_parley_stone()
    make_hunters_blind()
    make_strewn_baggage()
