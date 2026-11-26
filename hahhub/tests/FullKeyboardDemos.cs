using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal static class FullKeyboardDemos {
    const int W=1920,H=720,VideoW=1920,VideoH=800;
    static double now;
    static Func<double> Clock=()=>now;
    static void Action(IKeyEffect fx,Glass glass,string key,bool down){
        var k=glass.data.keys.First(p=>p.code==key);if(down)fx.Strike(k);else fx.Release(k);
    }
    static readonly string[] leftPoolKeys={"KeyA","KeyS","KeyQ"};
    static readonly string[] rightPoolKeys={"KeyL","KeyP","Enter"};
    static string PoolKey(int frame,out int beat){
        int start=frame<172?10:172;
        beat=frame-start;
        if(beat<0||frame>368)return null;
        var side=frame<172?leftPoolKeys:rightPoolKeys;
        string key=side[beat/10%side.Length];beat%=10;return key;
    }
    static void Sequence(string name,int f,IKeyEffect fx,Glass glass){
        if(name=="glitter"){
            int beat;var tap=PoolKey(f,out beat);
            if(tap!=null&&(beat==0||beat==4))Action(fx,glass,tap,beat==0);
            if(f==172)Action(fx,glass,leftPoolKeys[16%leftPoolKeys.Length],false);
            return;
        }
        if(name=="frost"){
            if(f==12||f==370)Action(fx,glass,"Space",f==12);
            if(f==427||f==462)Action(fx,glass,"KeyH",f==427);
            return;
        }
        string key="KeyH";
        if(f==16||f==218)Action(fx,glass,key,true);
        if(f==112||f==396)Action(fx,glass,key,false);
        if(f==(name=="fuse"?245:270))Action(fx,glass,name=="prism"?"Space":"KeyJ",true);
        if(f==(name=="fuse"?264:305))Action(fx,glass,name=="prism"?"Enter":"KeyK",true);
        if(f==(name=="fuse"?279:335))Action(fx,glass,name=="prism"?"ShiftLeft":"KeyU",true);
        if(f==396){
            foreach(var k in (name=="prism"?new[]{"Space","Enter","ShiftLeft"}:new[]{"KeyJ","KeyK","KeyU"}))Action(fx,glass,k,false);
        }
        if(name=="silk"&&f==440)Action(fx,glass,"KeyD",true);
        if(name=="silk"&&f==845)Action(fx,glass,"KeyD",false);
    }
    internal static void Light(Graphics g,int frame){
        float t=frame/60f;
        for(int i=0;i<3;i++){
            float x=(t*173+540*i)%2650-340;
            using(var pen=new Pen(Color.FromArgb(58,i%2==0?Color.FromArgb(86,177,219):Color.FromArgb(212,135,128)),12+i*2))
                g.DrawBezier(pen,x-230,850,x+190,390,x-110,260,x+190,-70);
        }
    }
    static readonly List<object> diversity=new List<object>();
    static byte[] Pixels(Bitmap image){
        var bits=image.LockBits(new Rectangle(0,0,image.Width,image.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
        try{var data=new byte[bits.Stride*bits.Height];Marshal.Copy(bits.Scan0,data,0,data.Length);
            return data;}
        finally{image.UnlockBits(bits);}
    }
    static void Variants(IKeyEffect fx,Glass g,string name,string output=null){
        var hashes=new HashSet<string>();var samples=new List<byte[]>();double min=1;
        using(var b=new Bitmap(W,H,PixelFormat.Format32bppPArgb))
        using(var draw=Graphics.FromImage(b)){
            for(int i=0;i<5;i++){
                fx.Clear();now=i*4000;fx.Strike(g.scans[35]);
                // Advance from the actual press, including the post-fuse particle frame.
                int frames=name=="fuse"?94:36;
                for(int j=0;j<=frames;j++){now=i*4000+j*1000.0/60;fx.Bounds(W,H);}
                draw.Clear(Color.Transparent);fx.Draw(draw,W,H,0,0,null,Rectangle.Empty);
                var pixels=Pixels(b);using(var sha=SHA256.Create())hashes.Add(Convert.ToBase64String(sha.ComputeHash(pixels)));
                if(name=="glitter"){
                    fx.Strike(g.scans[35]);fx.Bounds(W,H);
                    draw.Clear(Color.Transparent);fx.Draw(draw,W,H,0,0,null,Rectangle.Empty);
                    if(!pixels.SequenceEqual(Pixels(b)))throw new Exception("key repeat rerolled the liquid");
                    if(output!=null){
                        var world=KeyboardStage.World(g.data);
                        using(var stage=KeyboardStage.Create(g.data,world))
                        using(var board=Graphics.FromImage(stage)){
                            board.DrawImageUnscaled(b,-world.Left,-world.Top);
                            stage.Save(Path.Combine(output,"press-"+(i+1)+".png"),ImageFormat.Png);
                        }
                    }
                }
                foreach(var before in samples){
                    int different=0,visible=0;
                    for(int y=0;y<H;y+=2)for(int x=0;x<W;x+=2){
                        int k=(y*W+x)*4;
                        if(before[k+3]<16&&pixels[k+3]<16)continue;
                        visible++;
                        if(Math.Abs(before[k]-pixels[k])+Math.Abs(before[k+1]-pixels[k+1])+Math.Abs(before[k+2]-pixels[k+2])+Math.Abs(before[k+3]-pixels[k+3])>40)different++;
                    }
                    if(visible>0)min=Math.Min(min,different/(double)visible);
                }
                samples.Add(pixels);
            }
        }
        if(hashes.Count!=5)throw new Exception(name+" reproduced a previous shape");
        if(name=="glitter" && min<.10)throw new Exception("glitter variation below 10% of visible samples: "+min);
        diversity.Add(new{effect=name,presses=5,distinct=hashes.Count,minimumChangedPercent=Math.Round(min*100,1)});
        fx.Clear();
    }
    public static void GlitterVariants(Glass glass,string folder,string report){
        Directory.CreateDirectory(folder);diversity.Clear();
        using(var fx=new GlitterFlow(glass.data,Clock))Variants(fx,glass,"glitter",folder);
        File.WriteAllText(report,Project.Json.Serialize(diversity));
        Console.WriteLine(File.ReadAllText(report));
    }
    public static void ExportDiversity(Glass glass,string path){diversity.Clear();VerifyVariants(glass);File.WriteAllText(path,Project.Json.Serialize(diversity));}
    public static void VerifyVariants(Glass glass){
        using(var fracture=new Glass(Clock))using(var hole=new BlackHole(glass.data.pitch,Clock))
        using(var tentacles=new Tendrils(glass.data,Clock))using(var spiders=new SilkColony(glass.data,Clock))
        using(var glitter=new GlitterFlow(glass.data,Clock)){
            Variants(glitter,glass,"glitter");Variants(fracture,glass,"fracture");Variants(hole,glass,"hole");Variants(new InkSplash(glass.data,Clock),glass,"ink");
            Variants(new FrostLance(glass.data,Clock),glass,"frost");Variants(tentacles,glass,"tendrils");Variants(spiders,glass,"silk");
            Variants(new Prism(glass.data,Clock),glass,"prism");Variants(new VoxelFuse(glass.data,Clock),glass,"fuse");
        }
    }
    public static void Profile(Glass glass){
        using(var b=new Bitmap(W,H,PixelFormat.Format32bppPArgb))using(var g=Graphics.FromImage(b)){
            foreach(var scene in new IKeyEffect[]{new FrostLance(glass.data,Clock),new Tendrils(glass.data,Clock),new SilkColony(glass.data,Clock),new VoxelFuse(glass.data,Clock)}){
                now=0;foreach(int scan in new[]{33,34,35,36})scene.Strike(glass.scans[scan]);
                double draw=0,clear=0,step=0;int frames=0;
                for(int i=0;i<95;i++){
                    now=i*1000.0/60;var t=Stopwatch.StartNew();var area=scene.Bounds(W,H);step+=t.Elapsed.TotalMilliseconds;
                    if(i%5!=0 || area.IsEmpty)continue;
                    t.Restart();g.Clear(Color.Transparent);clear+=t.Elapsed.TotalMilliseconds;
                    t.Restart();scene.Draw(g,W,H,0,0,null,Rectangle.Empty);g.Flush(FlushIntention.Sync);draw+=t.Elapsed.TotalMilliseconds;frames++;
                }
                Console.WriteLine(scene.GetType().Name+" draw="+draw/frames+" clear="+clear/frames+" bounds="+step/95);
                var disp=scene as IDisposable;if(disp!=null)disp.Dispose();
            }
        }
    }
    public static void Thumbnails(Glass glass,string folder){
        Directory.CreateDirectory(folder);
        var center=glass.data.keys.First(k=>k.code=="KeyH");
        var side=glass.data.keys.First(k=>k.code=="KeyJ");
        using(var photo=new Bitmap(Path.Combine(Project.Base,"cover.jpg")))
        using(var background=new Bitmap(W,H,PixelFormat.Format32bppPArgb))
        using(var bg=Graphics.FromImage(background)){
            // Position a highlight from the shared wallpaper across the active switch.
            bg.DrawImage(photo,new Rectangle(0,0,W,H));
            string[] names={"frost","tendrils","silk","fuse","prism","fog","snow","glitter"};
            IKeyEffect[] fx={new FrostLance(glass.data,Clock),new Tendrils(glass.data,Clock){rainbow=true},
                new SilkColony(glass.data,Clock),new VoxelFuse(glass.data,Clock),new Prism(glass.data,Clock),new WeatherScene(glass.data,false,Clock),new WeatherScene(glass.data,true,Clock),new GlitterFlow(glass.data,Clock)};
            int[] times={3200,1450,5600,1333,630,1744,2624,1040};
            int[] widths={188,274,127,259,123,170,154,255};
            for(int n=0;n<fx.Length;n++)using((IDisposable)fx[n])
            using(var result=new Bitmap(W,H,PixelFormat.Format32bppPArgb))
            using(var g=Graphics.FromImage(result)){
                now=0;fx[n].Strike(center);
                for(int i=0;i<=times[n];i+=16){
                    now=i;if(n==3&&i==1072)fx[n].Strike(side);
                    fx[n].Bounds(W,H);
                }
                g.DrawImageUnscaled(background,0,0);
                fx[n].Draw(g,W,H,0,0,background,new Rectangle(0,0,W,H));
                float cx=center.cap.Left+center.cap.Width*.5f+(n==3?48:0);
                float cy=center.cap.Top+center.cap.Height*.5f;
                if(fx[n] is GlitterFlow){
                    var tank=glass.data.keys.Select(k=>k.cap).Aggregate(RectangleF.Union);
                    cx=tank.Left+tank.Width*.5f;cy=tank.Top+tank.Height*.5f;
                }
                if(fx[n] is FrostLance){
                    // Frame an actual cluster, since the centre of the pressed key is clear.
                    float best=0;
                    var search=Rectangle.Ceiling(RectangleF.Inflate(center.cap,120,120));
                    search.Intersect(new Rectangle(48,48,W-96,H-96));
                    for(int cy0=search.Top;cy0<search.Bottom;cy0+=12)for(int cx0=search.Left;cx0<search.Right;cx0+=12){
                        int score=0;
                        for(int y=cy0-40;y<=cy0+40;y+=8)for(int x=cx0-40;x<=cx0+40;x+=8){
                            var a=result.GetPixel(x,y);var b=background.GetPixel(x,y);
                            score+=Math.Abs(a.B-b.B)+Math.Abs(a.G-b.G);
                        }
                        if(score>best){best=score;cx=cx0;cy=cy0;}
                    }
                }
                int width=widths[n],height=width*30/34;
                using(var thumb=new Bitmap(204,180))
                using(var dst=Graphics.FromImage(thumb)){
                    dst.InterpolationMode=InterpolationMode.HighQualityBicubic;
                    dst.DrawImage(result,new Rectangle(0,0,204,180),new Rectangle((int)cx-width/2,(int)cy-height/2,width,height),GraphicsUnit.Pixel);
                    thumb.Save(Path.Combine(folder,names[n]+".jpg"),ImageFormat.Jpeg);
                }
            }
        }
    }
    public static void Export(Glass glass,string directory,string only=null){
        Directory.CreateDirectory(directory);var world=KeyboardStage.World(glass.data);
        using(var stage=KeyboardStage.Create(glass.data,world))
        using(var tendrils=new Tendrils(glass.data,Clock))
        using(var silk=new SilkColony(glass.data,Clock))
        using(var glitter=new GlitterFlow(glass.data,Clock)){
            var results=new List<object>();var names=new[]{"frost","tendrils","silk","prism","fuse","glitter"};
            var effects=new IKeyEffect[]{new FrostLance(glass.data,Clock),tendrils,silk,new Prism(glass.data,Clock),new VoxelFuse(glass.data,Clock),glitter};
            for(int i=0;i<names.Length;i++)if(only==null||only==names[i])results.Add(Movie(names[i],effects[i],glass,stage,world,directory));
            File.WriteAllText(Path.Combine(directory,only==null?"frames.json":only+"-frames.json"),Project.Json.Serialize(results));
        }
    }
    static object Movie(string name,IKeyEffect scene,Glass glass,Bitmap stage,Rectangle world,string folder){
        // On .NET Framework the redirected stdin writer inherits the console encoding.
        // A UTF-8 BOM would shift every BGRA sample by three bytes.
        Console.InputEncoding=new System.Text.UTF8Encoding(false);
        var path=Path.Combine(folder,name+".mp4");
        var info=new ProcessStartInfo("ffmpeg","-y -hide_banner -loglevel error -f rawvideo -pix_fmt bgra -s 1920x800 -r 60 -i pipe:0 -an -c:v libx264 -threads 4 -preset veryfast -crf 18 -pix_fmt yuv420p -movflags +faststart \""+path+"\""){
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true};
        var bytes=new byte[VideoW*VideoH*4];var times=new List<double>();int maximum=0;
        bool wholeBoard=scene is GlitterFlow||scene is FrostLance;
        using(var proc=Process.Start(info))
        using(var overlay=new Bitmap(wholeBoard?world.Width:W,wholeBoard?world.Height:H,PixelFormat.Format32bppPArgb))
        using(var layer=Graphics.FromImage(overlay))
        using(var physical=stage.Clone(new Rectangle(-world.Left,-world.Top,W,H),PixelFormat.Format32bppArgb))
        using(var src=new Bitmap(W,H,PixelFormat.Format32bppArgb))
        using(var sg=Graphics.FromImage(src))
        using(var output=new Bitmap(VideoW,VideoH,PixelFormat.Format32bppPArgb))
        using(var painter=Graphics.FromImage(output)){
            float scale=Math.Min((VideoW-14)/(float)world.Width,(VideoH-14)/(float)world.Height);
            float left=(VideoW-world.Width*scale)/2,top=(VideoH-world.Height*scale)/2;
            var snapshot=Rectangle.Round(new RectangleF(left,top,world.Width*scale,world.Height*scale));
            var desk=new RectangleF(left-world.X*scale,top-world.Y*scale,W*scale,H*scale);
            painter.InterpolationMode=InterpolationMode.NearestNeighbor;painter.PixelOffsetMode=PixelOffsetMode.Half;
            painter.Clear(Color.FromArgb(16,21,28));painter.DrawImage(stage,snapshot);
            if(wholeBoard)((SkillScene)scene).RenderWorld=world;
            scene.Clear();int total=name=="silk"?1000:510;
            for(int f=0;f<total;f++){
                now=f*1000.0/60;
                if(name=="tendrils")((Tendrils)scene).rainbow=f>=214;
                sg.DrawImageUnscaled(physical,0,0);Light(sg,f);
                var timer=Stopwatch.StartNew();Sequence(name,f,scene,glass);
                var area=scene.Bounds(W,H);layer.Clear(Color.Transparent);
                var sample=scene.SampleArea(W,H);
                if(sample.IsEmpty)scene.Draw(layer,W,H,wholeBoard?world.X:0,wholeBoard?world.Y:0,null,Rectangle.Empty);
                else using(var crop=src.Clone(sample,PixelFormat.Format32bppArgb))scene.Draw(layer,W,H,wholeBoard?world.X:0,wholeBoard?world.Y:0,crop,sample);
                layer.Flush(FlushIntention.Sync);timer.Stop();
                if(!area.IsEmpty)times.Add(timer.Elapsed.TotalMilliseconds);
                if(scene is VoxelFuse)maximum=Math.Max(maximum,((VoxelFuse)scene).ParticleCount);
                if(scene is SilkColony)maximum=Math.Max(maximum,((SilkColony)scene).Population);
                if(wholeBoard){
                    painter.DrawImage(stage,snapshot);painter.DrawImage(src,desk);painter.DrawImage(overlay,snapshot);
                    int beat;var tap=PoolKey(f,out beat);
                    if(name=="glitter"&&tap!=null&&beat<4){
                        var cap=glass.data.keys.First(k=>k.code==tap).cap;
                        using(var pen=new Pen(Color.FromArgb(210,233,244,249),2.8f))
                            painter.DrawRectangle(pen,left+(cap.X-world.X)*scale,top+(cap.Y-world.Y)*scale,cap.Width*scale,cap.Height*scale);
                    }
                }else{painter.DrawImage(src,desk);painter.DrawImage(overlay,desk);}
                painter.Flush();
                if(name=="fuse" && (f==58||f==319))output.Save(Path.Combine(folder,f==58?"fuse-armed.jpg":"fuse-chain.jpg"),ImageFormat.Jpeg);
                if(f==((name=="fuse")?106:name=="silk"?812:325))output.Save(Path.Combine(folder,name+".jpg"),ImageFormat.Jpeg);
                var bits=output.LockBits(new Rectangle(0,0,VideoW,VideoH),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
                try{Marshal.Copy(bits.Scan0,bytes,0,bytes.Length);}finally{output.UnlockBits(bits);}
                proc.StandardInput.BaseStream.Write(bytes,0,bytes.Length);
            }
            proc.StandardInput.BaseStream.Close();proc.WaitForExit();if(proc.ExitCode!=0)throw new IOException("Video encode failed");
        }
        times.Sort();var result=new{name,renderP95ms=Math.Round(times[(int)(times.Count*.95)],2),maximum,chainHits=scene is VoxelFuse?((VoxelFuse)scene).ChainHits:0,bursts=scene is VoxelFuse?((VoxelFuse)scene).BurstCount:0,sameKeyDistinct=5,frameRate=60,keys=84};
        Console.WriteLine(Project.Json.Serialize(result));return result;
    }
}
