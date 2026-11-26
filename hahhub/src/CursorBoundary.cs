using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class KeyboardScreen {
    const string Prefix=@"\\?\DISPLAY#HIV1230#";
    const string MonitorClass="{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    // This model and its native timing are the identifiers of the IPI 84 panel.
    // Resolve the EDID for the exact instance, including when the saved panel is offline.
    public static bool Recognized(string id){
        if(String.IsNullOrEmpty(id)||!id.StartsWith(Prefix,StringComparison.OrdinalIgnoreCase))return false;
        var parts=id.Substring(Prefix.Length).Split('#');
        if(parts.Length!=2||parts[0].Length==0||parts[0].IndexOfAny(new[]{'\\','/'})>=0||
           !String.Equals(parts[1],MonitorClass,StringComparison.OrdinalIgnoreCase))return false;
        try{
            using(var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY\HIV1230\"+parts[0]+@"\Device Parameters")){
                var edid=key==null?null:key.GetValue("EDID") as byte[];
                return MatchesEdid(edid);
            }
        }catch(System.Security.SecurityException){return false;}catch(UnauthorizedAccessException){return false;}catch(IOException){return false;}
    }
    internal static bool MatchesEdid(byte[] e){
        if(e==null||e.Length<128||e[0]!=0||e[7]!=0||e[54]+e[55]==0)return false;
        for(int i=1;i<7;i++)if(e[i]!=255)return false;
        if(e[8]!=0x21||e[9]!=0x36||e[10]!=0x30||e[11]!=0x12)return false;
        int checksum=0;for(int i=0;i<128;i++)checksum+=e[i];
        if((checksum&255)!=0)return false;
        return (e[56]+((e[58]&240)<<4))==1920 && (e[59]+((e[61]&240)<<4))==720;
    }
    static string FileFor(string id){
        using(var sha=SHA256.Create()){
            string hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(id.ToUpperInvariant())),0,12).Replace("-","");
            return Path.Combine(Project.Data,"cursor-boundary-"+hash+".txt");
        }
    }
    public static bool Blocked(string id){
        try{return File.ReadAllText(FileFor(id)).Trim()=="1";}
        catch(FileNotFoundException){return true;}catch(DirectoryNotFoundException){return true;}
        catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
    }
    public static void SetBlocked(string id,bool block){Project.Atomic(FileFor(id),block?"1":"0");}

    internal static bool Eligible(Output target,IList<Output> screens){
        return target!=null&&Recognized(target.Id)&&CanIsolate(target,screens);
    }
    internal static bool CanIsolate(Output target,IList<Output> screens){
        if(target.Primary||target.Mirrored)return false;
        if(target.Bounds.IsEmpty)return true; // The switch configures a saved panel before reconnect.
        return target.Usable && screens.Any(d=>d.Primary&&d.Usable&&!d.Bounds.IntersectsWith(target.Bounds))
            &&!screens.Any(d=>d!=target&&d.Bounds.IntersectsWith(target.Bounds));
    }
}

internal sealed class CursorMap {
    readonly Output[] blocked,allowed;
    public CursorMap(Output[] blocked,Output[] allowed){this.blocked=blocked;this.allowed=allowed;}
    public static CursorMap From(List<Output> all){
        var restricted=all.Where(d=>d.Usable&&KeyboardScreen.Eligible(d,all)&&KeyboardScreen.Blocked(d.Id)&&!CalibrationGuide.ActiveOn(d.Id)).ToArray();
        if(restricted.Length==0)return null;
        var free=all.Where(d=>!d.Bounds.IsEmpty&&!restricted.Contains(d)).ToArray();
        return free.Any(d=>d.Primary)?new CursorMap(restricted,free):null;
    }
    public Output AtBlocked(Point p){
        for(int i=0;i<blocked.Length;i++)if(blocked[i].Bounds.Contains(p))return blocked[i];
        return null;
    }
    // If the keyboard is the only screen between two facing desktop edges, skip over it.
    Output Across(Rectangle from,Rectangle keyboard,Point next,out Point edge){
        edge=next;
        foreach(var dest in allowed){
            var to=dest.Bounds;
            if(from.Right==keyboard.Left && keyboard.Right==to.Left)edge=new Point(to.Left,next.Y);
            else if(from.Left==keyboard.Right && keyboard.Left==to.Right)edge=new Point(to.Right-1,next.Y);
            else if(from.Bottom==keyboard.Top && keyboard.Bottom==to.Top)edge=new Point(next.X,to.Top);
            else if(from.Top==keyboard.Bottom && keyboard.Top==to.Bottom)edge=new Point(next.X,to.Bottom-1);
            else continue;
            if(to.Contains(edge))return dest;
        }
        return null;
    }
    // Hold the entering edge while preserving tangential movement and drag button state.
    // When enabling the rule under the cursor, use the closest point on a free monitor.
    public Output ReturnPoint(Point current,Point next,out Point result){
        result=current;Output best=null;double distance=Double.PositiveInfinity;
        foreach(var screen in allowed){
            var r=screen.Bounds;
            var p=new Point(Math.Min(r.Right-1,Math.Max(next.X,r.Left)),Math.Min(r.Bottom-1,Math.Max(next.Y,r.Top)));
            if(r.Contains(current)){
                var keyboard=AtBlocked(next);
                if(keyboard!=null){
                    var other=Across(r,keyboard.Bounds,next,out result);
                    if(other!=null)return other;
                }
                result=p;return screen;
            }
            double dx=(double)next.X-p.X,dy=(double)next.Y-p.Y,score=dx*dx+dy*dy;
            if(score<distance){distance=score;result=p;best=screen;}
        }
        return best;
    }
}

// The hook has its own message loop, separate from both WPF and the effect renderer.
// It exists only while a verified secondary keyboard display requires a boundary.
internal sealed class CursorBoundary : IDisposable {
    const int RefreshMessage=0x8000+145,StopMessage=RefreshMessage+1;
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h,int msg,IntPtr w,IntPtr l);
    readonly Thread worker;
    volatile bool closing;
    IntPtr handle;
    public CursorBoundary(){
        worker=new Thread(Run){IsBackground=true,Name="hahhub pointer boundary"};
        worker.SetApartmentState(ApartmentState.STA);worker.Start();
    }
    void Run(){
        try{
            using(var window=new BoundaryWindow()){
                Interlocked.Exchange(ref handle,window.Handle);
                if(!closing){window.Refresh();Application.Run();}
            }
        }catch(Exception error){Trace.TraceWarning(error.Message);}
        finally{Interlocked.Exchange(ref handle,IntPtr.Zero);}
    }
    void Post(int msg){
        var h=Interlocked.CompareExchange(ref handle,IntPtr.Zero,IntPtr.Zero);
        if(h!=IntPtr.Zero)PostMessage(h,msg,IntPtr.Zero,IntPtr.Zero);
    }
    public void Refresh(){Post(RefreshMessage);}
    public void Dispose(){closing=true;Post(StopMessage);worker.Join(2000);}

    sealed class BoundaryWindow : NativeWindow,IDisposable {
        delegate IntPtr MouseProc(int code,IntPtr message,IntPtr data);
        [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int kind,MouseProc callback,IntPtr module,uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string module);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out Point position);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
        [DllImport("wtsapi32.dll")] static extern bool WTSRegisterSessionNotification(IntPtr window,int flags);
        [DllImport("wtsapi32.dll")] static extern bool WTSUnRegisterSessionNotification(IntPtr window);
        readonly System.Windows.Forms.Timer settle=new System.Windows.Forms.Timer{Interval=500};
        readonly MouseProc mouse;
        CursorMap map;IntPtr hook;bool locked;
        public BoundaryWindow(){
            mouse=OnMouse;
            CreateHandle(new CreateParams{Caption="hahhub pointer boundary",Style=unchecked((int)0x80000000),ExStyle=0x08000080});
            WTSRegisterSessionNotification(Handle,0);
            settle.Tick+=delegate{settle.Stop();Refresh();};
        }
        void Release(){
            map=null;
            if(hook!=IntPtr.Zero){UnhookWindowsHookEx(hook);hook=IntPtr.Zero;}
        }
        public void Refresh(){
            try{
                var next=locked?null:CursorMap.From(Displays.Connected());
                if(next==null){Release();return;}
                map=next;
                if(hook==IntPtr.Zero)hook=SetWindowsHookEx(14,mouse,GetModuleHandle(null),0);
                Point cursor;
                if(hook!=IntPtr.Zero&&GetCursorPos(out cursor))Redirect(cursor);
            }catch(Exception error){Release();Trace.TraceWarning(error.Message);}
        }
        bool Redirect(Point proposed){
            if(map==null)return false;
            var denied=map.AtBlocked(proposed);
            if(denied==null||!Displays.StillAt(denied,proposed))return false;
            Point current,edge;
            if(!GetCursorPos(out current))return false;
            var free=map.ReturnPoint(current,proposed,out edge);
            if(free==null||!Displays.StillAt(free,edge))return false;
            return edge==current||SetCursorPos(edge.X,edge.Y);
        }
        IntPtr OnMouse(int code,IntPtr msg,IntPtr data){
            if(code==0&&msg.ToInt64()==0x200&&map!=null){
                var point=new Point(Marshal.ReadInt32(data),Marshal.ReadInt32(data,4));
                if(Redirect(point))return new IntPtr(1);
            }
            return CallNextHookEx(IntPtr.Zero,code,msg,data);
        }
        protected override void WndProc(ref Message m){
            if(m.Msg==StopMessage){Release();Application.ExitThread();return;}
            if(m.Msg==RefreshMessage){Refresh();return;}
            if(m.Msg==0x007e||m.Msg==0x0219||m.Msg==0x0218){
                Release();settle.Stop();settle.Start();
            }
            if(m.Msg==0x02b1){
                if(m.WParam.ToInt32()==7){locked=true;Release();}
                if(m.WParam.ToInt32()==8){locked=false;Refresh();}
            }
            base.WndProc(ref m);
        }
        public void Dispose(){settle.Dispose();Release();WTSUnRegisterSessionNotification(Handle);DestroyHandle();}
    }
}
