// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { EntityChangeEventArgs } from './EntityChangeEventArgs.js';
import { ViewportStatusFlags as F } from './ViewportStatusFlags.js';
import { Polyline2D } from './Polyline2D.js';
import { Polyline2DVertex } from './Polyline2DVertex.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { BoundingRectangle } from '../BoundingRectangle.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { MathHelper } from '../MathHelper.js';
import { ObservableCollection } from '../Collections/ObservableCollection.js';
import { SunReferences } from '../Objects/SunReferences.js';
import { EventHook } from '../../runtime/EventHook.js';
import { InstallViewFields } from '../../runtime/ViewFields.js';
import { MultiplyDouble as mul } from '../../runtime/GeometryRuntime.js';
import { ArgumentException, ArgumentOutOfRangeException, NullReferenceException, RequireInteger } from '../../runtime/Errors.js';
const ref = value => { if(value == null) throw new NullReferenceException(); return value; };
const fields = {
  Center:[()=>Vector3.Zero], Width:[297], Height:[210], ViewCenter:[()=>Vector2.Zero],
  SnapBase:[()=>Vector2.Zero], SnapSpacing:[()=>new Vector2(10)], GridSpacing:[()=>new Vector2(10)],
  ViewDirection:[()=>Vector3.UnitZ], ViewTarget:[()=>Vector3.Zero], LensLength:[50],
  FrontClipPlane:[0], BackClipPlane:[0], ViewHeight:[250], SnapAngle:[0], TwistAngle:[0],
  CircleZoomPercent:[1000], UcsOrigin:[()=>Vector3.Zero], UcsXAxis:[()=>Vector3.UnitX],
  UcsYAxis:[()=>Vector3.UnitY], Elevation:[0]
};
/** Paper-space viewport. Registered layout/document operations remain external host hooks. */
export class Viewport extends EntityObject {
  #boundary = null; #frozen = new ObservableCollection(); #stacking = 2; #id = 2;
  Status = F.AdaptiveGridDisplay | F.DisplayGridBeyondDrawingLimits | F.CurrentlyAlwaysEnabled | F.UcsIconVisibility | F.GridMode;
  Sun = null; SunHandlePresent = false; // Set by the internal SUN ownership adapter.
  constructor(...args) {
    super(EntityType.Viewport, DxfObjectCode.Viewport);
    for(const name of ['ClippingBoundaryAdded','ClippingBoundaryRemoved'])
      Object.defineProperty(this, name, {value:new EventHook(), enumerable:true});
    this.#frozen.BeforeAddItem.Add((_,e) => {
      if(e.Item == null) e.Cancel = true;
      else if(this.Owner !== null && e.Item.Owner === null) e.Cancel = true;
      else if(this.Owner === null && e.Item.Owner !== null) e.Cancel = true;
      else if(this.Owner !== null && e.Item.Owner !== null) {
        // Match the original owner's Block -> BlockRecord -> BlockRecords -> document chain.
        // Registered duplicates are not checked by the source's else-if chain.
        if(ref(ref(ref(this.Owner).Owner).Owner).Owner !== ref(e.Item.Owner).Owner) e.Cancel = true;
      } else if(this.#frozen.Contains(e.Item)) e.Cancel = true;
    });
    if(args.length === 0) return;
    if(args.length === 1) {
      if(typeof args[0] === 'number') this.#id = this.#stacking = RequireInteger(args[0],-32768,32767,'id');
      else this.ClippingBoundary = args[0];
      return;
    }
    if(args.length === 2 && args.every(v => v instanceof Vector2)) {
      const [bottom,top] = args;
      this.Center = new Vector3(mul(top.X + bottom.X,.5),mul(top.Y + bottom.Y,.5),0);
      // Preserve the pinned constructor's half-span dimensions, not a silently repaired rectangle.
      this.Width = mul(top.X - bottom.X,.5); this.Height = mul(top.Y - bottom.Y,.5); return;
    }
    if(args.length === 3 && args[0] instanceof Vector2) {
      this.Center = new Vector3(args[0].X,args[0].Y,0); this.Width=args[1];this.Height=args[2];return;
    }
    throw new ArgumentException('No matching Viewport constructor.');
  }
  static CreateOverload(signature,...args) {
    if(['short','netDxf.Entities.EntityObject','netDxf.Vector2,netDxf.Vector2','netDxf.Vector2,double,double'].includes(signature)) return new Viewport(...args);
    throw new ArgumentException('Unknown Viewport constructor signature.','signature');
  }
  get Stacking() { return this.#stacking; }
  set Stacking(value) { if(value < -1) throw new ArgumentOutOfRangeException('value'); this.#stacking=value; }
  get Id() { return this.#id; } set Id(value) { this.#id=value; } // internal in C#
  get FrozenLayers() { return this.#frozen; }
  get ClippingBoundary() { return this.#boundary; }
  set ClippingBoundary(value) {
    if(value != null) {
      let bounds;
      switch(value.Type) {
        case EntityType.Circle: bounds=new BoundingRectangle(new Vector2(value.Center.X,value.Center.Y),value.Radius);break;
        case EntityType.Ellipse: bounds=new BoundingRectangle(new Vector2(value.Center.X,value.Center.Y),value.MajorAxis,value.MinorAxis,value.Rotation);break;
        case EntityType.Polyline2D: bounds=new BoundingRectangle(value.PolygonalVertexes(6,MathHelper.Epsilon,MathHelper.Epsilon));break;
        case EntityType.Polyline3D: bounds=new BoundingRectangle(Array.from(value.Vertexes,p=>new Vector2(p.X,p.Y)));break;
        case EntityType.Spline: bounds=new BoundingRectangle(Array.from(value.ControlPoints,p=>new Vector2(p.X,p.Y)));break;
        default: throw new ArgumentException('Only lightweight polylines, polylines, circles, ellipses and splines are allowed as a viewport clipping boundary.');
      }
      this.Width=bounds.Width;this.Height=bounds.Height;this.Center=new Vector3(bounds.Center.X,bounds.Center.Y,0);
      this.Status |= F.NonRectangularClipping;
    } else { value=null;this.Status &= ~F.NonRectangularClipping; }
    // Reassigning the same boundary still refreshes its bounding rectangle.
    if(this.#boundary === value) return;
    if(this.#boundary !== null) { this.#boundary.RemoveReactor(this);this.OnClippingBoundaryRemovedEvent(this.#boundary); }
    if(value !== null) { value.AddReactor(this);this.OnClippingBoundaryAddedEvent(value); }
    this.#boundary=value;
  }
  OnClippingBoundaryAddedEvent(item) { this.ClippingBoundaryAdded.Invoke(this,new EntityChangeEventArgs(item)); }
  OnClippingBoundaryRemovedEvent(item) { this.ClippingBoundaryRemoved.Invoke(this,new EntityChangeEventArgs(item)); }
  TransformBy(matrix,translation) {
    [matrix,translation]=this.$transformArguments(matrix,translation);
    let normal=Matrix3.Multiply(matrix,this.Normal);if(Vector3.Equals(Vector3.Zero,normal)) normal=this.Normal;
    this.Normal=normal;
    let boundary=this.#boundary;
    if(boundary === null) {
      if(matrix.IsIdentity) { this.Center=Vector3.Add(this.Center,translation);return; }
      const c=this.Center,w=mul(this.Width,.5),h=mul(this.Height,.5);
      boundary=new Polyline2D([new Polyline2DVertex(c.X-w,c.Y-h),new Polyline2DVertex(c.X+w,c.Y-h),
        new Polyline2DVertex(c.X+w,c.Y+h),new Polyline2DVertex(c.X-w,c.Y+h)],true);
    }
    boundary.TransformBy(matrix,translation);this.ClippingBoundary=boundary;
  }
  Clone() {
    SunReferences.CheckClone(this);
    const copy=this.$copyEntityAttributes(new Viewport());
    copy.ClippingBoundary=this.#boundary === null ? null : this.#boundary.Clone();
    // Assignment order is significant: preserve authored bounds, not the boundary's current bounds.
    for(const name of ['Center','Width','Height','Stacking','Id','ViewCenter','SnapBase','SnapSpacing','GridSpacing','ViewDirection','ViewTarget',
      'LensLength','FrontClipPlane','BackClipPlane','ViewHeight','SnapAngle','TwistAngle','CircleZoomPercent','Status','UcsOrigin','UcsXAxis','UcsYAxis','Elevation']) copy[name]=this[name];
    for(const layer of this.#frozen) copy.#frozen.Add(ref(layer).Clone());
    this.$finishEntityClone(copy);copy.SunHandlePresent=this.SunHandlePresent;return copy;
  }
}
InstallViewFields(Viewport,fields);
