import test from 'node:test';
import assert from 'node:assert/strict';
import * as api from '../../index.js';
import { DxfReader } from '../../netDxf/IO/DxfReader.js';
import { DxfWriter } from '../../netDxf/IO/DxfWriter.js';
import { DocumentTagReader, dimensionOverrideFields, dimensionOverrideBase } from '../../runtime/TypedDocumentIO.js';
import { storedTablePacket } from '../../tools/stored-table-corpus.mjs';
import { DecodeTableText } from '../../runtime/TablePayload.js';
import { BoxedScalar } from '../../runtime/BoxedScalar.js';
import { fromBits } from '../../tools/wire.mjs';

const v2=(x,y)=>new api.Vector2(x,y),v3=(x,y,z=0)=>new api.Vector3(x,y,z);
const all=doc=>Array.from(doc.Entities.All);
function write(doc,binary){const stream=new api.MemoryStream();new DxfWriter().Write(stream,doc,binary);assert.equal(stream.CanWrite,true);return stream.ToArray();}
function cycle(doc,binary){const reader=new DxfReader(),first=reader.Read(write(doc,binary)),second=new DxfReader().Read(write(first,binary));assert.equal(first.Objects.Validate().Count,0);assert.equal(second.Objects.Validate().Count,0);return [first,second];}
function entityCase(name,make,verify,minimum=13){for(let version=minimum;version<=18;version++)for(const binary of[false,true])test(`typed document ${name} / version ${version} / ${binary?'binary':'text'}`,()=>{
  const doc=new api.DxfDocument(version),source=make();source.Layer=new api.Layer('Layer Ω');source.Linetype=new api.Linetype('Dashed Ω',[new api.LinetypeSimpleSegment(.5),new api.LinetypeSimpleSegment(-.25)]);source.Color=new api.AciColor(3);source.Lineweight=25;source.IsVisible=false;source.LinetypeScale=2;
  const data=new api.XData(new api.ApplicationRegistry('APP Ω'));data.XDataRecord.Add(new api.XDataRecord(1000,'metadata Ω'));source.XData.Add(data);doc.Entities.Add(source);
  for(const copy of cycle(doc,binary)){const entities=all(copy);assert.equal(entities.length,1);const target=entities[0];assert.equal(target.constructor,source.constructor);assert.equal(target.Handle,source.Handle);assert.equal(target.Layer,copy.Layers.get_Item('Layer Ω'));assert.equal(target.Linetype,copy.Linetypes.get_Item('Dashed Ω'));assert.equal(target.Color.Index,3);assert.equal(target.Lineweight,25);assert.equal(target.IsVisible,false);assert.equal(target.LinetypeScale,2);assert.equal(target.XData.get_Item('APP Ω').XDataRecord.get_Item(0).Value,'metadata Ω');verify(target,source);}
});}
entityCase('LINE',()=>new api.Line(v3(1,2,3),v3(4,5,6)),(a,b)=>{assert.deepEqual(a.StartPoint,b.StartPoint);assert.deepEqual(a.EndPoint,b.EndPoint);});
entityCase('ARC',()=>new api.Arc(v3(1,2,3),2,20,75),(a,b)=>{assert.deepEqual(a.Center,b.Center);assert.equal(a.Radius,2);assert.equal(a.StartAngle,20);assert.equal(a.EndAngle,75);});
entityCase('CIRCLE',()=>new api.Circle(v3(1,2,3),2),(a,b)=>{assert.deepEqual(a.Center,b.Center);assert.equal(a.Radius,2);});
entityCase('ELLIPSE',()=>new api.Ellipse(v3(1,2,3),5,3),(a,b)=>{assert.deepEqual(a.Center,b.Center);assert.equal(a.MajorAxis,5);assert.equal(a.MinorAxis,3);});
entityCase('POINT',()=>new api.Point(v3(1,2,3)),(a,b)=>assert.deepEqual(a.Position,b.Position));
for(const name of ['Ray','XLine'])entityCase(name,()=>new api[name](v3(1,2,3),v3(1,0,0)),(a,b)=>{assert.deepEqual(a.Origin,b.Origin);assert.deepEqual(a.Direction,b.Direction);});
for(const name of ['Face3D','Solid','Trace'])entityCase(name,()=>new api[name](),(a,b)=>{for(const key of ['FirstVertex','SecondVertex','ThirdVertex','FourthVertex'])assert.deepEqual(a[key],b[key]);});
entityCase('TEXT',()=>new api.Text('Hi Ω 😀',v3(2,3),5),(a,b)=>{assert.equal(a.Value,b.Value);assert.deepEqual(a.Position,b.Position);assert.equal(a.Alignment,b.Alignment);assert.equal(a.Height,5);});
entityCase('MTEXT',()=>new api.MText('First\\PSecond Ω 😀',v3(2,3),5),(a,b)=>{assert.equal(a.Value,b.Value);assert.deepEqual(a.Position,b.Position);assert.equal(a.Height,5);});
entityCase('LWPOLYLINE',()=>new api.Polyline2D([new api.Polyline2DVertex(0,0),new api.Polyline2DVertex(4,2)],false),(a,b)=>{assert.equal(a.Vertexes.Count,2);for(let i=0;i<2;i++)assert.deepEqual(a.Vertexes.get_Item(i).Position,b.Vertexes.get_Item(i).Position);});
entityCase('MESH',()=>new api.Mesh([v3(0,0),v3(4,0),v3(0,4)],[[0,1,2]]),(a,b)=>{assert.deepEqual([...a.Vertexes],[...b.Vertexes]);assert.deepEqual([...a.Faces],[...b.Faces]);},17);
entityCase('VIEWPORT',()=>new api.Viewport(),(a,b)=>{assert.equal(a.Width,b.Width);assert.equal(a.Height,b.Height);// The private normalization cache is not DXF data. Compare every public component exactly.
assert.deepEqual(a.ViewDirection.ToArray(),b.ViewDirection.ToArray());});

for(const binary of [false,true])for(const name of ['AlignedDimension','LinearDimension','Angular2LineDimension','Angular3PointDimension','RadialDimension','DiametricDimension','OrdinateDimension','ArcLengthDimension'])test(`typed ${name} references and DSTYLE / ${binary}`,()=>{
  const d=new api.DxfDocument(18),source=new api[name]();if(source instanceof api.ArcLengthDimension){source.StartAngle=0;source.EndAngle=90;source.Update();}source.StyleOverrides.Add(api.DimensionStyleOverrideType.TextHeight,3);d.Entities.Add(source);
  for(const doc of cycle(d,binary)){const e=all(doc)[0];assert.equal(e.constructor,source.constructor);assert.equal(e.Measurement,source.Measurement);assert.equal(e.StyleOverrides.get_Item(api.DimensionStyleOverrideType.TextHeight).Value,3);assert.equal(e.Style,doc.DimensionStyles.get_Item('Standard'));const records=[...e.XData.get_Item('ACAD').XDataRecord];assert.equal(records.filter(r=>r.Code===1000&&r.Value==='DSTYLE').length,1);}
});
for(const binary of [false,true])for(const [alignmentName,alignment]of Object.entries(api.TextAlignment))test(`typed TEXT alignment ${alignmentName} / ${binary}`,()=>{
  const d=new api.DxfDocument(18),e=new api.Text('Value',v3(4,8),2);e.Alignment=alignment;e.Width=10;d.Entities.Add(e);for(const copy of cycle(d,binary)){const r=all(copy)[0];assert.equal(r.Alignment,alignment);assert.deepEqual(r.Position,e.Position);if(alignment===12||alignment===14)assert.equal(r.Width,10);}
});
for(const binary of [false,true])test(`typed INSERT preserves array, ATTRIB, ATTDEF and shared block identity / ${binary}`,()=>{
  const d=new api.DxfDocument(18),b=new api.Block('Block Ω'),definition=new api.AttributeDefinition('TAG');definition.Prompt='Prompt Ω';definition.Value='Default';b.AttributeDefinitions.Add(definition);b.Entities.Add(new api.Line(v3(1,2),v3(3,4)));
  const one=new api.Insert(b,v3(2,4)),two=new api.Insert(b,v3(6,8));one.ColumnCount=3;one.RowCount=2;one.ColumnSpacing=4;one.RowSpacing=5;one.Attributes.get_Item(0).Value='Instance Ω';d.Entities.Add(one);d.Entities.Add(two);
  for(const copy of cycle(d,binary)){const[x,y]=all(copy);assert.equal(x.Block,y.Block);assert.equal(x.Block,copy.Blocks.get_Item('Block Ω'));assert.equal(x.Attributes.get_Item(0).Value,'Instance Ω');assert.equal(x.Attributes.get_Item(0).Definition,x.Block.AttributeDefinitions.get_Item('TAG'));assert.equal(x.Block.AttributeDefinitions.get_Item('TAG').Prompt,'Prompt Ω');assert.deepEqual([x.RowCount,x.ColumnCount,x.RowSpacing,x.ColumnSpacing],[2,3,5,4]);}
});
for(const binary of [false,true])for(const smooth of [0,5,6])test(`typed retained polyline records / smooth ${smooth} / ${binary}`,()=>{
  const d=new api.DxfDocument(18),source=new api.Polyline3D([v3(0,0),v3(1,2),v3(3,4),v3(4,1)]);source.SmoothType=smooth;d.Entities.Add(source);
  for(const copy of cycle(d,binary)){const e=all(copy)[0];assert.equal(e.SmoothType,smooth);assert.deepEqual([...e.Vertexes],[...source.Vertexes]);if(!smooth)assert.equal(e.HasStoredRecords,true);}
});
for(const binary of [false,true])for(const name of ['polyline','spline','gradient','solid','pattern','associative'])test(`typed HATCH ${name} / ${binary}`,()=>{
  const d=new api.DxfDocument(18),pattern=name==='gradient'?new api.HatchGradientPattern():name==='pattern'?api.HatchPattern.Net:api.HatchPattern.Solid;pattern.Origin=v2(2,3);pattern.Angle=30;pattern.Scale=2;let path;
  if(name==='polyline')path=new api.HatchBoundaryPath([new api.Polyline2D([v2(0,0),v2(5,0),v2(5,5)],true)]);
  else if(name==='spline'){const e=new api.HatchBoundaryPath.Spline();e.Degree=2;e.Knots=[0,0,0,1,1,1];e.ControlPoints=[v3(0,0,1),v3(2,4,1),v3(4,0,1)];e.IsRational=true;path=api.HatchBoundaryPath.FromEdges([e]);}
  else path=new api.HatchBoundaryPath([new api.Circle(api.Vector3.Zero,3)]);
  const source=new api.Hatch(pattern,[path],name==='associative');source.PixelSize=.125;source.SeedPoints.Add(v2(1,1));d.Entities.Add(source);
  for(const doc of cycle(d,binary)){const e=all(doc).find(e=>e instanceof api.Hatch);assert.equal(e.BoundaryPaths.Count,1);assert.equal(e.BoundaryPaths.get_Item(0).Edges.length,path.Edges.length);assert.deepEqual(e.Pattern.Origin,v2(2,3));assert.equal(e.PixelSize,.125);assert.deepEqual([...e.SeedPoints],[...source.SeedPoints]);assert.equal(e.Associative,name==='associative');if(e.Associative)assert.equal(e.BoundaryPaths.get_Item(0).Entities.get_Item(0),all(doc).find(x=>x instanceof api.Circle));}
});
for(const binary of [false,true])test(`typed OBJECTS graph retains alias, extension, reactors and XRECORD payload / ${binary}`,()=>{
  const doc=new api.DxfDocument(18),leaf=new api.DxfXRecord(),line=new api.Line(),ext=new api.DxfDictionary();leaf.Data.Add(new api.DxfTag(1,'kept Ω'));doc.NamedObjects.Add('leaf',leaf);doc.NamedObjects.Add('alias',leaf,false);doc.Entities.Add(line);ext.Add('nested',new api.DxfPlaceholder());doc.Objects.SetExtensionDictionary(line,ext);line.PersistentReactors.Add(leaf);
  for(const copy of cycle(doc,binary)){const e=all(copy)[0],data=copy.NamedObjects.get_Item('leaf');assert.equal(copy.NamedObjects.get_Item('alias'),data);assert.equal(data.Data.get_Item(0).Value,'kept Ω');assert.equal(e.PersistentReactors.get_Item(0),data);assert.equal(e.ExtensionDictionary.get_Item('nested').Owner,e.ExtensionDictionary);assert.equal(e.ExtensionDictionary.Owner,e);}
});
for(const binary of [false,true])test(`typed MULTILEADER resolves registered style and nested text resources / ${binary}`,()=>{
  const d=new api.DxfDocument(18),e=new api.MultiLeader(),style=new api.DxfMLeaderStyle();style.Properties.TextStyle=d.TextStyles.get_Item('Standard');d.Objects.AddMLeaderStyle('LeaderStyle',style);e.Properties.Style=style;e.Properties.TextStyle=style.Properties.TextStyle;e.Properties.LeaderLinetype=d.Linetypes.get_Item('Continuous');e.Context.MText=new api.MLeaderMTextContent();e.Context.MText.Style=e.Properties.TextStyle;e.Context.MText.Text='Leader Ω';d.Entities.Add(e);
  for(const doc of cycle(d,binary)){const item=all(doc)[0];assert.equal(item.Context.MText.Text,'Leader Ω');assert.equal(item.Context.MText.Style,doc.TextStyles.get_Item('Standard'));assert.equal(item.Properties.Style,doc.NamedObjects.get_Item('ACAD_MLEADERSTYLE').get_Item('LeaderStyle'));}
});
for(const binary of [false,true])test(`typed SECTION and VIEW deferred references / ${binary}`,()=>{
  const d=new api.DxfDocument(18),e=new api.Section();e.Name='Cut Ω';e.Vertices.Add(v3(0,0));e.Vertices.Add(v3(1,0));d.Entities.Add(e);const v=new api.View('View');v.LiveSection=e;d.Views.Add(v);
  for(const doc of cycle(d,binary)){const section=all(doc)[0];assert.equal(doc.Views.get_Item('View').LiveSection,section);assert.deepEqual([...section.Vertices],[v3(0,0),v3(1,0)]);assert.equal(section.Name,'Cut Ω');}
});
for(const binary of [false,true])test(`typed layer-state and external-reference metadata / ${binary}`,()=>{
  const d=new api.DxfDocument(18),layer=d.Layers.Add(new api.Layer('Walls'));layer.IsVisible=false;layer.Transparency=new api.Transparency(25);d.Layers.StateManager.AddNew('Saved','Description Ω');const state=d.Layers.StateManager.get_Item('Saved');state.CurrentLayer='Walls';state.PaperSpace=true;
  d.Entities.Add(new api.Insert(new api.Block('External','source Ω.dxf',true)));
  for(const doc of cycle(d,binary)){const s=doc.Layers.StateManager.get_Item('Saved');assert.equal(s.Description,'Description Ω');assert.equal(s.CurrentLayer,'Walls');assert.equal(s.PaperSpace,true);assert.equal(s.Properties.get_Item('Walls').Flags,state.Properties.get_Item('Walls').Flags);assert.equal(doc.Blocks.get_Item('External').XrefFile,'source Ω.dxf');assert.equal(doc.Blocks.get_Item('External').IsXRef,true);}
});
for(const binary of [false,true])test(`typed paper layouts, thumbnail, comments and group membership / ${binary}`,()=>{
  const d=new api.DxfDocument(18);d.Comments.Clear();d.Comments.Add('comment Ω');d.ThumbnailImage=Uint8Array.of(1,2,3,255);for(const name of ['Paper1','Paper2']){d.Layouts.Add(new api.Layout(name));d.Entities.ActiveLayout=name;d.Entities.Add(new api.Line());}d.Entities.ActiveLayout='Model';const line=new api.Line();d.Entities.Add(line);d.Groups.Add(new api.Group('G',[line]));
  for(const doc of cycle(d,binary)){assert.equal(doc.Layouts.Count,3);assert.deepEqual(doc.ThumbnailImage,Uint8Array.of(1,2,3,255));assert.equal(doc.Groups.get_Item('G').Entities.get_Item(0),all(doc)[0]);assert.equal(doc.Comments.Count,binary?0:1);for(const name of ['Paper1','Paper2'])assert.equal(doc.Layouts.get_Item(name).AssociatedBlock.Entities.Count,1);}
});
for(const binary of [false,true])test(`typed stored TABLE payload uses actual source-preserving codec / ${binary}`,()=>{
  const d=new api.DxfDocument(18),tags=storedTablePacket().map(([c,v])=>new api.DxfTag(c,v&&typeof v==='object'?'int'in v?v.int:'short'in v?v.short:'double'in v?fromBits(v.double):v:v)),display=d.Blocks.Add(new api.Block('Display')),table=new api.StoredTable(d,tags,DecodeTableText);d.Entities.Add(table);table.Resolve();
  for(const doc of cycle(d,binary)){const e=all(doc)[0];assert.equal(e.constructor,api.StoredTable);assert.equal(e.Grid.RowCount,table.Grid.RowCount);assert.equal(e.Grid.ColumnCount,table.Grid.ColumnCount);assert.deepEqual([...e.Payload].map(t=>[t.Code,t.Value]),[...table.Payload].map(t=>[t.Code,t.Value]));}
});

test('typed document restores borrowed stream ownership and current-position input',()=>{
  const d=new api.DxfDocument(18);d.Entities.Add(new api.Line());const bytes=write(d,true),stream=new api.MemoryStream();stream.Write(Uint8Array.of(1,2,3),0,3);stream.Write(bytes,0,bytes.length);stream.Position=3;const r=new DxfReader().Read(stream);assert.equal(all(r).length,1);assert.equal(stream.CanRead,true);assert.equal(stream.Position,stream.Length);
});
test('typed reader rejects duplicate physical object identities without fabricating a replacement',()=>{
  const d=new api.DxfDocument(18);d.Entities.Add(new api.Line());d.Entities.Add(new api.Line());const text=new TextDecoder().decode(write(d,false)),[a,b]=all(d);const duplicated=text.replace('\r\n5\r\n'+b.Handle+'\r\n','\r\n5\r\n'+a.Handle+'\r\n');assert.throws(()=>new DxfReader().Read(new TextEncoder().encode(duplicated)));
});
test('typed reader does not dispatch inherited JavaScript property names',()=>{
  const d=new api.DxfDocument(18);d.Entities.Add(new api.Line());const text=new TextDecoder().decode(write(d,false)).replace('\r\nLINE\r\n','\r\nconstructor\r\n');const copy=new DxfReader().Read(new TextEncoder().encode(text));assert.equal(all(copy)[0].constructor,api.DxfOpaqueEntity);assert.equal(all(copy)[0].CodeName,'constructor');
});
test('typed writer produces ordered sections and rejects invalid nested section state',()=>{
  const d=new api.DxfDocument(18),writer=new DxfWriter(),raw=api.DxfRawDocument.Load(write(d,false));assert.deepEqual([...raw.Sections].map(x=>x.Name),['HEADER','CLASSES','TABLES','BLOCKS','ENTITIES','OBJECTS']);writer.chunk={Write(){}};writer.BeginSection('HEADER');assert.throws(()=>writer.BeginSection('ENTITIES'),{name:'InvalidOperationException'});writer.EndSection();
});
test('typed cursor propagates end-of-input errors instead of spinning on malformed sequences',()=>{
  const c=new DocumentTagReader([new api.DxfTag(0,'EOF')],0);assert.throws(()=>c.Next(),{name:'EndOfStreamException'});
});
