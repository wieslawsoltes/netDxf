// Supplemental inputs only; metadata and byte output are compared with the unchanged C# API.
import { D, R, E, A, V, I } from './geometry-corpus.mjs';
const N=(type,args=[],id='p',nonPublic=false)=>({kind:'new',type:'Entities.'+type,args,id,...(nonPublic?{nonPublic}:{})});
const C=(target,member,args=[],id)=>({kind:'call',target,member,args,...(id?{id}:{})});
const S=(target,member,value)=>({kind:'set',target,member,value});
const G=(target,member,id)=>({kind:'get',target,member,id});
const snap=target=>({kind:'snapshot',target});
const bytes=n=>A('Byte',Array.from({length:n},(_,i)=>({byte:i*131%256})));
const upper=()=>V('Vector3',1,2,3),lower=()=>V('Vector3',4,5,6);
const frame2=(data=bytes(33),desc='dormant',version=7,kind=3,tile=1)=>[data,upper(),lower(),desc,{short:version},E('Entities.OleObjectType',kind),{short:tile}];
export function oleCorpus(){
  const out=[],add=(name,steps)=>out.push({name:'ole/'+name,category:'ole',request:{steps}});
  for(const type of ['OleFrame','Ole2Frame']){
    const params=n=>type==='OleFrame'?[bytes(n),{short:7}]:frame2(bytes(n));
    for(const size of [0,1,127,128,255,1025])add(`${type}/bytes/${size}`,[N(type,params(size)),C('p','GetBinaryData',[],'a'),C('p','GetBinaryData',[],'b'),{kind:'reference-equals',args:[R('a'),R('b')]},C('p','Clone',[],'q'),snap('q')]);
    for(let row=0;row<4;row++)for(let col=0;col<4;col++)for(const value of [Number.MIN_VALUE,NaN,2]){
      const m=Array.from({length:16},(_,i)=>i%5===0?1:0);m[row*4+col]=value;
      add(`${type}/matrix/${row}/${col}/${value}`,[N(type,params(3)),C('p','TransformBy',[{new:'Matrix4',args:m.map(D)}]),snap('p')]);
    }
    for(const v of [[0,0,0],[0,Number.MIN_VALUE,0],[0,0,Infinity],[NaN,0,0]])add(type+'/translation/'+out.length,[N(type,params(3)),C('p','TransformBy',[{static:'Matrix3',property:'Identity'},V('Vector3',...v)]),snap('p')]);
    for(const version of [-32768,-1,0,1,32767])add(type+'/version/'+version,[N(type,type==='OleFrame'?[bytes(2),{short:version}]:frame2(bytes(2),'v',version))]);
    add(type+'/null',[N(type,type==='OleFrame'?[null,{short:1}]:frame2(null))]);
  }
  for(const present of [false,true])for(const version of [0,1,7,32767])add(`legacy/presence/${present}/${version}`,[N('OleFrame',[bytes(2),{short:version}]),C('p','WithOleVersionPresence',[present],'q'),C('q','Clone',[],'copy'),snap('p'),snap('copy')]);
  for(let mask=0;mask<64;mask++)add('metadata/'+mask,[N('Ole2Frame',frame2()),C('p','WithMetadataFields',[E('Entities.Ole2FrameMetadataFields',mask)],'q'),C('q','Clone',[],'copy'),C('q','WithMetadataFields',[E('Entities.Ole2FrameMetadataFields',63)],'restored'),snap('p')]);
  for(const mask of [-2147483648,-1,64,127])add('invalid-mask/'+mask,[N('Ole2Frame',frame2()),C('p','WithMetadataFields',[E('Entities.Ole2FrameMetadataFields',mask)]),snap('p')]);
  for(const description of [null,'','日本😀','line\r','line\n','a\0b',{utf16:[0xd800]},String.raw`Literal \U+000A`])add('description/'+out.length,[N('Ole2Frame',frame2()),N('Ole2Frame',frame2(bytes(2),description)),C('p','Clone')]);
  for(const kind of [-32768,-1,0,1,2,3,4,32767])for(const tile of [-1,0,1,2])add(`kind/${kind}/${tile}`,[N('Ole2Frame',frame2(bytes(2),'d',2,kind,tile))]);
  for(const corner of [1,2])for(let axis=0;axis<3;axis++)for(const n of [-Infinity,Infinity,NaN,-0,Number.MIN_VALUE]){
    const args=frame2(),xyz=[1,2,3];xyz[axis]=n;args[corner]=V('Vector3',...xyz);add('corner/'+out.length,[N('Ole2Frame',args)]);
  }
  add('internal/legacy',[N('OleFrame',[bytes(4),{short:7},false,false],'p',true),C('p','Clone')]);
  add('internal/ole2',[N('Ole2Frame',[...frame2(),false,E('Entities.Ole2FrameMetadataFields',0)],'p',true),C('p','Clone')]);
  return out;
}
