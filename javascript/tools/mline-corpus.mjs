// Deterministic inputs only; no expected geometry or exception outputs are embedded.
import {D,I,R,V,A,E} from './geometry-corpus.mjs';
const N=(type,args=[],id='p',signature,nonPublic=false)=>({kind:'new',type,args,id,...(signature?{signature}:{}),...(nonPublic?{nonPublic:true}:{})});
const S=(target,member,value)=>({kind:'set',target,member,value});
const G=(target,member,id)=>({kind:'get',target,member,id});
const C=(target,member,args=[],id,signature)=>({kind:'call',target,member,args,...(id?{id}:{}),...(signature?{signature}:{})});
const snap=target=>({kind:'snapshot',target});
const ix=(target,index,id)=>({kind:'index',target,args:[I(index)],id});
const ELEM='Objects.MLineStyleElement',STYLE='Objects.MLineStyle';
const element=(offset,color,linetype)=>({new:ELEM,args:color===undefined?[D(offset)]:[D(offset),{new:'AciColor',args:[{short:color}]},{new:'Tables.Linetype',args:[linetype??'ByLayer']}]});
const style=(offsets=[.5,-.5],id='style')=>N(STYLE,['Style',A(ELEM,offsets.map(o=>element(o)))],id,['String',`IEnumerable<${ELEM}>`]);
const points=list=>A('Vector2',list.map(p=>V('Vector2',...p)));
const model=(coords=[[0,0],[4,0],[5,3]],closed=false,scale=1,id='p')=>N('Entities.MLine',[points(coords),R('style'),D(scale),closed],id);
const matrix=values=>({new:'Matrix3',args:values.map(D)});
const shapes=[[],[[0,0]],[[0,0],[3,4]],[[0,0],[0,0]],[[0,0],[4,0],[4,3]],[[0,0],[4,0],[8,0]],[[0,0],[4,0],[0,0]],[[0,0],[3,4],[5,-2],[-1,3]]];
const transforms=[[1,0,0,0,1,0,0,0,1],[2,0,0,0,2,0,0,0,2],[-1,0,0,0,1,0,0,0,1],[0,-1,0,1,0,0,0,0,1],[2,0,0,0,3,0,0,0,1],[1,.5,0,0,1,0,0,0,1],[2,1,-1,1,3,2,-1,0,2],[0,0,0,0,0,0,0,0,0],[NaN,0,0,0,1,0,0,0,1]];
const label=v=>D(v).double;
export function mlineCorpus(){
  const out=[],add=(name,category,steps)=>out.push({name:'mline/'+name,category,request:{steps}});
  for(const offset of [-Infinity,-1,-0,0,Number.MIN_VALUE,.5,Infinity,NaN]){
    add('element/'+label(offset),'mline-style',[N(ELEM,[D(offset)]),C('p','ToString'),C('p','Clone',[],'q'),C('p','Equals',[R('p')],null,['Object']),C('p','CompareTo',[R('q')]),C('p','GetHashCode'),S('p','Offset',D(7)),snap('p'),snap('q')]);
    add('equality/'+label(offset),'mline-style',[N(ELEM,[D(offset)]),
      N(STYLE,['S',A(ELEM,[R('p')])],'s',['String',`IEnumerable<${ELEM}>`]),G('s','Elements','elements'),
      C('elements','Contains',[R('p')]),C('elements','IndexOf',[R('p')]),C('elements','Remove',[R('p')]),snap('s')]);
    for(const other of [-Infinity,-1,0,.5,Infinity,NaN])add(`compare/${label(offset)}/${label(other)}`,'mline-style',[N(ELEM,[D(offset)]),N(ELEM,[D(other)],'q'),C('p','CompareTo',[R('q')]),C('p','Equals',[R('q')],null,['Object'])]);
  }
  for(const color of [null,{static:'AciColor',property:'Red'}])for(const line of [null,{static:'Tables.Linetype',property:'Dashed'}])add('element-resources/'+out.length,'mline-style',[N(ELEM,[D(1),color,line]),C('p','ToString'),C('p','Clone'),S('p','Color',null),S('p','Linetype',null),snap('p')]);
  for(const name of [null,'',' ','S',' Standard ','A/B','*A','żółć'])for(const elements of [null,A(ELEM,[]),A(ELEM,[null])])add('style-constructor/'+out.length,'mline-style',[N(STYLE,[name,elements],'p',['String',`IEnumerable<${ELEM}>`]),C('p','Clone'),C('p','HasReferences'),C('p','GetReferences')]);
  for(const n of [1,2,3,16,17,32])add('sorting/'+n,'mline-style',[style(Array.from({length:n},(_,i)=>i%6===0?NaN:(i*17%11)-5),'p'),G('p','Elements','elements'),C('elements','Sort'),C('p','Clone'),snap('p')]);
  for(const member of ['StartAngle','EndAngle'])for(const value of [-Infinity,0,9.999999999,10,90,170,170.000001,Infinity,NaN])add(`${member}/${label(value)}`,'mline-style',[style(undefined,'p'),S('p',member,D(value)),snap('p'),C('p','Clone')]);
  for(const text of [null,'','line\ntext','a\0b','Ω 😀'])add('description/'+out.length,'mline-style',[N(STYLE,['S',text],'p',['String','String']),S('p','Description',text),C('p','Clone'),snap('p')]);
  for(const event of ['MLineStyleElementAdded','MLineStyleElementRemoved','MLineStyleElementLinetypeChanged'])for(const fail of [false,true]){
    add(`event/${event}/${fail}`,'mline-style-events',[style(),G('style','Elements','elements'),N(ELEM,[D(2)],'e'),
      {kind:'observe',target:'style',member:event,observer:'style',throw:fail},C('elements','Add',[R('e')]),
      S('e','Linetype',{static:'Tables.Linetype',property:'Dashed'}),C('elements','Remove',[R('e')]),
      S('e','Linetype',{static:'Tables.Linetype',property:'Dot'}),snap('style'),snap('e'),{kind:'events'}]);
  }
  for(const replacement of [null,{static:'Tables.Linetype',property:'Dot'}])add('linetype-substitute/'+out.length,'mline-style-events',[style(),G('style','Elements','elements'),ix('elements',0,'e'),
    {kind:'observe',target:'style',member:'MLineStyleElementLinetypeChanged',observer:'change',replace:replacement},S('e','Linetype',{static:'Tables.Linetype',property:'Dashed'}),snap('style'),{kind:'events'},C('style','Clone')]);
  for(const event of ['BeforeAddItem','AddItem','BeforeRemoveItem','RemoveItem'])for(const fail of [false,true])for(const cancel of [false,true])add(`collection-event/${event}/${fail}/${cancel}`,'mline-style-events',[
    style(),G('style','Elements','elements'),ix('elements',0,'e'),
    {kind:'observe',target:'elements',member:event,observer:'collection',throw:fail,cancel},
    C('elements',event.includes('Add')?'Add':'Remove',[R('e')]),S('e','Linetype',{static:'Tables.Linetype',property:'Dashed'}),snap('style'),{kind:'events'}]);
  add('same-offset-remove','mline-style-events',[style(),G('style','Elements','elements'),ix('elements',0,'e'),N(ELEM,[D(.5)],'equal'),C('elements','Remove',[R('equal')]),
    {kind:'observe',target:'style',member:'MLineStyleElementLinetypeChanged',observer:'changed'},S('e','Linetype',{static:'Tables.Linetype',property:'Dashed'}),{kind:'events'},snap('style')]);
  add('default','mline-model',[N('Entities.MLine'),C('p','Update'),C('p','Explode'),C('p','Clone'),C('p','TransformBy',[matrix(transforms[0]),V('Vector3',0,0,0)]),snap('p')]);
  const p=points(shapes[2]);for(const args of [[p],[p,true],[p,D(2)],[p,D(-2),true],[null],[p,R('style'),D(.5)],[p,null,D(1),false]])add('model-overload/'+out.length,'mline-model',[style(),N('Entities.MLine',args),C('p','Clone'),snap('p')]);
  for(const [i,shape]of shapes.entries())for(const closed of [false,true])for(const scale of [-2,-0,0,1,3])for(const justification of [0,1,2,99])add(`update/${i}/${closed}/${label(scale)}/${justification}`,'mline-geometry',[
    style(),model(shape,closed,scale),S('p','Justification',E('Entities.MLineJustification',justification)),C('p','Update'),snap('p'),C('p','Explode'),C('p','Clone')]);
  for(const value of [-Infinity,NaN,Infinity,Number.MIN_VALUE])for(const member of ['Scale','Elevation'])add(`scalar/${member}/${label(value)}`,'mline-geometry',[style(),model(),S('p',member,D(value)),C('p','Update'),C('p','Explode'),C('p','Clone'),snap('p')]);
  for(const flag of [0,1,2,16,32,64,256,512,1024,1907])for(const count of [1,2,4,5])for(const scale of [-1,1])for(const closed of [false,true])add(`caps/${flag}/${count}/${scale}/${closed}`,'mline-caps',[
    style(Array.from({length:count},(_,i)=>count/2-i)),S('style','Flags',E('Objects.MLineStyleFlags',flag)),model(undefined,closed,scale),C('p','Explode'),C('p','Clone')]);
  for(const caps of [16|256,64|1024,32|512|2])for(const noStart of [false,true])for(const noEnd of [false,true])add(`visibility/${caps}/${noStart}/${noEnd}`,'mline-caps',[
    style([2,1,-1,-2]),S('style','Flags',E('Objects.MLineStyleFlags',caps)),G('style','Elements','elements'),ix('elements',0,'first'),S('first','Color',{static:'AciColor',property:'Red'}),S('first','Linetype',{static:'Tables.Linetype',property:'Dashed'}),
    model(),S('p','NoStartCaps',noStart),S('p','NoEndCaps',noEnd),S('p','IsVisible',false),S('p','Elevation',D(10)),C('p','Explode')]);
  for(const [i,m]of transforms.entries())for(const normal of [[0,0,1],[0,3,4],[-2,3,4]])for(const scale of [-2,1])for(const closed of [false,true])add(`transform/${i}/${normal}/${scale}/${closed}`,'mline-transform',[
    style(),model(undefined,closed,scale),S('p','Normal',V('Vector3',...normal)),S('p','Elevation',D(3)),C('p','TransformBy',[matrix(m),V('Vector3',7,-8,9)]),snap('p'),C('p','Clone'),C('p','Explode')]);
  add('cache-identities','mline-model',[style(),model(),G('p','Vertexes','vertices'),ix('vertices',0,'old'),S('old','Position',V('Vector2',9,8)),G('old','Distances','oldDistances'),C('p','Update'),ix('vertices',0,'fresh'),{kind:'reference-equals',args:[R('old'),R('fresh')]},snap('old'),snap('p')]);
  for(const list of [[],[.5],[.5,0],[.5,1,2,3,4],[.5,0,2,4]])add('breaks/'+list,'mline-model',[
    style(),model(shapes[2]),G('p','Vertexes','vertices'),ix('vertices',0,'v'),G('v','Distances','distances'),
    {kind:'set-index',target:'distances',args:[I(0)],value:{new:'List<Double>',args:[A('Double',list.map(D))]}},C('p','Explode'),C('p','Clone'),snap('p')]);
  for(const value of [null,{new:STYLE,args:['Replacement']}])for(const fail of [false,true])add('style-event/'+out.length,'mline-model',[
    style(),model(),{kind:'observe',target:'p',member:'MLineStyleChanged',observer:'style',replace:value,throw:fail},S('p','Style',{new:STYLE,args:['Proposed']}),snap('p'),C('p','Update'),C('p','Clone'),{kind:'events'}]);
  for(const distances of [null,A('List<Double>',[]),A('List<Double>',[null]),A('List<Double>',[{new:'List<Double>',args:[A('Double',[D(-0),D(2)])]}])])add('vertex/'+out.length,'mline-model',[
    N('Entities.MLineVertex',[V('Vector2',1,2),V('Vector2',3,4),V('Vector2',5,6),distances],'v',null,true),C('v','Clone'),C('v','ToString'),G('v','Position','position'),S('position','X',D(99)),snap('v')]);
  add('matrix4','mline-transform',[style(),model(),C('p','TransformBy',[{new:'Matrix4',args:[2,0,0,3,0,-1,0,4,0,0,.5,5,9,8,7,6].map(D)}]),snap('p')]);
  for(const scale of [NaN,Infinity,-Infinity,Number.MIN_VALUE])for(const flag of [64|1024,16|256])add(`nonfinite-caps/${label(scale)}/${flag}`,'mline-caps',[
    style(),S('style','Flags',E('Objects.MLineStyleFlags',flag)),model(shapes[2],false,scale),C('p','Explode'),C('p','Clone'),snap('p')]);
  for(const action of ['clear-elements','clear-vertices','null-vertex','add-element'])add('edited-cache/'+action,'mline-model',[
    style(),model(),G('p','Vertexes','vertices'),G('style','Elements','elements'),
    ...(action==='clear-elements'?[C('elements','Clear')]:action==='clear-vertices'?[C('vertices','Clear')]:action==='null-vertex'?[{kind:'set-index',target:'vertices',args:[I(0)],value:null}]:[C('elements','Add',[element(3)])]),
    C('p','Explode'),C('p','Clone'),C('p','TransformBy',[matrix(transforms[0]),V('Vector3',0,0,0)]),C('p','Update'),snap('p')]);
  return out;
}
