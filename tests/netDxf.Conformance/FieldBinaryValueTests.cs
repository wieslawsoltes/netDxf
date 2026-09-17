using netDxf;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly int[] FieldBinarySizes = { 0, 1, 126, 127, 128, 254, 255, 1024 };
    private static byte[] FieldBytes(int length) => Enumerable.Range(0, length).Select(i => (byte)((i * 73 + 19) & 255)).ToArray();

    private static void RegisterFieldBinaryValueTests()
    {
        Run("field-binary/value-contract", FieldBinaryValueContract);
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        foreach (int size in FieldBinarySizes)
            Run($"field-binary/roundtrip/{version}/{binary}/{size}", () => FieldBinaryRoundTrip(version, binary, size));
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad2000, DxfVersion.AutoCad2018 })
        foreach (bool binary in new[] { false, true })
        {
            foreach (string mode in new[] { "named", "split", "empty-chunk", "retained", "fallback", "maximum", "to-null", "to-scalar" })
                Run($"field-binary/boundary/{version}/{binary}/{mode}", () => FieldBinaryBoundary(version, binary, mode));
            foreach (string fault in new[] { "negative", "huge", "short", "long", "no-bytes", "missing-size", "duplicate-size", "date-is-not-buffer", "extra-chunk", "zero-chunk", "named-count" })
                Run($"field-binary/reject/{version}/{binary}/{fault}", () => FieldBinaryRejected(version, binary, fault));
        }
        foreach (bool binary in new[] { false, true })
        foreach (int count in new[] { 16, 17 })
            Run($"field-binary/forest-budget/{binary}/{count}", () => FieldBinaryForestBudget(binary, count));
        Run("field-binary/projection-budget", FieldBinaryProjectionBudget);
    }

    private static void FieldBinaryValueContract()
    {
        Throws<ArgumentNullException>(() => new DxfFieldBinaryValue(null!));
        Throws<ArgumentOutOfRangeException>(() => new DxfFieldBinaryValue(new byte[DxfFieldBinaryValue.MaximumLength + 1]));
        Throws<ArgumentOutOfRangeException>(() => new DxfFieldResult(new byte[DxfFieldBinaryValue.MaximumLength + 1], "too large"));
        var source = FieldBytes(256); var expected = (byte[])source.Clone();
        var value = new DxfFieldBinaryValue(source); var result = new DxfFieldResult(source, "bytes");
        source[0] ^= 255;
        Check(value.ToArray().SequenceEqual(expected), "binary constructor aliases caller input");
        var projected = (DxfFieldBinaryValue)result.Value;
        Check(projected.Equals(value) && value.GetHashCode() == projected.GetHashCode(), "binary result ownership/equality");
        var exported = value.ToArray(); exported[0] ^= 255;
        Check(value[0] == expected[0] && value.Length == 256, "binary export aliases internal data");
        Check(value.Equals(value) && !value.Equals(null) && !value.Equals((object)expected), "binary value equality kinds");
        Check(!value!.Equals(new DxfFieldBinaryValue(source)) && !value.Equals(new DxfFieldBinaryValue(Array.Empty<byte>())), "binary inequality");
        Throws<IndexOutOfRangeException>(() => _ = value[-1]);
        Throws<IndexOutOfRangeException>(() => _ = value[value.Length]);
        var empty = new DxfFieldBinaryValue(Array.Empty<byte>());
        Check(new DxfFieldResult(empty, "").Value != null && empty.Length == 0, "empty binary conflated with null");
    }

    private static void FieldBinaryRoundTrip(DxfVersion version, bool binary, int size)
    {
        var doc = FieldResultDocument(version, binary); var root = ResultRoot(doc); var child = root.Children[0];
        string suffix = $"{version}-{binary}-{size}.dxf";
        var original = doc.Objects.Items.OfType<DxfStoredField>().ToDictionary(f => f, f => f.Payload);
        var old = child.Evaluation; var refs = child.References.ToArray();
        SaveFieldResults(doc, binary, "field-binary-before-" + suffix); long seed = StoredFieldSeed(doc);
        byte[] bytes = FieldBytes(size); var value = new DxfFieldResult(bytes, "binary:" + size, "buffer-view");
        if (size != 0) bytes[0] ^= 255;
        Equal(2, doc.Objects.EvaluateFieldTree(root, input => input.Field == child ? value : input.ComposeText()), "binary and parent updates");
        var stored = (DxfFieldBinaryValue)child.Evaluation.Value;
        Check(stored.ToArray().SequenceEqual(FieldBytes(size)), "binary cache bytes changed");
        Equal(seed, StoredFieldSeed(doc), "binary update allocated a handle");
        Check(refs.SequenceEqual(child.References), "binary update changed resource references");
        Check(old.Value == null && old.Data.SelectMany(d => d.Tags).SequenceEqual(child.Evaluation.Data.SelectMany(d => d.Tags)), "old scalar snapshot mutated");
        Check(original.Where(p => p.Key != root && p.Key != child).All(p => ReferenceEquals(p.Key.Payload, p.Value)), "unselected FIELD changed");
        int at = child.Payload.ToList().FindIndex(t => t.Code == 7) + 1;
        int kind = child.Payload.ToList().FindIndex(at, t => t.Code == 90);
        Equal(128, (int)child.Payload[kind].Value, "Buffer type must be 128, not Date (8)");
        Equal(size, (int)child.Payload[kind + 1].Value, "binary stored length");
        var chunks = child.Payload.Skip(kind + 2).TakeWhile(t => t.Code == 310).ToArray();
        Equal((size + 126) / 127, chunks.Length, "canonical binary chunk count");
        Check(chunks.All(t => ((byte[])t.Value).Length is > 0 and <= 127), "binary chunks outside physical limit");
        var payload = child.Payload;
        Equal(0, doc.Objects.EvaluateFieldTree(root, input => input.Field == child
            ? new DxfFieldResult(FieldBytes(size), "binary:" + size, "buffer-view") : input.ComposeText()), "equal binary bytes must be no-op");
        Check(ReferenceEquals(payload, child.Payload), "equal binary bytes rewrote snapshots");
        SaveFieldResults(doc, binary, "field-binary-after-" + suffix);
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "opposite binary-value transport"); output.Position = 0;
        var loaded = ResultRoot(DxfDocument.Load(output)!);
        Check(((DxfFieldBinaryValue)loaded.Children[0].Evaluation.Value).Equals(stored), "binary-value transport reload");
        Equal("binary:" + size, loaded.Evaluation.Value, "binary child display composition");
        Equal(0, loaded.Database.Validate().Count, "binary-value graph invalid");
    }

    // Replace an existing scalar packet in a copied native-shaped record. This is
    // explicitly constructed Buffer evidence, not a new native producer observation.
    private static void PutFieldBinary(List<DxfTag> tags, int kind, byte[] bytes, int chunkSize = 127, bool emptyChunk = false)
    {
        int scalarEnd = kind + 1;
        int oldKind = (int)tags[kind].Value;
        if (oldKind is 1 or 2 or 4 || oldKind == 0 && tags[scalarEnd].Code == 91) scalarEnd++;
        else if (oldKind == 128)
        { scalarEnd++; while (scalarEnd < tags.Count && tags[scalarEnd].Code == 310) scalarEnd++; }
        var replacement = new List<DxfTag> { new(90, 128), new(92, bytes.Length) };
        for (int at = 0; at < bytes.Length; at += chunkSize)
            replacement.Add(new DxfTag(310, bytes.Skip(at).Take(chunkSize).ToArray()));
        if (emptyChunk && bytes.Length == 0) replacement.Add(new DxfTag(310, Array.Empty<byte>()));
        tags.RemoveRange(kind, scalarEnd - kind); tags.InsertRange(kind, replacement);
    }

    private static DxfDocument BinarySource(DxfVersion version, bool binary, string mode)
    {
        return FieldResultDocument(version, binary, raw =>
        {
            var record = StoredFieldRecord(raw, "14F"); var tags = record.Tags.ToList();
            int marker = tags.FindIndex(t => t.Code == (mode == "named" ? 6 : 7));
            int kind = tags.FindIndex(marker + 1, t => t.Code == 90);
            PutFieldBinary(tags, kind, mode == "empty-chunk" ? Array.Empty<byte>() : FieldBytes(255), mode == "split" ? 31 : 127, mode == "empty-chunk");
            return raw.WithRecord(record, tags);
        });
    }

    private static void FieldBinaryBoundary(DxfVersion version, bool binary, string mode)
    {
        var doc = BinarySource(version, binary, mode); var root = ResultRoot(doc); var child = root.Children[0];
        var old = child.Evaluation; Check(old != null, "qualified binary source did not project");
        var snapshot = old!; var before = child.Payload; var refs = child.References.ToArray();
        if (mode == "named")
        {
            var data = snapshot.Data.First(); var buffer = (DxfFieldBinaryValue)data.Value;
            Check(buffer.ToArray().SequenceEqual(FieldBytes(255)), "named binary data changed");
            var exported = buffer.ToArray(); exported[0] ^= 255;
            var physical = (byte[])data.Tags.First(t => t.Code == 310).Value; physical[0] ^= 255;
            doc.Objects.EvaluateFieldTree(root, input => input.Field == child ? new DxfFieldResult(1, "one") : input.ComposeText());
            Check(((DxfFieldBinaryValue)child.Evaluation.Data[0].Value).Equals(buffer), "result update changed named binary data");
            Check(data.Tags.SequenceEqual(child.Evaluation.Data[0].Tags), "result update resegmented named data");
        }
        else if (mode is "split" or "empty-chunk" or "retained")
        {
            doc.Objects.EvaluateFieldTree(root, input => DxfFieldResult.Failure(input.Evaluation, DxfFieldResultStatus.OtherError, 7, "retain"));
            int cache = before.ToList().FindIndex(t => t.Code == 7);
            Check(before.Skip(cache).SequenceEqual(child.Payload.Skip(cache)), "retained failure changed binary segmentation or padding");
            Equal(0, doc.Objects.EvaluateFieldTree(root, input => DxfFieldResult.Failure(input.Evaluation, DxfFieldResultStatus.OtherError, 7, "retain")), "same retained binary failure not no-op");
        }
        else
        {
            object? value = mode == "to-null" ? null : mode == "to-scalar" ? "text" : FieldBytes(mode == "maximum" ? DxfFieldBinaryValue.MaximumLength : 128);
            doc.Objects.EvaluateFieldTree(root, input => mode == "fallback"
                ? DxfFieldResult.FailureWithValue(DxfFieldResultStatus.InvalidCode, 9, "fallback", value!, "replacement")
                : input.Field == child ? new DxfFieldResult(value!, "replacement") : input.ComposeText());
            if (value is byte[] bytes) Check(((DxfFieldBinaryValue)child.Evaluation.Value).ToArray().SequenceEqual(bytes), "binary fallback/max bytes");
            else Equal(value, child.Evaluation.Value, "binary to other scalar kind");
        }
        Check(refs.SequenceEqual(child.References), "binary boundary changed graph membership");
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "binary boundary output"); output.Position = 0;
        var again = ResultRoot(DxfDocument.Load(output)!).Children[0];
        Equal(child.Evaluation.Value, again.Evaluation.Value, "binary boundary reload");
    }

    private static void FieldBinaryRejected(DxfVersion version, bool binary, string fault)
    {
        var doc = FieldResultDocument(version, binary, raw =>
        {
            var record = StoredFieldRecord(raw, "14F"); var tags = record.Tags.ToList();
            int marker = tags.FindIndex(t => t.Code == (fault == "named-count" ? 6 : 7));
            int kind = tags.FindIndex(marker + 1, t => t.Code == 90);
            PutFieldBinary(tags, kind, FieldBytes(3));
            if (fault == "negative") tags[kind + 1] = new DxfTag(92, -1);
            if (fault == "huge") tags[kind + 1] = new DxfTag(92, int.MaxValue);
            if (fault == "short") tags[kind + 1] = new DxfTag(92, 2);
            if (fault == "long" || fault == "named-count") tags[kind + 1] = new DxfTag(92, 4);
            if (fault == "no-bytes") tags.RemoveAt(kind + 2);
            if (fault == "missing-size") tags.RemoveAt(kind + 1);
            if (fault == "duplicate-size") tags.Insert(kind + 2, new DxfTag(92, 3));
            if (fault == "date-is-not-buffer") tags[kind] = new DxfTag(90, 8);
            if (fault == "extra-chunk") tags.Insert(kind + 3, new DxfTag(310, new byte[] { 0 }));
            if (fault == "zero-chunk") tags.Insert(kind + 2, new DxfTag(310, Array.Empty<byte>()));
            return raw.WithRecord(record, tags);
        });
        var root = ResultRoot(doc); var child = root.Children[0]; var before = child.Payload;
        Check(child.Evaluation == null, "malformed/unsupported binary projected: " + fault);
        int calls = 0;
        Throws<NotSupportedException>(() => doc.Objects.EvaluateFieldTree(root, _ => { calls++; return new DxfFieldResult(1, "one"); }));
        Equal(0, calls, "malformed binary reached evaluator");
        Check(ReferenceEquals(before, child.Payload), "malformed binary was discarded");
    }

    private static void FieldBinaryForestBudget(bool binary, int count)
    {
        var doc = FieldResultDocument(DxfVersion.AutoCad2018, binary, raw =>
        {
            var leaf = StoredFieldRecord(raw, "14F"); var source = leaf.Tags.ToList(); var additional = new List<DxfTag>();
            for (int level = 2; level <= count; level++)
            {
                string handle = level == 2 ? "14F" : (0xB100 + level).ToString("X");
                string owner = level == 2 ? "14E" : level == 3 ? "14F" : (0xB100 + level - 1).ToString("X");
                var tags = source.Select(t => t.Code == 5 ? new DxfTag(5, handle) : t).ToList();
                int subclass = tags.FindIndex(t => t.Code == 100);
                for (int i = 0; i < subclass; i++) if (tags[i].Code == 330) tags[i] = new DxfTag(330, owner);
                int children = tags.FindIndex(subclass, t => t.Code == 90);
                if (level < count)
                { tags[children] = new DxfTag(90, 1); tags.Insert(children + 1, new DxfTag(360, (0xB100 + level + 1).ToString("X"))); }
                if (level == 2) raw = raw.WithRecord(leaf, tags); else additional.AddRange(tags);
            }
            var all = raw.Tags.ToList(); int objects = all.FindIndex(t => t.Code == 2 && Equals(t.Value, "OBJECTS"));
            int end = all.FindIndex(objects + 1, t => t.Code == 0 && Equals(t.Value, "ENDSEC"));
            all.InsertRange(end, additional); return raw.WithTags(all);
        });
        var root = ResultRoot(doc); var before = doc.Objects.Items.OfType<DxfStoredField>().ToDictionary(f => f, f => f.Payload);
        var value = new DxfFieldResult(new DxfFieldBinaryValue(FieldBytes(DxfFieldBinaryValue.MaximumLength)), "binary"); int calls = 0;
        int Evaluate() => doc.Objects.EvaluateFieldTree(root, _ => { calls++; return value; });
        if (count == 16) Equal(count, Evaluate(), "exact forest binary budget rejected");
        else
        {
            Throws<ArgumentException>(() => Evaluate()); Equal(17, calls, "forest binary bound checked at wrong node");
            Check(before.All(p => ReferenceEquals(p.Value, p.Key.Payload)), "binary budget partially committed earlier nodes");
            Equal(count, doc.Objects.EvaluateFieldTree(root, _ => new DxfFieldResult(1, "one")), "binary budget failure poisoned guard");
        }
    }

    private static void FieldBinaryProjectionBudget()
    {
        var doc = FieldResultDocument(DxfVersion.AutoCad2018, false, raw =>
        {
            var record = StoredFieldRecord(raw, "14F"); var tags = record.Tags.ToList();
            int count = tags.FindIndex(t => t.Code == 93); int cache = tags.FindIndex(t => t.Code == 7);
            tags.RemoveRange(count + 1, cache - count - 1); tags[count] = new DxfTag(93, 5);
            var data = new List<DxfTag>(); byte[] bytes = FieldBytes(DxfFieldBinaryValue.MaximumLength);
            for (int i = 0; i < 5; i++)
            {
                data.Add(new DxfTag(6, "buffer" + i)); data.Add(new DxfTag(93, 0));
                var scalar = new List<DxfTag> { new(90, 0), new(91, 0) }; PutFieldBinary(scalar, 0, bytes);
                data.AddRange(scalar); data.AddRange(new[] { new DxfTag(94, 0), new DxfTag(300, ""), new DxfTag(302, ""), new DxfTag(304, "ACVALUE_END") });
            }
            tags.InsertRange(count + 1, data); return raw.WithRecord(record, tags);
        });
        var child = ResultRoot(doc).Children[0];
        Check(child.Evaluation == null && child.Payload.Any(t => t.Code == 310), "oversized total binary projection not preserved/guarded");
    }
}
