using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

// Positions and dimensions are measured in the original IPI 84 canvas (1920 × 720).
internal sealed class BoxTune {
    public int x,y,width=100,height=100;
    public bool Valid(){return Math.Abs((long)x)<=400&&Math.Abs((long)y)<=240&&width>=25&&width<=200&&height>=25&&height<=200;}
}
internal sealed class Alignment {
    public BoxTune layout=new BoxTune(),cap=new BoxTune(),housing=new BoxTune(),stem=new BoxTune();
    public Dictionary<string,BoxTune> individual=new Dictionary<string,BoxTune>();
    public BoxTune Layer(int i){return i==0?layout:i==1?cap:i==2?housing:stem;}
    public BoxTune Local(string code,int i,bool create){
        BoxTune b;string k=code+"/"+i;
        if(!individual.TryGetValue(k,out b)&&create)individual.Add(k,b=new BoxTune());
        return b;
    }
    public bool Valid(){return new[]{layout,cap,housing,stem}.All(b=>b!=null&&b.Valid())&&individual!=null&&individual.Count<=252&&individual.Values.All(b=>b!=null&&b.Valid());}
    static string FileFor(string id){
        if(string.IsNullOrWhiteSpace(id))throw new ArgumentException("Missing display");
        using(var hash=SHA256.Create())return Path.Combine(Project.Data,"alignment-"+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(id.ToUpperInvariant())),0,12).Replace("-","")+".json");
    }
    public static Alignment Read(string id){
        if(string.IsNullOrEmpty(id))return new Alignment();
        var path=FileFor(id);if(!File.Exists(path))return new Alignment();
        var a=Project.Json.Deserialize<Alignment>(File.ReadAllText(path));if(a==null||!a.Valid())throw new InvalidDataException("Invalid display calibration");
        return a;
    }
    public void Save(string id){if(!Valid())throw new InvalidDataException("Invalid display calibration");Project.Atomic(FileFor(id),Project.Json.Serialize(this));}
    public static DateTime Stamp(string id){return string.IsNullOrEmpty(id)?DateTime.MinValue:File.GetLastWriteTimeUtc(FileFor(id));}
}
internal sealed class KeyGeometry {
    readonly GlassData data;
    readonly PointF[] origins;
    readonly float pitchX,pitchY,axis;
    string display;DateTime stamp;
    public KeyGeometry(GlassData data){
        this.data=data;origins=data.keys.Select(k=>new PointF(k.x*1920,k.y*720)).ToArray();
        pitchX=(data.keys.First(k=>k.code=="KeyS").x-data.keys.First(k=>k.code=="KeyA").x)*1920;
        pitchY=data.pitch*720;axis=data.axisSize;Apply(new Alignment());
    }
    public RectangleF Box(Alignment a,int index,int layer){
        var p=origins[index];var key=data.keys[index];var layout=a.layout;var tune=a.Layer(layer);var local=a.Local(key.code,layer,false);
        float sx=layout.width*.01f,sy=layout.height*.01f;
        float x=960+(p.X-960)*sx+layout.x+tune.x,y=360+(p.Y-360)*sy+layout.y+tune.y;
        float w=(layer==1?(Math.Max(1,key.w)-.08f)*pitchX:layer==2?axis:axis*.5f)*sx*tune.width*.01f;
        float h=(layer==1?.92f*pitchY:layer==2?axis:axis*.5f)*sy*tune.height*.01f;
        if(local!=null){x+=local.x;y+=local.y;w*=local.width*.01f;h*=local.height*.01f;}
        return new RectangleF(x-w*.5f,y-h*.5f,w,h);
    }
    public void Apply(Alignment a){
        for(int i=0;i<data.keys.Length;i++){
            var k=data.keys[i];k.cap=Box(a,i,1);k.housing=Box(a,i,2);k.stem=Box(a,i,3);
            k.x=(k.stem.X+k.stem.Width*.5f)/1920;k.y=(k.stem.Y+k.stem.Height*.5f)/720;
        }
    }
    public bool Sync(string id){
        var time=Alignment.Stamp(id);if(display==id&&time==stamp)return false;
        Apply(Alignment.Read(id));stamp=time;display=id;return true;
    }
}
