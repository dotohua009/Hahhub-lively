using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

internal class PreviewUsage {
    static readonly BindingFlags fields=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static readonly JavaScriptSerializer json=new JavaScriptSerializer();
    static string folder;
    static object studio;
    static Window window;
    static MethodInfo show,down,up;
    static DispatcherTimer animation;
    static int ticks;
    static IDictionary scans;
    static readonly int[] keys={30,31,32,33,35,36,37,38}; // A S D F H J K L
    static long Stamp(){return DateTime.UtcNow.ToFileTimeUtc();}
    static object Field(object obj,string name){return obj.GetType().GetField(name,fields).GetValue(obj);}
    static void Write(string file,object row){File.AppendAllText(Path.Combine(folder,file),json.Serialize(row)+Environment.NewLine);}
    static void Press(int i){down.Invoke(studio,new[]{scans[keys[i]]});}
    static void Lift(int i){up.Invoke(studio,new[]{scans[keys[i]]});}

    [DllImport("user32.dll")]static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")]static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll")]static extern bool CloseDesktop(IntPtr h);
    [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(System.Drawing.Point p);
    [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr h,uint flags);
    [DllImport("nvml.dll",CallingConvention=CallingConvention.Cdecl)]static extern int nvmlInit_v2();
    [DllImport("nvml.dll",CallingConvention=CallingConvention.Cdecl)]static extern int nvmlDeviceGetHandleByIndex_v2(uint index,out IntPtr gpu);
    [DllImport("nvml.dll",CallingConvention=CallingConvention.Cdecl)]static extern int nvmlDeviceGetPowerUsage(IntPtr gpu,out uint milliwatts);
    [DllImport("nvml.dll",CallingConvention=CallingConvention.Cdecl)]static extern int nvmlShutdown();

    static async Task Phase(int pass,string effect,string mode,int length){
        var hero=(FrameworkElement)window.FindName("Hero");var pt=hero.PointToScreen(new System.Windows.Point(hero.ActualWidth*.5,hero.ActualHeight*.5));
        var visible=GetAncestor(WindowFromPoint(new System.Drawing.Point((int)pt.X,(int)pt.Y)),2)==new WindowInteropHelper(window).Handle;
        long start=Stamp();int first=ticks,actions=0;
        var watch=Stopwatch.StartNew();
        if(mode=="hold"){Press(0);actions++;await Task.Delay(length);}
        else if(mode=="multi"){
            int i=0;
            while(watch.ElapsedMilliseconds<length){
                if(i>=2)Lift((i-2)%keys.Length);
                Press(i%keys.Length);i++;actions++;
                int delay=(int)(i*125L-watch.ElapsedMilliseconds);
                await Task.Delay(Math.Max(1,delay));
            }
        }else await Task.Delay(length);
        long end=Stamp();int totalTicks=ticks-first;bool timer=animation.IsEnabled;
        if(mode=="hold")Lift(0);else if(mode=="multi")for(int i=0;i<keys.Length;i++)Lift(i);
        Write("phases.jsonl",new{pass,effect,mode,start,end,timerTicks=totalTicks,timerStillOn=timer,actions,visible});
    }

    static async Task Run(string prefs){
        var a=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"HAHHAN Studio.exe"));
        a.GetType("Project").GetField("Data",fields).SetValue(null,prefs);
        var type=a.GetType("Studio");studio=Activator.CreateInstance(type,true);window=(Window)Field(studio,"window");
        show=type.GetMethod("ShowEffect",fields);down=type.GetMethod("TestHit",fields);up=type.GetMethod("ReleasePreview",fields);
        scans=(IDictionary)Field(Field(studio,"glass"),"scans");
        animation=(DispatcherTimer)Field(studio,"preview");animation.Tick+=delegate{ticks++;};
        window.Topmost=true;window.ShowActivated=false;window.ShowInTaskbar=false;window.Title="hahhub · Preview measurement";
        window.WindowStartupLocation=WindowStartupLocation.Manual;window.Show();window.UpdateLayout();
        var screen=System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s=>!s.Primary)??System.Windows.Forms.Screen.PrimaryScreen;
        var area=screen.WorkingArea;
        var src=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        var pos=src.CompositionTarget.TransformFromDevice.Transform(new System.Windows.Point(area.Left+48,area.Top+48));
        window.Left=pos.X;window.Top=pos.Y;await Task.Delay(1000);window.UpdateLayout();
        var entries=(IEnumerable)a.GetType("SkillLibrary").GetField("Entries",fields).GetValue(null);
        var ids=new List<string>();foreach(var e in entries)ids.Add((string)Field(e,"id"));
        string selectFile=Path.Combine(prefs,"preview-test-effect.txt");
        if(File.Exists(selectFile)){
            var picks=File.ReadAllText(selectFile).Split(',').Select(id=>id.Trim()).ToList();
            if(picks.Any(id=>!ids.Contains(id)))throw new ArgumentException("Unknown effect in test filter");
            ids.RemoveAll(id=>!picks.Contains(id));
        }
        var hero=(FrameworkElement)window.FindName("Hero");
        Write("fixture.jsonl",new{pid=Process.GetCurrentProcess().Id,logical=Environment.ProcessorCount,tier=RenderCapability.Tier>>16,
            heroWidth=hero.ActualWidth,heroHeight=hero.ActualHeight,resolution=screen.Bounds.ToString(),windowWidth=window.ActualWidth,windowHeight=window.ActualHeight,
            scale=src.CompositionTarget.TransformToDevice.M11,rate=Field(studio,"fps"),ids});
        var sample=new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"sample "+Process.GetCurrentProcess().Id+" \""+folder+"\""){UseShellExecute=false,CreateNoWindow=true};
        using(var observer=Process.Start(sample)){
            try{
                for(int i=0;i<80&&!File.Exists(Path.Combine(folder,"ready"));i++){if(observer.HasExited)throw new IOException("Sampler exited");await Task.Delay(250);}
                if(!File.Exists(Path.Combine(folder,"ready")))throw new TimeoutException("No GPU telemetry");
                for(int pass=1;pass<=2;pass++){
                    if(pass==2)ids.Reverse();
                    foreach(string id in ids){
                        show.Invoke(studio,new object[]{id});await Task.Delay(1400);
                        await Phase(pass,id,"idle",5200);
                        await Phase(pass,id,"hold",12500);
                        show.Invoke(studio,new object[]{id});await Task.Delay(300);
                        await Phase(pass,id,"multi",9000);
                    }
                }
            }finally{File.WriteAllText(Path.Combine(folder,"stop"),"");if(!observer.WaitForExit(8000))observer.Kill();}
        }
        window.Close();
    }

    static void Sample(int pid){
        var process=Process.GetProcessById(pid);IntPtr gpu=IntPtr.Zero;
        try{if(nvmlInit_v2()==0)nvmlDeviceGetHandleByIndex_v2(0,out gpu);}
        catch(DllNotFoundException){} // GPU engine counters remain available without NVIDIA telemetry.
        catch(EntryPointNotFoundException){}
        string query="SELECT Name,RunningTime,Timestamp_Sys100NS FROM Win32_PerfRawData_GPUPerformanceCounters_GPUEngine WHERE Name LIKE 'pid_"+pid+"_%'";
        using(var reader=new ManagementObjectSearcher(query))
        using(var writer=new StreamWriter(Path.Combine(folder,"telemetry.jsonl"))){
            writer.AutoFlush=true;int n=0;
            while(!File.Exists(Path.Combine(folder,"stop"))&&!process.HasExited){
                var engines=new Dictionary<string,ulong>();ulong gpuTime=0;
                using(var rows=reader.Get())foreach(ManagementObject r in rows)using(r){
                    var name=(string)r["Name"];if(!name.StartsWith("pid_"+pid+"_"))continue;
                    engines[name]=(ulong)r["RunningTime"];gpuTime=(ulong)r["Timestamp_Sys100NS"];
                }
                long at=Stamp();process.Refresh();double cpu=process.TotalProcessorTime.TotalSeconds;
                uint raw;double? watts=null;
                if(gpu!=IntPtr.Zero&&nvmlDeviceGetPowerUsage(gpu,out raw)==0)watts=raw*.001;
                IntPtr desktop=OpenInputDesktop(0,false,0x100);bool unlocked=desktop!=IntPtr.Zero;if(unlocked)CloseDesktop(desktop);
                writer.WriteLine(json.Serialize(new{at,cpu,engines,gpuTime,watts,unlocked}));
                if(engines.Count>0&&n==0){File.WriteAllText(Path.Combine(folder,"ready"),"");n=1;}
                Thread.Sleep(500);
            }
        }
        if(gpu!=IntPtr.Zero)nvmlShutdown();
    }
    [STAThread]static int Main(string[] args){
        folder=args[args.Length-1];try{
            if(args[0]=="sample"){Sample(int.Parse(args[1]));return 0;}
            SetProcessDpiAwarenessContext(new IntPtr(-4));var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            Exception fault=null;
            app.Startup+=async delegate{try{await Run(args[0]);}catch(Exception e){fault=e;}finally{app.Shutdown();}};
            app.Run();if(fault!=null)throw fault;File.WriteAllText(Path.Combine(folder,"complete"),"");return 0;
        }catch(Exception ex){File.WriteAllText(Path.Combine(folder,"error-"+args.Length+".txt"),ex.ToString());return 1;}
    }
}
