using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

// Exercise the same effect objects and desktop compositor paths as Host, without taking a monitor.
internal static class BudgetChecks {
    const int W=1920,H=720;
    static double now;
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static object Stress(string name,IKeyEffect effect,Glass glass){
        now=0;effect.Clear();effect.Budget.SetRate(60);
        var times=new List<double>();var work=new Stopwatch();var keys=glass.data.keys;
        var swarm=effect as SilkColony;var fuse=effect as VoxelFuse;var hole=effect as BlackHole;var ink=effect as InkSplash;
        int frames=swarm!=null?1800:480,peak=0;
        // Use a real 84-key chord and fast re-strikes, then leave four held so delayed work peaks.
        var chord=new[]{glass.scans[30],glass.scans[33],glass.scans[34],glass.scans[38]};
        using(var buffer=new Surface.Dib(new Size(W,H),PixelFormat.Format32bppPArgb))
        using(var wallpaper=new Bitmap(W,H,PixelFormat.Format32bppRgb))
        using(var background=Graphics.FromImage(wallpaper))
        using(var g=Graphics.FromImage(buffer.bitmap)){
            background.Clear(Color.SlateGray);
            var prev=Rectangle.Empty;
            for(int i=0;i<frames;i++){
                now=i*1000.0/60;
                if(i>=335&&effect is Glass)now+=55000; // Fully grown fractures on all held keys.
                work.Restart();
                if(i==0||i==36){foreach(var k in keys)effect.Strike(k);}
                if(i==14||i==42){foreach(var k in keys)effect.Release(k);}
                if(i>46&&i<140&&i%4==0){var k=keys[(i*13)%keys.Length];effect.Strike(k);effect.Release(k);}
                if(i==148)foreach(var k in chord)effect.Strike(k);
                if(i>=148&&i%11==0&&swarm!=null)effect.Strike(chord[0]);
                var area=effect.Bounds(W,H);
                var dirty=prev.IsEmpty?area:area.IsEmpty?prev:Rectangle.Union(prev,area);prev=area;
                if(!dirty.IsEmpty){
                    g.CompositingMode=CompositingMode.SourceCopy;g.FillRectangle(Brushes.Transparent,dirty);
                    g.CompositingMode=CompositingMode.SourceOver;
                }
                var sample=effect.SampleArea(W,H);
                using(var source=sample.IsEmpty?null:wallpaper.Clone(sample,PixelFormat.Format32bppRgb)){
                    if(hole!=null){g.Flush();hole.DrawDesktop(buffer,W,H,source,sample);}
                    else if(ink!=null){g.Flush();ink.DrawDesktop(buffer,W,H);}
                    else effect.Draw(g,W,H,0,0,source,sample);
                }
                g.Flush(FlushIntention.Sync);work.Stop();
                if(!area.IsEmpty){double ms=work.Elapsed.TotalMilliseconds;effect.Budget.Observe(ms);if(i>=24)times.Add(ms);}
                if(swarm!=null){peak=Math.Max(peak,swarm.Population);Check(swarm.Population<=420,"swarm exceeds capacity");}
                if(fuse!=null){peak=Math.Max(peak,fuse.ParticleCount);Check(fuse.ParticleCount<=1500,"fuse exceeds capacity");}
            }
            foreach(var k in keys)effect.Release(k);
            for(int i=0;i<1100&&effect.Active;i++){now+=1000.0/60;effect.Bounds(W,H);}
            Check(!effect.Active&&effect.Bounds(W,H).IsEmpty,name+" did not idle");
        }
        times.Sort();Check(times.Count>0,name+" missing frames");
        double p95=times[(int)(times.Count*.95)];
        Console.WriteLine("{0}  P95 {1:F2} ms  peak {2}  idle",name,p95,peak);
        Check(p95<=1000.0/60,name+" exceeded the 60 FPS render budget at P95");
        return new{name,p95Ms=Math.Round(p95,2),p99Ms=Math.Round(times[(int)(times.Count*.99)],2),overFrame=times.Count(ms=>ms>1000.0/60),samples=times.Count,peak,drained=true};
    }
    public static void Run(Glass layout,string report,string only=null){
        var budget=new EffectBudget();budget.SetRate(60);
        for(int i=0;i<9;i++)budget.Observe(25);Check(budget.Spiders==96&&budget.RestAfter(25)>1,"overload response");
        for(int i=0;i<1200;i++)budget.Observe(1);Check(budget.Spiders==420,"quality recovery");
        budget.SetRate(16);Check(budget.Spiders==96,"eco ceiling");
        var t=new Func<double>(()=>now);var data=layout.data;
        var effects=new IKeyEffect[]{new Glass(t),new BlackHole(data.pitch,t),new InkSplash(data,t,new Random(42)),new GlitterFlow(data,t),
            new Prism(data,t),new FrostLance(data,t),new WeatherScene(data,false,t),new WeatherScene(data,true,t),new Tendrils(data,t),new SilkColony(data,t),new VoxelFuse(data,t)};
        var names=new[]{"fracture","black-hole","ink","glitter","prism","ice","fog","snow","tendrils","silk","fuse"};
        var records=new List<object>();
        for(int i=0;i<effects.Length;i++)try{if(only==null||names[i]==only)records.Add(Stress(names[i],effects[i],layout));}
        finally{effects[i].Clear();var disposable=effects[i] as IDisposable;if(disposable!=null)disposable.Dispose();}
        File.WriteAllText(report,Project.Json.Serialize(records));
    }
}
