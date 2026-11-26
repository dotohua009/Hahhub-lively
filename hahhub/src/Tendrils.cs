using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// Orthographic, depth-tested tube impostors. The height field separates crossing arms and lifts shadows.
internal sealed class Tendrils : SkillScene,IDisposable {
    public const string Id="hahhub-tendril";
    public int color=0x9567ae;public bool rainbow;
    const float Pad=235;
    float Scale=2f/3;
    int[] pixels;float[] depth;
    int width,height,stride;float left,top;
    GCHandle pin;Bitmap tile;
    struct Joint {public float x,y,z,r;}
    static readonly int[] spectrum={0xe6579d,0xf59760,0xd5d570,0x45c3a8,0x5588d1,0xa664c4};
    static readonly float[] normal=new float[513];
    static Tendrils(){for(int i=0;i<normal.Length;i++)normal[i]=(float)Math.Sqrt(Math.Max(0,1-i/512f));}
    public Tendrils(GlassData data,Func<double> time=null):base(data,time){}
    protected override RectangleF Extent(Contact p){return RectangleF.Inflate(p.key.cap,Pad,Pad);}
    void BeginTile(RectangleF area){
        // Keep the raster aligned to world pixels as the dirty region changes.
        left=(float)Math.Floor(area.Left*Scale)/Scale;top=(float)Math.Floor(area.Top*Scale)/Scale;
        width=(int)Math.Ceiling((area.Right-left)*Scale);height=(int)Math.Ceiling((area.Bottom-top)*Scale);
        if(tile==null||tile.Width<width||tile.Height<height){
            int cols=Math.Max((width+31)/32*32,tile==null?0:tile.Width);
            int rows=Math.Max((height+31)/32*32,tile==null?0:tile.Height);
            Dispose();stride=cols;pixels=new int[cols*rows];depth=new float[pixels.Length];
            pin=GCHandle.Alloc(pixels,GCHandleType.Pinned);
            tile=new Bitmap(cols,rows,cols*4,PixelFormat.Format32bppPArgb,pin.AddrOfPinnedObject());
        }else for(int y=0;y<height;y++){
            Array.Clear(depth,y*stride,width);Array.Clear(pixels,y*stride,width);
        }
    }
    int Pigment(float hue){
        if(!rainbow)return color;
        float h=(hue-(float)Math.Floor(hue))*6;int i=(int)h%6;
        return Mix(spectrum[i],spectrum[(i+1)%6],h-(int)h);
    }
    void Tube(Joint a,Joint b,int pigment,int stripe){
        float dx=b.x-a.x,dy=b.y-a.y,len2=dx*dx+dy*dy;
        float inv=1/Math.Max(.001f,len2),rad=Math.Max(a.r,b.r)+1;
        int x0=Math.Max(0,(int)(Math.Min(a.x,b.x)-rad)),x1=Math.Min(width-1,(int)(Math.Max(a.x,b.x)+rad));
        int y0=Math.Max(0,(int)(Math.Min(a.y,b.y)-rad)),y1=Math.Min(height-1,(int)(Math.Max(a.y,b.y)+rad));
        int red=pigment>>16,green=(pigment>>8)&255,blue=pigment&255;
        for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++){
            float t=((x-a.x)*dx+(y-a.y)*dy)*inv;t=t<0?0:t>1?1:t;float r=a.r+(b.r-a.r)*t;
            float u=(x-a.x-t*dx)/r,v=(y-a.y-t*dy)/r,d=u*u+v*v;
            if(d>=1)continue;
            float nz=normal[(int)(d*512)],z=a.z+(b.z-a.z)*t+nz*r;
            int pos=y*stride+x;
            if(z<depth[pos])continue;
            float nd=Math.Max(0,-u*.48f-v*.64f+nz*.54f);
            float spec=Math.Max(0,-u*.28f-v*.40f+nz*.87f);
            spec*=spec;spec*=spec;spec*=spec;spec*=spec;
            int grain=((x*29+y*37+stripe*7)&15);
            float light=.17f+.78f*nd+.07f*(1-nz)+grain*.0027f;
            float gloss=spec*84+(1-nz)*9;
            int alpha=Math.Min(255,(int)((1-d)*r*240)+35);
            int rr=Math.Min(255,(int)(red*light+gloss+10)),gg=Math.Min(255,(int)(green*light+gloss+4)),bb=Math.Min(255,(int)(blue*light+gloss+8));
            if(alpha<255&&depth[pos]>0 && ((uint)pixels[pos]>>24)>190)alpha=255;
            pixels[pos]=(alpha<<24)|((rr*alpha/255)<<16)|((gg*alpha/255)<<8)|(bb*alpha/255);depth[pos]=z;
        }
    }
    void Sucker(Joint j,float nx,float ny,int pigment){
        float r=j.r*.28f;if(r<.65f)return;
        float cx=j.x+nx*j.r*.49f,cy=j.y+ny*j.r*.49f;
        int shade=Mix(pigment,0xf6c7b8,.65f),dark=Mix(pigment,0x301a30,.6f);
        for(int y=Math.Max(0,(int)(cy-r));y<Math.Min(height,cy+r+1);y++)for(int x=Math.Max(0,(int)(cx-r));x<Math.Min(width,cx+r+1);x++){
            float u=(x-cx)/r,v=(y-cy)/r,d=u*u+v*v;
            if(d>=1)continue;int pos=y*stride+x;float z=j.z+j.r*.91f;
            if(depth[pos]>z+.6f)continue;
            pixels[pos]=unchecked((int)0xff000000)|(d<.26f?dark:shade);depth[pos]=z;
        }
    }
    void GrowCluster(Contact p,KeycapRim rim,Contact neighbor,int side,int group,int groups,double held,float fade){
        int seed=p.seed+side*809+group*5791;
        float fraction=(group+.21f+.58f*Noise(seed,8))/groups;
        float turn=rim.SideTurn(side,fraction),hue=Noise(seed,39);
        int handedness=Noise(seed,19)<.5f?-1:1;
        int shoots=Noise(seed,16)<.55f?3:2;
        for(int i=0;i<shoots;i++){
            int armSeed=seed+i*313;
            int curlSide=i==1?-handedness:handedness;
            float start=turn+(Noise(armSeed,35)-.5f)*9/rim.Length;
            float delay=i==0?Noise(armSeed,4)*140:180+(i-1)*630+Noise(armSeed,4)*770;
            if(i==0&&(Noise(armSeed,23)<.55f||(group==0&&side==p.seed%4)))delay=0;
            Arm(rim,neighbor,p.birth,armSeed,i,start,curlSide,hue,(float)held-delay,fade);
        }
    }
    void Arm(KeycapRim rim,Contact neighbor,double birth,int seed,int order,float start,int direction,float hue,float age,float fade){
        if(age<0)return;
        bool primary=order==0;
        float vigor=Noise(seed,8),girth=Noise(seed,31);
        float wrap=primary?19+35*Noise(seed,2):7+19*Noise(seed,2);
        float reach=primary?80+122*vigor:23+93*vigor*vigor;
        float radius=primary?5.8f+9*girth*girth:1.8f+4.5f*girth;
        float duration=(primary?580:440)+reach*(3.5f+Noise(seed,9)*6);
        float grown=(primary?.065f:.012f)+.79f*Smooth(age/duration);
        grown+=(primary?.145f:.198f)*Smooth((age-duration*.68)/(750+Noise(seed,5)*1210));
        float total=wrap+reach,visible=total*grown*fade;
        if(visible<.6f)return;
        float lift=4+Noise(seed,17)*17,phase=Noise(seed,6)*6.283f;
        float clockPhase=(float)(now-birth)*.001f*(.68f+.79f*Noise(seed,11));
        float twist=(Noise(seed,22)<.45f?-1:1)*(2.6f+5.8f*Noise(seed,44));
        PointF normal;
        var exit=rim.At(start+direction*wrap/rim.Length,out normal);
        float exitOffset=5+3*Sin(phase);
        var tip=At(exit,normal.X*exitOffset,normal.Y*exitOffset);
        float outward=(float)Math.Atan2(normal.Y,normal.X);
        var target=neighbor==null?tip:KeycapRim.Closest(neighbor.key.cap,tip);
        float pull=neighbor==null||order!=0?0:Smooth(1-Distance(tip,target)/190)*Smooth((now-Math.Max(birth,neighbor.birth))/620);
        int wrapLinks=Math.Max(4,(int)Math.Ceiling(wrap/5)),freeLinks=Math.Max(8,(int)Math.Ceiling(reach/7));
        float distance=0;Joint previous=new Joint();
        for(int i=0;i<=wrapLinks+freeLinks;i++){
            // Step on a fixed path and reveal only its prefix. The final partial segment carries the growing tip.
            float next=i<=wrapLinks?wrap*i/wrapLinks:wrap+reach*(i-wrapLinks)/freeLinks;
            float s=Math.Min(visible,next),u=s/total;
            PointF pos;float height;
            if(i<=wrapLinks){
                float v=s/wrap;PointF n;
                var edge=rim.At(start+direction*s/rim.Length,out n);
                float offset=-3+(exitOffset+3)*Smooth(v)+Sin(v*6.283f+phase)*Sin(v*Math.PI)*4;
                pos=At(edge,n.X*offset,n.Y*offset);
                height=1.6f+5.5f*Smooth(v)+3.5f*Sin(v*Math.PI);
            }else{
                float v=(s-wrap)/reach,mid=((distance+s)*.5f-wrap)/reach;
                float peel=Smooth(mid/.38f);
                float angle=outward+direction*(1-peel)*1.52f+twist*mid*mid*mid
                    +(.16f+.73f*mid)*Sin(phase+mid*5.6f-clockPhase)*Smooth(mid/.32f);
                float step=s-distance;
                tip=At(tip,Cos(angle)*step,Sin(angle)*step);
                pos=Lerp(tip,target,pull*v*v);
                height=7.1f+lift*Sin(v*Math.PI)*(.60f+.40f*Sin(phase-clockPhase+v*3));
            }
            float shoulder=.24f+.76f*Smooth(s/(11+radius*.55f));
            float taper=(1-.78f*u)*(1-.25f*u);
            float tipWidth=Smooth((visible-s)/(8+radius*1.28f));
            var joint=new Joint{x=(pos.X-left)*Scale,y=(pos.Y-top-height*.47f)*Scale,z=height*Scale,
                r=.42f+radius*Scale*shoulder*taper*tipWidth*(.93f+.07f*Sin(phase+u*13))};
            if(i>0){
                int pigment=Pigment(hue+order*.047f+u*.087f);
                Tube(previous,joint,pigment,seed%37);
                float dx=joint.x-previous.x,dy=joint.y-previous.y,len=(float)Math.Sqrt(dx*dx+dy*dy);
                if(len>.35f&&i%2==0&&joint.r>2.35f)Sucker(joint,-direction*dy/len,direction*dx/len,pigment);
            }
            if(next>=visible)break;
            previous=joint;distance=s;
        }
    }
    protected override void Paint(Graphics g,int w,int h,Bitmap image,Rectangle sample){
        Scale=.40f+.2666667f*Budget.Count(100)/100f;
        var visible=g.VisibleClipBounds;visible.Inflate(20,20);
        foreach(var p in contacts){
            var area=RectangleF.Intersect(Extent(p),visible);
            if(area.Width<=0||area.Height<=0)continue;
            BeginTile(area);var rim=new KeycapRim(p.key.cap,4);
            Contact neighbor=null;float best=205;
            foreach(var q in contacts){
                if(p==q||q.released>=0)continue;
                var near=KeycapRim.Closest(q.key.cap,Center(p.key.cap));
                float d=Distance(near,KeycapRim.Closest(p.key.cap,near));
                if(d<best){neighbor=q;best=d;}
            }
            double age=now-p.birth,held=(p.released<0?now:p.released)-p.birth;float fade=Fade(p);
            for(int side=0;side<4;side++){
                float span=side%2==0?p.key.cap.Width:p.key.cap.Height;
                int groups=Math.Min(7,Math.Max(1,(int)Math.Round(span/128)));
                for(int group=0;group<groups;group++)GrowCluster(p,rim,neighbor,side,group,groups,held,fade);
            }
            // Cast shadows onto the transparent floor, displaced by each fragment's actual height.
            for(int y=height-2;y>=0;y--)for(int x=width-2;x>=0;x--){
                int idx=y*stride+x;if(depth[idx]<=.01f||((x+y)&1)!=0)continue;
                int d=2+(int)(depth[idx]*.31f),sx=x+d/2,sy=y+d;
                if(sx<width-1&&sy<height&&depth[sy*stride+sx]==0){
                    pixels[sy*stride+sx]=0x4906040a;if(depth[sy*stride+sx+1]==0)pixels[sy*stride+sx+1]=0x35050308;
                }
            }
            var save=g.Save();
            g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;g.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(tile,new RectangleF(left,top,width/Scale,height/Scale),new RectangleF(0,0,width,height),GraphicsUnit.Pixel);g.Restore(save);
            for(int i=0;i<32;i++){
                PointF outward;var root=rim.At(Noise(p.seed,i),out outward);float t=(float)((age*.00083+Noise(p.seed+1,i))%1);
                float r=(5+75*t)*Smooth(age/430)*fade;
                var pt=At(root,outward.X*r,outward.Y*r-9*Sin(t*Math.PI));
                float size=(1+2*Noise(p.seed+4,i))*Sin(Math.PI*t)*fade;
                Dot(g,Pigment(i*.14f),.76f,pt,size,size*.78f);Dot(g,0xffdedf,.7f,At(pt,-size*.28f,-size*.29f),size*.27f,size*.20f);
            }
        }
    }
    public void Dispose(){if(tile!=null){tile.Dispose();tile=null;}if(pin.IsAllocated)pin.Free();}
}
