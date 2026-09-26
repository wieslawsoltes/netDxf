// Inputs only. Deterministic date/username assignments are operations against both APIs,
// not canonicalization of their results. Actual constructor clock behavior is tested separately.
import {D,I,R,V,E,A} from './geometry-corpus.mjs';
const N=(type,args=[],id='h')=>({kind:'new',type,args,id});
const C=(target,member,args=[],id,signature)=>({kind:'call',target,member,args,...(id?{id}:{}),...(signature?{signature}:{})});
const G=(target,member,id)=>({kind:'get',target,member,...(id?{id}:{})});
const S=(target,member,value)=>({kind:'set',target,member,value,nonPublic:true});
const DT=(ticks='638000000000000123',kind=1)=>({datetime:{ticks,kind}});
const TS=ticks=>({timespan:String(ticks)});
const init=()=>[N('Header.HeaderVariables'),S('h','LastSavedBy','exact-test-user'),
  ...['TdCreate','TduCreate','TdUpdate','TduUpdate'].map((name,i)=>S('h',name,DT(String(638000000000000123n+BigInt(i)),i%3)))];
const known=()=>C('h','KnownValues');
const snapshot=target=>({kind:'snapshot',target});
const item=(target,index,id)=>({kind:'index',target,args:[I(index)],id});
const variable=(name,value=D(1),code=40,id='v')=>N('Header.HeaderVariable',[name,{short:code},value],id);
export function headerCorpus(){
  const out=[],add=(name,category,steps)=>out.push({name:'header/'+name,category,request:{steps}});
  add('defaults','header-known',[...init(),known(),C('h','KnownNames'),C('h','CustomValues'),C('h','CustomNames'),G('h','CurrentUCS')]);
  const enums={AcadVer:'Header.DxfVersion',Angdir:'Units.AngleDirection',AttMode:'Header.AttMode',AUnits:'Units.AngleUnitType',CeLweight:'Lineweight',
    CMLJust:'Entities.MLineJustification',LUnits:'Units.LinearUnitType',InsUnits:'Units.DrawingUnits',PdMode:'Header.PointShape'};
  for(const [name,type]of Object.entries(enums))for(const value of [-2147483648,-1,0,1,2,3,4,5,8,12,13,14,18,24,25,2147483647])
    add(`enum/${name}/${value}`,'header-known',[...init(),S('h',name,E(type,value)),G('h',name),G('h','InsUnits'),known()]);
  const scalars=[-Infinity,-Number.MAX_VALUE,-1,-Number.MIN_VALUE,-0,0,Number.MIN_VALUE,.25,1,Number.MAX_VALUE,Infinity,NaN];
  for(const name of ['Angbase','CeLtScale','CMLScale','TextSize','LtScale','PdSize'])for(const value of scalars)
    add(`scalar/${name}/${D(value).double}`,'header-known',[...init(),S('h',name,D(value)),G('h',name),known()]);
  for(const name of ['AUprec','LUprec','PLineGen','PsLtScale','SplineSegs','SurfU','SurfV'])for(const value of [-32768,-1,0,1,2,6,8,9,199,200,201,32767])
    add(`short/${name}/${value}`,'header-known',[...init(),S('h',name,{short:value}),G('h',name),known()]);
  for(const name of ['Extnames','MirrText','LwDisplay'])for(const value of [false,true])
    add(`boolean/${name}/${value}`,'header-known',[...init(),S('h',name,value),G('h',name),known()]);
  const texts=[null,'',' ','  spaces  ','a\0b','a\nb','\r','東京 Zażółć 😀',{utf16:[0xd800]},{utf16:[0xdc00]},'0'.repeat(300)];
  for(const name of ['CeLtype','CLayer','CMLStyle','DimStyle','TextStyle','DwgCodePage','LastSavedBy','HandleSeed'])for(const [i,value]of texts.entries())
    add(`text/${name}/${i}`,'header-known',[...init(),S('h',name,value),G('h',name),known()]);
  for(const name of ['TdCreate','TduCreate','TdUpdate','TduUpdate'])for(const ticks of [0n,1n,9999n,10000n,621355967999999999n,621355968000000000n,638000000000000123n,3155378975999999999n])for(const kind of [0,1,2])
    add(`date/${name}/${ticks}/${kind}`,'header-time',[...init(),S('h',name,DT(String(ticks),kind)),G('h',name,'date'),C('date','ToString'),known()]);
  for(const ticks of [0n,1n,-1n,10000n,-10000n,864000000000n,-864000000000n,9223372036854775807n,-9223372036854775808n])
    add(`duration/${ticks}`,'header-time',[...init(),S('h','TdinDwg',TS(ticks)),G('h','TdinDwg','time'),C('time','ToString'),known()]);
  for(const v of [[-0,1,2],[Number.MIN_VALUE,0,0],[NaN,Infinity,-Infinity]])add(`vector/${out.length}`,'header-known',[
    ...init(),N('Vector3',v.map(D),'v'),S('h','InsBase',R('v')),S('v','X',D(99)),G('h','InsBase','copy'),S('copy','Y',D(77)),G('h','InsBase'),known()]);
  add('reference-properties','header-known',[...init(),N('AciColor',[],'color'),S('h','CeColor',R('color')),S('color','Index',{short:2}),G('h','CeColor','same'),
    {kind:'reference-equals',args:[R('color'),R('same')]},S('h','CeColor',null),N('Tables.UCS',['Shared'],'ucs'),S('h','CurrentUCS',R('ucs')),S('ucs','Origin',V('Vector3',1,2,3)),G('h','CurrentUCS'),S('h','CurrentUCS',null)]);
  add('unit-side-effects','header-known',[...init(),S('h','InsUnits',E('Units.DrawingUnits',6)),S('h','LUnits',E('Units.LinearUnitType',4)),G('h','InsUnits'),
    S('h','LUnits',E('Units.LinearUnitType',2)),G('h','InsUnits'),S('h','InsUnits',E('Units.DrawingUnits',6)),S('h','LUnits',E('Units.LinearUnitType',3)),G('h','InsUnits'),known()]);
  const names=['$CUSTOM','$custom','$東京','$ŻÓŁĆ','$żółć','$ß','$SS','$Σ','$ς','$σ','$İ','$i','$ı','$ſ','$S','$😀','$','ACAD','$ACADVER','$acadver','$UCSORG'];
  for(const name of names)add('custom/'+name,'header-custom',[...init(),variable(name),C('h','AddCustomVariable',[R('v')]),C('h','ContainsCustomVariable',[name]),
    C('h','TryGetCustomVariable',[name,{out:true}],null,['String','Header.HeaderVariable&']),C('h','CustomValues'),C('h','CustomNames'),C('h','RemoveCustomVariable',[name]),C('h','CustomNames')]);
  for(const name of [null,'','$MISSING'])for(const op of ['ContainsCustomVariable','RemoveCustomVariable','TryGetCustomVariable'])
    add(`custom-null/${op}/${name}`,'header-custom',[...init(),C('h',op,op==='TryGetCustomVariable'?[name,{out:true}]:[name],null,op==='TryGetCustomVariable'?['String','Header.HeaderVariable&']:['String'])]);
  add('custom-nulls-and-known-collision','header-custom',[...init(),C('h','AddCustomVariable',[null]),variable('$lTsCaLe'),C('h','AddCustomVariable',[R('v')]),C('h','CustomValues')]);
  add('custom-slot-reuse','header-custom',[...init(),...['A','B','C','D'].flatMap((name,i)=>[variable('$'+name,D(i),40,name),C('h','AddCustomVariable',[R(name)])]),
    C('h','CustomNames',[],'old'),C('h','RemoveCustomVariable',['$b']),C('h','RemoveCustomVariable',['$d']),variable('$E',D(8),40,'E'),C('h','AddCustomVariable',[R('E')]),
    variable('$F',D(9),40,'F'),C('h','AddCustomVariable',[R('F')]),C('h','CustomNames'),snapshot('old'),C('h','ClearCustomVariables'),snapshot('old')]);
  add('known-list-aliasing','header-known',[...init(),C('h','KnownValues',[],'old'),item('old',4,'entry'),S('entry','Value',D(12)),G('h','Angbase'),
    C('old','RemoveAt',[I(0)]),C('h','KnownNames'),C('old','Clear'),known()]);
  add('custom-list-aliasing','header-custom',[...init(),variable('$A'),C('h','AddCustomVariable',[R('v')]),C('h','CustomValues',[],'old'),S('v','Value',D(12)),snapshot('old'),
    C('old','Clear'),C('h','CustomValues'),C('h','RemoveCustomVariable',['$a']),S('v','Value',D(23)),C('h','CustomValues')]);
  const fields=[['AcadVer',0],['Angbase',4],['Angdir',5],['AUprec',8],['CeColor',9],['CeLtScale',10],['CeLtype',11],['LUnits',20],['InsBase',24],['MirrText',22],['TdCreate',35],['TdinDwg',39]];
  const values=[null,D(4),{box:{short:4}},{box:I(4)},'wrong',false,V('Vector3',1,2,3),DT(),TS(4),{box:E('Header.AttMode',2)}];
  for(const [name,index]of fields)for(const [i,value]of values.entries())add(`known-box/${name}/${i}`,'header-boxing',[
    ...init(),C('h','KnownValues',[],'list'),item('list',index,'entry'),S('entry','Value',value),G('h',name),snapshot('entry')]);
  add('enum-short-formatting','header-boxing',[...init(),C('h','KnownValues',[],'list'),...[[0,'enum'],[8,'short'],[22,'bool'],[35,'date'],[39,'span']].flatMap(([i,id])=>[item('list',i,id),C(id,'ToString')])]);
  for(const type of ['int','short','byte','double','long'])add('explicit-box-format/'+type,'header-boxing',[variable('$BOX',{box:{[type]:type==='double'?D(.25).double:type==='long'?'123456789123456789':4}}),C('v','ToString'),snapshot('v')]);
  return out;
}
