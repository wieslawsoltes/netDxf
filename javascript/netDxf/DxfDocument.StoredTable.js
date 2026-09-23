// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import * as api from '../index.js';
import { IsRetainedParent } from '../runtime/RetainedPolylineRegistration.js';
import { HatchSourceRelations } from './Entities/HatchSourceRelations.js';
import { ObjectMetadataMembers } from './DxfDocument.MetadataReferences.js';
import { ArgumentNullException, NotSupportedException } from '../runtime/Errors.js';
const simpleEntities=new Set(['ARC','CIRCLE','ELLIPSE','3DFACE','LINE','POINT','POLYLINE','LWPOLYLINE','SOLID','SPLINE','HELIX','TRACE','MESH','RAY','XLINE','WIPEOUT','HATCH','LIGHT','OLEFRAME','OLE2FRAME','BODY','REGION','3DSOLID']);
const methods = {
  ValidateStoredTableEntityAdoption(entity){
    if(entity==null)throw new ArgumentNullException('entity');
    if(entity instanceof api.MultiLeader)entity.ValidateIncoming(this);
    if(entity instanceof api.Section)entity.Validate(this);
    if(entity instanceof api.StoredTable)entity.ValidateIncoming(this);
    if(entity instanceof api.Insert)this.ValidateStoredTableBlockAdoption(entity.Block);
    else if(entity instanceof api.Dimension&&entity.Block!==null)this.ValidateStoredTableBlockAdoption(entity.Block);
    if(IsRetainedParent(entity))entity.ValidateStoredRecords(this,false);
    if(!simpleEntities.has(entity.CodeName)&&!['DIMENSION','ARC_DIMENSION','LEADER','TOLERANCE','INSERT','SHAPE','TEXT','MTEXT','IMAGE','MLINE','DGNUNDERLAY','DWFUNDERLAY','PDFUNDERLAY','VIEWPORT','MULTILEADER','SECTION','SECTIONOBJECT','ACAD_TABLE'].includes(entity.CodeName))
      throw new NotSupportedException('Unknown entity registration: '+entity.CodeName);
  },
  ValidateStoredTableBlockAdoption(root){
    const visited=new Set();
    const visit=block=>{
      if(block==null||visited.has(block)||this.Blocks.Contains(block.Name))return;
      visited.add(block);
      for(const entity of block.Entities){
        if(entity instanceof api.Hatch)HatchSourceRelations.ValidateOwner(entity,block,this);
        if(IsRetainedParent(entity))entity.ValidateStoredRecords(this,false);
        if(entity instanceof api.Section)entity.Validate(this);
        else if(entity instanceof api.StoredTable)entity.ValidateIncoming(this);
        else if(entity instanceof api.Insert)visit(entity.Block);
        else if(entity instanceof api.Dimension)visit(entity.Block);
      }
    };visit(root);
  },
  StoredTableReferencesRemoval(root){
    // The source's section-reference scan initializes the object database even
    // for an ordinary entity. Keep that observable allocation and inspect every
    // registered schema reference which can constrain the admitted objects.
    const removed=new Set(ObjectMetadataMembers(root));
    const block=root instanceof api.Block?root:root instanceof api.Layout?root.AssociatedBlock:null;
    if(block!==null){for(const member of ObjectMetadataMembers(block))removed.add(member);removed.add(block.Record);for(const item of [...block.Entities,...block.AttributeDefinitions.Values])for(const member of ObjectMetadataMembers(item))removed.add(member);}
    for(const item of removed)if(item instanceof api.Hatch)item.ValidateOpaqueSourceRelease();
    if(this.StoredPolylineReferencesRemoval(removed))return true;
    if(this.SectionReferencesRemoval(removed))return true;
    const database=this.Objects;
    for(const item of removed)if(api.SunReferences.Get(item)!==null)return true;
    for(const item of database.Items){
      if(removed.has(item))continue;
      if(['SECTION_SETTINGS','TABLECONTENT','TABLEGEOMETRY','CELLSTYLEMAP','SUNSTUDY','FIELD','DIMASSOC'].includes(item.CodeName)){
        // Stored packets also reference owner-held ATTRIB/ENDBLK/layout VIEWPORT
        // identities which intentionally are not top-level database registrations.
        const references = item.References ?? (item instanceof api.DxfOpaqueObject
          ? Array.from(item.Tags).filter(api.DxfObjectDatabase.IsReference).map(tag => this.StoredTableHandleTarget(tag.Value))
          : item.DatabaseReferences);
        if(Array.from(references).some(target=>removed.has(target)))return true;
        const seen=new Set();for(let owner=item.Owner;owner!==null&&!seen.has(owner);owner=owner.Owner){if(removed.has(owner))return true;seen.add(owner);}
      }
    }
    for(const item of this.AddedObjects.Values)if(!removed.has(item)&&(item instanceof api.StoredTable||item instanceof api.DxfTableStyle)&&Array.from(item.References).some(target=>removed.has(target)))return true;
    return false;
  }
};
export function InstallDocumentStoredTable(Type){for(const name of Object.keys(methods))Object.defineProperty(Type.prototype,name,Object.getOwnPropertyDescriptor(methods,name));}
