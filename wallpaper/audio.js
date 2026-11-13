'use strict';
const keyAudio=(()=>{
  let context=null,enabled=false,volume=.25,last=-Infinity,voices=new Set();
  function stop(){for(const node of voices){try{node.stop();}catch{}}voices.clear();if(context&&context.state==='running')context.suspend().catch(()=>{});}
  function configure(on,level){enabled=on;volume=level/100;if(!on||!volume)stop();}
  async function play(mode){
    if(!enabled||!volume)return;const now=performance.now();if(now-last<55)return;last=now;
    try{context??=new(window.AudioContext||window.webkitAudioContext)();if(context.state==='suspended')await context.resume();if(!enabled||!volume||context.state!=='running'||performance.now()-now>300)return;
      const t=context.currentTime,g=context.createGain(),duration=mode===7?.22:.12;g.connect(context.destination);g.gain.setValueAtTime(volume*.22,t);g.gain.exponentialRampToValueAtTime(.0001,t+duration);
      let source;if(mode===7){const buffer=context.createBuffer(1,Math.ceil(context.sampleRate*duration),context.sampleRate),data=buffer.getChannelData(0);for(let i=0;i<data.length;i++)data[i]=(Math.random()*2-1)*(Math.random()>.94?1:.18);source=context.createBufferSource();source.buffer=buffer;const filter=context.createBiquadFilter();filter.type='highpass';filter.frequency.value=2200;source.connect(filter);filter.connect(g);}
      else {source=context.createOscillator();source.type=mode===8?'triangle':'sine';source.frequency.setValueAtTime(mode===8?170:550+Math.random()*650,t);source.frequency.exponentialRampToValueAtTime(mode===8?70:250,t+duration);source.connect(g);}
      voices.add(source);source.onended=()=>{voices.delete(source);source.disconnect();g.disconnect();if(!voices.size&&context.state==='running')context.suspend().catch(()=>{});};source.start(t);source.stop(t+duration);
    }catch{stop();}
  }
  return {configure,play,stop,get state(){return{enabled,voices:voices.size,state:context?.state||'closed'};}};
})();
