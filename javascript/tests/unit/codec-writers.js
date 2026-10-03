import test from 'node:test';
import assert from 'node:assert/strict';
import { TextCodeValueWriter } from '../../netDxf/IO/TextCodeValueWriter.js';
import { BinaryCodeValueWriter } from '../../netDxf/IO/BinaryCodeValueWriter.js';
import { BinarySentinel } from '../../runtime/BinaryCursor.js';
import { MemoryStream } from '../../runtime/MemoryStream.js';
import { Encoding } from '../../runtime/Encoding.js';
import { Culture } from '../../runtime/GeometryRuntime.js';
import { IOException,NotSupportedException } from '../../runtime/Errors.js';
import { codecWritersCorpus } from '../../tools/codec-writers-corpus.mjs';
function sink(){const stream=new MemoryStream();let flushes=0;stream.Flush=()=>{flushes++;};return{stream,flushes:()=>flushes};}
test('text writer preserves constructor/null failure timing',()=>{
  const writer=new TextCodeValueWriter(null);assert.equal(writer.CurrentPosition,0);assert.equal(writer.ToString(),'0:');
  assert.throws(()=>writer.Write(1,'text'),{name:'NullReferenceException'});assert.equal(writer.Code,1);assert.equal(writer.Value,null);assert.equal(writer.CurrentPosition,0);
});
test('text writer passes the code and Boolean through integer WriteLine callbacks',()=>{
  const calls=[],writer=new TextCodeValueWriter({WriteLine:value=>calls.push(value)});writer.Write(290,true);writer.Write(70,42);
  assert.deepEqual(calls,[290,1,70,'42']);assert.equal(writer.CurrentPosition,4);
});
test('text output failure keeps preceding Value and exact logical position',()=>{
  let calls=0;const writer=new TextCodeValueWriter({WriteLine(){if(++calls===4)throw new IOException('write');}});writer.Write(70,42);
  assert.throws(()=>writer.Write(1,'after'),{name:'IOException'});assert.equal(writer.Code,1);assert.equal(writer.Value,42);assert.equal(writer.CurrentPosition,3);
});
test('text re-entrant unknown-code diagnostic observes the current tag state',()=>{
  let writer,trigger=true;writer=new TextCodeValueWriter({WriteLine(){if(trigger){trigger=false;writer.Write(70,42);}}});
  assert.throws(()=>writer.Write(80,'invalid'),e=>e.Message==='Code 70 not valid at line 3');assert.equal(writer.Code,70);assert.equal(writer.Value,42);
});
test('binary constructor writes a fresh sentinel rather than exposing the shared signature',()=>{
  const original=BinarySentinel.slice();new BinaryCodeValueWriter({Write(buffer){buffer[0]=0;}});assert.deepEqual(BinarySentinel,original);
  assert.throws(()=>new BinaryCodeValueWriter(null),{name:'NullReferenceException'});
});
test('binary position queries invoke Flush before asking the caller position',()=>{
  const {stream,flushes}=sink(),writer=new BinaryCodeValueWriter(stream);assert.equal(flushes(),0);assert.equal(writer.CurrentPosition,22);assert.equal(flushes(),1);
  stream.Flush=()=>{throw new IOException('flush-first');};assert.throws(()=>writer.CurrentPosition,e=>e.Message==='flush-first');
});
test('binary unknown codes flush then preserve the nonseekable position error',()=>{
  const writes=[],writer=new BinaryCodeValueWriter({Write:b=>writes.push([...b]),Flush(){writes.push('flush');},get Position(){throw new NotSupportedException('position');}});
  assert.throws(()=>writer.Write(80,null),e=>e.name==='NotSupportedException');assert.deepEqual(writes.at(-1),'flush');assert.equal(writer.Code,80);assert.equal(writer.Value,null);
});
test('binary numeric output survives caller re-entry before it copies the buffer',()=>{
  const output=[],stream={Write(buffer,offset=0,count=buffer.length){if(writer&&reenter){reenter=false;writer.WriteInt(0x12345678);}output.push(...buffer.subarray(offset,offset+count));}};
  let writer=null,reenter=false;writer=new BinaryCodeValueWriter(stream);output.length=0;reenter=true;writer.WriteShort(0x5678);
  assert.deepEqual(output,[0x78,0x56,0x34,0x12,0x78,0x56]);
});
test('binary Boolean uses WriteByte whereas a string terminator uses encoded Write',()=>{
  const calls=[],writer=new BinaryCodeValueWriter({Write:b=>calls.push(['Write',[...b]]),WriteByte:b=>calls.push(['WriteByte',b])});calls.length=0;
  writer.WriteBool(true);writer.WriteString('');assert.deepEqual(calls,[['WriteByte',1],['Write',[]],['Write',[0]]]);
});
test('binary string chunks preserve exact UTF-8 scalar boundaries and committed prefix',()=>{
  const calls=[],writer=new BinaryCodeValueWriter({Write:bytes=>calls.push(bytes.slice())});calls.length=0;
  writer.WriteString('Ω'.repeat(40000));assert.deepEqual(calls.map(c=>c.length),[65536,14464,1]);
  calls.length=0;assert.throws(()=>writer.WriteString('A'.repeat(70000)+'\ud800'),{name:'EncoderFallbackException'});assert.deepEqual(calls.map(c=>c.length),[65536]);
});
for(const length of [65535,65536])test('binary UTF-8 invalid scalar is validated before output capacity '+length,()=>{
  const calls=[],writer=new BinaryCodeValueWriter({Write:bytes=>calls.push(bytes.slice())});calls.length=0;
  assert.throws(()=>writer.WriteString('A'.repeat(length)+'\ud800tail'),{name:'EncoderFallbackException'});assert.equal(calls.length,0);
});
test('binary null strings fail without writing a length or terminator',()=>{
  const stream=new MemoryStream(),writer=new BinaryCodeValueWriter(stream);assert.throws(()=>writer.WriteString(null),{name:'NullReferenceException'});assert.equal(stream.Length,22);
});
test('binary chunk limits are validated before changing current Code or writing',()=>{
  const stream=new MemoryStream(),writer=new BinaryCodeValueWriter(stream);writer.Write(70,42);const original=stream.ToArray();
  for(const value of [null,'bad',new Uint8Array(256)])assert.throws(()=>writer.Write(310,value));
  assert.deepEqual(stream.ToArray(),original);assert.equal(writer.Code,70);assert.equal(writer.Value,42);
});
test('legacy unknown and comment groups leave tag and bytes unchanged',()=>{
  const stream=new MemoryStream(),writer=new BinaryCodeValueWriter(stream,true);writer.Write(70,42);const original=stream.ToArray();
  for(const code of [80,999])assert.throws(()=>writer.Write(code,'unused'),{name:'ArgumentOutOfRangeException',ParamName:'code'});
  assert.deepEqual(stream.ToArray(),original);assert.equal(writer.Code,70);
});
test('writer ToString is culture-sensitive but serialized text numeric values stay invariant',()=>{
  const old=Culture.Current;try{Culture.Current='pl-PL';const output=[],writer=new TextCodeValueWriter({WriteLine:v=>output.push(v)});writer.Write(40,1.5);
    assert.equal(writer.ToString(),'40:1,5');assert.deepEqual(output,[40,'1.5']);writer.Write(310,Uint8Array.of(0xab));assert.equal(writer.ToString(),'310:System.Byte[]');
  }finally{Culture.Current=old;}
});
test('writer corpus is deterministic and all commands remain explicitly counted',()=>{
  const cases=codecWritersCorpus();assert.deepEqual(cases,codecWritersCorpus());assert.equal(cases.length,509);assert.equal(new Set(cases.map(p=>p.name)).size,509);
  assert.equal(cases.reduce((n,p)=>n+p.request.steps.length,0),5527);assert.ok(cases.every(p=>!Object.hasOwn(p,'expected')));
});
