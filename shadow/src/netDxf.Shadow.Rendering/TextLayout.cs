using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

public sealed record TextLine(string Text, double X, double Y);

/// <summary>Basic DXF escapes and multiline layout. Raw rich formatting remains in the snapshot.</summary>
public static class TextLayout
{
    public static string PlainText(string source, bool mtext)
    {
        ArgumentNullException.ThrowIfNull(source);
        var output = new StringBuilder(source.Length);
        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (character == '\r') continue;
            if (character == '%' && index + 2 < source.Length && source[index + 1] == '%')
            {
                var replacement = char.ToLowerInvariant(source[index + 2]) switch { 'd' => '°', 'p' => '±', 'c' => 'Ø', _ => '\0' };
                if (replacement != '\0') { output.Append(replacement); index += 2; continue; }
            }
            if (character == '\\' && index + 1 < source.Length)
            {
                var code = source[++index];
                if (code == 'U' && index + 5 < source.Length && source[index + 1] == '+' && ushort.TryParse(source.AsSpan(index + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var unicode))
                { output.Append((char)unicode); index += 5; continue; }
                if (code is '\\' or '{' or '}') { output.Append(code); continue; }
                if (mtext)
                {
                    if (code == 'P') { output.Append('\n'); continue; }
                    if (code == '~') { output.Append('\u00a0'); continue; }
                    if (code is 'L' or 'l' or 'O' or 'o' or 'K' or 'k') continue;
                    if ("ACcFfHhQqTtWwSsp".Contains(code))
                    {
                        var terminator = source.IndexOf(';', index + 1);
                        if (terminator >= 0)
                        {
                            if (code == 'S') output.Append(source.AsSpan(index + 1, terminator - index - 1).ToString().Replace('^', '/').Replace('#', '/'));
                            index = terminator; continue;
                        }
                    }
                }
                output.Append('\\').Append(code); continue;
            }
            if (mtext && (character is '{' or '}')) continue;
            output.Append(character);
        }
        return output.ToString();
    }

    public static ImmutableArray<TextLine> Layout(CadText text, Func<string, double> measure)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);
        var wrappedRows = new List<string>();
        foreach (var paragraph in PlainText(text.Content, text.Multiline).Split('\n'))
        {
            if (!text.Multiline || text.Width <= 0 || measure(paragraph) <= text.Width)
            { wrappedRows.Add(paragraph); continue; }
            var currentText = string.Empty;
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = currentText.Length == 0 ? word : currentText + " " + word;
                if (currentText.Length > 0 && measure(candidate) > text.Width)
                { wrappedRows.Add(currentText); currentText = word; }
                else currentText = candidate;
            }
            wrappedRows.Add(currentText);
        }
        var rowHeight = text.Height * 1.2;
        var totalHeight = wrappedRows.Count * rowHeight;
        var alignment = text.Alignment;
        var baseline = alignment.StartsWith("Top", StringComparison.Ordinal) ? text.Height * 0.8 :
            alignment.StartsWith("Middle", StringComparison.Ordinal) ? -totalHeight / 2 + text.Height * 0.8 :
            alignment.StartsWith("Bottom", StringComparison.Ordinal) ? -totalHeight + text.Height * 0.8 : 0;
        var result = ImmutableArray.CreateBuilder<TextLine>();
        foreach (var rowText in wrappedRows)
        {
            var rowWidth = measure(rowText);
            var left = alignment.EndsWith("Center", StringComparison.Ordinal) || alignment == "Middle" ? -rowWidth / 2 : alignment.EndsWith("Right", StringComparison.Ordinal) ? -rowWidth : 0;
            result.Add(new TextLine(rowText, left, baseline)); baseline += rowHeight;
        }
        return result.ToImmutable();
    }
}
