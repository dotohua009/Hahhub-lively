using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Only forwards transient key transitions to this wallpaper's WebView2 window.
// No text conversion, key history, file logging of input, or network listener.
internal sealed class KeyBloomInput : NativeWindow, IDisposable
{
    const string InstanceName = "Local\\KeyBloom.IPI84.InputBridge";
    readonly string root;
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly EventWaitHandle stop;
    IntPtr target;
    uint targetPid;
    bool registered;
    long count;
    DateTime lastStatus=DateTime.MinValue;
    [StructLayout(LayoutKind.Sequential)] struct Device { public ushort page, usage; public uint flags; public IntPtr hwnd; }
    [StructLayout(LayoutKind.Sequential)] struct Header { public uint type, size; public IntPtr device, wparam; }
    [StructLayout(LayoutKind.Sequential)] struct Keyboard { public ushort scan, flags, reserved, vk; public uint message, extra; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] struct Point { public int x,y; }
    delegate bool EnumProc(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll")] static extern bool RegisterRawInputDevices(Device[] devices,uint count,uint size);
    [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr input,uint command,IntPtr buffer,ref uint size,uint headerSize);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr param);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent,EnumProc callback,IntPtr param);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string title);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder text,int count);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd,uint msg,IntPtr wp,IntPtr lp);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);

    static IntPtr FindTarget(string folder)
    {
        string expected = "\"" + Path.Combine(folder,"index.html") + "\"";
        string playerPath = null;
        foreach (Process p in Process.GetProcessesByName("Lively"))
        { using(p) { try { playerPath=Path.Combine(Path.GetDirectoryName(p.MainModule.FileName),"plugins","webview2","Lively.Player.WebView2.exe"); break; } catch {} } }
        if(playerPath==null)return IntPtr.Zero;
        uint targetPid=0;
        using(var query=new ManagementObjectSearcher("SELECT ProcessId,CommandLine,ExecutablePath FROM Win32_Process WHERE Name='Lively.Player.WebView2.exe'"))
        using(var results=query.Get())
        {
            foreach(ManagementObject item in results)
            { using(item) { string cmd=item["CommandLine"] as string,exe=item["ExecutablePath"] as string;if(cmd!=null && string.Equals(exe,playerPath,StringComparison.OrdinalIgnoreCase) && cmd.IndexOf(expected,StringComparison.OrdinalIgnoreCase)>=0){targetPid=Convert.ToUInt32(item["ProcessId"]);break;} } }
        }
        if(targetPid==0)return IntPtr.Zero;
        IntPtr found=IntPtr.Zero;
        EnumProc inspect=delegate(IntPtr h,IntPtr ignored) {
            uint pid;GetWindowThreadProcessId(h,out pid);
            if(pid==targetPid){IntPtr child=FindWindowEx(h,IntPtr.Zero,"Chrome_WidgetWin_0",null);if(child!=IntPtr.Zero){IntPtr input=FindWindowEx(child,IntPtr.Zero,"Chrome_WidgetWin_1",null);if(input!=IntPtr.Zero){found=input;return false;}}}
            return true;
        };
        EnumWindows(delegate(IntPtr h,IntPtr p){inspect(h,p);if(found!=IntPtr.Zero)return false;EnumChildWindows(h,inspect,p);return found==IntPtr.Zero;},IntPtr.Zero);
        return found;
    }
    static IntPtr KeyFlags(ushort scan,ushort flags,bool down)
    { uint bits=1u|((uint)scan<<16);if((flags&2)!=0)bits|=1u<<24;if(!down)bits|=3u<<30;return new IntPtr(unchecked((int)bits)); }
    bool NativeLivelyAlreadyForwards()
    {
        var text=new StringBuilder(128);GetClassName(GetForegroundWindow(),text,text.Capacity);
        if(text.ToString()!="WorkerW" && text.ToString()!="Progman")return false;
        Point p;Rect r;return GetCursorPos(out p)&&GetWindowRect(target,out r)&&p.x>=r.left&&p.x<r.right&&p.y>=r.top&&p.y<r.bottom;
    }
    void Discover()
    {
        if(stop.WaitOne(0)){Application.ExitThread();return;}
        try {
            uint pid;
            if(target==IntPtr.Zero||!IsWindow(target)||GetWindowThreadProcessId(target,out pid)==0||pid!=targetPid){target=FindTarget(root);if(target!=IntPtr.Zero)GetWindowThreadProcessId(target,out targetPid);}
            bool enable=target!=IntPtr.Zero;
            if(enable!=registered){var d=new Device{page=1,usage=6,flags=enable?0x100u:1u,hwnd=enable?Handle:IntPtr.Zero};if(RegisterRawInputDevices(new[]{d},1,(uint)Marshal.SizeOf(typeof(Device))))registered=enable;lastStatus=DateTime.MinValue;}
            if(DateTime.UtcNow-lastStatus>=TimeSpan.FromSeconds(15)){
            File.WriteAllText(Path.Combine(root,"bridge-status.json"),"{\"active\":"+(enable&&registered?"true":"false")+",\"forwardedTransitions\":"+count+",\"pid\":"+Process.GetCurrentProcess().Id+",\"utc\":\""+DateTime.UtcNow.ToString("o")+"\"}");
            lastStatus=DateTime.UtcNow;}
        } catch { target=IntPtr.Zero; }
    }
    KeyBloomInput(string folder,EventWaitHandle stopEvent)
    {
        root=folder;stop=stopEvent;CreateHandle(new CreateParams{Caption="KeyBloom Input Bridge",Parent=new IntPtr(-3)});
        timer.Interval=1500;timer.Tick+=delegate{Discover();};Discover();timer.Start();
    }
    protected override void WndProc(ref Message message)
    {
        if(message.Msg==0x00ff && target!=IntPtr.Zero && IsWindow(target) && !NativeLivelyAlreadyForwards())
        {
            uint size=0,hs=(uint)Marshal.SizeOf(typeof(Header));
            if(GetRawInputData(message.LParam,0x10000003,IntPtr.Zero,ref size,hs)==0 && size>=hs+Marshal.SizeOf(typeof(Keyboard)) && size<1024)
            {
                IntPtr buffer=Marshal.AllocHGlobal((int)size);
                try {
                    if(GetRawInputData(message.LParam,0x10000003,buffer,ref size,hs)==size)
                    {
                        Header header=(Header)Marshal.PtrToStructure(buffer,typeof(Header));
                        if(header.type==1){Keyboard key=(Keyboard)Marshal.PtrToStructure(IntPtr.Add(buffer,(int)hs),typeof(Keyboard));if(key.vk>0 && key.vk<255){bool down=(key.flags&1)==0;
                            // WebView2 needs an internal focus notification; the OS foreground window stays unchanged.
                            if(down)PostMessage(target,0x7,IntPtr.Zero,IntPtr.Zero);
                            if(PostMessage(target,down?0x100u:0x101u,new IntPtr(key.vk),KeyFlags(key.scan,key.flags,down)))count++;}}
                    }
                } finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        base.WndProc(ref message);
    }
    public void Dispose(){timer.Dispose();if(registered)RegisterRawInputDevices(new[]{new Device{page=1,usage=6,flags=1,hwnd=IntPtr.Zero}},1,(uint)Marshal.SizeOf(typeof(Device)));DestroyHandle();}
    [STAThread] static int Main(string[] args)
    {
        try {
            string folder=Path.GetDirectoryName(Application.ExecutablePath);
            if(args.Length>0 && args[0]=="--stop"){try{using(var e=EventWaitHandle.OpenExisting(InstanceName+".Stop"))e.Set();}catch(WaitHandleCannotBeOpenedException){}return 0;}
            if(args.Length>0 && args[0]=="--self-test"){
                if(KeyFlags(0x1e,0,true).ToInt64()!=0x1e0001 || (KeyFlags(0x4d,2,true).ToInt64()&(1<<24))==0)throw new Exception("Invalid key flag mapping");
                IntPtr h=FindTarget(folder);if(h==IntPtr.Zero)throw new Exception("Wallpaper input window not found");
                IntPtr foreground=GetForegroundWindow();PostMessage(h,0x7,IntPtr.Zero,IntPtr.Zero);Thread.Sleep(50);
                if(!PostMessage(h,0x100,new IntPtr(65),KeyFlags(0x1e,0,true)))throw new Exception("Key down failed");Thread.Sleep(350);PostMessage(h,0x101,new IntPtr(65),KeyFlags(0x1e,0,false));
                if(GetForegroundWindow()!=foreground)throw new Exception("Foreground changed during test");
                File.WriteAllText(Path.Combine(folder,"bridge-self-test.json"),"{\"targetFound\":true,\"foregroundUnchanged\":true,\"syntheticKey\":\"KeyA\",\"utc\":\""+DateTime.UtcNow.ToString("o")+"\"}");return 0;
            }
            bool created;using(var mutex=new Mutex(true,InstanceName,out created)){if(!created)return 0;using(var stop=new EventWaitHandle(false,EventResetMode.ManualReset,InstanceName+".Stop"))using(var bridge=new KeyBloomInput(folder,stop)){Application.Run();}mutex.ReleaseMutex();}return 0;
        } catch(Exception e){File.WriteAllText(Path.Combine(Path.GetTempPath(),"KeyBloom-input-error.txt"),e.ToString());return 1;}
    }
}
