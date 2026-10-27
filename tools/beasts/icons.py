"""Icons of the beasts for the off-screen markers (the game's compass): each beast seen from the side, on a
transparent background with a dark outline, 64x64.

  python3 icons.py <out dir>    ->  <name>_icon.png per beast
"""
import sys
import numpy as np
from PIL import Image, ImageFilter
from sdf import render
from beasts import ALL, PATTERNS

BIG = 256
SIZE = 64
KEY = (1.0, 0.0, 1.0)

def icon(name):
    prims = ALL[name]()
    rgb = render(prims, yaw=0.35, pitch=0.15, size=BIG, bg=KEY, pattern=PATTERNS.get(name))
    key = np.array(KEY) * 255
    mask = (np.abs(rgb.astype(int) - key.astype(int)).sum(axis=2) > 6).astype(np.uint8) * 255
    image = Image.fromarray(rgb).convert('RGBA')
    image.putalpha(Image.fromarray(mask))
    # A dark outline round the beast, so it reads on any ground.
    grown = Image.fromarray(mask).filter(ImageFilter.MaxFilter(13))
    outline = Image.new('RGBA', image.size, (20, 16, 10, 0))
    outline.putalpha(grown)
    outline.alpha_composite(image)
    box = outline.getbbox()
    w, h = box[2] - box[0], box[3] - box[1]
    side = max(w, h)
    square = Image.new('RGBA', (side, side), (0, 0, 0, 0))
    square.paste(outline.crop(box), ((side - w) // 2, (side - h) // 2))
    return square.resize((SIZE, SIZE), Image.LANCZOS)

if __name__ == '__main__':
    out = sys.argv[1]
    for name in sys.argv[2:] or list(ALL):
        icon(name).save('%s/%s_icon.png' % (out, name))
        print(name, 'ok')
