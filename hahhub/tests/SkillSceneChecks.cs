using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

// Compile alongside src with /main:SkillSceneChecks. No physical input or display ownership required.
internal static class SkillSceneChecks {
    const int W=1920,H=720;
    static double time;
    static readonly List<object> records=new List<object>();
    static void Require(bool ok,string msg){if(!ok)throw new Exception(msg);}
    static HashSet<int> SilkPixels(SilkColony scene,LayoutKey key,double at){
        time=at;scene.Bounds(W,H);
        var crop=Rectangle.Ceiling(RectangleF.Inflate(key.cap,52,52));
        var pixels=new HashSet<int>();
        using(var image=new Bitmap(crop.Width,crop.Height,PixelFormat.Format32bppPArgb)){
            using(var g=Graphics.FromImage(image)){
                g.SmoothingMode=SmoothingMode.AntiAlias;g.TranslateTransform(-crop.X,-crop.Y);
                scene.DrawSilk(g);
            }
            for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)
                if(image.GetPixel(x,y).A>16)pixels.Add(y*crop.Width+x);
        }
        return pixels;
    }
    static void CheckWebGrowth(Glass glass){
        var key=glass.data.keys.First(k=>k.code=="KeyA");
        var other=glass.data.keys.First(k=>k.code=="KeyL");
        using(var web=new SilkColony(glass.data,()=>time)){
            time=0;web.Strike(key);
            Require(SilkPixels(web,key,0).Count==0,"web visible on first frame");
            var start=SilkPixels(web,key,130);
            var supports=SilkPixels(web,key,720);
            var middle=SilkPixels(web,key,1700);
            var full=SilkPixels(web,key,6500);
            Require(start.Count>0 && start.Count*3<supports.Count && supports.Count<middle.Count && middle.Count<full.Count*.9,"silk growth stages");
            Require(start.Count(full.Contains)>=start.Count*.94 && middle.Count(full.Contains)>=middle.Count*.94,"new silk must preserve earlier thread paths");
            web.Strike(key);web.Strike(other);
            Require(SilkPixels(web,key,6500).SetEquals(full) && SilkPixels(web,other,6500).Count==0,"new and repeated key timing");
            var neighboring=SilkPixels(web,other,6660);
            Require(neighboring.Count>0 && neighboring.Count<supports.Count && SilkPixels(web,key,6660).SetEquals(full),"independent webs");
            web.Clear();time=0;web.Strike(key);time=45;web.Release(key);
            var tap=SilkPixels(web,key,205);
            Require(tap.Count>0 && tap.Count<supports.Count/2,"tap grew a complete web");
            Require(SilkPixels(web,key,460).IsSubsetOf(tap) && SilkPixels(web,key,900).Count==0,"tap kept weaving after release");
            Console.WriteLine("Silk pixels at 0/130/720/1700/6500 ms: 0/{0}/{1}/{2}/{3}; tap: {4}",start.Count,supports.Count,middle.Count,full.Count,tap.Count);
        }
    }
    // Read the production overlay in board coordinates, including the gaps between caps.
    static PointF PoolShape(GlitterFlow flow,RectangleF tank){
        var world=flow.RenderWorld;
        using(var frame=new Bitmap(world.Width,world.Height)){
            using(var g=Graphics.FromImage(frame))flow.Draw(g,W,H,world.X,world.Y,null,Rectangle.Empty);
            int wet=0;double sumX=0;
            for(int y=0;y<40;y++)for(int x=0;x<100;x++){
                var u=(x+.5f)/100;
                int px=(int)(tank.Left+u*tank.Width)-world.X,py=(int)(tank.Top+(y+.5f)*tank.Height/40)-world.Y;
                if(frame.GetPixel(px,py).A>27){wet++;sumX+=u;}
            }
            Require(frame.GetPixel(0,0).A==0,"liquid outside the keyboard");
            float coverage=wet/4000f;
            Require(Math.Abs(flow.FillFraction-.5)<.001,"conservation of liquid volume");
            Require(coverage>.43 && coverage<.60,"visible pool footprint "+coverage);
            return new PointF((float)(sumX/wet),coverage);
        }
    }
    static void CheckChannelForks(GlitterFlow flow,GlassData board,RectangleF tank){
        // A right-going current must separate before a switch and turn back together
        // behind it. Probe the actual stream field at the four shoulders of each housing.
        var type=typeof(GlitterFlow);var flags=BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
        int w=(int)type.GetField("W",flags).GetRawConstantValue(),h=(int)type.GetField("H",flags).GetRawConstantValue();
        var psi=(float[])type.GetField("horizontal",flags).GetValue(flow);
        int forks=0,interior=0;
        foreach(var k in board.keys){
            var b=k.housing;
            if(!tank.Contains(RectangleF.Inflate(b,b.Width*.44f,b.Height*.44f)))continue;
            int correct=0;
            for(int side=-1;side<=1;side+=2)for(int flank=-1;flank<=1;flank+=2){
                float px=b.X+b.Width*(.5f+.42f*side),py=b.Y+b.Height*(.5f+.50f*flank);
                int x=(int)((px-tank.X)*w/tank.Width),y=(int)Math.Round((py-tank.Y)*h/tank.Height);
                int j=y*(w+1)+x;float cross=psi[j]-psi[j+1];
                if(cross*side*flank<-.035f)correct++;
            }
            interior++;if(correct==4)forks++;
        }
        Require(interior>=30&&forks>=interior*.9,"split/rejoin at calibrated switch shoulders "+forks+"/"+interior);
        Console.WriteLine("Split and rejoin in both branches at {0}/{1} interior housings",forks,interior);
    }
    static void CheckGlitterReflection(Glass glass,RectangleF tank){
        using(var scene=new GlitterFlow(glass.data,()=>time))
        using(var source=new Bitmap(W,H,PixelFormat.Format32bppRgb))
        using(var frame=new Bitmap(W,H,PixelFormat.Format32bppPArgb))
        using(var sg=Graphics.FromImage(source))
        using(var g=Graphics.FromImage(frame)){
            time=0;scene.Strike(glass.scans[30]);scene.Bounds(W,H);
            int mid=(int)(tank.X+tank.Width*.5f);
            int[] times={0,16,110};double[] contrast=new double[3];
            for(int pass=0;pass<times.Length;pass++){
                time=times[pass];scene.Bounds(W,H);
                var sample=scene.SampleArea(W,H);
                Require(sample.IsEmpty==(pass==1),"reflection capture cadence");
                sg.Clear(pass==0?Color.Blue:Color.Red);
                sg.FillRectangle(pass==0?Brushes.Red:Brushes.Blue,0,0,mid,H);
                g.Clear(Color.Transparent);
                using(var crop=sample.IsEmpty?null:source.Clone(sample,source.PixelFormat))
                    scene.Draw(g,W,H,0,0,crop,sample);
                int hits=0;double difference=0;
                for(int y=(int)(tank.Top+tank.Height*.77f);y<Math.Min(H,tank.Bottom-5);y+=7)
                for(int x=W/6;x<5*W/6;x+=11){
                    var p=frame.GetPixel(x,y);if(p.A<44)continue;
                    difference+=(x<mid?1:-1)*(p.R-p.B);hits++;
                }
                Require(hits>500,"visible reflected liquid");contrast[pass]=difference/hits;
            }
            Require(contrast[0]>28 && contrast[1]>28 && contrast[2]<-28,"reflected source regions did not move with the input frame: "+string.Join(", ",contrast));
            Console.WriteLine("Reflection colour contrast at 0/16/110 ms: {0:F1}/{1:F1}/{2:F1}",contrast[0],contrast[1],contrast[2]);
        }
    }
    static void CheckGlitter(Glass glass){
        var left=glass.data.keys.First(k=>k.code=="KeyA");
        var right=glass.data.keys.First(k=>k.code=="Enter");
        var tank=glass.data.keys.Select(k=>k.cap).Aggregate(RectangleF.Union);
        using(var flow=new GlitterFlow(glass.data,()=>time)){
            flow.RenderWorld=KeyboardStage.World(glass.data);time=0;
            for(int i=0;i<=150;i++){
                time=i*1000.0/60;
                if(i%10==0)flow.Strike(left);
                if(i%10==4)flow.Release(left);
                if(i==143)flow.Strike(right);
                if(i==145)flow.Release(right);
                flow.Bounds(W,H);
            }
            CheckChannelForks(flow,glass.data,tank);
            var first=PoolShape(flow,tank);
            Require(first.X<.34,"repeated left taps should draw the shared pool left "+first);
            flow.Strike(right);flow.Strike(right);flow.Bounds(W,H);
            Require(Math.Abs(PoolShape(flow,tank).X-first.X)<.005,"direction jumped on right keydown");
            flow.Release(left);
            for(int i=151;i<=370;i++){
                time=i*1000.0/60;
                if(i%10==4)flow.Release(right);
                if(i%10==0)flow.Strike(right);
                flow.Bounds(W,H);
                if(i==195)PoolShape(flow,tank);
            }
            var second=PoolShape(flow,tank);
            Require(second.X>.64,"sustained right activity should carry the pool across the board "+second);
            flow.Release(right);
            time+=3300;Require(flow.Bounds(W,H).IsEmpty&&!flow.Active&&flow.SampleArea(W,H).IsEmpty,"idle liquid requested rendering or capture");
            Console.WriteLine("Shared glitter pool: fill {0:P1}/{1:P1}, centroid X {2:P1}/{3:P1}",first.Y,second.Y,first.X,second.X);
        }
        CheckGlitterReflection(glass,tank);
    }
    static void CheckNavigation(){
        var app=new Application();var studio=new Studio();
        var show=typeof(Studio).GetMethod("ShowEffect",BindingFlags.NonPublic|BindingFlags.Instance);
        foreach(var entry in SkillLibrary.Entries){
            show.Invoke(studio,new object[]{entry.id});
            Require(((RadioButton)studio.window.FindName(entry.tile)).IsChecked==true,entry.title+" selection");
            Require((((StackPanel)studio.window.FindName("FusePanel")).Visibility==Visibility.Visible)==(entry.id==VoxelFuse.Id),"fuse settings visibility");
            Require(((TextBlock)studio.window.FindName("EffectTitle")).Text==UiText.T(entry.title),"page title");
            Require((((CheckBox)studio.window.FindName("Enhanced")).Visibility==Visibility.Visible)==(entry.id==BlackHole.Id||entry.id==InkSplash.Id),"preset visibility");
        }
        studio.window.Close();app.Shutdown();
    }
    static void Backdrop(Graphics g,Glass glass,LayoutKey key,int frame){
        float x=key.x*W,y=key.y*H;
        using(var fill=new LinearGradientBrush(new Rectangle(0,0,W,H),Color.FromArgb(16,27,49),Color.FromArgb(64,84,109),22f))g.FillRectangle(fill,0,0,W,H);
        for(int j=-4;j<=6;j++){
            float left=x-300+j*90+(frame%180)*1.5f;
            using(var pen=new Pen(Color.FromArgb(105,j%2==0?Color.FromArgb(102,195,214):Color.FromArgb(240,144,122)),j%2==0?12:5)){
                g.DrawBezier(pen,left-100,y+180,left+5,y+30,left-38,y-25,left+150,y-185);
            }
        }
        using(var pen=new Pen(Color.FromArgb(48,190,208,227),1))
        using(var font=new Font("Segoe UI",9))
        using(var caption=new SolidBrush(Color.FromArgb(140,211,224,239))){
            foreach(var k in glass.data.keys){
                var c=k.cap;g.DrawRectangle(pen,c.X,c.Y,c.Width,c.Height);
                if(k.code.StartsWith("Key"))g.DrawString(k.code.Substring(3),font,caption,c.X+6,c.Y+4);
            }
        }
    }
    static int Different(Bitmap a,Bitmap b,PointF center){
        int n=0,x=(int)(center.X*W),y=(int)(center.Y*H);
        for(int j=-16;j<16;j+=2)for(int i=-16;i<16;i+=2)if(a.GetPixel(x+i,y+j)!=b.GetPixel(x+i,y+j))n++;
        return n;
    }
    static void CheckPresets(Glass glass){
        var key=glass.scans[33];
        foreach(bool hole in new[]{false,true}){
            using(var first=new Bitmap(W,H))using(var second=new Bitmap(W,H)){
                for(int j=0;j<2;j++){
                    time=0;
                    IKeyEffect effect=hole?(IKeyEffect)new BlackHole(glass.data.pitch,()=>time){enhanced=j==1}:new InkSplash(glass.data,()=>time,new Random(7)){enhanced=j==1};
                    try{
                        effect.Strike(key);time=390;effect.Bounds(W,H);
                        using(var g=Graphics.FromImage(j==0?first:second))effect.Draw(g,W,H,0,0,null,Rectangle.Empty);
                    }finally{effect.Clear();}
                }
                int differences=0,cx=(int)(key.x*W),cy=(int)(key.y*H);
                for(int y=cy-80;y<cy+80;y+=2)for(int x=cx-80;x<cx+80;x+=2)if(first.GetPixel(x,y)!=second.GetPixel(x,y))differences++;
                Require(differences>25,"upgraded preset must render a distinct frame");
            }
        }
    }
    static void CheckPrism(Glass glass){
        var key=glass.scans[33];using(var scene=new Prism(glass.data,()=>time)){
        time=0;scene.Strike(key);time=410;scene.Bounds(W,H);var sample=scene.SampleArea(W,H);
        using(var a=new Bitmap(W,H))using(var b=new Bitmap(W,H))
        using(var crop=new Bitmap(sample.Width+31,sample.Height+17,PixelFormat.Format32bppRgb)){
            using(var c=Graphics.FromImage(crop))c.Clear(Color.Crimson);
            using(var g=Graphics.FromImage(a))scene.Draw(g,W,H,0,0,crop,sample);
            using(var c=Graphics.FromImage(crop))c.Clear(Color.Teal);
            using(var g=Graphics.FromImage(b))scene.Draw(g,W,H,0,0,crop,sample);
            Require(Different(a,b,new PointF(key.x,key.y))>130,"diamond must reflect a changed source frame");
            Require(b.GetPixel(0,0).A==0,"transparent surround");
        }}
    }
    static void CheckChain(Glass glass){
        using(var scene=new VoxelFuse(glass.data,()=>time)){
            time=0;scene.Strike(glass.scans[35]);time=600;scene.Strike(glass.scans[36]);
            time=1190;scene.Bounds(1920,720);Require(scene.BurstCount==0,"fuse timer");
            time=1201;var area=scene.Bounds(1920,720);Require(scene.BurstCount==1&&scene.ChainHits==1,"first wave must arm neighbor early");
            Require(area.Width>6*glass.scans[35].cap.Height && scene.ParticleCount>=390,"wide multilayer emission");
            time=1510;scene.Bounds(1920,720);Require(scene.BurstCount==2,"neighbor must detonate before its own fuse");
        }
    }
    static void CheckLibrarySettings(){
        // The test executable runs from a disposable stage. Keep synthetic choices away from the desktop host.
        string sandbox=Path.Combine(Project.Base,"test-user");
        Directory.CreateDirectory(sandbox);
        typeof(Project).GetField("Data",BindingFlags.Public|BindingFlags.Static).SetValue(null,sandbox);
        File.WriteAllText(Path.Combine(sandbox,"skills.json"),"{\"rainbow\":true}");
        typeof(UiText).GetField("preference",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,Path.Combine(sandbox,"language.txt"));
        UiText.Save(true);
        Project.SaveChoice(new OutputChoice{Id="test-keyboard",Label="Test display",Enabled=false});
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        var first=new Studio();
        ((RadioButton)first.window.FindName("FuseTile")).IsChecked=true;
        var slider=(Slider)first.window.FindName("FuseDuration");
        Require(slider.Value==1200&&slider.Minimum==150&&slider.Maximum==5000,"legacy default and range");
        Require(((TextBlock)first.window.FindName("EffectTitle")).Text=="方块连爆","localized fuse");
        slider.Value=2750;
        var saved=SkillOptions.Read();
        Require(saved.fuseMs==2750&&saved.rainbow,"auto-save and preserve unrelated preferences");
        first.window.Close();
        var reopened=new Studio();
        Require(((RadioButton)reopened.window.FindName("FuseTile")).IsChecked==true,"restore effect");
        Require(((Slider)reopened.window.FindName("FuseDuration")).Value==2750,"restore fuse");
        Require(((TextBlock)reopened.window.FindName("FuseDurationValue")).Text.Contains("2.75"),"seconds label");
        ((RadioButton)reopened.window.FindName("PrismTile")).IsChecked=true;
        Require(((TextBlock)reopened.window.FindName("EffectTitle")).Text=="钻石切面","localized prism");
        Require(((StackPanel)reopened.window.FindName("FusePanel")).Visibility==Visibility.Collapsed,"fuse settings isolated");
        var choice=Project.ReadChoice();
        Require(choice.Effect==Prism.Id&&choice.Id=="test-keyboard"&&!choice.Enabled,"only effect changes");
        using(var glass=new Glass())using(var hole=new BlackHole(glass.data.pitch))
        using(var library=new SkillLibrary(glass,hole,new InkSplash(glass.data))){
            library.Configure(saved);
            Require(library.Select(choice.Effect) is Prism&&library.Select(VoxelFuse.Id)==library.Fuse,"desktop library routing");
            Require(library.Fuse.fuseMs==2750,"background host applies saved delay");
            using(var fuse=new VoxelFuse(glass.data,()=>time)){
                foreach(int ms in new[]{150,2750,5000}){
                    fuse.Clear();time=0;fuse.fuseMs=ms;fuse.Strike(glass.scans[35]);fuse.Release(glass.scans[35]);
                    fuse.fuseMs=1200;time=ms-1;fuse.Bounds(W,H);Require(fuse.BurstCount==0,"armed fuse retains its delay");
                    time=ms;fuse.Bounds(W,H);Require(fuse.BurstCount==1,"detonate on selected delay");
                }
            }
        }
        reopened.window.Close();app.Shutdown();
    }
    static void Run(string name,Glass glass,IKeyEffect scene,string framesFolder){
        time=0;scene.Clear();
        var targets=new[]{glass.scans[33],glass.scans[34],glass.scans[35],glass.scans[36]};
        if(name=="tendrils")((Tendrils)scene).rainbow=true;
        bool silk=scene is SilkColony;
        int hold=silk?930:132,end=hold+(silk?650:scene is GlitterFlow?210:120),peak=0;
        var times=new List<double>();
        int frameNumber=0;bool visible=false;
        string dir=framesFolder==null?null:Path.Combine(framesFolder,name);if(dir!=null)Directory.CreateDirectory(dir);
        using(var overlay=new Bitmap(W,H,PixelFormat.Format32bppPArgb))
        using(var source=new Bitmap(W,H,PixelFormat.Format32bppArgb))
        using(var g=Graphics.FromImage(overlay))
        using(var bg=Graphics.FromImage(source)){
            Rectangle prev=Rectangle.Empty;
            for(int i=0;i<=end;i++){
                time=i*1000.0/60;
                bool video=dir!=null && i%3==0 && i<=(silk?780:222);
                if(video||scene is Prism)Backdrop(bg,glass,targets[0],i);
                var watch=Stopwatch.StartNew();
                if(i==0||i==21||i==43||i==63)scene.Strike(targets[i==0?0:i==21?1:i==43?2:3]);
                if(i==hold)foreach(var key in targets)scene.Release(key);
                var area=scene.Bounds(W,H);var dirty=prev.IsEmpty?area:area.IsEmpty?prev:Rectangle.Union(prev,area);
                if(!dirty.IsEmpty){
                    g.CompositingMode=CompositingMode.SourceCopy;g.FillRectangle(Brushes.Transparent,dirty);
                    g.CompositingMode=CompositingMode.SourceOver;
                }
                var region=scene.SampleArea(W,H);
                if(region.IsEmpty)scene.Draw(g,W,H,0,0,null,Rectangle.Empty);
                else using(var crop=source.Clone(region,PixelFormat.Format32bppArgb))scene.Draw(g,W,H,0,0,crop,region);
                g.Flush(FlushIntention.Sync);watch.Stop();
                if(!area.IsEmpty&&i>2){times.Add(watch.Elapsed.TotalMilliseconds);visible=true;}
                prev=area;if(silk)peak=Math.Max(peak,((SilkColony)scene).Population);
                if(video && (!silk||i%6==0)){
                    var rect=new Rectangle((int)(targets[0].x*W)-210,(int)(targets[0].y*H)-118,640,270);
                    using(var display=source.Clone(rect,PixelFormat.Format32bppArgb))using(var painter=Graphics.FromImage(display)){
                        painter.DrawImage(overlay,new Rectangle(0,0,rect.Width,rect.Height),rect,GraphicsUnit.Pixel);
                        display.Save(Path.Combine(dir,(frameNumber++).ToString("D4")+".png"),ImageFormat.Png);
                    }
                }
                if(!scene.Active && i>hold+1)break;
            }
        }
        Require(visible && !scene.Active,name+" output or tail");
        Require(scene.Bounds(W,H).IsEmpty,name+" idle bounds");
        if(silk)Require(peak==scene.Budget.Spiders && peak<=420,"held swarm population must plateau at its budget");
        times.Sort();records.Add(new{name,frames=times.Count,p95Ms=Math.Round(times[(int)(times.Count*.95)],2),peakSpiders=peak,drained=!scene.Active});
        var disposable=scene as IDisposable;if(disposable!=null)disposable.Dispose();
    }
    [STAThread]public static int Main(string[] args){
        try{
            if(args.Length>2&&args[2]=="--keycaps"){KeycapChecks.Run(args[0]);return 0;}
            if(args.Length>2&&args[2]=="--weather"){WeatherChecks.Run(args[0],args[1]);return 0;}
            if(args.Length>2&&args[2]=="--boundary"){CursorBoundaryChecks.Run();return 0;}
            if(args.Length>2&&args[2]=="--library"){CheckLibrarySettings();Console.WriteLine("Library routing, settings persistence, defaults and fuse timing passed.");return 0;}
            using(var glass=new Glass()){
                if(args.Length>2&&args[2]=="--thumbnails"){FullKeyboardDemos.Thumbnails(glass,args[0]);return 0;}
                var choice=Project.ReadChoice();glass.geometry.Sync(choice.Id);
                if(args.Length>2 && args[2]=="--glitter"){CheckGlitter(glass);FullKeyboardDemos.GlitterVariants(glass,args[0],args[1]);return 0;}
                if(args.Length>2 && args[2]=="--budget"){BudgetChecks.Run(glass,args[1],args.Length>3?args[3]:null);return 0;}
                if(args.Length>2 && args[2]=="--diversity"){FullKeyboardDemos.ExportDiversity(glass,args[1]);return 0;}
                if(args.Length>2 && args[2]=="--profile"){FullKeyboardDemos.Profile(glass);return 0;}
                CheckGlitter(glass);CheckWebGrowth(glass);CheckPresets(glass);CheckPrism(glass);CheckNavigation();CheckChain(glass);
                if(args.Length>2 && args[2]=="--full"){FullKeyboardDemos.VerifyVariants(glass);FullKeyboardDemos.Export(glass,args[0],args.Length>3?args[3]:null);return 0;}
                if(args.Length>2&&args[2]=="--measure"){FullKeyboardDemos.VerifyVariants(glass);args[0]=null;}
                Run("glitter",glass,new GlitterFlow(glass.data,()=>time),args[0]);
                Run("frost",glass,new FrostLance(glass.data,()=>time),args[0]);
                Run("tendrils",glass,new Tendrils(glass.data,()=>time),args[0]);
                Run("silk",glass,new SilkColony(glass.data,()=>time),args[0]);
                Run("prism",glass,new Prism(glass.data,()=>time),args[0]);
                Run("fuse",glass,new VoxelFuse(glass.data,()=>time),args[0]);
            }
            File.WriteAllText(args[1],Project.Json.Serialize(records));Console.WriteLine(File.ReadAllText(args[1]));return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
