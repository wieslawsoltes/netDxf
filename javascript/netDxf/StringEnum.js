// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import * as enums from '../Enums.generated.js';
import { ReferenceList } from '../runtime/ReferenceList.js';
import { GenericDictionary } from '../runtime/GenericDictionary.js';
import { Culture } from '../runtime/DisplayFormatting.js';
import { OrdinalIgnoreCaseEquals } from '../runtime/Collections.js';
import { BoxedString } from '../runtime/BoxedString.js';
import { ArgumentException, NullReferenceException } from '../runtime/Errors.js';

/** CLR StringComparison values, exposed as a JavaScript language adapter. */
export const StringComparison = Object.freeze({CurrentCulture:0, CurrentCultureIgnoreCase:1,
  InvariantCulture:2, InvariantCultureIgnoreCase:3, Ordinal:4, OrdinalIgnoreCase:5});
const types = new Map(), bound = new Map(), collators = new Map();
for (const [name, type] of Object.entries(enums)) {
  if (name.endsWith('StringValues') || typeof type !== 'object' || type === null) continue;
  const strings = enums[name + 'StringValues'] ?? {};
  // Generated enum objects retain declaration order. The reflection backing field
  // precedes the public constants, but has no StringValue attribute.
  const fields = [{name:'value__', value:0, attributed:false}];
  for (const [field, value] of Object.entries(type)) fields.push({name:field, value,
    attributed:Object.hasOwn(strings, value), text:strings[value]});
  types.set(type, fields);
}
function fieldsFor(type) {
  const fields = types.get(type);
  if (!fields) throw new ArgumentException('Use a generated enum object as the generic type argument.', 'enumType');
  return fields;
}
function text(value) { return value instanceof BoxedString ? value.Value : value; }
function checkComparison(comparison) {
  if (!Number.isInteger(comparison) || comparison < 0 || comparison > 5)
    throw new ArgumentException('The string comparison type passed in is currently not supported.', 'comparisonType');
}
function equal(a, b, comparison) {
  a = text(a); b = text(b);
  checkComparison(comparison);
  if (a === b) return true;
  if (a == null || b == null) return false;
  if (comparison === 4) return a === b;
  if (comparison === 5) return OrdinalIgnoreCaseEquals(a, b);
  // All attributed labels in the pinned enum registry are ASCII. .NET's ICU
  // ignore-case tailoring retains width differences when IgnoreWidth is absent;
  // Intl's secondary-strength (ignore-case) comparator also ignores width.
  // Thus a fullwidth ASCII/currency character cannot match these ASCII labels.
  // This is deliberately local to this registry, not a general collation port.
  // Source: https://github.com/dotnet/runtime/blob/v8.0.0/src/native/libs/System.Globalization.Native/pal_collation.c
  // CloneCollatorWithOptions / FillIgnoreWidthRules (primary width distinction).
  if ((comparison & 1) && /^[\x00-\x7f]*$/.test(a) && /[\uff01-\uff5e\uffe0-\uffe6]/u.test(b)) return false;
  const culture = comparison < 2 ? Culture.Current.Name : '';
  const key = culture + '/' + (comparison & 1);
  let collator = collators.get(key);
  if (!collator) {
    // Linguistic comparison follows the selected host ICU. Its exact qualification
    // is separate from ordinal equality; no ASCII-only fallback changes semantics.
    collator = new Intl.Collator(culture || 'en-US', {usage:'sort', sensitivity:comparison & 1 ? 'accent' : 'variant'});
    collators.set(key, collator);
  }
  return collator.compare(a, b) === 0;
}
/** StringEnum<T> adapter. new StringEnum(EnumType) supplies erased T; For(EnumType)
 * returns a cached bound class with the source's parameterless constructor and
 * static method signatures. EnumType is the generated enum object, not CLR Type.
 */
export class StringEnum {
  #type;
  constructor(enumType) { fieldsFor(enumType); this.#type = enumType; }
  get EnumType() { return this.#type; }
  GetStringValues() { return new ReferenceList(fieldsFor(this.#type).filter(f=>f.attributed).map(f=>f.text)); }
  GetValues() {
    const values = new GenericDictionary(0, null, 'enum', 'string');
    for (const field of fieldsFor(this.#type)) if (field.attributed) values.Add(field.value, field.text);
    return values;
  }
  static For(enumType) {
    fieldsFor(enumType);
    if (!bound.has(enumType)) bound.set(enumType, class extends StringEnum {
      constructor() { super(enumType); }
      static GetStringValue(value) { return StringEnum.GetStringValue(enumType, value); }
      static Parse(value, comparison = 0) { return StringEnum.Parse(enumType, value, comparison); }
      static IsStringDefined(value, comparison = 0) { return StringEnum.IsStringDefined(enumType, value, comparison); }
    });
    return bound.get(enumType);
  }
  static GetStringValue(enumType, value) {
    // All pinned enum types have unique underlying values. Undefined values and
    // unnamed flag combinations have no reflected StringValue field.
    return fieldsFor(enumType).find(f=>f.name !== 'value__' && f.value === value && f.attributed)?.text ?? null;
  }
  static IsStringDefined(enumType, value, comparison = 0) {
    for (const s of new StringEnum(enumType).GetStringValues()) {
      if (s == null) throw new NullReferenceException();
      if (equal(s, value, comparison)) return true;
    }
    return false;
  }
  static Parse(enumType, value, comparison = 0) {
    let enumStringValue = null;
    for (const field of fieldsFor(enumType)) {
      if (field.attributed) enumStringValue = field.text;
      if (equal(enumStringValue, value, comparison)) return field.name === 'value__' ? 0 : field.value;
    }
    return 0;
  }
}
export class StringValueAttribute {
  #value;
  constructor(value) { this.#value = text(value); }
  get Value() { return this.#value; }
}
