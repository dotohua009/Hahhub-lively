async (page)=>{
  const test=await page.context().newPage();
  try{
    await test.setViewportSize({width:1920,height:720});await test.goto(page.url());
    const result=await test.evaluate(()=>{
      const assert=(ok,msg)=>{if(!ok)throw Error(msg);},c=document.createElement('canvas');c.width=1920;c.height=720;const ctx=c.getContext('2d');
      const settings={...keyBloom.state.settings,gapMode:1,gapBrightness:100,axisSize:72,axisColor:'#000000',axisTransparency:0,axisRadius:12,axisFeather:8};
      const draw=changes=>{Object.assign(settings,changes);ctx.fillStyle='#000000';ctx.fillRect(0,0,c.width,c.height);keySurfaces.gaps(ctx,keyBloom.keys,keyBloom.box,c.width,c.height,1000,settings,[]);};
      const sample=(b,dx=0,dy=0)=>[...ctx.getImageData(b.cx+dx,b.cy+dy,1,1).data].slice(0,3),black=a=>a.every(x=>x===0);
      const boxes=['KeyA','Space','Enter','ShiftLeft'].map(code=>keyBloom.box(keyBloom.keys.find(k=>k.code===code)));
      draw({});
      for(const b of boxes){assert(black(sample(b)),'opaque shaft at calibrated center');assert(black(sample(b,20)),'same inner size on every key');assert(!black(sample(b,40)),'long keys have no wider shaft hole');assert(!black(sample(b,35,35)),'rounded corners expose flow');}
      const b=boxes[0],full=[];draw({axisTransparency:100});for(const x of [0,20,30,34,40])full.push(sample(b,x));
      draw({axisTransparency:0});const ratios=[0,20,30,34,40].map((x,i)=>sample(b,x).reduce((a,v)=>a+v,0)/full[i].reduce((a,v)=>a+v,0));
      assert(ratios[0]===0&&ratios[1]===0&&ratios[2]>0&&ratios[3]>ratios[2]&&ratios[4]===1,'feather fades inside fixed boundary');
      draw({axisColor:'#804020',axisTransparency:0});assert(sample(b).join(',')==='128,64,32','custom shaft color');
      draw({axisTransparency:50});const mixed=sample(b);assert(mixed.every((x,i)=>Math.abs(x-(full[0][i]+[128,64,32][i])/2)<2),'transparency blends with flow');
      draw({axisColor:'#000000',axisTransparency:0,axisSize:100,axisRadius:0,axisFeather:0});for(const box of boxes)assert(black(sample(box,40))&&!black(sample(box,52)),'uniform size adjustment updates cached geometry');
      draw({axisSize:72});const edgePoints=[[2,2],[1917,2],[2,717],[1917,717]];for(const [x,y]of edgePoints){const p=[...ctx.getImageData(x,y,1,1).data];assert(p.slice(0,3).some(v=>v>0),'flow reaches viewport edges');}
      livelyPropertyListener('axisSize',999);assert(keyBloom.state.settings.axisSize===140,'size clamp');livelyPropertyListener('axisColor','invalid');assert(keyBloom.state.settings.axisColor==='#101820','reject invalid color');
      for(const [key,value]of Object.entries({gapMode:1,gapBrightness:75,axisSize:72,axisTransparency:55,axisColor:'#101820',axisRadius:12,axisFeather:8,keyLight:0}))livelyPropertyListener(key,value);
      return{pass:true,uniformKeys:boxes.length,featherRatios:ratios,fullViewport:true};
    });
    await test.waitForTimeout(100);await test.screenshot({path:'work/v13-axis-rgb.jpg',type:'jpeg',quality:85});return result;
  }finally{await test.close();}
}
