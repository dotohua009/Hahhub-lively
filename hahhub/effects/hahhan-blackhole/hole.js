'use strict';
const canvas=document.querySelector('canvas'), ctx=canvas.getContext('2d');
const bg=document.createElement('canvas'), backing=bg.getContext('2d',{willReadFrequently:true});
const cover=new Image(), size=192, steps=1024, diameter=3;
const tile=document.createElement('canvas');tile.width=tile.height=size;
const pixels=tile.getContext('2d'), frame=pixels.createImageData(size,size);
const keys=new Map(LAYOUT.keys.map(k=>[k.code,k])), wells=new Map();
const sine=Float32Array.from({length:steps},(_,i)=>Math.sin(i*2*Math.PI/steps));
const sin=i=>sine[i&(steps-1)], clamp=x=>Math.max(0,Math.min(1,x));
const shadow=[6,7,18],band=[245,239,234],rim=[228,231,242],dark=[2,3,10];
const disc=[];
for(let y=0;y<size;y++)for(let x=0;x<size;x++){
  const u=(x+.5)*2/size-1,v=(y+.5)*2/size-1,r=Math.hypot(u,v);
  if(r<1)disc.push({i:(y*size+x)*4,r,u,v,a:Math.round(Math.atan2(v,u)*steps/(2*Math.PI))&(steps-1)});
}
let backgroundPixels;
let power=80,cap=60,due=0,paused=false,request=0,pointerKey=null;
function fit(){
  bg.width=canvas.width=Math.max(1,innerWidth);bg.height=canvas.height=Math.max(1,innerHeight);
  if(cover.naturalWidth){backing.drawImage(cover,0,0,bg.width,bg.height);backgroundPixels=backing.getImageData(0,0,bg.width,bg.height);}
}
cover.onload=fit;cover.src=BACKGROUND;document.body.style.backgroundImage='url('+BACKGROUND+')';fit();addEventListener('resize',fit);
window.livelyPropertyListener=(name,value)=>{
  if(name==='rim'&&/^#[0-9a-f]{6}$/i.test(value))for(let i=0;i<3;i++)rim[i]=parseInt(value.slice(1+i*2,3+i*2),16);
  if(name==='fps'){cap=[16,24,30,60][value]||60;due=0;}
  if(name==='pull'&&Number.isFinite(+value))power=Math.max(20,Math.min(100,+value));
};
function clear(){cancelAnimationFrame(request);request=0;wells.clear();pointerKey=null;ctx.clearRect(0,0,canvas.width,canvas.height);}
window.livelyWallpaperPlaybackChanged=data=>{
  if(typeof data==='string'){try{data=JSON.parse(data);}catch{return;}}
  if(typeof data?.IsPaused==='boolean'){paused=data.IsPaused;if(paused)clear();}
};
function openness(h,now){
  const turn=Math.max(h.released??now,h.birth+155),age=clamp(((h.released===null?now:Math.min(now,turn))-h.pressed)/240);
  const open=1-(1-h.base)*(1-age)**2;
  const exit=h.released===null?0:clamp((now-turn)/560);
  return open*(1-exit*exit*(3-2*exit));
}
function strike(key,now=performance.now()){
  if(!key||paused)return;let h=wells.get(key.code);
  if(h&&openness(h,now)>0){
    if(h.released===null)return;
    h.baseGrowth=growth(h,now);h.base=openness(h,now);h.pressed=now;h.released=null;
  }else{
    const seed=key.scan||Math.trunc(key.x*937),p=seed*137;
    const edge=new Float32Array(steps);
    h={key,edge,p,spin:seed%2? -1:1,base:.16,baseGrowth:0,birth:now,pressed:now,released:null};wells.set(key.code,h);
  }
  if(!request)request=requestAnimationFrame(animate);
}
function release(key,now=performance.now()){const h=wells.get(key?.code);if(h&&h.released===null){h.released=now;if(!request)request=requestAnimationFrame(animate);}}
function growth(h,now){
  const t=clamp(((h.released??now)-h.pressed-360)/4340),exit=h.released===null?0:clamp((now-h.released)/560);
  return (h.baseGrowth+(1-h.baseGrowth)*t*t)*(1-exit*exit*(3-2*exit));
}
function plan(now){
  const W=canvas.width,H=canvas.height,ux=diameter*LAYOUT.pitch*720*W/1920,uy=diameter*LAYOUT.pitch*H;
  const lenses=[];let covered=false;
  function components(left){
    while(left.length&&!covered){
      const group=[left.shift()];
      for(const h of group)for(let j=left.length-1;j>=0;j--){
        const k=left[j].key,dx=(k.x-h.key.x)*W*diameter/ux,dy=(k.y-h.key.y)*H*diameter/uy;
        if(dx*dx+dy*dy<=2.36)group.push(left.splice(j,1)[0]);
      }
      merge(group);
    }
  }
  function merge(group){
    let cx=0,cy=0,mass=0,open=0,g=0;
    for(const h of group){
      const o=openness(h,now),m=o*o;cx+=h.key.x*m;cy+=h.key.y*m;mass+=m;open=Math.max(open,o);g=Math.max(g,growth(h,now));
    }
    if(!mass)return;if(g>=1){covered=true;return;}
    cx=cx*W/mass;cy=cy*H/mass;
    const full=Math.hypot(2*Math.max(Math.abs(cx),Math.abs(W-cx))/ux,2*Math.max(Math.abs(cy),Math.abs(H-cy))/uy)/.26;
    const size=Math.max(1,Math.sqrt(mass)),scale=size+(full-size)*g,w=ux*scale,height=uy*scale,x=cx-w/2,y=cy-height/2;
    group.sort((a,b)=>a.birth-b.birth||a.key.scan-b.key.scan);
    const first=group[0],newest=group[group.length-1];let join=clamp((now-newest.birth)/360);
    if(group.length===1||join>=1){lenses.push({h:first,w,height,x,y,open,opacity:1});return;}
    const start=lenses.length;group.pop();components(group);merge([newest]);join=join*join*(3-2*join);
    for(const lens of lenses.slice(start)){
      lens.x+=(x-lens.x)*join;lens.y+=(y-lens.y)*join;lens.w+=(w-lens.w)*join;lens.height+=(height-lens.height)*join;
      if(lens.h!==first)lens.opacity*=1-join;else lens.open+=(open-lens.open)*join;
    }
  }
  components([...wells.values()]);if(covered)return null;
  return lenses;
}
function paint({h,w,height,x,y,open,opacity},now){
  const left=Math.max(0,Math.floor(x)),top=Math.max(0,Math.floor(y)),right=Math.min(bg.width,Math.ceil(x+w)),bottom=Math.min(bg.height,Math.ceil(y+height));
  if(right<=left||bottom<=top)return;
  const image=backgroundPixels;if(!image)return;const data=image.data;
  const output=frame.data,elapsed=(now-h.birth)*(.0008+.0005*power/100),phase=elapsed%1,other=(phase+.5)%1,weight=1-Math.abs(2*phase-1),wave=Math.trunc((now-h.birth)*.22);
  function sample(angle,radius){
    const xx=Math.min(image.width-1,Math.max(0,Math.floor(x+w*(.5+.5*radius*sin(angle+steps/4)))));
    const yy=Math.min(image.height-1,Math.max(0,Math.floor(y+height*(.5+.5*radius*sin(angle)))));
    return 4*(xx+yy*image.width);
  }
  const age=now-h.birth,drift=Math.trunc(age*.115),tilt=h.p+Math.trunc(53*sin(Math.trunc(age*.13)+h.p));
  const major=.42+.016*sin(drift+h.p),minor=major*(.84+.09*sin(Math.trunc(age*.15)-h.p));
  for(let j=0;j<steps;j++){
    const c=sin(j-tilt+steps/4),s=sin(j-tilt);
    h.edge[j]=major*minor/Math.sqrt(minor*minor*c*c+major*major*s*s)+.009*sin(3*j+h.p+drift)+.005*sin(4*j-h.p-2*drift);
  }
  for(const {i,r,a,u,v} of disc){
    const core=h.edge[a]*open,d=r-core,inward=clamp((1-r)/(1-core)),pull=inward*(2-inward)*open;
    const bend=pull*(.36+.64*power/100)*(.91+.09*sin(3*a+h.p));
    const turn=Math.trunc(bend*(.18+.68*inward)*h.spin*steps),radial=(1-r)*bend;
    const p=sample(a+Math.trunc(turn*(.40+phase)),r+radial*(.22+.78*phase)),q=sample(a+Math.trunc(turn*(.40+other)),r+radial*(.22+.78*other));
    const lip=1-clamp(Math.abs(d-.018)/.047),arc=.56+.30*sin(2*a+h.p-wave)+.14*sin(5*a+wave);
    const spiral=Math.max(0,sin(a*2+h.p+Math.trunc(d*(1700+900*inward))*h.spin+h.spin*wave*2));
    const fold=spiral**8*pull*clamp(d*16)*.385;
    const shade=clamp((.10-d)*8)*open*.65,glint=lip*lip*arc*open*.88,coreMix=clamp(-d*46);
    const halo=(1-clamp(Math.abs(d-.018)/.088))**2*arc*open*.30;
    for(let channel=0;channel<3;channel++){
      let col=data[q+channel]+(data[p+channel]-data[q+channel])*weight;
      col+=(shadow[channel]-col)*shade;col+=(band[channel]-col)*fold;
      col+=(rim[channel]-col)*halo;col+=(rim[channel]+(255-rim[channel])*.16-col)*glint;
      output[i+channel]=col+(dark[channel]-col)*coreMix;
    }
    const alpha=clamp((1-r)*15)*clamp(open*5)*opacity;
    output[i+3]=255*alpha;
  }
  pixels.putImageData(frame,0,0);ctx.drawImage(tile,x,y,w,height);
}
function animate(now){
  request=0;
  if(now+.2>=due){
    const dt=1000/cap;due=due>0&&now-due<dt?due+dt:now+dt;ctx.clearRect(0,0,canvas.width,canvas.height);
    for(const [key,h] of wells)if(openness(h,now)<=0)wells.delete(key);
    const lenses=plan(now);
    if(lenses===null){ctx.fillStyle='#02030a';ctx.fillRect(0,0,canvas.width,canvas.height);if([...wells.values()].some(h=>h.released===null&&growth(h,now)>=1))return;}
    else for(const lens of lenses)paint(lens,now);
  }
  if(wells.size)request=requestAnimationFrame(animate);
}
addEventListener('keydown',e=>{if(!e.repeat)strike(keys.get(e.code));});addEventListener('keyup',e=>release(keys.get(e.code)));
canvas.addEventListener('pointerdown',e=>{
  if(!e.isPrimary)return;
  const distance=k=>Math.hypot(k.x*innerWidth-e.clientX,k.y*innerHeight-e.clientY);
  pointerKey=LAYOUT.keys.reduce((a,b)=>distance(a)<distance(b)?a:b);canvas.setPointerCapture(e.pointerId);strike(pointerKey);
});
canvas.addEventListener('lostpointercapture',()=>{release(pointerKey);pointerKey=null;});
addEventListener('blur',()=>{for(const h of wells.values())release(h.key);});
