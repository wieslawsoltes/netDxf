// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { DxfOpaqueEntity } from '../Entities/DxfOpaqueEntity.js';
import { PrepareClassDefinitions } from './DxfClasses.js';
import { CheckStyleUnicode } from './DxfWriter.TextStyle.js';
import { EncodeDxfDatabaseText } from '../../runtime/DxfStringEncoding.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
function* opaque(document){for(const block of document.Blocks)for(const entity of block.Entities)if(entity instanceof DxfOpaqueEntity)yield entity;}
export function ValidateOpaqueText(text,binary) {
  CheckStyleUnicode(text);
  if(text.includes('\0')||!binary&&/[\r\n]/.test(text))throw new InvalidOperationException('Unknown entity contains a string unsupported by the selected transport.');
}
export function ValidateOpaqueEntityClasses(document,binary,definitions) {
  for(const entity of opaque(document)) {
    entity.ValidatePreparedClass(definitions);if(!definitions.Contains(entity.CodeName))continue;
    const definition=definitions.get_Item(entity.CodeName);
    ValidateOpaqueText(definition.Name,binary);ValidateOpaqueText(definition.CppClassName,binary);ValidateOpaqueText(definition.ApplicationName,binary);
  }
}
export function ValidateOpaqueEntities(document,binary=false) {
  let total=0;const entities=Array.from(opaque(document));
  for(const entity of entities) {
    entity.Validate(document);const tags=entity.OutputTags(text=>text);
    // Native short-circuiting does not update the total when the record limit fails.
    if(tags.Count>65536||(total+=tags.Count-1)>1048576)throw new InvalidOperationException("Unknown entity output exceeds the reader's tag admission budget.");
    for(const tag of tags) {
      if(binary&&tag.Code===999)throw new NotSupportedException('Retained unknown entity comments require ASCII output.');
      if(binary&&tag.Value instanceof Uint8Array&&tag.Value.length>255)throw new NotSupportedException('Retained unknown entity binary chunks exceed binary transport framing.');
      if(typeof tag.Value==='string')ValidateOpaqueText(tag.Value,binary);
    }
  }
  if(entities.length!==0)ValidateOpaqueEntityClasses(document,binary,PrepareClassDefinitions(document));
}
export function PreflightOpaqueEntities(document,binary=false){ValidateOpaqueEntities(document,binary);}
export function IsOpaqueEntityClass(document,name){for(const entity of opaque(document))if(OrdinalIgnoreCaseEquals(entity.CodeName,name))return true;return false;}
export function WriteOpaqueEntity(chunk,document,entity){for(const tag of entity.OutputTags(text=>EncodeDxfDatabaseText(text,document.DrawingVariables.AcadVer)))chunk.Write(tag.Code,tag.Value);}
