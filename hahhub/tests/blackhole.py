"""Exercise the standalone Lively entry in Edge and compare to the captured background."""
import json
from pathlib import Path
from playwright.sync_api import sync_playwright

root = Path(__file__).resolve().parents[1]
pack = root / 'effects' / 'hahhan-blackhole'
glass = json.loads((root/'effects/hahhan-glass/glass-data.js').read_text()[14:].strip()[:-1])
with sync_playwright() as runner:
    browser = runner.chromium.launch(channel='msedge', headless=True)
    page = browser.new_page(viewport={'width':1920, 'height':720})
    page.goto((pack/'index.html').as_uri())
    page.wait_for_function('cover.complete && cover.naturalWidth>0')
    assert page.evaluate('LAYOUT.keys') == glass['keys']
    shape = page.evaluate("""()=>{
      clear();const a=keys.get('KeyA'),s=keys.get('KeyS'),l=keys.get('KeyL');
      strike(a,1000);const one=plan(1390)[0].w;strike(s,1000);
      const adjacent=plan(1390),d=keys.get('KeyD');strike(d,1390);
      const third=plan(1390),chain=third.length===2&&Math.abs(third[0].w-adjacent[0].w)<.001&&plan(1800).length===1;
      wells.delete(d.code);strike(l,1390);const distant=plan(1800);
      const full=plan(5800)===null;
      release(a,5800);release(s,5800);release(l,5800);
      const recovering=plan(6030)!==null;clear();
      return {chain,merged:adjacent.length===1&&adjacent[0].w>one*1.35,independent:distant.length===2,full,recovering};
    }""")
    assert all(shape.values()),shape
    page.keyboard.down('a')
    page.wait_for_timeout(300)
    count = page.evaluate("""()=>{
        const rgba=ctx.getImageData(0,0,1920,720).data,k=keys.get('KeyA');
        let inside=0,outside=0,rx=3*LAYOUT.pitch*720/2;
        for(let i=3;i<rgba.length;i+=4)if(rgba[i]>18){
            const x=((i-3)/4)%1920,y=Math.floor((i-3)/4/1920);
            if(Math.hypot(x-k.x*1920,y-k.y*720)>rx+2)outside++;else inside++;
        }
        return {inside,outside};
    }""")
    assert count['inside']>75000 and count['outside']==0,count
    page.keyboard.down('l')
    page.keyboard.up('a')
    page.wait_for_function("!wells.has('KeyA') && wells.has('KeyL')")
    page.keyboard.up('l')
    page.wait_for_function('!wells.size && !request')
    page.evaluate("livelyPropertyListener('pull',52)")
    assert page.evaluate('power')==52
    page.mouse.down()
    page.evaluate('livelyWallpaperPlaybackChanged({IsPaused:true})')
    assert page.evaluate('paused && !wells.size && !request')
    browser.close()
    print(json.dumps(count))
