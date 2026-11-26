using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

internal sealed class KeyboardAudition : Control {
    readonly Glass glass=new Glass();
    [System.Runtime.InteropServices.DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint period);
    [System.Runtime.InteropServices.DllImport("winmm.dll")] static extern uint timeEndPeriod(uint period);
    bool precise;double nextFrame;
    readonly Timer pulse=new Timer{Interval=16};
    readonly HashSet<int> held=new HashSet<int>();
    readonly Stopwatch clock=Stopwatch.StartNew();
    readonly Bitmap board,overlay,back;readonly Rectangle world;
    readonly Graphics layer,bg;readonly IKeyEffect[] scenes;
    bool testing;int selected;LayoutKey pointer;Rectangle prev;
    public KeyboardAudition(){
        SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Selectable,true);
        TabStop=true;BackColor=Color.FromArgb(17,23,30);
        glass.geometry.Sync(Project.ReadChoice().Id);
        world=KeyboardStage.World(glass.data);board=KeyboardStage.Create(glass.data,world);
        overlay=new Bitmap(1920,720,PixelFormat.Format32bppPArgb);layer=Graphics.FromImage(overlay);
        back=new Bitmap(1920,720,PixelFormat.Format32bppPArgb);bg=Graphics.FromImage(back);
        scenes=new IKeyEffect[]{new VoxelFuse(glass.data),new Prism(glass.data),new FrostLance(glass.data),new Tendrils(glass.data){rainbow=true},new SilkColony(glass.data)};
        pulse.Tick+=delegate{
            double now=clock.Elapsed.TotalMilliseconds;
            nextFrame=Math.Max(nextFrame+1000.0/60,now+1);
            pulse.Interval=Math.Max(10,(int)Math.Round(nextFrame-now));Invalidate();
        };
    }
    public void Effect(int n){scenes[selected].Clear();selected=n;held.Clear();pointer=null;Invalidate();Focus();}
    public void Delay(decimal seconds){((VoxelFuse)scenes[0]).fuseMs=(int)(1000*seconds);}
    float Zoom{get{return Math.Max(.01f,Math.Min((Width-12f)/world.Width,(Height-12f)/world.Height));}}
    RectangleF Stage{get{float z=Zoom;return new RectangleF((Width-world.Width*z)/2,(Height-world.Height*z)/2,world.Width*z,world.Height*z);}}
    void StopFrames(){pulse.Stop();if(precise){timeEndPeriod(1);precise=false;}}
    void Press(LayoutKey k){
        if(k==null)return;
        scenes[selected].Strike(k);
        if(!pulse.Enabled){precise=timeBeginPeriod(1)==0;nextFrame=clock.Elapsed.TotalMilliseconds+1000.0/60;pulse.Interval=17;pulse.Start();}
        Invalidate();
    }
    protected override bool IsInputKey(Keys key){return true;}
    protected override void WndProc(ref Message m){
        if((Focused||testing)&&(m.Msg==0x100||m.Msg==0x101||m.Msg==0x104||m.Msg==0x105)){
            int vk=m.WParam.ToInt32();long bits=m.LParam.ToInt64();
            bool sys=m.Msg==0x104||m.Msg==0x105;
            if(!(sys&&vk==(int)Keys.F4)){
                int scan=(int)((bits>>16)&511);LayoutKey k;
                if(glass.scans.TryGetValue(scan,out k)){
                    if(m.Msg==0x100||m.Msg==0x104){if(held.Add(scan))Press(k);}
                    else if(held.Remove(scan))scenes[selected].Release(k);
                    return;
                }
            }
        }
        base.WndProc(ref m);
    }
    protected override void OnLostFocus(EventArgs e){
        foreach(int scan in held)scenes[selected].Release(glass.scans[scan]);held.Clear();EndPointer();base.OnLostFocus(e);
    }
    protected override void OnMouseDown(MouseEventArgs e){
        base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;Focus();EndPointer();
        var frame=Stage;var x=(e.X-frame.X)/Zoom+world.X;var y=(e.Y-frame.Y)/Zoom+world.Y;
        pointer=glass.data.keys.FirstOrDefault(k=>k.cap.Contains(x,y));if(pointer!=null){Capture=true;Press(pointer);}
    }
    void EndPointer(){if(pointer!=null){scenes[selected].Release(pointer);pointer=null;}}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);Capture=false;EndPointer();}
    protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture)EndPointer();}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);var g=e.Graphics;var scene=scenes[selected];var area=scene.Bounds(1920,720);
        var clean=prev.IsEmpty?area:area.IsEmpty?prev:Rectangle.Union(prev,area);prev=area;
        if(!clean.IsEmpty){layer.CompositingMode=CompositingMode.SourceCopy;layer.FillRectangle(Brushes.Transparent,clean);layer.CompositingMode=CompositingMode.SourceOver;}
        var rect=Stage;g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;
        g.DrawImage(board,rect);
        g.TranslateTransform(rect.X-world.X*Zoom,rect.Y-world.Y*Zoom);g.ScaleTransform(Zoom,Zoom);
        int frame=(int)((clock.Elapsed.TotalSeconds*60)%100000);
        var sample=scene.SampleArea(1920,720);
        if(sample.IsEmpty)scene.Draw(layer,1920,720,0,0,null,Rectangle.Empty);
        else {
            bg.DrawImageUnscaled(board,world.X,world.Y);FullKeyboardDemos.Light(bg,frame);
            using(var crop=back.Clone(sample,PixelFormat.Format32bppArgb))scene.Draw(layer,1920,720,0,0,crop,sample);
        }
        FullKeyboardDemos.Light(g,frame);
        if(!area.IsEmpty)g.DrawImage(overlay,area,area,GraphicsUnit.Pixel);
        if(!scene.Active)StopFrames();
    }
    protected override void Dispose(bool disposing){
        if(disposing){StopFrames();pulse.Dispose();foreach(var scene in scenes){var d=scene as IDisposable;if(d!=null)d.Dispose();}
            bg.Dispose();back.Dispose();layer.Dispose();overlay.Dispose();board.Dispose();glass.Dispose();}
        base.Dispose(disposing);
    }
    [STAThread]static int Main(string[] args){
        Application.EnableVisualStyles();
        bool zh=UiText.Chinese;
        using(var win=new Form{Text="hahhub  ·  "+(zh?"全键盘效果预览":"Full keyboard preview"),ClientSize=new Size(1380,635),
            MinimumSize=new Size(720,380),StartPosition=FormStartPosition.CenterScreen,BackColor=Color.FromArgb(25,33,43),Font=new Font("Segoe UI",10)})
        using(var pane=new KeyboardAudition{Dock=DockStyle.Fill}){
            var bar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=51,Padding=new Padding(10,8,0,0),WrapContents=false};
            var select=new ComboBox{Width=180,DropDownStyle=ComboBoxStyle.DropDownList};
            select.Items.AddRange(zh?new[]{"方块连爆","钻石切面","冰刺","触须","蛛群"}:new[]{"Voxel Fuse","Prism","Frost Lance","Tendrils","Silk Colony"});
            var label=new Label{Text=zh?"引信（秒）":"Fuse (seconds)",AutoSize=true,Margin=new Padding(14,6,3,0),ForeColor=Color.Gainsboro};
            var fuse=new NumericUpDown{Minimum=.15m,Maximum=5,DecimalPlaces=2,Increment=.15m,Value=1.2m,Width=75};
            select.SelectedIndexChanged+=delegate{bool show=select.SelectedIndex==0;label.Visible=fuse.Visible=show;pane.Effect(select.SelectedIndex);};
            fuse.ValueChanged+=delegate{pane.Delay(fuse.Value);};
            var hint=new Label{Text=zh?"点击键帽或按下实体键盘":"Click a keycap or press a physical key",AutoSize=true,Margin=new Padding(18,6,0,0),ForeColor=Color.LightSteelBlue};
            bar.Controls.AddRange(new Control[]{select,label,fuse,hint});win.Controls.Add(pane);win.Controls.Add(bar);select.SelectedIndex=0;
            win.Shown+=delegate{pane.Focus();};
            if(args.Length==2&&args[0]=="--check"){
                win.ShowInTaskbar=false;win.Opacity=0;win.StartPosition=FormStartPosition.Manual;win.Location=new Point(-30000,-30000);
                win.Show();Application.DoEvents();pane.testing=true;
                using(var shot=new Bitmap(win.ClientSize.Width,win.ClientSize.Height)){
                    long bits=35L<<16;
                    var msg=Message.Create(pane.Handle,0x100,(IntPtr)Keys.H,(IntPtr)bits);pane.WndProc(ref msg);
                    if(!pane.scenes[0].Active||!pane.held.Contains(35))throw new InvalidOperationException("H keydown was not delivered");
                    msg=Message.Create(pane.Handle,0x101,(IntPtr)Keys.H,(IntPtr)bits);pane.WndProc(ref msg);
                    if(pane.held.Contains(35))throw new InvalidOperationException("H keyup was not delivered");
                    win.DrawToBitmap(shot,win.ClientRectangle);shot.Save(args[1],ImageFormat.Png);
                }
                return 0;
            }
            Application.Run(win);return 0;
        }
    }
}
