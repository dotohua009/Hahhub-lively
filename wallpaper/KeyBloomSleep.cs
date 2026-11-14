using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;

// Temporarily disconnects one verified display output. Never sends DDC power commands.
internal static class KeyBloomSleep
{
    [StructLayout(LayoutKind.Sequential)] public struct Luid {public uint low;public int high;}
    [StructLayout(LayoutKind.Sequential)] public struct Source {public Luid adapter;public uint id,mode,status;}
    [StructLayout(LayoutKind.Sequential)] public struct Target {public Luid adapter;public uint id,mode,technology,rotation,scaling,numerator,denominator,scan;public int available;public uint status;}
    [StructLayout(LayoutKind.Sequential)] public struct DisplayPath {public Source source;public Target target;public uint flags;}
    [StructLayout(LayoutKind.Explicit,Size=64)] public struct Mode {
        [FieldOffset(0)]public uint type;[FieldOffset(4)]public uint id;[FieldOffset(8)]public Luid adapter;
        [FieldOffset(16)]public ulong a;[FieldOffset(24)]public ulong b;[FieldOffset(32)]public ulong c;
        [FieldOffset(40)]public ulong d;[FieldOffset(48)]public ulong e;[FieldOffset(56)]public ulong f;
        public uint Width{get{return (uint)a;}}public uint Height{get{return (uint)(a>>32);}}
        public int X{get{return (int)(b>>32);}}public int Y{get{return (int)c;}}
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct SourceName {public uint type,size;public Luid adapter;public uint id;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string name;}
    [StructLayout(LayoutKind.Sequential)] struct LastInput {public uint size,time;}
    public sealed class Snapshot {public DisplayPath[] paths;public Mode[] modes;public int keyboard;}
    [DllImport("user32.dll")]static extern int GetDisplayConfigBufferSizes(uint flags,out uint paths,out uint modes);
    [DllImport("user32.dll")]static extern int QueryDisplayConfig(uint flags,ref uint paths,[Out]DisplayPath[] path,ref uint modes,[Out]Mode[] mode,IntPtr topology);
    [DllImport("user32.dll")]static extern int SetDisplayConfig(uint paths,DisplayPath[] path,uint modes,Mode[] mode,uint flags);
    [DllImport("user32.dll")]static extern int DisplayConfigGetDeviceInfo(ref SourceName name);
    [DllImport("user32.dll")]static extern bool GetLastInputInfo(ref LastInput info);
    [DllImport("user32.dll")]static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll")]static extern bool CloseDesktop(IntPtr desktop);
    static readonly string root=AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
    static readonly string recovery=Path.Combine(root,"sleep-recovery.json");
    static readonly string manualFlag=Path.Combine(root,"sleep-manual.flag");
    static readonly string wakeRequest=Path.Combine(root,"sleep-wake.request");
    static readonly JavaScriptSerializer json=new JavaScriptSerializer();
    const string mutexName="Local\\KeyBloom.KeyboardScreenSleep";
    static uint Idle(){var info=new LastInput{size=8};return GetLastInputInfo(ref info)?unchecked((uint)Environment.TickCount-info.time):0;}
    static bool DesktopAvailable(){var h=OpenInputDesktop(0,false,0x100);if(h==IntPtr.Zero)return false;CloseDesktop(h);return true;}
    static bool Due(bool enabled,uint idle,int minutes){return enabled&&minutes>=1&&minutes<=120&&idle>=(uint)minutes*60000;}
    static bool ShouldSleep(bool manual,bool wake,bool enabled,uint idle,int minutes,bool cooledDown){return !wake&&(manual||(Due(enabled,idle,minutes)&&cooledDown));}
    static string Normalize(string name){return name.TrimStart('\\').ToUpperInvariant();}
    static Snapshot Capture(){
        for(int attempt=0;attempt<3;attempt++){
            uint pc,mc;int rc=GetDisplayConfigBufferSizes(2,out pc,out mc);if(rc!=0||pc<1||pc>128||mc>512)throw new Exception("Display enumeration failed: "+rc);
            var p=new DisplayPath[pc];var m=new Mode[mc];rc=QueryDisplayConfig(2,ref pc,p,ref mc,m,IntPtr.Zero);if(rc==122)continue;if(rc!=0)throw new Exception("Display query failed: "+rc);Array.Resize(ref p,(int)pc);Array.Resize(ref m,(int)mc);return new Snapshot{paths=p,modes=m,keyboard=-1};
        }throw new Exception("Display topology is changing.");
    }
    static string Name(DisplayPath path){var n=new SourceName{type=1,size=(uint)Marshal.SizeOf(typeof(SourceName)),adapter=path.source.adapter,id=path.source.id};if(DisplayConfigGetDeviceInfo(ref n)!=0)throw new Exception("Display identity unavailable.");return n.name;}
    static Dictionary<string,object> Assignment(){
        string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Lively Wallpaper","WallpaperLayout.json");
        var entries=json.Deserialize<List<Dictionary<string,object>>>(File.ReadAllText(file));
        var match=entries.Where(x=>string.Equals(Convert.ToString(x["LivelyInfoPath"]).TrimEnd('\\'),root,StringComparison.OrdinalIgnoreCase)).ToList();
        if(match.Count!=1)throw new Exception("Wallpaper must be assigned to exactly one keyboard screen.");
        var screen=(Dictionary<string,object>)match[0]["LivelyScreen"];if(Convert.ToBoolean(screen["IsPrimary"]))throw new Exception("Primary display is protected.");return screen;
    }
    static Dictionary<string,Dictionary<string,object>> Settings(Dictionary<string,object> screen){
        string library=Directory.GetParent(Directory.GetParent(root).FullName).FullName;
        return json.Deserialize<Dictionary<string,Dictionary<string,object>>>(File.ReadAllText(Path.Combine(library,"SaveData","wpdata",Path.GetFileName(root),Convert.ToString(screen["Index"]),"LivelyProperties.json")));
    }
    static Snapshot Prepare(Dictionary<string,object> screen){
        var state=Capture();if(state.paths.Length<2)throw new Exception("Cannot disconnect the only display.");
        for(int i=0;i<state.paths.Length;i++){
            var path=state.paths[i];if(Normalize(Name(path))!=Normalize(Convert.ToString(screen["DeviceName"])))continue;
            if(path.source.mode>=state.modes.Length)throw new Exception("Missing source mode.");var mode=state.modes[path.source.mode];
            if(mode.type!=1||mode.Width!=1920||mode.Height!=720||(mode.X==0&&mode.Y==0))throw new Exception("Target is not a non-primary 1920 x 720 keyboard screen.");
            if(state.keyboard!=-1)throw new Exception("Ambiguous display.");state.keyboard=i;
        }
        if(state.keyboard<0)throw new Exception("Keyboard screen is not connected.");
        Apply(state,false,true);Apply(state,true,true);return state;
    }
    static void Apply(Snapshot state,bool asleep,bool validate){
        if(state==null||state.paths==null||state.modes==null||state.keyboard<0||state.keyboard>=state.paths.Length)throw new Exception("Invalid recovery state.");
        var paths=asleep?state.paths.Where((p,i)=>i!=state.keyboard).ToArray():state.paths;
        int rc=SetDisplayConfig((uint)paths.Length,paths,(uint)state.modes.Length,state.modes,0x20u|(validate?0x40u:0x80u));
        if(rc!=0)throw new Exception((validate?"Validation":"Display change")+" failed: "+rc);
    }
    static bool SameLuid(Luid a,Luid b){return a.low==b.low&&a.high==b.high;}
    static bool Matches(Snapshot current,Snapshot expected,bool asleep){
        var wanted=expected.paths.Where((p,i)=>!asleep||i!=expected.keyboard).ToArray();if(current.paths.Length!=wanted.Length)return false;
        foreach(var path in wanted){var matches=current.paths.Where(p=>p.target.id==path.target.id&&SameLuid(p.target.adapter,path.target.adapter)).ToArray();if(matches.Length!=1)return false;
            var actual=matches[0];if(actual.source.mode>=current.modes.Length||path.source.mode>=expected.modes.Length)return false;
            var a=current.modes[actual.source.mode];var b=expected.modes[path.source.mode];if(a.Width!=b.Width||a.Height!=b.Height||a.X!=b.X||a.Y!=b.Y||actual.target.rotation!=path.target.rotation||actual.target.numerator* (ulong)path.target.denominator!=path.target.numerator*(ulong)actual.target.denominator)return false;
        }return true;
    }
    static void Restore(){if(!File.Exists(recovery))return;var state=json.Deserialize<Snapshot>(File.ReadAllText(recovery));
        // Never roll another display's user edits back after a topology change.
        var current=Capture();if(Matches(current,state,false)){File.Delete(recovery);return;}
        if(!Matches(current,state,true))throw new Exception("Other display layout changed; automatic recovery stopped.");
        Apply(state,false,false);if(!Matches(Capture(),state,false))throw new Exception("Restore verification failed.");File.Delete(recovery);
    }
    static void Status(string state,string error=null){File.WriteAllText(Path.Combine(root,"sleep-status.json"),json.Serialize(new{state=state,idleMilliseconds=Idle(),utc=DateTime.UtcNow.ToString("o"),error=error}));}
    static void Sleep(Snapshot state){
        File.WriteAllText(recovery+".tmp",json.Serialize(state));File.Move(recovery+".tmp",recovery);
        string ready="Local\\KeyBloom.SleepGuard."+Process.GetCurrentProcess().Id;
        using(var signal=new EventWaitHandle(false,EventResetMode.ManualReset,ready)){
            Process.Start(new ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath,"--guard "+Process.GetCurrentProcess().Id){UseShellExecute=false,CreateNoWindow=true});
            if(!signal.WaitOne(5000))throw new Exception("Recovery guard did not start.");
        }
        Apply(state,true,false);if(!Matches(Capture(),state,true))throw new Exception("Other-screen preservation check failed.");Status("sleeping");
    }
    static int Guard(int pid){
        using(var ready=EventWaitHandle.OpenExisting("Local\\KeyBloom.SleepGuard."+pid))ready.Set();
        Process owner=null;try{owner=Process.GetProcessById(pid);}catch(ArgumentException){}
        while(File.Exists(recovery)&&owner!=null&&!owner.HasExited)Thread.Sleep(500);
        if(owner!=null)owner.Dispose();
        if(File.Exists(manualFlag)&&!File.Exists(wakeRequest))return 0;
        if(File.Exists(recovery)){for(int i=0;i<120;i++){try{if(DesktopAvailable()){Restore();Status("recovered");return 0;}}catch{}Thread.Sleep(1000);}return 1;}return 0;
    }
    static int Main(string[] args){
        try{
            if(args.Length==1&&(args[0]=="--sleep-now"||args[0]=="--wake")){
                string bridge=Path.Combine(root,"KeyBloomInput.exe");if(!File.Exists(bridge))throw new Exception("Input helper is missing.");
                if(args[0]=="--sleep-now"){File.Delete(wakeRequest);File.WriteAllText(manualFlag,"manual");}
                else{File.WriteAllText(wakeRequest,"wake");File.Delete(manualFlag);}
                Process.Start(new ProcessStartInfo(bridge){UseShellExecute=false,CreateNoWindow=true});return 0;
            }
            if(args.Length==2&&args[0]=="--guard")return Guard(int.Parse(args[1]));
            if(args.Length>0&&args[0]=="--self-test"){
                if(Marshal.SizeOf(typeof(DisplayPath))!=72||Marshal.SizeOf(typeof(Mode))!=64||!Due(true,600000,10)||Due(true,599999,10)||Due(false,700000,10)||Due(true,700000,0))throw new Exception("Idle or native layout self-test failed.");
                if(!ShouldSleep(true,false,true,0,10,false)||!ShouldSleep(true,false,false,0,10,true)||ShouldSleep(true,true,true,900000,10,true)||ShouldSleep(false,false,true,0,10,true)||!ShouldSleep(false,false,true,600000,10,true)||ShouldSleep(false,false,true,600000,10,false))throw new Exception("Manual sleep and wake policy self-test failed.");
                var original=Capture();var copy=json.Deserialize<Snapshot>(json.Serialize(original));if(!Matches(copy,original,false))throw new Exception("Snapshot round trip failed.");Status("self-test-passed");return 0;
            }
            bool created;using(var mutex=new Mutex(true,mutexName,out created)){if(!created)return 0;
                if(File.Exists(recovery)&&(!File.Exists(manualFlag)||File.Exists(wakeRequest))&&DesktopAvailable())Restore();
                if(args.Length>0&&args[0]=="--test"){
                    var initial=Prepare(Assignment());try{Sleep(initial);Thread.Sleep(5000);}finally{Restore();}
                    File.WriteAllText(Path.Combine(root,"sleep-test.json"),json.Serialize(new{singleDisplayDisconnected=true,restored=true,otherDisplayModesPreserved=Matches(Capture(),initial,false),utc=DateTime.UtcNow.ToString("o")}));Status("test-passed");return 0;
                }
                if(args.Length!=2||args[0]!="--monitor")return 0;
                using(var parent=Process.GetProcessById(int.Parse(args[1]))){
                    bool sleeping=File.Exists(recovery);Dictionary<string,object> screen=null;DateTime lastStatus=DateTime.MinValue,lastWake=DateTime.UtcNow;
                    if(sleeping){var saved=json.Deserialize<Snapshot>(File.ReadAllText(recovery));var current=Capture();if(Matches(current,saved,false)){File.Delete(recovery);sleeping=false;}else if(!Matches(current,saved,true))throw new Exception("Manual sleep layout changed; automatic changes stopped.");}
                    try{while(!parent.HasExited){
                        try{
                            bool manual=File.Exists(manualFlag),wake=File.Exists(wakeRequest);
                            if(wake){if(!DesktopAvailable()){Thread.Sleep(500);continue;}Restore();sleeping=false;lastWake=DateTime.UtcNow;File.Delete(wakeRequest);Status("awake");lastStatus=DateTime.UtcNow;}
                            // Manual sleep must remain independent of keyboard/mouse input and Lively's disconnected-screen layout.
                            bool enabled=false;int minutes=10;
                            if(!manual){if(!sleeping)screen=Assignment();var settings=Settings(screen);enabled=settings.ContainsKey("sleepEnabled")&&Convert.ToBoolean(settings["sleepEnabled"]["value"]);minutes=settings.ContainsKey("sleepMinutes")?Convert.ToInt32(settings["sleepMinutes"]["value"]):10;}
                            uint idleBefore=Idle();bool desired=ShouldSleep(manual,wake,enabled,idleBefore,minutes,DateTime.UtcNow-lastWake>TimeSpan.FromSeconds(15));
                            if(DesktopAvailable()){
                                if(sleeping&&!desired){Restore();sleeping=false;lastWake=DateTime.UtcNow;Status("awake");lastStatus=DateTime.UtcNow;File.AppendAllText(Path.Combine(root,"sleep-cycles.jsonl"),json.Serialize(new{state="awake",idleMilliseconds=idleBefore,utc=DateTime.UtcNow.ToString("o")})+Environment.NewLine);}
                                else if(!sleeping&&desired){Sleep(Prepare(manual?Assignment():screen));sleeping=true;Status(manual?"manual-sleeping":"sleeping");lastStatus=DateTime.UtcNow;var journal=Path.Combine(root,"sleep-cycles.jsonl");if(File.Exists(journal)&&new FileInfo(journal).Length>32768)File.WriteAllText(journal,"");File.AppendAllText(journal,json.Serialize(new{state=manual?"manual-sleeping":"sleeping",idleMilliseconds=idleBefore,minutes=minutes,utc=DateTime.UtcNow.ToString("o")})+Environment.NewLine);}
                            }
                            if(DateTime.UtcNow-lastStatus>TimeSpan.FromSeconds(15)){Status(sleeping?(manual?"manual-sleeping":"sleeping"):manual?"waiting-for-unlock":enabled?"watching":"disabled");lastStatus=DateTime.UtcNow;}
                        }catch(Exception e){Status("error",e.Message);if(File.Exists(recovery)&&DesktopAvailable()&&!File.Exists(manualFlag)){Restore();sleeping=false;lastWake=DateTime.UtcNow;}Thread.Sleep(3000);}
                        Thread.Sleep(500);
                    }}finally{if(File.Exists(recovery)&&DesktopAvailable()&&!File.Exists(manualFlag))Restore();}
                }mutex.ReleaseMutex();
            }return 0;
        }catch(Exception e){Status("error",e.Message);return 1;}
    }
}
