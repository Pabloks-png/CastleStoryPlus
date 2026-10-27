import sys, numpy as np
from PIL import Image, ImageDraw
from sdf import render
from beasts import ALL, PATTERNS
size=300
names=list(ALL)
sheet=Image.new('RGB',(size*3,size*len(names)),(28,35,43))
for r,n in enumerate(names):
    prims=ALL[n]()
    for c,yaw in enumerate((-1.2,0.0,2.2)):
        img=render(prims,yaw=yaw,pitch=0.22,size=size,pattern=PATTERNS.get(n))
        sheet.paste(Image.fromarray(img),(c*size,r*size))
    ImageDraw.Draw(sheet).text((8,r*size+6),n,fill=(255,230,180))
sheet.save(sys.argv[1]); print('ok')
