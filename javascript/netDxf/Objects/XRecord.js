// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { ReferenceList } from '../../runtime/ReferenceList.js';
/** Legacy layer-state projection, not the registered DxfXRecord object. */
export class XRecord {
  #entries=new ReferenceList(); Handle=''; OwnerHandle=''; Flags=1;
  get Codename(){return 'XRECORD';}
  get Entries(){return this.#entries;}
}
