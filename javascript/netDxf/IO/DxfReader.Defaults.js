// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { ApplicationRegistries } from '../Collections/ApplicationRegistries.js';
import { BlockRecords } from '../Collections/BlockRecords.js';
import { DimensionStyles } from '../Collections/DimensionStyles.js';
import { Layers } from '../Collections/Layers.js';
import { Linetypes } from '../Collections/Linetypes.js';
import { TextStyles } from '../Collections/TextStyles.js';
import { ShapeStyles } from '../Collections/ShapeStyles.js';
import { UCSs } from '../Collections/UCSs.js';
import { Views } from '../Collections/Views.js';
import { VPorts } from '../Collections/VPorts.js';
import { DictionaryObject } from '../Objects/DictionaryObject.js';
import { RasterVariables } from '../Objects/RasterVariables.js';
/** Restore only absent collections; never replace a populated table or its identity. */
export function EnsureTableCollections(document) {
  if (document.ApplicationRegistries === null) document.ApplicationRegistries = new ApplicationRegistries(document);
  if (document.Blocks === null) document.Blocks = new BlockRecords(document);
  if (document.DimensionStyles === null) document.DimensionStyles = new DimensionStyles(document);
  if (document.Layers === null) document.Layers = new Layers(document);
  if (document.Linetypes === null) document.Linetypes = new Linetypes(document);
  if (document.TextStyles === null) document.TextStyles = new TextStyles(document);
  if (document.ShapeStyles === null) document.ShapeStyles = new ShapeStyles(document);
  if (document.UCSs === null) document.UCSs = new UCSs(document);
  if (document.Views === null) document.Views = new Views(document);
  if (document.VPorts === null) document.VPorts = new VPorts(document);
}
export function EnsureObjectCollections(reader) {
  if (reader.doc.Layouts === null) reader.CreateObjectCollection(new DictionaryObject(null));
  if (reader.doc.RasterVariables === null) reader.doc.RasterVariables = new RasterVariables(reader.doc);
}
