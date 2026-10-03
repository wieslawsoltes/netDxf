// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
/** Structural projection of the original internal C# interface. No stub codec is created.
 * Code: Int16; Value: boxed primitive; CurrentPosition: the concrete codec's position representation.
 * Reader Code5IsString is read/write; the other properties are read-only.
 * All members retain their original spelling and synchronous calling convention.
 */
const properties = Object.freeze(["Code", "Value", "CurrentPosition", "Code5IsString"]);
const methods = Object.freeze(["Next", "ReadByte", "ReadBytes", "ReadShort", "ReadInt", "ReadLong", "ReadBool", "ReadDouble", "ReadString", "ReadHex"]);
function descriptor(value, name) {
  for (let object = value; object !== null; object = Object.getPrototypeOf(object)) {
    const found = Object.getOwnPropertyDescriptor(object, name);
    if (found) return found;
  }
  return null;
}
export const ICodeValueReader = Object.freeze({
  Name: 'ICodeValueReader', Properties: properties, Methods: methods,
  IsImplementedBy(value) {
    if (value === null || (typeof value !== 'object' && typeof value !== 'function')) return false;
    return properties.every(name => descriptor(value, name) !== null)
      && methods.every(name => typeof descriptor(value, name)?.value === 'function');
  },
  [Symbol.hasInstance](value) { return this.IsImplementedBy(value); }
});
