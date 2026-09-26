// Source-guided regressions for Block APIs, gradient packets and active DIMSTYLE
// projection. Supplemental identities never replace the original allocation tests.
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import * as api from '../../node-entry.js';
import {DxfReader} from '../../netDxf/IO/DxfReader.js';
import {DxfWriter} from '../../netDxf/IO/DxfWriter.js';
import {EncodeDxfText} from '../../runtime/DxfStringEncoding.js';
import {ProbeMalformedGradient,GradientPacketTags} from '../netDxf.Conformance/HatchGradientPacketTests.js';
import {RawFixtureBytes} from '../netDxf.Conformance/RawDocumentTests.js';
const root=fileURLToPath(new URL('../..',import.meta.url));
const one=items=>{const values=Array.from(items);assert.equal(values.length,1);return values[0];};
function profile(name,action){const old=api.SetTypedIOConfiguration(name);try{return action();}finally{api.SetTypedIOConfiguration(old);}}
function directory(action){const folder=fs.mkdtempSync(path.join(os.tmpdir(),'netdxf-block-'));try{return action(folder);}finally{fs.rmSync(folder,{recursive:true,force:true});}}
function save(doc,binary){const stream=new api.MemoryStream();new DxfWriter().Write(stream,doc,binary);return stream.ToArray();}
function header(doc,binary){const raw=api.DxfRawDocument.Load(save(doc,binary)),section=one(Array.from(raw.Sections).filter(s=>s.Name==='HEADER')),values=new Map();let name=null;
  for(const tag of Array.from(section.Records).flatMap(record=>Array.from(record.Tags))){if(tag.Code===9){name=tag.Value;assert.ok(!values.has(name),'Duplicate header variable '+name);values.set(name,[]);}else if(name)values.get(name).push([tag.Code,tag.Value]);}return values;}

for(const configuration of ['Debug','Release'])for(let version=13;version<=18;version++)for(const binary of [false,true])for(let fault=0;fault<34;fault++)
  test(`gradient invalid functional/${configuration}/${version}/${binary}/${fault}`,()=>profile(configuration,()=>ProbeMalformedGradient(version,binary,fault)));
for(const [name,type] of Object.entries(api.HatchGradientPatternType))for(const binary of [false,true])
  test(`gradient lowercase known name/${name}/${binary}`,()=>{const tags=GradientPacketTags(18,type,0).map(tag=>tag.Code===470?new api.DxfTag(470,tag.Value.toLowerCase()):tag);
    const doc=new DxfReader().Read(RawFixtureBytes(tags,binary));assert.equal(one(doc.Entities.Hatches).Pattern.GradientType,type);});

for(const module of ['netDxf/Blocks/Block.js','netDxf/DxfDocument.js','netDxf/Tables/DimensionStyleOverride.js','netDxf/IO/DxfReader.js','netDxf/IO/DxfWriter.js','index.js','node-entry.js'])
  test(`typed deep import initializes cyclic models/${module}`,()=>{const source=`const api=await import(${JSON.stringify('./'+module)});if(Object.keys(api).length===0)throw Error('empty module');`;
    const result=spawnSync(process.execPath,['--input-type=module','-e',source],{cwd:root,encoding:'utf8',timeout:20000});assert.equal(result.status,0,result.stderr);});

test('Block.Create preserves model-space geometry, definitions, units and independent data',()=>{
  const doc=new api.DxfDocument(18),line=new api.Line(new api.Vector3(1,2,3),new api.Vector3(4,5,6));doc.DrawingVariables.InsBase=new api.Vector3(9,8,7);doc.DrawingVariables.InsUnits=4;doc.Entities.Add(line);
  const definition=new api.AttributeDefinition('TAG');definition.Value='default';definition.Prompt='prompt';doc.Layouts.get_Item('Model').AssociatedBlock.AttributeDefinitions.Add(definition);
  const data=new api.XData(new api.ApplicationRegistry('BLOCK_COPY'));data.XDataRecord.Add(new api.XDataRecord(1000,'payload'));line.XData.Add(data);
  doc.Layouts.Add(new api.Layout('Paper'));doc.Entities.ActiveLayout='Paper';doc.Entities.Add(new api.Circle(api.Vector3.Zero,2));
  const copy=api.Block.Create(doc,'Copy'),cloned=one(copy.Entities);assert.equal(cloned.constructor,api.Line);assert.notEqual(cloned,line);assert.equal(cloned.Owner,copy);assert.equal(cloned.Handle,null);
  assert.ok(copy.Origin.Equals(doc.DrawingVariables.InsBase));assert.equal(copy.Record.Units,4);assert.equal(copy.Record.Owner,null);
  assert.equal(copy.AttributeDefinitions.get_Item('TAG').Value,'default');assert.notEqual(copy.AttributeDefinitions.get_Item('TAG'),definition);
  cloned.XData.get_Item('BLOCK_COPY').XDataRecord.Clear();assert.equal(line.XData.get_Item('BLOCK_COPY').XDataRecord.Count,1);assert.equal(doc.Entities.ActiveLayout,'Paper');
});
test('Block.Create follows source active-layout HATCH unlinking even while copying ModelSpace',()=>{
  const doc=new api.DxfDocument(18);const hatch=()=>new api.Hatch(api.HatchPattern.Solid,[new api.HatchBoundaryPath([new api.Circle(api.Vector3.Zero,2)])],true);
  const model=hatch();doc.Entities.Add(model);doc.Layouts.Add(new api.Layout('Paper'));doc.Entities.ActiveLayout='Paper';const paper=hatch();doc.Entities.Add(paper);
  api.Block.Create(doc,'Copy');assert.equal(paper.Associative,false);assert.equal(model.Associative,true);assert.equal(doc.Entities.ActiveLayout,'Paper');
});
test('Block.Create argument validation precedes source HATCH side effects',()=>{
  assert.throws(()=>api.Block.Create(null,'Copy'),{name:'ArgumentNullException',ParamName:'doc'});
  const doc=new api.DxfDocument(),h=new api.Hatch(api.HatchPattern.Solid,[new api.HatchBoundaryPath([new api.Circle(api.Vector3.Zero,2)])],true);doc.Entities.Add(h);
  assert.throws(()=>api.Block.Create(doc,''),{name:'ArgumentException',ParamName:'name'});assert.equal(h.Associative,true);
});
test('Block.Create relinks cloned MTEXT columns to the cloned entity collection',()=>{
  const doc=new api.DxfDocument(),text=new api.MText('abcd');text.Columns=new api.MTextColumns();text.Columns.Count=2;text.Columns.Width=2;text.Columns.Gutter=1;
  const columns=text.ConvertToLinkedColumns(['ab','cd']);for(const item of columns)doc.Entities.Add(item);
  const copy=api.Block.Create(doc,'Copy'),texts=Array.from(copy.Entities);assert.equal(texts[0].Columns.LinkedColumns.get_Item(0),texts[1]);assert.notEqual(texts[1],columns.get_Item(1));assert.equal(texts[1].Owner,copy);
});
for(const configuration of ['Release','Debug'])for(let version=13;version<=18;version++)for(const binary of [false,true])
  test(`Block.Save/Load file overloads/${configuration}/${version}/${binary}`,()=>profile(configuration,()=>directory(folder=>{
    const file=path.join(folder,'Block Ω.dxf'),source=new api.Block('Source',[new api.Line(new api.Vector3(1,2,3),new api.Vector3(4,5,6))],[new api.AttributeDefinition('TAG')]);source.Origin=new api.Vector3(7,8,9);source.Record.Units=4;
    assert.equal(binary?source.Save(file,version,true):source.Save(file,version),true);assert.equal(source.Handle,null);assert.equal(source.Entities.get_Item(0).Handle,null);
    for(const restored of [api.Block.Load(file),api.Block.Load(file,[folder]),api.Block.Load(file,'Named'),api.Block.Load(file,'Named',[folder]),api.Block.Load(file,null,[folder])]){
      assert.ok(restored instanceof api.Block);assert.ok(['Block Ω','Named'].includes(restored.Name));assert.ok(restored.Origin.Equals(source.Origin));assert.equal(restored.Record.Units,4);assert.equal(restored.AttributeDefinitions.Count,1);assert.ok(one(restored.Entities).EndPoint.Equals(source.Entities.get_Item(0).EndPoint));assert.equal(restored.Record.Owner,null);
    }
    assert.deepEqual(fs.readdirSync(folder),['Block Ω.dxf']);
  })));
for(const configuration of ['Debug','Release'])test(`Block.Load failure boundary/${configuration}`,()=>profile(configuration,()=>directory(folder=>{
  const absent=path.join(folder,'absent.dxf');if(configuration==='Debug')assert.throws(()=>api.Block.Load(absent));else assert.equal(api.Block.Load(absent),null);
  const oldVersion=path.join(folder,'old.dxf');fs.writeFileSync(oldVersion,'0\r\nSECTION\r\n2\r\nHEADER\r\n9\r\n$ACADVER\r\n1\r\nAC1009\r\n0\r\nENDSEC\r\n0\r\nEOF\r\n');
  if(configuration==='Debug')assert.throws(()=>api.Block.Load(oldVersion),{name:'DxfVersionNotSupportedException'});else assert.equal(api.Block.Load(oldVersion),null);
  const invalid=path.join(folder,'invalid.dxf');fs.writeFileSync(invalid,'0\r\nSECTION\r\n2\r\nHEADER\r\n9\r\n$ACADVER\r\n1\r\nAC1032\r\n');
  if(configuration==='Debug')assert.throws(()=>api.Block.Load(invalid));else{
    assert.throws(()=>api.Block.Load(invalid),{name:'NullReferenceException'});assert.throws(()=>api.Block.Load(invalid,'Explicit'),{name:'ArgumentNullException',ParamName:'doc'});
  }
})));
test('Block.Create and Save refuse opaque geometry before changing the destination',()=>directory(folder=>{
  const source=new api.DxfDocument();source.Entities.Add(new api.Line());const text=new TextDecoder().decode(save(source,false)).replace('\r\nLINE\r\n','\r\nCUSTOM_UNKNOWN\r\n');const doc=new DxfReader().Read(new TextEncoder().encode(text));
  assert.throws(()=>api.Block.Create(doc,'Copy'),{name:'NotSupportedException'});const file=path.join(folder,'old.dxf');fs.writeFileSync(file,'original');
  assert.throws(()=>doc.Layouts.get_Item('Model').AssociatedBlock.Save(file,18),{name:'NotSupportedException'});assert.equal(fs.readFileSync(file,'utf8'),'original');
}));

const expectedZero=mask=>((mask&4)?((mask&8)?0:3):((mask&8)?2:1))|((mask&1)?4:0)|((mask&2)?8:0);
function setZeros(target,mask,prefix=''){for(const [bit,name] of [[1,'SuppressLinearLeadingZeros'],[2,'SuppressLinearTrailingZeros'],[4,'SuppressZeroFeet'],[8,'SuppressZeroInches']])target[prefix+name]=!!(mask&bit);}
for(let version=13;version<=18;version++)for(const binary of [false,true])for(let mask=0;mask<16;mask++)
  test(`active DIMSTYLE zero suppression/${version}/${binary}/${mask}`,()=>{
    const doc=new api.DxfDocument(version),style=doc.DimensionStyles.get_Item(doc.DrawingVariables.DimStyle);setZeros(style,mask);setZeros(style.AlternateUnits,mask);setZeros(style.Tolerances,mask);setZeros(style.Tolerances,mask,'Alternate');style.TextDirection=1;
    const variables=header(doc,binary);for(const name of ['$DIMZIN','$DIMALTZ','$DIMTZIN','$DIMALTTZ'])assert.deepEqual(variables.get(name),[[70,expectedZero(mask)]],name);
    assert.deepEqual(variables.get('$DIMTXTDIRECTION'),[[70,1]]);assert.deepEqual(variables.get('$DIMTOL'),[[70,0]]);assert.deepEqual(variables.get('$DIMLIM'),[[70,0]]);
  });
for(const binary of [false,true])for(const mode of [0,1,2,3])for(const lower of [0,.125])
  test(`active DIMSTYLE tolerances/${binary}/${mode}/${lower}`,()=>{
    const doc=new api.DxfDocument(18),style=doc.DimensionStyles.get_Item('Standard');style.Tolerances.DisplayMethod=mode;style.Tolerances.LowerLimit=lower;style.Tolerances.UpperLimit=.25;style.TextFillColor=new api.AciColor(4);
    const h=header(doc,binary);assert.deepEqual(h.get('$DIMTOL'),[[70,mode===1||mode===2?1:0]]);assert.deepEqual(h.get('$DIMLIM'),[[70,mode===3?1:0]]);
    assert.deepEqual(h.get('$DIMTM'),[[40,mode===2&&lower===0?api.MathHelper.Epsilon:lower]]);assert.deepEqual(h.get('$DIMTP'),[[40,.25]]);assert.deepEqual(h.get('$DIMTFILL'),[[70,2]]);assert.deepEqual(h.get('$DIMTFILLCLR'),[[70,4]]);
  });
function projection(style,callback=()=>{}){const writer=new DxfWriter(),doc=new api.DxfDocument(18),tags=[];writer.doc=doc;writer.chunk={Write(code,value){tags.push([code,value]);callback(code,value);}};writer.WriteActiveDimensionStyleSystemVariables(style);return tags;}
const defaultOrder=['DIMADEC','DIMALT','DIMALTD','DIMALTF','DIMALTRND','DIMALTTD','DIMALTTZ','DIMALTU','DIMALTZ','DIMAPOST','DIMATFIT','DIMAUNIT','DIMASZ','DIMAZIN','DIMSAH','DIMBLK','DIMLDRBLK','DIMCEN','DIMCLRD','DIMCLRE','DIMCLRT','DIMDEC','DIMDLE','DIMDLI','DIMDSEP','DIMEXE','DIMEXO','DIMFXLON','DIMFXL','DIMGAP','DIMJUST','DIMLFAC','DIMLUNIT','DIMLWD','DIMLWE','DIMPOST','DIMRND','DIMSCALE','DIMSD1','DIMSD2','DIMSE1','DIMSE2','DIMSOXD','DIMTAD','DIMTDEC','DIMTFAC','DIMTIH','DIMTIX','DIMTM','DIMTMOVE','DIMTOFL','DIMTOH','DIMTOL','DIMLIM','DIMTOLJ','DIMTP','DIMTXT','DIMTXTDIRECTION','DIMTZIN','DIMZIN','DIMFRAC','DIMLTYPE','DIMLTEX1','DIMLTEX2'];
test('active DIMSTYLE emits the pinned header ordering without invented fields',()=>{const tags=projection(api.DimensionStyle.Default);assert.deepEqual(tags.filter(([code])=>code===9).map(([,name])=>name),defaultOrder.map(n=>'$'+n));assert.equal(tags.length,defaultOrder.length*2);});
for(const [first,second,expected] of [[null,null,[['$DIMSAH',0],['$DIMBLK','']]],[null,'Second',[['$DIMSAH',1],['$DIMBLK1',''],['$DIMBLK2','Second']]],['First',null,[['$DIMSAH',1],['$DIMBLK1','First'],['$DIMBLK2','']]],['Same','sAME',[['$DIMSAH',0],['$DIMBLK','Same']]],['First','Second',[['$DIMSAH',1],['$DIMBLK1','First'],['$DIMBLK2','Second']]]])
  test(`active DIMSTYLE arrow names/${first}/${second}`,()=>{const style=api.DimensionStyle.Default;style.DimArrow1=first===null?null:new api.Block(first);style.DimArrow2=second===null?null:new api.Block(second);const tags=projection(style),start=tags.findIndex(t=>t[1]==='$DIMSAH'),end=tags.findIndex(t=>t[1]==='$DIMLDRBLK');const pairs=[];for(let i=start;i<end;i+=2)pairs.push([tags[i][1],tags[i+1][1]]);assert.deepEqual(pairs,expected);});
for(const units of [1,2,3,4,5])for(const stacked of [false,true])test(`active DIMSTYLE alternate units/${units}/${stacked}`,()=>{const style=api.DimensionStyle.Default;style.AlternateUnits.LengthUnits=units;style.AlternateUnits.StackUnits=stacked;const tags=projection(style),at=tags.findIndex(t=>t[1]==='$DIMALTU');assert.deepEqual(tags[at+1],[70,units<=3?units:units===4?(stacked?4:6):(stacked?5:7)]);});
test('active DIMSTYLE values are read after the corresponding name callback',()=>{
  const style=api.DimensionStyle.Default;const tags=projection(style,(code,value)=>{if(code!==9)return;if(value==='$DIMTXT')style.TextHeight=8;if(value==='$DIMZIN')setZeros(style,3);if(value==='$DIMTOL')style.Tolerances.DisplayMethod=3;});
  const after=name=>tags[tags.findIndex(t=>t[1]===name)+1];assert.deepEqual(after('$DIMTXT'),[40,8]);assert.deepEqual(after('$DIMZIN'),[70,expectedZero(3)]);assert.deepEqual(after('$DIMTOL'),[70,0]);assert.deepEqual(after('$DIMLIM'),[70,0],'The tolerance branch was selected before its callbacks.');
});
test('active DIMSTYLE affix placeholders and conditional values follow pinned source',()=>{
  const style=api.DimensionStyle.Default;style.DimPrefix='';style.DimSuffix=' suffix';style.AlternateUnits.Prefix='Alt';style.AlternateUnits.Suffix=' unit';let tags=projection(style);let at=tags.findIndex(t=>t[1]==='$DIMAPOST');assert.deepEqual(tags[at+1],[1,'Alt unit']);
  style.DimPrefix='Ω';tags=projection(style);at=tags.findIndex(t=>t[1]==='$DIMAPOST');assert.deepEqual(tags[at+1],[1,'Alt[] unit']);at=tags.findIndex(t=>t[1]==='$DIMPOST');assert.deepEqual(tags[at+1],[1,EncodeDxfText('Ω<> suffix',18)]);
});
