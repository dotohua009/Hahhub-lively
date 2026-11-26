using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

internal sealed class InkSplash : IKeyEffect {
    readonly EffectBudget budget=new EffectBudget();
    public EffectBudget Budget{get{return budget;}}
    public const string Id="hahhub-ink";
    public bool enhanced;
    const int Lifetime=1520;
    const double Tau=2*Math.PI;
    static readonly Color[] colors={Color.FromArgb(245,35,85),Color.FromArgb(255,127,18),Color.FromArgb(249,213,20),
        Color.FromArgb(86,223,49),Color.FromArgb(12,209,174),Color.FromArgb(27,157,249),Color.FromArgb(134,76,251),Color.FromArgb(239,42,191)};
    enum Impact { Burst, Flick, Mist, Pool }
    const int PixelsPerUnit=100,SprayPeriod=135;
    sealed class Flight {public float delay,duration,bend,lift;}
    sealed class Drop {
        public float x,y,limitX,limitY,offsetX,offsetY,restitution;
        public Flight flight;public bool escapes;public int half,side;public byte[] mask;
    }
    sealed class Sprite : IDisposable {
        public readonly Surface.Dib dib;public readonly Rectangle box;
        public Sprite(Rectangle box){this.box=box;dib=new Surface.Dib(box.Size,PixelFormat.Format32bppPArgb);}
        public void Dispose(){dib.Dispose();}
    }
    sealed class Splash {
        public bool enhanced;public LayoutKey key;public Color color;public double birth,end,fade;public RectangleF bounds;
        public readonly GraphicsPath body=new GraphicsPath();
        public PathGradientBrush wet;public Drop[] drops;public uint[] palette;public Sprite sprite,bodyStamp;public bool settled;
        public void Dispose(){wet.Dispose();body.Dispose();if(sprite!=null)sprite.Dispose();if(bodyStamp!=null)bodyStamp.Dispose();}
    }
    sealed class Stain {
        public LayoutKey key;public Color color;public Color pigment;public double birth,released,next;public int recovery;public float previous,strength;public Sprite sprite;
    }
    readonly Dictionary<LayoutKey,Stain> stains=new Dictionary<LayoutKey,Stain>();
    readonly List<Splash> splashes=new List<Splash>();
    public InkTuning tuning=new InkTuning();
    readonly Func<double> clock;readonly Random random;readonly float pitchX,pitchY;
    readonly Impact[] order={Impact.Burst,Impact.Flick,Impact.Mist,Impact.Pool};int next=4;
    float hue;
    public InkSplash(GlassData data,Func<double> testClock=null,Random source=null){
        var timer=Stopwatch.StartNew();clock=testClock??(()=>timer.Elapsed.TotalMilliseconds);
        pitchX=data.keys.First(k=>k.code=="KeyS").x-data.keys.First(k=>k.code=="KeyA").x;
        pitchY=data.pitch;
        random=source??new Random();hue=Unit();
    }
    float Unit(){return (float)random.NextDouble();}
    float Range(float low,float high){return low+(high-low)*Unit();}
    float Bell(){return (Unit()+Unit()+Unit()-1.5f)*1.45f;}
    Impact NextImpact(){
        if(next==order.Length){
            var last=order[order.Length-1];
            for(int i=order.Length-1;i>0;i--){int j=random.Next(i+1);var tmp=order[i];order[i]=order[j];order[j]=tmp;}
            if(order[0]==last){var tmp=order[0];order[0]=order[1];order[1]=tmp;}
            next=0;
        }
        return order[next++];
    }
    Color NextColor(){
        if(enhanced){var neon=new[]{0xf04aa1,0x804bd4,0x2dcbcc,0xfd9976};return Color.FromArgb(unchecked((int)0xff000000)|neon[random.Next(neon.Length)]);}
        hue=(hue+Range(.19f,.79f))%1;float v=hue*colors.Length;int slot=(int)v;
        var a=colors[slot];var b=colors[(slot+1)%colors.Length];float t=v-slot;
        return Color.FromArgb((int)(a.R+(b.R-a.R)*t),(int)(a.G+(b.G-a.G)*t),(int)(a.B+(b.B-a.B)*t));
    }
    static float Ease(double age,double duration){
        float t=(float)Math.Max(0,Math.Min(1,age/duration));return 1-(1-t)*(1-t)*(1-t);
    }
    public bool Active{get{return splashes.Count!=0||stains.Count!=0;}}
    public void Strike(LayoutKey key){
        Stain st;if(key==null||(stains.TryGetValue(key,out st)&&st.released<0))return;
        double now=clock();if(!Budget.Start(now))return;
        if(!stains.ContainsKey(key))TrimStains(Budget.Keys-1);
        var color=NextColor();Dye(key,color,now);
        Emit(key,color,now,false);
    }
    void Emit(LayoutKey key,Color color,double now,bool continuous){
        int maxLayers=Budget.Count(10);
        if(continuous&&splashes.Count>=maxLayers)return;
        TrimSplashes(maxLayers-1);
        var style=NextImpact();bool flick=style==Impact.Flick,mist=style==Impact.Mist,pool=style==Impact.Pool;
        double direction=Unit()*Tau;float power=Range(.85f,1.18f);
        float core=pool?Range(.34f,.46f):mist?Range(.17f,.26f):Range(.25f,.36f);
        int jets=flick?random.Next(3,7):pool?random.Next(4,8):random.Next(6,13);
        var angles=new double[jets];var lengths=new float[jets];var widths=new float[jets];
        for(int j=0;j<jets;j++){
            angles[j]=flick&&j<jets-1?direction+Bell()*.53:direction+Tau*(j+Unit()*.9)/jets;
            lengths[j]=Range(.08f,flick?.55f:pool?.24f:.38f)*power;
            widths[j]=Range(.055f,pool?.20f:.14f);
        }
        float halfW=key.cap.Width/(3840*pitchX),halfH=key.cap.Height/(1440*pitchY);
        float ox=((key.cap.Left+key.cap.Width*.5f)/1920-key.x)/pitchX,oy=((key.cap.Top+key.cap.Height*.5f)/720-key.y)/pitchY;
        if(continuous)core*=.52f;
        var s=new Splash{key=key,color=color,enhanced=enhanced,birth=now,end=now+Lifetime,fade=now+560};
        var contour=new PointF[continuous?36:90];double lobe=Unit()*Tau;
        for(int i=0;i<contour.Length;i++){
            double a=direction+i*Tau/contour.Length;
            double r=core*(1+.17*Math.Sin(3*a+lobe)+.10*Math.Sin(5*a-direction)+.07*Bell());
            if(flick)r*=.70+.42*Math.Abs(Math.Cos(a-direction))+.30*Math.Cos(a-direction);
            for(int j=0;j<jets;j++){
                double d=Math.Sin((a-angles[j])*.5)*2/widths[j];r+=lengths[j]*Math.Exp(-d*d);
            }
            contour[i]=new PointF((float)Math.Max(ox-halfW+.004,Math.Min(ox+halfW-.004,Math.Cos(a)*r+Math.Max(ox-halfW*.65f,Math.Min(ox+halfW*.65f,0)))),(float)Math.Max(oy-halfH+.004,Math.Min(oy+halfH-.004,Math.Sin(a)*r+Math.Max(oy-halfH*.65f,Math.Min(oy+halfH*.65f,0)))));
        }
        s.body.AddClosedCurve(contour,.34f);
        s.wet=new PathGradientBrush(s.body){CenterPoint=new PointF(-core*.19f,core*.12f),CenterColor=s.color,SurroundColors=new[]{Color.FromArgb(0,s.color)}};
        int pores=random.Next(3,8);
        for(int i=0;i<pores;i++){
            double a=Unit()*Tau;float d=core*Range(.50f,.68f),r=Range(.008f,.026f);
            s.body.AddEllipse((float)Math.Cos(a)*d-r,(float)Math.Sin(a)*d-r,r*2,r*2);
        }
        var flights=new Flight[random.Next(8,13)];
        for(int i=0;i<flights.Length;i++)
            flights[i]=new Flight{delay=Range(0,58),duration=Range(245,470),bend=Range(-.15f,.15f),lift=Range(.02f,.18f)};
        int count=mist?random.Next(205,306):flick?random.Next(145,228):pool?random.Next(108,172):random.Next(165,260);
        if(continuous)count=random.Next(26,53);
        count=Budget.Count(count);
        s.drops=new Drop[count];s.bounds=s.body.GetBounds();s.palette=Palette(s.color);
        float escapeChance=Range(.10f,.18f);
        for(int i=0;i<count;i++){
            int j=random.Next(jets);double a=angles[j]+Bell()*(mist?.53:flick?.15:.27);
            if(Unit()<(flick?.10f:.22f))a=direction+Unit()*Tau;
            float ux=(float)Math.Cos(a),uy=(float)Math.Sin(a);
            float reach=Range(.48f,1.1f)+lengths[j];
            if(flick)reach*=.8f+.65f*Math.Max(0,(float)Math.Cos(a-direction));else if(mist)reach*=1.16f;
            float distance=core*.62f+reach*(float)Math.Pow(Unit(),pool?1.7:.83)*power;
            float radius=Range(.0065f,.018f)+(float)Math.Pow(Unit(),5)*(pool?.065f:mist?.031f:.05f);
            float stretch=Unit()<(flick?.30f:.14f)?Range(1.6f,3.4f):Range(.85f,1.3f);
            var d=MakeDrop(radius,stretch,ux,uy,Range(.57f,.96f));
            float extent=d.half/(float)PixelsPerUnit;
            d.offsetX=ox;d.offsetY=oy;d.limitX=Math.Max(.006f,halfW-extent);d.limitY=Math.Max(.006f,halfH-extent);d.restitution=Range(.27f,.62f);
            d.escapes=Unit()<escapeChance;d.flight=flights[i%flights.Length];
            float wall=Math.Min((d.limitX+Math.Sign(ux)*ox)/Math.Max(.001f,Math.Abs(ux)),(d.limitY+Math.Sign(uy)*oy)/Math.Max(.001f,Math.Abs(uy)));
            if(d.escapes||Unit()<.89f)distance=Math.Max(distance,wall*Range(1.15f,d.escapes?1.85f:2.15f));
            if(d.escapes)distance=Math.Max(.05f,wall)+Math.Min(2.6f,Math.Max(.25f,distance-wall))*tuning.spread/100f;
            d.x=ux*distance;d.y=uy*distance;s.drops[i]=d;
            float left=d.escapes?Math.Min(0,d.x)-extent:ox-halfW,right=d.escapes?Math.Max(0,d.x)+extent:ox+halfW;
            float top=d.escapes?Math.Min(0,d.y)-extent:oy-halfH,bottom=d.escapes?Math.Max(0,d.y)+extent:oy+halfH;
            s.bounds=RectangleF.Union(s.bounds,RectangleF.FromLTRB(left,top,right,bottom));
        }
        s.bounds.Inflate(.06f,.06f);splashes.Add(s);
    }
    static uint[] Palette(Color color){
        var table=new uint[256];
        for(int a=1;a<256;a++)table[a]=((uint)a<<24)|((uint)(color.R*a/255)<<16)|((uint)(color.G*a/255)<<8)|(uint)(color.B*a/255);
        return table;
    }
    static Drop MakeDrop(float r,float stretch,float ux,float uy,float opacity){
        float minor=Math.Max(.67f,r*PixelsPerUnit),major=minor*stretch;
        int half=(int)Math.Ceiling(Math.Max(major,minor)+.6f),side=half*2+1;
        var d=new Drop{half=half,side=side,mask=new byte[side*side]};
        for(int y=-half;y<=half;y++)for(int x=-half;x<=half;x++){
            float along=(x*ux+y*uy)/major,across=(-x*uy+y*ux)/minor;
            float coverage=.5f+minor*(1-(float)Math.Sqrt(along*along+across*across));
            d.mask[(y+half)*side+x+half]=(byte)(255*opacity*Math.Min(1,Math.Max(0,coverage)));
        }
        return d;
    }
    internal static float Bounce(float travel,float wall,float restitution){
        // Reflect the overshoot, losing only normal speed. Wide keys can travel farther before first contact.
        while(Math.Abs(travel)>wall){
            float side=Math.Sign(travel);travel=side*(wall-(Math.Abs(travel)-wall)*restitution);
        }
        return travel;
    }
    static float StainOpacity(Stain st,double now){
        float t=(float)Math.Max(0,Math.Min(1,st.released<0?0:(now-st.released)/st.recovery));
        return (st.previous+(st.strength-st.previous)*Ease(now-st.birth,135))*(1-t*t*(3-2*t));
    }
    void Dye(LayoutKey key,Color fresh,double now){
        Stain st;float retained=0;
        if(stains.TryGetValue(key,out st)){
            retained=StainOpacity(st,now);st.sprite.Dispose();
            float t=.50f/(.50f+retained);
            fresh=Color.FromArgb((int)(st.color.R+(fresh.R-st.color.R)*t),(int)(st.color.G+(fresh.G-st.color.G)*t),(int)(st.color.B+(fresh.B-st.color.B)*t));
        }else{st=new Stain{key=key};stains.Add(key,st);}
        st.pigment=fresh;st.released=-1;st.next=now+SprayPeriod;st.birth=now;st.color=fresh;st.previous=retained;st.strength=Math.Min(.88f,.53f+retained*.70f);
        int w=Math.Max(2,(int)Math.Round(key.housing.Width/(1920*pitchX)*PixelsPerUnit*.5f)),h=Math.Max(2,(int)Math.Round(key.housing.Height/(720*pitchY)*PixelsPerUnit*.5f));
        st.sprite=new Sprite(new Rectangle(-w,-h,2*w,2*h));PaintStain(st);
    }
    static unsafe void PaintStain(Stain st){
        var sprite=st.sprite;var color=Palette(st.color);var dst=(uint*)sprite.dib.pixels;int w=sprite.box.Width,h=sprite.box.Height;
        for(int y=0;y<h;y++)for(int x=0;x<w;x++){
            float u=2*(x+.5f)/w-1,v=2*(y+.5f)/h-1;
            float cx=Math.Max(0,Math.Abs(u)-.71f),cy=Math.Max(0,Math.Abs(v)-.71f);
            float sdf=(float)Math.Sqrt(cx*cx+cy*cy)-.27f;
            float cover=Math.Max(0,Math.Min(1,-sdf*12));
            float wet=(float)(.50+.19*Math.Cos(u*3.2+v*2.4)+.12*Math.Sin(u*5-v*4)+.14*Math.Exp(-55*(sdf+.09)*(sdf+.09)));
            dst[y*w+x]=color[(int)(255*cover*wet)];
        }
    }
    public void Release(LayoutKey key){
        Stain st;if(key==null||!stains.TryGetValue(key,out st)||st.released>=0)return;
        double now=clock();st.released=now;st.recovery=tuning.recovery;
        bool latest=true;for(int i=splashes.Count-1;i>=0;i--){
            var s=splashes[i];if(s.key!=key)continue;
            s.end=latest?now+st.recovery:Math.Min(s.end,now+st.recovery);
            s.fade=Math.Min(s.fade,s.end-(s.end-now)*.65);latest=false;
        }
    }
    void TrimStains(int keep){
        while(stains.Count>keep){
            var old=stains.Values.OrderBy(v=>v.released<0).ThenBy(v=>v.birth).First();
            old.sprite.Dispose();stains.Remove(old.key);
        }
    }
    void TrimSplashes(int keep){
        while(splashes.Count>keep){splashes[0].Dispose();splashes.RemoveAt(0);}
    }
    public void Clear(){
        foreach(var s in splashes)s.Dispose();splashes.Clear();
        foreach(var st in stains.Values)st.sprite.Dispose();stains.Clear();
    }
    public Rectangle SampleArea(int w,int h){return Rectangle.Empty;}
    Rectangle ScreenBox(LayoutKey key,RectangleF b,int w,int h){
        float sx=w*pitchX,sy=h*pitchY;
        return Rectangle.FromLTRB((int)Math.Floor(w*key.x+b.Left*sx)-2,(int)Math.Floor(h*key.y+b.Top*sy)-2,
            (int)Math.Ceiling(w*key.x+b.Right*sx)+2,(int)Math.Ceiling(h*key.y+b.Bottom*sy)+2);
    }
    public Rectangle Bounds(int w,int h){
        TrimStains(Budget.Keys);TrimSplashes(Budget.Count(10));
        double now=clock();var area=Rectangle.Empty;
        foreach(var st in stains.Values)if(st.released<0&&now>=st.next){
            Emit(st.key,st.pigment,now,true);st.next=now+SprayPeriod;
        }
        for(int i=splashes.Count-1;i>=0;i--){
            var s=splashes[i];if(now>=s.end){s.Dispose();splashes.RemoveAt(i);continue;}
            var box=ScreenBox(s.key,s.bounds,w,h);area=area.IsEmpty?box:Rectangle.Union(area,box);
        }
        foreach(var st in stains.Values.ToArray()){
            if(st.released>=0&&now-st.released>=st.recovery){st.sprite.Dispose();stains.Remove(st.key);continue;}
            var box=Rectangle.Ceiling(new RectangleF(st.key.housing.X*w/1920-2,st.key.housing.Y*h/720-2,st.key.housing.Width*w/1920+4,st.key.housing.Height*h/720+4));
            area=area.IsEmpty?box:Rectangle.Union(area,box);
        }
        return Rectangle.Intersect(area,new Rectangle(0,0,w,h));
    }
    [DllImport("msvcrt.dll",CallingConvention=CallingConvention.Cdecl)]static extern IntPtr memset(IntPtr dest,int value,UIntPtr bytes);
    static Rectangle TileBounds(RectangleF b){
        return Rectangle.FromLTRB((int)Math.Floor(b.Left*PixelsPerUnit)-2,(int)Math.Floor(b.Top*PixelsPerUnit)-2,
            (int)Math.Ceiling(b.Right*PixelsPerUnit)+2,(int)Math.Ceiling(b.Bottom*PixelsPerUnit)+2);
    }
    static unsafe void Raster(Splash s,double age){
        if(s.settled)return;
        if(s.sprite==null){
            s.sprite=new Sprite(TileBounds(s.bounds));
            s.bodyStamp=new Sprite(TileBounds(s.body.GetBounds()));
            using(var g=Graphics.FromImage(s.bodyStamp.dib.bitmap)){
                g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;
                g.TranslateTransform(-s.bodyStamp.box.X,-s.bodyStamp.box.Y);g.ScaleTransform(PixelsPerUnit,PixelsPerUnit);
                using(var brush=new SolidBrush(Color.FromArgb(135,s.color)))
                using(var pen=new Pen(Color.FromArgb(102,s.color),.016f)){
                    g.FillPath(brush,s.body);g.FillPath(s.wet,s.body);g.DrawPath(pen,s.body);
                    if(s.enhanced){
                        // Broken, offset brush strokes stay attached to the baked outline.
                        var contour=s.body.PathPoints;
                        int take=Math.Min(64,contour.Length-2);
                        using(var shadow=new Pen(Color.FromArgb(215,43,21,62),.023f))
                        using(var glint=new Pen(Color.FromArgb(233,255,189,226),.014f)){
                            for(int j=1;j<take;j+=6){
                                var a=contour[j];var b=contour[j+1];var c=contour[j+2];
                                g.DrawLine(shadow,a,b);
                                a.X-=.009f;a.Y-=.013f;c.X-=.005f;c.Y-=.007f;g.DrawLine(glint,a,c);
                            }
                        }
                    }
                }
                g.Flush(FlushIntention.Sync);
            }
        }
        var sprite=s.sprite;var box=sprite.box;
        memset(sprite.dib.pixels,0,(UIntPtr)((long)box.Width*box.Height*4));
        uint* dst=(uint*)sprite.dib.pixels,src=(uint*)s.bodyStamp.dib.pixels;
        var body=s.bodyStamp.box;float scale=.13f+.87f*Ease(age,145);
        if(scale>=1){
            for(int y=0;y<body.Height;y++)Buffer.MemoryCopy(src+y*body.Width,
                dst+(y+body.Y-box.Y)*box.Width+body.X-box.X,body.Width*4,body.Width*4);
        }else{
            int left=(int)Math.Ceiling(body.Left*scale),right=(int)Math.Ceiling(body.Right*scale);
            for(int y=(int)Math.Ceiling(body.Top*scale);y<(int)Math.Ceiling(body.Bottom*scale);y++){
                var row=src+Math.Max(0,Math.Min(body.Height-1,(int)Math.Floor(y/scale)-body.Y))*body.Width;
                var outRow=dst+(y-box.Y)*box.Width-box.X;
                for(int x=left;x<right;x++)outRow[x]=row[Math.Max(0,Math.Min(body.Width-1,(int)Math.Floor(x/scale)-body.X))];
            }
        }
        foreach(var d in s.drops){
            var f=d.flight;if(age<=f.delay)continue;
            float t=Ease(age-f.delay,f.duration),arc=t*(1-t);
            float x=d.x*t+f.bend*arc,y=d.y*t-f.lift*arc;
            bool flipX=!d.escapes&&Math.Abs(x-d.offsetX)>d.limitX,flipY=!d.escapes&&Math.Abs(y-d.offsetY)>d.limitY;
            if(flipX)x=d.offsetX+Bounce(x-d.offsetX,d.limitX,d.restitution);if(flipY)y=d.offsetY+Bounce(y-d.offsetY,d.limitY,d.restitution);
            int left=(int)Math.Round(x*PixelsPerUnit)-d.half-box.X,top=(int)Math.Round(y*PixelsPerUnit)-d.half-box.Y;
            for(int j=Math.Max(0,-top);j<Math.Min(d.side,box.Height-top);j++){
                int srcRow=(flipY?d.side-1-j:j)*d.side;
                var row=dst+(top+j)*box.Width;
                for(int i=Math.Max(0,-left);i<Math.Min(d.side,box.Width-left);i++){
                    int a=d.mask[srcRow+(flipX?d.side-1-i:i)];if(a==0)continue;
                    int p=left+i;row[p]=s.palette[a+((int)(row[p]>>24)*(255-a)+127)/255];
                }
            }
        }
        s.settled=age>=530;
    }
    [StructLayout(LayoutKind.Sequential,Pack=1)] struct Blend {public byte op,flags,opacity,alpha;}
    [DllImport("msimg32.dll")]static extern bool AlphaBlend(IntPtr dest,int x,int y,int w,int h,IntPtr src,int sx,int sy,int sw,int sh,Blend blend);
    [DllImport("gdi32.dll")]static extern bool GdiFlush();
    static void Blit(Graphics g,Surface.Dib dib,Sprite sprite,float cx,float cy,float sx,float sy,float opacity){
        if(opacity<=0)return;var b=sprite.box;
        var dest=Rectangle.FromLTRB((int)Math.Round(cx+b.Left*sx),(int)Math.Round(cy+b.Top*sy),
            (int)Math.Round(cx+b.Right*sx),(int)Math.Round(cy+b.Bottom*sy));
        if(dest.Width<=0||dest.Height<=0)return;
        if(dib!=null){
            if(!AlphaBlend(dib.dc,dest.X,dest.Y,dest.Width,dest.Height,sprite.dib.dc,0,0,b.Width,b.Height,
                new Blend{opacity=(byte)(255*opacity),alpha=1}))throw new InvalidOperationException("Ink compositing failed");
        }else if(opacity>=1)g.DrawImage(sprite.dib.bitmap,dest,0,0,b.Width,b.Height,GraphicsUnit.Pixel);
        else using(var attr=new ImageAttributes()){
            var matrix=new ColorMatrix();matrix.Matrix33=opacity;attr.SetColorMatrix(matrix);
            g.DrawImage(sprite.dib.bitmap,dest,0,0,b.Width,b.Height,GraphicsUnit.Pixel,attr);
        }
    }
    public void Draw(Graphics g,int w,int h,int dx,int dy,Bitmap wallpaper,Rectangle sample){DrawCore(g,null,w,h,dx,dy);}
    public void DrawDesktop(Surface.Dib dib,int w,int h){DrawCore(null,dib,w,h,0,0);}
    void DrawCore(Graphics g,Surface.Dib dib,int w,int h,int dx,int dy){
        double now=clock();float sx=w*pitchX/PixelsPerUnit,sy=h*pitchY/PixelsPerUnit;
        foreach(var st in stains.Values)Blit(g,dib,st.sprite,(st.key.housing.X+st.key.housing.Width*.5f)*w/1920-dx,(st.key.housing.Y+st.key.housing.Height*.5f)*h/720-dy,sx,sy,StainOpacity(st,now));
        foreach(var s in splashes){
            double age=now-s.birth;float opacity=1-(float)Math.Max(0,Math.Min(1,(now-s.fade)/Math.Max(1,s.end-s.fade)));
            if(opacity<=0)continue;Raster(s,age);Blit(g,dib,s.sprite,s.key.x*w-dx,s.key.y*h-dy,sx,sy,opacity);
        }
        if(dib!=null)GdiFlush();
    }
}
