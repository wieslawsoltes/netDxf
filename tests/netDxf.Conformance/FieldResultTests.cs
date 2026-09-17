using netDxf;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly string[] FieldResultKinds = { "empty", "integer", "double", "string" };
    private static DxfFieldResult ResultForKind(string kind) => kind switch
    {
        "empty" => new(null!, "empty"),
        "integer" => new(42, "42 units"),
        "double" => new(-12.5, "-12.500", "inner numeric display"),
        _ => new(@"Literal \U+0041 Ω 😀", @"Literal \U+0041 Ω 😀")
    };
    private static void RegisterFieldResultTests()
    {
        Run("field-results/constructors", FieldResultConstructors);
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        {
            foreach (string kind in FieldResultKinds)
                Run($"field-results/roundtrip/{version}/{binary}/{kind}", () => FieldResultRoundTrip(version, binary, kind));
            foreach (string mode in new[] { "formula", "no-op", "empty-transaction", "text-markers", "negative-zero" })
                Run($"field-results/operation/{version}/{binary}/{mode}", () => FieldResultOperation(version, binary, mode));
        }
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad2000, DxfVersion.AutoCad2018 })
        foreach (bool binary in new[] { false, true })
        {
            foreach (string fault in new[] { "null-input", "null-edit", "duplicate", "foreign", "stale", "ancestor", "enumeration", "disposal", "reentry", "profile", "owner", "late-callback", "null-result", "nested-evaluate", "nested-apply", "child-root", "null-root", "null-evaluator", "bad-context", "disabled", "late-unsupported", "encoded-limit", "text-budget" })
                Run($"field-results/reject/{version}/{binary}/{fault}", () => FieldResultRejection(version, binary, fault));
            foreach (string fault in new[] { "state-missing", "state-duplicate", "named-count", "named-kind", "cache-kind", "cache-scalar", "display-length", "trailing", "invalid-utf16", "null-padding" })
                Run($"field-results/projection/{version}/{binary}/{fault}", () => FieldResultProjection(version, binary, fault));
            foreach (int count in new[] { 249, 250, 251, 499, 500 })
                Run($"field-results/chunks/{version}/{binary}/{count}", () => FieldResultChunks(version, binary, count));
        }
    }
    private static DxfDocument FieldResultDocument(DxfVersion version, bool binary, Func<DxfRawDocument, DxfRawDocument>? mutate = null)
    {
        // Only the AC1015 and AC1032 inputs are producer files. Intermediate profiles
        // are explicitly constructed carriers of their corresponding compact/modern grammar.
        var source = StoredFieldCarrier(version < DxfVersion.AutoCad2007 ? DxfVersion.AutoCad2000 : DxfVersion.AutoCad2018, binary, out _);
        using var bytes = new MemoryStream(); Check(source.Save(bytes, binary), "FIELD base carrier save"); bytes.Position = 0;
        var raw = DxfRawDocument.Load(bytes); var tags = raw.Tags.ToList();
        int index = tags.FindIndex(t => t.Code == 9 && Equals(t.Value, "$ACADVER"));
        string profile = version switch { DxfVersion.AutoCad2000 => "AC1015", DxfVersion.AutoCad2004 => "AC1018", DxfVersion.AutoCad2007 => "AC1021", DxfVersion.AutoCad2010 => "AC1024", DxfVersion.AutoCad2013 => "AC1027", _ => "AC1032" };
        // Construct an explicit synthetic source profile; immutable raw edits must not bypass version guards.
        tags[index + 1] = new DxfTag(1, profile); raw = DxfRawDocument.Create(tags, binary);
        return StoredFieldLoad(mutate == null ? raw : mutate(raw), binary);
    }
    private static DxfStoredField ResultRoot(DxfDocument doc) => (DxfStoredField)doc.GetObjectByHandle("14E");
    private static void SaveFieldResults(DxfDocument doc, bool binary, string name)
    {
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "FIELD results save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, name), output.ToArray());
    }
    private static void FieldResultRoundTrip(DxfVersion version, bool binary, string kind)
    {
        var doc = FieldResultDocument(version, binary); var root = ResultRoot(doc); var child = root.Children.Single();
        Check(doc.Objects.Items.OfType<DxfStoredField>().All(f => f.Evaluation != null), "native cache not projected");
        var originals = doc.Objects.Items.OfType<DxfStoredField>().ToDictionary(f => f, f => f.Payload);
        var snapshots = originals.Keys.ToDictionary(f => f, f => f.Evaluation); var refs = originals.Keys.ToDictionary(f => f, f => f.References.ToArray());
        string suffix = $"{version}-{binary}-{kind}.dxf";
        SaveFieldResults(doc, binary, "field-results-before-" + suffix); long seed = StoredFieldSeed(doc);
        var result = ResultForKind(kind); var calls = new List<string>();
        int changed = doc.Objects.EvaluateFieldTree(root, input =>
        {
            calls.Add(input.Field.Handle);
            Check(originals.All(pair => ReferenceEquals(pair.Key.Payload, pair.Value)), "result published during callback");
            return input.Field == child ? result : input.ComposeText();
        });
        Equal(2, changed, "field and ancestor cached result count");
        Check(calls.SequenceEqual(new[] { child.Handle, root.Handle }), "not child-first exactly once");
        Equal(seed, StoredFieldSeed(doc), "FIELD evaluation allocated handles");
        Equal(result.Value, child.Evaluation.Value, "typed result"); Equal(result.FormattedText, root.Evaluation.Value, "text parent result");
        foreach (var field in new[] { root, child })
        {
            var before = snapshots[field]; var after = field.Evaluation;
            Equal((before.StoredState & ~4) | 56, after.StoredState, "cached state"); Equal(2, after.StoredStatus, "success status");
            Equal(0, after.StoredErrorCode, "stale evaluator error"); Equal("", after.ErrorMessage, "stale error message");
            Equal(before.StoredEvaluationOptions, after.StoredEvaluationOptions, "options changed");
            Equal(before.StoredValueFlags, after.StoredValueFlags, "AcValue flags changed");
            Equal(before.FormatString, after.FormatString, "format controls changed");
            Check(before.Data.SelectMany(v => v.Tags).SequenceEqual(after.Data.SelectMany(v => v.Tags)), "evaluator data changed");
            Check(refs[field].SequenceEqual(field.References), "dependency graph changed");
            Check(before.Value == null, "old cache snapshot mutated");
        }
        Check(originals.Where(p => p.Key != root && p.Key != child).All(p => ReferenceEquals(p.Key.Payload, p.Value)), "unselected tree changed");
        var current = root.Payload; var childCurrent = child.Payload;
        Equal(0, doc.Objects.EvaluateFieldTree(root, input => input.Field == child ? result : input.ComposeText()), "repeat result not no-op");
        Check(ReferenceEquals(current, root.Payload) && ReferenceEquals(childCurrent, child.Payload), "no-op replaced snapshots");
        SaveFieldResults(doc, binary, "field-results-after-" + suffix);
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "opposite FIELD output"); output.Position = 0;
        var again = DxfDocument.Load(output)!; var reloaded = ResultRoot(again);
        Equal(root.Evaluation.FormattedText, reloaded.Evaluation.FormattedText, "parent display reload");
        Equal(result.Value, reloaded.Children.Single().Evaluation.Value, "cache kind/value reload");
        Equal(0, again.Objects.Validate().Count, "reloaded FIELD graph invalid");
        Check(!doc.Entities.Remove(doc.Entities.Lines.First()), "FIELD host removal protection lost");
    }
    private static void FieldResultOperation(DxfVersion version, bool binary, string mode)
    {
        var doc = FieldResultDocument(version, binary); var root = ResultRoot(doc); var child = root.Children.Single();
        if (mode == "empty-transaction")
        { var p = root.Payload; Equal(0, doc.Objects.ApplyFieldResults(Array.Empty<DxfFieldResultEdit>()), "empty transaction"); Check(ReferenceEquals(p, root.Payload), "empty changed payload"); return; }
        if (mode == "formula")
        {
            doc.Objects.EvaluateFieldTree(root, input => input.Field == child
                ? new DxfFieldResult(DxfTableFormula.Parse("=SUM(A1:A3)*2").Evaluate(3, 1, a => a.Row + 1), "12")
                : input.ComposeText());
            Equal(12.0, child.Evaluation.Value, "host formula evaluator result"); Equal("12", root.Evaluation.Value, "parent after formula");
        }
        else
        {
            var result = mode == "negative-zero" ? new DxfFieldResult(-0.0, "0") : new DxfFieldResult("%<\\_FldIdx 99>%", "%<\\_FldIdx 99>%");
            var requests = new[] { root.Evaluation.WithResult(new DxfFieldResult(result.FormattedText, result.FormattedText)), child.Evaluation.WithResult(result) };
            Equal(2, doc.Objects.ApplyFieldResults(requests), "explicit batch");
            if (mode == "negative-zero") SameDoubleBits(-0.0, (double)child.Evaluation.Value, "cache negative zero");
            else Equal(result.FormattedText, root.Evaluation.Value, "child text was executed");
            var current = root.Payload;
            Equal(0, doc.Objects.EvaluateFieldTree(root, input => input.Field == child ? result : input.ComposeText()), "composed repeat");
            Check(ReferenceEquals(current, root.Payload), "no-op changed root snapshot");
        }
    }
    private static void FieldResultRejection(DxfVersion version, bool binary, string fault)
    {
        var doc = FieldResultDocument(version, binary, raw =>
        {
            if (fault != "disabled" && fault != "late-unsupported") return raw;
            var record = StoredFieldRecord(raw, fault == "disabled" ? "14F" : "14E"); var tags = record.Tags.ToList();
            if (fault == "disabled") tags[tags.FindIndex(t => t.Code == 91)] = new DxfTag(91, 0);
            else tags.Add(new DxfTag(99, 123));
            return raw.WithRecord(record, tags);
        });
        var root = ResultRoot(doc); var child = root.Children.Single();
        var result = ResultForKind("integer");
        var originalRoot = root.Evaluation; var originalChild = child.Evaluation;
        if (fault == "stale") doc.Objects.EvaluateFieldTree(root, input => input.Field == child ? ResultForKind("double") : input.ComposeText());
        var before = doc.Objects.Items.OfType<DxfStoredField>().ToDictionary(f => f, f => f.Payload); long seed = StoredFieldSeed(doc);
        IEnumerable<DxfFieldResultEdit> Edits()
        {
            try
            {
                if (fault == "reentry") Throws<InvalidOperationException>(() => doc.Objects.ApplyFieldResults(Array.Empty<DxfFieldResultEdit>()));
                if (fault == "foreign")
                { var foreign = FieldResultDocument(version, binary); yield return ResultRoot(foreign).Evaluation.WithResult(result); yield break; }
                if (fault != "ancestor") yield return fault == "null-edit" ? null! : originalRoot.WithResult(result);
                if (fault == "duplicate") yield return originalRoot.WithResult(result);
                yield return originalChild.WithResult(result);
                if (fault == "enumeration") throw new InvalidOperationException("enumeration failure");
                if (fault == "profile") doc.DrawingVariables.AcadVer = version == DxfVersion.AutoCad2000 ? DxfVersion.AutoCad2018 : DxfVersion.AutoCad2000;
                if (fault == "owner") ((DxfDictionary)root.Owner).Remove("TEXT");
            }
            finally { if (fault == "disposal") throw new InvalidOperationException("disposal failure"); }
        }
        Exception? error = null;
        try
        {
            if (new[] { "null-input", "null-edit", "duplicate", "foreign", "stale", "ancestor", "enumeration", "disposal", "reentry", "profile", "owner" }.Contains(fault))
                doc.Objects.ApplyFieldResults(fault == "null-input" ? null! : Edits());
            else doc.Objects.EvaluateFieldTree(fault == "child-root" ? child : fault == "null-root" ? null! : root,
                fault == "null-evaluator" ? null! : input =>
                {
                    if (fault == "nested-apply") Throws<InvalidOperationException>(() => doc.Objects.ApplyFieldResults(Array.Empty<DxfFieldResultEdit>()));
                    if (fault == "nested-evaluate") Throws<InvalidOperationException>(() => doc.Objects.EvaluateFieldTree(root, _ => result));
                    if (fault == "late-callback" && input.Field == root) throw new InvalidOperationException("late parent failure");
                    if (fault == "null-result") return null!;
                    if (fault == "encoded-limit") return new DxfFieldResult(new string('\\', 149797), "encoded overflow");
                    if (fault == "text-budget") return new DxfFieldResult(new string('x', 1048576), new string('x', 1048576));
                    return input.Field == child ? result : input.ComposeText();
                }, fault == "bad-context" ? 0 : 32);
        }
        catch (Exception caught) { error = caught; }
        Check(error is ArgumentException or InvalidOperationException or NotSupportedException, "invalid FIELD operation accepted: " + fault);
        Check(before.All(p => ReferenceEquals(p.Value, p.Key.Payload)), "rejected operation partially published");
        Equal(seed, StoredFieldSeed(doc), "rejected FIELD operation allocated handles");
        doc.DrawingVariables.AcadVer = version;
        if (fault == "owner") ((DxfDictionary)root.Owner).Add("TEXT", root);
        if (fault != "late-unsupported" && fault != "disabled")
            doc.Objects.EvaluateFieldTree(root, input => input.Field == child ? result : input.ComposeText());
    }
    private static void FieldResultProjection(DxfVersion version, bool binary, string fault)
    {
        var doc = FieldResultDocument(version, binary, raw =>
        {
            var record = StoredFieldRecord(raw, "14F"); var tags = record.Tags.ToList();
            int cache = tags.FindIndex(t => t.Code == 7); int data = tags.FindIndex(t => t.Code == 6);
            int kind = tags.FindIndex(cache + 1, t => t.Code == 90);
            int state = tags.FindIndex(t => t.Code == 94);
            if (fault == "state-missing") tags.RemoveAt(state);
            if (fault == "state-duplicate") tags.Insert(state, tags[state]);
            if (fault == "named-count") tags[data - 1] = new DxfTag(93, int.MaxValue);
            if (fault == "named-kind") tags[tags.FindIndex(data + 1, t => t.Code == 90)] = new DxfTag(90, 128);
            if (fault == "cache-kind") tags[kind] = new DxfTag(90, 128);
            if (fault == "cache-scalar") { tags[kind] = new DxfTag(90, 1); tags[kind + 1] = new DxfTag(140, 1.0); }
            if (fault == "display-length") tags[tags.FindLastIndex(t => t.Code == 98)] = new DxfTag(98, 99);
            if (fault == "trailing") tags.Add(new DxfTag(99, 3));
            if (fault == "invalid-utf16") tags[tags.FindLastIndex(t => t.Code == 301)] = new DxfTag(301, @"\U+D800");
            if (fault == "null-padding") { tags[kind] = new DxfTag(90, 0); tags[kind + 1] = new DxfTag(91, 1); }
            return raw.WithRecord(record, tags);
        });
        var field = (DxfStoredField)doc.GetObjectByHandle("14F");
        Check(field.Evaluation == null, "unqualified cache projected: " + fault);
        Check(ResultRoot(doc).Evaluation != null, "other FIELD projection disabled");
        var before = field.Payload;
        Throws<NotSupportedException>(() => doc.Objects.EvaluateFieldTree(ResultRoot(doc), _ => ResultForKind("integer")));
        Check(ReferenceEquals(before, field.Payload), "unqualified FIELD mutated");
    }
    private static void FieldResultChunks(DxfVersion version, bool binary, int count)
    {
        var doc = FieldResultDocument(version, binary); var root = ResultRoot(doc); var child = root.Children.Single();
        string text = new string('x', count) + @"\U+0041 😀 Ω" + new string('y', count);
        doc.Objects.EvaluateFieldTree(root, input => input.Field == child ? new DxfFieldResult(text, text) : input.ComposeText());
        Check(root.Payload.Any(t => t.Code == 9), "display string not chunked");
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "chunked FIELD save"); output.Position = 0;
        var again = ResultRoot(DxfDocument.Load(output)!);
        Equal(text, again.Evaluation.Value, "scalar Unicode roundtrip"); Equal(text, again.Evaluation.FormattedText, "chunked Unicode roundtrip");
        Equal(text.Length, (int)again.Payload.Last().Value, "UTF-16 display length");
    }
    private static void FieldResultConstructors()
    {
        foreach (object value in new object[] { 1L, true, new ArraySegment<byte>(new byte[] { 1 }), double.NaN, double.PositiveInfinity, "\0", "\ud800" })
            Throws<ArgumentException>(() => new DxfFieldResult(value, "display"));
        Throws<ArgumentException>(() => new DxfFieldResult(1, null!));
        Throws<ArgumentException>(() => new DxfFieldResult(1, "display", "\0"));
        Throws<ArgumentException>(() => new DxfFieldResult(new string('x', 1048577), "display"));
    }
}
