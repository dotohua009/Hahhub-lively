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
    [StructLayout(LayoutKind.Sequential)]struct Rect {public int left,top,right,bottom;}
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")]static extern bool GetWindowDisplayAffinity(IntPtr window,out uint affinity);
    [DllImport("user32.dll")]static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
    [DllImport("user32.dll")]static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll")]static extern bool CloseDesktop(IntPtr desktop);
    static long SampleHash(Bitmap frame){
        long hash=17;
        for(int y=1;y<9;y++)for(int x=1;x<12;x++)hash=unchecked(hash*31+frame.GetPixel(x*frame.Width/13,y*frame.Height/10).ToArgb());
        return hash;
    }
    static int CheckVideoPicture(NativeWindow surface,IntPtr player,Rectangle screen){
        Rect r;uint affinity;
        if(!GetWindowRect(surface.Handle,out r)||!GetWindowDisplayAffinity(surface.Handle,out affinity)||affinity!=0||
            r.right-r.left>=screen.Width||r.bottom-r.top>=screen.Height)
            throw new InvalidDataException("A local hit created a display-wide or capture-protected video overlay.");
        var effect=Rectangle.FromLTRB(r.left-screen.Left,r.top-screen.Top,r.right-screen.Left,r.bottom-screen.Top);
        var image=(Bitmap)Field(Field(surface,"buffer"),"bitmap");
        int transparent=0,ink=0;
        for(int y=effect.Top;y<effect.Bottom;y+=3)for(int x=effect.Left;x<effect.Right;x+=3){
            var alpha=image.GetPixel(x,y).A;
            if(alpha==0)transparent++;else if(alpha>30)ink++;
        }
        if(transparent<12||ink<8)throw new InvalidDataException("The presented area lacks a transparent background or visible key effect.");
        var desktop=OpenInputDesktop(0,false,0x101);
        if(desktop==IntPtr.Zero)return -1; // Screen capture is unavailable on the secure desktop.
        CloseDesktop(desktop);
        using(var direct=new Bitmap(screen.Width,screen.Height))
        using(var output=new Bitmap(screen.Width,screen.Height)){
            using(var g=Graphics.FromImage(direct)){
                var dc=g.GetHdc();try{if(!PrintWindow(player,dc,3))throw new InvalidDataException("Video frame unavailable");}
                finally{g.ReleaseHdc(dc);}
            }
            using(var g=Graphics.FromImage(output))g.CopyFromScreen(screen.Location,Point.Empty,screen.Size);
            int checkedPoints=0,matched=0;
            for(int y=1;y<=6;y++)for(int x=1;x<=12;x++){
                var p=new Point(screen.Width*x/13,screen.Height*y/7);if(effect.Contains(p))continue;
                var expected=direct.GetPixel(p.X,p.Y);if(Math.Max(expected.R,Math.Max(expected.G,expected.B))<35)continue;
                checkedPoints++;var actual=output.GetPixel(p.X,p.Y);
                if(Math.Abs(actual.R-expected.R)+Math.Abs(actual.G-expected.G)+Math.Abs(actual.B-expected.B)<100)matched++;
            }
            if(checkedPoints<8||matched<checkedPoints*.7)throw new InvalidDataException("Video outside the effect differs from the player: "+matched+"/"+checkedPoints);
            return matched;
        }
    }
    sealed class FrameTimes : NativeWindow, IDisposable {
        readonly List<double> gaps=new List<double>();
        readonly Stopwatch clock=Stopwatch.StartNew();
        double last;
        public FrameTimes(IntPtr host){AssignHandle(host);}
        protected override void WndProc(ref Message m){
            base.WndProc(ref m);
            if(m.Msg!=0x8000+85)return;
            double now=clock.Elapsed.TotalMilliseconds;
            if(last>0)gaps.Add(now-last);
            last=now;
        }
        public object Result(string effect){
            if(gaps.Count<2)throw new InvalidDataException("Too few animation frames");
            gaps.Sort();
            return new{effect,frames=gaps.Count,fps=Math.Round(1000/gaps.Average(),1),
                p95Ms=Math.Round(gaps[(int)Math.Ceiling(gaps.Count*.95)-1],1),maxMs=Math.Round(gaps.Last(),1)};
        }
        public void Dispose(){ReleaseHandle();}
    }
    public void Run(string dir,string report,bool checkVideo,bool pacing){
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
            var ink=Field(host,"ink");if(pacing)effects.Add(ink);
            foreach(var effect in effects){
                hostType.GetField("bounds",Flags).SetValue(host,screen);hostType.GetField("effect",Flags).SetValue(host,effect);
                hostType.GetField("frameInterval",Flags).SetValue(host,1000.0/60);
                if(pacing){
                    using(var timing=new FrameTimes(((NativeWindow)host).Handle)){
                        Call(host,"Strike",key);
                        var hold=Stopwatch.StartNew();
                        int taps=1;
                        var inkKeys=new[]{30,33,38,31,37,32};
                        while(hold.ElapsedMilliseconds<3200){
                            Application.DoEvents();Thread.Sleep(1);
                            if(effect==ink&&hold.ElapsedMilliseconds>=taps*100){
                                var tap=((IDictionary)Field(glass,"scans"))[inkKeys[taps%inkKeys.Length]];if(!Object.ReferenceEquals(tap,key)){Call(host,"Strike",tap);Call(host,"ReleaseKey",tap);}taps++;
                            }
                        }
                        results.Add(timing.Result(effect.GetType().Name));
                    }
                    Call(host,"Idle");continue;
                }
                Call(host,"Strike",key);
                var sw=Stopwatch.StartNew();int start=(int)hostType.GetProperty("Frames").GetValue(host,null),reads=0,changed=0;long last=-1;
                var source=Field(Field(host,"surface"),"wallpaper");
                int clearVideoPoints=0;
                var sampleAt=0.0;while(sw.Elapsed.TotalMilliseconds<2000){
                    Application.DoEvents();Thread.Sleep(1);
                    if(checkVideo&&clearVideoPoints==0&&sw.ElapsedMilliseconds>=280)
                        clearVideoPoints=CheckVideoPicture(surface,player,screen);
                    if(sw.Elapsed.TotalMilliseconds<sampleAt)continue;sampleAt+=160;
                    var b=(Bitmap)Field(source,"buffer");if(b==null)continue;
                    long hash;
                    if(checkVideo){
                        var frames=Field(source,"videoFrames");if(frames==null)continue;
                        lock(Field(frames,"gate")){
                            if(!(bool)Field(frames,"ready"))continue;
                            hash=SampleHash((Bitmap)Field(Field(frames,"front"),"bitmap"));
                        }
                    }else hash=SampleHash(b);
                    reads++;
                    if(last!=-1&&last!=hash)changed++;last=hash;
                }
                double fps=((int)hostType.GetProperty("Frames").GetValue(host,null)-start)/sw.Elapsed.TotalSeconds;
                if(checkVideo&&(reads<3||changed<1||(IntPtr)Field(source,"lastPlayer")!=player||GetWindow(surface.Handle,2)!=player))
                    throw new InvalidDataException("Missing live video frames or overlay above player: "+effect.GetType().Name+" reads="+reads+" changed="+changed);
                results.Add(new{effect=effect.GetType().Name,fps=Math.Round(fps,1),sourceSamples=reads,changedSamples=changed,clearVideoPoints});
                if(checkVideo&&effect==glass){
                    Call(host,"ReleaseKey",key);
                    var tuning=Field(glass,"tuning");
                    double limit=Convert.ToDouble(Field(tuning,"delay"))+Convert.ToDouble(Field(tuning,"heal"))+2000;
                    sw.Restart();
                    while((bool)Field(surface,"shown")&&sw.ElapsedMilliseconds<limit){Application.DoEvents();Thread.Sleep(2);}
                    if((bool)Field(surface,"shown")||(bool)hostType.GetProperty("Drawing").GetValue(host,null))
                        throw new InvalidDataException("Fracture did not release the video after healing.");
                }
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
        try{bool pacing=args.Length>2&&args[2]=="--pacing";test.Run(args[0],args[1],pacing||(args.Length>2&&args[2]=="--video"),pacing);}
        catch(Exception error){Console.Error.WriteLine(error.GetBaseException().Message);Environment.ExitCode=1;}
        finally{AppDomain.Unload(domain);}
    }
}
