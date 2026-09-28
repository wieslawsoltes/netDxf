import test from 'node:test';
import assert from 'node:assert/strict';
import { DxfDocument, DxfRawDocument, DxfTag, DimensionStyle, MemoryStream, MathHelper } from '../../index.js';
import { DxfReader } from '../../netDxf/IO/DxfReader.js';
import { ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { getProperty } from '../../runtime/TypedDocumentIO.js';
function readStyle(binary,code,value){const doc=new DxfDocument();doc.DimensionStyles.Add(new DimensionStyle('RECOVERY'));const stream=new MemoryStream(),input=new MemoryStream();try{assert.equal(doc.Save(stream,binary),true);stream.Position=0;const raw=DxfRawDocument.Load(stream),record=raw.Sections.flatMap(s=>s.Records).find(r=>r.Name==='DIMSTYLE'&&Array.from(r.Tags).some(t=>t.Code===2&&t.Value==='RECOVERY'));assert.ok(record);const tags=Array.from(record.Tags).filter(t=>t.Code!==code);tags.push(new DxfTag(code,value));raw.WithRecord(record,tags).Save(input,binary);input.Position=0;return new DxfReader().Read(input).DimensionStyles.get_Item('RECOVERY');}finally{input.Dispose();stream.Dispose();}}
for(const binary of [false,true]){
  for(const[code,property,bad]of[[40,'DimScaleOverall',0],[140,'TextHeight',0],[143,'AlternateUnits.Multiplier',0],[146,'TextFractionHeightScale',0],[41,'ArrowSize',-1],[42,'ExtLineOffset',-1],[43,'DimBaselineSpacing',-1],[44,'ExtLineExtend',-1],[46,'DimLineExtend',-1],[171,'AlternateUnits.LengthPrecision',-1],[179,'AngularPrecision',-2],[271,'LengthPrecision',-1],[272,'Tolerances.Precision',-1],[274,'Tolerances.AlternatePrecision',-1],[144,'DimScaleLinear',MathHelper.Epsilon/2],[45,'DimRoundoff',1e-8],[148,'AlternateUnits.Roundoff',-1]]){
    test(`DIMSTYLE group ${code} follows pinned import recovery / ${binary}`,()=>assert.equal(getProperty(readStyle(binary,code,bad),property),getProperty(DimensionStyle.Default,property)));
  }
  test(`DIMSTYLE zero roundoff is retained rather than rounded up / ${binary}`,()=>assert.equal(readStyle(binary,45,0).DimRoundoff,0));
  test(`DIMSTYLE negative nonzero linear scale is retained / ${binary}`,()=>assert.equal(readStyle(binary,144,-2).DimScaleLinear,-2));
  test(`DIMSTYLE fixed extension length has no blanket numeric recovery / ${binary}`,()=>assert.throws(()=>readStyle(binary,49,-1),ArgumentOutOfRangeException));
}
