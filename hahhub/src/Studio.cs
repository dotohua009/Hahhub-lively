using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageView=System.Windows.Controls.Image;
using Forms=System.Windows.Forms;

internal sealed partial class Studio
{
    public readonly Window window;
    readonly Glass glass=new Glass();
    readonly SkillLibrary library;SkillOptions options;
    readonly BlackHole hole;readonly InkSplash ink;string selectedId=Project.Id;IKeyEffect effect;bool selecting,holeDirty,inkDirty;
    readonly DispatcherTimer preview=new DispatcherTimer(),status=new DispatcherTimer();
    int fps;
    bool dirty;
    Bitmap previewCover,boardBackdrop,previewPixels;
    WriteableBitmap previewImage;
    readonly Stopwatch previewClock=Stopwatch.StartNew();
    Rectangle boardWorld;
    string pendingId;
    string videoTargetId;
    VideoWindow video;
    bool updatingSupport;
    WindowState restoreState=WindowState.Normal;
    bool exiting,checking,installing;
    Updates available;
    DateTime lastCheck;
    string updateMessage="Check for a new version.";
    CursorBoundary cursorBoundary;
    string boundaryId;
    readonly Button targetButton;
    readonly Button applyButton;
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint key,uint type);
    T Get<T>(string id) where T:FrameworkElement{return (T)window.FindName(id);}
    static BitmapImage ImageFile(string path){var b=new BitmapImage();b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.UriSource=new Uri(path);b.EndInit();b.Freeze();return b;}
    void Note(string msg){Get<TextBlock>("Feedback").Text=msg;}
    public Studio(){
        hole=new BlackHole(glass.data.pitch);ink=new InkSplash(glass.data);library=new SkillLibrary(glass,hole,ink);options=SkillOptions.Read();library.Configure(options);effect=glass;
        using(var f=File.OpenRead(Path.Combine(Project.Base,"Studio.xaml")))window=(Window)XamlReader.Load(f);
        UiText.Load();
        UiText.Apply(window);
        window.ShowInTaskbar=false;
        window.Icon=BitmapFrame.Create(new Uri(Path.Combine(Project.Base,"studio.ico")));
        new StudioFrame(window);
        SetBoardBackdrop();
        Get<ImageView>("Tile").Source=ImageFile(Path.Combine(Project.Pack(),"thumbnail.jpg"));
        Get<ImageView>("HoleImage").Source=ImageFile(Path.Combine(Project.Base,"effects",BlackHole.Id,"thumbnail.jpg"));
        Get<ImageView>("InkImage").Source=ImageFile(Path.Combine(Project.Base,"effects",InkSplash.Id,"thumbnail.jpg"));
        foreach(var item in new[]{new[]{"FrostImage","frost"},new[]{"TendrilImage","tendrils"},new[]{"SilkImage","silk"},new[]{"FuseImage","fuse"},new[]{"PrismImage","prism"},new[]{"FogImage","fog"},new[]{"SnowImage","snow"},new[]{"GlitterImage","glitter"}})
            Get<ImageView>(item[0]).Source=ImageFile(Path.Combine(Project.Base,"effects","hahhub-materials","thumbnails",item[1]+".jpg"));
        glass.rimColor=Project.RimColor(false);hole.rimColor=Project.RimColor(true);
        InitPalette();InitAlignment();
        ink.tuning=InkTuning.Read();Get<Slider>("Spray").Value=ink.tuning.spread;Get<Slider>("Dry").Value=ink.tuning.recovery;
        UpdateInkLabels();foreach(var n in new[]{"Spray","Dry"})Get<Slider>(n).ValueChanged+=delegate{
            ink.tuning.spread=(int)Get<Slider>("Spray").Value;ink.tuning.recovery=(int)Get<Slider>("Dry").Value;
            UpdateInkLabels();inkDirty=true;Note(UiText.T("Preview updated."));
        };
        Get<Button>("ChooseRim").Click+=delegate{ChooseRim();};
        try{glass.tuning=Project.ReadTuning();}catch(IOException){}
        Get<Slider>("Density").Value=glass.tuning.density;Get<Slider>("Spread").Value=glass.tuning.spread;Get<Slider>("Heal").Value=glass.tuning.heal;
        Get<Slider>("Delay").Value=glass.tuning.delay;
        Get<RadioButton>(glass.tuning.crackSize==0?"LargeCracks":"SmallCracks").IsChecked=true;
        foreach(var n in new[]{"LargeCracks","SmallCracks"})Get<RadioButton>(n).Checked+=delegate{
            UpdateTuning();dirty=true;glass.Clear();TestHit(glass.scans[33]);glass.Release(glass.scans[33]);Note(UiText.T("Crack style updated in preview."));};
        foreach(var n in new[]{"Density","Spread","Heal","Delay"})Get<Slider>(n).ValueChanged+=delegate{UpdateTuning();dirty=true;Note(UiText.T("Preview updated."));};
        hole.power=Project.HolePower();Get<Slider>("Pull").Value=hole.power;
        Get<TextBlock>("PullNum").Text=hole.power+"%";
        Get<Slider>("Pull").ValueChanged+=delegate{hole.power=(int)Get<Slider>("Pull").Value;Get<TextBlock>("PullNum").Text=hole.power+"%";holeDirty=true;Note(UiText.T("Pull strength updated in preview."));};
        var fuseDelay=Get<Slider>("FuseDuration");
        fuseDelay.Value=options.fuseMs;
        UpdateFuseLabel();
        fuseDelay.ValueChanged+=delegate{
            library.Fuse.fuseMs=(int)Math.Round(fuseDelay.Value);
            UpdateFuseLabel();
            if(!fuseDelay.IsMouseCaptureWithin)SaveFuseDelay();
        };
        fuseDelay.AddHandler(Mouse.LostMouseCaptureEvent,new MouseEventHandler(delegate{SaveFuseDelay();}),true);
        InitCategories();InitWeather();
        ShowEffect(Project.ReadChoice().Effect);
        foreach(var entry in SkillLibrary.Entries)Get<RadioButton>(entry.tile).Checked+=delegate{
            if(selecting)return;
            var id=entry.id;
            try{var choice=Project.ReadChoice();choice.Effect=id;Project.SaveChoice(choice);Host.NotifySettings();ShowEffect(id);
                if(!Get<Grid>("EffectView").IsVisible)ShowPage("EffectView");
                if(choice.Enabled)Program.EnsureHost();
                Note(choice.Enabled?UiText.T("Effect applied to the selected display."):UiText.T("Effect ready to preview."));}
            catch(Exception e){ShowEffect(selectedId);Note(e.Message);}
        };
        foreach(var name in new[]{"Enhanced","Spectrum"}){
            var box=Get<CheckBox>(name);
            box.Click+=delegate{
                try{
                    var next=SkillOptions.Read();
                    if(name=="Spectrum")next.rainbow=box.IsChecked==true;
                    else if(effect==hole)next.holeEnhanced=box.IsChecked==true;
                    else if(effect==ink)next.inkEnhanced=box.IsChecked==true;
                    next.Save();options=next;library.Configure(next);Host.NotifySettings();
                    effect.Clear();var key=glass.scans[33];TestHit(key);ReleasePreview(key);Note(UiText.T("Settings saved."));
                }catch(Exception error){ShowEffect(selectedId);Note(error.Message);}
            };
        }
        fps=Project.Fps();Get<RadioButton>(fps==16?"Low":fps==24?"Mid":fps==60?"Ultra":"High").IsChecked=true;
        foreach(var n in new[]{"Low","Mid","High","Ultra"}){var r=Get<RadioButton>(n);r.Checked+=delegate{try{int cap=r.Name=="Low"?16:r.Name=="Mid"?24:r.Name=="Ultra"?60:30;Project.Atomic(Project.ProfilePath,cap.ToString());fps=cap;Host.NotifySettings();UpdateRate();Note(UiText.T("Frame rate saved."));}catch(Exception e){Note(e.Message);}};}
        targetButton=Get<Button>("Target");applyButton=Get<Button>("Apply");
        targetButton.Click+=delegate{ChooseOutput(targetButton,false);};
        Get<System.Windows.Controls.Primitives.ToggleButton>("CursorBoundary").Click+=delegate{
            try{
                var saved=Project.ReadChoice();var screens=Choices(saved);
                var pick=screens.FirstOrDefault(d=>Same(d.Id,pendingId??saved.Id));
                if(pick!=null&&Same(pick.Id,boundaryId)&&KeyboardScreen.Eligible(pick,screens)){
                    bool block=Get<System.Windows.Controls.Primitives.ToggleButton>("CursorBoundary").IsChecked==true;
                    KeyboardScreen.SetBlocked(pick.Id,block);
                    if(cursorBoundary!=null)cursorBoundary.Refresh();
                }
            }catch(Exception error){Note(error.Message);}
            Refresh();
        };
        var videoTarget=Get<Button>("VideoTarget");
        videoTarget.Click+=delegate{ChooseOutput(videoTarget,true);};
        var videoTile=Get<RadioButton>("VideoTile");
        videoTile.Checked+=delegate{ShowPage("VideoView");};
        videoTile.Click+=delegate{if(!Get<Grid>("VideoView").IsVisible)ShowPage("VideoView");};
        Get<Button>("StartVideo").Click+=delegate{StartVideo();};
        Get<Button>("StopVideo").Click+=delegate{if(video!=null)video.Close();};
        Get<Button>("PauseVideo").Click+=async delegate{if(video!=null)await video.TogglePause();};
        var mute=Get<CheckBox>("MuteVideo");
        RoutedEventHandler muteChanged=delegate{if(video!=null)video.Muted=mute.IsChecked==true;};
        mute.Checked+=muteChanged;mute.Unchecked+=muteChanged;
        var fill=Get<CheckBox>("FillVideo");
        RoutedEventHandler fitChanged=async delegate{var session=video;if(session!=null){session.FillScreen=fill.IsChecked==true;await session.ApplyFit();}};
        fill.Checked+=fitChanged;fill.Unchecked+=fitChanged;
        Get<TextBox>("VideoUrl").KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Enter){e.Handled=true;StartVideo();}};
        Get<Button>("Save").Click+=delegate{try{SaveSettings();Note(UiText.T("Settings saved."));}catch(Exception e){Note(e.Message);}};
        applyButton.Click+=delegate{Apply();};
        var projectLink=(System.Windows.Documents.Hyperlink)window.FindName("ProjectLink");
        projectLink.RequestNavigate+=delegate(object sender,System.Windows.Navigation.RequestNavigateEventArgs e){
            e.Handled=true;
            try{Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri){UseShellExecute=true});}catch(Exception ex){Note(ex.Message);}
        };
        Get<Button>("Settings").Click+=delegate{ShowPage("SettingsView");};
        Get<TextBlock>("VersionLabel").Text="v"+Updates.Current;
        Get<Button>("CheckUpdate").Click+=delegate{CheckUpdates(true);};
        Get<Button>("InstallUpdate").Click+=delegate{InstallAvailable();};
        var english=Get<RadioButton>("LangEnglish");
        var chinese=Get<RadioButton>("LangChinese");
        (UiText.Chinese?chinese:english).IsChecked=true;
        RoutedEventHandler chooseLanguage=delegate(object sender,RoutedEventArgs e){
            bool useChinese=sender==chinese;
            if(useChinese==UiText.Chinese)return;
            try{UiText.Save(useChinese);}
            catch(Exception error){(UiText.Chinese?chinese:english).IsChecked=true;MessageBox.Show(window,error.Message,window.Title);return;}
            UiText.Apply(window);
            UpdateSkillLabels();
            Get<Button>("BackToEffect").Content=UiText.T(videoTile.IsChecked==true?"Back to video":"Back to effects");
            Get<Button>("PauseVideo").Content=UiText.T(video!=null&&video.Paused?"Resume":"Pause");
            var feedback=Get<TextBlock>("Feedback");feedback.Text=UiText.Retranslate(feedback.Text);
            var playback=Get<TextBlock>("VideoStatus");playback.Text=UiText.Retranslate(playback.Text);
            UpdateControls();
            Refresh();
        };
        chinese.Checked+=chooseLanguage;english.Checked+=chooseLanguage;
        var support=Get<CheckBox>("WallpaperSupport");
        RoutedEventHandler saveSupport=delegate{
            if(updatingSupport||!support.IsEnabled)return;
            try{Project.WallpaperIntegration=support.IsChecked==true;Host.NotifySettings();Refresh();}
            catch(Exception e){Refresh();Get<TextBlock>("WallpaperSupportState").Text=e.Message;}
        };
        support.Checked+=saveSupport;support.Unchecked+=saveSupport;
        Get<Button>("BackToEffect").Click+=delegate{ShowPage(videoTile.IsChecked==true?"VideoView":"EffectView");};
        foreach(var entry in SkillLibrary.Entries)Get<RadioButton>(entry.tile).Click+=delegate{
            if(!Get<Grid>("EffectView").IsVisible)ShowPage("EffectView");
        };
        window.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){
            if(Get<Grid>("EditorShade").Visibility==Visibility.Visible){EditorKey(e);return;}
            if(e.Key==Key.Escape&&Get<Grid>("SettingsView").IsVisible){ShowPage(videoTile.IsChecked==true?"VideoView":"EffectView");e.Handled=true;}
        };
        Get<Button>("Community").Click+=delegate{try{Process.Start(new ProcessStartInfo("https://github.com/lively-community/lively/discussions"){UseShellExecute=true});}catch(Exception e){Note(e.Message);}};
        var hero=Get<Grid>("Hero");LayoutKey mouseKey=null;
        hero.SizeChanged+=delegate{hero.Clip=new RectangleGeometry(new Rect(hero.RenderSize),11,11);};
        hero.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){
            hero.Focus();var pt=e.GetPosition(Get<Canvas>("BoardPlane"));
            mouseKey=glass.data.keys.FirstOrDefault(k=>k.cap.Contains((float)pt.X+boardWorld.X,(float)pt.Y+boardWorld.Y));
            if(mouseKey!=null){hero.CaptureMouse();TestHit(mouseKey);}};
        hero.MouseLeftButtonUp+=delegate{if(mouseKey!=null){ReleasePreview(mouseKey);mouseKey=null;}hero.ReleaseMouseCapture();};
        hero.LostMouseCapture+=delegate{if(mouseKey!=null){ReleasePreview(mouseKey);mouseKey=null;}};
        hero.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.IsRepeat||e.Key==Key.Tab)return;var k=PreviewKey(e);if(k!=null){TestHit(k);e.Handled=true;}};
        hero.KeyUp+=delegate(object sender,KeyEventArgs e){var k=PreviewKey(e);if(k!=null){ReleasePreview(k);e.Handled=true;}};
        hero.LostKeyboardFocus+=delegate{effect.Clear();};
        preview.Tick+=delegate{PreviewFrame();};status.Interval=TimeSpan.FromSeconds(2);status.Tick+=delegate{Refresh();};
        window.Loaded+=delegate{Refresh();status.Start();};window.Closed+=delegate{preview.Stop();status.Stop();if(video!=null)video.Close();library.Dispose();glass.Dispose();hole.Dispose();ink.Clear();if(previewCover!=null)previewCover.Dispose();if(boardBackdrop!=null)boardBackdrop.Dispose();if(previewPixels!=null)previewPixels.Dispose();};
        UpdateTuning();UpdateRate();Note(UiText.T("Effects appear on the selected display."));
    }
    public void OpenPanel(){
        window.Show();
        if(window.WindowState==WindowState.Minimized)window.WindowState=restoreState;
        window.Activate();
    }
    public int Run(Application app,bool background,string updated){
        using(cursorBoundary=new CursorBoundary())
        using(var icon=new Icon(Path.Combine(Project.Base,"studio.ico")))
        using(var menu=new Forms.ContextMenu())
        using(var tray=new Forms.NotifyIcon{Text=window.Title,Icon=icon,ContextMenu=menu}){
            var open=menu.MenuItems.Add(UiText.T("Open hahhub"),delegate{OpenPanel();});
            var quit=menu.MenuItems.Add(UiText.T("Quit hahhub"),delegate{exiting=true;Program.StopHost();app.Shutdown();});
            menu.Popup+=delegate{open.Text=UiText.T("Open hahhub");quit.Text=UiText.T("Quit hahhub");};
            tray.MouseDoubleClick+=delegate(object sender,Forms.MouseEventArgs e){if(e.Button==Forms.MouseButtons.Left)OpenPanel();};
            app.SessionEnding+=delegate{exiting=true;};
            app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            window.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){
                if(!exiting){e.Cancel=true;window.Dispatcher.BeginInvoke((Action)window.Hide);}
            };
            window.StateChanged+=delegate{
                if(window.WindowState==WindowState.Minimized)window.Hide();else restoreState=window.WindowState;
            };
            window.IsVisibleChanged+=delegate{
                if(window.IsVisible){Refresh();status.Start();}
                else{status.Stop();preview.Stop();effect.Clear();Get<ImageView>("LivePreview").Source=null;}
            };
            tray.Visible=true;
            if(Project.ReadChoice().Enabled)Program.EnsureHost();
            if(!background)OpenPanel();
            Guid token;
            if(Guid.TryParseExact(updated,"N",out token)){
                System.Threading.EventWaitHandle ready;
                if(System.Threading.EventWaitHandle.TryOpenExisting(@"Local\hahhub.Updated."+token.ToString("N"),out ready))
                    using(ready)ready.Set();
            }
            return app.Run();
        }
    }
    void ShowPage(string page){
        if(page=="SettingsView"){
            var qr=Get<ImageView>("DonationCode");
            if(qr.Source==null)qr.Source=ImageFile(Path.Combine(Project.Base,"donation.png"));
            Get<Button>("BackToEffect").Content=Get<RadioButton>("VideoTile").IsChecked==true?UiText.T("Back to video"):UiText.T("Back to effects");
        }
        foreach(var view in new[]{"EffectView","SettingsView","VideoView"})Get<Grid>(view).Visibility=view==page?Visibility.Visible:Visibility.Collapsed;
        if(page=="SettingsView"){Refresh();CheckUpdates(false);}
        if(page=="VideoView"){Refresh();Get<TextBox>("VideoUrl").Focus();}
        else Get<Button>(page=="SettingsView"?"BackToEffect":"Settings").Focus();
    }
    void UpdateControls(){
        string text=UiText.T(updateMessage);
        if(available!=null&&updateMessage=="Version {0} is available.")text=string.Format(text,available.Version);
        Get<TextBlock>("UpdateStatus").Text=text;
        Get<Button>("CheckUpdate").IsEnabled=!checking&&!installing;
        var apply=Get<Button>("InstallUpdate");
        apply.Visibility=available!=null&&available.Version>Updates.Current?Visibility.Visible:Visibility.Collapsed;
        apply.IsEnabled=!checking&&!installing;
    }
    async void CheckUpdates(bool manual){
        if(checking||installing||(!manual&&DateTime.UtcNow-lastCheck<TimeSpan.FromMinutes(15))){UpdateControls();return;}
        checking=true;updateMessage="Checking for updates…";UpdateControls();
        try{
            available=await Updates.Check();
            updateMessage=available==null?"No published version yet.":available.Version>Updates.Current?"Version {0} is available.":"You’re up to date.";
            var receipt=Path.Combine(Project.Data,"last-update.txt");
            if(File.Exists(receipt)&&File.ReadAllText(receipt)=="restored"){
                if(available==null||available.Version<=Updates.Current)updateMessage="Update failed. Your previous version was restored.";
                File.Delete(receipt);
            }
        }catch(Exception){available=null;updateMessage="Could not check for updates. Check your connection and retry.";}
        finally{checking=false;lastCheck=DateTime.UtcNow;if(!exiting)UpdateControls();}
    }
    async void InstallAvailable(){
        if(available==null||checking||installing)return;
        if(video!=null&&MessageBox.Show(window,UiText.T("Stop the current video and restart to update?"),window.Title,MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        installing=true;updateMessage="Downloading and verifying…";UpdateControls();
        try{await available.Start();exiting=true;Program.StopHost();Application.Current.Shutdown();}
        catch(Exception){installing=false;updateMessage="Update could not be installed. Retry when ready.";UpdateControls();}
    }
    async void StartVideo(){
        var feedback=Get<TextBlock>("VideoStatus");
        try{
            var link=VideoLink.Parse(Get<TextBox>("VideoUrl").Text);
            string id=videoTargetId??Project.ReadChoice().Id;
            var target=Displays.Connected().FirstOrDefault(x=>Same(x.Id,id)&&x.Usable);
            if(target==null)throw new InvalidOperationException(UiText.T("Select a connected, extended display."));
            if(Project.ReadChoice().Enabled)Program.EnsureHost();
            if(video!=null)video.Close();
            var pause=Get<Button>("PauseVideo");
            var session=new VideoWindow(target,s=>{feedback.Text=s;pause.IsEnabled=video!=null&&video.CanPause;pause.Content=video!=null&&video.Paused?UiText.T("Resume"):UiText.T("Pause");}){
                FillScreen=Get<CheckBox>("FillVideo").IsChecked==true,Muted=Get<CheckBox>("MuteVideo").IsChecked==true};video=session;
            session.FormClosed+=delegate{if(video==session){video=null;Get<Button>("StopVideo").IsEnabled=false;pause.IsEnabled=false;pause.Content=UiText.T("Pause");feedback.Text=session.Result;}};
            Get<Button>("StopVideo").IsEnabled=true;feedback.Text=target.DisplayName+UiText.T(" · Loading video…");
            await session.Play(link);
            if(session.IsDisposed&&video==session){video=null;Get<Button>("StopVideo").IsEnabled=false;feedback.Text=session.Result;}
        }catch(Exception e){feedback.Text=e.Message;}
    }
    void InitCategories(){
        var tabs=Get<Panel>("CategoryTabs");
        foreach(RadioButton tab in tabs.Children){
            tab.Checked+=delegate{
                foreach(RadioButton item in tabs.Children)
                    Get<StackPanel>((string)item.Tag).Visibility=item==tab?Visibility.Visible:Visibility.Collapsed;
            };
        }
    }
    string EffectName{get{return SkillLibrary.Find(selectedId).title;}}
    bool BodyColor{get{return selectedId==Tendrils.Id||selectedId==SilkColony.Id;}}
    int SelectedColor{get{return selectedId==Tendrils.Id?options.tendrilColor:selectedId==SilkColony.Id?options.spiderColor:effect==hole?hole.rimColor:glass.rimColor;}}
    void UpdateSkillLabels(){
        var entry=SkillLibrary.Find(selectedId);Get<TextBlock>("EffectTitle").Text=UiText.T(entry.title);
        Get<TextBlock>("SkillHint").Text=entry.hint==""?"":UiText.T(entry.hint);
        string caption=UiText.T(BodyColor?"Body color":"Edge glow");
        Get<TextBlock>("PigmentCaption").Text=caption;Get<TextBlock>("PaletteTitle").Text=caption;
        System.Windows.Automation.AutomationProperties.SetName(Get<Button>("ChooseRim"),caption);
    }
    void PaintSwatch(){
        int rgb=SelectedColor;
        Get<Border>("RimSwatch").Background=new SolidColorBrush(System.Windows.Media.Color.FromRgb((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb));
        Get<TextBlock>("RimHex").Text="#"+rgb.ToString("X6");
    }
    void ShowEffect(string id){
        selecting=true;
        try{
            bool useHole=id==BlackHole.Id,useInk=id==InkSplash.Id;
            var entry=SkillLibrary.Find(id)??SkillLibrary.Entries[0];selectedId=entry.id;
            preview.Stop();effect.Clear();effect=library.Select(selectedId);if(fps>0)effect.Budget.SetRate(fps);
            Get<ImageView>("LivePreview").Source=null;UpdateSkillLabels();
            var tile=Get<RadioButton>(entry.tile);tile.IsChecked=true;
            foreach(RadioButton tab in Get<Panel>("CategoryTabs").Children)
                if((string)tab.Tag==((Panel)tile.Parent).Name){tab.IsChecked=true;break;}
            Get<StackPanel>("GlassPanel").Visibility=effect==glass?Visibility.Visible:Visibility.Collapsed;
            Get<StackPanel>("HolePanel").Visibility=useHole?Visibility.Visible:Visibility.Collapsed;
            ShowWeatherControls();
            Get<StackPanel>("FusePanel").Visibility=selectedId==VoxelFuse.Id?Visibility.Visible:Visibility.Collapsed;
            Get<StackPanel>("InkPanel").Visibility=useInk?Visibility.Visible:Visibility.Collapsed;
            Get<Grid>("RimRow").Visibility=effect==glass||useHole||BodyColor?Visibility.Visible:Visibility.Collapsed;
            Get<StackPanel>("SkillPanel").Visibility=entry.hint==""?Visibility.Collapsed:Visibility.Visible;
            var toggle=Get<CheckBox>("Enhanced");toggle.Visibility=useInk||useHole?Visibility.Visible:Visibility.Collapsed;
            toggle.IsChecked=useHole?options.holeEnhanced:options.inkEnhanced;
            var spectrum=Get<CheckBox>("Spectrum");spectrum.Visibility=selectedId==Tendrils.Id?Visibility.Visible:Visibility.Collapsed;spectrum.IsChecked=options.rainbow;
            PaintSwatch();
        }finally{selecting=false;}
    }
    void UpdateFuseLabel(){
        Get<TextBlock>("FuseDurationValue").Text=(library.Fuse.fuseMs/1000.0).ToString("0.00")+" s";
    }
    void SaveFuseDelay(){
        if(options.fuseMs==library.Fuse.fuseMs)return;
        try{
            var next=SkillOptions.Read();
            next.fuseMs=library.Fuse.fuseMs;
            next.Save();
            options=next;
            Host.NotifySettings();
            Note(UiText.T("Settings saved."));
        }catch(Exception error){
            Get<Slider>("FuseDuration").Value=options.fuseMs;
            Note(error.Message);
        }
    }
    void UpdateInkLabels(){
        Get<TextBlock>("SprayNum").Text=ink.tuning.spread+"%";Get<TextBlock>("DryNum").Text=(ink.tuning.recovery/1000.0).ToString("0.0")+" s";
    }
    void SaveSettings(){
        if(effect is WeatherScene){SaveWeather();return;}
        if(effect==library.Fuse){SaveFuseDelay();return;}
        if(effect==hole){Project.Atomic(Project.HolePowerPath,hole.power.ToString());holeDirty=false;}
        else if(effect==ink){ink.tuning.Save();inkDirty=false;}
        else if(effect==glass){Project.SaveTuning(glass.tuning);dirty=false;}
        Host.NotifySettings();
    }
    void UpdateTuning(){var t=glass.tuning;t.density=(int)Get<Slider>("Density").Value;t.spread=(int)Get<Slider>("Spread").Value;t.heal=(int)Get<Slider>("Heal").Value;
        t.delay=(int)Get<Slider>("Delay").Value;t.crackSize=Get<RadioButton>("SmallCracks").IsChecked==true?1:0;
        Get<TextBlock>("DelayNum").Text=(t.delay/1000.0).ToString("0.0")+" s";
        Get<TextBlock>("DensityNum").Text=t.density+"%";Get<TextBlock>("SpreadNum").Text=t.spread+"%";Get<TextBlock>("HealNum").Text=(t.heal/1000.0).ToString("0.00")+" s";}
    void UpdateRate(){effect.Budget.SetRate(fps);Get<TextBlock>("Rate").Text=fps+" FPS";preview.Interval=TimeSpan.FromMilliseconds(Math.Ceiling(1000.0/fps));}
    LayoutKey PreviewKey(KeyEventArgs e){
        var key=e.Key==Key.System?e.SystemKey:e.Key;uint vk=MapVirtualKey((uint)KeyInterop.VirtualKeyFromKey(key),4);
        int scan=(int)(vk&255)+((vk>>8)==0xe0?256:0);LayoutKey k;return glass.scans.TryGetValue(scan,out k)?k:null;
    }
    void TestHit(LayoutKey k){effect.Strike(k);if(!preview.IsEnabled){PreviewFrame();if(effect.Active)preview.Start();}}
    void ReleasePreview(LayoutKey key){effect.Release(key);if(effect.Active&&!preview.IsEnabled)preview.Start();}
    static BitmapSource FrozenBitmap(Bitmap bmp){
        var bits=bmp.LockBits(new Rectangle(0,0,bmp.Width,bmp.Height),ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try{var src=BitmapSource.Create(bmp.Width,bmp.Height,96,96,PixelFormats.Pbgra32,null,bits.Scan0,bits.Stride*bmp.Height,bits.Stride);src.Freeze();return src;}
        finally{bmp.UnlockBits(bits);}
    }
    void SetBoardBackdrop(){
        boardWorld=KeyboardStage.World(glass.data);
        if(boardBackdrop!=null)boardBackdrop.Dispose();
        if(previewCover!=null){previewCover.Dispose();previewCover=null;}
        boardBackdrop=KeyboardStage.Create(glass.data,boardWorld);
        var plane=Get<Canvas>("BoardPlane");plane.Width=boardWorld.Width;plane.Height=boardWorld.Height;
        var cover=Get<ImageView>("Cover");cover.Width=boardWorld.Width;cover.Height=boardWorld.Height;cover.Source=FrozenBitmap(boardBackdrop);
        var image=Get<ImageView>("LivePreview");image.Width=boardWorld.Width;image.Height=boardWorld.Height;
    }
    void PreviewFrame(){
        double started=previewClock.Elapsed.TotalMilliseconds;
        var target=Get<ImageView>("LivePreview");var hero=Get<Grid>("Hero");double fit=Math.Min(1,Math.Min(hero.ActualWidth/boardWorld.Width,hero.ActualHeight/boardWorld.Height));
        int w=Math.Max(1,(int)(1920*fit)),h=Math.Max(1,(int)(720*fit));
        var skill=effect as SkillScene;if(skill!=null)skill.RenderWorld=boardWorld;
        if(effect.Bounds(w,h).IsEmpty&&(skill==null||!skill.Active)){preview.Stop();target.Source=null;return;}
        if(previewCover==null||previewCover.Width!=w||previewCover.Height!=h){
            if(previewCover!=null)previewCover.Dispose();
            using(var crop=boardBackdrop.Clone(new Rectangle(-boardWorld.X,-boardWorld.Y,1920,720),boardBackdrop.PixelFormat))previewCover=new Bitmap(crop,w,h);
        }
        float sx=w/1920f,sy=h/720f;
        int pw=Math.Max(1,(int)Math.Round(boardWorld.Width*sx)),ph=Math.Max(1,(int)Math.Round(boardWorld.Height*sy));
        if(previewPixels==null||previewPixels.Width!=pw||previewPixels.Height!=ph){
            if(previewPixels!=null)previewPixels.Dispose();
            previewPixels=new Bitmap(pw,ph,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            previewImage=new WriteableBitmap(pw,ph,96,96,PixelFormats.Pbgra32,null);
        }
        using(var g=Graphics.FromImage(previewPixels)){
            g.Clear(System.Drawing.Color.Transparent);g.TranslateTransform(-boardWorld.X*sx,-boardWorld.Y*sy);
            effect.Draw(g,w,h,0,0,previewCover,new Rectangle(0,0,w,h));
        }
        var bits=previewPixels.LockBits(new Rectangle(0,0,pw,ph),ImageLockMode.ReadOnly,previewPixels.PixelFormat);
        try{previewImage.WritePixels(new Int32Rect(0,0,pw,ph),bits.Scan0,bits.Stride*ph,bits.Stride);}
        finally{previewPixels.UnlockBits(bits);}
        if(target.Source!=previewImage)target.Source=previewImage;
        double workMs=previewClock.Elapsed.TotalMilliseconds-started;
        effect.Budget.Observe(workMs);
        var interval=TimeSpan.FromMilliseconds(Math.Ceiling(Math.Max(1000.0/Math.Max(1,fps),workMs+effect.Budget.RestAfter(workMs))));
        if(preview.Interval!=interval)preview.Interval=interval;
        if(effect==hole&&hole.Parked)preview.Stop();
    }
    static List<Output> Choices(OutputChoice saved){
        var screens=Displays.Connected();
        if(!string.IsNullOrEmpty(saved.Id)&&!screens.Any(x=>Same(x.Id,saved.Id)))
            screens.Insert(0,new Output{Id=saved.Id,Label=(saved.Label??UiText.T("Previous display"))});
        return screens;
    }
    static bool Same(string a,string b){return string.Equals(a,b,StringComparison.OrdinalIgnoreCase);}
    void ChooseOutput(Button anchor,bool forVideo){
        try{
            var saved=Project.ReadChoice();var menu=new ContextMenu{PlacementTarget=anchor,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom};
            foreach(var screen in Choices(saved)){
                var item=new MenuItem{Header=screen.ToString(),IsChecked=Same(screen.Id,(forVideo?videoTargetId:pendingId)??saved.Id),IsEnabled=forVideo?screen.Usable:!screen.Mirrored};
                item.Click+=delegate{if(forVideo)videoTargetId=screen.Id;else pendingId=screen.Id;Refresh();};menu.Items.Add(item);
            }
            if(menu.Items.Count==0)menu.Items.Add(new MenuItem{Header=UiText.T("No extended display available"),IsEnabled=false});
            anchor.ContextMenu=menu;menu.IsOpen=true;
        }catch(Exception e){if(forVideo)Get<TextBlock>("VideoStatus").Text=e.Message;else Note(e.Message);}
    }
    void UpdateBoundaryButton(Output pick,List<Output> outputs){
        var button=Get<System.Windows.Controls.Primitives.ToggleButton>("CursorBoundary");
        boundaryId=KeyboardScreen.Eligible(pick,outputs)?pick.Id:null;
        button.Visibility=boundaryId==null?Visibility.Collapsed:Visibility.Visible;
        if(boundaryId==null)return;
        bool block=KeyboardScreen.Blocked(boundaryId);
        button.IsChecked=block;
        string help=UiText.T(block?"Pointer entry blocked. Click to allow.":"Pointer entry allowed. Click to block.");
        button.ToolTip=help;
        System.Windows.Automation.AutomationProperties.SetHelpText(button,help);
    }
    void Refresh(){
        try{
            var saved=Project.ReadChoice();var screens=Choices(saved);
            if(guide!=null&&!screens.Any(d=>d.Usable&&Same(d.Id,guide.target.Id)&&d.Bounds==guide.target.Bounds))CloseEditor();
            var videoTarget=screens.FirstOrDefault(x=>Same(x.Id,videoTargetId??saved.Id));
            Get<Button>("VideoTarget").Content=(videoTarget==null?UiText.T("Select display"):videoTarget.ToString())+"  ▾";
            if(video!=null)video.CheckOutput();
            var castingTarget=VideoLease.Target(screens,saved.Id);
            bool casting=castingTarget!=null;
            bool preferred=Project.WallpaperIntegration;
            var support=Get<CheckBox>("WallpaperSupport");
            updatingSupport=true;
            try{support.IsEnabled=!casting;support.IsChecked=preferred&&!casting;}finally{updatingSupport=false;}
            Get<TextBlock>("WallpaperSupportState").Text=casting?
                string.Format(preferred?UiText.T("Wallpaper sampling will resume when casting stops on {0}."):UiText.T("Wallpaper sampling will stay off after casting on {0}."),castingTarget.DisplayName):
                preferred?UiText.T("Effects sample the live wallpaper. Casting temporarily switches the source to video."):UiText.T("Wallpaper sampling is off.");
            var live=screens.FirstOrDefault(x=>Same(x.Id,saved.Id));
            var pick=screens.FirstOrDefault(x=>Same(x.Id,pendingId??saved.Id));
            UpdateBoundaryButton(pick,screens);
            if(glass.geometry.Sync(castingTarget!=null?castingTarget.Id:pick==null?null:pick.Id)){effect.Clear();Get<ImageView>("LivePreview").Source=null;SetBoardBackdrop();}
            bool current=pick!=null&&Same(pick.Id,saved.Id);
            targetButton.Content=(pick==null?UiText.T("Select display"):pick.ToString())+"  ▾";
            targetButton.ToolTip=UiText.T("Choose a display for keyboard effects");
            string state=UiText.T("Effects paused");
            if(saved.Enabled&&live!=null)state=live.DisplayName+(live.Mirrored?UiText.T(" · Mirrored · paused"):live.Bounds.IsEmpty?UiText.T(" · Waiting for display"):UiText.T(" · Effects on"));
            if(casting&&saved.Enabled)state=castingTarget.DisplayName+UiText.T(" · Video effects on");
            Get<TextBlock>("DisplayState").Text=state;
            applyButton.Content=current&&saved.Enabled?UiText.T("Pause effects"):saved.Enabled?UiText.T("Use this display"):UiText.T("Enable effects");
            applyButton.IsEnabled=pick!=null&&((current&&saved.Enabled)||pick.Usable||(current&&!pick.Mirrored));
        }catch(Exception e){UpdateBoundaryButton(null,null);applyButton.IsEnabled=false;Note(e.Message);}
    }
    void Apply(){
        try{
            var saved=Project.ReadChoice();var pick=Choices(saved).FirstOrDefault(x=>Same(x.Id,pendingId??saved.Id));
            if(pick==null)throw new IOException(UiText.T("Select a connected display again."));
            bool enable=!(Same(pick.Id,saved.Id)&&saved.Enabled);
            if(enable&&!pick.Usable&&(!Same(pick.Id,saved.Id)||pick.Mirrored))throw new IOException(UiText.T("This display cannot show effects independently."));
            if(enable){
                if((effect==hole&&holeDirty)||(effect==glass&&dirty)||(effect==ink&&inkDirty))SaveSettings();
                Program.EnsureHost();
            }
            Project.SaveChoice(new OutputChoice{Id=pick.Id,Label=pick.Label,Enabled=enable,Effect=selectedId});Host.NotifySettings();pendingId=null;
            Note(!enable?UiText.T("Effects paused."):pick.Usable?UiText.T("Display saved."):UiText.T("Effects will start when the display connects."));
        }catch(Exception e){Note(e.Message);}finally{Refresh();}
    }
    public void Snapshot(string output){
        window.ShowInTaskbar=false;window.ShowActivated=false;window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-12000;window.Top=-12000;
        window.Show();window.UpdateLayout();TestHit(glass.data.keys.First(k=>k.code=="KeyK"));System.Threading.Thread.Sleep(160);PreviewFrame();window.UpdateLayout();
        var b=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);b.Render(window);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(b));using(var f=File.Create(output))png.Save(f);
        window.Close();
    }
}
