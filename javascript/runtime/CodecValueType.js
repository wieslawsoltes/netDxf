// CLR primitive cast diagnostics for the typed code/value-reader adapters.
// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfTagValueType as T } from '../netDxf/IO/DxfGroupCode.js';
import { InvalidCastException } from './Errors.js';
const names=new Map([[T.String,'System.String'],[T.Handle,'System.String'],[T.Double,'System.Double'],[T.Int16,'System.Int16'],[T.Int32,'System.Int32'],[T.Int64,'System.Int64'],[T.Boolean,'System.Boolean'],[T.BinaryData,'System.Byte[]'],[-2,'System.Byte']]);
export function CodecCastException(actual,expected){
  return new InvalidCastException("Unable to cast object of type '"+names.get(actual)+"' to type '"+names.get(expected)+"'.");
}
