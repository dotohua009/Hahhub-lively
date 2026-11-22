"""Check keyboard mapping, overlapping dabs, rim preferences and idle rendering in the Lively entries."""
from pathlib import Path

from playwright.sync_api import sync_playwright

effects = Path(__file__).resolve().parents[1] / 'effects'
with sync_playwright() as runner:
    browser = runner.chromium.launch(channel='msedge', headless=True)
    page = browser.new_page(viewport={'width': 960, 'height': 360})
    page.goto((effects / 'hahhub-ink/index.html').as_uri())
    for key in ('a', 'l'):
        page.keyboard.press(key)
    page.wait_for_function("""() => ['KeyA','KeyL'].every(code => {
      const k=keys.get(code);return ctx.getImageData(k.x*canvas.width,k.y*canvas.height,1,1).data[3]>100;
    })""")
    assert page.evaluate("splashes.length===2 && splashes[0].color!==splashes[1].color")
    samples = page.evaluate("""() => {
      const result=[];
      for(let i=0;i<24;i++){
        strike(keys.get('KeyA'));release('KeyA');const s=splashes.at(-1);result.push([s.type,s.particles,s.color]);
      }
      return result;
    }""")
    assert {r[0] for r in samples} == {'burst', 'flick', 'mist', 'pool'}
    assert min(r[1] for r in samples)>=108 and max(r[1] for r in samples)>=205
    assert len({r[2] for r in samples})>12
    stats = page.evaluate("""() => {
      let hit=0,escaped=0,total=0;
      for(const s of splashes)for(const f of s.flights)for(const p of f.drops){
        total++;
        if(Math.abs(p.x)>p.wx||Math.abs(p.y)>p.wy){
          if(p.escapes)escaped++;else hit++;
        }
        if(!p.escapes&&(Math.abs(bounce(p.x,p.wx,p.restitution))>p.wx+.0001||
                        Math.abs(bounce(p.y,p.wy,p.restitution))>p.wy+.0001))throw Error('escaped wall');
      }
      return {hit:hit/total,escaped:escaped/total};
    }""")
    assert stats['hit']>.65 and .05<stats['escaped']<.24, stats
    assert page.evaluate("keys.get('Space').w===6.25 && keys.get('Enter').w===2.25")
    page.wait_for_timeout(1800)
    assert page.evaluate("ctx.getImageData(keys.get('KeyA').x*canvas.width,keys.get('KeyA').y*canvas.height,1,1).data[3]>0")
    page.wait_for_function('!stains.size && !frame')
    assert page.evaluate('!ctx.getImageData(0,0,canvas.width,canvas.height).data.some(Boolean)')
    page.evaluate("livelyPropertyListener('recovery',500);livelyPropertyListener('spread',250)")
    page.keyboard.down('a')
    page.keyboard.down('l')
    page.wait_for_function("splashes.filter(s=>s.key.code==='KeyA').length>=3")
    page.keyboard.up('l')
    page.wait_for_function("!stains.has('KeyL') && stains.has('KeyA')")
    assert page.evaluate("stains.get('KeyA').released===null && splashes.some(s=>s.key.code==='KeyA'&&s.particles<55)")
    page.keyboard.up('a')
    page.wait_for_function('!stains.size && !splashes.length && !frame')
    page.mouse.click(350, 180)
    assert page.evaluate('splashes.length===1')
    page.evaluate('livelyWallpaperPlaybackChanged({IsPaused:true})')
    page.keyboard.press('a')
    assert page.evaluate('!splashes.length && !frame')
    for package, check in [('hahhan-glass', "rim==='#dd4411'"), ('hahhan-blackhole', "rim.join(',')==='221,68,17'")]:
        page.goto((effects / package / 'index.html').as_uri())
        page.evaluate("livelyPropertyListener('rim','#dd4411');livelyPropertyListener('rim','bad')")
        assert page.evaluate(check)
    browser.close()
    print('Ink input, variants, dye, pause and two edge colors passed.', stats)
