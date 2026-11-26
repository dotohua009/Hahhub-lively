using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;

internal sealed class SilkColony : SkillScene,IDisposable {
    public const string Id="hahhub-silk";
    const int Limit=420, Sides=8, Headings=16, Poses=6;
    sealed class Junction { public PointF p; public List<int> edges=new List<int>(); }
    struct Spider { public int node,next,serial;public float t,speed,phase,feeding; }
    readonly List<Junction> nodes=new List<Junction>();
    readonly Spider[] spiders=new Spider[Limit];
    sealed class PathMap {public int[] toward;public List<int> feedingKeys;}
    readonly Dictionary<int,PathMap> routes=new Dictionary<int,PathMap>();
    List<int> feedingKeys=new List<int>();
    readonly Dictionary<LayoutKey,int> indices=new Dictionary<LayoutKey,int>();
    readonly float[] gaps;
    int[] toward;int count,sequence,food=-1;
    double last=-1,birth,depart=-1;float pending,retiring;
    public int color=0x191722;
    Bitmap atlas;int atlasColor;
    public SilkColony(GlassData data,Func<double> time=null):base(data,time){
        gaps=new float[data.keys.Length];
        for(int i=0;i<data.keys.Length;i++)indices[data.keys[i]]=i;
    }
    public int Population {get{return count;}}
    static float LegSpan(float gap,int id){return 3f*gap*(1.10f+.26f*Noise(4,id));}
    public override bool Active {get{return count>0||base.Active;}}
    void Link(int a,int b){if(!nodes[a].edges.Contains(b)){nodes[a].edges.Add(b);nodes[b].edges.Add(a);}}
    void Build(){
        nodes.Clear();
        for(int k=0;k<layout.keys.Length;k++){
            var cap=layout.keys[k].cap;float gap=float.MaxValue;
            foreach(var other in layout.keys){
                var b=other.cap;
                if(b==cap)continue;
                if(Math.Min(cap.Bottom,b.Bottom)-Math.Max(cap.Top,b.Top)>Math.Min(cap.Height,b.Height)*.5f){
                    float d=Math.Max(b.Left-cap.Right,cap.Left-b.Right);if(d>0)gap=Math.Min(gap,d);
                }
                if(Math.Min(cap.Right,b.Right)-Math.Max(cap.Left,b.Left)>Math.Min(cap.Width,b.Width)*.45f){
                    float d=Math.Max(b.Top-cap.Bottom,cap.Top-b.Bottom);if(d>0)gap=Math.Min(gap,d);
                }
            }
            gaps[k]=gap==float.MaxValue?Math.Max(2,cap.Height*.08f):Math.Max(2,Math.Min(cap.Height*.35f,gap));
            cap.Inflate(gaps[k]*.42f,gaps[k]*.42f);
            float x=cap.Left,y=cap.Top,r=cap.Right,bottom=cap.Bottom,cx=(x+r)/2,cy=(y+bottom)/2;
            foreach(var pt in new[]{new PointF(x,y),new PointF(cx,y),new PointF(r,y),new PointF(r,cy),new PointF(r,bottom),new PointF(cx,bottom),new PointF(x,bottom),new PointF(x,cy)})
                nodes.Add(new Junction{p=pt});
            for(int i=0;i<Sides;i++)Link(k*Sides+i,k*Sides+(i+1)%Sides);
        }
        // Bridge nearby cap edges, including staggered rows and wide modifiers.
        for(int a=0;a<nodes.Count;a++)for(int b=a/Sides*Sides+Sides;b<nodes.Count;b++){
            if(Distance(nodes[a].p,nodes[b].p)<Math.Min(layout.keys[a/Sides].cap.Height,layout.keys[b/Sides].cap.Height)*.62f)Link(a,b);
        }
    }
    void Route(int key){
        food=key;PathMap known;
        if(routes.TryGetValue(key,out known)){toward=known.toward;feedingKeys=known.feedingKeys;return;}
        toward=new int[nodes.Count];feedingKeys=new List<int>();
        var dist=new float[nodes.Count];var done=new bool[nodes.Count];
        for(int i=0;i<dist.Length;i++){dist[i]=i/Sides==key?0:float.MaxValue;toward[i]=i;}
        for(int n=0;n<dist.Length;n++){
            int a=-1;float best=float.MaxValue;
            for(int i=0;i<dist.Length;i++)if(!done[i]&&dist[i]<best){best=dist[i];a=i;}
            if(a<0)break;done[a]=true;
            foreach(var b in nodes[a].edges){
                float cost=best+Math.Max(1,Distance(nodes[a].p,nodes[b].p));
                if(cost<dist[b]){dist[b]=cost;toward[b]=a;}
            }
        }
        for(int i=0;i<layout.keys.Length;i++)if(dist[i*Sides]<float.MaxValue)feedingKeys.Add(i);
        feedingKeys.Sort((a,b)=>dist[a*Sides].CompareTo(dist[b*Sides]));
        int half=(layout.keys.Length+1)/2;
        if(feedingKeys.Count>half)feedingKeys.RemoveRange(half,feedingKeys.Count-half);
        routes[key]=new PathMap{toward=toward,feedingKeys=feedingKeys};
    }
    public override void Strike(LayoutKey key){
        if(key==null)return;
        if(nodes.Count==0)Build();
        bool idle=!Active;if(!BeginContact(key))return;
        if(idle){birth=clock();last=birth;}pending=Math.Max(10,pending);
        depart=-1;retiring=0;
        if(food!=indices[key])Route(indices[key]);
    }
    public override void Clear(){base.Clear();count=0;food=-1;last=-1;depart=-1;pending=retiring=0;nodes.Clear();routes.Clear();feedingKeys=new List<int>();toward=null;}
    protected override void Step(){
        base.Step();
        count=Math.Min(count,Budget.Spiders);
        if(last<0)return;
        float dt=(float)Math.Min(.08,Math.Max(0,(now-last)*.001));last=now;
        int bait=-1;
        for(int i=contacts.Count-1;i>=0;i--)if(contacts[i].released<0){bait=indices[contacts[i].key];break;}
        if(bait>=0 && bait!=food)Route(bait);
        if(bait<0 && depart<0)depart=now;
        if(food<0||feedingKeys.Count==0)return;
        if(bait>=0){
            int radius=Math.Min(feedingKeys.Count,2+(int)((now-birth)/180));
            pending+=dt*(24+Math.Min(40,(float)(now-birth)*.006f));
            while(pending>=1 && count<Budget.Spiders){
                int id=sequence++;int k=feedingKeys[(int)(Noise(37,id)*radius)%radius];
                int n=k*Sides+id%Sides;
                spiders[count++]=new Spider{node=n,next=toward[n],serial=id,speed=38+Noise(18,id)*78,phase=Noise(3,id)*6.28f};
                pending--;
            }
            pending=Math.Min(1,pending);
        }else if(now-depart>1050){
            retiring+=dt*(50+count*.48f);
            int remove=Math.Min(count,(int)retiring);count-=remove;retiring-=remove;
        }
        for(int i=0;i<count;i++){
            var s=spiders[i];
            if(bait>=0 && s.node/Sides==food){
                s.feeding+=dt;
                if(s.feeding>1.7f+Noise(31,s.serial)*1.6f){
                    // A satiated spider slips beneath the cap. New arrivals keep the distant seams populated.
                    s.serial=sequence++;int far=Math.Min(feedingKeys.Count,2+(int)((now-birth)/180));
                    int key=feedingKeys[(int)(Noise(49,s.serial)*far)%far];
                    s.node=key*Sides+s.serial%Sides;s.next=toward[s.node];s.t=s.feeding=0;
                }
            }else s.feeding=0;
            float travel=dt*s.speed;
            for(int hop=0;travel>0 && hop<6;hop++){
                float length=Math.Max(1,Distance(nodes[s.node].p,nodes[s.next].p));
                float left=(1-s.t)*length;
                if(travel<left){s.t+=travel/length;break;}
                travel-=left;s.node=s.next;s.t=0;
                int next=toward[s.node];
                if(next==s.node||(depart>=0 && now-depart>1050)){
                    int side=s.node%Sides,step=(s.serial%2==0?1:7);
                    next=s.node-side+(side+step)%Sides;
                }
                s.next=next;
            }
            spiders[i]=s;
        }
        if(!Active){food=-1;last=-1;depart=-1;pending=retiring=0;}
    }
    PointF Body(Spider s){
        var a=nodes[s.node].p;var b=nodes[s.next].p;float len=Math.Max(1,Distance(a,b)),gap=gaps[s.node/Sides];
        var p=Lerp(a,b,s.t);float side=(Sin(now*.005+s.phase+s.t*3)*.54f+Sin(s.phase*4)*.42f)*gap;
        return At(p,-(b.Y-a.Y)*side/len,(b.X-a.X)*side/len);
    }
    protected override RectangleF SceneBounds(){
        RectangleF r=RectangleF.Empty;
        foreach(var p in contacts){var cap=p.key.cap;cap.Inflate(64,64);r=Union(r,cap);}
        for(int i=0;i<count;i++){var s=spiders[i];float pad=LegSpan(gaps[s.node/Sides],s.serial)*.5f+2;r=Union(r,Around(Body(s),pad,pad));}
        return r;
    }
    ParticleSprite[] poses; ParticleSurface layer;
    void MakeAtlas(){
        if(atlas!=null&&atlasColor==color)return;
        if(atlas!=null)atlas.Dispose();
        const int tile=48;
        atlas=new Bitmap(tile*Headings,tile*Poses,PixelFormat.Format32bppPArgb);atlasColor=color;
        using(var g=Graphics.FromImage(atlas)){
            g.SmoothingMode=SmoothingMode.AntiAlias;
            for(int h=0;h<Headings;h++)for(int p=0;p<Poses;p++){
                var state=g.Save();g.TranslateTransform(tile*h+24,tile*p+24);g.RotateTransform(360f*h/Headings);g.ScaleTransform(1.5f,1.5f);
                for(int k=0;k<4;k++)for(int side=-1;side<=1;side+=2){
                    float walk=Sin((p/(float)Poses)*6.28+k*2.1+side)*2;
                    PointF hip=new PointF(3-k,side*1.3f),knee=new PointF(9-k*3.8f+walk*.45f,side*(4.6f+k*.46f)),ankle=new PointF(9-k*4.7f+walk,side*(8.4f+k*.4f)),foot=new PointF(13-k*6+walk,side*(12-Math.Abs(k-1.5f)));
                    Line(g,color,1,1.65f,hip,knee);Line(g,color,1,1.25f,knee,ankle);Line(g,color,1,.78f,ankle,foot);
                    Line(g,Mix(color,0xa89c9f,.38f),.8f,.55f,hip,knee);
                }
                Dot(g,color,1,new PointF(-2,0),6,4);Dot(g,color,1,new PointF(5,0),3.4f,2.9f);
                Dot(g,Mix(color,0x937a68,.38f),.95f,new PointF(-3,-.4f),3.1f,1.9f);
                Dot(g,0xdca37c,.85f,new PointF(6.7f,-1),.72f,.58f);
                Dot(g,Mix(color,0xbac8da,.42f),.92f,new PointF(-2,-1.7f),2.2f,.95f);g.Restore(state);
            }
        }
        poses=new ParticleSprite[Headings*Poses];
        for(int p=0;p<Poses;p++)for(int h=0;h<Headings;h++)poses[p*Headings+h]=new ParticleSprite(atlas,new Rectangle(h*tile,p*tile,tile,tile));
    }
    sealed class Spoke {
        public PointF tip;
        public double start, duration;
        public double Reaches(float fraction){return start+duration*fraction;}
    }
    sealed class Strand {
        public PointF a, control, b, bead;
        public double start, duration;
        public float opacity;
        public bool hasBead;
    }
    sealed class Weave {
        public PointF hub;
        public Spoke[] spokes;
        public List<Strand> strands=new List<Strand>();
    }

    // Each contact owns a stable silk pattern for its entire lifetime.
    readonly ConditionalWeakTable<Contact,Weave> webs=
        new ConditionalWeakTable<Contact,Weave>();
    static readonly ConditionalWeakTable<Contact,Weave>.CreateValueCallback plan=PlanWeb;

    static Weave PlanWeb(Contact contact){
        var cap=contact.key.cap;
        int seed=contact.seed, count=12+(int)(Noise(seed,81)*7);
        var web=new Weave{
            hub=At(contact.origin,(Noise(seed,24)-.5f)*Math.Min(cap.Width,cap.Height)*.50f,
                                  (Noise(seed,13)-.5f)*cap.Height*.44f),
            spokes=new Spoke[count]
        };
        cap.Inflate(Math.Min(48,cap.Height*.36f),Math.Min(48,cap.Height*.36f));
        int first=(int)(Noise(seed,59)*count)%count;
        for(int rank=0;rank<count;rank++){
            // Alternate sides so the first load-bearing threads span the cap.
            int i=(first+rank/2+(rank%2)*((count+1)/2))%count;
            float angle=(i+.36f*Noise(seed,i))*6.283f/count, ux=Cos(angle),uy=Sin(angle);
            float distance=Math.Min((ux>=0?cap.Right-web.hub.X:web.hub.X-cap.Left)/Math.Max(.001f,Math.Abs(ux)),
                                    (uy>=0?cap.Bottom-web.hub.Y:web.hub.Y-cap.Top)/Math.Max(.001f,Math.Abs(uy)));
            float reach=distance*(.63f+.36f*Noise(seed+21,i));
            web.spokes[i]=new Spoke{tip=At(web.hub,ux*reach,uy*reach),
                start=rank*43, duration=125+reach*2.5+70*Noise(seed+9,i)};
        }
        // Two tips travel around each course, starting from the same spoke.
        double courseStart=210;
        int winding=Noise(seed,87)<.5f?1:-1;
        for(int ring=1;ring<=7;ring++){
            var joins=new PointF[count];
            var ready=new double[count];
            for(int i=0;i<count;i++){
                float f=(ring+.28f*Noise(seed+i,ring))/8f;
                joins[i]=Lerp(web.hub,web.spokes[i].tip,f);
                ready[i]=web.spokes[i].Reaches(f);
            }
            double finish=courseStart;
            for(int side=0;side<2;side++){
                int i=first, direction=side==0?winding:-winding;
                int steps=side==0?(count+1)/2:count/2;
                double cursor=courseStart;
                for(int step=0;step<steps;step++){
                    int j=(i+direction+count)%count;
                    int sector=direction>0?i:j;
                    PointF a=joins[i],b=joins[j];
                    var control=At(Lerp(a,b,.5f),1.2f,1.1f+ring*.66f);
                    double start=Math.Max(cursor,Math.Max(ready[i],ready[j]));
                    double duration=29+Distance(a,b)*1.65+15*Noise(seed+sector,ring+12);
                    if((sector*13+ring*7+seed)%23!=0){
                        web.strands.Add(new Strand{a=a,control=control,b=b,start=start,duration=duration,
                            bead=Lerp(Lerp(a,b,.5f),control,.75f),hasBead=(sector+ring)%4==0,
                            opacity=.18f+.29f*Noise(seed+sector,ring)});
                    }
                    cursor=start+duration;i=j;
                }
                finish=Math.Max(finish,cursor);
            }
            first=(first+winding+count)%count;
            courseStart=finish;
        }
        return web;
    }

    static void DrawStrand(Graphics g,Pen pen,Strand strand,float t){
        if(t>=1){g.DrawBezier(pen,strand.a,strand.control,strand.control,strand.b);return;}
        // De Casteljau split: retain the exact portion of the finished curve behind the moving tip.
        var c1=Lerp(strand.a,strand.control,t);
        var c2=Lerp(c1,strand.control,t);
        var other=Lerp(strand.control,Lerp(strand.control,strand.b,t),t);
        g.DrawBezier(pen,strand.a,c1,c2,Lerp(c2,other,t));
    }

    internal void DrawSilk(Graphics g){
        foreach(var p in contacts){
            double age=now-p.birth;
            if(p.released>=0)age=Math.Min(age,p.released-p.birth+160);
            if(age<=0)continue;
            float fade=Fade(p);
            var web=webs.GetValue(p,plan);
            using(var shadow=new Pen(Ink(0x293541,.45f*fade),1.25f))
            using(var thread=new Pen(Ink(0xd8e6ed,.66f*fade),.92f))
            using(var cross=new Pen(Color.White,.8f)){
                var shadowRoot=At(web.hub,1,1.6f);
                foreach(var spoke in web.spokes){
                    if(age<=spoke.start)continue;
                    var tip=Lerp(web.hub,spoke.tip,Unit((age-spoke.start)/spoke.duration));
                    g.DrawLine(shadow,shadowRoot,At(tip,1,1.6f));g.DrawLine(thread,web.hub,tip);
                }
                foreach(var strand in web.strands){
                    if(age<=strand.start)continue;
                    float t=Unit((age-strand.start)/strand.duration);
                    cross.Color=Ink(0xdce6ec,fade*strand.opacity);
                    DrawStrand(g,cross,strand,t);
                    if(strand.hasBead && t>.5f){
                        float shine=fade*Smooth((t-.5f)*3);
                        Dot(g,0x63889a,shine*.84f,strand.bead,1.5f,1.24f);
                        Dot(g,0xf3fbff,shine*.96f,At(strand.bead,-.4f,-.4f),.85f,.69f);
                    }
                }
            }
        }
    }
    protected override void Paint(Graphics g,int w,int h,Bitmap image,Rectangle sample){
        DrawSilk(g);MakeAtlas();BeginLayer(ref layer,.85f);
        for(int i=0;i<count;i++){
            var s=spiders[i];var a=nodes[s.node].p;var b=nodes[s.next].p;var pos=Body(s);
            int head=((int)Math.Round(Math.Atan2(b.Y-a.Y,b.X-a.X)*Headings/(2*Math.PI))+Headings)%Headings;
            int pose=((int)(now/85)+s.serial)%Poses;float size=LegSpan(gaps[s.node/Sides],s.serial);
            layer.Stamp(poses[pose*Headings+head],pos.X,pos.Y,size,size);
        }
        layer.Present(g);
    }
    public void Dispose(){if(layer!=null){layer.Dispose();layer=null;}if(atlas!=null){atlas.Dispose();atlas=null;}Clear();}
}
