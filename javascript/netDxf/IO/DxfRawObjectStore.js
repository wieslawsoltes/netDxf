// netDxf library. Copyright (c) Daniel Carvajal. Licensed under the MIT License.
import { DxfRawDocument } from './DxfRawDocument.js';
import { DxfRawHandleIndex } from './DxfRawHandleIndex.js';
import { DxfRawObjectTransaction } from './DxfRawObjectTransaction.js';
import { DxfTag } from './DxfTag.js';
import * as M from './DxfRawObjectModel.js';
import { ReadOnlyList, OrdinalIgnoreCaseEquals as Eq, OrdinalIgnoreCaseKey } from '../../runtime/Collections.js';
import * as E from '../../runtime/Errors.js';

const construct = Symbol('DxfRawObjectStore.Open');
const states = new WeakMap();
/** Internal access shared by the mirrored partial-class modules. */
export const ObjectStoreState = store => states.get(store);

export class DxfRawObjectStore {
  #byHandle = new Map();
  constructor(document, options, cancellationToken, key) {
    if (key !== construct) throw new E.NotSupportedException('Use DxfRawObjectStore.Open.');
    if (document == null) throw new E.ArgumentNullException('document');
    if (!(document instanceof DxfRawDocument)) throw new E.ArgumentException('Expected DxfRawDocument.', 'document');
    options ??= new M.DxfRawObjectStoreOptions();
    const index = DxfRawHandleIndex.Create(document, null, cancellationToken);
    states.set(this, { options, index }); this.Document = document;
    const objects = []; let sections = 0;
    for (const section of document.Sections) {
      if (!Eq(section.Name, 'OBJECTS')) continue;
      if (++sections !== 1) throw new E.InvalidDataException('Multiple OBJECTS sections are ambiguous for object editing.');
      for (const record of section.Records) {
        E.ThrowIfCancellationRequested(cancellationToken);
        if (objects.length === options.MaximumObjects) throw new E.InvalidDataException('OBJECTS record budget exceeded.');
        const value = DxfObjectSchema.Read(record.Tags, record, options.MaximumPayloadTags);
        objects.push(value);
        if (value.Handle === null) continue;
        const handle = DxfObjectText.Number(value.Handle);
        let matches = this.#byHandle.get(handle);
        if (!matches) this.#byHandle.set(handle, matches = []);
        matches.push(value);
      }
    }
    this.Objects = ReadOnlyList(objects); Object.freeze(this);
  }
  static Open(document, options = null, cancellationToken = null) {
    return new DxfRawObjectStore(document, options, cancellationToken, construct);
  }
  Get(handle) {
    const key = DxfObjectText.Number(DxfObjectText.Handle(handle, false)), matches = this.#byHandle.get(key);
    if (!matches) return null;
    if (matches.length !== 1 || states.get(this).index.FindDefinitions(handle).Count !== 1)
      throw new E.InvalidDataException('Ambiguous object identity: ' + handle);
    return matches[0];
  }
  get RootDictionary() {
    if (this.Objects.Count === 0) return null;
    const first = this.Objects[0];
    if (!(first instanceof M.DxfRawDictionary) || (first.OwnerHandle !== null && first.OwnerHandle !== '0'))
      throw new E.InvalidDataException('The first OBJECTS record is not an unowned root dictionary.');
    return first;
  }
  BeginEdit(cancellationToken = null) { return new DxfRawObjectTransaction(this, cancellationToken); }
  static EncodeText(text) { return DxfObjectText.Encode(text); }
  static DecodeText(text) { return DxfObjectText.Decode(text); }
}

/** Internal text/handle helpers mirrored from DxfRawObjectStore.cs. */
export class DxfObjectText {
  static Number(handle) { return BigInt('0x' + handle); }
  static Handle(handle, allowNull) {
    if (handle == null) throw new E.ArgumentNullException('handle');
    if (typeof handle !== 'string' || !/^[0-9a-fA-F]{1,16}$/.test(handle) || (!allowNull && BigInt('0x' + handle) === 0n))
      throw new E.ArgumentException('Expected a ' + (allowNull ? '' : 'nonzero ') + 'one-to-sixteen digit hexadecimal handle.', 'handle');
    return BigInt('0x' + handle).toString(16).toUpperCase();
  }
  static ValidateName(name) {
    if (name == null) throw new E.ArgumentNullException('name');
    if (name.length === 0) throw new E.ArgumentException('A dictionary name cannot be empty.', 'name');
    this.ValidateText(name);
  }
  static ValidateText(value) {
    if (value == null) throw new E.ArgumentNullException('value');
    if (typeof value !== 'string') throw new E.ArgumentException('Expected string.', 'value');
    if (/[\0\r\n]/.test(value)) throw new E.ArgumentException('Object text cannot contain transport delimiters.', 'value');
    for (let i = 0; i < value.length; i++) {
      const c = value.charCodeAt(i);
      if (c >= 0xd800 && c <= 0xdfff) {
        const next = value.charCodeAt(i + 1);
        if (c > 0xdbff || !(next >= 0xdc00 && next <= 0xdfff))
          throw new E.ArgumentException('Object text must contain paired UTF-16 surrogates.', 'value');
        i++;
      }
    }
  }
  static Encode(value) {
    this.ValidateText(value); let result = '';
    for (let i = 0; i < value.length; i++) {
      const c = value.charCodeAt(i);
      result += c === 92 || c > 127 ? '\\U+' + c.toString(16).toUpperCase().padStart(4, '0') : value[i];
    }
    return result;
  }
  static Decode(value) {
    if (value == null) throw new E.ArgumentNullException('value');
    // Replace exactly one escape layer; escaped backslashes cannot start a second pass.
    return value.replace(/\\U\+([0-9A-Fa-f]{4})/g, (_, code) => String.fromCharCode(parseInt(code, 16)));
  }
  static Cloning(value) { return E.RequireInteger(value, 0, 5, 'value'); }
}

export class DxfObjectSchema {
  static RegisteredClasses = Object.freeze({ ACDBDICTIONARYWDFLT: 'AcDbDictionaryWithDefault',
    ACDBPLACEHOLDER: 'AcDbPlaceHolder', DICTIONARYVAR: 'AcDbDictionaryVar', IDBUFFER: 'AcDbIdBuffer', SORTENTSTABLE: 'AcDbSortentsTable' });
  static Supported = Object.freeze(['DICTIONARY', 'ACDBDICTIONARYWDFLT', 'XRECORD', 'ACDBPLACEHOLDER', 'DICTIONARYVAR', 'IDBUFFER', 'SORTENTSTABLE']);
  static Read(input, source, budget) {
    const tags = Array.isArray(input) ? input : Array.from(input);
    const type = tags.length > 0 && tags[0].Code === 0 ? tags[0].Value : '';
    let handle = null, owner = null;
    const view = Object.isFrozen(tags) ? tags : ReadOnlyList(tags);
    try {
      if (tags.length > budget) throw new E.InvalidDataException('Object tag budget exceeded.');
      let start = tags.length, end = tags.length, depth = 0, owned = false;
      for (let i = 1; i < tags.length; i++) {
        const tag = tags[i];
        if (tag.Code === 102) {
          const control = tag.Value;
          if (control.startsWith('{')) depth++;
          else if (control === '}' && depth > 0) depth--;
          else throw new E.InvalidDataException('Invalid common object control group.');
          continue;
        }
        if (depth !== 0) continue;
        if (tag.Code === 100 || tag.Code === 1001) { start = i; break; }
        if (tag.Code === 101) throw new E.NotSupportedException('Embedded objects have no generic editable schema.');
        if (tag.Code === 5) {
          if (handle !== null) throw new E.InvalidDataException('Multiple object identities.');
          handle = DxfObjectText.Handle(tag.Value, false);
        }
        if (tag.Code === 330) {
          if (owned) throw new E.InvalidDataException('Multiple common owners.');
          owned = true; owner = DxfObjectText.Handle(tag.Value, true);
        }
      }
      if (depth !== 0 || handle === null) throw new E.InvalidDataException('Incomplete object prefix.');
      if (!this.Supported.includes(type)) throw new E.NotSupportedException('No schema registered for ' + type + '.');
      for (let i = start; i < tags.length; i++) if (tags[i].Code === 1001) { end = i; break; }
      if (type === 'ACDBPLACEHOLDER') {
        if (start !== end) throw new E.NotSupportedException('Private placeholder subclasses are not inferred.');
        return new M.DxfRawPlaceholder(handle, owner, source, view, start);
      }
      if (start >= end || tags[start].Code !== 100) throw new E.InvalidDataException('Missing object subclass.');
      const marker = tags[start].Value, body = tags.slice(start + 1, end).filter(t => t.Code !== 999);
      if (type === 'DICTIONARY' || type === 'ACDBDICTIONARYWDFLT') {
        if (marker !== 'AcDbDictionary') throw new E.InvalidDataException('Expected AcDbDictionary.');
        let hard = null, cloning = null, derived = false, defaultSeen = false, defaultHandle = null;
        const entries = [], names = new Set();
        for (let i = 0; i < body.length; i++) {
          const t = body[i];
          if (!derived && t.Code === 280 && hard === null) {
            if (t.Value !== 0 && t.Value !== 1) throw new E.InvalidDataException('Invalid dictionary ownership flag.');
            hard = t.Value !== 0;
          } else if (!derived && t.Code === 281 && cloning === null) cloning = DxfObjectText.Cloning(t.Value);
          else if (!derived && t.Code === 3 && i + 1 < body.length && [350, 360].includes(body[i + 1].Code)) {
            const entry = new M.DxfRawDictionaryEntry(DxfObjectText.Decode(t.Value), body[i + 1].Value, body[i + 1].Code === 360);
            const key = OrdinalIgnoreCaseKey(entry.Name);
            if (names.has(key)) throw new E.InvalidDataException('Duplicate case-insensitive dictionary key.');
            names.add(key); entries.push(entry); i++;
          } else if (!derived && type === 'ACDBDICTIONARYWDFLT' && t.Code === 100 && t.Value === 'AcDbDictionaryWithDefault') derived = true;
          else if (derived && t.Code === 340 && !defaultSeen) { defaultHandle = DxfObjectText.Handle(t.Value, true); defaultSeen = true; }
          else throw new E.NotSupportedException('Incomplete, duplicate or unsupported dictionary field at ' + t.Code + '.');
        }
        if (type === 'ACDBDICTIONARYWDFLT' && !derived) throw new E.InvalidDataException('Missing default dictionary subclass.');
        return new M.DxfRawDictionary(type, handle, owner, source, view, start, end, hard, cloning, defaultHandle, entries);
      }
      if (type === 'XRECORD') {
        if (marker !== 'AcDbXrecord') throw new E.InvalidDataException('Expected AcDbXrecord.');
        let cloning = null;
        if (body.length && body[0].Code === 280) cloning = DxfObjectText.Cloning(body.shift().Value);
        for (const t of body) this.ValidateXRecordTag(t);
        return new M.DxfRawXRecord(handle, owner, source, view, start, end, cloning, body);
      }
      if (type === 'DICTIONARYVAR') {
        if (marker !== 'DictionaryVariables') throw new E.InvalidDataException('Expected DictionaryVariables.');
        let schema = null, value = null;
        for (const t of body) {
          if (t.Code === 280 && schema === null) schema = t.Value;
          else if (t.Code === 1 && value === null) { value = DxfObjectText.Decode(t.Value); DxfObjectText.ValidateText(value); }
          else throw new E.NotSupportedException('Duplicate or unsupported DICTIONARYVAR field.');
        }
        return new M.DxfRawDictionaryVariable(handle, owner, source, view, start, end, schema, value);
      }
      if (type === 'IDBUFFER') {
        if (marker !== 'AcDbIdBuffer') throw new E.InvalidDataException('Expected AcDbIdBuffer.');
        const handles = body.map(t => {
          if (t.Code !== 330) throw new E.NotSupportedException('Unsupported IDBUFFER field.');
          return DxfObjectText.Handle(t.Value, true);
        });
        return new M.DxfRawIdBuffer(handle, owner, source, view, start, end, handles);
      }
      if (marker !== 'AcDbSortentsTable') throw new E.InvalidDataException('Expected AcDbSortentsTable.');
      let block = null; const sort = [], entities = new Set();
      for (let i = 0; i < body.length; i++) {
        if (body[i].Code === 330 && block === null) block = DxfObjectText.Handle(body[i].Value, false);
        else if (body[i].Code === 331 && i + 1 < body.length && body[i + 1].Code === 5) {
          const entry = new M.DxfRawSortOrderEntry(body[i].Value, body[++i].Value);
          if (entities.has(entry.EntityHandle)) throw new E.InvalidDataException('Duplicate SORTENTSTABLE entity.');
          entities.add(entry.EntityHandle); sort.push(entry);
        } else throw new E.NotSupportedException('Incomplete or unsupported SORTENTSTABLE pair.');
      }
      if (block === null) throw new E.InvalidDataException('Missing SORTENTSTABLE block pointer.');
      return new M.DxfRawSortentsTable(handle, owner, source, view, start, end, block, sort);
    } catch (error) {
      if (!(error instanceof E.ArgumentException || error instanceof E.InvalidDataException || error instanceof E.NotSupportedException)) throw error;
      return new M.DxfRawOpaqueStoredObject(type, handle, owner, source, view, error.message);
    }
  }
  static ValidateXRecordTag(tag) {
    if (!(tag instanceof DxfTag)) throw new E.ArgumentException('An XRECORD tag cannot be null.');
    if (tag.Code <= 0 || tag.Code > 369 || tag.Code === 5 || tag.Code === 105)
      throw new E.ArgumentException('Structural, identity or XData code is not editable XRECORD payload: ' + tag.Code);
    if (tag.Value instanceof Uint8Array && tag.Value.length > 127)
      throw new E.ArgumentException('XRECORD binary chunks must not exceed 127 bytes.');
  }
  static Wrap(value, body) {
    const tags = Array.from(value.Tags);
    return [...tags.slice(0, value.BodyStart), ...body,
      ...tags.slice(value.BodyStart, value.BodyEnd).filter(t => t.Code === 999), ...tags.slice(value.BodyEnd)];
  }
  static DictionaryBody(value, entries, hardOwner, cloning, defaultHandle) {
    const body = [new DxfTag(100, 'AcDbDictionary')];
    if (hardOwner !== null) body.push(new DxfTag(280, hardOwner ? 1 : 0));
    if (cloning !== null) body.push(new DxfTag(281, cloning));
    for (const entry of entries) body.push(new DxfTag(3, DxfObjectText.Encode(entry.Name)), new DxfTag(entry.IsHardOwner ? 360 : 350, entry.Handle));
    if (value.HasDefault) {
      body.push(new DxfTag(100, 'AcDbDictionaryWithDefault'));
      if (defaultHandle !== null) body.push(new DxfTag(340, defaultHandle));
    }
    return body;
  }
}
