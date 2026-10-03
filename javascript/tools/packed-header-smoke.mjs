// Executed from the offline-installed tarball, after all existing core smoke checks.
import assert from 'node:assert/strict';
import { userInfo } from 'node:os';
import { HeaderVariables, HeaderVariable, HeaderDateTime, HeaderTimeSpan,
  LinearUnitType, DrawingUnits } from '@netdxf/javascript';
import { BoxedScalar } from '@netdxf/javascript/runtime/BoxedScalar.js';
{
  const clock = new HeaderDateTime(638400000000000123n, 2);
  const header = new HeaderVariables({UserName:()=> 'PACKED', Now:()=>clock, UtcNow:()=>new HeaderDateTime(clock.Ticks,1)});
  assert.equal(header.KnownNames().Count,40);
  assert.equal(header.TdCreate.Ticks,638400000000000123n);
  assert.equal(header.TduCreate.Kind,1);
  assert.equal(header.LastSavedBy,'PACKED');
  header.LUnits=LinearUnitType.Architectural;
  assert.equal(header.InsUnits,DrawingUnits.Inches);
  header.TdinDwg=new HeaderTimeSpan(-1n);
  assert.equal(header.TdinDwg.ToString(),'-00:00:00.0000001');
  const variable=new HeaderVariable('$PACKED',70,new BoxedScalar('Int16',12));
  header.AddCustomVariable(variable);
  const lookup={value:null};assert.equal(header.TryGetCustomVariable('$packed',lookup),true);
  assert.equal(lookup.value,variable);
  header.CustomValues().get_Item(0).Value=false;
  assert.equal(variable.ToString(),'$PACKED:False');
  const entry=Array.from(header.KnownValues()).find(v=>v.Name==='$TEXTSIZE');
  entry.Value=3.75;assert.equal(header.TextSize,3.75);
  header.ClearCustomVariables();assert.equal(header.CustomNames().Count,0);
}
// Importing the explicit Node entry supplies host identity, without a package dependency.
const nodeHeaders=await import('@netdxf/javascript/node');
assert.equal(new nodeHeaders.HeaderVariables().LastSavedBy,userInfo().username);
