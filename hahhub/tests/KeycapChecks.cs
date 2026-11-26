using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal static class KeycapChecks {
    static double time;
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static SkillScene Scene(string name,Glass glass){
        switch(name){
            case "tendrils":return new Tendrils(glass.data,()=>time){rainbow=true};
            case "ice":return new FrostLance(glass.data,()=>time);
            default:return new WeatherScene(glass.data,name=="snow",()=>time);
        }
    }
    static Bitmap Frame(Glass glass,string name,LayoutKey key,Rectangle world,int end){
        var scene=Scene(name,glass);scene.RenderWorld=world;
        using((IDisposable)scene){
            time=0;scene.Strike(key);
            for(int t=0;t<=end;t+=16){time=t;scene.Bounds(1920,720);}
            var output=new Bitmap(world.Width,world.Height,PixelFormat.Format32bppPArgb);
            using(var g=Graphics.FromImage(output)){
                g.TranslateTransform(-world.X,-world.Y);scene.Draw(g,1920,720,0,0,null,Rectangle.Empty);
            }
            return output;
        }
    }
    static string Hash(Bitmap image){
        var area=new Rectangle(0,0,image.Width,image.Height);var bits=image.LockBits(area,ImageLockMode.ReadOnly,image.PixelFormat);
        try{var bytes=new byte[bits.Stride*bits.Height];Marshal.Copy(bits.Scan0,bytes,0,bytes.Length);
            using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(bytes));}
        finally{image.UnlockBits(bits);}
    }
    static void CheckShoots(RectangleF cap,Func<PointF,bool> painted,string name){
        var rim=new KeycapRim(cap,4);
        int total=0;
        for(int side=0;side<4;side++){
            int hits=0;
            for(int i=0;i<64;i++){
                PointF normal;var pt=rim.At(rim.SideTurn(side,(i+.5f)/64),out normal);
                if(painted(pt))hits++;
            }
            Check(hits>=1,name+" missing roots on side "+side);
            total+=hits;
        }
        Check(total<229,name+" forms a continuous border: "+total+"/256");
        Console.WriteLine(name+" "+Math.Round(cap.Width)+" x "+Math.Round(cap.Height)+": "+total+"/256 edge samples covered");
    }
    static void CheckRoots(Bitmap image,Rectangle world,RectangleF cap,string name){
        CheckShoots(cap,delegate(PointF p){
            int x=(int)Math.Round(p.X-world.X),y=(int)Math.Round(p.Y-world.Y);
            for(int dy=Math.Max(0,y-3);dy<Math.Min(image.Height,y+4);dy++)
                for(int dx=Math.Max(0,x-3);dx<Math.Min(image.Width,x+4);dx++)if(image.GetPixel(dx,dy).A>145)return true;
            return false;
        },name);
    }
    static int Outgrowth(Bitmap image,Rectangle world,RectangleF cap){
        int count=0;var outer=RectangleF.Inflate(cap,13,13);
        var crop=Rectangle.Ceiling(RectangleF.Inflate(cap,215,215));crop.Offset(-world.X,-world.Y);
        crop.Intersect(new Rectangle(0,0,image.Width,image.Height));
        for(int y=crop.Top;y<crop.Bottom;y+=2)for(int x=crop.Left;x<crop.Right;x+=2)
            if(!outer.Contains(world.X+x,world.Y+y)&&image.GetPixel(x,y).A>145)count++;
        return count;
    }
    static void CheckPreview(Glass glass,Rectangle world){
        var app=new System.Windows.Application{ShutdownMode=System.Windows.ShutdownMode.OnExplicitShutdown};
        var studio=new Studio();var win=studio.window;
        try{
            win.ShowActivated=false;win.ShowInTaskbar=false;win.WindowStartupLocation=System.Windows.WindowStartupLocation.Manual;
            win.Left=-12000;win.Top=-12000;win.Show();win.UpdateLayout();
            var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            var model=(Glass)typeof(Studio).GetField("glass",flags).GetValue(studio);
            typeof(Studio).GetMethod("ShowEffect",flags).Invoke(studio,new object[]{Tendrils.Id});
            var space=model.data.keys.First(k=>k.code=="Space");
            typeof(Studio).GetMethod("TestHit",flags).Invoke(studio,new object[]{space});
            System.Threading.Thread.Sleep(960);
            typeof(Studio).GetMethod("PreviewFrame",flags).Invoke(studio,null);win.UpdateLayout();
            var overlay=(System.Windows.Controls.Image)win.FindName("LivePreview");
            var image=(System.Windows.Media.Imaging.BitmapSource)overlay.Source;
            Check(overlay.Width==world.Width&&overlay.Height==world.Height,"preview canvas matches board");
            var data=new byte[image.PixelWidth*image.PixelHeight*4];image.CopyPixels(data,image.PixelWidth*4,0);
            CheckShoots(space.cap,delegate(PointF pt){
                int x=(int)((pt.X-world.Left)*image.PixelWidth/world.Width);
                int y=(int)((pt.Y-world.Top)*image.PixelHeight/world.Height);
                bool hit=false;
                for(int j=Math.Max(0,y-2);!hit&&j<=Math.Min(image.PixelHeight-1,y+2);j++)
                    for(int k=Math.Max(0,x-2);k<=Math.Min(image.PixelWidth-1,x+2);k++)
                        if(data[(j*image.PixelWidth+k)*4+3]>110){hit=true;break;}
                return hit;
            },"WPF Space");
        }finally{win.Close();app.Shutdown();}
    }
    static void CheckFogAndIce(Glass glass,LayoutKey key,Rectangle world,string folder){
        var cap=key.cap;
        foreach(string effect in new[]{"fog","ice"})foreach(int timeMs in new[]{0,240,1536}){
            using(var image=Frame(glass,effect,key,world,timeMs)){
                int bands=key.code=="Space"?9:3;
                for(int b=0;b<bands;b++){
                    int painted=0,samples=0;
                    for(int v=1;v<=7;v++)for(int u=1;u<=9;u++){
                        float x=(b+u/10f)/bands,y=v/8f;
                        if(effect=="ice"){x=.25f+.5f*x;y=.40f+.2f*y;}
                        int px=(int)(cap.X+cap.Width*x)-world.X,py=(int)(cap.Y+cap.Height*y)-world.Y;
                        if(image.GetPixel(px,py).A>(effect=="fog"?6:0))painted++;
                        samples++;
                    }
                    if(effect=="fog")Check(painted>=samples*.23,key.code+" fog gap in band "+b+" at "+timeMs+"ms: "+painted+"/"+samples);
                    else Check(painted==0,key.code+" ice crossed the clear centre at "+timeMs+"ms");
                }
                if(effect=="ice"&&timeMs==1536)Check(Outgrowth(image,world,cap)>50,key.code+" missing ice on perimeter");
                if(key.code=="Space"&&timeMs==1536)
                using(var board=KeyboardStage.Create(glass.data,world)){
                    using(var g=Graphics.FromImage(board))g.DrawImageUnscaled(image,0,0);
                    var region=Rectangle.Ceiling(RectangleF.Inflate(cap,36,95));region.Offset(-world.X,-world.Y);
                    region.Intersect(new Rectangle(0,0,board.Width,board.Height));
                    using(var result=board.Clone(region,PixelFormat.Format32bppArgb))result.Save(Path.Combine(folder,"space-"+effect+".png"),ImageFormat.Png);
                }
            }
        }
        Console.WriteLine(key.code+": fog across the cap and clear ice centre at 0/240/1536 ms");
    }
    static void CheckIceGrowth(Glass glass,string folder){
        var key=glass.data.keys.First(k=>k.code=="Space");
        var cap=key.cap;var rim=new KeycapRim(cap,2);
        var region=Rectangle.Ceiling(RectangleF.Inflate(cap,165,175));
        var layouts=new System.Collections.Generic.HashSet<string>();
        using(var ice=new FrostLance(glass.data,()=>time))
        using(var surface=new Bitmap(region.Width,region.Height,PixelFormat.Format32bppPArgb))
        using(var brush=Graphics.FromImage(surface))
        using(var board=KeyboardStage.Create(glass.data,region)){
            ice.RenderWorld=region;
            for(int press=0;press<5;press++){
                time=press*10000;ice.Strike(key);int firstCount=0,midArea=0;
                foreach(int ms in new[]{128,1344,5728}){
                    time=press*10000+ms;ice.Bounds(1920,720);brush.Clear(Color.Transparent);
                    ice.Draw(brush,1920,720,region.X,region.Y,null,Rectangle.Empty);
                    var hits=new bool[512];
                    for(int i=0;i<hits.Length;i++){
                        PointF normal;var pt=rim.At(i/(float)hits.Length,out normal);
                        for(int d=-3;d<=13;d+=2){
                            int x=(int)(pt.X+d*normal.X-region.X),y=(int)(pt.Y+d*normal.Y-region.Y);
                            if(surface.GetPixel(x,y).A>90){hits[i]=true;break;}
                        }
                    }
                    int anchor=Array.IndexOf(hits,true);Check(anchor>=0,"ice rim empty");
                    int gap=0,longest=0,groups=0;
                    for(int i=1;i<=hits.Length;i++){
                        if(!hits[(anchor+i)%hits.Length]){gap++;continue;}
                        longest=Math.Max(longest,gap);if(gap>=6)groups++;gap=0;
                    }
                    int covered=hits.Count(h=>h);
                    Check(covered<hits.Length*.6 && longest>hits.Length*.13,"ice formed a closed border");
                    if(ms==128){
                        firstCount=covered;
                        Check(groups>=2&&groups<=3,"expected 2–3 initial ice buds, got "+groups);
                        layouts.Add(string.Concat(hits.Select(v=>v?"1":"0")));
                        string before=Hash(surface);ice.Strike(key);
                        brush.Clear(Color.Transparent);ice.Draw(brush,1920,720,region.X,region.Y,null,Rectangle.Empty);
                        Check(Hash(surface)==before,"repeat keydown restarted growth");
                    }
                    int outside=Outgrowth(surface,region,cap);
                    if(ms==1344)midArea=outside;
                    if(ms==5728)Check(covered>firstCount+5&&outside>midArea*1.12,"held space did not grow additional ice");
                    if(press==0){
                        using(var still=new Bitmap(board))
                        using(var g=Graphics.FromImage(still)){
                            g.DrawImageUnscaled(surface,0,0);still.Save(Path.Combine(folder,"growth-"+ms+".png"),ImageFormat.Png);
                        }
                        Console.WriteLine("Space {0} ms: {1} groups, {2:P1} rim occupied, {3:P1} longest gap",ms,groups,covered/512.0,longest/512.0);
                    }
                }
                ice.Release(key);time+=1200;Check(ice.Bounds(1920,720).IsEmpty&&!ice.Active,"ice failed to idle");
            }
        }
        Check(layouts.Count==5,"independent presses reused the same ice roots");
    }
    internal static void Run(string folder){
        Directory.CreateDirectory(folder);
        using(var glass=new Glass()){
            var saved=Project.ReadChoice();glass.geometry.Sync(saved.Id);
            var world=KeyboardStage.World(glass.data);CheckPreview(glass,world);CheckIceGrowth(glass,folder);
            foreach(string code in new[]{"KeyA","Enter","ShiftLeft","Space"}){
                var key=glass.data.keys.First(k=>k.code==code);
                using(var image=Frame(glass,"tendrils",key,world,3104)){
                    CheckRoots(image,world,key.cap,code);
                    if(code=="Space")using(var early=Frame(glass,"tendrils",key,world,384))
                    using(var board=KeyboardStage.Create(glass.data,world)){
                        int first=Outgrowth(early,world,key.cap),grown=Outgrowth(image,world,key.cap);
                        Check(grown>first*1.6+50,"held tendrils must extend beyond the rim");
                        Console.WriteLine("Space outgrowth at 0.38 / 3.10 s: "+first+" / "+grown);
                        using(var g=Graphics.FromImage(board))g.DrawImageUnscaled(image,0,0);
                        var crop=Rectangle.Ceiling(RectangleF.Inflate(key.cap,62,66));crop.Offset(-world.X,-world.Y);
                        crop.Intersect(new Rectangle(0,0,board.Width,board.Height));
                        using(var result=board.Clone(crop,PixelFormat.Format32bppArgb))result.Save(Path.Combine(folder,"space-tendrils.png"),ImageFormat.Png);
                    }
                }
            }
            foreach(string code in new[]{"KeyA","ShiftLeft","Space"})CheckFogAndIce(glass,glass.data.keys.First(k=>k.code==code),world,folder);
            var enter=glass.data.keys.First(k=>k.code=="Enter");var shell=enter.housing;var stem=enter.stem;var cap=enter.cap;
            foreach(string name in new[]{"tendrils","ice","fog","snow"}){
                string hash;using(var before=Frame(glass,name,enter,world,1536))hash=Hash(before);
                enter.housing.Offset(-107,45);enter.stem.Offset(-83,37);
                using(var shifted=Frame(glass,name,enter,world,1536))Check(hash==Hash(shifted),name+" depends on housing or stem");
                enter.cap=new RectangleF(cap.X-140,cap.Y-20,cap.Width*.8f,cap.Height*.92f);
                using(var moved=Frame(glass,name,enter,world,1536)){
                    Check(Hash(moved)!=hash,name+" ignored cap calibration");
                    if(name=="tendrils")CheckRoots(moved,world,enter.cap,"recalibrated Enter");
                }
                enter.cap=cap;enter.housing=shell;enter.stem=stem;
            }
        }
    }
}
