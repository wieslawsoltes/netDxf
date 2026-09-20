// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Reflection;
using System.Runtime.ExceptionServices;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private sealed class CountFailureWriter : StringWriter
    {
        internal int FailCode;
        internal bool Armed = true;
        public override void WriteLine(int value)
        {
            if (this.Armed && value == this.FailCode) throw new IOException("Injected count output failure.");
            base.WriteLine(value);
        }
    }

    private static void RegisterSplineCountIntegrationTests()
    {
        foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2007))
            foreach (bool binary in new[] { false, true }) for (int policy = 0; policy < 3; policy++)
            {
                int mode = policy;
                Run($"spline-counts/integration/{version}/{binary}/{mode}", () =>
                {
                    var doc = new DxfDocument(version);
                    doc.Entities.Add(CountSubject(1));
                    doc.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX));
                    doc.Entities.Add(CountSubject(4));
                    doc.Entities.Add(CountSubject(2));
                    SetCountPolicy(doc, mode);
                    byte[] bytes = CountSave(doc, binary);
                    using var input = new MemoryStream(bytes);
                    var raw = DxfRawDocument.Load(input);
                    var records = raw.Sections.SelectMany(s => s.Records).Where(r =>
                        r.Name == "SPLINE" || r.Name == "HELIX" || r.Name == "LINE").ToArray();
                    Equal(4, records.Length, "Mixed entity inventory");
                    foreach (var record in records)
                    {
                        if (record.Name == "LINE")
                        {
                            Check(!record.Tags.Any(t => t.Code >= 72 && t.Code <= 74), "Count adapter leaked into another entity");
                            continue;
                        }
                        var part = record.Tags.SkipWhile(t => t.Code != 100 || !Equals(t.Value, "AcDbSpline"))
                            .Skip(1).TakeWhile(t => t.Code != 100).ToArray();
                        foreach (var pair in new[] { (72, 40), (73, 10), (74, 11) })
                        {
                            var counts = part.Where(t => t.Code == pair.Item1).ToArray();
                            Equal(mode == 0 ? 0 : 1, counts.Length, "Mixed count presence");
                            if (mode != 0) Equal(part.Count(t => t.Code == pair.Item2), (int)(short)counts[0].Value, "Mixed payload inventory");
                        }
                        if (record.Name == "HELIX")
                        {
                            var tail = record.Tags.SkipWhile(t => t.Code != 100 || !Equals(t.Value, "AcDbHelix")).ToArray();
                            Check(tail.Length > 0 && !tail.Any(t => t.Code >= 72 && t.Code <= 74), "Counts leaked into HELIX metadata");
                        }
                    }
                });
            }
        foreach (int code in new[] { 71, 72, 73, 74 })
            Run($"spline-counts/sink-restoration/{code}", () =>
            {
                var assembly = typeof(DxfDocument).Assembly;
                var writerType = assembly.GetType("netDxf.IO.DxfWriter", true)!;
                var sinkType = assembly.GetType("netDxf.IO.TextCodeValueWriter", true)!;
                object writer = Activator.CreateInstance(writerType, nonPublic: true)!;
                using var text = new CountFailureWriter { FailCode = code };
                object sink = Activator.CreateInstance(sinkType, text)!;
                var chunk = writerType.GetField("chunk", BindingFlags.NonPublic | BindingFlags.Instance)!;
                chunk.SetValue(writer, sink);
                var doc = new DxfDocument(); SetCountPolicy(doc, 2);
                writerType.GetField("doc", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(writer, doc);
                var method = writerType.GetMethod("WriteSplineWithCountPolicy", BindingFlags.NonPublic | BindingFlags.Instance)!;
                void Write()
                {
                    try { method.Invoke(writer, new object[] { CountSubject(1), false }); }
                    catch (TargetInvocationException e) when (e.InnerException != null)
                    { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
                }
                Throws<IOException>(Write);
                Check(ReferenceEquals(sink, chunk.GetValue(writer)), "Failure retained a wrapping writer");
                text.Armed = false; text.GetStringBuilder().Clear();
                Write();
                Check(ReferenceEquals(sink, chunk.GetValue(writer)), "Successful output retained a wrapping writer");
                var lines = text.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (int countCode in new[] { 72, 73, 74 })
                    Equal(1, lines.Where((_, i) => (i & 1) == 0).Count(s => s == countCode.ToString()), "Retry count multiplicity");
            });
    }
}
