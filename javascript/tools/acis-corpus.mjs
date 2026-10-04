// Supplemental requests only. The unchanged C# assembly supplies every expected result.
import { D, R, A, V, I } from './geometry-corpus.mjs';
const N=(type,args=[],id='p')=>({kind:'new',type:'Entities.'+type,args,id});
const C=(target,member,args=[],id)=>({kind:'call',target,member,args,...(id?{id}:{})});
const S=(target,member,value)=>({kind:'set',target,member,value});
const G=(target,member,id)=>({kind:'get',target,member,id});
const snap=target=>({kind:'snapshot',target});
const chunk=(code,text)=>({new:'Entities.AcisSatChunk',args:[{short:code},text]});
const lines=value=>A('String',value);
const chunks=value=>A('Entities.AcisSatChunk',value);
export function acisCorpus() {
  const out=[],add=(name,steps)=>out.push({name:'acis/'+name,category:'acis',request:{steps}});
  const printable=String.fromCharCode(...Array.from({length:95},(_,i)=>i+32));
  for(const type of ['Body','Region','Solid3D']) {
    for(const [i,value] of [[],[''],['700 0 1 0','opaque'],[printable],['  whitespace  ',''],['0'.repeat(254)+'A'+'C'.repeat(260)],['A'.repeat(255)],['x'.repeat(1024)]].entries())
      add(type+'/lines/'+i,[N(type),C('p','SetSatLines',[lines(value)]),C('p','Clone',[],'q'),G('p','EncodedSatChunks','old'),C('p','SetSatLines',[lines(['new'])]),snap('old'),snap('q')]);
    for(let cp=0;cp<160;cp++)add(type+'/ascii/'+cp,[N(type),C('p','SetSatLines',[lines(['baseline'])]),C('p','SetSatLines',[lines(['first',String.fromCharCode(cp)])]),snap('p')]);
    for(const [i,value] of [null,[null],[chunk(3,'x')],[chunk(1,'^')],[chunk(1,'^x')],[chunk(1,'^'),chunk(3,' ')],[chunk(1,'a'),null],[chunk(1,'\\U+0041'),chunk(3,' '),chunk(1,'')]].entries())
      add(type+'/encoded/'+i,[N(type),C('p','SetSatLines',[lines(['baseline'])]),C('p','SetEncodedSatChunks',[value===null?null:chunks(value)]),snap('p'),C('p','Clone')]);
    for(const text of ['',' ','^ ','^^','@_','A','~','^','^x'])for(const split of [0,1])
      add(type+'/split/'+text+'/'+split,[N(type),C('p','SetEncodedSatChunks',[chunks([chunk(1,text.slice(0,split)),chunk(3,text.slice(split))])]),snap('p')]);
    for(let row=0;row<4;row++)for(let col=0;col<4;col++)for(const value of [Number.MIN_VALUE,NaN,Infinity,2]) {
      const m=Array.from({length:16},(_,i)=>i%5===0?1:0);m[row*4+col]=value;
      add(type+'/matrix4/'+row+'/'+col+'/'+value,[N(type),C('p','SetSatLines',[lines(['opaque'])]),C('p','TransformBy',[{new:'Matrix4',args:m.map(D)}]),snap('p')]);
    }
    for(const [i,v] of [[0,0,0],[-0,0,0],[Number.MIN_VALUE,0,0],[NaN,0,0],[0,Infinity,0]].entries())
      add(type+'/translation/'+i,[N(type),C('p','TransformBy',[{static:'Matrix3',property:'Identity'},V('Vector3',...v)]),snap('p')]);
  }
  for(const code of [-32768,-1,0,1,2,3,4,32767])for(const text of [null,'','x','x'.repeat(255),'x'.repeat(256),'\n','é'])add('chunk/'+code+'/'+String(text).length+'/'+out.length,[N('AcisSatChunk',[{short:code},text])]);
  for(const value of [null,'0','00','','AB',' 0'])add('history/'+JSON.stringify(value),[N('Solid3D'),S('p','HistoryHandle','0'),S('p','HistoryHandle',value),C('p','Clone'),snap('p')]);
  let seed=0x534154;
  for(let i=0;i<96;i++) {
    const text=Array.from({length:300+i},()=>{seed=(Math.imul(seed,1664525)+1013904223)>>>0;return String.fromCharCode(32+seed%95);}).join('');
    add('seeded/'+i,[N(['Body','Region','Solid3D'][i%3]),C('p','SetSatLines',[lines([text])]),G('p','EncodedSatChunks','chunks'),N('Body',[],'q'),C('q','SetEncodedSatChunks',[R('chunks')]),snap('q')]);
  }
  return out;
}
