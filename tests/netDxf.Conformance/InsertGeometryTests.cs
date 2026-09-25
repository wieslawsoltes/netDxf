// Copyright (c) netDxf contributors. Licensed under the MIT License.
using NetDxf.Qualification;
namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterInsertGeometryTests()
    {
        foreach (var item in InsertGeometryCases.All(ArtifactDirectory))
            Run("insert-geometry/" + item.Id, item.Test);
    }
}
