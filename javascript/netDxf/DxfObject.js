// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { XDataDictionary } from './Collections/XDataDictionary.js';
import { ObservableCollectionEventArgs } from './Collections/ObservableCollectionEventArgs.js';
import { EventHook } from '../runtime/EventHook.js';
import { ReferenceList } from '../runtime/ReferenceList.js';
import { ArgumentException, NotSupportedException } from '../runtime/Errors.js';
export class DxfObject {
  #codeName; #handle = null; #owner = null; #extension = null; #xdata = new XDataDictionary(); #reactors = new ReferenceList();
  constructor(codename) {
    if (new.target === DxfObject) throw new NotSupportedException('DxfObject is abstract.');
    this.#codeName = codename;
    Object.defineProperties(this, { XDataAddAppReg: { value: new EventHook(), enumerable: true }, XDataRemoveAppReg: { value: new EventHook(), enumerable: true } });
    this.#xdata.AddAppReg.Add((sender, e) => this.OnXDataAddAppRegEvent(e.Item));
    this.#xdata.RemoveAppReg.Add((sender, e) => this.OnXDataRemoveAppRegEvent(e.Item));
  }
  get CodeName() { return this.#codeName; }
  set CodeName(value) { this.#codeName = value; } // protected in C#
  get Handle() { return this.#handle; }
  set Handle(value) { this.#handle = value; } // internal in C#
  get Owner() { return this.#owner; }
  set Owner(value) { this.#owner = value; } // internal in C#
  get ExtensionDictionary() { return this.#extension; }
  set ExtensionDictionary(value) { this.#extension = value; } // internal in C#
  get PersistentReactors() { return this.#reactors; }
  get XData() { return this.#xdata; }
  OnXDataAddAppRegEvent(item) { this.XDataAddAppReg.Invoke(this, new ObservableCollectionEventArgs(item)); }
  OnXDataRemoveAppRegEvent(item) { this.XDataRemoveAppReg.Invoke(this, new ObservableCollectionEventArgs(item)); }
  AssignHandle(entityNumber) {
    if (typeof entityNumber !== 'bigint' || BigInt.asIntN(64, entityNumber) !== entityNumber) throw new ArgumentException('Expected a signed Int64 BigInt.', 'entityNumber');
    this.#handle = BigInt.asUintN(64, entityNumber).toString(16).toUpperCase(); return BigInt.asIntN(64, entityNumber + 1n);
  }
  ToString() { return this.#codeName; }
}
