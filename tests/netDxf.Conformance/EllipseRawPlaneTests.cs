// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly short[] EllipsePlaneFields = {10,20,30,11,21,31,40,41,42,210,220,230};
    private static DxfRawDocument EllipsePlaneEdit(DxfRawDocument raw, DxfRawRecord record,
        Vector3 center, Vector3 major, Vector3 normal, double ratio = .25, double start = .25, double end = 5.75)
        => (DxfRawDocument)RawLineCall(raw, "WithEllipseGeometryAndPlane", record, center, major, ratio, start, end, normal);
    private static Vector3 EllipsePlaneNormal(int variant) => variant switch
    { 0 => new(2,-3,6), 1 => new(0,2,0), 2 => new(-2,3,-6), _ => Vector3.UnitZ };
    private static Vector3 EllipsePlaneMajor(int variant) => variant switch
    { 1 => new(0,0,6), 3 => new(6,-8,0), _ => new(3,2,0) };

    private static void RegisterEllipseRawPlaneTests()
    {
        foreach (DxfVersion version in HandleProfiles.Where(v => v >= DxfVersion.AutoCad13))
            foreach (bool binary in new[] {false,true}) foreach (bool block in new[] {false,true})
                for (int variant = 0; variant < 4; variant++)
                {
                    int v = variant;
                    Run($"ellipse-raw-plane/matrix/{version}/{binary}/{block}/{v}", () =>
                    {
                        var raw = LoadRaw(RawFixtureBytes(RawEllipseTags(version, block, v), binary));
                        var record = RawEllipseRecord(raw); byte[] source = SaveRaw(raw); var view = RawEllipseRead(raw, record);
                        Check(ReferenceEquals(raw, EllipsePlaneEdit(raw, record, RawLinePoint(view,"Center"), RawLinePoint(view,"MajorAxis"),
                            RawLinePoint(view,"ExtrusionDirection"), RawEllipseScalar(view,"AxisRatio"), RawEllipseScalar(view,"StartParameter"),
                            RawEllipseScalar(view,"EndParameter"))), "Plane no-op lost snapshot");
                        var edited = EllipsePlaneEdit(raw, record, new(8,-16,32), EllipsePlaneMajor(v), EllipsePlaneNormal(v));
                        var replacement = RawEllipseRecord(edited);
                        AssertOutsideRecordUnchanged(raw, record, edited, replacement.Tags.Count);
                        var a = record.Tags.Where(t => !EllipsePlaneFields.Contains(t.Code)).ToArray();
                        var b = replacement.Tags.Where(t => !EllipsePlaneFields.Contains(t.Code)).ToArray();
                        SameRawTags(a,b); Check(a.Zip(b).All(p => ReferenceEquals(p.First,p.Second)), "Unselected identity changed");
                        foreach (var tag in record.Tags.Where(t => EllipsePlaneFields.Contains(t.Code)))
                        {
                            var next = replacement.Tags.Single(t => t.Code == tag.Code);
                            if (ParameterBits((double)tag.Value) == ParameterBits((double)next.Value))
                                Check(ReferenceEquals(tag,next), "Unchanged selected tag lost identity");
                        }
                        Check(source.SequenceEqual(SaveRaw(raw)) && !edited.HasOriginalBytes, "Source bytes or edited-byte flag changed");
                        RawLinePointBits(RawEllipseNormal(v),RawLinePoint(view,"ExtrusionDirection"));
                        foreach (bool output in new[] {false,true})
                        {
                            byte[] bytes = SaveRaw(edited,output); var loaded = LoadRaw(bytes); var g = RawEllipseRead(loaded,RawEllipseRecord(loaded));
                            RawLinePointBits(new(8,-16,32),RawLinePoint(g,"Center")); RawLinePointBits(EllipsePlaneMajor(v),RawLinePoint(g,"MajorAxis"));
                            RawLinePointBits(EllipsePlaneNormal(v),RawLinePoint(g,"ExtrusionDirection"));
                            SameDoubleBits(.25,RawEllipseScalar(g,"AxisRatio"),"Plane ratio"); SameDoubleBits(.25,RawEllipseScalar(g,"StartParameter"),"Plane start");
                            SameDoubleBits(5.75,RawEllipseScalar(g,"EndParameter"),"Plane end");
                            Equal(version,loaded.Version,"Plane version"); Equal(raw.EncodingCodePage,loaded.EncodingCodePage,"Plane encoding"); SameRawTags(edited.Tags,loaded.Tags);
                            string stem = $"ellipse-raw-plane-{version}-{binary}-{block}-{v}-{output}";
                            File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-before.dxf"),SaveRaw(raw,output));
                            File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-after.dxf"),bytes);
                        }
                        Throws<ArgumentException>(() => EllipsePlaneEdit(edited,record,Vector3.Zero,Vector3.UnitX,Vector3.UnitZ));
                    });
                }
        DxfRawDocument Modified(Action<List<DxfTag>> action, int variant = 0)
        { var tags = RawEllipseTags(DxfVersion.AutoCad2018,false,variant); action(tags); return DxfRawDocument.Create(tags); }
        foreach (bool reverse in new[] {false,true})
            Run($"ellipse-raw-plane/normal-only/{reverse}", () =>
            {
                var raw = Modified(_ => {}); var record = RawEllipseRecord(raw); var g = RawEllipseRead(raw,record);
                var normal = reverse ? new Vector3(0,0,-1) : new Vector3(0,3,4);
                var edit = EllipsePlaneEdit(raw,record,RawLinePoint(g,"Center"),RawLinePoint(g,"MajorAxis"),normal,.5,0,2*Math.PI);
                var selected = RawEllipseRecord(edit);
                Check(record.Tags.Where(t => RawEllipseFields.Contains(t.Code)).All(t => selected.Tags.Any(n => ReferenceEquals(t,n))), "Normal-only edit rewrote geometry");
                RawLinePointBits(normal,RawLinePoint(RawEllipseRead(edit,selected),"ExtrusionDirection"));
            });
        foreach (short code in new short[] {39,92,310,1005,1010,1041,1042})
            Run($"ellipse-raw-plane/guard/{code}", () =>
            {
                var raw = Modified(t => t.Insert(RawLineAt(t,1001)+(code>=1000?1:0),new(code,code==92?(object)0:code==310?new byte[]{1}:code==1005?"B":2.0)));
                var record = RawEllipseRecord(raw); byte[] source = SaveRaw(raw);
                Check(ReferenceEquals(raw,EllipsePlaneEdit(raw,record,new(1.25,-2,3),new(4,0,0),Vector3.UnitZ,.5,0,2*Math.PI)),"Guarded no-op changed source");
                Throws<NotSupportedException>(() => EllipsePlaneEdit(raw,record,new(1.25,-2,3),new(4,0,0),new(0,3,4),.5,0,2*Math.PI));
                Check(source.SequenceEqual(SaveRaw(raw)),"Guard rejection changed source");
            });
        foreach (short code in new short[] {320,330,340,350,360,1005})
            Run($"ellipse-raw-plane/incoming/{code}", () =>
            {
                var raw=Modified(t=>{int at=t.FindIndex(x=>x.Code==0&&Equals(x.Value,"POINT"))+2;if(code==1005)t.Insert(at++,new(1001,"REF"));t.Insert(at,new(code,"000a"));});
                Throws<NotSupportedException>(()=>EllipsePlaneEdit(raw,RawEllipseRecord(raw),Vector3.Zero,Vector3.UnitX,new(0,3,4)));
            });
        foreach (bool embedded in new[] {false,true})
            Run($"ellipse-raw-plane/private/{embedded}", () =>
            {
                var raw=Modified(t=>t.InsertRange(RawLineAt(t,1001),embedded?new DxfTag[]{new(101,"Embedded Object"),new(210,9.0)}:new DxfTag[]{new(102,"{APP"),new(210,9.0),new(102,"}")}));
                RawLinePointBits(Vector3.UnitZ,RawLinePoint(RawEllipseRead(raw,RawEllipseRecord(raw)),"ExtrusionDirection"));
                Throws<NotSupportedException>(()=>EllipsePlaneEdit(raw,RawEllipseRecord(raw),Vector3.Zero,Vector3.UnitX,new(0,3,4)));
            });
        for (int slot=0;slot<12;slot++) foreach (double bad in new[] {double.NaN,double.PositiveInfinity,double.NegativeInfinity})
        {
            int s=slot;
            Run($"ellipse-raw-plane/nonfinite/{s}/{ParameterBits(bad)}",()=>
            {
                var raw=Modified(_=>{}); byte[] source=SaveRaw(raw); double[] p={0,0,0,1,0,0,.5,.25,5.75,0,0,1}; p[s]=bad;
                Throws<ArgumentOutOfRangeException>(()=>EllipsePlaneEdit(raw,RawEllipseRecord(raw),new(p[0],p[1],p[2]),new(p[3],p[4],p[5]),new(p[9],p[10],p[11]),p[6],p[7],p[8]));
                Check(source.SequenceEqual(SaveRaw(raw)),"Nonfinite rejection changed bytes");
            });
        }
        foreach (double bad in new[] {0.0,-1.0,1.0001})
            Run($"ellipse-raw-plane/bad-ratio/{bad}",()=>{var raw=Modified(_=>{});Throws<ArgumentOutOfRangeException>(()=>EllipsePlaneEdit(raw,RawEllipseRecord(raw),Vector3.Zero,Vector3.UnitX,Vector3.UnitZ,bad));});
        foreach (Vector3 normal in new[] {Vector3.Zero,Vector3.UnitX,new Vector3(1,2,3)})
            Run($"ellipse-raw-plane/invalid-normal/{normal}",()=>{var raw=Modified(_=>{});Throws<ArgumentOutOfRangeException>(()=>EllipsePlaneEdit(raw,RawEllipseRecord(raw),Vector3.Zero,Vector3.UnitX,normal));});
        foreach (double epsilon in new[] {1e-12,1.0,100.0})
            Run($"ellipse-raw-plane/tolerance/{epsilon}",()=>
            {
                var raw=Modified(_=>{});double old=MathHelper.Epsilon;
                try{MathHelper.Epsilon=epsilon;EllipsePlaneEdit(raw,RawEllipseRecord(raw),Vector3.Zero,Vector3.UnitX,new(1e-13,0,1));Throws<ArgumentOutOfRangeException>(()=>EllipsePlaneEdit(raw,RawEllipseRecord(raw),Vector3.Zero,Vector3.UnitX,new(1e-8,0,1)));}
                finally{MathHelper.Epsilon=old;}
            });
        foreach (double scale in new[] {double.Epsilon,-double.Epsilon,1e-200,-1e200,double.MaxValue})
            Run("ellipse-raw-plane/extrusion-scale/"+ParameterBits(scale),()=>
            {
                var raw=Modified(_=>{});var edit=EllipsePlaneEdit(raw,RawEllipseRecord(raw),Vector3.Zero,Vector3.UnitX,new(0,0,scale),double.Epsilon,-7,19);
                foreach(bool binary in new[]{false,true}){var loaded=LoadRaw(SaveRaw(edit,binary));var g=RawEllipseRead(loaded,RawEllipseRecord(loaded));RawLinePointBits(new(0,0,scale),RawLinePoint(g,"ExtrusionDirection"));SameDoubleBits(-7,RawEllipseScalar(g,"StartParameter"),"Stored parameter normalized");}
            });
        Run("ellipse-raw-plane/optional-bits-and-budget",()=>
        {
            var tags=RawEllipseTags(DxfVersion.AutoCad13,false,1);var raw=DxfRawDocument.Create(tags,false,new DxfRawOptions(maximumTags:tags.Count+3));
            Throws<InvalidOperationException>(()=>EllipsePlaneEdit(raw,RawEllipseRecord(raw),Vector3.Zero,new(0,0,2),Vector3.UnitY));
            raw=DxfRawDocument.Create(tags,false,new DxfRawOptions(maximumTags:tags.Count+4));
            var edit=EllipsePlaneEdit(raw,RawEllipseRecord(raw),Vector3.Zero,new(0,0,2),Vector3.UnitY);Equal(tags.Count+4,edit.Tags.Count,"Complete-vector component budget");
            var record=RawEllipseRecord(edit);Check(record.Tags.Any(t=>t.Code==210) && !record.Tags.Any(t=>t.Code==30),"Required vector X or optional center Z presence");
            raw=DxfRawDocument.Create(tags);edit=EllipsePlaneEdit(raw,RawEllipseRecord(raw),new(1.25,-2,0),new(4,0,0),new(-0.0,0,1),.5,0,2*Math.PI);
            record=RawEllipseRecord(edit);Equal(tags.Count+3,edit.Tags.Count,"Signed-zero extrusion did not materialize one complete vector");SameDoubleBits(-0.0,(double)record.Tags.Single(t=>t.Code==210).Value,"Extrusion sign bit");
            Check(record.Tags.Count(t=>t.Code==220||t.Code==230)==2,"Changed extrusion omitted required vector defaults");
            Check(ReferenceEquals(edit,EllipsePlaneEdit(edit,record,new(1.25,-2,0),new(4,0,0),new(-0.0,0,1),.5,0,2*Math.PI)),"Signed-zero repeat lost identity");
        });
        foreach (short missing in new short[]{210,220,230})
            Run($"ellipse-raw-plane/partial-extrusion/{missing}",()=>
            {
                var raw=Modified(t=>t.RemoveAt(RawLineAt(t,missing)));var edit=EllipsePlaneEdit(raw,RawEllipseRecord(raw),new(8,-16,32),new(3,2,0),new(2,-3,6));
                var record=RawEllipseRecord(edit);RawLinePointBits(new(2,-3,6),RawLinePoint(RawEllipseRead(edit,record),"ExtrusionDirection"));
                Equal(1,record.Tags.Count(t=>t.Code==missing),"Missing extrusion component not inserted once");
            });
        for (int layout = 0; layout < 8; layout++) foreach (bool binary in new[] { false, true })
        {
            int kind = layout;
            Run($"ellipse-raw-plane/vector-packet/{kind}/{binary}", () =>
            {
                short[][] layouts = {
                    Array.Empty<short>(), new short[] {210}, new short[] {220}, new short[] {230},
                    new short[] {220,230}, new short[] {210,230}, new short[] {210,220}, new short[] {230,220,210}
                };
                var raw = Modified(tags => {
                    int at = RawLineAt(tags,210);
                    tags.RemoveAll(t => t.Code == 210 || t.Code == 220 || t.Code == 230);
                    foreach (short code in layouts[kind]) {
                        tags.Insert(at++, new DxfTag(code, code == 230 ? 1.0 : 0.0));
                        if (kind == 7) tags.Insert(at++, new DxfTag(999,"retained vector separator"));
                    }
                });
                var record = RawEllipseRecord(raw); byte[] source = SaveRaw(raw);
                var edit = EllipsePlaneEdit(raw,record,new(8,-16,32),new(0,0,6),new(0,2,0));
                var result = RawEllipseRecord(edit); int x = result.Tags.ToList().FindIndex(t=>t.Code==210);
                Check(x >= 0 && result.Tags[x+1].Code==220 && result.Tags[x+2].Code==230,"Extrusion vector groups are not complete and adjacent");
                Equal(3,result.Tags.Count(t=>t.Code==210||t.Code==220||t.Code==230),"Extrusion vector duplicates");
                var before = record.Tags.Where(t=>t.Code!=210&&t.Code!=220&&t.Code!=230&&!RawEllipseFields.Contains(t.Code)).ToArray();
                var after = result.Tags.Where(t=>t.Code!=210&&t.Code!=220&&t.Code!=230&&!RawEllipseFields.Contains(t.Code)).ToArray();
                SameRawTags(before,after);Check(before.Zip(after).All(p=>ReferenceEquals(p.First,p.Second)),"Regrouping changed unrelated tags");
                var oldX=record.Tags.FirstOrDefault(t=>t.Code==210);
                if(oldX!=null)Check(ReferenceEquals(oldX,result.Tags[x]),"Unchanged X component lost identity");
                RawLinePointBits(new(0,2,0),RawLinePoint(RawEllipseRead(edit,result),"ExtrusionDirection"));
                Check(source.SequenceEqual(SaveRaw(raw)),"Regrouping mutated source bytes");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"ellipse-plane-packet-{kind}-{binary}.dxf"),SaveRaw(edit,binary));
            });
        }
        Run("ellipse-raw-plane/snapshot-and-old-api",()=>
        {
            var raw=Modified(_=>{});var other=Modified(_=>{});var record=RawEllipseRecord(raw);
            Throws<ArgumentException>(()=>EllipsePlaneEdit(raw,RawEllipseRecord(other),Vector3.Zero,Vector3.UnitX,Vector3.UnitZ));
            Throws<ArgumentNullException>(()=>EllipsePlaneEdit(raw,null!,Vector3.Zero,Vector3.UnitX,Vector3.UnitZ));
            Throws<ArgumentException>(()=>EllipsePlaneEdit(raw,raw.Sections.SelectMany(s=>s.Records).First(r=>r.Name=="POINT"),Vector3.Zero,Vector3.UnitX,Vector3.UnitZ));
            var old=RawEllipseEdit(raw,record,new(8,-16,32),new(6,-8,0));var explicitPlane=EllipsePlaneEdit(raw,record,new(8,-16,32),new(6,-8,0),Vector3.UnitZ);
            SameRawTags(old.Tags,explicitPlane.Tags);
            Throws<ArgumentOutOfRangeException>(()=>RawEllipseEdit(raw,record,Vector3.Zero,Vector3.UnitZ));
            var legacy=DxfRawDocument.Create(RawEllipseTags(DxfVersion.AutoCad12));Throws<NotSupportedException>(()=>EllipsePlaneEdit(legacy,RawEllipseRecord(legacy),Vector3.Zero,Vector3.UnitX,Vector3.UnitZ));
        });
        foreach(DxfVersion version in SupportedVersions)foreach(bool binary in new[]{false,true})
            Run($"ellipse-raw-plane/typed/{version}/{binary}",()=>
            {
                var doc=new DxfDocument(version);doc.Comments.Clear();doc.Entities.Add(new Ellipse(Vector3.Zero,8,4));using var output=new MemoryStream();Check(doc.Save(output,binary),"Typed source");
                var raw=LoadRaw(output.ToArray());var edit=EllipsePlaneEdit(raw,RawEllipseRecord(raw),new(8,-16,32),new(0,0,6),Vector3.UnitY,.25,0,2*Math.PI);
                using var input=new MemoryStream(SaveRaw(edit,binary));var loaded=DxfDocument.Load(input)!;var e=loaded.Entities.Ellipses.Single();
                RawLinePointBits(new(8,-16,32),e.Center);RawLinePointBits(Vector3.UnitY,e.Normal);Near(12,e.MajorAxis,"Typed major");Near(3,e.MinorAxis,"Typed minor");Check(e.IsFullEllipse,"Typed closure");Equal(0,loaded.Objects.Validate().Count,"Typed graph");
            });
    }
}
