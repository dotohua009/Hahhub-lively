using System;
using System.Drawing;
using System.Drawing.Imaging;

internal sealed partial class GlitterFlow {
    // Continuous foil hues are baked once. White is reserved for the centre of a glint.
    static readonly int[] Spectrum=BuildSpectrum();
    static readonly Lazy<ParticleSprite[]> flares=new Lazy<ParticleSprite[]>(BuildFlares);
    readonly uint[] environment=new uint[N],reflections=new uint[N],reflectedRows=new uint[N*3];
    readonly float[] longSin=new float[W],longCos=new float[W],crossSin=new float[W],crossCos=new float[W];
    readonly float[] rowSin=new float[H],rowCos=new float[H],eddySin=new float[H],eddyCos=new float[H];
    struct Wave {
        public float x,y,speed,offset;
        public Wave(float angle,float frequency,float speed,float offset){
            x=Cos(angle)*frequency;y=Sin(angle)*frequency;
            this.speed=speed;this.offset=offset;
        }
    }
    Wave swell,ripple;
    int filmHue;
    void ChooseWaves(){
        float angle=Rand()*6.2832f;
        swell=new Wave(angle,.057f+.060f*Rand(),.28f+.32f*Rand(),6.2832f*Rand());
        ripple=new Wave(angle+1.05f+1.02f*Rand(),.105f+.090f*Rand(),-.24f-.32f*Rand(),6.2832f*Rand());
        filmHue=random.Next(Spectrum.Length);
    }
    const double ReflectionInterval=1000.0/12;
    double lastProbe=double.NegativeInfinity;
    bool hasEnvironment;

    static int[] BuildSpectrum(){
        int[] stops={0x73eaff,0x8786ff,0xde78ff,0xff85b2,0xffcc69,0xbeefa7,0x73eaff};
        var hues=new int[(stops.Length-1)*4];
        for(int i=0;i<hues.Length;i++)hues[i]=Mix(stops[i/4],stops[i/4+1],(i%4)*.25f);
        return hues;
    }
    static uint Premultiply(int rgb,int a){
        return (uint)(a<<24|((rgb>>16&255)*a/255)<<16|((rgb>>8&255)*a/255)<<8|(rgb&255)*a/255);
    }
    static ParticleSprite[] BuildFlares(){
        var sprites=new ParticleSprite[Spectrum.Length];
        for(int i=0;i<sprites.Length;i++){
            var star=new ParticleSprite(49,49);
            for(int y=0;y<star.h;y++)for(int x=0;x<star.w;x++){
                float u=(x-24)/24f,v=(y-24)/24f;
                double r2=u*u+v*v;
                double core=Math.Exp(-r2*240);
                double rays=Math.Exp(-Math.Abs(v)*32-Math.Abs(u)*2.0)+Math.Exp(-Math.Abs(u)*34-Math.Abs(v)*2.7);
                double halo=Math.Exp(-r2*12)*.21;
                float a=Unit((core+rays*.85+halo)*Unit((1-r2)*3));
                int tip=(i+(u+v<0?3:0))%Spectrum.Length;
                star.data[y*star.w+x]=Premultiply(Mix(Spectrum[tip],0xffffff,Unit(core+rays*.62)),Channel(a*255));
            }
            sprites[i]=star;
        }
        return sprites;
    }

    // The host can reuse the small environment map between samples while the
    // liquid and its highlights continue to animate at the selected render rate.
    public override Rectangle SampleArea(int w,int h){
        return visible&&now-lastProbe>=ReflectionInterval?Pixels(tank,w,h):Rectangle.Empty;
    }
    unsafe void ReadEnvironment(int w,int h,Bitmap image,Rectangle region){
        if(now-lastProbe<ReflectionInterval)return;
        if(region.IsEmpty){hasEnvironment=false;return;}
        lastProbe=now;hasEnvironment=false;
        if(image==null)return;
        int sw=Math.Min(region.Width,image.Width),sh=Math.Min(region.Height,image.Height);
        if(sw<1||sh<1)return;
        PixelFormat format=image.PixelFormat;
        if(format!=PixelFormat.Format32bppArgb&&format!=PixelFormat.Format32bppPArgb&&format!=PixelFormat.Format32bppRgb)
            format=PixelFormat.Format32bppArgb;
        var bits=image.LockBits(new Rectangle(0,0,sw,sh),ImageLockMode.ReadOnly,format);
        try{
            float stepX=tank.Width*w/(1920*W),stepY=tank.Height*h/(720*H);
            float startX=tank.Left*w/1920f-region.X,startY=tank.Top*h/720f-region.Y;
            for(int y=0;y<H;y++){
                int y0=Math.Max(0,Math.Min(sh-1,(int)(startY+(y+.25f)*stepY)));
                int y1=Math.Max(0,Math.Min(sh-1,(int)(startY+(y+.75f)*stepY)));
                var top=(uint*)((byte*)bits.Scan0+y0*bits.Stride);
                var bottom=(uint*)((byte*)bits.Scan0+y1*bits.Stride);
                for(int x=0;x<W;x++){
                    int x0=Math.Max(0,Math.Min(sw-1,(int)(startX+(x+.25f)*stepX)));
                    int x1=Math.Max(0,Math.Min(sw-1,(int)(startX+(x+.75f)*stepX)));
                    environment[y*W+x]=Blend(Blend(top[x0],top[x1],128),Blend(bottom[x0],bottom[x1],128),128)&0xffffff;
                }
            }
            hasEnvironment=true;
        }finally{image.UnlockBits(bits);}
    }
    void PrepareWaves(){
        // Oblique waves of different wavelengths catch different sections of one broad light.
        for(int x=0;x<W;x++){
            double a=x*swell.x+phase*swell.speed+swell.offset,b=x*ripple.x+phase*ripple.speed+ripple.offset;
            longSin[x]=Sin(a);longCos[x]=Cos(a);crossSin[x]=Sin(b);crossCos[x]=Cos(b);
        }
        for(int y=0;y<H;y++){
            double a=y*swell.y,b=y*ripple.y;
            rowSin[y]=Sin(a);rowCos[y]=Cos(a);eddySin[y]=Sin(b);eddyCos[y]=Cos(b);
        }
    }
    uint SurfaceReflection(int x,int y,int j,float slopeX,float slopeY,float dye){
        float crest=longSin[x]*rowCos[y]+longCos[x]*rowSin[y];
        float cross=crossSin[x]*eddyCos[y]+crossCos[x]*eddySin[y];
        float nx=.62f*slopeX+.24f*crest+.11f*cross+.12f*right[j];
        float ny=.74f*slopeY+.19f*crest-.15f*cross+.11f*down[j];
        float facing=nx*.68f+ny*.73f+.10f*dx+.065f*dy;
        float ribbon=Unit(1-Math.Abs(facing-.19f)*6.3f);
        ribbon=ribbon*ribbon*(.55f+.45f*dye);
        float silver=Unit(1-Math.Abs(facing-.24f)*17);
        silver=silver*silver*(.54f+.46f*dye);
        int hue=(int)((1.33f+.76f*nx-.81f*ny+.47f*dye+.12f*cross)*Spectrum.Length);
        int foil=Spectrum[(hue+Spectrum.Length+filmHue)%Spectrum.Length];
        int tint=Mix(foil,0xf3fcff,.09f+.65f*silver);
        int strength=Channel(ribbon*88+silver*52);
        if(hasEnvironment){
            // Reflect neighbouring scene detail across the perturbed normal. The
            // scaled capture contains only source wallpaper/video, never the overlay.
            int sx=Math.Max(0,Math.Min(W-1,x+(int)(nx*22+dx*2)));
            int sy=Math.Max(0,Math.Min(H-1,y-(int)(ny*19+dy*2)));
            uint scene=environment[sy*W+sx];
            int luma=(int)(((scene>>16)*54+((scene>>8)&255)*183+(scene&255)*19)>>8);
            int weight=13+(int)(31*Unit(Math.Abs(facing)*2.2f))+luma/9;
            int weightSum=weight+strength;
            tint=Mix((int)scene,tint,strength/(float)weightSum);
            strength=weightSum;
        }
        return Premultiply(tint,strength);
    }
}
