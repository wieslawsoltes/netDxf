// Supplemental oracle inputs. Expected results always come from the pinned C# implementation.
import { D, I, R, V, E, A } from './geometry-corpus.mjs';
const N=(type,args=[],id='p',extra={})=>({kind:'new',type,args,id,...extra});
const S=(target,member,value,nonPublic=false)=>({kind:'set',target,member,value,...(nonPublic?{nonPublic:true}:{})});
const G=(target,member,id,nonPublic=false)=>({kind:'get',target,member,id,...(nonPublic?{nonPublic:true}:{})});
const C=(target,member,args=[],id,nonPublic=false)=>({kind:'call',target,member,args,...(id?{id}:{}),...(nonPublic?{nonPublic:true}:{})});
const snap=target=>({kind:'snapshot',target});
const shorts=values=>A('Int16',values.map(value=>({short:value})));
const points=n=>A('Vector3',Array.from({length:n},(_,i)=>V('Vector3',i+1,2*i-1,3-i)));
const face=indices=>({new:'Entities.PolyfaceMeshFace',args:[shorts(indices)]});
const mesh=(indices=[1,-2,3])=>N('Entities.PolyfaceMesh',[points(4),A('Entities.PolyfaceMeshFace',[face(indices)])]);
const tag=(code,value)=>({new:'IO.DxfTag',args:[{short:code},value]});
const newRecord=(id='r',code='VERTEX')=>N('Entities.PolyfaceMeshRecord',[code,{new:'List<IO.DxfTag>',args:[A('IO.DxfTag',[tag(0,code),tag(5,'A'),tag(330,'B')])]}],id,{nonPublic:true});
export function polyfaceCorpus(){
  const out=[],add=(name,steps)=>out.push({name:'polyface/'+name,category:'polyface',request:{steps}});
  for(let count=1;count<=4;count++)for(let mask=0;mask<(1<<count);mask++)for(const padding of [false,true]) {
    const indices=Array.from({length:count},(_,i)=>(mask&(1<<i)?-1:1)*(i+1));if(padding&&count<4){indices.push(0);while(indices.length<4)indices.push(-32768);}
    add(`topology/${count}/${mask}/${padding}`,[mesh(indices),C('p','Explode'),C('p','Clone',[],'copy'),snap('copy')]);
  }
  for(const values of [null,[],[0],[0,1],[1,2,3,4,5],[-32768],[32767],[1,0,-32768],[1,-5],[-1,2,0,32767]])
    add('face/guard/'+JSON.stringify(values),[N('Entities.PolyfaceMeshFace',values===null?[null]:[shorts(values)])]);
  for(let n=0;n<=5;n++)for(const raw of [false,true])for(const values of [[],[[1]],[[1,2,3]],[[5]],[[0,1]],[[1,0,32767]],[[1],[-2],[1,2,-3,4]]])
    add(`mesh/constructor/${n}/${raw}/${JSON.stringify(values)}`,[N('Entities.PolyfaceMesh',[points(n),raw?A('Int16[]',values.map(shorts)):A('Entities.PolyfaceMeshFace',values.map(face))]),]);
  const rawSignature=['IEnumerable<Vector3>','IEnumerable<Int16[]>'];
  for(const vertices of [null,points(2),points(4)])for(const raw of [true,false])add(`null/${out.length}`,[N('Entities.PolyfaceMesh',[vertices,null],'p',{signature:raw?rawSignature:['IEnumerable<Vector3>','IEnumerable<Entities.PolyfaceMeshFace>']})]);
  add('null-face',[N('Entities.PolyfaceMesh',[points(4),A('Entities.PolyfaceMeshFace',[null])])]);
  add('raw-null-face',[N('Entities.PolyfaceMesh',[points(4),A('Int16[]',[null])],'p',{signature:rawSignature})]);
  add('mutable-index',[mesh(),G('p','Faces','faces'),{kind:'index',target:'faces',args:[I(0)],id:'face'},G('face','VertexIndexes','indices'),
    ...[0,5,-32768,1].flatMap(value=>[{kind:'set-index',target:'indices',args:[I(0)],value:{short:value}},C('p','Explode'),C('p','Clone')])]);
  add('array-copy',[mesh(),G('p','Vertexes','vertices'),{kind:'index',target:'vertices',args:[I(0)],id:'point'},S('point','X',D(99)),snap('p'),
    {kind:'set-index',target:'vertices',args:[I(0)],value:V('Vector3',4,5,6)},C('p','Explode'),C('p','Clone',[],'q'),G('q','Vertexes','copyVertices'),
    {kind:'set-index',target:'copyVertices',args:[I(0)],value:V('Vector3',9,9,9)},snap('p'),snap('q')]);
  for(const norm of [[0,0,0],[0,3,4],[NaN,0,1],[Infinity,0,1]])add(`normal/${norm}`,[mesh(),S('p','Normal',V('Vector3',...norm)),C('p','Clone'),C('p','Explode')]);
  for(const mode of [0,1,2])for(const invisible of [false,true])add(`appearance/${mode}/${invisible}`,[mesh(),S('p','IsVisible',!invisible),
    S('p','Layer',{new:'Tables.Layer',args:['PARENT']}),S('p','Color',{static:'AciColor',property:'Blue'}),
    G('p','Faces','faces'),{kind:'index',target:'faces',args:[I(0)],id:'face'},S('face','Layer',mode===0?null:{new:'Tables.Layer',args:['FACE']}),
    S('face','Color',mode===2?{static:'AciColor',property:'Red'}:null),C('p','Explode'),C('p','Clone')]);
  for(const duplicate of [false,true])for(const throws of [false,true])for(const replacement of [null,{new:'Tables.Layer',args:['SUB']}]) {
    add(`events/${out.length}`,[N('Entities.PolyfaceMeshFace',[shorts([1,2,-3])],'f'),N('Entities.PolyfaceMesh',[points(4),A('Entities.PolyfaceMeshFace',duplicate?[R('f'),R('f')]:[R('f')])]),
      {kind:'observe',target:'p',member:'PolyfaceMeshFaceLayerChanged',observer:'layer',replace:replacement,throw:throws},S('f','Layer',{new:'Tables.Layer',args:['PROPOSED']}),snap('p'),{kind:'events'},C('p','Clone')]);
  }
  let seed=0x50464d53;const random=()=>{seed=(Math.imul(seed,1664525)+1013904223)>>>0;return (seed%33-16)/8;};
  for(let i=0;i<64;i++)add(`transform/${i}`,[mesh(),C('p','TransformBy',[{new:'Matrix3',args:Array.from({length:9},()=>D(random()))},V('Vector3',random(),random(),random())]),C('p','Explode'),C('p','Clone')]);
  // Internal record construction/fields are explicitly selected, not replacements for typed IO.
  for(const code of ['VERTEX','SEQEND'])for(const privateData of [false,true])for(const faceColor of [null,{static:'AciColor',property:'Red'},{static:'AciColor',property:'ByLayer'}])
    add(`record/${out.length}`,[newRecord('r',code),S('r','HasPrivateData',privateData,true),S('r','IsFaceRecord',code==='VERTEX'),N('Entities.PolyfaceMeshFace',[shorts([1,2,3])],'face'),S('face','Color',faceColor),S('r','StoredFace',R('face'),true),
      C('r','CanClone',[],null,true),C('r','TopologyTagCount',[],null,true),C('r','CopyForClone',[R('face')],'copy',true),snap('copy')]);
  for(const key of ['Layer','Linetype'])add('record/resources/'+key,[newRecord(),G('r','Resources','map',true),{kind:'map-add',target:'map',args:[I(3),{new:'Tables.'+key,args:['RESOURCE']}]},snap('r'),C('r','CopyForClone',[null],'copy',true),snap('copy')]);
  add('retained/clone',[mesh(),newRecord('a'),newRecord('b'),newRecord('c'),newRecord('d'),newRecord('faceRecord'),newRecord('end','SEQEND'),
    G('p','Faces','faces'),{kind:'index',target:'faces',args:[I(0)],id:'face'},S('faceRecord','IsFaceRecord',true),S('faceRecord','StoredFace',R('face'),true),S('faceRecord','FaceIndex',I(0),true),
    C('p','SetStoredRecords',[null,A('Entities.PolyfaceMeshRecord',['a','b','c','d','faceRecord','end'].map(R))],null,true),
    S('p','StoredHeaderTags',{new:'List<IO.DxfTag>',args:[A('IO.DxfTag',[tag(0,'POLYLINE')])]},true),
    S('p','DeclaredVertexCount',{short:123}),S('p','DeclaredFaceCount',{short:-1}),snap('p'),C('p','Clone',[],'q'),snap('q'),
    S('p','HasPrivateHeader',true,true),C('p','Clone')]);
  for(const resource of ['Point','Layer','Linetype'])add('record/resource-guard/'+resource,[newRecord(),G('r','Resources','resources',true),
    {kind:'map-add',target:'resources',args:[I(0),resource==='Point'?{new:'Entities.Point',args:[]}:{new:'Tables.'+resource,args:['R']}]},C('r','CanClone',[],null,true),C('r','CopyForClone',[null],null,true)]);
  for(const hasExtension of [false,true])for(const hasReactor of [false,true])add(`record/metadata/${hasExtension}/${hasReactor}`,[newRecord(),
    N('Entities.Point',[],'target'),S('target','Handle','AB'),...(hasExtension?[N('Objects.DxfDictionary',[],'extension'),S('r','ExtensionDictionary',R('extension'))]:[]),
    G('r','PersistentReactors','reactors'),...(hasReactor?[C('reactors','Add',[R('target')])]:[]),C('r','CanClone',[],null,true),C('r','TopologyTagCount',[],null,true)]);
  for(const code of ['VERTEX','SEQEND'])add('record/opaque/'+code,[newRecord('r',code),S('r','IdentityIndex',I(1),true),S('r','OwnerIndex',I(2),true),G('r','Tags','tags',true),
    C('tags','Add',[tag(340,'EF')]),C('tags','Add',[tag(1005,'FF')]),S('r','XDataStart',I(5),true),G('r','OpaqueHandleTags','refs',true),snap('refs')]);
  for(const original of [null,[],[1,2,3],[1,0,3],[1,-2,3]])add('record/original-indices/'+JSON.stringify(original),[newRecord(),N('Entities.PolyfaceMeshFace',[shorts([1,2,3])],'face'),S('r','StoredFace',R('face'),true),S('r','OriginalIndexes',original===null?null:shorts(original),true),G('r','FaceIndexesUnchanged','unchanged',true)]);
  return out;
}
