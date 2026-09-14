using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterOleVersionPresenceTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                foreach (bool present in new[] { false, true })
                    foreach (short value in new short[] { 0, 1, 7, short.MaxValue })
                    {
                        DxfVersion v = version; bool b = binary, p = present; short n = value;
                        Run($"oleframe/version-presence/{v}/{b}/{p}/{n}", () => OleVersionPresence(v, b, p, n));
                    }
    }

    private static void OleVersionPresence(DxfVersion version, bool binary, bool present, short value)
    {
        var tags = LegacyOleTags(version, 128, true);
        int at = tags.FindIndex(t => t.Code == 70);
        if (present) tags[at] = new(70, value); else tags.RemoveAt(at);
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Version-presence input rejected.");
        OleFrame frame = doc.Entities.OleFrames.Single();
        Equal(present ? value : (short)1, frame.OleVersion, "Existing version getter contract changed");
        doc.Entities.Add((OleFrame)frame.Clone());
        for (int cycle = 0; cycle < 3; ++cycle)
        {
            using var output = new MemoryStream();
            Check(doc.Save(output, cycle % 2 == 0 ? !binary : binary), "Version-presence save failed.");
            output.Position = 0;
            var raw = DxfRawDocument.Load(output);
            foreach (var record in raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "OLEFRAME"))
            {
                var versions = record.Tags.Where(t => t.Code == 70).ToArray();
                Equal(present ? 1 : 0, versions.Length, "OLE version presence changed");
                if (present) Equal(value, (short)versions[0].Value, "Explicit OLE version changed");
                Equal(128, (int)record.Tags.Single(t => t.Code == 90).Value, "Payload length changed");
                Check(record.Tags.Where(t => t.Code == 310).SelectMany(t => (byte[])t.Value)
                    .SequenceEqual(OlePayload(128)), "Opaque payload changed.");
                Equal("OLE", (string)record.Tags.Single(t => t.Code == 1).Value, "Required terminator lost");
            }
            if (cycle == 1 && value == 1)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"oleversion-{version}-{binary}-{present}.dxf"), output.ToArray());
            output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Version-presence reload failed.");
            Equal(2, doc.Entities.OleFrames.Count(), "Clone/frame count changed");
            foreach (OleFrame current in doc.Entities.OleFrames)
            {
                Equal(present ? value : (short)1, current.OleVersion, "Reload getter contract changed");
                Equal("after legacy bytes", (string)current.XData["LEGACY_OLE"].XDataRecord.Single().Value, "XData changed");
            }
            Equal(new Vector3(10, 20, 30), doc.Entities.Lines.Single().StartPoint, "Following LINE changed");
        }
        Check(input.CanRead, "Reader closed caller stream.");
    }
}
