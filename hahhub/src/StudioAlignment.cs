using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

internal sealed partial class Studio {
    CalibrationGuide guide;bool filling;
    readonly string[] layerNames={"AlignLayout","AlignKeycap","AlignSwitchhousing","AlignStem"};
    void InitAlignment(){
        var keys=Get<ComboBox>("GuideKey");
        keys.ItemsSource=glass.data.keys.Select(k=>k.code).ToArray();
        keys.SelectionChanged+=delegate{if(guide!=null&&keys.SelectedIndex>=0){guide.index=keys.SelectedIndex;FillAlignment();}};
        for(int i=0;i<layerNames.Length;i++){
            int layer=i;Get<RadioButton>(layerNames[i]).Checked+=delegate{if(guide!=null){guide.layer=layer;FillAlignment();}};
        }
        var only=Get<CheckBox>("OnlyKey");only.Checked+=delegate{FillAlignment();};only.Unchecked+=delegate{FillAlignment();};
        foreach(var n in new[]{"AlignX","AlignY","AlignW","AlignH"})Get<Slider>(n).ValueChanged+=delegate{
            if(guide==null||filling)return;var box=EditingBox(true);
            box.x=(int)Get<Slider>("AlignX").Value;box.y=(int)Get<Slider>("AlignY").Value;
            box.width=(int)Get<Slider>("AlignW").Value;box.height=(int)Get<Slider>("AlignH").Value;
            GuideValues(box);guide.Invalidate();
        };
        Get<Button>("Calibrate").Click+=delegate{OpenAlignment();};
        Get<Button>("DiscardAlignment").Click+=delegate{CloseEditor();};
        Get<Button>("SaveAlignment").Click+=delegate{
            if(guide==null)return;
            try{
                var target=Displays.Connected().FirstOrDefault(d=>d.Usable&&Same(d.Id,guide.target.Id)&&d.Bounds==guide.target.Bounds);
                if(target==null){CloseEditor();MessageBox.Show(window,UiText.T("Select a connected display again."),"hahhub");return;}
                guide.draft.Save(target.Id);CloseEditor();Refresh();Note(UiText.T("Alignment saved."));
            }catch(Exception err){MessageBox.Show(window,err.Message,"hahhub");}
        };
        window.IsVisibleChanged+=delegate{if(!window.IsVisible&&Get<Grid>("EditorShade").Visibility==Visibility.Visible)CloseEditor();};
        window.Closed+=delegate{if(guide!=null){guide.Dispose();guide=null;Host.NotifySettings();}};
    }
    void OpenAlignment(){
        try{
            var screens=Displays.Connected();var saved=Project.ReadChoice();var target=screens.FirstOrDefault(d=>d.Usable&&Same(d.Id,pendingId??saved.Id));
            if(target==null){MessageBox.Show(window,UiText.T("Select a connected display again."),"hahhub");return;}
            guide=new CalibrationGuide(glass,target,Alignment.Read(target.Id));guide.EditBox=()=>EditingBox(true);guide.Changed=FillAlignment;guide.SelectKey=i=>Get<ComboBox>("GuideKey").SelectedIndex=i;
            Get<TextBlock>("GuideDisplay").Text=target.DisplayName+"   ·   "+target.Bounds.Width+" × "+target.Bounds.Height;
            OpenEditor("AlignmentPanel");Get<CheckBox>("OnlyKey").IsChecked=false;Get<RadioButton>("AlignKeycap").IsChecked=true;
            Get<ComboBox>("GuideKey").SelectedIndex=guide.index;FillAlignment();Get<RadioButton>("AlignKeycap").Focus();
            bool sameMonitor=System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle).Bounds.IntersectsWith(target.Bounds);
            guide.Place(!sameMonitor);
            if(cursorBoundary!=null)cursorBoundary.Refresh();
            window.Activate();Host.NotifySettings();
        }catch(Exception e){if(guide!=null)CloseEditor();MessageBox.Show(window,e.Message,"hahhub");}
    }
    BoxTune EditingBox(bool write){
        int l=guide.layer;
        if(l!=0&&Get<CheckBox>("OnlyKey").IsChecked==true)return guide.draft.Local(glass.data.keys[guide.index].code,l,write)??new BoxTune();
        return guide.draft.Layer(l);
    }
    void FillAlignment(){
        if(guide==null||filling)return;filling=true;
        try{
            var box=EditingBox(false);Get<CheckBox>("OnlyKey").IsEnabled=guide.layer!=0;
            Get<Slider>("AlignX").Value=box.x;Get<Slider>("AlignY").Value=box.y;
            Get<Slider>("AlignW").Value=box.width;Get<Slider>("AlignH").Value=box.height;GuideValues(box);guide.Invalidate();
        }finally{filling=false;}
    }
    void GuideValues(BoxTune b){
        Get<TextBlock>("AlignXNum").Text=b.x+" px";Get<TextBlock>("AlignYNum").Text=b.y+" px";
        Get<TextBlock>("AlignWNum").Text=b.width+"%";Get<TextBlock>("AlignHNum").Text=b.height+"%";
    }
    void EditorKey(KeyEventArgs e){
        if(Get<Grid>("EditorShade").Visibility!=Visibility.Visible)return;
        if(e.Key==Key.Escape){CloseEditor();e.Handled=true;return;}
        if(guide==null||e.IsRepeat||Keyboard.Modifiers!=ModifierKeys.None)return;
        if(e.Key>=Key.A&&e.Key<=Key.Z||e.Key>=Key.D0&&e.Key<=Key.D9){
            var k=PreviewKey(e);if(k!=null){Get<ComboBox>("GuideKey").SelectedIndex=Array.IndexOf(glass.data.keys,k);e.Handled=true;}
        }
    }
}
