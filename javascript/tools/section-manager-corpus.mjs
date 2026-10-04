// Deterministic input-only SECTION_MANAGER lifecycle and callback fixtures.
const r=ref=>({ref}), a=(array,values)=>({array,values}), e=value=>({enum:'Header.DxfVersion',value});
const n=(id,type,args=[])=>({method:'new',id,value:{new:type,args}}),g=(target,member,id)=>({method:'get',target,member,id});
const c=(target,member,args=[],id)=>({method:'call',target,member,args,...(id?{id}:{})});
const s=(target,member,value)=>({method:'set',target,member,value}),snap=target=>({method:'snapshot',target});
const model=target=>({method:'manager-model',target}),detail=target=>({method:'model',target});
const seed=value=>({method:'retained-set',target:'doc',field:'NumHandles',value:{long:String(value)}});
const begin=(version=18)=>[n('doc','DxfDocument',[e(version)]),g('doc','Entities','entities'),g('doc','Objects','db'),g('db','Root','root'),g('doc','Classes','classes'),g('doc','DrawingVariables','variables')];
const sections=()=>[n('first','Entities.Section'),n('second','Entities.Section'),c('entities','Add',[r('first')]),c('entities','Add',[r('second')])];
const create=(members=[r('first'),r('second'),r('first')],flag=false)=>c('db','CreateSectionManager',[a('Entities.Section',members),flag],'manager');
const replace=(members,flag=true)=>c('manager','ReplaceSections',[a('Entities.Section',members),flag]);
const callback=(action,members,options={})=>({method:'manager-call',action,target:action==='create'?'db':'manager',members,flag:true,log:'calls',...(action==='create'?{id:'manager'}:{}),...options});
const state=()=>[snap('doc'),g('db','Items','allObjects'),snap('allObjects'),c('db','Validate')];
const end=()=>[c('db','EraseSectionManager',[r('manager')]),model('manager'),c('entities','Remove',[r('first')]),...state()];
const klass=(count=37)=>[n('class','DxfClass',['SECTION_MANAGER','AcDbSectionManager','ObjectDBX Classes']),s('class','ProxyFlags',{short:1024}),s('class','InstanceCount',count===null?null:{int:count}),c('classes','Add',[r('class')])];
export function sectionManagerCorpus(){
  const all=[],add=(name,steps)=>all.push({name:'section-manager/'+name,request:{op:'document-ownership',steps}});
  for(const version of [13,14,15,16,17,18])for(const flag of [false,true])for(const count of [0,1,3]){
    add(`create/${version}/${flag}/${count}`,[...begin(version),...(version>=15?sections():[]),create(Array.from({length:count},(_,i)=>r(i%2?'second':'first')),flag),model('manager'),...state(),c('entities','Remove',[r('first')]),...end()]);
  }
  for(const count of [null,0,1,37])add('class/'+count,[...begin(),...sections(),...klass(count),create(),detail('class'),g('manager','Sections','oldSections'),g('manager','Tags','oldTags'),replace([r('second')]),snap('oldSections'),detail('oldTags'),model('manager'),...end(),detail('class')]);
  for(const [property,value] of [['ApplicationName','Wrong'],['ProxyFlags',{short:0}],['WasProxy',true],['IsEntity',true]])add('class-conflict/'+property,[...begin(),...sections(),...klass(),s('class',property,value),create(),...state()]);
  add('class-cpp-conflict',[...begin(),...sections(),n('class','DxfClass',['SECTION_MANAGER','Wrong','ObjectDBX Classes']),s('class','ProxyFlags',{short:1024}),c('classes','Add',[r('class')]),create(),...state()]);
  add('edited-class-retained',[...begin(),...sections(),...klass(),create(),s('class','ApplicationName','Inspection metadata'),s('class','ProxyFlags',{short:123}),s('class','IsEntity',true),s('class','WasProxy',true),s('class','InstanceCount',{int:37}),...end(),detail('class')]);
  for(const action of ['create','replace'])for(const count of [0,1,4,65536,65537])add(`capacity/${action}/${count}`,[...begin(),...sections(),...(action==='replace'?[create()]:[]),callback(action,[r('first')],{repeat:count}),snap('calls'),g('manager','Sections','members'),g('members','Count','size'),g('manager','Tags','tags'),g('tags','Count','packetSize'),...state()]);
  for(const action of ['create','replace'])for(const stage of ['get','move','current','dispose'])for(const kind of ['throw','reenter','catch-reenter']){
    add(`enumeration/${action}/${stage}/${kind}`,[...begin(),...sections(),...(action==='replace'?[create()]:[]),callback(action,[r('first')],{hooks:[{stage,kind}]}),snap('calls'),...state(),...(action==='create'?[create()]:[replace([r('second')])]),model('manager'),...end()]);
  }
  for(const action of ['create','replace'])for(const list of [null,[null],[]])add(`null/${action}/${JSON.stringify(list)}`,[...begin(),...sections(),...(action==='replace'?[create()]:[]),callback(action,list),snap('calls'),...state()]);
  for(const action of ['create','replace'])for(const fault of ['detached','foreign','removed']){
    const setup=fault==='foreign'?[n('other','DxfDocument',[e(18)]),g('other','Entities','otherEntities'),n('invalid','Entities.Section'),c('otherEntities','Add',[r('invalid')])]:[n('invalid','Entities.Section'),...(fault==='removed'?[c('entities','Add',[r('invalid')]),c('entities','Remove',[r('invalid')])]:[])];
    add(`identity/${action}/${fault}`,[...begin(),...sections(),...setup,...(action==='replace'?[create()]:[]),callback(action,[r('first'),r('invalid')]),snap('calls'),...state()]);
  }
  for(const action of ['create','replace'])for(const stage of ['get','dispose'])for(const fault of ['profile','anchor','root-reactor','section-reactor','detach','seed']){
    const mutation={profile:{kind:'set',target:r('variables'),member:'AcadVer',value:e(14)},anchor:{kind:'call',target:r('root'),member:'Add',args:['ACAD_SECTION_MANAGER',r('placeholder'),true]},'root-reactor':{kind:'call',target:r('rootReactors'),member:'Add',args:[r('placeholder')]},'section-reactor':{kind:'call',target:r('sectionReactors'),member:'Add',args:[r('placeholder')]},detach:{kind:'call',target:r('entities'),member:'Remove',args:[r('second')]},seed:{kind:'set',target:r('doc'),member:'NumHandles',value:{long:'9223372036854775807'}}}[fault];
    add(`mutation/${action}/${stage}/${fault}`,[...begin(),...sections(),n('placeholder','Objects.DxfPlaceholder'),g('root','PersistentReactors','rootReactors'),g('second','PersistentReactors','sectionReactors'),...(action==='replace'?[create([r('first')])]:[]),callback(action,[r('second')],{hooks:[{stage,...mutation}]}),snap('calls'),...state(),...(action==='replace'?[model('manager')]:[])]);
  }
  for(const value of [0n,-1n,9223372036854775807n,9223372036854775806n,1n])add('allocation/'+value,[...begin(),...sections(),seed(value),create(),...state()]);
  add('allocation-owner-held',[...begin(),n('block','Blocks.Block',['Reserved']),g('block','AttributeDefinitions','definitions'),n('definition','Entities.AttributeDefinition',['TAG']),c('definitions','Add',[r('definition')]),n('insert','Entities.Insert',[r('block')]),c('entities','Add',[r('insert')]),g('insert','Attributes','attributes'),{method:'item',target:'attributes',args:[{int:0}],id:'attribute'},g('attribute','Handle','attributeHandle'),...sections(),{method:'manager-seed-from',target:'doc',handle:r('attributeHandle')},create(),...state()]);
  for(const anchor of ['ACAD_SECTION_MANAGER','acad_section_manager'])add('occupied-anchor/'+anchor,[...begin(),...sections(),n('placeholder','Objects.DxfPlaceholder'),c('root','Add',[anchor,r('placeholder'),false]),create(),...state()]);
  for(const code of ['SECTION_MANAGER','SECTIONMANAGER','sectionmanager'])add('existing-packet/'+code,[...begin(),...sections(),{method:'manager-load',target:'doc',members:[r('first')],code,id:'loaded'},c('root','Remove',['ACAD_SECTION_MANAGER']),create(),...state()]);
  for(const code of ['SECTION_MANAGER','SECTIONMANAGER'])for(const hardOwner of [false,true])for(const anchor of ['ACAD_SECTION_MANAGER','acad_section_manager'])add(`loaded/${code}/${hardOwner}/${anchor}`,[...begin(),...sections(),{method:'manager-load',target:'doc',members:[r('first'),r('second'),r('first')],flag:false,code,hardOwner,anchor,id:'manager'},model('manager'),replace([r('second')]),model('manager'),...end()]);
  for(const fault of ['unresolved','wrong-target','wrong-anchor'])add('loader-reject/'+fault,[...begin(),...sections(),{method:'manager-load',target:'doc',members:[r('first')],...(fault==='unresolved'?{handles:['FFFF']}:fault==='wrong-target'?{handles:['0']}:{anchor:'Wrong'}),id:'manager'},...state()]);
  for(const fault of ['profile','anchor','reactors','root-owner','erased']){
    const mutation={profile:[s('variables','AcadVer',e(17))],anchor:[c('root','Remove',['ACAD_SECTION_MANAGER'])],reactors:[g('manager','PersistentReactors','reactors'),c('reactors','Clear')],'root-owner':[{method:'manager-set-owner',target:'manager',owner:r('doc')}],erased:[c('db','EraseSectionManager',[r('manager')])]}[fault];
    add('erase-reject/'+fault,[...begin(),...sections(),create(),...mutation,c('db','EraseSectionManager',[r('manager')]),replace([r('second')]),...state()]);
  }
  add('aliases-and-owned-metadata',[...begin(),...sections(),create(),n('extension','Objects.DxfDictionary'),n('note','Objects.DxfXRecord'),c('extension','Add',['NOTE',r('note'),true]),c('extension','Add',['ALIAS',r('note'),false]),c('root','Add',['MANAGER_ALIAS',r('manager'),true]),c('db','SetExtensionDictionary',[r('manager'),r('extension')]),...end(),g('note','IsErased','deadNote'),g('extension','IsErased','deadExtension')]);
  for(const code of [320,330,340,350,360])add('incoming-xrecord/'+code,[...begin(),...sections(),create(),g('manager','Handle','handle'),n('source','Objects.DxfXRecord'),g('source','Data','data'),n('tag','IO.DxfTag',[{short:code},r('handle')]),c('data','Add',[r('tag')]),c('root','Add',['Incoming',r('source'),true]),c('db','EraseSectionManager',[r('manager')]),model('manager'),c('data','Clear'),...end()]);
  add('ordinary-erase-clone-rejected',[...begin(),...sections(),create(),c('db','EraseOwnedTree',[r('manager')]),c('db','CloneObject',[r('manager'),r('root'),'COPY',null]),model('manager'),...state()]);
  for(let value=1;value<=24;value++){
    let random=value;const rand=n=>(random=(Math.imul(random,1664525)+1013904223)>>>0)%n,steps=[...begin(),...sections(),create()];
    for(let i=0;i<24;i++)steps.push(replace(Array.from({length:rand(9)},()=>r(rand(2)?'first':'second')),!!rand(2)),model('manager'),c('entities','Remove',[r('first')]));
    add('random/'+value,[...steps,...end()]);
  }
  return all;
}
