// Source-guided regressions for pinned ReadAttribute body order, defaults and admission.
import test from 'node:test';
import assert from 'node:assert/strict';
import { DxfDocument, DxfRawDocument, DxfTag, MemoryStream, AttributeDefinition, Block, Insert, TextStyle, MathHelper, TextAlignment } from '../../index.js';
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
