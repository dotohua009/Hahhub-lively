const assert=require('node:assert/strict');
global.KEYS=require('../wallpaper/layout.js').KEYS;
global.keySurfaces=require('../wallpaper/surfaces.js');
const games=require('../wallpaper/games.js');
const a=keySurfaces.fracture(1234,1),b=keySurfaces.fracture(5678,1);
assert.deepEqual(a,keySurfaces.fracture(1234,1),'seed is deterministic');
assert.notDeepEqual(a.lines,b.lines,'different strikes have different branches');
const extent=c=>Math.max(...c.lines.flat().map(p=>Math.hypot(...p)));
assert(extent(keySurfaces.fracture(1234,8))>extent(a)*1.8,'damage extends the fracture');
for(let seed=0;seed<100;seed++){const c=keySurfaces.fracture(seed,8);assert(c.lines.length<100);assert(c.lines.flat(2).every(Number.isFinite));}
games.reset(7);games.press('KeyA');const first=games.state.cracks[0].strikes[0];
for(let i=0;i<1000;i++)games.press('KeyA');
const crack=games.state.cracks[0];assert.equal(crack.strikes.length,8);assert.equal(crack.strikes[0],first,'first impact persists during repeated damage');
assert(new Set(crack.strikes.map(s=>s.seed)).size>1,'held input adds varied strikes');
console.log('PASS: reproducible seeds, independent impacts, widening fractures, finite geometry, bounded retained strikes');
