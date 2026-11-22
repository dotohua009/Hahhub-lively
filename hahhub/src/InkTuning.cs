using System;
using System.IO;

internal sealed class InkTuning {
    public int spread=160,recovery=3500;
    static string PathName{get{return Path.Combine(Project.Data,"ink.json");}}
    bool Valid(){return spread>=75&&spread<=250&&recovery>=500&&recovery<=6000;}
    public static InkTuning Read(){
        if(!File.Exists(PathName))return new InkTuning();
        var t=Project.Json.Deserialize<InkTuning>(File.ReadAllText(PathName));
        return t!=null&&t.Valid()?t:new InkTuning();
    }
    public void Save(){if(!Valid())throw new ArgumentOutOfRangeException();Project.Atomic(PathName,Project.Json.Serialize(this));}
}
