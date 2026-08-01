# TSC_KingTomb: a plain stone coffin, 1x2, for the eleven kings who wore
# the crown after the first one and are buried around him. Deliberately
# poorer work than his sarcophagus - a lidded box with one cut band and a
# notch where a crown would sit if there were one left to put there. Drawn
# in neutral greys so the def's <color> tint does the theming.
# Run:  py scripts\make_king_tombs.py
import os
import random

from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(__file__), "..", "Textures", "Things", "Building")

W, H = 256, 512  # supersampled; saved at 128x256 (a 1x2 building)


def grey(v, a=255):
    v = max(0, min(255, int(v)))
    return (v, v, v, a)


def make_tomb():
    rng = random.Random(20260801)
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    m = int(W * 0.10)          # margin: the coffin does not fill its cells
    box = [m, m, W - m, H - m]

    # Ground shadow, offset down-right so the lid reads as raised stone.
    shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle(
        [box[0] - 6, box[1] + 8, box[2] + 10, box[3] + 14], radius=18, fill=(0, 0, 0, 120))
    img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(9)))

    # The chest, then the lid inset on top of it: two stones, not one.
    d.rounded_rectangle(box, radius=16, fill=grey(118))
    lid = [box[0] + 10, box[1] + 8, box[2] - 10, box[3] - 14]
    d.rounded_rectangle(lid, radius=13, fill=grey(150))

    # Grain: a few tool marks, not a comb. At 128 pixels wide, anything
    # denser than this stops reading as stone and starts reading as a
    # radiator - which is exactly what the first pass looked like.
    for y in range(lid[1] + 40, lid[3] - 24, 34):
        x0 = lid[0] + rng.randint(10, 26)
        x1 = lid[2] - rng.randint(10, 26)
        d.line([x0, y, x1, y], fill=grey(132, 55), width=2)

    # One cut band near the head end, and the notch in it: the same five
    # points as the crown, with the middle one left empty.
    band_y = lid[1] + int((lid[3] - lid[1]) * 0.20)
    d.rectangle([lid[0] + 8, band_y - 3, lid[2] - 8, band_y + 3], fill=grey(88))
    d.line([lid[0] + 8, band_y - 4, lid[2] - 8, band_y - 4], fill=grey(178, 120), width=2)
    cx = (lid[0] + lid[2]) // 2
    for i in range(-2, 3):
        px = cx + i * 18
        top = band_y - (16 if i == 0 else 11)
        if i == 0:
            # the empty middle: a socket, not a point
            d.polygon([(px - 10, band_y), (px, top), (px + 10, band_y)], fill=grey(66))
        else:
            d.polygon([(px - 8, band_y), (px, top), (px + 8, band_y)], fill=grey(170))
            d.line([(px - 8, band_y), (px, top)], fill=grey(196, 160), width=2)

    # Edge lighting: bright along the top-left, dark along the bottom-right.
    d.line([lid[0] + 4, lid[1] + 4, lid[2] - 4, lid[1] + 4], fill=grey(186, 150), width=3)
    d.line([lid[0] + 4, lid[1] + 4, lid[0] + 4, lid[3] - 4], fill=grey(178, 130), width=3)
    d.line([lid[0] + 6, lid[3] - 5, lid[2] - 4, lid[3] - 5], fill=grey(70, 150), width=4)
    d.line([lid[2] - 5, lid[1] + 6, lid[2] - 5, lid[3] - 4], fill=grey(78, 130), width=4)

    # A crack, because eleven of these have been down here a long time.
    x = cx + rng.randint(-20, 20)
    y = lid[3] - 30
    pts = [(x, y)]
    for _ in range(5):
        x += rng.randint(-14, 14)
        y -= rng.randint(18, 30)
        pts.append((x, y))
    d.line(pts, fill=grey(84, 160), width=3)

    return img.resize((W // 2, H // 2), Image.LANCZOS)


def main():
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, "TSC_KingTomb.png")
    make_tomb().save(path)
    print("wrote", os.path.normpath(path))


if __name__ == "__main__":
    main()
