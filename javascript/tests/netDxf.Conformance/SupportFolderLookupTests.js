// Original identities/assertions from the immutable C# reference. Native process-
// observer, Parallel.For and reflection/.NET path-oracle cases remain unregistered.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { DxfDocument, SupportFolders } from '../../node-entry.js';
import { ArgumentException } from '../../runtime/Errors.js';
import { Run, Check, Equal, Throws, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';

export function RegisterSupportFolderLookupTests() {
  for (const [name, action] of [
    ['direct-file-precedence', SupportLookupDirect], ['ordered-fallback', SupportLookupOrder],
    ['relative-and-absolute-folders', SupportLookupPaths], ['empty-and-missing', SupportLookupMissing],
    ['failure-does-not-change-directory', SupportLookupFailure],
  ]) Run('support-lookup/' + name, action);
  for (const version of SupportedVersions) for (const binary of [false, true])
    Run(`support-lookup/loaded-document/${VersionName(version)}/${BooleanName(binary)}`, () => SupportLookupDocument(version, binary));
}
export function WithSupportLookupFolders(action) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'netDxf-lookup-')), current = process.cwd();
  try { action(root); }
  finally { process.chdir(current); fs.rmSync(root, { recursive: true, force: true }); }
}
export function SupportLookupFile(folder, name) {
  fs.mkdirSync(folder, { recursive: true }); const file = path.join(folder, name);
  fs.writeFileSync(file, 'resource fixture, not opened by FindFile'); return path.resolve(file);
}
export function SupportLookupDirect() { WithSupportLookupFolders(root => {
  const drawing = path.join(root, 'drawing'), first = path.join(drawing, 'first'), second = path.join(root, 'second');
  const expected = SupportLookupFile(drawing, 'symbol.shx'); SupportLookupFile(first, 'symbol.shx'); SupportLookupFile(second, 'symbol.shx');
  const folders = new SupportFolders(['first', second]); folders.WorkingFolder = drawing;
  Equal(expected, folders.FindFile('symbol.shx'), 'Existing drawing-relative file was overridden');
  Equal(expected, folders.FindFile(expected), 'Existing absolute file was overridden');
  Equal(expected, folders.FindFile('.' + path.sep + 'symbol.shx'), 'Dot-relative file precedence');
}); }
export function SupportLookupOrder() { WithSupportLookupFolders(root => {
  const drawing = path.join(root, 'drawing'); fs.mkdirSync(drawing);
  const first = path.join(drawing, 'first'), second = path.join(root, 'second');
  const firstFile = SupportLookupFile(first, 'symbol.shx'), secondFile = SupportLookupFile(second, 'symbol.shx');
  const folders = new SupportFolders(['missing', 'first', second]); folders.WorkingFolder = drawing;
  Equal(firstFile, folders.FindFile(path.join('lost', 'symbol.shx')), 'First matching support folder did not win');
  folders.Remove('first'); Equal(secondFile, folders.FindFile('symbol.shx'), 'Support folder removal was ignored');
  folders.Insert(0, 'first'); Equal(firstFile, folders.FindFile('symbol.shx'), 'Support folder order was ignored');
}); }
export function SupportLookupPaths() { WithSupportLookupFolders(root => {
  const drawing = path.join(root, 'drawing'); fs.mkdirSync(drawing);
  const unicode = 'Zażółć 東京.shx', parent = SupportLookupFile(root, unicode), child = SupportLookupFile(path.join(drawing, 'fonts'), 'child.shx');
  const folders = new SupportFolders(['fonts', '..']); folders.WorkingFolder = drawing; const current = process.cwd();
  Equal(child, folders.FindFile(path.join('invalid', 'child.shx')), 'Relative support folder base');
  Equal(parent, folders.FindFile(unicode), 'Parent support folder and Unicode path');
  Equal(parent, folders.FindFile(path.join('..', unicode)), 'Parent direct path');
  Equal(current, process.cwd(), 'Lookup changed process directory');
  process.chdir(root); folders.WorkingFolder = 'drawing';
  Equal(child, folders.FindFile('child.shx'), 'Relative working folder');
  Equal(root, process.cwd(), 'Relative working folder changed process directory');
}); }
export function SupportLookupMissing() { WithSupportLookupFolders(root => {
  const folders = new SupportFolders([null, '', 'missing']); folders.WorkingFolder = root;
  Equal('', folders.FindFile(null), 'Null filename should remain a miss');
  Equal('', folders.FindFile(''), 'Empty filename should remain a miss');
  Equal('', folders.FindFile('absent.shx'), 'Missing filename');
  Equal('', folders.FindFile(root + path.sep), 'A directory is not a file');
  const expected = SupportLookupFile(root, 'exists.shx'); folders.WorkingFolder = path.join(root, 'not-created');
  Equal(expected, folders.FindFile(expected), 'An absolute resource should not require the working directory to exist');
  Equal('', folders.FindFile('absent.shx'), 'Nonexistent working folder should give a miss');
}); }
export function SupportLookupFailure() { WithSupportLookupFolders(root => {
  const current = process.cwd(), folders = new SupportFolders(); folders.WorkingFolder = root;
  Throws(ArgumentException, () => folders.FindFile('bad\0path.shx'));
  Equal(current, process.cwd(), 'Invalid filename changed process directory');
  folders.WorkingFolder = 'bad\0directory'; Throws(ArgumentException, () => folders.FindFile('absent.shx'));
  Equal(current, process.cwd(), 'Invalid base path changed process directory');
  folders.WorkingFolder = root; folders.AddRange(['bad\0support']);
  Throws(ArgumentException, () => folders.FindFile('absent.shx'));
  Equal(current, process.cwd(), 'Invalid support path changed process directory');
}); }
export function SupportLookupDocument(version, binary) { WithSupportLookupFolders(root => {
  const drawing = path.join(root, 'drawing'); fs.mkdirSync(drawing);
  const expected = SupportLookupFile(path.join(drawing, 'fonts'), 'shape-Żółć.shx');
  SupportLookupFile(path.join(drawing, 'later-fonts'), 'shape-Żółć.shx'); const file = path.join(drawing, 'drawing.dxf');
  const document = new DxfDocument(version); Check(document.Save(file, binary), 'Support lookup document failed to save.'); const current = process.cwd();
  const loaded = DxfDocument.Load(file, ['fonts', 'later-fonts']); Check(loaded !== null, 'Support lookup document failed to load.');
  Equal(expected, loaded.SupportFolders.FindFile('shape-Żółć.shx'), 'Loaded drawing used wrong relative resource base or priority');
  Equal(current, process.cwd(), "Loaded drawing's lookup changed process directory");
}); }
