using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Dimensions;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Tables;

internal static class StoredDimensionFontVerification
{
    private sealed record FontCase(string Name, string File, string BigFont, TextStyleFontData? Extended);
    private static readonly FontCase[] Cases =
    [
        new("Bare", "stroke_primary", "", null),
        new("Independent", "fonts/primary.legacy", "fonts/companion.legacy", new("Synthetic family", unchecked((int)0xC3123456))),
        new("Empty", "", "unresolved_companion", new("", 0)),
        new("Mixed", "primary.shx", "companion.raw", new("Secondary family", 0x01000022))
    ];

    internal static void Register(List<(string Name, Action Run)> tests)
    {
        foreach (var value in Cases)
            tests.Add(($"dimension/stored font metadata/{value.Name} preserves annotation and source", () => Annotation(value)));
        tests.Add(("style/stored font factory preserves transport-safe metadata and strict authoring", StoredFactory));
        tests.Add(("style/source construction and history preserve independent stored font fields", SourceConstruction));
    }

    private static TextStyle Style(FontCase value)
    {
        var style = TextStyle.FromStoredFontFiles("Stored " + value.Name, value.File, value.BigFont);
        style.ExtendedFontData = value.Extended;
        style.WidthFactor = 0.85; style.ObliqueAngle = 8; style.LastHeight = 3.25;
        style.Flags = (TextStyleFlags)0x4000; style.TextGenerationFlags = 0x4000;
        return style;
    }

    private static void Annotation(FontCase value)
    {
        var textStyle = Style(value);
        var style = new DimensionStyle("Stored dimension")
        {
            TextStyle = textStyle, TextHeight = 2, ArrowSize = 1, TextOffset = 0.5, LengthPrecision = 1
        };
        var dimension = new AlignedDimension(new(0, 0), new(40, 0), 10, style);
        var document = new DxfDocument { BuildDimensionBlocks = false }; document.Entities.Add(dimension);
        var source = new ShadowSession(document); var snapshot = source.Current;
        var expected = FontState.Read(textStyle); var observer = new FontObserver();
        var builder = new CadSceneBuilder([new DimensionEntityProjector(resources: [observer])]);
        var scene = builder.Build(snapshot);
        Require(!scene.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error),
            "Stored font metadata prevented dimension projection: " + string.Join("; ", scene.Diagnostics.Select(d => d.Message)));
        Require(scene.Occurrences.Any(o => o.Geometry.Text is { Content.Length: > 0 }), "The dimension label is missing.");
        Require(scene.Occurrences.Count(o => o.Geometry.Filled) >= 2, "Dimension arrowheads are missing.");
        var strokes = scene.Occurrences.SelectMany(o => o.Geometry.DisplayContours).SelectMany(c => c.Curves).OfType<LineCurve>().ToArray();
        foreach (var x in new[] { 0.0, 40.0 })
            Require(strokes.Any(line => Math.Abs(line.A.X - x) < 1e-8 && Math.Abs(line.B.X - x) < 1e-8
                && Math.Abs(line.B.Y - line.A.Y) > 5), "A dimension extension line is missing.");
        Require(observer.Fonts.Count > 0 && observer.Fonts.All(font => font == expected),
            "Dimension reconstruction changed the captured font metadata.");
        var render = new RenderCompiler(builder).Compile(snapshot);
        Require(!render.Bounds.IsEmpty && render.Items.Any(item => item.Text is not null)
            && render.Items.Count(item => item.Filled) >= 2, "Dimension rendering omitted annotation primitives.");
        Require(!render.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), "Dimension rendering failed.");
        Require(FontState.Read(textStyle) == expected && dimension.Block is null && ReferenceEquals(snapshot, source.Refresh()),
            "Read-only projection mutated the source document.");
    }

    private static void StoredFactory()
    {
        const string file = "literal\\U+0041_青😀\0\r\n.legacy";
        const string big = "companion\\U+0042_青😀\0\r\n";
        var style = TextStyle.FromStoredFontFiles("Transport", file, big);
        Require(style.FontFile == file && style.BigFont == big && style.ExtendedFontData is null,
            "The stored factory normalized font metadata.");
        foreach (var binary in new[] { false, true })
        {
            var document = new DxfDocument(); document.TextStyles.Add((TextStyle)style.Clone());
            using var stream = new MemoryStream(); Require(document.Save(stream, binary), "Stored font metadata did not save.");
            stream.Position = 0;
            var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Stored font metadata did not reload.");
            Require(loaded.TextStyles[style.Name].FontFile == file && loaded.TextStyles[style.Name].BigFont == big,
                "Stored font metadata changed in transport.");
        }
        Throws<ArgumentNullException>(() => TextStyle.FromStoredFontFiles("Null", null!));
        Throws<ArgumentNullException>(() => TextStyle.FromStoredFontFiles("Null", "", null!));
        Throws<ArgumentException>(() => TextStyle.FromStoredFontFiles("Invalid", "\uD800"));
        Throws<ArgumentException>(() => TextStyle.FromStoredFontFiles("Invalid", "", "\uDC00"));
        Throws<ArgumentException>(() => new TextStyle("Authoring", "extensionless"));
        Throws<ArgumentException>(() => style.FontFile = "extensionless");
    }

    private static void SourceConstruction()
    {
        var authored = Style(Cases[1]);
        var template = new DxfDocument(); template.TextStyles.Add(authored);
        var captured = new ShadowSession(template).Current.Document["TextStyles"].Items.Single(s => s["Name"].Text == authored.Name);
        var document = new DxfDocument(); var source = new ShadowSession(document); var editor = new EditorSession(source);
        var edit = new ShadowGraphEdit(source.Current);
        var names = new[] { "Name", "FontFile", "BigFont", "FontFamilyName", "FontStyle", "Flags",
            "TextGenerationFlags", "WidthFactor", "ObliqueAngle", "LastHeight", "Height" };
        var values = names.ToDictionary(name => name, name => captured[name].Value);
        values["ExtendedFontData"] = captured["ExtendedFontData"].Value;
        Require(values["ExtendedFontData"] is ShadowValue.Structure,
            "Immutable extended font data acquired unstable source identity.");
        var created = edit.Create(typeof(TextStyle).FullName!, ShadowObjectKind.Table, values);
        var styles = source.Current.Document["TextStyles"];
        edit.ReplaceSequence(styles.Id!.Value, "$items", styles.Items.Select(s => s.Value).Append(new ShadowValue.Reference(created)));
        editor.Execute("Restore stored text style", edit.Build());
        void Check() => Require(FontState.Read(document.TextStyles[authored.Name]) == FontState.Read(authored),
            "Source construction changed independent font fields or extended metadata.");
        Check();
        for (var i = 0; i < 3; i++)
        {
            editor.Undo(); Require(!document.TextStyles.Contains(authored.Name), "Undo retained the constructed text style.");
            editor.Redo(); Check();
        }
        var data = (ShadowValue.Structure)values["ExtendedFontData"];
        editor.SetProperty(created, "ExtendedFontData", data with
            { Fields = data.Fields.SetItem("FamilyName", ShadowValue.From("Edited family")) });
        void CheckEdited() => Require(document.TextStyles[authored.Name].FontFamilyName == "Edited family"
            && document.TextStyles[authored.Name].FontFile == authored.FontFile
            && document.TextStyles[authored.Name].BigFont == authored.BigFont
            && document.TextStyles[authored.Name].ExtendedFontData.Flags == authored.ExtendedFontData.Flags,
            "Editing extended font data changed independent stored fields.");
        CheckEdited(); editor.Undo(); Check(); editor.Redo(); CheckEdited();
    }

    private sealed record FontState(string File, string BigFont, string Family, FontStyle Style, bool HasExtended,
        int ExtendedFlags, TextStyleFlags Flags, short Generation, double Width, double Oblique, double? LastHeight)
    {
        internal static FontState Read(TextStyle value) => new(value.FontFile, value.BigFont, value.FontFamilyName,
            value.FontStyle, value.ExtendedFontData is not null, value.ExtendedFontData?.Flags ?? 0,
            value.Flags, value.TextGenerationFlags, value.WidthFactor, value.ObliqueAngle, value.LastHeight);
        internal static FontState Read(ShadowReader value) => new(value["FontFile"].Text, value["BigFont"].Text,
            value["FontFamilyName"].Text, Enum.Parse<FontStyle>(value["FontStyle"].Text), value["ExtendedFontData"].Exists,
            (int)value["ExtendedFontData"]["Flags"].Number(), Enum.Parse<TextStyleFlags>(value["Flags"].Text),
            (short)value["TextGenerationFlags"].Number(), value["WidthFactor"].Number(), value["ObliqueAngle"].Number(),
            value["LastHeight"].Exists ? value["LastHeight"].Number() : null);
    }

    private sealed class FontObserver : ICacheableEntityProjector
    {
        public long Revision => 0;
        internal List<FontState> Fonts { get; } = [];
        public bool TryProject(ShadowReader entity, out CadGeometry geometry, out string? diagnostic)
        {
            geometry = CadGeometry.Empty; diagnostic = null;
            if (entity.Type is "Text" or "MText") Fonts.Add(FontState.Read(entity["Style"]));
            return false;
        }
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
