(() => {
  'use strict';
  const canvas=document.getElementById('screen'),ctx=canvas.getContext('2d'),byCode=new Map(KEYS.map(k=>[k.code,k]));
  // Runs inside Lively Wallpaper: https://github.com/lively-community/lively
  const settings={effect:0,splashRange:200,splashSpeed:300,holdDelay:180,repeatInterval:120,background:0,backgroundColor:'#ffffff',brightness:100,fps:30,keyLight:85,gameSpeed:100,glassRepair:12000,strength:100,duration:2600,labels:false,calibration:false,shiftX:0,shiftY:0,scaleX:100,scaleY:100};
  let W=innerWidth,H=innerHeight,events=[],held=new Map(),lastPress=null,lastFrame=0,received=0,frameCount=0,raf=0,inputPending=false;
  const TAU=Math.PI*2,clamp=(n,a,b)=>Math.max(a,Math.min(b,n));
  function box(k){const t=CALIBRATION.transform,v=CALIBRATION.viewport,sx=W/v.width,sy=H/v.height,px=t.px*sx*settings.scaleX/100,py=t.py*sy*settings.scaleY/100,cx=(t.x+8*t.px)*sx+settings.shiftX*W/1920,cy=(t.y+3*t.py)*sy+settings.shiftY*H/720;return{x:cx+(k.x-8)*px,y:cy+(k.y-3)*py,w:k.w*px,h:py,cx:cx+(k.cx-8)*px,cy:cy+(k.cy-3)*py};}
  function wake(){if(!raf&&!document.hidden){lastFrame=performance.now();raf=requestAnimationFrame(frame);}}
  function resize(){W=innerWidth;H=innerHeight;canvas.width=W;canvas.height=H;wake();}
  function trigger(code,time=performance.now()){if(!byCode.has(code))return false;const previous=lastPress&&time-lastPress.time>=0&&time-lastPress.time<1000&&lastPress.code!==code?lastPress.code:null;events.push({code,start:time,previous,seed:Math.random()*TAU,hit:keyGames.press(code)});if(events.length>96)events.shift();lastPress={code,time};received++;inputPending=true;wake();return true;}
  function key(code,down,time=performance.now()){if(!byCode.has(code))return;if(down){if(!held.has(code)){held.set(code,time+settings.holdDelay);trigger(code,time);}}else if(held.delete(code))keyGames.release(code);}
  function releaseAll(){for(const code of held.keys())key(code,false);}
  function isLight(){if(settings.background===5){const c=settings.backgroundColor;return (.299*parseInt(c.slice(1,3),16)+.587*parseInt(c.slice(3,5),16)+.114*parseInt(c.slice(5,7),16))*settings.brightness/100>115;}return settings.background<2&&settings.brightness>45;}
  function color(k,a=1,shift=0){return `hsla(${(182+k.cx*10+k.y*6+shift)%360},82%,${isLight()?43:66}%,${clamp(a,0,1)})`;}
  function glow(k,x,y,r,a){const g=ctx.createRadialGradient(x,y,0,x,y,r);g.addColorStop(0,color(k,a*.35));g.addColorStop(.45,color(k,a*.12));g.addColorStop(1,color(k,0));ctx.fillStyle=g;ctx.fillRect(x-r,y-r,r*2,r*2);}
  function keyLights(now){
    if(isLight()||!settings.keyLight)return;
    const lit=new Map();
    for(const e of events){const age=now-e.start;if(age<0||age>900)continue;lit.set(e.code,Math.max(lit.get(e.code)||0,1-age/900));}
    for(const code of held.keys())lit.set(code,.9+.1*Math.sin(now*.006));
    for(const [code,level] of lit){const k=byCode.get(code),b=box(k),r=Math.min(b.w,b.h)*.62,a=level*settings.keyLight/100,g=ctx.createRadialGradient(b.cx,b.cy,0,b.cx,b.cy,r);
      g.addColorStop(0,`rgba(244,253,255,${a})`);g.addColorStop(.24,`rgba(192,239,255,${a*.85})`);g.addColorStop(.58,color(k,a*.55));g.addColorStop(1,color(k,0));ctx.fillStyle=g;ctx.fillRect(b.cx-r,b.cy-r,r*2,r*2);
    }
  }
  function flower(k,b,e,age,fade,p){
    const r=p*(.12+.8*(1-Math.exp(-age*3)));glow(k,b.cx,b.cy,r*1.15,fade);ctx.save();ctx.translate(b.cx,b.cy);ctx.rotate(e.seed*.25+age*.13);ctx.lineWidth=1.7*W/1920;ctx.strokeStyle=color(k,fade*.82);
    for(let i=0;i<10;i++){ctx.rotate(TAU/10);ctx.beginPath();ctx.moveTo(0,0);ctx.bezierCurveTo(r*.36,-r*.33,r*.9,-r*.22,r,0);ctx.bezierCurveTo(r*.9,r*.22,r*.36,r*.33,0,0);ctx.stroke();}
    ctx.strokeStyle=color(k,fade*.48,24);for(const f of [.28,.56,.86]){ctx.beginPath();ctx.arc(0,0,r*f,0,TAU);ctx.stroke();}
    ctx.fillStyle=color(k,fade);ctx.beginPath();ctx.arc(0,0,3.2,0,TAU);ctx.fill();ctx.restore();
  }
  function ripple(k,b,e,age,fade,p){
    glow(k,b.cx,b.cy,p*.65,fade);for(let i=0;i<4;i++){const a=age-i*.14;if(a<0)continue;const r=p*(.06+a*.85);ctx.strokeStyle=color(k,fade*(1-i*.16),i*9);ctx.lineWidth=(3.6-i*.65)*(W/1920);ctx.beginPath();ctx.ellipse(b.cx,b.cy,r,r,0,0,TAU);ctx.stroke();}
  }
  function particles(k,b,e,age,fade,p){
    glow(k,b.cx,b.cy,p*.5,fade*.6);ctx.lineCap='round';for(let i=0;i<30;i++){const angle=i*2.399963+e.seed,speed=p*(.45+(i%7)*.17)*settings.splashRange/100,drag=1-Math.exp(-age*1.5*settings.splashSpeed/100),x=b.cx+Math.cos(angle)*speed*drag,y=b.cy+Math.sin(angle)*speed*drag+age*age*p*.13,len=p*.06*(1-age/3);ctx.strokeStyle=color(k,fade,((i*17)%70)-35);ctx.lineWidth=2+(i%3);ctx.beginPath();ctx.moveTo(x-Math.cos(angle)*len,y-Math.sin(angle)*len);ctx.lineTo(x,y);ctx.stroke();ctx.fillStyle=color(k,fade*.75);ctx.beginPath();ctx.arc(x,y,1.4+(i%3)*.6,0,TAU);ctx.fill();}
  }
  function connection(k,b,e,age,fade,p){
    glow(k,b.cx,b.cy,p*.6,fade);ctx.lineWidth=2;ctx.strokeStyle=color(k,fade*.9);ctx.beginPath();ctx.arc(b.cx,b.cy,p*(.12+.12*Math.min(age,1)),0,TAU);ctx.stroke();
    if(!e.previous)return;const a=box(byCode.get(e.previous)),dx=b.cx-a.cx,dy=b.cy-a.cy,len=Math.hypot(dx,dy)||1,bend=Math.min(55,len*.15),nx=-dy/len*bend,ny=dx/len*bend;
    for(let line=0;line<3;line++){ctx.lineWidth=1.5;ctx.strokeStyle=color(k,fade*(.6-line*.13),line*15);ctx.beginPath();const end=clamp(age*5,0,1);for(let t=0;t<=end+.008;t+=.008){const x=a.cx+dx*t+nx*Math.sin(t*Math.PI)*(line-1),y=a.cy+dy*t+ny*Math.sin(t*Math.PI)*(line-1);if(t===0)ctx.moveTo(x,y);else ctx.lineTo(x,y);}ctx.stroke();}
    const t=clamp(age*1.7,0,1);ctx.fillStyle=color(k,fade);ctx.beginPath();ctx.arc(a.cx+dx*t,a.cy+dy*t,4,0,TAU);ctx.fill();
  }
  const renderers=[flower,ripple,particles,connection];
  function glassScene(cracks){
    const light=isLight(),unit=W/1920;
    for(const c of cracks){
      const b=box(byCode.get(c.code)),p=Math.min(b.w,b.h),reach=p*(.3+c.damage*.23),alpha=Math.min(1,c.damage),path=new Path2D();
      const point=(i,j)=>{const angle=c.seed+i*TAU/9+Math.sin(i*17+c.seed)*.22+Math.sin(i*17+j*23+c.seed)*.08,r=p*j*.32*(.72+.25*(1+Math.sin(i*9+c.seed)));return{x:b.cx+Math.cos(angle)*r,y:b.cy+Math.sin(angle)*r,r};};
      for(let i=0;i<9;i++){
        let prev={x:b.cx,y:b.cy,r:0};path.moveTo(prev.x,prev.y);
        for(let j=1;j<=7;j++){
          const end=point(i,j),f=clamp((reach-prev.r)/(end.r-prev.r),0,1),x=prev.x+(end.x-prev.x)*f,y=prev.y+(end.y-prev.y)*f;
          path.lineTo(x,y);if(f<1)break;
          if(j>1&&(i+j)%2===0){const angle=c.seed+i*TAU/9+.55*(i%2?1:-1),length=Math.min(p*.3,reach-end.r);path.lineTo(x+Math.cos(angle)*length,y+Math.sin(angle)*length);path.moveTo(x,y);}
          prev=end;
        }
        for(let j=1;j<=5;j++){const a=point(i,j),b2=point((i+1)%9,j);if(a.r>reach||((i+j)%3===0))continue;path.moveTo(a.x,a.y);path.lineTo((a.x+b2.x)/2+p*.035*Math.sin(i+j),(a.y+b2.y)/2-p*.04);path.lineTo(b2.x,b2.y);}
        const a=point(i,1),b2=point((i+1)%9,1);ctx.fillStyle=light?`rgba(93,168,196,${alpha*.045})`:`rgba(177,225,255,${alpha*.055})`;ctx.beginPath();ctx.moveTo(b.cx,b.cy);ctx.lineTo(a.x,a.y);ctx.lineTo(b2.x,b2.y);ctx.closePath();ctx.fill();
      }
      ctx.lineJoin='miter';ctx.lineCap='butt';ctx.lineWidth=(.7+c.damage*.17)*unit;ctx.strokeStyle=light?`rgba(22,64,82,${alpha*.72})`:`rgba(171,222,248,${alpha*.82})`;ctx.stroke(path);
      ctx.save();ctx.translate(unit,-unit);ctx.lineWidth=.65*unit;ctx.strokeStyle=`rgba(255,255,255,${alpha*.8})`;ctx.stroke(path);ctx.restore();
    }
  }
  function glassImpact(b,e,age,p){
    if(age>.65)return;const fade=1-age/.65;ctx.fillStyle=isLight()?`rgba(60,135,162,${fade*.55})`:`rgba(217,244,255,${fade*.8})`;
    for(let i=0;i<8;i++){const a=i*TAU/8+e.seed,r=p*(.12+age*.65),x=b.cx+Math.cos(a)*r,y=b.cy+Math.sin(a)*r,s=p*.035*fade;ctx.beginPath();ctx.moveTo(x,y-s*2);ctx.lineTo(x+s,y+s);ctx.lineTo(x-s,y);ctx.closePath();ctx.fill();}
  }
  function background(now){
    if(settings.brightness===0){ctx.fillStyle='#000';ctx.fillRect(0,0,W,H);return;}
    const mode=settings.background,t=now*.00012;
    ctx.fillStyle=mode===0||mode===1?'#ffffff':mode===5?settings.backgroundColor:'#070e22';ctx.fillRect(0,0,W,H);
    if(mode===1||mode===2){
      for(let i=0;i<3;i++){const x=W*(.2+i*.3+.1*Math.sin(t+i*2)),y=H*(.45+.3*Math.cos(t*.7+i)),r=W*.45,g=ctx.createRadialGradient(x,y,0,x,y,r),c=mode===1?['#ffd9e888','#cee9ffbb','#d8ffe9aa'][i]:['#7543cc88','#00bba888','#095ab6aa'][i];g.addColorStop(0,c);g.addColorStop(1,c.slice(0,7)+'00');ctx.fillStyle=g;ctx.fillRect(0,0,W,H);}
    }
    if(mode===3){for(let i=0;i<130;i++){const x=((i*.61803398875)%1)*W,y=((i*i*.1747+now*.000007*(1+i%3))%1)*H,r=(.6+i%3*.5)*W/1920;ctx.fillStyle=`rgba(190,222,255,${.25+.55*(.5+.5*Math.sin(t*7+i))})`;ctx.beginPath();ctx.arc(x,y,r,0,TAU);ctx.fill();}}
    if(mode===4){const gap=28*W/1920,drift=(now*.008)%gap;ctx.strokeStyle='#243754';ctx.lineWidth=1;ctx.beginPath();for(let x=0;x<W;x+=gap){ctx.moveTo(x,0);ctx.lineTo(x,H);}for(let y=drift;y<H;y+=gap){ctx.moveTo(0,y);ctx.lineTo(W,y);}ctx.stroke();}
    if(settings.brightness<100){ctx.fillStyle=`rgba(0,0,0,${1-settings.brightness/100})`;ctx.fillRect(0,0,W,H);}
  }
  function gameScene(){
    const g=keyGames.state;
    if(g.mode===7){glassScene(g.cracks);return;}
    for(const target of g.targets){const k=byCode.get(target.code),b=box(k),p=Math.min(b.w,b.h),r=p*.23,x=b.cx,y=b.cy;
      glow(k,x,y,p*.65,.65);ctx.lineWidth=2*W/1920;
      if(g.mode===4){
        ctx.fillStyle=isLight()?'#25364b22':'#00000066';ctx.beginPath();ctx.ellipse(x,y+r*.65,r*1.35,r*.6,0,0,TAU);ctx.fill();
        const rise=Math.min(1,(g.time-target.start)/160,(target.end-g.time)/180),cy=y+r*(1-rise);
        ctx.fillStyle=color(k);ctx.beginPath();ctx.ellipse(x,cy,r,r*1.05,0,0,TAU);ctx.fill();
        for(const side of [-1,1]){ctx.beginPath();ctx.arc(x+side*r*.73,cy-r*.7,r*.38,0,TAU);ctx.fill();ctx.fillStyle='#ffffff';ctx.beginPath();ctx.arc(x+side*r*.35,cy-r*.08,r*.19,0,TAU);ctx.fill();ctx.fillStyle='#14243a';ctx.beginPath();ctx.arc(x+side*r*.35,cy-r*.05,r*.09,0,TAU);ctx.fill();ctx.fillStyle=color(k);}
        ctx.strokeStyle=color(k,.65);ctx.beginPath();ctx.arc(x,y,r*1.6,-Math.PI/2,-Math.PI/2+TAU*(target.end-g.time)/(target.end-target.start));ctx.stroke();
      }else{
        const f=(g.time-target.start)/(target.due-target.start),ballY=y-(1-f)*p*.9;
        ctx.strokeStyle=color(k,.22);ctx.beginPath();ctx.moveTo(x,y-p*.9);ctx.lineTo(x,y+p*.2);ctx.stroke();
        ctx.strokeStyle=color(k,.8);ctx.lineWidth=3*W/1920;ctx.beginPath();ctx.arc(x,y,r*.7,0,TAU);ctx.stroke();
        ctx.fillStyle=color(k);ctx.beginPath();ctx.arc(x,ballY,r*.42,0,TAU);ctx.fill();
        if(Math.abs(g.time-target.due)<=240){ctx.strokeStyle=color(k,.45);ctx.beginPath();ctx.arc(x,y,r,0,TAU);ctx.stroke();}
      }
    }
    for(const [code,level] of g.plants){const k=byCode.get(code),b=box(k),s=Math.min(b.w,b.h)/13,x=b.cx-s/2,base=b.cy+s*3.5,top=base-s*(2+level*.65);
      ctx.fillStyle=isLight()?'#27463e22':'#7bdfbc33';ctx.fillRect(x-s*3,base+s,7*s,s);
      ctx.fillStyle=isLight()?'#398869':'#7ee7a2';ctx.fillRect(x,top,s,base-top+s);
      for(let i=0;i<Math.min(level,5);i++){const side=i%2?1:-1,y=base-s*(1+i*.8);ctx.fillRect(x+side*s,y,s*2,s);ctx.fillRect(x+side*s*2,y-s,s,s);}
      if(level>=3){ctx.fillStyle=color(k);ctx.fillRect(x-s,top-s*2,s*3,s);ctx.fillRect(x-s*2,top-s,s*5,s*2);ctx.fillRect(x-s,top+s,s*3,s);ctx.fillStyle='#ffe49a';ctx.fillRect(x,top-s,s,s*2);}
      if(level===8){ctx.fillStyle=color(k,.65);ctx.fillRect(x-s*4,top-s*3,s*.6,s*.6);ctx.fillRect(x+s*4,top+s,s*.6,s*.6);}
    }
  }
  function grid(){ctx.lineWidth=1;for(const k of KEYS){const b=box(k);ctx.strokeStyle='rgba(60,82,100,.35)';ctx.strokeRect(b.x+3,b.y+3,b.w-6,b.h-6);ctx.strokeStyle='#008c78';ctx.beginPath();ctx.moveTo(b.cx-9,b.cy);ctx.lineTo(b.cx+9,b.cy);ctx.moveTo(b.cx,b.cy-9);ctx.lineTo(b.cx,b.cy+9);ctx.stroke();}}
  function draw(now){background(now);const light=isLight();
    if(settings.calibration)grid();
    if(settings.labels||settings.calibration){ctx.textAlign='center';ctx.textBaseline='middle';ctx.font=`600 ${20*W/1920}px 'Segoe UI','Microsoft YaHei',sans-serif`;ctx.fillStyle=light?'rgba(24,44,57,.53)':'rgba(230,245,255,.58)';for(const k of KEYS){const b=box(k);ctx.fillText(k.label,b.cx,b.cy-(settings.calibration?24*H/720:0));}}
    ctx.globalAlpha=settings.strength/100;
    if(settings.effect>=4)gameScene();
    for(const code of held.keys()){const k=byCode.get(code),b=box(k);if(settings.effect!==7)glow(k,b.cx,b.cy,Math.min(b.w,b.h)*.6,.65+.15*Math.sin(now*.004));}
    for(const e of events){const age=(now-e.start)/1000;if(age<0)continue;const fade=Math.pow(Math.max(0,1-age/(settings.duration/1000)),1.3),k=byCode.get(e.code),b=box(k),p=Math.min(b.h,b.w);if(settings.effect<4)renderers[settings.effect](k,b,e,age,fade,p);else if(settings.effect===7)glassImpact(b,e,age,p);else if(e.hit)particles(k,b,e,age,fade*(e.hit===2?1:.7),p*.65);else ripple(k,b,e,age,fade*.25,p*.35);}
    keyLights(now);ctx.globalAlpha=1;
  }
  // New input paints on the next display frame; continuing animation keeps the FPS limit.
  function frame(now){if(!inputPending&&now-lastFrame<1000/settings.fps-1){raf=requestAnimationFrame(frame);return;}keyGames.step(Math.min(100,now-lastFrame)*(settings.effect===4||settings.effect===5?settings.gameSpeed/100:1),settings.glassRepair);lastFrame=now;frameCount++;events=events.filter(e=>now-e.start<settings.duration);
    // Schedule from this frame so a paused wallpaper never catches up with a burst.
    for(const [code,next] of held){if(now>=next){trigger(code,now);held.set(code,now+settings.repeatInterval);}}
    inputPending=false;draw(now);
    const movingBackground=settings.brightness>0&&settings.background>=1&&settings.background<=4;
    const movingGame=settings.effect===4||settings.effect===5||(settings.effect===7&&keyGames.state.cracks.length>0);
    raf=events.length||held.size||movingBackground||movingGame?requestAnimationFrame(frame):0;
  }
  window.addEventListener('keydown',e=>{if(byCode.has(e.code)){e.preventDefault();key(e.code,true);}});window.addEventListener('keyup',e=>key(e.code,false));window.addEventListener('blur',releaseAll);
  document.addEventListener('visibilitychange',()=>{if(document.hidden){releaseAll();cancelAnimationFrame(raf);raf=0;}else wake();});
  canvas.addEventListener('pointerdown',e=>{if(e.button!==0)return;const k=KEYS.find(k=>{const b=box(k);return e.clientX>=b.x&&e.clientX<b.x+b.w&&e.clientY>=b.y&&e.clientY<b.y+b.h;});if(k)trigger(k.code);});
  window.livelyPropertyListener=(name,value)=>{
    wake();
    if(['labels','calibration'].includes(name)){settings[name]=value===true||value==='true';return;}
    if(name==='backgroundColor'){if(typeof value==='string'&&/^#[0-9a-f]{6}$/i.test(value))settings.backgroundColor=value;return;}
    if(name==='demo'){const now=performance.now();lastPress=null;if(settings.effect===4||settings.effect===5){keyGames.reset(settings.effect);keyGames.step(0);return;}['KeyQ','KeyW','KeyE','KeyR','KeyT','KeyY','KeyU','KeyI','KeyO','KeyP','Enter','Space'].forEach((code,i)=>{for(let j=0;j<(settings.effect===6?5:1);j++)trigger(code,now+i*100);});return;}
    if(name==='clear'){events=[];held.clear();lastPress=null;keyGames.reset(settings.effect);return;}
    const ranges={effect:[0,7],splashRange:[50,400],splashSpeed:[50,400],holdDelay:[0,1000],repeatInterval:[40,500],background:[0,5],brightness:[0,100],fps:[15,60],keyLight:[0,100],gameSpeed:[50,180],glassRepair:[3000,30000],strength:[10,100],duration:[600,6000],shiftX:[-200,200],shiftY:[-100,100],scaleX:[80,120],scaleY:[80,120]};
    if(!(name in ranges)||!Number.isFinite(Number(value)))return;settings[name]=clamp(Number(value),...ranges[name]);if(name==='background')settings.background=Math.round(settings.background);if(name==='effect'){settings.effect=Math.round(settings.effect);events=[];held.clear();lastPress=null;keyGames.reset(settings.effect);}
  };
  window.keyBloom={keys:KEYS,box,trigger,key,draw,get state(){return{viewport:{width:W,height:H},settings:{...settings},received,eventCount:events.length,active:[...held.keys()],frameCount,animating:!!raf,game:keyGames.state};}};
  resize();addEventListener('resize',resize);
})();
