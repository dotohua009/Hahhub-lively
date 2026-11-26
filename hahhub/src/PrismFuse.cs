using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

internal sealed class Prism : SkillScene,IDisposable {
    public const string Id="hahhub-prism";
    public Prism(GlassData data,Func<double> time=null):base(data,time){}
    protected override RectangleF Extent(Contact p){var b=p.key.cap;b.Inflate(23,23);return b;}
    public override Rectangle SampleArea(int w,int h){return Pixels(SceneBounds(),w,h);}
    static readonly int[] cool={0x6ccdf5,0x72e3d1,0x9f9ae8,0x8dd0ff,0x87dfbf};
    static readonly int[] warm={0xe7a2cb,0xffbf93,0xc4a9fd,0xe0ba70,0xde90a6};
    // The outer cap, housing and stem have independent calibrated offsets, including wide modifiers.
    static PointF[] Rail(RectangleF b,int horizontal,int vertical,float cut){
        float r=Math.Min(b.Width,b.Height)*cut;
        var corners=new[]{new PointF(b.Left+r,b.Top),new PointF(b.Right-r,b.Top),new PointF(b.Right,b.Top+r),
            new PointF(b.Right,b.Bottom-r),new PointF(b.Right-r,b.Bottom),new PointF(b.Left+r,b.Bottom),
            new PointF(b.Left,b.Bottom-r),new PointF(b.Left,b.Top+r)};
        var ring=new List<PointF>();
        for(int j=0;j<8;j++){
            int n=j%4==0?horizontal:j%4==2?vertical:1;
            for(int k=0;k<n;k++)ring.Add(Lerp(corners[j],corners[(j+1)%8],k/(float)n));
        }
        return ring.ToArray();
    }
    static RectangleF Inset(RectangleF r,float f){r.Inflate(-r.Width*f,-r.Height*f);return r;}
    static RectangleF BlendBox(RectangleF a,RectangleF b,float f){
        return new RectangleF(a.X+(b.X-a.X)*f,a.Y+(b.Y-a.Y)*f,a.Width+(b.Width-a.Width)*f,a.Height+(b.Height-a.Height)*f);
    }
    struct Plane { public float angle,offset,grain;public int band,tint; }
    struct Lighting { public int r,g,b,mix,alpha,dx,dy,shine; }
    sealed class CutMap {
        public int seed;public RectangleF cap,shell,stem;public ParticleSprite pixels;
        public short[] facets;public byte[] edges;public readonly List<Plane> planes=new List<Plane>();
        public Lighting[] lights;
    }
    readonly Dictionary<LayoutKey,CutMap> cuts=new Dictionary<LayoutKey,CutMap>();
    ParticleSurface layer;
    public void Dispose(){if(layer!=null){layer.Dispose();layer=null;}cuts.Clear();}
    static float Cross(PointF p,PointF q,float x,float y){return (q.X-p.X)*(y-p.Y)-(q.Y-p.Y)*(x-p.X);}
    static void Raster(CutMap map,short id,PointF a,PointF b,PointF c){
        if(Cross(a,b,c.X,c.Y)<0){var swap=b;b=c;c=swap;}
        int w=map.pixels.w,h=map.pixels.h;
        int left=Math.Max(0,(int)Math.Floor(Math.Min(a.X,Math.Min(b.X,c.X)))),right=Math.Min(w-1,(int)Math.Ceiling(Math.Max(a.X,Math.Max(b.X,c.X))));
        int top=Math.Max(0,(int)Math.Floor(Math.Min(a.Y,Math.Min(b.Y,c.Y)))),bottom=Math.Min(h-1,(int)Math.Ceiling(Math.Max(a.Y,Math.Max(b.Y,c.Y))));
        for(int y=top;y<=bottom;y++)for(int x=left;x<=right;x++)
            if(Cross(a,b,x+.5f,y+.5f)>=-.001f&&Cross(b,c,x+.5f,y+.5f)>=-.001f&&Cross(c,a,x+.5f,y+.5f)>=-.001f)map.facets[y*w+x]=id;
    }
    static CutMap Grind(Contact p){
        var k=p.key;var cap=Inset(k.cap,.022f);var core=Inset(k.stem,.06f);var shell=k.housing;
        var size=new ParticleSprite(Math.Max(4,(int)Math.Ceiling(cap.Width*.77f)),Math.Max(4,(int)Math.Ceiling(cap.Height*.77f)));
        var m=new CutMap{cap=cap,seed=p.seed,shell=shell,stem=k.stem,pixels=size,facets=new short[size.data.Length],edges=new byte[size.data.Length]};
        m.planes.Add(new Plane()); // Zero denotes the clear exterior.
        int nx=Math.Max(2,(int)Math.Round(cap.Width/(35+16*Noise(p.seed,8)))),ny=Math.Max(2,(int)Math.Round(cap.Height/32));
        float bevel=.052f+.062f*Noise(p.seed,11);var center=Center(core);
        var rings=new[]{Rail(cap,nx,ny,bevel),Rail(Inset(cap,.038f),nx,ny,bevel),Rail(BlendBox(cap,shell,.67f),nx,ny,.15f),
            Rail(shell,nx,ny,.12f),Rail(BlendBox(shell,core,.54f),nx,ny,.16f),Rail(core,nx,ny,.108f)};
        Func<PointF,PointF> local=q=>new PointF((q.X-cap.X)*size.w/cap.Width,(q.Y-cap.Y)*size.h/cap.Height);
        int cyan=cool[Math.Min(4,(int)(Noise(p.seed,3)*5))],rose=warm[Math.Min(4,(int)(Noise(p.seed,4)*5))],count=rings[0].Length;
        for(int ring=0;ring<5;ring++)for(int i=0;i<count;i++){
            int j=(i+1)%count;var a=rings[ring][i];var b=rings[ring][j];var c=rings[ring+1][j];var d=rings[ring+1][i];
            float n=Noise(p.seed+ring*41,i),angle=(float)Math.Atan2(a.Y-center.Y,a.X-center.X)+n*.60f;
            int tint=Mix(cyan,rose,Unit(Sin(angle+p.seed%7)*1.5f+.5f));
            if(n>.5f){var q=a;a=b;b=q;q=c;c=d;d=q;}
            a=local(a);b=local(b);c=local(c);d=local(d);
            for(int half=0;half<2;half++){
                short id=(short)m.planes.Count;
                m.planes.Add(new Plane{band=ring,angle=angle+half*.77f,grain=n,offset=(6+13*n)*((ring+half)%2==0?1:-1),
                    tint=half==0?tint:Mix(tint,0x182738,.39f+n*.14f)});
                if(half==0)Raster(m,id,a,b,d);else Raster(m,id,b,c,d);
            }
        }
        var table=rings[5];var origin=local(center);short last=(short)m.planes.Count;
        m.planes.Add(new Plane{band=5,angle=p.seed%6,offset=4,tint=cyan,grain=.44f});
        for(int i=0;i<count;i++)Raster(m,last,origin,local(table[i]),local(table[(i+1)%count]));
        // Clip every facet to the calibrated outer octagon even when the user independently offsets the housing.
        float cut=Math.Min(cap.Width,cap.Height)*bevel;
        for(int y=0;y<size.h;y++)for(int x=0;x<size.w;x++){
            float dx=Math.Min(x+.5f,size.w-x-.5f)*cap.Width/size.w,dy=Math.Min(y+.5f,size.h-y-.5f)*cap.Height/size.h;
            int i=y*size.w+x;if(dx+dy<cut){m.facets[i]=0;continue;}
            short id=m.facets[i];
            if(id!=0&&(x==0||y==0||m.facets[i-1]!=id||m.facets[i-size.w]!=id))m.edges[i]=1;
        }
        m.lights=new Lighting[m.planes.Count];return m;
    }
    unsafe void Shade(Contact p,CutMap map,int w,int h,System.Drawing.Imaging.BitmapData source,Rectangle sample){
        float age=(float)(now-p.birth),fade=Fade(p),angle=age*.00155f+p.seed%13;
        var cap=map.cap;var image=map.pixels;float sx=w/1920f,sy=h/720f;
        for(int i=1;i<map.planes.Count;i++){
            var plane=map.planes[i];
            float glint=Unit(Cos(plane.angle*2+angle+plane.band*.73f));glint*=glint;glint*=glint;glint*=glint;
            int shade=Mix(plane.tint,0xffffff,glint*.84f);
            map.lights[i]=new Lighting{r=shade>>16,g=(shade>>8)&255,b=shade&255,mix=(int)(65+42*plane.grain+65*glint),
                alpha=(int)(255*fade*Smooth((age+39-(5-plane.band)*21)/172)),
                dx=(int)(Cos(plane.angle)*plane.offset*sx),dy=(int)(Sin(plane.angle)*plane.offset*sy),shine=(int)(21+89*glint)};
        }
        for(int y=0;y<image.h;y++){
            int row=y*image.w,py=(int)((cap.Y+(y+.5f)*cap.Height/image.h)*sy)-sample.Top;
            float first=(cap.X+.5f*cap.Width/image.w)*sx-sample.Left,step=cap.Width/image.w*sx;
            for(int x=0;x<image.w;x++,first+=step){
                int i=row+x,id=map.facets[i];if(id==0){image.data[i]=0;continue;}
                var f=map.lights[id];uint pixel=0xff324352;
                if(source!=null){
                    int yy=Math.Max(0,Math.Min(sample.Height-1,py+f.dy)),xx=Math.Max(0,Math.Min(sample.Width-1,(int)first+f.dx));
                    pixel=((uint*)((byte*)source.Scan0+yy*source.Stride))[xx];
                }
                int rest=256-f.mix,edge=map.edges[i]*f.shine;
                int r=Math.Min(255,(((int)(pixel>>16)&255)*rest+f.r*f.mix>>8)+edge);
                int g=Math.Min(255,(((int)(pixel>>8)&255)*rest+f.g*f.mix>>8)+edge);
                int b=Math.Min(255,(((int)pixel&255)*rest+f.b*f.mix>>8)+edge);
                image.data[i]=(uint)((f.alpha<<24)|((r*f.alpha/255)<<16)|((g*f.alpha/255)<<8)|(b*f.alpha/255));
            }
        }
    }
    protected override void Paint(Graphics g,int w,int h,Bitmap image,Rectangle sample){
        BeginLayer(ref layer,.84f);
        // The capture buffer may be larger than its current valid region. Ignore its padding.
        int sw=image==null?0:Math.Min(sample.Width,image.Width),sh=image==null?0:Math.Min(sample.Height,image.Height);
        sample.Size=new Size(sw,sh);
        var format=image!=null&&image.PixelFormat==System.Drawing.Imaging.PixelFormat.Format32bppRgb
            ?System.Drawing.Imaging.PixelFormat.Format32bppRgb:System.Drawing.Imaging.PixelFormat.Format32bppArgb;
        var src=sw<=0||sh<=0?null:image.LockBits(new Rectangle(0,0,sw,sh),System.Drawing.Imaging.ImageLockMode.ReadOnly,format);
        try{
            foreach(var p in contacts){
                CutMap map;
                if(!cuts.TryGetValue(p.key,out map)||map.seed!=p.seed||map.cap!=Inset(p.key.cap,.022f)||map.shell!=p.key.housing||map.stem!=p.key.stem)
                    cuts[p.key]=map=Grind(p);
                Shade(p,map,w,h,src,sample);
                var c=Center(map.cap);layer.Stamp(map.pixels,c.X,c.Y,map.cap.Width,map.cap.Height);
                for(int j=0;j<6;j++){
                    float beam=Unit(Cos((now-p.birth)*.0027+Noise(p.seed,j+221)*14));beam*=beam;beam*=beam;beam*=beam;
                    float shine=beam*Fade(p)*Smooth((now-p.birth)/240);if(shine<.32f)continue;
                    var rail=j%2==0?map.shell:map.stem;var q=new PointF(j%3==0?rail.Left:rail.Right,rail.Top+rail.Height*Noise(p.seed+98,j));
                    if(!map.cap.Contains(q))continue;
                    float radius=(3+6*Noise(p.seed,j+27))*shine;
                    layer.Stroke(At(q,-radius,-radius*.28f),At(q,radius,radius*.28f),0xffffff,shine,1.2f);
                    layer.Stroke(At(q,radius*.16f,-radius*.61f),At(q,-radius*.16f,radius*.61f),0xcdf4ff,shine,1);
                }
            }
        }finally{if(src!=null)image.UnlockBits(src);}
        layer.Present(g);
    }
}
internal sealed class VoxelFuse : SkillScene,IDisposable {
    ParticleSurface layer;
    public void Dispose(){if(layer!=null){layer.Dispose();layer=null;}}
    public const string Id="hahhub-fuse";
    const int ParticleLimit=1500;
    public const int DefaultFuseMs=1200, MinFuseMs=150, MaxFuseMs=5000;
    public int fuseMs=DefaultFuseMs;
    sealed class Charge {public LayoutKey key;public PointF pos;public double armed,fire;public int seed;public float unit;}
    sealed class Wave {public PointF pos;public double born;public float range;public int seed;}
    enum Material { Ember,Chip,Flame,Smoke }
    struct Particle {public float x,y,vx,vy,age,life,r,spin,angle,z,lift;public int sprite,tint;public Material mat;}
    readonly List<Charge> charges=new List<Charge>();
    readonly List<Wave> waves=new List<Wave>();
    readonly List<Particle> particles=new List<Particle>(ParticleLimit);
    double previous=-1;
    public VoxelFuse(GlassData data,Func<double> time=null):base(data,time){}
    public override bool Active{get{return charges.Count+waves.Count+particles.Count>0;}}
    internal int BurstCount{get;private set;}
    internal int ChainHits{get;private set;}
    internal int ParticleCount{get{return particles.Count;}}
    public override void Strike(LayoutKey key){
        if(key==null||charges.Exists(c=>c.key==key))return;
        double t=clock();if(!Budget.Start(t)||charges.Count>=Budget.Keys)return;
        if(!Active)previous=t;
        charges.Add(new Charge{key=key,pos=Center(key.cap),unit=Math.Min(key.cap.Width,key.cap.Height),armed=t,fire=t+fuseMs,seed=NextSeed()});
    }
    public override void Release(LayoutKey key){}
    public override void Clear(){charges.Clear();waves.Clear();particles.Clear();previous=-1;BurstCount=0;ChainHits=0;}
    void Burst(Charge c){
        BurstCount++;int seed=c.seed;float radius=c.unit*(3.4f+1.1f*Noise(seed,3));
        if(waves.Count>=Budget.Keys)waves.RemoveAt(0);
        waves.Add(new Wave{pos=c.pos,born=now,range=radius,seed=seed});
        foreach(var other in charges){
            float d=Distance(other.pos,c.pos);
            if(d>0&&d<radius*.83f){
                double arrived=now+80+d*.79;
                if(arrived<other.fire){other.fire=arrived;ChainHits++;}
                other.pos=At(other.pos,(other.pos.X-c.pos.X)/d*13,(other.pos.Y-c.pos.Y)/d*13);
            }
        }
        for(int j=0;j<particles.Count;j++){
            var s=particles[j];float dx=s.x-c.pos.X,dy=s.y-c.pos.Y,d=(float)Math.Sqrt(dx*dx+dy*dy);
            if(d<radius&&d>1){float push=(radius-d)*1.16f/d;s.vx+=dx*push;s.vy+=dy*push;particles[j]=s;}
        }
        int rays=8+(int)(Noise(seed,6)*9),total=Budget.Count(390+(int)(Noise(seed,18)*135));
        float axis=Noise(seed,52)*6.28f;
        for(int i=0;i<total&&particles.Count<Budget.Count(ParticleLimit);i++){
            Material m=i<32?Material.Smoke:i<98?Material.Flame:i<160?Material.Chip:Material.Ember;
            bool sideCloud=(i>=17&&i<32)||(i>=59&&i<98);
            int jet=i%rays;float n=Noise(seed+19,i),a=axis+jet*6.283f/rays+.34f*Sin(jet*1.7+seed%11)+(.5f-Noise(seed,i))*(m==Material.Ember?.32f:1.25f);
            float speed=radius*(m==Material.Ember?.45f+n*1.85f:m==Material.Chip?.38f+n: .14f+n*.49f);
            if(sideCloud){
                int lobe=i%5;float lane=Noise(seed+68,lobe);
                a=axis+lobe*1.2566f+(lane-.5f)*.67f+(Noise(seed+28,i)-.5f)*.44f;
                speed=radius*(.81f+lane*.63f+n*.35f);
            }
            float offset=c.unit*(m==Material.Smoke?.26f:.16f)*Noise(seed+4,i);
            float size=m==Material.Smoke?c.unit*(.36f+.30f*n):m==Material.Flame?c.unit*(.26f+.30f*n):m==Material.Chip?2+n*5:1+n;
            if(sideCloud){size*=.73f;offset+=.22f*c.unit;}
            particles.Add(new Particle{lift=m==Material.Chip?46+95*n:m==Material.Ember?12+52*n:0,x=c.pos.X+Cos(a)*offset,y=c.pos.Y+Sin(a)*offset,vx=Cos(a)*speed,vy=Sin(a)*speed,
                r=size,age=m==Material.Smoke?-.13f-n*.21f:m==Material.Flame?-n*.07f:0,
                life=m==Material.Smoke?1.3f+n*.83f:m==Material.Flame?.37f+n*.31f:.55f+Noise(seed+14,i)*.94f,
                mat=m,spin=(n-.5f)*7,angle=a,sprite=(i+seed)%4,tint=n>.7f?0xffedad:n>.36f?0xffaf4c:0xef672e});
        }
    }
    protected override void Step(){
        int limit=Budget.Count(ParticleLimit);
        if(particles.Count>limit)particles.RemoveRange(limit,particles.Count-limit);
        float dt=previous<0?0:(float)Math.Max(0,Math.Min(.12,(now-previous)*.001));previous=now;
        for(int i=particles.Count-1;i>=0;i--){
            var p=particles[i];p.age+=dt;
            if(p.age>=p.life){particles[i]=particles[particles.Count-1];particles.RemoveAt(particles.Count-1);continue;}
            if(p.age>0){p.x+=p.vx*dt;p.y+=p.vy*dt;p.angle+=dt*p.spin;
                if(p.mat==Material.Chip||p.mat==Material.Ember){
                    p.z+=p.lift*dt;p.lift-=dt*260;
                    if(p.z<0){p.z=0;p.lift=-p.lift*.34f;p.vx*=.82f;p.vy*=.82f;}
                }
                float drag=p.mat==Material.Ember?.89f:p.mat==Material.Chip?1.38f:2.24f;
                p.vx*=1-dt*drag;p.vy=p.vy*(1-dt*drag)+dt*(p.mat==Material.Smoke?-26:p.mat==Material.Chip?89:8);}
            particles[i]=p;
        }
        waves.RemoveAll(v=>now-v.born>680);
        for(int i=charges.Count-1;i>=0;i--)if(now>=charges[i].fire){var c=charges[i];charges.RemoveAt(i);Burst(c);}
    }
    protected override RectangleF SceneBounds(){
        RectangleF r=RectangleF.Empty;foreach(var c in charges)r=Union(r,Around(c.pos,c.key.cap.Width*.51f+7,c.key.cap.Height*.54f+9));
        foreach(var s in waves)r=Union(r,Around(s.pos,s.range+8,s.range+8));
        foreach(var p in particles)if(p.age>=0)r=Union(r,Around(new PointF(p.x,p.y-p.z*.46f),p.r*2+30,p.r*2+30));
        return r;
    }
    static readonly Lazy<ParticleSprite[]> blocks=new Lazy<ParticleSprite[]>(()=>{
        using(var sheet=new Bitmap(System.IO.Path.Combine(Project.Base,"effects","hahhub-materials","fuse-blocks.png"))){
            var variants=new ParticleSprite[8];
            for(int i=0;i<8;i++)variants[i]=new ParticleSprite(sheet,new Rectangle(i%4*256,i/4*256,256,256));
            return variants;
        }
    });
    void Block(Charge c){
        var cap=c.key.cap;float width=cap.Width*1.12f,height=cap.Height*1.07f;
        layer.Stamp(blocks.Value[c.seed%8],c.pos.X,c.pos.Y,width,height);
        bool flash=c.fire-now<430 && (int)((c.fire-now)/57)%2==0;
        var wick=At(c.pos,width*.182f,-height*.280f);
        for(int j=0;j<(flash?21:11);j++){
            float a=j*2.4f+(float)now*.007f,d=2+(float)((now*.054+j*3)%16);
            var q=At(wick,Cos(a)*d,Sin(a)*d);
            layer.Stroke(At(q,-Cos(a)*2.9f,-Sin(a)*2.9f),q,flash?0xfff3ba:0xffb04d,1-d/32,1.8f);
        }
    }
    static readonly Lazy<ParticleSprite[]> chips=new Lazy<ParticleSprite[]>(()=>{
        var sprites=new ParticleSprite[3];int[] top={0xfbb86a,0xe65e3d,0xe0af84};
        for(int i=0;i<3;i++)using(var b=new Bitmap(24,24,System.Drawing.Imaging.PixelFormat.Format32bppPArgb)){
            using(var g=Graphics.FromImage(b)){
                g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                PointF a=new PointF(1,9),k=new PointF(14,2),c=new PointF(22,8),d=new PointF(20,18),e=new PointF(7,23),f=new PointF(2,19),ridge=new PointF(8,13);
                Face(g,top[i],1,a,k,c,ridge);Face(g,Mix(top[i],0x271d25,.5f),1,ridge,c,d,e);Face(g,0x50292c,1,a,ridge,e,f);Line(g,0xffddb0,.89f,1.4f,a,k);
            }
            sprites[i]=new ParticleSprite(b,new Rectangle(0,0,24,24));
        }
        return sprites;
    });
    static readonly Lazy<ParticleSprite> flashGlow=new Lazy<ParticleSprite>(()=>{
        const int size=96;
        using(var b=new Bitmap(size,size)){
            for(int y=0;y<size;y++)for(int x=0;x<size;x++){
                double u=(x+0.5-size/2)/(size/2),v=(y+0.5-size/2)/(size/2),q=u*u+v*v;
                int a=(int)(Math.Pow(Math.Max(0,1-q),3)*205);
                b.SetPixel(x,y,Color.FromArgb(a,255,(int)(111+95*Math.Exp(-5*q)),76));
            }
            return new ParticleSprite(b,new Rectangle(0,0,size,size));
        }
    });
    void ShockKeys(Graphics g,Wave wave,float r){
        foreach(var key in layout.keys){
            var c=Center(key.cap);float dx=c.X-wave.pos.X,dy=c.Y-wave.pos.Y,d=Distance(c,wave.pos);
            if(d<12||d>wave.range||Math.Abs(d-r)>46)continue;
            var edge=key.cap;var glow=(1-Math.Abs(d-r)/46)*(1-d/wave.range)*.82f;
            bool horizontal=Math.Abs(dy)>Math.Abs(dx);
            float ex=horizontal?edge.Left:(dx>0?edge.Left:edge.Right),ey=horizontal?(dy>0?edge.Top:edge.Bottom):edge.Top;
            var a=new PointF(ex,ey);var b=At(a,horizontal?edge.Width:0,horizontal?0:edge.Height);
            Line(g,0xff9a58,glow,2.6f,a,b);
        }
    }
    protected override void Paint(Graphics g,int w,int h,Bitmap image,Rectangle sample){
        BeginLayer(ref layer,.60f);
        foreach(var c in charges)Block(c);
        foreach(var v in waves){
            float t=Unit((now-v.born)/600),r=(1-(1-t)*(1-t))*v.range;
            if(r>2 && t<1)ShockKeys(g,v,r);
            if(t<.54f){
                float burst=(.56f+.44f*Smooth((now-v.born)/35))*(1-Smooth(t/.54f));
                layer.Stamp(flashGlow.Value,v.pos.X,v.pos.Y,v.range*1.48f,v.range*1.12f,0,(int)(burst*232));
            }
            if(r>2 && t<1){
                int segments=9+v.seed%7;
                for(int j=0;j<segments;j++){
                    float edge=r*(.84f+.23f*Noise(v.seed+73,j));
                    float alpha=(1-t)*(1-t)*(.12f+.48f*Noise(v.seed+2,j));
                    using(var pen=new Pen(Ink(0xffdfb3,alpha),.9f+2.2f*(1-t)))
                        g.DrawArc(pen,v.pos.X-edge,v.pos.Y-edge,edge*2,edge*2,j*360f/segments+v.seed%23,4+14*Noise(v.seed,j));
                }
            }
        }
        for(int pass=0;pass<3;pass++)foreach(var p in particles){
            if(p.age<=0)continue;
            bool cloud=p.mat==Material.Smoke||p.mat==Material.Flame;
            if(pass!=(p.mat==Material.Smoke?0:p.mat==Material.Flame?1:2))continue;
            float t=Unit(p.age/p.life);var o=new PointF(p.x,p.y-p.z*.46f);
            if(cloud){
                float opacity=(1-Smooth((t-.53f)/.47f))*Smooth(p.age/.08);
                float radius=p.r*(.49f+1.1f*Smooth(t));
                layer.Stamp(SkillMaterials.Sprite((p.mat==Material.Flame?16:20)+p.sprite),o.X,o.Y,2*radius,2*radius*(1+.14f*Sin(p.angle)),p.spin*.13f,(int)(opacity*(p.mat==Material.Smoke?178:250)));
            }else if(p.mat==Material.Ember){
                var tail=At(o,-p.vx*.048f,(-p.vy+p.lift*.46f)*.048f);layer.Stroke(tail,o,p.tint,1-t,p.r*.85f);
                if(p.r>1.7f)layer.Stroke(o,o,0xffefd0,(1-t)*.95f,p.r);
            }else{
                if(p.z>5)layer.Stroke(new PointF(p.x,p.y),new PointF(p.x+p.r*.4f,p.y),0x241a1a,(1-t)*.53f,p.r);
                layer.Stamp(chips.Value[p.sprite%3],o.X,o.Y,2.5f*p.r,2.2f*p.r,p.angle,(int)((1-t)*255));
            }
        }
        layer.Present(g);
    }
}
