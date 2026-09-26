import { binaryDiagnosticInputs } from './binary-diagnostic-inputs.mjs';
// Deterministic input-only fixtures. Internal construction does not claim typed-reader admission.
const r=ref=>({ref}), h=(handleOf,extra={})=>({handleOf,...extra}), i=int=>({int}), sh=short=>({short});
const n=(id,type,args=[])=>({method:'new',id,value:{new:type,args}});
const g=(target,member,id)=>({method:'get',target,member,id});
const c=(target,member,args=[],signature)=>({method:'call',target,member,args,...(signature?{signature}:{})});
const put=(target,field,value)=>({method:'dependency-set',target,field,value});
const model=target=>({method:'dependency-model',target});
const check=(target='stored',database=r('db'))=>({method:'dependency-validate',target,database});
const init=(version=18)=>[n('doc','DxfDocument',[{enum:'Header.DxfVersion',value:version}]),g('doc','Objects','db'),g('db','Root','root'),g('doc','Entities','entities'),
  n('line','Entities.Line'),c('entities','Add',[r('line')]),n('leaf','Objects.DxfPlaceholder'),c('root','Add',['LEAF',r('leaf'),true]),g('doc','TextStyles','styles'),n('style','Tables.TextStyle',['DependencyStyle','txt.shx']),c('styles','Add',[r('style')])];
const register=(target='stored',owner=r('root'),extra={})=>({method:'dependency-register',target,document:r('doc'),owner,name:owner.ref==='root'?'STORED':null,...extra});
const resolve=(target='stored',extra={})=>({method:'dependency-resolve',target,document:r('doc'),children:[],objects:[],trace:'trace',...extra});
const field=(extra={})=>({method:'dependency-new',target:'doc',kind:'field',id:'stored',tags:[[100,'AcDbField'],[1,'AcObjProp'],[2,'retained expression'],[90,i(0)],[97,i(0)]],evaluator:'AcObjProp',code:'retained expression',...extra});
const snapshot=target=>({method:'snapshot',target});
const erase=target=>c('db','EraseOwnedTree',[r(target)]);
const point=(id='point',target=h('line'),near=0,vector=[1,2,3],slot=0,osnap=1)=>({method:'dependency-new',kind:'point',id,args:[i(slot),sh(osnap),target,sh(-1),i(7),near,{new:'Vector3',args:vector}]});
const association=(extra={})=>({method:'dependency-new',target:'doc',kind:'association',id:'stored',tags:[[100,'AcDbDimAssoc'],[330,h('dimension')],[90,i(1)],[70,sh(0)],[71,sh(0)]],dimension:h('dimension'),mask:1,trans:0,rotated:0,points:[r('point')],...extra});
const dimension=()=>[n('dimension','Entities.AlignedDimension'),c('entities','Add',[r('dimension')]),n('extension','Objects.DxfDictionary'),c('db','SetExtensionDictionary',[r('dimension'),r('extension')])];
export function sunStudyPacket(hours=[true,false,true],references=[h('leaf'),h('line'),'0',h('style')]) {
  return [[100,'AcDbSunStudy'],[90,i(0)],[1,'Study \\U+03A9'],[2,'Stored description'],[70,sh(-3)],[3,'SheetSet'],[290,true],[4,'Subset'],[291,false],[91,i(0)],[292,true],[93,i(-2147483648)],[94,i(2147483647)],[95,i(-1)],[73,sh(hours.length)],...hours.map(value=>[290,value]),
    [340,references[0]],[341,references[1]],[342,references[2]],[74,sh(1)],[75,sh(2)],[76,sh(3)],[77,sh(4)],[40,{double:'8000000000000000'}],[293,true],[294,false],[343,references[3]]];
}
const study=(tags=sunStudyPacket(),extra={})=>({method:'dependency-new',target:'doc',kind:'study',id:'stored',tags,...extra});
const fullField=(extra={},resolution={})=>[field(extra),register(),g('stored','References','before'),resolve('stored',resolution),model('stored'),model('before'),check(),model('trace')];
const fullAssociation=(extra={},registration={})=>[...dimension(),point(),association(extra),register('stored',r('extension'),{name:'ACAD_DIMASSOC',...registration}),resolve(),model('stored'),check(),model('trace')];
const fullStudy=(tags=sunStudyPacket())=>[study(tags),register(),g('stored','References','before'),resolve(),model('stored'),model('before'),check(),model('trace')];
export function storedDependenciesCorpus(){
  const all=[];const add=(name,category,steps)=>all.push({name:'stored-dependencies/'+name,category,request:{op:'document-ownership',steps}});
  for(const version of [13,14,15,16,17,18]) {
    add('field/profile/'+version,'field',[...init(version),...fullField(),{method:'dependency-internal',target:'stored',member:'ValidateSource',args:[r('doc')]},g('doc','DrawingVariables','variables'),put('variables','AcadVer',{enum:'Header.DxfVersion',value:version===18?17:18}),check(),{method:'dependency-internal',target:'stored',member:'ValidateSource',args:[r('doc')]}]);
    add('association/profile/'+version,'association',[...init(version),...fullAssociation(),g('doc','DrawingVariables','variables'),put('variables','AcadVer',{enum:'Header.DxfVersion',value:version===18?17:18}),check()]);
    add('study/profile/'+version,'study',[...init(version),...fullStudy(),g('doc','DrawingVariables','variables'),put('variables','AcadVer',{enum:'Header.DxfVersion',value:version===18?17:18}),check()]);
  }
  for(const code of [5,105,320,329,330,331,339,340,349,350,359,360,369,390,399,480,481,1005])for(const nullReference of [false,true]) {
    const tags=field().tags.concat([[code,nullReference?'0000':h('leaf',{pad:2,lower:true})]]);
    add('field/reference/'+code+'/'+nullReference,'field',[...init(),...fullField({tags}),erase('leaf'),check(),model('stored')]);
  }
  for(const count of [0,1,2,4,12]){
    const tags=field().tags.concat(Array.from({length:count},()=>[340,h('leaf')]),[[331,'0000']]);
    add('field/repetitions/'+count,'field',[...init(),...fullField({tags},{objects:[...Array.from({length:count},()=>h('leaf')), '0', '0000000000000000']}),g('stored','ReferencedObjects','objects'),model('objects')]);
  }
  for(const count of [0,1,2,4,16]){
    const children=Array.from({length:count},(_,j)=>(0xc01+j).toString(16));
    const tags=field().tags.concat(children.map(handle=>[360,handle]));
    const steps=[...init(),field({tags}),register('stored',r('root'),{handle:'C00',preserve:true})];
    children.forEach((handle,j)=>steps.push(field({id:'child'+j,tags:field().tags.concat([[340,'c00']])}),register('child'+j,r('stored'),{handle,preserve:true})));
    steps.push(g('stored','Children','priorChildren'),resolve('stored',{children}),model('stored'),model('priorChildren'),check());
    children.forEach((_,j)=>steps.push(resolve('child'+j),check('child'+j)));
    steps.push(c('db','Validate'),erase('stored'),model('stored'));add('field/children/'+count,'field-ownership',steps);
  }
  for(const variant of ['duplicate','wrong-type','wrong-owner','cycle','missing','undeclared','extension-exempt']){
    const steps=[...init(),field({tags:field().tags.concat([[360,'C01']])}),register('stored',r('root'),{handle:'C00',preserve:true}),field({id:'child'}),register('child',r('stored'),{handle:'C01',preserve:true})];
    let children=['C01'];
    if(variant==='duplicate')children.push('C01');
    if(variant==='wrong-type')children=[h('leaf')];
    if(variant==='wrong-owner')steps.push(put('child','Owner',r('root')));
    if(variant==='cycle')steps.push(put('stored','Owner',r('child')));
    if(variant==='missing')children=['ABC'];
    if(variant==='undeclared')children=[];
    if(variant==='extension-exempt'){children=[];steps[steps.length-2]=n('child','Objects.DxfDictionary');steps.push(put('stored','ExtensionDictionary',r('child')));}
    steps.push(resolve('stored',{children}),model('stored'),check(),model('trace'));
    add('field/child-invalid/'+variant,'field-ownership',steps);
  }
  for(const spelling of ['c01','C01','00c01','00000000000000000000C01'])add('field/canonical/'+spelling,'field',[...init(),field({tags:field().tags.concat([[340,spelling],[331,spelling]])}),register('stored',r('root'),{handle:'C00',preserve:true}),n('target','Objects.DxfPlaceholder'),register('target',r('root'),{name:'TARGET',handle:'C01',preserve:true}),resolve('stored',{objects:[spelling]}),model('stored'),model('trace'),check()]);
  for(const phase of [1,2,3])add('field/resolver-throw/'+phase,'resolver',[...init(),field({tags:field().tags.concat([[340,h('leaf')],[331,h('line')],[343,h('style')]])}),register(),g('stored','References','before'),resolve('stored',{hooks:[{at:phase,kind:'throw'}]}),model('stored'),model('before'),model('trace'),check(),resolve(),model('stored'),check()]);
  for(const family of ['field','association','study']){
    const make=family==='field'?fullField:family==='association'?fullAssociation:fullStudy;
    add(family+'/unresolved','source-guards',[...init(),...(family==='association'?[...dimension(),point(),association()]:family==='field'?[field()]:[study()]),check(),check('stored',null),register('stored',family==='association'?r('extension'):r('root'),family==='association'?{name:'ACAD_DIMASSOC'}:{}),check()]);
    add(family+'/source-invariants','source-guards',[...init(),...make(),n('foreign','DxfDocument'),g('foreign','Objects','foreignDb'),check('stored',r('foreignDb')),check('stored',null),{method:'dependency-internal',target:'stored',member:'CloneShell'},erase('stored'),model('stored')]);
    add(family+'/resolve-repeat','resolver',[...init(),...make(),g('stored','References','old'),resolve(),model('old'),model('stored'),model('trace'),check()]);
    add(family+'/resource-removal','integration',[...init(),...(family==='field'?fullField({tags:field().tags.concat([[340,h('line')],[343,h('style')]])}):make()),c('entities','Remove',[r('line')]),c('styles','Remove',[r('style')]),model('stored'),check()]);
    add(family+'/metadata','integration',[...init(),...make(),n('data','XData',[{new:'Tables.ApplicationRegistry',args:['STORED_META']}]),g('stored','XData','metadata'),c('metadata','Add',[r('data')]),g('doc','ApplicationRegistries','apps'),c('apps','GetReferences',['STORED_META']),erase('stored'),c('metadata','Remove',['STORED_META']),c('apps','Remove',['STORED_META']),check()]);
  }
  for(const mutation of ['dimension-slot','owner','alias-case','alias-strength','alias-remove','reactor-add','reactor-clear','backlink-add','backlink-remove','handle-dimension','handle-geometry']){
    const steps=[...init(),...dimension(),point(),association(),register('stored',r('extension'),{name:'ACAD_DIMASSOC'}),g('stored','PersistentReactors','reactors'),c('reactors','Add',[r('dimension')]),g('line','PersistentReactors','backlinks'),c('backlinks','Add',[r('stored')]),resolve(),check()];
    if(mutation==='dimension-slot')steps.push(put('dimension','ExtensionDictionary',null));
    if(mutation==='owner')steps.push(put('stored','Owner',r('root')));
    if(mutation.startsWith('alias')){steps.push(c('extension','Remove',['ACAD_DIMASSOC']));if(mutation!=='alias-remove')steps.push({method:'dependency-internal',target:'extension',member:'AddLoaded',args:[mutation==='alias-case'?'acad_dimassoc':'ACAD_DIMASSOC',r('stored'),mutation!=='alias-strength']});}
    if(mutation==='reactor-add')steps.push(c('reactors','Add',[r('line')]));
    if(mutation==='reactor-clear')steps.push(c('reactors','Clear'));
    if(mutation==='backlink-add')steps.push(g('dimension','PersistentReactors','dimensionReactors'),c('dimensionReactors','Add',[r('stored')]));
    if(mutation==='backlink-remove')steps.push(c('backlinks','Clear'));
    if(mutation==='handle-dimension')steps.push(put('dimension','Handle','123'));
    if(mutation==='handle-geometry')steps.push(put('line','Handle','123'));
    steps.push(check(),model('stored'));add('association/mutation/'+mutation,'association',steps);
  }
  for(const mask of Array.from({length:16},(_,j)=>j))for(const osnap of [1,3,13]){
    const ids=[0,1,2,3].filter(slot=>mask&(1<<slot));const steps=[...init(),...dimension()];
    ids.forEach(slot=>steps.push(point('p'+slot,h('line'),slot===0?{double:'8000000000000000'}:slot/7,[slot,1e99,-1e99],slot,osnap)));
    steps.push(association({mask,trans:mask&1,rotated:(mask>>>1)&1,points:ids.map(slot=>r('p'+slot))}),register('stored',r('extension'),{name:'ACAD_DIMASSOC'}),resolve(),model('stored'),check(),model('trace'));
    add('association/points/'+mask+'/'+osnap,'association',steps);
  }
  for(const wrong of ['leaf','style','dimension','missing'])add('association/geometry-target/'+wrong,'association',[...init(),...dimension(),point('point',wrong==='missing'?'ABC':h(wrong)),association(),register('stored',r('extension'),{name:'ACAD_DIMASSOC'}),resolve(),model('stored'),check()]);
  for(const registration of [{name:'other'},{name:'acad_dimassoc'},{name:'ACAD_DIMASSOC',hard:false}])add('association/owner-admission/'+JSON.stringify(registration),'association',[...init(),...fullAssociation({},registration)]);
  for(const length of [0,1,2,24,48])add('study/hours/'+length,'study',[...init(),...fullStudy(sunStudyPacket(Array.from({length},(_,j)=>j%3===0))),g('stored','RawHourFlags','hours'),model('hours')]);
  for(const references of [['0','00','000','0000'],[h('leaf'),h('leaf'),h('leaf'),h('leaf')],[h('style',{pad:2,lower:true}),h('style',{lower:true}),h('leaf'),'0'],['FAFA',h('line'),'0',h('style')]])add('study/links/'+JSON.stringify(references),'study',[...init(),...fullStudy(sunStudyPacket([false,true],references))]);
  for(const variant of ['owner','ancestor','unregistered-owner','cycle','dependency','missing-self','missing-root']){
    const steps=[...init(),n('parent','Objects.DxfDictionary'),c('root','Add',['PARENT',r('parent'),true]),study(),register('stored',r('parent'),{name:'STUDY'})];
    if(variant==='cycle'){steps.push(put('parent','Owner',r('stored')),resolve());}
    else if(variant.startsWith('missing'))steps.push(resolve('stored',{missing:[h(variant==='missing-self'?'stored':'root')]}));
    else{steps.push(resolve(),check());
      if(variant==='owner')steps.push(put('stored','Owner',r('root')));
      if(variant==='ancestor')steps.push(put('parent','Owner',null));
      if(variant==='unregistered-owner')steps.push(put('parent','Handle','F00'));
      if(variant==='dependency')steps.push({method:'dependency-unregister',target:'leaf',document:r('doc')});
    }
    steps.push(model('stored'),check(),model('trace'));add('study/ancestry/'+variant,'study-ownership',steps);
  }
  for(const phase of [1,2,3,4,5])add('study/resolver-throw/'+phase,'resolver',[...init(),study(),register(),resolve('stored',{hooks:[{at:phase,kind:'throw'}]}),model('stored'),model('trace'),check(),resolve(),model('stored'),check()]);
  for(const kind of ['field','study'])for(const invalid of ['foreign-identity','profile-callback','owner-callback']){
    const steps=[...init(),n('foreign','DxfDocument'),g('foreign','TextStyles','foreignStyles'),n('foreignStyle','Tables.TextStyle',['DependencyStyle','txt.shx']),c('foreignStyles','Add',[r('foreignStyle')]),g('doc','DrawingVariables','variables'),kind==='field'?field({tags:field().tags.concat([[343,h('style')]])}):study(),register()];
    const options=invalid==='foreign-identity'?{overrides:[[h('style'),r('foreignStyle')]]}:invalid==='profile-callback'?{hooks:[{at:1,kind:'set',target:r('variables'),member:'AcadVer',value:{enum:'Header.DxfVersion',value:17}}]}:{hooks:[{at:1,kind:'set',target:r('stored'),member:'Owner',value:r('leaf')}]};
    steps.push(resolve('stored',options),model('stored'),check(),model('trace'));add(kind+'/'+invalid,'resolver',steps);
  }
  let state=0x5f1e1d01;const rand=n=>(state=(Math.imul(state,1664525)+1013904223)>>>0)%n;
  for(let run=0;run<32;run++){
    const tags=field().tags.concat(Array.from({length:rand(30)},()=>[[330,331,340,343,350,360,390,480,320][rand(9)],rand(4)===0?'0':h(['leaf','line','style'][rand(3)],{pad:rand(3),lower:rand(2)!==0})]));
    const objects=Array.from({length:rand(12)},()=>rand(3)===0?'0':h(['leaf','line','style'][rand(3)]));
    add('field/random/'+run,'randomized',[...init(),...fullField({tags},{objects}),erase('leaf'),c('styles','Remove',[r('style')]),c('entities','Remove',[r('line')]),check(),model('stored')]);
  }
  for(const input of binaryDiagnosticInputs())add(input.name,'binary-diagnostics',[input.step]);
  return all;
}
