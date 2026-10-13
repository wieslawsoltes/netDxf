using System.Collections.Immutable;
using System.Globalization;

namespace netDxf.Shadow.Rendering;

public enum ParagraphAlignment { Left, Center, Right, Justified, Distributed }
public enum ParagraphLineSpacing { Default, AtLeast, Exact, Multiple }
public enum ParagraphTabAlignment { Left, Center, Right }

/// <summary>A tab position in multiples of the initial entity text height.</summary>
public readonly record struct ParagraphTabStop(double Position, ParagraphTabAlignment Alignment = ParagraphTabAlignment.Left);

/// <summary>
/// Detached paragraph settings. Indents, tab positions and paragraph spacing are
/// multiples of the initial entity height, never the current inline height. Exact
/// and minimum line distances are drawing units; Multiple is a dimensionless factor.
/// </summary>
public sealed record RichTextParagraphStyle
{
    public static RichTextParagraphStyle Default { get; } = new();
    public double FirstLineIndent { get; init; }
    public double LeftIndent { get; init; }
    public double RightIndent { get; init; }
    public ParagraphAlignment Alignment { get; init; }
    public double SpaceBefore { get; init; }
    public double SpaceAfter { get; init; }
    public ParagraphLineSpacing LineSpacing { get; init; }
    public double LineSpacingValue { get; init; } = 1;
    public ImmutableArray<ParagraphTabStop> Tabs { get; init; } = [];

    // ImmutableArray's default equality compares backing storage, not values.
    // Independently parsed paragraphs and property resets must compare semantically.
    public bool Equals(RichTextParagraphStyle? other)
    {
        if (ReferenceEquals(this, other)) return true;
        return other is not null
            && FirstLineIndent.Equals(other.FirstLineIndent)
            && LeftIndent.Equals(other.LeftIndent) && RightIndent.Equals(other.RightIndent)
            && Alignment == other.Alignment
            && SpaceBefore.Equals(other.SpaceBefore) && SpaceAfter.Equals(other.SpaceAfter)
            && LineSpacing == other.LineSpacing && LineSpacingValue.Equals(other.LineSpacingValue)
            && Tabs.IsDefault == other.Tabs.IsDefault
            && (Tabs.IsDefault || Tabs.AsSpan().SequenceEqual(other.Tabs.AsSpan()));
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(FirstLineIndent); hash.Add(LeftIndent); hash.Add(RightIndent);
        hash.Add(Alignment); hash.Add(SpaceBefore); hash.Add(SpaceAfter);
        hash.Add(LineSpacing); hash.Add(LineSpacingValue); hash.Add(Tabs.IsDefault);
        if (!Tabs.IsDefault) foreach (var tab in Tabs) hash.Add(tab);
        return hash.ToHashCode();
    }

    public void Validate()
    {
        if (!double.IsFinite(FirstLineIndent) || Math.Abs(FirstLineIndent) > 10000
            || !ValidIndent(LeftIndent) || !ValidIndent(RightIndent)
            || !ValidIndent(SpaceBefore) || !ValidIndent(SpaceAfter)
            || !double.IsFinite(LineSpacingValue) || LineSpacingValue <= 0 || LineSpacingValue > 10000
            || !Enum.IsDefined(Alignment) || !Enum.IsDefined(LineSpacing))
            throw new ArgumentOutOfRangeException(nameof(RichTextParagraphStyle), "Invalid paragraph dimensions or alignment.");
        if (Tabs.IsDefault || Tabs.Length > 4096)
            throw new ArgumentOutOfRangeException(nameof(Tabs), "Tab settings must be initialized and within the 4096-stop budget.");
        var previous = 0.0;
        foreach (var tab in Tabs)
        {
            if (!double.IsFinite(tab.Position) || tab.Position <= previous || tab.Position > 10000 || !Enum.IsDefined(tab.Alignment))
                throw new ArgumentOutOfRangeException(nameof(Tabs), "Tab positions must be positive, distinct and increasing.");
            previous = tab.Position;
        }
        static bool ValidIndent(double value) => double.IsFinite(value) && value >= 0 && value <= 10000;
    }
}

/// <summary>Bounded parser for the argument of a DXF MTEXT paragraph control.</summary>
public static class MTextParagraphControl
{
    public static RichTextParagraphStyle Parse(string argument, RichTextParagraphStyle? inherited = null)
    {
        ArgumentNullException.ThrowIfNull(argument);
        var result = inherited ?? RichTextParagraphStyle.Default;
        result.Validate();
        if (argument.Length > 65536) throw new ArgumentException("Paragraph control budget exceeded.", nameof(argument));
        if (argument.AsSpan().Trim().SequenceEqual("*")) return RichTextParagraphStyle.Default;
        var span = argument.AsSpan();
        var tabMode = false;
        ImmutableArray<ParagraphTabStop>.Builder? tabs = null;
        var start = 0;
        while (start < span.Length)
        {
            var end = span[start..].IndexOf(',');
            end = end < 0 ? span.Length : start + end;
            var token = span[start..end].Trim();
            start = end + 1;
            // 'x' is an exporter marker, not a numerical multiplier.
            while (!token.IsEmpty && token[0] == 'x') token = token[1..];
            if (token.IsEmpty) continue;
            if (tabMode) { AddTab(token); continue; }
            var value = token[1..];
            switch (token[0])
            {
                case 'i': result = result with { FirstLineIndent = Number(value, 0) }; break;
                case 'l': result = result with { LeftIndent = Number(value, 0) }; break;
                case 'r': result = result with { RightIndent = Number(value, 0) }; break;
                case 'b': result = result with { SpaceBefore = Number(value, 0) }; break;
                case 'a': result = result with { SpaceAfter = Number(value, 0) }; break;
                case 'q':
                    result = result with { Alignment = value.ToString() switch
                    {
                        "l" or "*" => ParagraphAlignment.Left,
                        "c" => ParagraphAlignment.Center,
                        "r" => ParagraphAlignment.Right,
                        "j" => ParagraphAlignment.Justified,
                        "d" => ParagraphAlignment.Distributed,
                        _ => throw new FormatException("Unknown paragraph alignment.")
                    } };
                    break;
                case 's':
                    if (value.SequenceEqual("*"))
                        result = result with { LineSpacing = ParagraphLineSpacing.Default, LineSpacingValue = 1 };
                    else
                    {
                        if (value.Length < 2) throw new FormatException("Missing paragraph line spacing value.");
                        var spacing = value[0] switch
                        {
                            'a' => ParagraphLineSpacing.AtLeast,
                            'e' => ParagraphLineSpacing.Exact,
                            'm' => ParagraphLineSpacing.Multiple,
                            _ => throw new FormatException("Unknown paragraph line spacing mode.")
                        };
                        result = result with { LineSpacing = spacing, LineSpacingValue = Number(value[1..], 1) };
                    }
                    break;
                case 't':
                    tabMode = true;
                    tabs = ImmutableArray.CreateBuilder<ParagraphTabStop>();
                    if (!value.IsEmpty && !value.SequenceEqual("*")) AddTab(value);
                    break;
                default: throw new FormatException($"Unknown paragraph property '{token[0]}'.");
            }
        }
        if (tabs is not null)
        {
            // Normalize authoring order once. Duplicate stops are rejected, not silently
            // assigned exporter-dependent priority. Layout searches this sorted table.
            result = result with { Tabs = tabs.OrderBy(t => t.Position).ToImmutableArray() };
        }
        result.Validate();
        return result;

        void AddTab(ReadOnlySpan<char> token)
        {
            if (tabs!.Count >= 4096) throw new ArgumentException("Paragraph tab budget exceeded.", nameof(argument));
            var alignment = token[0] switch
            {
                'c' => ParagraphTabAlignment.Center,
                'r' => ParagraphTabAlignment.Right,
                _ => ParagraphTabAlignment.Left
            };
            if (token[0] is 'c' or 'r') token = token[1..];
            tabs.Add(new(Number(token, 0), alignment));
        }
        static double Number(ReadOnlySpan<char> value, double reset)
        {
            if (value.SequenceEqual("*")) return reset;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                throw new FormatException("Paragraph values must be finite invariant numbers.");
            return number;
        }
    }
}
