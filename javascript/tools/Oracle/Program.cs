// Development-only JSON-lines oracle. The shipped JavaScript never calls .NET.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using netDxf;
using netDxf.IO;
using netDxf.Header;
using netDxf.Entities;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static object Attempt(Func<object> action)
    {
        try { return new { ok = true, value = action() }; }
        catch (Exception error) { return new { ok = false, error = error.GetType().Name }; }
    }
    private static string Bits(double value) => unchecked((ulong)BitConverter.DoubleToInt64Bits(value)).ToString("X16", CultureInfo.InvariantCulture);
    private static double Double(string bits) => BitConverter.Int64BitsToDouble(unchecked((long)ulong.Parse(bits, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
    private static object Wire(DxfTag tag) => new object[] { tag.Code, (int)tag.ValueType, tag.Value switch {
        double d => Bits(d), long l => l.ToString(CultureInfo.InvariantCulture), byte[] b => Convert.ToBase64String(b), object v => v
    }};
    private static DxfTag Tag(JsonElement row)
    {
        short code = row[0].GetInt16();
        var type = (DxfTagValueType)row[1].GetInt32();
        JsonElement value = row[2];
        if (code == 5 && type == DxfTagValueType.String) return DxfTag.CreateDimensionStyleArrowName(value.GetString()!);
        object decoded = type switch {
            DxfTagValueType.Double => Double(value.GetString()!),
            DxfTagValueType.Int16 => value.GetInt16(), DxfTagValueType.Int32 => value.GetInt32(),
            DxfTagValueType.Int64 => long.Parse(value.GetString()!, CultureInfo.InvariantCulture),
            DxfTagValueType.Boolean => value.GetBoolean(), DxfTagValueType.BinaryData => value.GetBytesFromBase64(),
            _ => value.GetString()!
        };
        return new DxfTag(code, decoded);
    }
    private static DxfRawOptions? Options(JsonElement input)
    {
        if (!input.TryGetProperty("options", out var o)) return null;
        return new DxfRawOptions(o.TryGetProperty("maximumBytes", out var b) ? b.GetInt32() : 64 * 1024 * 1024,
            o.TryGetProperty("maximumTags", out var t) ? t.GetInt32() : 1000000,
            o.TryGetProperty("maximumStringLength", out var s) ? s.GetInt32() : 1024 * 1024);
    }
    private static byte[] Save(DxfRawDocument document, bool binary)
    { using var output = new MemoryStream(); document.Save(output, binary); return output.ToArray(); }
    private static DxfRawDocument ReadDocument(JsonElement input)
    {
        var options = Options(input);
        var doc = input.TryGetProperty("tags", out var tags)
            ? DxfRawDocument.Create(tags.EnumerateArray().Select(Tag), input.TryGetProperty("binary", out var binary) && binary.GetBoolean(), options)
            : DxfRawDocument.Load(new MemoryStream(input.GetProperty("bytes").GetBytesFromBase64(), false), options);
        if (input.TryGetProperty("edit", out var edit))
        {
            var replacement = doc.Tags.ToArray();
            replacement[edit.GetProperty("index").GetInt32()] = Tag(edit.GetProperty("tag"));
            doc = doc.WithTags(replacement);
        }
        if (input.TryGetProperty("normalize", out var normalize) && normalize.GetBoolean()) doc = doc.WithTags(doc.Tags);
        return doc;
    }
    private static object Raw(JsonElement input) => Snapshot(ReadDocument(input));
    private static object Snapshot(DxfRawDocument doc)
    {
        return new {
            version = (int)doc.Version, binary = doc.IsBinary, original = doc.HasOriginalBytes, codePage = doc.EncodingCodePage,
            tags = doc.Tags.Select(Wire).ToArray(),
            sections = doc.Sections.Select(s => new {
                name = s.Name, start = s.StartTagIndex, content = s.ContentStartTagIndex, end = s.EndTagIndex,
                preamble = s.Preamble.Count,
                records = s.Records.Select(r => new { name = r.Name, start = r.StartTagIndex, end = r.EndTagIndex, marker = r.MarkerCode }).ToArray()
            }).ToArray(),
            text = Attempt(() => Convert.ToBase64String(Save(doc, false))),
            binaryOutput = Attempt(() => Convert.ToBase64String(Save(doc, true)))
        };
    }
    private static object Occurrence(DxfRawHandleOccurrence item) => new {
        record = item.Record?.StartTagIndex, index = item.TagIndex, code = item.Code, handle = item.Handle,
        canonical = item.CanonicalHandle, numeric = item.NumericHandle.ToString(CultureInfo.InvariantCulture),
        role = (int)item.Role, context = item.Context, subclass = item.Subclass, reference = item.IsReference
    };
    private static object Handles(JsonElement input)
    {
        var doc = ReadDocument(input);
        DxfRawHandleIndexOptions? options = null;
        if (input.TryGetProperty("indexOptions", out var o)) options = new DxfRawHandleIndexOptions(
            o.TryGetProperty("maximumOccurrences", out var mo) ? mo.GetInt32() : 1000000,
            o.TryGetProperty("maximumDiagnostics", out var md) ? md.GetInt32() : 100000);
        var index = DxfRawHandleIndex.Create(doc, options);
        object? closure = null, remapped = null;
        if (input.TryGetProperty("roots", out var roots)) closure = Attempt(() => {
            var records = doc.Sections.SelectMany(s => s.Records).ToDictionary(r => r.StartTagIndex);
            var selected = roots.EnumerateArray().Select(r => records[r.GetInt32()]);
            var c = index.GetDependencyClosure(selected, (DxfRawReferenceTraversal)input.GetProperty("traversal").GetInt32());
            return new { records = c.Records.Select(r => r.StartTagIndex).ToArray(),
                unresolved = c.UnresolvedReferences.Select(x => x.TagIndex).ToArray(),
                ambiguous = c.AmbiguousReferences.Select(x => x.TagIndex).ToArray(),
                opaque = c.UninterpretedHandles.Select(x => x.TagIndex).ToArray(), resolved = c.AreSelectedReferencesResolved };
        });
        if (input.TryGetProperty("mapping", out var mapping)) remapped = Attempt(() => {
            var m = mapping.EnumerateArray().ToDictionary(r => r[0].GetString()!, r => r[1].GetString()!, StringComparer.Ordinal);
            var result = index.RemapHandles(m);
            return new { unchanged = ReferenceEquals(doc, result), document = Snapshot(result) };
        });
        return new { occurrences = index.Occurrences.Select(Occurrence).ToArray(),
            diagnostics = index.Diagnostics.Select(d => new { kind = (int)d.Kind, record = d.Record?.StartTagIndex,
                index = d.TagIndex, handle = d.Handle, message = d.Message }).ToArray(),
            closure, remapped };
    }
    private static object StoredObject(DxfRawStoredObject value) => new {
        kind = value.GetType().Name, typeName = value.TypeName, handle = value.Handle, ownerHandle = value.OwnerHandle,
        tags = value.Tags.Select(Wire).ToArray(),
        data = value switch {
            DxfRawDictionary d => (object)new { d.HardOwnerFlag, d.CloningFlag, d.HasDefault, d.DefaultHandle, d.Entries },
            DxfRawXRecord x => new { x.CloningFlag, tags = x.Data.Select(Wire).ToArray() },
            DxfRawDictionaryVariable v => new { v.SchemaNumber, v.Value },
            DxfRawIdBuffer b => new { b.Handles },
            DxfRawSortentsTable t => new { t.BlockRecordHandle, t.Entries },
            DxfRawOpaqueStoredObject o => new { opaque = true, reasonAvailable = o.Reason.Length > 0 },
            _ => (object?)null
        }
    };
    private static object Objects(JsonElement input)
    {
        var doc = ReadDocument(input);
        DxfRawObjectStoreOptions? options = null;
        if (input.TryGetProperty("storeOptions", out var o)) options = new DxfRawObjectStoreOptions(
            o.TryGetProperty("maximumObjects", out var mo) ? mo.GetInt32() : 100000,
            o.TryGetProperty("maximumPayloadTags", out var mp) ? mp.GetInt32() : 1000000,
            o.TryGetProperty("maximumChanges", out var mc) ? mc.GetInt32() : 100000);
        var store = DxfRawObjectStore.Open(doc, options);
        var results = new List<object>(); var references = new Dictionary<string, string>();
        DxfRawObjectTransaction? transaction = null;
        string? Resolve(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null :
            e.ValueKind == JsonValueKind.Object ? references[e.GetProperty("ref").GetString()!] : e.GetString();
        object? DecodeArgument(JsonElement e, Type type)
        {
            if (e.ValueKind == JsonValueKind.Null) return null;
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type == typeof(string)) return Resolve(e);
            if (type == typeof(bool)) return e.GetBoolean();
            if (type == typeof(short)) return e.GetInt16();
            if (type.IsEnum) return Enum.ToObject(type, e.GetInt32());
            if (type == typeof(IEnumerable<DxfTag>)) return e.EnumerateArray().Select(t=>t[2].ValueKind==JsonValueKind.Object?new DxfTag(t[0].GetInt16(),Resolve(t[2])!):Tag(t)).ToArray();
            if (type == typeof(IEnumerable<string>)) return e.EnumerateArray().Select(x=>Resolve(x)!).ToArray();
            if (type == typeof(IEnumerable<DxfRawSortOrderEntry>)) return e.EnumerateArray()
                .Select(x=>new DxfRawSortOrderEntry(Resolve(x[0])!,Resolve(x[1])!)).ToArray();
            throw new ArgumentException("Unsupported test protocol parameter " + type.Name);
        }
        try
        {
            if (input.TryGetProperty("steps", out var steps)) foreach (var step in steps.EnumerateArray())
            {
                results.Add(Attempt(() => {
                    string method = step.GetProperty("method").GetString()!;
                    if (method == "BeginEdit") { transaction?.Dispose(); transaction = DxfRawObjectStore.Open(doc,options).BeginEdit(); return null!; }
                    if (transaction == null) throw new InvalidOperationException("BeginEdit is required.");
                    var member = typeof(DxfRawObjectTransaction).GetMethod(method) ?? throw new ArgumentException("Unknown operation.");
                    var supplied = step.TryGetProperty("args",out var a) ? a.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
                    var parameters = member.GetParameters();
                    if (supplied.Length > parameters.Length) throw new ArgumentException("Too many test arguments.");
                    var arguments = parameters.Select((parameter,i)=>i<supplied.Length?DecodeArgument(supplied[i],parameter.ParameterType):parameter.DefaultValue).ToArray();
                    object? value;
                    try { value = member.Invoke(transaction,arguments); }
                    catch (System.Reflection.TargetInvocationException ex) { throw ex.InnerException!; }
                    if (step.TryGetProperty("as",out var id)) references[id.GetString()!]=(string)value!;
                    if (value is DxfRawDocument saved) { doc=saved; return new { committed=true }; }
                    if (value is DxfRawStoredObject stored) return StoredObject(stored);
                    return value!;
                }));
            }
            var current = DxfRawObjectStore.Open(doc,options);
            return new { results, references, document=Snapshot(doc), objects=current.Objects.Select(StoredObject).ToArray() };
        }
        finally { transaction?.Dispose(); }
    }
    private static object Format(JsonElement input)
    {
        // Exercise the production writer rather than duplicating its formatting rule.
        var type = typeof(DxfDocument).Assembly.GetType("netDxf.IO.TextCodeValueWriter", true)!;
        return input.GetProperty("bits").EnumerateArray().Select(item => {
            using var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\r\n" };
            var writer = Activator.CreateInstance(type, output)!;
            type.GetMethod("WriteDouble")!.Invoke(writer, new object[] { Double(item.GetString()!) });
            return output.ToString().TrimEnd('\r', '\n');
        }).ToArray();
    }
    private static object EncodingOperation(JsonElement input)
    {
        var encoding = Encoding.GetEncoding(input.GetProperty("codePage").GetInt32(), EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        return input.TryGetProperty("text", out var text) ? (object)Convert.ToBase64String(encoding.GetBytes(text.GetString()!))
            : encoding.GetString(input.GetProperty("bytes").GetBytesFromBase64());
    }
    private static object TypedFixture(JsonElement input)
    {
        var doc = new DxfDocument((DxfVersion)input.GetProperty("version").GetInt32());
        doc.Comments.Clear();
        doc.Entities.Add(new Line(new Vector3(1e-20, 2, 3), new Vector3(4, 5, 6)));
        doc.Entities.Add(new Circle(new Vector3(7, 8, 9), 1.25));
        // Dates/GUIDs are produced once, then the identical bytes feed both raw implementations.
        using var output = new MemoryStream();
        if (!doc.Save(output, input.GetProperty("binary").GetBoolean())) throw new InvalidOperationException("Typed fixture save returned false.");
        return Convert.ToBase64String(output.ToArray());
    }
    private static object TypedRead(JsonElement input)
    {
        using var stream = new MemoryStream(input.GetProperty("bytes").GetBytesFromBase64());
        var doc = DxfDocument.Load(stream) ?? throw new InvalidDataException("Typed Load returned null.");
        return new { version = (int)doc.DrawingVariables.AcadVer,
            lines = doc.Entities.Lines.Select(l => new[] { Bits(l.StartPoint.X), Bits(l.StartPoint.Y), Bits(l.StartPoint.Z), Bits(l.EndPoint.X), Bits(l.EndPoint.Y), Bits(l.EndPoint.Z) }).ToArray(),
            circles = doc.Entities.Circles.Select(c => new[] { Bits(c.Center.X), Bits(c.Center.Y), Bits(c.Center.Z), Bits(c.Radius) }).ToArray() };
    }
    private static object OrdinalMap()
    {
        var map = new List<int[]>();
        for (int i=0;i<=0x10ffff;i++)
        {
            if (!Rune.IsValid(i)) continue;
            var rune=new Rune(i); var upper=Rune.ToUpperInvariant(rune);
            if (rune!=upper && StringComparer.OrdinalIgnoreCase.Equals(rune.ToString(),upper.ToString()))
                map.Add(new[]{i,upper.Value});
        }
        return map;
    }
    public static int Main()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            object result = Attempt(() => {
                using var document = JsonDocument.Parse(line);
                var input = document.RootElement;
                return input.GetProperty("op").GetString() switch {
                    "lifecycle" => LifecycleOracle.Execute(input), "ordinal" => input.GetProperty("pairs").EnumerateArray().Select(p=>StringComparer.OrdinalIgnoreCase.Equals(p[0].GetString(),p[1].GetString())).ToArray(),
                    "ordinal-map" => OrdinalMap(), "filesystem" => FileSystemOracle.Execute(input), "geometry" => GeometryOracle.Execute(input), "collection" => CollectionOracle.Execute(input), "raw" => Raw(input), "handles" => Handles(input), "objects" => Objects(input), "format" => Format(input), "encoding" => EncodingOperation(input),
                    "typed-fixture" => TypedFixture(input), "typed-read" => TypedRead(input),
                    _ => throw new ArgumentException("Unknown oracle operation.")
                };
            });
            Console.WriteLine(JsonSerializer.Serialize(result, Json));
        }
        return 0;
    }
}
