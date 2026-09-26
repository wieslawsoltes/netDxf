// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { TableContentState } from '../../runtime/TableContentState.js';
import { TableHandleMap, CheckTableText, EncodeTableText, DecodeTableText, IsTableReference } from '../../runtime/TablePayload.js';
import { ConsumeManagedEnumerable } from '../../runtime/ManagedEnumerable.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { DxfTag } from '../IO/DxfTag.js';
import { Vector3 } from '../Vector3.js';
import { ArgumentException, ArgumentNullException, InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
const bits = value => { const view=new DataView(new ArrayBuffer(8));view.setFloat64(0,value);return view.getBigUint64(0); };
function sameScalar(a,b) {
  if(typeof a==='number' && typeof b==='number')return bits(a)===bits(b);
  if(a instanceof Vector3 && b instanceof Vector3)return sameScalar(a.X,b.X)&&sameScalar(a.Y,b.Y)&&sameScalar(a.Z,b.Z);
  return a===b;
}
export function InstallTableContentEditing(Type) {
  Type.prototype.ReplaceContent=function(name,description,tableStyle,values) {
    const s=TableContentState(this),fail=message=>{throw new InvalidOperationException(message);};
    if(s.editing){s.reentered=true;fail('TABLECONTENT replacement cannot be reentered.');}
    if(values==null)throw new ArgumentNullException('values');
    CheckTableText(name,'name');CheckTableText(description,'description');s.editing=true;s.reentered=false;
    try {
      const edits=[];
      ConsumeManagedEnumerable(values,edit=>{
        if(edit==null)throw new ArgumentException('A value edit cannot be null.','values');
        if(edits.length>=this.StoredValues.Count)throw new ArgumentException('Too many value edits for this snapshot.','values');edits.push(edit);
      });
      if(s.reentered)fail('TABLECONTENT replacement was reentered during enumeration.');
      if(!s.resolved || this.IsErased || this.Database===null || this.Database.Document!==s.source || s.source.GetObjectByHandle(this.Handle)!==this)fail('TABLECONTENT must remain registered in its source document.');
      const errors=this.Database.Validate();if(errors.Count)fail('Cannot replace TABLECONTENT in an invalid source database: '+Array.from(errors).join('; '));
      for(let owner=this.Owner;owner!==null;owner=owner.Owner)if(owner.CodeName==='ACAD_TABLE' && typeof owner.Validate==='function')owner.Validate(s.source);
      if(this.Name===null || this.Description===null)throw new NotSupportedException('The linked-data header is not qualified for replacement.');
      if(tableStyle!==null && (tableStyle.CodeName!=='TABLESTYLE' || tableStyle.Handle===null || s.source.GetObjectByHandle(tableStyle.Handle)!==tableStyle))throw new ArgumentException('The style must be an actual registered TABLESTYLE in the source document.','tableStyle');
      if(tableStyle!==this.TableStyle)for(let owner=this.Owner;owner!==null;owner=owner.Owner)if(owner.CodeName==='ACAD_TABLE')throw new NotSupportedException('Changing the style of TABLE-owned content requires cross-object synchronization.');
      const current=new Set(this.StoredValues),seen=new Set();
      for(const edit of edits){if(!current.has(edit.Original)||seen.has(edit.Original))throw new ArgumentException('Value edits must identify distinct values from the current snapshot.','values');seen.add(edit.Original);}
      const tags=Array.from(this.Payload),encode=text=>EncodeTableText(text,this.SourceVersion);let changed=false;
      if(name!==this.Name){tags[1]=new DxfTag(1,encode(name));changed=true;}
      if(description!==this.Description){tags[2]=new DxfTag(300,encode(description));changed=true;}
      if(tableStyle!==this.TableStyle){tags[tags.length-1]=new DxfTag(340,tableStyle===null?'0':tableStyle.Handle);changed=true;}
      for(const edit of edits){
        const original=edit.Original,value=edit.Value;
        if(!sameScalar(original.Value,value)){
          const index=original.ScalarIndex;
          if(value instanceof Vector3){tags[index]=new DxfTag(11,value.X);tags[index+1]=new DxfTag(21,value.Y);tags[index+2]=new DxfTag(31,value.Z);}
          else tags[index]=new DxfTag(tags[index].Code,typeof value==='string'?encode(value):value);
          changed=true;
        }
        if(original.DisplayIndex>=0 && original.FormattedText!==edit.FormattedText){tags[original.DisplayIndex]=new DxfTag(302,encode(edit.FormattedText));changed=true;}
      }
      if(!changed)return;
      const candidate=new Type(s.source,tags,DecodeTableText),next=TableContentState(candidate);
      if(candidate.StoredValues.Count!==this.StoredValues.Count)fail('Replacement changed the qualified value frame inventory.');
      const handles=new TableHandleMap(),references=new ReferenceList();
      for(let i=0;i<tags.length;i++){
        const tag=tags[i];if(!IsTableReference(tag)||BigInt('0x'+tag.Value)===0n)continue;
        const target=i===tags.length-1?tableStyle:s.handles.get(tag.Value);references.Add(target);handles.set(tag.Value,target);
      }
      Object.assign(s,{payload:next.payload,subclasses:next.subclasses,values:next.values,name:next.name,description:next.description,styleHandle:next.styleHandle,style:tableStyle,references,handles});
    }finally{s.editing=false;s.reentered=false;}
  };
}
