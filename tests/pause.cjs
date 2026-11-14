async(page)=>{
  const assert=(value,message)=>{if(!value)throw Error(message);};
  await page.evaluate(()=>{livelyPropertyListener('effect',7);livelyPropertyListener('clear',true);keyBloom.key('KeyA',true);advance(500);});
  assert((await page.evaluate(()=>keyBloom.state)).active.length===1,'held key exists before pause');
  await page.evaluate(()=>livelyWallpaperPlaybackChanged('{"IsPaused":true}'));
  let s=await page.evaluate(()=>keyBloom.state);assert(s.paused&&!s.animating&&!s.active.length&&!s.eventCount&&!s.game.cracks.length,'host pause clears stale held state and last effect');
  const count=s.received;await page.evaluate(()=>{keyBloom.key('KeyA',true);advance(30000);});
  assert((await page.evaluate(()=>keyBloom.state.received))===count,'paused wallpaper rejects new held input');
  await page.evaluate(()=>livelyWallpaperPlaybackChanged({IsPaused:false}));
  await page.evaluate(()=>advance(500));s=await page.evaluate(()=>keyBloom.state);
  assert(!s.paused&&!s.active.length&&s.received===count,'resume never repeats lost keyup');
  await page.evaluate(()=>{keyBloom.key('KeyA',true);advance(500);keyBloom.key('KeyA',false);});
  assert((await page.evaluate(()=>keyBloom.state.received))>count,'fresh input works after resume');
  await page.evaluate(()=>{livelyWallpaperPlaybackChanged('broken');livelyWallpaperPlaybackChanged({IsPaused:'false'});});
  assert(!(await page.evaluate(()=>keyBloom.state.paused)),'malformed host messages ignored');
  await page.evaluate(()=>livelyPropertyListener('clear',true));
  return {pass:true,checks:'pause cancels animation, clears held keys and fractures, blocks paused input, resume clears lost keyup, fresh input resumes, malformed callback ignored'};
}
