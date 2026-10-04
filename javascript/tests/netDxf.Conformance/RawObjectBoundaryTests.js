// Direct port of ObjectStoreIndependent in RawObjectBoundaryTests.cs. Other methods remain in the missing-test ledger.
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { DxfRawDocument, DxfRawObjectStore, DxfRawDictionary, DxfRawXRecord, DxfRawDictionaryVariable } from '../../index.js';
import { Run, Check, Equal } from './TestHarness.js';
import { SameRawTags } from './RawDocumentTests.js';
const sourceRoot=process.env.NETDXF_SOURCE_ROOT || path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
export function RegisterRawObjectBoundaryTests(){for(const version of ['R2000','R2018']) Run(`objects/independent/${version}`,()=>ObjectStoreIndependent(version));}
export function ObjectStoreIndependent(version){
  const bytes=fs.readFileSync(path.join(sourceRoot,'tests','fixtures',`raw-objects-${version}.dxf`));
  Equal(version==='R2000'?'308c500a530feddca2d2f01691faac2efed871bdf4fc708e0af149d0c1702e98':'0631df37e447399011c67a9650967995052bbe9f74a78bd5d65da7b06fa3eeee',createHash('sha256').update(bytes).digest('hex'));
  const raw=DxfRawDocument.Load(bytes),store=DxfRawObjectStore.Open(raw),parent=store.RootDictionary.Find('QA_OBJECTS').Handle;
  const dictionary=store.Get(parent),record=dictionary.Find('PAYLOAD').Handle; Check(dictionary instanceof DxfRawDictionary);
  Equal(record,dictionary.Find('PAYLOAD_ALIAS').Handle); const payload=store.Get(record); Check(payload instanceof DxfRawXRecord);
  Equal('ApplicationPayloadMarker',payload.Data.find(t=>t.Code===100).Value);Equal(77,payload.Data.find(t=>t.Code===280).Value);
  Equal(-9223372036854775808n,payload.Data.find(t=>t.Code===160).Value);
  const variable=dictionary.Find('VARIABLE').Handle,edit=store.BeginEdit();
  edit.SetVariable(variable,'edited independent value'); const copy=edit.CloneDictionaryTree(parent,store.RootDictionary.Handle,'COPIED_INDEPENDENT');
  const updated=edit.Commit(),reloaded=DxfRawObjectStore.Open(DxfRawDocument.Load(updated.ToBytes()));
  SameRawTags(payload.Data,reloaded.Get(record).Data); Check(reloaded.Get(variable) instanceof DxfRawDictionaryVariable);
  Equal('edited independent value',reloaded.Get(variable).Value); const copied=reloaded.Get(copy);
  Equal(copied.Find('PAYLOAD').Handle,copied.Find('PAYLOAD_ALIAS').Handle);
}
