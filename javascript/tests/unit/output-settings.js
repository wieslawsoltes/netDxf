import test from 'node:test';
import assert from 'node:assert/strict';
import { PaperMargin, PlotSettings, DxfPlotSettingsObject, DxfWipeoutVariables, RasterVariables,
  DxfXRecord, DxfDictionary, Line, Attribute, AttributeDefinition, Vector2 } from '../../index.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { jsGeometry } from '../../tools/foundations-wire.mjs';
import { doubleBits, fromBits } from '../../tools/wire.mjs';
const problems = plot => { const errors = new ReferenceList(); PlotSettings.ValidateValues(plot, errors); return errors.ToArray(); };

test('paper margins preserve value copies, signed zero and NaN payloads', () => {
  const m = new PaperMargin(-0, 2, fromBits('FFF8000000001234'), 4), q = Copy(m);
  assert.notEqual(q,m); assert.equal(doubleBits(q.Left),'8000000000000000');
  assert.equal(doubleBits(q.Right),'FFF8000000001234'); assert.equal(q.Equals(m),true);
  q.Bottom=99; assert.equal(m.Bottom,2); assert.equal(q.Equals(m),false);
});
test('plot defaults and all vector/margin properties have value ownership', () => {
  const p = new PlotSettings(); assert.deepEqual(problems(p),[]);
  assert.equal(p.Flags,688); assert.equal(p.PaperSize.X,210); assert.equal(p.PaperMargin.Bottom,20);
  for(const key of ['PaperSize','Origin','WindowBottomLeft','WindowUpRight','PaperImageOrigin']) {
    const v=new Vector2(3,4);p[key]=v;v.X=99;p[key].Y=77;
    assert.equal(p[key].X,3);assert.equal(p[key].Y,4);
  }
  const m=new PaperMargin(1,2,3,4);p.PaperMargin=m;m.Left=99;p.PaperMargin.Right=77;
  assert.equal(p.PaperMargin.Left,1);assert.equal(p.PaperMargin.Right,3);
});
test('standard scale, explicit factor and custom ratio are independent', () => {
  const p=new PlotSettings();p.PrintScaleNumerator=2.5;p.PrintScaleDenominator=20;p.StandardScaleType=25;p.StandardScaleFactor=-0;
  p.ScaleToFit=false;assert.equal(p.StandardScaleType,25);p.ScaleToFit=true;assert.equal(p.StandardScaleType,0);
  p.ScaleToFit=false;assert.equal(p.StandardScaleType,16);assert.equal(p.PrintScale,.125);assert.ok(Object.is(p.StandardScaleFactor,-0));
  p.StandardScaleFactor=null;assert.equal(p.StandardScaleFactor,null);assert.equal(p.PrintScale,.125);
});
test('plot source setters reject nonpositive ratios but defer nonfinite/schema checks', () => {
  const p=new PlotSettings();for(const value of [0,-0,-1,-Infinity])assert.throws(()=>{p.PrintScaleNumerator=value;},ArgumentOutOfRangeException);
  assert.equal(p.PrintScaleNumerator,1);p.PrintScaleNumerator=NaN;assert.ok(Number.isNaN(p.Clone().PrintScaleNumerator));
  assert.deepEqual(problems(p),['Plot settings numeric values must be finite.','Plot settings numeric values must be finite.']);
  p.PrintScaleNumerator=Infinity;p.StandardScaleFactor=1;
  assert.deepEqual(problems(p),['Plot settings numeric values must be finite.']);
});
test('finite explicit factors include negative and zero values with atomic rejection', () => {
  const p=new PlotSettings();p.StandardScaleFactor=-.5;
  for(const bad of [NaN,Infinity,-Infinity]){assert.throws(()=>{p.StandardScaleFactor=bad;},ArgumentOutOfRangeException);assert.equal(p.StandardScaleFactor,-.5);}
  p.StandardScaleFactor=-0;assert.ok(Object.is(p.Clone().StandardScaleFactor,-0));
  for(const n of [-1,33])assert.throws(()=>{p.StandardScaleType=n;},ArgumentOutOfRangeException);
  for(const n of [0,99])assert.throws(()=>{p.ShadePlotDPI=n;},ArgumentOutOfRangeException);
  p.ShadePlotDPI=100;assert.equal(p.Clone().ShadePlotDPI,100);
});
test('plot schema validates UTF-16 without silently sanitizing source strings', () => {
  const p=new PlotSettings();for(const bad of [null,'bad\0text','bad\rtext','bad\ntext','\ud800','\udc00','a\ud800b']){
    p.PageSetupName=bad;assert.equal(p.Clone().PageSetupName,bad);
    assert.deepEqual(problems(p),['Plot settings strings must be nonnull single-line Unicode text.']);
  }
  p.PageSetupName='Zażółć 東京 😀';assert.deepEqual(problems(p),[]);
});
test('plot validation aggregates errors in source order without modifying payload', () => {
  const p=new PlotSettings();p.PageSetupName=null;p.PlotterName='bad\n';p.Origin=new Vector2(NaN,Infinity);
  p.PaperUnits=-1;p.Flags=32768;
  assert.deepEqual(problems(p),[
    'Plot settings strings must be nonnull single-line Unicode text.','Plot settings strings must be nonnull single-line Unicode text.',
    'Plot settings numeric values must be finite.','Plot settings numeric values must be finite.',
    'Invalid plot settings enumeration or shade resolution.','Plot flags must fit their signed 16-bit DXF field.'
  ]);assert.equal(p.PageSetupName,null);assert.equal(p.Flags,32768);
});
test('graphical shade targets reject without disturbing a valid nongraphical target', () => {
  const p=new PlotSettings(),target=new DxfXRecord();p.ShadePlotObject=target;
  for(const invalid of [new Line(),new AttributeDefinition('TAG'),new Attribute(new AttributeDefinition('TAG'))]){
    assert.throws(()=>{p.ShadePlotObject=invalid;},ArgumentException);assert.equal(p.ShadePlotObject,target);
  }
  assert.equal(p.Clone().ShadePlotObject,target);p.ShadePlotObject=null;assert.equal(p.ShadePlotObject,null);
});
test('standalone page setup snapshots the payload but keeps shade identity for graph remapping', () => {
  const p=new PlotSettings(),reference=new DxfXRecord();p.ShadePlotObject=reference;p.StandardScaleFactor=-0;
  const page=new DxfPlotSettingsObject(p),settings=page.Settings;p.PageSetupName='changed';
  assert.equal(settings.PageSetupName,'');assert.equal(settings.ShadePlotObject,reference);
  const clone=page.CloneShell();assert.notEqual(clone.Settings,settings);assert.equal(clone.Settings.ShadePlotObject,reference);
  clone.Settings.StandardScaleFactor=null;assert.ok(Object.is(settings.StandardScaleFactor,-0));
  assert.throws(()=>new DxfPlotSettingsObject(null),ArgumentNullException);
  assert.equal(page.Settings,settings);assert.equal(clone.Handle,null);assert.equal(clone.Owner,null);
});
test('standalone plot references are lazy and explicit remapping can clear targets', () => {
  const page=new DxfPlotSettingsObject(),references=page.DatabaseReferences,target=new DxfXRecord();
  page.Settings.ShadePlotObject=target;assert.deepEqual([...references],[target]);
  const clone=page.CloneShell(),calls=[];page.CopyDatabaseReferencesTo(clone,item=>{calls.push(item);return null;});
  assert.deepEqual(calls,[target]);assert.equal(clone.Settings.ShadePlotObject,null);assert.equal(page.Settings.ShadePlotObject,target);
  page.Settings.ShadePlotObject=null;page.CopyDatabaseReferencesTo(clone,item=>{assert.equal(item,null);return target;});
  assert.equal(clone.Settings.ShadePlotObject,target);assert.deepEqual([...references],[]);
});
test('plot and wipeout schema checks require a dictionary without fabricating a database', () => {
  for(const model of [new DxfPlotSettingsObject(),new DxfWipeoutVariables()]){
    const errors=new ReferenceList();model.ValidateDatabaseSchema(null,errors);assert.equal(errors.Count,1);
    model.Owner=new DxfDictionary();errors.Clear();model.ValidateDatabaseSchema(null,errors);assert.equal(errors.Count,0);
    assert.equal(model.Database,null);
  }
});
test('wipeout shell retains its frame flag and drops document identity', () => {
  const model=new DxfWipeoutVariables();assert.equal(model.DisplayFrame,false);model.DisplayFrame=true;model.Handle='A';model.Owner=new DxfDictionary();
  const clone=model.CloneShell();assert.equal(clone.DisplayFrame,true);assert.equal(clone.Owner,null);assert.equal(clone.Handle,null);
});
test('raster variables retain source defaults and unrestricted enum storage', () => {
  const model=new RasterVariables(null);assert.equal(model.CodeName,'RASTERVARIABLES');assert.equal(model.Owner,null);
  assert.equal(model.DisplayFrame,true);assert.equal(model.DisplayQuality,1);assert.equal(model.Units,0);
  model.DisplayQuality=-1;model.Units=32767;assert.equal(model.DisplayQuality,-1);assert.equal(model.Units,32767);
});
test('oracle reference lookup and resolver dictionaries expose actual key errors', () => {
  assert.deepEqual(jsGeometry({steps:[{kind:'reference-equals',args:[{ref:'missing'},null]}]}),[{ok:false,error:'KeyNotFoundException',param:null}]);
  const prefix=[{kind:'new',type:'Objects.DxfPlotSettingsObject',args:[],id:'p'},{kind:'call',target:'p',member:'CloneShell',args:[],id:'q',nonPublic:true}];
  const call=resolver=>({kind:'call',target:'p',member:'CopyDatabaseReferencesTo',args:[{ref:'q'},{resolver}],nonPublic:true});
  assert.deepEqual(jsGeometry({steps:[...prefix,call([[null,null]])]}).at(-1),{ok:false,error:'ArgumentNullException',param:'key'});
  assert.deepEqual(jsGeometry({steps:[...prefix,call([[{ref:'p'},null],[{ref:'p'},null]])]}).at(-1),{ok:false,error:'ArgumentException',param:null});
});
test('output settings corpus has deterministic unique identities and no embedded expected results', async () => {
  const {outputSettingsCorpus}=await import('../../tools/output-settings-corpus.mjs');const corpus=outputSettingsCorpus();
  assert.deepEqual(corpus,outputSettingsCorpus());assert.equal(new Set(corpus.map(p=>p.name)).size,419);
  assert.equal(corpus.reduce((n,p)=>n+p.request.steps.length,0),3215);assert.ok(corpus.every(p=>!('expected'in p)));
});
