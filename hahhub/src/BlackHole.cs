using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading.Tasks;

internal interface IKeyEffect {
    EffectBudget Budget{get;}
    bool Active {get;}
    void Strike(LayoutKey key);
    void Release(LayoutKey key);
    void Clear();
    Rectangle Bounds(int w,int h);
    Rectangle SampleArea(int w,int h);
    void Draw(Graphics g,int w,int h,int dx,int dy,Bitmap wallpaper,Rectangle sample);
}

internal sealed class BlackHole : IKeyEffect,IDisposable {
    readonly EffectBudget budget=new EffectBudget();
    public EffectBudget Budget{get{return budget;}}
    public const string Id="hahhan-blackhole";
    public const int DefaultRim=0xe4e7f2;
    public int rimColor=DefaultRim;
    const int Resolution=192,Angles=1024;
    public const float DiameterInKeys=3;
    const float Tau=6.2831853f;
    static readonly float[] sine=new float[Angles];
    struct Cell {public int x,y,angle;public float radius,u,v;}
    static readonly Cell[] disc;
    static BlackHole(){
        for(int i=0;i<Angles;i++)sine[i]=(float)Math.Sin(i*Tau/Angles);
        var cells=new List<Cell>();
        for(int y=0;y<Resolution;y++)for(int x=0;x<Resolution;x++){
            float u=(2*x+1f)/Resolution-1,v=(2*y+1f)/Resolution-1,r=(float)Math.Sqrt(u*u+v*v);
            if(r<1)cells.Add(new Cell{x=x,y=y,radius=r,u=u,v=v,angle=(int)Math.Round(Math.Atan2(v,u)*Angles/Tau)&(Angles-1)});
        }
        disc=cells.ToArray();
    }
    sealed class Well {
        public LayoutKey key;public double birth,pressed,released=-1;public float baseOpen=.16f,baseGrowth;
        public readonly float[] edge=new float[Angles];public int phase,spin;public float oval,wobble,twist;
        public void UpdateEdge(double now){
            double age=now-birth;int drift=(int)(age*.115),tilt=phase+(int)(53*Sin((int)(age*.13)+phase));
            float major=.42f+.016f*Sin(drift+phase),minor=major*(oval+.065f*Sin((int)(age*.15)-phase));
            for(int i=0;i<Angles;i++){
                float c=Sin(i-tilt+Angles/4),s=Sin(i-tilt);
                float ellipse=major*minor/(float)Math.Sqrt(minor*minor*c*c+major*major*s*s);
                edge[i]=ellipse+wobble*Sin(3*i+phase+drift)+.005f*Sin(4*i-phase-2*drift);
            }
        }
        public float Growth(double now){
            double end=released<0?now:Math.Min(released,now);
            float t=Clamp((float)(end-pressed-360)/4340);
            float growth=baseGrowth+(1-baseGrowth)*t*t;
            float exit=released<0?0:Clamp((float)(now-released)/560);
            return growth*(1-exit*exit*(3-2*exit));
        }
        public float Open(double now){
            double close=released<0?now:Math.Min(now,Math.Max(released,birth+155));
            float t=Clamp((float)(close-pressed)/240);
            float open=1-(1-baseOpen)*(1-t)*(1-t);
            if(released<0)return open;
            float exit=Clamp((float)(now-Math.Max(released,birth+155))/560);
            return open*(1-exit*exit*(3-2*exit));
        }
    }
    readonly Dictionary<string,Well> wells=new Dictionary<string,Well>();
    readonly Func<double> clock;readonly float pitch;readonly Random shapeSeed;
    Bitmap tile;
    public int power=80;
    public bool enhanced;
    public BlackHole(float keyPitch,Func<double> testClock=null){
        var timer=Stopwatch.StartNew();clock=testClock??(()=>timer.Elapsed.TotalMilliseconds);pitch=keyPitch;shapeSeed=new Random(testClock==null?Guid.NewGuid().GetHashCode():41);
    }
    static float Clamp(float x){return Math.Max(0,Math.Min(1,x));}
    static float Sin(int i){return sine[i&(Angles-1)];}
    public bool Active {get{return wells.Count!=0;}}
    public void Strike(LayoutKey key){
        double now=clock();Well hole;
        if(wells.TryGetValue(key.code,out hole)&&hole.Open(now)>0){
            if(hole.released<0)return;
            hole.baseGrowth=hole.Growth(now);hole.baseOpen=hole.Open(now);hole.pressed=now;hole.released=-1;return;
        }
        if(!Budget.Start(now))return;
        TrimWells(Budget.Keys-1);
        int seed=shapeSeed.Next(1,8191);
        hole=new Well{key=key,birth=now,pressed=now,phase=seed%Angles,spin=seed%2==0?1:-1,oval=.73f+(float)shapeSeed.NextDouble()*.24f,wobble=.007f+(float)shapeSeed.NextDouble()*.024f,twist=.74f+(float)shapeSeed.NextDouble()*.65f};
        wells[key.code]=hole;
    }
    void TrimWells(int keep){
        while(wells.Count>keep){
            var victim=wells.Values.OrderBy(v=>v.released<0).ThenBy(v=>v.pressed).First();
            wells.Remove(victim.key.code);
        }
    }
    public void Release(LayoutKey key){Well v;if(wells.TryGetValue(key.code,out v)&&v.released<0)v.released=clock();}
    public void Clear(){wells.Clear();if(tile!=null){tile.Dispose();tile=null;}}
    public void Dispose(){Clear();}
    sealed class Lens {
        public Well well;public float open,opacity;public RectangleF box;
    }
    readonly List<Lens> lenses=new List<Lens>();
    internal int LensCount{get{return lenses.Count;}}
    bool covered;
    public bool Parked {get{return covered&&wells.Values.Any(v=>v.released<0&&v.Growth(clock())>=1);}}
    bool Adjacent(LayoutKey a,LayoutKey b){
        double x=(a.x-b.x)*1920/(720*pitch),y=(a.y-b.y)/pitch;
        return x*x+y*y<=2.36;
    }
    double planAt;
    void Plan(int w,int h,double now){
        lenses.Clear();covered=false;planAt=now;
        Components(wells.Values.OrderBy(v=>v.birth).ThenBy(v=>v.key.scan).ToList(),w,h,now);
    }
    void Components(List<Well> remaining,int w,int h,double now){
        while(remaining.Count>0&&!covered){
            var group=new List<Well>{remaining[0]};remaining.RemoveAt(0);
            for(int i=0;i<group.Count;i++)for(int j=remaining.Count-1;j>=0;j--)
                if(Adjacent(group[i].key,remaining[j].key)){group.Add(remaining[j]);remaining.RemoveAt(j);}
            Group(group,w,h,now);
        }
    }
    void Group(List<Well> group,int w,int h,double now){
        float total=0,x=0,y=0,growth=0,open=0;
        foreach(var v in group){
            float o=v.Open(now),mass=o*o;x+=v.key.x*mass;y+=v.key.y*mass;total+=mass;
            growth=Math.Max(growth,v.Growth(now));open=Math.Max(open,o);
        }
        if(total<=0)return;if(growth>=1){covered=true;return;}
        x=x*w/total;y=y*h/total;
        float unitX=DiameterInKeys*pitch*720*w/1920,unitY=DiameterInKeys*pitch*h;
        double rx=2*Math.Max(Math.Abs(x),Math.Abs(w-x))/unitX,ry=2*Math.Max(Math.Abs(y),Math.Abs(h-y))/unitY;
        float merged=Math.Max(1,(float)Math.Sqrt(total)),full=(float)Math.Sqrt(rx*rx+ry*ry)/.26f;
        float scale=merged+(full-merged)*growth,sw=unitX*scale,sh=unitY*scale;
        var box=new RectangleF(x-sw/2,y-sh/2,sw,sh);
        group.Sort((a,b)=>{int c=a.birth.CompareTo(b.birth);return c!=0?c:a.key.scan.CompareTo(b.key.scan);});
        var first=group[0];var newest=group[group.Count-1];float join=Clamp((float)(now-newest.birth)/360);
        if(group.Count==1||join>=1){lenses.Add(new Lens{well=first,open=open,opacity=1,box=box});return;}
        // Preserve previously fused groups as the next key moves into them.
        int start=lenses.Count;group.RemoveAt(group.Count-1);
        Components(group,w,h,now);Group(new List<Well>{newest},w,h,now);join=join*join*(3-2*join);
        for(int i=start;i<lenses.Count;i++){
            var v=lenses[i];var b=v.box;
            v.box=new RectangleF(b.X+(box.X-b.X)*join,b.Y+(box.Y-b.Y)*join,b.Width+(box.Width-b.Width)*join,b.Height+(box.Height-b.Height)*join);
            if(v.well!=first)v.opacity*=1-join;else v.open+=(open-v.open)*join;
        }
    }
    public Rectangle Bounds(int w,int h){
        double now=clock();TrimWells(Budget.Keys);foreach(var k in wells.Where(p=>p.Value.Open(now)<=0).Select(p=>p.Key).ToArray())wells.Remove(k);
        Plan(w,h,now);return Area(w,h);
    }
    Rectangle Area(int w,int h){
        var screen=new Rectangle(0,0,w,h);if(covered)return screen;
        Rectangle result=Rectangle.Empty;
        foreach(var lens in lenses){
            var b=lens.box;var r=Rectangle.FromLTRB((int)Math.Floor(b.Left),(int)Math.Floor(b.Top),(int)Math.Ceiling(b.Right),(int)Math.Ceiling(b.Bottom));
            result=result.IsEmpty?r:Rectangle.Union(result,r);
        }
        return Rectangle.Intersect(result,screen);
    }
    public Rectangle SampleArea(int w,int h){return covered?Rectangle.Empty:Area(w,h);}
    static uint Blend(uint a,uint b,int weight){
        uint t=(uint)weight,s=256-t;
        return ((((a&0xff00ff)*s+(b&0xff00ff)*t)>>8)&0xff00ff)|((((a&0xff00)*s+(b&0xff00)*t)>>8)&0xff00);
    }
    struct Source {public IntPtr pixels;public int stride;public Rectangle area;}
    static unsafe uint Sample(ref Source src,float x,float y){
        var region=src.area;
        x=Math.Max(0,Math.Min(region.Width-1,x-region.X));y=Math.Max(0,Math.Min(region.Height-1,y-region.Y));
        int ix=(int)x,iy=(int)y,nx=Math.Min(ix+1,region.Width-1),ny=Math.Min(iy+1,region.Height-1);
        var top=(uint*)((byte*)src.pixels+iy*src.stride);var bottom=(uint*)((byte*)src.pixels+ny*src.stride);
        int fx=(int)((x-ix)*256);
        return Blend(Blend(top[ix],top[nx],fx),Blend(bottom[ix],bottom[nx],fx),(int)((y-iy)*256));
    }
    static unsafe uint Bent(ref Source source,RectangleF box,int angle,float radius){
        return Sample(ref source,box.X+box.Width*(.5f+.5f*radius*Sin(angle+Angles/4)),box.Y+box.Height*(.5f+.5f*radius*Sin(angle)));
    }
    unsafe void Render(Well hole,double now,RectangleF box,float open,float opacity,BitmapData src,Rectangle sample){
        float gain=power*.01f,elapsed=(float)((now-hole.birth)*(.0008+.0005*gain));
        float phase=elapsed-(float)Math.Floor(elapsed),other=(phase+.5f)%1,weight=1-Math.Abs(2*phase-1);
        int wave=(int)((now-hole.birth)*.22)%Angles;
        hole.UpdateEdge(now);
        var data=tile.LockBits(new Rectangle(0,0,Resolution,Resolution),ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
        try{
            var source=new Source{area=sample,pixels=src==null?IntPtr.Zero:src.Scan0,stride=src==null?0:src.Stride};
            var dest=(byte*)data.Scan0;int stride=data.Stride;
            foreach(var cell in disc){
                float r=cell.radius,core=open*hole.edge[cell.angle],d=r-core;
                float inward=Clamp((1-r)/(1-core)),pull=inward*(2-inward)*open;
                int alpha=(int)(255*Clamp((1-r)*15)*Clamp(open*5)*opacity);
                var target=(uint*)(dest+cell.y*stride)+cell.x;
                if(d<-.024f&&alpha==255){*target=0xff02030a;continue;}
                float lip=1-Clamp(Math.Abs(d-.018f)/.047f);
                float arc=.56f+.30f*Sin(cell.angle*2+hole.phase-wave)+.14f*Sin(cell.angle*5+wave);
                uint color=0x100f19;
                if(src!=null){
                    // Reverse-map the outer texture into a tightening spiral. Crossfade two streams at their wraps.
                    float bend=pull*(.36f+.64f*gain)*(.91f+.09f*Sin(cell.angle*3+hole.phase));
                    int turn=(int)(hole.twist*bend*(.18f+.68f*inward)*hole.spin*Angles);
                    float radial=(1-r)*bend;
                    uint a=Bent(ref source,box,cell.angle+(int)(turn*(.40f+phase)),r+radial*(.22f+.78f*phase));
                    uint b=Bent(ref source,box,cell.angle+(int)(turn*(.40f+other)),r+radial*(.22f+.78f*other));
                    color=Blend(b,a,(int)(weight*256));
                }else alpha=(int)(alpha*Math.Max(Clamp((.13f-d)*7),lip*.85f));
                float depth=Clamp((.10f-d)*8)*open;
                color=Blend(color,0x060712,(int)(depth*166));
                // Compressed bands of falling material catch light along the direction of shear.
                float spiral=Math.Max(0,Sin(cell.angle*2+hole.phase+(int)(d*(1700+900*inward))*hole.spin+hole.spin*wave*2));
                spiral*=spiral;spiral*=spiral;spiral*=spiral;
                float fold=spiral*pull*Clamp(d*16);
                uint reflection=Blend(color,0xf5efea,168);
                color=Blend(color,reflection,(int)(fold*150));
                float halo=1-Clamp(Math.Abs(d-.018f)/.088f);
                color=Blend(color,(uint)rimColor,(int)(halo*halo*arc*open*76));
                color=Blend(color,Blend((uint)rimColor,0xffffff,42),(int)(lip*lip*arc*open*225));
                if(enhanced){
                    // A tilted accretion band crosses the near rim and arcs above the far rim.
                    float x=cell.u,y=cell.v+x*.25f,orb=open*.64f;
                    float near=Clamp(1-Math.Abs(y-.045f*open)/(.024f+.044f*open))*Clamp((orb-Math.Abs(x))*16)*Clamp((d+.035f)*20);
                    float curve=Clamp(1-Math.Abs(r-core-.055f)/.056f)*Clamp((-y+.07f)*7);
                    float streak=.48f+.52f*Sin(cell.angle*5+(int)(d*2400)-wave*2);
                    float glow=Clamp((near+curve*.89f)*(.64f+.36f*streak))*open;
                    color=Blend(color,Blend(0xffb369,(uint)rimColor,88),(int)(glow*243));
                    if(src==null)alpha=Math.Max(alpha,(int)(glow*opacity*235));
                }
                color=Blend(color,0x02030a,(int)(Clamp(-d*46)*256));
                uint pixel=Blend(0,color,alpha==255?256:alpha);
                *target=((uint)alpha<<24)|pixel;
            }
        }finally{tile.UnlockBits(data);}
    }
    static uint MixPixel(uint a,uint b,int weight){
        if(a==b)return a;
        uint alpha=((a>>24)*(uint)(256-weight)+(b>>24)*(uint)weight)>>8;
        return (alpha<<24)|Blend(a,b,weight);
    }
    int[] columns;
    readonly uint[][] filteredRows={new uint[Resolution],new uint[Resolution],new uint[Resolution],new uint[Resolution]};
    unsafe void Composite(Surface.Dib dest,RectangleF box,int w,int h){
        int left=Math.Max(0,(int)Math.Ceiling(box.Left-.5f)),top=Math.Max(0,(int)Math.Ceiling(box.Top-.5f));
        int right=Math.Min(w,(int)Math.Ceiling(box.Right-.5f)),bottom=Math.Min(h,(int)Math.Ceiling(box.Bottom-.5f));
        if(left>=right||top>=bottom)return;
        if(columns==null||columns.Length<w)columns=new int[w];
        float step=Resolution/box.Width;
        for(int x=left;x<right;x++)columns[x]=Math.Max(0,Math.Min((Resolution-1)*256-1,(int)(((x+.5f-box.X)*step-.5f)*256)));
        var src=tile.LockBits(new Rectangle(0,0,Resolution,Resolution),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
        try{
            var pixels=(byte*)src.Scan0;int stride=src.Stride;
            int bands=(right-left)*(long)(bottom-top)>=220000?Math.Min(filteredRows.Length,Environment.ProcessorCount):1;
            Action<int> paintBand=delegate(int band){
              var row=filteredRows[band];
              int y0=top+(bottom-top)*band/bands,y1=top+(bottom-top)*(band+1)/bands;
              for(int y=y0;y<y1;y++){
                int v=Math.Max(0,Math.Min((Resolution-1)*256-1,(int)(((y+.5f-box.Y)*Resolution/box.Height-.5f)*256)));
                var a=(uint*)(pixels+(v>>8)*stride);var b=(uint*)((byte*)a+stride);var output=(uint*)dest.pixels+y*w;
                for(int i=0;i<Resolution;i++)row[i]=MixPixel(a[i],b[i],v&255);
                for(int x=left;x<right;x++){
                    int u=columns[x],i=u>>8;uint pix=MixPixel(row[i],row[i+1],u&255);
                    uint alpha=pix>>24;
                    if(alpha==255||output[x]==0)output[x]=pix;
                    else if(alpha!=0){uint under=output[x],inv=255-alpha;
                        output[x]=pix+Blend(0,under,(int)inv)+((((under>>24)*inv+127)/255)<<24);}
                }
              }
            };
            if(bands==1)paintBand(0);else Parallel.For(0,bands,paintBand);
        }finally{tile.UnlockBits(src);}
    }
    public void DrawDesktop(Surface.Dib dest,int w,int h,Bitmap wallpaper,Rectangle sample){
        DrawCore(null,dest,w,h,0,0,wallpaper,sample);
    }
    public void Draw(Graphics g,int w,int h,int dx,int dy,Bitmap wallpaper,Rectangle sample){
        DrawCore(g,null,w,h,dx,dy,wallpaper,sample);
    }
    void DrawCore(Graphics g,Surface.Dib dest,int w,int h,int dx,int dy,Bitmap wallpaper,Rectangle sample){
        if(wells.Count==0)return;
        double now=dest==null?clock():planAt;if(dest==null)Plan(w,h,now);
        if(covered){
            if(dest!=null){unsafe{uint* pixel=(uint*)dest.pixels;for(int i=0;i<w*h;i++)pixel[i]=0xff02030a;}}
            else using(var dark=new SolidBrush(Color.FromArgb(2,3,10)))g.FillRectangle(dark,-dx,-dy,w,h);
            return;
        }
        if(tile==null)tile=new Bitmap(Resolution,Resolution,PixelFormat.Format32bppPArgb);
        BitmapData src=null;var state=g==null?null:g.Save();
        try{
            if(wallpaper!=null&&!sample.IsEmpty)src=wallpaper.LockBits(new Rectangle(0,0,sample.Width,sample.Height),ImageLockMode.ReadOnly,wallpaper.PixelFormat==PixelFormat.Format32bppRgb?PixelFormat.Format32bppRgb:PixelFormat.Format32bppArgb);
            if(g!=null){g.InterpolationMode=InterpolationMode.Bilinear;g.PixelOffsetMode=PixelOffsetMode.Half;}
            foreach(var lens in lenses){
                var box=lens.box;Render(lens.well,now,box,lens.open,lens.opacity,src,sample);
                if(dest!=null){Composite(dest,box,w,h);continue;}
                var visible=RectangleF.Intersect(box,new RectangleF(0,0,w,h));if(visible.IsEmpty)continue;
                var crop=new RectangleF((visible.X-box.X)*Resolution/box.Width,(visible.Y-box.Y)*Resolution/box.Height,visible.Width*Resolution/box.Width,visible.Height*Resolution/box.Height);
                visible.Offset(-dx,-dy);g.DrawImage(tile,visible,crop,GraphicsUnit.Pixel);
            }
        }finally{if(src!=null)wallpaper.UnlockBits(src);if(state!=null)g.Restore(state);}
    }
}
