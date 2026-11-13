// Physical layout: 16 units × 6 rows, with no extra function-row gap.
const KEY_ROWS = [
  [['Escape','Esc'], ...Array.from({length:12},(_,i)=>[`F${i+1}`,`F${i+1}`]), ['PrintScreen','PrtSc'],['ScrollLock','Scroll'],['Delete','Del']],
  [['Backquote','` ~'], ...'1234567890'.split('').map(x=>[`Digit${x}`,x]), ['Minus','−'],['Equal','='],['Backspace','Backspace',2],['Home','Home']],
  [['Tab','Tab',1.5], ...'QWERTYUIOP'.split('').map(x=>[`Key${x}`,x]),['BracketLeft','['],['BracketRight',']'],['Backslash','\\',1.5],['End','End']],
  [['CapsLock','Caps',1.75], ...'ASDFGHJKL'.split('').map(x=>[`Key${x}`,x]),['Semicolon',';'],['Quote',"'"],['Enter','Enter',2.25],['PageUp','PgUp']],
  [['ShiftLeft','L Shift',2.25], ...'ZXCVBNM'.split('').map(x=>[`Key${x}`,x]),['Comma',','],['Period','.'],['Slash','/'],['ShiftRight','R Shift',1.75],['ArrowUp','↑'],['PageDown','PgDn']],
  [['ControlLeft','L Ctrl',1.25],['MetaLeft','Win',1.25],['AltLeft','L Alt',1.25],['Space','Space',6.25],['AltRight','R Alt'],['Fn','Fn1'],['ControlRight','R Ctrl'],['ArrowLeft','←'],['ArrowDown','↓'],['ArrowRight','→']]
];
const KEYS = KEY_ROWS.flatMap((row,r)=>{let x=0;return row.map(([code,label,w=1])=>{const k={code,label,x,y:r,w,cx:x+w/2,cy:r+.5};x+=w;return k;});});
if (KEYS.length !== 84 || KEY_ROWS.some(row=>row.reduce((n,k)=>n+(k[2]||1),0)!==16) || new Set(KEYS.map(k=>k.code)).size!==84) throw Error('Invalid physical keyboard layout');
if (typeof module!=='undefined') module.exports={KEYS,KEY_ROWS};
