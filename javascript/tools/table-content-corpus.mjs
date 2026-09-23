// Deterministic inputs only. These retained fixtures are not typed file admission.
const r=ref=>({ref}), i=int=>({int}), d=double=>({double}), n=(id,type,args=[])=>({method:'new',id,value:{new:type,args}});
const g=(target,member,id)=>({method:'get',target,member,id}), c=(target,member,args=[],id)=>({method:'call',target,member,args,...(id?{id}:{})});
const at=(target,index,id)=>({method:'item',target,args:[i(index)],id}), model=target=>({method:'content-model',target}), snap=target=>({method:'snapshot',target});
const v=(x,y,z)=>({new:'Vector3',args:[x,y,z]});
const set=(target,member,value)=>({method:'set',target,member,value});
export function tableContentPacket({version=18,types=[1,2,4,32],text='text',flags=6,style='0',extra=[]}={}) {
  const tags=[[100,'AcDbLinkedData'],[1,'name'],[300,'description'],[100,'AcDbLinkedTableData'],[90,i(0)],[91,i(1)],[301,'ROW'],[1,'LINKEDTABLEDATAROW_BEGIN'],[90,i(types.length)]];
  for(const type of types){
    tags.push([300,'CELL'],[1,'LINKEDTABLEDATACELL_BEGIN'],[95,i(1)],[302,'CONTENT'],[1,'CELLCONTENT_BEGIN'],[90,i(1)],[300,'VALUE']);
    if(version>=15)tags.push([93,i(flags)]);
    tags.push([90,i(type)]);
    if(type===1)tags.push([91,i(-2147483648)]);else if(type===2)tags.push([140,d('8000000000000000')]);else if(type===4)tags.push([1,text]);else tags.push([11,d('8000000000000000')],[21,2],[31,3]);
    if(version>=15)tags.push([94,i(-7)],[300,'%.2f'],[302,'display'],[304,'ACVALUE_END']);
    tags.push([91,i(0)],[309,'CELLCONTENT_END'],[309,'LINKEDTABLEDATACELL_END']);
  }
  tags.push([309,'LINKEDTABLEDATAROW_END'],[92,i(0)],[100,'AcDbFormattedTableData'],...extra,[100,'AcDbTableContent'],[340,style]);return tags;
}
const init=(version=18)=>[n('doc','DxfDocument',[{enum:'Header.DxfVersion',value:version}]),g('doc','Objects','db'),g('db','Root','root')];
const load=(tags=tableContentPacket(),extra={})=>({method:'content-load',target:'doc',id:'content',tags,...extra});
const edit=(members=[],extra={})=>({method:'content-call',target:'content',name:'name',description:'description',style:null,members,log:'log',...extra});
const valueAt=(index=0,id='value')=>[g('content','StoredValues','values'),at('values',index,id)];
const withValue=(value,display='display',extra={})=>({method:'content-with',target:'value',value,display,id:'edit',...extra});
export function tableContentCorpus(){
  const all=[],add=(name,steps)=>all.push({name:'table-content/'+name,request:{op:'document-ownership',steps}});
  for(const version of [13,14,15,16,17,18]){
    add('version/'+version,[...init(version),load(tableContentPacket({version})),model('content'),edit(),model('content'),c('db','Validate')]);
    for(let index=0;index<4;index++){
      const value=[i(123),d('0000000000000000'),'Edited \\U+0041 😀',v(-1,2,3)][index],display=version>=15?'new display':null;
      add(`edit/${version}/${index}`,[...init(version),load(tableContentPacket({version})),...valueAt(index),g('content','Payload','old'),withValue(value,display),model('edit'),edit([r('edit')],{name:'new name',description:'new description'}),model('content'),model('old'),edit([r('edit')]),model('content'),c('db','Validate')]);
    }
  }
  const invalid=[null,i(0),{short:0},{byte:1},{long:'2'},d('0000000000000000'),d('7FF0000000000000'),d('FFF0000000000000'),d('7FF8000000000042'),'',true,v(1,2,3),v(d('7FF8000000000000'),1,2),{utf16:[0xd800]},'bad\0value'];
  for(const version of [14,18])for(let index=0;index<4;index++)for(let k=0;k<invalid.length;k++)add(`kind/${version}/${index}/${k}`,[...init(version),load(tableContentPacket({version})),...valueAt(index),withValue(invalid[k],version>=15?'display':null),model('edit')]);
  for(const display of [null,'','new','bad\0text',{utf16:[0xdc00]},{utf16:[0xd800,0xdc00]}])for(const version of [14,18])add(`display/${version}/${JSON.stringify(display)}`,[...init(version),load(tableContentPacket({version})),...valueAt(),withValue(i(4),display),model('edit')]);
  for(const version of [14,18])for(const text of ['TABLEFORMAT_BEGIN','DATAMAP_BEGIN','CELLCONTENT_BEGIN','LINKEDTABLEDATACELL_BEGIN','ACVALUE_END','Literal\\U+0041','😀漢字','\r\n','\\u+0041'])add(`marker-text/${version}/${JSON.stringify(text)}`,[...init(version),load(tableContentPacket({version,text})),...valueAt(2),model('value'),withValue(text,version>=15?text:null),edit([r('edit')],{name:text,description:text}),model('content')]);
  for(const key of ['name','description'])for(const text of [null,'','bad\0text',{utf16:[0xd800]},'😀\\U+0041'])add(`header/${key}/${JSON.stringify(text)}`,[...init(),load(),g('content','Payload','old'),edit([],{[key]:text}),g('content','Payload','new'),{method:'same',args:[r('old'),r('new')]},model('content')]);
  for(const stage of ['get','move','current','dispose'])for(const kind of ['throw','reenter','catch-reenter'])add(`enumerator/${stage}/${kind}`,[...init(),load(),...valueAt(),withValue(i(4)),edit([r('edit')],{name:'changed',hooks:[{stage,kind}]}),snap('log'),model('content'),edit(),model('content')]);
  for(const change of ['version','owner','erased','remove']){
    const setup=change==='version'?[g('doc','DrawingVariables','variables')]:[],hook=change==='version'?{kind:'set',target:r('variables'),member:'AcadVer',value:{enum:'Header.DxfVersion',value:17}}:change==='owner'?{kind:'set',target:r('content'),member:'Owner',value:null}:change==='erased'?{kind:'set',target:r('content'),member:'IsErased',value:true}:{kind:'call',target:r('root'),member:'Remove',args:['CONTENT_FIXTURE']};
    add('callback-mutation/'+change,[...init(),load(),...setup,...valueAt(),withValue(i(4)),edit([r('edit')],{hooks:[{stage:'dispose',...hook}]}),model('content'),c('db','Validate')]);
  }
  for(const members of [null,[null],[r('edit'),r('edit')],Array(5).fill(r('edit'))])add('invalid-enumeration/'+JSON.stringify(members),[...init(),load(),...valueAt(),withValue(i(4)),edit(members),snap('log'),model('content')]);
  add('null-enumerator',[...init(),load(),edit([],{nullEnumerator:true}),snap('log'),model('content')]);
  for(let mutation=0;mutation<24;mutation++){
    const tags=tableContentPacket(),start=tags.findIndex(t=>t[0]===1&&t[1]==='CELLCONTENT_BEGIN');
    if(mutation===0)tags.splice(0,1);if(mutation===1)tags[0][1]='Private';if(mutation===2)tags.push([100,'Private']);if(mutation===3)tags.pop();if(mutation===4)tags.push([340,'0']);if(mutation===5)tags[2][0]=301;if(mutation===6)tags.splice(3,0,[300,'extra']);
    if(mutation===7)tags[4]=[90,i(1)];if(mutation===8)tags[5]=[91,i(2)];if(mutation===9)tags[4]=[90,i(-1)];if(mutation===10)tags[5]=[91,i(-1)];if(mutation===11)tags[4][0]=91;
    if(mutation===12)tags[start+3]=[93,i(8)];if(mutation===13)tags[start+1]=[90,i(2)];if(mutation===14)tags[start+4]=[90,i(99)];if(mutation===15)tags[start+5][0]=140;
    if(mutation===16)tags.splice(start+6,0,[300,'private']);if(mutation===17)tags.splice(start,0,[1,'PRIVATE_BEGIN'],[309,'PRIVATE_END']);if(mutation===18)tags[start+10][1]='BAD_END';if(mutation===19)tags[start][1]='GRIDFORMAT_BEGIN';if(mutation===20)tags.splice(start+12,1);if(mutation===21)tags[start+11]=[91,i(1)];
    if(mutation===22)tags.splice(tags.length-2,0,[1,'TABLEFORMAT_BEGIN']);if(mutation===23)tags.splice(tags.length-2,0,[309,'TABLEFORMAT_END']);
    add('packet/'+mutation,[...init(),load(tags),model('content'),edit(),model('content')]);
  }
  for(const depth of [0,1,63,64,65]){const extra=[...Array.from({length:depth},()=>[1,'TABLEFORMAT_BEGIN']),...Array.from({length:depth},()=>[309,'TABLEFORMAT_END'])];add('nest/'+depth,[...init(),load(tableContentPacket({extra})),model('content')]);}
  for(const map of [[],[[90,i(0)],[309,'DATAMAP_END']],[[90,i(0)]],[[90,i(2)],[309,'DATAMAP_END']],[[90,i(1)],[300,'key'],[301,'DATAMAP_VALUE'],[90,i(2)],[140,2],[309,'DATAMAP_END']],[[90,i(1)],[300,'key'],[301,'DATAMAP_VALUE'],[93,i(6)],[90,i(4)],[1,'TABLEFORMAT_BEGIN'],[304,'ACVALUE_END'],[309,'DATAMAP_END']]])add('map/'+all.length,[...init(),load(tableContentPacket({extra:[[1,'DATAMAP_BEGIN'],...map]})),model('content')]);
  for(const code of [5,320,330,340,350,360,390,480]){
    const extra=[[code,{handleOf:'line',pad:3,lower:true}],[code,{handleOf:'line'}]];
    add('references/'+code,[...init(),g('doc','Entities','entities'),n('line','Entities.Line'),c('entities','Add',[r('line')]),load(tableContentPacket({extra})),model('content'),c('entities','Remove',[r('line')]),edit([],{description:'edited'}),model('content'),c('db','Validate')]);
  }
  add('style-rebinding',[...init(),{method:'table-load',target:'doc',id:'style',tags:[[100,'AcDbTableStyle']],name:'STYLE'},load(),g('content','References','old'),edit([],{style:r('style')}),model('content'),model('old'),c('db','EraseOwnedTree',[r('style')]),edit([],{style:null}),c('db','EraseOwnedTree',[r('style')]),c('db','Validate')]);
  add('wrong-style',[...init(),n('wrong','Objects.DxfXRecord'),c('root','Add',['wrong',r('wrong'),true]),load(),edit([],{style:r('wrong')}),model('content')]);
  for(const register of [false,true])for(const resolve of [false,true])add(`source/${register}/${resolve}`,[...init(),load(tableContentPacket(),{register,resolve}),edit(),model('content')]);
  for(let seed=1;seed<=24;seed++){
    let state=seed;const rand=()=>state=(Math.imul(state,1664525)+1013904223)>>>0,steps=[...init(),load()];
    for(let k=0;k<20;k++){const index=rand()%4,value=[i((rand()|0)),{double:'8000000000000000'},'text-'+rand(),v(rand()%11,-2,3)][index];steps.push(...valueAt(index),withValue(value,'display-'+k),edit([r('edit')],{description:'step '+k}),model('content'));}
    steps.push(c('db','Validate'));add('random/'+seed,steps);
  }
  return all;
}
