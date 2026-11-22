using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// Prefer the player surface. A DX11 swap chain can require the exposed desktop compositor instead.
internal sealed class WallpaperFrame : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int left,top,right,bottom; }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string name,string caption);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window,ref Point point);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
    [DllImport("user32.dll")] static extern bool IsHungAppWindow(IntPtr window);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dest,int x,int y,int w,int h,IntPtr source,int sx,int sy,uint operation);

    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr window,uint affinity);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder name,int count);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int result,int size);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out Rect result,int size);
    [DllImport("dwmapi.dll")] static extern int DwmFlush();
    delegate bool Visit(IntPtr window,IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumWindows(Visit callback,IntPtr data);

    Bitmap buffer;Surface.Dib capture;
    VideoFrames videoFrames;
    [DllImport("gdi32.dll")] static extern bool GdiFlush();
    IntPtr excluded,lastPlayer;bool composite;
    static bool Exposed(Rectangle region,IntPtr overlay,IntPtr anchor){
        if(anchor==IntPtr.Zero)return false;
        bool reached=false;
        EnumWindows(delegate(IntPtr window,IntPtr unused){
            if(window==anchor){reached=IsWindowVisible(window);return false;}
            if(window==overlay||!IsWindowVisible(window))return true;
            // GetWindowRect includes the invisible resize border of maximized windows on adjacent monitors.
            Rect rect;if(DwmGetWindowAttribute(window,9,out rect,Marshal.SizeOf(typeof(Rect)))!=0&&!GetWindowRect(window,out rect))return true;
            if(!region.IntersectsWith(Rectangle.FromLTRB(rect.left,rect.top,rect.right,rect.bottom)))return true;
            int cloaked;return DwmGetWindowAttribute(window,14,out cloaked,4)==0&&cloaked!=0;
        },IntPtr.Zero);
        return reached;
    }
    static Rectangle Client(IntPtr window){
        Rect rect;var origin=Point.Empty;
        return GetClientRect(window,out rect)&&ClientToScreen(window,ref origin)?
            new Rectangle(origin,new Size(rect.right,rect.bottom)):Rectangle.Empty;
    }
    static IntPtr PlayerIn(IntPtr parent,Rectangle screen,uint shell){
        for(var child=GetWindow(parent,5);child!=IntPtr.Zero;child=GetWindow(child,2)){
            uint process;GetWindowThreadProcessId(child,out process);
            if(process!=0&&process!=shell&&IsWindowVisible(child)&&Client(child).Contains(screen))return child;
        }
        return IntPtr.Zero;
    }
    internal static IntPtr FindPlayer(Rectangle screen){
        var prog=FindWindowEx(IntPtr.Zero,IntPtr.Zero,"Progman",null);
        if(prog==IntPtr.Zero)return IntPtr.Zero;
        uint shell;GetWindowThreadProcessId(prog,out shell);
        foreach(var parent in new[]{prog,IntPtr.Zero}){
            if(parent!=IntPtr.Zero){var direct=PlayerIn(parent,screen,shell);if(direct!=IntPtr.Zero)return direct;}
            var worker=IntPtr.Zero;
            while((worker=FindWindowEx(parent,worker,"WorkerW",null))!=IntPtr.Zero){
                uint process;GetWindowThreadProcessId(worker,out process);
                if(process!=shell||!IsWindowVisible(worker))continue;
                var player=PlayerIn(worker,screen,shell);if(player!=IntPtr.Zero)return player;
            }
        }
        return IntPtr.Zero;
    }
    public Bitmap Read(Rectangle screen,Rectangle region,IntPtr overlay){return Read(screen,region,overlay,IntPtr.Zero);}
    public Bitmap Read(Rectangle screen,Rectangle region,IntPtr overlay,IntPtr video){
        if(region.IsEmpty)return null;
        var player=video!=IntPtr.Zero?video:FindPlayer(screen);if(player==IntPtr.Zero)return null;
        var client=Client(player);if(!client.Contains(screen))return null;
        if(buffer==null||buffer.Width<region.Width||buffer.Height<region.Height){
            int w=(region.Width+63)/64*64,h=(region.Height+63)/64*64;
            if(capture!=null){w=Math.Max(w,buffer.Width);h=Math.Max(h,buffer.Height);capture.Dispose();capture=null;buffer=null;}
            capture=new Surface.Dib(new Size(w,h),PixelFormat.Format32bppRgb);buffer=capture.bitmap;
        }
        if(video!=IntPtr.Zero){
            if(client!=screen)return null;
            if(lastPlayer!=video||videoFrames==null||videoFrames.Size!=client.Size){
                if(videoFrames!=null)videoFrames.Dispose();
                videoFrames=new VideoFrames(video,client.Size);lastPlayer=video;
            }
            return videoFrames.CopyLatest(capture.dc,region)?buffer:null;
        }
        if(player!=lastPlayer){
            var name=new StringBuilder(128);GetClassName(player,name,name.Capacity);
            composite=name.ToString()=="WPEDesktopDX11Window";lastPlayer=player;
        }
        var position=new Point(screen.X+region.X,screen.Y+region.Y);
        if(!composite&&Copy(player,new Point(position.X-client.X,position.Y-client.Y),region.Size)&&HasColor(region.Size))return buffer;
        if(overlay==IntPtr.Zero)return null;
        var anchor=Surface.DesktopAnchor();
        var visible=new Rectangle(position,region.Size);if(!Exposed(visible,overlay,anchor))return null;
        if(excluded!=overlay){
            if(!SetWindowDisplayAffinity(overlay,0x11))return null;
            excluded=overlay;DwmFlush();
        }
        return Copy(IntPtr.Zero,position,region.Size)&&Exposed(visible,overlay,anchor)?buffer:null;
    }
    sealed class VideoFrames : IDisposable {
        public readonly Size Size;
        readonly IntPtr player;
        readonly object gate=new object();
        readonly Stopwatch age=new Stopwatch();
        Surface.Dib front,back;
        bool pending,ready,closed;
        public VideoFrames(IntPtr player,Size size){
            this.player=player;Size=size;
            front=new Surface.Dib(size,PixelFormat.Format32bppRgb);
            try{back=new Surface.Dib(size,PixelFormat.Format32bppRgb);}catch{front.Dispose();throw;}
        }
        public bool CopyLatest(IntPtr dest,Rectangle region){
            lock(gate){
                if(!pending&&(!age.IsRunning||age.Elapsed.TotalMilliseconds>=1000.0/30)){
                    pending=true;age.Restart();ThreadPool.QueueUserWorkItem(Capture);
                }
                return ready&&BitBlt(dest,0,0,region.Width,region.Height,front.dc,region.X,region.Y,0x00cc0020)&&GdiFlush();
            }
        }
        void Capture(object unused){
            // PrintWindow waits for WebView2. The key and animation thread uses the last complete frame meanwhile.
            bool ok=!IsHungAppWindow(player)&&PrintWindow(player,back.dc,3)&&GdiFlush();
            lock(gate){
                pending=false;
                if(closed){back.Dispose();return;}
                if(ok){var previous=front;front=back;back=previous;ready=true;}
            }
        }
        public void Dispose(){
            lock(gate){
                closed=true;front.Dispose();
                if(!pending)back.Dispose();
            }
        }
    }
    bool Copy(IntPtr window,Point point,Size size){
        var dc=GetDC(window);if(dc==IntPtr.Zero)return false;
        try{
            bool copied=BitBlt(capture.dc,0,0,size.Width,size.Height,dc,point.X,point.Y,0x00cc0020);
            return copied&&GdiFlush();
        }finally{ReleaseDC(window,dc);}
    }
    bool HasColor(Size region){
        for(int i=1;i<=6;i++){
            int x=(region.Width-1)*i/7,y=(region.Height-1)*(i*3%7)/7;
            if((buffer.GetPixel(x,y).ToArgb()&0xffffff)!=0)return true;
        }
        return false;
    }
    public void Dispose(){
        if(videoFrames!=null){videoFrames.Dispose();videoFrames=null;}
        if(capture!=null){capture.Dispose();capture=null;buffer=null;}
        if(excluded!=IntPtr.Zero){SetWindowDisplayAffinity(excluded,0);excluded=IntPtr.Zero;}
        lastPlayer=IntPtr.Zero;
    }
}
