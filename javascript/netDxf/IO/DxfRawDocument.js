import { SaveAtomic } from './DxfRawDocument.AtomicSave.js';
// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { DxfTag } from './DxfTag.js';
import { DxfTagValueType } from './DxfGroupCode.js';
import { DxfRawOptions } from './DxfRawOptions.js';
import { DxfRawSection } from './DxfRawSection.js';
import { DxfRawRecord } from './DxfRawRecord.js';
import { DxfRawTagContext } from './DxfRawTagContext.js';
import { DxfVersionNotSupportedException } from './DxfVersionNotSupportedException.js';
import { DxfVersion, DxfVersionStringValues } from '../Header/DxfVersion.js';
import { TextCodeValueReader } from './TextCodeValueReader.js';
import { TextCodeValueWriter } from './TextCodeValueWriter.js';
import { BinaryCodeValueReader } from './BinaryCodeValueReader.js';
import { BinaryCodeValueWriter } from './BinaryCodeValueWriter.js';
import { Encoding } from '../../runtime/Encoding.js';
import { MemoryStream } from '../../runtime/MemoryStream.js';
import { ReadOnlyList, OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { ArgumentException, ArgumentNullException, FormatException, InvalidDataException,
  EndOfStreamException, NotSupportedException, ThrowIfCancellationRequested } from '../../runtime/Errors.js';
const construct = Symbol('DxfRawDocument factory');
const Is = (tag,code,value) => tag.Code === code && OrdinalIgnoreCaseEquals(tag.Value,value);
const utf8Bom = b => b.length >= 3 && b[0] === 239 && b[1] === 187 && b[2] === 191;
const binaryPrefix = new TextEncoder().encode('AutoCAD Binary DXF');
const hasBinaryPrefix = b => b.length >= binaryPrefix.length && binaryPrefix.every((v,i) => b[i] === v);
function optionsOrDefault(options) {
  if (options == null) return new DxfRawOptions();
  if (!(options instanceof DxfRawOptions)) throw new ArgumentException('Expected DxfRawOptions.', 'options');
  return options;
}
function checkValueBudget(tag, options) {
  const value = tag.Value;
  if (typeof value === 'string' && value.length > options.MaximumStringLength)
    throw new InvalidDataException('Raw DXF exceeds the decoded string-length budget.');
  if (value instanceof Uint8Array && value.length > options.MaximumBytes)
    throw new InvalidDataException('Raw DXF binary data exceeds the byte budget.');
}
function snapshot(tags, options, token) {
  if (tags == null) throw new ArgumentNullException('tags');
  const result = [], context = new DxfRawTagContext();
  for (const tag of tags) {
    ThrowIfCancellationRequested(token);
    if (!(tag instanceof DxfTag)) throw new ArgumentException('A raw DXF tag sequence requires non-null DxfTag values.', 'tags');
    if (result.length >= options.MaximumTags) throw new InvalidDataException('Raw DXF exceeds the tag-count budget.');
    checkValueBudget(tag,options);
    if (tag.Code === 5 && (tag.ValueType === DxfTagValueType.String) !== context.Code5IsString)
      throw new ArgumentException('Group 5 requires a DIMBLK name only inside a DIMSTYLE table entry, and a handle elsewhere.', 'tags');
    context.Advance(tag); result.push(tag);
  }
  return result;
}
function indexSections(tags) {
  const sections = []; let start = -1, content = -1, name = null;
  for (let i = 0; i < tags.length; i++) {
    const tag = tags[i];
    if (Is(tag,0,'EOF')) {
      if (start >= 0) throw new FormatException("DXF EOF occurred before the section's ENDSEC.");
      if (i !== tags.length-1) throw new FormatException('DXF tags cannot follow EOF.');
      return sections;
    }
    if (tag.Code === 999) continue;
    if (start < 0) {
      if (!Is(tag,0,'SECTION')) throw new FormatException('Expected a DXF SECTION or EOF outside a section.');
      start = i;
    } else if (name == null) {
      if (tag.Code !== 2 || tag.Value.length === 0) throw new FormatException('A DXF SECTION requires a nonempty group-2 name.');
      name = tag.Value; content = i+1;
    } else if (Is(tag,0,'ENDSEC')) {
      sections.push(new DxfRawSection(name,tags,start,content,i+1)); start = -1; content = -1; name = null;
    }
  }
  throw new EndOfStreamException('The raw DXF tag sequence is missing its EOF record.');
}
function readProfile(tags) {
  let version = null, codePage = null, pending = null, completed = false;
  for (const tag of tags) {
    if (tag.Code === 999) continue;
    if (pending != null) {
      const expected = pending === '$ACADVER' ? 1 : 3;
      if (tag.Code !== expected) throw new FormatException(`Incorrect value group for ${pending}.`);
      if (expected === 1) version = tag.Value; else codePage = tag.Value;
      pending = null; completed = true; continue;
    }
    if (tag.Code !== 9) {
      if (completed) throw new FormatException('A raw DXF profile variable requires exactly one value tag.');
      continue;
    }
    completed = false; const name = tag.Value.toUpperCase();
    if (name === '$ACADVER') {
      if (version != null) throw new FormatException('Duplicate $ACADVER makes the raw DXF profile ambiguous.');
      pending = name;
    } else if (name === '$DWGCODEPAGE') {
      if (codePage != null) throw new FormatException('Duplicate $DWGCODEPAGE makes the raw DXF profile ambiguous.');
      pending = name;
    }
  }
  if (pending != null) throw new FormatException(`Missing value for ${pending}.`);
  if (version == null) throw new FormatException('Raw DXF requires a declared $ACADVER.');
  return { version, codePage };
}
function parseVersion(name) {
  const found = Object.entries(DxfVersionStringValues).find(([,value]) => OrdinalIgnoreCaseEquals(value,name));
  const version = found ? Number(found[0]) : DxfVersion.Unknown;
  if (version < DxfVersion.AutoCad12 || version > DxfVersion.AutoCad2018)
    throw new DxfVersionNotSupportedException('This raw DXF profile requires a recognized R11/R12–2018 database family.',version);
  return version;
}
function resolveEncoding(version,name) {
  if (version >= DxfVersion.AutoCad2007) return Encoding.UTF8;
  let codePage = 1252;
  if (name != null) {
    const match = /^(?:ANSI_|DOS)(\d+)$/i.exec(name);
    if (!match || Number(match[1]) > 2147483647)
      throw new NotSupportedException('The legacy raw DXF profile requires ANSI_<codepage>, DOS<codepage>, or absent/default 1252.');
    codePage = Number(match[1]);
  }
  return Encoding.GetEncoding(codePage === 0 ? 65001 : codePage);
}
function readBounded(stream,limit,token) {
  if (stream == null) throw new ArgumentNullException('stream');
  if (stream instanceof ArrayBuffer || ArrayBuffer.isView(stream)) {
    ThrowIfCancellationRequested(token);
    const bytes = stream instanceof ArrayBuffer ? new Uint8Array(stream) : new Uint8Array(stream.buffer,stream.byteOffset,stream.byteLength);
    if (bytes.length > limit) throw new InvalidDataException('Raw DXF exceeds the input byte budget.');
    return new Uint8Array(bytes);
  }
  if (!stream.CanRead || typeof stream.Read !== 'function') throw new ArgumentException('A readable stream is required.', 'stream');
  const copy = new MemoryStream(), buffer = new Uint8Array(8192);
  for (;;) {
    ThrowIfCancellationRequested(token);
    const wanted = Math.min(buffer.length,limit-copy.Length+1), count = stream.Read(buffer,0,wanted);
    if (!Number.isInteger(count) || count < 0 || count > wanted) throw new InvalidDataException('Invalid stream read count.');
    if (count === 0) return copy.ToArray();
    if (copy.Length + count > limit) throw new InvalidDataException('Raw DXF exceeds the input byte budget.');
    copy.Write(buffer,0,count);
  }
}
function* readTags(bytes,binary,encoding,options,token,legacy) {
  const reader = binary ? new BinaryCodeValueReader(bytes,encoding,legacy) :
    new TextCodeValueReader(encoding.GetString(utf8Bom(bytes) ? bytes.subarray(3) : bytes));
  const context = new DxfRawTagContext(); let count = 0;
  for (;;) {
    ThrowIfCancellationRequested(token);
    if (count++ >= options.MaximumTags) throw new InvalidDataException('Raw DXF exceeds the tag-count budget.');
    reader.Code5IsString = context.Code5IsString; reader.Next();
    const tag = reader.Code === 5 && reader.Code5IsString ? DxfTag.CreateDimensionStyleArrowName(reader.ReadString()) : new DxfTag(reader.Code,reader.Value);
    checkValueBudget(tag,options); context.Advance(tag); yield tag;
    if (Is(tag,0,'EOF')) break;
  }
  if (binary) {
    if (reader.CurrentPosition !== reader.Length) throw new FormatException('Unexpected bytes after binary DXF EOF.');
  } else for (let c; (c = reader.Reader.Read()) >= 0;) {
    ThrowIfCancellationRequested(token);
    if (c !== 32 && c !== 9 && c !== 13 && c !== 10) throw new FormatException('Unexpected data after text DXF EOF.');
  }
}
function findHeader(tags) {
  let state = 0, header = null;
  for (const tag of tags) {
    if (tag.Code === 999) { if (header != null) header.push(tag); continue; }
    if (Is(tag,0,'EOF')) break;
    if (state === 0) {
      if (!Is(tag,0,'SECTION')) throw new FormatException('Expected SECTION while determining the raw DXF profile.');
      state = 1;
    } else if (state === 1) {
      if (tag.Code !== 2) throw new FormatException('A DXF section name requires group 2.');
      if (Is(tag,2,'HEADER')) header = [];
      state = 2;
    } else if (Is(tag,0,'ENDSEC')) {
      if (header != null) return header;
      state = 0;
    } else if (header != null) header.push(tag);
  }
  throw new FormatException('No complete HEADER section was found for the raw DXF profile.');
}
class LimitedMemoryStream extends MemoryStream {
  #limit;
  constructor(limit) { super(); this.#limit = limit; }
  Write(buffer,offset=0,count=buffer.length-offset) {
    if (this.Position + count > this.#limit) throw new InvalidDataException('Raw DXF exceeds the normalized-output byte budget.');
    super.Write(buffer,offset,count);
  }
}
/** Immutable raw preservation/record-edit API. This is not the typed DxfDocument engine. */
export class DxfRawDocument {
  #originalBytes; #encoding; #codePageName; #options;
  constructor(tags,binary,originalBytes,options,key) {
    if (key !== construct) throw new NotSupportedException('Use DxfRawDocument.Load or DxfRawDocument.Create.');
    this.#options = options; this.Tags = ReadOnlyList(tags); this.Sections = ReadOnlyList(indexSections(this.Tags));
    let header = null;
    for (const section of this.Sections) {
      if (!OrdinalIgnoreCaseEquals(section.Name,'HEADER')) continue;
      if (header != null) throw new FormatException('Raw DXF requires an unambiguous single HEADER section.');
      header = section;
    }
    if (header == null) throw new FormatException('Raw DXF requires a HEADER section declaring its version.');
    const profile = readProfile(header.Content);
    this.Version = parseVersion(profile.version); this.#codePageName = profile.codePage;
    this.#encoding = resolveEncoding(this.Version,profile.codePage); this.IsBinary = binary; this.#originalBytes = originalBytes;
    Object.freeze(this);
  }
  SaveAtomic(file, binary = this.IsBinary, cancellationToken = null) {
    SaveAtomic(this, file, binary, cancellationToken);
  }
  get HasOriginalBytes() { return this.#originalBytes != null; }
  get EncodingCodePage() { return this.#encoding.CodePage; }
  static Load(stream,options=null,cancellationToken=null) {
    // Keep the original argument-validation order; BufferSource is a documented JS overload.
    if (stream == null) throw new ArgumentNullException('stream');
    options = optionsOrDefault(options);
    const bytes = readBounded(stream,options.MaximumBytes,cancellationToken), binary = hasBinaryPrefix(bytes);
    const legacy = binary && bytes.length > 23 && bytes[22] === 0 && bytes[23] !== 0;
    if (!binary && ((bytes.length >= 2 && ((bytes[0] === 255 && bytes[1] === 254) || (bytes[0] === 254 && bytes[1] === 255))) ||
      (bytes.length >= 4 && bytes[0] === 0 && bytes[1] === 0 && bytes[2] === 254 && bytes[3] === 255)))
      throw new NotSupportedException('UTF-16/UTF-32 DXF transports are not supported.');
    const bootstrap = new DxfRawOptions(options.MaximumBytes,options.MaximumTags,options.MaximumBytes);
    const profile = readProfile(findHeader(readTags(bytes,binary,Encoding.Latin1,bootstrap,cancellationToken,legacy)));
    const version = parseVersion(profile.version);
    if (binary && legacy !== (version < DxfVersion.AutoCad13)) throw new FormatException('Binary DXF group-code framing conflicts with its declared $ACADVER profile.');
    const encoding = resolveEncoding(version,profile.codePage);
    if (!binary && utf8Bom(bytes) && encoding.CodePage !== 65001) throw new NotSupportedException('A UTF-8 byte-order mark conflicts with the legacy raw DXF encoding profile.');
    const tags = snapshot(readTags(bytes,binary,encoding,options,cancellationToken,legacy),options,cancellationToken);
    return new DxfRawDocument(tags,binary,bytes,options,construct);
  }
  static Create(tags,binary=false,options=null) {
    options = optionsOrDefault(options);
    if (typeof binary !== 'boolean') throw new ArgumentException('Expected a Boolean transport flag.', 'binary');
    return new DxfRawDocument(snapshot(tags,options,null),binary,null,options,construct);
  }
  WithTags(tags) {
    const edited = DxfRawDocument.Create(tags,this.IsBinary,this.#options);
    if (edited.Version !== this.Version || edited.#codePageName !== this.#codePageName)
      throw new NotSupportedException('Raw tag edits cannot implicitly change the DXF version or code-page declaration.');
    return edited;
  }
  #validateRecordSnapshot(record) {
    if (record == null) throw new ArgumentNullException('record');
    if (!(record instanceof DxfRawRecord) || !record.IsFromSnapshot(this.Tags))
      throw new ArgumentException('The raw record belongs to a different document snapshot. Retrieve its current index before editing.', 'record');
    // Constructors are internal in C#; prevent forged ranges from becoming a JS editing capability.
    if (!this.Sections.some(section=>section.Records.includes(record)))
      throw new ArgumentException('The record is not an index published by this snapshot.', 'record');
  }
  WithRecord(record,replacement) {
    this.#validateRecordSnapshot(record);
    if (replacement == null) throw new ArgumentNullException('replacement');
    return this.WithTags(this.#rewriteRecord(record,replacement,false));
  }
  WithoutRecord(record) { this.#validateRecordSnapshot(record); return this.WithTags(this.#rewriteRecord(record,[],true)); }
  *#rewriteRecord(record,replacement,remove) {
    for (let i=0;i<record.StartTagIndex;i++) yield this.Tags[i];
    let first = true;
    for (const tag of replacement) {
      if (!(tag instanceof DxfTag)) throw new ArgumentException('A replacement record requires non-null DxfTag values.', 'replacement');
      if (Is(tag,0,'EOF') || Is(tag,0,'ENDSEC')) throw new ArgumentException('A replacement record cannot terminate a section or document.', 'replacement');
      if (first) {
        if (tag.Code !== record.MarkerCode || typeof tag.Value !== 'string' || tag.Value.length === 0)
          throw new ArgumentException('A replacement must begin with the same record marker code and a nonempty name.', 'replacement');
        first = false;
      } else if (tag.Code === record.MarkerCode || tag.Code === 0) throw new ArgumentException('A replacement must contain exactly one lexical record.', 'replacement');
      yield tag;
    }
    if (first && !remove) throw new ArgumentException('An empty replacement is not a record. Use WithoutRecord to remove it explicitly.', 'replacement');
    for (let i=record.EndTagIndex;i<this.Tags.length;i++) yield this.Tags[i];
  }
  Save(stream,binary=this.IsBinary,cancellationToken=null) {
    if (stream == null) throw new ArgumentNullException('stream');
    if (!stream.CanWrite || typeof stream.Write !== 'function') throw new ArgumentException('A writable stream is required.', 'stream');
    if (typeof binary !== 'boolean') { cancellationToken = binary; binary = this.IsBinary; }
    ThrowIfCancellationRequested(cancellationToken);
    const bytes = this.#originalBytes != null && binary === this.IsBinary ? this.#originalBytes : this.#serialize(binary,cancellationToken);
    for (let offset=0;offset<bytes.length;) {
      ThrowIfCancellationRequested(cancellationToken);
      const count = Math.min(65536,bytes.length-offset); stream.Write(bytes,offset,count); offset += count;
    }
  }
  /** JavaScript convenience overload; Save(stream, ...) retains the C# stream contract. */
  ToBytes(binary=this.IsBinary,cancellationToken=null) {
    const stream = new MemoryStream(); this.Save(stream,binary,cancellationToken); return stream.ToArray();
  }
  #serialize(binary,token) {
    for (const tag of this.Tags) {
      ThrowIfCancellationRequested(token); const value = tag.Value;
      if (binary && tag.Code === 999) throw new NotSupportedException('Binary DXF does not carry comment tags. Remove them explicitly before converting transports.');
      if (binary && value instanceof Uint8Array && value.length > 255)
        throw new NotSupportedException('The binary chunk length cannot fit in one byte. Raw output never splits unknown records automatically.');
      if (typeof value === 'string') {
        if (!binary && (value.includes('\r') || value.includes('\n')))
          throw new NotSupportedException('A raw binary string containing physical line breaks cannot be emitted as one text DXF value.');
        this.#encoding.GetByteCount(value);
      }
    }
    const output = new LimitedMemoryStream(this.#options.MaximumBytes);
    const writer = binary ? new BinaryCodeValueWriter(output,this.Version<DxfVersion.AutoCad13,this.#encoding) : new TextCodeValueWriter(output,this.#encoding);
    for (const tag of this.Tags) { ThrowIfCancellationRequested(token); writer.Write(tag.Code,tag.Value); }
    writer.Flush(); return output.ToArray();
  }
}
