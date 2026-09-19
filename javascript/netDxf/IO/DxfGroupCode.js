// Copyright (c) Daniel Carvajal. MIT License; see ../LICENSE and package LICENSE.

import { ArgumentOutOfRangeException } from '../../runtime/Errors.js';
export const DxfTagValueType = Object.freeze({ String: 0, Double: 1, Int16: 2, Int32: 3,
  Int64: 4, Boolean: 5, BinaryData: 6, Handle: 7 });
export const DxfHandleKind = Object.freeze({ None: 0, ObjectIdentity: 1, Arbitrary: 2,
  SoftPointer: 3, HardPointer: 4, SoftOwner: 5, HardOwner: 6, XData: 7 });
const types = new Int8Array(1072).fill(-1);
const kinds = new Int8Array(1072);
function range(first, last, type) { types.fill(type, first, last + 1); }
for (const [a,b] of [[0,9],[100,102],[300,309],[410,419],[430,439],[470,479],[999,1003],[1006,1009]]) range(a,b,0);
for (const [a,b] of [[10,59],[110,149],[210,239],[460,469],[1010,1059]]) range(a,b,1);
for (const [a,b] of [[60,79],[170,179],[270,289],[370,389],[400,409],[1060,1070]]) range(a,b,2);
for (const [a,b] of [[90,99],[420,429],[440,459],[1071,1071]]) range(a,b,3);
range(160,169,4); range(290,299,5); range(310,319,6); range(1004,1004,6);
for (const [a,b,kind] of [[5,5,1],[105,105,1],[320,329,2],[330,339,3],[340,349,4],
  [350,359,5],[360,369,6],[390,399,4],[480,481,4],[1005,1005,7]]) {
  range(a,b,7); kinds.fill(kind,a,b+1);
}
export class DxfGroupCode {
  static GetValueType(code) {
    if (Number.isInteger(code) && code >= 0 && code < types.length && types[code] >= 0) return types[code];
    throw new ArgumentOutOfRangeException('code', code, 'No supported DXF value encoding exists for this group code.');
  }
  /** C# out parameter: pass a mutable { value: ... } object as the second argument. */
  static TryGetValueType(code, result) {
    const known = Number.isInteger(code) && code >= 0 && code < types.length && types[code] >= 0;
    if (result != null) result.value = known ? types[code] : DxfTagValueType.String;
    return known;
  }
  static GetHandleKind(code) {
    return Number.isInteger(code) && code >= 0 && code < kinds.length ? kinds[code] : DxfHandleKind.None;
  }
}
