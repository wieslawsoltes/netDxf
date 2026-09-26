// Complete detached test bodies. Original document/wire identities remain unported.
import { UCS, Vector3 } from '../../index.js';
import { ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { Run, Near, Equal, Throws } from './TestHarness.js';
export function RegisterUcsElevationTests() {
  Run('ucs/elevation/defaults',()=>{
    Near(0,new UCS('Default').Elevation,'default elevation');
    Near(0,new UCS('Axes',Vector3.Zero,Vector3.UnitX,Vector3.UnitY).Elevation,'axis constructor elevation');
    Near(0,UCS.FromNormal('Normal',Vector3.Zero,Vector3.UnitZ).Elevation,'factory elevation');
  });
  Run('ucs/elevation/finite-values',()=>{
    const ucs=new UCS('Finite');ucs.Elevation=-12.5;
    for(const invalid of [NaN,-Infinity,Infinity]) { Throws(ArgumentOutOfRangeException,()=>{ucs.Elevation=invalid;});Near(-12.5,ucs.Elevation,'invalid assignment changed elevation'); }
  });
  Run('ucs/elevation/clone-and-origin',()=>{
    const source=new UCS('Source',new Vector3(1,2,3),Vector3.UnitY,Vector3.Negate(Vector3.UnitX));source.Elevation=44.125;
    const copy=source.Clone('Copy');Near(source.Elevation,copy.Elevation,'clone elevation');Equal(source.Origin,copy.Origin,'clone origin');Equal(source.GetTransformation(),copy.GetTransformation(),'clone axes');
    copy.Elevation=-9;Near(44.125,source.Elevation,'source elevation isolation');Equal(source.Origin,copy.Origin,'elevation must not move origin');
  });
}
