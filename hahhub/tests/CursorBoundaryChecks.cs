using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using Point = System.Drawing.Point;

internal static class CursorBoundaryChecks {
    static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    static Output Screen(string id,int x,int y,int w,int h,bool primary=false){
        return new Output{Id=id,Label=id,Bounds=new Rectangle(x,y,w,h),Primary=primary};
    }
    static void Reach(CursorMap map,Point from,Point to,Point expected){
        Point actual;var free=map.ReturnPoint(from,to,out actual);
        Check(free!=null&&free.Bounds.Contains(actual)&&map.AtBlocked(actual)==null&&actual==expected,"boundary "+from+" -> "+to+" got "+actual);
    }
    static void Geometry(){
        var main=Screen("main",0,0,2560,1440,true);
        var left=Screen("left",-1080,-300,1080,1920);
        var keyboard=Screen("panel",120,1440,1920,720);
        var all=new List<Output>{main,left,keyboard};
        Check(KeyboardScreen.CanIsolate(keyboard,all),"independent secondary");
        Check(!KeyboardScreen.Eligible(keyboard,all),"dimensions alone cannot block a monitor");
        Check(!KeyboardScreen.CanIsolate(main,all),"primary");
        Check(!KeyboardScreen.CanIsolate(keyboard,new[]{keyboard}),"no other screen");
        keyboard.Mirrored=true;Check(!KeyboardScreen.CanIsolate(keyboard,all),"mirrored");keyboard.Mirrored=false;
        all.Add(Screen("overlap",100,1550,640,480));Check(!KeyboardScreen.CanIsolate(keyboard,all),"overlap");all.RemoveAt(3);
        var map=new CursorMap(new[]{keyboard},new[]{main,left});
        Check(map.AtBlocked(new Point(-8,541))==null&&map.AtBlocked(new Point(530,1400))==null,"both ordinary monitors free");
        Reach(map,new Point(550,1439),new Point(575,1465),new Point(575,1439));
        Reach(map,new Point(350,1500),new Point(350,1500),new Point(350,1439));
        var top=Screen("above",120,-720,1920,720);
        Reach(new CursorMap(new[]{top},new[]{main}),new Point(532,0),new Point(501,-30),new Point(501,0));
        var mid=Screen("mid",-1920,0,1920,720);
        var far=Screen("far",-3000,-200,1080,1200);
        Reach(new CursorMap(new[]{mid},new[]{main}),new Point(0,255),new Point(-12,267),new Point(0,267));
        map=new CursorMap(new[]{mid},new[]{main,far});
        Reach(map,new Point(0,255),new Point(-12,267),new Point(-1921,267));
        Reach(map,new Point(-1921,260),new Point(-1917,271),new Point(0,271));
        var lower=Screen("below",120,2160,1920,1080);
        map=new CursorMap(new[]{keyboard},new[]{main,lower});
        Reach(map,new Point(524,1439),new Point(530,1445),new Point(530,2160));
        Reach(map,new Point(524,2160),new Point(530,2156),new Point(530,1439));
    }
    static void Edid(){
        var e=new byte[128];for(int i=1;i<7;i++)e[i]=255;
        e[8]=0x21;e[9]=0x36;e[10]=0x30;e[11]=0x12;e[54]=1;
        e[56]=128;e[58]=0x70;e[59]=208;e[61]=0x20;
        Action seal=delegate{e[127]=0;e[127]=unchecked((byte)(-e.Sum(x=>(int)x)));};
        seal();Check(KeyboardScreen.MatchesEdid(e),"keyboard EDID");
        e[127]^=1;Check(!KeyboardScreen.MatchesEdid(e),"invalid checksum");
        e[59]=128;seal();Check(!KeyboardScreen.MatchesEdid(e),"wrong native timing");
        e[59]=208;e[8]=0;seal();Check(!KeyboardScreen.MatchesEdid(e),"wrong vendor");
    }
    public static void Run(){
        Geometry();Edid();
        var saved=Project.ReadChoice();var live=Displays.Connected();
        foreach(var d in live){
            var center=new Point(d.Bounds.Left+d.Bounds.Width/2,d.Bounds.Top+d.Bounds.Height/2);
            Check(Displays.StillAt(d,center),"native monitor bounds");
            var handle=d.Monitor;d.Monitor=IntPtr.Zero;Check(!Displays.StillAt(d,center),"stale monitor handle");d.Monitor=handle;
            if(d.Primary||!KeyboardScreen.Recognized(d.Id))Check(!KeyboardScreen.Eligible(d,live),"ordinary monitor excluded");
        }
        string sandbox=Path.Combine(Project.Base,"test-cursor");Directory.CreateDirectory(sandbox);
        typeof(Project).GetField("Data").SetValue(null,sandbox);
        Check(KeyboardScreen.Blocked("panel-a"),"default block");
        KeyboardScreen.SetBlocked("panel-a",false);
        Check(!KeyboardScreen.Blocked("PANEL-A")&&KeyboardScreen.Blocked("panel-b"),"per-panel preferences");
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        Project.SaveChoice(new OutputChoice{Id="ordinary",Label="Ordinary",Enabled=false});
        var panel=new Studio();
        foreach(string name in new[]{"Tile","HoleImage","InkImage","FrostImage","TendrilImage","SilkImage","FuseImage","PrismImage"}){
            var image=(System.Windows.Controls.Image)panel.window.FindName(name);
            var source=image.Source as BitmapSource;
            Check(source!=null&&source.PixelWidth>=34&&image.Width==34&&image.Height==30,name+" thumbnail");
        }
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var refresh=typeof(Studio).GetMethod("Refresh",flags);
        refresh.Invoke(panel,null);
        var toggle=(ToggleButton)panel.window.FindName("CursorBoundary");
        Check(toggle.Visibility==Visibility.Collapsed,"ordinary UI hidden");
        bool measured=KeyboardScreen.Recognized(saved.Id);
        if(measured){
            Project.SaveChoice(new OutputChoice{Id=saved.Id,Label=saved.Label,Enabled=false});
            refresh.Invoke(panel,null);
            if(!live.Any(d=>d.Id==saved.Id)||live.Any(d=>d.Id==saved.Id&&KeyboardScreen.Eligible(d,live))){
                Check(toggle.Visibility==Visibility.Visible&&toggle.IsChecked==true,"known panel default UI");
                toggle.IsChecked=false;toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent,toggle));
                Check(!KeyboardScreen.Blocked(saved.Id)&&toggle.IsChecked==false,"real switch saves allowing entry");
                panel.window.Close();panel=new Studio();refresh.Invoke(panel,null);
                toggle=(ToggleButton)panel.window.FindName("CursorBoundary");Check(toggle.IsChecked==false,"reopened retains setting");
                toggle.IsChecked=true;toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent,toggle));
                Check(KeyboardScreen.Blocked(saved.Id),"switch restores block");
            }
            if(!live.Any(d=>KeyboardScreen.Eligible(d,live)))Check(CursorMap.From(live)==null,"unplugged leaves boundary disarmed");
        }
        panel.window.Close();app.Shutdown();
        Console.WriteLine("8 thumbnails, eligibility, edge movement, ordinary-screen transit, EDID and preferences passed. Live monitors: "+live.Count+"; saved IPI 84 identified: "+measured+"; saved panel connected: "+live.Any(d=>d.Id==saved.Id));
    }
}
