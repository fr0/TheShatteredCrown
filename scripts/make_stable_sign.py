# TSC_StablesBanner: the town stable's sign.
#
# Same shape and hardware as the other town signs (oak board, iron bracket,
# 128x160) so the six of them read as one set from across the square. The
# device is a horseshoe over a bale: what you can buy inside.
#
# Run:  py scripts\make_stable_sign.py
import os

from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(__file__), "..", "Textures", "Things", "Building")

W, H = 512, 640  # supersampled; saved at 128x160

IRON = (74, 70, 66)
IRON_LIGHT = (116, 110, 102)
BOARD = (108, 74, 44)
BOARD_DARK = (74, 50, 30)
BOARD_EDGE = (52, 34, 20)
STRAW = (198, 166, 88)
STRAW_DARK = (150, 122, 60)
SHOE = (196, 198, 204)
SHOE_DARK = (128, 132, 140)


def bracket(d):
    """The iron arm the board hangs from: post at the left, arm across."""
    d.rectangle([30, 40, 62, 120], fill=IRON)
    d.rectangle([30, 40, 300, 66], fill=IRON)
    d.ellipse([282, 46, 314, 78], fill=IRON_LIGHT)
    for x in (150, 250):
        d.line([(x, 66), (x, 104)], fill=IRON, width=8)


def board(d):
    """Oak board, planked, with a chamfered edge."""
    box = [56, 104, 456, 556]
    d.rounded_rectangle(box, radius=18, fill=BOARD_EDGE)
    d.rounded_rectangle([box[0] + 10, box[1] + 10, box[2] - 10, box[3] - 10], radius=14, fill=BOARD)
    for i in range(1, 4):
        y = box[1] + (box[3] - box[1]) * i / 4
        d.line([box[0] + 12, y, box[2] - 12, y], fill=BOARD_DARK, width=4)


def device(d):
    """A horseshoe, open end down, over a bale of straw."""
    cx, cy = 256, 300
    d.arc([cx - 118, cy - 128, cx + 118, cy + 108], start=180, end=360, fill=SHOE_DARK, width=54)
    d.arc([cx - 110, cy - 120, cx + 110, cy + 100], start=180, end=360, fill=SHOE, width=38)
    # The two heel ends, squared off.
    for x in (cx - 114, cx + 58):
        d.rectangle([x, cy - 10, x + 56, cy + 58], fill=SHOE_DARK)
        d.rectangle([x + 8, cy - 10, x + 48, cy + 46], fill=SHOE)
    # Nail holes.
    for angle_x in (-84, -46, 46, 84):
        d.ellipse([cx + angle_x - 9, cy - 74, cx + angle_x + 9, cy - 56], fill=SHOE_DARK)
    # The bale beneath.
    d.rounded_rectangle([cx - 130, cy + 92, cx + 130, cy + 200], radius=16, fill=STRAW_DARK)
    d.rounded_rectangle([cx - 120, cy + 100, cx + 120, cy + 190], radius=12, fill=STRAW)
    for x in range(cx - 100, cx + 101, 40):
        d.line([(x, cy + 104), (x - 14, cy + 186)], fill=STRAW_DARK, width=5)
    d.line([cx - 122, cy + 128, cx + 122, cy + 128], fill=STRAW_DARK, width=7)
    d.line([cx - 122, cy + 164, cx + 122, cy + 164], fill=STRAW_DARK, width=7)


def main():
    os.makedirs(OUT, exist_ok=True)
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle([66, 114, 466, 566], radius=18, fill=(0, 0, 0, 150))
    img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(10)))
    bracket(d)
    board(d)
    device(d)
    path = os.path.join(OUT, "TSC_StablesBanner.png")
    img.resize((128, 160), Image.LANCZOS).save(path)
    print("wrote", os.path.normpath(path))


if __name__ == "__main__":
    main()
