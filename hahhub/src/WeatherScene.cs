using System;
using System.Drawing;

// Weather uses calibrated keyboard pixels and paints a transparent layer over either source.
internal sealed class WeatherScene : SkillScene, IDisposable {
    public const string FogId="hahhub-frost-fog", SnowId="hahhub-snowdrift";
    readonly bool snow;
    readonly Random random;
    readonly Grain[] particles;
    int count, replace;
    double lastStep=-1;
    ParticleSurface layer;
    public WeatherTuning tuning=new WeatherTuning();

    struct Grain {
        public LayoutKey key;
        public float x,y,vx,vy,windX,windY,side,size,phase,life,age,releaseLimit;
        public int variant;
        public bool settle,resting,aloft;
    }
    public WeatherScene(GlassData layout,bool snow,Func<double> time=null):base(layout,time){
        this.snow=snow;random=new Random(NextSeed());particles=new Grain[snow?750:128];
    }
    public override bool Active {get{return contacts.Count!=0||count!=0;}}
    protected override double Tail {get{return tuning.clearMs;}}
    float Range(float a,float b){return a+(b-a)*(float)random.NextDouble();}
    static void Direction(Contact p,out float x,out float y){
        x=p.origin.X-p.from.X;y=p.origin.Y-p.from.Y;
        float length=(float)Math.Sqrt(x*x+y*y);
        if(length>1){x/=length;y/=length;}
        else{float angle=(Noise(p.seed,35)-.51f)*1.02f;x=Cos(angle);y=Sin(angle);}
    }
    static int MistLanes(float span,float pitch){
        return Math.Min(16,Math.Max(2,(int)Math.Ceiling(span/(pitch*.55f))));
    }
    void Emit(Contact p,float dx,float dy,int sequence){
        float spread=tuning.spread*.01f;
        var cap=p.key.cap;var center=Center(cap);
        var g=new Grain{releaseLimit=float.PositiveInfinity,key=p.key,windX=dx,windY=dy,side=random.Next(2)==0?-1:1,phase=Range(0,6.283f),variant=random.Next(4)};
        if(snow){
            float half=(Math.Abs(dx)*cap.Width+Math.Abs(dy)*cap.Height)*.5f;
            float across=Range(-.50f,.50f)*(Math.Abs(dy)*cap.Width+Math.Abs(dx)*cap.Height);
            g.aloft=random.NextDouble()<.34;
            float upwind=half+Range(8,g.aloft?72:42)*spread;
            g.x=center.X-dx*upwind-dy*across;g.y=center.Y-dy*upwind+dx*across;
            g.side=across>=0?1:-1;g.size=Range(1.4f,5.2f);
            float speed=Range(64,148)*spread;g.vx=dx*speed;g.vy=dy*speed;
            g.life=Range(.9f,1.7f)*tuning.clearMs*.001f;g.settle=!g.aloft&&random.NextDouble()<.85;
        }else{
            // Visit every patch of the calibrated face before revisiting a patch.
            // Interleave distant columns so a long key starts and keeps venting at both ends.
            float pitch=Math.Min(cap.Width,cap.Height);
            int columns=MistLanes(cap.Width,pitch),rows=MistLanes(cap.Height,pitch);
            int slot=sequence%columns,cycle=sequence/(columns*rows);
            int column=((slot+1)/2+(slot%2)*((columns-1)/2)+p.seed%columns+cycle)%columns;
            int row=(sequence/columns+column+p.seed%rows)%rows;
            float u=(column+Range(.08f,.92f))/columns,v=(row+Range(.08f,.92f))/rows;
            g.x=cap.Left+cap.Width*u;g.y=cap.Top+cap.Height*v;
            float edgeX=Math.Min(u,1-u)*cap.Width,edgeY=Math.Min(v,1-v)*cap.Height;
            float horizontal=Unit(.5f+(edgeY-edgeX)/(pitch*.60f));
            float radial=(float)Math.Atan2((v<.5f?-1:1)*(1-horizontal),(u<.5f?-1:1)*horizontal)+Range(-.48f,.48f);
            float swirl=(Noise(p.seed,18)-.5f)*1.6f;
            g.windX=Cos(radial+swirl)*.88f+dx*.23f;g.windY=Sin(radial+swirl)*.88f+dy*.23f;
            float speed=Range(38,103)*spread;
            g.vx=Cos(radial)*speed;g.vy=Sin(radial)*speed;
            g.aloft=true;g.variant=random.Next(WeatherSprites.Mist.Length);
            g.size=Range(35,60)+Range(0,1)*Range(0,47);
            g.life=Range(1.15f,1.95f)*(.77f+.23f*tuning.clearMs*.001f);

        }
        int capCount=Budget.Count(particles.Length);
        if(count<capCount)particles[count++]=g;
        else{replace%=capCount;particles[replace++]=g;}
    }
    void Gust(Contact p,float dx,float dy){
        float radius=160*tuning.spread*.01f;
        for(int i=0;i<count;i++){
            var g=particles[i];
            var near=KeycapRim.Closest(p.key.cap,new PointF(g.x,g.y));
            float x=g.x-near.X,y=g.y-near.Y;
            float distance=(float)Math.Sqrt(x*x+y*y);
            if(distance>=radius)continue;
            float power=(1-distance/radius)*(snow?155:105);
            float inv=1/Math.Max(7,distance);
            g.vx+=(dx*.70f+x*inv*.9f)*power;g.vy+=(dy*.70f+y*inv*.9f)*power;
            g.windX=dx;g.windY=dy;
            if(g.resting){g.resting=false;g.settle=false;g.life=Math.Min(g.releaseLimit,Math.Max(g.life,g.age+.34f));}
            particles[i]=g;
        }
    }
    public override void Strike(LayoutKey key){
        if(!BeginContact(key))return;
        now=clock();Step();
        var p=contacts[contacts.Count-1];float dx,dy;Direction(p,out dx,out dy);
        Gust(p,dx,dy);
        int burst=Math.Max(3,(snow?32:17)*tuning.density/100);
        if(!snow){
            var cap=key.cap;float pitch=Math.Min(cap.Width,cap.Height);
            burst=Math.Max(burst,MistLanes(cap.Width,pitch)*MistLanes(cap.Height,pitch));
        }
        for(int i=0;i<burst;i++)Emit(p,dx,dy,i);
    }
    public override void Release(LayoutKey key){
        if(!contacts.Exists(p=>p.key==key&&p.released<0))return;
        now=clock();Step();base.Release(key);
        float tail=tuning.clearMs*.001f;
        for(int i=0;i<count;i++)if(particles[i].key==key){
            particles[i].releaseLimit=Math.Min(particles[i].releaseLimit,particles[i].age+tail*(snow?1:.70f));
            particles[i].life=Math.Min(particles[i].life,particles[i].releaseLimit);
        }
    }
    public override void Clear(){base.Clear();count=replace=0;lastStep=-1;}
    protected override void Step(){
        count=Math.Min(count,Budget.Count(particles.Length));
        double previous=lastStep<0?now:lastStep;lastStep=now;
        float seconds=(float)Math.Max(0,(now-previous)*.001);
        float dt=Math.Min(.08f,seconds), spread=tuning.spread*.01f;
        for(int i=count-1;i>=0;i--){
            var g=particles[i];g.age+=seconds;
            if(g.age>=g.life){particles[i]=particles[--count];continue;}
            if(!g.resting&&dt>0){
                float speed=(snow?(g.aloft?149:99):103)*spread;
                float eddy=Sin(g.phase+g.age*(snow?3.7f:2.1f))*(snow?27:48);
                float drag=dt*(snow?1.3f:.86f);
                g.vx+=(g.windX*speed-g.windY*eddy-g.vx)*drag;
                g.vy+=(g.windY*speed+g.windX*eddy-g.vy)*drag;
                var cap=g.key.cap;var center=Center(cap);
                float nx=g.x+g.vx*dt,ny=g.y+g.vy*dt;
                if(!g.aloft&&cap.Contains(nx,ny)){
                    // Split the flow around the keycap. A grain keeps its chosen side through a collision.
                    float flank=(snow?90:34)*spread*g.side;
                    nx=g.x-g.windY*flank*dt;ny=g.y+g.windX*flank*dt;
                    if(cap.Contains(nx,ny)){
                        if(Math.Abs(g.windX)>=Math.Abs(g.windY))nx=g.windX>=0?cap.Left-.3f:cap.Right+.3f;
                        else ny=g.windY>=0?cap.Top-.3f:cap.Bottom+.3f;
                    }
                }
                g.x=nx;g.y=ny;
                float downstream=(nx-center.X)*g.windX+(ny-center.Y)*g.windY;
                float lee=(Math.Abs(g.windX)*cap.Width+Math.Abs(g.windY)*cap.Height)*.5f;
                if(g.settle&&downstream>lee){
                    // Behind the keycap the split streams lose speed and curl into the sheltered wake.
                    float transverse=(nx-center.X)*(-g.windY)+(ny-center.Y)*g.windX;
                    g.vx+=g.windY*transverse*dt*5;g.vy-=g.windX*transverse*dt*5;
                    if(downstream>lee+3+g.variant*4+g.size*1.8f){
                        g.resting=true;g.vx=g.vy=0;
                        g.life=Math.Min(g.releaseLimit,Math.Max(g.life,g.age+tuning.clearMs*.001f*(.35f+.12f*g.variant)));
                    }
                }
            }
            particles[i]=g;
        }
        base.Step();
        int held=0;foreach(var p in contacts)if(p.released<0)held++;
        double rate=(snow?82:29)*tuning.density*.01/Math.Max(1,held*.36);
        foreach(var p in contacts){
            if(p.released>=0)continue;
            double start=Math.Max(p.birth,now-dt*1000);
            int first=(int)Math.Floor((start-p.birth)*rate*.001);
            int end=(int)Math.Floor((now-p.birth)*rate*.001);
            float dx,dy;Direction(p,out dx,out dy);
            for(int i=first;i<end;i++)Emit(p,dx,dy,i);
        }
    }
    float Radius(Grain g){
        if(snow)return g.resting?g.size*2.5f+3:Math.Max(g.size*1.6f+3,.04f*Math.Max(Math.Abs(g.vx),Math.Abs(g.vy))+3);
        return g.size*(1.12f+1.34f*Math.Min(2,g.age))*.67f+3;
    }
    protected override RectangleF SceneBounds(){
        RectangleF area=RectangleF.Empty;
        foreach(var p in contacts){var cap=p.key.cap;cap.Inflate(38,38);area=Union(area,cap);}
        for(int i=0;i<count;i++){
            var g=particles[i];float r=Radius(g);area=Union(area,new RectangleF(g.x-r,g.y-r,2*r,2*r));
        }
        return area;
    }
    void DrawSurfaceMist(Contact p){
        var cap=p.key.cap;
        float pitch=Math.Min(cap.Width,cap.Height);
        int columns=Math.Min(12,(int)Math.Ceiling(cap.Width/pitch));
        int rows=Math.Min(12,(int)Math.Ceiling(cap.Height/pitch));
        float cellW=cap.Width/columns,cellH=cap.Height/rows;
        float age=(float)(now-p.birth)*.001f;
        float fade=p.released<0?1:1-Smooth((now-p.released)/Math.Min(440,tuning.clearMs));
        if(fade<=0)return;
        // Overlapping, slowly turning low clouds anchor the moving wisps to the whole cap.
        for(int row=0;row<rows;row++)for(int col=0;col<columns;col++){
            for(int side=0;side<2;side++){
                int salt=23+(row*columns+col)*13+side*6;
                float phase=Noise(p.seed,salt)*6.28f;
                float drift=Sin(age*.93f+phase),roll=Cos(age*.74f+phase);
                float x=cap.X+(col+.41f+.20f*side+.13f*drift)*cellW;
                float y=cap.Y+(row+.33f+.30f*side+.11f*roll)*cellH;
                int tile=Math.Min(WeatherSprites.Mist.Length-1,(int)(Noise(p.seed,salt+1)*WeatherSprites.Mist.Length));
                float opacity=(.35f+.65f*Smooth(age/.18f))*(.79f+.21f*roll)*fade;
                layer.Stamp(WeatherSprites.Mist[tile],x,y,cellW*(1.22f+.14f*roll),cellH*(1.02f+.16f*drift),
                    .24f*drift,(int)(opacity*(75+.52f*tuning.density)));
            }
        }
    }
    void DrawFrost(Contact p){
        float growth=Unit(((p.released<0?now:p.released)-p.birth)/1550);
        float fade=Fade(p);if(growth<=.015f||fade<.01f)return;
        var cap=p.key.cap;
        float pitch=Math.Min(cap.Width,cap.Height);
        var atlas=WeatherSprites.Frost;
        int count=Math.Min(96,(int)((18+12*Noise(p.seed,11))*cap.Width/pitch*tuning.density*.01f)+3);
        for(int i=0;i<count;i++){
            int salt=39+i*9;
            // A few broad ice plates, several feathered rosettes and many small grains.
            float r=Noise(p.seed,salt),size=i==0?.78f:i<4?.36f+.28f*r:.042f+.38f*r*r*r;
            float t=Smooth((growth-(i==0?0:.65f*Noise(p.seed,salt+1)))/(.25f+.55f*size));
            if(t<.01f)continue;
            float diameter=pitch*size;
            float u=Noise(p.seed,salt+2),v=Noise(p.seed,salt+3);
            float x=cap.Left+diameter*.5f+(cap.Width-diameter)*u,y=cap.Top+diameter*.5f+(cap.Height-diameter)*v;
            float aspect=.52f+.45f*Noise(p.seed,salt+4),rot=6.28f*Noise(p.seed,salt+5);
            int variant=(int)(Noise(p.seed,salt+6)*(atlas.Length-.001f));
            float span=diameter*(float)Math.Sqrt(t);
            layer.Stamp(atlas[variant],x,y,span,span*aspect,rot,(int)(fade*(.50f+.50f*t)*248));
        }
    }
    protected override void Paint(Graphics g,int w,int h,Bitmap image,Rectangle sample){
        BeginLayer(ref layer,snow?.84f:.55f);
        if(!snow)foreach(var p in contacts)DrawSurfaceMist(p);
        for(int i=0;i<count;i++){
            var m=particles[i];
            float remain=Unit((m.life-m.age)/Math.Max(.12f,m.life*.42f));
            if(snow){
                var pos=new PointF(m.x,m.y);
                if(!m.resting&&(m.size<3.6f||m.aloft))layer.Stroke(pos,At(pos,-m.vx*.038f,-m.vy*.038f),0xb8dcf4,.72f*remain,1.6f);
                if(m.resting){
                    float bank=m.size*(2.6f+.42f*m.variant);
                    layer.Stamp(WeatherSprites.Powder,m.x,m.y,bank,bank*.53f,m.phase*.23f,(int)(remain*234));
                }
                float size=m.resting?m.size*1.16f:m.size*2.25f;
                layer.Stamp(WeatherSprites.Flakes[m.variant],m.x,m.y,size,size,m.phase+(m.resting?0:m.age*1.7f),(int)(remain*249));
            }else{
                float width=m.size*(1.12f+1.34f*Math.Min(2,m.age));
                float angle=(float)Math.Atan2(m.vy,m.vx)+Sin(m.age*1.1f+m.phase)*.26f;
                float height=width*(.67f+.09f*Cos(m.phase+m.age));
                layer.Stamp(WeatherSprites.Mist[m.variant],m.x,m.y,width,height,angle,(int)(remain*(.46f+.54f*Smooth(m.age/.25f))*151));
            }
        }
        if(!snow)foreach(var p in contacts)DrawFrost(p);
        layer.Present(g);
    }
    public void Dispose(){Clear();if(layer!=null){layer.Dispose();layer=null;}}
}
internal sealed class WeatherTuning {
    public int density=100,spread=100,clearMs=1800;
    public void Clamp(){density=Math.Max(30,Math.Min(160,density));spread=Math.Max(50,Math.Min(180,spread));clearMs=Math.Max(400,Math.Min(4500,clearMs));}
}
