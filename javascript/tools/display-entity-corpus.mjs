// Supplemental oracle inputs for Text, Shape and Mesh. No expected values or runtime algorithms.
import { D, R, E, A, V, I } from './geometry-corpus.mjs';
const N=(type,args=[],id='p',signature)=>({kind:'new',type:type.includes('.')?type:'Entities.'+type,args,id,...(signature?{signature}:{})});
const S=(target,member,value)=>({kind:'set',target,member,value});
const G=(target,member,id)=>({kind:'get',target,member,id});
const C=(target,member,args=[],id,signature)=>({kind:'call',target,member,args,...(id?{id}:{}),...(signature?{signature}:{})});
const Q=(target,index,id)=>({kind:'index',target,args:[I(index)],id});
const snap=target=>({kind:'snapshot',target});
const style=(type='TextStyle',name='STYLE')=>({new:'Tables.'+type,args:[name,'font.shx']});
const text=()=>N('Text',['Hello 日本😀',V('Vector3',1,2,3),D(2.5)]);
const shape=()=>N('Shape',['ZIG',style('ShapeStyle'),V('Vector3',1,2,3),D(2.5),D(-450)]);
const mesh=()=>N('Mesh',[A('Vector3',[V('Vector3',0,0,0),V('Vector3',1,0,0),V('Vector3',0,1,0)]),A('Int32[]',[A('Int32',[I(0),I(1),I(2)])]),A('Entities.MeshEdge',[{new:'Entities.MeshEdge',args:[I(0),I(1),D(-1)]}])]);
const matrices=[[1,0,0,0,1,0,0,0,1],[-1,0,0,0,1,0,0,0,1],[1,0,0,0,-1,0,0,0,1],[2,0,0,0,3,0,0,0,4],
  [0,-1,0,1,0,0,0,0,1],[0,0,0,0,0,0,0,0,0],[2,1,-1,1,3,2,-1,0,2]];
const transform=m=>C('p','TransformBy',[{new:'Matrix3',args:m.map(D)},V('Vector3',3,-4,5)],null,['Matrix3','Vector3']);
export function displayEntityCorpus(){
  const out=[],add=(name,category,steps)=>out.push({name,category,request:{steps}});
  for(const value of [null,'','Hello','日本😀','a\0b'])for(const dimension of [2,3])add(`text/constructor/${dimension}/${JSON.stringify(value)}`,'text-model',[
    N('Text',[value,V('Vector'+dimension,...[1,2,3].slice(0,dimension)),D(2.5),style()]),C('p','Clone',[],'q'),snap('q')]);
  for(const type of ['Text','Shape']){
    const create=type==='Text'?text:shape;
    const fields=type==='Text'?['Height','Width','WidthFactor','ObliqueAngle','Rotation']:['Size','WidthFactor','ObliqueAngle','Rotation','Thickness'];
    for(const member of fields)for(const value of [-Infinity,-100,-85,-1,-1e-13,-0,0,1e-13,.009,.01,1,85,100,101,Infinity,NaN])
      add(`${type}/${member}/${Object.is(value,-0)?'-0':value}`,'display-guards',[create(),S('p',member,D(value)),snap('p'),C('p','Clone')]);
    for(const failure of [false,true])add(`${type}/style-event/${failure}`,'display-events',[
      create(),N('Tables.'+(type==='Text'?'TextStyle':'ShapeStyle'),['REPLACEMENT','s.shx'],'replacement'),
      {kind:'observe',target:'p',member:type==='Text'?'TextStyleChanged':'StyleChanged',observer:'style',replace:R('replacement'),throw:failure},
      S('p','Style',style(type==='Text'?'TextStyle':'ShapeStyle','PROPOSED')),snap('p'),{kind:'events'},C('p','Clone')]);
    add(`${type}/style-null`,'display-guards',[create(),S('p','Style',null),snap('p')]);
    add(`${type}/value-copy`,'display-model',[create(),G('p','Position','position'),S('position','X',D(99)),snap('p'),C('p','Clone',[],'q'),G('q','Style','style'),S('style','Name','CLONED'),snap('p'),snap('q')]);
  }
  for(const mirror of [false,true])for(let alignment=0;alignment<15;alignment++)for(let i=0;i<matrices.length;i++)
    add(`text/transform/${mirror}/${alignment}/${i}`,'text-transforms',[
      text(),{kind:'set',type:'Entities.Text',member:'DefaultMirrText',value:mirror},S('p','Alignment',E('Entities.TextAlignment',alignment)),
      S('p','Rotation',D(37)),S('p','ObliqueAngle',D(-12)),S('p','WidthFactor',D(.75)),S('p','IsBackward',alignment%2===0),S('p','IsUpsideDown',alignment%3===0),
      transform(matrices[i]),snap('p'),C('p','Clone')]);
  for(const width of [-2,.75,2])for(let i=0;i<matrices.length;i++)add(`shape/transform/${width}/${i}`,'shape-transforms',[
    shape(),S('p','WidthFactor',D(width)),S('p','ObliqueAngle',D(-12)),transform(matrices[i]),snap('p'),C('p','Clone')]);
  for(const name of [null,'',' ','ZIG'])add(`shape/name/${JSON.stringify(name)}`,'display-guards',[N('Shape',[name,style('ShapeStyle')]),shape(),S('p','Name',name),snap('p')]);
  for(const type of ['Text','Shape'])for(const height of [-1,0,NaN,Infinity])add(`${type}/constructor-size/${height}`,'display-guards',[
    type==='Text'?N('Text',['T',V('Vector3',1,2,3),D(height)]):N('Shape',['Z',style('ShapeStyle'),V('Vector3',1,2,3),D(height),D(0)])]);
  for(const [vertexes,faces] of [[null,null],[A('Vector3',[]),null],[null,A('Int32[]',[])]])add(`mesh/null/${out.length}`,'mesh-guards',[N('Mesh',[vertexes,faces])]);
  for(const subdivision of [0,1,5,255])for(const blend of [false,true])add(`mesh/metadata/${subdivision}/${blend}`,'mesh-model',[
    mesh(),S('p','SubdivisionLevel',{byte:subdivision}),S('p','BlendCrease',blend),C('p','Clone',[],'q'),snap('q')]);
  for(let i=0;i<matrices.length;i++)add(`mesh/transform/${i}`,'mesh-transforms',[mesh(),transform(matrices[i]),snap('p'),C('p','Clone')]);
  add('mesh/clone-isolation','mesh-model',[mesh(),C('p','Clone',[],'q'),G('q','Vertexes','vertices'),Q('vertices',1,'v'),S('v','X',D(42)),snap('q'),
    {kind:'set-index',target:'vertices',args:[I(1)],value:V('Vector3',42,2,3)},G('q','Edges','edges'),Q('edges',0,'edge'),S('edge','Crease',D(3)),snap('p'),snap('q')]);
  for(const field of ['Faces','Edges'])add(`mesh/null-element/${field}`,'mesh-guards',[mesh(),G('p',field,'list'),C('list','Add',[null]),C('p','Clone'),snap('p')]);
  let seed=0x54455854;const next=()=>{seed=(Math.imul(seed,1664525)+1013904223)>>>0;return seed;};
  for(let i=0;i<96;i++){
    const create=[text,shape,mesh][i%3],m=Array.from({length:9},()=>((next()%33)-16)/8);
    add(`display/seeded/${i}`,'display-seeded',[create(),transform(m),snap('p'),C('p','Clone')]);
  }
  return out;
}
