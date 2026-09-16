// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { DxfAtomicFile } from './DxfAtomicFile.js';
/** Partial-class implementation, invoked by DxfRawDocument.SaveAtomic. */
export function SaveAtomic(document, file, binary = document.IsBinary, cancellationToken = null) {
  if (typeof binary !== 'boolean') { cancellationToken = binary; binary = document.IsBinary; }
  DxfAtomicFile.Write(file, stream => document.Save(stream, binary, cancellationToken), cancellationToken);
}
