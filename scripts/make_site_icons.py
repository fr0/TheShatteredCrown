"""Generate world-map icons for TSC story sites.

Each icon is a parchment-white silhouette with a dark outline, drawn at 4x
and downscaled for antialiasing. World object materials are tinted by the
site faction's color, so fills stay near-white to take the tint cleanly.

Output: Textures/World/WorldObjects/TSC_Sites/*.png (128x128 RGBA)
"""
import os
from PIL import Image, ImageDraw, ImageFilter

SS = 4          # supersample factor
SIZE = 128
CANVAS = SIZE * SS

FILL = (242, 234, 216, 255)      # parchment
OUTLINE = (58, 49, 40, 255)      # dark walnut
DARK = (30, 26, 20, 255)         # openings / interiors
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "Textures", "World", "WorldObjects", "TSC_Sites")

def canvas():
    img = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)

BADGE_FILL = (208, 193, 163, 255)   # darker tan, so the parchment glyph reads on it

def save(img, name):
    # Round badge under every glyph: world-layer icons print tangent to the
    # planet and ROTATE with their position on the sphere (only the expanded
    # screen-space icons stay upright). A circular token reads correctly at
    # any rotation; a bare cottage silhouette reads as a mistake.
    badge = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    bd = ImageDraw.Draw(badge)
    m = W(0.05)
    bd.ellipse([m, m, CANVAS - m, CANVAS - m], fill=BADGE_FILL,
               outline=OUTLINE, width=int(LW * 1.5))
    glyph = img.resize((int(CANVAS * 0.70), int(CANVAS * 0.70)), Image.LANCZOS)
    off = (CANVAS - glyph.width) // 2
    badge.alpha_composite(glyph, (off, off))
    img = badge.resize((SIZE, SIZE), Image.LANCZOS)
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, name + ".png")
    img.save(path)
    print("wrote", path)

def W(f):
    """scale helper: fraction of canvas -> px"""
    return int(f * CANVAS)

LW = W(0.025)  # outline width


def poly(d, pts, fill=FILL):
    d.polygon([(W(x), W(y)) for x, y in pts], fill=fill, outline=OUTLINE, width=LW)


def rect(d, x0, y0, x1, y1, fill=FILL):
    d.rectangle([W(x0), W(y0), W(x1), W(y1)], fill=fill, outline=OUTLINE, width=LW)


def ellipse(d, x0, y0, x1, y1, fill=FILL):
    d.ellipse([W(x0), W(y0), W(x1), W(y1)], fill=fill, outline=OUTLINE, width=LW)


def line(d, pts, width=None, fill=OUTLINE):
    d.line([(W(x), W(y)) for x, y in pts], fill=fill, width=width or LW)


# ---------------------------------------------------------------- ruin
def make_ruin():
    img, d = canvas()
    # broken tower: left wall tall, right side collapsed
    poly(d, [(0.28, 0.85), (0.28, 0.25), (0.34, 0.25), (0.34, 0.18),
             (0.42, 0.18), (0.42, 0.25), (0.50, 0.25), (0.50, 0.18),
             (0.57, 0.18), (0.57, 0.30), (0.62, 0.42), (0.66, 0.60),
             (0.68, 0.85)])
    # rubble at the base
    ellipse(d, 0.58, 0.74, 0.78, 0.86)
    ellipse(d, 0.68, 0.68, 0.82, 0.78)
    # arrow slit
    rect(d, 0.40, 0.40, 0.45, 0.55, fill=DARK)
    save(img, "TSC_SiteRuin")


# ---------------------------------------------------------------- camp
def make_camp():
    img, d = canvas()
    # tent
    poly(d, [(0.18, 0.78), (0.42, 0.30), (0.66, 0.78)])
    poly(d, [(0.34, 0.78), (0.42, 0.55), (0.50, 0.78)], fill=DARK)
    # campfire beside it: flame + logs
    poly(d, [(0.74, 0.60), (0.80, 0.44), (0.86, 0.60), (0.82, 0.58),
             (0.80, 0.66), (0.78, 0.58)])
    line(d, [(0.70, 0.78), (0.90, 0.70)], width=LW * 2)
    line(d, [(0.70, 0.70), (0.90, 0.78)], width=LW * 2)
    save(img, "TSC_SiteCamp")


# ---------------------------------------------------------------- barrow
def make_barrow():
    img, d = canvas()
    # burial mound
    d.pieslice([W(0.12), W(0.30), W(0.88), W(1.10)], 180, 360,
               fill=FILL, outline=OUTLINE, width=LW)
    # stone door
    rect(d, 0.42, 0.50, 0.58, 0.72, fill=DARK)
    d.arc([W(0.42), W(0.42), W(0.58), W(0.60)], 180, 360, fill=OUTLINE, width=LW)
    # grass tufts
    for x in (0.24, 0.70):
        line(d, [(x, 0.46), (x - 0.02, 0.40)])
        line(d, [(x + 0.02, 0.46), (x + 0.04, 0.40)])
    save(img, "TSC_SiteBarrow")


# ---------------------------------------------------------------- grove
def make_grove():
    img, d = canvas()
    # trunk
    poly(d, [(0.46, 0.86), (0.47, 0.55), (0.53, 0.55), (0.54, 0.86)])
    # roots
    line(d, [(0.46, 0.86), (0.38, 0.90)], width=LW * 2)
    line(d, [(0.54, 0.86), (0.62, 0.90)], width=LW * 2)
    # canopy: overlapping circles
    ellipse(d, 0.22, 0.28, 0.54, 0.58)
    ellipse(d, 0.42, 0.20, 0.76, 0.52)
    ellipse(d, 0.30, 0.12, 0.64, 0.42)
    save(img, "TSC_SiteGrove")


# ---------------------------------------------------------------- cave
def make_cave():
    img, d = canvas()
    # rocky hill
    poly(d, [(0.10, 0.84), (0.22, 0.50), (0.36, 0.30), (0.56, 0.22),
             (0.74, 0.34), (0.86, 0.58), (0.90, 0.84)])
    # cave mouth
    d.pieslice([W(0.36), W(0.48), W(0.66), W(1.06)], 180, 360,
               fill=DARK, outline=OUTLINE, width=LW)
    # web hint in the mouth corner
    line(d, [(0.40, 0.62), (0.48, 0.62)], width=LW // 2, fill=FILL)
    line(d, [(0.40, 0.68), (0.46, 0.66)], width=LW // 2, fill=FILL)
    line(d, [(0.42, 0.58), (0.42, 0.70)], width=LW // 2, fill=FILL)
    save(img, "TSC_SiteCave")


# ---------------------------------------------------------------- village
def make_village():
    img, d = canvas()
    # left cottage
    rect(d, 0.12, 0.55, 0.42, 0.84)
    poly(d, [(0.08, 0.55), (0.27, 0.36), (0.46, 0.55)])
    rect(d, 0.23, 0.66, 0.31, 0.84, fill=DARK)
    # right cottage (slightly back)
    rect(d, 0.52, 0.48, 0.88, 0.84)
    poly(d, [(0.48, 0.48), (0.70, 0.26), (0.92, 0.48)])
    rect(d, 0.62, 0.60, 0.70, 0.70, fill=DARK)
    # chimney
    rect(d, 0.78, 0.30, 0.84, 0.44)
    save(img, "TSC_SiteVillage")


# ---------------------------------------------------------------- cellars
def make_cellars():
    img, d = canvas()
    # arch doorway
    rect(d, 0.28, 0.44, 0.72, 0.86)
    d.pieslice([W(0.28), W(0.26), W(0.72), W(0.62)], 180, 360,
               fill=FILL, outline=OUTLINE, width=LW)
    # dark interior with descending steps
    d.pieslice([W(0.34), W(0.34), W(0.66), W(0.62)], 180, 360, fill=DARK)
    rect(d, 0.34, 0.48, 0.66, 0.80, fill=DARK)
    for i, y in enumerate((0.58, 0.66, 0.74)):
        x = 0.36 + i * 0.04
        line(d, [(x, y), (0.66 - (x - 0.34), y)], width=LW, fill=FILL)
    save(img, "TSC_SiteCellars")


# ---------------------------------------------------------------- gallery
def make_gallery():
    img, d = canvas()
    # hillside
    poly(d, [(0.08, 0.86), (0.30, 0.40), (0.52, 0.26), (0.76, 0.38),
             (0.92, 0.86)])
    # timber-framed adit: posts + lintel
    rect(d, 0.38, 0.52, 0.62, 0.86, fill=DARK)
    rect(d, 0.34, 0.46, 0.66, 0.54)          # lintel beam
    rect(d, 0.34, 0.54, 0.40, 0.86)          # left post
    rect(d, 0.60, 0.54, 0.66, 0.86)          # right post
    save(img, "TSC_SiteGallery")


if __name__ == "__main__":
    make_ruin()
    make_camp()
    make_barrow()
    make_grove()
    make_cave()
    make_village()
    make_cellars()
    make_gallery()
