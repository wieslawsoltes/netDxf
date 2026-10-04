// Input-only tests of the pinned public values and internal retained packet API.
const r = ref => ({ref}), i = int => ({int}), d = double => ({double});
const n = (id, type, args = []) => ({method:'new', id, value:{new:type, args}});
const g = (target, member, id) => ({method:'get', target, member, id});
const c = (target, member, args = [], id) => ({method:'call', target, member, args, ...(id ? {id} : {})});
const at = (target, index, id) => ({method:'item', target, args:[i(index)], id});
const set = (target, member, value) => ({method:'set', target, member, value});
const model = target => ({method:'geometry-model', target}), snap = target => ({method:'snapshot', target});
const same = (a, b) => ({method:'same', args:[r(a), r(b)]});
const v = (x, y, z) => ({new:'Vector3', args:[x, y, z]});
const arr = (array, values) => ({array, values});
export function tableGeometryPacket({rows = 2, columns = 3, cells = 1, count = 1, reference = '0'} = {}) {
  const tags = [[100, 'AcDbTableGeometry'], [90, i(rows)], [91, i(columns)], [92, i(cells)]];
  for (let k = 0; k < cells; k++) {
    tags.push([93, i(-2147483648 + k)], [40, d('8000000000000000')], [41, -20], [330, reference], [94, i(count)]);
    for (let j = 0; j < count; j++) tags.push([10,-1-j], [20,2], [30,3], [11,4], [21,5], [31,6+j], [43,-7], [44,8+j], [45,9], [46,-10], [95,i(-2147483648+j)]);
  }
  return tags;
}
const init = (version = 18) => [n('doc','DxfDocument',[{enum:'Header.DxfVersion',value:version}]),g('doc','Entities','entities'),g('doc','TextStyles','styles'),n('styleA','Tables.TextStyle',['A','txt.shx']),n('styleB','Tables.TextStyle',['B','txt.shx']),c('styles','Add',[r('styleA')]),c('styles','Add',[r('styleB')])];
const load = (tags = tableGeometryPacket(), extra = {}) => ({method:'geometry-load',target:'doc',id:'geometry',tags,...extra});
const edit = (members, extra = {}) => ({method:'geometry-call',action:'replace',target:'geometry',rows:2,columns:3,members,log:'log',...extra});
const content = (id='content', values = [-1,2,3,4,5,6,-7,8,9,-10]) => n(id,'Objects.DxfStoredTableCellGeometry',[v(...values.slice(0,3)),v(...values.slice(3,6)),...values.slice(6),i(-2147483648)]);
const cell = (id='cell', reference=null) => n(id,'Objects.DxfStoredTableGeometryCell',[i(-2147483648),d('8000000000000000'),-20,reference,arr('Objects.DxfStoredTableCellGeometry',[r('content')])]);
export function tableGeometryCorpus() {
  const all = [], add = (name, steps) => all.push({name:'table-geometry/'+name,request:{op:'document-ownership',steps}});
  for (let scalar=0;scalar<12;scalar++) for(const value of [d('7FF8000000000042'),d('FFF8000000000042'),d('7FF0000000000000'),d('FFF0000000000000'),d('8000000000000000'),-1e100]) {
    const values=Array(12).fill(1);values[scalar]=value;
    add(`scalar/${scalar}/${JSON.stringify(value)}`,[content('content',values.slice(0,10)),n('cell','Objects.DxfStoredTableGeometryCell',[i(0),values[10],values[11],null,arr('Objects.DxfStoredTableCellGeometry',[r('content')])]),model('content'),model('cell')]);
  }
  for(const count of [0,1,2,17,95325,95326])add('constructor-count/'+count,[content(),{method:'geometry-call',action:'cell',args:[i(-1),-1,-2,null],members:[r('content')],repeat:count,log:'log',id:'cell'},snap('log'),g('cell','Geometry','data'),g('data','Count','count')]);
  for(const members of [null,[null]])add('constructor-null/'+JSON.stringify(members),[content(),{method:'geometry-call',action:'cell',args:[i(0),1,2,null],members,log:'log',id:'cell'},snap('log')]);
  for(const action of ['cell','replace']) for(const stage of ['get','move','current','dispose']) for(const fault of ['throw','catch-reenter','reenter']) {
    if(action==='cell'&&fault!=='throw')continue;
    add(`enumeration/${action}/${stage}/${fault}`,[...init(),load(),content(),cell(),{method:'geometry-call',target:'geometry',action,args:[i(1),2,3,null],rows:7,columns:8,members:[r(action==='cell'?'content':'cell')],hooks:[{stage,kind:fault}],log:'log'},snap('log'),model('geometry'),edit([]),model('geometry')]);
  }
  for(const action of ['cell','replace'])add('null-enumerator/'+action,[...init(),load(),content(),cell(),{method:'geometry-call',target:'geometry',action,args:[i(1),2,3,null],rows:7,columns:8,members:[],nullEnumerator:true,log:'log'},snap('log'),model('geometry')]);
  for(const version of [13,14,15,16,17,18])for(const count of [0,1,2])add(`source/${version}/${count}`,[...init(version),load(tableGeometryPacket({count})),model('geometry'),g('geometry','Cells','old'),edit([],{rows:0,columns:0}),model('geometry'),model('old')]);
  const base=tableGeometryPacket();
  for(let index=0;index<base.length;index++) {
    add('missing-tag/'+index,[...init(),load(base.filter((_,i)=>i!==index)),snap('doc')]);
    const tags=base.map(row=>row.slice());tags.splice(index,0,[1,'unexpected']);add('extra-tag/'+index,[...init(),load(tags),snap('doc')]);
  }
  for(const index of [1,2,3,8])for(const value of [-1,1048577,2147483647]) {
    const tags=base.map(row=>row.slice());tags[index][1]=i(value);add(`bad-count/${index}/${value}`,[...init(),load(tags),snap('doc')]);
  }
  for(const index of [5,6,9,10,11,12,13,14,15,16,17,18])for(const value of [d('7FF8000000000042'),d('7FF0000000000000')]) {
    const tags=base.map(row=>row.slice());tags[index][1]=value;add(`nonfinite-packet/${index}/${value.double}`,[...init(),load(tags),snap('doc')]);
  }
  for(const rows of [-1,0,1,1048576,1048577])for(const columns of [-1,0,1,1048576,1048577])add(`dimensions/${rows}/${columns}`,[...init(),load(),edit([],{rows,columns}),snap('log'),model('geometry')]);
  for(const members of [null,[null]])add('replacement-null/'+JSON.stringify(members),[...init(),load(),edit(members),model('geometry')]);
  add('snapshot-noop',[...init(),load(),g('geometry','Payload','oldTags'),g('geometry','Cells','oldCells'),g('geometry','References','oldRefs'),content(),cell(),edit([r('cell')]),snap('log'),g('geometry','Payload','newTags'),g('geometry','Cells','newCells'),same('oldTags','newTags'),same('oldCells','newCells'),edit([r('cell'),r('cell')],{rows:77,columns:88}),model('geometry'),model('oldTags'),model('oldCells'),model('oldRefs')]);
  for(const stage of ['get','move','current','dispose'])add('profile-mutation/'+stage,[...init(),load(),content(),cell(),g('doc','DrawingVariables','variables'),edit([r('cell')],{hooks:[{stage,kind:'set',target:r('variables'),member:'AcadVer',value:{enum:'Header.DxfVersion',value:17}}]}),snap('log'),model('geometry')]);
  for(const reference of ['0','0000',{handleOf:'styleA',pad:2,lower:true}])add('lexical/'+JSON.stringify(reference),[...init(),load(tableGeometryPacket({reference})),g('geometry','Cells','cells'),at('cells',0,'first'),edit([r('first')],{rows:99,columns:88}),model('geometry')]);
  add('dependency-replacement',[...init(),load(tableGeometryPacket({reference:{handleOf:'styleA'}})),g('geometry','References','old'),content(),cell('cell',r('styleB')),edit([r('cell'),r('cell')]),c('styles','Remove',[r('styleA')]),c('styles','Remove',[r('styleB')]),model('old'),model('geometry'),edit([]),c('styles','Remove',[r('styleB')]),snap('doc')]);
  add('foreign-reference',[...init(),load(),n('foreign','DxfDocument'),g('foreign','TextStyles','foreignStyles'),n('foreignStyle','Tables.TextStyle',['B','txt.shx']),c('foreignStyles','Add',[r('foreignStyle')]),content(),cell('cell',r('foreignStyle')),edit([r('cell')]),model('geometry')]);
  add('detached-reference',[...init(),load(),n('detached','Tables.TextStyle',['B','txt.shx']),content(),cell('cell',r('detached')),edit([r('cell')]),model('geometry')]);
  for(const registered of [false,true])add('unresolved/'+registered,[...init(),load(base,{register:registered,resolve:false}),edit([]),model('geometry')]);
  for(const count of [209714,209715])add('maximum-cells/'+count,[...init(),load(),n('empty','Objects.DxfStoredTableGeometryCell',[i(0),0,0,null,arr('Objects.DxfStoredTableCellGeometry',[])]),edit([r('empty')],{rows:0,columns:0,repeat:count}),snap('log'),g('geometry','Payload','payload'),g('payload','Count','tags'),g('geometry','Cells','cells'),g('cells','Count','count')]);
  for (const kind of ['attribute', 'endblock', 'layout-viewport', 'definition']) {
    const setup = kind === 'layout-viewport'
      ? [g('doc','Layouts','layouts'),n('sheet','Objects.Layout',['Sheet']),c('layouts','Add',[r('sheet')]),g('sheet','Viewport','target')]
      : [g('doc','Blocks','blocks'),n('block','Blocks.Block',['TargetBlock']),g('block','AttributeDefinitions','defs'),n('definition','Entities.AttributeDefinition',['TAG']),c('defs','Add',[r('definition')]),n('insert','Entities.Insert',[r('block')]),c('entities','Add',[r('insert')]),g('insert','Attributes','attributes'),at('attributes',0,'attribute'),{method:'geometry-internal-get',target:'block',member:'End',id:'endblock'},{method:'new',id:'target',value:r(kind)}];
    const remove = kind === 'layout-viewport' ? c('layouts','Remove',[r('sheet')]) : kind === 'attribute' ? c('entities','Remove',[r('insert')]) : kind === 'definition' ? c('defs','Remove',['TAG']) : c('blocks','Remove',[r('block')]);
    add('owner-held/'+kind,[...init(),...setup,load(tableGeometryPacket({reference:{handleOf:'target',pad:2,lower:true}})),model('geometry'),remove,model('geometry'),edit([]),remove,snap('doc')]);
  }
  let state=0x543ab911;const rand=n=>(state=(Math.imul(state,1664525)+1013904223)>>>0)%n;
  for(let run=0;run<32;run++) {
    const steps=[...init(),load(),g('geometry','Payload','initial')];
    for(let k=0;k<12;k++) {
      steps.push(content('content',Array.from({length:10},()=>rand(2001)/8-125)),cell('cell',rand(3)===0?r('styleB'):null),edit(Array.from({length:rand(4)},()=>r('cell')),{rows:rand(7),columns:rand(9)}),model('geometry'));
    }
    steps.push(model('initial'));add('random/'+run,steps);
  }
  return all;
}
