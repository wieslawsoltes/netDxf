// Reproducible inputs only; the two production implementations supply their own results.
import {dimensionSchema} from './dimension-schema.mjs';
import {D,I,R,V,E,A} from './geometry-corpus.mjs';
import {DimensionStyleOverrideType as O} from '../netDxf/Tables/DimensionStyleOverrideType.js';
const N=(type,args=[],id='p')=>({kind:'new',type,args,id});
const S=(target,member,value)=>({kind:'set',target,member,value});
const G=(target,member,id)=>({kind:'get',target,member,id});
const C=(target,member,args=[],id,signature)=>({kind:'call',target,member,args,...(id?{id}:{}),...(signature?{signature}:{})});
const snap=target=>({kind:'snapshot',target});
const idx=(target,i,id)=>({kind:'index',target,args:[I(i)],id});
const scalar=[-Infinity,-1,-1e-12,-Number.MIN_VALUE,-0,0,Number.MIN_VALUE,1e-12,1e-6,0.25,2,Infinity,NaN];
const enumType=t=>t==='Lineweight'?t:(t.startsWith('Dimension')?'Tables.':'Units.')+t;
const resources={AciColor:{static:'AciColor',property:'Red'},Block:{new:'Blocks.Block',args:['Arrow']},Linetype:{static:'Tables.Linetype',property:'Dashed'},TextStyle:{new:'Tables.TextStyle',args:['Text','simplex.shx']},DimensionStyleAlternateUnits:{new:'Tables.DimensionStyleAlternateUnits',args:[]},DimensionStyleTolerances:{new:'Tables.DimensionStyleTolerances',args:[]}};
export function dimensionCorpus(){
 const out=[],add=(name,category,steps)=>out.push({name:'dimensions/'+name,category,request:{steps}});
 for(const [type,properties]of Object.entries(dimensionSchema)){
   const init=N('Tables.'+type,type==='DimensionStyle'?['S']:[]);
   add(type+'/default','styles',[init,C('p','Clone',[],'q'),snap('p'),snap('q')]);
   for(const [key,t]of properties){
     const values=t==='double'?scalar.map(D):t==='short'?[-32768,-2,-1,0,1,8,200,32767].map(short=>({short})):t==='bool'?[false,true]:t==='string'?[null,'','a\0b','line\nline','東京 😀',{utf16:[0xd800]}]:t==='char'?[0,44,46,0xd800,0xffff].map(char=>({char})):resources[t]?[null,resources[t]]:[-1,0,1,2,3,4,99].map(v=>E(enumType(t),v));
     values.forEach((value,i)=>add(`${type}/${key}/${i}`,'styles',[init,S('p',key,value),G('p',key,'value'),C('p','Clone',[],'q'),snap('p'),snap('q')]));
   }
 }
 for(const preset of ['Default','Iso25'])add('preset/'+preset,'styles',[{kind:'value',value:{static:'Tables.DimensionStyle',property:preset},id:'p'},C('p','Clone',[],'q'),snap('q')]);
 for(const name of [null,'',' ','S','Standard',' Standard ','A/B','日本'])add('name/'+JSON.stringify(name),'styles',[N('Tables.DimensionStyle',[name]),C('p','Clone',['Copy']),C('p','HasReferences'),C('p','GetReferences')]);
 for(const [key,t]of [['DimArrow1','Block'],['DimArrow2','Block'],['LeaderArrow','Block'],['DimLineLinetype','Linetype'],['ExtLine1Linetype','Linetype'],['ExtLine2Linetype','Linetype'],['TextStyle','TextStyle']])for(const fail of [false,true])for(const replacement of [null,resources[t]])add(`event/${key}/${fail}/${replacement===null}`,'events',[
   N('Tables.DimensionStyle',['S']),{kind:'observe',target:'p',member:t==='Block'?'BlockChanged':t+'Changed',observer:'change',throw:fail,replace:replacement},S('p',key,resources[t]),G('p',key,'resource'),S('p',key,null),C('p','Clone'),snap('p'),{kind:'events'}]);
 const candidates=[null,D(0),D(-0),D(-1),D(2),D(Infinity),D(NaN),{box:{short:2}},{box:I(2)},false,true,'', 'wrong',{char:44},V('Vector3',1,2,3),...Object.values(resources).slice(0,4)];
 const enumKinds=['Lineweight','LinearUnitType','AngleUnitType','FractionFormatType','DimensionStyleTextVerticalPlacement','DimensionStyleTextHorizontalPlacement','DimensionStyleTextDirection','DimensionStyleFitOptions','DimensionStyleFitTextMove','DimensionStyleTolerancesDisplayMethod','DimensionStyleTolerancesVerticalPlacement'];
 candidates.push(...enumKinds.map(t=>({box:E(enumType(t),2)})));
 for(const [name,kind] of [...Object.entries(O),['Unknown',2147483647]])candidates.forEach((value,i)=>add(`override/${name}/${i}`,'overrides',[
   N('Tables.DimensionStyleOverride',[E('Tables.DimensionStyleOverrideType',kind),value]),G('p','Type'),G('p','Value'),C('p','ToString')]));
 const override=(kind,value)=>({new:'Tables.DimensionStyleOverride',args:[E('Tables.DimensionStyleOverrideType',kind),value]});
 const dictionary=()=>N('Collections.DimensionStyleOverrideDictionary');
 for(const event of ['BeforeAddItem','AddItem','BeforeRemoveItem','RemoveItem'])for(const cancel of [false,true])for(const fail of [false,true])for(const action of ['add','replace','remove','clear']){
   add(`dictionary/${event}/${cancel}/${fail}/${action}`,'dictionary',[dictionary(),N('Tables.DimensionStyleOverride',[E('Tables.DimensionStyleOverrideType',O.ArrowSize),D(2)],'a'),C('p','Add',[R('a')]),
    {kind:'observe',target:'p',member:event,observer:'event',cancel,throw:fail},
    ...(action==='replace'?[{kind:'set-index',target:'p',args:[E('Tables.DimensionStyleOverrideType',O.ArrowSize)],value:override(O.ArrowSize,D(3))}]:action==='add'?[C('p','Add',[override(O.TextOffset,D(3))])]:action==='remove'?[C('p','Remove',[E('Tables.DimensionStyleOverrideType',O.ArrowSize)])]:[C('p','Clear')]),snap('p'),{kind:'events'}]);
 }
 for(const action of ['add','replace','remove','clear'])add('dictionary/iterator/'+action,'dictionary',[
   dictionary(),C('p','Add',[override(O.ArrowSize,D(2))]),C('p','Add',[override(O.TextOffset,D(3))]),G('p','Types','keys'),G('p','Values','values'),C('p','GetEnumerator',[],'it'),C('it','MoveNext'),
   ...(action==='replace'?[{kind:'set-index',target:'p',args:[E('Tables.DimensionStyleOverrideType',O.ArrowSize)],value:override(O.ArrowSize,D(8))}]:action==='add'?[C('p','Add',[override(O.TextHeight,D(3))])]:action==='remove'?[C('p','Remove',[E('Tables.DimensionStyleOverrideType',O.ArrowSize)])]:[C('p','Clear')]),C('it','MoveNext'),G('it','Current'),snap('keys'),snap('values')]);
 add('dictionary/reuse','dictionary',[dictionary(),...['ArrowSize','TextOffset','TextHeight'].map((k,i)=>C('p','Add',[override(O[k],D(i+1))])),C('p','Remove',[E('Tables.DimensionStyleOverrideType',O.TextOffset)]),C('p','Add',[override(O.DimLineExtend,D(4))]),snap('p'),C('p','TryGetValue',[E('Tables.DimensionStyleOverrideType',O.ArrowSize),{out:true}],null,['Tables.DimensionStyleOverrideType','Tables.DimensionStyleOverride&'])]);
 const arrowNames=['Dot','DotSmall','DotBlank','OriginIndicator','OriginIndicator2','Open','Open90','Open30','Closed','DotSmallBlank','None','Oblique','BoxFilled','Box','ClosedBlank','DatumTriangleFilled','DatumTriangle','Integral','ArchitecturalTick'];
 for(const name of arrowNames)for(const scale of [-2,1,3])add(`arrow/${name}/${scale}`,'arrowheads',[
   {kind:'value',value:{static:'Entities.DimensionArrowhead',property:name},id:'p'},C('p','Clone',['Copy'],'q',['String']),N('Entities.Insert',[R('p')],'insert'),S('insert','Scale',V('Vector3',scale,scale,scale)),S('insert','Rotation',D(27)),S('insert','Position',V('Vector3',3,4,5)),C('insert','Explode'),snap('p'),snap('q')]);
 return out;
}
