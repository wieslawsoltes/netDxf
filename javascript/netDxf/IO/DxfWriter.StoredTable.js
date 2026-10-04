// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { StoredTable } from '../Entities/StoredTable.js';
import { EncodeDxfDatabaseText } from '../../runtime/DxfStringEncoding.js';
import { WriteXData } from '../../runtime/DxfXDataIO.js';
import { CheckStyleUnicode } from './DxfWriter.TextStyle.js';
import { InvalidOperationException } from '../../runtime/Errors.js';
export function PrepareStoredTableClasses(document, definitions) {
  if (!definitions.Contains('ACAD_TABLE')) return;
  const definition = definitions.get_Item('ACAD_TABLE');
  if (definition.CppClassName === 'AcDbTable' && definition.IsEntity)
    definition.InstanceCount = Array.from(document.Blocks).reduce((count, block) =>
      count + Array.from(block.Entities).filter(entity => entity instanceof StoredTable).length, 0);
}
export function ValidateStoredTables(document, isBinary) {
  for (const block of document.Blocks) for (const table of block.Entities) if (table instanceof StoredTable) {
    table.Validate(document);
    for (const tag of table.Payload) {
      if (typeof tag.Value !== 'string') continue;
      CheckStyleUnicode(tag.Value);
      if (!isBinary && /[\0\r\n]/.test(tag.Value))
        throw new InvalidOperationException('Stored TABLE contains an unsupported multiline text transport string.');
    }
  }
}
export function WriteStoredTable(chunk, document, table) {
  for (const tag of table.Payload) {
    const name = table.ChangedResourceName(tag);
    chunk.Write(tag.Code, name === null ? tag.Value : EncodeDxfDatabaseText(name, document.DrawingVariables.AcadVer));
  }
  WriteXData(chunk, () => document.DrawingVariables.AcadVer, table.XData);
}
