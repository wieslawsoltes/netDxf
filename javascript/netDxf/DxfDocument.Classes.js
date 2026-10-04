// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { DxfClassCollection } from './Collections/DxfClassCollection.js';
/** Initialize the original readonly per-document CLASS collection without allocating handles. */
export function InitializeDocumentClasses(document) {
  const classes = new DxfClassCollection();
  Object.defineProperty(document, 'Classes', { get: () => classes, enumerable: true });
}
