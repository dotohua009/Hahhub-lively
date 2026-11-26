using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation;

internal sealed partial class Studio {
    double hue,saturation,brightness;bool painting;int chosenColor;
    void OpenEditor(string panel){
        Get<Grid>("ShellContent").IsEnabled=false;
        Get<StackPanel>(panel).Visibility=Visibility.Visible;Get<Grid>("EditorShade").Visibility=Visibility.Visible;
    }
    void CloseEditor(){
        bool color=Get<StackPanel>("PalettePanel").IsVisible;
        if(guide!=null){guide.Dispose();guide=null;Host.NotifySettings();if(cursorBoundary!=null)cursorBoundary.Refresh();}
        Get<Grid>("EditorShade").Visibility=Visibility.Collapsed;
        foreach(var p in new[]{"PalettePanel","AlignmentPanel"})Get<StackPanel>(p).Visibility=Visibility.Collapsed;
        Get<Grid>("ShellContent").IsEnabled=true;Get<Button>(color?"ChooseRim":"Calibrate").Focus();
    }
    static int Rgb(double h,double s,double v){
        double c=v*s,sector=h/60%6,m=v-c,x=c*(1-Math.Abs(sector%2-1));
        double r=0,g=0,b=0;
        switch((int)sector){case 0:r=c;g=x;break;case 1:r=x;g=c;break;case 2:g=c;b=x;break;case 3:g=x;b=c;break;case 4:r=x;b=c;break;default:r=c;b=x;break;}
        return ((int)Math.Round((r+m)*255)<<16)|((int)Math.Round((g+m)*255)<<8)|(int)Math.Round((b+m)*255);
    }
    static SolidColorBrush RgbBrush(int rgb){return new SolidColorBrush(Color.FromRgb((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb));}
    void FromRgb(int rgb){
        double r=(rgb>>16)/255.0,g=((rgb>>8)&255)/255.0,b=(rgb&255)/255.0;
        brightness=Math.Max(r,Math.Max(g,b));double d=brightness-Math.Min(r,Math.Min(g,b));
        saturation=brightness==0?0:d/brightness;
        if(d>0)hue=(360+60*(brightness==r?(g-b)/d:brightness==g?2+(b-r)/d:4+(r-g)/d))%360;
    }
    static double Unit(double value){return Math.Min(1,Math.Max(0,value));}
    void PaintColor(bool rewrite){
        painting=true;
        try{
            chosenColor=Rgb(hue,saturation,brightness);var field=Get<Grid>("ColorField");var cursor=Get<Grid>("ColorCursor");
            Get<Border>("ColorBase").Background=RgbBrush(Rgb(hue,1,1));
            Canvas.SetLeft(cursor,Math.Max(0,(field.ActualWidth-16)*saturation));Canvas.SetTop(cursor,Math.Max(0,(field.ActualHeight-16)*(1-brightness)));
            Get<Slider>("Hue").Value=hue;Get<Border>("ColorSample").Background=RgbBrush(chosenColor);
            if(rewrite)Get<TextBox>("ColorHex").Text="#"+chosenColor.ToString("X6");
            ColorIsValid(true);
        }finally{painting=false;}
    }
    void ColorIsValid(bool valid){Get<TextBlock>("ColorError").Visibility=valid?Visibility.Collapsed:Visibility.Visible;Get<Button>("ApplyColor").IsEnabled=valid;}
    void ChooseRim(){
        FromRgb(SelectedColor);
        Get<TextBlock>("PaletteEffect").Text=UiText.T(EffectName);
        OpenEditor("PalettePanel");window.UpdateLayout();PaintColor(true);Get<Grid>("ColorField").Focus();
    }
    void InitPalette(){
        var field=Get<Grid>("ColorField");var hex=Get<TextBox>("ColorHex");
        Action<MouseEventArgs> sample=e=>{var p=e.GetPosition(field);saturation=Unit(p.X/Math.Max(1,field.ActualWidth));brightness=1-Unit(p.Y/Math.Max(1,field.ActualHeight));PaintColor(true);};
        field.MouseLeftButtonDown+=(o,e)=>{field.Focus();field.CaptureMouse();sample(e);};
        field.MouseMove+=(o,e)=>{if(field.IsMouseCaptured)sample(e);};
        field.MouseLeftButtonUp+=(o,e)=>field.ReleaseMouseCapture();
        field.KeyDown+=(o,e)=>{double step=(Keyboard.Modifiers&ModifierKeys.Shift)!=0?.1:.01;
            switch(e.Key){case Key.Left:saturation=Unit(saturation-step);break;case Key.Right:saturation=Unit(saturation+step);break;case Key.Up:brightness=Unit(brightness+step);break;case Key.Down:brightness=Unit(brightness-step);break;default:return;}
            PaintColor(true);e.Handled=true;};
        Get<Slider>("Hue").ValueChanged+=(o,e)=>{if(!painting){hue=e.NewValue;PaintColor(true);}};
        hex.TextChanged+=delegate{if(painting)return;var t=hex.Text.Trim();if(t.StartsWith("#"))t=t.Substring(1);
            int rgb;if(t.Length!=6||!int.TryParse(t,NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out rgb)){ColorIsValid(false);return;}
            FromRgb(rgb);PaintColor(false);};
        foreach(int rgb in new[]{0xDAE8F7,0x86BAFF,0x68E1EC,0xB09BFF,0xED94C5,0xFA9B81,0xF2CD83,0x91CFB5}){
            var button=new Button{Margin=new Thickness(2),Height=22,Padding=new Thickness(0),Background=RgbBrush(rgb),ToolTip="#"+rgb.ToString("X6")};
            AutomationProperties.SetName(button,"#"+rgb.ToString("X6"));button.Click+=delegate{FromRgb(rgb);PaintColor(true);};
            Get<System.Windows.Controls.Primitives.UniformGrid>("ColorPresets").Children.Add(button);
        }
        Get<Button>("CancelColor").Click+=delegate{CloseEditor();};
        Get<Button>("ApplyColor").Click+=delegate{
            try{
                if(BodyColor){
                    var next=SkillOptions.Read();
                    if(selectedId==Tendrils.Id){next.tendrilColor=chosenColor;next.rainbow=false;}else next.spiderColor=chosenColor;
                    next.Save();options=next;library.Configure(next);Get<CheckBox>("Spectrum").IsChecked=next.rainbow;
                }else{
                    Project.SaveRim(effect==hole,chosenColor);
                    if(effect==hole)hole.rimColor=chosenColor;else glass.rimColor=chosenColor;
                }
            }catch(Exception err){Get<TextBlock>("ColorError").Text=err.Message;Get<TextBlock>("ColorError").Visibility=Visibility.Visible;return;}

            CloseEditor();PaintSwatch();Host.NotifySettings();var key=glass.scans[33];TestHit(key);ReleasePreview(key);Note(UiText.T(BodyColor?"Color saved.":"Edge glow saved."));
        };
        hex.KeyDown+=(o,e)=>{if(e.Key==Key.Enter&&Get<Button>("ApplyColor").IsEnabled){Get<Button>("ApplyColor").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));e.Handled=true;}};
    }
}
