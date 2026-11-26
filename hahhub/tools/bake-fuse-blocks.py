"""Render eight original voxel charge materials for the full-keyboard audition."""
from pathlib import Path
from random import Random
import math
from PIL import Image, ImageDraw, ImageFont

S = 256
atlas = Image.new("RGBA", (4*S, 2*S))
for variant in range(8):
    rng = Random(1517 + variant)
    im = Image.new("RGBA", (S,S))
    draw = ImageDraw.Draw(im)
    def top(u,v,z=0):
        return (round(25 + 206*u - 17*v), round(7 + 13*u + 191*v + z))
    def face(points, tint):
        draw.polygon(points, fill=tint)
    def band(y1,y2,tint):
        face([top(0,y1),top(1,y1),top(1,y2),top(0,y2)],tint)
    face([top(0,1),top(1,1),top(1,1,30),top(0,1,30)], '#602e36')
    face([top(1,0),top(1,1),top(1,1,30),top(1,0,27)], '#33232b')
    for i in range(9):
        a,b=i/9,(i+1)/9
        face([top(a,1,1),top(b,1,1),top(b,1,27),top(a,1,27)],rng.choice(['#87393a','#a24740','#78313a']))
        draw.line([top(a,1,3),top(a,1,28)], fill='#42232b',width=3)
    band(0,1,'#642e36')
    for row in range(4):
        for col in range(5):
            u,v=col/5,row/4
            du,dv=.194,.239
            face([top(u,v),top(u+du,v),top(u+du,v+dv),top(u,v+dv)], rng.choice(['#d75948','#d46b4a','#af443e','#c55044']))
            draw.line([top(u+.012,v+.018),top(u+du-.01,v+.018)],fill='#fa986a',width=2)
            draw.line([top(u+du,v+.014),top(u+du,v+dv)],fill='#7c3b38',width=3)
            for j in range(9):
                px,py=top(u+rng.uniform(.02,.18),v+rng.uniform(.02,.21))
                draw.rectangle((px,py,px+rng.randint(1,5),py+rng.randint(1,3)),fill=rng.choice(['#b85143','#e77958','#c46b4e']))
    for y,tint,thick in [(.26,'#593b36',.052),(.30,'#bb916e',.28),(.31,'#ead3a3',.24),(.54,'#f6deb1',.014),(.55,'#866956',.019)]:
        band(y,y+thick,tint)
    letters={'T':['11111','00100','00100','00100','00100','00100','00100'],
             'N':['10001','11001','11001','10101','10011','10011','10001']}
    for n,ch in enumerate('TNT'):
        for j,line in enumerate(letters[ch]):
            for i,bit in enumerate(line):
                if bit=='1':
                    u=.23+n*.19+i*.032;v=.335+j*.026
                    face([top(u,v),top(u+.033,v),top(u+.033,v+.027),top(u,v+.027)],'#523832')
    # A low socket, braided cord, ceramic tip and orange pilot ember sit above the cap.
    cx,cy=top(.58,.20)
    draw.ellipse((cx-11,cy-7,cx+11,cy+8),fill='#562c32',outline='#f0a66e',width=2)
    points=[(cx+i*2,cy-2-int(11*math.sin(i/14*math.pi))+int(i*.26)) for i in range(15)]
    draw.line(points,fill='#402c2c',width=7)
    draw.line([(x,y-2) for x,y in points],fill='#dbb47c',width=4)
    for x,y in points[::3]:
        draw.line([(x-1,y-3),(x+2,y)],fill='#795948',width=2)
    x,y=points[-1]
    draw.ellipse((x-3,y-3,x+3,y+3),fill='#fff1b3')
    draw.line([top(0,0),top(1,0),top(1,1)], fill='#e8936a',width=2)
    atlas.alpha_composite(im,(variant%4*S,variant//4*S))
out=Path(__file__).resolve().parents[1]/'effects'/'hahhub-materials'/'fuse-blocks.png'
atlas.save(out)
