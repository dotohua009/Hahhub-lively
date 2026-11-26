using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

// Geometry and simulation use the same 1920 x 720 calibration space as KeyGeometry.
internal abstract class SkillScene : IKeyEffect {
    readonly EffectBudget budget=new EffectBudget();
    public EffectBudget Budget{get{return budget;}}
    protected sealed class Contact {
        public LayoutKey key;
        public PointF origin, from;
        public double birth, released = -1;
        public int seed;
    }
    protected readonly List<Contact> contacts = new List<Contact>();
    protected readonly GlassData layout;
    protected readonly Func<double> clock;
    protected double now;
    readonly Random seeds;
    protected int NextSeed(){return seeds.Next(1,0x3fffffff);}
    protected SkillScene(GlassData data, Func<double> time) {
        layout=data; seeds=new Random(time==null?Guid.NewGuid().GetHashCode():58217);var timer=Stopwatch.StartNew(); clock=time??(()=>timer.Elapsed.TotalMilliseconds);
    }
    public virtual bool Active { get { return contacts.Count>0; } }
    public virtual void Strike(LayoutKey key){BeginContact(key);}
    protected virtual void Evicted(Contact p){}
    void TrimContacts(int keep){
        while(contacts.Count>keep){
            int i=contacts.FindIndex(c=>c.released>=0);if(i<0)i=0;
            Evicted(contacts[i]);contacts.RemoveAt(i);
        }
    }
    protected bool BeginContact(LayoutKey key) {
        if(key==null)return false;
        var existing=contacts.Find(c=>c.key==key);
        if(existing!=null&&existing.released<0)return false;
        if(!Budget.Start(clock()))return false;
        var origin=Center(key.cap); var from=origin; double t=clock();
        for(int i=contacts.Count-1;i>=0;i--){
            var p=contacts[i];
            if(p.key==key){contacts.RemoveAt(i);continue;}
            if(from==origin && (p.released<0 || t-p.released<350) && Distance(origin,p.origin)<410)from=p.origin;
        }
        TrimContacts(Budget.Keys-1);
        contacts.Add(new Contact{key=key,origin=origin,from=from,birth=t,seed=NextSeed()});return true;
    }
    public virtual void Release(LayoutKey key){foreach(var p in contacts)if(p.key==key && p.released<0)p.released=clock();}
    public virtual void Clear(){contacts.Clear();}
    protected virtual double Tail {get{return 850;}}
    protected virtual void Step(){contacts.RemoveAll(p=>p.released>=0 && now-p.released>=Tail);TrimContacts(Budget.Keys);}
    protected float Fade(Contact p){return p.released<0?1:1-Smooth((now-p.released)/Tail);}
    protected virtual RectangleF Extent(Contact p){return Around(p.origin,100,100);}
    protected virtual RectangleF SceneBounds(){
        RectangleF box=RectangleF.Empty;
        foreach(var p in contacts)box=Union(box,Extent(p));
        return box;
    }
    public Rectangle Bounds(int w,int h){now=clock();Step();return Pixels(SceneBounds(),w,h);}
    protected static Rectangle Pixels(RectangleF r,int w,int h){
        if(r.IsEmpty)return Rectangle.Empty;
        return Rectangle.Intersect(new Rectangle(0,0,w,h),Rectangle.FromLTRB((int)Math.Floor(r.Left*w/1920)-2,(int)Math.Floor(r.Top*h/720)-2,(int)Math.Ceiling(r.Right*w/1920)+2,(int)Math.Ceiling(r.Bottom*h/720)+2));
    }
    public virtual Rectangle SampleArea(int w,int h){return Rectangle.Empty;}
    public void Draw(Graphics g,int w,int h,int dx,int dy,Bitmap image,Rectangle sample){
        if(!Active)return;
        var state=g.Save();
        try{
            g.TranslateTransform(-dx,-dy);g.ScaleTransform(w/1920f,h/720f);g.SmoothingMode=SmoothingMode.AntiAlias;
            Paint(g,w,h,image,sample);
        }finally{g.Restore(state);}
    }
    // The preview includes the portions of calibrated keycaps outside the physical display.
    public Rectangle RenderWorld=new Rectangle(0,0,1920,720);
    protected void BeginLayer(ref ParticleSurface layer,float scale){
        if(layer==null||layer.World!=RenderWorld){
            if(layer!=null)layer.Dispose();
            layer=new ParticleSurface(scale,RenderWorld);
        }
        layer.Begin();
    }
    protected abstract void Paint(Graphics g,int w,int h,Bitmap image,Rectangle sample);
    protected static float Unit(double v){return (float)Math.Max(0,Math.Min(1,v));}
    protected static float Smooth(double v){float t=Unit(v);return t*t*(3-2*t);}
    protected static float Noise(int seed,int i){
        unchecked{uint v=(uint)(seed*73856093+i*19349663);v^=v>>16;v*=0x7feb352d;v^=v>>15;return (v&65535)/65535f;}
    }
    protected static float Sin(double v){return (float)Math.Sin(v);}
    protected static float Cos(double v){return (float)Math.Cos(v);}
    protected static PointF Center(RectangleF r){return new PointF(r.X+r.Width*.5f,r.Y+r.Height*.5f);}
    protected static float Distance(PointF a,PointF b){float x=a.X-b.X,y=a.Y-b.Y;return (float)Math.Sqrt(x*x+y*y);}
    protected static PointF At(PointF a,float x,float y){return new PointF(a.X+x,a.Y+y);}
    protected static PointF Lerp(PointF a,PointF b,float t){return At(a,(b.X-a.X)*t,(b.Y-a.Y)*t);}
    protected static RectangleF Around(PointF p,float x,float y){return new RectangleF(p.X-x,p.Y-y,2*x,2*y);}
    protected static RectangleF Union(RectangleF a,RectangleF b){return a.IsEmpty?b:b.IsEmpty?a:RectangleF.Union(a,b);}
    protected static Color Ink(int rgb,float opacity){return Color.FromArgb((int)(255*Unit(opacity)),(rgb>>16)&255,(rgb>>8)&255,rgb&255);}
    protected static void Face(Graphics g,int rgb,float opacity,params PointF[] points){using(var b=new SolidBrush(Ink(rgb,opacity)))g.FillPolygon(b,points);}
    protected static void Line(Graphics g,int rgb,float opacity,float width,PointF a,PointF b){using(var pen=new Pen(Ink(rgb,opacity),width))g.DrawLine(pen,a,b);}
    protected static void Dot(Graphics g,int rgb,float opacity,PointF center,float rx,float ry){
        if(rx<=0||ry<=0||opacity<=0)return;
        using(var b=new SolidBrush(Ink(rgb,opacity)))g.FillEllipse(b,center.X-rx,center.Y-ry,2*rx,2*ry);
    }
    protected static int Mix(int a,int b,float t){
        float s=1-t;return ((int)(((a>>16)&255)*s+((b>>16)&255)*t)<<16)|((int)(((a>>8)&255)*s+((b>>8)&255)*t)<<8)|(int)((a&255)*s+(b&255)*t);
    }
}
internal sealed class SkillOptions {
    public bool inkEnhanced, holeEnhanced, rainbow;
    public WeatherTuning fog=new WeatherTuning(),snow=new WeatherTuning();
    public int fuseMs=VoxelFuse.DefaultFuseMs;
    public int tendrilColor=0x9567ae, spiderColor=0x191722;
    public static SkillOptions Read(){
        var file=Path.Combine(Project.Data,"skills.json");
        if(!File.Exists(file))return new SkillOptions();
        var o=Project.Json.Deserialize<SkillOptions>(File.ReadAllText(file))??new SkillOptions();
        o.fog=o.fog??new WeatherTuning();o.fog.Clamp();
        o.snow=o.snow??new WeatherTuning();o.snow.Clamp();
        o.fuseMs=Math.Max(VoxelFuse.MinFuseMs,Math.Min(VoxelFuse.MaxFuseMs,o.fuseMs));
        o.tendrilColor &= 0xffffff;o.spiderColor &= 0xffffff;
        return o;
    }
    public void Save(){Project.Atomic(Path.Combine(Project.Data,"skills.json"),Project.Json.Serialize(this));}
}
