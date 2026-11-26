using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;


// Persist the monitor interface path, never the DISPLAY number or desktop rectangle.
internal sealed class OutputChoice
{
    public string Id, Label;
    public string Effect=Project.Id;
    public bool Enabled;
    public Output Find(IEnumerable<Output> screens){
        if(!Enabled||string.IsNullOrEmpty(Id))return null;
        return screens.FirstOrDefault(s=>s.Usable && string.Equals(Id,s.Id,StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed class Output
{
    public string Id, Label;
    public Rectangle Bounds;
    public bool Mirrored, Primary;
    public string Source;
    public IntPtr Monitor;
    public bool Usable {get{return !Mirrored&&!Bounds.IsEmpty&&!string.IsNullOrEmpty(Id);}}
    public string DisplayName {get{return UiText.Display(Label);}}
    public override string ToString(){return DisplayName+(Mirrored?UiText.T(" · Mirrored"):Bounds.IsEmpty?UiText.T(" · Offline"):"");}
}

// All handles to this monitor-specific marker close automatically if the player exits.
internal sealed class VideoLease : IDisposable
{
    public const string Caption="hahhub Video Cast";
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool SetProp(IntPtr window,string name,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr RemoveProp(IntPtr window,string name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetProp(IntPtr window,string name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string title);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    readonly System.Threading.EventWaitHandle marker;
    readonly IntPtr window;readonly string tag;
    static string Name(string id){
        using(var hash=System.Security.Cryptography.SHA256.Create()){
            var bytes=System.Text.Encoding.UTF8.GetBytes(id.ToUpperInvariant());
            return @"Local\HAHHAN.Video."+BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","");
        }
    }
    public VideoLease(string id,IntPtr window=default(IntPtr)){
        bool created;
        this.window=window;tag=Name(id);
        marker=new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.ManualReset,tag,out created);
        if(!created){marker.Dispose();throw new InvalidOperationException(UiText.T("A video is already casting to this display. Stop it first."));}
        if(window!=IntPtr.Zero&&!SetProp(window,tag,new IntPtr(1))){marker.Dispose();throw new InvalidOperationException(UiText.T("Could not attach effects to the video."));}
        Host.NotifySettings();
    }
    public static IntPtr Player(string id){
        var h=IntPtr.Zero;string tag=Name(id);
        while((h=FindWindowEx(IntPtr.Zero,h,null,Caption))!=IntPtr.Zero)
            if(IsWindowVisible(h)&&GetProp(h,tag)!=IntPtr.Zero)return h;
        return IntPtr.Zero;
    }
    public static bool Active(string id){
        if(string.IsNullOrEmpty(id))return false;
        System.Threading.EventWaitHandle found;
        if(!System.Threading.EventWaitHandle.TryOpenExisting(Name(id),out found))return false;
        found.Dispose();return true;
    }
    public static Output Target(List<Output> outputs,string preferred){
        var used=outputs.Find(o=>o.Usable&&String.Equals(o.Id,preferred,StringComparison.OrdinalIgnoreCase)&&Active(o.Id));
        return used??outputs.Find(o=>o.Usable&&!String.Equals(o.Id,preferred,StringComparison.OrdinalIgnoreCase)&&Active(o.Id));
    }
    public void Dispose(){if(window!=IntPtr.Zero)RemoveProp(window,tag);marker.Dispose();Host.NotifySettings();}
}

internal static class Displays
{
    [StructLayout(LayoutKind.Sequential)] struct Luid {public uint low;public int high;}
    [StructLayout(LayoutKind.Sequential)] struct SourcePath {public Luid adapter;public uint id,mode,flags;}
    [StructLayout(LayoutKind.Sequential)] struct TargetPath{
        public Luid adapter;public uint id,mode,technology,rotation,scaling,rateNum,rateDen,scan,available,flags;
    }
    [StructLayout(LayoutKind.Sequential)] struct PathInfo{public SourcePath source;public TargetPath target;public uint flags;}
    [StructLayout(LayoutKind.Sequential,Size=64)] struct ModeInfo{public uint type;}
    [StructLayout(LayoutKind.Sequential)] struct Header{public uint type,size;public Luid adapter;public uint id;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct SourceName{
        public Header head;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string name;
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct TargetName{
        public Header head;public uint flags,tech;public ushort maker,product;public uint connector;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string friendly;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string path;
    }
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags,out uint paths,out uint modes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags,ref uint paths,[Out] PathInfo[] items,ref uint modes,[Out] ModeInfo[] info,IntPtr topology);
    [DllImport("user32.dll",EntryPoint="DisplayConfigGetDeviceInfo")] static extern int ReadSource(ref SourceName info);
    [DllImport("user32.dll",EntryPoint="DisplayConfigGetDeviceInfo")] static extern int ReadTarget(ref TargetName info);

    [StructLayout(LayoutKind.Sequential)] struct Rect {public int left,top,right,bottom;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct MonitorInfo{
        public int size;public Rect bounds,work;public uint flags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string name;
    }
    delegate bool VisitMonitor(IntPtr monitor,IntPtr dc,IntPtr rect,IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,VisitMonitor callback,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    static Dictionary<string,Tuple<IntPtr,MonitorInfo>> DesktopBounds(){
        var result=new Dictionary<string,Tuple<IntPtr,MonitorInfo>>(StringComparer.OrdinalIgnoreCase);
        EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,delegate(IntPtr mon,IntPtr dc,IntPtr rect,IntPtr data){
            var info=new MonitorInfo{size=Marshal.SizeOf(typeof(MonitorInfo))};
            if(GetMonitorInfo(mon,ref info))result[info.name]=Tuple.Create(mon,info);
            return true;
        },IntPtr.Zero);return result;
    }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point point,uint flags);
    internal static bool StillAt(Output screen,Point point){
        if(!screen.Bounds.Contains(point))return false;
        var mon=MonitorFromPoint(point,0);
        var info=new MonitorInfo{size=Marshal.SizeOf(typeof(MonitorInfo))};
        if(mon==IntPtr.Zero||mon!=screen.Monitor||!GetMonitorInfo(mon,ref info))return false;
        var r=info.bounds;
        return screen.Primary==((info.flags&1)!=0)&&String.Equals(screen.Source,info.name,StringComparison.OrdinalIgnoreCase)
            &&screen.Bounds==Rectangle.FromLTRB(r.left,r.top,r.right,r.bottom);
    }
    public static List<Output> Connected(){
        var list=new List<Output>();
        for(int attempt=0;attempt<3;attempt++){
            uint np,nm;if(GetDisplayConfigBufferSizes(2,out np,out nm)!=0||np==0)return list;
            var paths=new PathInfo[np];var modes=new ModeInfo[nm];
            int rc=QueryDisplayConfig(2,ref np,paths,ref nm,modes,IntPtr.Zero);
            if(rc==122)continue;if(rc!=0)return list;
            var desktops=DesktopBounds();
            foreach(var group in paths.Take((int)np).GroupBy(p=>new {p.source.adapter.low,p.source.adapter.high,p.source.id})){
                var src=group.First().source;
                var source=new SourceName{head=new Header{type=1,size=(uint)Marshal.SizeOf(typeof(SourceName)),adapter=src.adapter,id=src.id}};
                if(ReadSource(ref source)!=0)continue;
                Tuple<IntPtr,MonitorInfo> desktop;if(!desktops.TryGetValue(source.name,out desktop))continue;
                var monitor=desktop.Item2;
                var rect=monitor.bounds;var bounds=Rectangle.FromLTRB(rect.left,rect.top,rect.right,rect.bottom);
                bool cloned=group.Count()>1||desktops.Values.Count(m=>m.Item2.bounds.Equals(rect))>1;
                foreach(var p in group){
                    var target=new TargetName{head=new Header{type=2,size=(uint)Marshal.SizeOf(typeof(TargetName)),adapter=p.target.adapter,id=p.target.id}};
                    if(ReadTarget(ref target)!=0||string.IsNullOrWhiteSpace(target.path))continue;
                    string number=source.name.Replace(@"\\.\DISPLAY",""),label="Display "+number;
                    if(!string.IsNullOrWhiteSpace(target.friendly))label+=" · "+target.friendly;
                    list.Add(new Output{Id=target.path,Label=label,Bounds=bounds,Mirrored=cloned,Primary=(monitor.flags&1)!=0,Source=source.name,Monitor=desktop.Item1});
                }
            }
            break;
        }
        return list.OrderBy(s=>s.Label,StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
