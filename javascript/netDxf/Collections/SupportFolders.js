// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ArgumentNullException, NotSupportedException } from '../../runtime/Errors.js';
let host = null;
/** Explicit platform adapter. No filesystem access is performed by the portable entry. */
export function SetSupportFolderHost(value) { const previous = host; host = value; return previous; }
export class SupportFolders {
  #folders;
  constructor(folders = []) {
    if (folders == null) throw new ArgumentNullException('folders');
    this.#folders = new ReferenceList(folders); this.WorkingFolder = host?.CurrentDirectory() ?? '';
  }
  get Count() { return this.#folders.Count; }
  get IsReadOnly() { return false; }
  get_Item(index) { return this.#folders.get_Item(index); }
  set_Item(index, value) { if (value == null || value === '') throw new ArgumentNullException('value'); this.#folders.set_Item(index, value); }
  FindFile(file) {
    if (file == null || file === '') return '';
    if (host === null) throw new NotSupportedException('FindFile requires the Node entry or an explicit support-folder host.');
    const basePath = host.FullPath(this.WorkingFolder), candidate = host.Resolve(file, basePath);
    if (host.Exists(candidate)) return candidate;
    const name = host.FileName(file); if (!name) return '';
    for (const folder of this.#folders) if (folder != null && folder !== '') {
      const candidate = host.Resolve(host.Combine(folder, name), basePath); if (host.Exists(candidate)) return candidate;
    }
    return '';
  }
  Add(item) { this.#check(item); this.#folders.Add(item); }
  AddRange(collection) { this.#folders.AddRange(collection); }
  Clear() { this.#folders.Clear(); }
  Contains(item) { this.#check(item); return this.#folders.Contains(item); }
  CopyTo(array, arrayIndex = 0) { this.#folders.CopyTo(array, arrayIndex); }
  Remove(item) { this.#check(item); return this.#folders.Remove(item); }
  IndexOf(item) { return this.#folders.IndexOf(item); }
  Insert(index, item) { this.#check(item); this.#folders.Insert(index, item); }
  RemoveAt(index) { this.#folders.RemoveAt(index); }
  GetEnumerator() { return this.#folders.GetEnumerator(); }
  [Symbol.iterator]() { return this.GetEnumerator(); }
  #check(item) { if (item == null || item === '') throw new ArgumentNullException('item'); }
}
