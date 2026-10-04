// Complete original detached case only; all typed document/transport cases remain unregistered.
import {PolygonMesh,Vector3} from '../../index.js';
import {ArgumentException,ArgumentOutOfRangeException} from '../../runtime/Errors.js';
import {Run,Equal,Throws} from './TestHarness.js';
export function RegisterPolygonMeshCardinalityTests(){Run('polygonmesh/model/invalid-smooth-type',()=>{
 const mesh=new PolygonMesh(2,2,Array.from({length:4},()=>new Vector3()));
 Throws(ArgumentOutOfRangeException,()=>{mesh.SmoothType=8;});Equal(0,mesh.SmoothType,'Rejected enum mutated surface');
 Throws(ArgumentException,()=>new PolygonMesh(2,2,Array.from({length:3},()=>new Vector3())));
 Throws(ArgumentException,()=>new PolygonMesh(2,2,Array.from({length:5},()=>new Vector3())));
});}
