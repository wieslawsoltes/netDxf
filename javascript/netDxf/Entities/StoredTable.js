// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { Vector3 } from '../Vector3.js';
import { Matrix4 } from '../Matrix4.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { TableSnapshot, DecodeTableText } from '../../runtime/TablePayload.js';
import { DxfHandleKind } from '../IO/DxfGroupCode.js';
import { StoredTableGrid } from './StoredTableGrid.js';
import { SetStoredTableState, StoredTableState } from '../../runtime/StoredTableState.js';
import { ResolveTableNamedStyles, ValidateTableNamedStyles, ChangedTableTextStyle } from './StoredTable.NamedStyles.js';
import { ResolveTableBacking, TableBackingAgreement } from './StoredTable.Backing.js';
import { DxfStoredTableContent } from '../Objects/DxfStoredTableContent.js';
import { RegisterDatabaseModel } from '../../runtime/DatabaseModel.js';
import { InvalidDataException, InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
const semantic=tag=>![DxfHandleKind.None,DxfHandleKind.Arbitrary,DxfHandleKind.ObjectIdentity].includes(tag.HandleKind);
const canonical=value=>BigInt('0x'+value).toString(16).toUpperCase();
function vector(tags,code,fallback){
  const xs=tags.filter(t=>t.Code===code),ys=tags.filter(t=>t.Code===code+10),zs=tags.filter(t=>t.Code===code+20);
  if(!xs.length&&!ys.length&&!zs.length)return fallback;
  if(xs.length!==1||ys.length!==1||zs.length!==1)throw new InvalidDataException('Incomplete or repeated TABLE vector.');return new Vector3(xs[0].Value,ys[0].Value,zs[0].Value);
}
/** Source-bound retained entity. The constructor/Resolve adapt internal loading;
 * they do not invent a typed reader, editable grid or backing-regeneration engine. */
export class StoredTable extends EntityObject {
  constructor(source,input,decode=DecodeTableText){
    super(EntityType.StoredTable,'ACAD_TABLE');const tags=Array.from(input);
    let end=tags.length>0&&tags[0].Code===100&&tags[0].Value==='AcDbBlockReference'?tags.findIndex((t,i)=>i>0&&t.Code===100):0;if(end<0)end=tags.length;
    const block=tags.slice(0,end),normal=vector(block,210,Vector3.UnitZ);
    if(Vector3.IsZero(normal))throw new InvalidDataException('ACAD_TABLE has a zero normal.');super.Normal=normal;
    const names=block.filter(t=>t.Code===2);
    SetStoredTableState(this,{source,decode,version:source.DrawingVariables.AcadVer,payload:TableSnapshot(tags),normal,position:vector(block,10,Vector3.Zero),grid:StoredTableGrid.TryRead(tags,decode,source.DrawingVariables.AcadVer),
      handles:new Map(),references:[],namedStyles:new Map(),displayBlock:null,displayName:names.length===1?decode(names[0].Value):null,initialName:null,pending:true,removed:false,backing:null});
  }
  get SourceVersion(){return StoredTableState(this).version;}
  get Payload(){return StoredTableState(this).payload;}
  get Position(){return Copy(StoredTableState(this).position);}
  get Grid(){return StoredTableState(this).grid;}
  get Normal(){return Copy(StoredTableState(this).normal);}
  set Normal(value){const old=StoredTableState(this).normal;if(value.X!==old.X||value.Y!==old.Y||value.Z!==old.Z)throw new NotSupportedException('Stored TABLE normal changes require backing geometry regeneration.');}
  get References(){
    const s=StoredTableState(this),refs=s.references.slice();
    for(const tag of this.Payload){if(!semantic(tag))continue;const handle=canonical(tag.Value);if(s.handles.has(handle))continue;const target=s.source.StoredTableHandleTarget(handle);if(target!==null)refs.push(target);}
    return TableSnapshot(refs);
  }
  get BackingContent(){return StoredTableState(this).backing;}
  get StoredBackingContent(){const value=this.BackingContent;return value instanceof DxfStoredTableContent?value:null;}
  get BackingLiteralValuesAgree(){return TableBackingAgreement(this);}
  Resolve(){
    const s=StoredTableState(this);
    for(const tag of this.Payload){if(!semantic(tag))continue;const handle=canonical(tag.Value),target=s.source.StoredTableHandleTarget(handle);if(target===null)continue;s.handles.set(handle,target);s.references.push(target);}
    const output={};if(s.displayName!==null && s.source.Blocks.TryGetValue(s.displayName,output)){s.displayBlock=output.value;s.initialName=output.value.Name;s.references.push(output.value.Record);}
    ResolveTableNamedStyles(this);s.pending=false;ResolveTableBacking(this);
  }
  ValidateIncoming(document){const s=StoredTableState(this);if(s.removed)throw new InvalidOperationException('A removed stored TABLE cannot be reattached.');if(s.pending&&document===null)return;if(document!==s.source)throw new InvalidOperationException('A stored TABLE can only belong to its source document.');if(!s.pending)this.Validate(document);}
  Validate(document){
    const s=StoredTableState(this);if(s.pending||s.removed||document!==s.source)throw new InvalidOperationException('The stored TABLE is detached or has unresolved input state.');
    if(document.DrawingVariables.AcadVer!==this.SourceVersion)throw new NotSupportedException('Stored TABLE conversion between DXF versions requires schema regeneration.');
    ValidateTableNamedStyles(this);
    for(const [handle,target] of s.handles)if(document.StoredTableHandleTarget(handle)!==target)throw new InvalidOperationException('A stored TABLE dependency is no longer registered: '+handle);
    if(s.displayBlock!==null&&document.GetObjectByHandle(s.displayBlock.Record.Handle)!==s.displayBlock.Record)throw new InvalidOperationException('The stored TABLE display block is no longer registered.');
  }
  ChangedResourceName(tag){
    const style=ChangedTableTextStyle(this,tag);if(style!==null)return style;
    const s=StoredTableState(this);if(tag.Code!==2||s.displayBlock===null||s.displayBlock.Name===s.initialName)return null;
    const tags=Array.from(this.Payload),index=tags.indexOf(tag);let boundary=tags.findIndex((t,i)=>i>0&&t.Code===100);if(boundary<0)boundary=tags.length;
    return index<boundary?s.displayBlock.Name:null;
  }
  MarkRemoved(){StoredTableState(this).removed=true;}
  Clone(){throw new NotSupportedException('Stored TABLE cloning requires a complete application schema.');}
  TransformBy(matrix,translation){
    const size=matrix instanceof Matrix4&&translation===undefined?4:3;
    for(let r=0;r<size;r++)for(let c=0;c<size;c++)if(matrix.get_Item(r,c)!==(r===c?1:0))throw new NotSupportedException('Stored TABLE transforms require backing geometry regeneration.');
    if(size===3&&(translation.X!==0||translation.Y!==0||translation.Z!==0))throw new NotSupportedException('Stored TABLE transforms require backing geometry regeneration.');
  }
}
RegisterDatabaseModel('StoredTable',StoredTable);
