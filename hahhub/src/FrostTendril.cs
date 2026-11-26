using System;
using System.Drawing;
using System.IO;

internal static class SkillMaterials {
    const int Tile=192;
    static readonly Lazy<Bitmap[]> atlas=new Lazy<Bitmap[]>(LoadTiles);
    static Bitmap[] LoadTiles(){
        var tiles=new Bitmap[24];
        using(var sheet=new Bitmap(Path.Combine(Project.Base,"effects","hahhub-materials","skills.png"))){
            for(int i=0;i<tiles.Length;i++){
                var tile=new Bitmap(Tile,Tile,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using(var g=Graphics.FromImage(tile)){
                    g.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.DrawImage(sheet,new Rectangle(0,0,Tile,Tile),new Rectangle(i%6*Tile,i/6*Tile,Tile,Tile),GraphicsUnit.Pixel);
                }
                tiles[i]=tile;
            }
        }
        return tiles;
    }
    static readonly Lazy<ParticleSprite[]> sprites=new Lazy<ParticleSprite[]>(()=>Array.ConvertAll(atlas.Value,b=>new ParticleSprite(b,new Rectangle(0,0,Tile,Tile))));
    internal static ParticleSprite Sprite(int index){return sprites.Value[index];}
    public static void Stamp(Graphics g,int tile,RectangleF rect){
        if(rect.Width<.3f||rect.Height<.3f)return;
        g.DrawImage(atlas.Value[tile],rect,new RectangleF(0,0,Tile,Tile),GraphicsUnit.Pixel);
    }
}
internal sealed class FrostLance : SkillScene,IDisposable {
    public const string Id="hahhub-frost";
    ParticleSurface layer;
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<Contact,Cluster[]> growthPlans=
        new System.Runtime.CompilerServices.ConditionalWeakTable<Contact,Cluster[]>();
    static readonly Lazy<ParticleSprite[]> crystals=new Lazy<ParticleSprite[]>(CrystalSprites);
    public FrostLance(GlassData data,Func<double> time=null):base(data,time){}
    public void Dispose(){if(layer!=null){layer.Dispose();layer=null;}}
    protected override RectangleF Extent(Contact p){var r=p.key.cap;float pad=Math.Max(110,Math.Min(105,Math.Min(r.Width,r.Height))*1.95f);r.Inflate(pad,pad);return r;}
    protected override double Tail{get{return 1040;}}

    sealed class Cluster {
        public float turn,start,baseWidth,lean;
        public int tile;
        public Blade[] blades;
    }
    struct Blade {
        public float offset,lean,length,width,delay,duration;
        public int tile;
    }
    static float Between(Random rng,float lo,float hi){return lo+(hi-lo)*(float)rng.NextDouble();}
    static Cluster[] Plan(Contact p){
        var rng=new Random(p.seed);
        var rim=new KeycapRim(p.key.cap,2);
        float perimeter=rim.Length;
        int sites=Math.Min(9,3+(int)(perimeter/380)+rng.Next(2));
        var groups=new Cluster[sites];
        float origin=Between(rng,0,1),arc=Between(rng,.68f,.83f);
        // Reserve one open arc, then shuffle the remaining sites' birth order.
        var turns=new float[sites];
        for(int i=0;i<sites;i++){
            float turn=origin+arc*(i+Between(rng,.12f,.88f))/sites;
            int j=rng.Next(i+1);turns[i]=turns[j];turns[j]=turn;
        }
        // Separate the opening buds so a wide key does not start entirely on its bottom edge.
        PointF firstNormal;rim.At(turns[0],out firstNormal);
        for(int i=1;i<sites;i++){
            PointF n;rim.At(turns[i],out n);
            if(n.X*firstNormal.X+n.Y*firstNormal.Y>.15f)continue;
            float swap=turns[1];turns[1]=turns[i];turns[i]=swap;break;
        }
        int firstWave=2+rng.Next(2);
        float onset=0;
        for(int i=0;i<sites;i++){
            if(i>0)onset+=i<firstWave?Between(rng,12,38):Between(rng,410,840);
            float richness=Between(rng,0,1);
            int branches=i==0?4+rng.Next(3):i==1?1+rng.Next(2):1+(int)(6*richness*richness);
            var cluster=groups[i]=new Cluster{
                turn=turns[i],start=onset,
                baseWidth=Between(rng,.15f,.31f)+branches*.034f,lean=Between(rng,-.44f,.44f),
                tile=8+rng.Next(8),blades=new Blade[branches]
            };
            float lead=i==0?Between(rng,.97f,1.6f):Between(rng,.42f,1.43f);
            float delay=0;
            for(int j=0;j<branches;j++){
                if(j>0)delay+=Between(rng,95,380);
                float length=j==0?lead:lead*Between(rng,.20f,.84f);
                cluster.blades[j]=new Blade{
                    offset=j==0?0:Between(rng,-.45f,.45f)*cluster.baseWidth,
                    lean=cluster.lean+(j==0?0:Between(rng,-.66f,.66f)),
                    length=length,width=j==0?Between(rng,.13f,.33f):Between(rng,.045f,.23f),
                    delay=delay,duration=Between(rng,310,770)+650*length,tile=rng.Next(6)
                };
            }
        }
        return groups;
    }

    // Each sprite is one pointed prism. The faces meet at an off-centre ridge.
    static ParticleSprite[] CrystalSprites(){
        var tiles=new ParticleSprite[6];
        for(int i=0;i<tiles.Length;i++){
            var tile=tiles[i]=new ParticleSprite(48,112);
            float apex=.28f+.44f*Noise(521,i),shoulder=.14f+.04f*i;
            float ridge=.30f+.065f*i;
            for(int y=0;y<tile.h;y++)for(int x=0;x<tile.w;x++){
                float u=(x+.5f)/tile.w,v=(y+.5f)/tile.h;
                float left=apex*(1-Unit(v/shoulder))+.19f*v*v;
                float right=apex+(1-apex)*Unit(v/(shoulder+.10f))-.14f*v*v;
                if(u<=left||u>=right)continue;
                float across=(u-left)/(right-left),seam=Math.Abs(across-ridge);
                float facet=across<ridge?.56f+.27f*(across/ridge):.17f+.21f*across;
                if(v<shoulder*(.65f+across*.45f))facet+=.20f;
                float grain=Noise(i+17,x/3+(y/7)*151)*.045f+.026f*Sin(76*across+7*v+i);
                float fissure=Unit(1-Math.Abs(v-.69f+.16f*across+.07f*Sin(6*across+i))*82);
                float edge=Unit(1-Math.Min(u-left,right-u)*36),line=Unit(1-seam*23);
                int color=Mix(0x0b3959,0xc9f7ff,Unit(facet+grain+edge*.24f+line*.51f+fissure*.17f));
                float clarity=Unit(.51f+facet*.42f+edge*.21f+line*.26f+fissure*.14f);
                int a=(int)(255*clarity*Unit((u-left)*tile.w)*Unit((right-u)*tile.w)*Unit((1-v)*21));
                tile.data[y*tile.w+x]=(uint)(a<<24|((color>>16&255)*a/255)<<16|((color>>8&255)*a/255)<<8|(color&255)*a/255);
            }
        }
        return tiles;
    }
    void DrawCluster(KeycapRim rim,Cluster cluster,float unit,float age,float fade){
        float local=age-cluster.start;
        if(local<0)return;
        PointF normal;var root=rim.At(cluster.turn,out normal);
        float width=unit*cluster.baseWidth*(.52f+.48f*Smooth(local/260));
        float angle=(float)Math.Atan2(normal.Y,normal.X)+1.5708f;
        int alpha=(int)(255*fade);
        layer.Stamp(SkillMaterials.Sprite(cluster.tile),root.X+normal.X*width*.08f,root.Y+normal.Y*width*.08f,
            width,width*.62f,angle,alpha);
        foreach(var blade in cluster.blades){
            float elapsed=local-blade.delay;
            if(elapsed<0)continue;
            float fraction=Unit(elapsed/blade.duration);
            float growth=.25f*fraction+.75f*Smooth(fraction);
            float reach=unit*blade.length*growth;
            if(reach<1)continue;
            float thickness=unit*blade.width*Smooth(elapsed/(blade.duration*.47f));
            PointF n;var foot=rim.At(cluster.turn+unit*blade.offset/rim.Length,out n);
            float turn=(float)Math.Atan2(n.Y,n.X)+blade.lean;
            float along=reach*.47f;
            layer.Stamp(crystals.Value[blade.tile],foot.X+Cos(turn)*along,foot.Y+Sin(turn)*along,
                thickness,reach,turn+1.5708f,alpha);
            // Shed a few grains from an advancing tip, local to the growing cluster.
            if(blade.length>.58f&&growth>.18f&&growth<.97f){
                float drift=(elapsed/(300+blade.duration*.13f)+blade.tile*.23f)%1;
                var tip=At(foot,Cos(turn)*(reach+9*drift),Sin(turn)*(reach+9*drift)-5*drift);
                layer.Stroke(tip,At(tip,1.2f,-1.8f),0xcdf4ff,.72f*fade*Sin(drift*Math.PI),1.6f);
            }
        }
    }
    protected override void Paint(Graphics g,int w,int h,Bitmap image,Rectangle sample){
        BeginLayer(ref layer,.80f);
        foreach(var p in contacts){
            var rim=new KeycapRim(p.key.cap,2);
            float unit=Math.Min(105,Math.Min(p.key.cap.Width,p.key.cap.Height));
            float age=(float)((p.released<0?now:p.released)-p.birth);
            float fade=Fade(p);
            foreach(var cluster in growthPlans.GetValue(p,Plan))DrawCluster(rim,cluster,unit,age,fade);
        }
        layer.Present(g);
    }
}
