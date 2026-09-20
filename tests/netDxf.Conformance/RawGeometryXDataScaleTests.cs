// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterRawGeometryXDataScaleTests()
    {
        foreach (DxfVersion version in HandleProfiles)
            foreach (bool binary in new[] { false, true })
                foreach (short code in new short[] { 1040, 1041, 1042 })
                    foreach (double value in new[] { -2.0, 0.0, 3.5 })
                    {
                        Run($"raw-xdata-scale/{version}/{binary}/{code}/{value:R}", () =>
                        {
                            var tags = RawLineTags(version);
                            int at = tags.FindIndex(t => t.Code == 1001) + 1;
                            tags.Insert(at, new DxfTag(code, value));
                            byte[] bytes = version == DxfVersion.AutoCad12
                                ? RawR12Bytes(tags, binary) : RawFixtureBytes(tags, binary);
                            var raw = LoadRaw(bytes); var line = RawLineRecord(raw);
                            object geometry = RawLineRead(raw, line);
                            var start = RawLinePoint(geometry, "StartPoint");
                            var end = RawLinePoint(geometry, "EndPoint");
                            // No-op identity and read access are valid even when
                            // changing geometry requires application-specific regeneration.
                            Check(ReferenceEquals(raw, RawLineEdit(raw, line, start, end)), "Decorated no-op lost identity");
                            Check(bytes.SequenceEqual(SaveRaw(raw)), "No-op changed source bytes");
                            if (code == 1040)
                            {
                                var changed = RawLineEdit(raw, line, 2 * start, 2 * end);
                                var record = RawLineRecord(changed);
                                var before = line.Tags.Where(t => !RawLineCoordinateCodes.Contains(t.Code)).ToArray();
                                var after = record.Tags.Where(t => !RawLineCoordinateCodes.Contains(t.Code)).ToArray();
                                SameRawTags(before, after);
                                Check(before.Zip(after).All(p => ReferenceEquals(p.First, p.Second)), "Ordinary XData was replaced");
                            }
                            else
                            {
                                // Both endpoints scaled about the origin, so preserving
                                // the geometry-dependent scalar would leave stale data.
                                Throws<NotSupportedException>(() => RawLineEdit(raw, line, 2 * start, 2 * end));
                            }
                            Check(bytes.SequenceEqual(SaveRaw(raw)), "Attempt changed the original snapshot");
                            RawLinePointBits(start, RawLinePoint(RawLineRead(raw, line), "StartPoint"));
                            RawLinePointBits(end, RawLinePoint(RawLineRead(raw, line), "EndPoint"));
                        });
                    }
    }
}
