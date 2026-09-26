import test from 'node:test';
import assert from 'node:assert/strict';
import * as api from '../../index.js';
import * as io from '../../runtime/DxfTransport.js';
import { TextCodeValueReader } from '../../netDxf/IO/TextCodeValueReader.js';
import { TextCodeValueWriter } from '../../netDxf/IO/TextCodeValueWriter.js';
import { BinaryCodeValueReader } from '../../netDxf/IO/BinaryCodeValueReader.js';
import { BinaryCodeValueWriter } from '../../netDxf/IO/BinaryCodeValueWriter.js';
import { primitivePackets, splinePacket, helixTail, primitiveIOCorpus } from '../../tools/primitive-io-corpus.mjs';
function reader(tags) {
  const host={text:'',WriteLine(value){this.text+=String(value??'')+'\n';},Flush(){}},writer=new TextCodeValueWriter(host);
  for(const [code,value]of tags)writer.Write(code,value);writer.Write(0,'ENDSEC');writer.Write(0,'EOF');
  const result=new TextCodeValueReader(host.text);result.Next();return result;
}
const document=()=>new api.DxfDocument(18);
const capture=()=>({tags:[],Write(code,value){this.tags.push([code,value]);}});
const spline=(degree=3,periodic=false)=>io.ReadSpline(reader(splinePacket(degree,periodic)),document());
const helix=()=>io.ReadHelix(reader([...splinePacket(),...helixTail]),document());
for(const kind of Object.keys(primitivePackets))test(kind+' reads a typed model and leaves the next record current',()=>{
  const input=reader(primitivePackets[kind]),entity=io['Read'+kind](input,document());
  assert.ok(entity instanceof api[kind]);assert.equal(input.Value,'ENDSEC');input.Next();assert.equal(input.Value,'EOF');
  const output=capture();io['Write'+kind](output,18,entity);assert.equal(output.tags[0][0],100);assert.ok(output.tags.length>5);
});
test('primitive null writes preserve the completed subclass prefix',()=>{
  for(const kind of [...Object.keys(primitivePackets),'Spline','Helix']){
    const output=capture();assert.throws(()=>io['Write'+kind](output,18,null),{name:'NullReferenceException'});assert.equal(output.tags.length,1);assert.equal(output.tags[0][0],100);
  }
});
test('ARC/CIRCLE accept the original nonpositive-radius default',()=>{
  for(const kind of ['Arc','Circle'])for(const radius of [-1,-0,0]){
    const e=io['Read'+kind](reader([[100,'AcDbCircle'],[40,radius]]),document());assert.equal(e.Radius,1);
  }
});
test('SOLID/TRACE preserve source first-vertex elevation without inventing planarity validation',()=>{
  for(const kind of ['Solid','Trace']){
    const e=io['Read'+kind](reader([[100,'AcDbTrace'],[30,1],[31,2],[32,3],[33,4]]),document());assert.equal(e.Elevation,1);
    const output=capture();io['Write'+kind](output,18,e);assert.deepEqual(output.tags.filter(([code])=>code>=30&&code<=33).map(([,v])=>v),[1,1,1,1]);
  }
});
test('primitive XData registration precedes final ellipse-axis validation and is not silently rolled back',()=>{
  const d=document(),input=reader([[100,'AcDbEllipse'],[1001,'CALLER_APP'],[1000,'metadata'],[11,0],[21,0],[31,0]]);
  assert.throws(()=>io.ReadEllipse(input,d),{name:'ArgumentOutOfRangeException'});assert.equal(d.ApplicationRegistries.Contains('CALLER_APP'),true);assert.equal(input.Value,'ENDSEC');
});
test('primitive readers do not absorb an adjacent entity record',()=>{
  const input=reader([[100,'AcDbLine'],[10,1],[0,'POINT'],[100,'AcDbPoint'],[10,7]]),d=document();
  assert.equal(io.ReadLine(input,d).StartPoint.X,1);assert.equal(input.Value,'POINT');input.Next();assert.equal(io.ReadPoint(input,d).Position.X,7);
});
test('LINE writer reloads mutable model vectors after synchronous callback mutations',()=>{
  const line=new api.Line(new api.Vector3(1,2,3),new api.Vector3(4,5,6)),output=[];
  io.WriteLine({Write(code,value){output.push([code,value]);if(code===10)line.StartPoint=new api.Vector3(7,8,9);}},18,line);
  assert.deepEqual(output.filter(([c])=>[10,20,30].includes(c)),[[10,1],[20,8],[30,9]]);
});
test('CIRCLE output keeps the source local OCS center snapshot across callbacks',()=>{
  const e=new api.Circle(new api.Vector3(1,2,3),4),output=[];
  io.WriteCircle({Write(code,value){output.push([code,value]);if(code===10)e.Center=new api.Vector3(7,8,9);}},18,e);
  assert.deepEqual(output.filter(([c])=>[10,20,30].includes(c)),[[10,1],[20,2],[30,3]]);
});
test('primitive writes propagate callback failures with only the completed prefix',()=>{
  const e=new api.Line(),output=[],failure=new Error('output');
  assert.throws(()=>io.WriteLine({Write(c,v){if(c===11)throw failure;output.push([c,v]);}},18,e),error=>error===failure);
  assert.deepEqual(output.map(([c])=>c),[100,10,20,30]);
});
test('primitive binary body roundtrip preserves finite scalar bits and signed zero',()=>{
  const stream=new api.MemoryStream(),writer=new BinaryCodeValueWriter(stream),e=new api.Line(new api.Vector3(-0,1e-100,3),new api.Vector3(1e100,5,6));
  io.WriteLine(writer,18,e);writer.Write(0,'ENDSEC');writer.Flush();stream.Position=0;
  const input=new BinaryCodeValueReader(stream);input.Next();const loaded=io.ReadLine(input,document());
  assert.equal(Object.is(loaded.StartPoint.X,-0),true);assert.equal(loaded.StartPoint.Y,1e-100);assert.equal(loaded.EndPoint.X,1e100);assert.equal(stream.CanRead,true);
});
for(const degree of [1,2,3,7,10])test('periodic SPLINE degree '+degree+' compacts only the exact prefix overlap',()=>{
  const input=splinePacket(degree,true),e=io.ReadSpline(reader(input),document());
  assert.equal(e.IsClosedPeriodic,true);assert.equal(e.ControlPoints.length,degree+2);assert.deepEqual(e.ControlPoints[0].ToArray(),[0,0,-0]);
  const output=capture();io.WriteSpline(output,18,e);
  for(const code of [10,20,30,40,41])assert.deepEqual(output.tags.filter(([c])=>c===code).map(([,v])=>v),input.filter(([c])=>c===code).map(([,v])=>v));
  assert.equal(output.tags.some(([c])=>c===72||c===73||c===74),false);
});
test('periodic SPLINE rejects a mismatched overlap without refitting it',()=>{
  const input=splinePacket(3,true);input.find(p=>p[0]===10)[1]+=1;
  assert.throws(()=>io.ReadSpline(reader(input),document()),{name:'NotSupportedException'});
});
test('SPLINE partial weight lists use uniform defaults rather than partial substitution',()=>{
  const input=splinePacket().filter(([c])=>c!==41);input.push([41,9]);const e=io.ReadSpline(reader(input),document());
  assert.ok([...e.Weights].every(v=>v===1));
});
test('SPLINE completes controls and tangents only when their Z group arrives',()=>{
  const input=splinePacket();input.push([12,8],[22,9],[32,10],[12,88]);const e=io.ReadSpline(reader(input),document());assert.deepEqual(e.StartTangent.ToArray(),[8,9,10]);
  const noZ=io.ReadSpline(reader([...splinePacket(),[12,1],[22,2]]),document());assert.equal(noZ.StartTangent,null);
});
test('SPLINE normalized tolerance defaults and parameterization priority match the source',()=>{
  const input=splinePacket().map(([c,v])=>[c,c===70?32|64|128|256:[42,43,44].includes(c)?-1:v]);const e=io.ReadSpline(reader(input),document());
  assert.equal(e.KnotTolerance,1e-7);assert.equal(e.CtrlPointTolerance,1e-7);assert.equal(e.FitTolerance,1e-10);assert.equal(e.KnotParameterization,32);
});
test('SPLINE ordinary parsing ignores the normal while HELIX inherits it',()=>{
  const prefix=[...splinePacket(),[210,0],[220,1],[230,0]];
  assert.deepEqual(io.ReadSpline(reader(prefix),document()).Normal.ToArray(),[0,0,1]);
  assert.deepEqual(io.ReadHelix(reader([...prefix,...helixTail]),document()).Normal.ToArray(),[0,1,0]);
});
test('SPLINE optional XData emission is honored without deleting model metadata',()=>{
  const e=io.ReadSpline(reader([...splinePacket(),[1001,'APP'],[1000,'data']]),document()),output=capture();io.WriteSpline(output,18,e,false);
  assert.equal(output.tags.some(([c])=>c===1001),false);assert.equal(e.XData.Count,1);
});
test('HELIX shares its inherited spline body and emits XData exactly once',()=>{
  const e=io.ReadHelix(reader([...splinePacket(),[1001,'APP'],[1000,'a'],...helixTail,[1001,'APP'],[1000,'b']]),document()),output=capture();io.WriteHelix(output,18,e);
  assert.equal(output.tags.filter(([c])=>c===1001).length,1);assert.deepEqual([...e.XData.get_Item('APP').XDataRecord].map(r=>r.Value),['a','b']);
  assert.equal(output.tags.filter(([c,v])=>c===100&&v==='AcDbSpline').length,1);assert.equal(output.tags.filter(([c,v])=>c===100&&v==='AcDbHelix').length,1);
});
test('HELIX duplicate fields fail before validating a second field value',()=>{
  const input=reader([...splinePacket(),[100,'AcDbHelix'],[90,29],[90,-1]]);
  assert.throws(()=>io.ReadHelix(input,document()),error=>error.message==='HELIX has duplicate group 90.');assert.equal(input.Code,90);
});
test('HELIX preserves vector completeness and nested setter errors',()=>{
  assert.throws(()=>io.ReadHelix(reader([...splinePacket(),[100,'AcDbHelix'],[10,1]]),document()),error=>error.message==='Incomplete HELIX vector at group 10.');
  assert.throws(()=>io.ReadHelix(reader([...splinePacket(),[100,'AcDbHelix'],[12,0],[22,0],[32,0]]),document()),error=>error.name==='InvalidDataException'&&error.InnerException?.name==='ArgumentOutOfRangeException');
});
test('HELIX validates unused blocks and counts definitions rather than INSERT instances',()=>{
  const d=new api.DxfDocument(13),block=new api.Block('Unused');block.Entities.Add(helix());d.Blocks.Add(block);
  assert.throws(()=>io.ValidateHelixVersions(d),{name:'NotSupportedException'});d.DrawingVariables.AcadVer=18;io.ValidateHelixVersions(d);
  d.Entities.Add(new api.Insert(block));d.Entities.Add(new api.Insert(block));io.PrepareHelixClass(d,d.Classes);assert.equal(d.Classes.get_Item('HELIX').InstanceCount,1);
});
test('HELIX class refresh preserves existing properties and rejects only live conflicts',()=>{
  const d=document(),entry=new api.DxfClass('HELIX','Other','User');entry.IsEntity=false;entry.InstanceCount=17;d.Classes.Add(entry);
  io.PrepareHelixClass(d,d.Classes);assert.equal(entry.InstanceCount,17);d.Entities.Add(helix());assert.throws(()=>io.PrepareHelixClass(d,d.Classes),{name:'InvalidDataException'});
  assert.equal(d.Classes.get_Item('HELIX'),entry);assert.equal(entry.ApplicationName,'User');assert.equal(entry.InstanceCount,17);
});
test('HELIX zero-instance refresh keeps the class identity and resets its count',()=>{
  const d=document(),e=helix();d.Entities.Add(e);io.PrepareHelixClass(d,d.Classes);const entry=d.Classes.get_Item('HELIX');d.Entities.Remove(e);io.PrepareHelixClass(d,d.Classes);
  assert.equal(d.Classes.get_Item('HELIX'),entry);assert.equal(entry.InstanceCount,0);assert.equal(entry.ProxyFlags,4095);
});
test('primitive corpus is deterministic and entirely input-only',()=>{
  const corpus=primitiveIOCorpus();assert.deepEqual(corpus,primitiveIOCorpus());assert.equal(corpus.length,6416);assert.equal(new Set(corpus.map(p=>p.name)).size,6416);assert.equal(corpus.reduce((n,p)=>n+p.request.steps.length,0),25569);assert.ok(corpus.every(p=>!Object.hasOwn(p,'expected')));
});

for(const [property,code]of [['StartTangent',12],['EndTangent',13]])test(property+' cleared during output keeps native nullable error and output prefix',()=>{
  const e=spline();e[property]=new api.Vector3(1,2,3);const output=[];
  assert.throws(()=>io.WriteSpline({Write(c,v){output.push([c,v]);if(c===code)e[property]=null;}},18,e),error=>error.name==='InvalidOperationException'&&error.message==='Nullable object must have a value.');
  assert.equal(output.at(-1)[0],code);assert.equal(e[property],null);
});

test('negative periodic spline degree preserves RemoveRange validation before constructor validation',()=>{
  for(const degree of [-32768,-1]) {
    const tags=splinePacket(3,true).map(([code,value])=>[code,code===71?degree:value]);
    assert.throws(()=>io.ReadSpline(reader(tags),document()),{name:'ArgumentOutOfRangeException',ParamName:'count'});
  }
});

// Source-guided value semantics from pinned DxfWriter.WriteSpline. These are
// supplemental regressions; they do not replace original .NET test identities.
for (const kind of ['Spline', 'Helix']) {
  const wrap = curve => kind === 'Helix' ? new api.Helix(curve) : curve;
  const write = (chunk, entity) => io['Write' + kind](chunk, 18, entity);
  test(kind + ' periodic prefix copies the current vector but reads its weight after coordinate callbacks', () => {
    const e = wrap(spline(2, true)), tail = e.ControlPoints.length - e.Degree;
    const first = e.ControlPoints[tail].ToArray(), tags = []; let count = 0;
    write({Write(code, value) {
      tags.push([code, value]);
      if (code === 10 && count++ === 0) {
        e.ControlPoints[tail].Y = 71; e.ControlPoints[tail].Z = 72;
        e.ControlPoints[tail + 1].X = 81; e.ControlPoints[tail + 1].Y = 82;
        e.Weights[tail] = 9;
      }
    }}, e);
    const values = code => tags.filter(([c]) => c === code).map(([,v]) => v);
    assert.deepEqual([values(10)[0], values(20)[0], values(30)[0]], first);
    assert.equal(values(41)[0], 9); // weight is not part of the copied vector
    assert.equal(values(10)[1], 81); assert.equal(values(20)[1], 82);
    assert.equal(values(20)[e.Degree + tail], 71); // ordinary loop sees live edits
  });
  test(kind + ' fit-point iteration copies current value while observing edits to later elements', () => {
    const e = wrap(new api.Spline([new api.Vector3(1,2,3), new api.Vector3(4,5,6), new api.Vector3(7,8,9)]));
    e.StartTangent = null; e.EndTangent = null; const tags = []; let count = 0;
    write({Write(code, value) {
      tags.push([code, value]);
      if (code === 11 && count++ === 0) {
        e.FitPoints[0].Y = 51; e.FitPoints[0].Z = 52;
        e.FitPoints[1] = new api.Vector3(61,62,63);
      }
    }}, e);
    const values = code => tags.filter(([c]) => c === code).map(([,v]) => v);
    assert.deepEqual([values(11)[0], values(21)[0], values(31)[0]], [1,2,3]);
    assert.deepEqual([values(11)[1], values(21)[1], values(31)[1]], [61,62,63]);
  });
  test(kind + ' fit-point enumeration keeps the selected array if its property changes', () => {
    const e = wrap(new api.Spline([new api.Vector3(1,2,3), new api.Vector3(4,5,6), new api.Vector3(7,8,9)]));
    e.StartTangent = null; e.EndTangent = null; let selected = e.FitPoints, reads = 0, first = true; const tags = [];
    const proxy = new Proxy(e, {get(target, key) { if (key === 'FitPoints') { reads++; return selected; } return Reflect.get(target, key, target); }});
    write({Write(code, value) { tags.push([code, value]); if (code === 11 && first) { first = false; selected = [new api.Vector3(91,92,93)]; } }}, proxy);
    assert.equal(reads, 1);
    assert.deepEqual(tags.filter(([c]) => c === 11).slice(0,3).map(([,v]) => v), [1,4,7]);
  });
  test(kind + ' ordinary controls remain live between component writes', () => {
    const e = wrap(spline()), tags = []; let first = true;
    write({Write(code, value) { tags.push([code,value]); if (code === 10 && first) { first = false; e.ControlPoints[0].Y = 81; e.ControlPoints[0].Z = 82; e.Weights[0] = 7; } }}, e);
    for (const [code, expected] of [[20,81],[30,82],[41,7]]) assert.equal(tags.find(([c]) => c === code)[1], expected);
  });
  test(kind + ' a periodic prefix write failure does not read the following vector', () => {
    const e = wrap(spline(2,true)), tail = e.ControlPoints.length - e.Degree, failure = new Error('periodic output'), tags = []; let nextReads = 0;
    const points = new Proxy(e.ControlPoints, {get(target,key) { if (key === String(tail + 1)) nextReads++; return Reflect.get(target,key); }});
    const proxy = new Proxy(e, {get(target,key) { return key === 'ControlPoints' ? points : Reflect.get(target,key,target); }});
    assert.throws(() => write({Write(code,value) { tags.push([code,value]); if (code === 10) throw failure; }}, proxy), error => error === failure);
    assert.equal(tags.at(-1)[0], 10); assert.equal(nextReads, 0);
  });
  test(kind + ' a fit-point write failure stops before the next iteration', () => {
    const e = wrap(new api.Spline([new api.Vector3(1,2,3), new api.Vector3(4,5,6), new api.Vector3(7,8,9)]));
    e.StartTangent = null; e.EndTangent = null; const failure = new Error('fit output'), tags = []; let nextReads = 0;
    const points = new Proxy(e.FitPoints, {get(target,key) { if (key === '1') nextReads++; return Reflect.get(target,key); }});
    const proxy = new Proxy(e, {get(target,key) { return key === 'FitPoints' ? points : Reflect.get(target,key,target); }});
    assert.throws(() => write({Write(code,value) { tags.push([code,value]); if (code === 11) throw failure; }}, proxy), error => error === failure);
    assert.equal(tags.at(-1)[0], 11); assert.equal(nextReads, 0);
  });
}
