// Port of pinned CrossFeaturePersistenceTests.cs. Atomic operations use real files.
import { DxfDocument, Line, Vector3, Hatch, HatchPattern, DxfRawDocument, DxfRawHandleIndex, DxfRawHandleRole, MemoryStream, FileStream } from '../../node-entry.js';
import { InvalidDataException } from '../../runtime/Errors.js';
import { Run, Check, Equal, Throws, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import { WithAtomicDirectory, AtomicPrepare, AtomicUnchanged } from './AtomicSaveTests.js';
import { CheckLifetimeFileReleased } from './FileStreamLifetimeTests.js';
import { OleSingle } from './Ole2FrameTests.js';
export function RegisterCrossFeaturePersistenceTests(){for(const v of SupportedVersions)for(const b of [false,true]){const suffix=`${VersionName(v)}/${BooleanName(b)}`;Run('integration/atomic-empty-hatch/'+suffix,()=>AtomicEmptyHatchRejection(v,b));Run('integration/atomic-remapped-raw/'+suffix,()=>AtomicRemappedRaw(v,b));}}
export function AtomicEmptyHatchRejection(v,b){WithAtomicDirectory(path=>{AtomicPrepare(path,true);const doc=new DxfDocument(v);doc.Name='before failure';const folder=doc.SupportFolders.WorkingFolder,hatch=new Hatch(HatchPattern.Solid,false);doc.Entities.Add(hatch);const handle=hatch.Handle,seed=doc.DrawingVariables.HandleSeed;Throws(InvalidDataException,()=>doc.SaveAtomic(path,b));AtomicUnchanged(path,true);Equal('before failure',doc.Name,'Atomic empty-HATCH failure changed the name');Equal(folder,doc.SupportFolders.WorkingFolder,'Atomic empty-HATCH failure changed the folder');Equal(handle,hatch.Handle,'Atomic empty-HATCH preflight changed identity');Equal(seed,doc.DrawingVariables.HandleSeed,'Atomic empty-HATCH preflight allocated handles');Equal(1,Array.from(doc.Entities.Hatches).length,'Atomic rejection dropped the empty entity');});}
export function AtomicRemappedRaw(v,b){WithAtomicDirectory(path=>{
  const typed=new DxfDocument(v);typed.Comments.Clear();typed.Entities.Add(new Line(new Vector3(1,2,3),new Vector3(4,5,6)));const source=new MemoryStream(),unchanged=new MemoryStream();try{
    Check(typed.Save(source,b),'Atomic remap source export failed');source.Position=0;const raw=DxfRawDocument.Load(source),index=DxfRawHandleIndex.Create(raw),line=OleSingle(Array.from(index.Occurrences).filter(x=>x.Role===DxfRawHandleRole.Identity&&x.Record.Name==='LINE')),edited=index.RemapHandles(new Map([[line.Handle,'FFFFFF']]));AtomicPrepare(path,true);edited.SaveAtomic(path,!b);
    const output=new FileStream(path);try{const saved=DxfRawDocument.Load(output);Equal('LINE',OleSingle(DxfRawHandleIndex.Create(saved).FindDefinitions('FFFFFF')).Record.Name,'Atomic save lost the remapped identity');output.Position=0;const loaded=DxfDocument.Load(output);Check(loaded!==null,'Atomic remapped drawing rejected');Check(new Vector3(1,2,3).Equals(OleSingle(loaded.Entities.Lines).StartPoint),'Atomic remap changed geometry');}finally{output.Dispose();}
    raw.Save(unchanged);Equal(source.ToArray(),unchanged.ToArray(),'Atomic remap mutated original snapshot bytes');CheckLifetimeFileReleased(path);
  }finally{source.Dispose();unchanged.Dispose();}
});}
