// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfLayerIndex } from '../Objects/DxfLayerIndex.js';
import { PrepareStoredEnvelopeClass } from './DxfWriter.LayerFilterPointer.js';
import { EncodeDxfDatabaseText } from '../../runtime/DxfStringEncoding.js';
export function PrepareLayerIndexClass(document,definitions){
  PrepareStoredEnvelopeClass(document,definitions,'LAYER_INDEX','AcDbLayerIndex','ObjectDBX Classes',0,Array.from(document.Objects.Items).some(item=>item instanceof DxfLayerIndex));
}
export function WriteLayerIndexPayload(chunk,version,item){
  if(!(item instanceof DxfLayerIndex))return false;chunk.Write(100,'AcDbIndex');chunk.Write(40,item.Timestamp);chunk.Write(100,'AcDbLayerIndex');
  for(const entry of item.Entries){chunk.Write(8,EncodeDxfDatabaseText(entry.LayerName,version));chunk.Write(360,entry.Buffer.Handle);chunk.Write(90,entry.Count);}return true;
}
