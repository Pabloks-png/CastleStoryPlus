#!/usr/bin/env python3
"""Renders the build menu icons of the mod's buildings and blocks in CastleStoryPlus/Assets/Build/.

In the style of the game's own build icons: an isometric drawing of the thing itself, flat colours with a lit top,
a mid front and a dark right side, a dark outline and no background. The blocks are drawn like the game's block icons
(see GameView). One 128 x 128 PNG per entry. The game shows them through CastleStoryPlus.BuildIcon("<name>") in its
build menu Lua.

Needs rsvg-convert (librsvg). Run: python3 tools/make_build_icons.py [name ...]   (no names: all icons)
"""
import math
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "CastleStoryPlus", "Assets", "Build")
SIZE = 128
INK = "#1b1b1f"

STONE = ("#d3d1cb", "#aeaba3", "#8d8a83")
WOOD = ("#c99652", "#9c6c33", "#7a5226")
WOOD_DARK = ("#8a5d2e", "#6d4520", "#553418")
BRICK = ("#c96a4a", "#a5512f", "#863f24")
ROOF = ("#b4553a", "#93412b", "#76331f")
IRON = ("#a7adb4", "#80868d", "#62676e")
AWNING_RED = ("#e0523f", "#bd3a2b", "#9b2c20")
AWNING_WHITE = ("#f5efe2", "#d8d0bf", "#bcb3a0")
DARK = ("#3a2a1e", "#2c2017", "#211811")
FIRE = ("#ffd34d", "#ff9a2e", "#e0701a")
CRYSTAL = ("#9be0ff", "#58c4ff", "#2a8fd0")
DARK_CRYSTAL = ("#c78bff", "#9a4ee0", "#6e2ab0")
BOOKS = [("#c05048", "#9a3a32", "#7a2a24"), ("#4f8a56", "#3a6a40", "#2a5030"), ("#4f6ea8", "#3a5488", "#2a3e68"),
         ("#c8a050", "#a07c38", "#7c5e28")]
PAPER = ("#fbf4e0", "#e6dcc0", "#cbbf9e")
LEAVES = ("#8fd16a", "#5fa844", "#3f7f2e")
SOIL = ("#8a6a48", "#6e5236", "#553e28")


class Scene:
    """Isometric drawing: x to the right-down, z to the left-down, y up. Shapes are drawn in the order given."""

    def __init__(self, span=None):
        self.polys = []
        # A fixed size in projected units, so blocks of different sizes keep their relative size.
        self.span = span

    @staticmethod
    def project(x, y, z):
        return ((x - z) * math.cos(math.radians(30)), (x + z) * 0.5 - y)

    def poly(self, points, fill, width=3.0):
        self.polys.append(([self.project(*p) for p in points], fill, width))

    def box(self, x, y, z, w, h, d, colours, width=3.0):
        top, front, right = colours
        self.poly([(x, y + h, z), (x + w, y + h, z), (x + w, y + h, z + d), (x, y + h, z + d)], top, width)
        self.poly([(x, y, z + d), (x + w, y, z + d), (x + w, y + h, z + d), (x, y + h, z + d)], front, width)
        self.poly([(x + w, y, z), (x + w, y, z + d), (x + w, y + h, z + d), (x + w, y + h, z)], right, width)

    def gable(self, x, y, z, w, d, rise, colours, end=None):
        """A gable roof over x .. x+w, z .. z+d with the ridge along x; the front slope and the right gable end."""
        top, front, right = colours
        ridge = (z + d / 2.0)
        self.poly([(x, y, z + d), (x + w, y, z + d), (x + w, y + rise, ridge), (x, y + rise, ridge)], top)
        self.poly([(x + w, y, z), (x + w, y, z + d), (x + w, y + rise, ridge)], end or right)

    def line(self, a, b, colour, width):
        self.polys.append(([self.project(*a), self.project(*b)], colour, -width))

    def svg(self):
        xs = [p[0] for poly, _, _ in self.polys for p in poly]
        ys = [p[1] for poly, _, _ in self.polys for p in poly]
        span = self.span or max(max(xs) - min(xs), max(ys) - min(ys))
        scale = (SIZE - 16) / span
        ox = (SIZE - (max(xs) - min(xs)) * scale) / 2 - min(xs) * scale
        oy = (SIZE - (max(ys) - min(ys)) * scale) / 2 - min(ys) * scale
        parts = []
        for poly, fill, width in self.polys:
            pts = " ".join(f"{px * scale + ox:.1f},{py * scale + oy:.1f}" for px, py in poly)
            if width < 0:
                parts.append(f'<polyline points="{pts}" fill="none" stroke="{fill}" stroke-width="{-width}" stroke-linecap="round"/>')
            else:
                parts.append(f'<polygon points="{pts}" fill="{fill}" stroke="{INK}" stroke-width="{width}" stroke-linejoin="round"/>')
        return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{SIZE}" height="{SIZE}" viewBox="0 0 {SIZE} {SIZE}">'
                + "".join(parts) + "</svg>")


# ---------- blocks ----------
# Matched to the game's own block icons (the Icons_Bricks atlas in resources.assets, 128 x 128 cells, shown through
# IconKeys._Brick, _Block, _Plank ...): flat faces without lines between them, a thick black outline around the
# silhouette only, a light rim along the top face's front edges and the shape centred in the cell.

# The game's colours: top, short end (left), long side (right).
GAME_STONE = ("#b5b6b5", "#4c4d4c", "#656665")
GAME_WOOD = ("#846d42", "#4a3923", "#6b5236")
# The seams between the boards of a slab and the rim along the top face's front edges.
WOOD_SEAM = "#6f5a35"
STONE_RIM = "#c9cac9"
WOOD_RIM = "#957c4d"
# The outline around the silhouette, in pixels of the 128 icon.
OUTLINE = 3.2
# Pixels per block in the game's brick and half brick icons; longer pieces are made smaller to fit the cell, as the
# game does with its plank and log.
GAME_UNIT = 31.0
# The largest the drawn shape may be, outline not included.
FIT_W = 92.0
FIT_H = 84.0


class GameView(Scene):
    """The view of the game's block icons: the width (x) runs to the left and a little up, the length (z) to the right
    and a little up, y up. The visible faces are the top, the short end at z = 0 (left) and the long side at x = 0
    (right). Measured on the game's brick: per block 26 px left, 31 px right, 8 px up for both, 40 px high."""

    def __init__(self, unit=GAME_UNIT, fit=(FIT_W, FIT_H)):
        super().__init__()
        self.unit = unit
        self.fit = fit
        self.rims = []

    @staticmethod
    def project(x, y, z):
        return (z * 1.0 - x * 0.84, -z * 0.26 - x * 0.26 - y * 1.29)

    def brick(self, x, y, z, w, h, d, colours, rim=None):
        top, end, side = colours
        self.poly([(x, y + h, z), (x + w, y + h, z), (x + w, y + h, z + d), (x, y + h, z + d)], top)
        self.poly([(x, y, z), (x + w, y, z), (x + w, y + h, z), (x, y + h, z)], end)
        self.poly([(x, y, z), (x, y, z + d), (x, y + h, z + d), (x, y + h, z)], side)
        if rim:
            # the bevelled front edges of the top face
            self.rims.append(([self.project(x + w, y + h, z), self.project(x, y + h, z), self.project(x, y + h, z + d)], rim))

    def svg(self):
        xs = [p[0] for poly, _, _ in self.polys for p in poly]
        ys = [p[1] for poly, _, _ in self.polys for p in poly]
        width, height = max(xs) - min(xs), max(ys) - min(ys)
        scale = min(self.unit, self.fit[0] / width, self.fit[1] / height)
        ox = SIZE / 2 - (min(xs) + max(xs)) / 2 * scale
        oy = SIZE / 2 - (min(ys) + max(ys)) / 2 * scale

        def points(poly):
            return " ".join(f"{px * scale + ox:.2f},{py * scale + oy:.2f}" for px, py in poly)

        parts = []
        # The outline: every shape stroked thickly in black, then the fills on top hide the inner strokes.
        for poly, fill, width in self.polys:
            if width < 0:
                parts.append(f'<polyline points="{points(poly)}" fill="none" stroke="#000" stroke-width="{-width + 2 * OUTLINE:.2f}" stroke-linecap="round"/>')
            else:
                parts.append(f'<polygon points="{points(poly)}" fill="#000" stroke="#000" stroke-width="{2 * OUTLINE:.2f}" stroke-linejoin="round"/>')
        for poly, fill, width in self.polys:
            if width < 0:
                parts.append(f'<polyline points="{points(poly)}" fill="none" stroke="{fill}" stroke-width="{-width:.2f}"/>')
            else:
                # a thin stroke in the face's own colour closes the seams between faces
                parts.append(f'<polygon points="{points(poly)}" fill="{fill}" stroke="{fill}" stroke-width="0.6" stroke-linejoin="round"/>')
        for poly, colour in self.rims:
            parts.append(f'<polyline points="{points(poly)}" fill="none" stroke="{colour}" stroke-width="1.6" stroke-linejoin="round" stroke-opacity="0.8"/>')
        return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{SIZE}" height="{SIZE}" viewBox="0 0 {SIZE} {SIZE}">'
                + "".join(parts) + "</svg>")


def stone(w, d, h=1):
    """One whole stone the size of the block, as the blocks are drawn in the game, in the game's brick colours."""
    s = GameView()
    length, width = max(w, d), min(w, d)
    s.brick(0, 0, 0, width, h, length, GAME_STONE, STONE_RIM)
    return s


def slab(w, d):
    """A half high wooden slab in the game's plank colours, with faint seams between its boards (two per block)."""
    s = GameView()
    length, width = max(w, d), min(w, d)
    h = 0.5
    s.brick(0, 0, 0, width, h, length, GAME_WOOD, WOOD_RIM)
    for i in range(1, 2 * width):
        s.line((i / 2, h, 0.04), (i / 2, h, length - 0.04), WOOD_SEAM, 1.2)
    return s


def ladder():
    """A wooden ladder in the game's plank colours: two rails and four rungs."""
    s = GameView(unit=60.0)
    rail, depth, gap = 0.18, 0.16, 0.6
    s.brick(0, 0, gap + rail, depth, 1.6, rail, GAME_WOOD)
    for i in range(4):
        s.brick(0.03, 0.22 + i * 0.38, rail, depth - 0.06, 0.09, gap, GAME_WOOD)
    s.brick(0, 0, 0, depth, 1.6, rail, GAME_WOOD)
    return s


def sapling():
    """A young tree: a mound of soil, a thin trunk and three tiers of leaves, narrowing to the top."""
    s = Scene(span=2.4)
    s.box(0.45, 0, 0.45, 1.1, 0.18, 1.1, SOIL)
    s.box(0.92, 0.18, 0.92, 0.16, 0.9, 0.16, WOOD_DARK)
    s.box(0.5, 0.75, 0.5, 1.0, 0.35, 1.0, LEAVES)
    s.box(0.62, 1.1, 0.62, 0.76, 0.32, 0.76, LEAVES)
    s.box(0.76, 1.42, 0.76, 0.48, 0.3, 0.48, LEAVES)
    return s


def sapling_area():
    """A marker post with a sign, between two young trees."""
    s = Scene(span=3.2)
    for x, z in ((0.2, 1.6), (1.6, 0.2)):
        s.box(x + 0.4, 0, z + 0.4, 0.12, 0.6, 0.12, WOOD_DARK)
        s.box(x + 0.15, 0.5, z + 0.15, 0.62, 0.3, 0.62, LEAVES)
        s.box(x + 0.27, 0.8, z + 0.27, 0.38, 0.26, 0.38, LEAVES)
    s.box(1.45, 0, 1.45, 0.14, 1.5, 0.14, WOOD_DARK)
    s.box(1.1, 1.05, 1.62, 0.84, 0.42, 0.08, WOOD)
    return s


# ---------- buildings ----------

def warehouse():
    s = Scene()
    s.box(0, 0, 0, 9, 0.3, 6, STONE)
    # the hall: brick below, planks above, the cart doors open, a gable roof
    s.box(0, 0.3, 0, 7, 1.1, 6, BRICK)
    s.box(0, 1.4, 0, 7, 1.9, 6, WOOD)
    s.poly([(2, 0.3, 6.02), (5, 0.3, 6.02), (5, 2.7, 6.02), (2, 2.7, 6.02)], DARK[0])
    s.gable(-0.3, 3.3, -0.3, 7.6, 6.6, 1.9, ROOF, end=WOOD[2])
    # the brick works on the right: a lean-to on posts over a pallet of bricks and a stone heap
    s.box(7.4, 0.3, 3.8, 1.2, 0.8, 1.4, BRICK)
    s.box(7.4, 0.3, 0.8, 1.0, 0.6, 1.2, STONE)
    s.box(8.7, 0.3, 5.6, 0.25, 2.2, 0.25, WOOD_DARK)
    s.poly([(7, 3.2, -0.2), (9.2, 2.5, -0.2), (9.2, 2.5, 6.2), (7, 3.2, 6.2)], ROOF[0])
    return s


def smithy():
    s = Scene()
    s.box(0, 0, 0, 4, 0.3, 3, STONE)
    s.box(0, 0.3, 0, 4, 2, 1, STONE)
    s.box(0, 2.3, 0, 1, 2.2, 1, STONE)
    s.box(0.2, 0.3, 1, 1.2, 0.6, 0.9, BRICK)
    s.box(0.4, 0.9, 1.1, 0.8, 0.25, 0.6, FIRE)
    s.poly([(-0.1, 2.4, 1), (4.2, 2.4, 1), (4.2, 3.2, 3.2), (-0.1, 3.2, 3.2)], ROOF[0])
    s.box(3.8, 0.3, 2.8, 0.2, 2.9, 0.2, WOOD_DARK)
    s.box(2, 0.3, 1.6, 0.8, 0.4, 0.6, IRON)
    s.box(1.8, 0.7, 1.6, 1.2, 0.25, 0.6, IRON)
    return s


def armoury():
    s = Scene()
    s.box(0, 0, 0, 4, 0.3, 3, STONE)
    s.box(0, 0.3, 0, 4, 2.6, 0.15, WOOD)
    s.box(0, 0.3, 0, 0.15, 2.6, 3, WOOD)
    # armour stand with a helmet and a shield on the wall
    s.box(1.6, 0.3, 1.2, 0.15, 1.3, 0.15, WOOD_DARK)
    s.box(1.3, 1.0, 1.05, 0.75, 0.75, 0.45, IRON)
    s.box(1.45, 1.75, 1.1, 0.45, 0.4, 0.35, IRON)
    s.poly([(2.6, 1.4, 0.16), (3.4, 1.4, 0.16), (3.4, 2.1, 0.16), (3.0, 2.4, 0.16), (2.6, 2.1, 0.16)], AWNING_RED[0])
    for x, z in ((3.85, 2.85), (0, 2.85), (3.85, 0)):
        s.box(x, 0.3, z, 0.15, 2.6, 0.15, WOOD_DARK)
    s.gable(-0.2, 2.9, -0.2, 4.4, 3.4, 1.0, ROOF, end=WOOD[2])
    return s


def market():
    s = Scene()
    s.box(0, 0, 0, 4, 0.3, 3, STONE)
    s.box(0.2, 0.3, 0.2, 3.6, 1.3, 0.4, WOOD_DARK)
    for x in (0, 3.8):
        s.box(x, 0.3, 0, 0.2, 2.3, 0.2, WOOD_DARK)
        s.box(x, 0.3, 2.8, 0.2, 2.3, 0.2, WOOD_DARK)
    s.box(0.3, 0.3, 2.3, 3.4, 0.8, 0.3, WOOD)
    s.box(0.5, 1.1, 2.2, 0.5, 0.35, 0.5, WOOD)
    s.box(1.5, 1.1, 2.2, 0.6, 0.25, 0.4, BRICK)
    s.box(2.7, 1.1, 2.25, 0.35, 0.5, 0.35, CRYSTAL)
    # striped awning
    for i in range(8):
        colours = AWNING_RED if i % 2 == 0 else AWNING_WHITE
        x = i * 0.5
        s.poly([(x, 2.6, 2.6), (x + 0.5, 2.6, 2.6), (x + 0.5, 2.1, 3.5), (x, 2.1, 3.5)], colours[0], 2.5)
    s.gable(-0.2, 2.6, -0.2, 4.4, 3.0, 1.2, ROOF, end=WOOD[2])
    return s


def research():
    s = Scene()
    s.box(0, 0, 0, 4, 0.3, 3, STONE)
    s.box(0, 0.3, 0, 0.15, 2.6, 3, WOOD)
    # bookshelf along the back, full of coloured books
    s.box(0.15, 0.3, 0, 3.7, 2.2, 0.15, WOOD_DARK)
    for row in range(3):
        y = 0.4 + row * 0.7
        for i in range(9):
            colours = BOOKS[(i + row * 2) % len(BOOKS)]
            height = 0.45 + ((i * 7 + row * 3) % 4) * 0.05
            s.box(0.3 + i * 0.38, y, 0.15, 0.3, height, 0.35, colours, width=1.0)
        s.box(0.15, y - 0.08, 0.15, 3.7, 0.08, 0.4, WOOD_DARK, width=1.5)
    # reading desk with an open book and the glowing dark crystal
    s.box(1.9, 0.3, 1.4, 0.2, 0.9, 0.2, WOOD_DARK)
    s.box(1.6, 1.2, 1.2, 0.8, 0.1, 0.6, WOOD)
    s.box(1.65, 1.3, 1.25, 0.34, 0.05, 0.5, PAPER, width=1.5)
    s.box(2.01, 1.3, 1.25, 0.34, 0.05, 0.5, PAPER, width=1.5)
    s.box(2.8, 0.3, 1.0, 0.4, 0.8, 0.4, STONE)
    s.poly([(3.0, 1.1, 1.2), (2.85, 1.5, 1.2), (3.0, 1.95, 1.2), (3.15, 1.5, 1.2)], DARK_CRYSTAL[1])
    # a pile of books in the front corner
    for i, colours in enumerate(BOOKS[:3]):
        s.box(3.1 + i * 0.03, 0.3 + i * 0.16, 2.3, 0.6 - i * 0.06, 0.16, 0.45, colours, width=1.5)
    for x, z in ((3.85, 2.85), (0, 2.85), (3.85, 0)):
        s.box(x, 0.3, z, 0.15, 2.6, 0.15, WOOD_DARK)
    # only the back slope of the roof, so the shelves and the desk show
    s.poly([(-0.2, 2.9, -0.2), (4.2, 2.9, -0.2), (4.2, 3.6, 0.9), (-0.2, 3.6, 0.9)], ROOF[0])
    return s


ICONS = {
    "stone_brick_2x2": lambda: stone(2, 2),
    "stone_brick_2x4": lambda: stone(4, 2),
    "stone_brick_1x3": lambda: stone(3, 1),
    "stone_brick_1x4": lambda: stone(4, 1),
    "stone_brick_tall": lambda: stone(1, 1, 2),
    "wood_slab_1x1": lambda: slab(1, 1),
    "wood_slab_2x1": lambda: slab(2, 1),
    "wood_slab_2x2": lambda: slab(2, 2),
    "wood_slab_2x4": lambda: slab(4, 2),
    "wood_slab_1x3": lambda: slab(3, 1),
    "wood_slab_1x5": lambda: slab(5, 1),
    "wood_slab_2x3": lambda: slab(3, 2),
    "wood_ladder": ladder,
    "sapling": sapling,
    "sapling_area": sapling_area,
    "warehouse": warehouse,
    "smithy": smithy,
    "armoury": armoury,
    "market": market,
    "research": research,
}


def main():
    names = sys.argv[1:] or list(ICONS)
    os.makedirs(OUT, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        for name in names:
            draw = ICONS[name]
            src = os.path.join(tmp, name + ".svg")
            with open(src, "w") as f:
                f.write(draw().svg())
            subprocess.run(["rsvg-convert", "-w", str(SIZE), "-h", str(SIZE), "-o", os.path.join(OUT, name + ".png"), src], check=True)
    print("wrote", len(names), "icons to", os.path.normpath(OUT))


if __name__ == "__main__":
    main()
