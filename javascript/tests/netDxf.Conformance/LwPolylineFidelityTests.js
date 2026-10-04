// Complete detached model/transform bodies from the pinned original file.
// Document, wire, smoothing-save and INSERT cases are not shortened or registered here.
import {Polyline2D,Polyline2DVertex,Vector3,Matrix3} from '../../index.js';
import {ArgumentOutOfRangeException,NotSupportedException} from '../../runtime/Errors.js';
import {Run,Check,Equal,Throws} from './TestHarness.js';
export function RegisterLwPolylineFidelityTests(){Run('lw-fidelity/model',LwFidelityModel);Run('lw-fidelity/transform',LwFidelityTransform);}
export function LwFidelityModelPolyline(mode,identifiers,closed=true){
 const vertices=[new Polyline2DVertex(-3,2,.25),new Polyline2DVertex(1,-1,0),new Polyline2DVertex(6,4,-.5),new Polyline2DVertex(9,-2,.75)];
 vertices[0].EndWidthOverride=1.25;vertices[1].StartWidthOverride=0;vertices[2].StartWidthOverride=2.5;vertices[2].EndWidthOverride=3.75;
 const p=new Polyline2D(vertices,closed);p.Elevation=2.5;p.ConstantWidth=mode==='absent'?null:mode==='zero'?0:1.75;
 if(identifiers)[0,-17,2147483647,42].forEach((id,i)=>{p.Vertexes.get_Item(i).VertexIdentifier=id;});return p;
}
export function LwFidelityEqual(expected,actual){
 for(const key of ['ConstantWidth','IsClosed','Elevation'])Equal(expected[key],actual[key],key);Equal(expected.Vertexes.Count,actual.Vertexes.Count,'Vertex count');
 for(let i=0;i<expected.Vertexes.Count;i++){
  const a=expected.Vertexes.get_Item(i),b=actual.Vertexes.get_Item(i);
  for(const key of ['Position','Bulge','StartWidthOverride','EndWidthOverride','VertexIdentifier'])Equal(a[key],b[key],key);
  Equal(expected.GetEffectiveStartWidth(i),actual.GetEffectiveStartWidth(i),'Effective start width');Equal(expected.GetEffectiveEndWidth(i),actual.GetEffectiveEndWidth(i),'Effective end width');
 }
}
export function LwFidelityModel(){
 const vertex=new Polyline2DVertex(1,2);Check(vertex.StartWidthOverride===null&&vertex.EndWidthOverride===null&&vertex.VertexIdentifier===null,'Defaults invented optional fields.');
 vertex.StartWidth=0;vertex.EndWidth=0;vertex.VertexIdentifier=0;Equal(0,vertex.StartWidthOverride,'Explicit start zero lost');Equal(0,vertex.EndWidthOverride,'Explicit end zero lost');
 const copy=vertex.Clone();Equal(0,copy.VertexIdentifier,'Identifier copy');vertex.StartWidthOverride=null;vertex.EndWidthOverride=null;
 Equal(0,vertex.StartWidth,'Absent start default');Equal(0,vertex.EndWidth,'Absent end default');Equal(0,copy.StartWidthOverride,'Clone aliases presence');
 for(const bad of [-1,NaN,Infinity,-Infinity]){Throws(ArgumentOutOfRangeException,()=>{vertex.StartWidth=bad;});Throws(ArgumentOutOfRangeException,()=>{vertex.EndWidthOverride=bad;});Throws(ArgumentOutOfRangeException,()=>{new Polyline2D().ConstantWidth=bad;});}
 const p=LwFidelityModelPolyline('positive',true);
 for(let i=0;i<4;i++){Equal(1.75,p.GetEffectiveStartWidth(i),'Constant precedence start');Equal(1.75,p.GetEffectiveEndWidth(i),'Constant precedence end');}
 Equal(2.5,p.Vertexes.get_Item(2).StartWidthOverride,'Constant setting destroyed raw widths');p.ConstantWidth=0;Equal(2.5,p.GetEffectiveStartWidth(2),'Zero constant masked variable width');
 const clone=p.Clone();clone.Vertexes.get_Item(0).VertexIdentifier=-2147483648;clone.Vertexes.get_Item(2).StartWidth=9;
 Equal(0,p.Vertexes.get_Item(0).VertexIdentifier,'Clone changed source identifier');Equal(2.5,p.Vertexes.get_Item(2).StartWidthOverride,'Clone changed source raw width');
 p.ConstantWidth=5;Throws(ArgumentOutOfRangeException,()=>p.SetConstantWidth(-1));Equal(5,p.ConstantWidth,'Rejected setter cleared constant width');
 p.SetConstantWidth(3);Check(p.ConstantWidth===null,'Legacy uniform setter retained overriding group43');Check([...p.Vertexes].every(v=>v.StartWidthOverride===3&&v.EndWidthOverride===3),'Legacy uniform setter changed established behavior');
}
export function LwFidelityTransform(){
 const p=LwFidelityModelPolyline('positive',true),original=p.Clone();p.TransformBy(Matrix3.Scale(2),Vector3.UnitX);
 Equal(3.5,p.ConstantWidth,'Uniform scale constant width');Equal(5,p.Vertexes.get_Item(2).StartWidthOverride,'Uniform scale raw width');
 Check(p.Vertexes.get_Item(0).StartWidthOverride===null,'Uniform scale invented absent width');Equal(0,p.Vertexes.get_Item(1).StartWidthOverride,'Uniform scale lost explicit zero');
 Equal(original.Vertexes.get_Item(2).VertexIdentifier,p.Vertexes.get_Item(2).VertexIdentifier,'Transform changed identifier');
 const before=p.Clone();for(const m of [Matrix3.Scale(2,3,1),Matrix3.Scale(0),Matrix3.Scale(-1,1,1),new Matrix3(1,0,1,0,1,0,0,0,1)]){Throws(NotSupportedException,()=>p.TransformBy(m,Vector3.Zero));LwFidelityEqual(before,p);}
 p.TransformBy(Matrix3.Scale(3,3,7),Vector3.Zero);Equal(10.5,p.ConstantWidth,'In-plane scaling');
}
