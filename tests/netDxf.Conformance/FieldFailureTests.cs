using netDxf;
using netDxf.Header;
using netDxf.Objects;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly DxfFieldResultStatus[] FieldFailureStatuses = {
        DxfFieldResultStatus.EvaluatorNotFound, DxfFieldResultStatus.SyntaxError,
        DxfFieldResultStatus.InvalidCode, DxfFieldResultStatus.InvalidContext, DxfFieldResultStatus.OtherError };
    private const string FailureMessage = "Evaluation Ω \\U+0041 😀";
    private static DxfStoredField[] FieldRoots(DxfDocument doc) => new[] { ResultRoot(doc), (DxfStoredField)doc.GetObjectByHandle("15E") };
    private static DxfFieldResult SuccessfulField(DxfFieldEvaluationInput input) =>
        input.Children.Count == 0 ? new DxfFieldResult(42, "retained") : input.ComposeText();

    private static void RegisterFieldFailureTests()
    {
        Run("field-results/failure-constructors", FieldFailureConstructors);
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        foreach (var status in FieldFailureStatuses)
        foreach (string policy in new[] { "retained-empty", "retained-populated", "fallback" })
            Run($"field-results/failure/{version}/{binary}/{status}/{policy}", () => FieldFailureRoundTrip(version, binary, status, policy));
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad2000, DxfVersion.AutoCad2018 })
        foreach (bool binary in new[] { false, true })
        foreach (string scenario in new[] { "foreign-cache", "stale-cache", "failed-child", "explicit-fallback", "error-encoding", "error-budget", "error-newline", "success-clears-error" })
            Run($"field-results/failure-boundary/{version}/{binary}/{scenario}", () => FieldFailureBoundary(version, binary, scenario));
        foreach (bool binary in new[] { false, true })
        foreach (string scenario in new[] { "ordered", "reverse", "empty", "null", "null-entry", "duplicate", "child-root", "foreign", "enumeration", "disposal", "reentry", "late-unsupported", "late-disabled", "late-callback", "late-null", "late-encoding", "callback-profile", "nested-forest" })
            Run($"field-results/forest/{binary}/{scenario}", () => FieldForestBoundary(binary, scenario));
    }

    private static void FieldFailureRoundTrip(DxfVersion version, bool binary, DxfFieldResultStatus status, string policy)
    {
        var doc = FieldResultDocument(version, binary); var roots = FieldRoots(doc);
        if (policy != "retained-empty") Equal(4, doc.Objects.EvaluateFieldTrees(roots, SuccessfulField), "populate both trees");
        var fields = doc.Objects.Items.OfType<DxfStoredField>().ToArray();
        var payloads = fields.ToDictionary(f => f, f => f.Payload);
        var snapshots = fields.ToDictionary(f => f, f => f.Evaluation);
        var references = fields.ToDictionary(f => f, f => f.References.ToArray());
        string suffix = $"{version}-{binary}-{status}-{policy}.dxf";
        SaveFieldResults(doc, binary, "field-failures-before-" + suffix); long seed = StoredFieldSeed(doc);
        var calls = new List<string>();
        DxfFieldResult Failed(DxfFieldEvaluationInput input)
        {
            calls.Add(input.Field.Handle);
            Check(payloads.All(p => ReferenceEquals(p.Key.Payload, p.Value)), "failure published during callbacks");
            if (input.Children.Count != 0) Equal(status, input.Children[0].Status, "parent did not receive failed child outcome");
            return policy != "fallback" ? DxfFieldResult.Failure(input.Evaluation, status, -901, FailureMessage)
                : DxfFieldResult.FailureWithValue(status, -901, FailureMessage,
                    input.Children.Count == 0 ? (object)(-7) : "#ERR", "#ERR", "fallback display");
        }
        Equal(4, doc.Objects.EvaluateFieldTrees(roots, Failed), "four failed outcomes published atomically");
        Check(calls.SequenceEqual(new[] { "14F", "14E", "15F", "15E" }), "forest callback order");
        Equal(seed, StoredFieldSeed(doc), "failure publication allocated handles");
        foreach (var field in fields)
        {
            var before = snapshots[field]; var after = field.Evaluation;
            Equal((int)status, after.StoredStatus, "failure status"); Equal(-901, after.StoredErrorCode, "signed evaluator error");
            Equal(FailureMessage, after.ErrorMessage, "decoded error message");
            Equal((before.StoredState & ~4) | 8 | (policy == "fallback" ? 48 : 0), after.StoredState, "failure state/cache presence");
            Check(references[field].SequenceEqual(field.References), "failure changed dependency membership");
            Equal(before.FormatString, after.FormatString, "failure changed format expression");
            Check(before.Data.SelectMany(d => d.Tags).SequenceEqual(after.Data.SelectMany(d => d.Tags)), "failure changed named evaluator data");
            if (policy != "fallback")
            {
                int data = payloads[field].ToList().FindIndex(t => t.Code == 93);
                Check(payloads[field].Skip(data).SequenceEqual(field.Payload.Skip(data)), "retention rewrote cache, chunks, flags or named data");
                Equal(before.Value, after.Value, "retained scalar"); Equal(before.FormattedText, after.FormattedText, "retained display");
            }
            else Equal(field.Children.Count == 0 ? (object)(-7) : "#ERR", after.Value, "explicit fallback value");
        }
        SaveFieldResults(doc, binary, "field-failures-after-" + suffix);
        var current = fields.Select(f => f.Payload).ToArray();
        Equal(0, doc.Objects.EvaluateFieldTrees(roots, input => policy != "fallback"
            ? DxfFieldResult.Failure(input.Evaluation, status, -901, FailureMessage)
            : DxfFieldResult.FailureWithValue(status, -901, FailureMessage, input.Children.Count == 0 ? (object)(-7) : "#ERR", "#ERR", "fallback display")), "same failure is not a no-op");
        Check(current.SequenceEqual(fields.Select(f => f.Payload)), "same failure replaced snapshots");
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "opposite failure transport"); output.Position = 0;
        var loaded = DxfDocument.Load(output)!;
        foreach (var field in loaded.Objects.Items.OfType<DxfStoredField>())
        {
            Equal((int)status, field.Evaluation.StoredStatus, "failure status reload");
            Equal(FailureMessage, field.Evaluation.ErrorMessage, "error message reload");
            var source = (DxfStoredField)doc.GetObjectByHandle(field.Handle);
            Equal(source.Evaluation.Value, field.Evaluation.Value, "fallback/retained scalar reload");
        }
        Equal(0, loaded.Objects.Validate().Count, "failed outcome corrupted persistent graph");
    }

    private static void FieldFailureConstructors()
    {
        var doc = FieldResultDocument(DxfVersion.AutoCad2018, false); var original = ResultRoot(doc).Evaluation;
        foreach (int status in new[] { -1, 0, 1, 2, 3, 12, 128 })
        {
            Throws<ArgumentOutOfRangeException>(() => DxfFieldResult.Failure(original, (DxfFieldResultStatus)status, 0, "error"));
            Throws<ArgumentOutOfRangeException>(() => DxfFieldResult.FailureWithValue((DxfFieldResultStatus)status, 0, "error", null!, ""));
        }
        Throws<ArgumentNullException>(() => DxfFieldResult.Failure(null!, DxfFieldResultStatus.OtherError, 0, "error"));
        foreach (string? text in new[] { null, "bad\0text", "\ud800", new string('x', 1048577) })
            Throws<ArgumentException>(() => DxfFieldResult.Failure(original, DxfFieldResultStatus.OtherError, 0, text!));
        foreach (object value in new object[] { true, DateTime.MinValue, double.NaN, double.PositiveInfinity })
            Throws<ArgumentException>(() => DxfFieldResult.FailureWithValue(DxfFieldResultStatus.OtherError, 0, "", value, ""));
        var success = new DxfFieldResult(1, "one");
        Equal(DxfFieldResultStatus.Success, success.Status, "source-compatible success constructor");
        Equal(0, success.ErrorCode, "success error default"); Equal("", success.ErrorMessage, "success message default");
        Check(!success.RetainsCachedValue && DxfFieldResult.Failure(original, DxfFieldResultStatus.OtherError, 7, "").RetainsCachedValue, "explicit cache policy");
    }

    private static void FieldFailureBoundary(DxfVersion version, bool binary, string scenario)
    {
        var doc = FieldResultDocument(version, binary); var roots = FieldRoots(doc); var root = roots[0]; var child = root.Children[0];
        doc.Objects.EvaluateFieldTrees(roots, SuccessfulField);
        var stale = DxfFieldResult.Failure(child.Evaluation, DxfFieldResultStatus.OtherError, 5, "old");
        if (scenario == "stale-cache") doc.Objects.EvaluateFieldTree(root, input => input.Children.Count == 0 ? new DxfFieldResult(43, "new") : input.ComposeText());
        var before = doc.Objects.Items.OfType<DxfStoredField>().ToDictionary(f => f, f => f.Payload);
        long seed = StoredFieldSeed(doc);
        if (scenario == "success-clears-error")
        {
            doc.Objects.EvaluateFieldTrees(roots, input => DxfFieldResult.Failure(input.Evaluation, DxfFieldResultStatus.SyntaxError, 19, "bad"));
            Equal(4, doc.Objects.EvaluateFieldTrees(roots, SuccessfulField), "success did not clear failed metadata");
            Check(before.Keys.All(f => f.Evaluation.StoredStatus == 2 && f.Evaluation.StoredErrorCode == 0 && f.Evaluation.ErrorMessage == ""), "stale error survived success");
            return;
        }
        DxfFieldResult Evaluate(DxfFieldEvaluationInput input)
        {
            if (scenario == "foreign-cache" && input.Field == child)
                return DxfFieldResult.Failure(root.Evaluation, DxfFieldResultStatus.OtherError, 9, "wrong snapshot");
            if (scenario == "stale-cache" && input.Field == child) return stale;
            if (scenario == "error-encoding") return DxfFieldResult.Failure(input.Evaluation, DxfFieldResultStatus.OtherError, 9, new string('\\', 149797));
            if (scenario == "error-budget") return DxfFieldResult.Failure(input.Evaluation, DxfFieldResultStatus.OtherError, 9, new string('x', 1048576));
            if (scenario == "error-newline") return DxfFieldResult.Failure(input.Evaluation, DxfFieldResultStatus.OtherError, 9, "first\r\nsecond");
            if (input.Children.Count != 0 && scenario is "foreign-cache" or "stale-cache")
                return new DxfFieldResult("explicit parent", "explicit parent");
            if (input.Children.Count == 0) return DxfFieldResult.Failure(input.Evaluation, DxfFieldResultStatus.InvalidCode, 9, "child failed");
            return scenario == "explicit-fallback" ? new DxfFieldResult("host fallback", "host fallback") : input.ComposeText();
        }
        if (scenario is "explicit-fallback" or "error-newline")
        {
            Equal(4, doc.Objects.EvaluateFieldTrees(roots, Evaluate), "explicit outcome count");
            using var bytes = new MemoryStream();
            if (scenario == "error-newline" && !binary) { CheckSaveRejected(doc, bytes); Equal(0L, bytes.Length, "ASCII error newline wrote bytes"); }
            else { Check(doc.Save(bytes, binary), "error/fallback save"); bytes.Position = 0; Check(DxfDocument.Load(bytes) != null, "error/fallback reload"); }
        }
        else
        {
            Exception? error = null; try { doc.Objects.EvaluateFieldTrees(roots, Evaluate); } catch (Exception caught) { error = caught; }
            Check(error is ArgumentException or InvalidOperationException, "invalid failure result was accepted");
            Check(before.All(p => ReferenceEquals(p.Key.Payload, p.Value)), "late failure policy rejection partially published");
            Equal(seed, StoredFieldSeed(doc), "rejected failure allocated handles");
        }
    }

    private static void FieldForestBoundary(bool binary, string scenario)
    {
        var doc = FieldResultDocument(DxfVersion.AutoCad2018, binary, raw =>
        {
            if (scenario != "late-unsupported" && scenario != "late-disabled") return raw;
            var record = StoredFieldRecord(raw, "15E"); var tags = record.Tags.ToList();
            if (scenario == "late-unsupported") tags.Add(new netDxf.IO.DxfTag(99, 7));
            else tags[tags.FindIndex(t => t.Code == 91)] = new netDxf.IO.DxfTag(91, 0);
            return raw.WithRecord(record, tags);
        });
        var roots = FieldRoots(doc); var before = doc.Objects.Items.OfType<DxfStoredField>().ToDictionary(f => f, f => f.Payload);
        var calls = new List<string>(); long seed = StoredFieldSeed(doc); bool disposed = false;
        IEnumerable<DxfStoredField> Roots()
        {
            try
            {
                yield return scenario == "null-entry" ? null! : scenario == "child-root" ? roots[0].Children[0] : scenario == "reverse" ? roots[1] : roots[0];
                if (scenario == "enumeration") throw new InvalidOperationException("roots enumeration");
                if (scenario == "reentry") Throws<InvalidOperationException>(() => doc.Objects.ApplyFieldResults(Array.Empty<DxfFieldResultEdit>()));
                yield return scenario == "duplicate" ? roots[0] : scenario == "reverse" ? roots[0] : scenario == "foreign" ? ResultRoot(FieldResultDocument(DxfVersion.AutoCad2018, binary)) : roots[1];
            }
            finally { disposed = true; if (scenario == "disposal") throw new InvalidOperationException("roots disposal"); }
        }
        DxfFieldResult Evaluate(DxfFieldEvaluationInput input)
        {
            Check(disposed, "evaluator ran before roots disposal");
            calls.Add(input.Field.Handle); Check(before.All(p => ReferenceEquals(p.Key.Payload, p.Value)), "earlier tree published before later tree");
            if (scenario == "nested-forest") Throws<InvalidOperationException>(() => doc.Objects.EvaluateFieldTrees(roots, SuccessfulField));
            if (input.Field == roots[1])
            {
                if (scenario == "late-callback") throw new InvalidOperationException("last root failure");
                if (scenario == "late-null") return null!;
                if (scenario == "late-encoding") return new DxfFieldResult("value", new string('\\', 149797));
                if (scenario == "callback-profile") doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2000;
            }
            return SuccessfulField(input);
        }
        if (scenario == "empty")
        { Equal(0, doc.Objects.EvaluateFieldTrees(Array.Empty<DxfStoredField>(), _ => throw new Exception("unexpected callback")), "empty forest"); return; }
        if (scenario is "ordered" or "reverse")
        {
            Equal(4, doc.Objects.EvaluateFieldTrees(Roots(), Evaluate), "forest changed field count");
            Check(calls.SequenceEqual(scenario == "reverse" ? new[] { "15F", "15E", "14F", "14E" } : new[] { "14F", "14E", "15F", "15E" }), "forest order is not deterministic");
        }
        else
        {
            Exception? error = null; try { doc.Objects.EvaluateFieldTrees(scenario == "null" ? null! : Roots(), Evaluate); } catch (Exception caught) { error = caught; }
            Check(error is ArgumentException or InvalidOperationException or NotSupportedException, "invalid forest accepted");
            if (!scenario.StartsWith("late-", StringComparison.Ordinal) && scenario != "callback-profile" && scenario != "nested-forest" || scenario is "late-unsupported" or "late-disabled")
                Equal(0, calls.Count, "forest preflight failed after evaluator callbacks");
            Check(before.All(p => ReferenceEquals(p.Key.Payload, p.Value)), "forest failure partially published");
            doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2018;
        }
        Equal(seed, StoredFieldSeed(doc), "forest operation allocated handles");
        if (scenario != "late-unsupported" && scenario != "late-disabled") doc.Objects.EvaluateFieldTrees(roots, SuccessfulField);
    }
}
