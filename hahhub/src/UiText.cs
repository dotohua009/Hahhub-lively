using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

internal static class UiText
{
    static readonly string preference=Path.Combine(Project.Data,"language.txt");
    public static bool Chinese {get; private set;}
    static readonly Dictionary<string,string> ChineseText=new Dictionary<string,string>(StringComparer.Ordinal){
        {"Updates","软件更新"},
        {"Check now","检查更新"},
        {"Update and restart","更新并重启"},
        {"Check for a new version.","检查是否有新版本。"},
        {"Checking for updates…","正在检查更新…"},
        {"No published version yet.","更新通道暂未发布版本。"},
        {"Version {0} is available.","发现新版本 {0}。"},
        {"You’re up to date.","当前已是最新版。"},
        {"Could not check for updates. Check your connection and retry.","暂时无法检查更新，请检查网络后重试。"},
        {"Stop the current video and restart to update?","是否停止当前上屏视频并重启更新？"},
        {"Downloading and verifying…","正在下载并校验更新包…"},
        {"Update could not be installed. Retry when ready.","更新未能安装，可稍后重试。"},
        {"Update failed. Your previous version was restored.","上次更新失败，已恢复之前的版本。"},
        {" · Effects on"," · 交互已开启"},
        {" · Loading video…"," · 正在加载视频…"},
        {" · Mirrored"," · 复制模式"},
        {" · Mirrored · paused"," · 复制模式，已暂停"},
        {" · Offline"," · 离线"},
        {" · Video effects on"," · 视频交互已开启"},
        {" · Waiting for display"," · 等待连接"},
        {"A video is already casting to this display. Stop it first.","此屏幕已有视频上屏，请先停止原播放。"},
        {"Back to effects","返回效果"},
        {"Back to video","返回视频上屏"},
        {"Balanced","均衡"},
        {"Black Hole","黑洞"},
        {"Buffering","缓冲中"},
        {"Cannot play this video. Check the link and embedding permissions.","视频源无法播放，请检查链接或站点的播放权限。"},
        {"Cast video","开始上屏"},
        {"Casting failed ({0}).","视频上屏失败：{0}"},
        {"Casting stopped.","已停止上屏"},
        {"Casting stopped. The display is disconnected or mirrored.","目标屏已断开或处于复制模式，上屏已停止。"},
        {"Choose a display for keyboard effects","选择交互效果显示在哪块屏幕上"},
        {"Click or press a key","点击或按键预览"},
        {"Could not attach effects to the video.","无法连接视频交互层。"},
        {"Could not change playback. Try again.","无法切换播放状态，请重试。"},
        {"Could not locate the display. Start casting again.","无法定位到目标屏幕，请重新上屏。"},
        {"Could not read playback status. Start casting again.","无法读取播放状态，请重新上屏。"},
        {"Crack style updated in preview.","所选裂缝已在预览中生效"},
        {"Cracks","碎裂参数"},
        {"Density","裂纹密度"},
        {"Developer","二次开发作者"},
        {"Display saved.","已保存目标显示器"},
        {"Eco","省电"},
        {"Effect applied to the selected display.","已切换所选屏幕的交互效果"},
        {"Effect preview. Click or press a key.","互动预览，点击或按键"},
        {"Effect ready to preview.","已选择效果，可在上方预览"},
        {"Effects","我的效果库"},
        {"Effects appear on the selected display.","交互层叠加在所选屏幕的桌面上"},
        {"Effects paused","交互已暂停"},
        {"Effects paused.","已暂停交互"},
        {"Effects sample the live wallpaper. Casting temporarily switches the source to video.","使用此屏的动态壁纸画面绘制效果。视频上屏期间自动暂停。"},
        {"Effects will start when the display connects.","已记住目标，连接后启用交互"},
        {"Enable effects","启用交互"},
        {"Enter a complete HTTP or HTTPS video URL.","请输入完整的 HTTP 或 HTTPS 视频链接。"},
        {"Fill screen (crop edges)","等比铺满屏幕（裁切边缘）"},
        {"Fine","小裂缝"},
        {"Fracture","碎屏"},
        {"Frame rate saved.","已保存交互层功耗档"},
        {"GitHub","GitHub 项目"},
        {"Gravity","黑洞参数"},
        {"Heal delay","愈合延迟"},
        {"Heal duration","愈合时间"},
        {"Hold for 4.7 s to fill the display. Nearby holes merge. Release to let them shrink.","长按约 4.7 秒吞没目标屏。相邻键的洞口会合并，松手后收拢。"},
        {"IPI 84   /   Keyboard effects","IPI 84   /   按键交互壁纸"},
        {"Install Microsoft Edge WebView2 Runtime and try again.","请安装 Microsoft Edge WebView2 Runtime 后重试。"},
        {"Language","语言"},
        {"Lively Community  ↗","Lively 资源社区  ↗"},
        {"Mute","静音"},
        {"No extended display available","未检测到独立显示器"},
        {"No video frames received. Check that the video allows embedding, then try again.","未收到可播放画面。请确认视频可在站外播放，然后重试。"},
        {"Open hahhub","打开 hahhub"},
        {"Paste a Bilibili link or a direct MP4, WebM or M4V URL. Press Enter to cast.","粘贴哔哩哔哩视频播放页或 MP4、WebM、M4V 直链，按 Enter 上屏。"},
        {"Paste a Bilibili video page with a BV or av ID.","请粘贴 B 站视频播放页的 BV 或 av 链接。"},
        {"Pause","暂停播放"},
        {"Pause effects","暂停交互"},
        {"Paused","已暂停"},
        {"Performance","交互层功耗"},
        {"Playback finished. Desktop restored.","播放完毕，已返回桌面。"},
        {"Playing","正在播放"},
        {"Preview updated.","参数已在预览中生效"},
        {"Previous display","上次选择的显示器"},
        {"Pull strength","吸入强度"},
        {"Pull strength updated in preview.","吸入强度已在预览中生效"},
        {"Quit hahhub","退出 hahhub"},
        {"Ready to cast","等待播放"},
        {"Rendering stops when the effect ends.","效果结束后停止绘制。"},
        {"Resume","继续播放"},
        {"Save","保存参数"},
        {"Scan with WeChat to support HAHHAN.","使用微信扫一扫，支持 HAHHAN。"},
        {"Screen fit is unavailable. Start casting again.","画面适配暂不可用，请重新上屏。"},
        {"Select a connected display again.","请重新选择已连接的显示器"},
        {"Select a connected, extended display.","请选择已连接的扩展屏。"},
        {"Select display","选择目标屏幕"},
        {"Select display  ▾","选择显示器  ▾"},
        {"Settings","设置"},
        {"Settings saved.","保存成功。"},
        {"Smooth","流畅"},
        {"Spread","扩散范围"},
        {"Stop","停止上屏"},
        {"Support hahhub","捐赠支持"},
        {"The player could not load ({0}).","播放器加载失败（{0}）。"},
        {"The player stopped unexpectedly. Start casting again.","播放器已中断，请重新上屏。"},
        {"This display cannot show effects independently.","该显示器当前无法独立显示交互"},
        {"Use a Bilibili video page or a direct MP4, WebM or M4V link.","支持 B 站视频播放页，以及 MP4、WebM、M4V 视频直链。"},
        {"Use third-party wallpapers","支持第三方壁纸工具"},
        {"Use this display","切换到此屏"},
        {"Video Cast","视频上屏"},
        {"Video display","视频目标屏幕"},
        {"Video link","视频链接"},
        {"Wallpaper sampling is off.","已关闭第三方壁纸取样。"},
        {"Wallpaper sampling will resume when casting stops on {0}.","已暂停壁纸取样。{0} 停止上屏后将恢复。"},
        {"Wallpaper sampling will stay off after casting on {0}.","壁纸取样将保持关闭，当前上屏设备为 {0}。"},
        {"WeChat donation QR code for HAHHAN","HAHHAN 微信捐赠收款码"},
        {"Wide","大裂缝"},
    };
    public static void Load(){Chinese=File.Exists(preference)&&File.ReadAllText(preference).Trim()=="zh-CN";}
    public static void Save(bool chinese){Project.Atomic(preference,chinese?"zh-CN":"en");Chinese=chinese;}
    public static string T(string english){
        return Chinese?ChineseText[english]:english;
    }
    public static void Apply(Window window){
        foreach(var pair in ChineseText)window.Resources[pair.Key]=Chinese?pair.Value:pair.Key;
        window.Language=System.Windows.Markup.XmlLanguage.GetLanguage(Chinese?"zh-CN":"en");
    }
    public static string Retranslate(string message){
        foreach(var pair in ChineseText)
            if(pair.Key==message||pair.Value==message)return Chinese?pair.Value:pair.Key;
        return message;
    }
    public static string Display(string name){
        if(name==null)return "";
        string number=name.StartsWith("Display ",StringComparison.Ordinal)?name.Substring(8):
            name.StartsWith("屏幕 ",StringComparison.Ordinal)?name.Substring(3):null;
        return number==null?name:(Chinese?"屏幕 ":"Display ")+number;
    }
}
