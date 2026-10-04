import test from 'node:test';
import assert from 'node:assert/strict';
import { DxfRawDocument, MemoryStream, DxfTag, DxfVersion } from '../../index.js';
import { DxfReader } from '../../netDxf/IO/DxfReader.js';
import { DxfWriter } from '../../netDxf/IO/DxfWriter.js';
import { NotSupportedException } from '../../runtime/Errors.js';
import { OpaqueFixture, OpaqueName } from '../netDxf.Conformance/OpaqueEntityTests.js';

const T=(code,value)=>new DxfTag(code,value);
function addSection(raw,name,records,position='after') {
  const tags=[...raw.Tags];
  const at=position==='before'?raw.Sections.find(s=>s.Name==='ENTITIES').StartTagIndex:tags.findLastIndex(t=>t.Code===0&&t.Value==='EOF');
  assert.ok(at>=0);
  return raw.WithTags([...tags.slice(0,at),T(0,'SECTION'),T(2,name),...records,T(0,'ENDSEC'),...tags.slice(at)]);
}
function encode(raw,binary) {
  const stream=new MemoryStream();
  try{raw.Save(stream,binary);return stream.ToArray();}finally{stream.Dispose();}
}
for(const binary of [false,true]) {
  for(const position of ['before','after'])for(const kind of ['ACDSSCHEMA','ACDSRECORD','PRIVATE_ACDS_RECORD'])
    test(`opaque typed load refuses discarded ${kind} ${position} entities / ${binary}`,()=>{
      const source=encode(addSection(OpaqueFixture(18),'ACDSDATA',[T(0,kind),T(90,1)],position),binary),reader=new DxfReader(),input=new MemoryStream(source);
      try {
        assert.throws(()=>reader.Read(input),e=>e instanceof NotSupportedException&&e.message.includes('ACDSDATA'));
        assert.equal(reader.Context.hasDiscardedAcdsData,true);assert.equal(input.CanRead,true);
        const raw=DxfRawDocument.Load(source),section=raw.Sections.find(s=>s.Name==='ACDSDATA');
        assert.ok(section.Records.some(r=>r.Name===kind));assert.ok(raw.Sections.some(s=>s.Records.some(r=>r.Name===OpaqueName)));
      }finally{input.Dispose();}
    });
  test(`empty ACDSDATA does not prevent opaque preservation / ${binary}`,()=>{
    const reader=new DxfReader(),doc=reader.Read(encode(addSection(OpaqueFixture(18),'ACDSDATA',[]),binary));
    assert.equal(reader.Context.hasDiscardedAcdsData,false);assert.equal([...doc.Entities.OpaqueEntities].length,1);
    const output=new MemoryStream();try{new DxfWriter().Write(output,doc,binary);assert.equal([...new DxfReader().Read(output.ToArray()).Entities.OpaqueEntities].length,1);}finally{output.Dispose();}
  });
  test(`discarded ACDSDATA does not introduce a blanket known-entity refusal / ${binary}`,()=>{
    const raw=OpaqueFixture(18),record=raw.Sections.flatMap(s=>s.Records).find(r=>r.Name===OpaqueName),tags=[...raw.Tags];
    const source=raw.WithTags(tags.filter((_,i)=>i<record.StartTagIndex||i>=record.EndTagIndex));
    const reader=new DxfReader(),doc=reader.Read(encode(addSection(source,'ACDSDATA',[T(0,'ACDSRECORD'),T(90,1)]),binary));
    assert.equal(reader.Context.hasDiscardedAcdsData,true);assert.equal([...doc.Entities.Lines].length,1);assert.equal([...doc.Entities.OpaqueEntities].length,0);
  });
  test(`another discarded section does not masquerade as ACDSDATA / ${binary}`,()=>{
    const reader=new DxfReader(),doc=reader.Read(encode(addSection(OpaqueFixture(18),'FUTURE_SECTION',[T(0,'ACDSRECORD'),T(90,1)]),binary));
    assert.equal(reader.Context.hasDiscardedAcdsData,false);assert.equal([...doc.Entities.OpaqueEntities].length,1);
  });
}
for(const version of [DxfVersion.AutoCad2000,DxfVersion.AutoCad2018])for(const binary of [false,true])
  test(`whole writer supplies both transport flag and CLASS collection for opaque output / ${version}/${binary}`,()=>{
    const doc=new DxfReader().Read(encode(OpaqueFixture(version),binary)),entity=[...doc.Entities.OpaqueEntities][0],original=entity.SourceTags,output=new MemoryStream();
    try {
      new DxfWriter().Write(output,doc,binary);
      const raw=DxfRawDocument.Load(output.ToArray()),record=raw.Sections.flatMap(s=>s.Records).find(r=>r.Name===OpaqueName);
      assert.ok(record);assert.deepEqual([...record.Tags].map(t=>[t.Code,t.Value]),[...original].map(t=>[t.Code,t.Value]));
      assert.equal([...new DxfReader().Read(output.ToArray()).Entities.OpaqueEntities].length,1);assert.equal(output.CanWrite,true);
    }finally{output.Dispose();}
  });
test('reused reader does not carry discarded ACDSDATA state into a subsequent document',()=>{
  const reader=new DxfReader(),plain=OpaqueFixture(18),bad=addSection(plain,'ACDSDATA',[T(0,'ACDSRECORD'),T(90,1)]);
  assert.throws(()=>reader.Read(encode(bad,false)),NotSupportedException);const previous=reader.Context;
  const document=reader.Read(encode(plain,true));assert.notEqual(reader.Context,previous);assert.equal(reader.Context.hasDiscardedAcdsData,false);assert.equal([...document.Entities.OpaqueEntities].length,1);
});
