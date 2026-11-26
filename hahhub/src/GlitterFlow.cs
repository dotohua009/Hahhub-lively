using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// All keys tilt one half-filled tank in calibrated board space.
internal sealed partial class GlitterFlow : SkillScene, IDisposable {
    public const string Id="hahhub-glitter-flow";
    static readonly int[] colors={0xa7d7eb,0xb4b7f5,0xefc1df,0xf8dca8};
    static readonly Lazy<ParticleSprite[,]> foils=new Lazy<ParticleSprite[,]>(MakeFoils);
    static readonly Lazy<ParticleSprite[]> mica=new Lazy<ParticleSprite[]>(MakeMica);
    ParticleSurface layer;

    // Soft, striated mica plumes move with the same current as the discrete foil.
    static ParticleSprite[] MakeMica(){
        var plumes=new ParticleSprite[4];
        for(int k=0;k<plumes.Length;k++){
            var sprite=new ParticleSprite(40,20);
            for(int y=0;y<sprite.h;y++)for(int x=0;x<sprite.w;x++){
                float u=(x+.5f)/sprite.w*2-1,v=(y+.5f)/sprite.h*2-1;
                float bend=v+(.15f+.04f*k)*Sin(u*3+k);
                float envelope=Unit(1-u*u-bend*bend);
                float ridge=.5f+.5f*Cos(15*bend+2*u);
                int rgb=Mix(Mix(colors[k],0x494378,.47f),colors[k],ridge*envelope);
                uint a=(uint)(154*envelope*envelope*(.38f+.62f*ridge));
                sprite.data[y*sprite.w+x]=a<<24|((uint)((rgb>>16)&255)*a/255)<<16|
                    ((uint)((rgb>>8)&255)*a/255)<<8|(uint)(rgb&255)*a/255;
            }
            plumes[k]=sprite;
        }
        return plumes;
    }

    public GlitterFlow(GlassData data,Func<double> time=null):base(data,time){}
    static ParticleSprite[,] MakeFoils(){
        var sprites=new ParticleSprite[3*Spectrum.Length,3];
        for(int kind=0;kind<sprites.GetLength(0);kind++)for(int face=0;face<3;face++){
            int cut=kind/Spectrum.Length;
            bool star=cut==2,shard=cut==1;
            int n=star?10:shard?4:6;
            var outline=new PointF[n];
            for(int j=0;j<n;j++){
                float radius=star&&j%2==1?5:12;
                double a=j*Math.PI*2/n-Math.PI/2;
                outline[j]=new PointF(14+Cos(a)*radius,14+Sin(a)*radius);
            }
            int tint=Spectrum[kind%Spectrum.Length];
            int dark=Mix(0x293853,tint,.30f+face*.12f),bright=Mix(tint,0xfffcf0,.06f+face*.29f);
            using(var image=new Bitmap(28,28,PixelFormat.Format32bppPArgb))
            using(var g=Graphics.FromImage(image))
            using(var brush=new LinearGradientBrush(new Rectangle(2,2,24,24),Ink(bright,1),Ink(dark,1),48f)){
                g.SmoothingMode=SmoothingMode.AntiAlias;g.FillPolygon(brush,outline);
                int spectralEdge=Spectrum[(kind%Spectrum.Length+4)%Spectrum.Length];
                using(var facet=new SolidBrush(Ink(spectralEdge,.49f+face*.16f)))
                    g.FillPolygon(facet,new[]{outline[n-1],outline[0],new PointF(15,16)});
                using(var edge=new Pen(Ink(bright,.82f),1.3f))g.DrawLine(edge,outline[0],outline[1]);
                sprites[kind,face]=new ParticleSprite(image,new Rectangle(0,0,28,28));
            }
        }
        return sprites;
    }

    // The two stream functions describe unit horizontal and vertical flow around the
    // calibrated solid housings. Their face differences give the flux at each fork.
    const int W=176,H=66,N=W*H,Stride=W+1;
    readonly float[] water=new float[N],pigment=new float[N],right=new float[N],down=new float[N];
    readonly float[] horizontal=new float[(W+1)*(H+1)],vertical=new float[(W+1)*(H+1)];
    readonly bool[] solid=new bool[N];
    readonly float[] wake=new float[(W+1)*(H+1)];
    readonly int[] distance=new int[N],queue=new int[N],outlet=new int[N];
    readonly HashSet<LayoutKey> held=new HashSet<LayoutKey>();
    readonly int[] pixels=new int[N*9];
    readonly uint[] colorsLow=new uint[N],rows=new uint[N*3];
    readonly float[] density=new float[N],filtered=new float[N];
    static readonly Lazy<uint[]> optics=new Lazy<uint[]>(BuildOptics);
    Bitmap liquid; RectangleF tank;
    const int MaxFlakes=2320;
    struct Flake {public float x,y,vx,vy,size,tilt,roll,depth,grip,wet,spin,facet,glintWidth,aspect; public int cut;}
    readonly Flake[] flakes=new Flake[MaxFlakes];
    readonly int[] shore=new int[N],crowd=new int[N];
    Random random;
    float votesX,votesY,dx,dy,energy,phase;
    double last,released=-1,carry;
    int free,steps,wakeSteps;
    bool visible;
    public override bool Active{get{return visible;}}
    public override void Clear(){held.Clear();visible=false;released=-1;carry=0;}
    public double FillFraction{get{double sum=0;foreach(var v in water)sum+=v;return free==0?0:sum/free;}}
    float Rand(){return (float)random.NextDouble();}
    PointF Position(LayoutKey k){var p=Center(k.cap);return new PointF((p.X-tank.Left)/tank.Width,(p.Y-tank.Top)/tank.Height);}
    void Start(double time){
        tank=layout.keys[0].cap;foreach(var k in layout.keys)tank=RectangleF.Union(tank,k.cap);
        random=new Random(NextSeed());phase=Rand()*6.28f;free=steps=0;dx=0;dy=1;votesX=votesY=energy=0;
        for(int y=0;y<=H;y++)for(int x=0;x<=W;x++){int j=y*Stride+x;horizontal[j]=y;vertical[j]=-x;}
        Array.Clear(solid,0,N);Array.Clear(wake,0,wake.Length);
        wakeSteps=0;ChooseWaves();PrepareWaves();
        foreach(var k in layout.keys){
            var b=k.housing;float cx=(b.Left+b.Width*.5f-tank.Left)*W/tank.Width,cy=(b.Top+b.Height*.5f-tank.Top)*H/tank.Height;
            float rx=Math.Max(.7f,b.Width*.5f*W/tank.Width),ry=Math.Max(.7f,b.Height*.5f*H/tank.Height);
            int x0=Math.Max(0,(int)(cx-3.6f*rx)),x1=Math.Min(W,(int)Math.Ceiling(cx+3.6f*rx));
            int y0=Math.Max(0,(int)(cy-3.6f*ry)),y1=Math.Min(H,(int)Math.Ceiling(cy+3.6f*ry));
            for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++){
                float u=(x-cx)/rx,v=(y-cy)/ry,q=u*u+v*v;
                float influence=Math.Min(1,1/Math.Max(.001f,q))*Unit((12-q)/4);
                int j=y*Stride+x;horizontal[j]-=(y-cy)*influence;vertical[j]+=(x-cx)*influence;
                if(x<W&&y<H){
                    var stem=k.stem;
                    float px=tank.X+(x+.5f)*tank.Width/W,py=tank.Y+(y+.5f)*tank.Height/H;
                    if(stem.Contains(px,py))solid[y*W+x]=true;
                }
            }
        }
        for(int j=0;j<N;j++)if(!solid[j])free++;
        float remaining=free*.5f;
        for(int y=H-1;y>=0;y--)for(int x=0;x<W;x++){
            int j=y*W+x;float d=solid[j]?0:Math.Min(1,remaining);remaining-=d;water[j]=d;
            float cross=crossSin[x]*eddyCos[y]+crossCos[x]*eddySin[y];
            float band=Sin(x*swell.x+y*swell.y+swell.offset+2.7f*cross);
            pigment[j]=d*(.50f+.32f*band+.16f*cross);
        }
        visible=true;last=time;carry=0;released=-1;lastProbe=double.NegativeInfinity;hasEnvironment=false;
        Array.Clear(right,0,N);Array.Clear(down,0,N);
        float fines=.64f+.16f*Rand(),large=.941f+.035f*Rand(),stars=.25f+.43f*Rand();
        for(int i=0;i<flakes.Length;i++){
            // Mica starts in loose plumes with scattered pieces in the clearer oil.
            int cell;do{cell=random.Next(N);}while(water[cell]<.5f||Rand()>.28f+.72f*pigment[cell]);
            float rank=Rand(),size=rank<fines?1.5f+Rand()*2.6f:rank<large?3.5f+Rand()*4.8f:8+Rand()*6;
            int tint=random.Next(4),shape=rank>=large&&Rand()<stars?8:rank>=fines?4:0;
            flakes[i]=new Flake{x=cell%W+.14f+Rand()*.72f,y=cell/W+.14f+Rand()*.72f,size=size,
                tilt=Rand()*6.28f,roll=Rand()*6.28f,depth=Rand()*.90f,grip=.71f+Rand()*.58f,wet=1,cut=shape+tint,
                spin=(Rand()<.5f?-1:1)*(.58f+1.01f*Rand()),facet=Rand()*6.28f,
                glintWidth=.045f+.077f*Rand(),aspect=.63f+.57f*Rand()};
        }
        NearestLiquid();
        if(liquid==null)liquid=new Bitmap(W*3,H*3,PixelFormat.Format32bppPArgb);
    }
    // This local stream function changes tangential velocity. Solid faces still block the flow.
    void Stir(PointF position){
        wakeSteps=90;
        float cx=position.X*W+(Rand()-.5f)*4,cy=position.Y*H+(Rand()-.5f)*4;
        float rx=6+8*Rand(),ry=5+7*Rand(),turn=(Rand()<.5f?-1:1)*(1.8f+2.3f*Rand());
        for(int y=Math.Max(0,(int)(cy-ry));y<=Math.Min(H,(int)(cy+ry));y++){
            float v=(y-cy)/ry;
            for(int x=Math.Max(0,(int)(cx-rx));x<=Math.Min(W,(int)(cx+rx));x++){
                float u=(x-cx)/rx,weight=1-u*u-v*v;
                if(weight<=0)continue;
                int j=y*Stride+x;
                wake[j]=Math.Max(-5,Math.Min(5,wake[j]+turn*weight*weight));
            }
        }
    }
    public override void Strike(LayoutKey k){
        if(k==null||held.Contains(k))return;double t=clock();Advance(t);if(!visible)Start(t);
        var p=Position(k);Stir(p);votesX+=2*p.X-1;votesY+=2*p.Y-1;held.Add(k);released=-1;energy=Math.Min(1,energy+.38f);
    }
    public override void Release(LayoutKey k){if(!held.Contains(k))return;double t=clock();Advance(t);held.Remove(k);if(held.Count==0)released=t;}
    protected override void Step(){Advance(now);}
    protected override RectangleF SceneBounds(){return visible?tank:RectangleF.Empty;}
    void Advance(double t){
        if(!visible)return;
        if(released>=0&&t-released>=3200){Clear();return;}
        carry+=Math.Max(0,Math.Min(.10,(t-last)*.001));last=t;
        const float dt=1f/45;
        while(carry>=dt){Simulate(dt);carry-=dt;}
    }
    // A face transfer is limited by the donor volume and the receiver's free space.
    // The same transfer carries the pearlescent pigment, including around branches.
    void Transfer(int a,int b,float v){
        if(v<0){int tmp=a;a=b;b=tmp;v=-v;}
        float src=water[a],room=1-water[b];if(src<.0001f||room<.0001f)return;
        float amount=Math.Min(Math.Min(src,room),v*Math.Min(1,Math.Max(src,room)));
        float dye=pigment[a]*amount/src;
        water[a]-=amount;water[b]+=amount;pigment[a]-=dye;pigment[b]+=dye;
    }
    void Sweep(bool xAxis,bool forward){
        int start=forward?N-1:0,step=forward?-1:1;
        var faces=xAxis?right:down;int offset=xAxis?1:W;
        for(int i=0,j=start;i<N;i++,j+=step){float f=faces[j];if(f!=0)Transfer(j,j+offset,f);}
    }
    void Visit(int j,int d,ref int tail){
        if(!solid[j]&&distance[j]<0){distance[j]=d;queue[tail++]=j;}
    }
    // Transmit the tilt through full cells. At the receiving wall the fluid must
    // rise into available space instead of piling up in the old horizontal rows.
    void PressurePath(){
        int head=0,tail=0;
        for(int y=0;y<H;y++)for(int x=0;x<W;x++){
            int j=y*W+x;
            float depth=dx*((x+.5f)/W-.5f)+dy*((y+.5f)/H-.5f);
            distance[j]=-1;
            if(!solid[j]&&water[j]<.74f&&depth>.004f){distance[j]=0;queue[tail++]=j;}
        }
        while(head<tail){
            int j=queue[head++],x=j%W,d=distance[j]+1;
            if(x>0)Visit(j-1,d,ref tail);if(x<W-1)Visit(j+1,d,ref tail);
            if(j>=W)Visit(j-W,d,ref tail);if(j<N-W)Visit(j+W,d,ref tail);
        }
    }
    void AddWet(int next,ref int tail){
        if(outlet[next]==0){outlet[next]=1;queue[tail++]=next;}
    }
    void DisplaceFullColumns(){
        // Pressure redistributes a connected column's displaced volume over its
        // advancing shore. Each new part of that shore must touch existing water.
        for(int j=0;j<N;j++){
            bool front=distance[j]==0&&((j%W>0&&water[j-1]>.50f)||(j%W<W-1&&water[j+1]>.50f)||
                       (j>=W&&water[j-W]>.50f)||(j<N-W&&water[j+W]>.50f));
            outlet[j]=!solid[j]&&(water[j]>.015f||front)?0:-1;
        }
        for(int root=0;root<N;root++){
            if(outlet[root]!=0)continue;
            int head=0,tail=0;AddWet(root,ref tail);float room=0,supply=0,silt=0;
            while(head<tail){
                int j=queue[head++],x=j%W,y=j/W;
                float depth=dx*((x+.5f)/W-.5f)+dy*((y+.5f)/H-.5f);
                if(depth<-.014f){supply+=water[j];silt+=pigment[j];outlet[j]=2;}
                else if(distance[j]==0){room+=1-water[j];outlet[j]=3;}
                if(x>0)AddWet(j-1,ref tail);if(x<W-1)AddWet(j+1,ref tail);
                if(j>=W)AddWet(j-W,ref tail);if(j<N-W)AddWet(j+W,ref tail);
            }
            if(room<.01f||supply<.01f)continue;
            float displaced=Math.Min(room*.75f,supply*.070f),give=displaced/supply,take=displaced/room,dye=silt/supply;
            for(int i=0;i<tail;i++){
                int j=queue[i];
                if(outlet[j]==2){water[j]*=1-give;pigment[j]*=1-give;}
                else if(outlet[j]==3){float amount=(1-water[j])*take;water[j]+=amount;pigment[j]+=amount*dye;}
            }
        }
    }
    void Simulate(float dt){
        float decay=(float)Math.Exp(-dt/1.12);votesX*=decay;votesY*=decay;energy*=.974f;
        foreach(var k in held){var p=Position(k);votesX+=(2*p.X-1)*dt;votesY+=(2*p.Y-1)*dt;}
        float tx=2.7f*votesX,ty=.52f+2.1f*votesY,mag=(float)Math.Sqrt(tx*tx+ty*ty);
        tx/=Math.Max(.35f,mag);ty/=Math.Max(.35f,mag);
        dx+=(tx-dx)*.092f;dy+=(ty-dy)*.092f;phase+=dt*(2+energy);
        if(steps%3==0)PressurePath();
        float sx=dx*.76f,sy=dy*.57f;
        for(int y=0;y<H;y++)for(int x=0;x<W;x++){
            int j=y*W+x,c=y*Stride+x;
            if(x<W-1&&!solid[j]&&!solid[j+1]){
                int f=c+1;
                right[j]+=.24f*(Math.Max(-.96f,Math.Min(.96f,sx*(horizontal[f+Stride]-horizontal[f])+sy*(vertical[f+Stride]-vertical[f])+(wake[f+Stride]-wake[f])+.89f*(distance[j]-distance[j+1])+.075f*(water[j]-water[j+1])))-right[j]);
            }else right[j]=0;
            if(y<H-1&&!solid[j]&&!solid[j+W]){
                int f=c+Stride;
                down[j]+=.24f*(Math.Max(-.96f,Math.Min(.96f,sx*(horizontal[f]-horizontal[f+1])+sy*(vertical[f]-vertical[f+1])+(wake[f]-wake[f+1])+.89f*(distance[j]-distance[j+W])+.075f*(water[j]-water[j+W])))-down[j]);
            }else down[j]=0;
        }
        if(wakeSteps>0){
            if(--wakeSteps==0)Array.Clear(wake,0,wake.Length);
            else for(int i=0;i<wake.Length;i++)wake[i]*=.90f;
        }
        if((steps++&1)==0){Sweep(true,dx>0);Sweep(false,dy>0);}else{Sweep(false,dy>0);Sweep(true,dx>0);}
        DisplaceFullColumns();if(steps%3==0)NearestLiquid();MoveFlakes(dt);
    }
    void ShoreNeighbour(int next,int from,ref int tail){
        if(!solid[next]&&shore[next]<0){shore[next]=from;queue[tail++]=next;}
    }
    void NearestLiquid(){
        int head=0,tail=0;
        for(int j=0;j<N;j++){
            if(water[j]>.56f){shore[j]=j;queue[tail++]=j;}else shore[j]=-1;
        }
        while(head<tail){
            int j=queue[head++],x=j%W;
            if(x>0)ShoreNeighbour(j-1,j,ref tail);if(x<W-1)ShoreNeighbour(j+1,j,ref tail);
            if(j>=W)ShoreNeighbour(j-W,j,ref tail);if(j<N-W)ShoreNeighbour(j+W,j,ref tail);
        }
    }
    void MoveFlakes(float dt){
        int count=Budget.Count(MaxFlakes);Array.Clear(crowd,0,N);
        for(int i=0;i<count;i++){var f=flakes[i];crowd[(int)f.y*W+(int)f.x]++;}
        for(int i=0;i<count;i++){
            var f=flakes[i];int x=(int)f.x,y=(int)f.y,j=y*W+x;
            int l=x==0?j:j-1,r=x==W-1?j:j+1,t=y==0?j:j-W,b=y==H-1?j:j+W;
            float u=(f.x-x)*right[j]+(1-f.x+x)*right[l],v=(f.y-y)*down[j]+(1-f.y+y)*down[t];
            float curl=(down[r]-down[l]-right[b]+right[t])*.5f,speed=Math.Abs(u)+Math.Abs(v);
            float shear=Unit(Math.Abs(curl)*2.2f+energy*.7f+Math.Abs(u-f.vx)+Math.Abs(v-f.vy));
            f.depth=Unit(f.depth+dt*((.09f+f.size*.021f)*f.grip-.78f*shear));
            float drag=(.28f+.12f/f.grip)*(1-.62f*f.depth),carryBy=1-.74f*f.depth*f.depth;
            float du=(u*.87f+dx*(.012f+.006f*f.size))*carryBy;
            float dv=(v*.87f+dy*(.012f+.006f*f.size))*carryBy;
            if(water[j]<.56f&&shore[j]>=0&&shore[j]!=j){
                int to=shore[j];du+=(to%W-x)*1.05f;dv+=(to/W-y)*1.05f;
            }
            du+=Math.Max(-.32f,Math.Min(.32f,(crowd[l]-crowd[r])*.065f));
            dv+=Math.Max(-.32f,Math.Min(.32f,(crowd[t]-crowd[b])*.065f));
            f.vx+=(du-f.vx)*drag;f.vy+=(dv-f.vy)*drag;
            float nx=Math.Max(.04f,Math.Min(W-.04f,f.x+f.vx)),ny=Math.Max(.04f,Math.Min(H-.04f,f.y+f.vy));
            if(!solid[y*W+(int)nx])f.x=nx;else f.vx*=-.18f;
            if(!solid[(int)ny*W+(int)f.x])f.y=ny;else f.vy*=-.18f;
            f.tilt+=dt*(3.2f*curl+(.18f+speed)*f.spin*(.7f+f.grip)*(1-.72f*f.depth));
            f.roll+=dt*(curl*2.9f+.3f*(u-v)*(1-f.depth));
            f.wet+=.23f*(Unit(water[(int)f.y*W+(int)f.x]*3)-f.wet);flakes[i]=f;
        }
    }
    static int Channel(float value){return (int)Math.Max(0,Math.Min(255,value));}
    static uint Blend(uint a,uint b,uint t){
        const uint mask=0x00ff00ff;uint q=256-t;
        return (((a&mask)*q+(b&mask)*t)>>8&mask)|((((a>>8&mask)*q+(b>>8&mask)*t)>>8&mask)<<8);
    }
    // Reconstruct a thin oil meniscus from thickness and its slope before shading.
    // Keep the bulk transparent; the narrow lip and the flake normals catch the light.
    static uint[] BuildOptics(){
        var lut=new uint[256*32*8];
        for(int h=0;h<256;h++)for(int n=0;n<32;n++)for(int tint=0;tint<8;tint++){
            float d=h/255f,facing=n/31f*2-1,p=tint/7f;
            float wet=Smooth((d-.23f)/.12f);
            float lip=Unit(1-Math.Abs(d-.37f)*6.5f);lip*=lip;
            float bevel=lip*(.16f+.84f*Unit(facing*2.5f));
            float shadow=lip*(.35f+.65f*Unit(-facing*2));
            float rim=Unit(1-Math.Abs(d-.48f)*7)*Math.Max(0,-facing)*.6f;
            int a=Channel(wet*(27+22*d+13*p+107*bevel+62*shadow+35*rim));
            int r=Channel(59+59*p+148*bevel-37*shadow+54*rim);
            int g=Channel(99+24*p+129*bevel-49*shadow+55*rim);
            int b=Channel(147+34*p+78*bevel-47*shadow+24*rim);
            lut[(h*32+n)*8+tint]=(uint)((a<<24)|(r*a/255<<16)|(g*a/255<<8)|(b*a/255));
        }
        return lut;
    }
    static int LightPixel(uint field,uint sheen,uint[] table,int fade){
        int i=(int)(field>>24)*256+(int)(field>>16&255)/8*8+(int)(field>>8&255)/32;
        uint c=table[i];if(c==0)return 0;
        uint coverage=Math.Min(256u,(field>>24)*2);
        uint top=coverage==256?sheen:Blend(0,sheen,coverage);
        c=top+Blend(0,c,256-(top>>24));
        return fade>=256?(int)c:(int)Blend(0,c,(uint)fade);
    }
    static void Blur(float[] src,float[] dst){
        for(int y=0;y<H;y++)for(int x=0;x<W;x++){
            int j=y*W+x,l=x==0?j:j-1,r=x==W-1?j:j+1,t=y==0?j:j-W,b=y==H-1?j:j+W;
            dst[j]=(src[j]*4+src[l]+src[r]+src[t]+src[b])*.125f;
        }
    }
    void Shade(float fade){
        Blur(water,filtered);Blur(filtered,density);
        var table=optics.Value;int opacity=(int)(256*fade);
        PrepareWaves();
        for(int y=0;y<H;y++)for(int x=0;x<W;x++){
            int j=y*W+x;float d=density[j];
            if(d<=0){colorsLow[j]=0;reflections[j]=0;continue;}
            float west=x==0?d:density[j-1],north=y==0?d:density[j-W];
            float east=x==W-1?d:density[j+1],south=y==H-1?d:density[j+W];
            float slope=(east-west)*.63f+(south-north)*.77f;
            float dye=water[j]>.015f?Unit(pigment[j]/water[j]):.55f;
            colorsLow[j]=(uint)(Channel(d*255)<<24|Channel(127+205*slope)<<16|Channel(dye*255)<<8);
            reflections[j]=d<.13f?0:SurfaceReflection(x,y,j,east-west,south-north,dye);
        }
        // Interpolate the small simulation texture once. GDI+ bilinear scaling a
        // transparent board-size layer is expensive on the desktop compositor.
        for(int j=0;j<N;j++){
            uint c=colorsLow[j],next=colorsLow[j%W==W-1?j:j+1];int k=j*3;
            if((c|next)==0){rows[k]=rows[k+1]=rows[k+2]=0;reflectedRows[k]=reflectedRows[k+1]=reflectedRows[k+2]=0;continue;}
            rows[k]=c;rows[k+1]=Blend(c,next,85);rows[k+2]=Blend(c,next,171);
            uint light=reflections[j],other=reflections[j%W==W-1?j:j+1];
            reflectedRows[k]=light;reflectedRows[k+1]=Blend(light,other,85);reflectedRows[k+2]=Blend(light,other,171);
        }
        int width=W*3;
        for(int y=0;y<H;y++)for(int x=0;x<width;x++){
            int k=y*width+x,dst=y*3*width+x;uint c=rows[k],next=rows[y==H-1?k:k+width];
            if((c|next)<0x34000000){pixels[dst]=pixels[dst+width]=pixels[dst+2*width]=0;continue;}
            uint light=reflectedRows[k],other=reflectedRows[y==H-1?k:k+width];
            pixels[dst]=LightPixel(c,light,table,opacity);
            // The bulk has no sharp meniscus. Interpolate two shaded samples there.
            if((c>>24)>180&&(next>>24)>180){
                uint top=(uint)pixels[dst],bottom=(uint)LightPixel(next,other,table,opacity);
                pixels[dst+width]=(int)Blend(top,bottom,85);pixels[dst+width*2]=(int)Blend(top,bottom,171);continue;
            }
            pixels[dst+width]=LightPixel(Blend(c,next,85),Blend(light,other,85),table,opacity);
            pixels[dst+width*2]=LightPixel(Blend(c,next,171),Blend(light,other,171),table,opacity);
        }
        var bits=liquid.LockBits(new Rectangle(0,0,W*3,H*3),ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
        try{System.Runtime.InteropServices.Marshal.Copy(pixels,0,bits.Scan0,pixels.Length);}finally{liquid.UnlockBits(bits);}
    }
    protected override void Paint(Graphics g,int w,int h,Bitmap image,Rectangle sample){
        float fade=released>=0?1-Smooth((now-released-2600)/600):1;
        ReadEnvironment(w,h,image,sample);
        Shade(fade);var state=g.Save();g.SetClip(tank,CombineMode.Intersect);g.SmoothingMode=SmoothingMode.None;g.PixelOffsetMode=PixelOffsetMode.Half;g.InterpolationMode=InterpolationMode.NearestNeighbor;
        g.DrawImage(liquid,tank,new RectangleF(0,0,W*3,H*3),GraphicsUnit.Pixel);
        BeginLayer(ref layer,.68f);var chips=foils.Value;var silts=mica.Value;
        int count=Budget.Count(MaxFlakes);
        for(int i=0;i<count;i++){
            var p=flakes[i];if(p.wet<.03f)continue;
            float x=tank.Left+p.x*tank.Width/W,y=tank.Top+p.y*tank.Height/H;
            float normal=Cos(p.tilt+.37f*Sin(p.roll)),face=Math.Abs(normal);
            float spec=Unit((normal*.83f+.17f*Cos(p.roll-.65f)-.68f)*3.25f);spec*=spec;
            float opacity=fade*p.wet*(1-.28f*p.depth);
            if(i%35==0){
                float a=(float)Math.Atan2(p.vy,p.vx);
                layer.Stamp(silts[p.cut%4],x,y,25+31*p.grip,8+9*p.depth,a,(int)(opacity*92));
            }
            float diffraction=.5f+.5f*Sin(p.tilt*.9f+p.roll+.38f*dx-.30f*dy+p.cut*.93f);
            int hue=((int)(diffraction*(Spectrum.Length-1))+filmHue)%Spectrum.Length;
            var chip=chips[(p.cut/4)*Spectrum.Length+hue,spec>.51f?2:face>.39f?1:0];
            float width=p.size*(p.size>7?1.18f:1f);
            layer.Stamp(chip,x,y,width,width*p.aspect*(.15f+.84f*face),p.roll,(int)(opacity*(132+52*face+70*spec)));
            if(p.size>5f){
                // A facet crosses a narrow lobe as it rolls through the reflected strip light.
                float alignment=Cos(p.tilt+.35f*dx-.24f*dy+.42f*Sin(p.roll+p.x*.027f+p.facet));
                float flash=Smooth((alignment-1+p.glintWidth)/p.glintWidth);
                flash*=.76f+.24f*Cos(p.roll+p.cut);
                if(flash>.07f){
                    float span=(8+p.size*(p.size>8?4.9f:3.2f))*(.42f+.58f*flash);
                    layer.Stamp(flares.Value[hue],x,y,span,span*p.aspect,.35f*Sin(p.facet)+.18f*Sin(p.roll),(int)(opacity*flash*255));
                }
            }
        }
        layer.Present(g);g.Restore(state);
    }
    public void Dispose(){Clear();if(layer!=null)layer.Dispose();if(liquid!=null)liquid.Dispose();}
}
