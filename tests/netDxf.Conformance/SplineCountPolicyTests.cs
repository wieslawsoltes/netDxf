// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Reflection;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static PropertyInfo CountPolicyProperty() => typeof(DxfDocument).GetProperty("SplineCountPolicy")
        ?? throw new InvalidOperationException("The typed SPLINE count output policy is missing.");
    private static void SetCountPolicy(DxfDocument doc, int value)
    {
        var property = CountPolicyProperty();
        try { property.SetValue(doc, Enum.ToObject(property.PropertyType, value)); }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    private static int GetCountPolicy(DxfDocument doc) => Convert.ToInt32(CountPolicyProperty().GetValue(doc));

    private static Spline CountSubject(int kind)
    {
        var points = new[] { new Vector3(1, 2, 3), new Vector3(4, -2, 6), new Vector3(7, 8, -4), new Vector3(10, 3, 1) };
        if (kind == 3) return new Spline(points);
        var curve = new Spline(points, new[] { 1.0, .5, 2.0, 1.0 }, (short)(kind == 0 ? 1 : 2), kind == 2);
        return kind == 4 ? new Helix(curve) : curve;
    }
    private static DxfDocument CountDocument(DxfVersion version, Spline spline, int placement)
    {
        var doc = new DxfDocument(version);
        if (placement == 0) doc.Entities.Add(spline);
        else
        {
            var block = new Block("SPLINE_COUNTS"); block.Entities.Add(spline);
            if (placement == 1) doc.Entities.Add(new Insert(block));
            else doc.Blocks.Add(block); // An unreferenced definition must still be preflighted.
        }
        return doc;
    }
    private static byte[] CountSave(DxfDocument doc, bool binary)
    {
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "SPLINE count save failed");
        return stream.ToArray();
    }
    private static List<DxfTag> CountPacket(byte[] data)
    {
        using var stream = new MemoryStream(data);
        var record = DxfRawDocument.Load(stream).Sections.SelectMany(s => s.Records)
            .Single(r => r.Name == "SPLINE" || r.Name == "HELIX");
        return record.Tags.SkipWhile(t => t.Code != 100 || (string)t.Value != "AcDbSpline").Skip(1)
            .TakeWhile(t => t.Code != 100).ToList();
    }
    private static void CountCheck(byte[] data, Spline spline, bool present)
    {
        var tags = CountPacket(data); int[] codes = { 72, 73, 74 };
        int[] expected = { spline.Knots.Length, spline.ControlPoints.Length + (spline.IsClosedPeriodic ? spline.Degree : 0), spline.FitPoints.Count };
        for (int i = 0; i < 3; i++)
        {
            var values = tags.Where(t => t.Code == codes[i]).ToArray();
            Equal(present ? 1 : 0, values.Length, "Count packet presence");
            if (present) { Check(values[0].Value is short, "Count must be Int16"); Equal(expected[i], (int)(short)values[0].Value, "Serialized count"); }
        }
        Equal(expected[0], tags.Count(t => t.Code == 40), "Knot payload length");
        Equal(expected[1], tags.Count(t => t.Code == 10), "Control payload length");
        Equal(expected[2], tags.Count(t => t.Code == 11), "Fit payload length");
        if (present)
        {
            int at = tags.FindIndex(t => t.Code == 71);
            Check(tags.Skip(at + 1).Take(3).Select(t => (int)t.Code).SequenceEqual(codes), "Count order after degree");
        }
    }
    private static void CountWire(DxfVersion version, bool binary, int kind, int placement, int policy)
    {
        var spline = CountSubject(kind); var doc = CountDocument(version, spline, placement);
        var original = CountSave(doc, binary); CountCheck(original, spline, false);
        SetCountPolicy(doc, policy); byte[] data = CountSave(doc, binary); CountCheck(data, spline, policy != 0);
        var before = CountPacket(original); var after = CountPacket(data).Where(t => t.Code < 72 || t.Code > 74).ToArray();
        Equal(before.Count, after.Length, "Count policy changed packet length beyond counts");
        for (int i = 0; i < after.Length; i++)
        { Equal(before[i].Code, after[i].Code, "Count policy changed non-count order"); Equal(before[i].Value, after[i].Value, "Count policy changed payload"); }
        if (policy == 0)
        {
            // Repeated saves have pre-existing handle-allocation side effects.
            // Compare equivalent loaded snapshots, not two lifecycle stages.
            using var a = new MemoryStream(original); using var b = new MemoryStream(original);
            var defaultDoc = DxfDocument.Load(a)!; var explicitDoc = DxfDocument.Load(b)!;
            SetCountPolicy(explicitDoc, 0);
            Check(CountSave(defaultDoc, binary).SequenceEqual(CountSave(explicitDoc, binary)), "Explicit legacy policy changed default bytes");
        }
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"spline-counts-{version}-{binary}-{kind}-{placement}-{policy}.dxf"), data);
        using var stream = new MemoryStream(data); var loaded = DxfDocument.Load(stream)!;
        Check(loaded != null, "Counted spline load failed"); Equal(0, GetCountPolicy(loaded!), "Output preference leaked into DXF");
        var copy = loaded!.Blocks.SelectMany(b => b.Entities).OfType<Spline>().Single();
        Check(copy.ControlPoints.SequenceEqual(spline.ControlPoints) && copy.Knots.SequenceEqual(spline.Knots)
            && copy.Weights.SequenceEqual(spline.Weights) && copy.FitPoints.SequenceEqual(spline.FitPoints), "Count policy changed reloaded geometry");
    }

    private static Spline CountBoundary(int kind)
    {
        // Real output arrays near Int16 limits. The fit-only field injection models
        // retained fit data without invoking a huge, unrelated fitting operation.
        if (kind >= 4)
        {
            var spline = CountSubject(1);
            typeof(Spline).GetField("fitPoints", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(spline, Enumerable.Range(0, kind == 4 ? 32767 : 32768).Select(i => new Vector3(i, 0, 0)).ToArray());
            return spline;
        }
        int count = kind == 0 ? 32765 : kind == 1 ? 32766 : kind == 2 ? 32767 : 32768;
        return new Spline(Enumerable.Range(0, count).Select(i => new Vector3(i, i % 3, 0)), null, (short)1, false);
    }
    private static void CountLimits(int kind, bool binary, int policy)
    {
        var spline = CountBoundary(kind); var doc = CountDocument(DxfVersion.AutoCad2018, spline, 0);
        SetCountPolicy(doc, policy); bool representable = kind == 0 || kind == 4;
        if (policy == 2 && !representable)
        {
            using var stream = new MemoryStream(); var sentinel = new byte[] { 1, 3, 5, 7, 9 }; stream.Write(sentinel); stream.Position = 2;
            long seed = OwnershipSeed(doc); string? handle = spline.Handle; string? name = doc.Name;
#if DEBUG
            Throws<InvalidDataException>(() => doc.Save(stream, binary));
#else
            Check(!doc.Save(stream, binary), "Overflowing required count accepted");
#endif
            Check(sentinel.SequenceEqual(stream.ToArray()) && stream.Position == 2, "Count rejection changed stream");
            Equal(seed, OwnershipSeed(doc), "Count rejection allocated handles"); Equal(handle, spline.Handle, "Count rejection changed source handle"); Equal(name, doc.Name, "Count rejection changed name");
        }
        else
        {
            byte[] data = CountSave(doc, binary); CountCheck(data, spline, policy != 0 && representable);
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"spline-counts-limit-{kind}-{binary}-{policy}.dxf"), data);
        }
    }
    private static void CountFileRejection(bool binary, bool existing, bool atomic, int placement, bool helix)
        => WithAtomicDirectory(path =>
        {
            AtomicPrepare(path, existing);
            Spline curve = CountBoundary(1); if (helix) curve = new Helix(curve);
            var doc = CountDocument(DxfVersion.AutoCad2018, curve, placement); SetCountPolicy(doc, 2);
            doc.Name = "unchanged-count-document"; string folder = doc.SupportFolders.WorkingFolder;
            long seed = OwnershipSeed(doc); var blocks = doc.Blocks.ToArray(); var entities = blocks.SelectMany(b => b.Entities).ToArray();
            var objectsField = typeof(DxfDocument).GetField("objectDatabase", BindingFlags.NonPublic | BindingFlags.Instance)!;
            object? database = objectsField.GetValue(doc);
            if (atomic) Throws<InvalidDataException>(() => doc.SaveAtomic(path, binary));
            else
            {
#if DEBUG
                Throws<InvalidDataException>(() => doc.Save(path, binary));
#else
                Check(!doc.Save(path, binary), "Conventional save accepted overflowing required count");
#endif
            }
            AtomicUnchanged(path, existing); Equal("unchanged-count-document", doc.Name, "Name rollback");
            Equal(folder, doc.SupportFolders.WorkingFolder, "Folder rollback"); Equal(seed, OwnershipSeed(doc), "Handle-seed rollback");
            Check(ReferenceEquals(database, objectsField.GetValue(doc)), "Count preflight initialized/replaced OBJECTS");
            Check(blocks.SequenceEqual(doc.Blocks) && entities.SequenceEqual(doc.Blocks.SelectMany(b => b.Entities)), "Count rejection changed graph");
            // The same destination/document can be saved with the bounded fallback policy.
            SetCountPolicy(doc, 1);
            if (atomic) doc.SaveAtomic(path, binary); else Check(doc.Save(path, binary), "Fallback save failed");
            CountCheck(File.ReadAllBytes(path), curve, false); CheckLifetimeFileReleased(path);
        });

    private static void RegisterSplineCountPolicyTests()
    {
        RegisterSplineCountIntegrationTests();
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
            for (int kind = 0; kind < 5; kind++) for (int placement = 0; placement < 3; placement++) for (int policy = 0; policy < 3; policy++)
            {
                int k = kind, p = placement, mode = policy;
                if (k == 4 && version < DxfVersion.AutoCad2007) continue;
                Run($"spline-counts/wire/{version}/{binary}/{k}/{p}/{mode}", () => CountWire(version, binary, k, p, mode));
            }
        for (int kind = 0; kind < 6; kind++) foreach (bool binary in new[] { false, true }) for (int policy = 0; policy < 3; policy++)
        { int k = kind, p = policy; Run($"spline-counts/limits/{k}/{binary}/{p}", () => CountLimits(k, binary, p)); }
        foreach (bool binary in new[] { false, true }) foreach (bool exists in new[] { false, true }) foreach (bool atomic in new[] { false, true })
            for (int placement = 0; placement < 3; placement++) foreach (bool helix in new[] { false, true })
            { int p = placement; Run($"spline-counts/file/{binary}/{exists}/{atomic}/{p}/{helix}", () => CountFileRejection(binary, exists, atomic, p, helix)); }
        foreach (int invalid in new[] { -1, 3, int.MinValue, int.MaxValue }) Run($"spline-counts/invalid-policy/{invalid}", () =>
        {
            var doc = new DxfDocument(); Equal(0, GetCountPolicy(doc), "Default count policy"); SetCountPolicy(doc, 1);
            Throws<ArgumentOutOfRangeException>(() => SetCountPolicy(doc, invalid)); Equal(1, GetCountPolicy(doc), "Rejected policy changed state");
        });
    }
}
