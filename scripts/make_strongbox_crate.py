# TSC_StrongboxCrate: the crate a delve contract is actually about.
#
# It used to inherit vanilla's SealedCrate art, so the one thing the party
# came down here for looked exactly like every other cache on the floor.
# This is a guild shipping crate: pale timber, brass banding, and a red wax
# seal with the Wayfarers' mark on it, plus an opened variant with the lid
# off and the seal broken. Drawn to read at a glance against both cave floor
# and desert dirt.
#
# Run:  py scripts\make_strongbox_crate.py
import os
import random

from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(__file__), "..", "Textures", "Things", "Building")

S = 512  # supersampled; saved at 128

TIMBER = (150, 120, 84)
TIMBER_DARK = (112, 88, 60)
TIMBER_LIGHT = (176, 145, 104)
BRASS = (196, 156, 66)
BRASS_DARK = (128, 98, 38)
WAX = (150, 38, 34)
WAX_LIGHT = (186, 60, 52)


def base_crate(d, rng, lid=True):
    """Boards, banding and rivets: the parts both states share."""
    m = int(S * 0.09)
    box = [m, m, S - m, S - m]

    d.rounded_rectangle(box, radius=16, fill=TIMBER_DARK)
    inner = [box[0] + 10, box[1] + 10, box[2] - 10, box[3] - 10]
    d.rounded_rectangle(inner, radius=12, fill=TIMBER)

    # Planking: four boards with seams, each slightly different in tone so
    # the lid does not read as one flat slab.
    boards = 4
    height = (inner[3] - inner[1]) / boards
    for i in range(boards):
        top = inner[1] + i * height
        shade = rng.randint(-12, 12)
        d.rectangle([inner[0], top + 2, inner[2], top + height - 2],
                    fill=(TIMBER[0] + shade, TIMBER[1] + shade, TIMBER[2] + shade))
        d.line([inner[0], top, inner[2], top], fill=TIMBER_DARK, width=3)

    # Two brass bands across the width, riveted.
    for frac in (0.28, 0.72):
        y = inner[1] + (inner[3] - inner[1]) * frac
        d.rectangle([box[0] - 2, y - 14, box[2] + 2, y + 14], fill=BRASS_DARK)
        d.rectangle([box[0] - 2, y - 11, box[2] + 2, y + 9], fill=BRASS)
        for rivet in range(5):
            rx = box[0] + 26 + rivet * ((box[2] - box[0] - 52) / 4)
            d.ellipse([rx - 6, y - 6, rx + 6, y + 6], fill=BRASS_DARK)
            d.ellipse([rx - 4, y - 5, rx + 3, y + 2], fill=(226, 192, 110))

    # Corner brackets: the silhouette cue that says "shipping crate".
    arm = 54

    def bar(x0, y0, x1, y1):
        # PIL wants an ordered box; the corner arms are drawn by direction.
        d.rectangle([min(x0, x1), min(y0, y1), max(x0, x1), max(y0, y1)], fill=BRASS_DARK)

    for cx, cy, dx, dy in ((box[0], box[1], 1, 1), (box[2], box[1], -1, 1),
                           (box[0], box[3], 1, -1), (box[2], box[3], -1, -1)):
        bar(cx, cy, cx + arm * dx, cy + 16 * dy)
        bar(cx, cy, cx + 16 * dx, cy + arm * dy)
    return box, inner


def seal(d, cx, cy, broken=False):
    """The red wax disc, whole or cracked in half."""
    r = 46
    if not broken:
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=WAX)
        d.ellipse([cx - r + 7, cy - r + 5, cx + r - 9, cy + r - 11], fill=WAX_LIGHT)
        # The mark: a rider's road, three strokes, legible at 128px.
        d.line([cx - 20, cy + 12, cx + 20, cy - 14], fill=(96, 20, 18), width=7)
        d.line([cx - 20, cy - 6, cx + 4, cy - 6], fill=(96, 20, 18), width=6)
        d.line([cx - 2, cy + 16, cx + 20, cy + 16], fill=(96, 20, 18), width=6)
        return
    for side in (-1, 1):
        d.pieslice([cx - r, cy - r, cx + r, cy + r],
                   start=-80 if side < 0 else 100, end=100 if side < 0 else 280,
                   fill=WAX)
    d.line([cx, cy - r - 4, cx + 6, cy + r + 4], fill=(40, 30, 26), width=9)


def make(opened):
    rng = random.Random(20260801 + (1 if opened else 0))
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle(
        [int(S * 0.09) - 6, int(S * 0.09) + 10, S - int(S * 0.09) + 12, S - int(S * 0.09) + 16],
        radius=20, fill=(0, 0, 0, 120))
    img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(9)))

    box, inner = base_crate(d, rng)
    cx = (box[0] + box[2]) // 2
    cy = (box[1] + box[3]) // 2

    if opened:
        # Lid OFF, not a hole in the ground: the cavity is inset far enough
        # that the crate's own walls still show on every side, and the lid
        # sits beside it, slid down-right, with the broken seal on it. The
        # first pass filled almost the whole square with black and read as a
        # pit.
        cav = [inner[0] + 62, inner[1] + 62, inner[2] - 62, inner[3] - 62]
        d.rounded_rectangle([cav[0] - 8, cav[1] - 8, cav[2] + 8, cav[3] + 8],
                            radius=10, fill=TIMBER_DARK)
        d.rounded_rectangle(cav, radius=8, fill=(34, 27, 21))
        d.line([cav[0] + 6, cav[1] + 6, cav[2] - 6, cav[1] + 6], fill=(58, 46, 36), width=5)

        lid = [cx - 96, cy - 40, cx + 150, cy + 150]
        shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        ImageDraw.Draw(shadow).rounded_rectangle(
            [lid[0] + 6, lid[1] + 10, lid[2] + 8, lid[3] + 12], radius=12, fill=(0, 0, 0, 130))
        img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(7)))
        d.rounded_rectangle(lid, radius=12, fill=TIMBER_DARK)
        d.rounded_rectangle([lid[0] + 7, lid[1] + 7, lid[2] - 7, lid[3] - 7], radius=9, fill=TIMBER_LIGHT)
        for frac in (0.33, 0.66):
            y = lid[1] + (lid[3] - lid[1]) * frac
            d.line([lid[0] + 7, y, lid[2] - 7, y], fill=TIMBER_DARK, width=4)
        band = lid[1] + (lid[3] - lid[1]) * 0.5
        d.rectangle([lid[0], band - 11, lid[2], band + 11], fill=BRASS_DARK)
        d.rectangle([lid[0], band - 8, lid[2], band + 6], fill=BRASS)
        seal(d, (lid[0] + lid[2]) // 2, int(band), broken=True)
    else:
        seal(d, cx, cy)

    return img.resize((S // 4, S // 4), Image.LANCZOS)


def main():
    os.makedirs(OUT, exist_ok=True)
    for opened in (False, True):
        name = "TSC_StrongboxCrate_Open" if opened else "TSC_StrongboxCrate"
        path = os.path.join(OUT, name + ".png")
        make(opened).save(path)
        print("wrote", os.path.normpath(path))


if __name__ == "__main__":
    main()
