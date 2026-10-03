// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Globalization;
using netDxf;
using netDxf.Entities;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static readonly double[][] RltPatterns = {
        Array.Empty<double>(), new[] { 1.5, -.5 }, new[] { 0.0, -.25 },
        new[] { 1.25, -.25, .25, -.25 }, new[] { -0.0, -.125, .5, -.125 },
        new[] { double.Epsilon, -double.Epsilon }
    };

    private static void RegisterR12LinetypeTests()
    {
        foreach (bool binary in new[] { false, true })
        {
            bool b = binary;
            for (int kind = 0; kind < 12; kind++)
            for (int pattern = 0; pattern < RltPatterns.Length; pattern++)
            {
                int k = kind, p = pattern;
                Run($"r12-linetype/roundtrip/{b}/{k}/{p}", () => RltRoundtrip(b,k,p));
            }
            for (int fault = 0; fault < 14; fault++)
            { int f=fault; Run($"r12-linetype/read-refusal/{b}/{f}", () => RltReadRefusal(b,f)); }
            Run("r12-linetype/wire/"+b, () => RltWire(b));
            Run("r12-linetype/order-and-case/"+b, () => RltOrder(b));
            Run("r12-linetype/rounded-total/"+b, () => RltRoundedTotal(b));
            Run("r12-linetype/reference-identity/"+b, () => RltReferences(b));
        }
        for (int fault=0;fault<10;fault++)
        { int f=fault; Run("r12-linetype/write-refusal/"+f, () => RltWriteRefusal(f)); }
        Run("r12-linetype/budgets", RltBudgets);
        Run("r12-linetype/culture-and-encoding", RltCulture);
        Run("r12-linetype/source-and-iterator", RltSource);
        Run("r12-linetype/maximum-count", RltMaximum);
    }

    private static Linetype RltType(string name, double[] pattern, string description = "Café pattern") =>
        new(name, pattern.Select(v => (LinetypeSegment)new LinetypeSimpleSegment(v)), description);

    private static EntityObject RltEntity(int kind)
    {
        if (kind < 7) return R12Seeds(0)[kind];
        if (kind == 7) return new Polyline2D(new[]{Vector2.Zero,Vector2.UnitX,Vector2.UnitY},true);
        if (kind == 8) return new Polyline3D(new[]{Vector3.Zero,Vector3.UnitX,Vector3.UnitY},true);
        if (kind == 9) return new Text("Pattern",Vector3.Zero,1);
        if (kind == 10) return RmGrid();
        return RmPolyface(0,0);
    }

    private static void RltSame(Linetype expected, Linetype actual)
    {
        Check(string.Equals(expected.Name,actual.Name,StringComparison.OrdinalIgnoreCase),"Pattern reference name changed");
        Equal(expected.Description,actual.Description,"Pattern description changed");
        Equal(expected.Segments.Count,actual.Segments.Count,"Pattern element count changed");
        for(int i=0;i<expected.Segments.Count;i++)
            Equal(BitConverter.DoubleToInt64Bits(expected.Segments[i].Length),BitConverter.DoubleToInt64Bits(actual.Segments[i].Length),"Signed pattern element bits changed");
    }

    private static void RltRoundtrip(bool binary,int kind,int pattern)
    {
        var entity=RltEntity(kind);var linetype=RltType("PATTERN_"+pattern,RltPatterns[pattern]);
        entity.Linetype=linetype;entity.Layer.Linetype=linetype;
        string? handle=entity.Handle;var owner=entity.Owner;var segments=linetype.Segments.ToArray();
        var raw=DxfR12Codec.Create(new[]{entity});
        Check(entity.Handle==handle && ReferenceEquals(entity.Owner,owner)
            && segments.SequenceEqual(linetype.Segments),"Export changed source entities or pattern objects");
        for(int generation=0;generation<3;generation++)
        {
            raw=R12Reload(raw,generation%2==0?binary:!binary);
            var copy=DxfR12Codec.ReadEntities(raw).Single();
            Equal(entity.GetType(),copy.GetType(),"Pattern carrier type changed");
            RltSame(linetype,copy.Linetype);RltSame(linetype,copy.Layer.Linetype);
            Check(ReferenceEquals(copy.Linetype,copy.Layer.Linetype),"Decoded entity/layer pattern was not shared");
            var clone=(EntityObject)copy.Clone();RltSame(linetype,clone.Linetype);
            Check(!ReferenceEquals(clone.Linetype,copy.Linetype),"Clone aliases source linetype");
            raw=DxfR12Codec.Create(new[]{copy});
        }
    }

    private static EntityObject[] RltWireSeeds()
    {
        var dash=RltType("DASH",new[]{1.5,-.5},"Long dash");
        var center=RltType("CENTER",new[]{1.25,-.25,.25,-.25},"Center line");
        var dot=RltType("DOT",new[]{0.0,-.25},"Café dot");
        var layer=new Layer("STROKES"){Color=new AciColor(2),Linetype=dash};
        var faceLayer=new Layer("FACES"){Color=new AciColor(3),Linetype=dot};
        var face=new PolyfaceMeshFace(new short[]{1,-2,3}){Layer=faceLayer,Color=AciColor.ByBlock};
        return new EntityObject[]{
            new Line(new Vector3(0,1,2),new Vector3(3,4,5)){Layer=layer},
            new Line(new Vector3(6,7,8),new Vector3(9,10,11)){Layer=layer,Linetype=center,Color=new AciColor(3)},
            new PolyfaceMesh(new[]{Vector3.Zero,new Vector3(2,0,0),new Vector3(0,2,0)},new[]{face}){Layer=layer}
        };
    }
    private static void RltWire(bool binary)
    {
        var seeds=RltWireSeeds();var raw=DxfR12Codec.Create(seeds);
        for(int stage=0;stage<3;stage++)
        {
            bool format=stage==1?!binary:binary;
            byte[] bytes=R12Bytes(raw,format);
            File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"r12-linetypes-{(binary?"binary":"text")}-{new[]{"source","output","resave"}[stage]}.dxf"),bytes);
            using var input=new MemoryStream(bytes);raw=DxfRawDocument.Load(input);
            var copy=DxfR12Codec.ReadEntities(raw);Equal(3,copy.Count,"Wire pattern carriers");
            RltSame(seeds[0].Layer.Linetype,copy[0].Layer.Linetype);RltSame(seeds[1].Linetype,copy[1].Linetype);
            RltSame(((PolyfaceMesh)seeds[2]).Faces[0].Layer.Linetype,((PolyfaceMesh)copy[2]).Faces[0].Layer.Linetype);
            Equal("102",copy[2].Handle,"Following polyface identity");
            raw=DxfR12Codec.Create(copy);
        }
    }

    private static DxfRawRecord RltRecord(DxfRawDocument raw,string name) => raw.Sections.Single(s=>s.Name=="TABLES").Records
        .Single(r=>r.Name=="LTYPE" && r.Tags.Any(t=>t.Code==2 && Equals(t.Value,name)));

    private static void RltReadRefusal(bool binary,int fault)
    {
        var raw=R12Reload(DxfR12Codec.Create(RltWireSeeds()),binary);var record=RltRecord(raw,"DASH");
        var tags=record.Tags.ToList();
        switch(fault)
        {
            case 0:tags[tags.FindIndex(t=>t.Code==73)]=new DxfTag(73,(short)-1);break;
            case 1:tags[tags.FindIndex(t=>t.Code==40)]=new DxfTag(40,3.0);break;
            case 2:tags[tags.FindIndex(t=>t.Code==40)]=new DxfTag(40,-2.0);break;
            case 3:tags[tags.FindIndex(t=>t.Code==72)]=new DxfTag(72,(short)66);break;
            case 4:tags[tags.FindIndex(t=>t.Code==70)]=new DxfTag(70,(short)16);break;
            case 5:tags.Add(new DxfTag(73,(short)2));break;
            case 6:tags.Add(new DxfTag(100,"AcDbLinetypeTableRecord"));break;
            case 7:tags.Add(new DxfTag(74,(short)2));break;
            case 8:tags.RemoveAt(tags.FindLastIndex(t=>t.Code==49));break;
            case 9:tags.Add(new DxfTag(1001,"PRIVATE"));tags.Add(new DxfTag(1000,"payload"));break;
            case 10:tags[tags.FindIndex(t=>t.Code==2)]=new DxfTag(2,"CONTINUOUS");break;
            case 11:raw=raw.WithoutRecord(record);break;
            case 12:
                record=raw.Sections.Single(s=>s.Name=="TABLES").Records.Single(r=>r.Name=="LAYER"&&r.Tags.Any(t=>t.Code==2&&Equals(t.Value,"STROKES")));
                tags=record.Tags.Select(t=>t.Code==6?new DxfTag(6,"BYLAYER"):t).ToList();break;
            case 13:
                var all=raw.Tags.Take(record.EndTagIndex).Concat(record.Tags).Concat(raw.Tags.Skip(record.EndTagIndex));
                raw=DxfRawDocument.Create(all);break;
        }
        if(fault!=11 && fault!=13)raw=raw.WithRecord(record,tags);
        byte[] before=R12Bytes(raw,binary);R12Refuses(()=>DxfR12Codec.ReadEntities(raw));
        Check(before.SequenceEqual(R12Bytes(raw,binary)),"Refused pattern decode changed source");
    }

    private static void RltOrder(bool binary)
    {
        var raw=DxfR12Codec.Create(RltWireSeeds());var records=raw.Sections.Single(s=>s.Name=="TABLES").Records;
        int start=records[0].StartTagIndex;
        int end=records.First(r=>r.Name=="ENDTAB").EndTagIndex;
        int destination=records.Last(r=>r.Name=="ENDTAB").EndTagIndex;
        var reordered=raw.Tags.Take(start).Concat(raw.Tags.Skip(end).Take(destination-end))
            .Concat(raw.Tags.Skip(start).Take(end-start)).Concat(raw.Tags.Skip(destination));
        raw=DxfRawDocument.Create(reordered);
        var line=raw.Sections.Single(s=>s.Name=="ENTITIES").Records.First(r=>r.Name=="LINE"&&r.Tags.Any(t=>t.Code==6&&Equals(t.Value,"CENTER")));
        raw=raw.WithRecord(line,line.Tags.Select(t=>t.Code==6?new DxfTag(6,"cEnTeR"):t));
        var copy=DxfR12Codec.ReadEntities(R12Reload(raw,binary));
        Equal("CENTER",copy[1].Linetype.Name,"Case-insensitive forward linetype resolution");
        Equal("DASH",copy[0].Layer.Linetype.Name,"LAYER-before-LTYPE resolution");
    }

    private static void RltRoundedTotal(bool binary)
    {
        var type=RltType("ROUND",new[]{.1,-.2});
        var raw=DxfR12Codec.Create(new EntityObject[]{new Line(Vector3.Zero,Vector3.UnitX){Linetype=type}});
        var record=RltRecord(raw,"ROUND");raw=raw.WithRecord(record,record.Tags.Select(t=>t.Code==40?new DxfTag(40,.3):t));
        var item=DxfR12Codec.ReadEntities(R12Reload(raw,binary)).Single();RltSame(type,item.Linetype);
        record=RltRecord(DxfR12Codec.Create(new[]{item}),"ROUND");
        Equal(type.Length(),(double)record.Tags.Single(t=>t.Code==40).Value,"Redundant length was not recomputed");
    }

    private static void RltReferences(bool binary)
    {
        var a=RltType("Shared",new[]{.5,-.25});var b=RltType("sHaReD",new[]{.5,-.25});
        var layer=new Layer("WITH_PATTERN"){Linetype=a};
        var first=new Line(Vector3.Zero,Vector3.UnitX){Layer=layer,Linetype=b};
        var second=new Circle(Vector3.Zero,2){Layer=layer,Linetype=a};
        var raw=R12Reload(DxfR12Codec.Create(new EntityObject[]{first,second}),binary);
        Equal(1,raw.Sections.SelectMany(s=>s.Records).Count(r=>r.Name=="LTYPE"&&r.Tags.Any(t=>t.Code==2&&Equals(t.Value,"Shared"))),"Shared pattern duplicated");
        byte[] before=R12Bytes(raw,binary);var copy=DxfR12Codec.ReadEntities(raw);
        Check(ReferenceEquals(copy[0].Linetype,copy[1].Linetype)&&ReferenceEquals(copy[0].Layer.Linetype,copy[0].Linetype),"Pattern aliases not preserved");
        copy[0].Linetype.Segments[0].Length=3;
        Equal(3.0,copy[1].Linetype.Segments[0].Length,"Shared pattern edit not visible");
        Check(before.SequenceEqual(R12Bytes(raw,binary)) && a.Segments[0].Length==.5,"Decoded edit reached source");
    }

    private static void RltWriteRefusal(int fault)
    {
        var type=RltType("BAD",new[]{.5,-.25});var line=new Line(Vector3.Zero,Vector3.UnitX){Linetype=type};
        var entities=new List<EntityObject>{new Line(Vector3.Zero,Vector3.UnitY),line};
        switch(fault)
        {
            case 0:type.Segments.Add(new LinetypeTextSegment("X",TextStyle.Default,1));break;
            case 1:type.Description="bad\nline";break;
            case 2:type.Segments[1].Length=double.NaN;break;
            case 3:type.Segments[1].Length=double.PositiveInfinity;break;
            case 4:type.Segments[0].Length=double.MaxValue;type.Segments[1].Length=-double.MaxValue;break;
            case 5:type.XData.Add(new XData(new ApplicationRegistry("APP")));break;
            case 6:entities.Add(new Line(Vector3.Zero,Vector3.UnitY){Linetype=RltType("bad",new[]{1.0,-.25})});break;
            case 7:entities.Add(new Line(Vector3.Zero,Vector3.UnitY){Linetype=RltType("BAD",new[]{.5,-.25},"Other description")});break;
            case 8:line.Layer.Linetype=Linetype.ByBlock;break;
            case 9:line.Linetype=new Linetype("CONTINUOUS",new[]{new LinetypeSimpleSegment(1)});break;
        }
        using var output=new MemoryStream(new byte[]{11,22,33},true);
        R12Refuses(()=>DxfR12Codec.Save(output,entities));
        Check(output.Position==0&&output.ToArray().SequenceEqual(new byte[]{11,22,33}),"Pattern failure partially wrote destination");
    }

    private static void RltBudgets()
    {
        var seeds=RltWireSeeds();int count=DxfR12Codec.Create(seeds).Tags.Count;
        DxfR12Codec.Create(seeds,false,new DxfRawOptions(1000000,count,1024));
        Throws<InvalidDataException>(()=>DxfR12Codec.Create(seeds,false,new DxfRawOptions(1000000,count-1,1024)));
        using var stream=new MemoryStream();
        Throws<InvalidDataException>(()=>DxfR12Codec.Save(stream,seeds,false,new DxfRawOptions(100,1000,1024)));
        Equal(0L,stream.Length,"Pattern byte limit wrote output");
    }
    private static void RltCulture()
    {
        var before=CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("tr-TR");
            RltOrder(false);
            var type=RltType("UNICODE",new[]{1.0,-.5},"漢字");
            using var output=new MemoryStream();
            R12Refuses(()=>DxfR12Codec.Save(output,new EntityObject[]{new Line(Vector3.Zero,Vector3.UnitX){Linetype=type}}));
            Equal(0L,output.Length,"Unencodable description wrote output");
        }
        finally{CultureInfo.CurrentCulture=before;}
    }
    private static void RltSource()
    {
        var doc=new DxfDocument();var line=new Line(Vector3.Zero,Vector3.UnitX){Linetype=RltType("SOURCE",new[]{1.0,-.5})};doc.Entities.Add(line);
        string handle=line.Handle,typeHandle=line.Linetype.Handle;var owner=line.Linetype.Owner;
        DxfR12Codec.Create(new[]{line});
        Check(line.Handle==handle&&line.Linetype.Handle==typeHandle&&ReferenceEquals(owner,line.Linetype.Owner),"Authoring reassigned source resource identity");
        IEnumerable<EntityObject> Broken(){yield return line;throw new ApplicationException("late input");}
        using var output=new MemoryStream();Throws<ApplicationException>(()=>DxfR12Codec.Save(output,Broken()));Equal(0L,output.Length,"Late iterator failure wrote output");
    }
    private static void RltMaximum()
    {
        var type=RltType("MAXIMUM",Enumerable.Range(0,short.MaxValue).Select(i=>i%2==0?1.0:-1.0).ToArray());
        var line=new Line(Vector3.Zero,Vector3.UnitX){Linetype=type};
        RltSame(type,DxfR12Codec.ReadEntities(R12Reload(DxfR12Codec.Create(new[]{line}),true)).Single().Linetype);
        type.Segments.Add(new LinetypeSimpleSegment(1));Throws<NotSupportedException>(()=>DxfR12Codec.Create(new[]{line}));
    }
}
