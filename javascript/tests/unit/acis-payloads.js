import test from 'node:test';
import assert from 'node:assert/strict';
import { Body, Region, Solid3D, AcisSatChunk, Matrix3, Matrix4, Vector3, XData, ApplicationRegistry, XDataRecord, XDataCode } from '../../index.js';
import { NotSupportedException, ArgumentException } from '../../runtime/Errors.js';
for(const Type of [Body,Region,Solid3D]) {
  test(Type.name+' publishes immutable independently retained snapshots',()=>{
    const e=new Type(),parts=[new AcisSatChunk(1,'^'),new AcisSatChunk(3,' ')];e.SetEncodedSatChunks(parts);
    const old=e.EncodedSatChunks,lines=e.SatLines;parts.length=0;assert.deepEqual([...lines],['A']);
    assert.throws(()=>{old.get_Item(0).Text='changed';},TypeError);e.SetSatLines(['next']);assert.equal(old.Count,2);assert.deepEqual([...lines],['A']);
  });
  test(Type.name+' exact transform guards preserve payload and complete bottom row',()=>{
    const e=new Type();e.SetSatLines(['unchanged']);const before=e.EncodedSatChunks;
    e.TransformBy(Matrix3.Identity,Vector3.Zero);e.TransformBy(Matrix4.Identity);
    for(const [key,value] of [['M41',Number.MIN_VALUE],['M44',0],['M14',1e-30],['M11',NaN]]){
      const m=Matrix4.Identity;m[key]=value;assert.throws(()=>e.TransformBy(m),NotSupportedException);assert.equal(e.EncodedSatChunks,before);
    }
  });
  test(Type.name+' clone isolates appearance XData and proxy data without retaining identities',()=>{
    const e=new Type();e.SetSatLines(['A','opaque']);e.Handle='ABC';e.AddReactor(e);e.ProxyGraphics=Uint8Array.of(1,2);e.ColorName='Book';
    const xd=new XData(new ApplicationRegistry('SAT'));xd.XDataRecord.Add(new XDataRecord(XDataCode.String,'original'));e.XData.Add(xd);
    const c=e.Clone();assert.equal(c.Handle,null);assert.equal(c.Reactors.Count,0);assert.notEqual(c.Layer,e.Layer);assert.notEqual(c.EncodedSatChunks,e.EncodedSatChunks);
    c.SetSatLines(['changed']);c.XData.get_Item('SAT').XDataRecord.Clear();assert.deepEqual([...e.SatLines],['A','opaque']);assert.equal(xd.XDataRecord.Count,1);assert.equal(c.ColorName,'Book');
  });
  test(Type.name+' rejects a malformed escape across chunks transactionally',()=>{
    const e=new Type();e.SetSatLines(['baseline']);const before=e.EncodedSatChunks;
    assert.throws(()=>e.SetEncodedSatChunks([new AcisSatChunk(1,'^'),new AcisSatChunk(3,'x')]),ArgumentException);assert.equal(e.EncodedSatChunks,before);
  });
}
