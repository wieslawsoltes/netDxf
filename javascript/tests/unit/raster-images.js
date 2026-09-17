import test from 'node:test';
import assert from 'node:assert/strict';
import { Image, ImageDefinition, ImageDefinitionReactor, ImageResolutionUnits, Vector2, Vector3, Matrix3,
  Wipeout, ClippingBoundary, ClippingBoundaryType, XData, XDataCode, XDataRecord, ApplicationRegistry } from '../../index.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidOperationException, NullReferenceException } from '../../runtime/Errors.js';
import { SetSupportFileSystem } from '../../runtime/SupportFileSystem.js';
import { doubleBits, fromBits } from '../../tools/wire.mjs';
const def=(name='D',width=16,height=8)=>new ImageDefinition(name,'not-opened.png',width,96,height,120,ImageResolutionUnits.Inches);
const image=()=>new Image(def(),new Vector3(1,2,3),4,2);
const xy=v=>[v.X,v.Y];

test('image definitions use explicit metadata without opening or decoding files',()=>{
  const previous=SetSupportFileSystem({ReadAllBytes(){throw Error('Unexpected read');},Exists(){throw Error('Unexpected exists');},DirectorySeparators:'/',InvalidPathChars:'\0'});
  try {
    const d=new ImageDefinition('folder/a.png',16,96,8,120,ImageResolutionUnits.Inches);
    assert.equal(d.Name,'a');assert.equal(d.File,'folder/a.png');assert.equal(d.Width,16);assert.equal(d.Height,8);assert.equal(d.HasReferences(),false);assert.equal(d.GetReferences(),null);
    d.File='not-required-to-exist.any';assert.equal(d.Clone().File,d.File);
    assert.throws(()=>{d.File='\0bad';},ArgumentException);assert.equal(d.File,'not-required-to-exist.any');
    d.File='a\0b';assert.equal(d.File,'a\0b');
  } finally {SetSupportFileSystem(previous);}
});
test('image resolution units apply source-directed conversion once for each actual unit change',()=>{
  const d=def();d.ResolutionUnits=ImageResolutionUnits.Centimeters;
  assert.equal(d.HorizontalResolution,96/2.54);assert.equal(d.VerticalResolution,120/2.54);
  const h=d.HorizontalResolution;d.ResolutionUnits=ImageResolutionUnits.Centimeters;assert.equal(d.HorizontalResolution,h);
  d.ResolutionUnits=ImageResolutionUnits.Unitless;assert.equal(d.HorizontalResolution,h);
  d.ResolutionUnits=ImageResolutionUnits.Inches;assert.equal(d.HorizontalResolution,h*2.54);
  d.ResolutionUnits=-1;assert.equal(d.HorizontalResolution,h*2.54);assert.equal(d.Clone().ResolutionUnits,-1);
});
test('image-definition dimension guards are atomic and preserve CLR parameter order',()=>{
  assert.throws(()=>new ImageDefinition('D','file',0,-1,0,-1,0),{name:'ArgumentOutOfRangeException',ParamName:'width'});
  assert.throws(()=>new ImageDefinition('D','file',1,-1,0,-1,0),{name:'ArgumentOutOfRangeException',ParamName:'height'});
  assert.throws(()=>new ImageDefinition('D','file',1,-1,1,-1,0),{name:'ArgumentOutOfRangeException',ParamName:'horizontalResolution'});
  const d=def();for(const member of ['Width','Height']){const before=d[member];for(const v of [0,-1,0.5,Infinity,2147483648])assert.throws(()=>{d[member]=v;},ArgumentOutOfRangeException);assert.equal(d[member],before);}
  for(const member of ['HorizontalResolution','VerticalResolution']){d[member]=Infinity;assert.equal(d[member],Infinity);d[member]=NaN;assert.ok(Number.isNaN(d[member]));}
});
test('image constructor overloads separate drawing dimensions from default pixel clipping bounds',()=>{
  const d=def();for(const p of [new Vector2(1,2),new Vector3(1,2,3)])for(const args of [[d,p,new Vector2(4,2)],[d,p,4,2]]){
    const e=new Image(...args);assert.equal(e.Width,4);assert.equal(e.Height,2);assert.deepEqual(xy(e.ClippingBoundary.Vertexes[1]),[16,8]);
    assert.equal(e.DisplayOptions,7);assert.equal(e.Clipping,false);assert.equal(e.Brightness,50);assert.equal(e.Contrast,50);assert.equal(e.Fade,0);
  }
  assert.throws(()=>new Image(null,Vector3.Zero,4,2),{name:'ArgumentNullException',ParamName:'imageDefinition'});
});
test('image axes copy values, normalize nonunit directions and reject near-zero edits before mutation',()=>{
  const e=image(),axis=new Vector2(3,4);e.Uvector=axis;axis.X=99;e.Uvector.Y=99;assert.deepEqual(xy(e.Uvector),[0.6000000000000001,0.8]);
  for(const member of ['Uvector','Vvector']){const before=xy(e[member]);for(const v of [Vector2.Zero,new Vector2(1e-13,0)])assert.throws(()=>{e[member]=v;},{name:'ArgumentException',ParamName:'value'});assert.deepEqual(xy(e[member]),before);}
});
test('image Rotation rotates the existing axes incrementally rather than replacing their angle',()=>{
  const e=image();e.Rotation=90;assert.ok(Math.abs(e.Rotation-90)<1e-12);e.Rotation=90;assert.ok(Math.abs(e.Rotation-180)<1e-12);
  const before=xy(e.Uvector);e.Rotation=360;assert.deepEqual(xy(e.Uvector),before);
  // Angle values above are supplemental intent checks; the differential corpus compares exact bits.
});
test('definition replacement does not implicitly reset existing pixel clipping',()=>{
  const e=image(),boundary=e.ClippingBoundary;e.Definition=def('NEW',64,32);assert.equal(e.ClippingBoundary,boundary);
  assert.deepEqual(xy(boundary.Vertexes[1]),[16,8]);e.ClippingBoundary=null;assert.notEqual(e.ClippingBoundary,boundary);assert.deepEqual(xy(e.ClippingBoundary.Vertexes[1]),[64,32]);
});
test('image-definition callback substitution and failures preserve reference and boundary order',()=>{
  const e=image(),old=e.Definition,sub=def('SUB');e.ImageDefinitionChanged.Add((sender,args)=>{assert.equal(sender,e);assert.equal(e.Definition,old);args.NewValue=sub;});e.Definition=def('PROPOSED');assert.equal(e.Definition,sub);
  const q=image(),initial=q.Definition;q.ImageDefinitionChanged.Add(()=>{throw new InvalidOperationException();});assert.throws(()=>{q.Definition=sub;},InvalidOperationException);assert.equal(q.Definition,initial);
  const n=image(),boundary=n.ClippingBoundary;n.ImageDefinitionChanged.Add((sender,args)=>{args.NewValue=null;});n.Definition=sub;assert.equal(n.Definition,null);
  assert.throws(()=>{n.ClippingBoundary=null;},NullReferenceException);assert.equal(n.ClippingBoundary,boundary);assert.throws(()=>n.Clone(),NullReferenceException);
});
test('raster display range guards preserve state and unknown display flags survive cloning',()=>{
  const e=image();for(const member of ['Brightness','Contrast','Fade']){e[member]=100;for(const v of [-1,101])assert.throws(()=>{e[member]=v;},ArgumentOutOfRangeException);assert.equal(e[member],100);e[member]=0;}
  e.DisplayOptions=-1;e.Clipping=true;const q=e.Clone();assert.equal(q.DisplayOptions,-1);assert.equal(q.Clipping,true);
});
test('raster and definition cloning isolate metadata XData clipping and entity state',()=>{
  const e=image();for(const target of [e,e.Definition]){const data=new XData(new ApplicationRegistry('APP'));data.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData,Uint8Array.of(1,2)));target.XData.Add(data);}
  e.ProxyGraphics=Uint8Array.of(9,8);e.Handle='AB';e.AddReactor(e);const q=e.Clone();assert.notEqual(q.Definition,e.Definition);assert.notEqual(q.ClippingBoundary,e.ClippingBoundary);
  assert.equal(q.Handle,null);assert.equal(q.Reactors.Count,0);q.Definition.Width=99;assert.equal(e.Definition.Width,16);
  q.Definition.XData.get_Item('APP').XDataRecord.Clear();q.XData.get_Item('APP').XDataRecord.Clear();assert.equal(e.XData.get_Item('APP').XDataRecord.Count,1);assert.equal(e.Definition.XData.get_Item('APP').XDataRecord.Count,1);
  assert.deepEqual([...q.ProxyGraphics],[9,8]);
});
test('singular image transformation retains the source old-U fallback for the V axis',()=>{
  const e=image(),boundary=e.ClippingBoundary;e.TransformBy(Matrix3.Scale(0),new Vector3(3,4,5));assert.deepEqual(xy(e.Uvector),[1,0]);assert.deepEqual(xy(e.Vvector),[1,0]);
  assert.equal(e.Width,1e-12);assert.equal(e.Height,1e-12);assert.equal(e.ClippingBoundary,boundary);assert.deepEqual([e.Position.X,e.Position.Y,e.Position.Z],[3,4,5]);
});
test('wipeout overloads retain rectangular and polygonal data and own clone boundaries',()=>{
  const rectangle=new Wipeout(0,0,3,4),polygon=new Wipeout([Vector2.Zero,Vector2.UnitX,Vector2.UnitY]);assert.equal(rectangle.ClippingBoundary.Type,ClippingBoundaryType.Rectangular);assert.equal(polygon.ClippingBoundary.Type,ClippingBoundaryType.Polygonal);
  assert.throws(()=>new Wipeout(null),{name:'ArgumentNullException',ParamName:'clippingBoundary'});assert.throws(()=>new Wipeout([Vector2.Zero,Vector2.UnitX]),ArgumentOutOfRangeException);
  for(const e of [rectangle,polygon]){const q=e.Clone();assert.notEqual(q.ClippingBoundary,e.ClippingBoundary);assert.throws(()=>{e.ClippingBoundary=null;},ArgumentNullException);}
});
test('wipeout placement transforms clipping coordinates and elevation without aliasing',()=>{
  const boundary=new ClippingBoundary(0,0,3,4),e=new Wipeout(boundary);e.Elevation=7;e.TransformBy(Matrix3.Identity,new Vector3(3,4,5));
  assert.equal(e.Elevation,12);assert.deepEqual(xy(e.ClippingBoundary.Vertexes[0]),[3,4]);assert.deepEqual(xy(boundary.Vertexes[0]),[0,0]);assert.notEqual(e.ClippingBoundary,boundary);
});
test('raster binary scalar fields retain signed payload NaNs through warmed storage paths',()=>{
  const e=image(),d=def(),w=new Wipeout(0,0,3,4),n=fromBits('FFF8000000001234');
  for(let i=0;i<20000;i++){e.Width=1;d.HorizontalResolution=96;w.Elevation=0;e.Width=n;d.HorizontalResolution=n;w.Elevation=n;}
  // Observe each property directly: constructing a generic JS array can itself canonicalize NaNs.
  assert.equal(doubleBits(e.Width),'FFF8000000001234');
  assert.equal(doubleBits(d.HorizontalResolution),'FFF8000000001234');
  assert.equal(doubleBits(w.Elevation),'FFF8000000001234');
});
test('the internal reactor mirror retains immutable image handle data without claiming registration',()=>{
  for(const handle of [null,'','ABCD','not-a-handle']){const r=new ImageDefinitionReactor(handle);assert.equal(r.ImageHandle,handle);assert.equal(r.CodeName,'IMAGEDEF_REACTOR');assert.equal(r.Owner,null);assert.throws(()=>{r.ImageHandle='NEW';},TypeError);}
});
