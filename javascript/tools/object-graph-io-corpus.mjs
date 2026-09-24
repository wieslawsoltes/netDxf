// Input-only OBJECTS records. Full document/table construction remains a separate qualification.
import { retainedTag,sectionSettingsPacket } from './retained-record-io-corpus.mjs';
import { tableStylePacket,cellMapPacket } from './table-style-corpus.mjs';
import { tableContentPacket } from './table-content-corpus.mjs';
import { tableGeometryPacket } from './table-geometry-corpus.mjs';
import { dataPacket,spatialPacket } from './database-payload-corpus.mjs';
const next={method:'next'},read=(id,extra={})=>({method:'read',id,...extra}),op=method=>({method});
const h=name=>({h:name});
const root=(entries=[],extra=[])=>[[0,'DICTIONARY'],[5,'C0'],[330,'0'],...extra,[100,'AcDbDictionary'],[281,1],...entries.flatMap(([name,handle,hard=true])=>[[3,name],[hard?360:350,handle]])];
const object=(code,body=[],handle='C1',owner='C0',header=[])=>[[0,code],[5,handle],[330,owner],...header,...body];
export function objectGraphIOCorpus(){
  const all=[];
  const add=(name,tags,steps,{mode='text',version=18,admitResources=false}={})=>all.push({name:'object-graph-io/'+name,request:{op:'object-graph-io',mode,version,admitResources,tags:[[0,'SECTION'],[2,'OBJECTS'],...tags,[0,'ENDSEC'],[0,'EOF']].map(([c,v])=>retainedTag(c,v)),steps:[next,next,next,...steps]}});
  const leafBodies=[
    ['ACDBPLACEHOLDER',[]],['DICTIONARYVAR',[[100,'DictionaryVariables'],[280,0],[1,'Value \\U+03A9']]],
    ['XRECORD',[[100,'AcDbXrecord'],[280,1],[1,'literal\\U+005CU+0041'],[10,-0],[310,{bytes:[0,1,255]}],[330,'0']]],
    ['DICTIONARY',[[100,'AcDbDictionary']]],['ACDBDICTIONARYWDFLT',[[100,'AcDbDictionary'],[280,0],[281,1],[100,'AcDbDictionaryWithDefault'],[340,'0']]],
    ['IDBUFFER',[[100,'AcDbIdBuffer'],[330,'0'],[330,'0']]],
    ['OBJECT_PTR',[]],['LAYER_FILTER',[[100,'AcDbFilter'],[100,'AcDbLayerFilter'],[8,'0'],[8,'Layer \\U+03A9']]],
    ['LIGHTLIST',[[100,'AcDbLightList'],[90,0],[90,0]]],['DATATABLE',dataPacket([[1,'Count',[[93,5]]]],1)],
    ['TABLEGEOMETRY',[[100,'AcDbTableGeometry'],[90,0],[91,0],[92,0]]],
    ['SPATIAL_INDEX',[[100,'AcDbIndex'],[40,-0],[100,'AcDbSpatialIndex']]],['VBA_PROJECT',[[100,'AcDbVbaProject'],[90,2],[310,{bytes:[1,3]}]]],
    ['MLEADERSTYLE',[[100,'AcDbMLeaderStyle'],[179,2]]],['VENDOR_OPAQUE',[[100,'AcDbVendor'],[1,'opaque\\U+0041'],[330,'0']]],
  ];
  for(const mode of ['text','binary','legacy'])for(const [type,body]of leafBodies)for(const version of [13,14,15,16,17,18]){
    add(`${mode}/${type}/profile/${version}`,[...root([['Child','C1']]),...object(type,body)],[read('root',{kind:'dictionary',root:true}),read('item'),op('import'),op('transport'),op('prepare'),{method:'write',target:'root'},op('write'),op('validate')],{mode,version});
  }
  for(const [type,body]of leafBodies){
    for(let i=0;i<body.length;i++)for(const mutation of ['missing','duplicate']){
      const changed=mutation==='missing'?body.filter((_,j)=>j!==i):body.flatMap((v,j)=>j===i?[v,v]:[v]);
      add(`${type}/${mutation}/${i}`,[...root([['Child','C1']]),...object(type,changed)],[read('root',{kind:'dictionary',root:true}),read('item'),op('import'),op('write')]);
    }
    for(const header of [[],[[102,'{VENDOR'],[1,'private'],[102,'}']],[[102,'{ACAD_REACTORS'],[330,'C0'],[330,'C0'],[102,'}']],[[102,'{ACAD_REACTORS'],[330,'AB'],[102,'}']],[[102,'{ACAD_XDICTIONARY'],[360,'AB'],[102,'}']]]){
      add(`${type}/header/${JSON.stringify(header)}`,[...root([['Child','C1']]),...object(type,body,'C1','C0',header)],[read('root',{kind:'dictionary',root:true}),read('item'),op('import'),op('write'),op('validate')]);
    }
    for(const at of [1,2,3,4,7,10,14,21])add(`${type}/throw/${at}`,[...root([['Child','C1']]),...object(type,body)],[read('root',{kind:'dictionary',root:true}),read('item'),op('import'),{method:'write',throwAt:at},op('write')]);
    add(`${type}/orphan`,object(type,body,'C1','0'),[read('item'),op('import'),op('write'),op('validate')]);
    for(const text of ['Zażółć 東京','literal\\U+0041','a\\U+000Ab','a\\U+000Db'])add(`${type}/xdata/${text}`,[...root([['Child','C1']]),...object(type,[...body,[1001,'APP'],[1000,text]])],[read('root',{kind:'dictionary',root:true}),read('item'),op('import'),op('transport'),op('write')]);
  }
  for(const type of ['FIELD','ACAD_FIELD','DIMASSOC','SUN','SUNSTUDY','SECTIONSETTINGS','SECTION_SETTINGS','SECTIONMANAGER','SECTION_MANAGER','TABLESTYLE','TABLECONTENT','CELLSTYLEMAP','LAYER_INDEX']){
    for(const body of [[],[[100,'Vendor']],[[100,'Vendor'],[90,1]],[[100,'Vendor'],[1001,'APP'],[1000,'retained']]])
      add(`${type}/opaque/${body.length}`,[...root([['Child','C1']]),...object(type,body)],[read('root',{kind:'dictionary',root:true}),read('item'),op('import'),op('prepare'),op('write')]);
  }
  // Physical duplicates, noncanonical spelling and refusal of generated defaults.
  for(const handle of ['C1','c1','00C1','0','FFFFFFFFFFFFFFFF','7FFFFFFFFFFFFFFF','7FFFFFFFFFFFFFFE']){
    add(`identity/${handle}`,[...root([['Child',handle]]),...object('ACDBPLACEHOLDER',[],handle)],[read('root',{kind:'dictionary',root:true}),read('item'),op('import'),op('write')]);
    add(`duplicate/${handle}`,[...root([['Child','C1']]),...object('ACDBPLACEHOLDER',[]),...object('ACDBPLACEHOLDER',[],handle)],[read('root',{kind:'dictionary',root:true}),read('one'),read('item'),op('import')]);
  }
  for(const admitResources of [false,true])for(const target of ['line','light','block','style','layer']){
    add(`resource/${target}/${admitResources}`,[...root([['buffer','C1']]),...object('IDBUFFER',[[100,'AcDbIdBuffer'],[330,h(target)]])],[read('root',{kind:'dictionary',root:true}),read('item'),op('import'),op('write'),{method:'lookup',handle:h(target)}],{admitResources});
  }
  for(const code of [330,340,350,360,390,480,1005])for(const reference of ['C0','C2','FFF']){
    add(`xrecord/reference/${code}/${reference}`,[...root([['Data','C1'],['Other','C2']]),...object('XRECORD',[[100,'AcDbXrecord'],[code,reference]]),...object('ACDBPLACEHOLDER',[],'C2')],[read('root',{kind:'dictionary',root:true}),read('item'),read('other'),op('import'),op('write'),op('validate')]);
  }
  for(const entries of [[['one','C1'],['two','C1',false]],[['one','C1'],['one','C1']],[['one','0']],[['ACAD_GROUP','F00']],[]]){
    add(`dictionary/entries/${JSON.stringify(entries)}`,[...root(entries),...object('ACDBPLACEHOLDER')],[read('root',{kind:'dictionary',root:true}),read('item'),op('import'),{method:'write',target:'root'},op('validate')]);
  }
  for(const body of [[[3,'unpaired']],[[360,'C1']],[[3,'first'],[3,'second'],[360,'C1']],[[3,'first'],[999,'comment'],[360,'C1']]])
    add(`dictionary/pairs/${JSON.stringify(body)}`,[...root([]),...object('DICTIONARY',[[100,'AcDbDictionary'],...body])],[read('root',{kind:'dictionary',root:true}),read('item'),op('import')]);
  for(const header of [[[102,'{A'],[102,'{B'],[330,'FA'],[102,'}'],[102,'}']],[[102,'{A'],[330,'FA']],[[102,'}']],[[5,'C2']],[[330,'FA']]])
    add(`common/${JSON.stringify(header)}`,[...root([['Child','C1']]),...object('ACDBPLACEHOLDER',[],'C1','C0',header)],[read('root',{kind:'dictionary',root:true}),read('item'),op('import')]);
  for(const kind of ['record','xrecord'])for(const body of [[[100,'AcDbXrecord'],[280,1],[440,33554432],[370,-1]],[[100,'AcDbXrecord'],[102,'{PAYLOAD'],[330,'C0'],[102,'}']],[[100,'AcDbXrecord'],[1000,'private orphan']],[[100,'Wrong']]])
    add(`legacy/${kind}/${JSON.stringify(body)}`,object('XRECORD',body,'C1','0'),[read('item',{kind}),op('import'),op('write')]);
  for(const name of ['DICTIONARYVAR','IDBUFFER','ACDBPLACEHOLDER','FIELD','SECTION_MANAGER','MLEADERSTYLE','ACAD_TABLE'])for(const cpp of ['Wrong','AcDbPlaceHolder'])
    add(`class/${name}/${cpp}`,[],[{method:'class',name,cpp},op('prepare')]);
  add('empty-import',[],[op('import'),op('snapshot')]);
  add('reciprocal-extension',[...root([['host','C1']]),...object('ACDBPLACEHOLDER',[],'C1','C0',[[102,'{ACAD_XDICTIONARY'],[360,'C2'],[102,'}']]),...object('DICTIONARY',[[100,'AcDbDictionary']],'C2','C1')],[read('root',{kind:'dictionary',root:true}),read('item'),read('extension'),op('import'),op('write'),op('validate')]);
  for(const automatic of [[],['C1','c1','00C1','C0'],['C0',null],['AB','ab']])
    add('metadata/automatic/'+JSON.stringify(automatic),[...root([['Child','C1']]),...object('ACDBPLACEHOLDER')],[read('root',{kind:'dictionary',root:true}),read('item'),op('import'),{method:'metadata',automatic}]);
  for(const extra of [[[340,{h:'style'}]],[[340,{h:'style'}],[999,'ignored']],[[340,{h:'style'}],[300,'private']],[[340,{h:'style'}],[179,2]],[[100,'Other']]])
    add('mleader/style/'+JSON.stringify(extra),[...root([['Child','C1']]),...object('MLEADERSTYLE',[[100,'AcDbMLeaderStyle'],[179,2],...extra])],[read('root',{kind:'dictionary',root:true}),read('item'),op('import'),op('write')],{admitResources:true});
  for(const name of ['__proto__','constructor','toString','hasOwnProperty','valueOf'])add('dispatch/name/'+name,object(name,[[100,'Vendor'],[1,'retained']],'C1','0'),[read('item'),op('import'),op('write')]);
  const completeBodies=[
    ['FIELD',[[100,'AcDbField'],[1,'AcVar'],[2,'expression'],[90,0],[97,0]]],
    ['TABLESTYLE',tableStylePacket()],['CELLSTYLEMAP',cellMapPacket(2)],
    ['TABLEGEOMETRY',tableGeometryPacket({cells:0})],['TABLECONTENT',tableContentPacket()],
    ['SECTIONMANAGER',[[100,'AcDbSectionManager'],[70,0],[90,0]]],
    ['SECTIONSETTINGS',sectionSettingsPacket({types:0})],['SPATIAL_FILTER',spatialPacket(true,true)],
  ];
  for(const [name,body]of completeBodies)for(const mode of ['text','binary','legacy']){
    add(`complete/${name}/${mode}`,[...root([['Child','C1']]),...object(name,body)],[read('root',{kind:'dictionary',root:true}),read('item'),op('import'),op('transport'),op('write'),op('prepare'),op('validate')],{mode,admitResources:true});
  }
  for(const type of ['DICTIONARY','XRECORD'])for(const mode of ['text','binary','legacy'])for(const cloning of [32767,32768,65535,65536,-32769,-2147483648,2147483647]){
    add(`cloning/${type}/${mode}/${cloning}`,[...root([['Child','C1']]),...object(type,[[100,type==='DICTIONARY'?'AcDbDictionary':'AcDbXrecord']])],[read('root',{kind:'dictionary',root:true}),read('item'),op('import'),{method:'set',property:'Cloning',value:{int:cloning}},op('write')],{mode});
  }
  for(const when of [1,2,7,12,14])add(`writer/version-callback/${when}`,[...root([['Ω','C1']]),...object('DICTIONARYVAR',[[1,'value Ω']])],[read('root',{kind:'dictionary',root:true}),read('item'),op('import'),{method:'write',target:'root',versionAt:when,newVersion:13}],{version:18});
  for(const name of ['ACAD_GROUP','ACAD_LAYOUT','ACAD_MLINESTYLE','ACAD_IMAGE_DICT','ACAD_DGNDEFINITIONS','ACAD_DWFDEFINITIONS','ACAD_PDFDEFINITIONS']){
    const table={ACAD_GROUP:'groups',ACAD_LAYOUT:'layouts',ACAD_MLINESTYLE:'mlineStyles',ACAD_IMAGE_DICT:'images',ACAD_DGNDEFINITIONS:'dgn',ACAD_DWFDEFINITIONS:'dwf',ACAD_PDFDEFINITIONS:'pdf'}[name];
    add('managed/'+name,[...root([[name,h(table)]]),...object('DICTIONARY',[[100,'AcDbDictionary']],h(table))],[read('root',{kind:'dictionary',root:true}),read('item',{kind:'dictionary'}),op('import'),{method:'lookup',handle:h(table)},{method:'write',target:'root'}]);
  }
  for(const code of ['XRECORD','ACDBPLACEHOLDER','DICTIONARYVAR']){
    const body=code==='XRECORD'?[[100,'AcDbXrecord'],[1,'text']]:code==='DICTIONARYVAR'?[[1,'text']]:[];
    add('order/child-first/'+code,[...object(code,body),...root([['Child','C1']])],[read('item'),read('root',{kind:'dictionary',root:true}),op('import'),op('write'),op('import')]);
    add('metadata/replace/'+code,[...root([['Child','C1']]),...object(code,body)],[read('root',{kind:'dictionary',root:true}),read('item'),op('import'),{method:'replace'},op('metadata'),op('write')]);
    add('metadata/null/'+code,[...root([['Child','C1']]),...object(code,body)],[read('root',{kind:'dictionary',root:true}),read('item'),op('import'),{method:'reactor',value:null},op('metadata'),op('write')]);
  }

  for(const mode of ['text','binary','legacy'])for(const version of [13,18])for(const name of ['Unicode Ω','literal\\U+0041','']){
    add(`generated/${mode}/${version}/${name}`,[...root([['Typed','C1']]),...object('ACDBPLACEHOLDER')],[read('root',{kind:'dictionary',root:true}),read('item'),op('import'),{method:'generated',entries:[['AB',name]]},{method:'write',target:'root',generated:true}],{mode,version});
  }
  for(const kind of ['DICTIONARYVAR','XRECORD'])for(const at of [1,2,7,8,9,10,12]){
    const body=kind==='DICTIONARYVAR'?[[100,'DictionaryVariables'],[1,'Value Ω']]:[[100,'AcDbXrecord'],[1,'Value Ω'],[1,'Second Ω']];
    add(`dynamic-version/${kind}/${at}`,[...root([['Entry','C1']]),...object(kind,body)],[read('root',{kind:'dictionary',root:true}),read('item'),op('import'),{method:'write',versionAt:at,newVersion:13}]);
  }
  // Independent deterministic graph ordering; no expected nodes or bytes embedded.
  let state=0x15ca70d3;const random=n=>(state=(Math.imul(state,1664525)+1013904223)>>>0)%n;
  for(let run=0;run<32;run++){
    const count=1+random(16),children=Array.from({length:count},(_,i)=>({id:'child'+i,handle:(0xd00+i).toString(16).toUpperCase(),name:'Child '+i+' Ω'})),entries=children.map(c=>[c.name,c.handle]),objects=children.map(c=>({id:c.id,tags:object('XRECORD',[[100,'AcDbXrecord'],[280,random(6)],[1,'value '+random(12345)+' \\U+005C'],[90,random(2000)-1000],[330,'C0'],[310,{bytes:[random(256),random(256)]}]],c.handle)}));
    objects.push({id:'root',tags:root(entries)});
    for(let i=objects.length-1;i>0;i--){const j=random(i+1);[objects[i],objects[j]]=[objects[j],objects[i]];}
    add(`random/${run}`,objects.flatMap(o=>o.tags),[...objects.map(o=>read(o.id,o.id==='root'?{kind:'dictionary',root:true}:{})),op('import'),op('validate'),...children.map(c=>({method:'write',target:c.id})),{method:'write',target:'root'}],{mode:['text','binary','legacy'][run%3],version:13+run%6});
  }
  add('mleader/private-unknown',[...root([['Style','C1']]),...object('MLEADERSTYLE',[[100,'AcDbMLeaderStyle'],[179,2],[340,'C0'],[301,'private']])],[read('root',{kind:'dictionary',root:true}),read('item'),op('import'),op('write')]);
  return all;
}
