import test from 'node:test';
import assert from 'node:assert/strict';
import { Light, LightType, LightAttenuationType, LightShadowType, DxfLightList, DxfLightListEntry,
  Point, Vector3, Matrix3, Matrix4, XData, XDataRecord, XDataCode, ApplicationRegistry } from '../../index.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidOperationException } from '../../runtime/Errors.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
const xyz=v=>[v.X,v.Y,v.Z];
const state=l=>[xyz(l.Position),xyz(l.Target),l.AttenuationStartLimit,l.AttenuationEndLimit];
const authored=()=>{const l=new Light();l.Position=new Vector3(1,-2,3);l.Target=new Vector3(4,5,-6);l.AttenuationStartLimit=3;l.AttenuationEndLimit=2;return l;};

test('light defaults keep glyph visibility and lighting/shadow settings independent',()=>{
 const a=new Light(),b=new Light();assert.equal(a.LightType,LightType.Distant);assert.equal(a.AttenuationType,LightAttenuationType.InverseSquare);
 assert.equal(a.ShadowType,LightShadowType.RayTraced);assert.equal(a.Intensity,1);assert.equal(a.HotspotAngle,45);assert.equal(a.FalloffAngle,90);
 assert.equal(a.IsOn,true);assert.equal(a.CastShadows,true);assert.equal(a.PlotGlyph,false);assert.equal(a.UseAttenuationLimits,false);
 assert.equal(a.ShadowMapSize,512);assert.equal(a.ShadowMapSoftness,1);assert.deepEqual(xyz(a.Target),[0,0,1]);
 a.IsVisible=false;assert.equal(a.IsOn,true);a.CastShadows=false;assert.equal(b.CastShadows,true);
});
test('LIGHT names retain malformed code units but LIGHTLIST entry names reject them',()=>{
 const light=new Light();for(const name of ['','A\tB','日本😀','a\\U+0041','\ud800','\udc00']){light.Name=name;assert.equal(light.Clone().Name,name);}
 for(const name of ['\ud800','\udc00'])assert.throws(()=>new DxfLightListEntry(light,name),ArgumentException);
 assert.throws(()=>{light.Name=null;},ArgumentNullException);light.Name='original';
 for(const name of ['A\0B','A\rB','A\nB']){assert.throws(()=>{light.Name=name;},ArgumentException);assert.equal(light.Name,'original');}
});
test('light validators retain dormant values and signed zero without normalizing angles',()=>{
 const l=new Light();l.AttenuationStartLimit=100;l.AttenuationEndLimit=2;l.HotspotAngle=720;l.FalloffAngle=10;
 assert.equal(l.AttenuationEndLimit,2);assert.equal(l.FalloffAngle,10);assert.equal(l.HotspotAngle,720);
 for(const key of ['Intensity','AttenuationStartLimit','AttenuationEndLimit','HotspotAngle','FalloffAngle']){
  l[key]=-0;for(const value of [NaN,Infinity,-Infinity,-Number.MIN_VALUE,-1])assert.throws(()=>{l[key]=value;},ArgumentOutOfRangeException);
  assert.ok(Object.is(l[key],-0));assert.ok(Object.is(l.Clone()[key],-0));
 }
 l.ShadowMapSize=0;l.ShadowMapSoftness=0;assert.equal(l.Clone().ShadowMapSize,0);
});
test('light enum and integer checks are atomic and finite point coordinates are copied',()=>{
 const l=new Light();for(const [key,values] of [['LightType',[0,4]],['AttenuationType',[-1,3]],['ShadowType',[-1,2]],['VersionNumber',[-1,2147483648]],['ShadowMapSoftness',[-1,32768]]]){
  const before=l[key];for(const value of values)assert.throws(()=>{l[key]=value;},ArgumentOutOfRangeException);assert.equal(l[key],before);
 }
 const p=new Vector3(2,3,4);l.Position=p;p.X=9;l.Position.Y=9;assert.deepEqual(xyz(l.Position),[2,3,4]);
 assert.throws(()=>{l.Target=new Vector3(0,Infinity,0);},ArgumentOutOfRangeException);assert.deepEqual(xyz(l.Target),[0,0,1]);
});
test('light similarity transformations reflect points and scale attenuation without touching authored fields',()=>{
 const l=authored();l.Normal=new Vector3(0,1,0);l.Intensity=7;l.ProxyGraphics=Uint8Array.of(1,2);
 l.TransformBy(new Matrix3(-2,0,0,0,2,0,0,0,2),new Vector3(3,4,5));
 assert.deepEqual(state(l),[[1,0,11],[-5,14,-7],6,4]);assert.equal(l.Intensity,7);assert.deepEqual(xyz(l.Normal),[0,1,0]);assert.deepEqual([...l.ProxyGraphics],[1,2]);
});
test('light rejects singular, sheared and nonuniform frames and invalid translations atomically',()=>{
 const l=authored(),before=state(l);
 for(const m of [Matrix3.Scale(0),new Matrix3(2,0,0,0,2,0,0,0,3),new Matrix3(1,0.5,0,0,1,0,0,0,1)]){
  assert.throws(()=>l.TransformBy(m,Vector3.Zero),{name:'ArgumentException',ParamName:'transformation'});assert.deepEqual(state(l),before);
 }
 assert.throws(()=>l.TransformBy(Matrix3.Scale(0),new Vector3(NaN,0,0)),{name:'ArgumentOutOfRangeException',ParamName:'translation'});assert.deepEqual(state(l),before);
});
test('light transformation validates every computed result before applying any position or distance',()=>{
 for(const key of ['Position','Target','AttenuationStartLimit','AttenuationEndLimit']){
  const l=authored();l[key]=key==='Position'||key==='Target'?new Vector3(Number.MAX_VALUE,0,0):Number.MAX_VALUE;const before=state(l);
  assert.throws(()=>l.TransformBy(Matrix3.Scale(2),new Vector3(3,4,5)),{name:'ArgumentOutOfRangeException',ParamName:'transformation'});assert.deepEqual(state(l),before);
 }
});
test('stable light length admits tiny finite similarity transforms and the base matrix4 adapter',()=>{
 const l=new Light();l.Target=Vector3.UnitX;l.AttenuationStartLimit=1;
 l.TransformBy(Matrix3.Scale(Number.MIN_VALUE),Vector3.Zero);assert.equal(l.Target.X,Number.MIN_VALUE);assert.equal(l.AttenuationStartLimit,Number.MIN_VALUE);
 const m=authored();m.TransformBy(new Matrix4(0,-2,0,3,2,0,0,4,0,0,-2,5,9,8,7,6));assert.deepEqual(state(m),[[7,6,-1],[-7,12,17],6,4]);
});
test('light clone owns appearance XData and metadata and does not copy document identity',()=>{
 const l=authored();l.Name='label';l.VersionNumber=3;l.Handle='AB';l.AddReactor(l);l.Owner={CodeName:'BLOCK'};l.ProxyGraphics=Uint8Array.of(9,8);
 const data=new XData(new ApplicationRegistry('APP'));data.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData,Uint8Array.of(1,2)));l.XData.Add(data);
 const copy=l.Clone();assert.equal(copy.Owner,null);assert.equal(copy.Handle,null);assert.equal(copy.Reactors.Count,0);assert.deepEqual(state(copy),state(l));
 for(const key of ['Layer','Linetype','Color','Transparency'])assert.notEqual(copy[key],l[key]);copy.XData.get_Item('APP').XDataRecord.Clear();assert.equal(data.XDataRecord.Count,1);
 copy.ProxyGraphics=Uint8Array.of(7);assert.equal(l.ProxyGraphics[0],9);
});
test('LIGHTLIST keeps raw version, duplicate identity, independent aliases and immutable entry data',()=>{
 const a=new Light(),b=new Light(),list=new DxfLightList(-2147483648);list.StoredVersion=2147483647;
 list.Entries.AddRange([new DxfLightListEntry(a,'alias'),new DxfLightListEntry(b,''),new DxfLightListEntry(a,'duplicate')]);
 a.Name='actual';assert.deepEqual([...list.DatabaseReferences],[a,b,a]);assert.equal(list.Entries.get_Item(0).Name,'alias');
 assert.throws(()=>{list.Entries.get_Item(0).Name='other';},TypeError);assert.equal(list.CloneShell().StoredVersion,2147483647);assert.equal(list.CloneShell().Entries.Count,0);
 assert.throws(()=>new DxfLightListEntry(null,null),{name:'ArgumentNullException',ParamName:'light'});
});
test('LIGHTLIST checks collection indexes before entries and failed edits preserve iterator validity',()=>{
 const l=new Light(),list=new DxfLightList(0),e=new DxfLightListEntry(l,'x');list.Entries.Add(e);const it=list.Entries.GetEnumerator();it.MoveNext();
 assert.throws(()=>list.Entries.Insert(-1,null),{name:'ArgumentOutOfRangeException',ParamName:'index'});
 assert.throws(()=>list.Entries.set_Item(0,null),{name:'ArgumentNullException',ParamName:'entry'});assert.equal(it.MoveNext(),false);assert.equal(list.Entries.get_Item(0),e);
});
test('LIGHTLIST copy validates mapped LIGHT types without hiding partial progress on later failure',()=>{
 const a=new Light(),b=new Light(),mapped=new Light(),list=new DxfLightList(42);list.Entries.AddRange([new DxfLightListEntry(a,'A'),new DxfLightListEntry(b,'B')]);
 const copy=list.CloneShell();assert.throws(()=>list.CopyDatabaseReferencesTo(copy,v=>v===a?mapped:new Point()),{name:'ArgumentException',ParamName:null});
 assert.equal(copy.Entries.Count,1);assert.equal(copy.Entries.get_Item(0).Light,mapped);assert.equal(list.Entries.Count,2);
});
test('LIGHTLIST structural host checks registration and version without implying a document implementation',()=>{
 const l=new Light(),list=new DxfLightList(1),seen=[];list.Database={CheckRegistered(v){seen.push(v);throw new InvalidOperationException();}};
 assert.throws(()=>list.Entries.Add(new DxfLightListEntry(l,'name')),InvalidOperationException);assert.deepEqual(seen,[l]);assert.equal(list.Entries.Count,0);
 for(const version of [14,15,18]){const errors=new ReferenceList();list.ValidateDatabaseSchema({Document:{DrawingVariables:{AcadVer:version}}},errors);assert.equal(errors.Count,version<15?1:0);}
});
