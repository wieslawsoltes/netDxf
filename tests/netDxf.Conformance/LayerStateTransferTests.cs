// Copyright (c) netDxf contributors. Licensed under the MIT License.
using NetDxf.Qualification;
namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterLayerStateTransferTests()
    {
        foreach (var item in LayerStateTransferCases.All(ArtifactDirectory))
            Run("layer-state-transfer/" + item.Id, item.Test);
    }
}
