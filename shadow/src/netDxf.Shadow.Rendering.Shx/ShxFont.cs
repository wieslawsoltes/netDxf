using System.Collections.Immutable;

namespace netDxf.Shadow.Rendering.Shx;

public enum ShxEncoding { Unicode, PackedMultibyte, Shapes, Legacy }
[Flags]
public enum ShxEmbedding { Allowed = 0, Disallowed = 1, ReadOnly = 2 }

/// <summary>A decoded instruction. Operands have signed displacements and unsigned octant bitfields.</summary>
public sealed record ShxInstruction(byte Code, ImmutableArray<int> Operands);
public sealed record ShxSymbol(int Number, string Name, int DeclaredBytes, ImmutableArray<ShxInstruction> Instructions);
public sealed record ShxReadOptions(int MaximumBytes = 16_777_216, int MaximumSymbols = 65_536,
    int MaximumInstructions = 2_000_000);

/// <summary>Detached font/shape instructions. Loading never resolves paths or retains caller-owned bytes.</summary>
public sealed class ShxFont
{
    public string Name { get; }
    public int CapHeight { get; }
    public int Descender { get; }
    public bool SupportsVerticalText { get; }
    public ShxEncoding Encoding { get; }
    public ShxEmbedding Embedding { get; }
    public ImmutableDictionary<int, ShxSymbol> Symbols { get; }
    public ImmutableDictionary<string, int> Names { get; }
    public bool IsFont => CapHeight > 0 && Encoding != ShxEncoding.Shapes;

    internal ShxFont(string name, int above, int below, int mode, ShxEncoding encoding,
        ShxEmbedding embedding, ImmutableDictionary<int, ShxSymbol> symbols)
    {
        if (above < 0 || below < 0 || mode is not (0 or 2) || !Enum.IsDefined(encoding) || (embedding & ~(ShxEmbedding.Disallowed | ShxEmbedding.ReadOnly)) != 0)
            throw new FormatException("Invalid SHX font metadata.");
        Name = name; CapHeight = above; Descender = below; SupportsVerticalText = mode == 2;
        Encoding = encoding; Embedding = embedding; Symbols = symbols;
        var names = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        foreach (var symbol in symbols.Values.OrderBy(s => s.Number))
            if (symbol.Name.Length > 0 && !names.TryAdd(symbol.Name, symbol.Number))
                throw new FormatException($"Duplicate SHX symbol name '{symbol.Name}'.");
        Names = names.ToImmutable();
    }
}
