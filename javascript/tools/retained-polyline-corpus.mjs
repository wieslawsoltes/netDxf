// Deterministic inputs only. Synthetic internal records qualify registration and
// topology, not typed DXF loading, writing or round-trip fidelity.
const r=ref=>({ref}),n=(id,type,args=[])=>({method:'new',id,value:{new:type,args}});
const g=(target,member,id)=>({method:'get',target,member,id});
const c=(target,member,args=[],id,signature)=>({method:'call',target,member,args,...(id?{id}:{}),...(signature?{signature}:{})});
const item=(target,index,id)=>({method:'item',target,args:[typeof index==='number'?{int:index}:index],id});
const s=(target,member,value)=>({method:'set',target,member,value});
const raw=(target,field,value)=>({method:'retained-set',target,field,value});
const snap=target=>({method:'retained-model',target}),doc=()=>({method:'snapshot',target:'doc'});
const v=(x,y,z)=>({new:'Vector3',args:[x,y,z]});
const families=['Polyline3D','PolygonMesh','PolyfaceMesh','Polyline2D'];
const create=(family='Polyline3D',extra={})=>({method:'retained-create',id:'p',family,...extra});
const init=(family='Polyline3D',extra={},version=18)=>[
  n('doc','DxfDocument',[{enum:'Header.DxfVersion',value:version}]),g('doc','Entities','entities'),
  create(family,{version,...extra}),g('p','VertexRecords','vertices'),item('vertices',0,'first'),g('p','EndSequenceRecord','end')
];
const add=()=>c('entities','Add',[r('p')]);
export function retainedPolylineCorpus(){
  const all=[],push=(name,steps)=>all.push({name:'retained-polyline/'+name,request:{op:'document-ownership',steps}});
  for(const family of families)for(const version of [13,14,15,16,17,18])for(const preserveHandles of [false,true])for(const blockOwner of [false,true]) {
    push(`registration/${family}/${version}/${preserveHandles}/${blockOwner}`,[...init(family,{preserveHandles,blockOwner},version),snap('p'),add(),snap('p'),doc(),
      c('p','Clone',[],'copy'),snap('copy'),c('entities','Remove',[r('p')]),snap('p'),doc(),add(),snap('p'),doc(),
      n('other','DxfDocument',[{enum:'Header.DxfVersion',value:version}]),g('other','Entities','otherEntities'),c('otherEntities','Add',[r('copy')]),snap('copy')]);
  }
  for(const family of families)for(const version of [13,18])push(`version/${family}/${version}`,[...init(family,{},version),raw('first','SourceVersion',{enum:'Header.DxfVersion',value:version===13?18:13}),add(),doc(),snap('p')]);
  for(const family of families)push('cross-document/'+family,[...init(family),add(),c('entities','Remove',[r('p')]),n('other','DxfDocument',[{enum:'Header.DxfVersion',value:18}]),g('other','Entities','otherEntities'),c('otherEntities','Add',[r('p')]),snap('p'),doc()]);
  for(const family of families)for(const privateData of [false,true])push(`private/${family}/${privateData}`,[...init(family),raw('first','HasPrivateData',privateData),add(),c('entities','Remove',[r('p')]),snap('p'),doc()]);
  for(const family of ['PolyfaceMesh','Polyline2D'])push('private-header/'+family,[...init(family),raw('p','HasPrivateHeader',true),add(),c('entities','Remove',[r('p')]),snap('p'),doc()]);
  for(const family of families)push('block-move/'+family,[...init(family,{blockOwner:true}),n('b','Blocks.Block',['B']),g('doc','Blocks','blocks'),c('blocks','Add',[r('b')]),g('b','Entities','members'),add(),snap('p'),c('entities','Remove',[r('p')]),c('members','Add',[r('p')]),snap('p'),doc(),c('blocks','Remove',[r('b')]),snap('p'),doc()]);
  for(const faceLayer of [null,'Stored','CustomFace'])push('face-layer/'+String(faceLayer),[...init('PolyfaceMesh',{faceLayer}),add(),snap('p'),g('p','FaceRecords','faces'),item('faces',0,'faceRecord'),g('faceRecord','Face','face'),s('face','Layer',null),snap('p'),g('doc','Layers','layers'),c('layers','GetReferences',['Stored']),c('entities','Remove',[r('p')]),snap('p'),doc()]);
  for(const blockOwner of [false,true])push('topology/'+blockOwner,[...init('Polyline3D',{blockOwner}),add(),snap('p'),c('p','InsertVertex',[{int:2},v(11,12,13)]),item('vertices',2,'inserted'),snap('inserted'),snap('p'),
    c('p','MoveVertex',[{int:0},{int:4}]),snap('p'),c('p','Reverse'),snap('p'),c('p','Reverse'),c('p','RemoveVertexAt',[{int:1}]),snap('p'),snap('first'),snap('end'),doc()]);
  for(const operation of [
    c('p','InsertVertex',[{int:-1},v(0,0,0)]),c('p','InsertVertex',[{int:5},v(0,0,0)]),c('p','InsertVertex',[{int:1},v({double:'7FF8000000000000'},0,0)]),
    c('p','RemoveVertexAt',[{int:-1}]),c('p','RemoveVertexAt',[{int:4}]),c('p','MoveVertex',[{int:0},{int:4}]),c('p','MoveVertex',[{int:-1},{int:0}])
  ])push('invalid/'+all.length,[...init(),add(),doc(),operation,snap('p'),doc()]);
  push('detached-topology',[...init(),c('p','InsertVertex',[{int:1},v(1,1,1)]),c('p','RemoveVertexAt',[{int:0}]),c('p','MoveVertex',[{int:0},{int:1}]),snap('p'),doc()]);
  push('minimum',[...init(),add(),c('p','RemoveVertexAt',[{int:0}]),c('p','RemoveVertexAt',[{int:0}]),c('p','RemoveVertexAt',[{int:0}]),snap('p'),doc()]);
  for(const seed of ['0','-1','1','34','9223372036854775807'])push('seed/'+seed,[...init(),add(),raw('doc','NumHandles',{long:seed}),c('p','InsertVertex',[{int:1},v(0,0,0)]),snap('p'),doc()]);
  for(const family of families)for(const tags of [4096,4097])push(`tag-budget/${family}/${tags}`,[...init(),add(),{method:'retained-create',id:'other',family},c('entities','Add',[r('other')]),g('other','EndSequenceRecord','foreignEnd'),raw('foreignEnd','XDataStart',{int:tags}),c('p','InsertVertex',[{int:1},v(0,0,0)]),snap('p'),doc()]);
  for(const channel of ['XData','XRecord','reactor','dictionary-default','header','parent-XData','extension','private']) {
    const setup=channel==='private'?[raw('first','HasPrivateData',true)]:channel==='extension'?[g('doc','Objects','db'),n('extension','Objects.DxfDictionary'),c('db','SetExtensionDictionary',[r('first'),r('extension')])]:channel==='reactor'?[n('source','Entities.Line'),c('entities','Add',[r('source')]),g('source','PersistentReactors','reactors'),c('reactors','Add',[r('first')])]:channel==='XRecord'?[g('doc','NamedObjects','root'),n('source','Objects.DxfXRecord'),g('source','Data','tags'),n('tag','IO.DxfTag',[{short:340},r('handle')]),c('tags','Add',[r('tag')]),c('root','Add',['source',r('source'),true])]:channel==='dictionary-default'?[g('doc','NamedObjects','root'),n('source','Objects.DxfDictionaryWithDefault'),s('source','Default',r('first')),c('root','Add',['source',r('source'),true])]:channel==='header'?[g('doc','DrawingVariables','variables'),n('variable','Header.HeaderVariable',['$REF',{short:330},r('handle')]),c('variables','AddCustomVariable',[r('variable')])]:[
      n('source','Entities.Line'),c('entities','Add',[r('source')]),n('appid','Tables.ApplicationRegistry',['REFS']),n('data','XData',[r('appid')]),g('data','XDataRecord','tags'),
      ...(channel==='parent-XData'?[g('p','Handle','handle')]:[]),n('tag','XDataRecord',[{enum:'XDataCode',value:1005},r('handle')]),c('tags','Add',[r('tag')]),g(channel==='parent-XData'?'first':'source','XData','metadata'),c('metadata','Add',[r('data')])];
    push('reference/'+channel,[...init(),add(),g('first','Handle','handle'),...setup,doc(),c('p','RemoveVertexAt',[{int:0}]),snap('p'),c('entities','Remove',[r('p')]),snap('p'),doc()]);
  }
  for(let seed=1;seed<=24;seed++){
    let random=seed;const next=n=>(random=(Math.imul(random,1664525)+1013904223)>>>0)%n;
    const steps=[...init(),add()];let count=4;
    for(let at=0;at<32;at++){
      const op=next(4);
      if(op===0){const index=next(count+1);steps.push(c('p','InsertVertex',[{int:index},v(next(100)/4,next(100)/8,next(100)/16)]));count++;}
      else if(op===1){steps.push(c('p','MoveVertex',[{int:next(count)},{int:next(count)}]));}
      else if(op===2&&count>2){steps.push(c('p','RemoveVertexAt',[{int:next(count)}]));count--;}
      else steps.push(c('p','Reverse'));
      steps.push(snap('p'),doc());
    }
    push('random/'+seed,steps);
  }
  return all;
}
