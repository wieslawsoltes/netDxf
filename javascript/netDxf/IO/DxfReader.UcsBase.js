// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { UCS } from '../Tables/UCS.js';
import { SubclassMarker } from '../SubclassMarker.js';
import { UcsReferences } from '../Collections/UcsReferences.js';
import { WrappedInvalidData } from '../../runtime/DxfXDataIO.js';
import { ArgumentException, InvalidDataException, NullReferenceException } from '../../runtime/Errors.js';
export class UcsBaseContext {
  #controls = 0; #publicSubclass = true; #xdata = false;
  Observe(code, value) {
    if (code === 1001 && this.#controls === 0) this.#xdata = true;
    if (code === 102) {
      if (value === null) throw new NullReferenceException();
      if (value.startsWith('{')) this.#controls++;
      else if (value === '}' && this.#controls > 0) this.#controls--;
    } else if (code === 100 && this.#controls === 0) this.#publicSubclass = value === SubclassMarker.Ucs;
  }
  get IsPublic() { return this.#publicSubclass && this.#controls === 0 && !this.#xdata; }
}
export function CompleteUcsBase(context, owner, type, handle) {
  try { owner.SetLoadedOrthographicBase(type, null, handle !== null); }
  catch (error) { if (error instanceof ArgumentException) throw WrappedInvalidData('Invalid UCS orthographic base fields.', error); throw error; }
  if (type !== 0 || handle !== null) context.ucsBaseReferences.push([owner, handle]);
}
export function ResolveUcsBaseReferences(context) {
  for (const [owner, handle] of context.ucsBaseReferences) {
    if (context.GetObjectBySourceHandle(owner.Handle) !== owner)
      throw new InvalidDataException('The referring UCS source record was not retained.');
    const empty = handle === null || handle === '0', resolved = empty ? null : context.GetObjectBySourceHandle(handle);
    const target = resolved instanceof UCS ? resolved : null;
    if (target === null && !empty) throw new InvalidDataException('Unresolved or non-UCS source identity for group 346: ' + handle);
    try { owner.SetLoadedOrthographicBase(owner.OrthographicViewType, target, handle !== null); }
    catch (error) { if (error instanceof ArgumentException) throw WrappedInvalidData('Invalid UCS base reference.', error); throw error; }
  }
  for (const ucs of context.Document.UCSs) UcsReferences.Validate(ucs, context.Document);
}
