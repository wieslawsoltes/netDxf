// Independent input scenarios only. Expected values are never stored in this corpus.
import { D, I, R, E, A, V } from './geometry-corpus.mjs';
const H = 'Entities.HatchBoundaryPath';
const N = (type, args = [], id = 'edge', signature) => ({ kind: 'new', type: type.includes('.') ? type : 'Entities.' + type,
  args, id, ...(signature ? { signature } : {}) });
const G = (target, member, id) => ({ kind: 'get', target, member, ...(id ? { id } : {}) });
const S = (target, member, value) => ({ kind: 'set', target, member, value });
const C = (target, member, args = [], id, signature, nonPublic = false) => ({ kind: 'call', target, member, args,
  ...(id ? { id } : {}), ...(signature ? { signature } : {}), ...(nonPublic ? { nonPublic: true } : {}) });
const snap = target => ({ kind: 'snapshot', target });
const put = (target, index, value) => ({ kind: 'set-index', target, args: [I(index)], value });
const mat = values => ({ new: 'Matrix3', args: values.map(D) });
const vector = (x, y, z) => V('Vector3', x, y, z);
const point = (x, y) => V('Vector2', x, y);
const identity = [1,0,0,0,1,0,0,0,1];
const pathFromEdges = (edges = ['edge'], id = 'path') => N(H, [A(H + '+Edge', edges.map(R))], id, ['IEnumerable<' + H + '+Edge>']);
const pathFromEntities = (entities = ['source'], id = 'path') => N(H, [A('Entities.EntityObject', entities.map(R))], id, ['IEnumerable<Entities.EntityObject>']);
function edgeSteps(kind, id = 'edge') {
  const type = ['Straight', 'Bulged'].includes(kind) ? 'Polyline' : kind === 'Periodic' ? 'Spline' : kind;
  const steps = [N(H + '+' + type, [], id)];
  const assign = values => steps.push(...Object.entries(values).map(([key, value]) => S(id, key, value)));
  if (kind === 'Line') assign({ Start: point(1,2), End: point(3,-4) });
  else if (kind === 'Arc') assign({ Center: point(2,3), Radius: D(2), StartAngle: D(20), EndAngle: D(290), IsCounterclockwise: true });
  else if (kind === 'Ellipse') assign({ Center: point(2,3), EndMajorAxis: point(4,1), MinorRatio: D(.25), StartAngle: D(20), EndAngle: D(290), IsCounterclockwise: false });
  else if (type === 'Polyline') assign({ IsClosed: true, Vertexes: A('Vector3', [[0,0,-0],[4,0,kind==='Bulged'?.5:0],[5,3,kind==='Bulged'?-.75:0],[0,3,0]].map(p=>vector(...p))) });
  else {
    const periodic = kind === 'Periodic';
    const controls = periodic ? [[0,0,1],[4,7,1],[10,1,1],[7,-5,1],[-3,-2,1],[0,0,1],[4,7,1]] : [[1,2,1],[3,5,2],[6,-1,1]];
    assign({ Degree: { short: 2 }, IsRational: true, IsPeriodic: periodic,
      Knots: A('Double', (periodic ? Array.from({length:10},(_,i)=>i*.25-3) : [0,0,0,1,1,1]).map(D)),
      ControlPoints: A('Vector3', controls.map(p=>vector(...p))), StartTangent: point(3,4), EndTangent: point(-5,6) });
    steps.push(G(id,'FitPoints',id+'Fits'), C(id+'Fits','Add',[point(1,2)]), C(id+'Fits','Add',[point(7,8)]));
  }
  return steps;
}
function hatchSteps(mode = 'Solid', associated = false, paths = ['path'], id = 'h') {
  const pattern = mode === 'Gradient' ? {new:'Entities.HatchGradientPattern',args:[]} : {static:'Entities.HatchPattern',property:mode==='Solid'?'Solid':'Line'};
  const steps = [N('Hatch', [pattern,A(H,paths.map(R)),associated], id), G(id,'Pattern',id+'Pattern')];
  if (mode === 'User') steps.push(S(id+'Pattern','Type',E('Entities.HatchType',0)));
  if (mode === 'Double') steps.push(S(id+'Pattern','IsDouble',true));
  return steps;
}
export function hatchEntityCorpus() {
  const probes = [], add = (name, category, steps) => probes.push({name:'hatch-entity/'+name,category,request:{steps}});
  for (const kind of ['Line','Arc','Ellipse','Polyline','Spline']) {
    const type = H+'+'+kind;
    add('defaults/'+kind,'hatch-boundary',[N(type),C('edge','Clone'),C('edge','ConvertTo'),snap('edge')]);
    add('null-entity/'+kind,'hatch-boundary',[N(type,[null],'bad',['Entities.EntityObject']),{kind:'call',type,member:'ConvertFrom',args:[null],signature:['Entities.EntityObject']}]);
  }
  for (const kind of ['Line','Arc','Ellipse','Straight','Bulged','Spline','Periodic']) {
    add('clone-convert/'+kind,'hatch-boundary',[...edgeSteps(kind),C('edge','Clone',[],'clone'),C('edge','ConvertTo',[],'entity'),snap('edge'),snap('clone'),pathFromEdges(),C('path','Clone',[],'pathClone'),snap('path')]);
    add('mixed/'+kind,'hatch-boundary',[...edgeSteps(kind),...edgeSteps('Line','line'),pathFromEdges(['edge','line']),C('path','Clone'),snap('edge'),snap('path')]);
  }
  for (const ccw of [false,true]) for (const [start,end] of [[0,360],[360,0],[350,10],[10,350],[-360,720],[42,42]]) for (const kind of ['Arc','Ellipse'])
    add(`angles/${kind}/${ccw}/${start}/${end}`,'hatch-boundary',[...edgeSteps(kind),S('edge','IsCounterclockwise',ccw),S('edge','StartAngle',D(start)),S('edge','EndAngle',D(end)),C('edge','ConvertTo'),C('edge','Clone'),snap('edge')]);
  for (const count of [0,1,2,4]) for (const closed of [false,true]) for (const bulge of [-1,-0,0,.5]) {
    add(`polyline/${count}/${closed}/${Object.is(bulge,-0)?'-0':bulge}`,'hatch-boundary',[N(H+'+Polyline'),S('edge','Vertexes',A('Vector3',Array.from({length:count},(_,i)=>vector(i*3,i%2,bulge)))),S('edge','IsClosed',closed),C('edge','Explode'),C('edge','ConvertTo'),pathFromEdges(),C('path','Clone')]);
  }
  const entityInputs = [
    ['Line',[vector(1,2,3),vector(4,5,6)]], ['Circle',[vector(1,2,3),D(2)]], ['Arc',[vector(1,2,3),D(2),D(20),D(300)]],
    ['Ellipse',[vector(1,2,3),D(6),D(2)]], ['Polyline2D',[A('Vector2',[point(0,0),point(3,0),point(2,2)]),true]],
    ['Polyline3D',[A('Vector3',[vector(0,0,0),vector(3,0,0),vector(2,2,0)]),true]],
    ['Spline',[A('Vector3',[vector(0,0,0),vector(2,3,0),vector(4,0,0)]),A('Double',[D(1),D(1),D(1)]),{short:2},false]]
  ];
  for (const [kind,args] of entityInputs) for (const normal of [[0,0,1],[0,0,-1],[1,2,3]]) for (const associated of [false,true])
    add(`source/${kind}/${normal}/${associated}`,'hatch-association',[
      N(kind,args,'source'),S('source','Normal',vector(...normal)),pathFromEntities(),...hatchSteps('Solid',associated),
      snap('source'),C('h','Clone',[],'clone'),C('h','CreateBoundary',[false],'detached'),snap('h'),snap('source'),
      C('h','CreateBoundary',[true],'linked'),snap('h'),C('h','UnLinkBoundary',[],'released'),snap('h'),snap('source'),snap('clone')]);
  for (const associated of [false,true]) for (const action of ['null-add','duplicate-add','foreign-add','remove','clear','replace','unlink','identity','relink']) {
    const steps=[N('Line',[vector(0,0,0),vector(2,3,0)],'source'),pathFromEntities(),pathFromEntities(['source'],'other'),...hatchSteps('Solid',associated),G('h','BoundaryPaths','paths'),C('paths','Add',[R('other')]),
      {kind:'observe',target:'h',member:'HatchBoundaryPathAdded',observer:'add'}, {kind:'observe',target:'h',member:'HatchBoundaryPathRemoved',observer:'remove'}];
    if(action==='null-add')steps.push(C('paths','Add',[null]));
    if(action==='duplicate-add')steps.push(C('paths','Add',[R('path')]));
    if(action==='foreign-add')steps.push(N('Hatch',[{static:'Entities.HatchPattern',property:'Solid'},false],'foreign'),G('foreign','BoundaryPaths','foreignPaths'),C('foreignPaths','Add',[R('path')]),snap('foreign'));
    if(action==='remove')steps.push(C('paths','Remove',[R('path')]));
    if(action==='clear')steps.push(C('paths','Clear'));
    if(action==='replace')steps.push(pathFromEntities(['source'],'replacement'),put('paths',0,R('replacement')));
    if(action==='unlink')steps.push(C('h','UnLinkBoundary'));
    if(action==='identity')steps.push(C('h','TransformBy',[mat(identity),vector(0,0,0)],null,['Matrix3','Vector3']));
    if(action==='relink')steps.push(C('h','CreateBoundary',[true]));
    steps.push(snap('h'),snap('path'),snap('other'),snap('source'),{kind:'events'});
    add(`lifecycle/${associated}/${action}`,'hatch-association',steps);
  }
  for (const event of ['BeforeAddItem','AddItem','BeforeRemoveItem','RemoveItem']) for (const cancel of [false,true]) for (const failure of [false,true]) {
    const steps=[...edgeSteps('Line'),pathFromEdges(),...hatchSteps('Solid',true),G('h','BoundaryPaths','paths'),...edgeSteps('Arc','extra'),pathFromEdges(['extra'],'second'),
      {kind:'observe',target:'paths',member:event,observer:'path-event',cancel,throw:failure},
      C('paths',event.includes('Add')?'Add':'Remove',[R(event.includes('Add')?'second':'path')]),snap('h'),snap('path'),snap('second'),{kind:'events'}];
    add(`events/${event}/${cancel}/${failure}`,'hatch-events',steps);
  }
  for (const value of [null,-1,-0,0,Number.MIN_VALUE,.125,NaN,Infinity,-Infinity])
    add('pixel/'+(value===null?'null':D(value).double),'hatch-model',[N('Hatch',[{static:'Entities.HatchPattern',property:'Line'},false],'h'),S('h','PixelSize',value===null?null:D(value)),C('h','Clone'),snap('h')]);
  for (const method of ['Add','Insert','set_Item']) for (const index of [-1,0,1]) for (const value of [[1,2],[-0,0],[NaN,0],[0,Infinity]]) {
    const args=method==='Add'?[point(...value)]:[I(index),point(...value)];
    add(`seeds/${method}/${index}/${value.map(v=>D(v).double)}`,'hatch-model',[
      N('Hatch',[{static:'Entities.HatchPattern',property:'Solid'},false],'h'),G('h','SeedPoints','seeds'),C('seeds',method,args),snap('h'),C('h','Clone')]);
  }
  // Identity still validates and unlinks; rejected transforms must not unlink or emit path events.
  const transforms = [identity,[2,0,0,0,2,0,0,0,2],[2,0,0,0,3,0,0,0,1],[1,.5,0,0,1,0,0,0,1],[-1,0,0,0,1,0,0,0,1],[0,-1,0,1,0,0,0,0,1],[0,0,0,0,1,0,0,0,1],[NaN,0,0,0,1,0,0,0,1]];
  for (const kind of ['Line','Arc','Ellipse','Straight','Bulged','Spline','Periodic']) for (const mode of ['Solid','Line','Gradient','User','Double']) for (const [plane,normal] of [[0,[0,0,1]],[1,[1,2,3]],[2,[0,0,-1]]])
    transforms.forEach((matrix,i)=>add(`affine/${kind}/${mode}/${plane}/${i}`,'hatch-affine',[
      ...edgeSteps(kind),pathFromEdges(),...hatchSteps(mode),S('h','Normal',vector(...normal)),S('h','Elevation',D(4)),S('h','PixelSize',D(.0625)),
      G('h','SeedPoints','seeds'),C('seeds','Add',[point(3,-2)]),C('seeds','Add',[point(-0,0)]),
      C('h','TransformBy',[mat(matrix),i===0?vector(-0,0,0):vector(7,-11,13)],null,['Matrix3','Vector3']),
      snap('h'),snap('path'),snap('edge'),C('h','Clone'),C('h','CreateBoundary',[false])
    ]));
  for (const field of ['M11','M22','M33']) for (const value of [NaN,Infinity,-Infinity,0])
    add(`atomic/${field}/${D(value).double}`,'hatch-association',[N('Line',[vector(0,0,0),vector(2,3,0)],'source'),pathFromEntities(),...hatchSteps('Solid',true),
      {kind:'new',type:'Matrix3',args:identity.map(D),id:'matrix'},S('matrix',field,D(value)),C('h','TransformBy',[R('matrix'),vector(0,0,0)],null,['Matrix3','Vector3']),snap('h'),snap('source')]);
  // Periodic compact/overlap encodings, knot and weight rejection, and metadata retention.
  for (const defect of ['none','compact','overlap','zero-weight','negative-weight','nonrational','count','spans','repeated','empty','degree','weight-range']) {
    const steps=[...edgeSteps('Periodic'),G('edge','ControlPoints','controls'),G('edge','Knots','knots')];
    if(defect==='compact')steps.push(S('edge','ControlPoints',A('Vector3',[[10,1,1],[7,-5,1],[-3,-2,1],[0,0,1],[4,7,1]].map(p=>vector(...p)))));
    if(defect==='overlap')steps.push(put('controls',0,vector(.125,0,1)));
    if(defect==='zero-weight')steps.push(put('controls',2,vector(10,1,0)));
    if(defect==='negative-weight')steps.push(put('controls',2,vector(10,1,-1)));
    if(defect==='nonrational')steps.push(S('edge','IsRational',false),put('controls',2,vector(10,1,2)));
    if(defect==='count')steps.push(S('edge','Knots',A('Double',Array.from({length:11},(_,i)=>D(i*.25-3)))));
    if(defect==='spans')steps.push(put('knots',0,D(-3.125)));
    if(defect==='repeated')steps.push(put('knots',1,D(-3)));
    if(defect==='empty')steps.push(S('edge','Knots',A('Double',[])),S('edge','ControlPoints',A('Vector3',[])));
    if(defect==='degree')steps.push(S('edge','Degree',{short:11}));
    if(defect==='weight-range')steps.push(put('controls',2,vector(10,1,Number.MIN_VALUE)),put('controls',3,vector(7,-5,1e308)));
    steps.push(C('edge','ConvertTo',[],'converted'),C('converted','PolygonalVertexes',[I(17)]),C('edge','Clone'),snap('edge'),pathFromEdges(),...hatchSteps(),C('h','CreateBoundary',[true]),snap('h'),snap('edge'));
    add('periodic/'+defect,'hatch-spline',steps);
  }
  for (const member of ['StartTangent','EndTangent']) for (const value of [null,[0,0],[-0,0],[NaN,0],[0,Infinity]])
    add(`spline-tangent/${member}/${value?.map(v=>D(v).double)??'null'}`,'hatch-spline',[...edgeSteps('Spline'),S('edge',member,value===null?null:point(...value)),C('edge','Clone'),C('edge','ConvertTo'),snap('edge')]);
  return probes;
}
