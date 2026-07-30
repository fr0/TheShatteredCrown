# Section-header plates for the Workshop DESCRIPTION (not the gallery).
#
# Steam renders [img] tags inline in the page text, and the convention on
# polished mod pages is a plate per section instead of bare [h1] text.
# These match the title banner's language: gold Georgia caps on a dark
# ground, thin gold rules, a small diamond at each junction.
#
#   docs/workshop/header_*.png     one per description section
#
# Unlike the banner and screenshots these are hosted on IMGUR, not the
# item page: Add/Edit Images also feeds the preview carousel, and header
# plates are not screenshots. Direct i.imgur.com links go into
# docs/workshop/image-urls.txt, and Build-Release.ps1 -CopyDescription
# substitutes them.
#
# Run:  py scripts\make_banners.py
import os
import random

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.join(os.path.dirname(__file__), "..")
OUT = os.path.join(ROOT, "docs", "workshop")

GOLD = (232, 199, 112)
GOLD_DIM = (232, 199, 112, 150)
PARCHMENT = (226, 214, 186)

# token in WorkshopDescription.txt -> (file stem, plate text)
HEADERS = [
    ("IMG_H_WHAT", "header_what", "WHAT THIS MOD IS"),
    ("IMG_H_MODES", "header_modes", "TWO WAYS TO PLAY"),
    ("IMG_H_COMPAT", "header_compat", "COMPATIBILITY"),
    ("IMG_H_FAQ", "header_faq", "FAQ"),
    ("IMG_H_CREDITS", "header_credits", "CREDITS"),
]

W, H = 1200, 110  # banner width; Steam shows the description at 636 and scales


def font(name, size):
    for candidate in (name, "georgiab.ttf", "timesbd.ttf", "arialbd.ttf"):
        path = os.path.join(r"C:\Windows\Fonts", candidate)
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def ground(w=None, h=None):
    """Dark parchment-less dark: vertical gradient, faint grain, deep edges."""
    w, h = w or W, h or H
    img = Image.new("RGB", (w, h), (10, 9, 8))
    d = ImageDraw.Draw(img)
    for y in range(h):
        t = 1.0 - abs((y / h) - 0.5) * 2.0  # brightest mid-band
        d.line([(0, y), (w, y)], fill=(int(10 + 16 * t), int(9 + 12 * t), int(8 + 9 * t)))
    rng = random.Random(7)  # deterministic: re-runs produce identical files
    grain = Image.new("L", (w // 2, h // 2), 0)
    grain.putdata([rng.randint(0, 26) for _ in range((w // 2) * (h // 2))])
    grain = grain.resize((w, h)).filter(ImageFilter.GaussianBlur(0.6))
    img = Image.composite(Image.new("RGB", (w, h), (32, 27, 20)), img, grain)
    # edge fade so plates sit into the page instead of floating on it
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).rounded_rectangle([6, 4, w - 6, h - 4], radius=10, fill=255)
    dark = Image.new("RGB", (w, h), (5, 5, 5))
    return Image.composite(img, dark, mask.filter(ImageFilter.GaussianBlur(4)))


def tracked(d, text, fnt, tracking):
    """Width of text drawn with extra per-glyph advance."""
    return sum(d.textlength(ch, font=fnt) + tracking for ch in text) - tracking


def draw_tracked(d, x, y, text, fnt, tracking, fill):
    for ch in text:
        d.text((x + 2, y + 2), ch, font=fnt, fill=(0, 0, 0, 200))
        d.text((x, y), ch, font=fnt, fill=fill)
        x += d.textlength(ch, font=fnt) + tracking


def diamond(d, cx, cy, r, fill):
    d.polygon([(cx - r, cy), (cx, cy - r), (cx + r, cy), (cx, cy + r)], fill=fill)


def plate(text):
    img = ground().convert("RGBA")
    d = ImageDraw.Draw(img)
    size, tracking = 44, 7
    fnt = font("georgiab.ttf", size)
    while tracked(d, text, fnt, tracking) > W * 0.62 and size > 18:
        size -= 2
        fnt = font("georgiab.ttf", size)
    tw = tracked(d, text, fnt, tracking)
    x = (W - tw) / 2
    box = d.textbbox((0, 0), text, font=fnt)
    y = (H - (box[3] - box[1])) / 2 - box[1]
    draw_tracked(d, x, y, text, fnt, tracking, GOLD)
    # rules out to the edges, diamonds at the junctions
    cy = H // 2
    gap = 36
    d.line([(28, cy), (x - gap, cy)], fill=GOLD_DIM, width=2)
    d.line([(x + tw + gap, cy), (W - 28, cy)], fill=GOLD_DIM, width=2)
    diamond(d, x - gap, cy, 5, GOLD)
    diamond(d, x + tw + gap, cy, 5, GOLD)
    return img.convert("RGB")


def wordmark():
    """
    The title alone on a TRANSPARENT ground: gold gradient through the
    glyphs, dark stroke and soft shadow so it reads on Steam's dark page
    and on anything lighter. Trimmed to content.
    """
    text = "THE SHATTERED CROWN"
    W, Hc = 2600, 420
    img = Image.new("RGBA", (W, Hc), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    size, tracking = 170, 14
    fnt = font("georgiab.ttf", size)
    while tracked(d, text, fnt, tracking) > W * 0.94 and size > 40:
        size -= 4
        fnt = font("georgiab.ttf", size)
    tw = tracked(d, text, fnt, tracking)
    x0 = (W - tw) / 2
    box = d.textbbox((0, 0), text, font=fnt)
    y = (Hc - (box[3] - box[1])) / 2 - box[1]

    def draw_line(canvas, fill, stroke=None, stroke_w=0):
        dc = ImageDraw.Draw(canvas)
        x = x0
        for ch in text:
            dc.text((x, y), ch, font=fnt, fill=fill,
                    stroke_width=stroke_w, stroke_fill=stroke)
            x += dc.textlength(ch, font=fnt) + tracking

    # soft shadow, then the stroked glyphs, then the gradient through them
    shadow = Image.new("RGBA", (W, Hc), (0, 0, 0, 0))
    draw_line(shadow, (0, 0, 0, 200), stroke=(0, 0, 0, 200), stroke_w=6)
    img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(7)), (0, 8))

    draw_line(img, GOLD, stroke=(58, 42, 18, 255), stroke_w=3)

    mask = Image.new("L", (W, Hc), 0)
    draw_line(mask, 255)
    grad = Image.new("RGBA", (W, Hc))
    gd = ImageDraw.Draw(grad)
    top, bottom = (246, 224, 156), (198, 158, 78)
    for gy in range(Hc):
        t = gy / Hc
        gd.line([(0, gy), (W, gy)], fill=tuple(
            int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,))
    img.paste(grad, (0, 0), mask)

    pad = 30
    img = img.crop(img.getbbox())
    trimmed = Image.new("RGBA", (img.width + pad * 2, img.height + pad * 2), (0, 0, 0, 0))
    trimmed.alpha_composite(img, (pad, pad))
    return trimmed


# In-game sign texture stem -> caption. Order is reading order on the strip.
TOWN_SIGNS = [
    ("TSC_GuildBanner", "GUILD HALL"),
    ("TSC_TavernBanner", "TAVERN"),
    ("TSC_SmithBanner", "SMITH"),
    ("TSC_TempleBanner", "TEMPLE"),
    ("TSC_TrainingBanner", "TRAINING HALL"),
    ("TSC_MageBanner", "MAGE GUILD"),
]


def towns_strip():
    """
    The six town-facility signs, straight from the shipped textures, on the
    plate ground with a caption under each. One image instead of six saves
    five uploads and five [img] tags against the 8000-char page limit.
    """
    w, h = 1200, 260
    img = ground(w, h).convert("RGBA")
    d = ImageDraw.Draw(img)
    cap = font("georgiab.ttf", 22)
    cell = w // len(TOWN_SIGNS)
    tex = os.path.join(ROOT, "Textures", "Things", "Building")
    for i, (stem, caption) in enumerate(TOWN_SIGNS):
        sign = Image.open(os.path.join(tex, stem + ".png")).convert("RGBA")
        sign = sign.resize((sign.width * 3 // 2, sign.height * 3 // 2), Image.NEAREST)
        cx = i * cell + cell // 2
        shadow = Image.new("RGBA", sign.size, (0, 0, 0, 0))
        shadow.paste((0, 0, 0, 140), None, sign.split()[3])
        img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(4)),
                            (cx - sign.width // 2 + 3, 14 + 5))
        img.alpha_composite(sign, (cx - sign.width // 2, 14))
        tw = d.textlength(caption, font=cap)
        d.text((cx - tw / 2 + 1, h - 39 + 1), caption, font=cap, fill=(0, 0, 0, 200))
        d.text((cx - tw / 2, h - 39), caption, font=cap, fill=GOLD)
    return img.convert("RGB")


def main():
    made = []
    for token, stem, text in HEADERS:
        out = os.path.join(OUT, stem + ".png")
        plate(text).save(out, optimize=True)
        made.append((token, out))
        print(f"{token:16s} -> {os.path.relpath(out, ROOT)}")
    out = os.path.join(OUT, "banner.png")
    wordmark().save(out, optimize=True)
    print(f"{'IMG_BANNER':16s} -> {os.path.relpath(out, ROOT)} (transparent wordmark)")
    out = os.path.join(OUT, "towns.png")
    towns_strip().save(out, optimize=True)
    print(f"{'IMG_TOWNS':16s} -> {os.path.relpath(out, ROOT)} (facility signs strip)")
    print("\nUpload these to imgur (logged in, so they persist), right-click each")
    print("-> Copy image address, and put the i.imgur.com URLs in")
    print("docs/workshop/image-urls.txt.")
    return made


if __name__ == "__main__":
    main()
