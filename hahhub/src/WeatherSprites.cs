using System;

// Precompute translucent, premultiplied sprites once per process, on first use.
internal static class WeatherSprites {
    static readonly Lazy<ParticleSprite[]> mist=new Lazy<ParticleSprite[]>(BuildMist);
    static readonly Lazy<ParticleSprite[]> frost=new Lazy<ParticleSprite[]>(BuildFrost);
    internal static ParticleSprite[] Frost {get{return frost.Value;}}
    static readonly Lazy<ParticleSprite[]> flakes=new Lazy<ParticleSprite[]>(BuildFlakes);
    static readonly Lazy<ParticleSprite> powder=new Lazy<ParticleSprite>(BuildPowder);
    internal static ParticleSprite Powder {get{return powder.Value;}}
    internal static ParticleSprite[] Mist {get{return mist.Value;}}
    internal static ParticleSprite[] Flakes {get{return flakes.Value;}}
    static float Hash(int x,int y,int seed){
        unchecked{uint n=(uint)(x*1597334677^y*381201581^seed*977);n=(n^(n>>16))*0x7feb352d;n^=n>>15;return (n&65535)/65535f;}
    }
    static float Noise(float x,float y,int seed){
        int i=(int)Math.Floor(x),j=(int)Math.Floor(y);float a=x-i,b=y-j;
        a=a*a*(3-2*a);b=b*b*(3-2*b);
        float v=Hash(i,j,seed),w=Hash(i,j+1,seed);
        return (v+(Hash(i+1,j,seed)-v)*a)*(1-b)+(w+(Hash(i+1,j+1,seed)-w)*a)*b;
    }
    static uint Premultiply(float opacity,int r,int g,int b){
        uint a=(uint)(255*Math.Max(0,Math.Min(1,opacity)));
        return a<<24|((uint)r*a/255)<<16|((uint)g*a/255)<<8|(uint)b*a/255;
    }
    static ParticleSprite[] BuildMist(){
        var variants=new ParticleSprite[12];
        for(int s=0;s<variants.Length;s++){
            var sprite=new ParticleSprite(58,58);variants[s]=sprite;
            for(int y=0;y<sprite.h;y++)for(int x=0;x<sprite.w;x++){
                float u=(x+.5f)/sprite.w,v=(y+.5f)/sprite.h,dx=2*u-1,dy=2*v-1;
                float rim=Math.Max(0,1-dx*dx-dy*dy);
                float warp=Noise(u*3,v*3,s);
                float waves=Noise(5*u+warp*1.7f,4*v-warp*2,s+7)*.56f+
                    Noise(10*u-warp,9*v+warp*2,s+17)*.30f+Noise(24*u,20*v,s+34)*.14f;
                float curls=(float)Math.Pow(Math.Max(0,1-Math.Abs(waves-.52f)*3),2.6);
                float light=waves*.56f+curls*.44f;
                sprite.data[y*sprite.w+x]=Premultiply(rim*rim*(.12f+waves*.37f+curls*.45f),
                    (int)(117+104*light),(int)(165+77*light),(int)(195+58*light));
            }
        }
        return variants;
    }
    static ParticleSprite[] BuildFrost(){
        var atlas=new ParticleSprite[16];
        for(int s=0;s<atlas.Length;s++){
            var sprite=atlas[s]=new ParticleSprite(64,64);
            int arms=4+s%4;float turn=Hash(s,3,22)*3;
            for(int y=0;y<64;y++)for(int x=0;x<64;x++){
                float u=(x-31.5f)/31.5f,v=(y-31.5f)/31.5f,r=(float)Math.Sqrt(u*u+v*v);
                if(r>=1)continue;
                float a=(float)Math.Atan2(v,u)+turn;
                float mineral=Noise(x*.13f,y*.13f,s+18),dust=Hash(x,y,s+82);
                float theta=a*arms+.62f*mineral;
                float ridge=(float)Math.Abs(Math.Cos(theta));
                float border=.43f+.30f*ridge+.25f*mineral;
                float inside=Math.Max(0,Math.Min(1,(border-r)*12));
                if(inside==0)continue;
                float vein=(float)Math.Exp(-Math.Abs(Math.Sin(theta))*r*23);
                float barbs=(float)Math.Pow(.5+.5*Math.Cos(35*r+7*Math.Abs(Math.Sin(theta))),7)*ridge;
                float facet=.5f+.5f*(float)Math.Cos(Math.Floor(a*arms/Math.PI)*2.4+s);
                float edge=Math.Max(0,1-Math.Abs(r-border+.05f)*17);
                float light=Math.Min(1,.16f+.30f*facet+.24f*mineral+.26f*vein+.25f*edge+.24f*barbs);
                float alpha=inside*(.18f+.30f*mineral+.31f*vein+.22f*barbs+.18f*edge)*(dust>.89f?1.35f:1);
                sprite.data[y*64+x]=Premultiply(alpha,(int)(105+148*light),(int)(158+95*light),(int)(184+71*light));
            }
        }
        return atlas;
    }
    static ParticleSprite BuildPowder(){
        var sprite=new ParticleSprite(26,18);
        for(int y=0;y<sprite.h;y++)for(int x=0;x<sprite.w;x++){
            float u=(x+.5f)/sprite.w,v=(y+.5f)/sprite.h,dx=2*u-1,dy=2*v-1;
            float height=Math.Max(0,1-dx*dx-dy*dy);
            float grain=Noise(u*12,v*7,22),light=.72f-v*.46f+grain*.32f;
            sprite.data[y*sprite.w+x]=Premultiply(height*(.60f+grain*.40f),
                (int)(92+157*light),(int)(133+117*light),(int)(166+88*light));
        }
        return sprite;
    }
    static ParticleSprite[] BuildFlakes(){
        var variants=new ParticleSprite[4];
        for(int s=0;s<variants.Length;s++){
            var sprite=new ParticleSprite(23,23);variants[s]=sprite;
            for(int y=0;y<sprite.h;y++)for(int x=0;x<sprite.w;x++){
                double dx=(x-11)/10.0,dy=(y-11)/10.0,rad=Math.Sqrt(dx*dx+dy*dy);
                double angle=Math.Atan2(dy,dx);
                double spoke=Math.Abs(Math.Sin(angle*(s%2==0?3:2)));
                float core=(float)Math.Max(0,1-rad*(s<2?2.2:1.5));
                float arms=(float)Math.Max(0,1-rad)*Math.Max(0,1-(float)(spoke*rad*7));
                float alpha=Math.Min(1,core*.96f+arms*.96f);
                sprite.data[y*sprite.w+x]=Premultiply(alpha,207+(int)(46*core),228+(int)(26*core),251);
            }
        }
        return variants;
    }
}
