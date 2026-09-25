// Copyright (c) netDxf contributors. Licensed under the MIT License.
using NetDxf.Qualification;
namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterLegacyMeshEditTests()
    {
        foreach (var item in LegacyMeshEditCases.All(ArtifactDirectory))
            Run("legacy-mesh-edits/" + item.Id, item.Test);
    }
}
