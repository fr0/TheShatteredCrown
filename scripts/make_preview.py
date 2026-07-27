# Builds the Workshop imagery from gameplay screenshots:
#
#   About/Preview.png              the TILE - the one image RimWorld's
#                                  uploader publishes. Big title, readable
#                                  when Steam shrinks it to a thumbnail.
#   docs/workshop/gallery_*.png    the GALLERY - extra images to add by hand
#                                  on the Steam item page after publishing
#                                  ("Add/Edit Images"). Captioned, not
#                                  titled, so they read as a set.
#
# Raw captures live in docs/workshop/ and are never shipped to subscribers.
#
# Run:  py scripts\make_preview.py
import os

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont

ROOT = os.path.join(os.path.dirname(__file__), "..")
SHOTS = os.path.join(ROOT, "docs", "workshop")

TITLE = "THE SHATTERED CROWN"
SUBTITLE = "a medieval party RPG for RimWorld"

GOLD = (232, 199, 112)
PARCHMENT = (226, 214, 186)

# source, output, caption, options
#   fit "width"   - fill the frame edge to edge (world captures)
#   fit "contain" - whole image inside the frame, letterboxed (UI panels,
#                   where cropping would eat the part that matters)
#   bright        - cave shots need lifting; UI panels are already lit and
#                   wash out if you touch them
GALLERY = [
    ("turn_order.png", "gallery_01_turn_based.png",
     "Turn-based combat: initiative order, action points, and a combat log that shows the real numbers.",
     {"fit": "width", "bright": 1.45}),
    ("dialogue.png", "gallery_02_dialogue.png",
     "Hand-written characters, and choices that are yours to make.",
     {"fit": "contain", "bright": 1.0, "vignette": False}),
    ("tactical_ap.png", "gallery_03_action_points.png",
     "Every order is priced in action points before you commit, with the path cost previewed.",
     {"fit": "width", "bright": 1.45}),
]


def font(name, size):
    for candidate in (name, "georgiab.ttf", "timesbd.ttf", "arialbd.ttf"):
        path = os.path.join(r"C:\Windows\Fonts", candidate)
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def fit_to_canvas(src, w, h):
    """Fit full width (never crop the UI), pad the remainder with scenery."""
    scaled_h = int(src.height * (w / src.width))
    src = src.resize((w, scaled_h), Image.LANCZOS)
    img = Image.new("RGB", (w, h), (10, 9, 8))
    if scaled_h >= h:
        img.paste(src.crop((0, 0, w, h)), (0, 0))
        return img
    img.paste(src, (0, 0))
    tail = src.crop((0, scaled_h - 2, w, scaled_h)).resize((w, h - scaled_h + 2), Image.BILINEAR)
    img.paste(tail, (0, scaled_h - 2))
    return img


def contain_on_canvas(src, w, h, pad=0.90):
    """
    Whole image inside the frame, centred on a dark ground. For UI panels:
    fitting to width would push the bottom (the dialogue OPTIONS) off the
    canvas, and those are the point of the shot.
    """
    scale = min((w * pad) / src.width, (h * pad) / src.height)
    inner = src.resize((int(src.width * scale), int(src.height * scale)), Image.LANCZOS)
    img = Image.new("RGB", (w, h), (24, 19, 15))
    img.paste(inner, ((w - inner.width) // 2, (h - inner.height) // 2))
    return img


def grade(img, brightness=1.35, vignette_on=True):
    """Cave captures are nearly black; lift them, then re-deepen the edges."""
    w, h = img.size
    if abs(brightness - 1.0) > 0.01:
        img = ImageEnhance.Brightness(img).enhance(brightness)
        img = ImageEnhance.Color(img).enhance(1.15)
        img = ImageEnhance.Contrast(img).enhance(1.08)
    if not vignette_on:
        return img
    vignette = Image.new("L", (w, h), 0)
    ImageDraw.Draw(vignette).ellipse(
        [-int(w * 0.25), -int(h * 0.35), int(w * 1.25), int(h * 1.35)], fill=255
    )
    vignette = vignette.filter(ImageFilter.GaussianBlur(w * 0.10))
    return Image.composite(img, ImageEnhance.Brightness(img).enhance(0.45), vignette)


def plate(img, height_frac):
    """Dark gradient band along the bottom, gold rule on top of it."""
    w, h = img.size
    ph = int(h * height_frac)
    band = Image.new("RGBA", (w, ph), (0, 0, 0, 0))
    bd = ImageDraw.Draw(band)
    for y in range(ph):
        a = int(232 * min(1.0, (y / ph) * 1.5 + 0.25))
        bd.line([(0, y), (w, y)], fill=(8, 7, 6, a))
    img = img.convert("RGBA")
    img.alpha_composite(band, (0, h - ph))
    ImageDraw.Draw(img).line([(0, h - ph), (w, h - ph)], fill=GOLD + (170,), width=2)
    return img, ph


def centered(d, text, fnt, y, w, fill):
    tw = d.textbbox((0, 0), text, font=fnt)[2]
    x = (w - tw) // 2
    d.text((x + 2, y + 2), text, font=fnt, fill=(0, 0, 0, 210))
    d.text((x, y), text, font=fnt, fill=fill)


def build_tile():
    w, h = 1200, 675  # 16:9, above Steam's 640x360 floor
    src = Image.open(os.path.join(SHOTS, "turn_order.png")).convert("RGB")
    img = grade(fit_to_canvas(src, w, h))
    img, ph = plate(img, 0.32)
    d = ImageDraw.Draw(img)
    top = h - ph + int(ph * 0.14)
    centered(d, TITLE, font("georgiab.ttf", 74), top, w, GOLD)
    centered(d, SUBTITLE, font("georgia.ttf", 30), top + 84, w, PARCHMENT)
    out = os.path.join(ROOT, "About", "Preview.png")
    img.convert("RGB").save(out, optimize=True)
    return out


def build_gallery():
    w, h = 1920, 1080
    made = []
    for source, name, caption, opts in GALLERY:
        path = os.path.join(SHOTS, source)
        if not os.path.exists(path):
            print("  skipping missing %s" % source)
            continue
        src = Image.open(path).convert("RGB")
        plate_frac = 0.16
        if opts.get("fit") == "contain":
            # Fit ABOVE the caption plate, not behind it: the last dialogue
            # option is the one that shows choices have consequences, and it
            # was landing under the caption.
            free_h = h - int(h * plate_frac)
            framed = contain_on_canvas(src, w, free_h, pad=0.94)
            canvas = Image.new("RGB", (w, h), (24, 19, 15))
            canvas.paste(framed, (0, 0))
            framed = canvas
        else:
            framed = fit_to_canvas(src, w, h)
        img = grade(framed, brightness=opts.get("bright", 1.45),
                    vignette_on=opts.get("vignette", True))
        img, ph = plate(img, plate_frac)
        d = ImageDraw.Draw(img)
        centered(d, caption, font("georgia.ttf", 40), h - ph + int(ph * 0.30), w, PARCHMENT)
        out = os.path.join(SHOTS, name)
        img.convert("RGB").save(out, optimize=True)
        made.append(out)
    return made


def main():
    tile = build_tile()
    print("tile:    %s (%.0f KB)" % (os.path.normpath(tile), os.path.getsize(tile) / 1024))
    for g in build_gallery():
        print("gallery: %s (%.0f KB)" % (os.path.normpath(g), os.path.getsize(g) / 1024))
    print("\nThe tile ships with the mod. Add the gallery images by hand on the")
    print("Steam item page after publishing (Add/Edit Images).")


if __name__ == "__main__":
    main()
