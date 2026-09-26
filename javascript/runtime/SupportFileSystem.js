import { ArgumentException, NotSupportedException } from './Errors.js';
let adapter = null;
/** Explicit host for legacy support-file lookup; portable model/text APIs do not open files. */
export function SetSupportFileSystem(value) {
  const previous = adapter;
  if (value === null) { adapter = null; return previous; }
  if (!value || ['ReadAllBytes','Exists'].some(key => typeof value[key] !== 'function') ||
      typeof value.DirectorySeparators !== 'string' || !value.DirectorySeparators.includes('/') ||
      typeof value.InvalidPathChars !== 'string') throw new ArgumentException('Invalid support-file host.', 'value');
  adapter = Object.freeze({ ReadAllBytes: value.ReadAllBytes.bind(value), Exists: value.Exists.bind(value),
    DirectorySeparators: value.DirectorySeparators, InvalidPathChars: value.InvalidPathChars });
  return previous;
}
function host() {
  if (!adapter) throw new NotSupportedException('Support-file access requires @netdxf/javascript/node or an explicit support-file host.');
  return adapter;
}
export const SupportFileSystem = Object.freeze({
  ReadAllBytes(file) { return host().ReadAllBytes(file); },
  Exists(file) { return host().Exists(file); },
  get DirectorySeparators() { return adapter?.DirectorySeparators ?? '/'; },
  get InvalidPathChars() { return adapter?.InvalidPathChars ?? '\0'; }
});
export function PathFileName(file) {
  let index = -1;
  for (const separator of SupportFileSystem.DirectorySeparators) index = Math.max(index, file.lastIndexOf(separator));
  return file.slice(index + 1);
}
export function PathExtension(file) {
  const name = PathFileName(file), dot = name.lastIndexOf('.');
  return dot < 0 || dot === name.length - 1 ? '' : name.slice(dot);
}
export function PathFileNameWithoutExtension(file) {
  const name = PathFileName(file), dot = name.lastIndexOf('.');
  return dot < 0 ? name : name.slice(0, dot);
}
