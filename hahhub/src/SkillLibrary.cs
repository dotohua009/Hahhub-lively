using System;

internal sealed class SkillLibrary : IDisposable {
    internal sealed class Entry {
        public readonly string id,title,tile,hint;
        public Entry(string id,string title,string tile,string hint){this.id=id;this.title=title;this.tile=tile;this.hint=hint;}
    }
    public static readonly Entry[] Entries={
        new Entry(Project.Id,"Fracture","GlassTile",""),new Entry(BlackHole.Id,"Black Hole","HoleTile",""),new Entry(InkSplash.Id,"Ink Splash","InkTile",""),
        new Entry(GlitterFlow.Id,"Glitter Flow","GlitterTile","Tap one side to tilt the liquid. Holographic flakes turn and catch the light as it flows."),
        new Entry(WeatherScene.FogId,"Frost Fog","FogTile","Mist rolls off the whole keycap as patches of frost form. Nearby taps stir the cloud."),
        new Entry(WeatherScene.SnowId,"Snowdrift","SnowTile","Hold to bank snow behind the keycap. Sweep across keys to lift it into the wind."),
        new Entry(FrostLance.Id,"Frost Lance","FrostTile","Tap for scattered ice buds. Hold to grow clusters of long and short spikes from the keycap edge."),
        new Entry(Tendrils.Id,"Tendrils","TendrilTile","Hold to unfurl tendrils around the keycap. Press a nearby key to attract their tips."),
        new Entry(SilkColony.Id,"Silk Colony","SilkTile","Tap to spin the first threads. Hold to weave the web and draw more spiders along the seams."),
        new Entry(VoxelFuse.Id,"Voxel Fuse","FuseTile","Press nearby keys to plant charges and trigger a chain reaction."),
        new Entry(Prism.Id,"Prism","PrismTile","Hold a key to refract the background through its cap and switch facets.")
    };
    public static Entry Find(string id){return Array.Find(Entries,e=>e.id==id);}
    internal readonly VoxelFuse Fuse;
    readonly Prism prism;
    readonly GlitterFlow glitter;
    readonly WeatherScene fog,snow;
    readonly FrostLance frost;readonly Tendrils tendrils;readonly SilkColony colony;
    readonly BlackHole hole;readonly Glass glass;readonly InkSplash ink;
    public SkillLibrary(Glass glass,BlackHole hole,InkSplash ink){
        this.glass=glass;this.hole=hole;this.ink=ink;var data=glass.data;
        fog=new WeatherScene(data,false);snow=new WeatherScene(data,true);
        glitter=new GlitterFlow(data);
        Fuse=new VoxelFuse(data);prism=new Prism(data);
        frost=new FrostLance(data);tendrils=new Tendrils(data);colony=new SilkColony(data);
    }
    public IKeyEffect Select(string id){
        switch(id){
            case GlitterFlow.Id:return glitter;
            case WeatherScene.FogId:return fog;case WeatherScene.SnowId:return snow;
            case FrostLance.Id:return frost;case Tendrils.Id:return tendrils;case SilkColony.Id:return colony;
            case VoxelFuse.Id:return Fuse;case Prism.Id:return prism;
            case BlackHole.Id:return hole;case InkSplash.Id:return ink;default:return glass;
        }
    }
    public void Configure(SkillOptions settings){
        fog.tuning=settings.fog;snow.tuning=settings.snow;
        Fuse.fuseMs=settings.fuseMs;
        tendrils.color=settings.tendrilColor;tendrils.rainbow=settings.rainbow;colony.color=settings.spiderColor;
        ink.enhanced=settings.inkEnhanced;hole.enhanced=settings.holeEnhanced;
    }
    public void Dispose(){glitter.Dispose();fog.Dispose();snow.Dispose();Fuse.Dispose();prism.Dispose();frost.Dispose();colony.Dispose();tendrils.Dispose();}
}
