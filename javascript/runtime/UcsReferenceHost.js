import { ArgumentException, InvalidOperationException } from './Errors.js';
// Internal ownership adapter. It does not create a document or register any object.
function documentOf(owner) {
  const seen = new Set();
  while (owner != null) {
    if (seen.has(owner)) throw new InvalidOperationException('Cyclic ownership chain.');
    seen.add(owner);
    if (owner.CodeName === 'DOCUMENT') return owner;
    owner = owner.Owner;
  }
  return null;
}
function targets(owner) {
  if (owner?.CodeName === 'UCS') return owner.BaseUcs == null ? [] : [owner.BaseUcs];
  const value = owner?.CodeName === 'VIEW' ? owner.Ucs : owner?.CodeName === 'VPORT' ? owner : null;
  return value == null ? [] : [value.NamedUcs, value.BaseUcs].filter(v => v != null);
}
function referenceList(document, target) {
  const refs = document.UCSs.References;
  return typeof refs.get_Item === 'function' ? refs.get_Item(target.Name) : refs.get(target.Name);
}
export const UcsReferenceHost = Object.freeze({
  Check(owner, target) {
    const document = documentOf(owner);
    if (document !== null && target != null && (target.Owner !== document.UCSs || target.Handle == null || document.GetObjectByHandle(target.Handle) !== target))
      throw new ArgumentException('A referenced UCS must already be registered in the same document.', 'target');
  },
  Replace(owner, previous, next) {
    this.Check(owner, next); const document = documentOf(owner);
    if (document === null || previous === next) return;
    if (previous != null) referenceList(document, previous).Remove(owner);
    if (next != null) referenceList(document, next).Add(owner);
  },
  Register(owner) { const document = documentOf(owner); if (document !== null) for (const target of targets(owner)) referenceList(document, target).Add(owner); },
  Unregister(owner) { const document = documentOf(owner); if (document !== null) for (const target of targets(owner)) referenceList(document, target).Remove(owner); }
});
