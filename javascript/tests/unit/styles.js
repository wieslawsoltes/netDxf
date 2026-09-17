import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Layer, Linetype, TextStyle, ShapeStyle, TextStyleFontData, LinetypeTextSegment, LinetypeShapeSegment, LinetypeSimpleSegment,
  FontStyle, Vector2, AciColor, Transparency, Lineweight, XData, ApplicationRegistry, XDataCode, XDataRecord } from '../../node-entry.js';
import { SetSupportFileSystem, SupportFileSystem, PathExtension, PathFileNameWithoutExtension } from '../../runtime/SupportFileSystem.js';
import { NodeSupportFileSystem } from '../../runtime/NodeSupportFileSystem.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidOperationException, NotSupportedException, FileNotFoundException, EndOfStreamException } from '../../runtime/Errors.js';
const root=fileURLToPath(new URL('../../../',import.meta.url));
const temporary=action=>{const dir=fs.mkdtempSync(path.join(os.tmpdir(),'netdxf-styles-'));try{return action(dir);}finally{fs.rmSync(dir,{recursive:true,force:true});}};
const data=()=>{const x=new XData(new ApplicationRegistry('APP'));x.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData,Uint8Array.of(1,2,3)));return x;};
test('style metadata is immutable and preserves every signed Int32 flag bit',()=>{
  for(const flags of [-2147483648,0xF3123456|0,-1,0,1,0x01000000,0x02000000,0x03000000,2147483647]){
    const f=new TextStyleFontData('日本😀',flags);assert.equal(f.Flags,flags);assert.equal(f.FontStyle,(flags>>>24)&3);assert.ok(Object.isFrozen(f));assert.throws(()=>{f.Flags=0;},TypeError);
  }
  assert.throws(()=>new TextStyleFontData(null,0),ArgumentNullException);assert.throws(()=>new TextStyleFontData('😀'.repeat(128),0),ArgumentOutOfRangeException);
  for(const flags of [0.5,NaN,Infinity,2147483648,-2147483649])assert.throws(()=>new TextStyleFontData('Family',flags),ArgumentOutOfRangeException);
});
test('font prefix remains a live XData view and failed removal is atomic',()=>{
  const s=new TextStyle('S','asian.shx');s.BigFont='big.shx';s.ExtendedFontData=new TextStyleFontData('Family',0xF3123456|0);
  const x=s.XData.get_Item('ACAD'),registry=x.ApplicationRegistry;
  x.XDataRecord.set_Item(0,new XDataRecord(XDataCode.String,'Direct'));
  assert.equal(s.FontFamilyName,'Direct');x.XDataRecord.Add(new XDataRecord(XDataCode.String,'Tail'));x.XDataRecord.Add(new XDataRecord(XDataCode.Int32,42));
  const before=x.XDataRecord.ToArray();assert.throws(()=>{s.FontFile='new.ttf';},InvalidOperationException);
  assert.equal(s.FontFile,'asian.shx');assert.equal(s.BigFont,'big.shx');assert.deepEqual(x.XDataRecord.ToArray(),before);assert.equal(s.XData.get_Item('ACAD'),x);assert.equal(x.ApplicationRegistry,registry);
});
test('font style editing retains unknown flags and file/family transitions match the source',()=>{
  const s=new TextStyle('S','font.ttf');s.FontStyle=FontStyle.Bold;assert.equal(s.ExtendedFontData,null);
  s.ExtendedFontData=new TextStyleFontData('Family',0xF3123456|0);s.FontStyle=FontStyle.Italic;assert.equal(s.ExtendedFontData.Flags,0xF1123456|0);
  s.FontFamilyName='Replacement';assert.equal(s.FontFile,'');assert.equal(s.BigFont,'');assert.equal(s.ExtendedFontData.Flags,0);
  s.FontFile='asian.shx';assert.equal(s.ExtendedFontData,null);s.BigFont='big.shx';s.FontFile='simplex.shx';assert.equal(s.BigFont,'');
});
test('style clones own XData and exact model fields without aliasing',()=>{
  for(const source of [new TextStyle('T','asian.shx'),new ShapeStyle('S','shapes.shx'),new Linetype('L'),new Layer('Y')]){
    source.XData.Add(data());const copy=source.Clone('COPY'),a=source.XData.get_Item('APP'),b=copy.XData.get_Item('APP');
    assert.notEqual(a,b);assert.notEqual(a.XDataRecord,b.XDataRecord);b.XDataRecord.Clear();assert.equal(a.XDataRecord.Count,1);assert.equal(source.Name,source instanceof TextStyle?'T':source instanceof ShapeStyle?'S':source instanceof Linetype?'L':'Y');
  }
});
test('layer default attributes, reserved names and source clone omissions are explicit',()=>{
  const layer=Layer.Default;assert.equal(layer.Name,'0');assert.equal(layer.IsReserved,true);assert.equal(layer.HasTransparencyAssignment,false);
  assert.throws(()=>{layer.Color=AciColor.ByLayer;},ArgumentException);assert.throws(()=>{layer.Color=AciColor.ByBlock;},ArgumentException);
  for(const value of [Lineweight.ByLayer,Lineweight.ByBlock])assert.throws(()=>{layer.Lineweight=value;},ArgumentException);
  layer.Description='Not copied by pinned C#';assert.equal(layer.Clone().Description,'');assert.equal(layer.Clone().HasTransparencyAssignment,false);
  layer.Transparency=new Transparency(0);assert.equal(layer.Clone().HasTransparencyAssignment,true);assert.notEqual(layer.Clone().Transparency,layer.Transparency);
});
test('named style callback exceptions preserve names; replacements preserve linetype identity',()=>{
  for(const item of [new TextStyle('T','font.shx'),new Linetype('L'),new ShapeStyle('S','s.shx'),new Layer('Y')]){
    const before=item.Name,handler=()=>{throw new InvalidOperationException('failure');};item.NameChanged.Add(handler);assert.throws(()=>{item.Name='NEW';},InvalidOperationException);assert.equal(item.Name,before);item.NameChanged.Remove(handler);item.Name='NEW';assert.equal(item.Name,'NEW');
  }
  const layer=new Layer('L'),substitute=new Linetype('SUB');layer.LinetypeChanged.Add((sender,args)=>{args.NewValue=substitute;});layer.Linetype=new Linetype('PROPOSED');assert.equal(layer.Linetype,substitute);
});
test('fresh linetype presets and all complex segment clones are independent',()=>{
  for(const preset of ['ByLayer','ByBlock','Continuous','Center','DashDot','Dashed','Dot']){const a=Linetype[preset],b=Linetype[preset];assert.notEqual(a,b);a.Segments.Clear();assert.equal(b.Segments.Count,Linetype[preset].Segments.Count);}
  for(const segment of [new LinetypeTextSegment('TEXT',TextStyle.Default,1),new LinetypeShapeSegment('ZIG',new ShapeStyle('SHAPES','ltypeshp.shx'))]){
    segment.Offset=new Vector2(2,3);segment.Offset.X=99;assert.equal(segment.Offset.X,2);const copy=segment.Clone();assert.notEqual(copy.Style,segment.Style);copy.Offset=new Vector2(9,9);assert.equal(segment.Offset.X,2);
  }
});
test('duplicate segment event subscriptions are removed one occurrence at a time',()=>{
  const line=new Linetype('L'),s=new LinetypeTextSegment('T',TextStyle.Default,1),trace=[];
  line.LinetypeTextSegmentStyleChanged.Add(()=>trace.push('changed'));line.Segments.Add(s);line.Segments.Add(s);
  s.Style=new TextStyle('A','s.shx');assert.equal(trace.length,2);line.Segments.Remove(s);s.Style=new TextStyle('B','s.shx');assert.equal(trace.length,3);
  line.Segments.Clear();s.Style=new TextStyle('C','s.shx');assert.equal(trace.length,3);
});
test('segment post-add and post-remove failures retain source mutation/subscription ordering',()=>{
  const line=new Linetype('L'),s=new LinetypeTextSegment('T',TextStyle.Default,1),trace=[];
  const fail=()=>{throw new InvalidOperationException();};line.LinetypeTextSegmentStyleChanged.Add(()=>trace.push('changed'));
  line.LinetypeSegmentAdded.Add(fail);assert.throws(()=>line.Segments.Add(s),InvalidOperationException);assert.equal(line.Segments.Count,1);s.Style=new TextStyle('A','s.shx');assert.equal(trace.length,0);
  line.LinetypeSegmentAdded.Remove(fail);line.Segments.Add(s);line.LinetypeSegmentRemoved.Add(fail);assert.throws(()=>line.Segments.Remove(s),InvalidOperationException);s.Style=new TextStyle('B','s.shx');assert.equal(trace.length,1);
});
test('all unchanged LIN support definitions load and roundtrip through portable text',()=>{
  for(const [name,count] of [['acad.lin',45],['acadiso.lin',60]]){
    const file=path.join(root,'TestDxfDocument/Support',name),names=Linetype.NamesFromFile(file);assert.equal(names.Count,count);
    for(const name of names){const line=Linetype.Load(file,name);assert.equal(line.Name,name);assert.equal(Linetype.LoadText(line.ToLinString(),name).ToLinString(),line.ToLinString());}
  }
});
test('LIN append output retains existing bytes and Unicode names',()=>temporary(dir=>{
  const file=path.join(dir,'żółć-😀.lin'),line=new Linetype('日本😀',[new LinetypeSimpleSegment(0.5),new LinetypeTextSegment('Zażółć',TextStyle.Default,-0.25)],'desc');
  line.Save(file);const first=fs.readFileSync(file);line.Save(file);assert.deepEqual(fs.readFileSync(file),Buffer.concat([first,first]));
  assert.equal(Linetype.Load(file,'日本😀').ToLinString(),line.ToLinString());assert.deepEqual(Linetype.NamesFromFile(file).ToArray(),['日本😀','日本😀']);
}));
test('LIN extension guards and null lookup preserve validation order',()=>{
  assert.throws(()=>Linetype.Load(null,null),ArgumentNullException);assert.throws(()=>Linetype.Load('bad.txt',null),ArgumentException);
  assert.equal(Linetype.Load('does-not-exist.lin',null),null);assert.throws(()=>Linetype.Load('does-not-exist.lin','L'),FileNotFoundException);
});
test('SHX names, duplicate-case lookup and original identifiers are read from unchanged bytes',()=>{
  const file=path.join(root,'TestDxfDocument/Support/ltypeshp.shx'),shape=new ShapeStyle('S',file);
  const names=ShapeStyle.NamesFromFile(file).ToArray();assert.deepEqual(names,['TRACK1','ZIG','BOX','CIRC1','BAT','AMZIGZAG']);
  for(let i=0;i<names.length;i++){assert.equal(shape.ShapeNumber(names[i].toLowerCase()),130+i);assert.equal(shape.ShapeName(130+i),names[i]);assert.equal(shape.ContainsShapeName(names[i]),true);}
  assert.equal(shape.ShapeNumber(null),0);assert.equal(shape.ShapeNumber('missing'),0);assert.equal(shape.ShapeName(-1),'');
});
test('SHX truncated and invalid files fail without hanging or fabricating names',()=>{
  const bytes=fs.readFileSync(path.join(root,'TestDxfDocument/Support/ltypeshp.shx'));
  for(const length of [24,25,26,27,28,29,31,33])assert.throws(()=>ShapeStyle.NamesFromBytes(bytes.subarray(0,length)),EndOfStreamException);
  assert.throws(()=>ShapeStyle.NamesFromBytes(new Uint8Array(24)),ArgumentException);
});
test('support filesystem rejects invalid hosts atomically and restores scoped host bindings',()=>{
  const before=SetSupportFileSystem(NodeSupportFileSystem);
  try{assert.throws(()=>SetSupportFileSystem({}),ArgumentException);assert.equal(SupportFileSystem.Exists(path.join(root,'TestDxfDocument/Support/ltypeshp.shx')),true);
    const old=SetSupportFileSystem(null);assert.throws(()=>SupportFileSystem.Exists('file'),NotSupportedException);SetSupportFileSystem(old);
    assert.equal(SupportFileSystem.Exists(null),false);assert.equal(SupportFileSystem.Exists(''),false);assert.equal(SupportFileSystem.Exists(root),false);
  }finally{SetSupportFileSystem(before);}
});
test('portable path profile is explicit and supports both host separator conventions',()=>{
  const old=SetSupportFileSystem({ReadAllBytes:()=>new Uint8Array(),Exists:()=>false,DirectorySeparators:'/\\',InvalidPathChars:'\0'});
  try{assert.equal(PathExtension('a.b\\file'), '');assert.equal(PathFileNameWithoutExtension('a.b\\file.shx'),'file');}finally{SetSupportFileSystem(old);}
});
