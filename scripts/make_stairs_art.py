# TSC_StoneStairs: top-down worked-stone staircase descending into dark,
# for every portal that IS a stair (cellar, cistern, delve, keep). The
# barrow and the survey gallery keep vanilla's CaveEntranceA - those are
# mouths, not stairs. Drawn in neutral greys so each def's <color> tint
# still does the theming. Run:  py scripts\make_stairs_art.py
import os
import random

from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(__file__), "..", "Textures", "Things", "Building")

S = 512  # supersampled; saved at 256


def grey(v, a=255):
    v = max(0, min(255, int(v)))
    return (v, v, v, a)


def make_stairs(up=False):
    """Descending by default; up=True draws the return stair, treads
    brightening toward the surface light instead of drowning in dark."""
    rng = random.Random(20260730)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    frame_out = int(S * 0.04)          # outer edge of the stone collar
    frame_in = int(S * 0.16)           # inner edge (the shaft opening)

    # Soft ground shadow so the collar sits on terrain instead of floating.
    shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle(
        [frame_out - 8, frame_out - 4, S - frame_out + 8, S - frame_out + 12],
        radius=28, fill=(0, 0, 0, 110))
    img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(7)))

    # Stone collar: a ring of blocks with mortar seams and per-block jitter.
    d.rounded_rectangle([frame_out, frame_out, S - frame_out, S - frame_out],
                        radius=22, fill=grey(150))
    seam = grey(105)
    block = int(S * 0.115)
    for side in range(4):
        for i in range(frame_out, S - frame_out, block):
            v = 150 + rng.randint(-14, 16)
            pad = 3
            if side == 0:      # top
                box = [i + pad, frame_out + pad, min(i + block - pad, S - frame_out - pad), frame_in - pad]
            elif side == 1:    # bottom
                box = [i + pad, S - frame_in + pad, min(i + block - pad, S - frame_out - pad), S - frame_out - pad]
            elif side == 2:    # left
                box = [frame_out + pad, i + pad, frame_in - pad, min(i + block - pad, S - frame_out - pad)]
            else:              # right
                box = [S - frame_in + pad, i + pad, S - frame_out - pad, min(i + block - pad, S - frame_out - pad)]
            if box[2] > box[0] and box[3] > box[1]:
                d.rectangle(box, fill=grey(v), outline=seam, width=2)

    # The shaft. Treads descend to the NORTH (top of the tile): the nearest
    # tread sits at floor brightness, each one deeper is darker and slightly
    # narrower, and the last visible step drowns in the dark.
    left, right = frame_in, S - frame_in
    top, bottom = frame_in, S - frame_in
    d.rectangle([left, top, right, bottom], fill=grey(18))
    treads = 8
    span = bottom - top
    for i in range(treads):
        t0 = bottom - span * (i + 1) // treads
        t1 = bottom - span * i // treads
        inset = i * int(S * 0.012)
        v = (74 + i * 16) if up else (148 - i * 17)
        d.rectangle([left + inset, t0, right - inset, t1], fill=grey(v))
        # Tread lip: the worn edge that catches the light.
        d.rectangle([left + inset, t1 - 5, right - inset, t1], fill=grey(v + 34))
        # Faint wear channel down the middle of each step.
        cx0 = (left + right) // 2 - int(S * 0.10)
        cx1 = (left + right) // 2 + int(S * 0.10)
        d.rectangle([cx0, t0, cx1, t1 - 6], fill=grey(v - 8))

    # Side walls of the shaft shade inward over the funnel.
    for i in range(treads):
        t0 = bottom - span * (i + 1) // treads
        t1 = bottom - span * i // treads
        inset = i * int(S * 0.012)
        d.rectangle([left, t0, left + inset, t1], fill=grey(34))
        d.rectangle([right - inset, t0, right, t1], fill=grey(26))

    if up:
        # Daylight spilling down from the surface end.
        light = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        ld = ImageDraw.Draw(light)
        for i, a in enumerate((36, 64, 96, 150)):
            ld.rectangle([left, top, right, top + span * (4 - i) // 9], fill=(255, 252, 235, a))
        img.alpha_composite(light.filter(ImageFilter.GaussianBlur(8)))
    else:
        # Darkness pooling over the deep end.
        dark = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        dd = ImageDraw.Draw(dark)
        for i, a in enumerate((60, 105, 150, 205)):
            dd.rectangle([left, top, right, top + span * (4 - i) // 9], fill=(4, 4, 8, a))
        img.alpha_composite(dark.filter(ImageFilter.GaussianBlur(6)))

    # Mortar outline around the opening, so shaft and collar read as cut stone.
    d.rectangle([left, top, right, bottom], outline=grey(92), width=4)

    return img.resize((256, 256), Image.LANCZOS)


def make_gate():
    """The monastery gate, 3x1: grey oak a hand thick, iron straps, stone
    jambs. Drawn aged - it has been shut for eight hundred years."""
    rng = random.Random(20260731)
    W, H = 768, 256  # 3:1, saved at 384x128
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    jamb = 40
    # Stone jambs at each end.
    for x0 in (0, W - jamb):
        d.rectangle([x0, 0, x0 + jamb, H], fill=(128, 126, 122, 255))
        for y in range(0, H, 64):
            d.line([(x0, y), (x0 + jamb, y)], fill=(96, 94, 90, 255), width=4)
    # Oak leaves: vertical planks, grey-brown with age.
    for x in range(jamb, W - jamb, 48):
        v = 96 + rng.randint(-10, 10)
        d.rectangle([x, 8, x + 46, H - 8], fill=(v, int(v * 0.86), int(v * 0.68), 255))
        d.line([(x, 8), (x, H - 8)], fill=(52, 44, 34, 255), width=3)
    # The central seam where the leaves meet.
    d.line([(W // 2, 4), (W // 2, H - 4)], fill=(40, 34, 26, 255), width=6)
    # Iron straps, two per leaf, studded.
    iron, stud = (58, 60, 66, 255), (30, 31, 35, 255)
    for y in (H // 4, 3 * H // 4):
        d.rectangle([jamb, y - 14, W - jamb, y + 14], fill=iron)
        for x in range(jamb + 24, W - jamb, 48):
            d.ellipse([x - 6, y - 6, x + 6, y + 6], fill=stud)
    # Top and bottom shadow so it reads as set into the arch.
    d.rectangle([0, 0, W, 8], fill=(20, 18, 16, 255))
    d.rectangle([0, H - 8, W, H], fill=(20, 18, 16, 255))
    return img.resize((384, 128), Image.LANCZOS)


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, kwargs in (("TSC_StoneStairs.png", {}), ("TSC_StoneStairsUp.png", {"up": True})):
        path = os.path.join(OUT, name)
        make_stairs(**kwargs).save(path)
        print("wrote", os.path.normpath(path))
    gate = os.path.join(OUT, "TSC_MonasteryGate.png")
    make_gate().save(gate)
    print("wrote", os.path.normpath(gate))


if __name__ == "__main__":
    main()
