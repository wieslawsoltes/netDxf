using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using DxfAttribute = netDxf.Entities.Attribute;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly string[] FieldHostKinds = { "TEXT", "MTEXT", "ATTRIB", "ATTDEF" };
    private const string HostLiteral = "FIELD Ω {braces} \\path 😀";

    private static void RegisterFieldTextHostTests()
    {
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        foreach (string kind in FieldHostKinds)
            Run($"field-host/roundtrip/{version}/{binary}/{kind}", () => FieldHostRoundTrip(version, binary, kind));
        foreach (bool binary in new[] { false, true })
        foreach (string kind in FieldHostKinds)
        {
            foreach (string fault in new[] { "late-callback", "late-null", "reentry", "host-mutation", "proxy-mutation", "literal-symbol", "literal-field", "literal-close", "literal-unicode", "literal-unicode-lower", "tab", "wrong-slot", "soft-slot", "foreign", "duplicate" })
                Run($"field-host/atomic/{binary}/{kind}/{fault}", () => FieldHostAtomic(binary, kind, fault));
            foreach (string mode in new[] { "failed", "cache-only", "host-only", "case", "newline" })
                Run($"field-host/policy/{binary}/{kind}/{mode}", () => FieldHostPolicy(binary, kind, mode));
        }
        foreach (bool binary in new[] { false, true })
        foreach (string kind in FieldHostKinds)
        foreach (string mode in new[] { "amount", "date", "angle", "unknown" })
            Run($"field-host/standard-provider/{binary}/{kind}/{mode}", () => FieldHostProvider(binary, kind, mode));
        foreach (bool binary in new[] { false, true })
        foreach (string mode in new[] { "unsupported", "columns", "linked", "null-roots", "null-evaluator", "null-batch", "empty", "invalid-context", "parent-proxy" })
            Run($"field-host/admission/{binary}/{mode}", () => FieldHostAdmission(binary, mode));
    }

    private static DxfObject FindFieldHost(DxfDocument doc, string handle) => doc.GetObjectByHandle(handle)
        ?? doc.Blocks.SelectMany(block => block.Entities).OfType<Insert>()
            .SelectMany(insert => insert.Attributes).Single(attribute => attribute.Handle == handle);

    private static string HostText(DxfObject host) => host switch
    { Text text => text.Value, MText text => text.Value, DxfAttribute attr => attr.Value, AttributeDefinition attr => attr.Value, _ => throw new NotSupportedException() };
    private static void HostText(DxfObject host, string text)
    {
        switch (host)
        { case Text item: item.Value = text; break; case MText item: item.Value = text; break; case DxfAttribute item: item.Value = text; break; case AttributeDefinition item: item.Value = text; break; default: throw new NotSupportedException(); }
    }
    private static byte[]? HostProxy(DxfObject host) => host switch
    { EntityObject item => item.ProxyGraphics, DxfAttribute item => item.ProxyGraphics, AttributeDefinition item => item.ProxyGraphics, _ => throw new NotSupportedException() };
    private static void HostProxy(DxfObject host, byte[] bytes)
    {
        switch (host)
        { case EntityObject item: item.ProxyGraphics = bytes; break; case DxfAttribute item: item.ProxyGraphics = bytes; break; case AttributeDefinition item: item.ProxyGraphics = bytes; break; default: throw new NotSupportedException(); }
    }
    private static string ExpectedHost(string value, string kind) => kind == "MTEXT"
        ? value.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}").Replace("\r\n", "\\P").Replace("\r", "\\P").Replace("\n", "\\P") : value;
    private static DxfFieldResult HostSuccess(DxfFieldEvaluationInput input) => input.Children.Count == 0
        ? new DxfFieldResult(42, HostLiteral) : input.ComposeText();

    private static DxfDocument FieldHostDocument(DxfVersion version, bool binary, string kind)
    {
        // Preserve the existing pinned compact/modern FIELD graph. Actual text hosts
        // replace the prior synthetic LINE hosts; these are declared synthetic carriers.
        var fieldDoc = FieldResultDocument(version, binary);
        using var sourceBytes = new MemoryStream(); Check(fieldDoc.Save(sourceBytes, binary), "source FIELD carrier"); sourceBytes.Position = 0;
        var source = DxfRawDocument.Load(sourceBytes);
        var doc = new DxfDocument(version); var hosts = new List<DxfObject>();
        for (int i = 0; i < 2; i++)
        {
            DxfObject host;
            if (kind == "TEXT") { var item = new Text("original" + i, new Vector3(i, 2, 0), 1); doc.Entities.Add(item); host = item; }
            else if (kind == "MTEXT") { var item = new MText("original" + i, new Vector3(i, 2, 0), 1, 10); doc.Entities.Add(item); host = item; }
            else
            {
                var definition = new AttributeDefinition("TAG" + i) { Value = "original" + i };
                var block = new Block("HOST_BLOCK_" + i); block.AttributeDefinitions.Add(definition);
                if (kind == "ATTRIB")
                { var insert = new Insert(block) { ProxyGraphics = new byte[] { 9, 8 } }; doc.Entities.Add(insert); host = insert.Attributes.Single(); }
                else { doc.Blocks.Add(block); host = definition; }
            }
            HostProxy(host, new byte[] { 1, 2, 3 }); hosts.Add(host);
        }
        using var bytes = new MemoryStream(); Check(doc.Save(bytes, binary), "text-host base"); bytes.Position = 0;
        var raw = DxfRawDocument.Load(bytes);
        for (int i = 0; i < 2; i++)
        {
            var record = StoredFieldRecord(raw, hosts[i].Handle); var tags = record.Tags.ToList();
            tags[tags.FindIndex(t => t.Code == 5)] = new DxfTag(5, i == 0 ? "14B" : "15B");
            int at = tags.FindIndex(t => t.Code == 100);
            tags.InsertRange(at, new[] { new DxfTag(102, "{ACAD_XDICTIONARY"), new DxfTag(360, i == 0 ? "14C" : "15C"), new DxfTag(102, "}") });
            raw = raw.WithRecord(record, tags);
        }
        var root = StoredFieldRecord(raw, doc.Objects.Root.Handle);
        raw = raw.WithRecord(root, root.Tags.Concat(new[] { new DxfTag(3, "ACAD_FIELDLIST"), new DxfTag(350, "150") }));
        var selected = new HashSet<string> { "14C", "14D", "14E", "14F", "15C", "15D", "15E", "15F", "150" };
        var added = source.Sections.Single(s => s.Name == "OBJECTS").Records.Where(r => r.Tags.Any(t => t.Code == 5 && selected.Contains((string)t.Value)))
            .SelectMany(record => record.Tags.Select(t => record.Name == "FIELDLIST" && t.Code == 330 ? new DxfTag(330, doc.Objects.Root.Handle) : t)).ToArray();
        var all = raw.Tags.ToList(); int objects = all.FindIndex(t => t.Code == 2 && Equals(t.Value, "OBJECTS"));
        all.InsertRange(all.FindIndex(objects, t => t.Code == 0 && Equals(t.Value, "ENDSEC")), added);
        var classes = source.Sections.Single(s => s.Name == "CLASSES").Records.Where(r => r.Tags.Any(t => t.Code == 1 && ((string)t.Value == "FIELD" || (string)t.Value == "FIELDLIST"))).SelectMany(r => r.Tags);
        int cls = all.FindIndex(t => t.Code == 2 && Equals(t.Value, "CLASSES"));
        all.InsertRange(all.FindIndex(cls, t => t.Code == 0 && Equals(t.Value, "ENDSEC")), classes);
        return StoredFieldLoad(raw.WithTags(all), binary);
    }

    private static void FieldHostRoundTrip(DxfVersion version, bool binary, string kind)
    {
        var doc = FieldHostDocument(version, binary, kind); var root = ResultRoot(doc);
        var host = FindFieldHost(doc, "14B"); var other = FindFieldHost(doc, "15B");
        var snapshots = doc.Objects.Items.OfType<DxfStoredField>().ToDictionary(f => f, f => f.Payload);
        var refs = snapshots.Keys.ToDictionary(f => f, f => f.References.ToArray());
        SaveFieldResults(doc, binary, $"field-hosts-before-{version}-{binary}-{kind}.dxf"); long seed = StoredFieldSeed(doc);
        Equal(2, doc.Objects.EvaluateFieldTreesAndUpdateTextHosts(new[] { root }, input =>
        {
            Check(snapshots.All(p => ReferenceEquals(p.Key.Payload, p.Value)), "FIELD changed during evaluation");
            Equal("original0", HostText(host), "host changed during evaluation"); return HostSuccess(input);
        }), "FIELD count is separate from host changes");
        Equal(ExpectedHost(HostLiteral, kind), HostText(host), "literal host result");
        Equal("original1", HostText(other), "unselected host changed");
        Check(HostProxy(host) == null && HostProxy(other)!.SequenceEqual(new byte[] { 1, 2, 3 }), "host proxy invalidation scope");
        if (host is DxfAttribute attr) Check(attr.Owner.ProxyGraphics == null, "owning INSERT proxy stayed stale");
        Equal(seed, StoredFieldSeed(doc), "host update allocated handles");
        Check(refs.All(p => p.Value.SequenceEqual(p.Key.References)), "FIELD graph changed");
        SaveFieldResults(doc, binary, $"field-hosts-after-{version}-{binary}-{kind}.dxf");
        var payload = root.Payload;
        HostProxy(host, new byte[] { 4 });
        Equal(0, doc.Objects.EvaluateFieldTreesAndUpdateTextHosts(new[] { root }, HostSuccess), "same result was not a no-op");
        Check(ReferenceEquals(root.Payload, payload) && HostProxy(host)!.SequenceEqual(new byte[] { 4 }), "no-op changed payload/proxy");
        using var bytes = new MemoryStream(); Check(doc.Save(bytes, !binary), "opposite host transport"); bytes.Position = 0;
        var loaded = DxfDocument.Load(bytes)!; Equal(HostText(host), HostText(FindFieldHost(loaded, "14B")), "host text reload");
        var second = (DxfStoredField)doc.GetObjectByHandle("15E");
        Equal(2, doc.Objects.ApplyFieldResultsAndUpdateTextHosts(new[] {
            second.Evaluation.WithResult(new DxfFieldResult("batch", "batch")),
            second.Children[0].Evaluation.WithResult(new DxfFieldResult(1, "batch")) }), "explicit host batch");
        Equal("batch", HostText(other), "batch did not update its root host");
        Equal(0, doc.Objects.Validate().Count, "updated graph invalid");
    }

    private static void FieldHostAtomic(bool binary, string kind, string fault)
    {
        var doc = FieldHostDocument(DxfVersion.AutoCad2018, binary, kind); var roots = FieldRoots(doc);
        var host = FindFieldHost(doc, "14B");
        if (fault is "wrong-slot" or "soft-slot")
        {
            var dictionary = (DxfDictionary)roots[1].Owner; dictionary.Remove("TEXT");
            dictionary.Add(fault == "wrong-slot" ? "OTHER" : "TEXT", roots[1], fault != "soft-slot");
        }
        var snapshots = doc.Objects.Items.OfType<DxfStoredField>().ToDictionary(f => f, f => f.Payload);
        long seed = StoredFieldSeed(doc); int calls = 0;
        DxfFieldResult Evaluate(DxfFieldEvaluationInput input)
        {
            calls++;
            if (input.Field == roots[1])
            {
                if (fault == "late-callback") throw new InvalidOperationException("last root");
                if (fault == "late-null") return null!;
                if (fault == "host-mutation") HostText(host, "callback-owned");
                if (fault == "proxy-mutation") HostProxy(host, new byte[] { 7 });
                if (fault == "reentry") Throws<InvalidOperationException>(() => doc.Objects.ApplyFieldResultsAndUpdateTextHosts(Array.Empty<DxfFieldResultEdit>()));
            }
            string value = fault switch { "literal-symbol" => "%%d", "literal-field" => "%<x>%", "literal-close" => "x>%", "literal-unicode" => @"\U+0041", "literal-unicode-lower" => @"\u+0041", "tab" => "a\tb", _ => HostLiteral };
            return input.Children.Count == 0 ? new DxfFieldResult(42, value) : input.ComposeText();
        }
        var requested = fault == "foreign" ? new[] { roots[0], ResultRoot(FieldHostDocument(DxfVersion.AutoCad2018, binary, kind)) }
            : fault == "duplicate" ? new[] { roots[0], roots[0] } : roots;
        Exception? failure = null; try { doc.Objects.EvaluateFieldTreesAndUpdateTextHosts(requested, Evaluate); } catch (Exception e) { failure = e; }
        Check(failure is ArgumentException or InvalidOperationException or NotSupportedException, "invalid host transaction accepted");
        Check(snapshots.All(p => ReferenceEquals(p.Key.Payload, p.Value)), "FIELD partially published on host failure");
        Equal(fault == "host-mutation" ? "callback-owned" : "original0", HostText(host), "host failure rollback boundary");
        Equal("original1", HostText(FindFieldHost(doc, "15B")), "late host partially published");
        Equal(seed, StoredFieldSeed(doc), "rejected host transaction allocated handles");
        if (fault is "wrong-slot" or "soft-slot" or "foreign" or "duplicate") Equal(0, calls, "preflight happened after callbacks");
        // The guard is usable again even when another selected root is unqualified.
        doc.Objects.EvaluateFieldTreesAndUpdateTextHosts(new[] { roots[0] }, HostSuccess);
    }

    private static void FieldHostPolicy(bool binary, string kind, string mode)
    {
        var doc = FieldHostDocument(DxfVersion.AutoCad2018, binary, kind); var root = ResultRoot(doc); var host = FindFieldHost(doc, "14B");
        if (mode == "case")
        {
            var fields = (DxfDictionary)root.Owner; fields.Remove("TEXT"); fields.Add("text", root);
            var ext = host.ExtensionDictionary; ext.Remove("ACAD_FIELD"); ext.Add("acad_field", fields);
        }
        if (mode == "failed")
        {
            doc.Objects.EvaluateFieldTreesAndUpdateTextHosts(new[] { root }, input => DxfFieldResult.FailureWithValue(DxfFieldResultStatus.OtherError, 2, "failure", "#ERR", "#ERR"));
            Equal("original0", HostText(host), "failed outcome overwrote host text"); Check(HostProxy(host) != null, "failed root cleared host proxy"); return;
        }
        if (mode == "cache-only")
        { doc.Objects.EvaluateFieldTree(root, HostSuccess); Equal("original0", HostText(host), "old cache-only API changed host"); return; }
        if (mode == "host-only") doc.Objects.EvaluateFieldTree(root, HostSuccess);
        if (mode == "newline")
        {
            var old = root.Payload;
            int Apply() => doc.Objects.EvaluateFieldTreesAndUpdateTextHosts(new[] { root }, input => input.Children.Count == 0 ? new DxfFieldResult(1, "a\r\nb") : input.ComposeText());
            if (kind != "MTEXT")
            { Throws<NotSupportedException>(() => Apply()); Check(ReferenceEquals(old, root.Payload), "single-line failure changed FIELD"); return; }
            Apply(); Equal("a\\Pb", HostText(host), "MTEXT paragraph literal conversion");
            using var output = new MemoryStream();
            if (binary) { Check(doc.Save(output, true), "binary newline FIELD save"); output.Position = 0; Check(DxfDocument.Load(output) != null, "binary newline reload"); }
            else { CheckSaveRejected(doc, output); Equal(0L, output.Length, "ASCII FIELD newline wrote bytes"); }
            return;
        }
        Equal(mode == "host-only" ? 0 : 2, doc.Objects.EvaluateFieldTreesAndUpdateTextHosts(new[] { root }, HostSuccess), "host-only FIELD count");
        Equal(ExpectedHost(HostLiteral, kind), HostText(host), "policy did not synchronize host");
    }

    private static void FieldHostProvider(bool binary, string kind, string mode)
    {
        var source = FieldHostDocument(DxfVersion.AutoCad2018, binary, kind);
        using var bytes = new MemoryStream(); Check(source.Save(bytes, binary), "provider host input"); bytes.Position = 0;
        var raw = DxfRawDocument.Load(bytes); var child = StoredFieldRecord(raw, "14F"); var tags = child.Tags.ToList();
        int at = tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbField"));
        tags[at + 1] = new DxfTag(1, mode == "unknown" ? "UnknownHostEvaluator" : "AcVar");
        string code = mode switch {
            "date" => "\\AcVar Date \\f \"yyyy-MM-dd HH:mm:ss\"",
            "angle" => "\\AcVar Angle \\f \"%au0%pr2\"",
            "unknown" => "inert private code", _ => "\\AcVar Amount \\f \"%lu2%pr2\"" };
        tags[at + 2] = new DxfTag(2, code);
        while (at + 3 < tags.Count && tags[at + 3].Code == 3) tags.RemoveAt(at + 3);
        var doc = StoredFieldLoad(raw.WithRecord(child, tags), binary); var root = ResultRoot(doc); var host = FindFieldHost(doc, "14B");
        var provider = StandardProvider();
        Equal(2, doc.Objects.EvaluateFieldTreesAndUpdateTextHosts(new[] { root }, provider.Evaluate), "provider outcome count");
        string expected = mode switch { "date" => "2026-09-17 13:05:09", "angle" => "90.00", "unknown" => "original0", _ => "21.00" };
        Equal(expected, HostText(host), "standard provider host output");
        Equal(mode == "unknown" ? 64 : 2, root.Evaluation.StoredStatus, "provider root state");
        Check((HostProxy(host) != null) == (mode == "unknown"), "provider outcome proxy policy");
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "provider host output"); output.Position = 0;
        Equal(expected, HostText(FindFieldHost(DxfDocument.Load(output)!, "14B")), "provider host reload");
    }

    private static void FieldHostAdmission(bool binary, string mode)
    {
        var doc = mode == "unsupported" ? FieldResultDocument(DxfVersion.AutoCad2018, binary)
            : FieldHostDocument(DxfVersion.AutoCad2018, binary, mode == "parent-proxy" ? "ATTRIB" : "MTEXT");
        var root = ResultRoot(doc); var host = FindFieldHost(doc, "14B");
        if (mode == "columns") ((MText)host).Columns = new MTextColumns();
        if (mode == "linked")
        {
            var parent = new MText("parent", Vector3.Zero, 1) { Columns = new MTextColumns { Storage = MTextColumnStorage.LegacyLinked, Count = 2 } };
            parent.Columns.LinkedColumns.Add((MText)host); doc.Entities.Add(parent);
        }
        var old = root.Payload; int calls = 0;
        DxfFieldResult Callback(DxfFieldEvaluationInput input)
        {
            calls++;
            if (mode == "parent-proxy") ((DxfAttribute)host).Owner.ProxyGraphics = new byte[] { 5 };
            return HostSuccess(input);
        }
        if (mode == "empty")
        { Equal(0, doc.Objects.EvaluateFieldTreesAndUpdateTextHosts(Array.Empty<DxfStoredField>(), Callback), "empty forest"); Equal(0, doc.Objects.ApplyFieldResultsAndUpdateTextHosts(Array.Empty<DxfFieldResultEdit>()), "empty host batch"); }
        else
        {
            Exception? error = null;
            try
            {
                if (mode == "null-batch") doc.Objects.ApplyFieldResultsAndUpdateTextHosts(null!);
                else doc.Objects.EvaluateFieldTreesAndUpdateTextHosts(mode == "null-roots" ? null! : new[] { root }, mode == "null-evaluator" ? null! : Callback, mode == "invalid-context" ? 64 : 32);
            }
            catch (Exception e) { error = e; }
            Check(error is ArgumentException or InvalidOperationException or NotSupportedException, "unqualified admission accepted");
            Check(ReferenceEquals(old, root.Payload), "admission failure changed root");
        }
        if (mode != "parent-proxy") Equal(0, calls, "admission failed after callback");
    }
}
