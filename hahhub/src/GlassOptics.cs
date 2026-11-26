using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// Render each shard's transmission, reflection and sheen in the same pass over its covered pixels.
internal sealed unsafe class GlassOptics : IDisposable {
    Surface.Dib layer;
    float[] crossings=new float[0];
    [DllImport("msvcrt.dll",CallingConvention=CallingConvention.Cdecl)]
    static extern IntPtr memset(IntPtr dest,int value,UIntPtr bytes);

    static uint Mix(uint a,uint b,int weight){
        uint t=(uint)weight,s=256-t;
        return ((((a&0xff00ff)*s+(b&0xff00ff)*t)>>8)&0xff00ff)|((((a&0xff00)*s+(b&0xff00)*t)>>8)&0xff00);
    }
    static uint Texel(BitmapData data,int x,int y){
        x=Math.Max(0,Math.Min(data.Width-1,x));y=Math.Max(0,Math.Min(data.Height-1,y));
        return ((uint*)((byte*)data.Scan0+y*data.Stride))[x]&0xffffff;
    }
    public void Draw(Graphics g,Bitmap image,Rectangle sample,PointF center,float xs,float ys,float radius,
                     float front,Pattern shape,float unit,int dx,int dy,int step=1){
        // Anchor the optical transform to the key even when the capture rectangle moves.
        center=new PointF(center.X+dx,center.Y+dy);
        var area=Rectangle.Intersect(Rectangle.FromLTRB((int)Math.Floor(center.X-radius),(int)Math.Floor(center.Y-radius),
            (int)Math.Ceiling(center.X+radius),(int)Math.Ceiling(center.Y+radius)),sample);
        if(area.Width<=0||area.Height<=0)return;
        // Snap the reduced reflection buffer to screen pixels so pressure never shifts the cracks.
        var grid=Rectangle.FromLTRB(area.Left/step,area.Top/step,(area.Right+step-1)/step,(area.Bottom+step-1)/step);
        center=new PointF(center.X/step,center.Y/step);xs/=step;ys/=step;radius/=step;unit/=step;
        area=grid;
        if(layer==null||layer.bitmap.Width<area.Width||layer.bitmap.Height<area.Height){
            var size=new Size(Math.Max(layer==null?0:layer.bitmap.Width,(area.Width+63)/64*64),
                              Math.Max(layer==null?0:layer.bitmap.Height,(area.Height+63)/64*64));
            Dispose();layer=new Surface.Dib(size,PixelFormat.Format32bppPArgb);
        }
        memset(layer.pixels,0,(UIntPtr)((long)area.Height*layer.bitmap.Width*4));
        var source=image.LockBits(new Rectangle(Point.Empty,sample.Size),ImageLockMode.ReadOnly,
            image.PixelFormat==PixelFormat.Format32bppRgb?PixelFormat.Format32bppRgb:PixelFormat.Format32bppArgb);
        try{
            foreach(var facet in shape.facets){
                if(front<=facet.enter)continue;
                var polygon=facet.Visible(front,center,xs,ys);
                if(polygon.Length>=3)Fill(source,sample.Location,area,polygon,facet,center,radius,xs,ys,unit,step);
            }
        }finally{image.UnlockBits(source);}
        var target=new Rectangle(area.X*step-dx,area.Y*step-dy,area.Width*step,area.Height*step);
        var previous=g.InterpolationMode;var offset=g.PixelOffsetMode;
        if(step>1){
            g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.Half;
        }
        g.DrawImage(layer.bitmap,target,0,0,area.Width,area.Height,GraphicsUnit.Pixel);
        if(step>1){g.PixelOffsetMode=offset;g.InterpolationMode=previous;}
    }
    void Fill(BitmapData source,Point crop,Rectangle area,PointF[] polygon,Facet facet,PointF center,float radius,float xs,float ys,float unit,int step){
        if(crossings.Length<polygon.Length)crossings=new float[polygon.Length];
        float nx=facet.nx,ny=facet.ny,xx=1-2*nx*nx,xy=-2*nx*ny,yy=1-2*ny*ny;
        float shift=(7+10*Math.Abs(facet.tone))*unit,offset=(31+20*Math.Abs(facet.tone))*unit;
        uint ambient=Texel(source,(int)((center.X+nx*offset)*step)-crop.X,(int)((center.Y+ny*offset)*step)-crop.Y);
        uint highlight=Mix(0xffffff,ambient,162);
        float top=polygon[0].Y,bottom=top,low=float.MaxValue,hi=float.MinValue;
        foreach(var p in polygon){top=Math.Min(top,p.Y);bottom=Math.Max(bottom,p.Y);}
        foreach(var p in facet.points){
            float projection=p[0]*xs*nx+p[1]*ys*ny;low=Math.Min(low,projection);hi=Math.Max(hi,projection);}
        float inv=1/Math.Max(1,hi-low);
        for(int y=Math.Max(area.Top,(int)Math.Ceiling(top-.5f));y<Math.Min(area.Bottom,(int)Math.Ceiling(bottom-.5f));y++){
            float py=y+.5f,ry=py-center.Y;
            float circle=(float)Math.Sqrt(Math.Max(0,radius*radius-ry*ry));
            int count=0;var prev=polygon[polygon.Length-1];
            foreach(var p in polygon){
                if((prev.Y>py)!=(p.Y>py))crossings[count++]=prev.X+(py-prev.Y)*(p.X-prev.X)/(p.Y-prev.Y);
                prev=p;
            }
            if(count>2)Array.Sort(crossings,0,count);
            else if(count==2&&crossings[0]>crossings[1]){var temp=crossings[0];crossings[0]=crossings[1];crossings[1]=temp;}
            for(int j=0;j+1<count;j+=2){
                float left=Math.Max(area.Left,Math.Max(center.X-circle,crossings[j]));
                float right=Math.Min(area.Right,Math.Min(center.X+circle,crossings[j+1]));
                if(left>=right)continue;
                int first=(int)Math.Floor(left),end=(int)Math.Ceiling(right);
                float rx=first+.5f-center.X;
                double mx=center.X+(double)xx*rx+(double)xy*ry+nx*offset,my=center.Y+(double)xy*rx+(double)yy*ry+ny*offset;
                int sy=(int)((py-ny*shift)*step)-crop.Y;
                var dest=(uint*)layer.pixels+(y-area.Top)*layer.bitmap.Width+first-area.Left;
                for(int x=first;x<end;x++,mx+=xx,my+=xy,dest++){
                    uint transmitted=Texel(source,(int)((x+.5f-nx*shift)*step)-crop.X,sy);
                    uint color=Mix(transmitted,Texel(source,(int)(mx*step)-crop.X,(int)(my*step)-crop.Y),120);
                    float grad=Math.Max(0,Math.Min(1,((x+.5f-center.X)*nx+ry*ny-low)*inv));
                    color=Mix(color,highlight,(int)((1-grad)*(78+28*Math.Abs(facet.tone))));
                    color=Mix(color,0x080e17,(int)(grad*24));
                    int alpha=(int)(255*(Math.Min(right,x+1)-Math.Max(left,x)));
                    if(alpha>=255)*dest=0xff000000|color;
                    else if(alpha>0){
                        uint under=*dest;
                        *dest=((uint)(alpha+((under>>24)*(255-alpha)+127)/255)<<24)|Mix(under,color,alpha);
                    }
                }
            }
        }
    }
    public void Dispose(){if(layer!=null){layer.Dispose();layer=null;}}
}
