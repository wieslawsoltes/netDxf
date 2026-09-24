// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
// Selected class-preparation methods; not complete MultiLeader/Section/StoredTable IO mirrors.
import * as api from '../index.js';
import { InvalidDataException } from './Errors.js';
const allEntities=doc=>Array.from(doc.Blocks).flatMap(block=>Array.from(block.Entities));
function prepare(definitions,name,cpp,app,isEntity,count,typed,flags){
  if(definitions.Contains(name)){
    const item=definitions.get_Item(name);
    if(item.CppClassName!==cpp||item.IsEntity!==isEntity){if(typed)throw new InvalidDataException('CLASS conflicts with '+name);return;}
    item.InstanceCount=count;
  }else if(typed){const item=new api.DxfClass(name,cpp,app);item.ProxyFlags=flags;item.IsEntity=isEntity;item.InstanceCount=count;definitions.Add(item);}
}
export function PrepareMultiLeaderClasses(doc,definitions){
  const count=allEntities(doc).filter(e=>e instanceof api.MultiLeader).length;
  const objects=Array.from(doc.Objects.Items),styles=objects.filter(o=>o.CodeName==='MLEADERSTYLE').length;
  prepare(definitions,'MULTILEADER','AcDbMLeader','ACDB_MLEADER_CLASS',true,count,count>0,1025);
  prepare(definitions,'MLEADERSTYLE','AcDbMLeaderStyle','ACDB_MLEADERSTYLE_CLASS',false,styles,objects.some(o=>o instanceof api.DxfMLeaderStyle),4095);
}
export function PrepareStoredTableClasses(doc,definitions){
  if(!definitions.Contains('ACAD_TABLE'))return;
  const item=definitions.get_Item('ACAD_TABLE');if(item.CppClassName==='AcDbTable'&&item.IsEntity)item.InstanceCount=allEntities(doc).filter(e=>e instanceof api.StoredTable).length;
}
export function PrepareSectionClasses(doc,definitions){
  for(const name of ['SECTION','SECTIONOBJECT']){
    const count=allEntities(doc).filter(e=>e instanceof api.Section&&e.CodeName===name).length;
    prepare(definitions,name,'AcDbSection','ObjectDBX Classes',true,count,count!==0,1025);
  }
  for(const name of ['SECTIONSETTINGS','SECTION_SETTINGS']){
    const objects=Array.from(doc.Objects.Items),count=objects.filter(o=>o.CodeName===name).length;
    prepare(definitions,name,'AcDbSectionSettings','ObjectDBX Classes',false,count,objects.some(o=>o.CodeName===name&&o instanceof api.DxfSectionSettings),1024);
  }
}
