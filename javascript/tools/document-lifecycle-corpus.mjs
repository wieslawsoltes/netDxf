// Additional input-only document and object lifecycle scenarios. No expected outputs.
const r=ref=>({ref}), n=(id,type,args=[],signature)=>({method:'new',id,value:{new:type,args,...(signature?{signature}:{})}});
const g=(target,member,id)=>({method:'get',target,member,id});
const c=(target,member,args=[],signature,id)=>({method:'call',target,member,args,...(signature?{signature}:{}),...(id?{id}:{})});
const s=(target,member,value)=>({method:'set',target,member,value});
const item=(target,value,id)=>({method:'item',target,args:[value],id});
const snap=target=>({method:'snapshot',target}),model=target=>({method:'model',target});
const init=()=>[n('doc','DxfDocument'),g('doc','Entities','entities')];
const db=()=>[...init(),g('doc','Objects','db'),g('db','Root','root')];
const enumValue=(name,value)=>({enum:name,value}),arr=(array,values)=>({array,values});
export function documentLifecycleCorpus(){
  const all=[],add=(name,steps)=>all.push({name:'document-lifecycle/'+name,request:{op:'document-ownership',steps}});
  for(const version of [0,12,13,14,15,16,17,18,99])add('version/'+version,[n('doc','DxfDocument',[enumValue('Header.DxfVersion',version)]),snap('doc')]);
  for(const name of [null,'','0','00',' 0','0 ','0x0','ffffffffffffffff','10000000000000000','22','000022','ff','GG'])add('handle/'+String(name),[...init(),n('line','Entities.Line'),c('entities','Add',[r('line')]),c('doc','GetObjectByHandle',[name]),snap('doc')]);
  for(const name of [null,'','missing','MODEL','model','Model'])add('active/'+String(name),[...init(),s('entities','ActiveLayout',name),n('line','Entities.Line'),c('entities','Add',[r('line')]),snap('doc')]);
  const records=[['Layers','Tables.Layer',['A']],['Linetypes','Tables.Linetype',['A']],['TextStyles','Tables.TextStyle',['A','txt.shx']],['DimensionStyles','Tables.DimensionStyle',['A']],['ApplicationRegistries','Tables.ApplicationRegistry',['A']],['MlineStyles','Objects.MLineStyle',['A']],['Blocks','Blocks.Block',['A']]];
  for(const [table,type,args] of records){
    add('canonical/'+table,[...init(),g('doc',table,'table'),n('a',type,args),n('other',type,args),c('table','Add',[r('a')]),c('table','Add',[r('other')]),c('table','Contains',[r('other')],[type]),c('table','Remove',[r('other')]),snap('doc')]);
    add('rename-collision/'+table,[...init(),g('doc',table,'table'),n('a',type,args),n('b',type,args.map((v,i)=>i===0?'B':v)),c('table','Add',[r('a')]),c('table','Add',[r('b')]),s('a','Name','b'),snap('doc'),s('a','Name','a'),snap('doc')]);
    add('clear/'+table,[...init(),g('doc',table,'table'),n('a',type,args),c('table','Add',[r('a')]),c('table','Clear'),snap('doc')]);
  }
  for(const method of ['Add','Remove'])add('entity-null/'+method,[...init(),c('entities',method,[null],['Entities.EntityObject']),snap('doc')]);
  add('entity-cross-document',[...init(),n('other','DxfDocument'),g('other','Entities','others'),n('line','Entities.Line'),c('entities','Add',[r('line')]),c('others','Add',[r('line')]),c('others','Remove',[r('line')]),snap('doc'),snap('other'),c('entities','Remove',[r('line')]),c('others','Add',[r('line')]),snap('other')]);
  add('block-membership',[...init(),g('doc','Blocks','blocks'),n('block','Blocks.Block',['B']),g('block','Entities','members'),n('line','Entities.Line'),c('members','Add',[r('line')]),c('blocks','Add',[r('block')]),snap('doc'),n('point','Entities.Point'),c('members','Add',[r('point')]),snap('doc'),c('members','Remove',[r('line')]),snap('doc'),c('blocks','Remove',[r('block')]),snap('doc')]);
  add('group-members',[...init(),g('doc','Groups','groups'),n('group','Objects.Group',['G']),g('group','Entities','members'),n('a','Entities.Line'),n('b','Entities.Point'),c('members','Add',[r('a')]),c('groups','Add',[r('group')]),c('members','Add',[r('b')]),snap('doc'),c('entities','Remove',[r('a')]),c('members','Remove',[r('a')]),c('entities','Remove',[r('a')]),c('groups','Remove',[r('group')]),c('entities','Remove',[r('b')]),snap('doc')]);
  for(const mask of [0,1,2,4,8,16,32,64,128,256,512,1024,2047,4095,-1])add('layer-state/'+mask,[...init(),g('doc','Layers','layers'),g('layers','StateManager','states'),n('a','Tables.Layer',['A']),c('layers','Add',[r('a')]),s('a','IsVisible',false),s('a','IsLocked',true),s('a','IsFrozen',true),s('a','Plot',false),c('states','AddNew',['Saved']),item('states','Saved','state'),model('state'),s('state','Description','Zażółć 東京'),s('state','PaperSpace',true),{method:'las',target:'state'},s('a','IsVisible',true),s('a','IsLocked',false),s('states','Options',enumValue('Objects.LayerPropertiesRestoreFlags',mask)),c('states','Restore',['Saved']),model('a'),c('states','Update',['Saved']),model('state'),c('state','Clone',['Copy'],['String'],'clone'),model('clone'),c('states','RemoveAll'),snap('doc')]);
  for(const name of [null,'','other'])add('layer-state-missing/'+String(name),[...init(),g('doc','Layers','layers'),g('layers','StateManager','states'),c('states','Restore',[name]),c('states','Update',[name]),snap('doc')]);
  for(let count of [0,1,2,7,16])add('dictionary-adopt/'+count,[...db(),n('folder','Objects.DxfDictionary'),...Array.from({length:count},(_,i)=>[n('leaf'+i,'Objects.DxfPlaceholder'),c('folder','Add',['leaf'+i,r('leaf'+i),true])]).flat(),c('root','Add',['folder',r('folder'),true]),g('db','Items','items'),snap('items'),model('folder'),c('db','Validate'),c('db','Clone',[r('folder'),r('root'),'copy',null],undefined,'copy'),model('copy'),c('db','Validate'),snap('doc'),c('db','EraseOwnedTree',[r('folder')]),g('db','Items','after'),snap('after'),c('db','Validate'),model('folder')]);
  add('dictionary-alias',[...db(),n('leaf','Objects.DxfPlaceholder'),c('root','Add',['one',r('leaf'),true]),c('root','Add',['two',r('leaf'),false]),model('root'),c('db','EraseOwnedTree',[r('leaf')]),model('root'),model('leaf'),c('db','EraseOwnedTree',[r('leaf')]),snap('doc')]);
  for(const type of ['Objects.DxfPlaceholder','Objects.DxfDictionaryVariable','Objects.DxfXRecord'])add('clone-object/'+type,[...db(),n('leaf',type),c('root','Add',['value',r('leaf'),true]),c('db','CloneObject',[r('leaf'),r('root'),'clone',null],undefined,'clone'),model('clone'),c('db','Validate'),snap('doc')]);
  for(const channel of ['XRecord','XData','header','reactor','default']){
    const setup=channel==='XRecord'?[n('source','Objects.DxfXRecord'),g('source','Data','data'),n('tag','IO.DxfTag',[{short:330},r('handle')]),c('data','Add',[r('tag')]),c('root','Add',['source',r('source'),true])]:channel==='XData'?[n('source','Entities.Line'),n('registry','Tables.ApplicationRegistry',['APP']),n('data','XData',[r('registry')]),g('data','XDataRecord','tags'),n('tag','XDataRecord',[enumValue('XDataCode',1005),r('handle')]),c('tags','Add',[r('tag')]),g('source','XData','xdata'),c('xdata','Add',[r('data')]),c('entities','Add',[r('source')])]:channel==='header'?[g('doc','DrawingVariables','variables'),n('variable','Header.HeaderVariable',['$CUSTOM',{short:330},r('handle')]),c('variables','AddCustomVariable',[r('variable')])]:channel==='reactor'?[n('source','Entities.Line'),g('source','PersistentReactors','reactors'),c('reactors','Add',[r('leaf')]),c('entities','Add',[r('source')])]:[n('source','Objects.DxfDictionaryWithDefault'),s('source','Default',r('leaf')),c('root','Add',['source',r('source'),true])];
    add('erase-referenced/'+channel,[...db(),n('leaf','Objects.DxfPlaceholder'),c('root','Add',['leaf',r('leaf'),true]),g('leaf','Handle','handle'),...setup,c('db','EraseOwnedTree',[r('leaf')]),model('leaf'),c('db','Validate'),snap('doc')]);
  }
  add('extension-clone',[...db(),n('a','Entities.Line'),n('b','Entities.Point'),c('entities','Add',[r('a')]),c('entities','Add',[r('b')]),n('extension','Objects.DxfDictionary'),n('leaf','Objects.DxfPlaceholder'),c('extension','Add',['leaf',r('leaf'),true]),c('db','SetExtensionDictionary',[r('a'),r('extension')]),c('db','CloneExtensionDictionary',[r('a'),r('b'),null],undefined,'clone'),model('clone'),c('db','Validate'),c('db','EraseOwnedTree',[r('clone')]),model('clone'),g('b','ExtensionDictionary','detached'),snap('doc')]);
  for(const version of [13,14,15,16,17,18]){
    const start=[n('doc','DxfDocument',[enumValue('Header.DxfVersion',version)]),g('doc','Entities','entities'),g('doc','Objects','db'),g('db','Root','root')];
    add('sun/'+version,[...start,g('doc','Viewport','host'),n('sun','Objects.DxfSun'),c('db','SetSun',[r('host'),r('sun')]),model('sun'),c('db','Validate'),c('db','EraseOwnedTree',[r('sun')]),g('host','Sun','removed'),snap('doc')]);
    add('draw-order/'+version,[...start,n('line','Entities.Line'),n('point','Entities.Point'),c('entities','Add',[r('line')]),c('entities','Add',[r('point')]),g('line','Owner','block'),g('block','Record','record'),n('first','Objects.DxfSortOrderEntry',[r('line'),'100']),n('second','Objects.DxfSortOrderEntry',[r('point'),'200']),c('db','CreateSortentsTable',[r('record'),arr('Objects.DxfSortOrderEntry',[r('second'),r('first')]),true],undefined,'order'),g('order','Entries','entries'),snap('entries'),g('doc','DrawingVariables','drawingVariables'),c('drawingVariables','CustomValues',[],undefined,'customValues'),model('customValues'),c('db','Validate'),snap('doc'),c('db','EraseOwnedTree',[r('order')]),snap('doc')]);
  }
  add('sun-clone',[...db(),g('doc','Viewport','host'),g('doc','Views','views'),n('view','Tables.View',['SunView']),c('views','Add',[r('view')]),n('sun','Objects.DxfSun'),c('db','SetSun',[r('host'),r('sun')]),c('db','CloneSun',[r('sun'),r('view'),null],undefined,'clone'),model('clone'),c('db','Validate'),c('views','Remove',[r('view')]),c('db','EraseOwnedTree',[r('clone')]),c('views','Remove',[r('view')]),snap('doc')]);
  add('output-settings',[...db(),n('plot','Objects.PlotSettings'),c('db','AddPlotSettings',['Plan',r('plot')],undefined,'output'),model('output'),c('db','SetWipeoutVariables',[true],undefined,'wipeout'),model('wipeout'),c('db','SetWipeoutVariables',[false]),model('wipeout'),c('db','Validate'),c('db','EraseOwnedTree',[r('output')]),snap('doc')]);
  add('spatial-filter',[...db(),n('block','Blocks.Block',['B']),n('insert','Entities.Insert',[r('block')]),c('entities','Add',[r('insert')]),n('filter','Objects.DxfSpatialFilter'),c('db','SetSpatialFilter',[r('insert'),r('filter')]),model('filter'),c('db','Validate'),c('db','SetSpatialFilter',[r('insert'),r('filter')]),snap('doc')]);
  add('geodata',[...db(),g('doc','Blocks','blocks'),item('blocks','*Model_Space','block'),g('block','Record','record'),n('data','Objects.DxfGeoData',[r('record')]),c('db','SetGeoData',[r('data')]),c('db','GetGeoData',[r('record')]),model('data'),c('db','Validate'),c('db','SetGeoData',[r('data')]),c('db','EraseOwnedTree',[r('data')]),c('db','GetGeoData',[r('record')]),snap('doc')]);
  for(let seed=1;seed<=32;seed++){
    let state=seed;const random=n=>(state=(Math.imul(state,1664525)+1013904223)>>>0)%n,steps=[...init(),g('doc','Layers','layers')];
    for(let i=0;i<8;i++)steps.push(n('line'+i,'Entities.Line'));
    for(let i=0;i<80;i++){
      const id='line'+random(8),op=random(4);
      if(op===0)steps.push(c('entities','Add',[r(id)]));
      else if(op===1)steps.push(c('entities','Remove',[r(id)]));
      else if(op===2)steps.push(n('layer','Tables.Layer',['L'+random(5)]),s(id,'Layer',r('layer')));
      else steps.push(c('layers','Remove',['L'+random(5)]));
      steps.push(snap('doc'));
    }
    add('randomized/'+seed,steps);
  }
  return all;
}
