import test from 'node:test';
import assert from 'node:assert/strict';
import { OleFrame, Ole2Frame, Ole2FrameMetadataFields, Vector3, Matrix3, Matrix4, XData, ApplicationRegistry, XDataRecord, XDataCode } from '../../index.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, NotSupportedException } from '../../runtime/Errors.js';
const make=(bytes=Uint8Array.of(1,2,255))=>new Ole2Frame(bytes,new Vector3(1,2,3),new Vector3(4,5,6),'dormant',7,3,1);
for(const [name,create] of [['OleFrame',data=>new OleFrame(data,7)],['Ole2Frame',make]]){
  test(name+' defensive binary snapshots and detached clone state',()=>{
    const input=Uint8Array.of(1,2,255),e=create(input);input[0]=99;const result=e.GetBinaryData();result[1]=77;assert.deepEqual([...e.GetBinaryData()],[1,2,255]);
    e.Handle='AB';e.AddReactor(e);e.ProxyGraphics=Uint8Array.of(4);const x=new XData(new ApplicationRegistry('OLE'));x.XDataRecord.Add(new XDataRecord(XDataCode.String,'data'));e.XData.Add(x);
    const q=e.Clone();assert.equal(q.Handle,null);assert.equal(q.Reactors.Count,0);assert.notEqual(q.Layer,e.Layer);assert.notEqual(q.XData.get_Item('OLE'),x);assert.notEqual(q.BinaryData,e.BinaryData);assert.deepEqual([...q.ProxyGraphics],[4]);
  });
  test(name+' honors exact affine identity and inherited Matrix4 last-row semantics',()=>{
    const e=create(new Uint8Array());e.TransformBy(Matrix3.Identity,Vector3.Zero);const m=Matrix4.Identity;m.M41=5;m.M44=0;e.TransformBy(m);
    const t=Matrix3.Identity;t.M12=Number.MIN_VALUE;assert.throws(()=>e.TransformBy(t,Vector3.Zero),NotSupportedException);
    assert.throws(()=>e.TransformBy(Matrix3.Identity,new Vector3(0,1e-30,0)),NotSupportedException);
    assert.equal(e.BinaryDataLength,0);
  });
  test(name+' immutable metadata and explicit internal borrowed-storage adapter',()=>{
    const data=Uint8Array.of(1,2),e=create(data);assert.throws(()=>{e.OleVersion=2;},TypeError);assert.throws(()=>{e.BinaryDataLength=5;},TypeError);
    const internal=name==='OleFrame'?new OleFrame(data,7,false,false):new Ole2Frame(data,Vector3.Zero,Vector3.UnitX,'x',2,2,0,false,0);
    data[0]=44;assert.equal(internal.GetBinaryData()[0],44);assert.notEqual(internal.Clone().BinaryData,data);
  });
}
test('all OLE2 metadata presence masks preserve dormant values and independent bytes',()=>{
  const e=make();for(let mask=0;mask<64;mask++){const q=e.WithMetadataFields(mask);assert.equal(q.MetadataFields,mask);assert.equal(q.OleVersion,7);assert.equal(q.Description,'dormant');assert.equal(q.ObjectType,3);assert.equal(q.TileMode,1);assert.equal(q.Clone().MetadataFields,mask);assert.notEqual(q.BinaryData,e.BinaryData);assert.equal(q.WithMetadataFields(63).Description,'dormant');}
  assert.equal(e.MetadataFields,63);
});
test('legacy OLE omission preserves version instead of substituting a load-time default',()=>{
  const e=new OleFrame(Uint8Array.of(1),7),q=e.WithOleVersionPresence(false);assert.equal(q.HasOleVersion,false);assert.equal(q.OleVersion,7);assert.equal(q.Clone().HasOleVersion,false);assert.equal(e.HasOleVersion,true);assert.equal(q.WithOleVersionPresence(true).OleVersion,7);
});
test('OLE2 validates source parameters in order and copies corner values',()=>{
  const bad=new Vector3(Infinity,0,0);assert.throws(()=>new Ole2Frame(null,bad,bad,null),{name:'ArgumentNullException',ParamName:'binaryData'});
  assert.throws(()=>new Ole2Frame(new Uint8Array(),bad,bad,null),{name:'ArgumentNullException',ParamName:'description'});
  assert.throws(()=>new Ole2Frame(new Uint8Array(),bad,bad,''),{name:'ArgumentOutOfRangeException',ParamName:'upperLeftCorner'});
  const upper=new Vector3(1,2,3),lower=new Vector3(4,5,6),e=new Ole2Frame(new Uint8Array(),upper,lower);upper.X=9;lower.Y=9;e.UpperLeftCorner.Z=9;assert.equal(e.UpperLeftCorner.X,1);assert.equal(e.LowerRightCorner.Y,5);assert.equal(e.UpperLeftCorner.Z,3);
});
test('OLE2 description only rejects transport delimiters, not arbitrary UTF-16 code units',()=>{
  for(const text of ['a\rb','a\nb','a\0b'])assert.throws(()=>new Ole2Frame(new Uint8Array(),Vector3.Zero,Vector3.UnitX,text),ArgumentException);
  const e=new Ole2Frame(new Uint8Array(),Vector3.Zero,Vector3.UnitX,'\ud800');assert.equal(e.Clone().Description,'\ud800');
});
