// Input descriptions only. No expected parser output, geometry or exception is embedded here.
import {D,I,R,V,E,A} from './geometry-corpus.mjs';
const N=(type,args=[],id='t',signature)=>({kind:'new',type,args,id,...(signature?{signature}:{})});
const S=(target,member,value)=>({kind:'set',target,member,value});
const G=(target,member,id)=>({kind:'get',target,member,...(id?{id}:{})});
const C=(target,member,args=[],id)=>({kind:'call',target,member,args,...(id?{id}:{})});
const snap=target=>({kind:'snapshot',target});
const parse=(value,id='parsed')=>({kind:'call',type:'Entities.Tolerance',member:'ParseStringRepresentation',args:[value],id});
const tryParse=value=>({kind:'call',type:'Entities.Tolerance',member:'TryParseStringRepresentation',args:[value,{out:true}],signature:['String','Entities.Tolerance&']});
const symbol=n=>E('Entities.ToleranceGeometricSymbol',n),material=n=>E('Entities.ToleranceMaterialCondition',n);
const val=(text='0.25',m=1,diameter=true)=>({new:'Entities.ToleranceValue',args:[diameter,text,material(m)]});
const datum=(text='A',m=1)=>({new:'Entities.DatumReferenceValue',args:[text,material(m)]});
const texts=[null,'','A','東京 😀','a\0b','a\nb','a\rb','a^Jb','a%%vb','{\\Fgdt;n}',{utf16:[0xd800]},{utf16:[0xdc00]}];
export function toleranceCorpus(){
 const out=[],add=(name,category,steps,culture='')=>out.push({name:'tolerance/'+name,category,request:{steps,...(culture?{culture}:{})}});
 for(const [i,args]of [[],[null],[{new:'Entities.ToleranceEntry',args:[]}],[null,V('Vector2',-0,2)],[null,V('Vector3',-0,2,3)]].entries())add('ctor/'+i,'constructors',[N('Entities.Tolerance',args),C('t','Clone',[],'q'),C('t','ToStringRepresentation'),snap('q')]);
 for(const g of [-1,...Array.from({length:15},(_,i)=>i),99])for(const m of [-1,0,1,2,3,99])for(const diameter of [false,true])add(`frame/${g}/${m}/${diameter}`,'framing',[
  N('Entities.ToleranceEntry',[],'e'),S('e','GeometricSymbol',symbol(g)),S('e','Tolerance1',val('0.25',m,diameter)),S('e','Tolerance2',val('0.5',m,!diameter)),S('e','Datum1',datum('A',m)),S('e','Datum2',datum('B',m)),S('e','Datum3',datum('C',m)),N('Entities.Tolerance',[R('e')]),C('t','ToStringRepresentation',[],'text'),parse(R('text')),C('parsed','ToStringRepresentation'),C('t','Clone',[],'q'),snap('q')]);
 for(const name of ['Tolerance1','Tolerance2','Datum1','Datum2','Datum3'])for(const [i,text]of texts.entries())add(`cell/${name}/${i}`,'framing',[
  N('Entities.ToleranceEntry',[],'e'),S('e',name,name.startsWith('Tolerance')?val(text):datum(text)),N('Entities.Tolerance',[R('e')]),C('t','ToStringRepresentation',[],'text'),parse(R('text')),C('parsed','ToStringRepresentation'),C('t','Clone')]);
 for(const [i,projected]of texts.slice(0,8).entries())for(const [j,id]of texts.slice(0,8).entries())for(const show of [false,true])add(`projected/${i}/${j}/${show}`,'projected',[
  N('Entities.Tolerance'),S('t','ProjectedToleranceZoneValue',projected),S('t','DatumIdentifier',id),S('t','ShowProjectedToleranceZoneSymbol',show),C('t','ToStringRepresentation',[],'text'),parse(R('text')),C('parsed','ToStringRepresentation')]);
 const inputs=[null,'','A','%%v','%%v1%%v2%%vA%%vB%%vC%%vEXTRA','{\\Fgdt;j}%%v{\\Fgdt;n}1{\\Fgdt;m}%%v2%%vA^J20{\\Fgdt;p}^JID','first^J%%v2^J%%vignored','^J^J%%vignored','%%V','{x;j}%%v1','%%v1{a;m}{b;l}', '%%v{a;n}{b;s}x','x^J20{\\Fgdt;p}','%%v1^J%%v2^Jprojected^JID','{','{x','{x;','{x;j','{x;jx','%%v{x;jx}'];
 inputs.forEach((text,i)=>add('parse/'+i,'parser',[parse(text),tryParse(text),C('parsed','ToStringRepresentation'),C('parsed','Clone')]));
 for(const culture of ['','en-US','pl-PL','tr-TR','ja-JP'])for(const prefix of ['','\u0000','\u00ad','\u034f','\u200b','\u2060','\ufe0f','\uff05\uff05\uff56','\u0301'])for(const body of ['%%v1','{\\Fgdt;j}%%v1'])add('culture/'+culture+'/'+out.length,'culture',[parse(prefix+body),C('parsed','ToStringRepresentation')],culture);
 for(const name of ['TextHeight','Rotation'])for(const value of [-Infinity,-1,-0,0,Number.MIN_VALUE,1,360,721,Infinity,NaN])add(`scalar/${name}/${D(value).double}`,'scalars',[N('Entities.Tolerance'),S('t',name,D(value)),G('t',name),C('t','Clone',[],'q'),snap('q')]);
 for(const name of ['Position','Normal'])for(const [i,v]of [[0,0,0],[-0,2,3],[0,3,4],[1,2,3],[Infinity,1,0],[NaN,0,1]].entries())add(`vector/${name}/${i}`,'geometry',[N('Entities.Tolerance'),S('t',name,V('Vector3',...v)),G('t',name,'v'),S('v','X',D(99)),snap('t'),C('t','Clone')]);
 const matrices=[[1,0,0,0,1,0,0,0,1],[2,0,0,0,3,0,0,0,4],[-1,0,0,0,1,0,0,0,1],[0,-1,0,1,0,0,0,0,1],[1,2,0,0,1,0,0,0,1],[2,1,-1,1,3,2,-1,0,2],[0,0,0,0,0,0,0,0,0],[NaN,0,0,0,1,0,0,0,1],[Infinity,0,0,0,1,0,0,0,1]];
 for(const [i,m]of matrices.entries())for(const r of [-90,0,27,180,300])for(const normal of [[0,0,1],[0,3,4]])add(`transform/${i}/${r}/${normal}`,'transforms',[N('Entities.Tolerance',[null,V('Vector3',1,2,3)]),S('t','Rotation',D(r)),S('t','Normal',V('Vector3',...normal)),C('t','TransformBy',[{new:'Matrix3',args:m.map(D)},V('Vector3',7,-8,9)]),snap('t'),C('t','Clone')]);
 for(const fail of [false,true])for(const replacement of [null,{new:'Tables.DimensionStyle',args:['Replacement']}] )add(`event/${fail}/${replacement===null}`,'events',[
  N('Entities.Tolerance'),{kind:'observe',target:'t',member:'ToleranceStyleChanged',observer:'style',replace:replacement,throw:fail},S('t','Style',{new:'Tables.DimensionStyle',args:['Proposed']}),snap('t'),{kind:'events'},C('t','Clone')]);
 add('clone-shared-entries','cloning',[N('Entities.ToleranceEntry',[],'e'),S('e','Tolerance1',val()),N('Entities.Tolerance',[R('e')]),S('t','Entry2',R('e')),S('t','ColorName','Book'),S('t','ProxyGraphics',A('Byte',[{byte:1},{byte:255}])),C('t','Clone',[],'q'),G('q','Entry1','e1'),G('q','Entry2','e2'),{kind:'reference-equals',args:[R('e1'),R('e2')]},G('e1','Tolerance1','v'),S('v','Value','edit'),snap('q'),snap('t')]);
 return out;
}
