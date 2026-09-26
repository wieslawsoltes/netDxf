// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { CellStyleMapState } from '../../runtime/CellStyleMapState.js';
import { TableSnapshot,CheckTableText,EncodeTableText } from '../../runtime/TablePayload.js';
import { ConsumeManagedEnumerable } from '../../runtime/ManagedEnumerable.js';
import { OrdinalIgnoreCaseKey } from '../../runtime/Collections.js';
import { DxfTag } from '../IO/DxfTag.js';
import { XDataCode } from '../XDataCode.js';
import { ArgumentException,ArgumentNullException,InvalidOperationException } from '../../runtime/Errors.js';
const invalid=message=>{throw new InvalidOperationException(message);};
export function InstallCellStyleMapEditing(Type,EntryType) {
  Type.prototype.ReplaceEntryNames=function(names) {
    const state=CellStyleMapState(this);
    if(state.editing){state.reentered=true;invalid('CELLSTYLEMAP name replacement cannot be reentered.');}
    if(names==null)throw new ArgumentNullException('names');state.editing=true;state.reentered=false;
    try {
      const replacements=[];
      ConsumeManagedEnumerable(names,name=>{
        if(replacements.length>=this.Entries.Count)throw new ArgumentException('The name count must equal the current entry count.','names');
        CheckTableText(name,'names');replacements.push(name);
      });
      if(state.reentered)invalid('CELLSTYLEMAP name replacement was reentered during enumeration.');
      if(replacements.length!==this.Entries.Count)throw new ArgumentException('The name count must equal the current entry count.','names');
      if(!state.resolved||this.IsErased||this.Database===null||this.Database.Document!==state.source||state.source.GetObjectByHandle(this.Handle)!==this)invalid('CELLSTYLEMAP must remain registered in its source document.');
      const errors=this.Database.Validate();if(errors.Count)invalid('Cannot edit CELLSTYLEMAP in an invalid source database: '+Array.from(errors).join('; '));
      let count=this.Payload.Count+2;if(this.ExtensionDictionary!==null)count+=3;
      const reactors=new Set(Array.from(this.PersistentReactors).filter(item=>item!==null).map(item=>item.Handle===null?null:OrdinalIgnoreCaseKey(item.Handle)));
      if(reactors.size)count+=reactors.size+2;
      for(const data of this.XData.Values){count++;for(const record of data.XDataRecord)count+=record.Code===XDataCode.BinaryData?Math.max(1,Math.trunc((record.Value.length+126)/127)):1;}
      if(count>Type.MaximumPayloadTags)invalid('CELLSTYLEMAP and its common metadata exceed the stored record tag limit.');
      if(Array.from(this.Entries).every((entry,i)=>entry.Name===replacements[i]))return;
      const tags=Array.from(this.Payload),entries=[];
      for(let i=0;i<this.Entries.Count;i++) {
        const original=this.Entries.get_Item(i),name=replacements[i];
        if(name!==original.Name)tags[original.NameIndex]=new DxfTag(300,EncodeTableText(name,this.SourceVersion,'name'));
        entries.push(new EntryType(original.Id,original.StoredType,name,Array.from(original.FormatPayload),original.NameIndex));
      }
      const nextPayload=TableSnapshot(tags),nextEntries=TableSnapshot(entries);state.payload=nextPayload;state.entries=nextEntries;
    }finally{state.editing=false;state.reentered=false;}
  };
}
