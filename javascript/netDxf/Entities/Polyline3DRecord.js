// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfObject } from '../DxfObject.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { DxfTag } from '../IO/DxfTag.js';
import { Vector3 } from '../Vector3.js';
import { Layer } from '../Tables/Layer.js';
import { Linetype } from '../Tables/Linetype.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { IsStoredReference, HasNonzeroXDataReference, SameSequence } from '../../runtime/StoredRecord.js';
import { InvalidCastException, InvalidOperationException, NotSupportedException, NullReferenceException } from '../../runtime/Errors.js';
const at=(list,i)=>list.get_Item?list.get_Item(i):list[i];
/** Original internal retained-record model; construction does not register a DXF record. */
export class Polyline3DRecord extends DxfObject {
  Tags;Resources=new Map();OriginalResourceNames=new Map();Coordinates=new Map();MetadataGroups=new Map();
  ReactorHandles=new ReferenceList();OriginalReactors=new ReferenceList();ExtensionHandle=null;OriginalExtension=null;SourceOwner=null;
  CommonEnd=0;XDataStart;Position=Vector3.Zero;IsAuthored=false;IsRemoved=false;SourceDocument=null;
  IdentityIndex=0;OwnerIndex=0;HasPrivateData=false;SourceVersion=0;UsesBlockRecordOwner=false;
  constructor(codeName,tags){super(codeName);if(tags===null)throw new NullReferenceException();this.Tags=tags;this.XDataStart=tags.Count??tags.length;}
  get Layer(){return Array.from(this.Resources.values()).find(r=>r instanceof Layer)??null;}
  get Linetype(){return Array.from(this.Resources.values()).find(r=>r instanceof Linetype)??null;}
  get IsSequenceEnd(){return this.CodeName===DxfObjectCode.EndSequence;}
  get StoredOwner(){if(!this.UsesBlockRecordOwner)return this.Owner;const b=this.Owner?.Owner;return b?.CodeName===DxfObjectCode.Block?b.Record??null:null;}
  get References(){return this.Resources.values();}
  get OpaqueHandleTags(){const r=this;return {*[Symbol.iterator](){for(let i=0;i<r.XDataStart;i++){
    if(r.MetadataGroups.has(i)){i=r.MetadataGroups.get(i);continue;}const tag=at(r.Tags,i);
    if(i!==r.IdentityIndex&&i!==r.OwnerIndex&&!r.Resources.has(i)&&IsStoredReference(tag))yield tag;
  }}};}
  Validate(document,owner,registered){
    if(this.IsRemoved)throw new InvalidOperationException('A removed VERTEX cannot be adopted or emitted again.');
    if(document.DrawingVariables.AcadVer!==this.SourceVersion)throw new NotSupportedException('Retained record version conversion requires schema regeneration.');
    if((this.SourceDocument!==null&&this.SourceDocument!==document)||this.Owner!==owner)throw new InvalidOperationException('Retained record source ownership changed.');
    const current=this.Handle===null?null:document.GetObjectByHandle(this.Handle);
    if(registered?current!==this:current!=null&&current!==this)throw new InvalidOperationException('Inconsistent retained record registration.');
    if(registered&&(this.StoredOwner===null||document.GetObjectByHandle(this.StoredOwner.Handle)!==this.StoredOwner))throw new InvalidOperationException('Invalid stored owner.');
    if(this.SourceDocument===null&&!registered)return;
    for(const target of this.References)if(document.GetObjectByHandle(target.Handle)!==target)throw new InvalidOperationException('Retained resource is outside its document.');
  }
  CanClone(){return !this.HasPrivateData&&this.ExtensionDictionary===null&&this.PersistentReactors.Count===0&&Array.from(this.Resources.values()).every(r=>r instanceof Layer||r instanceof Linetype)&&!HasNonzeroXDataReference(this);}
  TopologyTagCount(){
    let count=this.XDataStart,extension=false,reactors=false;
    for(const [first,last] of this.MetadataGroups){
      const isExtension=at(this.Tags,first).Value==='{ACAD_XDICTIONARY';const unchanged=isExtension?this.ExtensionDictionary===this.OriginalExtension:
        SameSequence(this.PersistentReactors,this.OriginalReactors)&&SameSequence(Array.from(this.ReactorHandles).filter(h=>h!=='0'),Array.from(this.PersistentReactors,t=>t.Handle));
      if(!unchanged){count-=last-first+1;count+=isExtension?(this.ExtensionDictionary===null?0:3):(this.PersistentReactors.Count===0?0:this.PersistentReactors.Count+2);}
      if(isExtension)extension=true;else reactors=true;
    }
    if(!extension&&this.ExtensionDictionary!==null)count+=3;if(!reactors&&this.PersistentReactors.Count!==0)count+=this.PersistentReactors.Count+2;
    for(const data of this.XData.Values)count+=1+data.XDataRecord.Count;return count;
  }
  CopyForClone(){
    const copy=new Polyline3DRecord(this.CodeName,new ReferenceList(this.Tags));
    for(const k of ['SourceOwner','CommonEnd','XDataStart','Position','IdentityIndex','OwnerIndex','ExtensionHandle','UsesBlockRecordOwner','SourceVersion','IsAuthored'])copy[k]=Copy(this[k]);
    for(const k of ['Coordinates','MetadataGroups','OriginalResourceNames'])for(const [a,b] of this[k])copy[k].set(a,b);
    copy.ReactorHandles.AddRange(this.ReactorHandles);
    for(const [k,value] of this.Resources){if(value===null)throw new NullReferenceException();if(!(value instanceof Layer)&&!(value instanceof Linetype))throw new InvalidCastException();copy.Resources.set(k,value.Clone());}
    for(const d of this.XData.Values)copy.XData.Add(d.Clone());return copy;
  }
}
