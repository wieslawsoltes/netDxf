// Shared source-derived helpers for immutable retained table packets.
// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { ReferenceList } from './ReferenceList.js';
import { ReadOnlyReferenceView } from './DatabaseModel.js';
import { TrimDotNet } from './InvariantFloat.js';
import { DxfHandleKind } from '../netDxf/IO/DxfGroupCode.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException,NotSupportedException } from './Errors.js';
export const MaximumEditedStringLength = 1048576;
export function TableSnapshot(values) {
  const list=new ReferenceList(values),reject=()=>{throw new NotSupportedException('Collection is read-only.');};
  return Object.freeze({...ReadOnlyReferenceView(list),get Count(){return list.Count;},get length(){return list.Count;},IsReadOnly:true,
    Contains:value=>list.Contains(value),IndexOf:value=>list.IndexOf(value),CopyTo:(array,index=0)=>list.CopyTo(array,index),
    set_Item:reject,Add:reject,Insert:reject,Remove:reject,RemoveAt:reject,Clear:reject});
}
export const IsTableReference = tag => [DxfHandleKind.SoftPointer,DxfHandleKind.HardPointer,DxfHandleKind.SoftOwner,DxfHandleKind.HardOwner].includes(tag.HandleKind);
export function ValidTableUtf16(text) {
  for(let i=0;i<text.length;i++) {
    const value=text.charCodeAt(i);
    if(value>=0xd800&&value<=0xdfff) {
      if(value>0xdbff||i+1>=text.length)return false;
      const low=text.charCodeAt(++i);if(low<0xdc00||low>0xdfff)return false;
    }
  }
  return true;
}
// This is the original DxfStoredTableContent.CheckEditableText shared by styles/maps.
export function CheckTableText(text,parameter) {
  if(text==null)throw new ArgumentNullException(parameter);
  if(text.length>MaximumEditedStringLength)throw new ArgumentOutOfRangeException(parameter,undefined,'The edited string exceeds the admission limit.');
  if(text.includes('\0')||!ValidTableUtf16(text))throw new ArgumentException('Edited text must be valid UTF-16 without NUL.',parameter);
}
export function EncodeTableText(text,version,parameter='text') {
  const parts=[];let length=0;
  for(let i=0;i<text.length;i++) {
    const value=text.charCodeAt(i),escape=value===92||version<15&&value>127,size=escape?7:1;
    if(length>MaximumEditedStringLength-size)throw new ArgumentOutOfRangeException(parameter,undefined,'The encoded string exceeds the admission limit.');
    parts.push(escape?'\\U+'+value.toString(16).toUpperCase().padStart(4,'0'):text[i]);length+=size;
  }
  return parts.join('');
}
export function DecodeTableText(text) {
  const result=[];
  for(let i=0;i<text.length;i++) {
    let value=text[i];
    if(value==='\\'&&i+6<text.length&&(text[i+1]==='U'||text[i+1]==='u')&&text[i+2]==='+') {
      const hex=TrimDotNet(text.slice(i+3,i+7));
      if(/^[0-9a-f]+$/i.test(hex)){value=String.fromCharCode(parseInt(hex,16));i+=6;}
    }
    result.push(value);
  }
  return result.join('');
}

// Handles compare ordinal-ignore-case but dictionary iteration retains the first spelling.
export class TableHandleMap {
  #items=new Map();
  set(key,value){const folded=key.toUpperCase(),old=this.#items.get(folded);this.#items.set(folded,[old?old[0]:key,value]);}
  get(key){return this.#items.get(key.toUpperCase())?.[1];}
  has(key){return this.#items.has(key.toUpperCase());}
  [Symbol.iterator](){return this.#items.values();}
}
