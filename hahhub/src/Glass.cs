using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

internal sealed class Tuning
{
    public int density=75, spread=85, heal=1800, delay=800, crackSize=0;
    public bool Valid(){return density>=30 && density<=100 && spread>=45 && spread<=150 && heal>=500 && heal<=4000 && delay>=0 && delay<=5000 && (crackSize==0||crackSize==1);}
}

internal static class Project
{
    public const string Id="hahhan-glass";
    public static readonly string Base=AppDomain.CurrentDomain.BaseDirectory;
    public static readonly string Data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"HAHHAN","Interaction Studio");
    internal static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    public static string Pack(){return Path.Combine(Base,"effects",Id);}
    public static string ChoiceFile {get{return Path.Combine(Data,"output.json");}}
    public static OutputChoice ReadChoice(){
        if(!File.Exists(ChoiceFile))return new OutputChoice();
        var choice=Json.Deserialize<OutputChoice>(File.ReadAllText(ChoiceFile))??new OutputChoice();
        if(!string.IsNullOrEmpty(choice.Effect)&&choice.Effect!=Id&&choice.Effect!=BlackHole.Id)choice.Effect=BlackHole.Id;
        return choice;
    }
    public static void SaveChoice(OutputChoice choice){Atomic(ChoiceFile,Json.Serialize(choice));}
    public static void Atomic(string path,string data){
        Directory.CreateDirectory(Path.GetDirectoryName(path));string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(tmp,data,new UTF8Encoding(false));if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}
        finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
    static string TuningFile {get{return Path.Combine(Data,"glass.json");}}
    public static Tuning ReadTuning(){
        if(!File.Exists(TuningFile))return new Tuning();
        var t=Json.Deserialize<Tuning>(File.ReadAllText(TuningFile));
        return t!=null&&t.Valid()?t:new Tuning();
    }
    public static void SaveTuning(Tuning t){
        if(!t.Valid())throw new ArgumentOutOfRangeException();
        Atomic(TuningFile,Json.Serialize(t));
    }
    public static string HolePowerPath {get{return Path.Combine(Data,"blackhole-power.txt");}}
    public static int HolePower(){int n;return File.Exists(HolePowerPath)&&int.TryParse(File.ReadAllText(HolePowerPath),out n)&&n>=20&&n<=100?n:80;}
    public static string ProfilePath {get{return Path.Combine(Data,"fps.txt");}}
    public static bool WallpaperIntegration{
        get{string path=Path.Combine(Data,"wallpaper-integration.txt");return !File.Exists(path)||File.ReadAllText(path).Trim()!="0";}
        set{Atomic(Path.Combine(Data,"wallpaper-integration.txt"),value?"1":"0");}
    }
    public static int Fps(){int value;return File.Exists(ProfilePath)&&int.TryParse(File.ReadAllText(ProfilePath),out value)&&(value==16||value==24||value==30||value==60)?value:16;}

}
internal sealed class LayoutKey {public string code{get;set;} public int scan{get;set;} public float x{get;set;} public float y{get;set;}}
