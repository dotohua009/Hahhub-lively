using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;

internal sealed class StudioFrame
{
    readonly Window window;
    readonly Button maximize;
    readonly Path glyph;
    bool pressed;

    const int ExtendedStyle=-20, ToolWindow=0x80, AppWindow=0x40000, MaxButton=9;
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h,int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h,int index,int value);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h,int attribute,ref int value,int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr h,ref Margins margins);
    [StructLayout(LayoutKind.Sequential)] struct Margins {public int left,right,top,bottom;}
    [StructLayout(LayoutKind.Sequential)] struct MouseTrack {public int size,flags;public IntPtr window;public int time;}
    [DllImport("user32.dll")] static extern bool TrackMouseEvent(ref MouseTrack data);
    [StructLayout(LayoutKind.Sequential)] struct PixelPoint {public int x,y;}
    [StructLayout(LayoutKind.Sequential)] struct PixelRect {public int left,top,right,bottom;}
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo {public int size;public PixelRect monitor,work;public int flags;}
    [StructLayout(LayoutKind.Sequential)] struct MinMaxInfo {public PixelPoint reserved,maxSize,maxPosition,minTrack,maxTrack;}
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h,int flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);

    public StudioFrame(Window window){
        this.window=window;
        maximize=(Button)window.FindName("MaximizeWindow");
        glyph=(Path)window.FindName("MaximizeGlyph");
        WindowChrome.SetWindowChrome(window,new WindowChrome{
            CaptionHeight=35,GlassFrameThickness=new Thickness(-1),UseAeroCaptionButtons=false,
            ResizeBorderThickness=SystemParameters.WindowResizeBorderThickness
        });
        WindowChrome.SetIsHitTestVisibleInChrome((UIElement)window.FindName("CaptionButtons"),true);
        ((Button)window.FindName("MinimizeWindow")).Click+=delegate{SystemCommands.MinimizeWindow(window);};
        ((Button)window.FindName("CloseWindow")).Click+=delegate{window.Close();};
        maximize.Click+=delegate{ToggleSize();};
        window.StateChanged+=delegate{UpdateMaximizeButton();};
        UpdateMaximizeButton();
        window.SourceInitialized+=delegate{
            IntPtr h=new WindowInteropHelper(window).Handle;
            SetWindowLong(h,ExtendedStyle,(GetWindowLong(h,ExtendedStyle)&~AppWindow)|ToolWindow);
            int dark=1,round=2,border=0x605344,acrylic=3;
            DwmSetWindowAttribute(h,20,ref dark,4);
            DwmSetWindowAttribute(h,33,ref round,4);
            DwmSetWindowAttribute(h,34,ref border,4);
            var source=HwndSource.FromHwnd(h);
            var margins=new Margins{left=-1,right=-1,top=-1,bottom=-1};
            if(!SystemParameters.HighContrast&&DwmSetWindowAttribute(h,38,ref acrylic,4)==0&&DwmExtendFrameIntoClientArea(h,ref margins)==0)
                source.CompositionTarget.BackgroundColor=Colors.Transparent;
            else window.Background=new SolidColorBrush(Color.FromRgb(29,38,50));
            source.AddHook(WindowMessage);
        };
    }
    void ToggleSize(){
        if(window.WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(window);
        else SystemCommands.MaximizeWindow(window);
    }
    void UpdateMaximizeButton(){
        bool full=window.WindowState==WindowState.Maximized;
        string key=full?"Restore":"Maximize";
        maximize.SetResourceReference(FrameworkElement.ToolTipProperty,key);
        maximize.SetResourceReference(AutomationProperties.NameProperty,key);
        glyph.Data=Geometry.Parse(full?"M.5,3.5 H7.5 V10.5 H.5 Z M3,1 H10 V8 H9":"M.5,.5 H9.5 V9.5 H.5 Z");
    }
    IntPtr WindowMessage(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp,ref bool handled){
        if(msg==0x24){
            // A tool window otherwise maximizes over the taskbar.
            var monitor=new MonitorInfo{size=Marshal.SizeOf(typeof(MonitorInfo))};
            if(GetMonitorInfo(MonitorFromWindow(hwnd,2),ref monitor)){
                var limits=(MinMaxInfo)Marshal.PtrToStructure(lp,typeof(MinMaxInfo));
                limits.maxPosition.x=monitor.work.left-monitor.monitor.left;
                limits.maxPosition.y=monitor.work.top-monitor.monitor.top;
                limits.maxSize.x=monitor.work.right-monitor.work.left;
                limits.maxSize.y=monitor.work.bottom-monitor.work.top;
                var dpi=HwndSource.FromHwnd(hwnd).CompositionTarget.TransformToDevice;
                limits.minTrack.x=Math.Max(limits.minTrack.x,(int)Math.Ceiling(window.MinWidth*dpi.M11));
                limits.minTrack.y=Math.Max(limits.minTrack.y,(int)Math.Ceiling(window.MinHeight*dpi.M22));
                Marshal.StructureToPtr(limits,lp,false);handled=true;
            }
        }else if(msg==0x84&&maximize.IsVisible&&maximize.IsEnabled){
            // HTMAXBUTTON enables the Windows 11 snap layout menu.
            long xy=lp.ToInt64();
            var local=maximize.PointFromScreen(new Point((short)xy,(short)(xy>>16)));
            if(new Rect(maximize.RenderSize).Contains(local)){handled=true;return (IntPtr)MaxButton;}
        }else if(msg==0xa0&&wp==(IntPtr)MaxButton){
            if(maximize.Tag==null){
                maximize.Tag="Hot";
                var track=new MouseTrack{size=Marshal.SizeOf(typeof(MouseTrack)),flags=0x12,window=hwnd};
                TrackMouseEvent(ref track);
            }
        }else if(msg==0x2a2||msg==0x1f){
            pressed=false;maximize.Tag=null;
        }else if(msg==0xa1&&wp==(IntPtr)MaxButton){
            pressed=true;handled=true;
        }else if(msg==0xa2&&wp==(IntPtr)MaxButton&&pressed){
            pressed=false;handled=true;ToggleSize();
        }
        return IntPtr.Zero;
    }
}
