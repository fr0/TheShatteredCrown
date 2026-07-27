# Buff overlay motes: TSC_BarkWreath (spinning leaf ring for Barkskin) and
# TSC_ShieldMark (small steel shield for Stand Fast). Output goes to
# Textures/Things/Mote/. Run:  py scripts\make_buff_overlays.py
import math
import os

from PIL import Image, ImageDraw, ImageFilter

OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "Textures", "Things", "Mote")

LEAF_GREENS = [(96, 138, 56, 255), (118, 158, 66, 255), (72, 110, 48, 255)]
TWIG_BROWN = (98, 74, 46, 255)
TWIG_DARK = (74, 55, 34, 255)


def make_wreath(size=256):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    cx = cy = size / 2
    ring_r = size * 0.36

    # Twig ring underneath the leaves: two rough concentric strokes.
    for r, color, w in ((ring_r, TWIG_DARK, 9), (ring_r, TWIG_BROWN, 5)):
        draw.ellipse([cx - r - w, cy - r - w, cx + r + w, cy + r + w], outline=color, width=w)

    # Leaves: ellipses laid tangentially around the ring, alternating greens.
    n = 16
    leaf_w, leaf_h = int(size * 0.16), int(size * 0.085)
    for i in range(n):
        angle = (360.0 / n) * i
        rad = math.radians(angle)
        lx = cx + ring_r * math.cos(rad)
        ly = cy + ring_r * math.sin(rad)
        leaf = Image.new("RGBA", (leaf_w, leaf_h), (0, 0, 0, 0))
        ld = ImageDraw.Draw(leaf)
        color = LEAF_GREENS[i % len(LEAF_GREENS)]
        ld.ellipse([0, 0, leaf_w - 1, leaf_h - 1], fill=color)
        # Midrib for a bit of read at small sizes.
        rib = tuple(max(0, c - 30) for c in color[:3]) + (255,)
        ld.line([2, leaf_h // 2, leaf_w - 3, leaf_h // 2], fill=rib, width=1)
        # Tangent to the ring, jittered so it reads organic, not geometric.
        leaf = leaf.rotate(-(angle + 90) + (13 if i % 2 else -9), expand=True, resample=Image.BICUBIC)
        img.alpha_composite(leaf, (int(lx - leaf.width / 2), int(ly - leaf.height / 2)))

    # A few berries for color contrast.
    for i in range(5):
        angle = math.radians(72 * i + 20)
        bx = cx + ring_r * math.cos(angle)
        by = cy + ring_r * math.sin(angle)
        draw.ellipse([bx - 4, by - 4, bx + 4, by + 4], fill=(150, 46, 42, 255))

    return img.filter(ImageFilter.GaussianBlur(0.4))


def make_shield(size=128):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    m = size * 0.12  # margin
    top = size * 0.16
    mid = size * 0.55
    # Heater shield: flat-ish top, sides curving to a bottom point.
    pts = [
        (m, top), (size - m, top),                      # top edge
        (size - m, mid), (size / 2, size - m),          # right curve to point
        (m, mid),                                        # left curve back up
    ]
    outline = (52, 66, 96, 255)
    fill = (150, 176, 216, 255)
    fill_dark = (112, 138, 180, 255)
    rim = (214, 228, 248, 255)

    draw.polygon(pts, fill=fill, outline=None)
    # Lower half shading (cheap depth): darker polygon over the bottom part.
    draw.polygon([(m, mid), (size - m, mid), (size / 2, size - m)], fill=fill_dark)
    # Rim + outline.
    draw.line(pts + [pts[0]], fill=rim, width=5, joint="curve")
    draw.line(pts + [pts[0]], fill=outline, width=2, joint="curve")
    # Gloss stroke.
    draw.line([(size * 0.3, size * 0.26), (size * 0.62, size * 0.24)], fill=(235, 242, 252, 210), width=4)
    # Central boss.
    bx, by, br = size / 2, size * 0.42, size * 0.07
    draw.ellipse([bx - br, by - br, bx + br, by + br], fill=rim, outline=outline, width=2)

    return img.filter(ImageFilter.GaussianBlur(0.4))


# ---------------------------------------------------------------- glyph marks
# Small status glyphs (128px, drawn bold - they render at ~half a cell).
# Every glyph gets a dark halo so it reads on any terrain.

def _glyph(size=128):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img), size


def _halo(img):
    """Dark soft outline behind the glyph: alpha silhouette, blurred."""
    alpha = img.split()[3].filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.GaussianBlur(2))
    halo = Image.new("RGBA", img.size, (20, 16, 10, 0))
    halo.putalpha(alpha.point(lambda a: min(200, a)))
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    out.alpha_composite(halo)
    out.alpha_composite(img)
    return out


def make_hymn_note():  # Battle Hymn: gold eighth-note, one connected shape
    S = 512  # supersampled, downscaled to 128 at the end
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    gold = (238, 196, 76, 255)
    stem_w = S * 0.075
    head_cx, head_cy = S * 0.36, S * 0.72
    head_rx, head_ry = S * 0.16, S * 0.115
    stem_x1 = head_cx + head_rx * 0.98   # stem hugs the head's right edge
    stem_x0 = stem_x1 - stem_w
    stem_top = S * 0.16

    # Head: tilted ellipse (drawn straight, rotated, pasted).
    head = Image.new("RGBA", (int(head_rx * 2) + 8, int(head_ry * 2) + 8), (0, 0, 0, 0))
    hd = ImageDraw.Draw(head)
    hd.ellipse([2, 2, head_rx * 2 + 4, head_ry * 2 + 4], fill=gold)
    head = head.rotate(20, expand=True, resample=Image.BICUBIC)
    img.alpha_composite(head, (int(head_cx - head.width / 2), int(head_cy - head.height / 2)))

    # Stem: flush with the head, one flat rectangle.
    d.rectangle([stem_x0, stem_top, stem_x1, head_cy], fill=gold)

    # Flag: a tapering swoosh growing straight out of the stem top - outer
    # curve down-right, inner curve back, closed into one polygon.
    pts = []
    steps = 14
    for i in range(steps + 1):
        t = i / steps
        pts.append((stem_x1 + S * 0.26 * math.sin(t * math.pi / 2), stem_top + S * 0.36 * t))
    for i in range(steps, -1, -1):
        t = i / steps
        pts.append((stem_x0 + S * 0.14 * math.sin(t * math.pi / 2), stem_top + stem_w * 0.6 + S * 0.30 * t))
    d.polygon(pts, fill=gold)

    return _halo(img.resize((128, 128), Image.LANCZOS))


def make_ward_rune():  # Arcane Ward: cyan sigil diamond
    img, d, s = _glyph()
    cyan, core = (98, 216, 240, 255), (210, 248, 255, 255)
    pts = [(s / 2, s * 0.1), (s * 0.88, s / 2), (s / 2, s * 0.9), (s * 0.12, s / 2)]
    d.line(pts + [pts[0]], fill=cyan, width=9, joint="curve")
    d.line([(s / 2, s * 0.28), (s / 2, s * 0.72)], fill=core, width=7)
    d.line([(s * 0.34, s / 2), (s * 0.66, s / 2)], fill=core, width=7)
    return _halo(img)


def make_rage_flame():  # Rage: red flame, yellow core
    img, d, s = _glyph()
    d.polygon([(s * 0.5, s * 0.08), (s * 0.74, s * 0.38), (s * 0.66, s * 0.5), (s * 0.82, s * 0.62),
               (s * 0.6, s * 0.92), (s * 0.36, s * 0.92), (s * 0.2, s * 0.6), (s * 0.4, s * 0.46),
               (s * 0.34, s * 0.3)], fill=(214, 62, 40, 255))
    d.polygon([(s * 0.5, s * 0.42), (s * 0.62, s * 0.64), (s * 0.5, s * 0.85), (s * 0.38, s * 0.64)],
              fill=(250, 200, 80, 255))
    return _halo(img)


def make_anvil():  # Unbreakable: grey anvil
    img, d, s = _glyph()
    steel, hi = (150, 156, 168, 255), (208, 214, 224, 255)
    d.polygon([(s * 0.1, s * 0.3), (s * 0.9, s * 0.3), (s * 0.82, s * 0.5), (s * 0.58, s * 0.56),
               (s * 0.58, s * 0.68), (s * 0.42, s * 0.68), (s * 0.42, s * 0.56), (s * 0.26, s * 0.5)],
              fill=steel)
    d.polygon([(s * 0.3, s * 0.68), (s * 0.7, s * 0.68), (s * 0.78, s * 0.84), (s * 0.22, s * 0.84)], fill=steel)
    d.line([(s * 0.12, s * 0.32), (s * 0.88, s * 0.32)], fill=hi, width=5)
    return _halo(img)


def _chevrons(color, n=2, slant=0.26):
    img, d, s = _glyph()
    w = 14
    for i in range(n):
        x = s * (0.2 + slant * i)
        d.line([(x, s * 0.2), (x + s * 0.24, s * 0.5), (x, s * 0.8)], fill=color, width=w, joint="curve")
    return _halo(img)


def make_flurry():  # Flurry: white double chevron
    return _chevrons((245, 245, 240, 255), n=2, slant=0.3)


def make_charge():  # Charge: blue triple chevron
    return _chevrons((120, 176, 240, 255), n=3, slant=0.22)


def _arrow(color, glow=None):
    img, d, s = _glyph()
    if glow:
        # Charge burst: short rays radiating off the arrowhead (a closed
        # ring read as a magnifying glass). Skips the shaft's direction.
        cx, cy = s * 0.753, s * 0.247
        r0, r1 = s * 0.14, s * 0.24
        for deg in (-80, -35, 10, 55, 100, 145):
            rad = math.radians(deg)
            d.line([(cx + r0 * math.cos(rad), cy + r0 * math.sin(rad)),
                    (cx + r1 * math.cos(rad), cy + r1 * math.sin(rad))], fill=glow, width=6)
    a, b = (s * 0.18, s * 0.82), (s * 0.8, s * 0.2)
    d.line([a, b], fill=color, width=10)
    d.polygon([(s * 0.86, s * 0.14), (s * 0.62, s * 0.22), (s * 0.78, s * 0.38)], fill=color)
    for f in (0.0, 0.12):
        d.line([(s * (0.18 + f), s * (0.82 - f)), (s * (0.06 + f), s * (0.78 - f))], fill=color, width=7)
        d.line([(s * (0.18 + f), s * (0.82 - f)), (s * (0.22 + f), s * (0.94 - f))], fill=color, width=7)
    return _halo(img)


def make_quiver_arrow():  # Swift Quiver: pale gold arrow
    return _arrow((236, 226, 160, 255))


def make_charged_arrow():  # Charged Shot: green arrow, charged ring
    return _arrow((150, 230, 110, 255), glow=(210, 255, 170, 255))


def make_crossed_swords():  # Challenged: orange crossed blades
    img, d, s = _glyph()
    blade, hilt = (222, 226, 234, 255), (222, 130, 62, 255)
    for flip in (1, -1):
        x0, x1 = (s * 0.18, s * 0.82) if flip == 1 else (s * 0.82, s * 0.18)
        d.line([(x0, s * 0.16), (x1, s * 0.8)], fill=blade, width=10)
        d.line([(x1 + (s * 0.05 * flip), s * 0.86), (x1 - (s * 0.05 * flip), s * 0.74)], fill=hilt, width=8)
    return _halo(img)


def make_tremor():  # Shaken: purple tremor waves
    img, d, s = _glyph()
    purple = (172, 134, 210, 255)
    for i, y in enumerate((0.3, 0.52, 0.74)):
        pts = []
        for t in range(0, 21):
            x = s * (0.14 + 0.72 * t / 20)
            pts.append((x, s * y + math.sin(t / 20 * math.pi * 3 + i) * s * 0.045))
        d.line(pts, fill=purple, width=9, joint="curve")
    return _halo(img)


def make_target():  # Marked: red target reticle
    img, d, s = _glyph()
    red = (230, 62, 52, 255)
    d.ellipse([s * 0.2, s * 0.2, s * 0.8, s * 0.8], outline=red, width=9)
    d.ellipse([s * 0.44, s * 0.44, s * 0.56, s * 0.56], fill=red)
    for dx, dy in ((0, -1), (0, 1), (-1, 0), (1, 0)):
        cx, cy = s / 2, s / 2
        d.line([(cx + dx * s * 0.3, cy + dy * s * 0.3), (cx + dx * s * 0.46, cy + dy * s * 0.46)], fill=red, width=9)
    return _halo(img)


def make_lead_weight():  # Leaden: slate kettlebell weight
    img, d, s = _glyph()
    slate, hi = (128, 116, 152, 255), (176, 166, 196, 255)
    d.arc([s * 0.3, s * 0.1, s * 0.7, s * 0.5], 180, 360, fill=slate, width=11)
    d.polygon([(s * 0.22, s * 0.38), (s * 0.78, s * 0.38), (s * 0.7, s * 0.86), (s * 0.3, s * 0.86)], fill=slate)
    d.line([(s * 0.3, s * 0.44), (s * 0.66, s * 0.44)], fill=hi, width=5)
    return _halo(img)


def make_thorn_snare():  # Snared: thorned vine loop
    img, d, s = _glyph()
    vine, thorn = (96, 128, 58, 255), (70, 96, 44, 255)
    cx, cy, r = s / 2, s / 2, s * 0.32
    d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=vine, width=10)
    for i in range(8):
        ang = math.radians(45 * i + 12)
        bx, by = cx + r * math.cos(ang), cy + r * math.sin(ang)
        tx, ty = cx + (r + s * 0.12) * math.cos(ang + 0.28), cy + (r + s * 0.12) * math.sin(ang + 0.28)
        d.polygon([(bx - 5, by - 5), (bx + 5, by + 5), (tx, ty)], fill=thorn)
    return _halo(img)


def make_brand():  # Righteous Brand: gold flame over a bar
    img, d, s = _glyph()
    gold, core = (240, 190, 70, 255), (255, 240, 190, 255)
    d.polygon([(s * 0.5, s * 0.08), (s * 0.68, s * 0.34), (s * 0.6, s * 0.46), (s * 0.72, s * 0.56),
               (s * 0.5, s * 0.72), (s * 0.28, s * 0.56), (s * 0.4, s * 0.46), (s * 0.32, s * 0.34)], fill=gold)
    d.polygon([(s * 0.5, s * 0.3), (s * 0.58, s * 0.5), (s * 0.5, s * 0.64), (s * 0.42, s * 0.5)], fill=core)
    d.line([(s * 0.24, s * 0.84), (s * 0.76, s * 0.84)], fill=gold, width=11)
    return _halo(img)


def make_crown():  # Kingsblade: gold crown, gem in the band
    img, d, s = _glyph()
    gold, dark, gem = (240, 202, 88, 255), (168, 128, 36, 255), (176, 44, 48, 255)
    # Band + three points, one closed silhouette.
    pts = [
        (s * 0.16, s * 0.74), (s * 0.16, s * 0.36), (s * 0.31, s * 0.54),
        (s * 0.5, s * 0.22), (s * 0.69, s * 0.54), (s * 0.84, s * 0.36),
        (s * 0.84, s * 0.74),
    ]
    d.polygon(pts, fill=gold)
    d.line([(s * 0.16, s * 0.68), (s * 0.84, s * 0.68)], fill=dark, width=4)
    for x in (0.16, 0.5, 0.84):
        d.ellipse([s * x - 6, s * (0.3 if x == 0.5 else 0.4) - 6,
                   s * x + 6, s * (0.3 if x == 0.5 else 0.4) + 6], fill=gold)
    d.ellipse([s * 0.44, s * 0.7, s * 0.56, s * 0.82], fill=gem)
    return _halo(img)


GLYPHS = {
    "TSC_CrownMark": make_crown,
    "TSC_HymnNote": make_hymn_note,
    "TSC_WardRune": make_ward_rune,
    "TSC_RageFlame": make_rage_flame,
    "TSC_AnvilMark": make_anvil,
    "TSC_FlurryChevrons": make_flurry,
    "TSC_ChargeChevrons": make_charge,
    "TSC_QuiverArrow": make_quiver_arrow,
    "TSC_ChargedArrow": make_charged_arrow,
    "TSC_CrossedSwords": make_crossed_swords,
    "TSC_TremorLines": make_tremor,
    "TSC_TargetMark": make_target,
    "TSC_LeadWeight": make_lead_weight,
    "TSC_ThornSnare": make_thorn_snare,
    "TSC_BrandFlame": make_brand,
}


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    make_wreath().save(os.path.join(OUT_DIR, "TSC_BarkWreath.png"))
    make_shield().save(os.path.join(OUT_DIR, "TSC_ShieldMark.png"))
    for name, fn in GLYPHS.items():
        fn().save(os.path.join(OUT_DIR, name + ".png"))
    print(f"wrote wreath, shield, and {len(GLYPHS)} glyph marks ->", os.path.normpath(OUT_DIR))


if __name__ == "__main__":
    main()
