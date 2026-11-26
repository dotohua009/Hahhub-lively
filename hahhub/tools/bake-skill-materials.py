"""Bake original orthographic ice prisms and shaded volume sprites for the native overlay."""
from pathlib import Path
import cv2
import numpy as np
from scipy.spatial import ConvexHull
from scipy.ndimage import gaussian_filter, map_coordinates

N = 192
Y, X = np.mgrid[:N, :N].astype(float)
rng = np.random.default_rng(7291)
def noise(scale):
    grid = rng.random((scale, scale), dtype=np.float32)
    return cv2.resize(grid, (N, N), interpolation=cv2.INTER_CUBIC)

def cluster(kind):
    rgba = np.zeros((N, N, 4))
    depth = np.full((N, N), -1000.)
    rock = kind >= 8
    count = 23 if rock else 10
    for i in range(count):
        a = rng.uniform(0, np.pi*2)
        distance = rng.uniform(1, 53 if rock else 23)
        root = np.array([95+distance*np.cos(a), (105 if rock else 153)+distance*np.sin(a), 0.])
        radius = rng.uniform(13, 28) if rock else rng.uniform(5, 13)
        length = rng.uniform(19, 40) if rock else rng.uniform(43, 135)
        az = rng.uniform(-1.4, 1.4) if not rock else a
        direction = np.array([np.sin(az)*.69, -np.cos(az)*.65, rng.uniform(.38, .91)])
        direction /= np.linalg.norm(direction)
        cross = np.cross(direction, [1., 0., 0.]); cross /= np.linalg.norm(cross)
        perp = np.cross(direction, cross)
        n = rng.integers(5, 8); spin=rng.uniform(0, 6)
        angles=np.arange(n)*2*np.pi/n+spin
        sides=np.cos(angles)[:,None]*cross+np.sin(angles)[:,None]*perp
        bottom=root+sides*radius*.78
        crown=root+direction*length*rng.uniform(.55,.76)+sides*radius*rng.uniform(.8,1.25)
        tip=root+direction*length+cross*radius*.4
        vertices=np.vstack([bottom,crown,tip])
        hull=ConvexHull(vertices)
        low=np.full(X.shape,-1000.); top=np.full(X.shape,1000.)
        normal=np.zeros((N,N,3)); good=np.ones(X.shape,bool)
        for nx,ny,nz,k in hull.equations:
            side=nx*X+ny*Y+k
            if abs(nz)<1e-7:
                good &= side<=0
            elif nz>0:
                plane=-side/nz; take=plane<top
                normal[take]=[nx,ny,nz];top=np.minimum(top,plane)
            else:
                low=np.maximum(low,-side/nz)
        mask=good & (top>low) & (top>depth)
        if not mask.any():continue
        nx,ny,nz=normal.transpose(2,0,1);thick=np.maximum(0,top-low)
        key=np.clip(-nx*.47-ny*.67+nz*.56,0,1)
        rim=(1-np.clip(nz,0,1))**2
        env=.5+.5*np.sin(4*nx-3.7*ny+2*nz)
        shine=np.clip((key-.69)*4,0,1)**2
        vein=np.clip(1-abs(np.sin((X*.086+Y*.037+top*.075)+2*noise(4)))*12,0,1)
        band=.5+.5*np.sin(top*.6+X*.07+Y*.12+noise(6)*1.5)
        grain=noise(50); needles=(np.maximum(0,grain-.62)*3)**2
        f=.10+.42*key+.21*env+.13*rim+.065*band+.09*grain
        base=np.stack([12+144*f**1.8,46+172*f,78+159*f],-1)
        base+=((shine*.72+vein*.25+needles*.22)*100)[...,None]
        # Reflect bright studio strips along the sharp edges of the crystal.
        edges=cv2.morphologyEx(np.float32(nz),cv2.MORPH_GRADIENT,np.ones((3,3),np.uint8))
        base+=np.clip(edges*105,0,74)[...,None]
        base[:,:,0]+=np.minimum(1,thick/18)*10
        rgba[mask,:3]=np.clip(base[mask],0,255)
        rgba[mask,3]=np.minimum(250,168+thick[mask]*2+shine[mask]*40)
        depth[mask]=top[mask]
    return rgba

def plume(kind):
    n=52
    field=rng.random((n,n,n),dtype=np.float32)
    field=gaussian_filter(field,1.4,mode='wrap');field=(field-field.mean())/(field.std()+1e-6)
    x=(X-N/2)/(N*.43);y=(Y-N/2)/(N*.43)
    total=np.zeros_like(x);rgb=np.zeros((N,N,3))
    for z in np.linspace(1.15,-1.15,35):
        coords=np.array([np.broadcast_to(z,x.shape),y,x])
        lump=map_coordinates(field,coords*6+20,order=1,mode='wrap')
        detail=map_coordinates(field,coords*16+13,order=1,mode='wrap')
        density=np.clip((1-np.sqrt(x*x+y*y+z*z)+lump*.16+detail*.045)*3,0,1)
        density*=np.clip((1.22-np.sqrt(x*x+y*y))*5,0,1)
        alpha=density*.20
        light=np.clip(.42-.27*x-.42*y+.29*z+.15*lump+.05*detail,0,1)
        if kind<4:
            heat=np.clip(density*.56+light*.51+.12*detail,0,1)
            col=np.stack([89+166*np.minimum(1,heat*1.8),16+231*heat**1.65,8+175*heat**3.9],-1)
        else:
            col=np.stack([26+95*light,27+80*light,33+73*light],-1)
        weight=(1-total)*alpha
        rgb+=weight[...,None]*col;total+=weight
    return np.dstack([rgb/np.maximum(.001,total[...,None]),total*255])

atlas=np.zeros((4*N,6*N,4),np.uint8)
for i in range(24):
    img=cluster(i) if i<16 else plume(i-16)
    atlas[(i//6)*N:(i//6+1)*N,(i%6)*N:(i%6+1)*N]=np.clip(img,0,255).astype('uint8')
path=Path(__file__).resolve().parents[1]/'effects'/'hahhub-materials'/'skills.png'
path.parent.mkdir(parents=True,exist_ok=True)
from PIL import Image
Image.fromarray(atlas).save(path,optimize=True)
