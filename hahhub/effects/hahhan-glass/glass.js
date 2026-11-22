'use strict';
const canvas=document.querySelector('canvas'), ctx=canvas.getContext('2d');
const keys=new Map(GLASS.keys.map(k=>[k.code,k])), hits=new Map();
const options={density:75,spread:85,heal:1800,delay:800,crackSize:0}, limits={density:[30,100],spread:[45,150],heal:[500,4000],delay:[0,5000],crackSize:[0,1]};
let next=0, paused=false,cap=60,due=0;
let rim='#e6f1ff';
const joints=[];
const embedded=Boolean(window.hahhanNative && window.chrome?.webview);
function status(){document.title='HAHHAN / glass / '+[options.density,options.spread,options.heal,options.delay,options.crackSize,Number(paused)].join('/');}
window.livelyPropertyListener=(name,value)=>{
  if(name==='rim'&&/^#[0-9a-f]{6}$/i.test(value)){rim=value;return;}
  if(name==='fps'){cap=[16,24,30,60][value]||60;due=0;return;}
  if(!Object.hasOwn(limits,name)||!Number.isFinite(Number(value)))return;
  options[name]=Math.min(limits[name][1],Math.max(limits[name][0],Math.round(Number(value))));
  status();
};
function fit(){canvas.width=Math.max(1,innerWidth);canvas.height=Math.max(1,innerHeight);}
function clear(){cancelAnimationFrame(next);next=0;hits.clear();joints.length=0;ctx.clearRect(0,0,canvas.width,canvas.height);}
window.livelyWallpaperPlaybackChanged=data=>{
  if(typeof data==='string'){try{data=JSON.parse(data);}catch{return;}}
  if(typeof data?.IsPaused!=='boolean')return;
  paused=data.IsPaused;if(paused)clear();status();
};
function reach(hit,now){
  const age=Math.max(0,(hit.released===null?now:Math.min(now,hit.released))-hit.pressed-(hit.fresh?90:0));
  const peak=Math.min(24,hit.base+.24*Math.min(1,age/300)+.00047*age);
  return hit.released===null||now<=hit.mend?peak:peak*Math.max(0,(hit.expires-now)/(hit.expires-hit.mend));
}
function contact(hit){
  const r=GLASS.axisSize*.25/(720*GLASS.pitch*hit.scale);let first=Infinity;
  for(const [start,end,x,y,bx,by,,rank] of hit.shape.segments){
    if(start>=first)break;if(rank>hit.density||bx*bx+by*by<r*r)continue;
    const dx=bx-x,dy=by-y,d=dx*dx+dy*dy,dot=x*dx+y*dy,c=r*r-x*x-y*y;
    const f=c<=0||!d?0:(Math.sqrt(dot*dot+d*c)-dot)/d;
    first=Math.min(first,start+(end-start)*Math.min(1,f));
  }
  return first;
}
function expire(now){
  for(const [k,h] of hits)if(h.released!==null&&now>=h.expires)hits.delete(k);
  for(let i=joints.length-1;i>=0;i--)if(!hits.has(joints[i].from.key.code)||!hits.has(joints[i].to.key.code))joints.splice(i,1);
}
const dist=(a,b)=>Math.hypot(b[0]-a[0],b[1]-a[1]);
const lerp=(a,b,f)=>[a[0]+(b[0]-a[0])*f,a[1]+(b[1]-a[1])*f];
function growingEdges(h,now,visit){
  const front=reach(h,now);
  for(const [start,end,x,y,bx,by,style,rank] of h.shape.segments){
    if(start>=front)break;if(rank>h.density)continue;
    const t=Math.min(1,(front-start)/Math.max(.00001,end-start));
    visit(h.x+x*h.scale,h.y+y*h.scale,h.x+(x+(bx-x)*t)*h.scale,h.y+(y+(by-y)*t)*h.scale,style);
  }
}
function toward(hit,target,now){
  let best=[hit.x,hit.y],distance=dist(best,target);
  growingEdges(hit,now,(x,y,bx,by)=>{
    const dx=bx-x,dy=by-y,t=Math.max(0,Math.min(1,((target[0]-x)*dx+(target[1]-y)*dy)/Math.max(.000001,dx*dx+dy*dy)));
    const p=[x+t*dx,y+t*dy],d=dist(p,target);if(d<distance){best=p;distance=d;}
  });
  return best;
}
function jointReach(j,now){
  const grown=Math.min(j.length,j.start+Math.max(0,(j.end===null?now:Math.min(now,j.turn))-j.at)*.009);
  return j.end===null||now<=j.mend?grown:grown*Math.max(0,(j.end-now)/Math.max(1,j.end-j.mend));
}
function retarget(j,now,newborn=false){
  const released=[j.from,j.to].filter(h=>h.released!==null).sort((a,b)=>a.expires-b.expires);
  const first=released[0],end=first?.expires??null;
  if(end===j.end)return;
  j.start=jointReach(j,now);j.at=now;j.end=end;
  j.turn=newborn&&end>now?now+Math.min(140,(end-now)*.35):now;
  j.mend=Math.max(j.turn,first?.mend??now);
}
function connect(hit,now){
  let other=null,closest=Infinity;
  for(const h of hits.values())if(h!==hit){const d=Math.hypot(h.x-hit.x,h.y-hit.y);if(d<closest){closest=d;other=h;}}
  if(!other||joints.some(j=>(j.from===other&&j.to===hit)||(j.to===other&&j.from===hit)))return;
  const a=toward(other,[hit.x,hit.y],now),b=toward(hit,a,now),len=dist(a,b);if(len<.02)return;
  const count=Math.max(3,Math.min(54,Math.ceil(len/.23))),points=[a],seed=other.key.scan*17+hit.key.scan*73;
  for(let i=1;i<count;i++){
    const t=i/count,offset=Math.sin(Math.PI*t)*(.11*Math.sin(seed+i*1.87)+.075*Math.sin(seed*.17+i*.61))*Math.min(1,len),p=lerp(a,b,t);
    points.push([p[0]-(b[1]-a[1])/len*offset,p[1]+(b[0]-a[0])/len*offset]);
  }
  points.push(b);const length=points.slice(1).reduce((total,p,i)=>total+dist(points[i],p),0);
  const j={from:other,to:hit,at:now,start:0,turn:now,end:null,length,points};retarget(j,now,true);joints.push(j);
}
function strike(key,now=performance.now()){
  if(!key||paused||embedded)return;expire(now);
  let hit=hits.get(key.code);
  if(hit){
    if(hit.released===null)return;
    hit.base=reach(hit,now);hit.pressed=now;hit.released=null;hit.fresh=false;
    for(const j of joints)if(j.from===hit||j.to===hit)retarget(j,now);
  }else{
    const shapes=options.crackSize===0?GLASS.largeShapes:GLASS.shapes;
    hit={key,large:options.crackSize===0,shape:shapes[(key.scan*31+3)%shapes.length],x:key.x*1920/(720*GLASS.pitch),y:key.y/GLASS.pitch,
      scale:options.spread/100,density:options.density,base:0,fresh:true,pressed:now,released:null};
    hit.base=contact(hit);
    hits.set(key.code,hit);
  }
  connect(hit,now);if(!next)next=requestAnimationFrame(frame);
}
function release(key,now=performance.now()){
  const h=hits.get(key?.code);if(!h||h.released!==null)return;
  h.released=now;h.mend=now+options.delay;h.expires=h.mend+options.heal;for(const j of joints)if(j.from===h||j.to===h)retarget(j,now);
}
function frame(now){
  if(now+.2<due){next=requestAnimationFrame(frame);return;}
  const dt=1000/cap;due=due>0&&now-due<dt?due+dt:now+dt;
  next=0;expire(now);ctx.clearRect(0,0,canvas.width,canvas.height);
  const sx=720*GLASS.pitch*canvas.width/1920,sy=GLASS.pitch*canvas.height,unit=Math.max(.65,canvas.height/720);
  function paint(paths,alpha,width=unit){
    ctx.save();ctx.globalAlpha=alpha;
    for(let k=0;k<paths.length;k++){
      const p=paths[k];ctx.strokeStyle=rim+(k===0?'3e':'20');ctx.lineWidth=[4.35,3.65,3.15][k]*width;ctx.stroke(p);
      ctx.strokeStyle=['rgba(9,19,29,.84)','rgba(9,19,29,.84)','rgba(9,19,29,.59)'][k];
      ctx.lineWidth=[2.05,1.35,.85][k]*width;ctx.stroke(p);
      ctx.save();ctx.translate(.55*unit,-.52*unit);ctx.strokeStyle=rim+['f5','c9','96'][k];
      ctx.lineWidth=[.92,.64,.43][k]*width;ctx.stroke(p);ctx.restore();
    }ctx.restore();
  }
  for(const h of hits.values()){
    const front=reach(h,now);
    for(const facet of h.shape.facets){
      if(front<=facet.at)continue;ctx.fillStyle='rgba('+(facet.tone>=0?'214,229,241':'10,20,33')+','+((14+23*Math.abs(facet.tone))/255*Math.min(1,(front-facet.at)*8))+')';ctx.beginPath();
      facet.points.forEach(([x,y],i)=>ctx[i?'lineTo':'moveTo']((h.x+x*h.scale)*sx,(h.y+y*h.scale)*sy));ctx.closePath();ctx.fill();
    }
    const paths=[new Path2D(),new Path2D(),new Path2D()];
    growingEdges(h,now,(x,y,bx,by,k)=>{paths[k].moveTo(x*sx,y*sy);paths[k].lineTo(bx*sx,by*sy);});paint(paths,1,h.large?unit*1.24:unit);
  }
  for(const j of joints){
    const p=new Path2D();let length=jointReach(j,now);
    p.moveTo(j.points[0][0]*sx,j.points[0][1]*sy);
    for(let i=1;i<j.points.length&&length>0;i++){
      const a=j.points[i-1],b=j.points[i],len=dist(a,b),end=lerp(a,b,Math.min(1,length/Math.max(.00001,len)));
      p.lineTo(end[0]*sx,end[1]*sy);length-=len;
    }paint([new Path2D(),p],1);
  }
  if(hits.size)next=requestAnimationFrame(frame);
}
addEventListener('keydown',e=>{if(!e.repeat)strike(keys.get(e.code));});
addEventListener('keyup',e=>release(keys.get(e.code)));
let pointerKey=null;
canvas.addEventListener('pointerdown',e=>{
  if(!e.isPrimary)return;const x=e.clientX/innerWidth,y=e.clientY/innerHeight;
  pointerKey=GLASS.keys.reduce((a,b)=>Math.hypot((a.x-x)*innerWidth,(a.y-y)*innerHeight)<Math.hypot((b.x-x)*innerWidth,(b.y-y)*innerHeight)?a:b);
  canvas.setPointerCapture(e.pointerId);strike(pointerKey);
});
canvas.addEventListener('lostpointercapture',()=>{release(pointerKey);pointerKey=null;});
addEventListener('blur',()=>{for(const h of hits.values())release(h.key);});
addEventListener('resize',()=>{fit();if(hits.size&&!next)next=requestAnimationFrame(frame);});
fit();status();
