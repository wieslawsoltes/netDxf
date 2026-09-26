// Copyright (c) netDxf contributors. Licensed under the MIT License.
using NetDxf.Qualification;
namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterPolyline2DEditTests()
    {
        foreach (var item in Polyline2DEditCases.All(ArtifactDirectory))
            Run("polyline2d-edits/" + item.Id, item.Test);
    }
}
