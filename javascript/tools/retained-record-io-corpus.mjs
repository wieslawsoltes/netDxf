// Deterministic input-only admission, resolution and output cases. No stored results.
import { DxfGroupCode, DxfTagValueType as T } from '../netDxf/IO/DxfGroupCode.js';
import { tableStylePacket, cellMapPacket } from './table-style-corpus.mjs';
import { tableGeometryPacket } from './table-geometry-corpus.mjs';
import { tableContentPacket } from './table-content-corpus.mjs';
import { sunStudyPacket } from './stored-dependencies-corpus.mjs';
const h=(name,extra={})=>({h:name,...extra}),op=method=>({method});
export function retainedTag(code,value) {
  if(typeof value==='number') {
    const type=DxfGroupCode.GetValueType(code);
    if(type===T.Int16)value={short:value};else if(type===T.Int32)value={int:value};else if(type===T.Int64)value={long:String(value)};
    else if(Object.is(value,-0))value={double:'8000000000000000'};
  }
  return [code,value];
}
export function sectionGeometryPacket(color=63) {
  return [[90,1],[91,-3],[92,7],[color,256],[8,'Layer\\U+03A9'],[6,'ByLayer'],[40,-0],[1,'ByColor'],[370,-1],[70,0],[71,100],[72,0],[2,'ANSI31'],[41,-0],[42,1],[43,2],[3,'SectionGeometrySettingsEnd']];
}
export function sectionSettingsPacket({count=2,repeat=true,sources=[h('line'),'0',h('line')],destination=h('block'),types=1,initial=true,color=63}={}) {
  const tags=[[100,'AcDbSectionSettings'],[90,1],[91,types]];
  for(let j=0;j<types;j++) {
    tags.push([1,'SectionTypeSettings'],[90,j],[91,-1],[92,sources.length],...sources.map(s=>[330,s]),[331,destination],[1,'sheet\\U+03A9.dxf'],[93,count]);
    if(initial && (!repeat||count===0))tags.push([2,'SectionGeometrySettings']);
    for(let k=0;k<count;k++){if(repeat && (initial||k>0))tags.push([2,'SectionGeometrySettings']);tags.push(...sectionGeometryPacket(color));}
    tags.push([3,'SectionTypeSettingsEnd']);
  }
  return tags;
}
function dimAssocPacket(mask=1,osnap=13) {
  const tags=[[100,'AcDbDimAssoc'],[330,h('dimension')],[90,mask],[70,0],[71,1]];
  for(let i=0;i<4;i++)if(mask&(1<<i))tags.push([1,'AcDbOsnapPointRef'],[72,osnap],[331,h('line',{pad:2,lower:true})],[73,-1],[91,i],[40,-0],[10,1],[20,2],[30,3],[75,0]);
  return tags;
}
const fieldPacket=()=>[[100,'AcDbField'],[1,'AcObjProp'],[2,'prefix\\U+'],[3,'03A9'],[90,0],[97,3],[331,h('line')],[331,'0'],[331,h('line',{pad:2,lower:true})]];
export function retainedRecordIOCorpus() {
  const result=[];
  const add=(name,kind,tags,steps,extra={})=>result.push({name:`retained-record/${kind}/${name}`,request:{op:'retained-record-io',kind,mode:'text',version:18,tags:tags.map(([c,v])=>retainedTag(c,v)),steps,...extra}});
  const spec={
    StoredField:{type:'FIELD',packet:fieldPacket()},
    StoredDimAssoc:{type:'DIMASSOC',packet:dimAssocPacket()},
    StoredSunStudy:{type:'SUNSTUDY',packet:sunStudyPacket([true,false,true],[h('leaf'),h('line'),'0',h('style')])},
    TableStyle:{type:'TABLESTYLE',packet:tableStylePacket().map(([c,v])=>[c,c===7?'Standard':v])},
    StoredTableGeometry:{type:'TABLEGEOMETRY',packet:tableGeometryPacket({reference:h('line')})},
    StoredTableContent:{type:'TABLECONTENT',packet:tableContentPacket({extra:[[330,h('line')]]})},
    StoredCellStyleMap:{type:'CELLSTYLEMAP',packet:cellMapPacket(2)},
    SectionSettings:{type:'SECTIONSETTINGS',packet:sectionSettingsPacket()},
    SectionManager:{type:'SECTION_MANAGER',packet:[[100,'AcDbSectionManager'],[70,1],[90,3],[330,h('section')],[330,h('section')],[330,h('section',{pad:2,lower:true})]]},
  };
  const header=(kind)=>[[5,'C00'],[330,h(kind==='StoredDimAssoc'?'extension':'root')]];
  const registration=kind=>({method:'register',...(kind==='StoredDimAssoc'?{owner:'extension',name:'ACAD_DIMASSOC'}:kind==='SectionManager'?{owner:'root',name:'ACAD_SECTION_MANAGER',hard:false}:{})});
  const lifecycle=kind=>[op('read'),registration(kind),op('resolve'),op('write'),op('validate')];
  for(const mode of ['text','binary','legacy'])for(const [kind,{type,packet}]of Object.entries(spec)) {
    for(const version of [13,14,15,16,17,18])add(`profile/${mode}/${version}`,kind,[...header(kind),...packet],lifecycle(kind),{mode,version,type});
    add(`xdata/${mode}`,kind,[...header(kind),...packet,[1001,'RECORD_APP'],[1000,'text\\U+03A9'],[1070,-7]],lifecycle(kind),{mode,type});
    add(`orphan-xdata/${mode}`,kind,[...header(kind),...packet,[1000,'orphan']], [op('read')],{mode,type});
    add(`header-private/${mode}`,kind,[...header(kind),[102,'{PRIVATE'],[1,'value'],[102,'}'],...packet],lifecycle(kind),{mode,type});
    add(`nested-header/${mode}`,kind,[[5,'C00'],[102,'{ACAD_REACTORS'],[330,h('root')],[102,'{INNER'],[330,h('leaf')],[102,'}'],[102,'}'],[330,h('root')],...packet],lifecycle(kind),{mode,type});
    add(`header-unterminated/${mode}`,kind,[[5,'C00'],[102,'{PRIVATE'],...packet],[op('read')],{mode,type});
    for(let i=0;i<packet.length;i++) {
      add(`missing/${mode}/${i}`,kind,[...header(kind),...packet.filter((_,j)=>i!==j)],[op('read')],{mode,type});
      if(i<35)add(`duplicate/${mode}/${i}`,kind,[...header(kind),...packet.slice(0,i),packet[i],...packet.slice(i)],[op('read')],{mode,type});
    }
    for(const [name,suffix]of Object.entries({private:[[100,'PrivateApplication']],control:[[102,'{Private'],[1,'nested'],[102,'}']],scalar:[[300,'private scalar']]}))
      add(`variant/${mode}/${name}`,kind,[...header(kind),...packet,...suffix],lifecycle(kind),{mode,type});
  }
  for(const [kind,{type,packet}]of Object.entries(spec)) {
    for(const action of ['ambiguous','unaccept','discard','replace','unregister'])for(const target of ['line','block','section'])
      add(`source/${target}/${action}`,kind,[...header(kind),...packet],[op('read'),registration(kind),{method:'source',target,action},op('resolve'),op('write'),op('validate')],{type});
    for(const at of [1,2,3,4,9,16,25,38,50])add(`callback/${at}`,kind,[...header(kind),...packet],[op('read'),registration(kind),op('resolve'),{method:'write',hooks:[{at,kind:'throw'}]}],{type});
    add('repeat-resolution',kind,[...header(kind),...packet],[op('read'),registration(kind),op('resolve'),op('resolve'),op('write'),op('validate')],{type});
    add('version-change',kind,[...header(kind),...packet],[op('read'),registration(kind),op('resolve'),{method:'set',target:'variables',property:'AcadVer',value:{short:17}},op('write'),op('validate')],{type});
    add('wrong-write-target',kind,[],[{method:'write',target:'leaf'}],{type});
  }
  // Public-header outputs must survive a false return or count failure unchanged.
  const hf=fieldPacket().slice(1);
  for(let n=0;n<=hf.length;n++)add(`header-prefix/${n}`,'StoredField',hf.slice(0,n),[op('header')],{type:'FIELD'});
  for(const count of [-1,1,2147483647])for(const code of [90,97])add(`header-count/${code}/${count}`,'StoredField',hf.map(([c,v])=>[c,c===code?count:v]),[op('header')],{type:'FIELD'});
  for(const type of ['ACAD_FIELD','FIELD'])add('name/'+type,'StoredField',[...header('StoredField'),...fieldPacket()],lifecycle('StoredField'),{type});
  for(let mask=0;mask<16;mask++)for(const osnap of [1,2,3,13,14])add(`mask/${mask}/${osnap}`,'StoredDimAssoc',[...header('StoredDimAssoc'),...dimAssocPacket(mask,osnap)],lifecycle('StoredDimAssoc'),{type:'DIMASSOC'});
  for(const count of [-1,0,1,4,32767]){const packet=sunStudyPacket([true,false],[h('leaf'),h('line'),'0',h('style')]);packet[14]=[73,count];add('hour-count/'+count,'StoredSunStudy',packet,[op('shape')],{type:'SUNSTUDY'});}
  for(const kind of ['StoredDimAssoc','SectionManager'])add('duplicate-reactor',kind,[[5,'C00'],[102,'{ACAD_REACTORS'],[330,h('root')],[330,h('root',{pad:2,lower:true})],[102,'}'],...spec[kind].packet],[op('read')],{type:spec[kind].type});
  for(const count of [0,1,2,3,7])for(const repeat of [false,true])for(const initial of [false,true])for(const type of ['SECTIONSETTINGS','SECTION_SETTINGS'])
    add(`geometry/${count}/${repeat}/${initial}/${type}`,'SectionSettings',[...header('SectionSettings'),...sectionSettingsPacket({count,repeat,initial})],lifecycle('SectionSettings'),{type});
  for(const code of [90,91,92,93])for(const value of [-1,0,1025,2147483647]){
    const p=sectionSettingsPacket(),index=p.findIndex(([c])=>c===code);p[index]=[code,value];add(`budget/${code}/${value}`,'SectionSettings',[...header('SectionSettings'),...p],[op('read')],{type:'SECTIONSETTINGS'});
  }
  for(const [code,values]of [[63,[-1,0,256,257]],[70,[-1,0,100,101]],[71,[-1,0,100,101]],[8,['','\\U+0000','\\U+D800','字😀']],[2,['ANSI31','\\U+0000']],[40,[-0,-1,1e200]]])for(const value of values){
    const p=sectionSettingsPacket({count:1}),start=p.findIndex(([c,v])=>c===2&&v==='SectionGeometrySettings'),index=p.findIndex(([c],i)=>i>start&&c===code);p[index]=[code,value];add(`geometry-value/${code}/${JSON.stringify(value)}`,'SectionSettings',[...header('SectionSettings'),...p],[op('read')],{type:'SECTIONSETTINGS'});
  }
  for(const type of ['SECTION_MANAGER','SECTIONMANAGER'])for(const flag of [-1,0,1,2])for(const count of [-1,0,1,65536,65537])
    add(`count/${type}/${flag}/${count}`,'SectionManager',[[5,'C00'],[100,'AcDbSectionManager'],[70,flag],[90,count]],lifecycle('SectionManager'),{type});
  // Unsupported private XRECORD controls remain opaque; ordinary records are not claimed here.
  for(const mode of ['text','binary','legacy'])for(const body of [[],[[1,'plain']],[[1000,'private']],[[102,'{Private'],[1001,'inside'],[102,'}']],[[102,'{Private'],[1000,'inside'],[102,'}'],[1001,'APP'],[1000,'outside']],[[1001,'APP'],[1000,'ordinary XData']]])
    add(`private/${mode}/${result.length}`,'PrivateXRecord',[[5,'C00'],[100,'AcDbXrecord'],[280,1],...body],[op('read')],{mode,type:'XRECORD'});
  for(const mode of ['text','binary','legacy'])for(const type of ['SPATIAL_INDEX','VBA_PROJECT']) {
    const p=type==='SPATIAL_INDEX'?[[100,'AcDbIndex'],[40,-0],[100,'AcDbSpatialIndex']]:[[100,'AcDbVbaProject'],[90,3],[310,{bytes:[0,128,255]}]];
    const steps=[op('read'),op('register'),op('write')];
    add('envelope/'+mode+'/'+type,'StoredEnvelope',p,steps,{mode,type});
    for(let i=0;i<p.length;i++){add(`envelope-missing/${mode}/${type}/${i}`,'StoredEnvelope',p.filter((_,j)=>j!==i),[op('read')],{mode,type});add(`envelope-duplicate/${mode}/${type}/${i}`,'StoredEnvelope',[...p.slice(0,i),p[i],...p.slice(i)],[op('read')],{mode,type});}
    add('envelope-private/'+mode+'/'+type,'StoredEnvelope',[...p,[100,'Private'],[1,'payload']],[op('read')],{mode,type});
  }
  for(const kind of ['StoredField','StoredDimAssoc','StoredSunStudy','StoredTableContent','StoredTableGeometry','StoredCellStyleMap','TableStyle','SectionManager']) {
    const {type,packet}=spec[kind],cpp={StoredField:'AcDbField',StoredDimAssoc:'AcDbDimAssoc',StoredSunStudy:'AcDbSunStudy',StoredTableContent:'AcDbTableContent',StoredTableGeometry:'AcDbTableGeometry',StoredCellStyleMap:'AcDbCellStyleMap',TableStyle:'AcDbTableStyle',SectionManager:'AcDbSectionManager'}[kind];
    for(const isTyped of [false,true])for(const wrong of [false,true])for(const entity of [false,true]) {
      const steps=isTyped?[op('read'),registration(kind),op('resolve')]:[op('opaque')];
      add(`class/${isTyped}/${wrong}/${entity}`,kind,[...header(kind),...packet],[...steps,{method:'class',name:type,cpp:wrong?'PrivateCpp':cpp,entity},op(kind==='StoredSunStudy'?'write':'prepare')],{type});
    }
  }
  let state=0x553ac01;const random=n=>(state=(Math.imul(state,1664525)+1013904223)>>>0)%n;
  for(let i=0;i<40;i++) {
    const count=random(5),sources=Array.from({length:random(8)},()=>random(3)?h('line'):'0');
    add(`random/${i}`,'SectionSettings',[...header('SectionSettings'),...sectionSettingsPacket({count,sources,types:random(4),repeat:!!random(2),color:random(2)?62:63})],lifecycle('SectionSettings'),{type:i%2?'SECTIONSETTINGS':'SECTION_SETTINGS',mode:i%2?'binary':'text'});
  }
  return result;
}
