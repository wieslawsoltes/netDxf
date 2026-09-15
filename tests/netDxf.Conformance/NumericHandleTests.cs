using System.Globalization;
using System.Reflection;
using netDxf;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    // Exercise both boundaries of each semantic handle family admitted for authored XRECORD data.
    private static readonly short[] NumericSemanticCodes = { 330, 339, 340, 349, 350, 359, 360, 369 };
    private const string NumericHandleApp = "NUMERIC_HANDLES";

    private static void RunNumericHandleTests()
    {
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
            Run($"numeric-handles/roundtrip-clone-erasure/{version}/{binary}", () => NumericHandleGraph(version, binary));
        Run("numeric-handles/lookup/case-and-leading-zeroes", NumericHandleLookupAliases);
        Run("numeric-handles/lookup/reject-invalid-tokens", NumericHandleLookupInvalid);
        Run("numeric-handles/lookup/document-zero-and-full-width-misses", NumericHandleLookupZero);
        Run("numeric-handles/lookup/erased-identity-is-not-reused", NumericHandleLookupErased);
    }

    private static (DxfDocument Document, DxfXRecord Target, string Name) NumericHandleLookupFixture(DxfVersion version = DxfVersion.AutoCad2018)
    {
        var doc = new DxfDocument(version);
        for (int i = 0; i < 16; i++)
        {
            var target = new DxfXRecord(); string name = "NUMERIC_EXTERNAL_" + i;
            target.Data.Add(new DxfTag(1, "external target")); doc.Objects.Root.Add(name, target);
            if (target.Handle.Any(c => c is >= 'A' and <= 'F')) return (doc, target, name);
        }
        throw new Exception("Fixture did not allocate a handle containing hexadecimal letters");
    }

    private static string NumericHandleAlias(string value) => value.ToLowerInvariant().PadLeft(16, '0');

    private static void NumericHandleLookupAliases()
    {
        var (doc, target, _) = NumericHandleLookupFixture(); long seed = NumericHandleSeed(doc); int count = doc.Objects.Items.Count;
        foreach (string spelling in new[] { target.Handle, target.Handle.ToLowerInvariant(), "0" + target.Handle, NumericHandleAlias(target.Handle) })
            Check(ReferenceEquals(target, doc.GetObjectByHandle(spelling)), "Equivalent public lookup selected another identity: " + spelling);
        Equal(seed, NumericHandleSeed(doc), "Lookup allocated handles"); Equal(count, doc.Objects.Items.Count, "Lookup changed registration");
    }

    private static void NumericHandleLookupInvalid()
    {
        var (doc, target, _) = NumericHandleLookupFixture(); long seed = NumericHandleSeed(doc); int count = doc.Objects.Items.Count;
        string[] invalid = { null!, "", " ", "\t", " " + target.Handle, target.Handle + " ", target.Handle + "\n", "+" + target.Handle,
            "-" + target.Handle, "0x" + target.Handle, "G", "1.0", "Ａ", "\0", new string('0', 17), "10000000000000000", "0" + NumericHandleAlias(target.Handle) };
        foreach (string spelling in invalid) Check(doc.GetObjectByHandle(spelling) == null, "Invalid public handle token resolved: " + spelling);
        Equal(seed, NumericHandleSeed(doc), "Invalid lookup allocated handles"); Equal(count, doc.Objects.Items.Count, "Invalid lookup changed registration");
    }

    private static void NumericHandleLookupZero()
    {
        var (doc, _, _) = NumericHandleLookupFixture();
        // The public document identity is zero. Semantic reference fields below must
        // nevertheless treat every numeric zero spelling as null, including on clone.
        foreach (string spelling in new[] { "0", "0000", new string('0', 16) })
            Check(ReferenceEquals(doc, doc.GetObjectByHandle(spelling)), "Public document-zero identity changed");
        foreach (string spelling in new[] { "7FFFFFFFFFFFFFFF", "8000000000000000", "FFFFFFFFFFFFFFFF", "ffffffffffffffff" })
            Check(doc.GetObjectByHandle(spelling) == null, "Valid missing unsigned handle resolved unexpectedly");
    }

    private static void NumericHandleLookupErased()
    {
        var (doc, target, _) = NumericHandleLookupFixture(); string handle = target.Handle, alias = NumericHandleAlias(handle); long seed = NumericHandleSeed(doc);
        doc.Objects.EraseOwnedTree(target);
        Check(target.IsErased && doc.GetObjectByHandle(handle) == null && doc.GetObjectByHandle(alias) == null, "Erased public identity remains visible");
        Equal(seed, NumericHandleSeed(doc), "Erasure changed allocation seed");
        var next = new DxfXRecord(); doc.Objects.Root.Add("NUMERIC_AFTER_ERASE", next);
        Check(ulong.Parse(next.Handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) > ulong.Parse(handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture), "Erased numeric identity was reused");
        Check(doc.GetObjectByHandle(alias) == null, "New registration resurrected an erased alias");
    }

    private static void NumericHandleGraph(DxfVersion version, bool binary)
    {
        var fixture = NumericHandleLookupFixture(version); var source = fixture.Document; var external = fixture.Target;
        var graph = new DxfDictionary(); var target = new DxfXRecord(); var links = new DxfXRecord();
        target.Data.Add(new DxfTag(1, "owned target")); graph.Add("TARGET", target); graph.Add("LINKS", links); source.Objects.Root.Add("NUMERIC_GRAPH", graph);
        string targetAlias = NumericHandleAlias(target.Handle), externalAlias = NumericHandleAlias(external.Handle);
        Check(externalAlias.Any(c => c is >= 'a' and <= 'f'), "Semantic lowercase spelling control has no letters");
        foreach (short code in NumericSemanticCodes)
        {
            links.Data.Add(new DxfTag(code, targetAlias)); links.Data.Add(new DxfTag(code, "0000"));
        }
        links.Data.Add(new DxfTag(330, externalAlias));
        links.Data.Add(new DxfTag(320, targetAlias)); links.Data.Add(new DxfTag(329, externalAlias)); links.Data.Add(new DxfTag(320, "0000"));
        var xdata = new XData(new ApplicationRegistry(NumericHandleApp));
        foreach (string handle in new[] { targetAlias, externalAlias, "0000", new string('0', 16) })
            xdata.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, handle));
        links.XData.Add(xdata);
        Equal(0, source.Objects.Validate().Count, "Numeric references failed source validation");
        var authoredTags = links.Data.ToArray(); var authoredXData = links.XData[NumericHandleApp].XDataRecord.ToArray();
        source = NumericHandleRoundTrip(source, binary);
        graph = (DxfDictionary)source.Objects.Root["NUMERIC_GRAPH"]; target = (DxfXRecord)graph["TARGET"]; links = (DxfXRecord)graph["LINKS"]; external = (DxfXRecord)source.Objects.Root[fixture.Name];
        NumericHandleAssert(links, target.Handle, external.Handle, target.Handle, external.Handle, true);
        Check(ReferenceEquals(source.GetObjectByHandle(targetAlias), target) && ReferenceEquals(source.GetObjectByHandle(externalAlias), external), "Loaded numeric spelling resolved to another object");
        // Typed ReadHex canonicalizes every handle field, including arbitrary values.
        // Restore authored spelling explicitly so clone preflight must handle padded
        // nonzero references and numeric nulls instead of seeing only canonical input.
        links.Data.Clear(); foreach (DxfTag tag in authoredTags) links.Data.Add(tag);
        links.XData[NumericHandleApp].XDataRecord.Clear(); foreach (XDataRecord record in authoredXData) links.XData[NumericHandleApp].XDataRecord.Add(record);
        NumericHandleAssert(links, targetAlias, externalAlias, targetAlias, externalAlias);

        var destination = new DxfDocument(version); var mappedExternal = new DxfXRecord(); mappedExternal.Data.Add(new DxfTag(1, "mapped external"));
        destination.Objects.Root.Add("NUMERIC_EXTERNAL", mappedExternal);
        long failedSeed = NumericHandleSeed(destination); int failedCount = destination.Objects.Items.Count;
        Throws<InvalidOperationException>(() => destination.Objects.Clone(graph, destination.Objects.Root, "NUMERIC_COPY"));
        Equal(failedSeed, NumericHandleSeed(destination), "Unmapped numeric clone allocated handles"); Equal(failedCount, destination.Objects.Items.Count, "Unmapped numeric clone registered children");
        Check(!destination.Objects.Root.Contains("NUMERIC_COPY"), "Unmapped numeric clone attached a root");
        var mappings = new Dictionary<DxfObject, DxfObject>(ReferenceEqualityComparer.Instance) { [external] = mappedExternal };
        // No mapping is supplied for source document identity zero: stored zeroes are null.
        var copy = destination.Objects.Clone(graph, destination.Objects.Root, "NUMERIC_COPY", mappings);
        var copyTarget = (DxfXRecord)copy["TARGET"]; var copyLinks = (DxfXRecord)copy["LINKS"];
        NumericHandleAssert(copyLinks, copyTarget.Handle, mappedExternal.Handle, targetAlias, externalAlias);
        Check(ReferenceEquals(copyTarget.Owner, copy) && ReferenceEquals(copyLinks.Owner, copy), "Mapped clone ownership changed");
        NumericHandleAssert(links, targetAlias, externalAlias, targetAlias, externalAlias);
        destination = NumericHandleRoundTrip(destination, binary);
        copy = (DxfDictionary)destination.Objects.Root["NUMERIC_COPY"]; copyTarget = (DxfXRecord)copy["TARGET"]; copyLinks = (DxfXRecord)copy["LINKS"]; mappedExternal = (DxfXRecord)destination.Objects.Root["NUMERIC_EXTERNAL"];
        NumericHandleAssert(copyLinks, copyTarget.Handle, mappedExternal.Handle, target.Handle, external.Handle, true);

        foreach (short code in NumericSemanticCodes)
        {
            mappedExternal.Data.Add(new DxfTag(code, NumericHandleAlias(copyTarget.Handle)));
            NumericHandleEraseRejected(destination, copy);
            mappedExternal.Data.RemoveAt(mappedExternal.Data.Count - 1);
        }
        var incoming = new XData(new ApplicationRegistry(NumericHandleApp));
        incoming.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, NumericHandleAlias(copyTarget.Handle))); mappedExternal.XData.Add(incoming);
        NumericHandleEraseRejected(destination, copy); mappedExternal.XData.Remove(NumericHandleApp);

        // Matching arbitrary values and numeric-null semantic fields must not block erasure.
        string erasedAlias = NumericHandleAlias(copyTarget.Handle);
        mappedExternal.Data.Add(new DxfTag(320, erasedAlias)); mappedExternal.Data.Add(new DxfTag(329, erasedAlias));
        foreach (short code in NumericSemanticCodes) mappedExternal.Data.Add(new DxfTag(code, "0000"));
        var nullData = new XData(new ApplicationRegistry(NumericHandleApp)); nullData.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, "0000")); mappedExternal.XData.Add(nullData);
        var erased = new DxfDatabaseObject[] { copy, copyTarget, copyLinks }; string[] erasedHandles = erased.Select(item => item.Handle).ToArray(); long eraseSeed = NumericHandleSeed(destination);
        destination.Objects.EraseOwnedTree(copy);
        Equal(eraseSeed, NumericHandleSeed(destination), "Numeric erasure allocated handles");
        Check(!destination.Objects.Root.Contains("NUMERIC_COPY"), "Erased numeric root alias survived");
        foreach (var item in erased)
            Check(item.IsErased && item.Database == null && destination.GetObjectByHandle(NumericHandleAlias(item.Handle)) == null, "Numeric erased identity survived");
        Check(ReferenceEquals(source.GetObjectByHandle(targetAlias), target) && !target.IsErased, "Destination erasure changed source identity");
        string externalHandle = mappedExternal.Handle;
        destination = NumericHandleRoundTrip(destination, binary); mappedExternal = (DxfXRecord)destination.GetObjectByHandle(externalHandle);
        foreach (string handle in erasedHandles) Check(destination.GetObjectByHandle(NumericHandleAlias(handle)) == null, "Erased numeric identity returned on reload");
        Check(mappedExternal.Data.Where(t => t.Code is 320 or 329).Select(t => (string)t.Value).SequenceEqual(new[] { copyTarget.Handle, copyTarget.Handle }), "Reload changed arbitrary numeric values");
        Check(mappedExternal.Data.Where(t => NumericSemanticCodes.Contains(t.Code)).All(t => (string)t.Value == "0"), "Reload changed numeric-null semantic values");
        Equal("0", (string)mappedExternal.XData[NumericHandleApp].XDataRecord.Single().Value, "Reload changed numeric-null XData value");
    }

    private static void NumericHandleAssert(DxfXRecord links, string target, string external, string arbitraryTarget, string arbitraryExternal, bool canonicalNull = false)
    {
        string zero = canonicalNull ? "0" : "0000";
        var expected = new List<(short Code, string Value)>();
        foreach (short code in NumericSemanticCodes) { expected.Add((code, target)); expected.Add((code, zero)); }
        expected.Add((330, external)); expected.Add((320, arbitraryTarget)); expected.Add((329, arbitraryExternal)); expected.Add((320, zero));
        var actual = links.Data.Select(t => (t.Code, (string)t.Value)).ToArray();
        Check(actual.SequenceEqual(expected), "Numeric/null/arbitrary XRECORD sequence differs; expected " + string.Join(";", expected) + "; actual " + string.Join(";", actual));
        Check(links.XData[NumericHandleApp].XDataRecord.Select(t => (string)t.Value).SequenceEqual(new[] { target, external, zero, canonicalNull ? "0" : new string('0', 16) }), "Exact numeric/null XData sequence differs");
    }

    private static DxfDocument NumericHandleRoundTrip(DxfDocument document, bool binary)
    {
        Equal(0, document.Objects.Validate().Count, "Numeric graph pre-save validation");
        using var stream = new MemoryStream(); Check(document.Save(stream, binary), "Numeric graph save failed");
        byte[] bytes = stream.ToArray();
        Equal(binary, System.Text.Encoding.ASCII.GetString(bytes, 0, 18) == "AutoCAD Binary DXF", "Numeric graph actual transport differs");
        foreach (DxfXRecord record in document.Objects.Items.OfType<DxfXRecord>())
        {
            foreach (DxfTag tag in record.Data.Where(t => t.ValueType == DxfTagValueType.Handle)) NumericHandleWireSpelling(bytes, binary, tag.Code, (string)tag.Value);
            foreach (XData data in record.XData.Values)
                foreach (XDataRecord tag in data.XDataRecord.Where(t => t.Code == XDataCode.DatabaseHandle)) NumericHandleWireSpelling(bytes, binary, 1005, (string)tag.Value);
        }
        stream.Position = 0;
        var loaded = DxfDocument.Load(stream) ?? throw new Exception("Numeric graph reload failed");
        Equal(document.DrawingVariables.AcadVer, loaded.DrawingVariables.AcadVer, "Numeric graph profile changed");
        Equal(0, loaded.Objects.Validate().Count, "Numeric graph post-load validation"); return loaded;
    }

    private static void NumericHandleWireSpelling(byte[] bytes, bool binary, short code, string value)
    {
        // Inspect emitted code/value bytes directly: the standard raw and typed
        // readers both canonicalize hexadecimal spelling through ReadHex.
        if (binary)
        {
            byte[] needle = new byte[3 + value.Length]; needle[0] = (byte)code; needle[1] = (byte)(code >> 8);
            System.Text.Encoding.ASCII.GetBytes(value).CopyTo(needle, 2);
            Check(bytes.AsSpan().IndexOf(needle) >= 0, "Binary writer changed handle spelling " + code + ":" + value);
        }
        else
        {
            string[] lines = System.Text.Encoding.ASCII.GetString(bytes).Replace("\r\n", "\n").Split('\n'); bool found = false;
            for (int i = 0; i + 1 < lines.Length; i += 2)
                if (short.TryParse(lines[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out short actual) && actual == code && lines[i + 1] == value) { found = true; break; }
            Check(found, "ASCII writer changed handle spelling " + code + ":" + value);
        }
    }

    private static void NumericHandleEraseRejected(DxfDocument document, DxfDictionary graph)
    {
        var members = new[] { graph, (DxfDatabaseObject)graph["TARGET"], (DxfDatabaseObject)graph["LINKS"] };
        long seed = NumericHandleSeed(document); int count = document.Objects.Items.Count;
        Throws<InvalidOperationException>(() => document.Objects.EraseOwnedTree(graph));
        Equal(seed, NumericHandleSeed(document), "Numeric incoming guard allocated handles"); Equal(count, document.Objects.Items.Count, "Numeric incoming guard changed registration count");
        foreach (var member in members) Check(!member.IsErased && ReferenceEquals(document.GetObjectByHandle(NumericHandleAlias(member.Handle)), member), "Rejected numeric erasure changed member identity");
        Check(ReferenceEquals(document.Objects.Root["NUMERIC_COPY"], graph), "Rejected numeric erasure detached the owning alias");
    }

    private static long NumericHandleSeed(DxfDocument document) => (long)typeof(DxfDocument).GetProperty("NumHandles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(document)!;
}
