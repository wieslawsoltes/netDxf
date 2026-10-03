import test from 'node:test';
import assert from 'node:assert/strict';
import { DxfDocument, DxfRawDocument, DxfTag, MemoryStream, PlotSettings } from '../../index.js';
import { DxfReader } from '../../netDxf/IO/DxfReader.js';
import { DxfWriter } from '../../netDxf/IO/DxfWriter.js';
import { DatabaseIOContext } from '../../runtime/DatabaseIOContext.js';
import { FormatException } from '../../runtime/Errors.js';
const T=(code,value)=>new DxfTag(code,value),marker=T(100,'AcDbPlotSettings'),layout=T(100,'AcDbLayout');
const reader=()=>{const r=new DxfReader();r.doc=new DxfDocument(18);r.Context=new DatabaseIOContext(r.doc);return r;};
const framing=e=>e instanceof FormatException&&e.message==='Embedded plot settings must be followed by the AcDbLayout subclass.';
for(const tail of [[],[T(100,'PrivateLayout')],[T(0,'NEXT')],[T(100,'PrivateLayout'),layout],[T(0,'NEXT'),layout]])
  test(`embedded plot framing refuses first invalid terminator ${JSON.stringify(tail.map(t=>t.Value))}`,()=>{
    const r=reader();assert.throws(()=>r.ReadPlotSettings({Tags:[marker,T(333,'AB'),...tail]}),framing);assert.deepEqual(r.Context.outputShadeReferences,[]);
  });
test('embedded framing refusal precedes parsing invalid plot fields and queuing shade links',()=>{
  const r=reader();assert.throws(()=>r.ReadPlotSettings({Tags:[marker,T(333,'AB'),T(72,-1)]}),framing);
  assert.deepEqual(r.Context.outputShadeReferences,[]);
});
test('a valid terminator preserves field errors and does not queue a partially parsed plot',()=>{
  const r=reader();assert.throws(()=>r.ReadPlotSettings({Tags:[marker,T(333,'AB'),T(72,-1),layout]}),e=>e instanceof FormatException&&!framing(e));assert.deepEqual(r.Context.outputShadeReferences,[]);
});
test('comments inside embedded plot data are ignored but the physical layout terminator is mandatory',()=>{
  const r=reader(),plot=r.ReadPlotSettings({Tags:[marker,T(999,'comment'),T(1,'named setup'),T(999,'second'),T(333,'AB'),layout,T(72,-1)]});
  assert.equal(plot.PageSetupName,'named setup');assert.deepEqual(r.Context.outputShadeReferences,[[plot,'AB']]);
});
test('an empty embedded plot packet keeps defaults and queues only its completed null shade link',()=>{
  const r=reader(),plot=r.ReadPlotSettings({Tags:[marker,layout]});assert.ok(plot instanceof PlotSettings);assert.deepEqual(r.Context.outputShadeReferences,[[plot,null]]);
});
test('an XData prefix is not a substitute for the embedded layout terminator',()=>{
  const r=reader();assert.throws(()=>r.ReadPlotSettings({Tags:[marker,T(1001,'PRIVATE'),T(1000,'x'),layout]}),e=>e instanceof FormatException&&e.message.includes('1001'));assert.deepEqual(r.Context.outputShadeReferences,[]);
});
for(const binary of [false,true])for(const defect of ['wrong','truncated'])
  test(`whole LAYOUT input validates plot termination before block lookup / ${defect}/${binary}`,()=>{
    const output=new MemoryStream();new DxfWriter().Write(output,new DxfDocument(18),binary);
    try {
      const raw=DxfRawDocument.Load(output.ToArray()),record=raw.Sections.flatMap(s=>s.Records).find(r=>r.Name==='LAYOUT');assert.ok(record);
      const tags=[...record.Tags],at=tags.findIndex(t=>t.Code===100&&t.Value==='AcDbLayout');assert.ok(at>0);
      const changed=defect==='wrong'?tags.map((t,i)=>i===at?T(100,'WrongLayout'):t):tags.slice(0,at),input=new MemoryStream();
      try{raw.WithRecord(record,changed).Save(input,binary);input.Position=0;assert.throws(()=>new DxfReader().Read(input),framing);assert.equal(input.CanRead,true);}finally{input.Dispose();}
    }finally{output.Dispose();}
  });
