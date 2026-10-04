// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { GetOwnership } from './DxfDeclaredOwnership.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { ArgumentException, InvalidOperationException } from '../../runtime/Errors.js';
const codes=Object.freeze([102,360,70,90,10,20,30,90,90,361,102,90,91,102,360]);
export function InstallTableComposite(Type) {
  Object.defineProperty(Type.prototype,'IsCompositeTableRoundtripRecord',{get() {
    return this.Data.Count===codes.length && this.IsTableRoundtripRecord && codes.every((code,i)=>this.Data.get_Item(i).Code===code)
      && this.Data.get_Item(10).Value==='ACAD_ROUNDTRIP_PRE2007_TABLE' && this.Data.get_Item(13).Value==='ACAD_ROUNDTRIP_PRE2007_TABLECELL';
  }});
  Type.prototype.BindCompositeTableRoundtripChildren=function(content,geometry,cellData) {
    if(this.IsErased)throw new InvalidOperationException('An erased record cannot bind owned objects.');
    if(this.IsSchemaManaged)throw new InvalidOperationException('The ownership schema is already bound.');
    if(!this.IsCompositeTableRoundtripRecord)throw new ArgumentException('The composite TABLE ownership envelope is not recognized.');
    if(content==null||content.CodeName!=='TABLECONTENT'||geometry==null||geometry.CodeName!=='TABLEGEOMETRY'||cellData==null)throw new ArgumentException('The composite TABLE slots require TABLECONTENT, TABLEGEOMETRY and DATATABLE targets.');
    this.CheckOwnedCandidate(content,this.Data.get_Item(1));this.CheckOwnedCandidate(geometry,this.Data.get_Item(9));this.CheckOwnedCandidate(cellData,this.Data.get_Item(14));
    if(this.Database!==null)for(const item of this.Database.Items)if(item.Owner===this&&item!==content&&item!==geometry&&item!==cellData&&item!==this.ExtensionDictionary)throw new ArgumentException('The composite TABLE envelope has an undeclared owned child.');
    Object.assign(GetOwnership(this),{content,geometry,cellData,contentSlot:1,geometrySlot:9});content.Owner=this;geometry.Owner=this;cellData.Owner=this;
  };
  Type.prototype.ValidateCompositeTableOwnership=function(errors) {
    const s=GetOwnership(this);if(s.cellData===null)return;
    if(!this.IsCompositeTableRoundtripRecord){errors.Add('Invalid composite TABLE ownership envelope: '+(this.Handle??''));return;}
    if(this.Database!==null&&!OrdinalIgnoreCaseEquals(this.Data.get_Item(14).Value,s.cellData.Handle))errors.Add('Composite TABLE DATATABLE handle mismatch: '+(this.Handle??''));
  };
}
