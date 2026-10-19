#!/usr/bin/env python3
"""Photographs what Castle Story Plus adds or changes, each piece alone, for docs/Gallery.md: the mod's buildings
and blocks (with their build menu name and description), the 3 x 3 stockpile, the stockpiles at every Stacking tier
and with the mod's resources, and the armour stands with their kits.

Needs a running game (a sandbox; the test world itself need not be built) and Pillow. Each piece of the test world's
plan is shown alone in the DevTools photo studio (POST /api/studio), built and filled on the same spot, and
photographed from its front, centred and fitted to its size, without any of the menu (POST /api/screenshot?ui=false:
no menus, build job markers or icons over things).

    tools/make_gallery.py [--api http://127.0.0.1:27860] [--out docs] [--only NAME] [--all]

--only NAME takes only pieces whose name contains NAME (for trying); --all takes the game's own buildings and
blocks too.
"""
import argparse
import json
import math
import os
import re
import time
import urllib.parse
import urllib.request

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SIZE = (480, 300)
# The buildings' fronts face north (+z) as the studio places them: looked at from there, a little from the side.
YAW = 155
PITCH = 30
# Part of the screen's width kept; the piece is fitted into it.
CROP = 0.7
# The game's resources whose look on the 2 x 2 stockpile the mod changed or added (coal, steel, dark crystal, glass,
# blue crystal in its crate); the others look there as in the game until Stacking.
CHANGED_ON_PALLET = {"RawClay", "Terracotta", "DarkCrystal", "GlassIngot", "RawBlueCrystal"}
# The soldier class of a kit, from its items' names (the longest match wins: "Crossbow" holds "Bow").
CLASSES = [("Sword", "Knight"), ("Bow", "Archer"), ("Crossbow", "Arbalist"), ("Halbard", "Halberdier"),
           ("Alchemist", "Alchemist"), ("Geomancer", "Artificer")]
# Names of the stockpiles' resources as the game shows them.
RESOURCES = {"RawClay": "Coal", "Terracotta": "Steel", "DarkCrystal": "Dark crystal", "GlassIngot": "Glass",
             "RawBlueCrystal": "Blue crystal", "RawOrangeCrystal": "Orange crystal", "IronIngot": "Iron",
             "RawIron": "Raw iron", "RawStone": "Raw stone", "RawFiber": "Plant fibre", "Stone_DoubleBrick": "Brick",
             "Wood_DoubleLog_AA": "Log", "Wood_DoublePlank_AA": "Plank", "BlueCrystalChunck": "Blue crystal chunk",
             "PurifiedBrimstone": "Brimstone", "RawGlass": "Raw glass"}


def call(api, method, path, **query):
    url = api + path
    if query:
        url += "?" + urllib.parse.urlencode({k: str(v) for k, v in query.items()})
    request = urllib.request.Request(url, method=method, data=b"" if method == "POST" else None)
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.loads(response.read().decode("utf-8"))


def slug(text):
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")


def label(name):
    return name.replace("_", " ")


def resource(name):
    return RESOURCES.get(name, label(name))


def kit_class(kit):
    found = [(len(key), name) for key, name in CLASSES if key.lower() in kit.lower()]
    return max(found)[1] if found else label(kit.split("+")[0])


def wanted(piece):
    """What the mod made or changed: its own buildings and blocks, the 3 x 3 stockpile, the 2 x 2 one with Stacking
    or with a resource it looks new with, and the armour stands (kit bundles on them)."""
    if piece.get("kits"):
        return True
    if piece.get("fill"):
        if "Large" in piece["name"] or (piece.get("stackingTier") or 0) > 0:
            return True
        return piece["fill"] in CHANGED_ON_PALLET
    return bool(piece.get("mod"))


def fit_distance(size):
    """Camera distance that fits a piece of this size (x, y, z) into the cropped picture (about 40 degrees high)."""
    radius = 0.5 * math.sqrt(sum(v * v for v in size))
    return max(4.5, 3.6 * radius + 1.5)


def wait_ready(api, timeout=240):
    """Waits until the studio has the piece standing; answers its status."""
    until = time.time() + timeout
    status = None
    while time.time() < until:
        status = call(api, "GET", "/api/studio")
        if not status["running"]:
            return status
        time.sleep(0.4)
    return status


def shoot(api, bounds, file):
    """The world's cameras put in front of the piece for the picture (the game's camera script cannot shift them),
    the middle of it saved as a JPEG."""
    centre = bounds["centre"]
    path = call(api, "POST", "/api/screenshot", ui="false", name="gallery_shot", at="%.2f,%.2f,%.2f" % tuple(centre),
                distance="%.2f" % fit_distance(bounds["size"]), pitch=PITCH, yaw=YAW)["path"]
    for _ in range(50):
        if os.path.exists(path) and os.path.getsize(path) > 0:
            break
        time.sleep(0.1)
    time.sleep(0.3)
    image = Image.open(path).convert("RGB")
    w, h = image.size
    cw = int(w * CROP)
    ch = int(cw * SIZE[1] / SIZE[0])
    image = image.crop(((w - cw) // 2, (h - ch) // 2, (w + cw) // 2, (h + ch) // 2)).resize(SIZE, Image.LANCZOS)
    os.makedirs(os.path.dirname(file), exist_ok=True)
    image.save(file, "JPEG", quality=82, optimize=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--api", default="http://127.0.0.1:27860")
    parser.add_argument("--out", default=os.path.join(ROOT, "docs"))
    parser.add_argument("--only", help="only pieces whose name contains this (for trying)")
    parser.add_argument("--all", action="store_true", help="every piece, the game's own buildings and blocks too")
    args = parser.parse_args()
    pieces = call(args.api, "GET", "/api/studio/plan")["pieces"]
    images = os.path.join(args.out, "gallery")

    buildings = {}
    stockpiles = {}
    stands = []
    for piece in pieces:
        if args.only and args.only.lower() not in piece["name"].lower():
            continue
        if not args.all and not wanted(piece):
            continue
        if piece.get("fill"):
            file = os.path.join(images, "stockpiles", "%s-%s-t%d.jpg" % (slug(piece["name"]), slug(piece["fill"]), piece.get("stackingTier") or 0))
        elif piece.get("kits"):
            file = os.path.join(images, "stands", "stand-%d.jpg" % len(stands))
        else:
            file = os.path.join(images, slug(piece["section"]), slug(piece["name"]) + ".jpg")
        print("%3d/%d %s" % (piece["index"] + 1, len(pieces), os.path.relpath(file, args.out)), flush=True)
        # A stockpile of raw blue crystal takes minutes to fill: the studio may still be busy with the last piece.
        wait_ready(args.api)
        call(args.api, "POST", "/api/studio", index=piece["index"])
        status = wait_ready(args.api)
        if status and status["state"] == "ready" and not status.get("bounds") and status.get("spot"):
            # Nothing measured (a ladder is not solid): the studio's spot and the piece's size.
            size = piece["size"]
            spot = status["spot"]
            status["bounds"] = {"centre": [spot[0], spot[1] + size[1] / 2.0 - 0.5, spot[2]], "size": size}
        if not status or status["state"] != "ready" or not status.get("bounds"):
            print("    left out: %s" % (status["state"] if status else "no answer"), flush=True)
            continue
        shoot(args.api, status["bounds"], file)
        if piece.get("fill"):
            stockpiles.setdefault(piece["name"], {}).setdefault(piece["fill"], {})[piece.get("stackingTier") or 0] = file
        elif piece.get("kits"):
            stands.append((piece["kits"], file))
        else:
            buildings.setdefault(piece["section"], []).append((piece, file))

    write_page(args.out, buildings, stands, stockpiles)


def rel(out, file):
    return os.path.relpath(file, out).replace(os.sep, "/")


def plain(text, title):
    """A build menu description as plain text: without the game's markup and its title in front."""
    text = re.sub(r"<[^>]+>", "", text.replace("\n", " ")).strip()
    if title and text.startswith(title):
        text = text[len(title):].strip()
    return text


def table(lines, cells, columns=3):
    lines += ["| " + " | ".join([""] * columns) + " |", "|" + "---|" * columns]
    for i in range(0, len(cells), columns):
        row = cells[i:i + columns]
        lines.append("| " + " | ".join(row + [""] * (columns - len(row))) + " |")
    lines.append("")


def write_page(out, buildings, stands, stockpiles):
    lines = ["# Gallery", "",
             "What Castle Story Plus adds to the game or changes in how it looks, each photographed alone in the "
             "DevTools photo studio (`tools/make_gallery.py`).", ""]
    contents = [section for section in buildings] + (["Armour stands"] if stands else []) + (["Stockpiles"] if stockpiles else [])
    lines += ["- [%s](#%s)" % (section, slug(section)) for section in contents] + [""]
    for section, entries in buildings.items():
        lines += ["## " + section, ""]
        for piece, file in entries:
            title = piece.get("title") or label(piece["name"])
            lines += ["### " + title, "", "![%s](%s)" % (title, rel(out, file)), ""]
            if piece.get("description"):
                lines += [plain(piece["description"], title), ""]
    if stands:
        lines += ["## Armour stands", "",
                  "Weapon stands show the kits they hold: a stand for every soldier class, full of its kits, and one with "
                  "a kit of several classes (the mod's mixed stands).", ""]
        cells = []
        for kits, file in stands:
            text = ("Mixed: " + ", ".join(kit_class(k) for k in kits)) if len(kits) > 1 else kit_class(kits[0])
            cells.append("![%s](%s)<br>%s" % (text, rel(out, file), text))
        table(lines, cells)
    if stockpiles:
        lines += ["## Stockpiles", "",
                  "Full, at every tier of the Stacking research (0: not researched). The 3 x 3 stockpile is the mod's; "
                  "on the game's 2 x 2 one the mod draws the extra layers of Stacking and its own resources (coal, "
                  "steel, dark crystal), and glass and blue crystal look new.", ""]
        for kind, resources in stockpiles.items():
            title = "3 x 3 stockpile" if "Large" in kind else "2 x 2 stockpile"
            tiers = sorted({t for r in resources.values() for t in r})
            lines += ["### " + title, "", "| Resource | " + " | ".join("Stacking %d" % t for t in tiers) + " |",
                      "|---|" + "---|" * len(tiers)]
            for name, files in resources.items():
                cells = ["![%s %d](%s)" % (resource(name), t, rel(out, files[t])) if t in files else "" for t in tiers]
                lines.append("| %s | %s |" % (resource(name), " | ".join(cells)))
            lines.append("")
    with open(os.path.join(out, "Gallery.md"), "w") as f:
        f.write("\n".join(lines))
    print("Wrote", os.path.join(out, "Gallery.md"))


if __name__ == "__main__":
    main()
