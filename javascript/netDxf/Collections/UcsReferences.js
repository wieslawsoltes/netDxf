// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { UcsReferenceHost } from '../../runtime/UcsReferenceHost.js';
import { ArgumentException, InvalidOperationException } from '../../runtime/Errors.js';
export const UcsReferences = Object.freeze({
  ...UcsReferenceHost,
  Targets(owner) {
    if (owner.CodeName === 'UCS') return owner.BaseUcs === null ? [] : [owner.BaseUcs];
    const data = owner.CodeName === 'VIEW' ? owner.Ucs : owner.CodeName === 'VPORT' ? owner : null;
    return data === null ? [] : [data.NamedUcs,data.BaseUcs].filter(value=>value !== null);
  },
  Validate(owner, document) {
    for (const target of this.Targets(owner)) if (target.Owner !== document.UCSs || target.Handle === null || document.GetObjectByHandle(target.Handle) !== target)
      throw new ArgumentException('A referenced UCS must already be registered in the same document.','target');
    if (owner.CodeName === 'UCS') owner.ValidateOrthographicBase();
    if (owner.CodeName === 'VIEW' && owner.Ucs !== null) owner.Ucs.Validate();
    if (owner.CodeName === 'VPORT' && owner.BaseUcs !== null && owner.UcsOrthographicType === 0)
      throw new InvalidOperationException('A VPORT base UCS requires a nonzero orthographic type.');
  },
  ValidateXData(owner, document) {
    for (const data of owner.XData.Values) if (data.ApplicationRegistry.Owner !== null && data.ApplicationRegistry.Owner.Owner !== document)
      throw new ArgumentException('Clone XData whose application registry belongs to another document before adding this table record.','owner');
  },
});
