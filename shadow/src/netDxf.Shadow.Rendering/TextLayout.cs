using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

public sealed record TextLine(string Text, double X, double Y);

/// <summary>Basic DXF escapes and multiline layout. Raw formatting remains available for richer shapers.</summary>
public static class TextLayout
{
    public static string PlainText(string source, bool mtext)
    {
        var output = new StringBuilder(source.Length);
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (c == '\r') continue;
            if (c == '%' && i + 2 < source.Length && source[i + 1] == '%')
            {
                var replacement = char.ToLowerInvariant(source[i + 2]) switch { 'd' => '°', 'p' => '±', 'c' => 'Ø', _ => '\0' };
                if (replacement != '\0') { output.Append(replacement); i += 2; continue; }
            }
            if (c == '\\' && i + 1 < source.Length)
            {
                var code = source[++i];
                if (code == 'U' && i + 5 < source.Length && source[i + 1] == '+' && ushort.TryParse(source.AsSpan(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var unicode))
                { output.Append((char)unicode); i += 5; continue; }
                if (code is '\\' or '{' or '}') { output.Append(code); continue; }
                if (mtext)
                {
                    if (code == 'P') { output.Append('\n'); continue; }
                    if (code == '~') { output.Append('\u00a0'); continue; }
                    if (code is 'L' or 'l' or 'O' or 'o' or 'K' or 'k') continue;
                    if ("ACcFfHhQqTtWwSsp".Contains(code))
                    {
                        var end = source.IndexOf(';', i + 1);
                        if (end >= 0)
                        {
                            if (code == 'S') output.Append(source.AsSpan(i + 1, end - i - 1).ToString().Replace('^', '/').Replace('#', '/'));
                            i = end; continue;
                        }
                    }
                }
                output.Append('\\').Append(code); continue;
            }
            if (mtext && c is '{' or '}') continue;
            output.Append(c);
        }
        return output.ToString();
    }

    public static ImmutableArray<TextLine> Layout(CadText text, Func<string, double> measure)
    {
        var lines = new List<string>();
        foreach (var paragraph in PlainText(text.Content, text.Multiline).Split('\n'))
        {
            if (!text.Multiline || text.Width <= 0 || measure(paragraph) <= text.Width) { lines.Add(paragraph); continue; }
            var line = string.Empty;
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && measure(candidate) > text.Width) { lines.Add(line); line = word; }
                else line = candidate;
            }
            lines.Add(line);
        }
        var lineHeight = text.Height * 1.2;
        var totalHeight = lines.Count * lineHeight;
        var alignment = text.Alignment;
        var y = alignment.StartsWith("Top", StringComparison.Ordinal) ? text.Height * 0.8 :
            alignment.StartsWith("Middle", StringComparison.Ordinal) ? -totalHeight / 2 + text.Height * 0.8 :
            alignment.StartsWith("Bottom", StringComparison.Ordinal) ? -totalHeight + text.Height * 0.8 : 0;
        var result = ImmutableArray.CreateBuilder<TextLine>();
        foreach (var line in lines)
        {
            var width = measure(line);
            var x = alignment.EndsWith("Center", StringComparison.Ordinal) || alignment == "Middle" ? -width / 2 : alignment.EndsWith("Right", StringComparison.Ordinal) ? -width : 0;
            result.Add(new(line, x, y)); y += lineHeight;
        }
        return result.ToImmutable();
    }
}
