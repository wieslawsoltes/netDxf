// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { StoredTable } from '../Entities/StoredTable.js';
import { DxfTag } from './DxfTag.js';
import { DecodeDxfText } from '../../runtime/DxfStringEncoding.js';
import { ReadXDataRecord } from '../../runtime/DxfXDataIO.js';
import { InvalidDataException } from '../../runtime/Errors.js';
/** Enter at the first TABLE subclass tag. The terminating group zero remains unread. */
export function ReadStoredTable(chunk, document, storedTables) {
  const tags = [], data = []; let extended = false;
  while (chunk.Code !== 0) {
    if (chunk.Code === 1001) { extended = true; data.push(ReadXDataRecord(chunk, document)); continue; }
    if (extended) throw new InvalidDataException('TABLE XData must follow its complete subclass payload.');
    if (chunk.Code >= 1000 && chunk.Code <= 1071)
      throw new InvalidDataException('TABLE extended data must begin with an application registry.');
    tags.push(new DxfTag(chunk.Code, chunk.Value)); chunk.Next();
  }
  const table = new StoredTable(document, tags, DecodeDxfText);
  table.XData.AddRange(data); storedTables.Add(table); return table;
}
export function ResolveStoredTables(storedTables) { for (const table of storedTables) table.Resolve(); }
