// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfClass } from '../DxfClass.js';
import { DxfLayerFilter } from '../Objects/DxfLayerFilter.js';
import { DxfObjectPointer } from '../Objects/DxfObjectPointer.js';
import { EncodeDxfDatabaseText } from '../../runtime/DxfStringEncoding.js';
import { InvalidDataException } from '../../runtime/Errors.js';
export function PrepareStoredEnvelopeClass(document,definitions,name,cppName,applicationName,flags,typedPresent){
  const count=Array.from(document.Objects.Items).filter(item=>item.CodeName===name).length;
  if(!typedPresent){
    if(definitions.Contains(name)&&definitions.get_Item(name).CppClassName===cppName&&!definitions.get_Item(name).IsEntity)definitions.get_Item(name).InstanceCount=count;return;
  }
  if(definitions.Contains(name)){
    const definition=definitions.get_Item(name);if(definition.CppClassName!==cppName||definition.IsEntity)throw new InvalidDataException('CLASS conflicts with a typed database object: '+name);definition.InstanceCount=count;
  }else{const definition=new DxfClass(name,cppName,applicationName);definition.ProxyFlags=flags;definition.IsEntity=false;definition.WasProxy=false;definition.InstanceCount=count;definitions.Add(definition);}
}
export function PrepareLayerFilterPointerClasses(document,definitions){
  PrepareStoredEnvelopeClass(document,definitions,'LAYER_FILTER','AcDbLayerFilter','ObjectDBX Classes',0,Array.from(document.Objects.Items).some(item=>item instanceof DxfLayerFilter));
  PrepareStoredEnvelopeClass(document,definitions,'OBJECT_PTR','CAseDLPNTableRecord','',1,Array.from(document.Objects.Items).some(item=>item instanceof DxfObjectPointer));
}
export function WriteLayerFilterPointerPayload(chunk,version,item){
  if(item instanceof DxfLayerFilter){chunk.Write(100,'AcDbFilter');chunk.Write(100,'AcDbLayerFilter');for(const name of item.LayerNames)chunk.Write(8,EncodeDxfDatabaseText(name,version));return true;}
  return item instanceof DxfObjectPointer;
}
