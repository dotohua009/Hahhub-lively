async (page)=>{
  const test=await page.context().newPage(),errors=[],assert=(ok,msg)=>{if(!ok)throw Error(msg);};test.on('pageerror',e=>errors.push(e.message));
  try{
    await test.setViewportSize({width:1920,height:720});await test.goto(page.url());
    const set=values=>test.evaluate(values=>{for(const [k,v]of Object.entries(values))livelyPropertyListener(k,v);},values);
    const pixels=()=>test.evaluate(()=>{const ctx=document.querySelector('canvas').getContext('2d'),b=keyBloom.box(keyBloom.keys.find(k=>k.code==='KeyH'));return {center:[...ctx.getImageData(b.cx,b.cy,1,1).data],gap:[...ctx.getImageData(b.x+1,b.cy,1,1).data]};});
    await set({keyLight:0,category:0,animation:0});
    const gaps=[];for(let mode=1;mode<=4;mode++){
      await set({gapMode:mode});await test.waitForTimeout(90);const sample=await pixels();assert(sample.center.slice(0,3).some(v=>v>0),'default shaft center retains light');
      const colored=await test.evaluate(()=>{const d=document.querySelector('canvas').getContext('2d').getImageData(0,0,1920,720).data;let n=0;for(let i=0;i<d.length;i+=4)if(d[i]+d[i+1]+d[i+2]>70)n++;return n;});assert(colored>1000,'gap effect renders channels');gaps.push({mode,colored});
      await test.keyboard.press('h');await test.waitForTimeout(100);await set({clear:true});
    }
    await set({gapMode:0});await test.waitForTimeout(90);assert(!(await test.evaluate(()=>keyBloom.state.animating)),'turning gaps off returns to idle');
    await set({category:1,game:1});assert(await test.evaluate(()=>keyBloom.state.settings.effect===5),'game selection routes correctly');
    await set({category:0,animation:6});assert(await test.evaluate(()=>keyBloom.state.settings.effect===8),'letterpress selection routes correctly');
    await test.keyboard.press('h');await test.waitForTimeout(120);const stamp=await pixels();assert(stamp.center.slice(0,3).some(v=>v>10),'letterpress draws on struck key');
    await test.screenshot({path:'work/v13-letterpress.png'});
    await set({clear:true,animation:5});for(let i=0;i<4;i++)await test.keyboard.press('h');await test.waitForTimeout(150);
    const crack=await test.evaluate(()=>keyBloom.state.game.cracks[0]);assert(crack.strikes.length===4,'four strikes retained');assert(new Set(crack.strikes.map(s=>s.seed)).size===4,'each strike varies');
    await test.screenshot({path:'work/v13-glass.png'});
    await set({soundEnabled:true,soundVolume:10});await test.keyboard.press('a');await test.waitForTimeout(50);
    assert(await test.evaluate(()=>keyBloom.state.audio.state==='running'&&keyBloom.state.audio.voices>0),'sound plays on enabled input');
    await set({soundEnabled:false});await test.waitForTimeout(80);assert(await test.evaluate(()=>keyBloom.state.audio.voices===0&&keyBloom.state.audio.state!=='running'),'mute stops active audio');
    await set({clear:true,gapMode:3});await test.waitForTimeout(100);await test.screenshot({path:'work/v13-lava.png'});
    await set({gapMode:0});assert(errors.length===0,errors.join(';'));return{pass:true,gaps,errors};
  }finally{await test.close();}
}
