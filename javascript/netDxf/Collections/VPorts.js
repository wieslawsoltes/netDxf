// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { RegisteredTable } from '../../runtime/RegisteredTable.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { DxfObjectReferences } from './DxfObjectReferences.js';
import { VPort } from '../Tables/VPort.js';
import { UcsReferences } from './UcsReferences.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { ArgumentException, ArgumentNullException } from '../../runtime/Errors.js';
function readOnly(items) { return Object.freeze({get Count(){return items.Count;},get_Item(i){return items.get_Item(i);},GetEnumerator(){return items.GetEnumerator();},[Symbol.iterator](){return this.GetEnumerator();}}); }
export class VPorts extends RegisteredTable {
  #records = new ReferenceList(); #view;
  constructor(document, handle = null, createActive = true) { super(document,DxfObjectCode.VportTable,handle); this.#view=readOnly(this.#records); if(createActive)this.EnsureActive(); }
  get Records() { return this.#view; }
  GetConfiguration(name) { if(name==null)throw new ArgumentNullException('name'); return readOnly(new ReferenceList(Array.from(this.#records).filter(r=>OrdinalIgnoreCaseEquals(r.Name,name)))); }
  Add(item, assignHandle = true) { if(item==null)throw new ArgumentNullException('item'); return this.get_Item(item.Name) ?? this.AddRecord(item,assignHandle); }
  AddRecord(record, assignHandle = true) {
    if(record==null)throw new ArgumentNullException('record'); if(record.Owner===this)return record;
    if(record.Owner!==null)throw new ArgumentException('Clone a viewport record before moving it between documents.','record');
    UcsReferences.ValidateXData(record,this.Owner); UcsReferences.Validate(record,this.Owner);
    if(assignHandle || !record.Handle) { do {this.Owner.NumHandles=record.AssignHandle(this.Owner.NumHandles);} while(this.Owner.GetObjectByHandle(record.Handle)!==null); }
    else if(this.Owner.GetObjectByHandle(record.Handle)!==null)throw new ArgumentException('Duplicate viewport record handle: '+record.Handle,'record');
    this.Owner.AddedObjects.Add(record.Handle,record); record.Owner=this; UcsReferences.Register(record); this.#records.Add(record);
    if(!this.List.ContainsKey(record.Name)){this.List.Add(record.Name,record);this.References.Add(record.Name,new DxfObjectReferences());} return record;
  }
  EnsureActive() { if(!this.Contains(VPort.DefaultName))this.AddRecord(VPort.Active,true); }
  Remove(value) {
    if(typeof value==='string') {
      if(VPort.IsActiveName(value)||!this.Contains(value)||this.HasReferences(value))return false;
      const records=this.GetConfiguration(value); if(Array.from(records).some(r=>r.Sun!==null))return false;
      for(const record of records)this.Remove(record);return records.Count!==0;
    }
    const record=value; if(record==null||record.Owner!==this)return false;
    if(VPort.IsActiveName(record.Name)&&this.GetConfiguration(VPort.DefaultName).Count===1)return false;
    if(this.HasReferences(record.Name)||record.Sun!==null)return false;
    UcsReferences.Unregister(record);this.Owner.AddedObjects.Remove(record.Handle);this.#records.Remove(record);this.#rebuild();record.Owner=null;record.Handle=null;return true;
  }
  ValidateRecordRename(record) { if(this.HasReferences(record.Name))throw new ArgumentException('A referenced viewport configuration cannot be renamed.'); }
  CommitRecordRename(record,name) { this.#rebuild(record,name); }
  #rebuild(renamed=null,newName=null) {
    const old=new Map(Array.from(this.References,p=>[p.Key,p.Value]));this.List.Clear();this.References.Clear();
    for(const record of this.#records){const name=record===renamed?newName:record.Name;if(this.List.ContainsKey(name))continue;this.List.Add(name,record);
      const previous=Array.from(old).find(([key])=>OrdinalIgnoreCaseEquals(key,name));this.References.Add(name,previous?previous[1]:new DxfObjectReferences());}
  }
}
