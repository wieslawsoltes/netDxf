using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterLightTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                foreach (short kind in new short[] { 1, 2, 3 })
                    foreach (bool reverse in new[] { false, true })
                    {
                        short k = kind; bool r = reverse;
                        Run($"light/wire/{v}/{b}/{k}/{r}", () => LightWire(v, b, k, r));
                    }
                for (int placement = 0; placement < 4; placement++)
                {
                    int p = placement;
                    Run($"light/placement/{v}/{b}/{p}", () => LightPlacement(v, b, p));
                }
                for (int failure = 0; failure < 10; failure++)
                {
                    int f = failure;
                    Run($"light/invalid/{v}/{b}/{f}", () => LightInvalid(v, b, f));
                }
            }
        Run("light/api/validation-and-clone", LightApi);
        foreach (int transform in Enumerable.Range(0, 7))
        {
            int t = transform; Run($"light/transform/{t}", () => LightTransform(t));
        }
    }

    private static List<DxfTag> LightTags(DxfVersion version, short kind, bool reverse)
    {
        var tags = new List<DxfTag>
        {
            new(0, "SECTION"), new(2, "HEADER"), new(9, "$ACADVER"), new(1, HeaderVersion(version)),
            new(9, "$DWGCODEPAGE"), new(3, "ANSI_1252"),
            new(0, "ENDSEC"), new(0, "SECTION"), new(2, "ENTITIES"),
            new(0, "LIGHT"), new(5, "200"), new(100, "AcDbEntity"), new(8, "0"), new(62, (short)3), new(100, "AcDbLight")
        };
        var packet = new List<DxfTag>
        {
            new(90, 0), new(1, "Lamp \\U+017B\\U+00F3\\U+0142\\U+0107"), new(70, kind),
            new(290, false), new(291, true), new(40, 1.2500000000000002),
            new(10, 2.0), new(20, -3.0), new(30, 5.0), new(11, -7.0), new(21, 11.0), new(31, 13.0),
            new(72, (short)(kind - 1)), new(292, true), new(41, 0.125), new(42, 2048.0),
            new(50, 37.5), new(51, 62.75), new(293, false), new(73, (short)1), new(91, 1024), new(280, (short)7)
        };
        if (reverse) packet.Reverse();
        tags.AddRange(packet);
        tags.AddRange(new DxfTag[]
        {
            new(1001, "LIGHT_TEST"), new(1000, "retained light data"), new(0, "LINE"), new(5, "201"),
            new(100, "AcDbEntity"), new(8, "0"), new(100, "AcDbLine"),
            new(10, 20.0), new(20, 30.0), new(30, 40.0), new(11, 50.0), new(21, 60.0), new(31, 70.0),
            new(0, "ENDSEC"), new(0, "EOF")
        });
        return tags;
    }

    private static Light LoadTestLight()
    {
        using var input = new MemoryStream(RawFixtureBytes(LightTags(DxfVersion.AutoCad2018, 3, false), false));
        return (DxfDocument.Load(input) ?? throw new InvalidOperationException("LIGHT rejected.")).Entities.Lights.Single();
    }

    private static void AssertLight(Light light, short kind)
    {
        Equal((LightType)kind, light.LightType, "Light kind");
        Equal("Lamp Żółć", light.Name, "Unicode light name");
        Equal(0, light.VersionNumber, "Light schema version");
        Equal(new Vector3(2, -3, 5), light.Position, "WCS position");
        Equal(new Vector3(-7, 11, 13), light.Target, "WCS target");
        SameDoubleBits(1.2500000000000002, light.Intensity, "Authored intensity");
        Equal((LightAttenuationType)(kind - 1), light.AttenuationType, "Attenuation kind");
        Equal(0.125, light.AttenuationStartLimit, "Attenuation start");
        Equal(2048.0, light.AttenuationEndLimit, "Attenuation end");
        Equal(37.5, light.HotspotAngle, "Hotspot degrees"); Equal(62.75, light.FalloffAngle, "Falloff degrees");
        Check(!light.IsOn && light.PlotGlyph && light.UseAttenuationLimits && !light.CastShadows, "Light flags changed.");
        Equal(LightShadowType.ShadowMap, light.ShadowType, "Shadow type");
        Equal(1024, light.ShadowMapSize, "Shadow map size"); Equal((short)7, light.ShadowMapSoftness, "Softness");
        Equal((short)3, light.Color.Index, "Common entity color collided with light parameters");
        Equal("retained light data", (string)light.XData["LIGHT_TEST"].XDataRecord.Single().Value, "XData");
    }

    private static void LightWire(DxfVersion version, bool binary, short kind, bool reverse)
    {
        var tags = LightTags(version, kind, reverse);
        if (reverse && !binary)
        {
            int end = tags.FindIndex(t => t.Code == 1001);
            // Add comments at every scalar boundary, including before the version in reversed order.
            int marker = tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbLight"));
            for (int i = end; i > marker; i--) tags.Insert(i, new(999, "LIGHT 0 ENDSEC 1001"));
        }
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("LIGHT input rejected.");
        var source = doc.Entities.Lights.Single(); AssertLight(source, kind);
        doc.Entities.Add((Light)source.Clone());
        for (int cycle = 0; cycle < 3; cycle++)
        {
            using var output = new MemoryStream();
            if (version < DxfVersion.AutoCad2007)
            {
#if DEBUG
                Throws<NotSupportedException>(() => doc.Save(output, binary));
#else
                Check(!doc.Save(output, binary), "Unsupported LIGHT export accepted.");
#endif
                Equal(0L, output.Length, "Version preflight wrote data");
                return;
            }
            bool transport = cycle % 2 == 0 ? binary : !binary;
            Check(doc.Save(output, transport), "LIGHT save failed.");
            output.Position = 0;
            var raw = DxfRawDocument.Load(output);
            foreach (var record in raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "LIGHT"))
            {
                var packet = record.Tags.SkipWhile(t => !(t.Code == 100 && Equals(t.Value, "AcDbLight")))
                    .Skip(1).TakeWhile(t => t.Code != 1001).ToArray();
                foreach (var expected in LightTags(version, kind, false).SkipWhile(t => !(t.Code == 100 && Equals(t.Value, "AcDbLight")))
                    .Skip(1).TakeWhile(t => t.Code != 1001 && t.Code != 0))
                    if (expected.Code != 1) Equal(expected.Value, packet.Single(t => t.Code == expected.Code).Value, "Exact LIGHT wire field");
            }
            if (cycle == 0 && !reverse)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"light-{version}-{binary}-{kind}.dxf"), output.ToArray());
            output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("LIGHT reload failed.");
            Equal(2, doc.Entities.Lights.Count(), "LIGHT clone count");
            foreach (Light light in doc.Entities.Lights) AssertLight(light, kind);
            Equal(new Vector3(20, 30, 40), doc.Entities.Lines.Single().StartPoint, "Following entity");
        }
        Check(input.CanRead, "Caller stream was closed.");
    }

    private static void LightPlacement(DxfVersion version, bool binary, int placement)
    {
        var doc = new DxfDocument(version); var light = (Light)LoadTestLight().Clone();
        switch (placement)
        {
            case 0: doc.Entities.Add(light); break;
            case 1:
                doc.Layouts.Add(new Layout("Lighting")); doc.Entities.ActiveLayout = "Lighting";
                doc.Entities.Add(light); doc.Entities.ActiveLayout = "Model"; break;
            case 2:
                var inner = new Block("InnerLight"); inner.Entities.Add(light);
                var outer = new Block("OuterLight"); outer.Entities.Add(new Insert(inner)); doc.Entities.Add(new Insert(outer)); break;
            default: var unused = new Block("UnusedLight"); unused.Entities.Add(light); doc.Blocks.Add(unused); break;
        }
        using var output = new MemoryStream(); output.Write(new byte[] { 19, 23, 47 }); output.Position = 1;
        string? handle = light.Handle; string seed = doc.DrawingVariables.HandleSeed;
        if (version < DxfVersion.AutoCad2007)
        {
#if DEBUG
            Throws<NotSupportedException>(() => doc.Save(output, binary));
#else
            Check(!doc.Save(output, binary), "Legacy LIGHT admitted.");
#endif
            Check(output.ToArray().SequenceEqual(new byte[] { 19, 23, 47 }), "Preflight modified existing bytes.");
            Equal(1L, output.Position, "Preflight moved stream"); Equal(seed, doc.DrawingVariables.HandleSeed, "Preflight allocated handles");
            Equal(handle, light.Handle, "Preflight changed identity");
        }
        else
        {
            output.SetLength(0); output.Position = 0; Check(doc.Save(output, binary), "Placed LIGHT save failed.");
            output.Position = 0; var copy = DxfDocument.Load(output) ?? throw new InvalidOperationException("Placed LIGHT reload failed.");
            AssertLight(copy.Blocks.SelectMany(b => b.Entities).OfType<Light>().Single(), 3);
        }
    }

    private static void LightInvalid(DxfVersion version, bool binary, int failure)
    {
        var tags = LightTags(version, 2, false);
        int at = tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbLight"));
        switch (failure)
        {
            case 0: tags.Insert(at + 1, new(40, 2.0)); break;
            case 1: tags[tags.FindIndex(t => t.Code == 70)] = new(70, (short)4); break;
            case 2: tags[tags.FindIndex(t => t.Code == 72)] = new(72, (short)-1); break;
            case 3: tags.RemoveAt(tags.FindIndex(t => t.Code == 31)); break;
            case 4: tags[tags.FindIndex(t => t.Code == 40)] = new(40, -1.0); break;
            case 5: tags[tags.FindIndex(t => t.Code == 91)] = new(91, -1); break;
            case 6: tags[at] = new(100, "AcDbPoint"); break;
            case 7: tags.Insert(at + 1, new(100, "AcDbLight")); break;
            case 8: tags.Insert(at + 1, new(101, "Embedded Object")); break;
            default: tags[tags.FindIndex(t => t.Code == 73)] = new(73, (short)2); break;
        }
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
#if DEBUG
        Throws<InvalidDataException>(() => DxfDocument.Load(input));
#else
        Check(DxfDocument.Load(input) == null, "Malformed LIGHT accepted.");
#endif
        Check(input.CanRead, "Rejected LIGHT closed caller stream.");
    }

    private static void LightApi()
    {
        var light = LoadTestLight(); var copy = (Light)light.Clone();
        AssertLight(copy, 3);
        Check(copy.Handle == null && copy.Owner == null, "Clone copied identity/owner.");
        Check(!ReferenceEquals(copy.XData["LIGHT_TEST"], light.XData["LIGHT_TEST"]), "Clone shares XData.");
        Check(!ReferenceEquals(copy.Color, light.Color) && !ReferenceEquals(copy.Layer, light.Layer), "Clone shares tables/color.");
        foreach (double bad in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, -1.0 })
        {
            Throws<ArgumentOutOfRangeException>(() => light.Intensity = bad);
            Throws<ArgumentOutOfRangeException>(() => light.AttenuationStartLimit = bad);
            Throws<ArgumentOutOfRangeException>(() => light.AttenuationEndLimit = bad);
            Throws<ArgumentOutOfRangeException>(() => light.HotspotAngle = bad);
            Throws<ArgumentOutOfRangeException>(() => light.FalloffAngle = bad);
        }
        Throws<ArgumentOutOfRangeException>(() => light.Position = new(double.NaN, 0, 0));
        Throws<ArgumentOutOfRangeException>(() => light.Target = new(0, double.PositiveInfinity, 0));
        Throws<ArgumentOutOfRangeException>(() => light.ShadowMapSoftness = -1);
        Throws<ArgumentNullException>(() => light.Name = null!);
        AssertLight(light, 3);
        var block = new Block("ClonedLight"); block.Entities.Add(copy);
        var insert = new Insert(block, new Vector3(10, 20, 30)) { Scale = new Vector3(2, 2, 2) };
        var clonedInsert = (Insert)insert.Clone();
        var clonedLight = clonedInsert.Block.Entities.OfType<Light>().Single(); clonedLight.Name = "changed";
        Equal("Lamp Żółć", copy.Name, "INSERT clone aliased light");
        Light exploded = insert.Explode().OfType<Light>().Single();
        Equal(new Vector3(14, 14, 40), exploded.Position, "INSERT explosion position");
        Equal(4096.0, exploded.AttenuationEndLimit, "INSERT explosion attenuation");
        copy = (Light)light.Clone();
        var doc = new DxfDocument(); doc.Entities.Add(copy); Check(doc.Entities.Remove(copy), "LIGHT removal failed.");
        Check(!doc.Entities.Lights.Any(), "LIGHT remained after removal.");
    }

    private static void LightTransform(int operation)
    {
        Light light = LoadTestLight(); Vector3 oldPosition = light.Position, oldTarget = light.Target;
        Matrix3 matrix = operation switch
        {
            0 => Matrix3.Identity,
            1 => Matrix3.RotationX(0.3) * Matrix3.RotationZ(-0.6),
            2 => Matrix3.Reflection(Vector3.UnitX) * Matrix3.Scale(3),
            3 => Matrix3.Scale(1, 2, 3),
            4 => new Matrix3(1, 0.2, 0, 0, 1, 0, 0, 0, 1),
            5 => Matrix3.Scale(0),
            _ => Matrix3.Scale(double.MaxValue)
        };
        Vector3 translation = new(17, -23, 31);
        if (operation >= 3)
        {
            Throws<ArgumentException>(() => light.TransformBy(matrix, translation));
            AssertLight(light, 3); return;
        }
        light.TransformBy(matrix, translation);
        Equal(matrix * oldPosition + translation, light.Position, "Transformed light position");
        Equal(matrix * oldTarget + translation, light.Target, "Transformed light target");
        Near(operation == 2 ? 6144 : 2048, light.AttenuationEndLimit, "Attenuation distance scaling");
        Equal(37.5, light.HotspotAngle, "Transform changed angle");
        Equal(1.2500000000000002, light.Intensity, "Transform changed authored intensity");
    }
}
