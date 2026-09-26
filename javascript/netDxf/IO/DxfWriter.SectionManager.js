// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfStoredSectionManager } from '../Objects/DxfStoredSectionManager.js';
import { DxfClass } from '../DxfClass.js';
import { InvalidDataException } from '../../runtime/Errors.js';
export function WriteSectionManagerPayload(chunk,version,item) {
  if(!(item instanceof DxfStoredSectionManager))return false;
  for(const tag of item.Tags)chunk.Write(tag.Code,tag.Value);return true;
}
export function PrepareSectionManagerClasses(document,definitions) {
  for(const name of ['SECTION_MANAGER','SECTIONMANAGER']) {
    if(!Array.from(document.Objects.Items).some(item=>item instanceof DxfStoredSectionManager && item.CodeName===name))continue;
    const count=Array.from(document.Objects.Items).filter(item=>item.CodeName===name).length;
    if(definitions.Contains(name)) {
      const definition=definitions.get_Item(name);
      if(definition.CppClassName!=='AcDbSectionManager' || definition.IsEntity)throw new InvalidDataException('CLASS conflicts with a stored section manager.');
      definition.InstanceCount=count;
    }else{
      const definition=new DxfClass(name,'AcDbSectionManager','ObjectDBX Classes');definition.ProxyFlags=1024;definition.IsEntity=false;definition.InstanceCount=count;definitions.Add(definition);
    }
  }
}
