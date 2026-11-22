"""Run with Python Playwright and the installed Edge browser."""
from pathlib import Path
import json
from playwright.sync_api import sync_playwright

root = Path(__file__).resolve().parents[1]
wall = root / "effects" / "hahhan-glass"
params = json.loads((wall / "LivelyProperties.json").read_text(encoding="utf-8-sig"))
assert set(params) == {"density", "spread", "heal", "delay", "crackSize","fps","rim"}
with sync_playwright() as p:
    browser = p.chromium.launch(channel="msedge", headless=True)
    page = browser.new_page(viewport={"width": 1920, "height": 720})
    page.add_init_script("""window.drawCount=0;const r=window.requestAnimationFrame.bind(window);
      window.requestAnimationFrame=f=>r(t=>{window.drawCount++;f(t)});""")
    page.goto((wall / "index.html").as_uri())
    fixed = page.evaluate("GLASS.keys.map(k=>[k.x,k.y])")
    page.evaluate("livelyPropertyListener('shiftX',1000)")
    assert fixed == page.evaluate("GLASS.keys.map(k=>[k.x,k.y])")
    page.evaluate("livelyPropertyListener('heal',500)")
    assert page.title().endswith("75/85/500/800/0/0")
    def hit(code):
        page.keyboard.press(code)
        page.wait_for_function("hits.size>0 && drawCount>0")
        page.wait_for_timeout(55)
        return page.evaluate("""() => {
          const a=ctx.getImageData(0,0,canvas.width,canvas.height).data;let n=0,x=0,y=0;
          for(let i=3;i<a.length;i+=4)if(a[i]>20){n++;const j=(i-3)/4;x+=j%canvas.width;y+=Math.floor(j/canvas.width)}
          return {x:x/n,y:y/n,pixels:n};
        }""")
    A = hit("a")
    page.wait_for_function("!hits.size && !next")
    L = hit("l")
    assert A["pixels"] > 100 and L["pixels"] > 100 and L["x"] - A["x"] > 800
    page.keyboard.press("a")
    assert page.evaluate("hits.size") == 2
    page.evaluate("livelyWallpaperPlaybackChanged(JSON.stringify({IsPaused:true}))")
    page.keyboard.press("j")
    assert page.evaluate("!hits.size && !next") and page.title().endswith("/1")
    page.evaluate("livelyWallpaperPlaybackChanged({IsPaused:false})")
    page.keyboard.press("k")
    page.wait_for_function("!hits.size && !next")
    before = page.evaluate("drawCount")
    page.wait_for_timeout(350)
    assert before == page.evaluate("drawCount")
    graph = page.evaluate("""() => {
      clear(); strike(keys.get('KeyA'),1000); const h=hits.get('KeyA'), shape=h.shape;
      function tips(t){const a=[];growingEdges(h,t,(...edge)=>a.push(edge));return a;}
      const early=tips(1180), later=tips(3100);
      const fixed=early.slice(0,30).every((e,i)=>e.every((v,j)=>v===later[i][j]));
      const initial=tips(1000);
      const radius=Math.max(...initial.map(e=>Math.hypot(e[2]-h.x,e[3]-h.y)))*720*GLASS.pitch;
      const contactOk=Math.abs(2*radius-GLASS.axisSize/2)<.1;
      release(h.key,3100);
      const plateau=JSON.stringify(tips(3899))===JSON.stringify(later);
      const mid=tips(4100),inner=tips(4250);
      const reverse=inner.length<mid.length&&mid.length<later.length&&inner.length>0;
      h.released=null;release(h.key,3100);strike(keys.get('KeyA'),3100);
      const retained=h.shape===shape && JSON.stringify(tips(3100))===JSON.stringify(later);
      strike(keys.get('KeyL'),3100);
      const connected=joints.length===1 && joints[0].from===h && joints[0].to===hits.get('KeyL');
      const onOldEdge=growingPointOnEdge(joints[0].points[0],later);
      function growingPointOnEdge(p,list){return list.some(([x,y,bx,by])=>{
        const dx=bx-x,dy=by-y,t=Math.max(0,Math.min(1,((p[0]-x)*dx+(p[1]-y)*dy)/(dx*dx+dy*dy||1)));
        return Math.hypot(p[0]-x-t*dx,p[1]-y-t*dy)<.0001;
      });}
      expire(6000); const staysWhileHeld=hits.size===2; clear();
      livelyPropertyListener('crackSize',1);strike(h.key,7000);const fine=hits.get(h.key.code);
      const sizeChoice=fine.shape!==shape&&fine.shape.segments.length>shape.segments.length*1.5;clear();
      livelyPropertyListener('delay',0);strike(h.key,8000);const instant=hits.get(h.key.code);
      release(h.key,8200);const zeroDelay=reach(instant,8201)<reach(instant,8200);clear();
      return {plateau,sizeChoice,zeroDelay,fixed,retained,connected,onOldEdge,staysWhileHeld,contactOk,reverse,earlyEdges:early.length,lateEdges:later.length};
    }""")
    assert all(graph[k] for k in ('plateau','sizeChoice','zeroDelay','fixed','retained','connected','onOldEdge','staysWhileHeld','contactOk','reverse')), graph
    assert graph['lateEdges'] > graph['earlyEdges']
    browser.close()
    report={"layout":len(fixed),"sliders":list(params),"A":A,"L":L,"chord":True,"growth":graph,"pauseForms":True,"idleFrames":0}
    (root / "tests" / "browser.json").write_text(json.dumps(report,indent=2),encoding="utf-8")
    print(json.dumps(report))

