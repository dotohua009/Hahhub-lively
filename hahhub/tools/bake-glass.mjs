// Re-bake with Node 22.15+ and an unpacked cracked-glass@0.4.0 npm release.
import fs from 'node:fs';
import { registerHooks } from 'node:module';
import { pathToFileURL } from 'node:url';
import path from 'node:path';
registerHooks({resolve(id,ctx,next){
  if(id.startsWith('.')&&!path.extname(id))id+='.js';
  return next(id,ctx);
}});
const root=process.argv[2];
if(!root)throw Error('Pass the cracked-glass 0.4.0 package directory');
if(JSON.parse(fs.readFileSync(path.join(root,'package.json'))).version!=='0.4.0')throw Error('Expected version 0.4.0');
const {generateFracture}=await import(pathToFileURL(path.resolve(root,'dist/fracture/index.js')));
const dest=new URL('../effects/hahhan-glass/glass-data.js',import.meta.url);
const data=JSON.parse(fs.readFileSync(dest,'utf8').trim().slice('const GLASS = '.length,-1));
const round=x=>Math.round(x*10000)/10000;
function bake(seed,large=false){
  const pattern=generateFracture({mode:'radial',width:512,height:512,seed,impactHole:large?.16:.30,micro:false,
    rays:{count:large?5+seed%2:7+seed%3,angleJitter:1,waviness:large?.43:.70,doubling:!large,doublingStartRing:3},
    rings:{count:large?4:8,spacing:'geometric',jitter:1,partial:.93},jaggedness:large?.28:.68,deviation:.84,stubs:{density:large?.12:.80}});
  const segments=[],facets=[],network=[];
  const dist=(a,b)=>Math.hypot(b[0]-a[0],b[1]-a[1]);
  const noise=n=>{const x=Math.sin(n*127.1+seed*311.7)*43758.5453;return x-Math.floor(x);};
  // Warp the entire mesh together, so all intersecting edges still share their vertices.
  function warp(px,py){
    const x=(px-256)/110,y=(py-256)/110,r=Math.hypot(x,y),a=Math.atan2(y,x);
    const radius=r*(.95+.16*Math.sin(3*a+seed)+.10*Math.sin(5*a-seed));
    const angle=a+.14*Math.sin(2*a+seed+r*1.4);
    return [radius*Math.cos(angle),radius*Math.sin(angle)*.89];
  }
  const coords=flat=>Array.from({length:flat.length/2},(_,i)=>warp(flat[2*i],flat[2*i+1]));
  const token=p=>p.map(round).join(',');
  const add=(a,b,at,end,style,rank)=>{
    if(dist(a,b)>.0001)segments.push([at,end,...a,...b,style,rank].map(round));
  };
  function walk(points,start,cost,style,rank,register=false){
    let at=start;
    for(let j=1;j<points.length;j++){
      const a=points[j-1],b=points[j],len=dist(a,b),end=at+cost*len;
      add(a,b,at,end,style,rank);
      if(register&&len>.0001)network.push({a,b,at,end,rank});
      at=end;
    }
    return at;
  }
  function nearest(p,maxRank=100){
    let best={point:[0,0],time:0,d:Math.hypot(...p),rank:0};
    for(const e of network){
      if(e.rank>maxRank)continue;
      const dx=e.b[0]-e.a[0],dy=e.b[1]-e.a[1],f=Math.max(0,Math.min(1,((p[0]-e.a[0])*dx+(p[1]-e.a[1])*dy)/(dx*dx+dy*dy)));
      const q=[e.a[0]+dx*f,e.a[1]+dy*f],d=dist(q,p);
      if(d<best.d)best={point:q,d,time:e.at+(e.end-e.at)*f,rank:e.rank};
    }
    return best;
  }
  const base=pattern.cracks.map((c,i)=>({kind:c.kind,i,pts:coords(c.points)}));
  const rays=base.filter(c=>c.kind==='ray').sort((a,b)=>Math.hypot(...a.pts[0])-Math.hypot(...b.pts[0]));
  const junctions=new Set(rays.flatMap(c=>c.pts.map(token)));
  function simplify(pts){
    return pts.filter((p,i)=>i===0||i===pts.length-1||junctions.has(token(p))||dist(pts[i-1],p)>.016||i%3===0);
  }
  for(const c of rays){
    const pts=simplify(c.pts),first=pts[0],root=nearest(first,0);
    const cost=.66+1.39*noise(c.i*2),rank=0;
    const start=walk([root.point,first],root.time,.98,1,rank,true);
    let at=walk(pts,start,cost,noise(c.i+19)<.28?1:0,rank,true);
    for(let f=0;f<(large?(c.i%3===0?1:0):3);f++){
      const j=Math.min(pts.length-2,Math.max(1,Math.floor((.08+.36*noise(c.i+f*31))*pts.length)));
      const origin=pts[j],parent=nearest(origin,0),v=pts[j+1];
      let angle=Math.atan2(v[1]-origin[1],v[0]-origin[0])+(noise(f+c.i)>.5?1:-1)*(.36+.44*noise(21*f+c.i));
      let p=origin,time=parent.time;
      for(let n=0;n<4;n++){
        angle+=(noise(c.i*3+21*f+n)-.5)*.48;
        const size=.04+.07*noise(53*c.i+f*8+n),q=[p[0]+size*Math.cos(angle),p[1]+size*Math.sin(angle)];
        add(p,q,time,time+size*1.6,2,30+Math.round(noise(f+c.i)*35));time+=size*1.6;p=q;
      }
    }
    if(c.i%3!==0){
      let a=pts.at(-1),angle=Math.atan2(a[1],a[0]);
      for(let j=0;j<24;j++){
        const bend=angle+.16*Math.sin(seed+c.i*2.6+j*1.77),len=.58+.16*Math.cos(c.i+j);
        const b=[a[0]+len*Math.cos(bend),a[1]+len*Math.sin(bend)];
        add(a,b,at,at+len*cost,2,20);at+=len*cost;a=b;
      }
    }
  }
  // Each arc grows from a parent-ray junction, not by revealing a whole ring at once.
  for(const c of base.filter(c=>c.kind==='ring'||c.kind==='crush')){
    const pts=simplify(c.pts),crush=c.kind==='crush';let start=0;
    for(let j=1;j<pts.length;j++){
      if(j!==pts.length-1&&!junctions.has(token(pts[j])))continue;
      let run=pts.slice(start,j+1);start=j;
      const rank=crush?0:Math.round(12+70*noise(j+c.i*71));
      let root=nearest(run[0],0);const far=nearest(run.at(-1),0);
      if(far.time+far.d<root.time+root.d){run.reverse();root=far;}
      walk([root.point,...run],root.time,crush?.78:1.16,crush?0:1,rank,true);
    }
  }
  for(const c of base.filter(c=>c.kind==='stub')){
    const pts=simplify(c.pts),root=nearest(pts[0],55);
    walk([root.point,...pts],root.time,1.24,2,Math.max(root.rank,40+(c.i*23%60)));
  }
  for(const shard of pattern.shards){
    const pts=coords(shard.polygon),reach=Math.max(...pts.map(p=>Math.hypot(...p)));
    if(reach<(large?3.1:1.65)&&pts.length>2&&(large||(shard.hash>>>0)%3!==2)){
      const arrival=pts.map(p=>nearest(p).time),at=Math.max(...arrival);
      facets.push({enter:round(Math.min(...arrival)),at:round(at+.01),tone:round(noise(shard.hash%9999)*2-1),points:pts.map(p=>p.map(round))});
    }
  }
  const core=rays.filter(c=>Math.hypot(...c.pts[0])<.14).map(c=>c.pts[0]).sort((a,b)=>Math.atan2(a[1],a[0])-Math.atan2(b[1],b[0]));
  core.forEach((p,i)=>{
    const q=core[(i+1)%core.length],chip=[p[0]*.33+q[0]*.22,p[1]*.33+q[1]*.22];
    walk(large?[[0,0],p]:[[0,0],chip,p],0,.65,1,0);
    facets.push({at:.035,tone:i%3===0?1:-.65,points:[[0,0],chip,p,q].map(p=>p.map(round))});
  });
  return {segments:segments.sort((a,b)=>a[0]-b[0]),facets};
}
data.shapes=Array.from({length:8},(_,i)=>bake(19+41*i));
data.largeShapes=Array.from({length:8},(_,i)=>bake(19+41*i,true));
fs.writeFileSync(dest,'const GLASS = '+JSON.stringify(data)+';\n');

