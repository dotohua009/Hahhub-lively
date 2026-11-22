'use strict';
const canvas=document.querySelector('canvas'),ctx=canvas.getContext('2d');
const keys=new Map(LAYOUT.keys.map(key=>[key.code,key]));
const colors=[[245,35,85],[255,127,18],[249,213,20],[86,223,49],[12,209,174],[27,157,249],[134,76,251],[239,42,191]];
const presses=new Map();
let sprayRange=160,recovery=3500;
const stains=new Map(),pitchX=keys.get('KeyS').x-keys.get('KeyA').x;
const splashes=[],styles=['burst','flick','mist','pool'];
let frame=0,paused=false,fps=60,lastPaint=0,hue=Math.random(),nextStyle=styles.length;
const rand=Math.random,tau=Math.PI*2;
const range=(lo,hi)=>lo+(hi-lo)*rand(),count=(lo,hi)=>Math.floor(range(lo,hi)),bell=()=>(rand()+rand()+rand()-1.5)*1.45;
function fit(){canvas.width=innerWidth;canvas.height=innerHeight;}
function clear(){cancelAnimationFrame(frame);frame=0;splashes.length=0;stains.clear();presses.clear();ctx.clearRect(0,0,canvas.width,canvas.height);}
function style(){
  if(nextStyle===styles.length){
    const last=styles.at(-1);
    for(let i=styles.length-1;i>0;i--){const j=count(0,i+1);[styles[i],styles[j]]=[styles[j],styles[i]];}
    if(styles[0]===last)[styles[0],styles[1]]=[styles[1],styles[0]];
    nextStyle=0;
  }
  return styles[nextStyle++];
}
function drop(path,x,y,r,a,stretch){
  if(stretch<1.4){path.moveTo(x+r*stretch,y);path.ellipse(x,y,r*stretch,r,0,0,tau);return;}
  const ux=Math.cos(a),uy=Math.sin(a),len=r*stretch,nx=-uy*r,ny=ux*r;
  const tipX=x+ux*len,tipY=y+uy*len,tailX=x-ux*len,tailY=y-uy*len;
  path.moveTo(tailX,tailY);
  path.bezierCurveTo(x+nx,y+ny,tipX+nx,tipY+ny,tipX,tipY);
  path.bezierCurveTo(tipX-nx,tipY-ny,x-nx,y-ny,tailX,tailY);path.closePath();
}
function strike(key,source=key?.code){
  if(!key||paused||presses.has(source))return;
  const alreadyHeld=Array.from(presses.values()).includes(key);
  presses.set(source,key);if(alreadyHeld)return;
  const now=performance.now();
  hue=(hue+range(.19,.79))%1;const v=hue*colors.length,slot=Math.floor(v),blend=v-slot;
  const rgb=colors[slot].map((c,i)=>Math.round(c+(colors[(slot+1)%colors.length][i]-c)*blend));
  const color='#'+rgb.map(c=>c.toString(16).padStart(2,'0')).join('');
  const previous=stains.get(key.code),retained=previous?stainOpacity(previous,now):0;
  const tint=previous?rgb.map((v,i)=>Math.round(previous.rgb[i]+(v-previous.rgb[i])*.5/(.5+retained))):rgb;
  stains.set(key.code,{key,rgb:tint,color,birth:now,released:null,next:now+135,previous:retained,strength:Math.min(.88,.53+retained*.70)});
  emit(key,color,now,false);
  if(!frame){lastPaint=0;frame=requestAnimationFrame(draw);}
}
function release(source){
  const key=presses.get(source);if(!key)return;
  presses.delete(source);if(Array.from(presses.values()).includes(key))return;
  const st=stains.get(key.code),now=performance.now();st.released=now;st.recovery=recovery;
  let latest=true;
  for(let i=splashes.length-1;i>=0;i--){
    const s=splashes[i];if(s.key!==key)continue;
    s.end=latest?now+recovery:Math.min(s.end,now+recovery);
    s.fade=Math.min(s.fade,s.end-(s.end-now)*.65);latest=false;
  }
}
function emit(key,color,now,continuous){
  const type=style(),flick=type==='flick',mist=type==='mist',pool=type==='pool';
  const direction=rand()*tau,power=range(.85,1.18),core=(pool?range(.34,.46):mist?range(.17,.26):range(.25,.36))*(continuous?.52:1);
  const n=flick?count(3,7):pool?count(4,8):count(6,13);
  const jets=Array.from({length:n},(_,j)=>({
    angle:flick&&j<n-1?direction+bell()*.53:direction+tau*(j+rand()*.9)/n,
    reach:range(.08,flick?.55:pool?.24:.38)*power,width:range(.055,pool?.20:.14)
  }));
  const halfW=((key.w||1)-.08)/2,halfH=.46;
  const points=[],path=new Path2D(),lobe=rand()*tau;
  for(let i=0;i<90;i++){
    const a=direction+i*tau/90;
    let r=core*(1+.17*Math.sin(3*a+lobe)+.10*Math.sin(5*a-direction)+.07*bell());
    if(flick)r*=.70+.42*Math.abs(Math.cos(a-direction))+.30*Math.cos(a-direction);
    for(const jet of jets)r+=jet.reach*Math.exp(-((2*Math.sin((a-jet.angle)*.5)/jet.width)**2));
    points.push([Math.max(-halfW+.02,Math.min(halfW-.02,Math.cos(a)*r)),Math.max(-halfH+.02,Math.min(halfH-.02,Math.sin(a)*r))]);
  }
  const first=points[0],end=points.at(-1);path.moveTo((first[0]+end[0])/2,(first[1]+end[1])/2);
  points.forEach(([x,y],i)=>{const next=points[(i+1)%points.length];path.quadraticCurveTo(x,y,(x+next[0])/2,(y+next[1])/2);});
  path.closePath();
  const pores=count(3,8);
  for(let i=0;i<pores;i++){
    const a=rand()*tau,d=core*range(.50,.68),r=range(.008,.026),x=Math.cos(a)*d,y=Math.sin(a)*d;
    path.moveTo(x+r,y);path.arc(x,y,r,0,tau);path.closePath();
  }
  const flights=Array.from({length:count(8,13)},()=>({path:new Path2D(),delay:range(0,58),duration:range(245,470),
    bend:range(-.15,.15),lift:range(.02,.18),opacity:range(.57,.96),drops:[]}));
  const particles=continuous?count(26,53):mist?count(205,306):flick?count(145,228):pool?count(108,172):count(165,260);
  const escapeChance=range(.10,.18);
  for(let i=0;i<particles;i++){
    const jet=jets[count(0,n)];
    let a=jet.angle+bell()*(mist?.53:flick?.15:.27);
    if(rand()<(flick?.10:.22))a=direction+rand()*tau;
    const ux=Math.cos(a),uy=Math.sin(a);
    let reach=range(.48,1.1)+jet.reach;
    if(flick)reach*=.8+.65*Math.max(0,Math.cos(a-direction));else if(mist)reach*=1.16;
    let d=core*.62+reach*rand()**(pool?1.7:.83)*power;
    const r=range(.0065,.018)+rand()**5*(pool?.065:mist?.031:.05);
    const stretch=rand()<(flick?.30:.14)?range(1.6,3.4):range(.85,1.3);
    const edge=r*Math.max(1,stretch)+.008,wx=halfW-edge,wy=halfH-edge,escapes=rand()<escapeChance;
    const wall=Math.min(wx/Math.max(.001,Math.abs(ux)),wy/Math.max(.001,Math.abs(uy)));
    if(escapes||rand()<.89)d=Math.max(d,wall*range(1.15,escapes?1.85:2.15));
    if(escapes)d=wall+Math.min(2.6,Math.max(.25,d-wall))*sprayRange/100;
    const p={x:ux*d,y:uy*d,r,a,stretch,wx,wy,escapes,restitution:range(.27,.62)};
    const flight=flights[i%flights.length];flight.drops.push(p);traceDrop(flight.path,p,1,flight);
  }
  splashes.push({key,type,particles,color,core,birth:now,end:now+1520,fade:now+560,path,flights});
}
function ease(age,duration){return 1-(1-Math.max(0,Math.min(1,age/duration)))**3;}
function bounce(v,wall,k){
  while(Math.abs(v)>wall)v=Math.sign(v)*(wall-(Math.abs(v)-wall)*k);
  return v;
}
function traceDrop(path,p,t,f){
  let x=p.x*t+f.bend*t*(1-t),y=p.y*t-f.lift*t*(1-t),a=p.a;
  if(!p.escapes){
    if(Math.abs(x)>p.wx){x=bounce(x,p.wx,p.restitution);a=Math.PI-a;}
    if(Math.abs(y)>p.wy){y=bounce(y,p.wy,p.restitution);a=-a;}
  }
  drop(path,x,y,p.r,a,p.stretch);
}
function stainOpacity(s,now){
  const t=Math.min(1,Math.max(0,s.released===null?0:(now-s.released)/s.recovery));
  return (s.previous+(s.strength-s.previous)*ease(now-s.birth,135))*(1-t*t*(3-2*t));
}
function paintStains(now){
  for(const [code,s] of stains){
    if(s.released!==null&&now-s.released>=s.recovery){stains.delete(code);continue;}
    const w=LAYOUT.axisSize*canvas.width/1920,h=LAYOUT.axisSize*canvas.height/720;
    ctx.save();ctx.translate(s.key.x*canvas.width,s.key.y*canvas.height);ctx.scale(w,h);
    const c=s.rgb.join(','),wash=ctx.createRadialGradient(-.13,.14,0,0,0,.65);
    wash.addColorStop(0,'rgba('+c+',.84)');wash.addColorStop(.77,'rgba('+c+',.46)');wash.addColorStop(1,'rgba('+c+',.28)');
    ctx.globalAlpha=stainOpacity(s,now);ctx.fillStyle=wash;ctx.beginPath();ctx.roundRect(-.49,-.49,.98,.98,.13);ctx.fill();ctx.restore();
  }
}
function draw(now){
  frame=0;
  if(now-lastPaint>=1000/fps-.5){
    lastPaint=now;ctx.clearRect(0,0,canvas.width,canvas.height);
    for(const s of stains.values())if(s.released===null&&now>=s.next){emit(s.key,s.color,now,true);s.next=now+135;}
    for(let i=splashes.length-1;i>=0;i--)if(now>=splashes[i].end)splashes.splice(i,1);
    paintStains(now);
    for(const s of splashes){
      const age=now-s.birth,spread=.13+.87*ease(age,145),opacity=1-Math.max(0,(now-s.fade)/Math.max(1,s.end-s.fade));
      ctx.save();ctx.translate(s.key.x*canvas.width,s.key.y*canvas.height);
      ctx.scale(canvas.width*pitchX,canvas.height*LAYOUT.pitch);
      ctx.save();ctx.scale(spread,spread);
      const wash=ctx.createRadialGradient(-s.core*.19,s.core*.12,0,0,0,s.core*1.5);
      wash.addColorStop(0,s.color);wash.addColorStop(1,s.color+'7e');
      ctx.globalAlpha=opacity;ctx.fillStyle=wash;ctx.fill(s.path,'evenodd');ctx.restore();ctx.fillStyle=s.color;
      for(const f of s.flights){
        if(age<=f.delay)continue;
        const t=ease(age-f.delay,f.duration);
        ctx.globalAlpha=opacity*f.opacity;
        if(t===1){ctx.fill(f.path);continue;}
        const moving=new Path2D();for(const p of f.drops)traceDrop(moving,p,t,f);ctx.fill(moving);
      }
      ctx.restore();
    }
  }
  if(splashes.length||stains.size)frame=requestAnimationFrame(draw);
}
window.livelyPropertyListener=(name,value)=>{if(name==='fps')fps=[16,24,30,60][value]||60;
  if(!Number.isFinite(+value))return;
  if(name==='spread')sprayRange=Math.max(75,Math.min(250,+value));
  if(name==='recovery')recovery=Math.max(500,Math.min(6000,+value));};
window.livelyWallpaperPlaybackChanged=value=>{
  if(typeof value==='string'){try{value=JSON.parse(value);}catch{return;}}
  if(typeof value?.IsPaused==='boolean'){paused=value.IsPaused;if(paused)clear();}
};
addEventListener('keydown',e=>{if(!e.repeat)strike(keys.get(e.code));});
addEventListener('keyup',e=>release(e.code));
addEventListener('blur',clear);
document.addEventListener('visibilitychange',()=>{if(document.hidden)clear();});
canvas.addEventListener('pointerdown',e=>{
  canvas.setPointerCapture(e.pointerId);
  const dist=k=>Math.hypot(k.x*innerWidth-e.clientX,k.y*innerHeight-e.clientY);
  strike(LAYOUT.keys.reduce((a,b)=>dist(a)<dist(b)?a:b),e.pointerId);
});
for(const event of ['pointerup','pointercancel','lostpointercapture'])canvas.addEventListener(event,e=>release(e.pointerId));
addEventListener('resize',fit);fit();
