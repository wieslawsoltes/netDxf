// Independent production-C# inputs. No expected values or model algorithms are defined here.
import { D, I, R, V, E, A } from './geometry-corpus.mjs';
const N=(type,args=[],id='p',nonPublic=false)=>({kind:'new',type:type.includes('.')||type.startsWith('List<')?type:'Objects.'+type,args,id,...(nonPublic?{nonPublic:true}:{})});
const G=(target,member,id,nonPublic=false)=>({kind:'get',target,member,id,...(nonPublic?{nonPublic:true}:{})});
const S=(target,member,value,nonPublic=false)=>({kind:'set',target,member,value,...(nonPublic?{nonPublic:true}:{})});
const C=(target,member,args=[],id,nonPublic=false)=>({kind:'call',target,member,args,...(id?{id}:{}),...(nonPublic?{nonPublic:true}:{})});
const shell=(target='p',id='copy')=>C(target,'CloneShell',[],id,true);
const snap=target=>({kind:'snapshot',target});
const P=()=>({new:'Objects.DxfPlaceholder',args:[]});
const tag=(code,value)=>({new:'IO.DxfTag',args:[{short:code},value]});
const bytes=count=>A('Byte',Array.from({length:count},(_,i)=>({byte:(i*37)&255})));
const column=(type,name,values)=>({new:'Objects.DxfDataColumn',args:[E('Objects.DxfDataCellType',type),name,A('Object',values)]});
const cols=values=>A('Objects.DxfDataColumn',values);
const refEqual=(a,b)=>({kind:'reference-equals',args:[R(a),R(b)]});
const opaque=(id,code)=>N('DxfOpaqueObject',[code,A('IO.DxfTag',[])],id,true);
const key=n=>Object.is(n,-0)?'-0':String(n);
export function databaseModelCorpus() {
  const corpus=[],add=(name,category,steps)=>corpus.push({name,category,request:{steps}});
  for(const type of ['DxfDictionary','DxfDictionaryWithDefault','DxfPlaceholder','DxfDictionaryVariable','DxfXRecord','DxfObjectPointer','DxfDataTable','DxfSun'])
    add('defaults/'+type,'models',[N(type),C('p','ToString'),shell(),snap('copy')]);
  for(const type of ['DxfDictionary','DxfDictionaryWithDefault'])for(const flag of [-1,0,1,2,3,4,5,6,32767])
    add(`dictionary/cloning/${type}/${flag}`,'dictionary',[N(type),S('p','Cloning',E('Objects.DictionaryCloningFlags',flag)),S('p','IsHardOwner',false),shell(),snap('p'),snap('copy')]);
  for(const name of [null,'',' ','\t','A','a\0b','a\rb','a\nb','Case','σ','ς','İ','ı','ß','SS','a/b','a|b','日本😀'])for(const type of ['DxfDictionary','DxfDictionaryWithDefault'])
    add(`dictionary/name/${type}/${JSON.stringify(name)}`,'dictionary',[N(type),N('DxfPlaceholder',[],'target'),C('p','Add',[name,R('target'),true]),snap('p'),snap('target'),C('p','Contains',[name]),{kind:'index',target:'p',args:[name],signature:['String']},C('p','Remove',[name]),snap('p'),snap('target')]);
  for(const pair of [['A','a'],['σ','ς'],['İ','i'],['ı','I'],['ß','SS'],['𐐀','𐐨']])
    add('dictionary/casing/'+pair.join('/'),'dictionary',[N('DxfDictionary'),N('DxfPlaceholder',[],'target'),C('p','Add',[pair[0],R('target'),true]),C('p','Add',[pair[1],P(),true]),C('p','Contains',[pair[1]]),snap('p')]);
  for(const target of [null,P(),{new:'Entities.Point',args:[]},{new:'Tables.Layer',args:['EXISTING']},{new:'Objects.DxfSun',args:[]}])for(const dictionaryHard of [false,true])for(const entryHard of [false,true])
    add('dictionary/target/'+corpus.length,'dictionary',[N('DxfDictionary'),S('p','IsHardOwner',dictionaryHard),C('p','Add',['A',target,entryHard]),snap('p'),shell()]);
  for(const erasedParent of [false,true])for(const erasedChild of [false,true])for(const alias of [false,true])
    add(`dictionary/erased/${erasedParent}/${erasedChild}/${alias}`,'dictionary',[N('DxfDictionary'),N('DxfPlaceholder',[],'child'),...(alias?[C('p','Add',['OLD',R('child'),false])]:[]),S('p','IsErased',erasedParent,true),S('child','IsErased',erasedChild,true),C('p','Add',['NEW',R('child'),true]),C('p','Remove',['OLD']),snap('p'),snap('child')]);
  add('dictionary/ownership','dictionary',[N('DxfDictionary'),N('DxfDictionary',[],'second'),N('DxfPlaceholder',[],'child'),C('p','Add',['first',R('child'),true]),C('p','Add',['alias',R('child'),false]),G('p','Entries','view'),
    C('second','Add',['cross',R('child'),false]),C('p','Remove',['first']),C('p','Remove',['alias']),snap('view'),snap('child'),C('second','Add',['still-owned',R('child'),false]),C('p','Add',['again',R('child'),false]),snap('view')]);
  add('dictionary/cycles','dictionary',[N('DxfDictionary'),N('DxfDictionary',[],'child'),C('p','Add',['SELF',R('p'),true]),C('p','Add',['CHILD',R('child'),true]),C('child','Add',['CYCLE',R('p'),false]),snap('p'),snap('child')]);
  for(const target of [null,P(),{new:'Tables.Layer',args:['L']},{new:'Entities.Point',args:[]}])
    add('dictionary/default/'+corpus.length,'dictionary',[N('DxfDictionaryWithDefault'),S('p','Default',target),C('p','Contains',['ABSENT']),{kind:'index',target:'p',args:['ABSENT']},C('p','Add',['entry',P(),false]),shell(),snap('copy'),snap('p')]);
  add('dictionary/loaded-null','dictionary',[N('DxfDictionaryWithDefault'),S('p','Default',P()),C('p','AddLoaded',['NULL',null,false],null,true),{kind:'index',target:'p',args:['NULL']},C('p','AddLoaded',['null',P(),true],null,true),snap('p')]);
  for(const value of [-32768,-1,0,1,255,256,32767])add('variable/schema/'+value,'variable',[N('DxfDictionaryVariable'),S('p','Schema',{short:value}),shell(),snap('p')]);
  for(const value of [null,'','a\0b','a\rb','a\nb','日本😀',{utf16:[0xd800]}])add('variable/value/'+JSON.stringify(value),'variable',[N('DxfDictionaryVariable'),S('p','Value',value),shell(),snap('p')]);
  const payloads=[null,tag(0,'INVALID'),tag(1,'data'),tag(5,'A'),tag(10,D(-0)),tag(90,I(-2147483648)),tag(102,'{PAYLOAD'),tag(105,'A'),tag(160,{long:'-9223372036854775808'}),tag(290,true),tag(330,'A'),tag(360,'B'),tag(369,'0'),tag(370,{short:3}),tag(999,'comment'),tag(1000,'XDATA')];
  for(const count of [0,1,127,128,255])payloads.push(tag(310,bytes(count)));
  for(const [i,value] of payloads.entries())for(const preserved of [false,true])
    add(`xrecord/payload/${i}/${preserved}`,'xrecord',[N('DxfXRecord'),G('p','Data','data'),preserved?C('p','AddLoadedData',[value],null,true):C('data','Add',[value]),snap('p'),shell(),snap('copy')]);
  for(const method of ['Insert','set_Item','RemoveAt'])for(const index of [-1,0,1,2])for(const invalidValue of [false,true])
    add(`xrecord/edit/${method}/${index}/${invalidValue}`,'xrecord',[N('DxfXRecord'),G('p','Data','data'),C('data','Add',[tag(1,'seed')]),C('data',method,[I(index),...(method==='RemoveAt'?[]:[invalidValue?null:tag(1,'new')])]),snap('p')]);
  const envelope=[tag(102,'ACAD_ROUNDTRIP_2008_TABLE_ENTITY'),tag(360,'A'),tag(361,'B')];
  for(let defect=0;defect<10;defect++) {
    let tags=envelope.slice();
    if(defect===1)tags[0]=tag(102,'wrong');if(defect===2)tags.splice(1,1);if(defect===3)tags.push(tag(360,'C'));if(defect===4)tags.push(tag(350,'D'));if(defect===5)tags.push(tag(102,'MARKER'));if(defect===6)tags.push(tag(340,'E'));
    add('xrecord/schema/'+defect,'ownership',[N('DxfXRecord'),G('p','Data','data'),...tags.map(t=>C('data','Add',[t])),opaque('content',defect===7?'OTHER':'TABLECONTENT'),opaque('geometry','TABLEGEOMETRY'),
      ...(defect===8?[S('geometry','IsErased',true,true)]:[]),...(defect===9?[N('DxfDictionary',[],'other'),S('geometry','Owner',R('other'),true)]:[]),
      C('p','BindTableRoundtripChildren',[R('content'),R('geometry')],null,true),snap('p'),snap('content'),snap('geometry'),C('data','Add',[tag(1,'edit')]),C('data','Clear'),snap('p'),shell(),snap('copy')]);
  }
  add('xrecord/schema-materialize','ownership',[N('DxfXRecord'),G('p','Data','data'),...envelope.map(t=>C('data','Add',[t])),opaque('content','TABLECONTENT'),opaque('geometry','TABLEGEOMETRY'),
    C('p','BindTableRoundtripChildren',[R('content'),R('geometry')],null,true),S('content','Handle','AC'),S('geometry','Handle','BC'),C('p','MaterializeOwnedObjectReferences',[],null,true),snap('p'),
    C('p','ReplaceLoadedData',[I(0),tag(1,'BROKEN')],null,true),N('List<String>',[],'errors'),C('p','ValidateDatabaseSchema',[null,R('errors')],null,true),snap('errors')]);
  const compositeCodes=[102,360,70,90,10,20,30,90,90,361,102,90,91,102,360];
  const compositeValues=['ACAD_ROUNDTRIP_2008_TABLE_ENTITY','A',{short:0},I(4),D(1),D(2),D(3),I(5),I(6),'B','ACAD_ROUNDTRIP_PRE2007_TABLE',I(0),I(0),'ACAD_ROUNDTRIP_PRE2007_TABLECELL','C'];
  for(let defect=0;defect<8;defect++) {
    const tags=compositeCodes.map((code,i)=>tag(code,compositeValues[i]));if(defect===1)tags.pop();if(defect===2)tags[10]=tag(102,'WRONG');if(defect===3)tags[14]=tag(340,'C');
    add('xrecord/composite/'+defect,'ownership',[N('DxfXRecord'),G('p','Data','data'),...tags.map(t=>C('data','Add',[t])),opaque('content','TABLECONTENT'),opaque('geometry','TABLEGEOMETRY'),N('DxfDataTable',[],'table'),
      ...(defect===4?[S('table','IsErased',true,true)]:[]),...(defect===5?[S('content','Owner',R('table'),true)]:[]),...(defect===6?[S('p','IsErased',true,true)]:[]),
      C('p','BindCompositeTableRoundtripChildren',[R('content'),R('geometry'),defect===7?null:R('table')],null,true),snap('p'),snap('content'),snap('geometry'),snap('table'),
      C('data','set_Item',[I(0),tag(102,'EDIT')]),shell()]);
  }
  const samples=[null,I(3),D(3),D(.25),{short:3},{byte:3},true,false,'text','',V('Vector3',1,2,3),V('Vector3',1,2,NaN),D(Infinity),D(NaN),P(),{new:'Entities.Point',args:[]}];
  for(let type=1;type<=11;type++)for(let i=0;i<samples.length;i++)add(`column/type/${type}/${i}`,'datatable',[N('DxfDataColumn',[E('Objects.DxfDataCellType',type),'Column',A('Object',[samples[i]])])]);
  for(const name of [null,'',' ', '日本😀','a\0b','a\rb','a\nb',{utf16:[0xd800]},{utf16:[0xdc00]}])for(const type of ['DxfDataColumn','DxfDataTable'])
    add('column/name/'+type+'/'+JSON.stringify(name),'datatable',type==='DxfDataColumn'?[N(type,[E('Objects.DxfDataCellType',1),name,A('Object',[])])]:[N(type),S('p','Name',name),snap('p')]);
  for(const type of [-1,0,12,32767])add('column/enum/'+type,'datatable',[N('DxfDataColumn',[E('Objects.DxfDataCellType',type),'',A('Object',[])])]);
  for(const rows of [-1,0,1,2,1048576,1048577])for(const values of [null,cols([]),cols([null]),cols([column(1,'A',[I(1)])]),cols([column(1,'A',[I(1)]),column(3,'B',['text'])])])
    add('datatable/dimensions/'+corpus.length,'datatable',[N('DxfDataTable'),C('p','SetColumns',[I(rows),values]),snap('p'),shell()]);
  for(const ownership of [5,6,7,8,9])add('datatable/ownership/'+ownership,'datatable',[N('DxfDataTable'),N('DxfPlaceholder',[],'child'),C('p','SetColumns',[I(1),cols([column(ownership,'owner',[R('child')])])]),snap('p'),snap('child'),
    C('p','SetColumns',[I(1),cols([column(ownership,'first',[R('child')]),column(ownership,'again',[R('child')])])]),snap('p'),N('DxfDataTable',[],'second'),G('p','Columns','columns'),C('second','SetColumns',[I(1),R('columns')]),snap('second'),C('p','SetColumns',[I(0),cols([])]),snap('child')]);
  for(const erased of [false,true])add('datatable/self/'+erased,'datatable',[N('DxfDataTable'),S('p','IsErased',erased,true),C('p','SetColumns',[I(1),cols([column(6,'self',[R('p')])])]),snap('p')]);
  add('datatable/value-snapshot','datatable',[N('netDxf.Vector3',[D(1),D(2),D(3)],'coordinate'),N('DxfDataColumn',[E('Objects.DxfDataCellType',4),'point',A('Object',[{copy:R('coordinate')}])],'column'),S('coordinate','X',D(99)),snap('column'),N('DxfDataTable'),C('p','SetColumns',[I(1),cols([R('column')])]),shell()]);
  for(const [member,values] of Object.entries({ColorIndex:[-1,0,1,256,257],TrueColor:[null,-1,0,0xffffff,0x1000000],Intensity:[-Infinity,-1,-0,0,Number.MIN_VALUE,Infinity,NaN],ShadowType:[-1,0,1,2,3],ShadowMapSize:[63,64,65,128,256,4096,4097],JulianDay:[-2147483648,0,2147483647],StoredTime:[-2147483648,0,2147483647],ShadowSoftness:[0,1,255],Enabled:[false,true],ShadowsEnabled:[false,true],DaylightSavingTime:[false,true]}))
    for(const value of values)for(const erased of [false,true]) {
      const descriptor=value===null?null:typeof value==='boolean'?value:member==='Intensity'?D(value):member==='ShadowSoftness'?{byte:value}:member==='ShadowType'?E('Objects.DxfSunShadowType',value):['ColorIndex','ShadowMapSize'].includes(member)?{short:value}:I(value);
      add(`sun/${member}/${key(value)}/${erased}`,'sun',[N('DxfSun'),S('p','IsErased',erased,true),S('p',member,descriptor),snap('p'),shell()]);
    }
  for(const type of ['DxfObjectPointer','DxfOpaqueObject'])add('object-shell/'+type,'models',[type==='DxfOpaqueObject'?opaque('p','CUSTOM'):N(type),shell()]);

  add('ownership/reference-copy','ownership',[N('DxfXRecord'),G('p','Data','data'),...envelope.map(t=>C('data','Add',[t])),opaque('a','TABLECONTENT'),opaque('b','TABLEGEOMETRY'),C('p','BindTableRoundtripChildren',[R('a'),R('b')],null,true),shell(),opaque('na','TABLECONTENT'),opaque('nb','TABLEGEOMETRY'),
    C('p','CopyDatabaseReferencesTo',[R('copy'),{resolver:[[R('a'),R('na')],[R('b'),R('nb')]]}],null,true),snap('copy'),snap('na'),snap('a')]);
  add('datatable/reference-copy','datatable',[N('DxfDataTable'),N('DxfPlaceholder',[],'owned'),N('Entities.Point',[],'pointer'),C('p','SetColumns',[I(2),cols([column(6,'owned',[R('owned'),null]),column(8,'refs',[R('pointer'),R('pointer')])])]),shell(),N('DxfPlaceholder',[],'newOwned'),N('Entities.Point',[],'newPointer'),
    C('p','CopyDatabaseReferencesTo',[R('copy'),{resolver:[[R('owned'),R('newOwned')],[R('pointer'),R('newPointer')]]}],null,true),snap('copy'),snap('newOwned'),snap('owned')]);
  return corpus;
}
