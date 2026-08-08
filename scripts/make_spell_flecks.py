# Warp-free spell VFX textures. The mod's cast effects used vanilla's
# psycast flecks (FleckGlowDistortBackground family), whose shader bends the
# background - the "fish-eye". These are plain glow textures for MoteGlow:
# white, so instanceColor tints them per spell.
#
# Run:  py scripts/make_spell_flecks.py
import math
import os

from PIL import Image

OUT = os.path.join(os.path.dirname(__file__), "..", "Textures", "Things", "Mote")
os.makedirs(OUT, exist_ok=True)


def ring(size=512, radius=0.80, thickness=0.05):
    """Soft annulus, gaussian-feathered. Two constraints that are not
    cosmetic: RGB is WHITE everywhere including fully transparent pixels
    (transparent black + DXT alpha noise veiled the whole quad dark - the
    visible rectangle around big rings), and there is no low-alpha interior
    wash (near-flat faint alpha is where DXT banding shows at war-cry
    scale). Alpha snaps to exact 0 outside the band so the quad's border
    blocks compress empty and the rectangle edge cannot reappear."""
    img = Image.new("RGBA", (size, size), (255, 255, 255, 0))
    px = img.load()
    c = (size - 1) / 2
    r_out = radius * c
    sigma = thickness * c
    margin = size * 0.06
    for y in range(size):
        for x in range(size):
            d = math.hypot(x - c, y - c)
            a = 255 * math.exp(-((d - r_out) ** 2) / (2 * sigma * sigma))
            # ramp to exact 0 before the quad border; the gaussian tail
            # otherwise clips against it as a visible straight edge
            window = min(x, y, size - 1 - x, size - 1 - y) / margin
            a *= max(0.0, min(1.0, window))
            px[x, y] = (255, 255, 255, int(a) if a >= 3 else 0)
    img.save(os.path.join(OUT, "TSC_SpellRing.png"))
    print("wrote TSC_SpellRing.png")


def line(w=512, h=128):
    """Horizontal beam: bright core, feathered edges and ends. Same rules
    as ring(): white RGB under zero alpha, and alpha snaps to exact 0."""
    img = Image.new("RGBA", (w, h), (255, 255, 255, 0))
    px = img.load()
    cy = (h - 1) / 2
    for y in range(h):
        edge = math.exp(-((y - cy) ** 2) / (2 * (h * 0.16) ** 2))
        for x in range(w):
            endfade = min(1.0, x / (w * 0.12), (w - 1 - x) / (w * 0.12))
            a = 255 * edge * max(0.0, endfade)
            px[x, y] = (255, 255, 255, int(a) if a >= 3 else 0)
    img.save(os.path.join(OUT, "TSC_SpellLine.png"))
    print("wrote TSC_SpellLine.png")


ring()
line()
