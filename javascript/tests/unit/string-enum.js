import test from 'node:test';
import assert from 'node:assert/strict';
import {StringEnum,StringValueAttribute,StringComparison,DxfVersion,HatchGradientPatternType,EntityType} from '../../index.js';
import {StringEnum as Standalone} from '../../netDxf/StringEnum.js';
import {Culture} from '../../runtime/DisplayFormatting.js';
import {stringEnumCorpus} from '../../tools/string-enum-corpus.mjs';
test('StringEnum exports standalone and cached generic bindings',()=>{
  assert.equal(Standalone,StringEnum);assert.equal(StringEnum.For(DxfVersion),StringEnum.For(DxfVersion));
  assert.equal(new (StringEnum.For(DxfVersion))().EnumType,DxfVersion);
  assert.equal(new StringEnum(EntityType).EnumType,EntityType);
});
test('StringEnum lists contain attributed fields in source declaration order',()=>{
  const helper=new StringEnum(DxfVersion),values=helper.GetStringValues();
  assert.equal(values.Count,19);assert.equal(values.get_Item(0),'Unknown');assert.equal(values.get_Item(18),'AC1032');
  values.Clear();assert.equal(helper.GetStringValues().Count,19);
  assert.deepEqual([...new StringEnum(HatchGradientPatternType).GetStringValues()].slice(0,3),['LINEAR','CYLINDER','INVCYLINDER']);
});
test('StringEnum dictionaries are fresh mutable snapshots',()=>{
  const helper=new StringEnum(DxfVersion),values=helper.GetValues();
  assert.equal(values.get_Item(DxfVersion.AutoCad2018),'AC1032');values.set_Item(DxfVersion.AutoCad2018,'changed');
  assert.equal(helper.GetValues().get_Item(DxfVersion.AutoCad2018),'AC1032');
});
test('StringEnum ignores unannotated enum values and unnamed numeric values',()=>{
  const Helper=StringEnum.For(EntityType);assert.equal(new Helper().GetValues().Count,0);
  assert.equal(Helper.GetStringValue(EntityType.Line),null);assert.equal(StringEnum.GetStringValue(DxfVersion,-1),null);
  assert.equal(StringEnum.GetStringValue(DxfVersion,2147483647),null);
});
test('StringEnum preserves source null parsing and default return values',()=>{
  const Helper=StringEnum.For(DxfVersion);assert.equal(Helper.Parse(null),0);assert.equal(Helper.Parse('no match'),0);
  assert.equal(Helper.IsStringDefined(null),false);assert.equal(Helper.IsStringDefined('no match'),false);
});
test('StringEnum validates comparison options even for equal or null strings',()=>{
  const Helper=StringEnum.For(DxfVersion);
  for(const comparison of [-1,6])for(const value of [null,'Unknown','AC1032'])
    for(const method of ['Parse','IsStringDefined'])assert.throws(()=>Helper[method](value,comparison),{name:'ArgumentException',ParamName:'comparisonType'});
  // With no annotated strings IsStringDefined performs no equality calls.
  assert.equal(StringEnum.For(EntityType).IsStringDefined(null,6),false);
});
test('StringEnum ordinal modes keep case width and embedded NUL distinctions',()=>{
  const Helper=StringEnum.For(DxfVersion);
  assert.equal(Helper.Parse('ac1032',StringComparison.Ordinal),0);
  assert.equal(Helper.Parse('ac1032',StringComparison.OrdinalIgnoreCase),18);
  assert.equal(Helper.Parse('ＡＣ１０３２',StringComparison.OrdinalIgnoreCase),0);
  assert.equal(Helper.Parse('AC'+'\0'+'1032',StringComparison.OrdinalIgnoreCase),0);
});
test('StringEnum current culture and invariant comparisons remain distinct in Turkish',()=>{
  const old=Culture.Current;try{Culture.Current='tr-TR';const Helper=StringEnum.For(HatchGradientPatternType);
    assert.equal(Helper.IsStringDefined('linear',StringComparison.CurrentCultureIgnoreCase),false);
    assert.equal(Helper.IsStringDefined('linear',StringComparison.InvariantCultureIgnoreCase),true);
  }finally{Culture.Current=old;}
});
test('StringValueAttribute retains immutable null empty and text values',()=>{
  for(const value of [null,'','custom']){const a=new StringValueAttribute(value);assert.equal(a.Value,value);assert.throws(()=>{a.Value='other';},TypeError);}
});
test('StringEnum input corpus covers every generated enum without embedding results',()=>{
  const corpus=stringEnumCorpus();assert.deepEqual(corpus,stringEnumCorpus());assert.equal(corpus.length,167);
  assert.equal(corpus.filter(c=>c.category==='all-types').length,77);
  assert.equal(corpus.reduce((n,p)=>n+p.request.steps.length,0),28788);assert.ok(corpus.every(c=>!('expected'in c)));
});
test('StringEnum preserves width distinctions for ASCII attributed labels in linguistic modes',()=>{
  const Helper=StringEnum.For(DxfVersion),old=Culture.Current;
  try{for(const culture of ['', 'en-US','tr-TR','ja-JP']){Culture.Current=culture;
    for(const value of ['ＡＣ１０３２','ＡＣ1032','Ａｃ１０３２'])for(const mode of [1,3]) {
      assert.equal(Helper.Parse(value,mode),0);assert.equal(Helper.IsStringDefined(value,mode),false);
    }
  }}finally{Culture.Current=old;}
});

test('StringEnum retains Japanese culture default width equivalence without ignore-case tailoring',()=>{
  const old=Culture.Current;try{Culture.Current='ja-JP';assert.equal(StringEnum.For(DxfVersion).Parse('ＡＣ１０３２'),18);}
  finally{Culture.Current=old;}
});
