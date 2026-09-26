import { tableContentPacket } from './table-content-corpus.mjs';
const r=ref=>({ref}),i=int=>({int}),short=short=>({short}),d=double=>({double}),n=(id,type,args=[])=>({method:'new',id,value:{new:type,args}});
const g=(target,member,id)=>({method:'get',target,member,id}),c=(target,member,args=[],id,signature)=>({method:'call',target,member,args,...(id?{id}:{}),...(signature?{signature}:{})});
const model=target=>({method:'stored-table-model',target}),set=(target,member,value)=>({method:'set',target,member,value}),snap=target=>({method:'snapshot',target});
const inside=(target,member,args=[])=>({method:'stored-table-internal',target,member,args});
const v=(x,y,z)=>({new:'Vector3',args:[x,y,z]});
export function storedTablePacket({rows=1,columns=3,types=[1,2,4],values=[i(-2147483648),d('8000000000000000'),'text'],style='Standard',display='Display',flags=6,old=false,header=[],tail=[]}={}){
  const tags=[[100,'AcDbBlockReference'],[2,display],[10,d('8000000000000000')],[20,2],[30,3],[210,0],[220,0],[230,2],[100,'AcDbTable'],[90,i(22)],[91,i(rows)],[92,i(columns)],...Array.from({length:Math.max(0,rows)},()=>[141,3]),...Array.from({length:Math.max(0,columns)},()=>[142,4]),[7,style],...header];
  for(let k=0;k<types.length;k++){
    tags.push([171,short(1)],[7,style]);
    if(old){tags.push([1,values[k]]);continue;}
    tags.push([301,'CELL_VALUE'],[93,i(flags)],[90,i(types[k])]);
    if(types[k]!==0)tags.push([types[k]===1?91:types[k]===2?140:1,values[k]]);
    tags.push([304,'ACVALUE_END']);
  }
  return [...tags,...tail];
}
const init=(version=18)=>[n('doc','DxfDocument',[{enum:'Header.DxfVersion',value:version}]),g('doc','Entities','entities'),g('doc','Blocks','blocks'),g('doc','TextStyles','styles'),n('display','Blocks.Block',['Display']),c('blocks','Add',[r('display')]),n('named','Tables.TextStyle',['Named','txt.shx']),c('styles','Add',[r('named')])];
const load=(tags=storedTablePacket(),extra={})=>({method:'stored-table-load',target:'doc',id:'table',tags,...extra});
export function storedTableCorpus(){
 const all=[],add=(name,steps)=>all.push({name:'stored-table/'+name,request:{op:'document-ownership',steps}});
 for(const version of [13,14,15,16,17,18])add('version/'+version,[...init(version),load(),model('table'),inside('table','Validate',[r('doc')]),c('entities','Remove',[r('table')]),c('entities','Add',[r('table')]),snap('doc')]);
 for(const type of [0,1,2,4,32,99])for(const flags of [0,1,2,3,4,6,-1]){const value=type===1?i(3):type===2?d('8000000000000000'):'text';add(`value/${type}/${flags}`,[...init(),load(storedTablePacket({columns:1,types:[type],values:[value],flags})),model('table')]);}
 for(let mutation=0;mutation<22;mutation++){
   const tags=storedTablePacket(),cell=tags.findIndex(t=>t[0]===171),value=tags.findIndex(t=>t[0]===301);
   if(mutation===0)tags.splice(3,1);if(mutation===1)tags.splice(6,1);if(mutation===2)tags[7][1]=0;if(mutation===3)tags.splice(5,0,[210,0]);if(mutation===4)tags.splice(1,0,[2,'Other']);
   if(mutation===5)tags[9][1]=i(99);if(mutation===6)tags.splice(10,0,[91,i(1)]);if(mutation===7)tags[10][1]=i(0);if(mutation===8)tags[11][1]=i(-1);if(mutation===9)tags[12][1]=-1;if(mutation===10)tags.splice(12,1);
   if(mutation===11)tags.push([171,short(1)]);if(mutation===12)tags.push([100,'Private']);if(mutation===13)tags.splice(value+1,1);if(mutation===14)tags.splice(value+2,1);if(mutation===15)tags.splice(value+3,1);if(mutation===16)tags.splice(value+4,1);if(mutation===17)tags.splice(value+1,0,[93,i(2)]);if(mutation===18)tags.splice(value+1,0,[90,i(1)]);if(mutation===19)tags.splice(value+1,0,[301,'CELL_VALUE']);if(mutation===20)tags[cell][1]=short(2);if(mutation===21)tags.splice(value+1,0,[91,i(1)]);
   add('packet/'+mutation,[...init(),load(tags),model('table')]);
 }
 for(const handle of ['0','F','000F'])for(const depth of [0,1,2])for(const inValue of [false,true]){
   const tags=storedTablePacket({columns:1,types:[4],values:['literal']});let at=tags.findIndex(t=>t[0]===301)+(inValue?1:0);
   tags.splice(at,0,...Array.from({length:depth},()=>[102,'{PRIVATE']),[344,handle],...Array.from({length:depth},()=>[102,'}']));
   add(`field/${handle}/${depth}/${inValue}`,[...init(),load(tags),model('table')]);
 }
 for(const version of [14,18])for(const size of [0,1,249,250,251,499,500]){
   const text='x'.repeat(size),chunks=[];for(let at=0;at<text.length-249;at+=250)chunks.push([2,text.slice(at,at+250)]);chunks.push([1,text.slice(chunks.length*250)]);
   const tags=storedTablePacket({columns:1,types:[],values:[]});tags.push([171,short(1)],...chunks);add(`legacy/${version}/${size}`,[...init(version),load(tags),model('table')]);
 }
 for(const size of [3,4])for(const pos of [0,1,8,9,15]){
   if(pos>=size*size)continue;const values=Array.from({length:size*size},(_,j)=>j%(size+1)===0?1:0);values[pos]+=0.01;
   add(`transform/${size}/${pos}`,[...init(),load(),c('table','TransformBy',[{new:'Matrix'+size,args:values},...(size===3?[v(0,0,0)]:[])]),model('table')]);
 }
 add('identity',[...init(),load(),c('table','TransformBy',[{static:'Matrix3',property:'Identity'},v(0,0,0)]),c('table','TransformBy',[{static:'Matrix4',property:'Identity'}]),set('table','Normal',v(0,0,2)),set('table','Normal',v(0,0,1)),set('table','Position',v(2,3,4)),c('table','Clone'),model('table')]);
 add('resource-renames',[...init(),load(storedTablePacket({style:'Named',tail:[[102,'{PRIVATE'],[7,'Named'],[2,'Display'],[102,'}']]})),model('table'),c('blocks','Remove',[r('display')]),c('styles','Remove',[r('named')]),set('display','Name','RenamedDisplay'),set('named','Name','RenamedStyle'),model('table'),inside('table','Validate',[r('doc')]),c('entities','Remove',[r('table')]),c('blocks','Remove',[r('display')]),c('styles','Remove',[r('named')]),snap('doc')]);
 for(const code of [5,320,330,340,350,360,390,480])add('reference/'+code,[...init(),n('line','Entities.Line'),c('entities','Add',[r('line')]),load(storedTablePacket({tail:[[code,{handleOf:'line',lower:true,pad:3}],[code,{handleOf:'line'}]]})),model('table'),c('entities','Remove',[r('line')]),model('table'),c('entities','Remove',[r('table')]),c('entities','Remove',[r('line')]),snap('doc')]);
 add('late-reference',[...init(),load(storedTablePacket({display:'Missing',tail:[[330,'40'],[330,'0040']]})),model('table'),n('line','Entities.Line'),{method:'manager-set-owner',target:'line',owner:null},set('line','Handle','40'),c('entities','Add',[r('line')]),model('table')]);
 for(const resolved of [false,true])add('cross-document/'+resolved,[...init(),load(storedTablePacket(),{register:false,resolve:resolved}),n('foreign','DxfDocument'),g('foreign','Entities','other'),c('other','Add',[r('table')]),snap('foreign'),n('container','Blocks.Block',['Container']),g('container','Entities','members'),c('members','Add',[r('table')]),g('foreign','Blocks','foreignBlocks'),c('foreignBlocks','Add',[r('container')]),snap('foreign')]);
 for(const owner of ['direct','block','insert','dimension']){
   const prefix=[...init(),load(storedTablePacket(),{register:false,resolve:false}),n('container','Blocks.Block',['Container']),g('container','Entities','members')];
   let operations=owner==='direct'?[c('entities','Add',[r('table')])]:[c('members','Add',[r('table')]),...owner==='block'?[c('blocks','Add',[r('container')])]:owner==='insert'?[n('insert','Entities.Insert',[r('container')]),c('entities','Add',[r('insert')])]:[n('dimension','Entities.AlignedDimension'),set('dimension','Block',r('container')),c('entities','Add',[r('dimension')])]];
   add('adoption/'+owner,[...prefix,...operations,inside('table','Resolve'),model('table'),inside('table','Validate',[r('doc')]),snap('doc')]);
 }
 for(const match of [true,false])for(const valueTypes of [[1],[2],[4],[1,2,4]]){
   const values=valueTypes.map(t=>t===1?i(-2147483648):t===2?d('8000000000000000'):'text');
   const body=tableContentPacket({types:valueTypes});if(!match){const tag=body.find(t=>t[0]===91&&typeof t[1]==='object'&&t[1].int===-2147483648)||body.find(t=>t[0]===140)||body.find(t=>t[0]===1&&t[1]==='text');tag[1]=tag[0]===91?i(123):tag[0]===140?3:'changed';}
   add('backing/'+valueTypes.join('-')+'/'+match,[...init(),load(storedTablePacket({columns:valueTypes.length,types:valueTypes,values}),{resolve:false}),g('doc','Objects','db'),n('extension','Objects.DxfDictionary'),c('db','SetExtensionDictionary',[r('table'),r('extension')]),n('record','Objects.DxfXRecord'),c('extension','Add',['ROUNDTRIP',r('record'),true]),{method:'content-load',target:'doc',owner:r('record'),tags:body,id:'content'},inside('table','Resolve'),model('table'),c('entities','Remove',[r('table')]),g('content','StoredValues','values'),{method:'item',target:'values',args:[i(0)],id:'value'},{method:'content-with',target:'value',value:valueTypes[0]===1?i(123):valueTypes[0]===2?3:'edit',display:'edit',id:'edit'},{method:'content-call',target:'content',name:'name',description:'description',style:null,members:[r('edit')],log:'log'},model('table'),c('db','Validate')]);
 }
 for(const count of [1,2,7,31]){const types=Array(count).fill(4),values=Array.from({length:count},(_,i)=>'cell '+i);add('grid/'+count,[...init(),load(storedTablePacket({columns:count,types,values})),g('table','Grid','grid'),{method:'item',target:'grid',args:[i(0),i(count-1)],id:'cell'},model('cell'),{method:'item',target:'grid',args:[i(1),i(count-1)]},{method:'item',target:'grid',args:[i(0),i(count)]},model('table')]);}
 return all;
}
