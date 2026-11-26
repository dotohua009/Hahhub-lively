using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

internal sealed partial class Studio {
    readonly string[] weatherSliders={"WeatherDensity","WeatherSpread","WeatherClear"};
    void InitWeather(){
        foreach(var name in weatherSliders){
            var slider=Get<Slider>(name);
            slider.ValueChanged+=delegate{
                var scene=effect as WeatherScene;if(selecting||scene==null)return;
                scene.tuning=ReadWeatherControls();
                WeatherLabels(scene.tuning);
                if(!slider.IsMouseCaptureWithin)SaveWeather();
            };
            slider.AddHandler(Mouse.LostMouseCaptureEvent,new MouseEventHandler(delegate{if(!selecting)SaveWeather();}),true);
        }
    }
    void ShowWeatherControls(){
        var scene=effect as WeatherScene;
        Get<StackPanel>("WeatherPanel").Visibility=scene==null?Visibility.Collapsed:Visibility.Visible;
        if(scene==null)return;
        var s=scene.tuning;
        Get<Slider>("WeatherDensity").Value=s.density;Get<Slider>("WeatherSpread").Value=s.spread;
        Get<Slider>("WeatherClear").Value=s.clearMs;WeatherLabels(s);
    }
    void WeatherLabels(WeatherTuning s){
        Get<TextBlock>("WeatherDensityValue").Text=s.density+"%";Get<TextBlock>("WeatherSpreadValue").Text=s.spread+"%";
        Get<TextBlock>("WeatherClearValue").Text=(s.clearMs/1000.0).ToString("0.00")+" s";
    }
    WeatherTuning ReadWeatherControls(){
        return new WeatherTuning{density=(int)Get<Slider>("WeatherDensity").Value,spread=(int)Get<Slider>("WeatherSpread").Value,clearMs=(int)Get<Slider>("WeatherClear").Value};
    }
    void SaveWeather(){
        if(!(effect is WeatherScene))return;
        try{
            var next=SkillOptions.Read();var s=ReadWeatherControls();
            var saved=selectedId==WeatherScene.FogId?next.fog:next.snow;
            if(s.density==saved.density&&s.spread==saved.spread&&s.clearMs==saved.clearMs)return;
            if(selectedId==WeatherScene.FogId)next.fog=s;else next.snow=s;
            next.Save();options=next;library.Configure(next);Host.NotifySettings();Note(UiText.T("Settings saved."));
        }catch(Exception error){library.Configure(options);ShowEffect(selectedId);Note(error.Message);}
    }
}
