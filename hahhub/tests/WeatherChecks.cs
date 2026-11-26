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
using System.Windows.Media.Imaging;

internal static class WeatherChecks {
    const int Width=1920,Height=720;
    static void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);}
    static LayoutKey Key(Glass g,string name){return g.data.keys.First(k=>k.code==name);}
    static T Item<T>(Studio studio,string id) where T:class{return studio.window.FindName(id) as T;}
    static void Panels(string folder){
        File.WriteAllText(Path.Combine(Project.Data,"skills.json"),"{\"rainbow\":true,\"fog\":null}");
        typeof(UiText).GetField("preference",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,Path.Combine(Project.Data,"language.txt"));
        UiText.Save(true);
        Project.SaveChoice(new OutputChoice{Id="offline-keyboard",Enabled=false,Effect=Project.Id});
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        var ui=new Studio();
        foreach(var group in new[]{
            new[]{"Materials","GlitterTile","GlassTile","InkTile","PrismTile"},
            new[]{"Weather","FogTile","SnowTile","FrostTile"},
            new[]{"Creatures","TendrilTile","SilkTile"},new[]{"Energy","HoleTile","FuseTile"}}){
            Item<RadioButton>(ui,group[0]+"Tab").IsChecked=true;
            foreach(RadioButton tab in Item<Panel>(ui,"CategoryTabs").Children){
                var panel=Item<StackPanel>(ui,(string)tab.Tag);
                Check((panel.Visibility==Visibility.Visible)==(tab.IsChecked==true),"only the browsed group is visible");
            }
            foreach(string name in group.Skip(1))Check(Item<RadioButton>(ui,name).Parent==Item<StackPanel>(ui,group[0]+"Tiles"),"category membership "+name);
            Check(Project.ReadChoice().Effect==Project.Id,"browsing applied an effect");
        }
        Item<RadioButton>(ui,"MaterialsTab").IsChecked=true;
        Item<RadioButton>(ui,"GlitterTile").IsChecked=true;
        Check(Project.ReadChoice().Effect==GlitterFlow.Id && Item<TextBlock>(ui,"EffectTitle").Text=="流麻","glitter selected and saved");
        Check(Item<System.Windows.Controls.Image>(ui,"GlitterImage").Source is BitmapSource,"glitter thumbnail");
        ui.window.Close();ui=new Studio();
        Check(Item<RadioButton>(ui,"GlitterTile").IsChecked==true && Item<RadioButton>(ui,"MaterialsTab").IsChecked==true,"restored glitter and category");
        Item<RadioButton>(ui,"WeatherTab").IsChecked=true;
        Check(Project.ReadChoice().Effect==GlitterFlow.Id,"browsing Weather preserves the active effect");
        Check(Item<StackPanel>(ui,"WeatherTiles").Visibility==Visibility.Visible&&Item<StackPanel>(ui,"MaterialsTiles").Visibility==Visibility.Collapsed,"separate weather group");
        foreach(string name in new[]{"FogTile","SnowTile","FrostTile"})Check(Item<RadioButton>(ui,name).Parent==Item<StackPanel>(ui,"WeatherTiles"),"weather membership "+name);
        foreach(string name in new[]{"FogImage","SnowImage"})Check(Item<System.Windows.Controls.Image>(ui,name).Source is BitmapSource,"rendered thumbnail "+name);
        Item<RadioButton>(ui,"FogTile").IsChecked=true;
        Check(Item<TextBlock>(ui,"EffectTitle").Text=="霜雾"&&Item<StackPanel>(ui,"WeatherPanel").Visibility==Visibility.Visible,"fog settings");
        Item<Slider>(ui,"WeatherDensity").Value=155;Item<Slider>(ui,"WeatherSpread").Value=125;Item<Slider>(ui,"WeatherClear").Value=2750;
        Item<RadioButton>(ui,"SnowTile").IsChecked=true;
        Check(Item<Slider>(ui,"WeatherDensity").Value==100&&Item<TextBlock>(ui,"EffectTitle").Text=="风吹雪","independent snow defaults");
        Item<Slider>(ui,"WeatherClear").Value=900;
        Item<RadioButton>(ui,"FrostTile").IsChecked=true;
        Check(Item<StackPanel>(ui,"WeatherPanel").Visibility==Visibility.Collapsed,"original ice controls");
        Item<RadioButton>(ui,"FogTile").IsChecked=true;
        var choice=Project.ReadChoice();
        Check(choice.Effect==WeatherScene.FogId&&choice.Id=="offline-keyboard"&&!choice.Enabled,"host target preserved");
        ui.window.Close();ui=new Studio();
        Check(Item<RadioButton>(ui,"WeatherTab").IsChecked==true&&Item<RadioButton>(ui,"FogTile").IsChecked==true,"category restored");
        Check(Item<Slider>(ui,"WeatherDensity").Value==155&&Item<Slider>(ui,"WeatherSpread").Value==125&&Item<Slider>(ui,"WeatherClear").Value==2750,"all controls restored");
        ui.Snapshot(Path.Combine(folder,"weather-ui.png"));app.Shutdown();
        var options=SkillOptions.Read();
        using(var glass=new Glass())using(var hole=new BlackHole(glass.data.pitch))
        using(var library=new SkillLibrary(glass,hole,new InkSplash(glass.data))){
            library.Configure(options);
            Check(((WeatherScene)library.Select(WeatherScene.SnowId)).tuning.clearMs==900&&((WeatherScene)library.Select(choice.Effect)).tuning.density==155&&options.rainbow,"host tuning and existing fields");
        }
    }
    static void FrostSeeds(Glass glass){
        double time=0;
        var key=Key(glass,"KeyG");var crop=Rectangle.Ceiling(RectangleF.Inflate(key.cap,5,5));
        var count=typeof(WeatherScene).GetField("count",BindingFlags.Instance|BindingFlags.NonPublic);
        var patterns=new List<int[]>();
        using(var fog=new WeatherScene(glass.data,false,()=>time))
        using(var frame=new Bitmap(crop.Width,crop.Height,PixelFormat.Format32bppPArgb))
        using(var paint=Graphics.FromImage(frame)){
            for(int press=0;press<5;press++){
                fog.Clear();time+=4000;fog.Strike(key);time+=1900;fog.Release(key);
                time+=480;fog.Bounds(Width,Height); // The low vapour has dispersed, but the frost is still visible.
                count.SetValue(fog,0); // Isolate the deposited ice from the moving vapour.
                int[] pattern=null;
                for(int repeat=0;repeat<2;repeat++){
                    paint.Clear(Color.Transparent);fog.Draw(paint,Width,Height,crop.X,crop.Y,null,Rectangle.Empty);
                    var pixels=new int[crop.Width*crop.Height];
                    for(int y=0;y<crop.Height;y++)for(int x=0;x<crop.Width;x++){
                        int alpha=frame.GetPixel(x,y).A;pixels[y*crop.Width+x]=alpha;
                        if(!key.cap.Contains(crop.X+x+.5f,crop.Y+y+.5f))Check(alpha<5,"frost outside the cap");
                    }
                    if(repeat==0)pattern=pixels;else Check(pattern.SequenceEqual(pixels),"ice flickered during a hold");
                }
                foreach(var old in patterns){
                    int overlap=0,changed=0;
                    for(int i=0;i<pattern.Length;i++){
                        if(Math.Max(pattern[i],old[i])<12)continue;
                        overlap++;if(Math.Abs(pattern[i]-old[i])>12)changed++;
                    }
                    Check(overlap>50&&changed>overlap*.25,"repeated freeze should produce a different pattern");
                }
                patterns.Add(pattern);
            }
        }
        Console.WriteLine("Five distinct, stable frost deposits on the same cap.");
    }
    static object Render(Glass glass,bool snow,string folder){
        double now=0;
        var measures=new List<double>();
        var a=Key(glass,"KeyG");var b=Key(glass,"KeyH");var c=Key(glass,"KeyJ");var d=Key(glass,"KeyK");
        var world=KeyboardStage.World(glass.data);
        var crop=new Rectangle((int)(a.x*Width)-100,(int)(a.y*Height)-110,560,230);
        var fields=BindingFlags.NonPublic|BindingFlags.Instance;
        using(var board=KeyboardStage.Create(glass.data,world))
        using(var scene=new WeatherScene(glass.data,snow,()=>now))
        using(var bitmap=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb))
        using(var draw=Graphics.FromImage(bitmap))
        using(var shot=new Bitmap(crop.Width,crop.Height))
        using(var composite=Graphics.FromImage(shot)){
            Check(!scene.Active&&scene.Bounds(Width,Height).IsEmpty,"idle");
            var name=snow?"snow":"fog";var keyCount=typeof(WeatherScene).GetField("count",fields);
            var buffer=typeof(WeatherScene).GetField("particles",fields);
            var particleType=buffer.FieldType.GetElementType();
            int woken=0,resting=0,peak=0;bool visible=false;
            var timer=new Stopwatch();
            for(int f=0;f<336;f++){
                now=f*(1000.0/60);
                if(f==5){
                    scene.Strike(a);int burst=(int)keyCount.GetValue(scene);
                    if(!snow){
                        var grains=(Array)buffer.GetValue(scene);var quadrants=new HashSet<int>();
                        for(int i=0;i<burst;i++){
                            var grain=grains.GetValue(i);
                            float x=(float)particleType.GetField("x").GetValue(grain),y=(float)particleType.GetField("y").GetValue(grain);
                            Check(a.cap.Contains(x,y),"fog must originate inside the calibrated keycap");
                            quadrants.Add((x>a.cap.Left+a.cap.Width/2?1:0)+(y>a.cap.Top+a.cap.Height/2?2:0));
                        }
                        Check(quadrants.Count==4,"emission should span the keycap face");
                    }
                    scene.Strike(a);Check(burst>0&&(int)keyCount.GetValue(scene)==burst,"distinct burst and repeat suppression");
                }
                if(f==120){
                    int n=(int)keyCount.GetValue(scene);var data=(Array)buffer.GetValue(scene);
                    var restingField=particleType.GetField("resting");var vx=particleType.GetField("vx");var vy=particleType.GetField("vy");
                    scene.Bounds(Width,Height);
                    n=(int)keyCount.GetValue(scene);
                    var velocity=new PointF[n];var wasResting=new bool[n];
                    for(int i=0;i<n;i++){object p=data.GetValue(i);velocity[i]=new PointF((float)vx.GetValue(p),(float)vy.GetValue(p));wasResting[i]=(bool)restingField.GetValue(p);if(wasResting[i])resting++;}
                    scene.Strike(b);
                    for(int i=0;i<n;i++){object p=data.GetValue(i);if((!snow||wasResting[i])&&Math.Abs((float)vx.GetValue(p)-velocity[i].X)+Math.Abs((float)vy.GetValue(p)-velocity[i].Y)>2)woken++;}
                    Check(woken>0,"neighbor must move existing "+name);
                }
                if(f==128)scene.Strike(c);if(f==137)scene.Strike(d);
                if(f==210)foreach(var k in new[]{a,b,c,d})scene.Release(k);
                timer.Restart();
                var area=scene.Bounds(Width,Height);
                draw.Clear(Color.Transparent);scene.Draw(draw,Width,Height,0,0,null,Rectangle.Empty);draw.Flush(FlushIntention.Sync);
                timer.Stop();if(f>=140&&f<210)measures.Add(timer.Elapsed.TotalMilliseconds);
                if(f==80){
                    Check(area.Width<Width*.70&&area.Height<Height*.85&&scene.SampleArea(Width,Height).IsEmpty,"localized transparent layer");
                    if(!snow){
                        int visibleOuter=0;var band=RectangleF.Inflate(a.cap,a.cap.Width*.25f,a.cap.Height*.25f);
                        for(int y=area.Top;y<area.Bottom;y+=3)for(int x=area.Left;x<area.Right;x+=3)
                            if(!band.Contains(x,y)&&bitmap.GetPixel(x,y).A>20)visibleOuter++;
                        Check(visibleOuter>80,"visible mist should roll beyond the keycap");
                    }
                    Check(bitmap.GetPixel(1,1).A==0,"untouched backdrop");
                    for(int y=area.Top;y<area.Bottom&&!visible;y+=2)for(int x=area.Left;x<area.Right;x+=2)if(bitmap.GetPixel(x,y).A>20){visible=true;break;}
                    Check(visible,"visible effect");
                }
                peak=Math.Max(peak,(int)keyCount.GetValue(scene));
                if(f%2==0){
                    var dest=new Rectangle(0,0,crop.Width,crop.Height);
                    composite.DrawImage(board,dest,new Rectangle(crop.X-world.Left,crop.Y-world.Top,crop.Width,crop.Height),GraphicsUnit.Pixel);
                    composite.DrawImage(bitmap,dest,crop,GraphicsUnit.Pixel);
                    shot.Save(Path.Combine(folder,name+"-"+(f/2).ToString("D3")+".png"),ImageFormat.Png);
                }
            }
            Check(!scene.Active&&scene.Bounds(Width,Height).IsEmpty,"release and idle shutdown");
            Check(peak<=((Array)buffer.GetValue(scene)).Length,"bounded particles");
            measures.Sort();
            return new{effect=name,width=Width,height=Height,heldKeys=4,p95RenderMs=Math.Round(measures[(int)(measures.Count*.95)],2),peakParticles=peak,restingBeforeGust=resting,particlesPushedByNeighbor=woken,idle=true};
        }
    }
    internal static void Run(string folder,string report){
        Directory.CreateDirectory(folder);
        var sandbox=Path.Combine(Project.Base,"weather-test");Directory.CreateDirectory(sandbox);typeof(Project).GetField("Data").SetValue(null,sandbox);
        Panels(folder);
        using(var glass=new Glass()){
            FrostSeeds(glass);
            string result=Project.Json.Serialize(new[]{Render(glass,false,folder),Render(glass,true,folder)});
            File.WriteAllText(report,result);Console.WriteLine(result);
        }
    }
}
