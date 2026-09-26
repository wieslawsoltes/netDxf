// Complete seven detached constructor cases only. Typed wire/ownership cases remain unported.
import { PolyfaceMesh, PolyfaceMeshFace, Vector3 } from '../../index.js';
import { ArgumentException } from '../../runtime/Errors.js';
import { Run, Check } from './TestHarness.js';
const points = [new Vector3(11,12,13),new Vector3(21,22,23),new Vector3(31,32,33),new Vector3(41,42,43)];
export function RegisterPolyfaceGrammarTests() {
  for(let fault=0;fault<7;fault++)Run(`polyface/constructor/${fault}`,()=>PolyfaceGrammarConstructor(fault));
}
export function PolyfaceGrammarConstructor(fault) {
  let rejected = false;
  try {
    if(fault===0)new PolyfaceMeshFace([]);
    else if(fault===1)new PolyfaceMeshFace(new Array(5).fill(0));
    else if(fault===2)new PolyfaceMeshFace([0,1]);
    else if(fault===3)new PolyfaceMesh(points,[[-32768]]);
    else if(fault===4)new PolyfaceMesh(points,[[5]]);
    else if(fault===5)new PolyfaceMesh(points,[new PolyfaceMeshFace([-5])]);
    else new PolyfaceMesh(points,[null]);
  } catch(error) { if(!(error instanceof ArgumentException))throw error; rejected=true; }
  Check(rejected,'Invalid constructor topology accepted');
}
