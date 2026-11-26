using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

internal static class KeyboardStage {
    public static Rectangle World(GlassData layout){
        var all=new RectangleF(0,0,1920,720);
        foreach(var k in layout.keys)all=RectangleF.Union(all,k.cap);
        all.Inflate(23,22);
        return Rectangle.FromLTRB((int)Math.Floor(all.Left),(int)Math.Floor(all.Top),(int)Math.Ceiling(all.Right),(int)Math.Ceiling(all.Bottom));
    }
    static GraphicsPath Round(RectangleF r,float radius){
        var path=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
        path.AddArc(r.Left,r.Top,d,d,180,90);path.AddArc(r.Right-d,r.Top,d,d,270,90);
        path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.Left,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
    }
    static string Legend(string code){
        if(code.StartsWith("Key")||code.StartsWith("Digit"))return code.Substring(code.StartsWith("Key")?3:5);
        switch(code){
            case "ArrowUp":return "↑";case "ArrowDown":return "↓";case "ArrowLeft":return "←";case "ArrowRight":return "→";
            case "Escape":return "Esc";case "PageUp":return "PgUp";case "PageDown":return "PgDn";case "MetaLeft":return "Win";
            case "PrintScreen":return "PrtSc";case "ScrollLock":return "ScrLk";case "CapsLock":return "Caps";
            case "BracketLeft":return "[";case "BracketRight":return "]";case "Backslash":return "\\";
            case "Quote":return "'";case "Semicolon":return ";";case "Comma":return ",";case "Period":return ".";
            case "Minus":return "–";case "Equal":return "=";case "Slash":return "/";case "Backquote":return "`";case "Space":return "Space";
        }
        return code.Replace("Right","").Replace("Left","").Replace("Control","Ctrl");
    }
    public static Bitmap Create(GlassData layout,Rectangle world){
        var image=new Bitmap(world.Width,world.Height,PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(image))
        using(var label=new Font("Segoe UI",13,FontStyle.Regular,GraphicsUnit.Pixel))
        using(var legend=new SolidBrush(Color.FromArgb(199,210,220,229)))
        using(var border=new Pen(Color.FromArgb(133,111,133,151),1.3f))
        using(var baseEdge=new SolidBrush(Color.FromArgb(11,16,24)))
        using(var shell=new Pen(Color.FromArgb(26,181,205,222),1))
        using(var wall=new LinearGradientBrush(new Rectangle(0,0,image.Width,image.Height),Color.FromArgb(26,36,47),Color.FromArgb(54,63,73),24)){
            g.FillRectangle(wall,0,0,image.Width,image.Height);g.TranslateTransform(-world.X,-world.Y);
            g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            foreach(var k in layout.keys){
                var cap=k.cap;cap.Inflate(-.8f,-.8f);if(cap.Width<6||cap.Height<6)continue;
                using(var path=Round(cap,9))g.FillPath(baseEdge,path);
                cap.Offset(0,-2.5f);cap.Inflate(-1.25f,-1.25f);
                float f=Math.Max(0,Math.Min(1,(k.x*.64f+k.y*.29f)));
                using(var face=new LinearGradientBrush(cap,Color.FromArgb(54+(int)(20*f),66+(int)(19*f),78+(int)(17*f)),Color.FromArgb(33+(int)(16*f),42+(int)(15*f),53+(int)(13*f)),78))
                using(var path=Round(cap,8)){g.FillPath(face,path);g.DrawPath(border,path);}
                using(var path=Round(k.housing,6))g.DrawPath(shell,path);
                using(var path=Round(k.stem,4))g.DrawPath(shell,path);
                g.DrawString(Legend(k.code),label,legend,cap.Left+7,cap.Top+5);
            }
        }
        return image;
    }
}
