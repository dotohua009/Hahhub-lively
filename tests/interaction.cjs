async (page) => {
  const errors=[];page.on('pageerror',e=>errors.push(e.message));
  const assert=(value,message)=>{if(!value)throw Error(message);};
  await page.setViewportSize({width:1920,height:720});
  await page.addInitScript(()=>{
    let now=0,seq=0,frames=new Map();
    performance.now=()=>now;
    requestAnimationFrame=callback=>{frames.set(++seq,callback);return seq;};
    cancelAnimationFrame=id=>frames.delete(id);
    const frame=()=>{const pending=[...frames.values()];frames.clear();for(const callback of pending)callback(now);};
    window.advance=(ms,jump=false)=>{if(jump){now+=ms;frame();return;}while(ms>0){const dt=Math.min(20,ms);now+=dt;ms-=dt;frame();}};
  });
  await page.reload();
  const state=()=>page.evaluate(()=>keyBloom.state);
  const tick=ms=>page.evaluate(ms=>advance(ms),ms);
  const mode=m=>page.evaluate(m=>livelyPropertyListener('effect',m),m);
  await mode(2);await page.evaluate(()=>{livelyPropertyListener('fps',15);livelyPropertyListener('brightness',0);livelyPropertyListener('keyLight',0);});await tick(1000);
  const idleFrames=(await state()).frameCount;await page.keyboard.down('h');await tick(16);
  assert((await state()).frameCount===idleFrames+1,'first input paints on next frame even at 15 FPS');await page.keyboard.up('h');
  const radius=()=>page.evaluate(()=>{const ctx=document.querySelector('canvas').getContext('2d'),b=keyBloom.box(keyBloom.keys.find(k=>k.code==='KeyH')),data=ctx.getImageData(0,0,innerWidth,innerHeight).data;let r=0;for(let y=0;y<innerHeight;y++)for(let x=0;x<innerWidth;x++){const i=(y*innerWidth+x)*4;if(Math.max(data[i],data[i+1],data[i+2])>40)r=Math.max(r,Math.hypot(x-b.cx,y-b.cy));}return r/Math.min(b.w,b.h);});
  await tick(84);const earlyRadius=await radius();assert(earlyRadius>.85,'splash reaches neighboring key within 100 ms');
  await tick(500);const wideRadius=await radius();assert(wideRadius>2.3,'splash covers multiple key widths');
  const movingFrames=(await state()).frameCount;await page.keyboard.press('j');await tick(16);
  assert((await state()).frameCount===movingFrames+1,'new input also bypasses limit during animation');
  const inputFrames=(await state()).frameCount;await tick(500);assert((await state()).frameCount-inputFrames<=8,'continuing animation still honors 15 FPS');
  await page.evaluate(()=>{livelyPropertyListener('fps',30);livelyPropertyListener('brightness',100);livelyPropertyListener('keyLight',85);});
  const counts=[];
  for(let m=0;m<8;m++){
    await mode(m);const before=(await state()).received;
    await page.keyboard.down('a');assert((await state()).received===before+1,`mode ${m}: tap immediate`);
    await tick(100);await page.keyboard.up('a');await tick(600);
    assert((await state()).received===before+1,`mode ${m}: tap single effect`);
    await page.keyboard.down('a');await page.keyboard.down('a');await tick(140);
    assert((await state()).received===before+2,`mode ${m}: repeat keydown deduplicated`);
    await tick(100);assert((await state()).received===before+3,`mode ${m}: hold starts within 240 ms`);
    await tick(600);let s=await state();assert(s.received===before+8,`mode ${m}: hold repeats every 120 ms`);
    await page.keyboard.up('a');await tick(1000);
    assert((await state()).received===s.received,`mode ${m}: release stops repeats`);
    counts.push({effect:m,events:s.received-before});
  }
  await mode(1);await page.keyboard.down('a');await page.keyboard.down('s');await tick(1000);
  assert((await state()).active.length===2,'multi-key hold');
  await page.keyboard.up('a');let before=(await state()).received;await tick(840);
  assert((await state()).received-before===7,'released key stops while other key repeats');await page.keyboard.up('s');
  await page.keyboard.down('Shift');await tick(16000);
  assert((await state()).active.includes('ShiftLeft'),'non-repeating modifier holds beyond 15 seconds');await page.keyboard.up('Shift');
  await page.keyboard.down('a');before=(await state()).received;
  await page.evaluate(()=>advance(60000,true));assert((await state()).received===before+1,'no catch-up burst after pause');
  await page.evaluate(()=>dispatchEvent(new Event('blur')));before=(await state()).received;await tick(1000);
  assert((await state()).received===before&&(await state()).active.length===0,'blur stops hold');await page.keyboard.up('a');
  await page.keyboard.down('a');await page.evaluate(()=>{Object.defineProperty(document,'hidden',{value:true,configurable:true});document.dispatchEvent(new Event('visibilitychange'));});
  before=(await state()).received;await tick(1000);assert((await state()).received===before,'hidden page stops hold');
  await page.evaluate(()=>delete document.hidden);await page.keyboard.up('a');
  for(const action of ['clear','effect']){
    await page.keyboard.down('a');await page.evaluate(action=>livelyPropertyListener(action,action==='effect'?1:true),action);
    before=(await state()).received;await tick(1000);assert((await state()).received===before&&(await state()).active.length===0,`${action} stops hold`);await page.keyboard.up('a');
  }
  await mode(6);await page.keyboard.down('a');await tick(5000);
  assert((await state()).game.plants[0][1]===8,'hold grows plant to capped level');await page.keyboard.up('a');
  await mode(7);await page.evaluate(()=>livelyPropertyListener('glassRepair',3000));
  await page.keyboard.down('a');await tick(10000);
  let s=await state();assert(s.game.cracks[0].damage===8,'hold deepens fracture and prevents healing');const seed=s.game.cracks[0].seed;
  await page.keyboard.up('a');s=await state();assert(s.game.cracks[0].lastHit===s.game.time,'repair clock starts at release');
  await tick(1580);assert((await state()).game.cracks[0].damage===8,'repair waits 1.6 seconds after release');
  await tick(1020);s=await state();assert(s.game.cracks[0].damage<8&&s.game.cracks[0].damage>0,'repair progresses');
  const damage=s.game.cracks[0].damage;await page.keyboard.press('a');s=await state();
  assert(s.game.cracks[0].seed===seed&&Math.abs(s.game.cracks[0].damage-damage-1)<1e-9,'tap adds to same fracture after holding');
  await tick(4700);assert((await state()).game.cracks.length===0,'released glass fully repairs');
  for(const m of [4,5]){
    await mode(m);await tick(40);const code=(await state()).game.targets[0].code;
    await page.keyboard.down(code);await tick(1900);assert((await state()).game.hits>=1,`mode ${m}: held key hits target`);await page.keyboard.up(code);
  }
  await mode(0);await page.evaluate(()=>{for(const k of keyBloom.keys)keyBloom.key(k.code,true);advance(3000);});
  assert((await state()).eventCount<=96,'simultaneous holds preserve event cap');
  await page.evaluate(()=>dispatchEvent(new Event('blur')));
  assert(errors.length===0,errors.join(';'));
  return {pass:true,counts,earlyRadius,wideRadius,checks:'next-frame input, wide splash, FPS limit, tap, hold, OS repeat, release, multi-key, 16s modifier, pause, blur, hidden, clear, mode switch, game hits, plant cap, glass damage and repair, event cap',errors};
}
