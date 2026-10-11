namespace netDxf.Shadow.Rendering;

/// <summary>Combines a presentation color with inherited occurrence opacity.</summary>
public static class RenderPaint
{
    public static uint Resolve(uint? local, uint inherited)
    {
        if (local is not { } color) return inherited;
        return (color & 0xffffffu) | (((color >> 24) * (inherited >> 24) + 127u) / 255u) << 24;
    }
}
