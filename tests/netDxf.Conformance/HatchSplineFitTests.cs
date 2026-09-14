using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly Vector2[] HatchFitValues = { new(0, 0), new(5.000000000000001, 5), new(10, 0) };

    private static void RegisterHatchSplineFitTests()
    {
        foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2010))
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                foreach (int count in new[] { 0, 1, 3 })
                    foreach (int mask in Enumerable.Range(0, 5))
                        foreach (bool comments in binary ? new[] { false } : new[] { false, true })
                        {
                            int c = count, m = mask; bool co = comments;
                            Run($"hatch/spline-fit/roundtrip/{v}/{b}/{c}/{m}/{co}", () => HatchSplineFitRoundTrip(v, b, c, m, co));
                        }
                Run($"hatch/spline-fit/reversed-tangents/{v}/{b}", () => HatchSplineFitReversed(v, b));
                foreach (int failure in Enumerable.Range(0, 5))
                {
                    int f = failure;
                    Run($"hatch/spline-fit/invalid/{v}/{b}/{f}", () => HatchSplineFitInvalid(v, b, f));
                }
            }
        foreach (DxfVersion version in SupportedVersions.Where(v => v < DxfVersion.AutoCad2010))
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                Run($"hatch/spline-fit/legacy-empty/{v}/{b}", () => HatchSplineFitLegacyEmpty(v, b));
            }
    }

    private static List<DxfTag> HatchSplineFitTags(DxfVersion version, int count, int mask, bool comments)
    {
        var tags = HatchEdgeDispatchTags(version, 4);
        int fit = tags.FindIndex(t => t.Code == 97);
        tags[fit] = new(97, count);
        var packet = new List<DxfTag>();
        for (int i = 0; i < count; ++i)
        {
            packet.Add(new(11, HatchFitValues[i].X)); packet.Add(new(21, HatchFitValues[i].Y));
        }
        if ((mask & 1) != 0 || mask == 4)
        {
            packet.Add(new(12, mask == 4 ? 0.0 : 10.0)); packet.Add(new(22, mask == 4 ? 0.0 : 20.0));
        }
        if ((mask & 2) != 0 || mask == 4)
        {
            packet.Add(new(13, mask == 4 ? 0.0 : 10.0)); packet.Add(new(23, mask == 4 ? 0.0 : -20.0));
        }
        // Close the quadratic edge with an independent LINE edge.
        packet.AddRange(new DxfTag[] { new(72, (short)1), new(10, 10.0), new(20, 0.0), new(11, 0.0), new(21, 0.0) });
        tags.InsertRange(fit + 1, packet);
        tags[tags.FindIndex(t => t.Code == 93)] = new(93, 2);
        if (comments)
            for (int i = fit + packet.Count + 1; i > fit; --i) tags.Insert(i, new(999, "97 12 13 ENDSEC"));
        return tags;
    }

    private static Spline HatchFitEntity(Hatch hatch) => (Spline)hatch.BoundaryPaths.Single().Edges.OfType<HatchBoundaryPath.Spline>().Single().ConvertTo();

    private static void AssertHatchFitEntity(Spline spline, int count, int mask)
    {
        Equal(count, spline.FitPoints.Count, "Spline fit-point count");
        for (int i = 0; i < count; ++i)
        {
            SameDoubleBits(HatchFitValues[i].X, spline.FitPoints[i].X, "Fit X precision/order");
            SameDoubleBits(HatchFitValues[i].Y, spline.FitPoints[i].Y, "Fit Y precision/order");
        }
        Equal<Vector3?>((mask & 1) != 0 ? new(10, 20, 0) : mask == 4 ? Vector3.Zero : null, spline.StartTangent, "Start tangent presence/value");
        Equal<Vector3?>((mask & 2) != 0 ? new(10, -20, 0) : mask == 4 ? Vector3.Zero : null, spline.EndTangent, "End tangent presence/value");
        Equal(3, spline.ControlPoints.Length, "Fit metadata changed controls");
        Equal(6, spline.Knots.Length, "Fit metadata changed knots");
    }

    private static void HatchSplineFitRoundTrip(DxfVersion version, bool binary, int count, int mask, bool comments)
    {
        using var input = new MemoryStream(RawFixtureBytes(HatchSplineFitTags(version, count, mask, comments), binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Spline fit fixture rejected.");
        var hatch = doc.Entities.Hatches.Single();
        AssertHatchFitEntity(HatchFitEntity(hatch), count, mask);
        var edge = hatch.BoundaryPaths.Single().Edges.OfType<HatchBoundaryPath.Spline>().Single();
        AssertHatchFitEntity((Spline)((HatchBoundaryPath.Spline)edge.Clone()).ConvertTo(), count, mask);
        AssertHatchFitEntity((Spline)((HatchBoundaryPath)hatch.BoundaryPaths.Single().Clone()).Edges.OfType<HatchBoundaryPath.Spline>().Single().ConvertTo(), count, mask);
        doc.Entities.Add((Hatch)hatch.Clone());
        for (int cycle = 0; cycle < 3; ++cycle)
        {
            using var output = new MemoryStream();
            bool transport = cycle % 2 == 0 ? !binary : binary;
            Check(doc.Save(output, transport), "Fit metadata save failed.");
            output.Position = 0;
            var raw = DxfRawDocument.Load(output);
            foreach (var record in raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "HATCH"))
            {
                var tags = record.Tags.SkipWhile(t => t.Code != 94).TakeWhile(t => t.Code != 72).ToArray();
                Equal(count, (int)tags.Single(t => t.Code == 97).Value, "Serialized fit count");
                Check(HatchFitValues.Take(count).Select(p => p.X).SequenceEqual(tags.Where(t => t.Code == 11).Select(t => (double)t.Value)), "Serialized fit X data changed.");
                Equal(mask == 4 || (mask & 1) != 0, tags.Any(t => t.Code == 12), "Serialized start presence");
                Equal(mask == 4 || (mask & 2) != 0, tags.Any(t => t.Code == 13), "Serialized end presence");
            }
            if (cycle == 1 && count == 3 && mask == 3 && !comments)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"hatch-spline-fit-{version}-{binary}.dxf"), output.ToArray());
            output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Spline fit reload failed.");
            foreach (Hatch current in doc.Entities.Hatches)
            {
                AssertHatchFitEntity(HatchFitEntity(current), count, mask);
                Equal(2.5, current.Elevation, "Fit metadata changed elevation");
                Equal(new Vector2(2, 3), current.SeedPoints.Single(), "Fit metadata changed seed");
                Equal("after pattern", (string)current.XData["DOUBLE_TEST"].XDataRecord.Single().Value, "Following fit XData lost");
                Equal(2, current.BoundaryPaths.Single().Edges.Count, "Following LINE edge lost");
            }
        }
        Check(input.CanRead, "Fit reader closed caller stream.");
    }

    private static void HatchSplineFitReversed(DxfVersion version, bool binary)
    {
        var tags = HatchSplineFitTags(version, 3, 3, false);
        int start = tags.FindIndex(t => t.Code == 12);
        var first = tags.GetRange(start, 2); tags.RemoveRange(start, 2); tags.InsertRange(start + 2, first);
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Reordered tangent packet rejected.");
        AssertHatchFitEntity(HatchFitEntity(doc.Entities.Hatches.Single()), 3, 3);
    }

    private static void HatchSplineFitInvalid(DxfVersion version, bool binary, int failure)
    {
        var tags = HatchSplineFitTags(version, 3, 3, false);
        int start = tags.FindIndex(t => t.Code == 12), fit = tags.FindIndex(t => t.Code == 97);
        switch (failure)
        {
            case 0: tags.InsertRange(start, new DxfTag[] { new(12, 1.0), new(22, 2.0) }); break;
            case 1: tags.InsertRange(start, new DxfTag[] { new(13, 1.0), new(23, 2.0) }); break;
            case 2: tags.RemoveAt(start + 1); break;
            case 3: tags[fit] = new(97, 2); break;
            case 4: tags[fit] = new(97, int.MaxValue); break;
        }
        ExpectEdgePacketInvalid(tags, binary);
    }

    private static void HatchSplineFitLegacyEmpty(DxfVersion version, bool binary)
    {
        using var input = new MemoryStream(RawFixtureBytes(HatchEdgeDispatchTags(version, 4), binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Legacy control spline rejected.");
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "Legacy control spline export changed.");
        output.Position = 0;
        var tags = DxfRawDocument.Load(output).Sections.SelectMany(s => s.Records).Single(r => r.Name == "HATCH").Tags;
        Equal(1, tags.Count(t => t.Code == 97), "Legacy spline emitted a fit packet");
    }
}
