// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterRawLineXDataScopeTests()
    {
        foreach (DxfVersion version in HandleProfiles) foreach (bool binary in new[] { false, true })
        {
            foreach (short after in new short[] { 1001, 1000, 1070 })
                Run($"raw-line-xdata/reject/{version}/{binary}/{after}", () =>
                {
                    var tags = RawLineTags(version);
                    int at = tags.FindIndex(t => t.Code == after) + 1;
                    tags.InsertRange(at, new DxfTag[] { new(101, "Embedded Object"), new(10, 999.0) });
                    byte[] bytes = version == DxfVersion.AutoCad12 ? RawR12Bytes(tags, binary) : RawFixtureBytes(tags, binary);
                    var raw = LoadRaw(bytes); var line = RawLineRecord(raw);
                    // The generic raw snapshot retains unknown tags. The schema reader must
                    // not let an embedded marker bypass its ordinary-data-after-XData rule.
                    Throws<FormatException>(() => raw.ReadLineGeometry(line));
                    Throws<FormatException>(() => raw.WithLineEndpoints(line, new(1.25, -2, 3), new(4, 5, -6)));
                    Check(bytes.SequenceEqual(SaveRaw(raw)), "Rejected semantic read changed source bytes");
                });
            Run($"raw-line-xdata/valid-embedded/{version}/{binary}", () =>
            {
                var tags = RawLineTags(version);
                int at = tags.FindIndex(t => t.Code == 1001);
                tags.InsertRange(at, new DxfTag[] { new(101, "Embedded Object"), new(10, 999.0) });
                byte[] bytes = version == DxfVersion.AutoCad12 ? RawR12Bytes(tags, binary) : RawFixtureBytes(tags, binary);
                var raw = LoadRaw(bytes); var line = RawLineRecord(raw);
                var geometry = raw.ReadLineGeometry(line);
                RawLinePointBits(new(1.25, -2, 3), geometry.StartPoint);
                Check(ReferenceEquals(raw, raw.WithLineEndpoints(line, geometry.StartPoint, geometry.EndPoint)), "Valid no-op changed snapshot");
                Throws<NotSupportedException>(() => raw.WithLineEndpoints(line, Vector3.Zero, Vector3.UnitX));
                Check(bytes.SequenceEqual(SaveRaw(raw)), "Valid decorated source changed bytes");
            });
        }
    }
}
