# TSC_GearIcon: the gizmo that opens the company gear screen.
#
# A sword laid across a kite shield, drawn flat and high-contrast so it
# still reads at 24px in a gizmo strip next to vanilla's own icons.
#
# Run:  py scripts\make_gear_icon.py
import os

from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(__file__), "..", "Textures", "UI")

S = 512  # supersampled; saved at 128

STEEL = (208, 210, 216)
STEEL_DARK = (140, 144, 154)
SHIELD = (176, 148, 96)
SHIELD_DARK = (118, 96, 58)
GRIP = (96, 72, 48)


def shield(d):
    """A kite shield: straight shoulders, drawn down to a point."""
    body = [(150, 120), (362, 120), (356, 268), (256, 400), (156, 268)]
    d.polygon(body, fill=SHIELD_DARK)
    inset = [(166, 136), (346, 136), (341, 262), (256, 374), (171, 262)]
    d.polygon(inset, fill=SHIELD)
    # A band across the boss, so the shape does not read as a plain kite.
    d.polygon([(168, 214), (344, 214), (342, 250), (170, 250)], fill=SHIELD_DARK)


def sword(d):
    """Blade from lower left to upper right, over the shield."""
    d.line([(120, 402), (392, 130)], fill=STEEL_DARK, width=44)
    d.line([(128, 394), (384, 138)], fill=STEEL, width=26)
    # Point.
    d.polygon([(370, 124), (404, 118), (398, 152)], fill=STEEL)
    # Crossguard and grip, at the low end.
    d.line([(108, 356), (166, 414)], fill=STEEL_DARK, width=30)
    d.line([(74, 448), (122, 400)], fill=GRIP, width=28)
    d.ellipse([56, 430, 96, 470], fill=STEEL_DARK)


def main():
    os.makedirs(OUT, exist_ok=True)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    shield(d)
    sword(d)
    path = os.path.join(OUT, "TSC_GearIcon.png")
    img.resize((128, 128), Image.LANCZOS).save(path)
    print("wrote", os.path.normpath(path))


if __name__ == "__main__":
    main()
