# The gauntlets and sabatons ship as flat light silhouettes (Delapouite,
# game-icons.net, CC BY 3.0), which vanish against pale floors and snow.
# This gives them the dark keyline the rest of the mod's item art has:
# the glyph's own alpha, dilated, filled black, composited underneath.
#
# Idempotent: re-running on an already-outlined file just re-derives the
# same outline from the same silhouette, so it is safe to run twice.
#
# Run:  py scripts\outline_gear_icons.py
import os

from PIL import Image, ImageFilter

ART = os.path.join(os.path.dirname(__file__), "..", "Textures", "Things", "Item")
NAMES = ["TSC_Gauntlets.png", "TSC_Sabatons.png"]

# Dilation radius in working pixels. The art is 128px and drawn small in
# game, so the line has to be thick enough to survive downscaling.
GROW = 7
OUTLINE = (18, 16, 14, 255)


def outline(path):
    src = Image.open(path).convert("RGBA")
    alpha = src.split()[3]
    # Threshold first: the source has soft edges, and dilating a soft edge
    # gives a muddy halo instead of a line.
    solid = alpha.point(lambda a: 255 if a > 128 else 0)
    halo = solid.filter(ImageFilter.MaxFilter(GROW))
    ring = Image.new("RGBA", src.size, OUTLINE)
    ring.putalpha(halo)
    out = Image.alpha_composite(ring, src)
    out.save(path)
    print("outlined", os.path.basename(path))


if __name__ == "__main__":
    for name in NAMES:
        outline(os.path.join(ART, name))
