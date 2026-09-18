import test from 'node:test';
import assert from 'node:assert/strict';
import {Ellipse,Vector2,Vector3,Matrix3,Layer,XData,XDataCode,XDataRecord,ApplicationRegistry} from '../../index.js';
import {ArgumentException,ArgumentOutOfRangeException} from '../../runtime/Errors.js';
import {doubleBits} from '../../tools/wire.mjs';

test('ellipse axis construction validation differs from reordering SetAxis editing',()=>{
 assert.throws(()=>new Ellipse(Vector3.Zero,2,4),ArgumentException);const e=new Ellipse(Vector3.Zero,4,2);e.Rotation=37;e.SetAxis(2,8);assert.equal(e.MajorAxis,8);assert.equal(e.MinorAxis,2);assert.equal(e.Rotation,37);
 for(const args of [[0,2],[2,0],[-1,2],[2,-Infinity]])assert.throws(()=>e.SetAxis(...args),ArgumentOutOfRangeException);assert.equal(e.MajorAxis,8);assert.equal(e.MinorAxis,2);
 assert.throws(()=>{e.MajorAxis=99;},TypeError);
});
test('ellipse sampling returns independent center-relative values with half-axis radii',()=>{
 const c=new Vector3(9,8,7),e=new Ellipse(c,4,2);c.X=99;e.Center.Y=99;const points=e.PolygonalVertexes(4);
 assert.equal(e.Center.X,9);assert.equal(e.Center.Y,8);assert.deepEqual([points.get_Item(0).X,points.get_Item(0).Y],[2,0]);points.get_Item(0).X=99;assert.equal(points.get_Item(0).X,2);
 const poly=e.ToPolyline2D(4);assert.equal(poly.IsClosed,true);assert.equal(poly.Elevation,7);assert.equal(poly.Vertexes.get_Item(0).Position.X,11);assert.equal(poly.Vertexes.get_Item(0).Position.Y,8);
});
test('ellipse closure retains epsilon comparison and partial arcs include both endpoints',()=>{
 const e=new Ellipse(Vector3.Zero,4,2);e.EndAngle=1e-13;assert.equal(e.IsFullEllipse,true);e.EndAngle=90;assert.equal(e.IsFullEllipse,false);
 const points=e.PolygonalVertexes(2);assert.equal(points.Count,2);assert.equal(points.get_Item(0).X,2);assert.equal(points.get_Item(1).Y,1);assert.equal(e.ToPolyline2D(5).IsClosed,false);
});
test('ellipse cloning owns mutable state while conversions preserve the narrower appearance contract',()=>{
 const e=new Ellipse(new Vector2(1,2),4,2);e.Layer=new Layer('E');e.ColorName='Book';e.IsVisible=false;e.ProxyGraphics=Uint8Array.of(1);e.Handle='AB';
 const xd=new XData(new ApplicationRegistry('E'));xd.XDataRecord.Add(new XDataRecord(XDataCode.String,'source'));e.XData.Add(xd);
 const copy=e.Clone(),poly=e.ToPolyline2D(7);copy.SetAxis(9,8);copy.XData.get_Item('E').XDataRecord.Clear();assert.equal(e.MajorAxis,4);assert.equal(xd.XDataRecord.Count,1);assert.notEqual(copy.Layer,e.Layer);
 assert.equal(copy.Handle,null);assert.equal(copy.IsVisible,false);assert.equal(copy.ColorName,'Book');assert.deepEqual([...copy.ProxyGraphics],[1]);assert.equal(poly.IsVisible,true);assert.equal(poly.ColorName,null);assert.equal(poly.ProxyGraphics,null);assert.equal(poly.XData.Count,0);
});
test('ellipse angles preserve canonical NaN bits on warmed edits',()=>{
 const e=new Ellipse();for(let i=0;i<10000;i++){e.Rotation=i;e.StartAngle=i;e.EndAngle=i;e.Rotation=Infinity;e.StartAngle=-Infinity;e.EndAngle=NaN;}
 // Observe fields directly: generic numeric-array storage may itself canonicalize NaNs.
 assert.equal(doubleBits(e.Rotation),'FFF8000000000000');assert.equal(doubleBits(e.StartAngle),'FFF8000000000000');assert.equal(doubleBits(e.EndAngle),'FFF8000000000000');
});
test('ellipse degenerate reconstruction returns without editing its model',()=>{
 // The C# body returns here; its Debug.Assert host diagnostic is not a browser dialog.
 const e=new Ellipse(new Vector3(1,2,3),4,2);e.Rotation=37;e.TransformBy(Matrix3.Scale(0),Vector3.UnitX);
 assert.equal(e.Center.X,1);assert.equal(e.Center.Y,2);assert.equal(e.Center.Z,3);assert.equal(e.MajorAxis,4);assert.equal(e.MinorAxis,2);assert.equal(e.Rotation,37);
});
test('ellipse nonuniform transform preserves thickness and opaque common data',()=>{
 const e=new Ellipse(new Vector3(1,2,3),4,2);e.Thickness=3;e.ProxyGraphics=Uint8Array.of(9);e.TransformBy(Matrix3.Scale(2,3,1),Vector3.UnitX);
 assert.equal(e.Center.X,3);assert.equal(e.Center.Y,6);assert.equal(e.Center.Z,3);assert.equal(e.Thickness,3);assert.deepEqual([...e.ProxyGraphics],[9]);assert.ok(e.MajorAxis>=e.MinorAxis);
});
