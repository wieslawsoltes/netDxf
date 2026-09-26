// Inputs only. Expected values come from the unchanged, pinned C# assembly.
import {D,I,R,V,A,E} from './geometry-corpus.mjs';
const N=(type,args=[],id='p',hidden=false)=>({kind:'new',type:'Objects.'+type,args,id,...(hidden?{nonPublic:true}:{})});
const S=(member,value,target='p')=>({kind:'set',target,member,value});
const G=(member,id,target='p')=>({kind:'get',target,member,id});
const C=(member,args=[],id,target='p',hidden=false)=>({kind:'call',target,member,args,...(id?{id}:{}),...(hidden?{nonPublic:true}:{})});
const snap=(target='p')=>({kind:'snapshot',target});
const index=(target,at,id)=>({kind:'index',target,args:[I(at)],id});
const put=(target,at,value)=>({kind:'set-index',target,args:[I(at)],value});
const eq=(a,b)=>({kind:'reference-equals',args:[R(a),R(b)]});
const host=(id='host',name='Host')=>({kind:'new',type:'Blocks.BlockRecord',args:[name],id,nonPublic:true});
const geo=()=>[host(),N('DxfGeoData',[R('host')])];
const point=(i=0)=>({new:'Objects.DxfGeoMeshPoint',args:[V('Vector2',i,i+1),V('Vector2',i+10,i+20)]});
const face=(a=0,b=1,c=2)=>({new:'Objects.DxfGeoMeshFace',args:[I(a),I(b),I(c)]});
const points=count=>A('Objects.DxfGeoMeshPoint',Array.from({length:count},(_,i)=>point(i)));
const faces=(...items)=>A('Objects.DxfGeoMeshFace',items);
const bytes=items=>A('Byte',items.map(byte=>({byte})));
const chunkArray=chunks=>A('Byte[]',chunks.map(chunk=>chunk===null?null:bytes(chunk)));
const label=value=>D(value).double;
const scalars=[-Infinity,-Number.MAX_VALUE,-1,-Number.MIN_VALUE,-0,0,Number.MIN_VALUE,1,1e308,Infinity,NaN];
export function geoDataVbaCorpus(){
  const out=[],add=(name,category,steps)=>out.push({name:'geodata-vba/'+name,category,request:{steps}});
  add('geo/defaults','geodata',[...geo(),C('CloneShell',[],'clone','p',true),snap(),snap('clone'),{kind:'get',target:'p',member:'DatabaseReferences',id:'references',nonPublic:true}]);
  add('geo/null-host','geodata',[N('DxfGeoData',[null]),N('DxfGeoData',[],'loaded',true),C('SetLoadedHost',[null],null,'loaded',true),snap('loaded')]);
  for(const field of ['HorizontalUnitScale','VerticalUnitScale','UserScaleFactor','SeaLevelElevation','CoordinateProjectionRadius'])
    for(const value of scalars)add(`scalar/${field}/${label(value)}`,'geodata',[
      ...geo(),S(field,D(value)),snap(),C('CloneShell',[],undefined,'p',true)]);
  for(const [field,size] of [['DesignPoint',3],['ReferencePoint',3],['UpDirection',3],['NorthDirection',2]])
    for(let component=0;component<size;component++)for(const value of scalars){
      const v=Array(size).fill(0);v[component]=value;
      add(`vector/${field}/${component}/${label(value)}`,'geodata',[
        ...geo(),S(field,V('Vector'+size,...v)),G(field,'copy'),S('X',D(99),'copy'),snap(),C('CloneShell',[],undefined,'p',true)]);
    }
  for(const [field,enumType] of [['CoordinateType','Objects.DxfGeoCoordinateType'],['ScaleEstimation','Objects.DxfGeoScaleEstimation'],
      ['HorizontalUnits','Units.DrawingUnits'],['VerticalUnits','Units.DrawingUnits']])
    for(const value of [-2147483648,-1,0,1,2,3,4,5,6,24,25,2147483647])
      add(`enum/${field}/${value}`,'geodata',[...geo(),S(field,E(enumType,value)),snap(),C('CloneShell',[],undefined,'p',true)]);
  const texts=[null,'','plain','line\nline','literal^J','literal^j','CR\rLF\n','nul\0','😀 Zażółć 東京',
    {utf16:[0xd800]},{utf16:[0xdc00]},{utf16:[0xd800,0xdc00]},{utf16:[0xd800,65]},'x'.repeat(1025)];
  for(const field of ['CoordinateSystemDefinition','GeoRssTag','ObservationFrom','ObservationTo','ObservationCoverage'])
    texts.forEach((value,i)=>add(`text/${field}/${i}`,'geodata',[...geo(),S(field,value),snap(),C('CloneShell',[],undefined,'p',true)]));
  for(const value of scalars)for(const target of [false,true])add(`point/${target}/${label(value)}`,'geo-mesh',[
    N('DxfGeoMeshPoint',target?[V('Vector2',2,3),V('Vector2',value,4)]:[V('Vector2',value,4),V('Vector2',2,3)]),
    G('Source','source'),G('Target','target'),S('X',D(77),'source'),S('Y',D(88),'target'),snap()]);
  for(let index=0;index<3;index++)for(const value of [-2147483648,-1,0,1,2147483647]){
    const args=[0,1,2];args[index]=value;
    add(`face/${index}/${value}`,'geo-mesh',[N('DxfGeoMeshFace',args.map(I)),G('First','first'),G('Second','second'),G('Third','third')]);
  }
  for(const [name,pts,fs] of [['empty',points(0),faces()],['valid',points(3),faces(face())],
    ['bad-face',points(2),faces(face())],['null-points',null,faces()],['null-faces',points(3),null],
    ['null-point',A('Objects.DxfGeoMeshPoint',[point(),null]),faces()],['null-face',points(3),faces(null)],
    ['both-nulls',null,null],['duplicate-face',points(1),faces(face(0,0,0))]]){
    add('mesh/'+name,'geo-mesh',[...geo(),C('SetMesh',[points(3),faces(face())]),G('MeshPoints','points'),G('MeshFaces','faces'),
      C('SetMesh',[pts,fs]),snap(),snap('points'),snap('faces'),C('CloneShell',[],'clone','p',true)]);
  }
  for(const field of ['MeshPoints','MeshFaces'])for(const op of ['Add','Insert','set_Item','RemoveAt'])for(const at of [-1,0,1])
    add(`collection/${field}/${op}/${at}`,'geo-mesh',[...geo(),G(field,'list'),C(op,op==='Add'?[null]:op==='RemoveAt'?[I(at)]:[I(at),null],undefined,'list'),snap()]);
  for(const action of ['remove-point','clear-faces','replace-point','self-replace','clone-edit','enumerator']){
    const steps=[...geo(),C('SetMesh',[points(3),faces(face())]),G('MeshPoints','points'),G('MeshFaces','faces')];
    if(action==='remove-point')steps.push(C('RemoveAt',[I(2)],undefined,'points'));
    if(action==='clear-faces')steps.push(C('Clear',[],undefined,'faces'));
    if(action==='replace-point')steps.push(put('points',0,point(8)));
    if(action==='self-replace')steps.push(C('SetMesh',[R('points'),R('faces')]));
    if(action==='clone-edit')steps.push(C('CloneShell',[],'clone','p',true),G('MeshPoints','copied','clone'),index('points',0,'a'),index('copied',0,'b'),eq('a','b'),C('Clear',[],undefined,'copied'),snap('clone'));
    if(action==='enumerator')steps.push(C('GetEnumerator',[],'it','points'),C('MoveNext',[],undefined,'it'),C('Add',[point(7)],undefined,'points'),C('MoveNext',[],undefined,'it'));
    steps.push(snap(),C('CloneShell',[],undefined,'p',true));add('mesh-state/'+action,'geo-mesh',steps);
  }
  for(const target of ['host','none','wrong']) {
    add('host-remap/'+target,'geodata',[...geo(),host('other','Destination'),N('DxfXRecord',[],'wrong'),C('CloneShell',[],'clone','p',true),
      C('CopyDatabaseReferencesTo',[R('clone'),{resolver:[[R('host'),target==='host'?R('other'):target==='wrong'?R('wrong'):null]]}],undefined,'p',true),
      snap(),snap('clone')]);
  }
  for(const count of [0,1,126,127,128,254,255,300,1024,4097]){
    const payload=Array.from({length:count},(_,i)=>(i*37+3)&255);
    add('vba/data/'+count,'vba',[N('DxfVbaProject'),S('Data',bytes(payload)),G('Data','data'),G('Chunks','chunks'),
      ...(count?[put('data',0,{byte:255}),index('chunks',0,'chunk'),put('chunk',0,{byte:254})]:[]),
      snap(),C('CloneShell',[],'clone','p',true),S('Data',bytes([])),snap('clone')]);
  }
  for(const chunks of [[],[[]],[[],[]],[[1,2],[],[3,4]],[[1],null],[[4],Array(128).fill(7)],Array.from({length:4},()=>Array(127).fill(3))])
    add('vba/chunks/'+out.length,'vba',[N('DxfVbaProject'),S('Data',bytes([9,8,7])),C('SetChunks',[chunkArray(chunks)]),snap(),C('CloneShell',[],undefined,'p',true)]);
  for(const op of ['data','chunks'])add('vba/null/'+op,'vba',[N('DxfVbaProject'),S('Data',bytes([9,8,7])),
    op==='data'?S('Data',null):C('SetChunks',[null]),snap()]);
  add('vba/snapshot-versus-internal','vba',[N('DxfVbaProject'),C('SetChunks',[chunkArray([[1,2],[],[3]])]),
    G('Chunks','snapshot'),{kind:'get',target:'p',member:'StoredChunks',id:'stored',nonPublic:true},
    index('snapshot',0,'public'),index('stored',0,'internal'),put('public',0,{byte:77}),snap(),
    put('internal',0,{byte:88}),snap(),C('CloneShell',[],'copy','p',true),index('stored',0,'retained'),put('retained',1,{byte:44}),snap('copy')]);
  for(const at of [-1,0,2])add('vba/byte-index/'+at,'vba',[N('DxfVbaProject'),S('Data',bytes([1,2])),G('Data','bytes'),
    index('bytes',at,'item'),put('bytes',at,{byte:9}),snap('bytes'),snap()]);
  return out;
}
