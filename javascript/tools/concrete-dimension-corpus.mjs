// Deterministic inputs only. C# and JavaScript supply all results independently.
import { D, I, R, V, E, A } from './geometry-corpus.mjs';
import { dimensionConstructors, dimensionProperties, dimensionBuildOverloads } from './concrete-dimension-schema.mjs';
import { DimensionStyleOverrideType as O } from '../netDxf/Tables/DimensionStyleOverrideType.js';
const names = Object.keys(dimensionProperties);
const N = (type,args=[],id='d',signature) => ({kind:'new',type,args,id,...(signature ? {signature} : {})});
const S = (target,member,value) => ({kind:'set',target,member,value});
const G = (target,member,id) => ({kind:'get',target,member,...(id ? {id} : {})});
const C = (target,member,args=[],id,signature) => ({kind:'call',target,member,args,...(id ? {id} : {}),...(signature ? {signature} : {})});
const snap = (target='d') => ({kind:'snapshot',target});
const build = (id='b',signature=['Entities.Dimension','String'],name='Generated') => ({kind:'call',type:'Entities.DimensionBlock',member:'Build',args:signature.length===1 ? [R('d')] : [R('d'),name],signature,id});
const fresh = name => N('Entities.'+name);
const style = () => ({new:'Tables.DimensionStyle',args:['Style']});
const override = (name,value) => ({new:'Tables.DimensionStyleOverride',args:[E('Tables.DimensionStyleOverrideType',O[name]),value]});
const v2 = (x,y) => V('Vector2',x,y);
function constructorArgument(parameter, index, variant) {
  const {type,name}=parameter;
  if(type==='double')return D(name==='radius' ? 2+variant : /Angle|rotation/.test(name) ? 15+variant*30 : 1+variant);
  if(type==='netDxf.Vector2')return /center|origin/i.test(name) ? v2(variant,-variant) : /second|end|leader/i.test(name) ? v2(4+variant,3) : v2(1,2+variant);
  if(type==='netDxf.Vector3')return V('Vector3',0,variant ? 3 : 0,variant ? 4 : 1);
  if(type==='netDxf.Tables.DimensionStyle')return variant ? {static:'Tables.DimensionStyle',property:'Iso25'} : style();
  if(type==='netDxf.Entities.Polyline2DVertex')return {new:'Entities.Polyline2DVertex',args:[v2(name==='startPoint'?0:4,name==='startPoint'?0:3),D(0.5)]};
  if(type==='netDxf.Entities.Arc')return {new:'Entities.Arc',args:[V('Vector3',0,0,2),D(3),D(20),D(150)]};
  if(type==='netDxf.Entities.Circle')return {new:'Entities.Circle',args:[V('Vector3',0,0,2),D(3)]};
  if(type==='netDxf.Entities.Line')return {new:'Entities.Line',args:[V('Vector3',0,0,0),V('Vector3',name==='secondLine'?0:4,name==='secondLine'?4:0,0)]};
  if(type==='netDxf.Entities.OrdinateDimensionAxis')return E('Entities.OrdinateDimensionAxis',variant%2);
  throw new Error('Unmapped constructor parameter '+JSON.stringify(parameter));
}
export function concreteDimensionCorpus() {
  const all=[],add=(name,category,steps)=>all.push({name:'concrete-dimension/'+name,category,request:{steps}});
  for(const [at,ctor] of dimensionConstructors.entries()) {
    for(let variant=0;variant<3;variant++)add(`constructor/${at}/${variant}`,'constructors',[
      N(ctor.type,ctor.parameters.map((p,i)=>constructorArgument(p,i,variant)),'d',ctor.signature),snap(),C('d','Clone',[],'copy'),snap('copy'),build(),snap()]);
    for(const [index,p]of ctor.parameters.entries())if(['netDxf.Entities.Line','netDxf.Entities.Arc','netDxf.Entities.Circle','netDxf.Tables.DimensionStyle'].includes(p.type))
      add(`constructor-null/${at}/${index}`,'null-constructors',[N(ctor.type,ctor.parameters.map((p,i)=>i===index?null:constructorArgument(p,i,0)),'d',ctor.signature)]);
  }
  for(const name of names) {
    for(const [member,values]of Object.entries({LineSpacingFactor:[0,0.25,1,4,5,NaN],TextRotation:[-360,-30,-0,0,27,720,NaN],Elevation:[-3,-0,0,4,Infinity,NaN],UserText:[null,'',' ','<> mm','above <>\\Xbelow <>','東京 Ø R']}))
      for(const [i,value]of values.entries())add(`base/${name}/${member}/${i}`,'base-properties',[fresh(name),S('d',member,typeof value==='number'?D(value):value),snap(),C('d','Clone'),build(),snap()]);
    for(const manuallySet of [false,true])for(const [pointIndex,point]of [[0,0],[7,-4],[-0,0]].entries())add(`text-position/${name}/${manuallySet}/${pointIndex}`,'placement',[
      fresh(name),S('d','TextReferencePoint',v2(...point)),S('d','TextPositionManuallySet',manuallySet),C('d','Update'),snap(),build(),snap(),C('d','Clone')]);
    for(const member of dimensionProperties[name]) {
      if(['Measurement','ArcAngle','ArcDefinitionPoint','DimLinePosition'].includes(member))continue;
      const value=member==='Axis'?E('Entities.OrdinateDimensionAxis',1):['Radius','StartAngle','EndAngle','Offset','Rotation'].includes(member)?D(member==='Radius'?3:12):v2(3,-2);
      add(`setter/${name}/${member}`,'derived-properties',[fresh(name),S('d',member,value),snap(),C('d','Update'),snap(),build(),snap()]);
    }
    for(const [index,point]of [[0,0],[3,4],[-7,3],[NaN,0]].entries())add(`line-position/${name}/${index}`,'placement',[
      fresh(name),C('d','SetDimensionLinePosition',[v2(...point)]),snap(),build(),snap()]);
    for(const event of ['DimensionStyleChanged','DimensionBlockChanged'])for(const fail of [false,true])for(const replace of [false,true])add(`events/${name}/${event}/${fail}/${replace}`,'events',[
      fresh(name),...(event==='DimensionBlockChanged'?[build(),S('d','Block',R('b'))]:[]),
      {kind:'observe',target:'d',member:event,observer:'change',throw:fail,...(replace?{replace:event==='DimensionStyleChanged'?style():null}:{})},
      ...(event==='DimensionStyleChanged'?[S('d','Style',style())]:[C('d','Update')]),snap(),{kind:'events'}]);
    const values=[['ArrowSize',D(0.75)],['TextHeight',D(2)],['TextOffset',D(-1)],['DimScaleOverall',D(2)],['DimScaleLinear',D(-2)],['DimLine1Off',true],['DimLine2Off',true],['ExtLine1Off',true],['ExtLine2Off',true],['DimPrefix',''],['DimPrefix','custom'],['DimSuffix','mm'],['DecimalSeparator',{char:44}],['LengthPrecision',{short:2}],['DimRoundoff',D(0.25)],['TextColor',{static:'AciColor',property:'Red'}],['DimArrow1',{static:'Entities.DimensionArrowhead',property:'Open'}],['DimArrow2',null]];
    for(const [member,value]of values)add(`overrides/${name}/${member}/${typeof value==='string'?value:''}`,'overrides',[
      fresh(name),G('d','StyleOverrides','o'),C('o','Add',[override(member,value)]),C('d','Update'),build(),snap(),C('d','Clone',[],'copy'),snap('copy')]);
    for(const [label,patch]of [['linear',{DimScaleLinear:D(2),DimPrefix:'P',DimSuffix:'mm'}],['rounding',{DimRoundoff:D(0.25)}],['off',{DimLine1Off:true,DimLine2Off:true,ExtLine1Off:true,ExtLine2Off:true}],['centers',{CenterMarkSize:D(-2)}],['architectural',{DimLengthUnits:E('Units.LinearUnitType',4)}],['alternate',{TextOffset:D(-1),TextHeight:D(3)}]])add(`style/${name}/${label}`,'formatting',[
      fresh(name),G('d','Style','s'),...Object.entries(patch).map(([key,value])=>S('s',key,value)),build(),snap()]);
    const matrices=[[1,0,0,0,1,0,0,0,1],[2,0,0,0,3,0,0,0,4],[-1,0,0,0,1,0,0,0,1],[0,-1,0,1,0,0,0,0,1],[1,2,0,0,1,0,0,0,1],[0,0,0,0,0,0,0,0,0],[NaN,0,0,0,1,0,0,0,1],[Infinity,0,0,0,1,0,0,0,1]];
    for(const [i,matrix]of matrices.entries())for(const normal of [[0,0,1],[0,3,4]])add(`transform/${name}/${i}/${normal}`,'transforms',[
      fresh(name),S('d','Normal',V('Vector3',...normal)),S('d','Elevation',D(2)),C('d','TransformBy',[{new:'Matrix3',args:matrix.map(D)},V('Vector3',5,-2,7)],undefined,['Matrix3','Vector3']),snap(),C('d','Clone'),build(),snap()]);
    for(const row of [[0,0,0,1],[9,8,7,6]])add(`matrix4/${name}/${row}`,'matrix4',[
      fresh(name),C('d','TransformBy',[{new:'Matrix4',args:[2,0,0,5,0,3,0,-2,0,0,4,7,...row].map(D)}],undefined,['Matrix4']),snap(),build(),snap()]);
    for(let seed=1;seed<=24;seed++) {
      let state=seed;const random=()=>((state=(Math.imul(state,1664525)+1013904223)>>>0)%2001-1000)/32;
      const steps=[fresh(name),S('d','TextRotation',D(random())),S('d','Elevation',D(random())),S('d','TextReferencePoint',v2(random(),random())),C('d','Update'),snap(),build(),snap(),C('d','Clone',[],'copy'),snap('copy')];
      add(`random/${name}/${seed}`,'randomized',steps);
    }
    add(`ownership/${name}`,'ownership',[fresh(name),N('Blocks.Block',['Owner'],'owner'),G('owner','Entities','entities'),C('entities','Add',[R('d')]),build(),snap('owner'),C('owner','Clone',['Copy'],'copy'),snap('copy'),C('entities','Remove',[R('d')],undefined,['Entities.EntityObject']),snap('owner')]);
  }
  for(const [i,item]of dimensionBuildOverloads.entries())add(`build-null/${i}`,'null-builders',[
    {kind:'call',type:'Entities.DimensionBlock',member:'Build',signature:item.signature.split(',').map(type=>type==='string'?'String':type),args:item.signature.includes(',')?[null,'Null']: [null]}]);
  return all;
}
