// netDxf library. Copyright (c) Daniel Carvajal. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace netDxf.IO
{
    /// <summary>Immutable schema-aware access to an OBJECTS section without dropping unrelated raw records.</summary>
    /// <remarks>This is a separate raw-document pipeline, not an implicit fallback in DxfDocument.</remarks>
    public sealed class DxfRawObjectStore
    {
        private readonly Dictionary<ulong, List<DxfRawStoredObject>> byHandle = new Dictionary<ulong, List<DxfRawStoredObject>>();
        internal readonly DxfRawObjectStoreOptions Options;
        internal readonly DxfRawHandleIndex Index;

        private DxfRawObjectStore(DxfRawDocument document, DxfRawObjectStoreOptions options, CancellationToken cancellationToken)
        {
            this.Document = document ?? throw new ArgumentNullException(nameof(document));
            this.Options = options ?? new DxfRawObjectStoreOptions();
            this.Index = DxfRawHandleIndex.Create(document, cancellationToken: cancellationToken);
            var objects = new List<DxfRawStoredObject>();
            int sections = 0;
            foreach (DxfRawSection section in document.Sections)
            {
                if (!StringComparer.OrdinalIgnoreCase.Equals(section.Name, "OBJECTS")) continue;
                if (++sections != 1) throw new InvalidDataException("Multiple OBJECTS sections are ambiguous for object editing.");
                foreach (DxfRawRecord record in section.Records)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (objects.Count == this.Options.MaximumObjects) throw new InvalidDataException("OBJECTS record budget exceeded.");
                    DxfRawStoredObject value = DxfObjectSchema.Read(record.Tags, record, this.Options.MaximumPayloadTags);
                    objects.Add(value);
                    if (value.Handle == null) continue;
                    ulong handle = DxfObjectText.Number(value.Handle);
                    if (!this.byHandle.TryGetValue(handle, out List<DxfRawStoredObject> list))
                        this.byHandle.Add(handle, list = new List<DxfRawStoredObject>());
                    list.Add(value);
                }
            }
            this.Objects = objects.AsReadOnly();
        }
        /// <summary>Opens schema views. Unsupported or malformed object bodies remain explicit opaque views.</summary>
        public static DxfRawObjectStore Open(DxfRawDocument document, DxfRawObjectStoreOptions options = null,
            CancellationToken cancellationToken = default(CancellationToken))
        { return new DxfRawObjectStore(document, options, cancellationToken); }
        /// <summary>Gets the exact source snapshot.</summary>
        public DxfRawDocument Document { get; }
        /// <summary>Gets schema and opaque objects in original record order.</summary>
        public IReadOnlyList<DxfRawStoredObject> Objects { get; }
        /// <summary>Gets a unique object, null when absent, or throws for ambiguous identity.</summary>
        public DxfRawStoredObject Get(string handle)
        {
            ulong key = DxfObjectText.Number(DxfObjectText.Handle(handle, false));
            if (!this.byHandle.TryGetValue(key, out List<DxfRawStoredObject> matches)) return null;
            if (matches.Count != 1 || this.Index.FindDefinitions(handle).Count != 1)
                throw new InvalidDataException("Ambiguous object identity: " + handle);
            return matches[0];
        }
        /// <summary>Gets the first OBJECTS dictionary with null/zero common owner, or null if the section is empty.</summary>
        /// <remarks>Does not guess a replacement root from a later object.</remarks>
        public DxfRawDictionary RootDictionary
        {
            get
            {
                if (this.Objects.Count == 0) return null;
                var first = this.Objects[0] as DxfRawDictionary;
                if (first == null || (first.OwnerHandle != null && first.OwnerHandle != "0"))
                    throw new InvalidDataException("The first OBJECTS record is not an unowned root dictionary.");
                return first;
            }
        }
        /// <summary>Begins a single-threaded staged edit. Commit creates a new immutable raw document.</summary>
        public DxfRawObjectTransaction BeginEdit(CancellationToken cancellationToken = default(CancellationToken))
        { return new DxfRawObjectTransaction(this, cancellationToken); }
        /// <summary>Escapes literal backslashes and non-ASCII UTF-16 code units for portable DXF text values.</summary>
        /// <remarks>Use explicitly for application XRECORD string data; raw payload strings are never silently reinterpreted.</remarks>
        public static string EncodeText(string text) { return DxfObjectText.Encode(text); }
        /// <summary>Decodes one layer of DXF Unicode escapes without recursively decoding escaped backslashes.</summary>
        public static string DecodeText(string text) { return DxfObjectText.Decode(text); }
    }

    internal static class DxfObjectText
    {
        internal static ulong Number(string handle) { return ulong.Parse(handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture); }
        internal static string Handle(string handle, bool allowNull)
        {
            if (handle == null) throw new ArgumentNullException(nameof(handle));
            if (handle.Length == 0 || handle.Length > 16 || handle.Any(c => !IsHex(c)) ||
                !ulong.TryParse(handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong value) || (!allowNull && value == 0))
                throw new ArgumentException("Expected a " + (allowNull ? "" : "nonzero ") + "one-to-sixteen digit hexadecimal handle.", nameof(handle));
            return value.ToString("X", CultureInfo.InvariantCulture);
        }
        private static bool IsHex(char c) { return c >= '0' && c <= '9' || c >= 'A' && c <= 'F' || c >= 'a' && c <= 'f'; }
        internal static void ValidateName(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (name.Length == 0) throw new ArgumentException("A dictionary name cannot be empty.", nameof(name));
            ValidateText(name);
        }
        internal static void ValidateText(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0)
                throw new ArgumentException("Object text cannot contain transport delimiters.", nameof(value));
            for (int i = 0; i < value.Length; ++i)
                if (char.IsSurrogate(value[i]))
                {
                    if (!char.IsHighSurrogate(value[i]) || i + 1 == value.Length || !char.IsLowSurrogate(value[i + 1]))
                        throw new ArgumentException("Object text must contain paired UTF-16 surrogates.", nameof(value));
                    ++i;
                }
        }
        internal static string Encode(string value)
        {
            ValidateText(value);
            var result = new StringBuilder(value.Length);
            foreach (char c in value)
                if (c == '\\' || c > 127) result.Append("\\U+").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                else result.Append(c);
            return result.ToString();
        }
        internal static string Decode(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var result = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; ++i)
            {
                if (value[i] == '\\' && i + 6 < value.Length && value[i + 1] == 'U' && value[i + 2] == '+' &&
                    ushort.TryParse(value.Substring(i + 3, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort c))
                { result.Append((char)c); i += 6; }
                else result.Append(value[i]);
            }
            return result.ToString();
        }
        internal static DxfDuplicateRecordCloning Cloning(short value)
        {
            if (value < 0 || value > 5) throw new ArgumentOutOfRangeException(nameof(value), "Cloning must be in 0..5.");
            return (DxfDuplicateRecordCloning)value;
        }
    }

    internal static class DxfObjectSchema
    {
        internal static readonly Dictionary<string, string> RegisteredClasses = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "ACDBDICTIONARYWDFLT", "AcDbDictionaryWithDefault" }, { "ACDBPLACEHOLDER", "AcDbPlaceHolder" },
            { "DICTIONARYVAR", "AcDbDictionaryVar" }, { "IDBUFFER", "AcDbIdBuffer" }, { "SORTENTSTABLE", "AcDbSortentsTable" }
        };
        internal static readonly HashSet<string> Supported = new HashSet<string>(new[]
        { "DICTIONARY", "ACDBDICTIONARYWDFLT", "XRECORD", "ACDBPLACEHOLDER", "DICTIONARYVAR", "IDBUFFER", "SORTENTSTABLE" }, StringComparer.Ordinal);

        internal static DxfRawStoredObject Read(IReadOnlyList<DxfTag> tags, DxfRawRecord source, int budget)
        {
            string type = tags.Count > 0 && tags[0].Code == 0 ? (string)tags[0].RawValue : "";
            string handle = null, owner = null;
            try
            {
                if (tags.Count > budget) throw new InvalidDataException("Object tag budget exceeded.");
                int start = tags.Count, end = tags.Count, depth = 0;
                bool owned = false;
                for (int i = 1; i < tags.Count; ++i)
                {
                    DxfTag tag = tags[i];
                    if (tag.Code == 102)
                    {
                        string control = (string)tag.RawValue;
                        if (control.StartsWith("{", StringComparison.Ordinal)) ++depth;
                        else if (control == "}" && depth > 0) --depth;
                        else throw new InvalidDataException("Invalid common object control group.");
                        continue;
                    }
                    if (depth != 0) continue;
                    if (tag.Code == 100 || tag.Code == 1001) { start = i; break; }
                    if (tag.Code == 101) throw new NotSupportedException("Embedded objects have no generic editable schema.");
                    if (tag.Code == 5)
                    {
                        if (handle != null) throw new InvalidDataException("Multiple object identities.");
                        handle = DxfObjectText.Handle((string)tag.RawValue, false);
                    }
                    if (tag.Code == 330)
                    {
                        if (owned) throw new InvalidDataException("Multiple common owners.");
                        owned = true; owner = DxfObjectText.Handle((string)tag.RawValue, true);
                    }
                }
                if (depth != 0 || handle == null) throw new InvalidDataException("Incomplete object prefix.");
                if (!Supported.Contains(type)) throw new NotSupportedException("No schema registered for " + type + ".");
                for (int i = start; i < tags.Count; ++i) if (tags[i].Code == 1001) { end = i; break; }
                if (type == "ACDBPLACEHOLDER")
                {
                    if (start != end) throw new NotSupportedException("Private placeholder subclasses are not inferred.");
                    return new DxfRawPlaceholder(handle, owner, source, tags, start);
                }
                if (start >= end || tags[start].Code != 100) throw new InvalidDataException("Missing object subclass.");
                string marker = (string)tags[start].RawValue;
                var body = tags.Skip(start + 1).Take(end - start - 1).Where(t => t.Code != 999).ToList();
                if (type == "DICTIONARY" || type == "ACDBDICTIONARYWDFLT")
                {
                    if (marker != "AcDbDictionary") throw new InvalidDataException("Expected AcDbDictionary.");
                    bool? hard = null; DxfDuplicateRecordCloning? cloning = null;
                    bool derived = false, defaultSeen = false; string defaultHandle = null;
                    var entries = new List<DxfRawDictionaryEntry>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < body.Count; ++i)
                    {
                        DxfTag t = body[i];
                        if (!derived && t.Code == 280 && !hard.HasValue)
                        { short v = (short)t.RawValue; if (v != 0 && v != 1) throw new InvalidDataException("Invalid dictionary ownership flag."); hard = v != 0; }
                        else if (!derived && t.Code == 281 && !cloning.HasValue) cloning = DxfObjectText.Cloning((short)t.RawValue);
                        else if (!derived && t.Code == 3 && i + 1 < body.Count && (body[i + 1].Code == 350 || body[i + 1].Code == 360))
                        {
                            var entry = new DxfRawDictionaryEntry(DxfObjectText.Decode((string)t.RawValue), (string)body[i + 1].RawValue, body[i + 1].Code == 360);
                            if (!names.Add(entry.Name)) throw new InvalidDataException("Duplicate case-insensitive dictionary key.");
                            entries.Add(entry); ++i;
                        }
                        else if (!derived && type == "ACDBDICTIONARYWDFLT" && t.Code == 100 && Equals(t.RawValue, "AcDbDictionaryWithDefault")) derived = true;
                        else if (derived && t.Code == 340 && !defaultSeen) { defaultHandle = DxfObjectText.Handle((string)t.RawValue, true); defaultSeen = true; }
                        else throw new NotSupportedException("Incomplete, duplicate or unsupported dictionary field at " + t.Code + ".");
                    }
                    if (type == "ACDBDICTIONARYWDFLT" && !derived) throw new InvalidDataException("Missing default dictionary subclass.");
                    return new DxfRawDictionary(type, handle, owner, source, tags, start, end, hard, cloning, defaultHandle, entries);
                }
                if (type == "XRECORD")
                {
                    if (marker != "AcDbXrecord") throw new InvalidDataException("Expected AcDbXrecord.");
                    DxfDuplicateRecordCloning? cloning = null;
                    if (body.Count != 0 && body[0].Code == 280) { cloning = DxfObjectText.Cloning((short)body[0].RawValue); body.RemoveAt(0); }
                    foreach (DxfTag t in body) ValidateXRecordTag(t);
                    return new DxfRawXRecord(handle, owner, source, tags, start, end, cloning, body);
                }
                if (type == "DICTIONARYVAR")
                {
                    if (marker != "DictionaryVariables") throw new InvalidDataException("Expected DictionaryVariables.");
                    short? schema = null; string value = null;
                    foreach (DxfTag t in body)
                        if (t.Code == 280 && !schema.HasValue) schema = (short)t.RawValue;
                        else if (t.Code == 1 && value == null) { value = DxfObjectText.Decode((string)t.RawValue); DxfObjectText.ValidateText(value); }
                        else throw new NotSupportedException("Duplicate or unsupported DICTIONARYVAR field.");
                    return new DxfRawDictionaryVariable(handle, owner, source, tags, start, end, schema, value);
                }
                if (type == "IDBUFFER")
                {
                    if (marker != "AcDbIdBuffer") throw new InvalidDataException("Expected AcDbIdBuffer.");
                    var handles = new List<string>();
                    foreach (DxfTag t in body)
                    {
                        if (t.Code != 330) throw new NotSupportedException("Unsupported IDBUFFER field.");
                        handles.Add(DxfObjectText.Handle((string)t.RawValue, true));
                    }
                    return new DxfRawIdBuffer(handle, owner, source, tags, start, end, handles);
                }
                if (marker != "AcDbSortentsTable") throw new InvalidDataException("Expected AcDbSortentsTable.");
                string block = null; var sort = new List<DxfRawSortOrderEntry>(); var entities = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < body.Count; ++i)
                    if (body[i].Code == 330 && block == null) block = DxfObjectText.Handle((string)body[i].RawValue, false);
                    else if (body[i].Code == 331 && i + 1 < body.Count && body[i + 1].Code == 5)
                    {
                        var entry = new DxfRawSortOrderEntry((string)body[i].RawValue, (string)body[++i].RawValue);
                        if (!entities.Add(entry.EntityHandle)) throw new InvalidDataException("Duplicate SORTENTSTABLE entity.");
                        sort.Add(entry);
                    }
                    else throw new NotSupportedException("Incomplete or unsupported SORTENTSTABLE pair.");
                if (block == null) throw new InvalidDataException("Missing SORTENTSTABLE block pointer.");
                return new DxfRawSortentsTable(handle, owner, source, tags, start, end, block, sort);
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidDataException || e is NotSupportedException)
            { return new DxfRawOpaqueStoredObject(type, handle, owner, source, tags, e.Message); }
        }

        internal static void ValidateXRecordTag(DxfTag tag)
        {
            if (tag == null) throw new ArgumentException("An XRECORD tag cannot be null.");
            // Autodesk documents XRECORD payload codes 1 through 369.
            // Within that payload, 100/101/102 are application data, not subclass/control markers.
            if (tag.Code <= 0 || tag.Code > 369 || tag.Code == 5 || tag.Code == 105)
                throw new ArgumentException("Structural, identity or XData code is not editable XRECORD payload: " + tag.Code);
            if (tag.RawValue is byte[] bytes && bytes.Length > 127)
                throw new ArgumentException("XRECORD binary chunks must not exceed 127 bytes.");
        }

        internal static List<DxfTag> Wrap(DxfRawStoredObject value, IEnumerable<DxfTag> body)
        {
            var tags = value.Tags.Take(value.BodyStart).ToList();
            tags.AddRange(body);
            // Retain comments from the edited subclass at the end of its canonicalized body.
            tags.AddRange(value.Tags.Skip(value.BodyStart).Take(value.BodyEnd - value.BodyStart).Where(t => t.Code == 999));
            tags.AddRange(value.Tags.Skip(value.BodyEnd));
            return tags;
        }
        internal static List<DxfTag> DictionaryBody(DxfRawDictionary value, IEnumerable<DxfRawDictionaryEntry> entries,
            bool? hardOwner, DxfDuplicateRecordCloning? cloning, string defaultHandle)
        {
            var body = new List<DxfTag> { new DxfTag(100, "AcDbDictionary") };
            if (hardOwner.HasValue) body.Add(new DxfTag(280, (short)(hardOwner.Value ? 1 : 0)));
            if (cloning.HasValue) body.Add(new DxfTag(281, (short)cloning.Value));
            foreach (DxfRawDictionaryEntry entry in entries)
            { body.Add(new DxfTag(3, DxfObjectText.Encode(entry.Name))); body.Add(new DxfTag((short)(entry.IsHardOwner ? 360 : 350), entry.Handle)); }
            if (value.HasDefault)
            {
                body.Add(new DxfTag(100, "AcDbDictionaryWithDefault"));
                if (defaultHandle != null) body.Add(new DxfTag(340, defaultHandle));
            }
            return body;
        }
    }
}
