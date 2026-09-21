// Deterministic inputs only. Independent C# and JS models provide every observed result.
import {D,I,R,V,E,A} from './geometry-corpus.mjs';
import {DimensionStyleOverrideType as O} from '../netDxf/Tables/DimensionStyleOverrideType.js';
const N=(type,args=[],id='l',signature,nonPublic=false)=>({kind:'new',type,args,id,...(signature?{signature}:{}),...(nonPublic?{nonPublic:true}:{})});
const S=(target,member,value)=>({kind:'set',target,member,value});
const G=(target,member,id)=>({kind:'get',target,member,id});
const C=(target,member,args=[],id,signature,nonPublic=false)=>({kind:'call',target,member,args,...(id?{id}:{}),...(signature?{signature}:{}),...(nonPublic?{nonPublic:true}:{})});
const snap=target=>({kind:'snapshot',target});
const pair=(x=4,y=3)=>A('Vector2',[V('Vector2',0,0),V('Vector2',x,y)]);
const fresh=()=>N('Entities.Leader',[pair()]);
const sty=()=>({new:'Tables.DimensionStyle',args:['Style']});
const color=()=>({static:'AciColor',property:'Red'});
const override=(name,v)=>({new:'Tables.DimensionStyleOverride',args:[E('Tables.DimensionStyleOverrideType',O[name]),v]});
const annotation=(kind,id='a')=>kind==='Insert'?N('Entities.Insert',[{new:'Blocks.Block',args:['B']}],id):N('Entities.'+kind,[],id);
const eq=(a,b)=>({kind:'reference-equals',args:[R(a),R(b)]});
const floats=[-Infinity,-1,-0,0,Number.MIN_VALUE,1,2,Infinity,NaN];
export function leaderCorpus(){
 const out=[],add=(name,category,steps)=>out.push({name:'leader/'+name,category,request:{steps}});
 const points=[null,A('Vector2',[]),A('Vector2',[V('Vector2',0,0)]),pair(),pair(-3,0),pair(0,3),pair(NaN,1)];
 for(const [i,p]of points.entries())for(const style of [null,sty()])add(`ctor/plain/${i}/${style===null}`,'constructors',[N('Entities.Leader',[p,style],'l',['IEnumerable<Vector2>','Tables.DimensionStyle']),C('l','Clone'),C('l','Update',[true]),snap('l')]);
 for(const [type,content]of [['String','Text'],['Entities.ToleranceEntry',{new:'Entities.ToleranceEntry',args:[]}],['Blocks.Block',{new:'Blocks.Block',args:['B']}]])for(const value of [content,null])for(const [i,p]of points.entries())add(`ctor/${type}/${value===null}/${i}`,'constructors',[
  N('Entities.Leader',[value,p,sty()],'l',[type,'IEnumerable<Vector2>','Tables.DimensionStyle']),C('l','Update',[true]),C('l','Clone',[],'q'),snap('l'),snap('q')]);
 for(const hook of [false,true])add('ctor/internal/'+hook,'constructors',[N('Entities.Leader',[pair(),sty(),hook],'l',['IEnumerable<Vector2>','Tables.DimensionStyle','Boolean'],true),C('l','Clone'),S('l','HasHookline',false),S('l','HasHookline',true),snap('l')]);
 for(const kind of ['MText','Text','Insert','Tolerance'])for(const rotation of [0,27,90,90.0000001,180,270,300])for(const aligned of (kind==='MText'?[1,2,3,4,5,6,7,8,9]:kind==='Text'?[0,1,2,3,4,5,6,7,8,9,10,11,12,13,14]:[0]))for(const reset of [false,true])add(`update/${kind}/${rotation}/${aligned}/${reset}`,'placement',[
  fresh(),annotation(kind),S('a','Rotation',D(rotation)),S('a','Position',V('Vector3',10,-7,11)),...(kind==='MText'?[S('a','AttachmentPoint',E('Entities.MTextAttachmentPoint',aligned))]:kind==='Text'?[S('a','Alignment',E('Entities.TextAlignment',aligned))]:[]),S('l','Annotation',R('a')),S('l','Offset',V('Vector2',2,3)),S('l','Elevation',D(4)),S('l','HasHookline',true),C('l','Update',[reset]),snap('l'),C('l','Clone',[],'q'),snap('q')]);
 for(const member of ['Offset','Direction'])for(const [i,values]of [[-0,1],[0,0],[2,3],[NaN,1],[Infinity,1],[Number.MIN_VALUE,0]].entries())add(`vector/${member}/${i}`,'values',[
  fresh(),S('l',member,V('Vector2',...values)),G('l',member,'v'),S('v','X',D(99)),snap('l'),C('l','Clone')]);
 for(const n of floats)add('elevation/'+D(n).double,'values',[fresh(),S('l','Elevation',D(n)),C('l','Clone'),C('l','TransformBy',[{static:'Matrix3',property:'Identity'},V('Vector3',0,0,0)]),snap('l')]);
 for(const kind of ['MText','Text','Insert','Tolerance','Line','Circle'])for(const fail of [false,true])for(const event of ['AnnotationAdded','AnnotationRemoved'])add(`events/${kind}/${fail}/${event}`,'events',[
  fresh(),annotation('MText','old'),S('l','Annotation',R('old')),annotation(kind),{kind:'observe',target:'l',member:event,observer:'event',throw:fail},S('l','Annotation',R('a')),snap('l'),snap('old'),snap('a'),{kind:'events'},S('l','Annotation',null),snap('l')]);
 for(const replace of [null,sty()])for(const fail of [false,true])add(`style/${replace===null}/${fail}`,'events',[
  fresh(),{kind:'observe',target:'l',member:'LeaderStyleChanged',observer:'style',throw:fail,replace},S('l','Style',sty()),C('l','Clone'),C('l','CalculateHookLine',[],null,null,true),snap('l'),{kind:'events'}]);
 const overrides=[['TextHeight',D(3)],['TextOffset',D(-2)],['DimScaleOverall',D(2)],['ArrowSize',D(5)],['TextVerticalPlacement',{box:E('Tables.DimensionStyleTextVerticalPlacement',1)}],['TextColor',color()]];
 for(const [name,val]of overrides)for(const kind of ['MText','Text','Insert','Tolerance'])for(const reset of [false,true])add(`override/${name}/${kind}/${reset}`,'overrides',[
  fresh(),annotation(kind),S('l','Annotation',R('a')),S('l','HasHookline',true),G('l','StyleOverrides','overrides'),C('overrides','Add',[override(name,val)]),C('l','Update',[reset]),snap('l'),C('l','Clone')]);
 for(const [name,value]of [['ArrowSize',D(2)],['DimLine1Off',true],['TextColor',color()],['DimPrefix',''],['DimPrefix','equal string'],['DimPrefix',{stringRef:'shared reference'}],['ArrowSize',{box:D(2)}]])for(const action of ['same-item','same-value','new-value'])add(`override-identity/${name}/${out.length}/${action}`,'override-identity',[
  fresh(),G('l','StyleOverrides','overrides'),{kind:'value',value,id:'value'},N('Tables.DimensionStyleOverride',[E('Tables.DimensionStyleOverrideType',O[name]),R('value')],'first'),C('overrides','Add',[R('first')]),
  ...(action==='same-item'?[]:[N('Tables.DimensionStyleOverride',[E('Tables.DimensionStyleOverrideType',O[name]),action==='same-value'?R('value'):value],'next')]),
  {kind:'observe',target:'l',member:'DimensionStyleOverrideAdded',observer:'added'},
  {kind:'set-index',target:'overrides',args:[E('Tables.DimensionStyleOverrideType',O[name])],value:R(action==='same-item'?'first':'next')},snap('l'),{kind:'events'},C('l','Clone')]);
 for(const action of ['clear','remove-one','set','add'])add('list/'+action,'lists',[
  fresh(),G('l','Vertexes','list'),C('list','GetEnumerator',[],'it'),G('it','Current'),C('it','MoveNext'),
  ...(action==='clear'?[C('list','Clear')]:action==='remove-one'?[C('list','RemoveAt',[I(0)])]:action==='set'?[{kind:'set-index',target:'list',args:[I(0)],value:V('Vector2',-1,-2)}]:[C('list','Add',[V('Vector2',8,9)])]),C('it','MoveNext'),G('l','Hook','hook'),S('l','HasHookline',true),C('l','Update',[true]),C('l','Clone'),snap('l')]);
 const matrices=[[1,0,0,0,1,0,0,0,1],[2,0,0,0,3,0,0,0,4],[-1,0,0,0,1,0,0,0,1],[0,-1,0,1,0,0,0,0,1],[1,2,0,0,1,0,0,0,1],[2,1,-1,1,3,2,-1,0,2],[0,0,0,0,0,0,0,0,0],[NaN,0,0,0,1,0,0,0,1],[Infinity,0,0,0,1,0,0,0,1]];
 for(const kind of ['none','MText','Text','Insert','Tolerance'])for(const [i,m]of matrices.entries())for(const normal of [[0,0,1],[0,3,4]])add(`transform/${kind}/${i}/${normal}`,'transforms',[
  fresh(),...(kind==='none'?[]:[annotation(kind),S('l','Annotation',R('a'))]),S('l','Normal',V('Vector3',...normal)),S('l','Elevation',D(5)),S('l','Offset',V('Vector2',2,3)),S('l','Direction',V('Vector2',3,4)),C('l','TransformBy',[{new:'Matrix3',args:m.map(D)},V('Vector3',7,-8,9)]),snap('l'),C('l','Clone'),C('l','Update',[true]),snap('l')]);
 add('cloning/identity','cloning',[fresh(),S('l','LineColor',color()),S('l','Direction',V('Vector2',3,4)),S('l','ProxyGraphics',A('Byte',[{byte:1},{byte:2}])),S('l','ColorName','Book'),G('l','LineColor','color'),C('l','Clone',[],'q'),G('q','LineColor','other'),eq('color','other'),S('other','Index',{short:3}),snap('l'),snap('q')]);
 for(const kind of ['MText','Text','Insert','Tolerance'])add('block/'+kind,'ownership',[fresh(),annotation(kind),S('l','Annotation',R('a')),N('Blocks.Block',['B'],'b'),G('b','Entities','entities'),C('entities','Add',[R('l')]),C('b','Clone',['Copy'],'q'),snap('q'),C('entities','Remove',[R('a')],null,['Entities.EntityObject']),S('l','Annotation',null),C('entities','Remove',[R('a')],null,['Entities.EntityObject']),snap('b')]);
 return out;
}
