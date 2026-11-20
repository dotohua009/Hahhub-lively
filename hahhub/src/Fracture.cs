using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

internal sealed class Facet {public float tone{get;set;} public float enter{get;set;} public float at{get;set;} public float[][] points{get;set;}
    public RectangleF bounds; public float nx,ny;float ux,uy,near,far;
    public void Prepare(){
        float x=points.Min(p=>p[0]),y=points.Min(p=>p[1]);
        bounds=RectangleF.FromLTRB(x,y,points.Max(p=>p[0]),points.Max(p=>p[1]));
        double direction=Math.Atan2(y+bounds.Height*.5f,x+bounds.Width*.5f);
        ux=(float)Math.Cos(direction);uy=(float)Math.Sin(direction);
        nx=(float)Math.Cos(direction+tone*.65);ny=(float)Math.Sin(direction+tone*.65);
        near=points.Min(p=>p[0]*ux+p[1]*uy);far=points.Max(p=>p[0]*ux+p[1]*uy);
    }
    public PointF[] Visible(float front,PointF origin,float xs,float ys){
        float phase=Math.Min(1,(front-enter)/Math.Max(.001f,at-enter));
        float cut=near+(far-near)*phase;var output=new List<PointF>(points.Length+2);
        var prev=points[points.Length-1];float prevSide=prev[0]*ux+prev[1]*uy-cut;
        foreach(var p in points){
            float side=p[0]*ux+p[1]*uy-cut;
            if(phase<1&&(side<0)!=(prevSide<0)){
                float t=prevSide/(prevSide-side);
                output.Add(new PointF(origin.X+(prev[0]+(p[0]-prev[0])*t)*xs,origin.Y+(prev[1]+(p[1]-prev[1])*t)*ys));
            }
            if(phase>=1||side<=0)output.Add(new PointF(origin.X+p[0]*xs,origin.Y+p[1]*ys));
            prev=p;prevSide=side;
        }
        return output.ToArray();
    }
}
internal sealed class Pattern {public float[][] segments{get;set;} public Facet[] facets{get;set;}
    // Stop at the first crack tip that reaches the calibrated contact circle.
    public float Contact(float radius,int density){
        float first=float.MaxValue;
        foreach(var e in segments){
            if(e[0]>=first)break;if(e[7]>density)continue;
            float x=e[2],y=e[3],dx=e[4]-x,dy=e[5]-y;
            if(e[4]*e[4]+e[5]*e[5]<radius*radius)continue;
            float d=dx*dx+dy*dy,dot=x*dx+y*dy,c=radius*radius-x*x-y*y;
            float part=c<=0||d==0?0:(float)((Math.Sqrt(dot*dot+d*c)-dot)/d);
            first=Math.Min(first,e[0]+(e[1]-e[0])*Math.Min(1,part));
        }
        if(first==float.MaxValue)throw new InvalidDataException("碎裂路径未覆盖轴心接触区");
        return first;
    }
}
internal sealed class GlassData {public float pitch{get;set;} public float axisSize{get;set;} public LayoutKey[] keys{get;set;} public Pattern[] shapes{get;set;} public Pattern[] largeShapes{get;set;}}
internal sealed class Glass : IKeyEffect,IDisposable
{
    sealed class Hit {
        public LayoutKey key;public Pattern shape;public PointF origin;public float scale,baseReach;public int density;
        public double pressed,released=-1,mend,expires;public bool large;public bool fresh=true;
        public float Reach(double now){
            double age=Math.Max(0,(released<0?now:Math.Min(now,released))-pressed-(fresh?90:0));
            float peak=Math.Min(24,baseReach+.24f*(float)Math.Min(1,age/300)+.00047f*(float)age);
            return released<0||now<=mend?peak:peak*(float)Math.Max(0,(expires-now)/(expires-mend));
        }
    }
    sealed class Joint {
        public Hit from,to;public double at,turn,mend,end=-1;public PointF[] points;public float start,length;
        public float Reach(double now){
            float grown=Math.Min(length,start+(float)Math.Max(0,(end<0?now:Math.Min(now,turn))-at)*.009f);
            return end<0||now<=mend?grown:grown*(float)Math.Max(0,(end-now)/Math.Max(1,end-mend));
        }
        public void Retarget(double now,bool newborn=false){
            var first=from.released<0?to:to.released<0||from.expires<=to.expires?from:to;
            double next=first.released<0?-1:first.expires;if(next==end)return;
            start=Reach(now);at=now;end=next;
            turn=newborn&&end>now?now+Math.Min(140,(end-now)*.35):now;
            mend=Math.Max(turn,first.mend);
        }
    }
    public readonly GlassData data;
    public readonly Dictionary<int,LayoutKey> scans;
    readonly Dictionary<string,Hit> hits=new Dictionary<string,Hit>();
    readonly List<Joint> joints=new List<Joint>();
    readonly Func<double> clock;
    public Tuning tuning=new Tuning();
    public bool Active {get{return hits.Count!=0;}}
    public Glass():this(null){}
    internal Glass(Func<double> testClock){
        var watch=Stopwatch.StartNew();clock=testClock??(()=>watch.Elapsed.TotalMilliseconds);
        var text=File.ReadAllText(Path.Combine(Project.Pack(),"glass-data.js")).Trim();
        const string prefix="const GLASS = ";
        if(!text.StartsWith(prefix)||!text.EndsWith(";"))throw new InvalidDataException("无效的效果包");
        data=Project.Json.Deserialize<GlassData>(text.Substring(prefix.Length,text.Length-prefix.Length-1));
        if(data.keys.Length!=84||data.shapes.Length==0||data.largeShapes==null||data.largeShapes.Length!=data.shapes.Length||data.shapes.Concat(data.largeShapes).Any(p=>p.segments==null))throw new InvalidDataException("碎裂路径与程序版本不一致");
        foreach(var shape in data.shapes.Concat(data.largeShapes))foreach(var facet in shape.facets)facet.Prepare();
        scans=data.keys.Where(k=>k.scan!=0).ToDictionary(k=>k.scan);
    }
    static float Dist(PointF a,PointF b){float x=a.X-b.X,y=a.Y-b.Y;return (float)Math.Sqrt(x*x+y*y);}
    static PointF Lerp(PointF a,PointF b,float t){return new PointF(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t);}
    static float Part(float[] edge,float reach){return Math.Min(1,(reach-edge[0])/Math.Max(.00001f,edge[1]-edge[0]));}
    PointF Toward(Hit hit,PointF target,double now){
        var best=hit.origin;float distance=Dist(best,target),front=hit.Reach(now);
        foreach(var e in hit.shape.segments){
            if(e[0]>=front)break;if(e[7]>hit.density)continue;
            float f=Part(e,front);
            var a=new PointF(hit.origin.X+hit.scale*e[2],hit.origin.Y+hit.scale*e[3]);
            var b=new PointF(a.X+f*hit.scale*(e[4]-e[2]),a.Y+f*hit.scale*(e[5]-e[3]));
            float dx=b.X-a.X,dy=b.Y-a.Y;
            float t=Math.Max(0,Math.Min(1,((target.X-a.X)*dx+(target.Y-a.Y)*dy)/Math.Max(.000001f,dx*dx+dy*dy)));
            var candidate=Lerp(a,b,t);float d=Dist(candidate,target);
            if(d<distance){best=candidate;distance=d;}
        }
        return best;
    }
    void Connect(Hit hit,double now){
        if(hits.Count<2)return;
        var other=hits.Values.Where(x=>x!=hit).OrderBy(x=>Dist(x.origin,hit.origin)).First();
        if(joints.Any(j=>(j.from==other&&j.to==hit)||(j.from==hit&&j.to==other)))return;
        var a=Toward(other,hit.origin,now);var b=Toward(hit,a,now);
        float len=Dist(a,b);if(len<.02f)return;
        int count=Math.Max(3,Math.Min(54,(int)Math.Ceiling(len/.23)));
        var points=new PointF[count+1];points[0]=a;points[count]=b;
        int seed=other.key.scan*17+hit.key.scan*73;
        for(int i=1;i<count;i++){
            float t=i/(float)count,offset=(float)(Math.Sin(Math.PI*t)*(.11*Math.Sin(seed+i*1.87)+.075*Math.Sin(seed*.17+i*.61)))*Math.Min(1,len);
            var p=Lerp(a,b,t);points[i]=new PointF(p.X-(b.Y-a.Y)/len*offset,p.Y+(b.X-a.X)/len*offset);
        }
        var joint=new Joint{from=other,to=hit,at=now,points=points};
        for(int i=1;i<points.Length;i++)joint.length+=Dist(points[i-1],points[i]);
        joint.Retarget(now,true);joints.Add(joint);
    }
    public void Strike(LayoutKey key){
        double now=clock();Expire(now);
        Hit hit;if(hits.TryGetValue(key.code,out hit)){
            if(hit.released<0)return;
            hit.baseReach=hit.Reach(now);hit.pressed=now;hit.released=-1;hit.fresh=false;
            foreach(var j in joints)if(j.from==hit||j.to==hit)j.Retarget(now);
        }else{
            var shapes=tuning.crackSize==0?data.largeShapes:data.shapes;
            hit=new Hit{key=key,pressed=now,large=tuning.crackSize==0,shape=shapes[(key.scan*31+3)%shapes.Length],
                origin=new PointF(key.x*1920/(720*data.pitch),key.y/data.pitch),scale=tuning.spread/100f,density=tuning.density};
            hit.baseReach=hit.shape.Contact(data.axisSize*.25f/(720*data.pitch*hit.scale),hit.density);
            hits.Add(key.code,hit);
        }
        Connect(hit,now);
    }
    public void Release(LayoutKey key){
        Hit hit;if(!hits.TryGetValue(key.code,out hit)||hit.released>=0)return;
        double now=clock();hit.released=now;hit.mend=now+tuning.delay;hit.expires=hit.mend+tuning.heal;
        foreach(var j in joints)if(j.from==hit||j.to==hit)j.Retarget(now);
    }
    void Expire(double now){
        foreach(var key in hits.Where(x=>x.Value.released>=0&&now>=x.Value.expires).Select(x=>x.Key).ToArray())hits.Remove(key);
        joints.RemoveAll(j=>!hits.ContainsKey(j.from.key.code)||!hits.ContainsKey(j.to.key.code));
    }
    public void Clear(){hits.Clear();joints.Clear();}
    public void Dispose(){Clear();}
    Rectangle Area(PointF p,float radius,int w,int h){
        float sx=720*data.pitch*w/1920,sy=data.pitch*h;
        return Rectangle.FromLTRB((int)Math.Floor((p.X-radius)*sx)-3,(int)Math.Floor((p.Y-radius)*sy)-3,
            (int)Math.Ceiling((p.X+radius)*sx)+3,(int)Math.Ceiling((p.Y+radius)*sy)+3);
    }
    public Rectangle Bounds(int w,int h){
        double now=clock();Expire(now);var area=Rectangle.Empty;
        foreach(var hit in hits.Values){
            var r=Area(hit.origin,(.12f+1.72f*hit.Reach(now))*hit.scale,w,h);
            area=area.IsEmpty?r:Rectangle.Union(area,r);
        }
        foreach(var j in joints)foreach(var p in j.points)area=Rectangle.Union(area,Area(p,.025f,w,h));
        return Rectangle.Intersect(new Rectangle(0,0,w,h),area);
    }
    public Rectangle SampleArea(int w,int h){
        var area=Rectangle.Empty;double now=clock();float sx=720*data.pitch*w/1920,sy=data.pitch*h;
        foreach(var hit in hits.Values){
            float front=hit.Reach(now);
            float footprint=0;
            foreach(var facet in hit.shape.facets){
                if(front<=facet.enter)continue;
                var b=facet.bounds;float x=Math.Max(Math.Abs(b.Left),Math.Abs(b.Right))*sx,y=Math.Max(Math.Abs(b.Top),Math.Abs(b.Bottom))*sy;
                footprint=Math.Max(footprint,(float)Math.Sqrt(x*x+y*y)*hit.scale);
            }
            float radius=Math.Min(footprint,FrontRadius(hit,front,sx,sy));
            var r=Rectangle.Ceiling(new RectangleF(hit.origin.X*sx-radius,hit.origin.Y*sy-radius,radius*2,radius*2));
            area=area.IsEmpty?r:Rectangle.Union(area,r);
        }
        if(area.IsEmpty)return area;
        int padding=(int)Math.Ceiling(60*Math.Max(.65f,h/720f))+2;area.Inflate(padding,padding);
        return Rectangle.Intersect(new Rectangle(0,0,w,h),area);
    }
    static float FrontRadius(Hit hit,float front,float sx,float sy){
        float squared=0;
        foreach(var e in hit.shape.segments){
            if(e[0]>=front)break;if(e[7]>hit.density)continue;
            float t=Part(e,front),x=(e[2]+(e[4]-e[2])*t)*sx,y=(e[3]+(e[5]-e[3])*t)*sy;
            squared=Math.Max(squared,Math.Max(x*x+y*y,e[2]*sx*e[2]*sx+e[3]*sy*e[3]*sy));
        }
        return hit.scale*(float)Math.Sqrt(squared);
    }
    static Color Sample(Bitmap image,Rectangle sample,float x,float y){
        return image.GetPixel(Math.Max(0,Math.Min(sample.Width-1,(int)x-sample.X)),Math.Max(0,Math.Min(sample.Height-1,(int)y-sample.Y)));
    }
    static Color Glint(Color c){
        return Color.FromArgb(94+c.R*161/255,94+c.G*161/255,94+c.B*161/255);
    }
    sealed class Ink {
        readonly List<PointF>[] points={new List<PointF>(),new List<PointF>(),new List<PointF>()};
        public void Line(int style,PointF a,PointF b){points[style].Add(a);points[style].Add(b);}
        public void Paint(Graphics g,float opacity,float unit,Color lightColor){
            for(int k=0;k<points.Length;k++){
                var pts=points[k];if(pts.Count<2)continue;
                var kinds=new byte[pts.Count];for(int i=1;i<kinds.Length;i+=2)kinds[i]=1;
                float darkWidth=k==0?2.05f:k==1?1.35f:.85f,lightWidth=k==0?.92f:k==1?.64f:.43f;
                using(var path=new GraphicsPath(pts.ToArray(),kinds))
                using(var dark=new Pen(Color.FromArgb((int)(opacity*(k==2?150:215)),9,19,29),darkWidth*unit))
                using(var light=new Pen(Color.FromArgb((int)(opacity*(k==0?244:k==1?201:150)),lightColor),lightWidth*unit)){
                    g.DrawPath(dark,path);var state=g.Save();
                    g.TranslateTransform(.55f*unit,-.52f*unit);g.DrawPath(light,path);g.Restore(state);
                }
            }
        }
    }
    public void Draw(Graphics g,int w,int h,int dx,int dy){Draw(g,w,h,dx,dy,null,Rectangle.Empty);}
    public void Draw(Graphics g,int w,int h,int dx,int dy,Bitmap wallpaper,Rectangle sample){
        double now=clock();float sx=720*data.pitch*w/1920,sy=data.pitch*h,unit=Math.Max(.65f,h/720f);
        Func<PointF,PointF> screen=p=>new PointF(p.X*sx-dx,p.Y*sy-dy);
        g.SmoothingMode=SmoothingMode.AntiAlias;
        var neutral=Color.FromArgb(238,246,252);
        var region=new Rectangle(0,0,sample.Width,sample.Height);
        using(var attributes=new ImageAttributes()){
        attributes.SetColorMatrix(new ColorMatrix{Matrix33=.47f});
        using(var mirror=wallpaper==null?null:new TextureBrush(wallpaper,region,attributes))
        using(var lens=wallpaper==null?null:new TextureBrush(wallpaper,WrapMode.Clamp,region)){
        foreach(var hit in hits.Values){
            float front=hit.Reach(now);
            var origin=screen(hit.origin);float xscale=sx*hit.scale,yscale=sy*hit.scale;
            float radius=FrontRadius(hit,front,sx,sy);
            var faceState=g.Save();
            using(var guard=new GraphicsPath()){
                if(radius>.01f)guard.AddEllipse(origin.X-radius,origin.Y-radius,2*radius,2*radius);
                g.SetClip(guard,CombineMode.Intersect);
            }
            foreach(var facet in hit.shape.facets){
                if(front<=facet.enter)continue;
                float opacity=Math.Min(1,(front-facet.enter)*14);
                var polygon=facet.Visible(front,origin,xscale,yscale);
                if(polygon.Length<3)continue;
                if(lens!=null){
                    float shift=(7+10*Math.Abs(facet.tone))*unit*opacity;
                    lens.ResetTransform();lens.TranslateTransform(sample.X-dx+facet.nx*shift,sample.Y-dy+facet.ny*shift);
                    g.FillPolygon(lens,polygon);
                    // A tilted shard reflects the opposite side of the impact, over its transmitted image.
                    float nx=facet.nx,ny=facet.ny,u=1-2*nx*nx,v=-2*nx*ny,z=1-2*ny*ny;
                    float ox=sample.X-dx-origin.X,oy=sample.Y-dy-origin.Y,offset=(31+20*Math.Abs(facet.tone))*unit;
                    using(var transform=new Matrix(u,v,v,z,origin.X+u*ox+v*oy+nx*offset,origin.Y+v*ox+z*oy+ny*offset)){
                        mirror.Transform=transform;mirror.WrapMode=WrapMode.Clamp;g.FillPolygon(mirror,polygon);
                    }
                    var light=Glint(Sample(wallpaper,sample,origin.X+dx+nx*offset,origin.Y+dy+ny*offset));
                    var box=new RectangleF(origin.X+facet.bounds.X*xscale,origin.Y+facet.bounds.Y*yscale,
                        Math.Max(1,facet.bounds.Width*xscale),Math.Max(1,facet.bounds.Height*yscale));
                    using(var sheen=new LinearGradientBrush(box,Color.FromArgb((int)(opacity*(78+28*Math.Abs(facet.tone))),light),
                        Color.FromArgb((int)(opacity*24),8,14,23),(float)(Math.Atan2(ny,nx)*180/Math.PI))){
                        g.FillPolygon(sheen,polygon);
                    }
                    continue;
                }
                using(var fill=new SolidBrush(Color.FromArgb((int)((14+23*Math.Abs(facet.tone))*opacity),facet.tone>=0?Color.FromArgb(214,229,241):Color.FromArgb(10,20,33))))
                    g.FillPolygon(fill,polygon);
            }
            g.Restore(faceState);
            var ink=new Ink();
            foreach(var e in hit.shape.segments){
                if(e[0]>=front)break;if(e[7]>hit.density)continue;
                float f=Part(e,front);var a=new PointF(origin.X+e[2]*xscale,origin.Y+e[3]*yscale);
                ink.Line((int)e[6],a,new PointF(a.X+f*(e[4]-e[2])*xscale,a.Y+f*(e[5]-e[3])*yscale));
            }
            var edgeLight=wallpaper==null?neutral:Glint(Sample(wallpaper,sample,origin.X+dx-24*unit,origin.Y+dy-32*unit));
            ink.Paint(g,1,unit*(hit.large?1.24f:1),edgeLight);
        }}}
        foreach(var j in joints){
            var ink=new Ink();float remaining=j.Reach(now);
            for(int i=1;i<j.points.Length&&remaining>0;i++){
                var a=j.points[i-1];var b=j.points[i];float len=Dist(a,b);
                ink.Line(1,screen(a),screen(Lerp(a,b,Math.Min(1,remaining/Math.Max(.00001f,len)))));remaining-=len;
            }
            ink.Paint(g,1,unit,neutral);
        }
    }
}
