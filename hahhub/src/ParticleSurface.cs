using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// Batch many premultiplied sprites into one partial overlay. All coordinates passed by effects are calibrated pixels.
internal sealed class ParticleSprite {
    public readonly int w,h;public readonly uint[] data;
    public ParticleSprite(int width,int height){w=width;h=height;data=new uint[w*h];}
    public ParticleSprite(Bitmap bmp,Rectangle src){
        w=src.Width;h=src.Height;data=new uint[w*h];
        var image=bmp.LockBits(src,ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
        try{unsafe{fixed(uint* dest=data){for(int y=0;y<h;y++)Buffer.MemoryCopy((byte*)image.Scan0+y*image.Stride,dest+y*w,w*4,w*4);}}}
        finally{bmp.UnlockBits(image);}
    }
}
internal sealed class ParticleSurface : IDisposable {
    readonly float scale;readonly int w,h;readonly uint[] data;
    GCHandle pin;Bitmap image;Rectangle area;
    public readonly Rectangle World;
    public ParticleSurface(float scale,Rectangle world){
        World=world;
        this.scale=scale;w=(int)Math.Ceiling(world.Width*scale);h=(int)Math.Ceiling(world.Height*scale);data=new uint[w*h];
        pin=GCHandle.Alloc(data,GCHandleType.Pinned);image=new Bitmap(w,h,w*4,PixelFormat.Format32bppPArgb,pin.AddrOfPinnedObject());
    }
    public void Begin(){Array.Clear(data,0,data.Length);area=Rectangle.Empty;}
    void Touch(int x,int y,int r,int b){
        var box=Rectangle.FromLTRB(x,y,r,b);area=area.IsEmpty?box:Rectangle.Union(area,box);
    }
    static uint Dim(uint c,uint k){return ((((c&0xff00ff)*k>>8)&0xff00ff)|((((c>>8)&0xff00ff)*k)&0xff00ff00));}
    static uint Over(uint src,uint dst){uint alpha=src>>24;return alpha==255||dst==0?src:src+Dim(dst,256-alpha);}
    public unsafe void Stamp(ParticleSprite sprite,float cx,float cy,float width,float height,float angle=0,int opacity=255){
        width*=scale;height*=scale;cx=(cx-World.X)*scale;cy=(cy-World.Y)*scale;if(width<.6f||height<.6f||opacity<=0)return;
        float c=(float)Math.Cos(angle),s=(float)Math.Sin(angle),hx=(Math.Abs(width*c)+Math.Abs(height*s))*.5f,hy=(Math.Abs(width*s)+Math.Abs(height*c))*.5f;
        int left=Math.Max(0,(int)Math.Floor(cx-hx)),right=Math.Min(w,(int)Math.Ceiling(cx+hx)),top=Math.Max(0,(int)Math.Floor(cy-hy)),bottom=Math.Min(h,(int)Math.Ceiling(cy+hy));
        if(left>=right||top>=bottom)return;Touch(left,top,right,bottom);
        float sx=sprite.w/width*65536,sy=sprite.h/height*65536;
        int du=(int)(c*sx),dv=(int)(-s*sy),uy=(int)(s*sx),vy=(int)(c*sy);
        int u0=(int)((sprite.w*.5f)*65536+(left+.5f-cx)*du+(top+.5f-cy)*uy);
        int v0=(int)((sprite.h*.5f)*65536+(left+.5f-cx)*dv+(top+.5f-cy)*vy);
        fixed(uint* source=sprite.data, dest=data){
            int sw=sprite.w,sh=sprite.h;
            for(int y=top;y<bottom;y++,u0+=uy,v0+=vy){
                uint* row=dest+y*w;int u=u0,v=v0;
                for(int x=left;x<right;x++,u+=du,v+=dv){
                    int ix=u>>16,iy=v>>16;if((uint)ix>=(uint)sw||(uint)iy>=(uint)sh)continue;
                    uint pix=source[iy*sw+ix];if((pix>>24)==0)continue;
                    if(opacity<255)pix=Dim(pix,(uint)(opacity+1));
                    row[x]=Over(pix,row[x]);
                }
            }
        }
    }
    // A subpixel ember is distributed across four pixels so thin trails remain continuous.
    void Spot(float x,float y,uint color){
        int ix=(int)Math.Floor(x),iy=(int)Math.Floor(y);
        if(ix<0||ix+1>=w||iy<0||iy+1>=h)return;
        int ax=(int)((x-ix)*255),ay=(int)((y-iy)*255),i=iy*w+ix;
        Accumulate(i,color,(256-ax)*(256-ay));Accumulate(i+1,color,ax*(256-ay));
        Accumulate(i+w,color,(256-ax)*ay);Accumulate(i+w+1,color,ax*ay);
    }
    void Accumulate(int i,uint c,int weight){
        if(weight>255)data[i]=Over(Dim(c,(uint)(weight>>8)),data[i]);
    }
    public void Stroke(PointF a,PointF b,int rgb,float opacity,float width){
        if(opacity<=0)return;
        float x=(a.X-World.X)*scale,y=(a.Y-World.Y)*scale,dx=(b.X-a.X)*scale,dy=(b.Y-a.Y)*scale;
        int n=Math.Max(1,(int)Math.Ceiling(Math.Max(Math.Abs(dx),Math.Abs(dy))));
        uint color=Dim((uint)rgb|0xff000000,(uint)(Math.Min(1,opacity*Math.Max(.7f,width*scale))*255+1));
        int left=Math.Max(0,(int)Math.Floor(Math.Min(x,x+dx))-1),top=Math.Max(0,(int)Math.Floor(Math.Min(y,y+dy))-1);
        int right=Math.Min(w,(int)Math.Ceiling(Math.Max(x,x+dx))+2),bottom=Math.Min(h,(int)Math.Ceiling(Math.Max(y,y+dy))+2);
        if(left>=right||top>=bottom)return;Touch(left,top,right,bottom);
        dx/=n;dy/=n;for(int i=0;i<=n;i++,x+=dx,y+=dy)Spot(x,y,color);
    }
    public void Present(Graphics g){
        if(area.IsEmpty)return;
        var r=area;r.Inflate(1,1);r.Intersect(new Rectangle(0,0,w,h));
        var save=g.Save();
        g.SmoothingMode=SmoothingMode.None;g.PixelOffsetMode=PixelOffsetMode.Half;g.InterpolationMode=InterpolationMode.NearestNeighbor;
        g.DrawImage(image,new RectangleF(World.X+r.X/scale,World.Y+r.Y/scale,r.Width/scale,r.Height/scale),r,GraphicsUnit.Pixel);
        g.Restore(save);
    }
    public void Dispose(){if(image!=null){image.Dispose();image=null;}if(pin.IsAllocated)pin.Free();}
}
