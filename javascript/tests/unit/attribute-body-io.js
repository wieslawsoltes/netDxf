// Source-guided regressions for pinned ReadAttribute body order, defaults and admission.
import test from 'node:test';
import assert from 'node:assert/strict';
import { DxfDocument, DxfRawDocument, DxfTag, MemoryStream, AttributeDefinition, Block, Insert, TextStyle, MathHelper, TextAlignment, Text } from '../../index.js';
import { DxfReader } from '../../netDxf/IO/DxfReader.js';
import { ArgumentException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';
const T=(c,v)=>new DxfTag(c,v);
function fixture(binary,edit){const doc=new DxfDocument(),block=new Block('ATTRIBUTE_BODY');block.AttributeDefinitions.Add(new AttributeDefinition('TAG'));const style=new TextStyle('BODY_WIDTH','txt.shx');style.WidthFactor=2.5;doc.TextStyles.Add(style);doc.Entities.Add(new Insert(block));const stream=new MemoryStream();try{assert.equal(doc.Save(stream,binary),true);stream.Position=0;const raw=DxfRawDocument.Load(stream),record=raw.Sections.flatMap(s=>s.Records).find(r=>r.Name==='ATTRIB');assert.ok(record);const tags=Array.from(record.Tags),common=tags.findIndex(t=>t.Code===100),start=tags.findIndex((t,i)=>i>common&&t.Code===100);return raw.WithRecord(record,edit(tags,start));}finally{stream.Dispose();}}
function fields(tags,start,replacements){const codes=new Set(replacements.map(([c])=>c)),body=tags.slice(start+1).filter(t=>!codes.has(t.Code));return [...tags.slice(0,start+1),...body,...replacements.map(([c,v])=>T(c,v))];}
function read(raw,binary,reader=new DxfReader()){const bytes=new MemoryStream();try{raw.Save(bytes,binary);bytes.Position=0;return reader.Read(bytes);}finally{bytes.Dispose();}}
const attributes=doc=>Array.from(Array.from(doc.Entities.Inserts)[0].Attributes);
const attribute=doc=>{const all=attributes(doc);assert.equal(all.length,1);return all[0];};
for(const binary of [false,true]){
  for(const horizontal of [3,5]){
    test(`ATTRIB alignment ${horizontal} preserves stored rotation / ${binary}`,()=>{
      const raw=fixture(binary,(t,b)=>fields(t,b,[[72,horizontal],[74,0],[10,1],[20,2],[30,3],[11,5],[21,2],[31,3],[50,37]])),a=attribute(read(raw,binary));
      assert.equal(a.Alignment,horizontal===3?TextAlignment.Aligned:TextAlignment.Fit);assert.equal(a.Width,4);assert.equal(a.Rotation,37);assert.deepEqual([a.Position.X,a.Position.Y,a.Position.Z],[1,2,3]);
    });
    test(`ATTRIB degenerate aligned width recovers to one ${horizontal} / ${binary}`,()=>{
      const raw=fixture(binary,(t,b)=>fields(t,b,[[72,horizontal],[74,0],[10,0],[20,0],[30,0],[11,0],[21,0],[31,0]]));assert.equal(attribute(read(raw,binary)).Width,1);
    });
  }
  for(const width of [0,MathHelper.Epsilon/2])test(`ATTRIB zero-width factor uses final style ${width} / ${binary}`,()=>{
    const raw=fixture(binary,(t,b)=>fields(t,b,[[41,width],[7,'BODY_WIDTH']]));assert.equal(attribute(read(raw,binary)).WidthFactor,2.5);
  });
  for(const [wire,expected]of[[350,-10],[270,0],[85,85],[-85,-85],[445,85],[-275,85]])test(`ATTRIB oblique recovery ${wire} / ${binary}`,()=>{
    const raw=fixture(binary,(t,b)=>fields(t,b,[[51,wire]]));assert.equal(attribute(read(raw,binary)).ObliqueAngle,expected);
  });
  for(const height of [0,-1])test(`ATTRIB rejects nonpositive height ${height} instead of inventing a default / ${binary}`,()=>{
    assert.throws(()=>read(fixture(binary,(t,b)=>fields(t,b,[[40,height]])),binary),ArgumentOutOfRangeException);
  });
  test(`ATTRIB omitted height retains the source zero until setter validation / ${binary}`,()=>{
    assert.throws(()=>read(fixture(binary,(t,b)=>t.filter((x,i)=>i<=b||x.Code!==40)),binary),ArgumentOutOfRangeException);
  });
  test(`ATTRIB repeated text flags accumulate source recognized values / ${binary}`,()=>{
    const a=attribute(read(fixture(binary,(t,b)=>fields(t,b,[[71,2],[71,4],[71,0]])),binary));assert.equal(a.IsBackward,true);assert.equal(a.IsUpsideDown,true);
  });
  test(`ATTRIB unrecognized generation bits do not become flags / ${binary}`,()=>{
    const a=attribute(read(fixture(binary,(t,b)=>fields(t,b,[[71,3]])),binary));assert.equal(a.IsBackward,false);assert.equal(a.IsUpsideDown,false);
  });
  test(`ATTRIB ordered scalars after XData are read without losing the metadata / ${binary}`,()=>{
    const raw=fixture(binary,(t,b)=>fields(t,b,[[1,'first'],[10,4],[1001,'BODY_META'],[1000,'metadata'],[1,'last'],[10,9],[50,27]])),a=attribute(read(raw,binary));
    assert.equal(a.Value,'last');assert.equal(a.Position.X,9);assert.equal(a.Rotation,27);assert.equal(a.XData.get_Item('BODY_META').XDataRecord.get_Item(0).Value,'metadata');
  });
  test(`ATTRIB registers styles in wire order, including superseded styles / ${binary}`,()=>{
    const doc=read(fixture(binary,(t,b)=>fields(t,b,[[7,'FIRST_BODY_STYLE'],[7,'BODY_WIDTH']])),binary);assert.ok(doc.TextStyles.get_Item('FIRST_BODY_STYLE'));assert.equal(attribute(doc).Style,doc.TextStyles.get_Item('BODY_WIDTH'));assert.equal(attribute(doc).WidthFactor,1);
  });
  test(`ATTRIB invalid alignment pair recovers to baseline-left / ${binary}`,()=>{
    const a=attribute(read(fixture(binary,(t,b)=>fields(t,b,[[72,6],[74,3],[10,7],[20,8],[30,9],[11,1],[21,2],[31,3]])),binary));assert.equal(a.Alignment,TextAlignment.BaselineLeft);assert.deepEqual([a.Position.X,a.Position.Y,a.Position.Z],[7,8,9]);
  });
  test(`ATTRIB absent second alignment point does not fall back to the first / ${binary}`,()=>{
    const raw=fixture(binary,(t,b)=>fields(t,b,[[72,1],[74,0],[10,7],[20,8],[30,9]]).filter((x,i)=>i<=b||![11,21,31].includes(x.Code)));const a=attribute(read(raw,binary));assert.deepEqual([a.Position.X,a.Position.Y,a.Position.Z],[0,0,0]);
  });
  test(`ATTRIB final empty tag is discarded after reading styles / ${binary}`,()=>{
    const doc=read(fixture(binary,(t,b)=>fields(t,b,[[2,'TAG'],[7,'STYLE_ON_DISCARDED_ATTRIBUTE'],[2,''],[40,-1]])),binary);assert.equal(attributes(doc).length,0);assert.ok(doc.TextStyles.get_Item('STYLE_ON_DISCARDED_ATTRIBUTE'));assert.throws(()=>new AttributeDefinition(''),ArgumentException);
  });
  test(`ATTRIB later nonempty tag restores admission / ${binary}`,()=>{
    const a=attribute(read(fixture(binary,(t,b)=>fields(t,b,[[2,''],[2,'TAG']])),binary));assert.equal(a.Tag,'TAG');assert.notEqual(a.Definition,null);
  });
  test(`ATTRIB unrecognized body/private markers do not blanket-filter native scalar reads / ${binary}`,()=>{
    const a=attribute(read(fixture(binary,(t,b)=>fields(t,b,[[102,'{PRIVATE'],[1,'native-ordered'],[102,'}']])),binary));assert.equal(a.Value,'native-ordered');
  });
  test(`ATTRIB body resource failure precedes empty-tag discard / ${binary}`,()=>{
    const reader=new DxfReader(),original=reader.Resource,failure=new Error('body resource callback');
    reader.Resource=function(property,name){if(property==='TextStyles'&&name==='THROW_BODY_STYLE')throw failure;return original.call(this,property,name);};
    assert.throws(()=>read(fixture(binary,(t,b)=>fields(t,b,[[2,''],[7,'THROW_BODY_STYLE']])),binary,reader),error=>error===failure);
  });
}

// TEXT and ATTDEF share wire fields but deliberately differ in recovery and XData registration.
function annotationFixture(kind,binary,edit){
  const doc=new DxfDocument(),style=new TextStyle('ANNOTATION_WIDTH','txt.shx');
  style.WidthFactor=2.5;doc.TextStyles.Add(style);doc.DrawingVariables.TextSize=3.25;doc.DrawingVariables.TextStyle=style.Name;
  if(kind==='TEXT')doc.Entities.Add(new Text());
  else{const block=new Block('ANNOTATION_BODY');block.AttributeDefinitions.Add(new AttributeDefinition('TAG'));doc.Blocks.Add(block);}
  const stream=new MemoryStream();
  try{assert.equal(doc.Save(stream,binary),true);stream.Position=0;const raw=DxfRawDocument.Load(stream),record=raw.Sections.flatMap(s=>s.Records).find(r=>r.Name===kind);
    assert.ok(record);const tags=Array.from(record.Tags),common=tags.findIndex(t=>t.Code===100),body=tags.findIndex((t,i)=>i>common&&t.Code===100);
    return raw.WithRecord(record,edit(tags,body));
  }finally{stream.Dispose();}
}
const annotation=(doc,kind)=>kind==='TEXT'?Array.from(doc.Entities.Texts)[0]:doc.Blocks.get_Item('ANNOTATION_BODY').AttributeDefinitions.get_Item('TAG');
for(const kind of ['TEXT','ATTDEF'])for(const binary of [false,true]){
  const vertical=kind==='TEXT'?73:74;
  const fixture=edit=>annotationFixture(kind,binary,edit);
  const loaded=edit=>annotation(read(fixture(edit),binary),kind);
  for(const horizontal of [3,5]){
    test(`${kind} aligned ${horizontal} uses stored rotation and three-dimensional width / ${binary}`,()=>{
      const item=loaded((t,b)=>fields(t,b,[[72,horizontal],[vertical,0],[10,1],[20,2],[30,3],[11,4],[21,6],[31,3],[50,37]]));
      assert.equal(item.Alignment,horizontal===3?TextAlignment.Aligned:TextAlignment.Fit);assert.equal(item.Width,5);assert.equal(item.Rotation,37);assert.deepEqual([item.Position.X,item.Position.Y,item.Position.Z],[1,2,3]);
    });
    test(`${kind} degenerate aligned ${horizontal} width recovers without inferring rotation / ${binary}`,()=>{
      const item=loaded((t,b)=>fields(t,b,[[72,horizontal],[vertical,0],[10,0],[20,0],[30,0],[11,0],[21,0],[31,0],[50,27]]));assert.equal(item.Width,1);assert.equal(item.Rotation,27);
    });
  }
  test(`${kind} final body fields after XData remain effective / ${binary}`,()=>{
    const item=loaded((t,b)=>fields(t,b,[[1,'early'],[10,4],[1001,'ANNOTATION_META'],[1000,'retained'],[1,'late'],[10,9],[50,27]]));assert.equal(item.Value,'late');assert.equal(item.Position.X,9);assert.equal(item.Rotation,27);assert.equal(item.XData.get_Item('ANNOTATION_META').XDataRecord.get_Item(0).Value,'retained');
  });
  test(`${kind} superseded text styles are registered in body order / ${binary}`,()=>{
    const doc=read(fixture((t,b)=>fields(t,b,[[7,'FIRST_ANNOTATION_STYLE'],[7,'ANNOTATION_WIDTH']])),binary);assert.ok(doc.TextStyles.get_Item('FIRST_ANNOTATION_STYLE'));assert.equal(annotation(doc,kind).Style,doc.TextStyles.get_Item('ANNOTATION_WIDTH'));
  });
  test(`${kind} recognized text-generation flags accumulate / ${binary}`,()=>{
    const item=loaded((t,b)=>fields(t,b,[[71,2],[71,4],[71,0]]));assert.equal(item.IsBackward,true);assert.equal(item.IsUpsideDown,true);
  });
  test(`${kind} unrecognized text-generation bits are not flags / ${binary}`,()=>{
    const item=loaded((t,b)=>fields(t,b,[[71,3]]));assert.equal(item.IsBackward,false);assert.equal(item.IsUpsideDown,false);
  });
  test(`${kind} invalid alignment pair falls back to baseline-left / ${binary}`,()=>{
    const item=loaded((t,b)=>fields(t,b,[[72,6],[vertical,3],[10,7],[20,8],[30,9],[11,1],[21,2],[31,3]]));assert.equal(item.Alignment,TextAlignment.BaselineLeft);assert.deepEqual([item.Position.X,item.Position.Y,item.Position.Z],[7,8,9]);
  });
  test(`${kind} absent second alignment point remains zero / ${binary}`,()=>{
    const item=loaded((t,b)=>fields(t,b,[[72,1],[vertical,0],[10,7],[20,8],[30,9]]).filter((x,i)=>i<=b||![11,21,31].includes(x.Code)));assert.deepEqual([item.Position.X,item.Position.Y,item.Position.Z],[0,0,0]);
  });
  test(`${kind} omitted height reaches setter validation / ${binary}`,()=>{
    assert.throws(()=>loaded((t,b)=>t.filter((x,i)=>i<=b||x.Code!==40)),ArgumentOutOfRangeException);
  });
  test(`${kind} body scope retains ordered scalar updates in private packets / ${binary}`,()=>{
    const item=loaded((t,b)=>fields(t,b,[[102,'{PRIVATE'],[1,'ordered-private'],[102,'}']]));assert.equal(item.Value,'ordered-private');
  });
  test(`${kind} empty style follows its distinct header-default policy / ${binary}`,()=>{
    const item=loaded((t,b)=>fields(t,b,[[7,'']]));assert.equal(item.Style.Name,kind==='TEXT'?'ANNOTATION_WIDTH':'Standard');
  });
  test(`${kind} omitted style does not use the current header style / ${binary}`,()=>{
    const item=loaded((t,b)=>t.filter((x,i)=>i<=b||x.Code!==7));assert.equal(item.Style.Name,'Standard');
  });
  test(`${kind} XData registry phase precedes or follows construction as in source / ${binary}`,()=>{
    const reader=new DxfReader(),raw=fixture((t,b)=>[...t.filter((x,i)=>i<=b||x.Code!==40),T(1001,'ANNOTATION_PHASE'),T(1000,'metadata')]);
    assert.throws(()=>read(raw,binary,reader),ArgumentOutOfRangeException);assert.equal(reader.doc.ApplicationRegistries.Contains('ANNOTATION_PHASE'),kind==='TEXT');
  });
  for(const [wire,expected]of(kind==='TEXT'?[[350,0],[85,85],[-85,-85],[86,0]]:[[350,-10],[85,85],[-85,-85],[445,85]])){
    test(`${kind} source-specific oblique recovery ${wire} / ${binary}`,()=>assert.equal(loaded((t,b)=>fields(t,b,[[51,wire]])).ObliqueAngle,expected));
  }
  if(kind==='TEXT'){
    for(const height of [0,-1])test(`TEXT nonpositive height ${height} uses header TEXTSIZE / ${binary}`,()=>assert.equal(loaded((t,b)=>fields(t,b,[[40,height]])).Height,3.25));
    for(const width of [0,-1,0.001,100.01])test(`TEXT width factor ${width} uses the source TEXTSIZE recovery / ${binary}`,()=>assert.equal(loaded((t,b)=>fields(t,b,[[41,width]])).WidthFactor,3.25));
    for(const width of [.01,100])test(`TEXT width boundary ${width} is retained / ${binary}`,()=>assert.equal(loaded((t,b)=>fields(t,b,[[41,width]])).WidthFactor,width));
  }else{
    for(const height of [0,-1])test(`ATTDEF nonpositive height ${height} is not TEXT recovery / ${binary}`,()=>assert.throws(()=>loaded((t,b)=>fields(t,b,[[40,height]])),ArgumentOutOfRangeException));
    for(const width of [0,MathHelper.Epsilon/2])test(`ATTDEF width ${width} uses final style fallback / ${binary}`,()=>assert.equal(loaded((t,b)=>fields(t,b,[[7,'ANNOTATION_WIDTH'],[41,width]])).WidthFactor,2.5));
    test(`ATTDEF omitted width uses final style fallback / ${binary}`,()=>assert.equal(loaded((t,b)=>fields(t,b,[[7,'ANNOTATION_WIDTH']]).filter((x,i)=>i<=b||x.Code!==41)).WidthFactor,2.5));
    test(`ATTDEF final tag and prompt are read beyond metadata / ${binary}`,()=>{
      const doc=read(fixture((t,b)=>fields(t,b,[[2,'OLD_TAG'],[3,'early'],[1001,'TAG_META'],[1000,'metadata'],[2,'TAG'],[3,'late']])),binary);assert.equal(annotation(doc,kind).Prompt,'late');assert.equal(annotation(doc,kind).Tag,'TAG');
    });
  }
}
