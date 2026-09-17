// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Globalization;
using netDxf.Entities;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterMeshEdgeDiagnosticTests()
    {
        foreach (string cultureName in new[] { "", "en-US", "pl-PL", "de-DE" })
            foreach (int overload in new[] { 0, 1, 2 })
                Run($"mesh-edge-diagnostic/{cultureName}/{overload}", () =>
                {
                    CultureInfo previous = CultureInfo.CurrentCulture;
                    try
                    {
                        CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
                        CultureInfo.CurrentCulture = culture;
                        IFormatProvider? provider = overload == 1 ? CultureInfo.InvariantCulture : null;
                        var edge = new MeshEdge(3, 7, 1.25);
                        string actual = overload == 0 ? edge.ToString() : edge.ToString(provider!);
                        string crease = overload == 0 ? "" + 1.25 : 1.25.ToString(provider);
                        string expected = $"MeshEdge: (3{culture.TextInfo.ListSeparator} 7) crease={crease}";
                        Equal(expected, actual, "mesh edge diagnostic type and culture");
                        Equal(3, edge.StartVertexIndex, "unchanged start");
                        Equal(7, edge.EndVertexIndex, "unchanged end");
                        Equal(1.25, edge.Crease, "unchanged crease");
                        Equal(actual, overload == 0 ? ((MeshEdge)edge.Clone()).ToString() : ((MeshEdge)edge.Clone()).ToString(provider!), "clone diagnostic");
                    }
                    finally { CultureInfo.CurrentCulture = previous; }
                });
    }
}
