import { DxfHandleKind } from '../netDxf/IO/DxfGroupCode.js';
import { XDataCode } from '../netDxf/XDataCode.js';
import { ArgumentNullException, ArgumentOutOfRangeException, FormatException, OverflowException, InvalidOperationException } from './Errors.js';

export const IsStoredReference = tag => [DxfHandleKind.SoftPointer, DxfHandleKind.HardPointer, DxfHandleKind.SoftOwner, DxfHandleKind.HardOwner].includes(tag.HandleKind);
export function HasNonzeroXDataReference(item) {
  for (const data of item.XData.Values) for (const tag of data.XDataRecord) {
    if (tag.Code !== XDataCode.DatabaseHandle) continue;
    const text = tag.Value;
    if (text == null) throw new ArgumentNullException('s');
    if (!/^[0-9a-fA-F]+$/.test(text)) throw new FormatException();
    const value = BigInt('0x' + text);
    if (value > 0xffffffffffffffffn) throw new OverflowException();
    if (value !== 0n) return true;
  }
  return false;
}
export function SameSequence(left, right) {
  if(left==null)throw new ArgumentNullException('first');
  if(right==null)throw new ArgumentNullException('second');
  const a = Array.from(left), b = Array.from(right);
  return a.length === b.length && a.every((v, i) => v === b[i]);
}
/** Array.AsReadOnly semantics: live array elements, no public mutation methods. */
export function ReadOnlyArrayView(array) {
  return Object.freeze({
    get Count() { return array.length; }, get length() { return array.length; },
    get_Item(index) {
      if (!Number.isInteger(index) || index < 0 || index >= array.length) throw new ArgumentOutOfRangeException('index', index);
      return array[index];
    },
    GetEnumerator() {
      let index = -1;
      return { get Current() { if(index<0||index>=array.length)throw new InvalidOperationException('Enumeration is not positioned on an element.');return array[index]; },
        MoveNext() { if(index<array.length)index++;return index<array.length; }, Reset() { index=-1; }, Dispose() {},
        next() { return this.MoveNext()?{done:false,value:this.Current}:{done:true}; }, [Symbol.iterator]() { return this; } };
    },
    [Symbol.iterator]() { return this.GetEnumerator(); }
  });
}
