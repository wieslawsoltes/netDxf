import test from 'node:test';
import assert from 'node:assert/strict';
import * as api from '../../index.js';
import { DxfTransport as io } from '../../index.js';
import { TextCodeValueReader } from '../../netDxf/IO/TextCodeValueReader.js';
import { DecodeDxfText, EncodeDxfText, EncodeDxfDatabaseText } from '../../runtime/DxfStringEncoding.js';
import { IOException } from '../../runtime/Errors.js';
import { transportSectionsCorpus } from '../../tools/transport-sections-corpus.mjs';
import { transportSectionsCall } from '../../tools/transport-sections-wire.mjs';
import { ValidateTransportObservation } from '../../tools/transport-sections-observation.mjs';
const chunk=(code,value)=>({Code:code,CurrentPosition:14,ReadInt:()=>value,ReadLong:()=>value,ReadShort:()=>value,ReadBytes:()=>value,ReadString:()=>value,ReadDouble:()=>value});
const output=()=>({tags:[],Write(code,value){this.tags.push([code,value]);}});
const readCommon=(data,code,value,version=18)=>io.ReadEntityCommonData(chunk(code,value),version,data);

test('transport standalone exports agree with the browser-safe package barrel',async()=>{
  assert.equal((await import('../../netDxf/IO/DxfThumbnailImage.js')).DxfThumbnailImage,api.DxfThumbnailImage);
  assert.equal((await import('../../netDxf/IO/DxfEntityCommonData.js')).ReadEntityCommonData,io.ReadEntityCommonData);
});
test('empty preview writes nothing while null validation preserves parameter order',()=>{
  const out=output();api.DxfThumbnailImage.Write(out,new Uint8Array());assert.equal(out.tags.length,0);
  assert.throws(()=>api.DxfThumbnailImage.Write(null,null),{name:'ArgumentNullException',ParamName:'chunk'});
  assert.throws(()=>api.DxfThumbnailImage.Write(out,null),{name:'ArgumentNullException',ParamName:'data'});
  assert.throws(()=>api.DxfThumbnailImage.Read(null),{name:'ArgumentNullException',ParamName:'chunk'});
});
test('preview leaves ENDSEC current and retains the following record',()=>{
  const reader=new TextCodeValueReader('2\nTHUMBNAILIMAGE\n90\n2\n310\n0305\n0\nENDSEC\n0\nEOF\n');reader.Next();
  assert.deepEqual(api.DxfThumbnailImage.Read(reader),Uint8Array.of(3,5));assert.equal(reader.ReadString(),'ENDSEC');reader.Next();assert.equal(reader.ReadString(),'EOF');
});
test('preview never allocates its untrusted declared count',()=>{
  const reader=new TextCodeValueReader('2\nTHUMBNAILIMAGE\n90\n2147483647\n0\nENDSEC\n');reader.Next();
  assert.throws(()=>api.DxfThumbnailImage.Read(reader),{name:'InvalidDataException'});assert.equal(reader.Code,0);
});
for(const nodeBuffer of [false,true])test('preview packets retain independent storage '+nodeBuffer,()=>{
  const bytes=nodeBuffer?Buffer.alloc(255,7):new Uint8Array(255).fill(7),out=output();api.DxfThumbnailImage.Write(out,bytes);bytes.fill(9);
  const packets=out.tags.filter(([code])=>code===310).map(([,value])=>value);assert.deepEqual(packets.map(p=>p.length),[127,127,1]);assert.ok(packets.every(p=>p[0]===7));packets[0][0]=10;assert.equal(packets[1][0],7);
});
test('preview caller exceptions retain the completed prefix and are not swallowed',()=>{
  const calls=[],error=new IOException('injected'),out={Write(c,v){calls.push([c,v]);if(c===310)throw error;}};
  assert.throws(()=>api.DxfThumbnailImage.Write(out,new Uint8Array(128)),e=>e===error);assert.deepEqual(calls.map(([c])=>c),[0,2,90,310]);
});
test('common metadata accepts count after chunks and completes without aliasing source bytes',()=>{
  const data=new io.EntityCommonDataReader(),bytes=Uint8Array.of(2,3);readCommon(data,310,bytes);bytes.fill(8);readCommon(data,92,2);data.Complete();
  assert.deepEqual(data.ProxyGraphics,Uint8Array.of(2,3));assert.equal(data.Payload.CanRead,false);data.Complete();assert.deepEqual(data.ProxyGraphics,Uint8Array.of(2,3));
});
test('missing proxy count differs from a declared empty proxy buffer',()=>{
  const absent=new io.EntityCommonDataReader();absent.Complete();assert.equal(absent.ProxyGraphics,null);
  const empty=new io.EntityCommonDataReader();readCommon(empty,92,0);empty.Complete();assert.equal(empty.ProxyGraphics.length,0);
  const uncounted=new io.EntityCommonDataReader();readCommon(uncounted,310,new Uint8Array());assert.throws(()=>uncounted.Complete(),{name:'InvalidDataException'});
});
test('proxy overrun and 129-byte packet fail before changing the retained payload',()=>{
  const data=new io.EntityCommonDataReader();readCommon(data,92,128);assert.throws(()=>readCommon(data,310,new Uint8Array(129)),{name:'InvalidDataException'});assert.equal(data.ActualLength,0);assert.equal(data.Payload.Length,0);
});
test('64-bit proxy size validation precedes conversion and allocation',()=>{
  const data=new io.EntityCommonDataReader();assert.throws(()=>readCommon(data,160,9223372036854775807n),{name:'InvalidDataException'});assert.equal(data.Payload,null);assert.equal(data.DeclaredLength,null);
});
test('duplicate metadata guards preserve completed state and read timing',()=>{
  const data=new io.EntityCommonDataReader();readCommon(data,430,'First');let reads=0;
  assert.throws(()=>io.ReadEntityCommonData({...chunk(430,'Second'),ReadString(){reads++;return 'Second';}},18,data),{name:'InvalidDataException'});assert.equal(reads,0);assert.equal(data.ColorName,'First');
  assert.throws(()=>io.ReadEntityCommonData(chunk(430,'Ignored'),13,new io.EntityCommonDataReader()),{name:'InvalidDataException'});
});
test('color decoding failure preserves ColorNameSeen and the caller exception',()=>{
  const data=new io.EntityCommonDataReader(),failure=new Error('decoder');assert.throws(()=>io.ReadEntityCommonData(chunk(430,'text'),18,data,()=>{throw failure;}),e=>e===failure);assert.equal(data.ColorNameSeen,true);assert.equal(data.ColorName,null);
});
test('proxy output uses the source-version count code and never changes the model',()=>{
  const entity=new api.Line();entity.ProxyGraphics=Uint8Array.of(1,2);const before=entity.ProxyGraphics;
  for(const version of [13,16,17,18]){const out=output();io.WriteEntityCommonData(out,version,entity.CommonData);assert.equal(out.tags[0][0],version<17?92:160);assert.equal(out.tags[0][1],version<17?2:2n);}
  assert.deepEqual(entity.ProxyGraphics,before);
});
test('proxy packets copy even Node Buffer views',()=>{
  const entity=new api.Line();entity.CommonData.ProxyGraphics=Buffer.alloc(128,4);const out=output();io.WriteEntityCommonData(out,18,entity.CommonData);entity.CommonData.ProxyGraphics.fill(9);
  assert.equal(out.tags[1][1][0],4);assert.equal(out.tags[2][1][0],4);
});
test('database color strings escape literals and line controls without double decoding',()=>{
  const entity=new api.Line();entity.ColorName='\\U+0041\0\r\nΩ';const out=output();io.WriteEntityCommonData(out,14,entity.CommonData);
  assert.equal(DecodeDxfText(out.tags[0][1]),entity.ColorName);assert.equal(out.tags[0][1].includes('\n'),false);
});
test('MTEXT ignores foreground codes without allocating a background object',()=>{
  const state={value:null};for(const code of [420,430,440,310])assert.equal(io.TryReadMTextBackground(chunk(code,0),state),false);assert.equal(state.value,null);
});
test('MTEXT rejected first value still exposes the initialized ref object and inner error',()=>{
  const state={value:null};assert.throws(()=>io.TryReadMTextBackground(chunk(45,0),state),error=>error.name==='InvalidDataException'&&error.InnerException.name==='ArgumentOutOfRangeException'&&error.InnerException.ParamName==='value');
  assert.equal(state.value.Flags,0);assert.equal(state.value.ScaleFactor,null);assert.equal(state.value.ColorIndex,null);
});
test('MTEXT repeated recognized fields update in place; a rejection preserves the earlier value',()=>{
  const state={value:null};io.TryReadMTextBackground(chunk(45,2),state);const original=state.value;
  io.TryReadMTextBackground(chunk(45,3),state);assert.equal(state.value,original);assert.throws(()=>io.TryReadMTextBackground(chunk(45,-1),state),{name:'InvalidDataException'});assert.equal(state.value.ScaleFactor,3);
});
test('active MTEXT defaults are materialized only in emitted tags',()=>{
  const b=new api.MTextBackgroundFill();b.ScaleFactor=null;b.ColorIndex=null;const out=output();io.WriteMTextBackground(out,18,b);
  assert.deepEqual(out.tags,[[90,1],[45,1.5],[63,7]]);assert.equal(b.ScaleFactor,null);assert.equal(b.ColorIndex,null);
});
test('MTEXT writer rejects unsupported profiles before writing any tag',()=>{
  const out=output(),b=api.MTextBackgroundFill.CreateTextFrame();assert.throws(()=>io.WriteMTextBackground(out,17,b),{name:'NotSupportedException'});assert.equal(out.tags.length,0);
});
test('mesh size preflight precedes vertex reads and accounts for repeated shared faces',()=>{
  const face={length:1048576},mesh={Faces:{Count:2048,get_Item:()=>face},get Vertexes(){throw new Error('premature vertex read');}};
  assert.throws(()=>io.ValidateMeshOutput(mesh,'Shared'),error=>error.name==='InvalidOperationException'&&error.message.includes('serialized face-list size'));
});
test('MESH versus POLYLINE mesh profile validation visits unused blocks',()=>{
  const document=new api.DxfDocument(13),block=new api.Block('Unused'),mesh=new api.Mesh([api.Vector3.Zero,api.Vector3.UnitX,api.Vector3.UnitY],[[0,1,2]]);block.Entities.Add(mesh);document.Blocks.Add(block);
  assert.throws(()=>io.ValidateMeshVersions(document),{name:'NotSupportedException'});document.DrawingVariables.AcadVer=16;io.ValidateMeshVersions(document);io.ValidateDocumentMeshOutput(document);
});
test('transport input corpus is deterministic with no embedded native expectations',()=>{
  const corpus=transportSectionsCorpus();assert.deepEqual(corpus,transportSectionsCorpus());assert.equal(corpus.length,1165);assert.equal(new Set(corpus.map(p=>p.name)).size,1165);assert.equal(corpus.reduce((n,p)=>n+p.request.steps.length,0),3473);assert.ok(corpus.every(p=>!Object.hasOwn(p,'expected')));
});
for(const property of ['output','common','reader','background'])test('transport observation rejects omitted '+property,()=>{
  const probe=transportSectionsCorpus()[0],rows=transportSectionsCall(probe.request);ValidateTransportObservation(rows,probe.request.steps.length);delete rows[0].value[property];assert.throws(()=>ValidateTransportObservation(rows,probe.request.steps.length));
});
test('DXF string adapters retain null, UTF-16 surrogate units and single-pass decoding',()=>{
  assert.equal(DecodeDxfText(null),null);assert.equal(EncodeDxfText(null,14),'');assert.equal(EncodeDxfText(null,18),null);
  assert.equal(DecodeDxfText('\\U+005CU+0041'),'\\U+0041');assert.equal(EncodeDxfText('😀',14),'\\U+D83D\\U+DE00');assert.equal(DecodeDxfText(EncodeDxfDatabaseText('\\U+0041 😀',14)),'\\U+0041 😀');
});
