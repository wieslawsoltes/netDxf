import { documentLifecycleCorpus } from './document-lifecycle-corpus.mjs';
// Deterministic input-only authoring scenarios. Native outputs are never embedded.
const r=ref=>({ref}), make=(id,type,args=[])=>({method:'new',id,value:{new:type,args}});
const get=(target,member,id)=>({method:'get',target,member,id});
const call=(target,member,args=[],signature)=>({method:'call',target,member,args,...(signature?{signature}:{})});
const set=(target,member,value)=>({method:'set',target,member,value});
const snap=target=>({method:'snapshot',target});
export function documentOwnershipCorpus(newLine = '\n'){
  const all=[],add=(name,steps)=>all.push({name:'document/'+name,request:{op:'document-ownership',steps}});
  const initial=()=>[make('doc','DxfDocument'),get('doc','Entities','entities')];
  add('default',initial());
  for(const [table,type,args] of [['Layers','Tables.Layer',['Layer']],['Linetypes','Tables.Linetype',['Pattern']],['TextStyles','Tables.TextStyle',['Caption','txt.shx']],['ShapeStyles','Tables.ShapeStyle',['Shapes','ltypeshp.shx']],['ApplicationRegistries','Tables.ApplicationRegistry',['APP']],['DimensionStyles','Tables.DimensionStyle',['DIM']],['MlineStyles','Objects.MLineStyle',['ML']],['UCSs','Tables.UCS',['UCS']],['Views','Tables.View',['VIEW']],['VPorts','Tables.VPort',['PORT']],['Blocks','Blocks.Block',['Block']],['Layouts','Objects.Layout',['Sheet']],['Groups','Objects.Group',['Group']]]){
    add('table/'+table,[...initial(),get('doc',table,'table'),make('item',type,args),call('table','Add',[r('item')]),snap('doc'),call('table','GetReferences',[r('item')]),set('item','Name','Changed'),snap('doc'),call('table','GetReferences',['Changed']),call('table','Remove',[r('item')]),snap('doc')]);
  }
  const entities=['Line','Point','Arc','Circle','Ellipse','Face3D','Solid','Trace','Ray','XLine','Text','MText','Mesh','Polyline2D','Polyline3D','PolyfaceMesh','PolygonMesh','Spline','Helix','AlignedDimension','LinearDimension','Angular2LineDimension','Angular3PointDimension','ArcLengthDimension','RadialDimension','DiametricDimension','OrdinateDimension','Tolerance','Wipeout','Viewport','Light','Body','Region','Solid3D','OleFrame','Ole2Frame'];
  const v=(x,y,z=0)=>({new:'Vector3',args:[x,y,z]}),arr=(array,values)=>({array,values}),points=arr('Vector3',[v(0,0),v(1,0),v(1,1),v(0,1)]);
  const spline={new:'Entities.Spline',args:[points,arr('Double',[1,1,1,1]),{short:3}]};
  const entityArgs={Mesh:[points,arr('Int32[]',[arr('Int32',[{int:0},{int:1},{int:2},{int:3}])])],PolyfaceMesh:[points,arr('Int16[]',[arr('Int16',[{short:1},{short:2},{short:3},{short:4}])])],PolygonMesh:[{short:2},{short:2},points],Spline:spline.args,Helix:[spline],Wipeout:[0,0,2,2],OleFrame:[arr('Byte',[{byte:1},{byte:2}]),{short:1}],Ole2Frame:[arr('Byte',[{byte:1},{byte:2}]),v(0,1),v(1,0),'',{short:2},{enum:'Entities.OleObjectType',value:2},{short:0}]};
  for(const type of entities)add('entity/'+type,[...initial(),make('entity','Entities.'+type,entityArgs[type]??[]),call('entities','Add',[r('entity')],['Entities.EntityObject']),snap('doc'),call('entities','Remove',[r('entity')],['Entities.EntityObject']),snap('doc')]);
  add('line-resource-lifecycle',[...initial(),make('entity','Entities.Line'),make('layer','Tables.Layer',['Custom']),set('entity','Layer',r('layer')),call('entities','Add',[r('entity')]),get('doc','Layers','layers'),call('layers','GetReferences',['Custom']),make('other','Tables.Layer',['Other']),set('entity','Layer',r('other')),call('layers','Remove',['Custom']),snap('doc'),call('entities','Remove',[r('entity')]),call('layers','Remove',['Other']),snap('doc')]);
  add('paper-space',[...initial(),get('doc','Layouts','layouts'),make('a','Objects.Layout',['A']),make('b','Objects.Layout',['B']),call('layouts','Add',[r('a')]),call('layouts','Add',[r('b')]),set('entities','ActiveLayout','B'),make('line','Entities.Line'),call('entities','Add',[r('line')]),snap('doc'),call('layouts','Remove',['A']),snap('doc'),call('layouts','Remove',['B']),snap('doc')]);
  add('dimension-regeneration',[...initial(),set('doc','BuildDimensionBlocks',true),make('dimension','Entities.AlignedDimension'),call('entities','Add',[r('dimension')]),snap('doc'),call('dimension','Update'),snap('doc'),call('entities','Remove',[r('dimension')]),snap('doc')]);
  return all.concat(documentLifecycleCorpus()).map(probe=>({...probe,request:{...probe.request,newLine}}));
}
