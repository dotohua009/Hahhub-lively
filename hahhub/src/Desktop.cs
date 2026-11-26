using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class Surface : NativeWindow,IDisposable
{
    [StructLayout(LayoutKind.Sequential,Pack=1)] struct Blend { public byte op,flags,opacity,alpha; }
    [StructLayout(LayoutKind.Sequential)] struct Header {public uint size;public int width,height;public ushort planes,bits;public uint compression,imageSize;public int x,y;public uint used,important;}
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h,IntPtr dst,ref Point p,ref Size size,IntPtr dc,ref Point origin,uint key,ref Blend blend,uint flags);
    [StructLayout(LayoutKind.Sequential)] struct DirtyRect {public int left,top,right,bottom;}
    [StructLayout(LayoutKind.Sequential)] unsafe struct LayerUpdate {
        public uint cb;public IntPtr destination;public Point* position;public Size* size;public IntPtr source;
        public Point* origin;public uint key;public Blend* blend;public uint flags;public DirtyRect* dirty;
    }
    [DllImport("user32.dll")] static extern unsafe bool UpdateLayeredWindowIndirect(IntPtr window,ref LayerUpdate update);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h,int command);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h,uint direction);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc,ref Header header,uint type,out IntPtr pixels,IntPtr section,uint offset);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    internal sealed class Dib : IDisposable {
        public IntPtr dc,pixels;IntPtr dib,old;public Bitmap bitmap;
        public Dib(Size size,PixelFormat format){
            try{
                dc=CreateCompatibleDC(IntPtr.Zero);var head=new Header{size=40,width=size.Width,height=-size.Height,planes=1,bits=32};
                dib=CreateDIBSection(dc,ref head,0,out pixels,IntPtr.Zero,0);
                if(dc==IntPtr.Zero||dib==IntPtr.Zero||pixels==IntPtr.Zero)throw new InvalidOperationException("无法分配交互画布");
                old=SelectObject(dc,dib);if(old==IntPtr.Zero||old==new IntPtr(-1))throw new InvalidOperationException("无法启用交互画布");
                bitmap=new Bitmap(size.Width,size.Height,size.Width*4,format,pixels);
            }catch{Dispose();throw;}
        }
        public void Dispose(){
            if(bitmap!=null){bitmap.Dispose();bitmap=null;}
            if(old!=IntPtr.Zero&&old!=new IntPtr(-1))SelectObject(dc,old);
            if(dib!=IntPtr.Zero)DeleteObject(dib);if(dc!=IntPtr.Zero)DeleteDC(dc);old=dib=dc=IntPtr.Zero;
        }
    }
    Dib buffer;Graphics graphics;Size size;bool shown;
    readonly WallpaperFrame wallpaper=new WallpaperFrame();
    public IntPtr VideoPlayer;
    bool sampleWallpaper=true;
    public bool SampleWallpaper{set{if(sampleWallpaper&&!value)wallpaper.Dispose();sampleWallpaper=value;}}
    Rectangle previous;
    public void Hide(){if(shown){ShowWindow(Handle,0);shown=false;}ReleaseBuffer();previous=Rectangle.Empty;wallpaper.Dispose();}
    void ReleaseBuffer(){if(graphics!=null){graphics.Dispose();graphics=null;}if(buffer!=null){buffer.Dispose();buffer=null;}size=Size.Empty;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string cls,string title);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string title);
    internal static IntPtr DesktopAnchor(){
        var shell=FindWindow("Progman",null);
        if(shell!=IntPtr.Zero&&FindWindowEx(shell,IntPtr.Zero,"SHELLDLL_DefView",null)!=IntPtr.Zero)return shell;
        var worker=IntPtr.Zero;
        while((worker=FindWindowEx(IntPtr.Zero,worker,"WorkerW",null))!=IntPtr.Zero)
            if(FindWindowEx(worker,IntPtr.Zero,"SHELLDLL_DefView",null)!=IntPtr.Zero)return worker;
        return IntPtr.Zero;
    }
    public void Place(bool show=false){
        if(!shown&&!show)return;
        var anchor=VideoPlayer!=IntPtr.Zero?VideoPlayer:DesktopAnchor();
        if(anchor==IntPtr.Zero){Hide();return;}
        var above=GetWindow(anchor,3);
        if(above==Handle&&shown)return;
        // Only position our own click-through window. Never reparent, hide or restack a wallpaper player.
        if(SetWindowPos(Handle,above==Handle?IntPtr.Zero:above,0,0,0,0,0x0013|(show?0x0040u:0u)|(above==Handle?4u:0u)))shown=true;
    }
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h,int index);
    public bool DesktopOnly {get{
        if(!shown)return false;
        const int required=0x080800a0;
        return (GetWindowLong(Handle,-20)&(required|8))==required && GetWindow(Handle,2)==DesktopAnchor();
    }}
    internal static Rectangle GlassViewport(Rectangle area,Size size){
        // Keep the compositor window still as individual crack tips advance across pixels.
        const int tile=128;
        return Rectangle.FromLTRB(area.Left/tile*tile,area.Top/tile*tile,
            Math.Min(size.Width,(area.Right+tile-1)/tile*tile),Math.Min(size.Height,(area.Bottom+tile-1)/tile*tile));
    }
    public unsafe void Paint(IKeyEffect effect,Rectangle screen,Rectangle area){
        if(area.IsEmpty||(VideoPlayer!=IntPtr.Zero&&!IsWindowVisible(VideoPlayer))){Hide();return;}
        if(Handle==IntPtr.Zero)CreateHandle(new CreateParams{Caption="HAHHAN Glass",Style=unchecked((int)0x80000000),ExStyle=0x080800a0});
        var windowArea=new Rectangle(Point.Empty,screen.Size);
        bool first=!shown||size!=windowArea.Size;
        if(size!=windowArea.Size){
            ReleaseBuffer();size=windowArea.Size;buffer=new Dib(size,PixelFormat.Format32bppPArgb);graphics=Graphics.FromImage(buffer.bitmap);
        }
        // A growing dirty region must never reposition the window or translate old geometry.
        var dirty=first?new Rectangle(Point.Empty,size):Rectangle.Union(previous,area);
        graphics.SetClip(dirty);
        graphics.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        graphics.FillRectangle(Brushes.Transparent,dirty);
        graphics.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceOver;
        var sample=(sampleWallpaper||VideoPlayer!=IntPtr.Zero)?effect.SampleArea(screen.Width,screen.Height):Rectangle.Empty;
        var frame=wallpaper.Read(screen,sample,Handle,VideoPlayer);var hole=effect as BlackHole;
        var splash=effect as InkSplash;
        if(hole!=null||splash!=null){
            graphics.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);
            if(splash!=null)splash.DrawDesktop(buffer,screen.Width,screen.Height);
            else hole.DrawDesktop(buffer,screen.Width,screen.Height,frame,sample);
        }else{effect.Draw(graphics,screen.Width,screen.Height,windowArea.X,windowArea.Y,frame,sample);graphics.Flush();}
        // Keep geometry in screen coordinates, but put only the affected pixels above the video.
        var presented=VideoPlayer==IntPtr.Zero?windowArea:hole==null?GlassViewport(area,size):area;
        var origin=presented.Location;var pos=new Point(screen.X+presented.X,screen.Y+presented.Y);var display=presented.Size;var blend=new Blend{opacity=255,alpha=1};
        var rect=new DirtyRect{left=dirty.Left,top=dirty.Top,right=dirty.Right,bottom=dirty.Bottom};
        var update=new LayerUpdate{cb=(uint)sizeof(LayerUpdate),position=&pos,size=&display,source=buffer.dc,origin=&origin,blend=&blend,flags=2,dirty=first||VideoPlayer!=IntPtr.Zero?null:&rect};
        if(!UpdateLayeredWindowIndirect(Handle,ref update)&&!UpdateLayeredWindow(Handle,IntPtr.Zero,ref pos,ref display,buffer.dc,ref origin,0,ref blend,2))
            throw new InvalidOperationException("无法显示碎裂层");
        previous=area;
        if(!shown)Place(true);
    }
    public void Dispose(){Hide();if(Handle!=IntPtr.Zero)DestroyHandle();}
}

internal sealed class Host : NativeWindow,IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct Device {public ushort page,usage;public uint flags;public IntPtr hwnd;}
    [StructLayout(LayoutKind.Sequential)] struct InputHeader {public uint type,size;public IntPtr device,param;}
    [StructLayout(LayoutKind.Sequential)] struct Key {public ushort scan,flags,reserved,vk;public uint msg,extra;}
    [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr input,uint cmd,IntPtr data,ref uint size,uint header);
    [DllImport("user32.dll")] static extern bool RegisterRawInputDevices(Device[] device,uint count,uint size);
    [DllImport("wtsapi32.dll")] static extern bool WTSRegisterSessionNotification(IntPtr h,uint flags);
    [DllImport("wtsapi32.dll")] static extern bool WTSUnRegisterSessionNotification(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr h);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string cls,string title);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window,uint msg,IntPtr w,IntPtr l);
    const int ReloadMessage=0x8000+84;
    public static void NotifySettings(){var h=FindWindow(null,"HAHHAN Glass Host");if(h!=IntPtr.Zero)PostMessage(h,ReloadMessage,IntPtr.Zero,IntPtr.Zero);}
    readonly Timer discovery=new Timer();
    const int FrameMessage=ReloadMessage+1;
    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint period);
    System.Threading.Timer animation;bool drawing,precise;int generation;
    void StopFrames(){drawing=false;generation++;
        if(animation!=null){animation.Dispose();animation=null;}
        if(precise){timeEndPeriod(1);precise=false;}
    }
    void Schedule(double now,double workMs){
        if(animation==null){
            var hwnd=Handle;var version=new IntPtr(++generation);
            animation=new System.Threading.Timer(delegate{PostMessage(hwnd,FrameMessage,version,IntPtr.Zero);},null,-1,-1);
        }
        if(!precise&&frameInterval<18)precise=timeBeginPeriod(1)==0;
        if(precise&&frameInterval>=18){timeEndPeriod(1);precise=false;}
        drawing=true;
        // Reserve a little idle time after a late frame rather than queueing catch-up work.
        nextFrame=Math.Max(nextFrame+frameInterval,now+effect.Budget.RestAfter(workMs));
        animation.Change(Math.Max(1,(int)Math.Ceiling(nextFrame-now)),-1);
    }
    readonly Surface surface=new Surface(); readonly Glass glass=new Glass();readonly BlackHole hole;readonly InkSplash ink;readonly SkillLibrary library;IKeyEffect effect;
    readonly Stopwatch clock=Stopwatch.StartNew();double nextFrame,frameInterval=62.5;
    readonly System.Collections.Generic.HashSet<int> held=new System.Collections.Generic.HashSet<int>();
    const int InputCapacity=256;
    readonly IntPtr input=Marshal.AllocHGlobal(InputCapacity);
    string activeId;bool registered,locked;Rectangle bounds;
    readonly bool probe;public int Frames{get;private set;}
    public bool Drawing{get{return drawing;}}
    public Host(bool offscreenProbe=false){probe=offscreenProbe;hole=new BlackHole(glass.data.pitch);ink=new InkSplash(glass.data);library=new SkillLibrary(glass,hole,ink);effect=glass;CreateHandle(new CreateParams{Caption=probe?"HAHHAN Effect Probe":"HAHHAN Glass Host",Style=unchecked((int)0x80000000),ExStyle=0x08000080});
        if(probe){bounds=new Rectangle(-30000,-30000,1920,720);return;}
        WTSRegisterSessionNotification(Handle,0);discovery.Interval=1500;discovery.Tick+=delegate{Discover();};Discover();discovery.Start();}
    public void ProbeStrike(int fps){if(!probe)throw new InvalidOperationException();glass.tuning.heal=1000;glass.tuning.delay=0;frameInterval=1000.0/fps;effect.Budget.SetRate(fps);Strike(glass.scans[30]);glass.Release(glass.scans[30]);if(!surface.DesktopOnly)throw new InvalidOperationException("桌面叠加窗口位置或样式不正确");}
    void Strike(LayoutKey loc){effect.Strike(loc);if(!drawing){nextFrame=clock.Elapsed.TotalMilliseconds;Draw();}}
    void ReleaseKey(LayoutKey key){effect.Release(key);if(!drawing&&effect.Active){nextFrame=clock.Elapsed.TotalMilliseconds;Draw();}}
    void Enable(bool enable){if(enable==registered)return;var d=new Device{page=1,usage=6,flags=enable?0x100u:1u,hwnd=enable?Handle:IntPtr.Zero};
        if(RegisterRawInputDevices(new[]{d},1,(uint)Marshal.SizeOf(typeof(Device))))registered=enable;
    }
    void Idle(){StopFrames();effect.Clear();held.Clear();surface.Hide();}
    void Detach(){Enable(false);Idle();surface.VideoPlayer=IntPtr.Zero;activeId=null;bounds=Rectangle.Empty;}
    void Discover(){
        if(probe)return;
        try{
            var choice=Project.ReadChoice();
            var outputs=Displays.Connected();
            var target=choice.Enabled&&!locked?choice.Find(outputs):null;
            var player=IntPtr.Zero;
            if(choice.Enabled&&!locked){
                var casting=VideoLease.Target(outputs,choice.Id);
                if(casting!=null){player=VideoLease.Player(casting.Id);target=player==IntPtr.Zero?null:casting;}
            }
            if(target==null||CalibrationGuide.ActiveOn(target.Id)){if(activeId!=null||registered)Detach();return;}
            if(activeId!=target.Id||bounds!=target.Bounds||surface.VideoPlayer!=player){Detach();activeId=target.Id;bounds=target.Bounds;surface.VideoPlayer=player;}
            if(glass.geometry.Sync(target.Id))Idle();
            surface.SampleWallpaper=player==IntPtr.Zero&&Project.WallpaperIntegration;
            library.Configure(SkillOptions.Read());
            IKeyEffect next=library.Select(choice.Effect);
            if(next!=effect){StopFrames();effect.Clear();surface.Hide();effect=next;}
            if(effect==ink)ink.tuning=InkTuning.Read();
            if(effect==hole){hole.power=Project.HolePower();hole.rimColor=Project.RimColor(true);}
            if(effect==glass){glass.tuning=Project.ReadTuning();glass.rimColor=Project.RimColor(false);}
            int rate=Project.Fps();effect.Budget.SetRate(rate);frameInterval=1000.0/rate;Enable(true);
            if(!registered)Idle();surface.Place();
        }catch(Exception e){Detach();Trace.TraceWarning(e.Message);}
    }
    void Draw(){try{if(!probe&&(!registered||activeId==null)){Detach();return;}double started=clock.Elapsed.TotalMilliseconds;var area=effect.Bounds(bounds.Width,bounds.Height);
        surface.Paint(effect,bounds,area);if(!area.IsEmpty)effect.Budget.Observe(clock.Elapsed.TotalMilliseconds-started);if(!area.IsEmpty)Frames++;
        if(!effect.Active||(effect==hole&&hole.Parked)){StopFrames();return;}
        double ended=clock.Elapsed.TotalMilliseconds;Schedule(ended,ended-started);}
        catch(Exception e){Detach();Trace.TraceWarning(e.Message);}}
    internal static bool HasKeyboardPayload(uint copied,uint headerSize){
        return copied>=headerSize+Marshal.SizeOf(typeof(Key))&&copied<=InputCapacity;
    }
    protected override void WndProc(ref Message m){
        if(m.Msg==FrameMessage){if(drawing&&m.WParam.ToInt32()==generation)Draw();return;}
        if(!probe&&m.Msg==ReloadMessage){Discover();return;}
        if(!probe&&(m.Msg==0x007e||(m.Msg==0x0219&&m.WParam.ToInt64()==7)))Detach(); // The next discovery resolves fresh physical bounds after a topology change.
        if(m.Msg==0x02b1){if(m.WParam.ToInt32()==7){locked=true;Enable(false);Idle();}if(m.WParam.ToInt32()==8){locked=false;Discover();}}
        if(m.Msg==0x00ff && registered && !locked && activeId!=null){
            uint len=InputCapacity,hs=(uint)Marshal.SizeOf(typeof(InputHeader));
            uint got=GetRawInputData(m.LParam,0x10000003,input,ref len,hs);
            if(HasKeyboardPayload(got,hs) && Marshal.ReadInt32(input)==1){
                var key=(Key)Marshal.PtrToStructure(IntPtr.Add(input,(int)hs),typeof(Key));int scan=key.scan+((key.flags&2)!=0?256:0);
                if((key.flags&1)!=0){LayoutKey released;if(held.Remove(scan)&&glass.scans.TryGetValue(scan,out released))ReleaseKey(released);}
                else {LayoutKey loc;if(glass.scans.TryGetValue(scan,out loc)&&held.Add(scan)){
                    IntPtr desk=OpenInputDesktop(0,false,0x100);if(desk!=IntPtr.Zero){CloseDesktop(desk);Strike(loc);
                    }else Idle();
                }}
            }
        }
        base.WndProc(ref m);
    }
    public void Dispose(){discovery.Dispose();Detach();surface.Dispose();library.Dispose();glass.Dispose();hole.Dispose();WTSUnRegisterSessionNotification(Handle);DestroyHandle();Marshal.FreeHGlobal(input);}
}
