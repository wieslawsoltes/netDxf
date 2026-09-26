// Deterministic inputs only; expected values are independently observed from C#.
const r=ref=>({ref}),s=value=>({short:value}),i=value=>({int:value});
const n=(id,type,args=[])=>({method:'new',id,value:{new:type,args}});
const g=(target,member,id)=>({method:'get',target,member,id});
const c=(target,member,args=[],id)=>({method:'call',target,member,args,...(id?{id}:{})});
const at=(target,index,id)=>({method:'item',target,args:[i(index)],id});
const model=target=>({method:'table-model',target}),snap=target=>({method:'snapshot',target});
const set=(target,member,value)=>({method:'set',target,member,value});
const same=(a,b)=>({method:'same',args:[r(a),r(b)]});
const arr=(array,values)=>({array,values});
export function tableTag(code,value){return [code,typeof value==='number'?(code===90||code===91?i(value):[3,7,100,102,300,309,310,330,340,360,4].includes(code)?value:code===40||code===41||code===140?Object.is(value,-0)?{double:'8000000000000000'}:value:s(value)):value];}
export function tableStylePacket({versioned=false,rows=3,borders=true,scalars=true,data=true,privateGroups=false}={}) {
  const tags=[[100,'AcDbTableStyle'],...(versioned?[[280,0]]:[]),[3,'Independent description'],[70,0],[71,7],[40,.06],[41,-0],[280,0],[281,0]];
  for(let row=0;row<rows;row++) {
    tags.push([7,'STYLE_REF']);if(scalars)tags.push([140,row+1],[170,5],[62,0],[63,7],[283,1]);
    if(data)tags.push([90,4],[91,2]);if(borders)for(let j=0;j<6;j++)tags.push([274+j,-2],[284+j,1],[64+j,0]);
    tags.push([4,'%lu2%pr2']);if(privateGroups)tags.push([102,'{PRIVATE'],[7,'Not-a-row'],[140,99],[102,'{NESTED'],[90,13],[102,'}'],[102,'}']);
  }
  return tags.map(([c,v])=>tableTag(c,v));
}
export function cellMapPacket(count=2,{depth=0,extra=[]}={}) {
  const tags=[[100,'AcDbCellStyleMap'],[90,count]];
  for(let j=0;j<count;j++)tags.push([300,'CELLSTYLE'],[1,'TABLEFORMAT_BEGIN'],...Array.from({length:depth},()=>[1,'CONTENTFORMAT_BEGIN']),[90,23],...extra,...Array.from({length:depth},()=>[309,'CONTENTFORMAT_END']),[309,'TABLEFORMAT_END'],[1,'CELLSTYLE_BEGIN'],[90,j%2],[91,-1],[300,'Entry '+j],[309,'CELLSTYLE_END']);
  return tags.map(([c,v])=>tableTag(c,v));
}
const init=(version=18)=>[n('doc','DxfDocument',[{enum:'Header.DxfVersion',value:version}]),g('doc','Entities','entities'),g('doc','TextStyles','styles'),n('styleA','Tables.TextStyle',['STYLE_REF','txt.shx']),n('styleB','Tables.TextStyle',['ALT','txt.shx']),c('styles','Add',[r('styleA')]),c('styles','Add',[r('styleB')])];
const load=(tags,kind='style',extras={})=>({method:'table-load',target:'doc',id:kind==='map'?'map':'table',kind,tags,...extras});
const header=(id='header',text='Edited Ω \\U+0041')=>n(id,'Objects.DxfTableStyleHeader',[text,s(1),s(-32768),.125,-0,true,false]);
const values=(id='values',height=4.125)=>n(id,'Objects.DxfTableStyleRowValues',[height,s(-12),s(-1),s(256),false]);
const edit=(target='table',members=[],extras={})=>({method:'table-call',target,action:target==='map'?'map':'style',members,log:'log',...extras});
export function tableStyleCorpus() {
  const corpus=[],add=(name,steps)=>corpus.push({name:'table-style/'+name,request:{op:'document-ownership',steps}});
  for(const text of ['', 'Header', 'Zażółć 東京 🧪', '\\U+0041', 'x'.repeat(255),null,'x'.repeat(256),'\0',{utf16:[0xd800]},{utf16:[0xdc00]},{utf16:[65,0xd800,66]}])add('header-text/'+corpus.length,[header('h',text),model('h')]);
  for(const value of [-1,-0,0,.01,1e100,{double:'7FF0000000000000'},{double:'FFF0000000000000'},{double:'7FF8000000000042'}]) {
    add('height/'+corpus.length,[values('v',value),model('v')]);
    for(const position of [3,4]){const args=['',s(0),s(0),0,0,false,false];args[position]=value;add('margin/'+corpus.length,[n('h','Objects.DxfTableStyleHeader',args),model('h')]);}
  }
  for(const flow of [-32768,-1,0,1,2,32767])add('flow/'+flow,[n('h','Objects.DxfTableStyleHeader',['',s(flow),s(0),0,0,false,false]),model('h')]);
  for(const code of [-2147483648,-1,0,2147483647])add('data-types/'+code,[n('d','Objects.DxfTableStyleRowDataTypes',[i(code),i(~code)]),model('d')]);
  for(const count of [0,1,5,6,7,65536])add('border-count/'+count,[n('b','Objects.DxfTableStyleBorderValues',[s(-32768),true,s(32767)]),{method:'table-call',action:'borders',members:[r('b')],repeat:count,log:'log',id:'borders'},snap('log'),model('borders')]);
  for(const phase of ['get','move','current','dispose'])add('border-throw/'+phase,[n('b','Objects.DxfTableStyleBorderValues',[s(-2),false,s(256)]),{method:'table-call',action:'borders',members:[r('b')],repeat:6,log:'log',hooks:[{stage:phase,kind:'throw'}]},snap('log')]);
  add('border-nulls',[{method:'table-call',action:'borders',members:null}, {method:'table-call',action:'borders',members:[null]}]);
  for(const version of [13,14,15,16,17,18])for(const versioned of [false,true]) {
    add(`lifecycle/${version}/${versioned}`,[...init(version),load(tableStylePacket({versioned,privateGroups:true})),model('table'),g('table','Tags','oldTags'),g('table','Rows','oldRows'),g('table','Header','oldHeader'),g('table','References','oldRefs'),g('table','Rows','rows'),at('rows',0,'row'),g('row','Values','scalars'),c('row','WithValues',[r('scalars')],'sameEdit'),edit('table',[r('sameEdit')]),g('table','Tags','newTags'),same('oldTags','newTags'),values(),header(),c('row','WithValues',[r('values')],'changedEdit'),edit('table',[r('changedEdit')],{header:r('header')}),model('table'),model('oldTags'),model('oldRows'),model('oldHeader'),model('oldRefs'),edit('table',[r('changedEdit')]),snap('doc')]);
    add(`map-lifecycle/${version}/${versioned}`,[...init(version),load(cellMapPacket(versioned?0:2,{depth:3}), 'map'),g('map','Payload','oldPayload'),g('map','Entries','oldEntries'),model('map'),edit('map',versioned?[]:['Entry 0','Entry 1']),g('map','Payload','samePayload'),same('oldPayload','samePayload'),edit('map',versioned?[]:['東京 \\U+0041','🧪']),model('map'),model('oldPayload'),model('oldEntries'),snap('doc')]);
  }
  for(const count of [0,1,2,4])add('row-count/'+count,[...init(),load(tableStylePacket({rows:count})),model('table'),header(),edit('table',[],{header:r('header')}),model('table')]);
  const rowFields=[140,170,62,63,283,90,91,...Array.from({length:6},(_,i)=>274+i),...Array.from({length:6},(_,i)=>284+i),...Array.from({length:6},(_,i)=>64+i)];
  for(const code of rowFields)for(const fault of ['missing','duplicate']) {
    let tags=tableStylePacket();const index=tags.findIndex(t=>t[0]===code);
    if(fault==='missing')tags.splice(index,1);else tags.splice(index,0,tags[index]);
    add(`projection/${code}/${fault}`,[...init(),load(tags),model('table'),g('table','Rows','rows'),at('rows',0,'row'),values(),c('row','WithValues',[r('values')],'edit'),edit('table',[r('edit')]),model('table')]);
  }
  for(const code of [283,...Array.from({length:6},(_,i)=>284+i)])for(const value of [-1,2]) {
    const tags=tableStylePacket();tags[tags.findIndex(t=>t[0]===code)]=tableTag(code,value);
    add(`flag/${code}/${value}`,[...init(),load(tags),model('table')]);
  }
  for(const fault of ['missing','reordered','duplicate-subclass','unterminated','unmatched','other-subclass','negative-margin','unknown-version','unknown-row-style']) {
    const tags=tableStylePacket({versioned:true});
    if(fault==='missing')tags.splice(2,1);
    if(fault==='reordered')[tags[2],tags[3]]=[tags[3],tags[2]];
    if(fault==='duplicate-subclass')tags.push(tableTag(100,'AcDbTableStyle'));
    if(fault==='unterminated')tags.push(tableTag(102,'{X'));
    if(fault==='unmatched')tags.push(tableTag(102,'}'));
    if(fault==='other-subclass')tags[0]=tableTag(100,'Private');
    if(fault==='negative-margin')tags[tags.findIndex(t=>t[0]===40)]=tableTag(40,-1);
    if(fault==='unknown-version')tags[1]=tableTag(280,1);
    if(fault==='unknown-row-style')for(const tag of tags)if(tag[0]===7)tag[1]='missing';
    add('header-projection/'+fault,[...init(),load(tags),model('table'),edit('table',[]),header(),edit('table',[],{header:r('header')}),model('table')]);
  }
  const editSetup=()=>[...init(),load(tableStylePacket()),g('table','Rows','rows'),at('rows',0,'row'),values(),c('row','WithValues',[r('values')],'edit')];
  for(const phase of ['get','move','current','dispose'])for(const kind of ['throw','reenter','catch-reenter'])add(`style-callback/${phase}/${kind}`,[...editSetup(),edit('table',[r('edit')],{hooks:[{stage:phase,kind}]}),snap('log'),model('table'),edit('table',[r('edit')]),model('table')]);
  for(const count of [0,1,2,3,4,20])add('style-edit-count/'+count,[...editSetup(),edit('table',[r('edit')],{repeat:count}),model('table')]);
  add('style-null-edits',[...editSetup(),edit('table',null),edit('table',[null]),model('table')]);
  for(const field of ['Name','Handle'])add('rename-rebind/'+field,[...editSetup(),g('table','References','oldRefs'),c('row','WithTextStyle',[r('styleB')],'rebind'),edit('table',[r('rebind')]),model('table'),model('oldRefs'),c('styles','GetReferences',[r('styleA')]),c('styles','GetReferences',[r('styleB')]),set('styleB',field,'Renamed'),model('table'),g('table','Rows','newRows'),at('newRows',0,'newRow'),c('newRow','WithValues',[r('values')],'currentEdit'),edit('table',[r('currentEdit')]),model('table'),snap('doc')]);
  add('foreign-rebind',[...editSetup(),n('foreign','DxfDocument'),g('foreign','TextStyles','foreignStyles'),n('foreignStyle','Tables.TextStyle',['ALT','txt.shx']),c('foreignStyles','Add',[r('foreignStyle')]),c('row','WithTextStyle',[r('foreignStyle')],'badEdit'),edit('table',[r('badEdit')]),model('table')]);
  for(const index of [-1,0,5,6])add('border-edit/'+index,[...editSetup(),g('row','Borders','borders'),n('border','Objects.DxfTableStyleBorderValues',[s(-32768),false,s(-1)]),c('borders','WithBorder',[i(index),r('border')],'newBorders'),c('row','WithBorders',[r('newBorders')],'borderEdit'),n('data','Objects.DxfTableStyleRowDataTypes',[i(-1),i(2147483647)]),c('borderEdit','WithDataTypes',[r('data')],'combined'),edit('table',[r('combined')]),model('table'),model('row')]);
  for(const fault of ['profile','source-erased','unresolved','detached']) {
    const steps=[...init(),load(tableStylePacket(),'style',{register:fault!=='detached',resolve:fault!=='unresolved'})];
    if(fault==='profile')steps.push(g('doc','DrawingVariables','vars'),set('vars','AcadVer',{enum:'Header.DxfVersion',value:17}));
    if(fault==='source-erased')steps.push(g('doc','Objects','db'),c('db','EraseOwnedTree',[r('table')]));
    add('source/'+fault,[...steps,edit('table',[]),model('table')]);
  }
  for(const phase of ['get','move','current','dispose'])for(const kind of ['throw','reenter','catch-reenter'])add(`map-callback/${phase}/${kind}`,[...init(),load(cellMapPacket(),'map'),edit('map',['A','B'],{hooks:[{stage:phase,kind}]}),snap('log'),model('map'),edit('map',['A','B']),model('map')]);
  for(const names of [null,[],['A'],['A','B','C'],['same','same'],[null,'B'],['',''],['\0','A'],[{utf16:[0xd800]},'B']])add('map-names/'+corpus.length,[...init(),load(cellMapPacket(),'map'),edit('map',names),model('map')]);
  for(const fault of ['negative-count','too-many','too-few','no-count','missing-marker','mismatched-end','subclass','trailing','bad-frame','short-frame']) {
    const tags=cellMapPacket();
    if(fault==='negative-count')tags[1]=tableTag(90,-1);
    if(fault==='too-many')tags[1]=tableTag(90,1048577);
    if(fault==='too-few')tags[1]=tableTag(90,1);
    if(fault==='no-count')tags.splice(1,1);
    if(fault==='missing-marker')tags.splice(2,1);
    if(fault==='mismatched-end')tags[tags.findIndex(t=>t[0]===309)]=tableTag(309,'GRIDFORMAT_END');
    if(fault==='subclass')tags.splice(4,0,tableTag(100,'Private'));
    if(fault==='trailing')tags.push(tableTag(90,0));
    if(fault==='bad-frame')tags.splice(4,0,tableTag(1,'CELLSTYLE_BEGIN'));
    if(fault==='short-frame')tags.splice(4,0,tableTag(1,'a'));
    add('map-packet/'+fault,[...init(),load(tags,'map'),model('map')]);
  }
  for(const depth of [0,1,62,63,64,65])add('map-depth/'+depth,[...init(),load(cellMapPacket(1,{depth}),'map'),model('map')]);
  for(const version of [14,15,16,17,18])for(const field of ['header','map'])add(`text-escaping/${version}/${field}`,[...init(version),load(field==='header'?tableStylePacket():cellMapPacket(),field==='map'?'map':'style'),...(field==='header'?[header('h','東京 🧪 \\U+0041'),edit('table',[],{header:r('h')}),model('table')]:[edit('map',['東京 🧪 \\U+0041','line\r\nnext']),model('map')])]);
  for(let seed=1;seed<=24;seed++) {
    let state=seed;const random=n=>(state=(Math.imul(state,1664525)+1013904223)>>>0)%n,steps=[...init(14+seed%5),load(tableStylePacket({versioned:seed%2===0}))];
    for(let k=0;k<18;k++){steps.push(g('table','Rows','rows'),at('rows',random(3),'row'),values('v',random(1024)/8),c('row','WithValues',[r('v')],'e'),edit('table',[r('e')]),model('table'));}
    steps.push(snap('doc'));add('random/'+seed,steps);
  }
  // Explicit pointer descriptors read each implementation's own registered identity.
  for(const lower of [false,true])for(const pad of [0,2])for(const kind of ['style','map']) {
    const packet=kind==='style'?tableStylePacket():cellMapPacket(1,{extra:[[340,{handleOf:'styleA',lower,pad}],[340,{handleOf:'styleA',lower,pad}]]});
    if(kind==='style')packet.push([340,{handleOf:'styleA',lower,pad}],[340,{handleOf:'styleA',lower,pad}]);
    add(`references/${kind}/${lower}/${pad}`,[...init(),load(packet,kind),model(kind==='style'?'table':'map'),c('styles','GetReferences',[r('styleA')]),c('styles','Remove',[r('styleA')]),snap('doc')]);
  }
  for(const kind of ['style','map'])for(const mode of ['invalid-utf16','profile-callback','reference-callback','unresolved-reference']) {
    let packet=kind==='style'?tableStylePacket():cellMapPacket();
    if(mode==='invalid-utf16')packet[packet.findIndex(p=>p[0]===(kind==='style'?3:300)&&p[1]!=='CELLSTYLE')]=[kind==='style'?3:300,{utf16:[0xd800]}];
    if(mode==='unresolved-reference'){
      if(kind==='style')packet.push([340,'abc']);else packet.splice(4,0,[340,'abc']);
    }
    const steps=[...init(),g('doc','DrawingVariables','variables'),load(packet,kind)];
    if(mode==='reference-callback')steps.push(n('line','Entities.Line'));
    const target=kind==='style'?'table':'map',members=kind==='style'?[]:['A','B'];
    const hooks=mode==='profile-callback'?[{stage:'dispose',kind:'set',target:r('variables'),member:'AcadVer',value:{enum:'Header.DxfVersion',value:17}}]:mode==='reference-callback'?[{stage:'dispose',kind:'set',target:r('line'),member:'Layer',value:{new:'Tables.Layer',args:['CallerLayer']}}]:[];
    add(`validation/${kind}/${mode}`,[...steps,model(target),edit(target,members,{hooks}),model(target),snap('doc')]);
  }
  for(const kind of ['style','map'])add(`limits/${kind}`,[...init(14),load(kind==='style'?tableStylePacket():cellMapPacket(),kind),...(kind==='style'?[header('oversize','X'.repeat(1048577))]:[edit('map',['X'.repeat(1048577),'B']),edit('map',['\\'.repeat(149797),'B'])]),model(kind==='style'?'table':'map')]);
  for(const action of ['style','map','borders'])add('null-enumerator/'+action,[...init(),load(action==='map'?cellMapPacket():tableStylePacket(),action==='map'?'map':'style'),{method:'table-call',target:action==='map'?'map':'table',action,members:[],nullEnumerator:true,log:'log'},snap('log')]);
  // JSON has no negative-zero spelling; use the existing exact input descriptor.
  return JSON.parse(JSON.stringify(corpus,(_,v)=>typeof v==='number'&&Object.is(v,-0)?{double:'8000000000000000'}:v));
}
