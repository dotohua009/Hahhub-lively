'use strict';
const keyGames = (() => {
  const letters = KEYS.filter(k => /^Key[A-Z]$/.test(k.code)).map(k => k.code);
  const lanes = ['KeyA','KeyS','KeyD','KeyF','KeyJ','KeyK','KeyL'];
  let mode=0, time=0, next=0, targets=[], plants=new Map(), cracks=new Map(), hits=0, misses=0, combo=0;
  function reset(value=mode) {mode=value;time=0;next=0;targets=[];plants.clear();cracks.clear();hits=0;misses=0;combo=0;}
  function step(delta, repairMs=12000) {
    if (!Number.isFinite(delta)||delta<0) return;
    // A 15 FPS frame at 180% game speed can exceed 100 ms; still bound resume jumps.
    time+=Math.min(delta,200);
    if(mode===7){
      const duration=Number.isFinite(repairMs)?Math.max(3000,Math.min(30000,repairMs)):12000;
      for(const [code,c] of cracks){c.damage=c.peak*Math.max(0,1-Math.max(0,time-c.lastHit-1600)/duration);if(c.damage<=0)cracks.delete(code);}
      return;
    }
    if (mode!==4&&mode!==5) return;
    targets=targets.filter(t=>{if(time<=t.end)return true;misses++;combo=0;return false;});
    if(time<next)return;
    next=time+(mode===4?720:620);
    const available=(mode===4?letters:lanes).filter(code=>!targets.some(t=>t.code===code));
    if(!available.length||targets.length>=(mode===4?3:4))return;
    targets.push({code:available[Math.floor(Math.random()*available.length)],start:time,due:time+1600,end:time+(mode===4?2400:1840)});
  }
  function press(code) {
    if(!KEYS.some(k=>k.code===code))return 0;
    if(mode===6){plants.set(code,Math.min(8,(plants.get(code)||0)+1));hits++;return 1;}
    if(mode===7){const c=cracks.get(code)||{code,damage:0,seed:Math.random()*Math.PI*2};c.damage=Math.min(8,c.damage+1);c.peak=c.damage;c.lastHit=time;cracks.set(code,c);hits++;return 1;}
    if(mode!==4&&mode!==5)return 0;
    const i=targets.findIndex(t=>t.code===code&&time<=t.end&&(mode===4||Math.abs(time-t.due)<=240));
    if(i<0){combo=0;return 0;}
    const perfect=mode===5&&Math.abs(time-targets[i].due)<=90;
    targets.splice(i,1);hits++;combo++;return perfect?2:1;
  }
  function release(code) {const c=cracks.get(code);if(c)c.lastHit=time;}
  return {reset,step,press,release,get state(){return {mode,time,hits,misses,combo,targets:targets.map(t=>({...t})),plants:[...plants],cracks:[...cracks.values()].map(c=>({...c}))};}};
})();
if(typeof module!=='undefined')module.exports=keyGames;
