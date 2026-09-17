// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { XDataRecord } from './XDataRecord.js';
import { ReferenceList } from '../runtime/ReferenceList.js';
import { ArgumentNullException } from '../runtime/Errors.js';
export class XData {
  #appReg; #container = null; #records = new ReferenceList();
  constructor(appReg) { if (appReg == null) throw new ArgumentNullException('appReg'); this.#appReg = appReg; }
  get ApplicationRegistry() { return this.#appReg; }
  set ApplicationRegistry(value) { this.#appReg = value; } // internal in C#
  get Container() { return this.#container; }
  set Container(value) { this.#container = value; } // internal in C#
  get XDataRecord() { return this.#records; }
  CopyStoredGraph() { return this.CopyForRegistry(this.#appReg.CloneStoredGraph()); }
  CopyForRegistry(registry) {
    const copy = new XData(registry);
    for (const record of this.#records) copy.#records.Add(new XDataRecord(record.Code, record.Value instanceof Uint8Array ? new Uint8Array(record.Value) : record.Value));
    return copy;
  }
  ToString() { return this.#appReg.Name; }
  Clone() { return this.CopyForRegistry(this.#appReg.Clone()); }
}
