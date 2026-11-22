using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class CalibrationGuide : Form {
    readonly Glass glass;public readonly Alignment draft;public readonly Output target;
    public int index,layer=1;
    public Action<int> SelectKey;
    public Func<BoxTune> EditBox;public Action Changed;
    PointF down;RectangleF startBox;int startX,startY,startW,startH,cornerX,cornerY;
    PointF GuidePoint(MouseEventArgs e){return new PointF(e.X*1920f/ClientSize.Width,e.Y*720f/ClientSize.Height);}
    static int Edge(float p,float lo,float hi){return Math.Abs(p-lo)<=10?-1:Math.Abs(p-hi)<=10?1:0;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string cls,string caption);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);
    static string TitleFor(string id){return "hahhub alignment "+id;}
    public static bool ActiveOn(string id){return IsWindowVisible(FindWindow(null,TitleFor(id)));}
    public CalibrationGuide(Glass glass,Output target,Alignment draft){
        this.glass=glass;this.target=target;this.draft=draft;
        Text=TitleFor(target.Id);FormBorderStyle=FormBorderStyle.None;AutoScaleMode=AutoScaleMode.None;
        ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Bounds=target.Bounds;DoubleBuffered=true;BackColor=Color.FromArgb(17,25,34);
        index=Array.FindIndex(glass.data.keys,k=>k.code=="KeyA");
    }
    protected override bool ShowWithoutActivation{get{return true;}}
    protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000080;return p;}}
    public void Place(bool topmost){
        var b=target.Bounds;Show();if(!SetWindowPos(Handle,new IntPtr(topmost?-1:-2),b.X,b.Y,b.Width,b.Height,0x10))throw new InvalidOperationException("Could not position alignment guide");
    }
    protected override void OnMouseDown(MouseEventArgs e){
        base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;
        down=GuidePoint(e);float x=down.X,y=down.Y;
        double best=double.MaxValue;int hit=index;
        for(int i=0;i<glass.data.keys.Length;i++){var r=glass.geometry.Box(draft,i,layer==0?1:layer);double dx=x-r.X-r.Width*.5,dy=y-r.Y-r.Height*.5,d=dx*dx+dy*dy;if(d<best){best=d;hit=i;}}
        var current=glass.geometry.Box(draft,index,layer==0?1:layer);
        int cx=Edge(x,current.Left,current.Right),cy=Edge(y,current.Top,current.Bottom);
        if(layer!=0&&cx!=0&&cy!=0)hit=index;
        if(SelectKey!=null)SelectKey(hit);
        startBox=glass.geometry.Box(draft,index,layer==0?1:layer);var b=EditBox();
        startX=b.x;startY=b.y;startW=b.width;startH=b.height;
        cornerX=Edge(x,startBox.Left,startBox.Right);cornerY=Edge(y,startBox.Top,startBox.Bottom);
        if(layer==0||cornerX==0||cornerY==0)cornerX=cornerY=0;
        Capture=true;
    }
    static int Limit(double v,int min,int max){return Math.Max(min,Math.Min(max,(int)Math.Round(v)));}
    protected override void OnMouseMove(MouseEventArgs e){
        base.OnMouseMove(e);if(!Capture||EditBox==null||e.Button!=MouseButtons.Left)return;
        var p=GuidePoint(e);float dx=p.X-down.X,dy=p.Y-down.Y;var b=EditBox();
        if(cornerX!=0){b.width=Limit(startW*(1+2*cornerX*dx/startBox.Width),25,200);b.height=Limit(startH*(1+2*cornerY*dy/startBox.Height),25,200);}
        else{b.x=Limit(startX+dx,-400,400);b.y=Limit(startY+dy,-240,240);}
        Changed();
    }
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);Capture=false;
    }
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.ScaleTransform(ClientSize.Width/1920f,ClientSize.Height/720f);
        using(var cap=new Pen(Color.FromArgb(151,202,237),2))
        using(var housing=new Pen(Color.FromArgb(213,226,235),1.8f){DashStyle=DashStyle.Dash})
        using(var stem=new Pen(Color.FromArgb(255,197,111),1.8f))
        using(var focus=new SolidBrush(Color.FromArgb(45,157,209,245))){
            var pens=new[]{cap,housing,stem};
            for(int i=0;i<glass.data.keys.Length;i++){
                for(int l=1;l<=3;l++){
                    var r=glass.geometry.Box(draft,i,l);if(i==index&&(l==layer||(layer==0&&l==1)))g.FillRectangle(focus,r);
                    var pen=pens[l-1];pen.Width=i==index?3.5f:l==layer?2.2f:1.1f;
                    g.DrawRectangle(pen,r.X,r.Y,r.Width,r.Height);
                    if(i==index&&l==layer)foreach(float x in new[]{r.Left,r.Right})foreach(float y in new[]{r.Top,r.Bottom})g.FillRectangle(pen.Brush,x-3,y-3,6,6);
                    if(i==index&&l==3){float x=r.X+r.Width*.5f,y=r.Y+r.Height*.5f;g.DrawLine(pen,x-5,y,x+5,y);g.DrawLine(pen,x,y-5,x,y+5);}
                }
            }
        }
    }
}
