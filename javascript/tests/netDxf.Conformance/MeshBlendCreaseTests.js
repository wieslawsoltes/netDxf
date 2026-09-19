// The complete detached default case. Typed wire/Block/Insert cases remain unported.
import { Mesh, MeshEdge, Vector3 } from '../../index.js';
import { Run, Check } from './TestHarness.js';
export function RegisterMeshBlendCreaseTests() {
  Run('mesh/blend/default', () => {
    // Exact ProfileMesh(0) input from MeshVersionTests.cs.
    const mesh = new Mesh([new Vector3(1e-20,0,0),new Vector3(2,0,0),new Vector3(0,3,1)],[[0,1,2]],
      [new MeshEdge(0,1,0),new MeshEdge(1,2,1.5),new MeshEdge(2,0,-1)]);
    mesh.SubdivisionLevel = 0;
    Check(!mesh.BlendCrease, 'Default mesh blend flag is not false.');
  });
}
