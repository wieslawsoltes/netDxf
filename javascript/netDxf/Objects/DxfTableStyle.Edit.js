// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { TableStyleState } from '../../runtime/TableStyleState.js';
import { CheckTableText,EncodeTableText,DecodeTableText,IsTableReference } from '../../runtime/TablePayload.js';
import { ConsumeManagedEnumerable } from '../../runtime/ManagedEnumerable.js';
import { DxfTag } from '../IO/DxfTag.js';
import { ArgumentException,ArgumentNullException,InvalidOperationException,NotSupportedException } from '../../runtime/Errors.js';
const invalid=message=>{throw new InvalidOperationException(message);};
function single(tags,code){const found=Array.from(tags).filter(t=>t.Code===code);if(found.length!==1)invalid('Sequence does not contain exactly one matching element.');return found[0];}
function add(map,key,value){if(map.has(key))throw new ArgumentException('An item with the same key has already been added.');map.set(key,value);}
function replaceScalar(map,tag,value){if(!Object.is(tag.Value,value))add(map,tag,new DxfTag(tag.Code,value));}
export function InstallTableStyleEditing(Type) {
  Type.prototype.ReplaceStyle=function(header,rows) {
    const state=TableStyleState(this);
    if(state.editing){state.reentered=true;invalid('TABLESTYLE replacement cannot be reentered.');}
    if(rows==null)throw new ArgumentNullException('rows');state.editing=true;state.reentered=false;
    try {
      const edits=[];
      ConsumeManagedEnumerable(rows,edit=>{
        if(edit==null)throw new ArgumentException('A row edit cannot be null.','rows');
        if(edits.length>=this.Rows.Count)throw new ArgumentException('Too many row edits for this snapshot.','rows');edits.push(edit);
      });
      if(state.reentered)invalid('TABLESTYLE replacement was reentered during enumeration.');
      if(!state.resolved||this.IsErased||this.Database===null||this.Database.Document!==state.source||state.source.GetObjectByHandle(this.Handle)!==this)invalid('TABLESTYLE must remain registered in its source document.');
      const errors=this.Database.Validate();if(errors.Count)invalid('Cannot replace TABLESTYLE in an invalid source database: '+Array.from(errors).join('; '));
      if(header!==null&&this.Header===null)throw new NotSupportedException('The stored TABLESTYLE header is not qualified for editing.');
      const current=new Set(this.Rows),seen=new Set();
      for(const edit of edits){if(!current.has(edit.Original)||seen.has(edit.Original))throw new ArgumentException('Row edits must identify distinct rows from the current snapshot.','rows');seen.add(edit.Original);}
      const replacements=new Map(),bindings=new Map(state.namedStyles);
      if(header!==null) {
        const offset=this.Header.StoredVersion!==null?1:0;
        if(header.Description!==this.Header.Description)add(replacements,state.publicTags[offset],new DxfTag(3,EncodeTableText(header.Description,this.SourceVersion)));
        const scalars=[header.FlowDirection,header.StoredFlags,header.HorizontalCellMargin,header.VerticalCellMargin,header.SuppressTitle?1:0,header.SuppressColumnHeading?1:0];
        scalars.forEach((value,i)=>replaceScalar(replacements,state.publicTags[offset+i+1],value));
      }
      for(const edit of edits) {
        const tags=edit.Original.Tags,values=edit.Values;
        if(edit.TextStyle!==null) {
          const style=edit.TextStyle,out={};
          if(style.Handle===null||style.Owner!==state.source.TextStyles||state.source.GetObjectByHandle(style.Handle)!==style||!state.source.TextStyles.TryGetValue(style.Name,out)||out.value!==style)throw new ArgumentException('The requested STYLE must already be registered in the source document.','textStyle');
          CheckTableText(style.Name,'textStyle');
          if(style!==edit.Original.TextStyle){const replacement=new DxfTag(7,EncodeTableText(style.Name,this.SourceVersion));add(replacements,tags.get_Item(0),replacement);bindings.delete(tags.get_Item(0));bindings.set(replacement,[style,style.Name]);}
        }
        if(edit.DataTypes!==null){replaceScalar(replacements,single(tags,90),edit.DataTypes.StoredDataType);replaceScalar(replacements,single(tags,91),edit.DataTypes.StoredUnitType);}
        if(values!==null)for(const [code,value] of [[140,values.TextHeight],[170,values.CellAlignment],[62,values.StoredTextColor],[63,values.StoredFillColor],[283,values.BackgroundColorEnabled?1:0]])replaceScalar(replacements,single(tags,code),value);
        if(edit.Borders!==null)for(let i=0;i<6;i++){const border=edit.Borders.Values.get_Item(i);replaceScalar(replacements,single(tags,274+i),border.StoredLineweight);replaceScalar(replacements,single(tags,284+i),border.IsVisible?1:0);replaceScalar(replacements,single(tags,64+i),border.StoredColor);}
      }
      if(replacements.size===0)return;
      const packet=Array.from(this.Tags,t=>replacements.get(t)??t),candidate=new Type(state.source,packet,DecodeTableText),next=TableStyleState(candidate);
      if(candidate.Rows.Count!==this.Rows.Count||Array.from(candidate.Rows).some((row,i)=>['Values','Borders','DataTypes'].some(key=>(row[key]===null)!==(this.Rows.get_Item(i)[key]===null))))invalid('Replacement changed the qualified row inventory.');
      for(const row of candidate.Rows){const binding=bindings.get(row.Tags.get_Item(0));if(binding)row.BindTextStyle(binding[0]);}
      const references=[];
      for(const tag of packet)if(IsTableReference(tag)&&state.handles.has(tag.Value.toUpperCase()))references.push(state.handles.get(tag.Value.toUpperCase()));
      for(const tag of next.publicTags){const binding=bindings.get(tag);if(binding)references.push(binding[0]);}
      // No caller enumeration/validation remains after this point.
      state.tags=next.tags;state.header=next.header;state.rows=next.rows;state.publicTags=next.publicTags;state.namedStyles=bindings;state.references=new ReferenceList(references);
    } finally {state.editing=false;state.reentered=false;}
  };
}
