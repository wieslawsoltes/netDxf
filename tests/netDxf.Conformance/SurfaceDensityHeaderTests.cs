using netDxf;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly string[][] SurfaceHeaderOrders =
    {
        new[] { "$SPLINESEGS", "$SURFU", "$SURFV" },
        new[] { "$SPLINESEGS", "$SURFV", "$SURFU" },
        new[] { "$SURFU", "$SPLINESEGS", "$SURFV" },
        new[] { "$SURFU", "$SURFV", "$SPLINESEGS" },
        new[] { "$SURFV", "$SPLINESEGS", "$SURFU" },
        new[] { "$SURFV", "$SURFU", "$SPLINESEGS" }
    };

    private static void RegisterSurfaceDensityHeaderTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                for (int order = 0; order < SurfaceHeaderOrders.Length; order++)
                {
                    int o = order;
                    Run($"header/surface-density/order/{v}/{b}/{o}", () => SurfaceHeaderOrder(v, b, o));
                }
                Run($"header/surface-density/omitted/{v}/{b}", () => SurfaceHeaderOmitted(v, b));
                Run($"header/surface-density/existing-recovery/{v}/{b}", () => SurfaceHeaderRecovery(v, b));
                Run($"header/surface-density/all-admitted-values/{v}/{b}", () => SurfaceHeaderRoundTrips(v, b));
            }
    }

    private static DxfDocument ReadSurfaceHeader(DxfVersion version, bool binary, IEnumerable<(string Name, short Value)> values)
    {
        using var stream = new MemoryStream(); object writer = NewCodeWriter(stream, binary);
        void T(short code, object value) => Invoke(writer, "Write", code, value);
        T(0, "SECTION"); T(2, "HEADER"); T(9, "$ACADVER"); T(1, HeaderVersion(version));
        T(9, "$DWGCODEPAGE"); T(3, "ANSI_1252");
        foreach (var (name, value) in values)
        {
            T(9, name);
            if (!binary) T(999, "surface density value follows");
            T(70, value);
        }
        T(0, "ENDSEC"); T(0, "EOF"); Invoke(writer, "Flush"); stream.Position = 0;
        DxfDocument document = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Surface HEADER failed to load.");
        Check(stream.CanRead, "Surface HEADER load closed caller stream.");
        return document;
    }

    private static void CheckSurfaceHeader(DxfDocument doc, short u, short v, short spline)
    {
        Equal(u, doc.DrawingVariables.SurfU, "$SURFU surface density");
        Equal(v, doc.DrawingVariables.SurfV, "$SURFV surface density");
        Equal(spline, doc.DrawingVariables.SplineSegs, "$SPLINESEGS must not be overwritten by surface density");
    }

    private static void SurfaceHeaderOrder(DxfVersion version, bool binary, int order)
    {
        foreach (var pair in new (short U, short V)[] { (2, 200), (200, 2), (13, 29), (199, 3), (6, 6) })
        {
            var values = new Dictionary<string, short> { ["$SURFU"] = pair.U, ["$SURFV"] = pair.V, ["$SPLINESEGS"] = 17 };
            DxfDocument doc = ReadSurfaceHeader(version, binary, SurfaceHeaderOrders[order].Select(n => (n, values[n])));
            CheckSurfaceHeader(doc, pair.U, pair.V, 17);
        }
    }

    private static void SurfaceHeaderOmitted(DxfVersion version, bool binary)
    {
        CheckSurfaceHeader(ReadSurfaceHeader(version, binary, Array.Empty<(string, short)>()), 6, 6, 8);
        CheckSurfaceHeader(ReadSurfaceHeader(version, binary, new[] { ("$SURFU", (short)13) }), 13, 6, 8);
        CheckSurfaceHeader(ReadSurfaceHeader(version, binary, new[] { ("$SURFV", (short)29) }), 6, 29, 8);
        CheckSurfaceHeader(ReadSurfaceHeader(version, binary, new[] { ("$SPLINESEGS", (short)17) }), 6, 6, 17);
    }

    private static void SurfaceHeaderRecovery(DxfVersion version, bool binary)
    {
        // Preserve the pre-existing reader recovery profile in this isolated routing fix.
        // Exact preservation of the API-admitted 0/1 density values is separate work.
        foreach (short raw in new short[] { short.MinValue, -1, 0, 1, 201, short.MaxValue })
        {
            CheckSurfaceHeader(ReadSurfaceHeader(version, binary, new[] { ("$SPLINESEGS", (short)17), ("$SURFU", raw), ("$SURFV", (short)29) }), 6, 29, 17);
            CheckSurfaceHeader(ReadSurfaceHeader(version, binary, new[] { ("$SPLINESEGS", (short)17), ("$SURFU", (short)13), ("$SURFV", raw) }), 13, 6, 17);
        }
    }

    private static void SurfaceHeaderRoundTrips(DxfVersion version, bool binary)
    {
        // Exhaust all 199 values admitted by the existing reader, with deliberately
        // unequal U/V densities and a spline setting outside that range.
        for (short u = 2; u <= 200; u++)
        {
            short v = (short)(202 - u);
            var doc = new DxfDocument(version);
            doc.DrawingVariables.SurfU = u; doc.DrawingVariables.SurfV = v; doc.DrawingVariables.SplineSegs = 237;
            for (int cycle = 0; cycle < 2; cycle++)
            {
                bool transport = cycle == 0 ? binary : !binary;
                using var output = new MemoryStream(); Check(doc.Save(output, transport), "Surface header save failed.");
                var raw = ReadRawHeader(output.ToArray(), transport);
                foreach (var entry in new[] { ("$SURFU", u), ("$SURFV", v), ("$SPLINESEGS", (short)237) })
                {
                    var tag = raw[entry.Item1].Single(); Equal((short)70, tag.Code, "Surface HEADER group code");
                    Equal(entry.Item2, (short)tag.Value, "Surface HEADER exact wire value");
                }
                CheckSurfaceHeader(doc, u, v, 237);
                output.Position = 0;
                doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Surface header reload failed.");
                CheckSurfaceHeader(doc, u, v, 237); Check(output.CanRead, "Surface round trip closed caller stream.");
            }
        }
    }
}
