// Complete port of pinned AtomicSaveTests.cs, including typed and filesystem cases.
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { DxfDocument, Line, Vector3, DxfRawDocument, DxfRawOptions, DxfTag, DxfVersion, MemoryStream, FileStream } from '../../node-entry.js';
import { DxfAtomicFile } from '../../netDxf/IO/DxfAtomicFile.js';
import * as E from '../../runtime/Errors.js';
import { Run, Check, Equal, SameDoubleBits, Throws, SupportedVersions, HeaderVersion, VersionName, BooleanName } from './TestHarness.js';
import { FileLifetimeConflictingDocument, CheckLifetimeFileReleased } from './FileStreamLifetimeTests.js';
import { fileURLToPath } from 'node:url';
import { SameRawTags } from './RawDocumentTests.js';
export const AtomicRawVersions = [DxfVersion.AutoCad12, DxfVersion.AutoCad13, DxfVersion.AutoCad14, ...SupportedVersions];
export const AtomicOriginal = Uint8Array.from({ length: 257 }, (_, i) => i);
export function RegisterAtomicSaveTests() {
  for(const v of SupportedVersions)for(const b of [false,true])for(const e of [false,true]) {
    const suffix=VersionName(v)+'/'+BooleanName(b)+'/'+BooleanName(e);
    Run('atomic/typed/success/'+suffix,()=>AtomicTypedSuccess(v,b,e));
    Run('atomic/typed/failure/'+suffix,()=>AtomicTypedFailure(v,b,e));
    Run('atomic/typed/cancellation/'+suffix,()=>AtomicTypedCancellation(v,b,e));
  }
  Run('atomic/open-path-errors',AtomicPathErrors);
  Run('atomic/symlink-and-readonly',AtomicLinksAndReadOnly);
  for (const v of AtomicRawVersions) for (const b of [false, true]) for (const e of [false, true]) {
    const suffix = `${VersionName(v)}/${BooleanName(b)}/${BooleanName(e)}`;
    Run('atomic/raw/exact/' + suffix, () => AtomicRawExact(v, b, e));
    Run('atomic/raw/invalid-transport/' + suffix, () => AtomicRawInvalid(v, b, e));
  }
  for (const e of [false, true]) for (let f = 0; f < 4; f++)
    Run(`atomic/staged-failure/${BooleanName(e)}/${f}`, () => AtomicStagedFailure(e, f));
  Run('atomic/reader-observes-old-or-new', AtomicReaderVisibility);
  Run('atomic/raw/budget-preserves-file', AtomicRawBudget);
}
export function WithAtomicDirectory(action) {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'netdxf-atomic-'));
  try { return action(path.join(directory, 'Zażółć drawing.dxf')); }
  finally { fs.rmSync(directory, { recursive: true, force: true }); }
}
export function AtomicPrepare(filename, existing) { if (existing) fs.writeFileSync(filename, AtomicOriginal); }
export function AtomicUnchanged(filename, existing) {
  Equal(existing, fs.existsSync(filename), 'Destination existence changed.');
  if (existing) Equal(AtomicOriginal, new Uint8Array(fs.readFileSync(filename)));
  Check(!fs.readdirSync(path.dirname(filename)).some(f => f.startsWith('.netdxf-') && f.endsWith('.tmp')), 'Staging file leaked.');
  CheckLifetimeFileReleased(filename);
}
export function AtomicRawSource(version, binary, invalid = false) {
  const T = (code, value) => new DxfTag(code, value);
  const tags = [T(0,'SECTION'),T(2,'HEADER'),T(9,'$ACADVER'),T(1,HeaderVersion(version)),T(9,'$DWGCODEPAGE'),T(3,'ANSI_1252'),T(0,'ENDSEC'),
    T(0,'SECTION'),T(2,'ENTITIES'),T(0,'LINE'),T(5,'AB'),T(10,1e-20),T(20,-0),T(30,3),T(11,4),T(21,5),T(31,6)];
  if (invalid) tags.push(binary ? T(1,'binary\nmultiline') : T(999,'comment'));
  tags.push(T(0,'ENDSEC'), T(0,'EOF')); return DxfRawDocument.Create(tags, binary);
}
export function AtomicRawExact(version, binary, existing) {
  WithAtomicDirectory(filename => {
    const authored = AtomicRawSource(version, binary), expected = authored.ToBytes();
    const input = new MemoryStream(expected), raw = DxfRawDocument.Load(input);
    AtomicPrepare(filename, existing); raw.SaveAtomic(filename);
    Equal(expected, new Uint8Array(fs.readFileSync(filename)), 'Exact atomic save lost bytes.');
    raw.SaveAtomic(filename, !binary);
    const changed = new FileStream(filename);
    try { SameRawTags(raw.Tags, DxfRawDocument.Load(changed).Tags); }
    finally { changed.Dispose(); }
    Check(raw.HasOriginalBytes && input.CanRead);
  });
}
export function AtomicRawInvalid(version, binary, existing) {
  WithAtomicDirectory(filename => {
    AtomicPrepare(filename, existing); const raw = AtomicRawSource(version, binary, true);
    Throws(E.NotSupportedException, () => raw.SaveAtomic(filename, !binary)); AtomicUnchanged(filename, existing);
    Throws(E.OperationCanceledException, () => raw.SaveAtomic(filename, binary, { aborted: true })); AtomicUnchanged(filename, existing);
  });
}
export function InvokeAtomicWriter(filename, write, token = null) { DxfAtomicFile.Write(filename, write, token); }
export function AtomicStagedFailure(existing, mode) {
  WithAtomicDirectory(filename => {
    AtomicPrepare(filename, existing); const token = { aborted: false };
    const write = stream => {
      Equal(path.dirname(filename), path.dirname(stream.Name)); stream.Write(new Uint8Array(123456));
      if (mode === 0) throw new E.IOException('Injected disk write failure.');
      if (mode === 1) { token.aborted = true; return; }
      if (mode === 2) { stream.Dispose(); return; }
      if (existing) fs.unlinkSync(filename); else fs.writeFileSync(filename, AtomicOriginal);
    };
    Throws(mode === 1 ? E.OperationCanceledException : mode === 2 ? E.ObjectDisposedException : E.IOException,
      () => InvokeAtomicWriter(filename, write, token));
    AtomicUnchanged(filename, mode === 3 ? !existing : existing);
  });
}
export function AtomicReaderVisibility() {
  WithAtomicDirectory(filename => {
    AtomicPrepare(filename, true); const opened = new FileStream(filename), replacement = new Uint8Array(300000).fill(71);
    try {
      InvokeAtomicWriter(filename, stream => {
        stream.Write(replacement, 0, replacement.length / 2);
        Equal(AtomicOriginal, new Uint8Array(fs.readFileSync(filename)), 'Uncommitted prefix visible.');
        stream.Write(replacement, replacement.length / 2, replacement.length / 2);
      });
      Equal(replacement, new Uint8Array(fs.readFileSync(filename)));
      const original = new Uint8Array(opened.Length); Equal(original.length, opened.Read(original)); Equal(AtomicOriginal, original);
    } finally { opened.Dispose(); }
  });
}
export function AtomicRawBudget() {
  WithAtomicDirectory(filename => {
    AtomicPrepare(filename, true); const source = AtomicRawSource(DxfVersion.AutoCad2018, false);
    const limited = DxfRawDocument.Create(source.Tags, false, new DxfRawOptions(16, 1000, 100));
    Throws(E.InvalidDataException, () => limited.SaveAtomic(filename)); AtomicUnchanged(filename, true);
  });
}

export function AtomicTypedSuccess(version,binary,existing){WithAtomicDirectory(file=>{
  AtomicPrepare(file,existing);const doc=new DxfDocument(version);doc.Comments.Clear();doc.Entities.Add(new Line(new Vector3(1e-20,2,3),new Vector3(4,5,6)));doc.SaveAtomic(file,binary);
  Equal('Zażółć drawing',doc.Name,'Atomic document name');Equal(path.dirname(file),doc.SupportFolders.WorkingFolder,'Atomic working folder');const loaded=DxfDocument.Load(file);Check(loaded!==null,'Atomic typed reload failed.');Equal(version,loaded.DrawingVariables.AcadVer,'Atomic typed version');
  const lines=Array.from(loaded.Entities.Lines);Equal(1,lines.length);SameDoubleBits(1e-20,lines[0].StartPoint.X,'Atomic typed geometry');CheckLifetimeFileReleased(file);
  Check(!fs.readdirSync(path.dirname(file)).some(f=>f.startsWith('.netdxf-')&&f.endsWith('.tmp')),'Success leaked staging file.');
  const dir=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../artifacts/conformance/fixtures');fs.mkdirSync(dir,{recursive:true});fs.copyFileSync(file,path.join(dir,`atomic-save-${VersionName(version)}-${BooleanName(binary)}-${BooleanName(existing)}.dxf`));
});}
export function AtomicTypedFailure(version,binary,existing){WithAtomicDirectory(file=>{
  AtomicPrepare(file,existing);const doc=FileLifetimeConflictingDocument(version);doc.Name='original name';const folder=doc.SupportFolders.WorkingFolder;
  Throws(E.InvalidDataException,()=>doc.SaveAtomic(file,binary));Equal('original name',doc.Name,'Failed atomic save changed name');Equal(folder,doc.SupportFolders.WorkingFolder,'Failed atomic save changed working folder');AtomicUnchanged(file,existing);
});}
export function AtomicTypedCancellation(version,binary,existing){WithAtomicDirectory(file=>{
  AtomicPrepare(file,existing);const doc=new DxfDocument(version);doc.Name='not touched';const handles=doc.DrawingVariables.HandleSeed;
  Throws(E.OperationCanceledException,()=>doc.SaveAtomic(file,binary,{aborted:true}));Equal(handles,doc.DrawingVariables.HandleSeed,'Pre-cancellation allocated handles');Equal('not touched',doc.Name,'Pre-cancellation changed name');AtomicUnchanged(file,existing);
});}
export function AtomicPathErrors(){WithAtomicDirectory(file=>{
  const doc=new DxfDocument();Throws(E.ArgumentNullException,()=>doc.SaveAtomic(null));Throws(E.ArgumentException,()=>doc.SaveAtomic(''));Throws(E.IOException,()=>doc.SaveAtomic(path.dirname(file)));Throws(E.DirectoryNotFoundException,()=>doc.SaveAtomic(path.join(file,'missing','a.dxf')));AtomicUnchanged(file,false);
});}
export function AtomicLinksAndReadOnly(){WithAtomicDirectory(file=>{
  fs.writeFileSync(file,AtomicOriginal);const mode=fs.statSync(file).mode;fs.chmodSync(file,mode&~0o222);
  try{Throws(E.UnauthorizedAccessException,()=>new DxfDocument().SaveAtomic(file));}finally{fs.chmodSync(file,mode);}
  AtomicUnchanged(file,true);
  if(process.platform!=='win32'){
    const link=file+'.link';fs.symlinkSync(file,link);Throws(E.NotSupportedException,()=>new DxfDocument().SaveAtomic(link));Equal(file,fs.readlinkSync(link),'Atomic save replaced link.');AtomicUnchanged(file,true);
    fs.unlinkSync(link);fs.symlinkSync(file+'.missing',link);Throws(E.NotSupportedException,()=>new DxfDocument().SaveAtomic(link));Check(!fs.existsSync(file+'.missing'),'Atomic save followed dangling link.');
  }
});}
