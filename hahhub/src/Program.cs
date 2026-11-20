using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

internal static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [STAThread] public static int Main(string[] args){
        try{SetProcessDpiAwarenessContext(new IntPtr(-4));
            if(args.Length>0&&args[0]=="--stop-host"){StopHost();return 0;}
            if(args.Length>0&&args[0]=="--host"){bool own;using(var m=new Mutex(true,@"Local\HAHHAN.Glass.Host",out own)){
                if(own)using(var stop=new EventWaitHandle(false,EventResetMode.ManualReset,@"Local\HAHHAN.Glass.Stop"))
                using(var h=new Host())using(var gate=new System.Windows.Forms.Timer{Interval=2000}){
                    gate.Tick+=delegate{if(stop.WaitOne(0))System.Windows.Forms.Application.ExitThread();};gate.Start();System.Windows.Forms.Application.Run();
                }}return 0;}
            if(args.Length==2&&args[0]=="--self-test"){SelfTest(args[1]);return 0;}
            if(args.Length==2&&args[0]=="--snapshot"){new Application();new Studio().Snapshot(args[1]);return 0;}
            bool background=args.Length==1&&args[0]=="--tray",first;
            if((args.Length==0||background)&&Updates.RecoverIfNeeded())return 0;
            using(var open=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\HAHHAN.Studio.Open",out first)){
                if(!first){if(!background)open.Set();return 0;}
                var app=new Application();var studio=new Studio();
                app.MainWindow=studio.window;
                var activation=ThreadPool.RegisterWaitForSingleObject(open,delegate{
                    if(!app.Dispatcher.HasShutdownStarted)app.Dispatcher.BeginInvoke((Action)studio.OpenPanel);
                },null,Timeout.Infinite,false);
                try{return studio.Run(app,background,args.Length==2&&args[0]=="--updated"?args[1]:null);}finally{activation.Unregister(null);}
            }
        }catch(Exception e){Directory.CreateDirectory(Project.Data);File.WriteAllText(Path.Combine(Project.Data,"last-error.txt"),e.ToString());
            if(args.Length==0)MessageBox.Show(e.Message,"hahhub");return 1;}
    }
    public static void StopHost(){
        EventWaitHandle stop;if(EventWaitHandle.TryOpenExisting(@"Local\HAHHAN.Glass.Stop",out stop))using(stop)stop.Set();
    }
    public static void EnsureHost(){
        Mutex mutex;if(Mutex.TryOpenExisting(@"Local\HAHHAN.Glass.Host",out mutex)){mutex.Dispose();return;}
        using(var host=Process.Start(new ProcessStartInfo(Path.Combine(Project.Base,"HAHHAN Studio.exe"),"--host"){UseShellExecute=false,CreateNoWindow=true})){
            if(host==null)throw new IOException("无法启动交互层");
        }
    }
    static void Check(bool test,string message){if(!test)throw new InvalidDataException(message);}
    static System.Collections.Generic.HashSet<int> InkPixels(Glass glass){
        var pixels=new System.Collections.Generic.HashSet<int>();
        using(var image=new Bitmap(640,240)){using(var canvas=Graphics.FromImage(image))glass.Draw(canvas,640,240,0,0);
            for(int y=0;y<240;y++)for(int x=0;x<640;x++)if(image.GetPixel(x,y).A>18)pixels.Add(y*640+x);}
        return pixels;
    }
    static void CheckRefraction(Glass glass){
        var area=glass.SampleArea(1920,720);
        using(var source=new Bitmap(area.Width,area.Height))
        using(var first=new Bitmap(area.Width,area.Height))
        using(var second=new Bitmap(area.Width,area.Height)){
            for(int k=0;k<2;k++){
                using(var paint=Graphics.FromImage(source)){
                    paint.Clear(k==0?Color.Lime:Color.Magenta);
                    for(int x=0;x<area.Width;x+=10)paint.FillRectangle(k==0?Brushes.Magenta:Brushes.Lime,x,0,5,area.Height);
                }
                using(var paint=Graphics.FromImage(k==0?first:second))glass.Draw(paint,1920,720,area.X,area.Y,source,area);
            }
            int displaced=0;
            for(int y=0;y<area.Height;y++)for(int x=0;x<area.Width;x++){
                var a=first.GetPixel(x,y);var b=second.GetPixel(x,y);
                if(a.A<245||Math.Abs(a.R-a.G)<75||Math.Abs(b.R-b.G)<75)continue;
                if((a.R>a.G)!=(x%10<5)&&(a.R>a.G)!=(b.R>b.G))displaced++;
            }
            Check(displaced>25,"碎片像素来自错位后的实时底图，随底图更新");
            var key=glass.scans[30];int cx=(int)(key.x*1920)-area.X,cy=(int)(key.y*720)-area.Y;
            for(int k=0;k<2;k++){
                using(var paint=Graphics.FromImage(source)){
                    paint.Clear(k==0?Color.OrangeRed:Color.DodgerBlue);
                    paint.FillEllipse(Brushes.DimGray,cx-27,cy-27,54,54);
                }
                using(var paint=Graphics.FromImage(k==0?first:second)){
                    paint.Clear(Color.Transparent);glass.Draw(paint,1920,720,area.X,area.Y,source,area);
                }
            }
            int reflected=0;
            for(int y=cy-12;y<=cy+12;y++)for(int x=cx-12;x<=cx+12;x++){
                var warm=first.GetPixel(x,y);var cool=second.GetPixel(x,y);
                if(warm.R-warm.B>40&&cool.B-cool.R>40)reflected++;
            }
            Check(reflected>60,"相同灰色底图上的碎片须映出周围冷暖光");
        }
    }
    static void CheckBlackHole(Glass glass){
        double now=0;var a=glass.scans[30];var l=glass.scans[38];var hole=new BlackHole(glass.data.pitch,()=>now);
        using(hole){
            hole.Strike(a);var region=hole.Bounds(1920,720);
            Check(region.Contains((int)(a.x*1920),(int)(a.y*720))&&Math.Abs(region.Width-glass.data.pitch*720*BlackHole.DiameterInKeys)<2,"黑洞按校准轴心，为原范围的三倍");
            now=350;
            using(var bg=new Bitmap(region.Width,region.Height))
            using(var first=new Bitmap(region.Width,region.Height))
            using(var second=new Bitmap(region.Width,region.Height)){
                using(var d=Graphics.FromImage(bg)){
                    d.Clear(Color.SlateBlue);
                    for(int x=2;x<bg.Width;x+=11)d.FillRectangle(Brushes.Gold,x,0,4,bg.Height);
                }
                using(var d=Graphics.FromImage(first))hole.Draw(d,1920,720,region.X,region.Y,bg,region);
                now+=420;using(var d=Graphics.FromImage(second))hole.Draw(d,1920,720,region.X,region.Y,bg,region);
                int bent=0,moved=0,cx=region.Width/2,cy=region.Height/2;
                for(int y=0;y<region.Height;y++)for(int x=0;x<region.Width;x++){
                    double r=Math.Sqrt((x-cx)*(x-cx)+(y-cy)*(y-cy))/cx;
                    var c=first.GetPixel(x,y);
                    if(r<.53||r>.88||c.A<230)continue;
                    if(Math.Abs(c.R-bg.GetPixel(x,y).R)>32)bent++;
                    if(Math.Abs(c.R-second.GetPixel(x,y).R)>15)moved++;
                }
                Check(first.GetPixel(cx,cy).R<12&&first.GetPixel(0,0).A==0&&bent>400&&moved>120,"实际纹理被卷入且长按期间继续流动");
                using(var d=Graphics.FromImage(bg))d.Clear(Color.LimeGreen);
                using(var d=Graphics.FromImage(second)){d.Clear(Color.Transparent);hole.Draw(d,1920,720,region.X,region.Y,bg,region);}
                var live=second.GetPixel(cx,cy+cx*3/4);Check(live.G>live.R*2,"动态底图取色");
            }
            hole.Clear();now=0;hole.Strike(a);var single=hole.Bounds(1920,720);
            var neighbor=glass.scans[31];hole.Strike(neighbor);now=380;
            var pair=hole.Bounds(1920,720);
            Check(hole.LensCount==1&&pair.Width>single.Width*1.35,"相邻的 A 与 S 融合并扩大");
            using(var image=new Bitmap(pair.Width,pair.Height)){
                using(var paint=Graphics.FromImage(image))hole.Draw(paint,1920,720,pair.X,pair.Y,null,Rectangle.Empty);
                var center=image.GetPixel((int)((a.x+neighbor.x)*960)-pair.X,(int)(a.y*720)-pair.Y);
                Check(center.A==255&&center.R<10,"两键中点成为合并洞口内核");
            }
            hole.Strike(l);hole.Bounds(1920,720);Check(hole.LensCount==2,"远键保留独立洞口");
            hole.Release(neighbor);hole.Release(l);now+=800;hole.Bounds(1920,720);
            Check(hole.LensCount==1,"相邻键松手后仍有一个洞口");
            int early=hole.Bounds(1920,720).Width;now+=1400;Check(hole.Bounds(1920,720).Width>early,"长按持续扩张");
            now=4800;Check(hole.Bounds(1920,720)==new Rectangle(0,0,1920,720)&&hole.Parked&&hole.SampleArea(1920,720).IsEmpty,"完全吞噬后停止采样");
            using(var image=new Bitmap(1920,720)){
                using(var paint=Graphics.FromImage(image))hole.Draw(paint,1920,720,0,0,null,Rectangle.Empty);
                foreach(int x in new[]{0,1919})foreach(int y in new[]{0,719})Check(image.GetPixel(x,y).ToArgb()==Color.FromArgb(2,3,10).ToArgb(),"最远四角全部吞没");
            }
            hole.Release(a);now+=100;hole.Bounds(1920,720);Check(!hole.Parked&&!hole.SampleArea(1920,720).IsEmpty,"松手恢复采样");
            now+=600;Check(hole.Bounds(1920,720).IsEmpty&&!hole.Active,"黑洞退出后休眠");
            hole.Strike(a);hole.Release(a);now+=100;Check(hole.Active&&!hole.Bounds(1920,720).IsEmpty,"短按可见");
            now+=700;Check(hole.Bounds(1920,720).IsEmpty,"短按收拢");
        }
    }
    static void SelfTest(string output){
        var video=VideoLink.Parse("https://www.bilibili.com/video/BV1RqHY6kEHA/?spm_id_from=test&p=2&t=13");
        Check(video.Bilibili&&video.Address.Host=="player.bilibili.com"&&video.Address.Query.Contains("p=2&t=13")&&!video.Address.Query.Contains("spm"),"B 站链接保留分 P 和时间");
        Check(!VideoLink.Parse("https://example.org/a.mp4?token=1").Bilibili,"媒体直链");
        foreach(string invalid in new[]{"file:///a.mp4","https://www.bilibili.com.evil.invalid/video/BV1RqHY6kEHA/","https://www.bilibili.com/video/abc"}){
            bool rejected=false;try{VideoLink.Parse(invalid);}catch(ArgumentException){rejected=true;}Check(rejected,"无效视频链接");
        }
        string probe="probe-"+Guid.NewGuid().ToString("N");
        using(var lease=new VideoLease(probe)){
            Check(VideoLease.Active(probe)&&!VideoLease.Active(probe+"2"),"视频互斥按显示器隔离");
            bool busy=false;try{using(new VideoLease(probe)){} }catch(InvalidOperationException){busy=true;}Check(busy,"同屏独占");
        }
        Check(!VideoLease.Active(probe),"结束播放后释放屏幕");
        Check(Host.HasKeyboardPayload(40,24)&&!Host.HasKeyboardPayload(39,24)&&!Host.HasKeyboardPayload(uint.MaxValue,24),"原始键盘包按实际复制字节数校验");
        double clock=0;var g=new Glass(()=>clock);Check(g.scans.Count==83,"物理映射，Fn 由固件处理");
        var a=g.scans[30];var l=g.scans[38];Check(a.code=="KeyA"&&l.code=="KeyL"&&a.x<l.x&&a.y==l.y,"键位中心");
        var selected=new OutputChoice{Id="keyboard",Label="屏幕 3",Enabled=true};
        var main=new Output{Id="main",Label="屏幕 3",Bounds=new Rectangle(0,0,2000,1000)};
        var keyboard=new Output{Id="keyboard",Label="屏幕 2",Bounds=new Rectangle(-1920,0,1920,720)};
        var topology=new[]{main,keyboard};
        var persisted=Project.Json.Deserialize<OutputChoice>(Project.Json.Serialize(selected));
        Check(persisted.Find(topology)==keyboard&&persisted.Find(new[]{main})==null,"设备重新编号和断连");
        keyboard.Mirrored=true;Check(selected.Find(topology)==null,"镜像屏不得单独启用");keyboard.Mirrored=false;
        selected.Enabled=false;Check(selected.Find(topology)==null,"暂停");selected.Id="main";selected.Enabled=true;
        Check(selected.Find(topology)==main&&new OutputChoice().Find(topology)==null,"目标切换与首次启动");
        var legacy=Project.Json.Deserialize<Tuning>("{\"density\":96,\"spread\":132,\"heal\":3050}");
        Check(legacy.Valid()&&legacy.delay==800&&legacy.crackSize==0&&legacy.heal==3050,"旧设置补上默认裂缝大小与延迟");
        Check(g.data.largeShapes.Zip(g.data.shapes,(large,small)=>large.segments.Length*1.5<small.segments.Length).All(x=>x),"大裂缝具有独立稀疏拓扑");
        g.tuning.heal=500;g.Strike(a);
        using(var first=new Bitmap(80,80)){
            int x=(int)(a.x*1920)-40,y=(int)(a.y*720)-40;float furthest=0;
            using(var canvas=Graphics.FromImage(first))g.Draw(canvas,1920,720,x,y);
            for(int j=0;j<80;j++)for(int i=0;i<80;i++)if(first.GetPixel(i,j).A>24)
                furthest=Math.Max(furthest,(float)Math.Sqrt(Math.Pow(i+x-a.x*1920,2)+Math.Pow(j+y-a.y*720,2)));
            Check(Math.Abs(furthest-g.data.axisSize*.25)<2,"首帧由校准轴宽的一半限定接触直径");
        }
        clock=180;var ra=g.Bounds(1920,720);Check(ra.Contains((int)(a.x*1920),(int)(a.y*720))&&ra.Width<600,"A 局部画布");
        using(var b=new Bitmap(ra.Width,ra.Height)){using(var draw=Graphics.FromImage(b))g.Draw(draw,1920,720,ra.X,ra.Y);
            int visible=0;for(int y=0;y<b.Height;y+=2)for(int x=0;x<b.Width;x+=2)if(b.GetPixel(x,y).A>8)visible++;Check(visible>75,"有效像素");}
        var originalInk=InkPixels(g);var firstBounds=ra;clock=1500;var grown=g.Bounds(1920,720);Check(grown.Width>firstBounds.Width&&g.Active,"长按越过愈合时间仍向外生长");
        CheckRefraction(g);
        var heldInk=InkPixels(g);
        Check(heldInk.Count>originalInk.Count&&originalInk.Count(p=>heldInk.Contains(p))>originalInk.Count*.97,"长按只延伸裂尖，原像素位置稳定");
        g.Release(a);clock+=450;var beforeRepeat=g.Bounds(1920,720);g.Strike(a);Check(g.Bounds(1920,720)==beforeRepeat,"连击保留原裂纹范围");
        Check(heldInk.SetEquals(InkPixels(g)),"原位复击不重抽图样");
        g.Strike(l);var both=g.Bounds(1920,720);Check(both.Contains((int)(l.x*1920),(int)(l.y*720))&&both.Width>ra.Width,"多键叠加");
        clock+=1100;int middle=(int)((a.x+l.x)*320);
        Check(InkPixels(g).Any(p=>Math.Abs(p%640-middle)<2),"两键之间出现连续连接裂纹");
        g.Release(l);g.Release(a);
        var released=InkPixels(g);clock+=g.tuning.delay;
        Check(released.SetEquals(InkPixels(g)),"延迟结束前主裂纹和连线保持原状");
        clock+=250;var retracting=InkPixels(g);clock+=100;var inner=InkPixels(g);
        Check(inner.Count>0&&inner.Count<retracting.Count&&inner.Count(p=>retracting.Contains(p))>inner.Count*.94,"愈合沿原路径反向收回");
        g.Strike(a);Check(inner.SetEquals(InkPixels(g)),"半途复击从当前裂尖接续");g.Release(a);
        clock+=g.tuning.delay+550;Check(g.Bounds(1920,720).IsEmpty&&!g.Active,"愈合后休眠");
        var frames=new System.Collections.Generic.Dictionary<int,int>();var cpuMs=new System.Collections.Generic.Dictionary<string,double>();int idle=0;
        using(var host=new Host(true)){
            foreach(int fps in new[]{16,24,30,60}){int start=host.Frames;using(var process=Process.GetCurrentProcess()){
                var initial=process.TotalProcessorTime;host.ProbeStrike(fps);Pump(1250);process.Refresh();cpuMs[fps.ToString()]=(process.TotalProcessorTime-initial).TotalMilliseconds;}
                frames[fps]=host.Frames-start;Check(frames[fps]>=3&&frames[fps]<=fps+1&&!host.Drawing,"原生限帧与停止");}
            int before=host.Frames;Pump(1100);idle=host.Frames-before;Check(idle==0,"空闲绘制");
        }
        CheckBlackHole(g);g.Dispose();File.WriteAllText(output,Project.Json.Serialize(new{layout=g.data.keys.Length,reportablePositions=g.scans.Count,A=new[]{a.x,a.y},L=new[]{l.x,l.y},singleDirtyPixels=ra.Width*ra.Height,screenPixels=1920*720,healed=true,connectedOutputs=Displays.Connected().Select(s=>new{s.Label,s.Mirrored,s.Usable}).ToArray(),routingChecks=true,retainedInk=true,reverseHealing=true,delayedHealing=true,distinctSizes=true,reflectedAmbient=true,liveTexture=true,contactDiameter=g.data.axisSize/2,heldGrowth=true,connectedDamage=true,blackHole=true,adjacentMerge=true,fullSwallow=true,framesPerOneSecond=frames.ToDictionary(x=>x.Key.ToString(),x=>x.Value),cpuMillisecondsPer1250msProbe=cpuMs,idleFrames=idle}));
    }
    static void Pump(int ms){using(var scope=new System.Windows.Forms.ApplicationContext())using(var timer=new System.Windows.Forms.Timer{Interval=ms}){
        timer.Tick+=delegate{timer.Stop();scope.ExitThread();};timer.Start();System.Windows.Forms.Application.Run(scope);}}
}
