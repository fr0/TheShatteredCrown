"""Generate equipment textures for TSC musical instruments.

Same hand as the check-spot art: flat shading, dark outline, drawn at 4x
and downscaled. Output 128x128 RGBA into Textures/Things/Item/Equipment/.

RimWorld draws an equipped weapon lying along the pawn's aim line with the
grip at the LEFT edge, so each instrument is drawn horizontally with its
held end (neck, beater, mouthpiece) at the left and the body to the right.
The defs pair this with equippedAngleOffset to sit it in the hand.
"""
import os
import math
from PIL import Image, ImageDraw

SS = 4
SIZE = 128
CANVAS = SIZE * SS
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "Textures", "Things", "Item", "Equipment")

OUTLINE = (44, 38, 32, 255)
WOOD = (146, 100, 56, 255)
WOOD_HI = (186, 138, 84, 255)
WOOD_LO = (104, 68, 36, 255)
GUT = (232, 220, 190, 255)
BONE = (226, 214, 188, 255)
BRASS = (204, 152, 54, 255)
BRASS_HI = (240, 200, 110, 255)
HIDE = (216, 198, 166, 255)
HIDE_LO = (178, 158, 126, 255)
ROPE = (162, 140, 100, 255)
HORN_DARK = (78, 62, 48, 255)
HORN_MID = (126, 102, 74, 255)
HORN_HI = (168, 142, 106, 255)


def canvas():
    img = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def W(f):
    return int(f * CANVAS)


LW = W(0.014)


def save(img, name):
    img = img.resize((SIZE, SIZE), Image.LANCZOS)
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, name + ".png")
    img.save(path)
    print("wrote", path)


def make_lute():
    """Pear body right, long fretted neck left, four gut strings between."""
    img, d = canvas()

    # neck, running left from the body
    d.rectangle([W(0.06), W(0.455), W(0.56), W(0.545)], fill=WOOD, outline=OUTLINE, width=int(LW))
    d.rectangle([W(0.06), W(0.455), W(0.56), W(0.485)], fill=WOOD_HI)
    # pegbox, angled back at the far left
    d.polygon([(W(0.06), W(0.45)), (W(0.16), W(0.45)), (W(0.16), W(0.55)), (W(0.06), W(0.55))],
              fill=WOOD_LO, outline=OUTLINE, width=int(LW))
    for py in (0.475, 0.525):
        d.line([W(0.075), W(py), W(0.145), W(py)], fill=BONE, width=int(LW * 0.9))

    # pear body
    body = [W(0.50), W(0.26), W(0.95), W(0.74)]
    d.ellipse(body, fill=WOOD, outline=OUTLINE, width=int(LW * 1.4))
    d.chord([W(0.52), W(0.28), W(0.93), W(0.72)], 150, 330, fill=WOOD_HI)
    d.ellipse([W(0.56), W(0.32), W(0.90), W(0.68)], outline=WOOD_LO, width=int(LW * 0.8))
    # rose (soundhole) and bridge
    d.ellipse([W(0.66), W(0.43), W(0.76), W(0.57)], fill=WOOD_LO, outline=OUTLINE, width=int(LW * 0.7))
    d.rectangle([W(0.855), W(0.44), W(0.885), W(0.56)], fill=WOOD_LO, outline=OUTLINE, width=int(LW * 0.6))

    # strings, neck through to bridge
    for sy in (0.472, 0.492, 0.512, 0.532):
        d.line([W(0.10), W(sy), W(0.87), W(sy)], fill=GUT, width=int(LW * 0.55))

    save(img, "TSC_Lute")


def make_drum():
    """
    Field drum seen head-on, with a sliver of shell for depth.

    Drawn side-on it reads as a barrel: two ellipses and staves. The drum
    head is the thing that says "drum", so it faces the viewer, with the
    rope Vs around the rim and a beater laid across it.
    """
    img, d = canvas()

    cx, cy, r = 0.58, 0.50, 0.36

    # shell, peeking out to the left of the head. An offset ellipse alone,
    # no filled rectangle behind it: the rectangle squared off the left side
    # and the whole thing read as a disc glued to a block.
    d.ellipse([W(cx - r - 0.11), W(cy - r), W(cx + r - 0.11), W(cy + r)],
              fill=WOOD_LO, outline=OUTLINE, width=int(LW * 1.2))

    # counter-hoop and head
    d.ellipse([W(cx - r), W(cy - r), W(cx + r), W(cy + r)],
              fill=WOOD, outline=OUTLINE, width=int(LW * 1.4))
    d.ellipse([W(cx - r * 0.86), W(cy - r * 0.86), W(cx + r * 0.86), W(cy + r * 0.86)],
              fill=HIDE, outline=OUTLINE, width=int(LW * 1.0))
    # skin: a little unevenness so it does not read as a flat disc
    d.chord([W(cx - r * 0.80), W(cy - r * 0.80), W(cx + r * 0.80), W(cy + r * 0.80)],
            185, 305, fill=(238, 226, 202, 255))
    d.ellipse([W(cx - r * 0.30), W(cy - r * 0.34), W(cx + r * 0.10), W(cy - r * 0.02)],
              fill=HIDE_LO)

    # rope tensioning: Vs around the hoop, pulled to the shell behind
    for i in range(10):
        a = math.radians(i * 36 + 10)
        hx, hy = cx + math.cos(a) * r * 0.93, cy + math.sin(a) * r * 0.93
        sx, sy = cx + math.cos(a) * r * 1.03 - 0.075, cy + math.sin(a) * r * 1.03
        d.line([W(hx), W(hy), W(sx), W(sy)], fill=ROPE, width=int(LW * 0.9))
    d.ellipse([W(cx - r * 0.93), W(cy - r * 0.93), W(cx + r * 0.93), W(cy + r * 0.93)],
              outline=WOOD_LO, width=int(LW * 0.9))

    # beater, laid across the head from the held (left) end
    d.line([W(0.05), W(0.66), W(0.50), W(0.44)], fill=OUTLINE, width=int(LW * 3.0))
    d.line([W(0.05), W(0.66), W(0.50), W(0.44)], fill=WOOD, width=int(LW * 2.0))
    d.line([W(0.05), W(0.66), W(0.50), W(0.44)], fill=WOOD_HI, width=int(LW * 0.7))
    d.ellipse([W(0.46), W(0.39), W(0.57), W(0.50)], fill=WOOD_LO, outline=OUTLINE, width=int(LW * 0.9))

    save(img, "TSC_Drum")


def make_horn():
    """Curved ox horn, brass mouthpiece at the held (left) end."""
    img, d = canvas()

    # the horn as a tapering curve: circles of growing radius along an arc
    pts = []
    for i in range(64):
        t = i / 63.0
        a = math.radians(196 + t * 118)
        cx = 0.52 + math.cos(a) * 0.34
        cy = 0.60 + math.sin(a) * 0.34
        pts.append((cx, cy, 0.020 + t * 0.085))

    for cx, cy, r in pts:
        d.ellipse([W(cx - r - 0.012), W(cy - r - 0.012), W(cx + r + 0.012), W(cy + r + 0.012)],
                  fill=OUTLINE)
    for cx, cy, r in pts:
        d.ellipse([W(cx - r), W(cy - r), W(cx + r), W(cy + r)], fill=HORN_MID)
    # highlight along the outer edge
    for cx, cy, r in pts:
        d.ellipse([W(cx - r * 0.55), W(cy - r * 0.85), W(cx + r * 0.25), W(cy - r * 0.15)],
                  fill=HORN_HI)
    # dark at the wide bell end
    bx, by, br = pts[-1]
    d.ellipse([W(bx - br), W(by - br), W(bx + br), W(by + br)], fill=HORN_DARK,
              outline=OUTLINE, width=int(LW))
    d.ellipse([W(bx - br * 0.62), W(by - br * 0.62), W(bx + br * 0.62), W(by + br * 0.62)],
              fill=(52, 42, 34, 255))

    # brass mouthpiece
    mx, my, mr = pts[0]
    d.ellipse([W(mx - mr - 0.03), W(my - mr - 0.03), W(mx + mr + 0.03), W(my + mr + 0.03)],
              fill=BRASS, outline=OUTLINE, width=int(LW))
    d.ellipse([W(mx - mr * 0.5), W(my - mr * 0.5), W(mx + mr * 0.5), W(my + mr * 0.5)], fill=BRASS_HI)

    # binding band: drawn ACROSS the tube, perpendicular to the curve. A ring
    # centred on the tube reads as a loose hoop floating over the horn at
    # this size, which is not what a binding is.
    cx, cy, r = pts[26]
    px, py, _ = pts[24]
    nx, ny, _ = pts[28]
    tx, ty = nx - px, ny - py
    length = math.hypot(tx, ty) or 1.0
    # unit normal to the local tangent, scaled to just past the tube edge
    ox, oy = -ty / length * (r + 0.012), tx / length * (r + 0.012)
    d.line([W(cx - ox), W(cy - oy), W(cx + ox), W(cy + oy)], fill=BRASS, width=int(LW * 2.2))
    d.line([W(cx - ox * 0.9), W(cy - oy * 0.9), W(cx + ox * 0.2), W(cy + oy * 0.2)],
           fill=BRASS_HI, width=int(LW * 0.8))

    save(img, "TSC_Horn")


if __name__ == "__main__":
    make_lute()
    make_drum()
    make_horn()
