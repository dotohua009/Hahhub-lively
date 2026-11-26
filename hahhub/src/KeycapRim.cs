using System;
using System.Drawing;

// Clockwise arc-length parameterization of the calibrated, rounded keycap outline.
internal struct KeycapRim {
    readonly RectangleF box;
    readonly float radius,across,down,arc;
    public readonly float Length;
    public KeycapRim(RectangleF cap,float inset){
        inset=Math.Min(inset,Math.Min(cap.Width,cap.Height)*.18f);
        box=RectangleF.Inflate(cap,-inset,-inset);
        radius=Math.Min(Math.Max(1,9-inset),Math.Min(box.Width,box.Height)*.5f);
        across=box.Width-2*radius;down=box.Height-2*radius;arc=radius*(float)(Math.PI*.5);
        Length=2*(across+down)+4*arc;
    }
    // Each side spans from one rounded corner midpoint to the next.
    public float SideTurn(int side,float fraction){
        float horizontal=across+arc,vertical=down+arc;
        float start=(side/2)*(horizontal+vertical)+(side%2)*horizontal-arc*.5f;
        return (start+fraction*(side%2==0?horizontal:vertical))/Length;
    }
    public PointF At(float turn,out PointF normal){
        float d=(turn-(float)Math.Floor(turn))*Length;
        float l=box.Left,r=box.Right,t=box.Top,b=box.Bottom,k=radius;
        if(d<=across){normal=new PointF(0,-1);return new PointF(l+k+d,t);}
        d-=across;if(d<arc)return Corner(r-k,t+k,d/k-(float)Math.PI*.5f,out normal);
        d-=arc;if(d<=down){normal=new PointF(1,0);return new PointF(r,t+k+d);}
        d-=down;if(d<arc)return Corner(r-k,b-k,d/k,out normal);
        d-=arc;if(d<=across){normal=new PointF(0,1);return new PointF(r-k-d,b);}
        d-=across;if(d<arc)return Corner(l+k,b-k,d/k+(float)Math.PI*.5f,out normal);
        d-=arc;if(d<=down){normal=new PointF(-1,0);return new PointF(l,b-k-d);}
        return Corner(l+k,t+k,(d-down)/k+(float)Math.PI,out normal);
    }
    PointF Corner(float x,float y,float angle,out PointF normal){
        normal=new PointF((float)Math.Cos(angle),(float)Math.Sin(angle));
        return new PointF(x+radius*normal.X,y+radius*normal.Y);
    }
    public static PointF Closest(RectangleF box,PointF p){
        return new PointF(Math.Max(box.Left,Math.Min(box.Right,p.X)),Math.Max(box.Top,Math.Min(box.Bottom,p.Y)));
    }
}
