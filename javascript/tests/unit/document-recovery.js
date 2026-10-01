import test from 'node:test';
import assert from 'node:assert/strict';
import { DxfDocument, DxfRawDocument, MemoryStream, Line, Vector3, Layout } from '../../index.js';
import { DxfReader } from '../../netDxf/IO/DxfReader.js';
import { DxfWriter } from '../../netDxf/IO/DxfWriter.js';
import { TextCodeValueWriter } from '../../netDxf/IO/TextCodeValueWriter.js';
import { BinaryCodeValueWriter } from '../../netDxf/IO/BinaryCodeValueWriter.js';
import { Culture } from '../../runtime/DisplayFormatting.js';
import { EndOfStreamException, FormatException } from '../../runtime/Errors.js';
import { CreateMinimalDocument } from '../netDxf.Conformance/MinimalDocumentTests.js';

function bytes(tags,binary){const stream=new MemoryStream(),writer=binary?new BinaryCodeValueWriter(stream):new TextCodeValueWriter(stream);try{for(const [code,value] of tags)writer.Write(code,value);writer.Flush();return stream.ToArray();}finally{stream.Dispose();}}
const header=[[0,'SECTION'],[2,'HEADER'],[9,'$ACADVER'],[1,'AC1032']];
for(const binary of [false,true])for(const section of ['HEADER','CLASSES','TABLES','BLOCKS','ENTITIES','OBJECTS'])test(`typed ${section} truncation keeps raw failure contract / ${binary}`,()=>{
  const tags=section==='HEADER'?[...header,[0,'EOF']]:[...header,[0,'ENDSEC'],[0,'SECTION'],[2,section],[0,'EOF']];
  const source=bytes(tags,binary),stream=new MemoryStream(source);
  try{assert.throws(()=>new DxfReader().Read(stream),EndOfStreamException);assert.equal(stream.CanRead,true);assert.throws(()=>DxfRawDocument.Load(source),FormatException);}finally{stream.Dispose();}
});
for(const binary of [false,true])test(`typed nontruncation section error stays a format error / ${binary}`,()=>{
  const source=bytes([...header,[0,'ENDSEC'],[0,'LINE'],[0,'EOF']],binary);
  assert.throws(()=>new DxfReader().Read(source),FormatException);assert.throws(()=>DxfRawDocument.Load(source),FormatException);
});
for(const newline of ['\n','\r\n'])test(`typed writer captures host newline / ${JSON.stringify(newline)}`,()=>{
  const previous=Culture.NewLine;Culture.NewLine=newline;
  class MutatingStream extends MemoryStream {Write(...args){super.Write(...args);Culture.NewLine=newline==='\n'?'\r\n':'\n';}}
  const output=new MutatingStream();
  try{
    new DxfWriter().Write(output,new DxfDocument(18),false);
    const text=new TextDecoder().decode(output.ToArray());assert.ok(text.endsWith('0'+newline+'EOF'+newline));
    assert.equal(text.split(newline).join('').includes('\n'),false);assert.equal(text.split(newline).join('').includes('\r'),false);
    assert.equal(new DxfReader().Read(output.ToArray()).DrawingVariables.AcadVer,18);assert.equal(output.CanWrite,true);
  }finally{Culture.NewLine=previous;output.Dispose();}
});
test('typed newline change leaves raw codec stream output unchanged',()=>{
  const previous=Culture.NewLine;Culture.NewLine='\n';const output=new MemoryStream();
  try{const writer=new TextCodeValueWriter(output);writer.Write(0,'EOF');writer.Flush();assert.equal(new TextDecoder().decode(output.ToArray()),'0\r\nEOF\r\n');}finally{Culture.NewLine=previous;output.Dispose();}
});
function stripObjects(raw){const section=Array.from(raw.Sections).find(s=>s.Name==='OBJECTS'),tags=Array.from(raw.Tags);assert.ok(section);return tags.filter((_,i)=>i<section.StartTagIndex||i>=section.EndTagIndex);}
for(const binary of [false,true])test(`missing OBJECTS recovers several physical paper blocks / ${binary}`,()=>{
  const doc=new DxfDocument(18);for(const name of ['A','B','C']){doc.Layouts.Add(new Layout(name));doc.Entities.ActiveLayout=name;doc.Entities.Add(new Line(Vector3.Zero,new Vector3(1,2,3)));}doc.Entities.ActiveLayout='Model';
  const output=new MemoryStream();new DxfWriter().Write(output,doc,binary);const raw=DxfRawDocument.Load(output.ToArray()),tags=stripObjects(raw);
  const copy=new DxfReader().Read(bytes(tags.map(t=>[t.Code,t.Value]),binary));
  assert.equal(copy.Layouts.Count,4);assert.deepEqual(Array.from(copy.Layouts).map(l=>l.Name),['Model','Layout1','Layout2','Layout3']);
  for(const source of doc.Layouts){const block=copy.Blocks.get_Item(source.AssociatedBlock.Name);assert.equal(block.Record.Handle,source.AssociatedBlock.Record.Handle);assert.ok(block.Record.Layout);assert.equal(block.Record.Layout.AssociatedBlock,block);
    for(const entity of source.AssociatedBlock.Entities){const retained=copy.GetObjectByHandle(entity.Handle);assert.ok(retained instanceof Line);assert.equal(retained.Owner,block);}}
  const again=new MemoryStream();try{new DxfWriter().Write(again,copy,binary);assert.equal(new DxfReader().Read(again.ToArray()).Layouts.Count,4);}finally{again.Dispose();output.Dispose();}
});
for(const binary of [false,true])test(`recovery does not turn an ordinary block into a layout / ${binary}`,()=>{
  const input=CreateMinimalDocument(18,binary,true,false,true);try{const copy=new DxfReader().Read(input);assert.equal(copy.Layouts.Count,1);assert.equal(copy.Blocks.get_Item('Component').Record.Layout,null);assert.equal(copy.Layouts.get_Item('Model').AssociatedBlock.Record.Layout,copy.Layouts.get_Item('Model'));}finally{input.Dispose();}
});

// Future physical records must not silently advance the HEADER allocator before
// generated collections are created. OBJECTS has a separate, later reservation.
for(const binary of [false,true])for(const futureSection of ['ENTITIES','OBJECTS','FUTURE_SECTION'])test(`HEADER seed is used before future ${futureSection} identity reservation / ${binary}`,()=>{
  const input=bytes([...header,[9,'$HANDSEED'],[5,'00008000'],[0,'ENDSEC'],[0,'SECTION'],[2,futureSection],[0,futureSection==='ENTITIES'?'LINE':'ACDBPLACEHOLDER'],[5,'F000'],[100,'AcDbEntity'],[8,'0'],[100,'AcDbLine'],[10,0],[20,0],[30,0],[11,1],[21,1],[31,1],[0,'ENDSEC'],[0,'EOF']],binary),reader=new DxfReader(),stop=new Error('captured allocator phase');
  reader.InitializeCollections=function(){assert.equal(this.doc.NumHandles,0x8000n);assert.equal(this.doc.DrawingVariables.HandleSeed,'8000');throw stop;};assert.throws(()=>reader.Read(input),e=>e===stop);
});
for(const binary of [false,true])test(`absent HEADER seed does not borrow a later source identity / ${binary}`,()=>{
  const input=bytes([...header,[0,'ENDSEC'],[0,'SECTION'],[2,'ENTITIES'],[0,'LINE'],[5,'F000'],[100,'AcDbEntity'],[8,'0'],[100,'AcDbLine'],[0,'ENDSEC'],[0,'EOF']],binary),reader=new DxfReader(),stop=new Error('captured default allocator phase');reader.InitializeCollections=function(){assert.equal(this.doc.NumHandles,1n);throw stop;};assert.throws(()=>reader.Read(input),e=>e===stop);
});
