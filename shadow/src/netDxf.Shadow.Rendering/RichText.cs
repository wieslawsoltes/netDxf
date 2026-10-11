using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

[Flags]
public enum TextDecoration { None = 0, Underline = 1, Overline = 2, StrikeThrough = 4 }

public sealed record RichTextStyle(string FontFamily, double Height, double WidthFactor = 1,
    double Tracking = 1, double ObliqueDegrees = 0, bool Bold = false, bool Italic = false,
    TextDecoration Decoration = TextDecoration.None, uint? Color = null, int VerticalAlignment = 0);

public sealed record RichTextInline(string Text, RichTextStyle Style, int SourceStart, int SourceLength,
    string? Lower = null, char StackSeparator = '\0');
public sealed record TextFormatDiagnostic(int Offset, string Code, string Message);
public sealed record RichTextDocument(ImmutableArray<RichTextInline> Inlines, ImmutableArray<TextFormatDiagnostic> Diagnostics);
public sealed record RichTextParseOptions(int MaximumCharacters = 1_000_000, int MaximumInlines = 100_000, int MaximumDepth = 64);

/// <summary>Single-pass, bounded DXF text interpreter. Formatting scopes are immutable; fields
/// are never executed. Unrecognized controls remain visible and produce diagnostics.</summary>
public static class RichText
{
    private static readonly ConditionalWeakTable<CadText, RichTextDocument> cache = new();
    public static RichTextDocument Parse(CadText text) => cache.GetValue(text,
        static value => Parse(value.Content, value.Multiline, new(value.FontFamily, value.Height)));

    public static RichTextDocument Parse(string source, bool multiline, RichTextStyle initial,
        RichTextParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(initial);
        options ??= new();
        if (options.MaximumCharacters < 1 || options.MaximumInlines < 1 || options.MaximumDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (source.Length > options.MaximumCharacters) throw new ArgumentException("Text input budget exceeded.", nameof(source));
        Validate(initial);
        var inlines = ImmutableArray.CreateBuilder<RichTextInline>();
        var issues = ImmutableArray.CreateBuilder<TextFormatDiagnostic>();
        var stack = new Stack<RichTextStyle>();
        var style = initial; var buffer = new StringBuilder(); var bufferStart = 0;
        for (var index = 0; index < source.Length; index++)
        {
            var start = index; var c = source[index];
            if (c == '\r') continue;
            if (multiline && c == '{')
            {
                Flush(index);
                if (stack.Count >= options.MaximumDepth) throw new ArgumentException("Text formatting depth budget exceeded.");
                stack.Push(style); continue;
            }
            if (multiline && c == '}')
            {
                Flush(index);
                if (stack.TryPop(out var previous)) style = previous;
                else { Append("}", index); Issue(index, "text.scope", "Unmatched closing formatting brace."); }
                continue;
            }
            if (c == '%' && index + 1 < source.Length && source[index + 1] == '<')
            {
                Flush(index);
                var depth = 1; var end = index + 2;
                for (; end + 1 < source.Length; end++)
                {
                    if (source[end] == '%' && source[end + 1] == '<')
                    {
                        if (++depth > options.MaximumDepth) throw new ArgumentException("Text field nesting budget exceeded.");
                        end++;
                    }
                    else if (source[end] == '>' && source[end + 1] == '%' && --depth == 0) { end += 2; break; }
                }
                end = Math.Min(source.Length, depth == 0 ? end : source.Length);
                Add(new(source[start..end], style, start, end - start));
                Issue(start, "text.field", "Field expression is displayed literally; no application field evaluator is configured.");
                index = end - 1; continue;
            }
            if (c == '%' && index + 2 < source.Length && source[index + 1] == '%')
            {
                var code = char.ToLowerInvariant(source[index + 2]);
                var symbol = code switch { 'd' => '°', 'p' => '±', 'c' => '∅', '%' => '%', _ => '\0' };
                if (symbol != '\0') { Append(symbol.ToString(), start); index += 2; continue; }
                if (code is 'u' or 'o' or 'k')
                {
                    Flush(index);
                    style = style with { Decoration = style.Decoration ^ (code == 'u' ? TextDecoration.Underline : code == 'o' ? TextDecoration.Overline : TextDecoration.StrikeThrough) };
                    index += 2; continue;
                }
                if (index + 4 < source.Length && int.TryParse(source.AsSpan(index + 2, 3), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                { Append(((char)number).ToString(), start); index += 4; continue; }
            }
            if (c != '\\' || index + 1 == source.Length) { Append(c.ToString(), start); continue; }
            var control = source[++index];
            if (control is '\\' or '{' or '}') { Append(control.ToString(), start); continue; }
            if (control == 'U' && index + 5 < source.Length && source[index + 1] == '+'
                && ushort.TryParse(source.AsSpan(index + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var unicode))
            { Append(((char)unicode).ToString(), start); index += 5; continue; }
            if (!multiline) { Append("\\" + control, start); continue; }
            if (control == '~') { Append("\u00a0", start); continue; }
            if (control is 'P' or 'N')
            {
                Append("\n", start);
                if (control == 'N') Issue(start, "text.column", "Column break is projected as a paragraph break.");
                continue;
            }
            if (control is 'L' or 'l' or 'O' or 'o' or 'K' or 'k')
            {
                Flush(start);
                var decoration = char.ToUpperInvariant(control) switch { 'L' => TextDecoration.Underline, 'O' => TextDecoration.Overline, _ => TextDecoration.StrikeThrough };
                style = style with { Decoration = char.IsUpper(control) ? style.Decoration | decoration : style.Decoration & ~decoration };
                continue;
            }
            if (!"ACcFfHhQqTtWwSsp".Contains(control))
            { Append("\\" + control, start); Issue(start, "text.control", $"Unrecognized formatting control \\{control}."); continue; }
            var terminator = index + 1;
            while (terminator < source.Length && source[terminator] != ';')
            {
                if (source[terminator] == '\\' && terminator + 1 < source.Length) terminator++;
                terminator++;
            }
            if (terminator == source.Length)
            { Append(source[start..], start); Issue(start, "text.terminator", "Unterminated formatting control remains visible."); break; }
            Flush(start);
            var argument = source[(index + 1)..terminator];
            try
            {
                if (control is 'S' or 's')
                {
                    var top = new StringBuilder(); var lower = new StringBuilder(); var divider = '\0';
                    for (var at = 0; at < argument.Length; at++)
                    {
                        var value = argument[at];
                        if (value == '\\' && at + 1 < argument.Length) { (divider == '\0' ? top : lower).Append(argument[++at]); continue; }
                        if (divider == '\0' && value is '/' or '#' or '^') { divider = value; continue; }
                        (divider == '\0' ? top : lower).Append(value);
                    }
                    Add(new(top.ToString(), style, start, terminator + 1 - start,
                        divider == '\0' ? null : lower.ToString(), divider));
                }
                else if (control is 'F' or 'f')
                {
                    var parts = argument.Split('|');
                    if (string.IsNullOrWhiteSpace(parts[0])) throw new FormatException("Font name is empty.");
                    var next = style with { FontFamily = parts[0] };
                    foreach (var part in parts.Skip(1))
                    {
                        if (part == "b1") next = next with { Bold = true }; else if (part == "b0") next = next with { Bold = false };
                        else if (part == "i1") next = next with { Italic = true }; else if (part == "i0") next = next with { Italic = false };
                        else if (part.StartsWith('c') || part.StartsWith('p')) Issue(start, "text.charset", "Font charset/pitch metadata is retained but system Unicode shaping is used.");
                        else Issue(start, "text.font", $"Unrecognized font qualifier '{part}'.");
                    }
                    style = next;
                }
                else if (control == 'p') Issue(start, "text.paragraph", "Paragraph-specific tab stops and indents require a paragraph layout extension.");
                else if (control == 'A')
                {
                    var alignment = int.Parse(argument, CultureInfo.InvariantCulture);
                    if (alignment is < 0 or > 2) throw new FormatException("Invalid inline vertical alignment.");
                    style = style with { VerticalAlignment = alignment };
                }
                else if (control is 'C' or 'c')
                {
                    var color = int.Parse(argument, CultureInfo.InvariantCulture);
                    if (control == 'c')
                    {
                        if (color is < 0 or > 0xffffff) throw new FormatException("True color is outside the RGB range.");
                        style = style with { Color = 0xff000000u | (uint)color };
                    }
                    else if (color is 0 or 256) style = style with { Color = null };
                    else if (IndexedColor(color) is { } rgb) style = style with { Color = rgb };
                    else { Issue(start, "text.palette", $"Indexed color {color} requires a palette extension; the inherited paint is used."); style = style with { Color = null }; }
                }
                else
                {
                    var relative = argument.EndsWith('x') || argument.EndsWith('X');
                    var value = double.Parse(relative ? argument[..^1] : argument, NumberStyles.Float, CultureInfo.InvariantCulture);
                    var next = char.ToUpperInvariant(control) switch
                    {
                        'H' => style with { Height = relative ? style.Height * value : value },
                        'W' => style with { WidthFactor = relative ? style.WidthFactor * value : value },
                        'T' => style with { Tracking = relative ? style.Tracking * value : value },
                        'Q' => style with { ObliqueDegrees = value },
                        _ => style
                    };
                    Validate(next); style = next;
                }
            }
            catch (Exception error) when (error is FormatException or OverflowException or ArgumentOutOfRangeException)
            { Append(source[start..(terminator + 1)], start); Issue(start, "text.format", error.Message); }
            index = terminator;
        }
        Flush(source.Length);
        if (stack.Count != 0) Issue(source.Length, "text.scope", "Unclosed formatting scopes end at the end of the text.");
        return new(inlines.ToImmutable(), issues.ToImmutable());

        void Append(string value, int offset) { if (buffer.Length == 0) bufferStart = offset; buffer.Append(value); }
        void Flush(int end)
        {
            if (buffer.Length == 0) return;
            Add(new(buffer.ToString(), style, bufferStart, Math.Max(0, end - bufferStart))); buffer.Clear();
        }
        void Add(RichTextInline inline)
        {
            if (inlines.Count >= options.MaximumInlines) throw new ArgumentException("Text inline budget exceeded.");
            inlines.Add(inline);
        }
        void Issue(int offset, string code, string message)
        {
            if (issues.Count >= options.MaximumInlines) throw new ArgumentException("Text diagnostic budget exceeded.");
            issues.Add(new(offset, code, message));
        }
    }

    private static void Validate(RichTextStyle style)
    {
        if (!(style.Height > 0) || !double.IsFinite(style.Height) || style.Height > 1e9
            || !(style.WidthFactor > 0) || !double.IsFinite(style.WidthFactor) || style.WidthFactor > 1e4
            || !(style.Tracking > 0) || !double.IsFinite(style.Tracking) || style.Tracking > 10
            || !double.IsFinite(style.ObliqueDegrees) || Math.Abs(style.ObliqueDegrees) >= 89)
            throw new ArgumentOutOfRangeException(nameof(style), "Invalid or excessive text size, width, tracking or oblique angle.");
    }
    private static uint? IndexedColor(int index) => index switch
    {
        1 => 0xffff0000, 2 => 0xffffff00, 3 => 0xff00ff00, 4 => 0xff00ffff,
        5 => 0xff0000ff, 6 => 0xffff00ff, 7 => 0xffffffff, 8 => 0xff808080, 9 => 0xffc0c0c0, _ => null
    };
}
