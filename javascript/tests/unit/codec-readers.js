import test from 'node:test';
import assert from 'node:assert/strict';
import { TextCodeValueReader } from '../../netDxf/IO/TextCodeValueReader.js';
import { BinaryCodeValueReader } from '../../netDxf/IO/BinaryCodeValueReader.js';
import { MemoryStream } from '../../runtime/MemoryStream.js';
import { BinarySentinel } from '../../runtime/BinaryCursor.js';
import { Culture } from '../../runtime/DisplayFormatting.js';
import { ArgumentNullException, IOException, NotSupportedException } from '../../runtime/Errors.js';
import { codecReadersCorpus } from '../../tools/codec-readers-corpus.mjs';
const binary=(tail)=>new MemoryStream(Uint8Array.from([...BinarySentinel,...tail]));
for(const [code,value,kind] of [[70,'32768','16-bit integer'],[90,'2147483648','32-bit integer'],[160,'9223372036854775808','64-bit integer'],[290,'2','boolean (0 or 1)'],[40,'NaN','finite double-precision number'],[5,'GG','hexadecimal handle (1 to 16 digits)']])
  test('text '+code+' failure keeps native diagnostic and previous typed value',()=>{
    const reader=new TextCodeValueReader('70\n42\n'+code+'\n'+value+'\n1\nafter\n');reader.Next();
    assert.throws(()=>reader.Next(),error=>error.name==='FormatException'&&error.Message===`Invalid ${kind} value for group code ${code} at line 4.`);
    assert.equal(reader.Code,code);assert.equal(reader.CurrentPosition,3);assert.equal(reader.ReadShort(),42);
    reader.Next();assert.equal(reader.ReadString(),'after');assert.equal(reader.CurrentPosition,5);
  });
for(const [value,message] of [['A','Binary chunk for group code 310 at line 2 must contain an even number of hexadecimal digits.'],['00FG','Invalid hexadecimal digit in binary chunk for group code 310 at line 2, byte 1.']])
  test('text binary chunks identify malformed payload '+value,()=>assert.throws(()=>new TextCodeValueReader('310\n'+value+'\n').Next(),error=>error.Message===message));
test('integer negative-zero group code is canonical but floating negative zero survives',()=>{
  const reader=new TextCodeValueReader('-0\nLINE\n40\n-0\n');reader.Next();assert.equal(Object.is(reader.Code,0),true);assert.equal(reader.ToString(),'0:LINE');
  reader.Next();assert.equal(Object.is(reader.ReadDouble(),-0),true);assert.equal(reader.ToString(),'40:-0');
});
test('text diagnostic ToString uses native culture, Boolean and array formatting',()=>{
  const previous=Culture.Current;try{Culture.Current='pl-PL';const reader=new TextCodeValueReader('40\n1.5\n290\n1\n310\nABCD\n');
    assert.equal(reader.ToString(),'0:');reader.Next();assert.equal(reader.ToString(),'40:1,5');reader.Next();assert.equal(reader.ToString(),'290:True');reader.Next();assert.equal(reader.ToString(),'310:System.Byte[]');
  }finally{Culture.Current=previous;}
});
test('skipped comments preserve native state when the following record fails',()=>{
  const reader=new TextCodeValueReader('999\ncomment\n70\nbad\n');reader.SkipComments=true;
  assert.throws(()=>reader.Next(),{name:'FormatException'});assert.equal(reader.ReadString(),'comment');assert.equal(reader.CurrentPosition,3);
});
test('text unknown group retains quoted code and exact logical line',()=>{
  const reader=new TextCodeValueReader('80\n1\n');assert.throws(()=>reader.Next(),error=>error.Message==='Code "80" not valid at line 1');assert.equal(reader.Value,null);
});
test('text reader consumes caller ReadLine and keeps state on callback failure',()=>{
  const reader=new TextCodeValueReader({ReadLine(){if(++this.calls===1)return '70';throw new IOException('injected');},calls:0});
  assert.throws(()=>reader.Next(),error=>error.Message==='injected');assert.equal(reader.Code,70);assert.equal(reader.CurrentPosition,1);assert.equal(reader.Value,null);
});
test('binary structural errors report the actual stream address',()=>{
  for(const [code,message] of [[80,'Code 80 not valid at byte address 24'],[999,'The comment group, 999, is not used in binary DXF files at byte address 24']]){
    const input=binary([code&255,code>>8]),reader=new BinaryCodeValueReader(input);
    assert.throws(()=>reader.Next(),error=>error.name==='Exception'&&error.Message===message);assert.equal(reader.Code,code);assert.equal(input.CanRead,true);
  }
});
test('binary unknown codes retain native nonseekable Position exception',()=>{
  const source=binary([80,0]);const stream={CanRead:true,CanSeek:false,Read:(...args)=>source.Read(...args),ReadByte:()=>source.ReadByte(),get Position(){throw new NotSupportedException('Position unavailable');}};
  const reader=new BinaryCodeValueReader(stream);assert.throws(()=>reader.Next(),error=>error.name==='NotSupportedException'&&error.Message==='Position unavailable');assert.equal(reader.Code,80);assert.equal(source.Position,24);
});
test('binary truncation consumes bytes but preserves preceding boxed primitive',()=>{
  const input=binary([70,0,42,0,90,0,1,2]),reader=new BinaryCodeValueReader(input);reader.Next();
  assert.throws(()=>reader.Next(),{name:'EndOfStreamException'});assert.equal(reader.ReadShort(),42);assert.equal(reader.Code,90);assert.equal(input.Position,input.Length);
});
test('binary fragmented streams never use a whole-input ToArray shortcut',()=>{
  const source=binary([1,0,65,0]),trace=[];source.ToArray=()=>{throw new Error('shortcut');};
  const stream={CanRead:true,CanSeek:true,get Position(){return source.Position;},Read(buffer,offset,count){trace.push(count);return source.Read(buffer,offset,Math.min(count,1));},ReadByte:()=>source.ReadByte()};
  const reader=new BinaryCodeValueReader(stream);reader.Next();assert.equal(reader.ReadString(),'A');assert.equal(trace[0],22);assert.ok(trace.includes(1));assert.equal(source.CanRead,true);
});
test('source exception Message retains parameter names while JS message stays backward compatible',()=>{
  const error=new ArgumentNullException('reader');assert.equal(error.message,'Value cannot be null.');assert.equal(error.Message,"Value cannot be null. (Parameter 'reader')");
});
test('codec corpus preserves unique deterministic input identities and requested commands',()=>{
  const cases=codecReadersCorpus();assert.deepEqual(cases,codecReadersCorpus());assert.equal(cases.length,913);assert.equal(new Set(cases.map(p=>p.name)).size,913);
  assert.equal(cases.reduce((n,p)=>n+p.request.steps.length,0),27962);assert.ok(cases.every(p=>!Object.hasOwn(p,'expected')));
});
