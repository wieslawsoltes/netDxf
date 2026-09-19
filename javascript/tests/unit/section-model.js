import test from 'node:test';
import assert from 'node:assert/strict';
import { Section, View, Vector3, Matrix3, Matrix4, DxfVersion, DxfPlaceholder, Line, XData, ApplicationRegistry, XDataRecord, XDataCode } from '../../index.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
const data=handle=>{const x=new XData(new ApplicationRegistry('SECTION'));x.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle,handle));return x;};
test('SECTION aliases and independent indicators retain stored distinctions',()=>{
  assert.equal(new Section().CodeName,'SECTIONOBJECT');assert.equal(new Section('SECTION').CodeName,'SECTION');
  assert.throws(()=>new Section('section'),ArgumentException);
  const s=new Section();s.State=-2147483648;s.Flags=2147483647;s.IndicatorTransparency=-32768;s.StoredIndicatorColor=0;s.StoredNativeIndicatorColor=256;s.IndicatorColorName='';
  const q=s.Clone();assert.equal(q.State,-2147483648);assert.equal(q.Flags,2147483647);assert.equal(q.IndicatorTransparency,-32768);
  assert.equal(q.StoredIndicatorColor,0);assert.equal(q.StoredNativeIndicatorColor,256);assert.equal(q.IndicatorColorName,'');
  q.StoredIndicatorColor=null;assert.equal(s.StoredIndicatorColor,0);q.HasSettingsField=false;assert.equal(q.Clone().HasStoredGeometrySettings,false);
});
test('SECTION collections copy coordinates on every input/output path and reject edits atomically',()=>{
  const s=new Section();
  for(const list of [s.Vertices,s.BackLineVertices]){
    const v=new Vector3(1,2,3);list.Add(v);v.X=99;list.get_Item(0).Y=99;
    for(const item of list)item.Z=99;assert.deepEqual([...list].map(v=>[v.X,v.Y,v.Z]),[[1,2,3]]);
    assert.throws(()=>list.Insert(-1,new Vector3(NaN,0,0)),{name:'ArgumentOutOfRangeException',ParamName:'index'});
    assert.throws(()=>list.set_Item(1,new Vector3(NaN,0,0)),{name:'ArgumentOutOfRangeException',ParamName:'index'});
    assert.throws(()=>list.set_Item(0,new Vector3(NaN,0,0)),{name:'ArgumentOutOfRangeException',ParamName:'value'});
    assert.equal(list.Count,1);assert.equal(list.get_Item(0).X,1);
    const iterator=list.GetEnumerator();iterator.MoveNext();list.Add(Vector3.Zero);assert.throws(()=>iterator.MoveNext(),InvalidOperationException);
  }
});
test('SECTION string and finite value guards do not normalize stored data',()=>{
  const s=new Section();s.VerticalDirection=new Vector3(2,3,4);assert.equal(s.VerticalDirection.Modulus(),Math.sqrt(29));
  s.Name='';s.IndicatorColorName=null;s.TopHeight=-0;s.BottomHeight=-12.5;
  for(const value of [NaN,Infinity,-Infinity])assert.throws(()=>{s.TopHeight=value;},ArgumentOutOfRangeException);
  for(const name of ['bad\n','bad\r','bad\0','\ud800','\udc00'])assert.throws(()=>{s.Name=name;},ArgumentException);
  assert.throws(()=>{s.Name=null;},ArgumentNullException);assert.equal(s.Name,'');assert.ok(Object.is(s.TopHeight,-0));
});
test('SECTION exact identity protects inert geometry even against a changed homogeneous row',()=>{
  const s=new Section();s.Vertices.Add(new Vector3(1,2,3));const list=s.Vertices;
  s.TransformBy(Matrix3.Identity,Vector3.Zero);s.TransformBy(Matrix4.Identity);
  for(const key of ['M41','M42','M43','M44']){const m=Matrix4.Identity;m[key]=Number.MIN_VALUE;assert.throws(()=>s.TransformBy(m),NotSupportedException);}
  assert.throws(()=>s.TransformBy(Matrix3.Identity,new Vector3(0,0,Number.MIN_VALUE)),NotSupportedException);assert.equal(s.Vertices,list);assert.equal(list.get_Item(0).X,1);
});
test('SECTION shallow graph cloning refuses unresolved ownership rather than silently discarding it',()=>{
  const s=new Section();s.GeometrySettings=new DxfPlaceholder();assert.throws(()=>s.Clone(),NotSupportedException);s.GeometrySettings=null;
  s.ExtensionDictionary=new DxfPlaceholder();assert.throws(()=>s.Clone(),NotSupportedException);s.ExtensionDictionary=null;
  const reactor=new Line();s.AddReactor(reactor);assert.throws(()=>s.Clone(),NotSupportedException);s.RemoveReactor(reactor);
  s.XData.Add(data('0000'));assert.doesNotThrow(()=>s.Clone());s.XData.Clear();s.XData.Add(data('AB'));assert.throws(()=>s.Clone(),NotSupportedException);
  s.IsErased=true;assert.throws(()=>s.Clone(),InvalidOperationException);
});
test('SECTION internal structural reference validation preserves version pending and reciprocal order',()=>{
  const section=new Section(),registered=new Set(),objects={IsRegistered:item=>registered.has(item)},document={DrawingVariables:{AcadVer:DxfVersion.AutoCad2007},Objects:objects,GetObjectByHandle:h=>h==='AB'?section:null};
  section.GeometrySettings=new DxfPlaceholder();section.PendingInputReferences=true;assert.doesNotThrow(()=>section.Validate(document));
  document.DrawingVariables.AcadVer=DxfVersion.AutoCad2004;assert.throws(()=>section.Validate(document),NotSupportedException);document.DrawingVariables.AcadVer=DxfVersion.AutoCad2007;
  section.PendingInputReferences=false;assert.throws(()=>section.Validate(document),InvalidOperationException);section.GeometrySettings.Database=objects;section.GeometrySettings.Owner=section;assert.doesNotThrow(()=>section.Validate(document));
  const reactor=new Line();section.AddReactor(reactor);assert.throws(()=>section.Validate(document),InvalidOperationException);registered.add(reactor);assert.doesNotThrow(()=>section.Validate(document));
  section.IsErased=true;assert.throws(()=>section.Validate(document),InvalidOperationException);
});
test('SECTION value clones detach identity and copy vertices common metadata and XData',()=>{
  const s=new Section('SECTION');s.Name='Original';s.Vertices.Add(new Vector3(1,2,3));s.Handle='AB';s.ProxyGraphics=Uint8Array.of(9);s.XData.Add(data('0'));
  const q=s.Clone();q.Vertices.set_Item(0,Vector3.Zero);q.XData.get_Item('SECTION').XDataRecord.Clear();assert.equal(s.Vertices.get_Item(0).X,1);assert.equal(s.XData.get_Item('SECTION').XDataRecord.Count,1);
  assert.equal(q.Handle,null);assert.equal(q.Owner,null);assert.notEqual(q.Layer,s.Layer);assert.deepEqual([...q.ProxyGraphics],[9]);
  const shared=s.CloneValues(false);assert.equal(shared.Layer,s.Layer);assert.equal(shared.Linetype,s.Linetype);assert.notEqual(shared.Color,s.Color);
});
test('named views keep SECTION references by identity without owning or cloning the section',()=>{
  const section=new Section(),view=new View('VIEW');view.LiveSection=section;const clone=view.Clone('COPY');assert.equal(clone.LiveSection,section);assert.equal(section.Owner,null);
  section.IsErased=true;assert.throws(()=>{view.LiveSection=section;},ArgumentException);assert.equal(view.LiveSection,section);
  view.ClearLiveSectionReference();assert.equal(view.HasStoredLiveSection,false);assert.equal(clone.LiveSection,section);
});
