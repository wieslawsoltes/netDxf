// Copyright (c) netDxf contributors. Licensed under the MIT License.
using NetDxf.Qualification;
namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterLayerStateIdentityTests()
    {
        foreach (var item in LayerStateIdentityCases.All(ArtifactDirectory))
            Run("layer-state-identity/" + item.Id, item.Test);
    }
}
