using netDxf;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterUcsOrthographicTests()
    {
        Run("ucs/orthographic/model", UcsOrthographicModelTests);
        foreach (DxfVersion version in SupportedVersions)
        {
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version;
                bool b = binary;
                Run($"ucs/orthographic/preserve/{v}/{b}", () => UcsOrthographicRetention(v, b));
                Run($"ucs/orthographic/edit/{v}/{b}", () => UcsOrthographicEdit(v, b));
                Run($"ucs/orthographic/absent/{v}/{b}", () => UcsOrthographicAbsent(v, b));
                foreach (string invalid in new[] { "zero-type", "negative-type", "large-type", "unpaired", "duplicate-coordinate",
                    "missing-x", "missing-y", "missing-z", "missing-all", "duplicate-type", "interrupted-pair" })
                {
                    string captured = invalid;
                    Run($"ucs/orthographic/invalid/{v}/{b}/{invalid}", () => UcsOrthographicInvalid(v, b, captured));
                }
            }
        }
    }

    private static void UcsOrthographicModelTests()
    {
        foreach (UCS ucs in new[] { new UCS("Default"), new UCS("Axes", new Vector3(1, 2, 3), Vector3.UnitX, Vector3.UnitY),
            UCS.FromNormal("Normal", Vector3.Zero, Vector3.UnitZ) })
        {
            Equal(0, ucs.OrthographicOrigins.Count, "new UCS invented overrides");
            Check(ReferenceEquals(ucs.OrthographicOrigins, ucs.OrthographicOrigins), "Read-only view is not stable.");
            for (int type = 1; type <= 6; type++)
            {
                UcsOrthographicType key = (UcsOrthographicType)type;
                Check(!ucs.TryGetOrthographicOrigin(key, out Vector3 point) && point == Vector3.Zero, "Missing override was invented.");
                ucs.SetOrthographicOrigin(key, OrthoPoint(type));
                Check(ucs.TryGetOrthographicOrigin(key, out point), "Stored override missing.");
                Equal(OrthoPoint(type), point, "override value");
                ucs.SetOrthographicOrigin(key, Vector3.Zero);
                Equal(Vector3.Zero, ucs.OrthographicOrigins[key], "explicit zero override");
                Check(ucs.RemoveOrthographicOrigin(key), "Override not removed.");
                Check(!ucs.RemoveOrthographicOrigin(key), "Absent override reported as removed.");
            }
            var dictionary = (IDictionary<UcsOrthographicType, Vector3>)ucs.OrthographicOrigins;
            Throws<NotSupportedException>(() => dictionary.Add(UcsOrthographicType.Top, Vector3.Zero));
            Equal(0, ucs.OrthographicOrigins.Count, "Read-only mutation changed the model.");
            foreach (int invalid in new[] { -1, 0, 7, short.MaxValue })
            {
                UcsOrthographicType type = (UcsOrthographicType)invalid;
                Throws<ArgumentOutOfRangeException>(() => ucs.SetOrthographicOrigin(type, Vector3.Zero));
                Throws<ArgumentOutOfRangeException>(() => ucs.TryGetOrthographicOrigin(type, out _));
                Throws<ArgumentOutOfRangeException>(() => ucs.RemoveOrthographicOrigin(type));
            }
            ucs.SetOrthographicOrigin(UcsOrthographicType.Top, OrthoPoint(1));
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                foreach (Vector3 point in new[] { new Vector3(invalid, 0, 0), new Vector3(0, invalid, 0), new Vector3(0, 0, invalid) })
                {
                    Throws<ArgumentOutOfRangeException>(() => ucs.SetOrthographicOrigin(UcsOrthographicType.Top, point));
                    Equal(OrthoPoint(1), ucs.OrthographicOrigins[UcsOrthographicType.Top], "Invalid assignment changed stored origin.");
                }
        }
        var original = new UCS("Original", new Vector3(3, 4, 5), Vector3.UnitY, -Vector3.UnitX) { Elevation = -2.5 };
        for (int type = 1; type <= 6; ++type) original.SetOrthographicOrigin((UcsOrthographicType)type, OrthoPoint(type));
        var copy = (UCS)original.Clone("Copy");
        Equal(6, copy.OrthographicOrigins.Count, "clone override count");
        Equal(original.Origin, copy.Origin, "clone base origin");
        Equal(original.GetTransformation(), copy.GetTransformation(), "clone axes");
        Near(original.Elevation, copy.Elevation, "clone elevation");
        foreach (var pair in original.OrthographicOrigins) Equal(pair.Value, copy.OrthographicOrigins[pair.Key], "clone override");
        copy.RemoveOrthographicOrigin(UcsOrthographicType.Left);
        copy.SetOrthographicOrigin(UcsOrthographicType.Right, Vector3.Zero);
        Equal(6, original.OrthographicOrigins.Count, "clone removal affected source");
        Equal(OrthoPoint(6), original.OrthographicOrigins[UcsOrthographicType.Right], "clone edit affected source");
    }

    private static void UcsOrthographicEdit(DxfVersion version, bool binary)
    {
        var document = new DxfDocument(version);
        var ucs = new UCS("Editable", new Vector3(100, -200, 300), Vector3.UnitY, -Vector3.UnitX) { Elevation = 12.5 };
        ucs.SetOrthographicOrigin(UcsOrthographicType.Left, Vector3.Zero);
        ucs.SetOrthographicOrigin(UcsOrthographicType.Right, OrthoPoint(6));
        ucs.SetOrthographicOrigin(UcsOrthographicType.Top, OrthoPoint(1));
        ucs.RemoveOrthographicOrigin(UcsOrthographicType.Right);
        document.UCSs.Add(ucs);
        using var output = new MemoryStream();
        Check(document.Save(output, binary), "Edited UCS failed to save."); output.Position = 0;
        DxfDocument loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Edited UCS failed to load.");
        UCS copy = loaded.UCSs[ucs.Name];
        Equal(2, copy.OrthographicOrigins.Count, "edited override count");
        Check(copy.TryGetOrthographicOrigin(UcsOrthographicType.Left, out Vector3 point), "Explicit zero origin was omitted.");
        Equal(Vector3.Zero, point, "explicit zero origin");
        Check(!copy.TryGetOrthographicOrigin(UcsOrthographicType.Right, out _), "Removed origin was regenerated.");
        Equal(OrthoPoint(1), copy.OrthographicOrigins[UcsOrthographicType.Top], "saved override");
        Equal(ucs.Origin, copy.Origin, "override moved base origin");
        Equal(ucs.XAxis, copy.XAxis, "override rotated axes");
        Near(12.5, copy.Elevation, "override changed elevation");
        Check(ReferenceEquals(loaded.GetObjectByHandle(copy.Handle), copy), "UCS handle ownership broken.");
        copy.SetOrthographicOrigin(UcsOrthographicType.Top, OrthoPoint(4));
        using var second = new MemoryStream();
        Check(loaded.Save(second, !binary), "Cross-transport save failed."); second.Position = 0;
        DxfDocument reloaded = DxfDocument.Load(second) ?? throw new InvalidOperationException("Cross-transport load failed.");
        Equal(OrthoPoint(4), reloaded.UCSs[ucs.Name].OrthographicOrigins[UcsOrthographicType.Top], "edit then cross-transport reload");
    }

    private static void UcsOrthographicAbsent(DxfVersion version, bool binary)
    {
        using var input = UcsOrthographicFixture(version, binary, Array.Empty<(short, object)>());
        DxfDocument loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("No-override fixture failed.");
        Equal(0, loaded.UCSs["Fixture"].OrthographicOrigins.Count, "absent origins were invented");
        using var output = new MemoryStream(); Check(loaded.Save(output, binary), "No-override save failed.");
        output.Position = 0; Equal(0, ReadUcsOrthographicTags(output, binary).Count, "writer invented origin overrides");
    }

    private static void UcsOrthographicInvalid(DxfVersion version, bool binary, string kind)
    {
        var tags = new List<(short Code, object Value)> { (71, (short)1), (13, 1.0), (23, 2.0), (33, 3.0) };
        switch (kind)
        {
            case "zero-type": tags[0] = (71, (short)0); break;
            case "negative-type": tags[0] = (71, (short)-1); break;
            case "large-type": tags[0] = (71, (short)7); break;
            case "unpaired": tags.RemoveAt(0); break;
            case "duplicate-coordinate": tags.Insert(2, (13, 4.0)); break;
            case "missing-x": tags.RemoveAt(1); break;
            case "missing-y": tags.RemoveAt(2); break;
            case "missing-z": tags.RemoveAt(3); break;
            case "missing-all": tags.RemoveRange(1, 3); break;
            case "duplicate-type": tags.AddRange(tags.ToArray()); break;
            case "interrupted-pair": tags.Insert(2, (71, (short)2)); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        using var input = UcsOrthographicFixture(version, binary, tags);
#if DEBUG
        Throws<InvalidDataException>(() => DxfDocument.Load(input));
#else
        Check(DxfDocument.Load(input) == null, "Malformed orthographic origin pair was silently accepted.");
#endif
        Check(input.CanRead, "Rejected fixture closed caller input.");
    }

    private static Vector3 OrthoPoint(int type) => new Vector3(type * 1.25, -type * 2.5, type * 3.75);

    private static void UcsOrthographicRetention(DxfVersion version, bool binary)
    {
        var pairs = new List<(short Code, object Value)>();
        // Reverse pair order and shuffle coordinate components: association comes from 71, not row order.
        for (short type = 6; type >= 1; type--)
        {
            Vector3 point = OrthoPoint(type);
            pairs.Add((71, type)); pairs.Add((33, point.Z));
            if (!binary) pairs.Add((999, "comment within origin pair"));
            pairs.Add((13, point.X)); pairs.Add((146, -17.625)); pairs.Add((23, point.Y));
        }
        pairs.Add((1001, "ORTHOGRAPHIC_TEST")); pairs.Add((1000, "origin metadata"));
        using var input = UcsOrthographicFixture(version, binary, pairs);
        DxfDocument loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("Orthographic UCS fixture failed to load.");
        Equal(new Vector3(1, 2, 3), loaded.UCSs["Fixture"].Origin, "base origin changed");
        Near(-17.625, loaded.UCSs["Fixture"].Elevation, "base elevation changed");
        Equal("origin metadata", (string)loaded.UCSs["Fixture"].XData["ORTHOGRAPHIC_TEST"].XDataRecord[0].Value, "paired data consumed XData");
        using var output = new MemoryStream();
        Check(loaded.Save(output, binary), "Orthographic UCS fixture failed to save.");
        output.Position = 0;
        List<(short Code, object Value)> written = ReadUcsOrthographicTags(output, binary);
        Equal(24, written.Count, "six UCS orthographic origin pairs were discarded");
        for (short type = 1; type <= 6; ++type)
        {
            int offset = (type - 1) * 4;
            Vector3 point = OrthoPoint(type);
            Equal(((short)71, (object)type), written[offset], "canonical pair order");
            Equal(((short)13, (object)point.X), written[offset + 1], "origin X");
            Equal(((short)23, (object)point.Y), written[offset + 2], "origin Y");
            Equal(((short)33, (object)point.Z), written[offset + 3], "origin Z");
        }
    }

    private static MemoryStream UcsOrthographicFixture(DxfVersion version, bool binary, IEnumerable<(short Code, object Value)> pairs)
    {
        using var template = UcsElevationFixture(version, binary, null);
        object reader = NewCodeReader(template, binary);
        var output = new MemoryStream();
        object writer = NewCodeWriter(output, binary);
        do
        {
            Invoke(reader, "Next");
            short code = TagCode(reader);
            object value = reader.GetType().GetProperty("Value")!.GetValue(reader)!;
            if (code == 0 && Equals(value, "ENDTAB"))
                foreach (var pair in pairs) Invoke(writer, "Write", pair.Code, pair.Value);
            Invoke(writer, "Write", code, value);
            if (code == 0 && Equals(value, "EOF")) break;
        } while (true);
        Invoke(writer, "Flush"); output.Position = 0;
        return output;
    }

    private static List<(short Code, object Value)> ReadUcsOrthographicTags(Stream input, bool binary)
    {
        object reader = NewCodeReader(input, binary);
        var tags = new List<(short Code, object Value)>();
        string record = "";
        while (true)
        {
            Invoke(reader, "Next");
            short code = TagCode(reader);
            if (code == 0)
            {
                record = (string)Invoke(reader, "ReadString")!;
                if (record == "EOF") return tags;
            }
            if (record == "UCS" && (code == 71 || code == 13 || code == 23 || code == 33))
                tags.Add((code, reader.GetType().GetProperty("Value")!.GetValue(reader)!));
        }
    }
}
