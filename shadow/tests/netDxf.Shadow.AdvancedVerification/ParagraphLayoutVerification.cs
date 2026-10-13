using System.Collections.Immutable;
using System.Globalization;
using netDxf.Shadow;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Rendering;

internal static class ParagraphLayoutVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("paragraph/layout/value equality and hash use ordered tab contents", () =>
        {
            var a = MTextParagraphControl.Parse("l2,t4,c8,r12");
            var b = MTextParagraphControl.Parse("l2,tr12,4,c8");
            Require(a == b && a.GetHashCode() == b.GetHashCode());
            Require(new HashSet<RichTextParagraphStyle> { a, b }.Count == 1);
            Require(a != (b with { LeftIndent = 3 }));
            Require(a != (b with { Tabs = [new(4), new(8), new(12, ParagraphTabAlignment.Right)] }));
            Require((a with { Tabs = default }) != (a with { Tabs = [] }));
        }));
        tests.Add(("paragraph/layout/legacy default preserves portable row metrics", () =>
        {
            var r = Layout(@"a\Pb");
            Require(r.Lines.Length == 2); Near(r.Lines[0].Baseline, 8); Near(r.Lines[1].Baseline, 20);
            Near(r.LayoutBounds.Height, 22);
        }));
        tests.Add(("paragraph/layout/hanging indent and right boundary survive wrapping", () =>
        {
            var r = Layout(@"\pi-1,l2,r1;aa bb cc dd ee ff gg");
            Require(r.Lines.Length == 2); Near(r.Lines[0].Left, 10); Near(r.Lines[1].Left, 20);
            Require(r.Lines.All(line => line.Right <= 90));
        }));
        tests.Add(("paragraph/layout/negative first indent clamps at the column origin", () =>
        {
            var r = Layout(@"\pi-3,l1;aa bb cc dd", 30);
            Near(r.Lines[0].Left, 0); Require(r.Lines.Length > 1);
            Require(r.Lines.Skip(1).All(line => line.Left == 10));
        }));
        foreach (var (code, x) in new[] { ("l", 0.0), ("c", 41.0), ("r", 82.0) })
            tests.Add(($"paragraph/layout/paragraph alignment {code}", () =>
                Near(Layout("\\pq" + code + ";a b").Runs[0].X, x)));
        tests.Add(("paragraph/layout/justification stretches wrapped rows but not the last", () =>
        {
            var r = Layout(@"\pqj;aa bb cc dd", 42);
            Require(r.Lines.Length == 2); Near(r.Lines[0].Right, 42); Near(r.Lines[1].Right, 30);
            Near(r.Runs.Single(run => run.Text == "bb").X, 30);
        }));
        tests.Add(("paragraph/layout/distribution includes the final row without splitting words", () =>
        {
            Near(Layout(@"\pqd;aa bb", 42).Lines.Single().Right, 42);
            var r = Layout("\\pqd;ffi\u0301");
            Require(r.Runs.Length == 1 && r.Runs[0].Text == "ffi\u0301");
        }));
        tests.Add(("paragraph/layout/authored terminal spaces affect center alignment", () =>
        {
            var r = Layout(@"\pqc;a ");
            Near(r.Runs[0].X, 44); Require(r.Runs[^1].Text == " ");
        }));
        tests.Add(("paragraph/layout/soft wrap consumes only breakable separator spaces", () =>
        {
            var r = Layout("aa bb", 12);
            Require(r.Lines.Length == 2 && r.Runs.All(run => run.Text != " "));
            var noBreak = Layout(@"a\~b", 6);
            Require(noBreak.Runs.Length == 1 && noBreak.Runs[0].Text == "a\u00a0b");
            Near(noBreak.Lines.Single().Right, 18);
        }));
        foreach (var source in new[] { @"{\pl2;A\P}B", @"{\pl2;A}\PB", @"{\pl2;A\PB\P}C" })
            tests.Add(($"paragraph/layout/scope boundary {source}", () =>
            {
                var r = Layout(source);
                Near(r.Lines[0].Left, 20); Near(r.Lines[^1].Left, 0);
            }));
        tests.Add(("paragraph/layout/empty paragraphs retain inherited control state", () =>
        {
            var r = Layout(@"\pl2;A\P\P");
            Require(r.Lines.Length == 3 && r.Lines.All(line => line.Left == 20));
            var empty = Layout(@"\pl2;");
            Require(empty.Runs.IsEmpty && empty.Lines.Length == 1); Near(empty.Lines[0].Left, 20);
        }));
        tests.Add(("paragraph/layout/default tabs advance to absolute stops", () =>
        {
            var r = Layout("A\tB\tC");
            Near(Run(r, "A").X, 0); Near(Run(r, "B").X, 40); Near(Run(r, "C").X, 80);
        }));
        foreach (var (stop, x) in new[] { ("4", 40.0), ("c8", 74.0), ("r8", 68.0) })
            tests.Add(($"paragraph/layout/aligned tab {stop}", () =>
                Near(Run(Layout("\\pt" + stop + ";A\tBB"), "BB").X, x)));
        tests.Add(("paragraph/layout/wide aligned fields skip overlapping earlier stops", () =>
            Near(Run(Layout("\\ptc1,c2,c8;AAAA\tBB"), "BB").X, 74)));
        tests.Add(("paragraph/layout/right tab measures across inline color boundaries", () =>
        {
            var r = Layout("\\ptr8;A\t{\\C1;B}C");
            Near(Run(r, "B").X, 68); Near(Run(r, "C").X, 74);
            Require(Run(r, "B").Style.Color == 0xffff0000);
        }));
        tests.Add(("paragraph/layout/consecutive tabs retain empty fields", () =>
            Near(Run(Layout("\\pt4,8;A\t\tB"), "B").X, 80)));
        tests.Add(("paragraph/layout/tab stops are not stretched by distribution", () =>
            Near(Run(Layout("\\pqd,t4;A\tB"), "B").X, 40)));
        foreach (var vertical in new[] { "Top", "Middle", "Bottom" })
            foreach (var horizontal in new[] { "Left", "Center", "Right" })
                tests.Add(($"paragraph/layout/attachment {vertical}{horizontal} transports layout bounds", () =>
                {
                    var r = Layout("a", alignment: vertical + horizontal);
                    var x = horizontal == "Left" ? 0 : horizontal == "Center" ? -50 : -100;
                    var y = vertical == "Top" ? 0 : vertical == "Middle" ? -5 : -10;
                    Near(r.LayoutBounds.MinX, x); Near(r.LayoutBounds.MaxX, x + 100);
                    Near(r.LayoutBounds.MinY, y); Near(r.LayoutBounds.MaxY, y + 10);
                    Near(r.Runs[0].X, x); Near(r.Lines[0].Baseline, y + 8);
                }));
        tests.Add(("paragraph/layout/overflow does not change declared attachment width", () =>
        {
            var r = Layout("longlong", 12, "TopCenter");
            Near(r.Runs[0].X, -6); Near(r.LayoutBounds.MinX, -6); Near(r.LayoutBounds.MaxX, 6);
            Require(r.Bounds.MaxX > r.LayoutBounds.MaxX);
        }));
        tests.Add(("paragraph/layout/exact spacing applies across explicit paragraph breaks", () =>
        {
            var r = Layout(@"\pse30;A\PB\PC");
            Near(r.Lines[1].Baseline - r.Lines[0].Baseline, 30);
            Near(r.Lines[2].Baseline - r.Lines[1].Baseline, 30);
        }));
        foreach (var (control, distance) in new[] { ("se5", 5.0), ("sa5", 50.0 / 3), ("sm2", 100.0 / 3) })
            tests.Add(($"paragraph/layout/wrapped line spacing {control}", () =>
            {
                var r = Layout("\\p" + control + ";aa bb cc", 12);
                Require(r.Lines.Length == 3);
                Near(r.Lines[1].Baseline - r.Lines[0].Baseline, distance);
            }));
        tests.Add(("paragraph/layout/multiple spacing observes the largest inline height", () =>
        {
            var r = Layout(@"\psm2;small {\H3x;tall}", 36);
            Require(r.Lines.Length == 2); Near(r.Lines[1].Baseline - r.Lines[0].Baseline, 100);
        }));
        tests.Add(("paragraph/layout/before and after spacing are additive only at paragraph boundaries", () =>
        {
            var r = Layout(@"\pb0.5,a0.75;A\PB");
            Near(r.Lines[0].Baseline, 13); Near(r.Lines[1].Baseline, 37.5); Near(r.LayoutBounds.Height, 47);
        }));
        tests.Add(("paragraph/layout/entity exact spacing and paragraph reset share a baseline policy", () =>
        {
            var text = Text(@"\pse5;a\P\ps*;b") with { LineSpacing = new(2, true) };
            var r = RichTextLayout.Layout(text, RichTextLayout.ApproximateMetrics);
            Near(r.Lines[1].Baseline - r.Lines[0].Baseline, 100.0 / 3);
        }));
        tests.Add(("paragraph/layout/entity at-least expands for tall runs while exact does not", () =>
        {
            var text = Text(@"a\P{\H3x;B}") with { LineSpacing = new(2) };
            var atLeast = RichTextLayout.Layout(text, RichTextLayout.ApproximateMetrics);
            var exact = RichTextLayout.Layout(text with { LineSpacing = new(2, true) }, RichTextLayout.ApproximateMetrics);
            Near(atLeast.Lines[1].Baseline - atLeast.Lines[0].Baseline, 50);
            Near(exact.Lines[1].Baseline - exact.Lines[0].Baseline, 100.0 / 3);
        }));
        tests.Add(("paragraph/layout/aligned tab lookup agrees with a linear independent oracle", TabOracle));
        tests.Add(("paragraph/layout/many tab fields are measured once", () =>
        {
            var calls = 0;
            var text = Text(string.Join('\t', Enumerable.Repeat("ab", 2000)), 0);
            var r = RichTextLayout.Layout(text, (style, word) =>
            {
                calls++; return RichTextLayout.ApproximateMetrics(style, word);
            });
            Require(calls == 2000 && r.Runs.Length == 2000);
        }));
        tests.Add(("paragraph/layout/budgets invalid settings and exhausted columns fail explicitly", () =>
        {
            Throws<InvalidOperationException>(() => RichTextLayout.Layout(Text(@"a\P\P"), RichTextLayout.ApproximateMetrics, maximumRuns: 2));
            foreach (var factor in new[] { double.NaN, double.PositiveInfinity, 0.0, 0.1, 5.0 })
                Throws<ArgumentOutOfRangeException>(() => RichTextLayout.Layout(Text("a") with { LineSpacing = new(factor) }, RichTextLayout.ApproximateMetrics));
            var r = Layout(@"\pl2,r2;visible", 10);
            Require(r.Diagnostics.Any(d => d.Code == "text.paragraph.width") && r.Runs.Single().Text == "visible");
        }));
        tests.Add(("paragraph/layout/stretched underline follows justified space width", () =>
        {
            var r = Layout(@"\pqd;\Laa bb\l", 42);
            Require(r.Rules.Length == 3);
            Near(r.Rules.Max(rule => rule.X2), 42);
            Require(r.Bounds.MaxX >= 42);
        }));
    }

    private static void TabOracle()
    {
        var random = new Random(941);
        for (var iteration = 0; iteration < 128; iteration++)
        {
            var stops = Enumerable.Range(1, 64).Select(i => new ParagraphTabStop(i, (ParagraphTabAlignment)random.Next(3))).ToArray();
            var a = new string('A', random.Next(1, 120));
            var b = new string('B', random.Next(1, 80));
            var cursor = a.Length * 6.0; var width = b.Length * 6.0;
            var expected = double.NaN;
            foreach (var stop in stops)
            {
                var position = stop.Position * 10;
                var start = position - (stop.Alignment == ParagraphTabAlignment.Center ? width / 2 : stop.Alignment == ParagraphTabAlignment.Right ? width : 0);
                if (position > cursor && start >= cursor) { expected = start; break; }
            }
            if (double.IsNaN(expected)) expected = (Math.Floor(Math.Max(cursor, stops[^1].Position * 10) / 40) + 1) * 40;
            var command = string.Join(',', stops.Select(stop =>
                (stop.Alignment == ParagraphTabAlignment.Center ? "c" : stop.Alignment == ParagraphTabAlignment.Right ? "r" : "") + stop.Position.ToString(CultureInfo.InvariantCulture)));
            var r = Layout("\\pt" + command + ";" + a + "\t" + b, 0);
            Near(Run(r, b).X, expected);
        }
    }

    private static CadText Text(string value, double width = 100, string alignment = "TopLeft")
        => new(value, Vec3.Zero, Vec3.UnitX, Vec3.UnitY, 10, "sans-serif", alignment, true, width);
    private static RichTextLayoutResult Layout(string value, double width = 100, string alignment = "TopLeft")
        => RichTextLayout.Layout(Text(value, width, alignment), RichTextLayout.ApproximateMetrics);
    private static PositionedTextRun Run(RichTextLayoutResult result, string value) => result.Runs.Single(run => run.Text == value);
    private static void Require(bool value, string message = "Paragraph layout contract failed.")
    { if (!value) throw new InvalidOperationException(message); }
    private static void Near(double actual, double expected)
        => Require(double.IsFinite(actual) && Math.Abs(actual - expected) < 1e-7, $"Expected {expected:R}, got {actual:R}.");
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
