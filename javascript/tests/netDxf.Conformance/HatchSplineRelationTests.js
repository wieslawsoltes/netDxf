// Port of pinned HatchSplineRelationTests.cs; original identities and assertions retained.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { DxfDocument, DxfRawDocument, DxfTag, DxfVersion, MemoryStream, Hatch, HatchPattern, HatchBoundaryPath, Block, Insert, Layout, Vector2, Vector3 } from '../../index.js';
import { ArgumentException, InvalidDataException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { PeekDocumentObjects } from '../../netDxf/DxfDocument.Objects.js';
import { Run, Check, Equal, SameDoubleBits, Throws, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const artifacts = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../artifacts/conformance/fixtures');
const single = items => { const a = Array.from(items); Equal(1, a.length, 'Expected one item'); return a[0]; };
const records = raw => Array.from(raw.Sections).flatMap(s => Array.from(s.Records));
export function RegisterHatchSplineRelationTests() {
  for (const v of SupportedVersions) for (const b of [false, true]) {
    const suffix = `${VersionName(v)}/${BooleanName(b)}`;
    Run(`hatch-spline-relations/producer/${suffix}`, () => HatchRelationsProducer(v, b));
    Run(`hatch-spline-relations/weights/${suffix}`, () => HatchRelationsWeights(v, b));
    Run(`hatch-spline-relations/rational-sparse-weights/${suffix}`, () => HatchRelationsRationalSparseWeights(v, b));
    for (const defect of ['short-knots','long-knots','descending','degree-controls','empty-controls','empty-knots','zero-degree','periodic-descending'])
      Run(`hatch-spline-relations/read/${suffix}/${defect}`, () => HatchRelationsMalformed(v, b, defect));
    for (let placement = 0; placement < 4; placement++)
      Run(`hatch-spline-relations/preflight/${suffix}/${placement}`, () => HatchRelationsPreflight(v, b, placement, 'short-knots'));
  }
  for (const b of [false, true]) for (const defect of ['zero-degree','negative-degree','null-controls','null-knots','short-controls','short-knots','long-knots','descending','nan-knot','infinite-knot','nan-control','infinite-weight'])
    Run(`hatch-spline-relations/api/${BooleanName(b)}/${defect}`, () => HatchRelationsPreflight(DxfVersion.AutoCad2018, b, 0, defect));
}
export function HatchRelationsRaw(version, binary) {
  const file = `ezdxf-hatch-spline-R${VersionName(version).replace('AutoCad','')}-${binary?'binary':'ascii'}.dxf`;
  const dir = path.join(root, 'tests/fixtures/hatch-spline-relations'), bytes = fs.readFileSync(path.join(dir, file));
  const manifest = JSON.parse(fs.readFileSync(path.join(dir, 'manifest.json'), 'utf8'));
  const expected = single(manifest.files.filter(x => x.file === file)).sha256;
  Equal(expected, crypto.createHash('sha256').update(bytes).digest('hex'), 'Pinned producer source');
  HatchRelationsAssertTransport(bytes, binary);
  const stream = new MemoryStream(bytes); try { return DxfRawDocument.Load(stream); } finally { stream.Dispose(); }
}
export function HatchRelationsAssertTransport(bytes, binary) {
  const sentinel = Buffer.from('AutoCAD Binary DXF\r\n\x1a\0', 'ascii');
  Equal(binary, Buffer.from(bytes).subarray(0, sentinel.length).equals(sentinel), 'Actual input bytes use the named DXF transport');
}
export function HatchRelationsLoad(raw) {
  const bytes = new MemoryStream(); try {
    raw.Save(bytes, raw.IsBinary); HatchRelationsAssertTransport(bytes.ToArray(), raw.IsBinary); bytes.Position = 0;
    const doc = DxfDocument.Load(bytes); Check(doc !== null, 'Producer HATCH load failed.'); return doc;
  } finally { bytes.Dispose(); }
}
export function HatchRelationsRoundTrip(doc, binary, output = null) {
  const bytes = new MemoryStream(); try {
    Check(doc.Save(bytes, binary), 'Spline relation export'); HatchRelationsAssertTransport(bytes.ToArray(), binary);
    if (output !== null) { fs.mkdirSync(artifacts, { recursive: true }); fs.writeFileSync(path.join(artifacts, output), bytes.ToArray()); }
    bytes.Position = 0; const loaded = DxfDocument.Load(bytes); Check(loaded !== null, 'Spline relation reload failed.'); return loaded;
  } finally { bytes.Dispose(); }
}
export function HatchRelationName(hatch) { return single(hatch.XData.get_Item('HATCH_SPLINE_REL').XDataRecord).Value; }
export function HatchRelationEdge(hatch) { return single(Array.from(single(hatch.BoundaryPaths).Edges).filter(e => e instanceof HatchBoundaryPath.Spline)); }
export function HatchRelationsEqual(expected, actual) {
  for (const key of ['Degree','IsRational','IsPeriodic']) Equal(expected[key], actual[key], `Stored ${key}`);
  Equal(expected.Knots.length, actual.Knots.length, 'Knot count'); Equal(expected.ControlPoints.length, actual.ControlPoints.length, 'Control count');
  for (let i = 0; i < expected.Knots.length; i++) SameDoubleBits(expected.Knots[i], actual.Knots[i], 'Stored knot order/value');
  for (let i = 0; i < expected.ControlPoints.length; i++) for (const key of ['X','Y','Z']) SameDoubleBits(expected.ControlPoints[i][key], actual.ControlPoints[i][key], `Stored control ${key}`);
  Equal(Array.from(expected.FitPoints), Array.from(actual.FitPoints), 'Fit data independent of control metadata');
  Equal(expected.StartTangent, actual.StartTangent, 'Start tangent'); Equal(expected.EndTangent, actual.EndTangent, 'End tangent');
}
export function HatchRelationsProducer(version, binary) {
  let doc = HatchRelationsLoad(HatchRelationsRaw(version, binary));
  const expected = new Map(Array.from(doc.Entities.Hatches, h => [HatchRelationName(h), HatchRelationEdge(h).Clone()]));
  Equal(4, expected.size, 'Four independent producer spline packets');
  for (let cycle = 0; cycle < 3; cycle++) {
    doc = HatchRelationsRoundTrip(doc, cycle === 1 ? !binary : binary, cycle === 2 ? `hatch-spline-relations-${VersionName(version)}-${BooleanName(binary)}.dxf` : null);
    for (const h of doc.Entities.Hatches) { HatchRelationsEqual(expected.get(HatchRelationName(h)), HatchRelationEdge(h)); Equal(2, single(h.BoundaryPaths).Edges.Count, 'Following closing edge remains intact'); }
    Equal(1, Array.from(doc.Entities.Lines).length, 'Following LINE survives counted lists'); Equal(0, doc.Objects.Validate().Count, 'No database damage');
  }
}
export function HatchRelationsWeights(version, binary) {
  let raw = HatchRelationsRaw(version, binary); const record = records(raw).find(r => r.Name === 'HATCH'), tags = Array.from(record.Tags), start = tags.findIndex(t => t.Code === 94), y = tags.findIndex((t,i) => i >= start && t.Code === 20);
  tags.splice(y + 1, 0, new DxfTag(42, 2.5)); raw = raw.WithRecord(record, tags);
  let doc = HatchRelationsLoad(raw), hatch = single(Array.from(doc.Entities.Hatches).filter(h => HatchRelationName(h) === 'QUADRATIC')), edge = HatchRelationEdge(hatch);
  Check(!edge.IsRational, 'Weight presence does not change rational flag'); SameDoubleBits(2.5, edge.ControlPoints[0].Z, 'Accepted source nondefault weight');
  doc = HatchRelationsRoundTrip(doc, !binary); hatch = single(Array.from(doc.Entities.Hatches).filter(h => HatchRelationName(h) === 'QUADRATIC')); edge = HatchRelationEdge(hatch);
  SameDoubleBits(2.5, edge.ControlPoints[0].Z, 'Accepted weight must survive output even with rational flag zero');
  edge.ControlPoints[0] = new Vector3(0,0,1); edge.ControlPoints[1] = new Vector3(5,10,2.5); edge.ControlPoints[2] = new Vector3(10,0,-0);
  const expected = edge.Clone(), copied = HatchRelationEdge(hatch.Clone()); HatchRelationsEqual(expected, copied);
  copied.Knots[0] = -10; copied.ControlPoints[0] = new Vector3(0,0,9);
  SameDoubleBits(0, edge.Knots[0], 'Clone knot storage is independent'); SameDoubleBits(1, edge.ControlPoints[0].Z, 'Clone control storage is independent');
  doc = HatchRelationsRoundTrip(doc, binary, `hatch-spline-relations-weights-${VersionName(version)}-${BooleanName(binary)}.dxf`);
  HatchRelationsEqual(expected, HatchRelationEdge(single(Array.from(doc.Entities.Hatches).filter(h => HatchRelationName(h) === 'QUADRATIC'))));
}
export function HatchRelationsRationalSparseWeights(version, binary) {
  const raw = HatchRelationsRaw(version, binary), record = single(records(raw).filter(r => r.Name === 'HATCH' && Array.from(r.Tags).some(t => t.Code === 1000 && t.Value === 'RATIONAL'))), tags = Array.from(record.Tags);
  const indexes = tags.flatMap((t,i) => t.Code === 42 ? [i] : []); Equal(4, indexes.length, 'Independent rational producer supplies four indexed weights');
  tags[indexes[3]] = new DxfTag(42,-0); tags[indexes[1]] = new DxfTag(42,-2.5); tags.splice(indexes[2],1); tags.splice(indexes[0],1);
  let doc = HatchRelationsLoad(raw.WithRecord(record,tags)); const edge = HatchRelationEdge(single(Array.from(doc.Entities.Hatches).filter(h => HatchRelationName(h) === 'RATIONAL')));
  Check(edge.IsRational, 'Sparse optional weights retain the explicit rational flag');
  [1,-2.5,1,-0].forEach((w,i) => SameDoubleBits(w, edge.ControlPoints[i].Z, 'Sparse rational weight defaults remain indexed'));
  const expected = new Map(Array.from(doc.Entities.Hatches,h => [HatchRelationName(h),HatchRelationEdge(h).Clone()]));
  for (let cycle = 0; cycle < 3; cycle++) {
    doc = HatchRelationsRoundTrip(doc,cycle === 1 ? !binary : binary,cycle === 2 ? `hatch-spline-relations-rational-weights-${VersionName(version)}-${BooleanName(binary)}.dxf` : null);
    for (const h of doc.Entities.Hatches) HatchRelationsEqual(expected.get(HatchRelationName(h)),HatchRelationEdge(h));
    Equal(1,Array.from(doc.Entities.Lines).length,'Following LINE survives sparse rational weights');
  }
}
export function HatchRelationsMalformed(version,binary,defect) {
  let raw = HatchRelationsRaw(version,binary); const record = records(raw).filter(r=>r.Name==='HATCH')[defect==='periodic-descending'?3:0], tags=Array.from(record.Tags), begin=tags.findIndex(t=>t.Code===94);
  const index=code=>tags.findIndex((t,i)=>i>=begin&&t.Code===code), set=(code,value)=>{tags[index(code)]=new DxfTag(code,value);};
  switch(defect){
    case 'short-knots':set(95,5);tags.splice(index(40),1);break;
    case 'long-knots':set(95,7);tags.splice(index(40),0,new DxfTag(40,0));break;
    case 'descending':case 'periodic-descending':tags[index(40)+1]=new DxfTag(40,100);break;
    case 'degree-controls':set(94,3);set(95,7);tags.splice(index(40),0,new DxfTag(40,0));break;
    case 'empty-controls':{set(96,0);const at=index(10),end=tags.findIndex((t,i)=>i>=at&&(t.Code===97||t.Code===72));tags.splice(at,end-at);break;}
    case 'empty-knots':set(95,0);tags.splice(index(40),6);break;
    case 'zero-degree':set(94,0);break;
  }
  raw=raw.WithRecord(record,tags);const bytes=new MemoryStream();try{
    raw.Save(bytes,binary);HatchRelationsAssertTransport(bytes.ToArray(),binary);bytes.Position=0;
    if(GetTypedIOConfiguration()==='Debug'){
      let error;try{DxfDocument.Load(bytes);}catch(e){error=e;}
      Check(error instanceof InvalidDataException,`Malformed HATCH spline relation accepted: ${defect}`);
      Check(error.message.includes('HATCH')&&error.message.includes('group code'),'Contextual spline rejection');
    }else Check(DxfDocument.Load(bytes)===null,'Release malformed spline input must return null');
  }finally{bytes.Dispose();}
}
export function HatchRelationsPreflight(version,binary,placement,defect){
  const edge=Object.assign(new HatchBoundaryPath.Spline(),{Degree:2,Knots:[0,0,0,1,1,1],ControlPoints:[new Vector3(0,0,1),new Vector3(5,10,1),new Vector3(10,0,1)]});
  const line=Object.assign(new HatchBoundaryPath.Line(),{Start:new Vector2(10,0),End:Vector2.Zero}),hatch=new Hatch(HatchPattern.Solid,[new HatchBoundaryPath([edge,line])],false),doc=new DxfDocument(version);
  switch(placement){
    case 0:doc.Entities.Add(hatch);break;
    case 1:doc.Layouts.Add(new Layout('SplinePaper'));doc.Entities.ActiveLayout='SplinePaper';doc.Entities.Add(hatch);doc.Entities.ActiveLayout='Model';break;
    case 2:{const inner=new Block('SplineInner');inner.Entities.Add(hatch);const outer=new Block('SplineOuter');outer.Entities.Add(new Insert(inner));doc.Entities.Add(new Insert(outer));break;}
    default:{const unused=new Block('SplineUnused');unused.Entities.Add(hatch);doc.Blocks.Add(unused);break;}
  }
  switch(defect){
    case 'zero-degree':edge.Degree=0;break;case 'negative-degree':edge.Degree=-1;break;case 'null-controls':edge.ControlPoints=null;break;case 'null-knots':edge.Knots=null;break;
    case 'short-controls':edge.ControlPoints=Array.from(edge.ControlPoints).slice(0,2);break;case 'short-knots':edge.Knots=Array.from(edge.Knots).slice(0,5);break;case 'long-knots':edge.Knots=[...edge.Knots,1];break;
    case 'descending':edge.Knots[1]=2;break;case 'nan-knot':edge.Knots[1]=NaN;break;case 'infinite-knot':edge.Knots[1]=Infinity;break;case 'nan-control':edge.ControlPoints[1]=new Vector3(NaN,10,1);break;case 'infinite-weight':edge.ControlPoints[1]=new Vector3(5,10,-Infinity);break;
  }
  const seed=doc.DrawingVariables.HandleSeed,handle=hatch.Handle,apps=doc.ApplicationRegistries.Count,layouts=doc.Layouts.Count,blocks=doc.Blocks.Count,database=PeekDocumentObjects(doc);
  const bytes=new MemoryStream(),original=new Uint8Array([10,20,30,40]);bytes.Write(original);bytes.Position=2;
  try{
    if(GetTypedIOConfiguration()==='Debug')Throws(ArgumentException,()=>doc.Save(bytes,binary));else Check(!doc.Save(bytes,binary),'Release invalid spline save returns false');
    Equal(original,bytes.ToArray(),'Invalid spline preflight preserves destination bytes');Equal(2,bytes.Position,'Invalid spline preflight preserves position');
    Equal(seed,doc.DrawingVariables.HandleSeed,'Invalid spline preflight allocates no handles');Equal(handle,hatch.Handle,'Invalid spline preflight preserves entity identity');
    Equal(apps,doc.ApplicationRegistries.Count,'No APPID registration');Equal(layouts,doc.Layouts.Count,'No layout mutation');Equal(blocks,doc.Blocks.Count,'No block mutation');Check(database===PeekDocumentObjects(doc),'No lazy OBJECTS creation');
    edge.Degree=2;edge.Knots=[0,0,0,1,1,1];edge.ControlPoints=[new Vector3(0,0,1),new Vector3(5,10,1),new Vector3(10,0,1)];
    const repaired=new MemoryStream();try{Check(doc.Save(repaired,binary),'Explicitly repaired spline can save after rejection');}finally{repaired.Dispose();}
  }finally{bytes.Dispose();}
}
