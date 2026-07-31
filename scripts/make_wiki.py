# Generates the classes / spells / feats wiki from the DEFS, so the pages
# can never drift from the game. Pure ElementTree - no game, no assembly.
#
#   docs/wiki/Home.md      index + the progression rules in brief
#   docs/wiki/Classes.md   every class with its abilities inline (combined page)
#   docs/wiki/Feats.md     general feats, then class feats by class
#
# Numbers are pulled from the defs (energy cost, cooldown, range, warmup);
# the effect prose is the def description - house style writes the real
# numbers into descriptions, so the wiki inherits them for free.
#
# Run:  py scripts\make_wiki.py
import glob
import io
import os
import re
from xml.etree import ElementTree as ET

ROOT = os.path.join(os.path.dirname(__file__), "..")
OUT = os.path.join(ROOT, "docs", "wiki")

NS = "TheShatteredCrown."


def load_defs():
    classes, abilities, feats, shard_powers = [], {}, [], set()
    for path in glob.glob(os.path.join(ROOT, "1.6", "Defs", "**", "*.xml"), recursive=True):
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError:
            continue
        for node in root:
            tag = node.tag
            if tag == NS + "TSC_ClassDef":
                classes.append(node)
            elif tag == "AbilityDef" and node.findtext("defName"):
                abilities[node.findtext("defName")] = node
            elif tag == NS + "TSC_FeatDef":
                feats.append(node)
            # Shard powers: abilities granted by carrying a crown shard.
            for ext in node.iter():
                if ext.get("Class") == NS + "TSC_ShardAbilityExtension":
                    name = ext.findtext("ability")
                    if name:
                        shard_powers.add(name)
    return classes, abilities, feats, shard_powers


def ticks_label(ticks):
    ticks = int(ticks)
    if ticks >= 60000:
        days = round(ticks / 60000, 1)
        return f"{days:g} day{'s' if days != 1 else ''}"
    return f"{round(ticks / 60, 1):g}s"


def ability_row(name, node):
    label = node.findtext("label", name)
    cooldown = node.findtext("cooldownTicksRange")
    verb = node.find("verbProperties")
    rng = verb.findtext("range") if verb is not None else None
    energy = None
    radius = None
    for comp in node.findall("comps/li"):
        cls = comp.get("Class", "")
        if cls.endswith("TSC_EnergyCost"):
            energy = comp.findtext("cost")
        if comp.findtext("radius"):
            radius = comp.findtext("radius")
    bits = []
    if energy:
        bits.append(f"{energy} energy")
    if cooldown:
        bits.append(f"{ticks_label(cooldown)} cooldown")
    if rng and float(rng) > 1:
        bits.append(f"range {float(rng):g}")
    if radius:
        bits.append(f"radius {float(radius):g}")
    stats = ", ".join(bits) if bits else "-"
    return label, stats, clean(node.findtext("description", ""))


def clean(text):
    return re.sub(r"\s+", " ", (text or "").replace("\\n", " ")).strip()


def title(text):
    """Word-initial caps only. str.title() capitalizes after apostrophes
    ("The King'S Mercy"), which is how that bug shipped."""
    return re.sub(r"(^|[\s\-])([a-z])", lambda m: m.group(1) + m.group(2).upper(), text)


def anchor(label):
    return re.sub(r"[^a-z0-9]+", "-", label.lower()).strip("-")


def main():
    os.makedirs(OUT, exist_ok=True)
    classes, abilities, feats, shard_powers = load_defs()
    classes.sort(key=lambda c: c.findtext("label", ""))

    # ---------------------------------------------------------- Classes.md
    # One combined page: each class carries its description, proficiencies,
    # icon strip (Steam) and abilities in unlock order. The old separate
    # unlock TABLE died in the merge - every ability heading already names
    # its level, and a table repeating the section right below it was
    # furniture.
    lines = ["# Classes and Abilities", "",
             "Class levels are assigned at level-up; every class also trains "
             "three proficiencies (bonus: 1 + class level / 4 on checks). "
             "Learning a class needs its class book, or a companion who "
             "teaches it. Energy recovers with sleep. In turn-based combat, "
             "cooldowns tick one round per turn (5s): a 10s cooldown is every "
             "other turn.", ""]
    for c in classes:
        label = title(c.findtext("label", "?"))
        lines += [f"## {label}", "", clean(c.findtext("description", "")), ""]
        profs = [p.text.replace("TSC_Prof_", "") for p in c.findall("proficiencies/li")]
        # Extra blank line: in the Steam guide the class blurb floats beside
        # the icon strip, and without it Proficiencies rides right up under
        # the description. Markdown collapses it, so the wiki is unaffected.
        lines += ["", f"**Proficiencies:** {', '.join(profs)}", ""]
        for u in sorted(c.findall("unlocks/li"), key=lambda u: int(u.findtext("level", "0"))):
            name = u.findtext("ability")
            node = abilities.get(name)
            if node is None:
                continue
            alabel, stats, desc = ability_row(name, node)
            lines += [f"### {title(alabel)}", "",
                      f"*Level {u.findtext('level')} - {stats}*", "", desc, ""]
    if shard_powers:
        lines += ["## Crown Shard Powers", "",
                  "Granted by CARRYING a shard, not by any class. The "
                  "cooldown belongs to the shard itself: hand it to another "
                  "carrier and the clock travels with it. No energy cost.", ""]
        for name in sorted(shard_powers):
            node = abilities.get(name)
            if node is None:
                continue
            alabel, stats, desc = ability_row(name, node)
            lines += [f"### {title(alabel)}", "", f"*{stats}*", "", desc, ""]
    write("Classes.md", lines)

    # ------------------------------------------------------------ Feats.md
    lines = ["# Feats", "",
             "One feat at character level 3 and every third level after. "
             "General feats are open to anyone; class feats require the "
             "listed class level and modify that class's abilities.", ""]
    general = [f for f in feats if not f.findtext("category")]
    by_class = {}
    for f in feats:
        cat = f.findtext("category")
        if cat:
            by_class.setdefault(cat, []).append(f)
    lines += ["## General", "", "| Feat | Effect |", "|---|---|"]
    for f in sorted(general, key=lambda f: int(f.findtext("order", "0"))):
        lines.append(f"| **{title(f.findtext("label", "?"))}** | {clean(f.findtext('description', ''))} |")
    lines.append("")
    for cat in sorted(by_class):
        lines += [f"## {cat}", "", "| Feat | Requires | Effect |", "|---|---|---|"]
        for f in sorted(by_class[cat], key=lambda f: int(f.findtext("order", "0"))):
            req = f.find("requirements/li")
            req_text = "-"
            if req is not None:
                cls = (req.findtext("classDef") or "").replace("TSC_Class_", "")
                req_text = f"{cls} {req.findtext('level', '?')}"
            lines.append(f"| **{title(f.findtext("label", "?"))}** | {req_text} "
                         f"| {clean(f.findtext('description', ''))} |")
        lines.append("")
    write("Feats.md", lines)

    # ------------------------------------------------------------- Home.md
    n_abilities = sum(1 for c in classes for _ in c.findall("unlocks/li"))
    write("Home.md", [
        "# The Shattered Crown - Player Wiki", "",
        f"- [Classes and Abilities](Classes.md) - {len(classes)} classes with all {n_abilities} abilities, plus the crown shard powers",
        f"- [Feats](Feats.md) - {len(general)} general and {sum(len(v) for v in by_class.values())} class feats", "",
        "Pages are GENERATED from the mod's defs by `scripts/make_wiki.py` - "
        "do not edit by hand; re-run the script after def changes.", ""])
    print(f"wiki: {len(classes)} classes, {n_abilities} class abilities, "
          f"{len(feats)} feats, {len(shard_powers)} shard powers -> docs/wiki/")


def write(name, lines):
    io.open(os.path.join(OUT, name), "w", encoding="utf-8", newline="\n").write("\n".join(lines))


# ---------------------------------------------------------------- Steam
# The same pages as Steam guide BBCode, one .txt per guide section. The
# markdown this script writes is regular enough to convert mechanically;
# cross-page links become plain text (guide sections cannot link into each
# other's anchors). Paste each file into one section of the guide editor.
STEAM_OUT = os.path.join(OUT, "steam")
# The PUBLIC guide keeps the crown's secrets: the shard powers section is
# late-campaign material, so it stays in the internal markdown (docs/wiki/)
# but is cut from the Steam output entirely - text and icon strip both.
# Flip when the guide should teach the endgame.
STEAM_INCLUDE_SHARDS = False
# Steam guide sections cap out around 8000 characters; oversized pages are
# split at heading boundaries into numbered parts.
SECTION_LIMIT = 7500


def md_to_bbcode(text):
    out = []
    in_table = False
    for line in text.split("\n"):
        if line.startswith("|"):
            cells = [c.strip() for c in line.strip("|").split("|")]
            if all(re.fullmatch(r"-+", c) for c in cells):
                continue  # the |---| separator row
            tag = "th" if not in_table else "td"
            if not in_table:
                out.append("[table]")
                in_table = True
            out.append("[tr]" + "".join(f"[{tag}]{inline(c)}[/{tag}]" for c in cells) + "[/tr]")
            continue
        if in_table:
            out.append("[/table]")
            in_table = False
        if line.startswith("### "):
            out.append(f"[h3]{inline(line[4:])}[/h3]")
        elif line.startswith("## "):
            out.append(f"[h2]{inline(line[3:])}[/h2]")
        elif line.startswith("# "):
            out.append(f"[h1]{inline(line[2:])}[/h1]")
        else:
            out.append(inline(line))
    if in_table:
        out.append("[/table]")
    return "\n".join(out)


def inline(text):
    text = re.sub(r"\[([^\]]+)\]\([^)]*\)", r"\1", text)          # links -> plain
    text = re.sub(r"\*\*([^*]+)\*\*", r"[b]\1[/b]", text)          # bold
    text = re.sub(r"(?<!\*)\*([^*\n]+)\*(?!\*)", r"[i]\1[/i]", text)  # italics
    return text


def emit_steam():
    os.makedirs(STEAM_OUT, exist_ok=True)
    classes, abilities, _, shard_powers = load_defs()
    classes.sort(key=lambda c: c.findtext("label", ""))
    strips = emit_icon_strips(classes, abilities, shard_powers)
    urls = load_image_urls()
    missing = set()
    for name in ("Classes.md", "Feats.md"):
        text = io.open(os.path.join(OUT, name), encoding="utf-8").read()
        bb = md_to_bbcode(text)
        if name == "Classes.md":
            if not STEAM_INCLUDE_SHARDS:
                # Cut from the shard heading to the end (it is the final
                # section of the page by construction).
                cut = bb.find("[h2]Crown Shard Powers[/h2]")
                if cut >= 0:
                    bb = bb[:cut].rstrip() + "\n"
                strips.pop("Crown Shard Powers", None)
            # The icon strips live where the abilities do.
            bb = inject_strips(bb, strips, urls, missing)
            # A rule before each Proficiencies line: it also clears the
            # floated icon strip, so the list never wraps around its edge.
            bb = bb.replace("[b]Proficiencies:[/b]", "[hr][/hr]\n[b]Proficiencies:[/b]")
        stem = name[:-3]
        if len(bb) <= SECTION_LIMIT:
            parts = [bb]
        else:
            # Split on [h2] boundaries, packing greedily under the limit.
            chunks = re.split(r"(?=\[h2\])", bb)
            parts, current = [], chunks[0]
            for chunk in chunks[1:]:
                if len(current) + len(chunk) > SECTION_LIMIT:
                    parts.append(current)
                    current = chunk
                else:
                    current += chunk
            parts.append(current)
        for i, part in enumerate(parts):
            suffix = f"_{i + 1}" if len(parts) > 1 else ""
            path = os.path.join(STEAM_OUT, f"{stem}{suffix}.txt")
            io.open(path, "w", encoding="utf-8", newline="\n").write(part)
            print(f"  steam/{stem}{suffix}.txt  {len(part)} chars")
    # The opening section: Steam surfaces it first, and convention says it
    # is the table of contents. Counts are live so it can never lie.
    n_abilities = sum(1 for c in classes for _ in c.findall("unlocks/li"))
    intro = "\n".join([
        "This guide is the player's reference for [b]The Shattered Crown[/b]: "
        "every class, every spell, every feat, with the real numbers - "
        "generated straight from the mod's files, so it is always current.",
        "",
        "[h1]Contents[/h1]",
        "[list]",
        f"[*][b]Classes & Abilities[/b] - all {len(classes)} classes: what "
        "each is for, the proficiencies it trains, and its "
        f"{n_abilities} abilities in unlock order with energy cost, "
        "cooldown, range and effect.",
        "[*][b]Feats[/b] - the general feats anyone can take, then the class "
        "feats and their requirements.",
        "[/list]",
        "",
        "[h1]The rules in brief[/h1]",
        "[list]",
        "[*]Every level-up offers a class level and a proficiency; multiclass freely.",
        "[*]One feat at character level 3 and every third level after.",
        "[*]Learning a class needs its class book, or a companion who teaches it.",
        "[*]Spells run on Energy, which recovers with sleep and scales with "
        "the class that granted them.",
        "[*]In turn-based combat, ability cooldowns tick one round per turn: "
        "a 10s cooldown is every other turn.",
        "[/list]",
        "",
        "Some late-campaign powers are deliberately left out. Find them.",
    ])
    io.open(os.path.join(STEAM_OUT, "Intro.txt"), "w", encoding="utf-8", newline="\n").write(intro)
    print(f"  steam/Intro.txt  {len(intro)} chars")
    if missing:
        print("\n  Icon strips not yet hosted - the BBCode carries tokens for them.")
        print("  Upload docs/wiki/steam/icons/*.png to the GUIDE (Add/Edit Images),")
        print("  insert each once with the editor to see its numeric id")
        print("  ([previewimg=<id>;...]), and put the ids in docs/workshop/image-urls.txt:")
        for token in sorted(missing):
            print(f"    {token}=")
        print("  Then re-run this script and re-paste the Classes sections.")


# ------------------------------------------------------------- icon strips
# One image per class for the Steam guide: its ability icons in unlock
# order, captioned, on the same dark plate ground as the description art.
# 12 uploads instead of 43. IMPORTANT: Steam GUIDES only render [img]
# for images ATTACHED TO THE GUIDE (Add/Edit Images -> steamusercontent
# URLs) - external hosts like postimg render as literal text, unlike
# workshop DESCRIPTIONS which accept any host. Upload the strips to the
# guide, copy each image address, and put those URLs in
# docs/workshop/image-urls.txt (WIKI_<CLASS>=https://images.steamusercontent...).
ICON_DIR = os.path.join(OUT, "steam", "icons")
GOLD = (232, 199, 112)


def emit_icon_strips(classes, abilities, shard_powers):
    try:
        from PIL import Image, ImageDraw, ImageFont
    except ImportError:
        print("  (PIL missing: icon strips skipped)")
        return {}

    def font(size):
        for name in ("georgiab.ttf", "arialbd.ttf"):
            p = os.path.join(r"C:\Windows\Fonts", name)
            if os.path.exists(p):
                return ImageFont.truetype(p, size)
        return ImageFont.load_default()

    os.makedirs(ICON_DIR, exist_ok=True)
    strips = {}

    def build(stem, entries):
        cell, icon_px, h = 108, 72, 128
        w = max(1, len(entries)) * cell + 16
        img = Image.new("RGB", (w, h), (16, 14, 11))
        d = ImageDraw.Draw(img)
        d.rectangle([0, 0, w - 1, h - 1], outline=(58, 50, 38))
        cap = font(15)
        for i, (label, icon_path) in enumerate(entries):
            x = 8 + i * cell
            full = os.path.join(ROOT, "Textures", icon_path + ".png")
            if os.path.exists(full):
                icon = Image.open(full).convert("RGBA").resize((icon_px, icon_px), Image.LANCZOS)
                img.paste(icon, (x + (cell - icon_px) // 2, 10), icon)
            text = title(label)
            tw = d.textlength(text, font=cap)
            while tw > cell - 4 and cap.size > 10:
                cap = font(cap.size - 1)
                tw = d.textlength(text, font=cap)
            d.text((x + (cell - tw) / 2, h - 34), text, font=cap, fill=GOLD)
        out = os.path.join(ICON_DIR, stem + ".png")
        img.save(out, optimize=True)
        return out

    for c in classes:
        clabel = c.findtext("label", "?")
        entries = []
        for u in sorted(c.findall("unlocks/li"), key=lambda u: int(u.findtext("level", "0"))):
            node = abilities.get(u.findtext("ability"))
            if node is not None and node.findtext("iconPath"):
                entries.append((node.findtext("label", "?"), node.findtext("iconPath")))
        if entries:
            build("class_" + clabel.lower(), entries)
            strips[title(clabel)] = ("WIKI_" + clabel.upper(), "class_" + clabel.lower() + ".png")
    shard_entries = []
    for name in sorted(shard_powers):
        node = abilities.get(name)
        if node is not None and node.findtext("iconPath"):
            shard_entries.append((node.findtext("label", "?"), node.findtext("iconPath")))
    if shard_entries:
        build("shard_powers", shard_entries)
        strips["Crown Shard Powers"] = ("WIKI_SHARDS", "shard_powers.png")
    return strips


def load_image_urls():
    urls = {}
    path = os.path.join(ROOT, "docs", "workshop", "image-urls.txt")
    if os.path.exists(path):
        for line in io.open(path, encoding="utf-8-sig"):
            line = line.strip()
            if line and not line.startswith("#") and "=" in line:
                key, value = line.split("=", 1)
                if value.strip():
                    urls[key.strip()] = value.strip()
    return urls


def inject_strips(bb, strips, urls, missing):
    # Steam GUIDES do not render [img] at all - not even steamusercontent
    # URLs. Guide-attached images use the guide's own tag:
    #   [previewimg=<fileid>;sizeOriginal,floatLeft;<filename>][/previewimg]
    # So the WIKI_ tokens hold each uploaded strip's numeric FILE ID (read
    # it from the tag the editor's own insert button produces).
    for heading, (token, filename) in strips.items():
        target = f"[h2]{heading}[/h2]"
        if target not in bb:
            continue
        value = (urls.get(token) or "").strip()
        if not value.isdigit():
            # Only the numeric file id works; a URL (or its hash segment)
            # renders nothing. Better a visible token than confident garbage.
            missing.add(token)
            tag = "{{" + token + "}}"
        else:
            tag = f"[previewimg={value};sizeOriginal,floatLeft;{filename}][/previewimg]"
        bb = bb.replace(target, f"{target}\n{tag}", 1)
    return bb


if __name__ == "__main__":
    main()
    emit_steam()
