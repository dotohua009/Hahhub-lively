'use strict';
const keySurfaces = (() => {
  const TAU=Math.PI*2;
  function random(seed){let n=seed>>>0;return()=>{n+=0x6D2B79F5;let t=n;t=Math.imul(t^(t>>>15),t|1);t^=t+Math.imul(t^(t>>>7),t|61);return((t^(t>>>14))>>>0)/4294967296;};}
  // Geometry is made once per strike, not regenerated while drawing or healing.
  function fracture(seed,level){
    const rnd=random(seed),lines=[],facets=[],rays=[],arms=5+Math.floor(rnd()*5),ox=(rnd()-.5)*.16,oy=(rnd()-.5)*.16;
    for(let i=0;i<arms;i++){
      const angle=i*TAU/arms+(rnd()-.5)*.65,reach=.3+level*.14+rnd()*.45,ray=[[ox,oy]];
      for(let j=1;j<=7;j++){const a=angle+(rnd()-.5)*.24,r=reach*j/7;ray.push([ox+Math.cos(a)*r,oy+Math.sin(a)*r]);}
      rays.push(ray);lines.push(ray);
      for(let j=2;j<7;j++)if(rnd()>.42){const p=ray[j],a=angle+(rnd()>.5?1:-1)*(.35+rnd()*.7),r=(.05+rnd()*.18)*(1+level*.05);lines.push([p,[p[0]+Math.cos(a)*r*.5,p[1]+Math.sin(a)*r*.5],[p[0]+Math.cos(a+.18)*r,p[1]+Math.sin(a+.18)*r]]);}
    }
    for(let i=0;i<arms;i++)for(let j=1;j<5;j++)if(rnd()>.38){const a=rays[i][j],b=rays[(i+1)%arms][Math.max(1,j+(rnd()>.7?1:0))],m=[(a[0]+b[0])*.5+(rnd()-.5)*.06,(a[1]+b[1])*.5+(rnd()-.5)*.06];lines.push([a,m,b]);if(j<3&&rnd()>.4)facets.push([[ox,oy],a,m,b]);}
    return {seed,lines,facets};
  }
  function glass(ctx,cracks,box,byCode,light,unit){
    for(const c of cracks){const b=box(byCode.get(c.code)),p=Math.min(b.w,b.h),alpha=Math.min(1,c.damage),heal=c.damage/c.peak;
      ctx.save();ctx.translate(b.cx,b.cy);ctx.scale(p,p);
      for(const [index,hit] of c.strikes.entries()){
        const a=alpha*(.65+.35*(index+1)/c.strikes.length),path=new Path2D();
        for(const line of hit.lines){const end=Math.max(1,(line.length-1)*heal);path.moveTo(...line[0]);for(let i=1;i<=Math.floor(end);i++)path.lineTo(...line[i]);const f=end%1,i=Math.floor(end);if(f&&line[i+1])path.lineTo(line[i][0]+(line[i+1][0]-line[i][0])*f,line[i][1]+(line[i+1][1]-line[i][1])*f);}
        for(const facet of hit.facets){ctx.beginPath();ctx.moveTo(...facet[0]);for(const v of facet.slice(1))ctx.lineTo(...v);ctx.closePath();ctx.fillStyle=light?`rgba(60,135,160,${a*.035})`:`rgba(174,219,242,${a*.045})`;ctx.fill();}
        ctx.lineJoin='bevel';ctx.lineCap='butt';ctx.lineWidth=(1.5+c.damage*.07)*unit/p;ctx.strokeStyle=`rgba(0,12,24,${a*.7})`;ctx.stroke(path);
        ctx.translate(.8*unit/p,-.65*unit/p);ctx.lineWidth=.6*unit/p;ctx.strokeStyle=`rgba(225,247,255,${a*.94})`;ctx.stroke(path);ctx.translate(-.8*unit/p,.65*unit/p);
      }
      ctx.restore();
    }
  }
  let gapMask=null,maskSignature='',maze=[],mazeSeed=(Math.random()*0xffffffff)>>>0;
  function gaps(ctx,keys,box,W,H,now,settings,events){
    if(!settings.gapMode||!settings.gapBrightness)return;
    const boxes=keys.map(box),width=settings.gapWidth*W/1920,signature=[W,H,width,...boxes.flatMap(b=>[b.x,b.y,b.w,b.h])].join(',');
    if(signature!==maskSignature){maskSignature=signature;gapMask=new Path2D();gapMask.rect(0,0,W,H);for(const b of boxes)gapMask.rect(b.x+width/2,b.y+width/2,Math.max(1,b.w-width),Math.max(1,b.h-width));maze=[];const rnd=random(mazeSeed);for(const b of boxes)for(const side of [0,1])if(rnd()>.27)maze.push(side?[b.x,b.y,b.x+b.w,b.y]:[b.x,b.y,b.x,b.y+b.h]);}
    ctx.save();ctx.clip(gapMask,'evenodd');ctx.globalAlpha*=settings.gapBrightness/100;
    const t=now*.001*settings.gapSpeed/100,mode=settings.gapMode;
    ctx.fillStyle=mode===3?'#160200':mode===2?'#001a28':'#030714';ctx.fillRect(0,0,W,H);
    if(mode===4){ctx.lineCap='round';for(let i=0;i<maze.length;i++){const [x,y,x2,y2]=maze[i],f=(t*.65+i*.173)%1;ctx.lineWidth=width*.7;ctx.strokeStyle=`hsla(${(i*19+t*55)%360},95%,65%,.7)`;ctx.beginPath();ctx.moveTo(x,y);ctx.lineTo(x2,y2);ctx.stroke();ctx.fillStyle='#eaffff';ctx.beginPath();ctx.arc(x+(x2-x)*f,y+(y2-y)*f,width*.3,0,TAU);ctx.fill();}}
    else {const g=ctx.createLinearGradient(-W*.4+Math.sin(t*.3)*W*.2,0,W,H);for(let i=0;i<=12;i++){const f=i/12,h=mode===1?(i*42+t*75)%360:mode===2?188+Math.sin(i+t)*22:18+Math.sin(i+t)*14;g.addColorStop(f,`hsl(${h},100%,${mode===3?46+14*Math.sin(i-t):48+18*Math.sin(i-t)}%)`);}ctx.fillStyle=g;ctx.fillRect(0,0,W,H);
      ctx.lineWidth=width*.15;for(let i=0;i<24;i++){ctx.strokeStyle=mode===3?'#ffcf6580':'#e5ffff80';ctx.beginPath();const y=(i/24*H+t*(mode===2?35:18))%H;for(let x=0;x<=W;x+=24){const yy=y+Math.sin(x/120+t*2+i)*width*.7;if(x===0)ctx.moveTo(x,yy);else ctx.lineTo(x,yy);}ctx.stroke();}}
    if(mode!==4){ctx.lineCap='round';for(let i=0;i<boxes.length;i++){
      const b=boxes[i],perimeter=2*(b.w+b.h),start=(t*(mode===3?42:85)+i*47+mazeSeed%113)%perimeter;
      const point=d=>{d=(d+perimeter)%perimeter;if(d<b.w)return[b.x+d,b.y];d-=b.w;if(d<b.h)return[b.x+b.w,b.y+d];d-=b.h;if(d<b.w)return[b.x+b.w-d,b.y+b.h];return[b.x,b.y+b.h-(d-b.w)];};
      ctx.strokeStyle=mode===3?'#ffd16c':'#d9ffff';ctx.lineWidth=width*.32;ctx.beginPath();for(let j=0;j<8;j++){const p=point(start+j*4);if(j===0)ctx.moveTo(...p);else ctx.lineTo(...p);}ctx.stroke();
      const tip=point(start+28);ctx.fillStyle=mode===3?'#fff1bf':'#ffffff';ctx.beginPath();ctx.arc(...tip,width*.19,0,TAU);ctx.fill();
    }}
    // A key strike pushes a bright wave through the surrounding channels.
    for(const e of events){const age=(now-e.start)/1000;if(age<0||age>1.2)continue;const b=box(keys.find(k=>k.code===e.code));ctx.strokeStyle=`rgba(255,255,255,${(1-age/1.2)*.9})`;ctx.lineWidth=width*.7;ctx.beginPath();ctx.arc(b.cx,b.cy,age*b.h*(3.5+e.seed*.16),0,TAU);ctx.stroke();}
    ctx.restore();
  }
  function letterpress(ctx,k,b,e,age,fade,light){const rnd=random(Math.floor(e.seed*1e8)),p=Math.min(b.w,b.h),rise=Math.exp(-age*15)*p*.12;ctx.save();ctx.translate(b.cx,b.cy+rise);ctx.rotate((rnd()-.5)*.06);const w=Math.min(b.w*.8,p*1.4),h=p*.76;
    ctx.fillStyle=light?'#15323d':'#b8dfe6';ctx.globalAlpha*=fade;ctx.fillRect(-w/2+3,-h/2+5,w,h);ctx.fillStyle=light?'#f3ecdb':'#192c32';ctx.fillRect(-w/2,-h/2,w,h);ctx.strokeStyle=light?'#8c8067':'#7197a0';ctx.lineWidth=1;ctx.strokeRect(-w/2,-h/2,w,h);
    const text=k.label==='Space'?'印':k.label;ctx.textAlign='center';ctx.textBaseline='middle';ctx.font=`bold ${p*(text.length>3?.22:.43)}px Georgia, 'Microsoft YaHei', serif`;ctx.fillStyle=light?'#16282e':'#e1faf8';ctx.fillText(text,0,1,w*.88);
    ctx.globalAlpha*=.4;for(let i=0;i<18;i++){ctx.fillStyle=light?'#fff5df':'#14242b';ctx.fillRect((rnd()-.5)*w,(rnd()-.5)*h,.6+rnd()*2,.8+rnd()*2);}ctx.restore();}
  return {fracture,glass,gaps,letterpress};
})();
if(typeof module!=='undefined')module.exports=keySurfaces;
