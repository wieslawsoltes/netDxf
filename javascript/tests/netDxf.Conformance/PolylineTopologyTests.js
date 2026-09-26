// Complete original authored/admission cases; registered document cases are not shortened.
import {Polyline3D,Vector3} from '../../index.js';
import {ArgumentException,NotSupportedException} from '../../runtime/Errors.js';
import {Run,Check,Equal,Throws} from './TestHarness.js';
export function RegisterPolylineTopologyTests(){Run('polyline-topology/authored',PolylineTopologyAuthored);Run('polyline-topology/admission-bound',PolylineTopologyBound);}
export function PolylineTopologyAuthored(){const p=new Polyline3D([Vector3.Zero,Vector3.UnitX]);p.InsertVertex(1,Vector3.UnitY);p.MoveVertex(0,2);p.RemoveVertexAt(1);Check(p.Vertexes.Count===2&&p.Vertexes.get_Item(0).Equals(Vector3.UnitY)&&p.Vertexes.get_Item(1).Equals(Vector3.Zero)&&p.VertexRecords.Count===0&&p.EndSequenceRecord===null,'authored topology semantics');Throws(ArgumentException,()=>p.InsertVertex(0,new Vector3(NaN,0,0)));}
export function PolylineTopologyBound(){const p=new Polyline3D(Array.from({length:65536},()=>Vector3.Zero));Throws(NotSupportedException,()=>p.InsertVertex(0,Vector3.Zero));p.RemoveVertexAt(0);p.InsertVertex(65535,Vector3.UnitX);Equal(65536,p.Vertexes.Count,'shared topology admission bound');}
