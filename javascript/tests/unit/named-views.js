import test from 'node:test';
import assert from 'node:assert/strict';
import { View,VPort,ViewUcs,UCS,DxfSun,DxfVersion,Vector2,Vector3,XData,XDataRecord,XDataCode,ApplicationRegistry } from '../../index.js';
import { ArgumentException,ArgumentNullException,ArgumentOutOfRangeException,InvalidOperationException,NotSupportedException } from '../../runtime/Errors.js';
const xyz = v => [v.X,v.Y,v.Z];

test('view and viewport defaults are fresh and vector property reads have value semantics',()=>{
 for(const Type of [View,VPort]){
  const a=new Type('A'),b=new Type('B'),key=Type===View?'ViewCenter':'GridSpacing',before=a[key];a[key].X=999;
  assert.deepEqual(a[key],before);const v=new Vector2(2,3);a[key]=v;v.Y=999;assert.equal(a[key].Y,3);assert.notDeepEqual(a[key],b[key]);
 }
 const a=new ViewUcs(),b=new ViewUcs();a.Origin.X=99;assert.deepEqual(xyz(a.Origin),[0,0,0]);a.Origin=new Vector3(1,2,3);assert.deepEqual(xyz(b.Origin),[0,0,0]);
});
test('view aliases retain property-specific argument names and no angle normalization',()=>{
 const v=new View('V');v.Rotation=-450;assert.equal(v.Rotation,-450);v.Camera=new Vector3(3,4,0);assert.deepEqual(xyz(v.ViewDirection),[3,4,0]);
 v.Fov=35;assert.equal(v.LensLength,35);v.Viewmode=-32768;assert.equal(v.ViewMode,-32768);
 assert.throws(()=>{v.Fov=NaN;},{name:'ArgumentOutOfRangeException',ParamName:'LensLength'});
 assert.throws(()=>{v.Camera=new Vector3(NaN,0,0);},{name:'ArgumentOutOfRangeException',ParamName:'ViewDirection'});
 assert.throws(()=>{v.Camera=Vector3.Zero;},{name:'ArgumentException',ParamName:'value'});
 assert.throws(()=>{v.ViewMode=32768;},ArgumentOutOfRangeException);assert.equal(v.ViewMode,-32768);
});
test('view direction and UCS axes accept tiny nonzero vectors without normalizing them',()=>{
 for(const [owner,member] of [[new View('V'),'ViewDirection'],[new VPort('P'),'UcsXAxis'],[new ViewUcs(),'YAxis']]){
  owner[member]=new Vector3(Number.MIN_VALUE,0,0);assert.equal(owner[member].X,Number.MIN_VALUE);
  assert.throws(()=>{owner[member]=Vector3.Zero;},ArgumentException);assert.equal(owner[member].X,Number.MIN_VALUE);
 }
});
test('viewport identity differs from name equality and remains stable across rename',()=>{
 const a=new VPort('A'),b=new VPort('a'),hash=a.GetHashCode();assert.equal(a.Equals(b),false);assert.equal(a.Equals(a),true);assert.equal(a.CompareTo(b),0);
 a.Name='OTHER';assert.equal(a.GetHashCode(),hash);assert.notEqual(a.GetHashCode(),b.GetHashCode());assert.equal(a.Clone().Equals(a),false);
 assert.equal(new View('SAME').Equals(new View('same')),true);
});
test('active viewport configuration is independently constructed and has a reserved name',()=>{
 const a=VPort.Active,b=VPort.Active;assert.notEqual(a,b);assert.equal(a.Name,'*Active');assert.equal(a.IsReserved,true);assert.equal(a.Equals(b),false);
 assert.equal(new VPort(' *aCtIvE ').Name,'*aCtIvE');assert.throws(()=>{a.Name='New';},ArgumentException);
 for(const name of [null,'',' \t\r\n'])assert.throws(()=>new VPort(name),ArgumentNullException);
});
test('view UCS bundle assignment enforces exclusive owner and supports detach/reuse',()=>{
 const a=new View('A'),b=new View('B'),bundle=new ViewUcs();a.Ucs=bundle;assert.equal(bundle.View,a);a.Ucs=bundle;
 assert.throws(()=>{b.Ucs=bundle;},ArgumentException);assert.equal(b.Ucs,null);assert.equal(bundle.View,a);
 a.Ucs=null;assert.equal(bundle.View,null);b.Ucs=bundle;assert.equal(bundle.View,b);assert.equal(a.Ucs,null);
});
test('view cloning owns its UCS bundle but retains named/base UCS identity',()=>{
 const a=new View('A'),named=new UCS('N'),base=new UCS('B');a.Ucs=Object.assign(new ViewUcs(),{NamedUcs:named,BaseUcs:base,OrthographicType:1});
 const b=a.Clone('COPY');assert.notEqual(b.Ucs,a.Ucs);assert.equal(a.Ucs.View,a);assert.equal(b.Ucs.View,b);
 assert.equal(b.Ucs.NamedUcs,named);assert.equal(b.Ucs.BaseUcs,base);b.Ucs.Origin=new Vector3(9,8,7);assert.deepEqual(xyz(a.Ucs.Origin),[0,0,0]);
 const detached=a.Ucs.Clone();assert.equal(detached.View,null);assert.equal(detached.NamedUcs,named);
});
test('base UCS relationship validation is explicitly deferred, not an invented setter guard',()=>{
 const bundle=new ViewUcs();bundle.BaseUcs=new UCS('B');assert.equal(bundle.OrthographicType,0);assert.throws(()=>bundle.Validate(),InvalidOperationException);
 assert.equal(bundle.Clone().BaseUcs,bundle.BaseUcs);bundle.OrthographicType=1;assert.doesNotThrow(()=>bundle.Validate());
});
test('view and viewport clones reject owned SUN before validating clone names',()=>{
 for(const Type of [View,VPort]){
  const p=new Type('P');p.SunHandlePresent=true;const q=p.Clone();assert.equal(q.Sun,null);assert.equal(q.SunHandlePresent,true);
  p.Sun=new DxfSun();assert.throws(()=>p.Clone('invalid/name'),NotSupportedException);assert.throws(()=>p.Clone(),NotSupportedException);
  p.Sun=null;assert.throws(()=>p.Clone('invalid/name'),ArgumentException);
 }
});
test('live-section metadata distinguishes omitted from explicitly null and clones independently',()=>{
 const view=new View('V');assert.equal(view.LiveSection,null);assert.equal(view.HasStoredLiveSection,false);view.LiveSection=null;
 assert.equal(view.HasStoredLiveSection,true);const copy=view.Clone();assert.equal(copy.HasStoredLiveSection,true);
 copy.ClearLiveSectionReference();assert.equal(copy.HasStoredLiveSection,false);assert.equal(view.HasStoredLiveSection,true);
});
test('live-section internal validation checks version before registration and preserves state',()=>{
 // Host-contract test only: it does not simulate a qualified typed document or Section implementation.
 const view=new View('V'),old={DrawingVariables:{AcadVer:DxfVersion.AutoCad2004}};view.Owner={Owner:old};
 assert.throws(()=>{view.LiveSection=null;},NotSupportedException);assert.equal(view.HasStoredLiveSection,false);
 const current={DrawingVariables:{AcadVer:DxfVersion.AutoCad2018}};view.Owner={Owner:current};view.LiveSection=null;assert.equal(view.HasStoredLiveSection,true);
 const target={IsErased:true,Handle:null};assert.throws(()=>{view.LiveSection=target;},{name:'ArgumentException',ParamName:'value'});assert.equal(view.LiveSection,null);
});
test('renames re-read the viewport owner after callbacks and never commit after a thrown event',()=>{
 // Structural host ordering, separate from registered VPorts and DxfDocument qualification.
 const calls=[],a={ValidateRecordRename:v=>calls.push('a-check'),CommitRecordRename:()=>calls.push('a-commit')},b={ValidateRecordRename:v=>calls.push('b-check'),CommitRecordRename:()=>calls.push('b-commit')};
 const port=new VPort('P');port.Owner=a;const move=()=>{port.Owner=b;};port.NameChanged.Add(move);port.Name='NEW';assert.deepEqual(calls,['a-check','b-check','b-commit']);
 port.NameChanged.Remove(move);calls.length=0;port.NameChanged.Add(()=>{throw new InvalidOperationException();});assert.throws(()=>{port.Name='FAIL';},InvalidOperationException);assert.deepEqual(calls,['b-check']);assert.equal(port.Name,'NEW');
});
test('view bundle validation checks new target registration before detaching the old bundle',()=>{
 // Structural host test, not an original registered-document test.
 const view=new View('V'),old=new ViewUcs();view.Ucs=old;
 const table={References:new Map()},document={CodeName:'DOCUMENT',UCSs:table,GetObjectByHandle:()=>null};table.Owner=document;view.Owner={Owner:document};
 const proposed=Object.assign(new ViewUcs(),{NamedUcs:new UCS('FOREIGN')});assert.throws(()=>{view.Ucs=proposed;},ArgumentException);
 assert.equal(view.Ucs,old);assert.equal(old.View,view);assert.equal(proposed.View,null);
});
test('view and viewport copies detach handles and independently clone XData binary payloads',()=>{
 for(const Type of [View,VPort]){
  const v=new Type('V'),data=new XData(new ApplicationRegistry('APP'));data.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData,Uint8Array.of(1,2,3)));v.XData.Add(data);v.Handle='ABC';
  const q=v.Clone('Q');assert.equal(q.Owner,null);assert.equal(q.Handle,null);q.XData.get_Item('APP').XDataRecord.Clear();assert.equal(data.XDataRecord.Count,1);
 }
});
