using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

internal sealed class VideoLink
{
    public readonly Uri Address;
    public readonly bool Bilibili;
    VideoLink(Uri address,bool bili){Address=address;Bilibili=bili;}

    public static VideoLink Parse(string text){
        Uri uri;
        if(!Uri.TryCreate(text.Trim(),UriKind.Absolute,out uri)||
           (uri.Scheme!="https"&&uri.Scheme!="http")||uri.UserInfo.Length!=0)
            throw new ArgumentException(UiText.T("Enter a complete HTTP or HTTPS video URL."));
        if(uri.Host=="www.bilibili.com"||uri.Host=="bilibili.com"||uri.Host=="m.bilibili.com"){
            var id=Regex.Match(uri.AbsolutePath,@"^/video/(BV[0-9a-zA-Z]{10}|av[0-9]{1,18})/?$");
            if(!id.Success)throw new ArgumentException(UiText.T("Paste a Bilibili video page with a BV or av ID."));
            var value=id.Groups[1].Value;
            string query=value.StartsWith("BV")?"bvid="+value:"aid="+value.Substring(2);
            foreach(var key in new[]{"p","t"}){
                var arg=Regex.Match(uri.Query,@"[?&]"+key+@"=([0-9]{1,8})(?:&|$)");
                if(arg.Success)query+="&"+key+"="+arg.Groups[1].Value;
            }
            return new VideoLink(new Uri("https://player.bilibili.com/player.html?"+query+"&autoplay=1&muted=0&danmaku=0"),true);
        }
        if(Regex.IsMatch(uri.AbsolutePath,@"\.(mp4|webm|m4v)$",RegexOptions.IgnoreCase))return new VideoLink(uri,false);
        throw new ArgumentException(UiText.T("Use a Bilibili video page or a direct MP4, WebM or M4V link."));
    }
}

internal sealed class VideoWindow : Form
{
    [StructLayout(LayoutKind.Sequential)] struct Rect{public int left,top,right,bottom;}
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int w,int h,uint flags);
    readonly Output output;
    readonly Action<string> report;
    CoreWebView2Controller controller;
    readonly Timer pulse=new Timer{Interval=1000};
    readonly Stopwatch loading=new Stopwatch();
    VideoLease lease;
    bool probing,hasPlayed;
    string result;
    public string Result{get{return result??UiText.T("Casting stopped.");}}
    public bool FillScreen=true;
    bool muted=true;
    public bool Muted{get{return muted;}set{muted=value;if(controller!=null)controller.CoreWebView2.IsMuted=value;}}
    public bool CanPause{get{return hasPlayed;}}
    public bool Paused{get;private set;}
    const string ProjectionCss="video{object-fit:var(--hahhan-fit,cover)!important}"+
        "video::-webkit-media-controls{display:none!important}"+
        ".bpx-player-control-wrap,.bpx-player-top-wrap,.bpx-player-state-wrap,.bpx-player-toast-wrap,"+
        ".bpx-player-tooltip-area,.bpx-player-context-area,.bpx-player-sending-area,.bpx-player-shadow-progress-area{display:none!important}"+
        "video,.bpx-player-video-area{cursor:none!important}";

    public VideoWindow(Output target,Action<string> report){
        output=target;this.report=report;
        Text=VideoLease.Caption;FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;
        AutoScaleMode=AutoScaleMode.None;ShowInTaskbar=false;BackColor=Color.Black;Bounds=target.Bounds;
        pulse.Tick+=async delegate{await UpdatePlayback();};
    }
    protected override bool ShowWithoutActivation{get{return true;}}
    protected override CreateParams CreateParams{get{
        var p=base.CreateParams;
        p.ExStyle=(p.ExStyle&~0x40000)|0x80|0x08000000; // Tool window, never activate the projection.
        return p;
    }}
    public void CheckOutput(){
        if(IsDisposed||Disposing)return;
        var prior=SetThreadDpiAwarenessContext(new IntPtr(-4));
        try{
            var live=Displays.Connected().FirstOrDefault(o=>o.Usable&&String.Equals(o.Id,output.Id,StringComparison.OrdinalIgnoreCase));
            if(live==null){Finish(UiText.T("Casting stopped. The display is disconnected or mirrored."));return;}
            var box=live.Bounds;Rect rect;
            if(!GetWindowRect(Handle,out rect)||Rectangle.FromLTRB(rect.left,rect.top,rect.right,rect.bottom)!=box)
                if(!SetWindowPos(Handle,IntPtr.Zero,box.X,box.Y,box.Width,box.Height,0x0014))
                    {Finish(UiText.T("Could not locate the display. Start casting again."));return;}
            if(controller!=null){
                var viewport=new Rectangle(Point.Empty,box.Size);
                if(controller.Bounds!=viewport)controller.Bounds=viewport;
                controller.NotifyParentWindowPositionChanged();
            }
        }finally{if(prior!=IntPtr.Zero)SetThreadDpiAwarenessContext(prior);}
    }
    protected override void WndProc(ref Message m){
        base.WndProc(ref m);
        if((m.Msg==0x007e||m.Msg==0x02e0)&&IsHandleCreated&&!IsDisposed)BeginInvoke((Action)CheckOutput);
    }
    void Finish(string message){if(IsDisposed||Disposing)return;result=message;Close();}
    public async Task Play(VideoLink link){
        try{
            CheckOutput();if(IsDisposed)return;
            lease=new VideoLease(output.Id,Handle);Show();CheckOutput();Host.NotifySettings();
            if(IsDisposed)return;
            var options=new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
            var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(Project.Data,"VideoPlayer"),options);
            if(IsDisposed)return;
            var created=await environment.CreateCoreWebView2ControllerAsync(Handle);
            if(IsDisposed){created.Close();return;}
            controller=created;controller.BoundsMode=CoreWebView2BoundsMode.UseRawPixels;
            controller.DefaultBackgroundColor=Color.Black;CheckOutput();
            if(IsDisposed)return;
            controller.IsVisible=true;
            controller.AcceleratorKeyPressed+=delegate(object sender,CoreWebView2AcceleratorKeyPressedEventArgs e){
                if(e.VirtualKey==(uint)Keys.Escape&&e.KeyEventKind==CoreWebView2KeyEventKind.KeyDown){e.Handled=true;BeginInvoke((Action)Close);}
            };
            var core=controller.CoreWebView2;
            core.IsMuted=muted;
            core.Settings.AreHostObjectsAllowed=false;core.Settings.IsWebMessageEnabled=false;
            core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.IsStatusBarEnabled=false;
            var document=link.Address;
            if(!link.Bilibili){
                // Handle just the local player document. The media URL streams normally, including range requests and redirects.
                document=new Uri(link.Address.GetLeftPart(UriPartial.Authority)+"/.hahhan/player-"+Guid.NewGuid().ToString("N"));
                string html="<!doctype html><meta name='viewport' content='width=device-width,initial-scale=1'><style>html,body,video{width:100%;height:100%;margin:0;background:black;overflow:hidden;display:block}"+ProjectionCss+"</style><video autoplay playsinline src=\""+WebUtility.HtmlEncode(link.Address.AbsoluteUri)+"\"></video>";
                core.AddWebResourceRequestedFilter(document.AbsoluteUri,CoreWebView2WebResourceContext.Document);
                core.WebResourceRequested+=delegate(object sender,CoreWebView2WebResourceRequestedEventArgs e){
                    if(e.Request.Uri==document.AbsoluteUri)e.Response=environment.CreateWebResourceResponse(
                        new MemoryStream(Encoding.UTF8.GetBytes(html)),200,"OK","Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nContent-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; media-src http: https:");
                };
            }
            core.NewWindowRequested+=delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e){e.Handled=true;};
            core.PermissionRequested+=delegate(object sender,CoreWebView2PermissionRequestedEventArgs e){e.State=CoreWebView2PermissionState.Deny;};
            core.DownloadStarting+=delegate(object sender,CoreWebView2DownloadStartingEventArgs e){e.Cancel=true;};
            core.NavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e){
                Uri u;if(!Uri.TryCreate(e.Uri,UriKind.Absolute,out u)){e.Cancel=true;return;}
                e.Cancel=link.Bilibili?!(u.Scheme=="https"&&u.Host=="player.bilibili.com"):
                    u!=document;
            };
            core.NavigationCompleted+=delegate(object sender,CoreWebView2NavigationCompletedEventArgs e){
                if(e.HttpStatusCode>=400||(!e.IsSuccess&&e.WebErrorStatus!=CoreWebView2WebErrorStatus.OperationCanceled)){
                    string reason=e.HttpStatusCode>=400?"HTTP "+e.HttpStatusCode:e.WebErrorStatus.ToString();
                    BeginInvoke((Action)(()=>Finish(string.Format(UiText.T("The player could not load ({0})."),reason))));
                }
            };
            core.DOMContentLoaded+=async delegate{
                await ApplyFit();
                if(!IsDisposed&&!link.Bilibili)try{
                    await core.ExecuteScriptAsync("(()=>{const v=document.querySelector('video');if(v){v.controls=false;v.autoplay=true;v.play().catch(()=>{})}})()");
                }catch(InvalidOperationException){}catch(COMException){}
            };
            core.ProcessFailed+=delegate{Finish(UiText.T("The player stopped unexpectedly. Start casting again."));};
            // Install the presentation style before the first paint, including hover and pause.
            string layout=link.Bilibili?"":"html,body,video{width:100%;height:100%;margin:0;background:black;overflow:hidden}";
            string css=Project.Json.Serialize(ProjectionCss+layout+(FillScreen?"":":root{--hahhan-fit:contain}"));
            await core.AddScriptToExecuteOnDocumentCreatedAsync("(()=>{if(window!==top)return;const add=()=>{if(!document.documentElement)return false;const s=document.createElement('style');s.textContent="+css+";document.documentElement.appendChild(s);return true};if(!add()){const o=new MutationObserver(()=>{if(add())o.disconnect()});o.observe(document,{childList:true,subtree:true})}})()");
            if(IsDisposed)return;
            loading.Start();pulse.Start();
            core.Navigate(document.AbsoluteUri);
        }catch(WebView2RuntimeNotFoundException){Finish(UiText.T("Install Microsoft Edge WebView2 Runtime and try again."));}
        catch(Exception e){Finish(string.Format(UiText.T("Casting failed ({0})."),e.Message));}
    }
    sealed class MediaState{
        public double time{get;set;}public double duration{get;set;}public int width{get;set;}
        public bool paused{get;set;}public bool ended{get;set;}public bool error{get;set;}public int ready{get;set;}
    }
    public async Task ApplyFit(){
        if(IsDisposed||controller==null)return;
        string mode=FillScreen?"cover":"contain";
        try{await controller.CoreWebView2.ExecuteScriptAsync("document.documentElement.style.setProperty('--hahhan-fit','"+mode+"')");}
        catch(InvalidOperationException){/* The player may have been closed during navigation. */}
        catch(System.Runtime.InteropServices.COMException){if(!IsDisposed)report(UiText.T("Screen fit is unavailable. Start casting again."));}
    }
    public async Task TogglePause(){
        if(IsDisposed||controller==null||!hasPlayed)return;
        try{await controller.CoreWebView2.ExecuteScriptAsync("(()=>{const v=document.querySelector('video');if(v){if(v.paused)v.play().catch(()=>{});else v.pause()}})()");await UpdatePlayback();}
        catch(InvalidOperationException){/* Window closed during playback. */}
        catch(COMException){if(!IsDisposed)report(UiText.T("Could not change playback. Try again."));}
    }
    static string Timestamp(double seconds){var n=(long)Math.Max(0,seconds);return (n/60)+":"+(n%60).ToString("00");}
    async Task UpdatePlayback(){
        if(probing||IsDisposed||controller==null)return;
        probing=true;
        try{
            const string probe="(()=>{const v=document.querySelector('video');return v?{time:v.currentTime||0,duration:Number.isFinite(v.duration)?v.duration:0,width:v.videoWidth,paused:v.paused,ended:v.ended,error:!!v.error,ready:v.readyState}:null})()";
            string json=await controller.CoreWebView2.ExecuteScriptAsync(probe);
            if(IsDisposed)return;
            var state=json=="null"?null:Project.Json.Deserialize<MediaState>(json);
            if(state!=null&&state.error){Finish(UiText.T("Cannot play this video. Check the link and embedding permissions."));return;}
            if(state!=null&&state.ended){Finish(UiText.T("Playback finished. Desktop restored."));return;}
            if(state!=null&&state.width>0&&state.time>0){
                hasPlayed=true;Paused=state.paused;string action=state.paused?UiText.T("Paused"):state.ready<3?UiText.T("Buffering"):UiText.T("Playing");
                report(output.DisplayName+" · "+action+"  "+Timestamp(state.time)+(state.duration>0?" / "+Timestamp(state.duration):""));
            }else if(!hasPlayed&&loading.Elapsed.TotalSeconds>35)Finish(UiText.T("No video frames received. Check that the video allows embedding, then try again."));
        }catch(Exception){if(!IsDisposed&&loading.Elapsed.TotalSeconds>35)Finish(UiText.T("Could not read playback status. Start casting again."));}
        finally{probing=false;}
    }
    protected override void Dispose(bool disposing){
        if(disposing){pulse.Dispose();if(controller!=null){controller.Close();controller=null;}}
        try{base.Dispose(disposing);}finally{if(disposing&&lease!=null){lease.Dispose();lease=null;}}
    }
}
