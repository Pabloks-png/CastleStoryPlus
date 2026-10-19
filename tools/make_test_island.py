#!/usr/bin/env python3
"""Makes the map "Test Island": a round, perfectly flat grass island with nothing on it (no trees, plants, boulders,
ore or crystals) and the player's crystal in the middle, for the DevTools test world (POST /api/testworld).

The map is written to the game's Info/Maps/TestIsland, from the official map Tortuga as the template (its factions,
idle groups, camera and settings). A map's terrain is Heightmap.png, 1024 x 1024, one pixel per column (x to the
right, z up from the bottom row): red is the column's lowest solid voxel, green one above its highest (solid for
red <= y < green), blue the voxel type and alpha its variant (HeightmapLayer). The player's starting point stands on
the top voxel (green - 1).

    tools/make_test_island.py [--radius 200] [--height 100] [--game "/path/to/Castle Story"]

Needs Pillow. Run it again to remake the map (a game started on the old one keeps its own copy in the save).
"""
import argparse
import json
import math
import os
import shutil
import uuid

from PIL import Image, ImageDraw

DEFAULT_GAME = os.path.expanduser("~/.local/share/Steam/steamapps/common/Castle Story")
SIZE = 1024
CENTRE = 512
# Grass, as Tortuga's meadow round its starting point (type 4 is bare earth).
GRASS = (9, 254)
# The cliff band under the edge, as Tortuga's rock.
ROCK = (7, 254)
EMPTY_LISTS = ["boulders", "crystals", "iron", "minerals", "plants", "pyrite", "trees"]


def heightmap(radius, top):
    """The island: flat on top at 'top' (the top voxel), deepest in the middle and rounded underneath."""
    image = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    pixels = image.load()
    for row in range(SIZE):
        z = SIZE - 1 - row
        for x in range(SIZE):
            r = math.hypot(x - CENTRE, z - CENTRE)
            if r > radius:
                continue
            edge = r / radius
            depth = int(round(6 + 44 * math.sqrt(max(0.0, 1.0 - edge * edge))))
            kind, variant = ROCK if edge > 0.985 else GRASS
            pixels[x, row] = (top + 1 - depth, top + 1, kind, variant)
    return image


def thumbnail(radius):
    image = Image.new("RGB", (512, 512), (52, 112, 168))
    draw = ImageDraw.Draw(image)
    scale = 512 / SIZE * 1.8
    r = radius * scale
    draw.ellipse((256 - r, 256 - r, 256 + r, 256 + r), fill=(96, 168, 64), outline=(120, 110, 96), width=4)
    draw.text((206, 246), "Test Island", fill=(255, 255, 255))
    return image


def move_start(objects, markers, at):
    """Every player starting point to 'at' (one crystal in the middle)."""
    position = {"$type": "UnityEngine.Vector3, UnityEngine", "x": float(at[0]), "y": float(at[1]), "z": float(at[2])}
    for marker in markers:
        marker["position"] = dict(position)
        marker["localPosition"] = dict(position)
    for obj in objects:
        if not obj.get("name", "").startswith("WorldEditorMarker"):
            continue
        for component in obj.get("components", []):
            kind = component.get("$type", "")
            if kind.startswith("UnityEngine.Transform"):
                component["position"] = dict(position)
            beacon = component.get("Beacon")
            if isinstance(beacon, dict):
                for inner in beacon.get("components", []):
                    if "position" in inner:
                        inner["position"] = {"$type": "Brix.Engine.XYZ, Brix.Core", "x": at[0], "y": at[1], "z": at[2]}
        if obj.get("name") == "Camera":
            pass


def place_camera(objects, at):
    for obj in objects:
        if obj.get("name") != "Camera":
            continue
        for component in obj.get("components", []):
            if component.get("$type", "").startswith("UnityEngine.Transform"):
                component["position"] = {"$type": "UnityEngine.Vector3, UnityEngine", "x": float(at[0]) + 60.0, "y": float(at[1]) + 80.0, "z": float(at[2]) - 60.0}


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--radius", type=int, default=200, help="island radius in voxels (default 200)")
    parser.add_argument("--height", type=int, default=100, help="y of the top voxel (default 100)")
    parser.add_argument("--game", default=DEFAULT_GAME, help="the Castle Story folder")
    args = parser.parse_args()
    maps = os.path.join(args.game, "Info", "Maps")
    template = os.path.join(maps, "Tortuga")
    target = os.path.join(maps, "TestIsland")
    if not os.path.isdir(template):
        raise SystemExit("No template map at " + template)

    codename = None
    old_meta = os.path.join(target, "meta.json")
    if os.path.exists(old_meta):
        with open(old_meta) as f:
            codename = json.load(f).get("__CODENAME")
        shutil.rmtree(target)
    os.makedirs(target)
    for name in ["Factions.json", "PlacedBlocks.json", "RemovedVoxels.json", "TerrainDamage.json", "TerrainSurfaceLayer.json"]:
        shutil.copy(os.path.join(template, name), os.path.join(target, name))
    for name in EMPTY_LISTS:
        with open(os.path.join(target, name + ".json"), "w") as f:
            f.write("[]")

    start = (CENTRE, args.height, CENTRE)
    with open(os.path.join(template, "gameobjects.json")) as f:
        objects = json.load(f)
    with open(os.path.join(template, "LuaMarkersCount.json")) as f:
        markers = json.load(f)
    move_start(objects, markers, start)
    place_camera(objects, start)
    with open(os.path.join(target, "gameobjects.json"), "w") as f:
        json.dump(objects, f, indent=2)
    with open(os.path.join(target, "LuaMarkersCount.json"), "w") as f:
        json.dump(markers, f, indent=2)

    with open(os.path.join(template, "meta.json")) as f:
        meta = json.load(f)
    # The same codename every time, so the game sees one map that changed, not a new one.
    meta["__CODENAME"] = codename or ("TestIsland_" + str(uuid.uuid4()))
    meta["__DISPLAYNAME"] = "Test Island"
    meta["__CUSTOMNAME"] = "Test Island"
    meta["__DESCRIPTION"] = "A flat island with nothing on it, for testing (Castle Story Plus DevTools)."
    meta["__MAPSIZE"] = "Small"
    meta["__WORKSHOPID"] = 0
    meta["__RATING"] = 0.5
    with open(os.path.join(target, "meta.json"), "w") as f:
        json.dump(meta, f, indent=2)

    heightmap(args.radius, args.height).save(os.path.join(target, "Heightmap.png"))
    thumbnail(args.radius).save(os.path.join(target, "thumbnail.png"))
    print("Made " + target + " (" + meta["__CODENAME"] + "): radius " + str(args.radius) + ", top at y " + str(args.height))


if __name__ == "__main__":
    main()
