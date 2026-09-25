// Copyright (c) netDxf contributors. Licensed under the MIT License.
using NetDxf.Qualification;
namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterPolyline3DEditTests()
    {
        foreach (var item in Polyline3DEditCases.All(ArtifactDirectory))
            Run("polyline3d-edits/" + item.Id, item.Test);
    }
}
