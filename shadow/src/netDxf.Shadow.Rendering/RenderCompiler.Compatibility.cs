using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

public sealed partial class RenderCompiler
{
    /// <summary>Retains the original static compilation binary signature.</summary>
    public static RenderScene Compile(CadScene scene, SamplingOptions? sampling,
        CancellationToken cancellationToken, ITextLayoutEngine? textLayout)
        => Compile(scene, sampling, cancellationToken, textLayout, null, null);
}
