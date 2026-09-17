using netDxf;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterFieldResultBoundaryTests()
    {
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad2000, DxfVersion.AutoCad2018 })
        foreach (bool binary in new[] { false, true })
        {
            foreach (string scenario in new[] { "escaped-code", "split-surrogate-escape", "repeated-children", "literal", "unknown", "missing-index", "negative-index", "range", "long-index", "unclosed", "unexpected-close", "field-delimiters", "empty" })
                Run($"field-results/text/{version}/{binary}/{scenario}", () => FieldTextComposition(version, binary, scenario));
            foreach (string scenario in new[] { "metadata", "immutability", "result-limit", "newline", "context-save", "context-open", "unknown-context", "signed-state", "null-edit-result" })
                Run($"field-results/boundary/{version}/{binary}/{scenario}", () => FieldResultBoundary(version, binary, scenario));
        }
        foreach (bool binary in new[] { false, true })
        foreach (int depth in new[] { DxfObjectDatabase.MaximumFieldEvaluationDepth, DxfObjectDatabase.MaximumFieldEvaluationDepth + 1 })
            Run($"field-results/depth/{binary}/{depth}", () => FieldResultDepth(binary, depth));
    }

    private static void FieldTextComposition(DxfVersion version, bool binary, string scenario)
    {
        string code = scenario switch
        {
            "escaped-code" => @"prefix \U+03A9 %<\_FldIdx 0>%", "split-surrogate-escape" => @"\U+D83D",
            "repeated-children" => @"%<\_FldIdx 0>% + %<\_FldIdx 0>%", "literal" => "literal 100% text",
            "unknown" => @"%<\AcVar Date>%", "missing-index" => @"%<\_FldIdx >%", "negative-index" => @"%<\_FldIdx -1>%",
            "range" => @"%<\_FldIdx 1>%", "long-index" => @"%<\_FldIdx 0000000>%", "unclosed" => @"%<\_FldIdx 0",
            "unexpected-close" => "a >% b", "field-delimiters" => @"%<\_FldIdx 0>%", _ => ""
        };
        var doc = FieldResultDocument(version, binary, raw =>
        {
            var record = StoredFieldRecord(raw, "14E"); var tags = record.Tags.ToList(); int at = tags.FindIndex(t => t.Code == 2);
            tags[at] = new DxfTag(2, code);
            if (scenario == "split-surrogate-escape") tags.Insert(at + 1, new DxfTag(3, @"\U+DE00 %<\_FldIdx 0>%"));
            return raw.WithRecord(record, tags);
        });
        var root = ResultRoot(doc); var child = root.Children.Single(); var payloads = new[] { root.Payload, child.Payload }; int calls = 0;
        Check(root.Evaluation != null, "split code disabled cache projection");
        string childText = scenario == "field-delimiters" ? @"%<\_FldIdx 99>% >%" : "result";
        bool good = new[] { "escaped-code", "split-surrogate-escape", "repeated-children", "literal", "field-delimiters", "empty" }.Contains(scenario);
        int Evaluate() => doc.Objects.EvaluateFieldTree(root, input =>
        {
            calls++; return input.Field == child ? new DxfFieldResult(42, childText) : input.ComposeText();
        });
        if (good)
        {
            Evaluate(); Equal(2, calls, "one callback per identity despite repeated child slots");
            string expected = scenario switch { "escaped-code" => "prefix Ω result", "split-surrogate-escape" => "😀 result", "repeated-children" => "result + result", "literal" => code, "field-delimiters" => childText, _ => "" };
            Equal(expected, root.Evaluation!.Value, "literal/child composition result");
            using var output = new MemoryStream(); Check(doc.Save(output, binary), "composed FIELD output"); output.Position = 0;
            Equal(expected, ResultRoot(DxfDocument.Load(output)!).Evaluation.Value, "composed result roundtrip");
        }
        else
        {
            Exception? error = null; try { Evaluate(); } catch (Exception caught) { error = caught; }
            Check(error is ArgumentException or FormatException or NotSupportedException, "malformed child code accepted");
            Check(ReferenceEquals(payloads[0], root.Payload) && ReferenceEquals(payloads[1], child.Payload), "failed composition published child result");
        }
    }

    private static void FieldResultBoundary(DxfVersion version, bool binary, string scenario)
    {
        var doc = FieldResultDocument(version, binary, raw =>
        {
            if (scenario != "signed-state") return raw;
            var record = StoredFieldRecord(raw, "14F"); var tags = record.Tags.ToList();
            int state = tags.FindIndex(t => t.Code == 94); tags[state] = new DxfTag(94, unchecked((int)0x80000004));
            return raw.WithRecord(record, tags);
        });
        var root = ResultRoot(doc); var child = root.Children.Single(); var prior = root.Payload;
        var result = new DxfFieldResult(3.5, "3.500");
        if (scenario == "null-edit-result")
        { Throws<ArgumentNullException>(() => root.Evaluation.WithResult(null!)); return; }
        if (scenario == "immutability")
        {
            Throws<NotSupportedException>(() => ((IList<DxfFieldDataValue>)root.Evaluation.Data).Clear());
            Throws<NotSupportedException>(() => ((IList<DxfTag>)root.Evaluation.Data[0].Tags).Clear());
            doc.Objects.EvaluateFieldTree(root, input =>
            {
                Throws<NotSupportedException>(() => ((IList<DxfFieldResult>)input.Children).Clear());
                return input.Field == child ? result : input.ComposeText();
            });
            return;
        }
        if (scenario == "result-limit")
        {
            int consumed = 0; bool disposed = false; long seed = StoredFieldSeed(doc);
            IEnumerable<DxfFieldResultEdit> Input()
            { try { while (true) { consumed++; yield return root.Evaluation.WithResult(result); } } finally { disposed = true; } }
            Throws<ArgumentException>(() => doc.Objects.ApplyFieldResults(Input()));
            Check(disposed && consumed == DxfObjectDatabase.MaximumFieldResultCount + 1, "result enumeration not bounded/disposed");
            Check(ReferenceEquals(prior, root.Payload), "limit published FIELD results"); Equal(seed, StoredFieldSeed(doc), "limit allocated handles");
            return;
        }
        if (scenario == "unknown-context")
        { Throws<ArgumentOutOfRangeException>(() => doc.Objects.EvaluateFieldTree(root, _ => result, 64)); return; }
        if (scenario == "metadata")
        {
            root.XData.Add(new XData(new netDxf.Tables.ApplicationRegistry("RESULT_METADATA")) { XDataRecord = { new XDataRecord(XDataCode.String, "retained") } });
            var extra = new DxfDictionary(); extra.Add("metadata", new DxfXRecord()); doc.Objects.SetExtensionDictionary(root, extra);
        }
        var dictionary = root.ExtensionDictionary; var reactors = root.PersistentReactors.ToArray();
        if (scenario == "newline") result = new DxfFieldResult("a\r\nb", "a\r\nb");
        doc.Objects.EvaluateFieldTree(root, input => input.Field == child ? result : input.ComposeText(), scenario == "context-save" ? 2 : scenario == "context-open" ? 1 : 32);
        if (scenario == "signed-state") Equal(unchecked((int)0x80000038), child.Evaluation.StoredState, "unknown state bits changed");
        Check(ReferenceEquals(dictionary, root.ExtensionDictionary) && reactors.SequenceEqual(root.PersistentReactors), "FIELD common metadata changed");
        using var bytes = new MemoryStream();
        if (scenario == "newline" && !binary) { CheckSaveRejected(doc, bytes); Equal(0L, bytes.Length, "newline rejection wrote bytes"); return; }
        Check(doc.Save(bytes, binary), "boundary result save"); bytes.Position = 0;
        var again = ResultRoot(DxfDocument.Load(bytes)!);
        Equal(root.Evaluation.FormattedText, again.Evaluation.FormattedText, "boundary display roundtrip");
        if (scenario == "metadata") Equal("retained", again.XData["RESULT_METADATA"].XDataRecord[0].Value, "FIELD XData lost");
    }

    private static void FieldResultDepth(bool binary, int depth)
    {
        // Extend only one native owner tree with explicitly synthetic FIELD records.
        var doc = FieldResultDocument(DxfVersion.AutoCad2018, binary, raw =>
        {
            var leaf = StoredFieldRecord(raw, "14F"); var source = leaf.Tags.ToList();
            var nodes = new List<DxfTag>();
            for (int level = 2; level <= depth; level++)
            {
                string handle = level == 2 ? "14F" : (0xA100 + level).ToString("X");
                string owner = level == 2 ? "14E" : level == 3 ? "14F" : (0xA100 + level - 1).ToString("X");
                var tags = source.Select(t => t.Code == 5 ? new DxfTag(5, handle) : t).ToList();
                int subclass = tags.FindIndex(t => t.Code == 100);
                for (int i = 0; i < subclass; i++) if (tags[i].Code == 330) tags[i] = new DxfTag(330, owner);
                int evaluator = subclass + 1; tags[evaluator] = new DxfTag(1, level == depth ? "HostResult" : "_text");
                tags[evaluator + 1] = new DxfTag(2, level == depth ? "value" : @"%<\_FldIdx 0>%");
                int count = tags.FindIndex(subclass, t => t.Code == 90);
                if (level < depth)
                { tags[count] = new DxfTag(90, 1); tags.Insert(count + 1, new DxfTag(360, (0xA100 + level + 1).ToString("X"))); }
                if (level == 2) raw = raw.WithRecord(leaf, tags); else nodes.AddRange(tags);
            }
            var all = raw.Tags.ToList(); int objects = all.FindIndex(t => t.Code == 2 && Equals(t.Value, "OBJECTS"));
            int end = all.FindIndex(objects + 1, t => t.Code == 0 && Equals(t.Value, "ENDSEC"));
            all.InsertRange(end, nodes); return raw.WithTags(all);
        });
        var root = ResultRoot(doc); var before = doc.Objects.Items.OfType<DxfStoredField>().ToDictionary(f => f, f => f.Payload);
        int calls = 0;
        int Evaluate() => doc.Objects.EvaluateFieldTree(root, input =>
        { calls++; return input.Children.Count == 0 ? new DxfFieldResult(7, "seven") : input.ComposeText(); });
        if (depth > DxfObjectDatabase.MaximumFieldEvaluationDepth)
        {
            Throws<InvalidOperationException>(() => Evaluate()); Equal(0, calls, "depth check occurred after callbacks");
            Check(before.All(p => ReferenceEquals(p.Key.Payload, p.Value)), "depth overflow partially published");
        }
        else
        {
            Equal(depth, Evaluate(), "deep tree result count"); Equal(depth, calls, "deep tree callback count");
            Equal("seven", root.Evaluation.Value, "deep composition result");
            using var bytes = new MemoryStream(); Check(doc.Save(bytes, binary), "deep FIELD graph save"); bytes.Position = 0;
            Equal("seven", ResultRoot(DxfDocument.Load(bytes)!).Evaluation.Value, "deep FIELD graph reload");
        }
    }
}
