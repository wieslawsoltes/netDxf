// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfObject } from './DxfObject.js';
import { TableSnapshot } from '../runtime/TablePayload.js';
import { ArgumentNullException } from '../runtime/Errors.js';
export const DxfVersionCompatibilityKind = Object.freeze({WriterRejection:0, DataOmission:1});
/** The source reference remains live; every other property is captured. */
export class DxfVersionCompatibilityDiagnostic {
  constructor(code, kind, source, sourceCodeName, propertyPath, message) {
    Object.assign(this,{Code:code,Kind:kind,SourceObject:source,
      SourceHandle:source instanceof DxfObject ? source.Handle : null,
      SourceCodeName:sourceCodeName,PropertyPath:propertyPath,Message:message});
    Object.freeze(this);
  }
}
const ordinal = (a,b) => a === b ? 0 : a == null ? -1 : b == null ? 1 : a < b ? -1 : 1;
/** Immutable, stably ordered diagnostics. An empty report is not a Save guarantee. */
export class DxfVersionCompatibilityReport {
  constructor(source, target, diagnostics) {
    if(diagnostics==null)throw new ArgumentNullException('source');
    const ordered=Array.from(diagnostics).sort((a,b)=>ordinal(a.SourceHandle??'',b.SourceHandle??'')||
      ordinal(a.SourceCodeName,b.SourceCodeName)||ordinal(a.PropertyPath,b.PropertyPath)||ordinal(a.Code,b.Code));
    Object.assign(this,{SourceVersion:source,TargetVersion:target,Diagnostics:TableSnapshot(ordered),
      HasKnownRejections:ordered.some(item=>item.Kind===DxfVersionCompatibilityKind.WriterRejection),
      HasKnownLosses:ordered.some(item=>item.Kind===DxfVersionCompatibilityKind.DataOmission)});
    Object.freeze(this);
  }
}
