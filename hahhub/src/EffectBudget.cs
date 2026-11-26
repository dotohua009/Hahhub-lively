using System;

// Frame work and emission limits are local to each renderer (preview or desktop).
internal sealed class EffectBudget {
    int fps=60, ceiling=3, level=3, over, headroom;
    double tokens=4, lastStart=double.NaN;
    public int Keys {get{return Math.Max(2,level+1);}}
    public int OpticalStep {get{return level==0?3:level==1?2:1;}}
    public int Spiders {get{return 96+level*108;}}
    public int Count(int maximum){return Math.Max(1,maximum*(level+2)/5);}
    public double WorkMs {get{return Math.Min(12,1000.0/fps*.65);}}
    public double RestAfter(double workMs){
        return level==0?Math.Min(250,Math.Max(1,workMs*(1000.0/fps/WorkMs-1))):1;
    }
    public void SetRate(int value){
        value=Math.Max(1,Math.Min(60,value));if(value==fps)return;
        fps=value;ceiling=value<=16?0:value<=24?1:value<=30?2:3;level=ceiling;over=headroom=0;
    }
    public bool Start(double now){
        if(double.IsNaN(lastStart)||now<lastStart)tokens=Keys;
        else tokens=Math.Min(Keys,tokens+Math.Max(0,now-lastStart)*(.012+.004*level));
        lastStart=now;if(tokens<1)return false;tokens--;return true;
    }
    public void Observe(double elapsed){
        if(elapsed>WorkMs){headroom=0;if(++over>=3){level=Math.Max(0,level-1);over=0;}}
        else{over=0;if(elapsed>WorkMs*.57){headroom=0;return;}
            if(++headroom>=fps*3){level=Math.Min(ceiling,level+1);headroom=0;}}
    }
}
