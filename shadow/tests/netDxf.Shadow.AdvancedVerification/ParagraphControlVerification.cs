using System.Globalization;
using netDxf.Entities;
using netDxf.Shadow.Rendering;

internal static class ParagraphControlVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        foreach (var (code, alignment) in new[]
        {
            ("l", ParagraphAlignment.Left), ("c", ParagraphAlignment.Center),
            ("r", ParagraphAlignment.Right), ("j", ParagraphAlignment.Justified),
            ("d", ParagraphAlignment.Distributed)
        })
            tests.Add(($"paragraph/control alignment {code}", () =>
                Require(MTextParagraphControl.Parse("xq" + code).Alignment == alignment)));
        tests.Add(("paragraph/indents, spacing, exponents and inheritance", () =>
        {
            var initial = MTextParagraphControl.Parse("xi-1.5,l2,r3,b0.5,a0.75");
            var next = MTextParagraphControl.Parse("l4e0,qc", initial);
            Require(initial.FirstLineIndent == -1.5 && initial.LeftIndent == 2 && initial.RightIndent == 3);
            Require(next.LeftIndent == 4 && next.RightIndent == 3 && next.FirstLineIndent == -1.5);
            Require(next.SpaceBefore == 0.5 && next.SpaceAfter == 0.75 && next.Alignment == ParagraphAlignment.Center);
        }));
        tests.Add(("paragraph/tab stop alignment and canonical ordering", () =>
        {
            var value = MTextParagraphControl.Parse("xtc12,4,r8");
            Require(value.Tabs.Select(t => t.Position).SequenceEqual(new double[] { 4, 8, 12 }));
            Require(value.Tabs.Select(t => t.Alignment).SequenceEqual(new[]
                { ParagraphTabAlignment.Left, ParagraphTabAlignment.Right, ParagraphTabAlignment.Center }));
        }));
        tests.Add(("paragraph/property resets do not mutate inherited state", () =>
        {
            var before = MTextParagraphControl.Parse("i2,l3,r4,qc,se6,t4,8");
            var after = MTextParagraphControl.Parse("i*,l*,r*,q*,s*,t", before);
            Require(after == RichTextParagraphStyle.Default);
            Require(before.LeftIndent == 3 && before.Tabs.Length == 2 && before.LineSpacingValue == 6);
            Require(MTextParagraphControl.Parse("*", before) == RichTextParagraphStyle.Default);
        }));
        foreach (var (code, mode) in new[]
        {
            ("a", ParagraphLineSpacing.AtLeast), ("e", ParagraphLineSpacing.Exact), ("m", ParagraphLineSpacing.Multiple)
        })
            tests.Add(($"paragraph/line spacing {code}", () =>
            {
                var value = MTextParagraphControl.Parse("s" + code + "2.5");
                Require(value.LineSpacing == mode && value.LineSpacingValue == 2.5);
            }));
        tests.Add(("paragraph/invariant numeric parsing", () =>
        {
            var old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
                Require(MTextParagraphControl.Parse("l1.25").LeftIndent == 1.25);
                Throws<FormatException>(() => MTextParagraphControl.Parse("l1,25"));
            }
            finally { CultureInfo.CurrentCulture = old; }
        }));
        tests.Add(("paragraph/invalid commands stay literal without partial state changes", () =>
        {
            foreach (var command in new[] { "l2,r-1", "lNaN", "lInfinity", "qa", "se0", "t4,4", "t-2", "z3" })
            {
                var literal = "\\p" + command + ";";
                var parsed = RichText.Parse(literal + "visible", true, new("sans-serif", 10));
                Require(parsed.Diagnostics.Any(d => d.Code == "text.format"));
                Require(string.Concat(parsed.Inlines.Select(i => i.Text)) == literal + "visible");
                Require(parsed.Inlines.All(i => i.Style.Paragraph == RichTextParagraphStyle.Default));
            }
        }));
        tests.Add(("paragraph/format scopes restore paragraph context", () =>
        {
            var parsed = RichText.Parse(@"{\pl2;A}\PB", true, new("sans-serif", 10));
            Require(parsed.Inlines.Single(i => i.Text == "A").Style.Paragraph.LeftIndent == 2);
            Require(parsed.Inlines.Single(i => i.Text == "\nB").Style.Paragraph.LeftIndent == 0);
            Require(parsed.Inlines.Single(i => i.IsParagraphControl).SourceLength == 5);
            Require(parsed.Diagnostics.IsEmpty);
        }));
        tests.Add(("paragraph/caret tabs and newlines are not interpreted in TEXT", () =>
        {
            var mtext = RichText.Parse("a^Ib^Jc^Md", true, new("sans-serif", 10));
            Require(string.Concat(mtext.Inlines.Select(i => i.Text)) == "a\tb\ncd");
            var text = RichText.Parse(@"a^Ib\pl2;", false, new("sans-serif", 10));
            Require(string.Concat(text.Inlines.Select(i => i.Text)) == @"a^Ib\pl2;");
        }));
        tests.Add(("paragraph/netDxf authoring controls are consumed without diagnostics", () =>
        {
            var entity = new MText();
            entity.StartParagraph(new MTextParagraphOptions
            {
                Alignment = MTextParagraphAlignment.Center, LeftIndent = 2,
                RightIndent = 1, FirstLineIndent = -1, SpacingBefore = 0.5,
                SpacingAfter = 0.75, LineSpacingStyle = MTextLineSpacingStyle.Multiple, LineSpacingFactor = 1.5
            });
            entity.Write("writer");
            var parsed = RichText.Parse(entity.Value, true, new("sans-serif", 10));
            Require(parsed.Diagnostics.IsEmpty);
            var p = parsed.Inlines.Single(i => i.Text == "writer").Style.Paragraph;
            Require(p.Alignment == ParagraphAlignment.Center && p.LeftIndent == 2 && p.RightIndent == 1 && p.FirstLineIndent == -1);
            Require(p.SpaceBefore == 0.5 && p.SpaceAfter == 0.75 && p.LineSpacing == ParagraphLineSpacing.Multiple && p.LineSpacingValue == 1.5);
        }));
        tests.Add(("paragraph/control tokens and tab tables obey budgets", () =>
        {
            Throws<ArgumentException>(() => RichText.Parse(@"\pl1;\pl2;x", true, new("sans-serif", 10), new(MaximumInlines: 2)));
            Throws<ArgumentException>(() => MTextParagraphControl.Parse(new string('x', 65537)));
            Throws<ArgumentException>(() => MTextParagraphControl.Parse("t" + string.Join(',', Enumerable.Range(1, 4097))));
        }));
    }
    private static void Require(bool value)
    {
        if (!value) throw new InvalidOperationException("Paragraph control contract failed.");
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
