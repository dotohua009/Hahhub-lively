async (page) => {
  const test=await page.context().newPage(),errors=[],assert=(ok,msg)=>{if(!ok)throw Error(msg);};
  test.on('pageerror',e=>errors.push(e.message));
  try {
    await test.setViewportSize({width:1920,height:720});
    await test.goto(page.url());
    await test.waitForTimeout(150);
    let s=await test.evaluate(()=>keyBloom.state);assert(!s.animating&&s.settings.fps===30,'idle stops animation');
    const frames=s.frameCount;await test.waitForTimeout(250);assert((await test.evaluate(()=>keyBloom.state.frameCount))===frames,'idle draws zero frames');
    const white=await test.evaluate(()=>[...document.querySelector('canvas').getContext('2d').getImageData(1000,200,1,1).data]);
    assert(white.every(x=>x===255),'white default retained');
    await test.evaluate(()=>{livelyPropertyListener('brightness',0);livelyPropertyListener('effect',2);});
    await test.waitForTimeout(100);await test.keyboard.down('a');await test.waitForTimeout(120);
    const lit=await test.evaluate(()=>{const b=keyBloom.box(keyBloom.keys.find(k=>k.code==='KeyA')),ctx=document.querySelector('canvas').getContext('2d');return {center:[...ctx.getImageData(Math.round(b.cx),Math.round(b.cy),1,1).data],corner:[...ctx.getImageData(1500,50,1,1).data]};});
    assert(lit.center.slice(0,3).every(x=>x>140),'key center brightens');assert(lit.corner.slice(0,3).every(x=>x===0),'dark background stays black');
    await test.keyboard.up('a');await test.waitForTimeout(2900);assert(!(await test.evaluate(()=>keyBloom.state.animating)),'sleeps after tap fades');
    await test.evaluate(()=>livelyPropertyListener('keyLight',0));await test.keyboard.down('a');await test.waitForTimeout(100);
    const dim=await test.evaluate(()=>{const b=keyBloom.box(keyBloom.keys.find(k=>k.code==='KeyA'));return [...document.querySelector('canvas').getContext('2d').getImageData(Math.round(b.cx),Math.round(b.cy),1,1).data];});
    assert(dim[0]<lit.center[0],'lighting slider disables added light');await test.keyboard.up('a');
    await test.evaluate(()=>{livelyPropertyListener('clear',true);livelyPropertyListener('brightness',100);livelyPropertyListener('background',3);});
    await test.waitForTimeout(150);assert((await test.evaluate(()=>keyBloom.state.animating)),'moving background stays animated');
    await test.evaluate(()=>livelyPropertyListener('brightness',0));await test.waitForTimeout(150);assert(!(await test.evaluate(()=>keyBloom.state.animating)),'invisible background stops animating');
    await test.evaluate(()=>{livelyPropertyListener('fps',999);livelyPropertyListener('keyLight',-1);});
    s=await test.evaluate(()=>keyBloom.state);assert(s.settings.fps===60&&s.settings.keyLight===0,'settings bounded');
    assert(errors.length===0,errors.join(';'));return{pass:true,lit,dim,errors};
  } finally {await test.close();}
}
