"""TSC_Woodpile (Harrowfield's Root farm set piece), redrawn in RimWorld's
orthographic 3/4 view: the top ~55%% is the pile seen from above (bark logs
lying east-west, a few split faces up), the bottom is the south face (rows of
cut ends, rounds and splits, packed over a dark interior), with end stakes
holding the stack the way farm cordwood is actually racked.

Output: Textures/Things/Building/TSC_Woodpile.png, 512x256 RGBA, drawn at 4x.
The def's drawSize (2.1, 1.05) is unchanged.

Run:  py scripts/make_woodpile_art.py
"""
import math
import os
import random

from PIL import Image, ImageDraw

SS = 4
OUT_W, OUT_H = 512, 256
CW, CH = OUT_W * SS, OUT_H * SS

OUTLINE = (44, 38, 32, 255)
BARK = (112, 86, 55, 255)
BARK_DARK = (88, 66, 42, 255)
BARK_LIGHT = (136, 106, 70, 255)
GRAIN = (198, 170, 126, 255)       # end grain / split face
GRAIN_DARK = (164, 136, 94, 255)
GAP = (40, 34, 28, 255)            # dark air between stacked ends
POST = (92, 68, 42, 255)

OUT_PATH = os.path.join(os.path.dirname(__file__), "..",
                        "Textures", "Things", "Building", "TSC_Woodpile.png")

LW = 9  # ~2.2px at output scale

rnd = random.Random(7)

img = Image.new("RGBA", (CW, CH), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# ground shadow, mostly hidden by the pile; castEdgeShadows does the rest
d.ellipse([70, 860, 1978, 1010], fill=(0, 0, 0, 55))

PILE_L, PILE_R = 150, 1898
TOP_Y0, TOP_Y1 = 170, 566          # top surface band
FACE_Y1 = 936                      # bottom of the south face


def draw_top_log(x0, x1, y0, y1, split_up):
    """One log seen from above: capsule body, end caps, grain along it."""
    body = GRAIN if split_up else rnd.choice([BARK, BARK_LIGHT])
    r = (y1 - y0) // 2
    d.rounded_rectangle([x0, y0, x1, y1], radius=r, fill=body,
                        outline=OUTLINE, width=LW)
    cy = (y0 + y1) // 2
    if split_up:
        # split face up: straight pale grain, one knot
        for fy in (0.32, 0.55, 0.74):
            gy = y0 + int((y1 - y0) * fy)
            d.line([x0 + r, gy, x1 - r, gy], fill=GRAIN_DARK, width=int(LW * 0.7))
        kx = rnd.randint(x0 + 300, x1 - 300)
        d.ellipse([kx - 22, cy - 16, kx + 22, cy + 16], fill=GRAIN_DARK,
                  outline=OUTLINE, width=int(LW * 0.5))
    else:
        # bark up: broken darker streaks, light catch along the crown
        for fy, tone in ((0.30, BARK_DARK), (0.62, BARK_DARK), (0.20, BARK_LIGHT)):
            gy = y0 + int((y1 - y0) * fy)
            sx = x0 + r
            while sx < x1 - r - 120:
                seg = rnd.randint(140, 380)
                d.line([sx, gy, min(sx + seg, x1 - r), gy], fill=tone,
                       width=int(LW * 0.7))
                sx += seg + rnd.randint(60, 160)
    # visible end grain caps
    for ex in (x0, x1):
        d.ellipse([ex - 26, y0 + 14, ex + 26, y1 - 14], fill=GRAIN,
                  outline=OUTLINE, width=int(LW * 0.8))


# --- top surface: three rows of logs, back row first ---------------------
rows = [
    (TOP_Y0, TOP_Y0 + 128, False),          # back, bark
    (TOP_Y0 + 132, TOP_Y0 + 262, True),     # middle, split faces up
    (TOP_Y0 + 266, TOP_Y1, False),          # front, bark
]
for y0, y1, split_up in rows:
    # each row is 2 logs of uneven length, like real stacking
    cut = rnd.randint(750, 1250)
    draw_top_log(PILE_L + rnd.randint(0, 30), PILE_L + cut, y0, y1, split_up)
    draw_top_log(PILE_L + cut + 18, PILE_R - rnd.randint(0, 30), y0, y1, split_up)

# --- south face: stacked cut ends over dark interior ---------------------
d.rectangle([PILE_L, TOP_Y1 - 20, PILE_R, FACE_Y1 - 30], fill=GAP)

R = 92
for row_i, cy in enumerate((700, 844)):
    cx = PILE_L + R + 24 + (R if row_i == 0 else 0)
    while cx <= PILE_R - R - 10:
        jitter = rnd.randint(-8, 8)
        y = cy + rnd.randint(-6, 6)
        rr = R + rnd.randint(-8, 4)
        box = [cx - rr + jitter, y - rr, cx + rr + jitter, y + rr]
        if rnd.random() < 0.45:
            # split piece: pale face, bark only on the outer arc, split lines
            d.ellipse(box, fill=GRAIN, outline=OUTLINE, width=LW)
            a0 = rnd.randint(0, 360)
            d.arc(box, a0, a0 + rnd.randint(120, 200), fill=BARK_DARK,
                  width=int(LW * 1.8))
            for _ in range(2):
                ang = rnd.uniform(0, math.pi)
                dx, dy = math.cos(ang) * rr * 0.72, math.sin(ang) * rr * 0.72
                d.line([cx + jitter - dx, y - dy, cx + jitter + dx, y + dy],
                       fill=GRAIN_DARK, width=int(LW * 0.7))
        else:
            # round: bark rim, end grain, off-center growth rings
            d.ellipse(box, fill=BARK, outline=OUTLINE, width=LW)
            d.ellipse([box[0] + 16, box[1] + 16, box[2] - 16, box[3] - 16],
                      fill=GRAIN)
            ox, oy = rnd.randint(-14, 14), rnd.randint(-14, 14)
            for ring in (0.62, 0.38, 0.16):
                rw = int(rr * ring)
                d.ellipse([cx + jitter + ox - rw, y + oy - rw,
                           cx + jitter + ox + rw, y + oy + rw],
                          outline=GRAIN_DARK, width=int(LW * 0.6))
        cx += int(rr * 2.02)

# --- end stakes, drawn last so they frame the stack ----------------------
for x0, x1 in ((96, 158), (1890, 1952)):
    d.rounded_rectangle([x0, 128, x1, 950], radius=26, fill=POST,
                        outline=OUTLINE, width=LW)
    d.line([x0 + (x1 - x0) // 3, 170, x0 + (x1 - x0) // 3, 910],
           fill=BARK_DARK, width=int(LW * 0.7))

img = img.resize((OUT_W, OUT_H), Image.LANCZOS)
img.save(OUT_PATH)
print("wrote", os.path.abspath(OUT_PATH))
