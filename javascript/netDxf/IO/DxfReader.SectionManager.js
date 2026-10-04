// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfStoredSectionManager } from '../Objects/DxfStoredSectionManager.js';
import { DxfOpaqueObject } from '../Objects/DxfOpaqueObject.js';
import { ReadStoredObjectHeader } from './DxfReader.LayerFilterPointer.js';
import { PayloadEnd } from '../../runtime/DatabaseIOContext.js';
import { FormatException } from '../../runtime/Errors.js';
export function ReadSectionManagerRecord(context,codeName,tags) {
  const version=context.Document.DrawingVariables.AcadVer;
  if(version>=15 && tags.every(tag=>tag.Code!==100))throw new FormatException('SECTION_MANAGER requires its subclass marker.');
  const {record,opaque,payload:start,handle}=ReadStoredObjectHeader(tags),end=PayloadEnd(tags,start),body=tags.slice(start,end);
  const unknown=version<15 || opaque.length!==0 || body.some(tag=>tag.Code===102 || tag.Code===100 && tag.Value!=='AcDbSectionManager' || ![100,70,90,330].includes(tag.Code) && tag.Code<1000)
    || body.some(tag=>tag.Code===70 && (tag.Value<0 || tag.Value>1));
  if(unknown){for(let i=start;i<tags.length;i++)opaque.push(tags[i]);record.Object=new DxfOpaqueObject(codeName,opaque);record.Object.Handle=handle;return record;}
  if(body.length<3 || body[0].Code!==100 || body[0].Value!=='AcDbSectionManager' || body[1].Code!==70 || body[2].Code!==90 || body.slice(3).some(tag=>tag.Code!==330))
    throw new FormatException('SECTION_MANAGER requires one ordered subclass, update flag, count and section-pointer list.');
  const count=body[2].Value;
  if(count<0 || count>DxfStoredSectionManager.MaximumSections || body.length-3!==count)throw new FormatException('SECTION_MANAGER section count does not match its bounded pointer list.');
  // DxfTag's handle grammar was already checked before this record adapter.
  if(new Set(record.Metadata.Reactors.map(value=>BigInt('0x'+value))).size!==record.Metadata.Reactors.length)throw new FormatException('SECTION_MANAGER repeats a persistent-reactor identity.');
  const manager=new DxfStoredSectionManager(context.Document,codeName,body,body[1].Value!==0,body.slice(3).map(tag=>tag.Value));
  manager.Handle=handle;record.Object=manager;
  if(end<tags.length)context.ReadDatabaseXData(manager,tags,end);context.storedSectionManagers.push(manager);return record;
}
export function ResolveSectionManagerReferences(context) {for(const manager of context.storedSectionManagers)manager.Resolve(key=>context.GetObjectBySourceHandle(key));}
