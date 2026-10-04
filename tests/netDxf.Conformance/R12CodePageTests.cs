// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Reflection;
using System.Text;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static readonly (int Page, string Text)[] RcpProfiles = {
        (874, "ภาษาไทย"), (932, "日本語"), (936, "中文简体"), (949, "한국어"), (950, "繁體中文"),
        (1250, "Zażółć gęślą jaźń"), (1251, "Привет мир"), (1252, "Café déjà vu"),
        (1253, "Ελληνικά"), (1254, "İstanbul ı ş ğ"), (1255, "שלום"), (1256, "مرحبا"),
        (1257, "Āžu čūska"), (1258, "Viê\u0323t")
    };

    private static void RegisterR12CodePageTests()
    {
        foreach (var profile in RcpProfiles)
        foreach (bool binary in new[] { false, true })
        {
            Run($"r12-codepage/wire/{profile.Page}/{binary}", () => RcpWire(profile.Page, profile.Text, binary));
            foreach (DxfVersion version in SupportedVersions)
                Run($"r12-codepage/modern/{profile.Page}/{version}/{binary}", () => RcpModern(profile.Page, profile.Text, version, binary));
        }
        foreach (string? value in new string?[] { null, "", "utf-8", "ANSI_1200", "ANSI_1201", "ANSI_12000",
            "ANSI_37", "ANSI_-1", "ANSI_999999", "ANSI_1252 ", "ANSI_1252\r\n0" })
            Run("r12-codepage/invalid/" + (value == null ? "null" : value.Replace("\r", "CR").Replace("\n", "LF")), () => RcpInvalid(value));
        foreach (bool binary in new[] { false, true })
        {
            Run("r12-codepage/unrepresentable/" + binary, () => RcpUnrepresentable(binary));
            Run("r12-codepage/exact-byte-budget/" + binary, () => RcpBudget(binary));
            foreach (var profile in new[] { ("DOS437", "Grüße"), ("DOS850", "Café"), ("dos932", "日本語") })
                Run($"r12-codepage/alias/{profile.Item1}/{binary}", () => RcpAlias(profile.Item1, profile.Item2, binary));
        }
        Run("r12-codepage/default-compatibility", RcpDefault);
        Run("r12-codepage/one-pass-and-snapshot", RcpSnapshot);
        Run("r12-codepage/explicit-reencoding", RcpReencode);
        Run("r12-codepage/cancellation", RcpCancellation);
        foreach (string mode in new[] { "array", "byte", "span", "async-memory", "async-array", "seek" })
            Run("r12-codepage/bounded-stream/" + mode, () => RcpBoundedStream(mode));
    }

    private static string RcpValue(string role, string text) => role + ":" + text + "\t^J\n\0\\U+0041";
    private static string RcpDescription(string text) => text + @" | literal \U+0041 ^J";

    private static EntityObject[] RcpSeeds(string value)
    {
        var layer = new Layer("NATIVE") { Color = new AciColor(2), IsFrozenInNewViewports = true };
        var style = new TextStyle("ENC_STYLE", "txt.shx");
        var pattern = new Linetype("NATIONAL") { Description = RcpDescription(value) };
        var block = new Block("NATIVE_BLOCK") { Layer = layer };
        var definition = new AttributeDefinition("VALUE", 2, style)
        {
            Value = RcpValue("default", value), Prompt = RcpValue("prompt", value), Flags = (AttributeFlags)5,
            Position = new Vector3(4, 5, 6), Layer = layer
        };
        block.AttributeDefinitions.Add(definition);
        block.Entities.Add(new Text(RcpValue("nested", value), new Vector3(7, 8, 9), 2, style) { Layer = layer });
        block.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX) { Layer = layer });
        var insert = new Insert(block, new Vector3(10, 20, 30)) { Layer = layer };
        insert.Attributes.Single().Value = RcpValue("instance", value);
        insert.Attributes.Single().Position = new Vector3(14, 15, 16);
        return new EntityObject[] {
            new Text(RcpValue("root", value), new Vector3(1, 2, 3), 2, style) { Layer = layer, Linetype = pattern }, insert
        };
    }

    private static void RcpCheck(IEnumerable<EntityObject> entities, string text)
    {
        var roots = entities.ToArray(); Equal(2, roots.Length, "Encoded root inventory");
        var rootText = (Text)roots[0]; var insert = (Insert)roots[1];
        Equal(RcpValue("root", text), rootText.Value, "Encoded root text");
        Equal(RcpDescription(text), rootText.Linetype.Description, "Encoded linetype description");
        R12Vector(new Vector3(1, 2, 3), rootText.Position); R12Vector(new Vector3(10, 20, 30), insert.Position);
        var definition = insert.Block.AttributeDefinitions["VALUE"]; var attribute = insert.Attributes.Single();
        Equal(RcpValue("default", text), definition.Value, "Encoded attribute default");
        Equal(RcpValue("prompt", text), definition.Prompt, "Encoded attribute prompt");
        Equal(RcpValue("instance", text), attribute.Value, "Encoded attribute instance");
        Equal(RcpValue("nested", text), insert.Block.Entities.OfType<Text>().Single().Value, "Encoded nested text");
        Check(ReferenceEquals(attribute.Definition, definition), "Encoding split attribute definition identity");
        Check(ReferenceEquals(rootText.Style, definition.Style) && ReferenceEquals(attribute.Style, definition.Style), "Encoding split style identity");
        Check(ReferenceEquals(rootText.Layer, attribute.Layer) && rootText.Layer.IsFrozenInNewViewports, "Encoding split layer state");
        R12Vector(new Vector3(4, 5, 6), definition.Position); R12Vector(new Vector3(14, 15, 16), attribute.Position);
        var line = insert.Block.Entities.OfType<Line>().Single(); R12Vector(Vector3.Zero, line.StartPoint); R12Vector(Vector3.UnitX, line.EndPoint);
    }

    private static string RcpDeclaration(DxfRawDocument raw) => (string)raw.Sections.Single(s => s.Name == "HEADER")
        .Records.Single(r => r.MarkerCode == 9 && r.Name == "$DWGCODEPAGE").Tags.Single(t => t.Code == 3).Value;

    private static void RcpWire(int page, string text, bool binary)
    {
        string name = "ANSI_" + page; var seeds = RcpSeeds(text);
        var raw = DxfR12Codec.CreateWithCodePage(seeds, name, binary);
        var plan = DxfR12SelectionPlan.Prepare(raw);
        Equal(name, plan.DrawingCodePage, "Plan reset source encoding"); Equal(page, plan.EncodingCodePage, "Effective R12 encoding");
        var snapshot = raw.Tags.ToArray();
        for (int stage = 0; stage < 3; stage++)
        {
            bool transport = stage == 1 ? !binary : binary;
            using var output = new MemoryStream();
            if (stage == 0) DxfR12Codec.SaveWithCodePage(output, seeds, name, transport);
            else plan.Save(output, DxfVersion.AutoCad12, transport);
            File.WriteAllBytes(Path.Combine(ArtifactDirectory,
                $"r12-codepage-{page}-{(binary ? "binary" : "text")}-{new[] { "source", "output", "resave" }[stage]}.dxf"), output.ToArray());
            output.Position = 0; var loaded = DxfRawDocument.Load(output);
            Equal(page, loaded.EncodingCodePage, "Actual R12 transport encoding"); Equal(name, RcpDeclaration(loaded), "Stored encoding declaration");
            RcpCheck(DxfR12Codec.ReadEntities(loaded), text);
            plan = DxfR12SelectionPlan.Prepare(loaded);
        }
        Check(snapshot.SequenceEqual(raw.Tags) && seeds.All(e => e.Handle == null && e.Owner == null), "Encoding changed source ownership or tags");
    }

    private static void RcpModern(int page, string text, DxfVersion version, bool binary)
    {
        var plan = DxfR12SelectionPlan.PrepareWithCodePage(RcpSeeds(text), "ANSI_" + page);
        var snapshot = plan.NormalizedSelection.Tags.ToArray();
        var editable = plan.CreateDocument(version); RcpCheck(editable.Entities.All, text);
        using var output = new MemoryStream(); plan.Save(output, version, binary);
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"r12-codepage-modern-{page}-{version}-{(binary ? "binary" : "text")}.dxf"), output.ToArray());
        output.Position = 0; var raw = DxfRawDocument.Load(output);
        Equal("ANSI_" + page, RcpDeclaration(raw), "Modern declaration not retained");
        Equal(version >= DxfVersion.AutoCad2007 ? 65001 : page, raw.EncodingCodePage, "Modern encoding family rule");
        output.Position = 0; var loaded = DxfDocument.Load(output) ?? throw new InvalidDataException("Encoded modern output failed to load");
        RcpCheck(loaded.Entities.All, text); RcpCheck(editable.Entities.All, text);
        Check(snapshot.SequenceEqual(plan.NormalizedSelection.Tags), "Modern output encoding modified the snapshot");
    }

    private static void RcpInvalid(string? page)
    {
        int count = 0;
        IEnumerable<EntityObject> Source() { count++; yield return new Point(Vector3.Zero); }
        using var output = new MemoryStream(); output.WriteByte(91); output.Position = 0;
        R12Refuses(() => DxfR12Codec.SaveWithCodePage(output, Source(), page!));
        Equal(0, count, "Invalid encoding enumerated caller source");
        Check(output.Position == 0 && output.Length == 1 && output.ToArray()[0] == 91, "Invalid profile changed destination");
    }

    private static void RcpUnrepresentable(bool binary)
    {
        var roots = RcpSeeds("雪"); using var output = new MemoryStream(); output.WriteByte(97); output.Position = 0;
        Throws<EncoderFallbackException>(() => DxfR12Codec.SaveWithCodePage(output, roots, "ANSI_1252", binary));
        var plan = DxfR12SelectionPlan.PrepareWithCodePage(roots, "ANSI_1252");
        Throws<EncoderFallbackException>(() => plan.Save(output, DxfVersion.AutoCad12, binary));
        Check(output.Position == 0 && output.ToArray().SequenceEqual(new byte[] { 97 }) && output.CanWrite, "Encoding failure wrote replacement data");
        RcpCheck(roots, "雪");
    }

    private static void RcpBudget(bool binary)
    {
        var plan = DxfR12SelectionPlan.PrepareWithCodePage(RcpSeeds("日本語"), "ANSI_932");
        using var reference = new MemoryStream(); plan.Save(reference, DxfVersion.AutoCad12, binary);
        int length = checked((int)reference.Length);
        using var exact = new MemoryStream(); plan.Save(exact, DxfVersion.AutoCad12, binary, new DxfRawOptions(length));
        Check(reference.ToArray().SequenceEqual(exact.ToArray()), "Exact encoded-byte budget changed bytes");
        using var failed = new MemoryStream(); failed.Write(new byte[] { 1, 2, 3 }); failed.Position = 1;
        Throws<InvalidDataException>(() => plan.Save(failed, DxfVersion.AutoCad12, binary, new DxfRawOptions(length - 1)));
        Check(failed.Position == 1 && failed.ToArray().SequenceEqual(new byte[] { 1, 2, 3 }), "Multibyte overflow reached destination");
    }

    private static void RcpAlias(string alias, string text, bool binary)
    {
        var raw = DxfR12Codec.CreateWithCodePage(RcpSeeds(text), alias, binary);
        var plan = DxfR12SelectionPlan.Prepare(R12Reload(raw, binary));
        Equal(alias, plan.DrawingCodePage, "Encoding alias spelling changed");
        using var output = new MemoryStream(); plan.Save(output, DxfVersion.AutoCad12, !binary); output.Position = 0;
        var loaded = DxfRawDocument.Load(output); Equal(alias, RcpDeclaration(loaded), "Resaved alias spelling changed");
        RcpCheck(DxfR12Codec.ReadEntities(loaded), text);
    }

    private static void RcpDefault()
    {
        var roots = RcpSeeds("Café");
        foreach (bool binary in new[] { false, true })
        {
            var old = DxfR12Codec.Create(roots, binary);
            var explicitDefault = DxfR12Codec.CreateWithCodePage(roots, "ANSI_1252", binary);
            Check(R12Bytes(old, binary).SequenceEqual(R12Bytes(explicitDefault, binary)), "Default authored bytes changed");
        }
        var raw = DxfR12Codec.Create(roots);
        var declaration = raw.Sections.Single(s => s.Name == "HEADER").Records.Single(r => r.Name == "$DWGCODEPAGE");
        // Construct an input with the optional declaration absent; WithTags intentionally forbids profile edits.
        var absent = DxfRawDocument.Create(raw.Tags.Where((_, i) => i < declaration.StartTagIndex || i >= declaration.EndTagIndex));
        var plan = DxfR12SelectionPlan.Prepare(absent, null);
        Equal("ANSI_1252", plan.DrawingCodePage, "Absent code-page default");
        Equal(1252, DxfR12SelectionPlan.Prepare(roots, null).EncodingCodePage, "Existing null-options call became ambiguous");
    }

    private static void RcpSnapshot()
    {
        int count = 0; var roots = RcpSeeds("Zażółć gęślą jaźń");
        IEnumerable<EntityObject> Once() { if (++count != 1) throw new Exception("Reenumerated source"); foreach (var entity in roots) yield return entity; }
        var plan = DxfR12SelectionPlan.PrepareWithCodePage(Once(), "ANSI_1250");
        ((Text)roots[0]).Value = "later change";
        using var output = new MemoryStream(); plan.Save(output, DxfVersion.AutoCad12); output.Position = 0;
        RcpCheck(DxfR12Codec.ReadEntities(DxfRawDocument.Load(output)), "Zażółć gęślą jaźń"); Equal(1, count, "Preparation enumeration count");
    }

    private static void RcpReencode()
    {
        var raw = R12Reload(DxfR12Codec.CreateWithCodePage(RcpSeeds("Café"), "ANSI_1252"), false);
        byte[] before = R12Bytes(raw, false);
        var plan = DxfR12SelectionPlan.PrepareWithCodePage(raw, "DOS850");
        using var output = new MemoryStream(); plan.Save(output, DxfVersion.AutoCad12, true); output.Position = 0;
        var changed = DxfRawDocument.Load(output); Equal(850, changed.EncodingCodePage, "Explicit target encoding");
        RcpCheck(DxfR12Codec.ReadEntities(changed), "Café"); Check(before.SequenceEqual(R12Bytes(raw, false)), "Reencoding changed caller bytes");
        Throws<NotSupportedException>(() => raw.WithTags(plan.NormalizedSelection.Tags));
    }

    private static void RcpCancellation()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using var output = new MemoryStream(); output.WriteByte(11);
        Throws<OperationCanceledException>(() => DxfR12Codec.SaveWithCodePage(output, RcpSeeds("日本語"), "ANSI_932", cancellationToken: cancelled.Token));
        Check(output.Position == 1 && output.Length == 1, "Cancelled encoding touched output");
    }

    private static void RcpBoundedStream(string mode)
    {
        Type type = typeof(DxfR12SelectionPlan).GetNestedType("SelectionOutputStream", BindingFlags.NonPublic)!;
        using var stream = (Stream)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { 5, CancellationToken.None }, null)!;
        stream.Write(new byte[] { 1, 2, 3, 4, 5 });
        void Overflow()
        {
            switch (mode)
            {
                case "array": stream.Write(new byte[] { 9 }, 0, 1); break;
                case "byte": stream.WriteByte(9); break;
                case "span": stream.Write(new ReadOnlySpan<byte>(new byte[] { 9 })); break;
                case "async-memory": stream.WriteAsync(new ReadOnlyMemory<byte>(new byte[] { 9 })).GetAwaiter().GetResult(); break;
                case "async-array": stream.WriteAsync(new byte[] { 9 }, 0, 1).GetAwaiter().GetResult(); break;
                case "seek": stream.Seek(1, SeekOrigin.End); break;
            }
        }
        Throws<InvalidDataException>(Overflow); Equal(5L, stream.Length, "Byte-limit bypass changed staged length");
        Equal(5L, stream.Position, "Byte-limit bypass moved staged position");
    }
}
