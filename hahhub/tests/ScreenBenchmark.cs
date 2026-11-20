// Runs the installed host's rendering loop against its selected display, without registering keyboard input.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;
public class ScreenBenchmark : MarshalByRefObject {
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
    static object Field(object o,string n){return o.GetType().GetField(n,Flags).GetValue(o);}
    static object Call(object o,string n,params object[] a){return o.GetType().InvokeMember(n,Flags|BindingFlags.InvokeMethod,null,o,a);}
    [DllImport("user32.dll")]static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr window,uint direction);
    public void Run(string dir,string report,bool checkVideo){
        var asm=Assembly.LoadFrom(Path.Combine(dir,"HAHHAN Studio.exe"));var hostType=asm.GetType("Host");
        var project=asm.GetType("Project");var choice=project.GetMethod("ReadChoice").Invoke(null,null);
        var screens=asm.GetType("Displays").GetMethod("Connected").Invoke(null,null);
        var target=Call(choice,"Find",screens);if(target==null)throw new InvalidOperationException("No selected display.");
        var screen=(Rectangle)Field(target,"Bounds");var results=new List<object>();bool parked=false,recovered=false;int corners=0;
        using(var host=(IDisposable)Activator.CreateInstance(hostType,Flags,null,new object[]{true},null)){
            var surface=(NativeWindow)Field(host,"surface");
            IntPtr player=IntPtr.Zero;
            if(checkVideo){
                hostType.GetField("probe",Flags).SetValue(host,false);
                Call(host,"Discover");player=(IntPtr)Field(surface,"VideoPlayer");
                if(player==IntPtr.Zero||!(bool)Field(host,"registered")||(bool)Field(surface,"sampleWallpaper"))
                    throw new InvalidDataException("Host did not switch from wallpaper to video");
                Call(host,"Enable",false);hostType.GetField("probe",Flags).SetValue(host,true);
            }
            var glass=Field(host,"glass");var key=((IDictionary)Field(glass,"scans"))[30];
            var effects=new List<object>{glass,Field(host,"hole")};
            foreach(var effect in effects){
                hostType.GetField("bounds",Flags).SetValue(host,screen);hostType.GetField("effect",Flags).SetValue(host,effect);
                hostType.GetField("frameInterval",Flags).SetValue(host,1000.0/60);
                Call(host,"Strike",key);
                var sw=Stopwatch.StartNew();int start=(int)hostType.GetProperty("Frames").GetValue(host,null),reads=0,changed=0;long last=-1;
                var source=Field(Field(host,"surface"),"wallpaper");
                var sampleAt=0.0;while(sw.Elapsed.TotalMilliseconds<2000){
                    Application.DoEvents();Thread.Sleep(1);
                    if(sw.Elapsed.TotalMilliseconds<sampleAt)continue;sampleAt+=160;
                    var b=(Bitmap)Field(source,"buffer");if(b==null)continue;reads++;
                    long hash=17;for(int y=1;y<9;y++)for(int x=1;x<12;x++)hash=unchecked(hash*31+b.GetPixel(x*b.Width/13,y*b.Height/10).ToArgb());
                    if(last!=-1&&last!=hash)changed++;last=hash;
                }
                double fps=((int)hostType.GetProperty("Frames").GetValue(host,null)-start)/sw.Elapsed.TotalSeconds;
                if(checkVideo&&(reads<3||changed<1||(IntPtr)Field(source,"lastPlayer")!=player||GetWindow(surface.Handle,2)!=player))
                    throw new InvalidDataException("Missing live video frames or overlay above player: "+effect.GetType().Name+" reads="+reads+" changed="+changed);
                results.Add(new{effect=effect.GetType().Name,fps=Math.Round(fps,1),sourceSamples=reads,changedSamples=changed});
                Call(host,"Idle");
                if(effect.GetType().Name=="BlackHole"){
                    Call(host,"Strike",key);var other=((IDictionary)Field(glass,"scans"))[31];Call(host,"Strike",other);
                    var hold=Stopwatch.StartNew();while(hold.ElapsedMilliseconds<5150){Application.DoEvents();Thread.Sleep(2);}
                    parked=!(bool)hostType.GetProperty("Drawing").GetValue(host,null);
                    var dib=Field(surface,"buffer");var image=(Bitmap)Field(dib,"bitmap");
                    foreach(var x in new[]{0,image.Width-1})foreach(var y in new[]{0,image.Height-1})
                        if(image.GetPixel(x,y).ToArgb()==Color.FromArgb(2,3,10).ToArgb())corners++;
                    Call(host,"ReleaseKey",key);Call(host,"ReleaseKey",other);
                    hold.Restart();while(hold.ElapsedMilliseconds<850){Application.DoEvents();Thread.Sleep(2);}
                    recovered=!(bool)hostType.GetProperty("Drawing").GetValue(host,null)&&!(bool)Field(surface,"shown");
                    if(!parked||!recovered||corners!=4)throw new InvalidDataException("Swallow or recovery failed");
                }
            }
        }
        File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{display=Field(target,"Label"),size=new[]{screen.Width,screen.Height},video=checkVideo,renderCap=60,fullDarkCorners=corners,parked=parked,recovered=recovered,results=results}));
    }
    [STAThread]public static void Main(string[] args){SetProcessDpiAwarenessContext(new IntPtr(-4));var domain=AppDomain.CreateDomain("bench",null,new AppDomainSetup{ApplicationBase=args[0]});
        var test=(ScreenBenchmark)domain.CreateInstanceFromAndUnwrap(typeof(ScreenBenchmark).Assembly.Location,typeof(ScreenBenchmark).FullName);
        try{test.Run(args[0],args[1],args.Length>2&&args[2]=="--video");}
        catch(Exception error){Console.Error.WriteLine(error.GetBaseException().Message);Environment.ExitCode=1;}
        finally{AppDomain.Unload(domain);}
    }
}
