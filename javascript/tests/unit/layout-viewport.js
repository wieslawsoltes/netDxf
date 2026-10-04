import test from 'node:test';
import assert from 'node:assert/strict';
import { Layout, Viewport, View, VPort, SunReferences, EntityChangeEventArgs, Block, PlotSettings,
  Circle, Ellipse, Line, Polyline2D, Polyline3D, Spline, Helix, Layer, DxfSun, DxfXRecord,
  DxfOpaqueObject, Vector2, Vector3, Matrix3, Matrix4, ViewportStatusFlags as F, XData, XDataRecord,
  XDataCode, ApplicationRegistry } from '../../index.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidOperationException,
  NotSupportedException, NullReferenceException } from '../../runtime/Errors.js';
import { doubleBits, fromBits } from '../../tools/wire.mjs';
const xy=v=>[v.X,v.Y];
const xyz=v=>[v.X,v.Y,v.Z];

test('paper layouts own independent default viewports and plot settings; model factory owns a model block',()=>{
  const a=new Layout('Sheet'),b=new Layout('Sheet');
  assert.equal(a.IsPaperSpace,true);assert.notEqual(a.Viewport,b.Viewport);assert.notEqual(a.PlotSettings,b.PlotSettings);
  assert.equal(a.TabOrder,0);assert.equal(a.Viewport.Id,1);assert.equal(a.Viewport.Stacking,1);assert.deepEqual(xy(a.Viewport.ViewCenter),[50,100]);
  assert.equal(a.Viewport.Status&F.GridMode,0);assert.equal(a.AssociatedBlock,null);
  const m=Layout.ModelSpace;assert.equal(m.Viewport,null);assert.equal(m.Name,'Model');assert.equal(m.IsReserved,true);assert.equal(m.IsPaperSpace,false);
  assert.equal(m.AssociatedBlock.Name,Block.DefaultModelSpaceName);assert.equal(m.AssociatedBlock.Record.Layout,null);
  assert.equal(new Layout('Model').AssociatedBlock,null);assert.notEqual(Layout.ModelSpace,m);
});
test('layout name classification uses the untrimmed source argument, and cloned tab zero is not silently changed',()=>{
  const padded=new Layout(' Model ');assert.equal(padded.Name,'Model');assert.equal(padded.IsPaperSpace,true);
  assert.throws(()=>new Layout('Sheet').Clone(),{name:'ArgumentException',ParamName:'value'});
  assert.throws(()=>Layout.ModelSpace.Clone('Other'),NotSupportedException);
  const l=new Layout('Sheet');l.TabOrder=1;assert.throws(()=>l.Clone('model'),ArgumentException);
  assert.throws(()=>{l.TabOrder=0;},ArgumentException);assert.equal(l.TabOrder,1);
  const b=new Layout('Later');b.TabOrder=31;assert.equal(l.CompareTo(b),-30);assert.throws(()=>l.CompareTo(null),ArgumentNullException);
});
test('layout vectors copy on assignment and access, while plot settings are intentionally shared',()=>{
  const l=new Layout('Sheet'),v=new Vector3(1,2,3);l.BasePoint=v;v.X=99;l.BasePoint.Y=88;
  assert.deepEqual(xyz(l.BasePoint),[1,2,3]);const p=new PlotSettings();l.PlotSettings=p;p.PageSetupName='shared';assert.equal(l.PlotSettings,p);
  assert.equal(l.PlotSettings.PageSetupName,'shared');l.PlotSettings=null;assert.throws(()=>l.Clone('Copy'),NullReferenceException);
});
test('layout clone isolates plot and viewport graphs without copying paper-space contents or identity',()=>{
  const l=new Layout('Sheet');l.TabOrder=2;l.Viewport.ClippingBoundary=new Circle(Vector3.Zero,2);l.Handle='A';
  l.AssociatedBlock=Block.PaperSpace;l.AssociatedBlock.Entities.Add(new Line());
  const data=new XData(new ApplicationRegistry('LAYOUT'));data.XDataRecord.Add(new XDataRecord(XDataCode.String,'source'));l.XData.Add(data);
  const q=l.Clone('Copy');assert.equal(q.TabOrder,2);assert.equal(q.AssociatedBlock,null);assert.equal(q.Handle,null);assert.equal(q.Owner,null);
  assert.notEqual(q.PlotSettings,l.PlotSettings);assert.notEqual(q.Viewport,l.Viewport);
  assert.notEqual(q.Viewport.ClippingBoundary,l.Viewport.ClippingBoundary);assert.equal(q.Viewport.ClippingBoundary.Reactors.get_Item(0),q.Viewport);
  q.XData.get_Item('LAYOUT').XDataRecord.Clear();assert.equal(data.XDataRecord.Count,1);
});
test('layout viewport host replacement checks precede assignment and metadata runs after assignment',()=>{
  const l=new Layout('Sheet'),before=l.Viewport,next=new Viewport(),events=[];
  const document={StoredTableReferencesRemoval(v){assert.equal(v,before);events.push('check');return true;},
    ReplaceLayoutViewportMetadata(layout,old,value){assert.equal(layout.Viewport,value);events.push('metadata');}};
  l.Owner={Owner:document};assert.throws(()=>{l.Viewport=next;},InvalidOperationException);assert.equal(l.Viewport,before);assert.deepEqual(events,['check']);
  document.StoredTableReferencesRemoval=()=>false;l.Viewport=next;assert.equal(l.Viewport,next);assert.deepEqual(events,['check','metadata']);
  l.Viewport=next;assert.deepEqual(events,['check','metadata','metadata']);
});
test('layout handle assignment delegates to the owner before its viewport and itself',()=>{
  const l=new Layout('Sheet');assert.throws(()=>l.AssignHandle(10n),NullReferenceException);
  const visited=[];l.Owner={AssignHandle(n){visited.push(n);return n+2n;}};
  assert.equal(l.AssignHandle(10n),14n);assert.deepEqual(visited,[10n]);assert.equal(l.Viewport.Handle,'C');assert.equal(l.Handle,'D');
});
test('viewport constructors preserve source half-span dimensions and internal stacking validation',()=>{
  const v=new Viewport(new Vector2(2,4),new Vector2(12,24));assert.deepEqual(xyz(v.Center),[7,14,0]);assert.equal(v.Width,5);assert.equal(v.Height,10);
  const internal=new Viewport(-2);assert.equal(internal.Stacking,-2);assert.throws(()=>internal.Clone(),ArgumentOutOfRangeException);
  const p=new Viewport(new Vector2(3,4),-5,0);assert.equal(p.Width,-5);assert.equal(p.Height,0);
});
test('viewport vector properties and nullable clipping entry retain value and default semantics',()=>{
  const v=new Viewport(null);const p=new Vector3(1,2,3);v.Center=p;p.X=99;v.Center.Y=99;
  assert.deepEqual(xyz(v.Center),[1,2,3]);assert.equal(v.ClippingBoundary,null);assert.equal(v.Id,2);
  assert.ok(v.Status&F.GridMode);v.ViewDirection=Vector3.Zero;assert.deepEqual(xyz(v.ViewDirection),[0,0,0]);
});
test('clipping assignment remeasures the same instance without duplicating reactors or events',()=>{
  const c=new Circle(new Vector3(3,4,19),2),v=new Viewport(c),events=[];v.ClippingBoundaryAdded.Add(()=>events.push('added'));
  assert.deepEqual(xyz(v.Center),[3,4,0]);assert.equal(v.Width,4);c.Radius=7;assert.equal(v.Width,4);
  v.ClippingBoundary=c;assert.equal(v.Width,14);assert.equal(c.Reactors.Count,1);assert.deepEqual(events,[]);
  v.ClippingBoundary=null;assert.equal(c.Reactors.Count,0);assert.equal(v.Status&F.NonRectangularClipping,0);assert.equal(v.Width,14);
});
test('unsupported viewport clipping types reject before changing bounds, flags or reactors',()=>{
  const c=new Circle(Vector3.Zero,2),v=new Viewport(c),width=v.Width;
  for(const bad of [new Line(),new Helix(new Spline([Vector3.Zero,Vector3.UnitX,Vector3.UnitY],null,2))]){
    assert.throws(()=>{v.ClippingBoundary=bad;},ArgumentException);assert.equal(v.ClippingBoundary,c);assert.equal(v.Width,width);assert.equal(c.Reactors.Count,1);
  }
});
test('clipping replacement notifications observe old boundary identity and source-ordered reactor changes',()=>{
  const a=new Circle(Vector3.Zero,2),b=new Circle(new Vector3(3,4,0),5),v=new Viewport(a),events=[];
  v.ClippingBoundaryRemoved.Add((sender,e)=>{events.push('remove');assert.equal(sender.ClippingBoundary,a);assert.equal(e.Item,a);assert.equal(a.Reactors.Count,0);assert.equal(sender.Width,10);});
  v.ClippingBoundaryAdded.Add((sender,e)=>{events.push('add');assert.equal(sender.ClippingBoundary,a);assert.equal(e.Item,b);assert.equal(b.Reactors.get_Item(0),v);});
  v.ClippingBoundary=b;assert.deepEqual(events,['remove','add']);assert.equal(v.ClippingBoundary,b);
});
test('throwing clipping observers retain the original partially applied contract rather than rollback',()=>{
  const a=new Circle(Vector3.Zero,2),b=new Circle(new Vector3(3,4,0),5),v=new Viewport(a);
  v.ClippingBoundaryRemoved.Add(()=>{throw new InvalidOperationException();});
  assert.throws(()=>{v.ClippingBoundary=b;},InvalidOperationException);assert.equal(v.ClippingBoundary,a);assert.equal(a.Reactors.Count,0);assert.equal(b.Reactors.Count,0);assert.equal(v.Width,10);
});
test('rectangular viewport translation keeps clipping absent; affine rotation generates a closed boundary',()=>{
  const v=new Viewport();v.Center=new Vector3(3,4,5);v.Width=10;v.Height=6;v.TransformBy(Matrix3.Identity,new Vector3(1,2,3));
  assert.deepEqual(xyz(v.Center),[4,6,8]);assert.equal(v.ClippingBoundary,null);
  v.TransformBy(new Matrix3(0,-1,0,1,0,0,0,0,1),Vector3.Zero);
  assert.ok(v.ClippingBoundary instanceof Polyline2D);assert.equal(v.ClippingBoundary.IsClosed,true);assert.equal(v.ClippingBoundary.Vertexes.Count,4);
  assert.equal(v.ClippingBoundary.Reactors.get_Item(0),v);assert.equal(v.Width,6);assert.equal(v.Height,10);assert.equal(v.Center.Z,0);
});
test('viewport transforms mutate its existing clip and refresh measured bounds even for identity',()=>{
  const c=new Circle(new Vector3(1,2,0),3),v=new Viewport(c);c.Radius=5;v.Width=123;
  v.TransformBy(Matrix4.Identity);assert.equal(v.Width,10);assert.equal(v.ClippingBoundary,c);assert.equal(c.Reactors.Count,1);
});
test('frozen layers reject detached duplicates by name and use source cancellation behavior',()=>{
  const v=new Viewport(),a=new Layer('Layer');v.FrozenLayers.Add(a);
  assert.throws(()=>v.FrozenLayers.Add(null),ArgumentException);assert.throws(()=>v.FrozenLayers.Add(new Layer('layer')),ArgumentException);
  const it=v.FrozenLayers.GetEnumerator();it.MoveNext();v.FrozenLayers.Add(new Layer('Other'));assert.throws(()=>it.MoveNext(),InvalidOperationException);
});
test('frozen layers preserve the ownership-chain checks including registered duplicate admission',()=>{
  const v=new Viewport(),layer=new Layer('Layer'),doc={};v.Owner={Owner:{Owner:{Owner:doc}}};layer.Owner={Owner:doc};
  v.FrozenLayers.Add(layer);v.FrozenLayers.Add(layer);assert.equal(v.FrozenLayers.Count,2);
  const foreign=new Layer('Foreign');foreign.Owner={Owner:{}};assert.throws(()=>v.FrozenLayers.Add(foreign),ArgumentException);
  assert.throws(()=>v.FrozenLayers.Add(new Layer('Detached')),ArgumentException);
});
test('viewport clone preserves authored bounds after clip cloning and isolates frozen layers',()=>{
  const v=new Viewport(new Circle(Vector3.Zero,2));v.Center=new Vector3(9,8,7);v.Width=123;v.Height=234;v.FrozenLayers.Add(new Layer('Frozen'));
  v.ProxyGraphics=Uint8Array.of(1,2);v.ColorName='book';v.IsVisible=false;v.SunHandlePresent=true;
  const q=v.Clone();assert.deepEqual(xyz(q.Center),[9,8,7]);assert.equal(q.Width,123);assert.equal(q.Height,234);
  assert.notEqual(q.ClippingBoundary,v.ClippingBoundary);assert.notEqual(q.FrozenLayers.get_Item(0),v.FrozenLayers.get_Item(0));
  assert.deepEqual([...q.ProxyGraphics],[1,2]);assert.equal(q.ColorName,'book');assert.equal(q.IsVisible,false);assert.equal(q.SunHandlePresent,true);assert.equal(q.Sun,null);
});
test('block admission includes clipping geometry and the viewport reactor protects its source',()=>{
  const c=new Circle(Vector3.Zero,2),v=new Viewport(c),b=new Block('B');b.Entities.Add(v);
  assert.deepEqual([...b.Entities],[v,c]);assert.equal(c.Owner,b);assert.equal(v.Owner,b);assert.equal(b.Entities.Remove(c),false);
  v.ClippingBoundary=null;assert.equal(b.Entities.Remove(c),true);assert.equal(c.Owner,null);
});
test('SUN host detection, presence and clone guard preserve explicit-null versus absent state',()=>{
  for(const host of [new View('V'),new VPort('P'),new Viewport()]){
    assert.equal(SunReferences.IsHost(host),true);SunReferences.Set(host,null,true);assert.equal(SunReferences.IsPresent(host),true);SunReferences.CheckClone(host);
    SunReferences.Set(host,new DxfSun(),false);assert.throws(()=>SunReferences.CheckClone(host),NotSupportedException);
  }
  assert.equal(SunReferences.Get(null),null);assert.equal(SunReferences.IsPresent(new Line()),false);assert.throws(()=>SunReferences.Set(new Line(),null),ArgumentException);
});
test('SUN profile validation distinguishes named views and keeps ownership validation separate',()=>{
  assert.throws(()=>SunReferences.CheckProfile(new View('V'),15),NotSupportedException);SunReferences.CheckProfile(new Viewport(),15);
  const v=new Viewport(),sun=new DxfSun(),errors=new ReferenceList();SunReferences.Set(v,sun);sun.Owner=v;
  const db={Document:{DrawingVariables:{AcadVer:14}},IsRegistered:()=>true};SunReferences.Validate(v,db,errors);assert.equal(errors.Count,1);
  db.Document.DrawingVariables.AcadVer=18;errors.Clear();SunReferences.Validate(v,db,errors);assert.equal(errors.Count,0);
  sun.Owner=null;SunReferences.Validate(v,db,errors);assert.deepEqual([...errors],['Invalid reciprocal SUN ownership: ']);
  errors.Clear();SunReferences.Set(v,new DxfXRecord());SunReferences.Validate(v,db,errors);assert.equal(errors.Count,1);
});
test('SUN clone guard runs before boundary clone, and layouts do not shallow-copy owned SUN data',()=>{
  const v=new Viewport();v.Sun=new DxfSun();assert.throws(()=>v.Clone(),NotSupportedException);
  const l=new Layout('Sheet');l.TabOrder=1;l.Viewport=v;assert.throws(()=>l.Clone(),NotSupportedException);
});
test('layout and viewport scalar storage retain signed zero and explicit NaN payloads in clones',()=>{
  const v=new Viewport();v.Width=-0;v.TwistAngle=fromBits('FFF8000000001234');const q=v.Clone();
  assert.equal(doubleBits(q.Width),'8000000000000000');assert.equal(doubleBits(q.TwistAngle),'FFF8000000001234');
  const l=new Layout('Sheet');l.TabOrder=1;l.Elevation=-0;assert.equal(doubleBits(l.Clone().Elevation),'8000000000000000');
});
test('entity change arguments retain a readonly shared entity reference including null',()=>{
  const line=new Line(),args=new EntityChangeEventArgs(line);assert.equal(args.Item,line);assert.throws(()=>{args.Item=null;},TypeError);assert.equal(new EntityChangeEventArgs(null).Item,null);
});

test('layout/viewport corpus is deterministic, unique and contains no expected outputs',async()=>{
  const {layoutViewportCorpus}=await import('../../tools/layout-viewport-corpus.mjs');const a=layoutViewportCorpus();
  assert.deepEqual(a,layoutViewportCorpus());assert.equal(a.length,580);assert.equal(new Set(a.map(p=>p.name)).size,580);
  assert.equal(a.reduce((n,p)=>n+p.request.steps.length,0),3881);assert.ok(a.every(p=>!Object.hasOwn(p,'expected')));
});
