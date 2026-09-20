// Deterministic input descriptions; neither expected values nor production algorithms live here.
import {D,I,R,V,A,E} from './geometry-corpus.mjs';
const N=(type,args=[],id='v',signature,nonPublic=false)=>({kind:'new',type,args,id,...(signature?{signature}:{}),...(nonPublic?{nonPublic:true}:{})});
const C=(target,member,args=[],id,signature,nonPublic=false)=>({kind:'call',target,member,args,...(id?{id}:{}),...(signature?{signature}:{}),...(nonPublic?{nonPublic:true}:{})});
const S=(target,member,value,nonPublic=false)=>({kind:'set',target,member,value,...(nonPublic?{nonPublic:true}:{})});
const G=(target,member,id,nonPublic=false)=>({kind:'get',target,member,id,...(nonPublic?{nonPublic:true}:{})});
const snap=target=>({kind:'snapshot',target});
const equal=(a,b)=>({kind:'reference-equals',args:[R(a),R(b)]});
const mat=m=>({new:'Matrix3',args:m.map(D)});
const numbers=[-Infinity,-1,-0,0,Number.MIN_VALUE,1,1e308,Infinity,NaN];
const label=n=>D(n).double;
const viewport=()=>N('Entities.Viewport');
const layout=(name='Sheet')=>N('Objects.Layout',[name],'l');
const validLayout=()=>[layout(),S('l','TabOrder',{short:2})];
const initLayer=(name,id)=>N('Tables.Layer',[name],id);
const boundary=(type,id='c')=>{
  let args;
  switch(type){
    case 'Circle': args=[V('Vector3',3,4,17),D(2)];break;
    case 'Ellipse':args=[V('Vector3',3,4,17),D(8),D(3)];break;
    case 'Arc':args=[V('Vector3',3,4,17),D(2),D(10),D(100)];break;
    case 'Polyline2D':args=[A('Entities.Polyline2DVertex',[{new:'Entities.Polyline2DVertex',args:[D(0),D(0),D(.5)]},{new:'Entities.Polyline2DVertex',args:[D(4),D(0),D(-.25)]},{new:'Entities.Polyline2DVertex',args:[D(2),D(3),D(0)]}]),true];break;
    case 'Polyline3D':args=[A('Vector3',[V('Vector3',0,0,7),V('Vector3',4,0,-2),V('Vector3',2,3,8)]),false];break;
    case 'Spline':args=[A('Vector3',[V('Vector3',0,0,7),V('Vector3',4,5,-2),V('Vector3',8,0,8)]),null,{short:2}];break;
    default:args=[];
  }
  return N('Entities.'+type,args,id);
};
const transforms=[[1,0,0,0,1,0,0,0,1],[2,0,0,0,2,0,0,0,2],[-1,0,0,0,1,0,0,0,1],[0,-1,0,1,0,0,0,0,1],[2,0,0,0,3,0,0,0,1],[1,.5,0,0,1,0,0,0,1],[2,1,-1,1,3,2,-1,0,2],[0,0,0,0,0,0,0,0,0],[NaN,0,0,0,1,0,0,0,1]];
const Sun=(member,args=[])=>({kind:'call',type:'Objects.SunReferences',member,args,nonPublic:true});
export function layoutViewportCorpus(){
  const out=[],add=(name,category,steps)=>out.push({name:'layout-viewport/'+name,category,request:{steps}});
  for(const name of [null,'',' ','Model','model',' Model ','Sheet','A/B','日本😀'])add('layout/name/'+JSON.stringify(name),'layout',[
    layout(name),C('l','Clone',[],'q'),S('l','TabOrder',{short:2}),C('l','Clone',[],'r'),C('l','HasReferences'),C('l','GetReferences'),snap('l')]);
  add('layout/model-factory','layout',[{kind:'value',value:{static:'Objects.Layout',property:'ModelSpace'},id:'l'},C('l','Clone',['Other']),G('l','AssociatedBlock','b'),snap('b'),S('l','Name','Other'),snap('l')]);
  for(const order of [-32768,-1,0,1,2,32767])add('layout/tab/'+order,'layout',[layout(),S('l','TabOrder',{short:order}),C('l','Clone',[],'q'),N('Objects.Layout',['B'],'b'),S('b','TabOrder',{short:31}),C('l','CompareTo',[R('b')],null,['Objects.Layout']),C('l','CompareTo',[null],null,['Objects.Layout']),snap('l')]);
  for(const name of [null,'','Copy','model',' Model ','A/B'])for(const emptyPlot of [false,true])add('layout/clone/'+JSON.stringify(name)+'/'+emptyPlot,'layout',[
    ...validLayout(),...(emptyPlot?[S('l','PlotSettings',null)]:[]),C('l','Clone',[name],'copy',['String']),snap('l')]);
  for(const member of ['MinLimit','MaxLimit','BasePoint','MinExtents','MaxExtents','UcsOrigin','UcsXAxis','UcsYAxis'])for(const number of numbers){
    const two=['MinLimit','MaxLimit'].includes(member),value=two?V('Vector2',number,2):V('Vector3',number,2,3);
    add('layout/'+member+'/'+label(number),'layout',[...validLayout(),S('l',member,value),G('l',member,'copy'),S('copy','X',D(99)),snap('l'),C('l','Clone',[],'q')]);
  }
  for(const value of numbers)add('layout/elevation/'+label(value),'layout',[...validLayout(),S('l','Elevation',D(value)),C('l','Clone'),snap('l')]);
  add('layout/viewport-clear','layout',[...validLayout(),G('l','Viewport','old'),S('l','Viewport',null),C('l','Clone'),snap('old'),snap('l')]);
  add('layout/associated-block','layout',[...validLayout(),N('Blocks.Block',['Paper'],'b'),S('l','AssociatedBlock',R('b')),C('l','Clone',[],'q'),G('q','AssociatedBlock','copyBlock'),snap('l')]);
  add('layout/handle-no-owner','layout',[...validLayout(),C('l','AssignHandle',[{long:'10'}],null,undefined,true),snap('l')]);
  for(const id of [-32768,-2,-1,0,1,2,32767])add('viewport/internal/'+id,'viewport',[N('Entities.Viewport',[{short:id}],'v',['Int16'],true),C('v','Clone'),snap('v')]);
  add('viewport/default','viewport',[viewport(),C('v','Clone'),C('v','ToString')]);
  add('viewport/null-boundary','viewport',[N('Entities.Viewport',[null],'v',['Entities.EntityObject']),C('v','Clone')]);
  for(const width of numbers)add('viewport/constructor-width/'+label(width),'viewport',[N('Entities.Viewport',[V('Vector2',2,3),D(width),D(-0)]),C('v','Clone')]);
  for(const x of numbers)add('viewport/corners/'+label(x),'viewport',[N('Entities.Viewport',[V('Vector2',2,4),V('Vector2',x,24)]),C('v','Clone')]);
  for(const member of ['Width','Height','LensLength','FrontClipPlane','BackClipPlane','ViewHeight','SnapAngle','TwistAngle','Elevation'])for(const value of numbers)
    add('viewport/scalar/'+member+'/'+label(value),'viewport',[viewport(),S('v',member,D(value)),C('v','Clone'),snap('v')]);
  for(const member of ['Stacking','CircleZoomPercent','Id'])for(const value of [-32768,-2,-1,0,1,2,32767])add('viewport/short/'+member+'/'+value,'viewport',[
    viewport(),S('v',member,{short:value},member==='Id'),C('v','Clone'),snap('v')]);
  for(const member of ['Center','ViewCenter','SnapBase','SnapSpacing','GridSpacing','ViewDirection','ViewTarget','UcsOrigin','UcsXAxis','UcsYAxis'])for(const value of numbers){
    const two=['ViewCenter','SnapBase','SnapSpacing','GridSpacing'].includes(member);
    add('viewport/vector/'+member+'/'+label(value),'viewport',[viewport(),S('v',member,two?V('Vector2',value,2):V('Vector3',value,2,3)),G('v',member,'copy'),S('copy','X',D(99)),snap('v'),C('v','Clone')]);
  }
  for(const kind of ['Circle','Ellipse','Polyline2D','Polyline3D','Spline','Arc','Line'])for(const normal of [[0,0,1],[0,0,-1],[1,2,3]])add('boundary/'+kind+'/'+normal,'viewport-clipping',[
    boundary(kind),S('c','Normal',V('Vector3',...normal)),viewport(),S('v','ClippingBoundary',R('c')),snap('v'),snap('c'),C('v','Clone',[],'q'),S('v','ClippingBoundary',R('c')),snap('v'),S('v','ClippingBoundary',null),snap('c'),snap('q')]);
  for(const kind of ['Polyline2D','Polyline3D'])add('boundary/empty/'+kind,'viewport-clipping',[N('Entities.'+kind,[],'c'),viewport(),S('v','ClippingBoundary',R('c')),C('v','Clone'),snap('v')]);
  add('boundary/refresh','viewport-clipping',[boundary('Circle'),N('Entities.Viewport',[R('c')]),S('c','Radius',D(9)),S('v','Width',D(123)),S('v','ClippingBoundary',R('c')),snap('v'),snap('c')]);
  for(const event of ['ClippingBoundaryAdded','ClippingBoundaryRemoved'])for(const fail of [false,true])for(const clear of [false,true])add(`boundary/event/${event}/${fail}/${clear}`,'viewport-events',[
    boundary('Circle','a'),boundary('Ellipse','b'),N('Entities.Viewport',[R('a')]),{kind:'observe',target:'v',member:event,observer:'event',throw:fail},
    S('v','ClippingBoundary',clear?null:R('b')),snap('v'),snap('a'),snap('b'),{kind:'events'}]);
  for(const action of ['duplicate','null','clear','replace','remove','add'])for(const event of ['BeforeAddItem','AddItem','BeforeRemoveItem','RemoveItem'])for(const cancel of [false,true]){
    const step=action==='replace'?{kind:'set-index',target:'frozen',args:[I(0)],value:R('b')}:action==='clear'?C('frozen','Clear'):C('frozen',action==='remove'?'Remove':'Add',[action==='null'?null:R(action==='duplicate'||action==='remove'?'a':'b')],null,action==='remove'?['Tables.Layer']:undefined);
    add(`frozen/${action}/${event}/${cancel}`,'viewport-frozen',[viewport(),initLayer('A','a'),initLayer('B','b'),G('v','FrozenLayers','frozen'),C('frozen','Add',[R('a')]),{kind:'observe',target:'frozen',member:event,observer:'collection',cancel},step,snap('v'),{kind:'events'},C('v','Clone')]);
  }
  add('frozen/case-duplicate','viewport-frozen',[viewport(),G('v','FrozenLayers','frozen'),initLayer('A','a'),initLayer('a','b'),C('frozen','Add',[R('a')]),C('frozen','Add',[R('b')]),snap('v')]);
  add('frozen/owned-viewport','viewport-frozen',[viewport(),N('Blocks.Block',['B'],'b'),G('b','Entities','entities'),C('entities','Add',[R('v')]),G('v','FrozenLayers','frozen'),initLayer('A','a'),C('frozen','Add',[R('a')]),C('v','Clone'),snap('b')]);
  for(const kind of ['none','Circle','Ellipse','Polyline2D','Polyline3D','Spline'])for(const [i,m]of transforms.entries())for(const normal of [[0,0,1],[0,3,4]])add(`transform/${kind}/${i}/${normal}`,'viewport-transform',[
    ...(kind==='none'?[]:[boundary(kind)]),viewport(),...(kind==='none'?[]:[S('v','ClippingBoundary',R('c'))]),S('v','Normal',V('Vector3',...normal)),S('v','Elevation',D(3)),
    C('v','TransformBy',[mat(m),V('Vector3',7,-8,9)]),snap('v'),C('v','Clone'),...(kind==='none'?[]:[snap('c')])]);
  for(const kind of ['none','Circle'])add('transform/matrix4/'+kind,'viewport-transform',[
    ...(kind==='none'?[]:[boundary(kind)]),viewport(),...(kind==='none'?[]:[S('v','ClippingBoundary',R('c'))]),C('v','TransformBy',[{new:'Matrix4',args:[2,0,0,3,0,-1,0,4,0,0,.5,5,9,8,7,6].map(D)}]),snap('v')]);
  for(const kind of ['Circle','Ellipse'])add('block/viewport/'+kind,'viewport-block',[
    boundary(kind),N('Entities.Viewport',[R('c')]),N('Blocks.Block',['B'],'b'),G('b','Entities','entities'),C('entities','Add',[R('v')]),snap('b'),
    C('entities','Remove',[R('c')],null,['Entities.EntityObject']),C('b','Clone',['Copy'],'q',['String']),snap('q'),S('v','ClippingBoundary',null),C('entities','Remove',[R('c')],null,['Entities.EntityObject']),snap('b')]);
  const hosts=[['view','Tables.View',['V']],['vport','Tables.VPort',['P']],['viewport','Entities.Viewport',[]],['wrong','Entities.Line',[]]];
  for(const [name,type,args]of hosts)for(const version of [12,13,14,15,16,17,18])add(`sun/profile/${name}/${version}`,'sun-host',[
    N(type,args,'host'),Sun('IsHost',[R('host')]),Sun('CheckProfile',[R('host'),E('Header.DxfVersion',version)]),Sun('Get',[R('host')]),Sun('IsPresent',[R('host')])]);
  for(const [name,type,args]of hosts)for(const target of ['none','DxfSun','DxfXRecord'])for(const present of [false,true])add(`sun/attachment/${name}/${target}/${present}`,'sun-host',[
    N(type,args,'host'),...(target==='none'?[]:[N('Objects.'+target,[],'sun')]),Sun('Set',[R('host'),target==='none'?null:R('sun'),present]),Sun('Get',[R('host')]),Sun('IsPresent',[R('host')]),Sun('CheckClone',[R('host')]),C('host','Clone'),snap('host')]);
  add('sun/null-host','sun-host',[Sun('IsHost',[null]),Sun('Get',[null]),Sun('IsPresent',[null]),Sun('CheckClone',[null]),Sun('Set',[null,null,true]),Sun('CheckProfile',[null,E('Header.DxfVersion',18)])]);
  add('viewport/clone-authored-bounds','viewport-clipping',[
    boundary('Circle'),N('Entities.Viewport',[R('c')]),S('v','Center',V('Vector3',9,8,7)),S('v','Width',D(123)),S('v','Height',D(234)),S('v','SunHandlePresent',true,true),C('v','Clone',[],'q'),G('q','ClippingBoundary','copy'),equal('c','copy'),snap('q'),snap('v')]);
  add('entity-event-args','viewport-events',[boundary('Line'),N('Entities.EntityChangeEventArgs',[R('c')],'args'),G('args','Item','item'),equal('c','item'),N('Entities.EntityChangeEventArgs',[null],'empty')]);
  return out;
}
