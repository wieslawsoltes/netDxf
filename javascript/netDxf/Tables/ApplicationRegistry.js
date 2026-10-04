// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { TableObject } from './TableObject.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { ArgumentException, ArgumentNullException } from '../../runtime/Errors.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import * as Binding from './ApplicationRegistry.Binding.js';
export class ApplicationRegistry extends TableObject {
  static get DefaultName() { return 'ACAD'; }
  static get Default() { return new ApplicationRegistry(ApplicationRegistry.DefaultName); }
  constructor(name, checkName = true) {
    super(name, DxfObjectCode.AppId, checkName);
    if (name == null || name === '') throw new ArgumentNullException('name');
    this.IsReserved = OrdinalIgnoreCaseEquals(name, ApplicationRegistry.DefaultName);
  }
  HasReferences() { return this.Owner != null && this.Owner.HasReferences(this.Name); }
  GetReferences() { return this.Owner?.GetReferences(this.Name) ?? null; }
  AttachXData(dictionary) { Binding.AttachXData(this, dictionary); }
  DetachXData(dictionary) { Binding.DetachXData(this, dictionary); }
  OnNameChangedEvent(oldName, newName) { Binding.OnNameChangedEvent(this, oldName, newName, (a,b) => super.OnNameChangedEvent(a,b)); }
  Clone(...args) { return args.length === 0 ? this.Clone(this.Name) : CloneApplicationRegistry(this, args[0]); }
  CloneStoredGraph() { return CloneApplicationRegistry(this, this.Name); }
}
function CloneApplicationRegistry(root, rootName) {
  const copies = new Map();
  function shell(source) { const copy = new ApplicationRegistry(source === root ? rootName : source.Name); copies.set(source, copy); return copy; }
  const copy = shell(root), pending = [{ source: root, copy, iterator: root.XData.Values.GetEnumerator(), deferred: null }];
  // Preserve depth-first construction/alias semantics without recursive native-stack growth.
  try {
    while (pending.length) {
      const frame = pending[pending.length - 1];
      if (frame.deferred !== null) {
        const data = frame.deferred, registry = copies.get(data.ApplicationRegistry); frame.deferred = null;
        if (frame.copy.XData.ContainsAppId(registry.Name)) throw new ArgumentException('The clone name collides with another application registry in its XData graph.', 'rootName');
        frame.copy.XData.Add(data.CopyForRegistry(registry)); continue;
      }
      if (!frame.iterator.MoveNext()) { frame.iterator.Dispose(); pending.pop(); continue; }
      const data = frame.iterator.Current; frame.deferred = data;
      if (!copies.has(data.ApplicationRegistry)) {
        const source = data.ApplicationRegistry, child = shell(source);
        pending.push({ source, copy: child, iterator: source.XData.Values.GetEnumerator(), deferred: null });
      }
    }
    return copy;
  } finally { for (const frame of pending) frame.iterator.Dispose(); }
}
