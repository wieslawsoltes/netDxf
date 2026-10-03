// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterR12PolylineTests()
    {
        foreach (bool binary in new[] { false, true })
        foreach (bool spatial in new[] { false, true })
        foreach (bool closed in new[] { false, true })
        for (int normal = 0; normal < 4; normal++)
        {
            int n = normal;
            Run($"r12-polyline/roundtrip/{binary}/{spatial}/{closed}/{normal}",
                () => RpRoundtrip(binary, spatial, closed, n));
        }
        foreach (bool binary in new[] { false, true })
        {
            foreach (int count in new[] { 0, 1, 2, 1000 })
            {
                Run($"r12-polyline/cardinality/{binary}/{count}", () => RpCardinality(binary, count));
            }
            for (int fault = 0; fault < 20; fault++)
            {
                int f = fault; Run($"r12-polyline/malformed/{binary}/{f}", () => RpMalformed(binary, f));
            }
            Run($"r12-polyline/default-widths/{binary}", () => RpDefaultWidths(binary));
            Run($"r12-polyline/constant-width/{binary}", () => RpConstantWidths(binary));
            Run($"r12-polyline/optional-fields/{binary}", () => RpOptional(binary));
            for (int normal = 0; normal < 3; normal++)
            {
                int n = normal; Run($"r12-polyline/wire/{binary}/{n}", () => RpWire(binary, n));
            }
        }
        for (int fault = 0; fault < 9; fault++)
        {
            int f = fault; Run("r12-polyline/source-refusal/" + f, () => RpRefusal(f));
        }
        Run("r12-polyline/budget-late-failure", RpBudget);
        Run("r12-polyline/source-identities", RpSource);
        Run("r12-polyline/edit-reauthor", RpEdit);
    }
    private static Polyline2D RpPlanar(int normal = 0, bool closed = true)
    {
        var p = new Polyline2D(new[] {
            new Polyline2DVertex(new Vector2(-0.0, 0), 1) { StartWidth = .5, EndWidth = 1 },
            new Polyline2DVertex(new Vector2(4, 0), -.5) { EndWidth = 0 },
            new Polyline2DVertex(new Vector2(4, 3), 0) { StartWidth = .25 },
            new Polyline2DVertex(new Vector2(0, 3), .25)
        }, closed) { Elevation = 5, Thickness = -2, Normal = R12Normal(normal), LinetypeGeneration = true };
        p.Layer = new Layer("PATHS") { Color = new AciColor(2) }; p.Color = new AciColor(3);
        return p;
    }
    private static Polyline3D RpSpatial(int normal = 0, bool closed = false)
    {
        var p = new Polyline3D(new[] { new Vector3(1,2,3), new Vector3(4,5,6), new Vector3(-7,8,9) }, closed)
        { Normal = R12Normal(normal), LinetypeGeneration = true, Layer = new Layer("PATHS") { Color = new AciColor(2) }, Color = new AciColor(4) };
        return p;
    }
    private static void RpCompare(EntityObject a, EntityObject b)
    {
        Equal(a.GetType(), b.GetType(), "Polyline type"); Equal(a.Color.Index, b.Color.Index, "Polyline color");
        Equal(a.Layer.Name, b.Layer.Name, "Polyline layer"); R12Vector(a.Normal, b.Normal);
        if (a is Polyline2D p)
        {
            var q = (Polyline2D)b; Equal(p.Vertexes.Count, q.Vertexes.Count, "2D count");
            Equal(p.IsClosed, q.IsClosed, "2D closure"); Equal(p.LinetypeGeneration, q.LinetypeGeneration, "2D pattern");
            Near(p.Elevation, q.Elevation, "Elevation"); Near(p.Thickness, q.Thickness, "Thickness");
            for (int i = 0; i < p.Vertexes.Count; i++)
            {
                Equal(p.Vertexes[i].Position, q.Vertexes[i].Position, "2D OCS position");
                Equal(BitConverter.DoubleToInt64Bits(p.Vertexes[i].Position.X), BitConverter.DoubleToInt64Bits(q.Vertexes[i].Position.X), "2D signed-zero X");
                Near(p.Vertexes[i].Bulge, q.Vertexes[i].Bulge, "Bulge");
                Near(p.GetEffectiveStartWidth(i), q.GetEffectiveStartWidth(i), "Effective start width");
                Near(p.GetEffectiveEndWidth(i), q.GetEffectiveEndWidth(i), "Effective end width");
            }
        }
        else
        {
            var p3 = (Polyline3D)a; var q = (Polyline3D)b;
            Check(p3.Vertexes.SequenceEqual(q.Vertexes), "3D WCS geometry");
            Equal(p3.IsClosed, q.IsClosed, "3D closure"); Equal(p3.LinetypeGeneration, q.LinetypeGeneration, "3D pattern");
        }
    }
    private static void RpRoundtrip(bool binary, bool spatial, bool closed, int normal)
    {
        EntityObject source = spatial ? RpSpatial(normal, closed) : RpPlanar(normal, closed);
        DxfRawDocument raw = DxfR12Codec.Create(new[] { source }, binary);
        for (int i = 0; i < 3; i++)
        {
            raw = R12Reload(raw, i % 2 == 0 ? binary : !binary);
            var typed = DxfR12Codec.ReadEntities(raw).Single(); RpCompare(source, typed);
            RpCompare(source, (EntityObject)typed.Clone());
            Equal("100", typed.Handle, "Parent identity");
            if (typed is Polyline2D p)
            {
                Check(p.EndSequenceRecord == null && p.VertexRecords.Count == 0, "Selection silently claimed child identity retention");
                for (int v = 0; v < p.Vertexes.Count; v++)
                {
                    var original = ((Polyline2D)source).Vertexes[v];
                    Equal(original.StartWidthOverride, p.Vertexes[v].StartWidthOverride, "Optional start width");
                    Equal(original.EndWidthOverride, p.Vertexes[v].EndWidthOverride, "Optional end width");
                }
            }
            raw = DxfR12Codec.Create(new[] { typed }, !binary);
        }
    }
    private static void RpCardinality(bool binary, int count)
    {
        EntityObject[] sources = { new Polyline2D(Enumerable.Range(0,count).Select(i=>new Vector2(i,-i))),
            new Polyline3D(Enumerable.Range(0,count).Select(i=>new Vector3(i,-i,i*2))) };
        var raw = R12Reload(DxfR12Codec.Create(sources), binary); var result = DxfR12Codec.ReadEntities(raw);
        Equal(2, result.Count, "Multiple chain inventory"); for(int i=0;i<2;i++) RpCompare(sources[i],result[i]);
    }
    private static void RpWire(bool binary, int normal)
    {
        var following = new Line(new Vector3(10,20,30), new Vector3(40,50,60)) { Layer = new Layer("PATHS") { Color = new AciColor(2) } };
        EntityObject[] seeds = { RpPlanar(normal), RpSpatial(normal), following };
        DxfRawDocument raw = DxfR12Codec.Create(seeds);
        for (int stage = 0; stage < 3; stage++)
        {
            bool format = stage == 1 ? !binary : binary;
            byte[] bytes = R12Bytes(raw,format);
            string name=$"r12-polylines-{normal}-{(binary ? "binary" : "text")}-{new[]{"source","output","resave"}[stage]}.dxf";
            File.WriteAllBytes(Path.Combine(ArtifactDirectory,name),bytes);
            using var input=new MemoryStream(bytes); raw=DxfRawDocument.Load(input);
            var typed=DxfR12Codec.ReadEntities(raw); Equal(3,typed.Count,"Wire parent inventory");
            RpCompare(seeds[0],typed[0]);RpCompare(seeds[1],typed[1]);R12Compare(seeds[2],typed[2]);
            var handles=raw.Sections.Single(s=>s.Name=="ENTITIES").Records.Select(r=>(string)r.Tags.Single(t=>t.Code==5).Value).ToArray();
            Equal(12,handles.Length,"Wire identity inventory"); Equal(12,handles.Distinct().Count(),"Duplicate wire identity");
            Equal("10B",typed[2].Handle,"Following LINE identity"); raw=DxfR12Codec.Create(typed);
        }
    }
    private static void RpMalformed(bool binary, int fault)
    {
        DxfRawDocument raw=R12Reload(DxfR12Codec.Create(new EntityObject[]{RpPlanar(),RpSpatial()}),binary);
        var records=raw.Sections.Single(s=>s.Name=="ENTITIES").Records;
        var head=records[0];var child=records[1];var end=records[5];
        if(fault==0) raw=raw.WithoutRecord(end);
        else if(fault==1) raw=raw.WithRecord(child,child.Tags.Select((t,i)=>i==0?new DxfTag(0,"LINE"):t));
        else if(fault==2) raw=raw.WithRecord(head,head.Tags.Select(t=>t.Code==70?new DxfTag(70,(short)16):t));
        else if(fault==3) raw=raw.WithRecord(child,child.Tags.Select(t=>t.Code==70?new DxfTag(70,(short)32):t));
        else if(fault==4) raw=raw.WithRecord(child,child.Tags.Select(t=>t.Code==30?new DxfTag(30,1.0):t));
        else if(fault==5) raw=raw.WithRecord(child,child.Tags.Select(t=>t.Code==40?new DxfTag(40,-1.0):t));
        else if(fault==6) raw=raw.WithRecord(child,child.Tags.Select(t=>t.Code==5?new DxfTag(5,"100"):t));
        else if(fault==7) raw=raw.WithRecord(end,end.Tags.Select(t=>t.Code==5?new DxfTag(5,"101"):t));
        else if(fault==8) raw=raw.WithRecord(child,child.Tags.Select(t=>t.Code==8?new DxfTag(8,"OTHER"):t));
        else if(fault==9) raw=raw.WithRecord(child,child.Tags.Concat(new[]{new DxfTag(62,(short)1)}));
        else if(fault==10) raw=raw.WithRecord(child,child.Tags.Concat(new[]{new DxfTag(1001,"APP"),new DxfTag(1000,"private")}));
        else if(fault==11) raw=raw.WithRecord(end,end.Tags.Concat(new[]{new DxfTag(39,1.0)}));
        else if(fault==12) raw=raw.WithRecord(head,head.Tags.Select(t=>t.Code==10?new DxfTag(10,1.0):t));
        else if(fault==13) raw=raw.WithRecord(head,head.Tags.Concat(new[]{new DxfTag(75,(short)5)}));
        else if(fault==14) raw=raw.WithRecord(child,child.Tags.Where(t=>t.Code!=20));
        else if(fault==15) raw=raw.WithRecord(child,child.Tags.Concat(new[]{new DxfTag(42,2.0)}));
        else if(fault==16) raw=raw.WithRecord(records[7],records[7].Tags.Concat(new[]{new DxfTag(42,1.0)}));
        else if(fault==17) raw=raw.WithRecord(records[7],records[7].Tags.Concat(new[]{new DxfTag(40,1.0)}));
        else if(fault==18) raw=raw.WithRecord(records[6],records[6].Tags.Concat(new[]{new DxfTag(39,1.0)}));
        else raw=raw.WithRecord(records[6],records[6].Tags.Select(t=>t.Code==5?new DxfTag(5,"101"):t));
        byte[] before=R12Bytes(raw,binary); R12Refuses(()=>DxfR12Codec.ReadEntities(raw));
        Check(before.SequenceEqual(R12Bytes(raw,binary)),"Rejected selection altered input");
    }
    private static void RpDefaultWidths(bool binary)
    {
        var p=RpPlanar(); var raw=DxfR12Codec.Create(new EntityObject[]{p}); var head=raw.Sections.Single(s=>s.Name=="ENTITIES").Records[0];
        raw=raw.WithRecord(head,head.Tags.Concat(new[]{new DxfTag(40,2.0),new DxfTag(41,3.0)}));
        var typed=(Polyline2D)DxfR12Codec.ReadEntities(R12Reload(raw,binary)).Single();
        double[] starts={.5,2,.25,2}, ends={1,0,3,3};
        for(int i=0;i<4;i++){Equal(starts[i],typed.GetEffectiveStartWidth(i),"Default start materialization");Equal(ends[i],typed.GetEffectiveEndWidth(i),"Default end materialization");}
        RpCompare(typed,(EntityObject)typed.Clone()); RpCompare(typed,DxfR12Codec.ReadEntities(R12Reload(DxfR12Codec.Create(new[]{typed}),!binary)).Single());
    }
    private static void RpConstantWidths(bool binary)
    {
        foreach(double width in new[]{0.0,2.5})
        {
            var p=RpPlanar();p.ConstantWidth=width;var first=p.Vertexes[0];var start=first.StartWidthOverride;
            var typed=(Polyline2D)DxfR12Codec.ReadEntities(R12Reload(DxfR12Codec.Create(new[]{p}),binary)).Single();
            RpCompare(p,typed);Check(p.ConstantWidth==width && ReferenceEquals(first,p.Vertexes[0]) && first.StartWidthOverride==start,"Width conversion mutated source");
        }
    }
    private static void RpOptional(bool binary)
    {
        var raw=DxfR12Codec.Create(new EntityObject[]{RpPlanar()});
        raw=raw.WithTags(raw.Tags.Where(t=>t.Code!=5 && t.Code!=66 && !(t.Code==9 && Equals(t.Value,"$HANDSEED"))));
        // Optional entity/child IDs and entities-follow marker do not create dependencies.
        var typed=(Polyline2D)DxfR12Codec.ReadEntities(R12Reload(raw,binary)).Single();
        Equal(4,typed.Vertexes.Count,"Optional fields");Check(typed.Handle==null,"Absent parent ID manufactured");
        var head=raw.Sections.Single(s=>s.Name=="ENTITIES").Records[0];
        raw=raw.WithRecord(head,head.Tags.Concat(new[]{new DxfTag(66,(short)0)}));
        Equal(1,DxfR12Codec.ReadEntities(raw).Count,"Obsolete follow flag controlled sequence parser");
    }
    private static void RpRefusal(int fault)
    {
        EntityObject p=RpPlanar();
        switch(fault)
        {
            case 0:((Polyline2D)p).Vertexes.Add(null!);break;
            case 1:((Polyline2D)p).Vertexes[3].Position=new Vector2(double.NaN,0);break;
            case 2:((Polyline2D)p).Vertexes[3].Bulge=double.PositiveInfinity;break;
            case 3:((Polyline2D)p).Vertexes[3].VertexIdentifier=7;break;
            case 4:((Polyline2D)p).SmoothType=PolylineSmoothType.Quadratic;break;
            case 5:((Polyline2D)p).Elevation=double.NaN;break;
            case 6:p=RpSpatial();((Polyline3D)p).SmoothType=PolylineSmoothType.Cubic;break;
            case 7:p=RpSpatial();((Polyline3D)p).Vertexes.Add(new Vector3(1,double.PositiveInfinity,3));break;
            case 8:p.ProxyGraphics=new byte[]{1,2};break;
        }
        using var output=new MemoryStream();output.WriteByte(9);output.Position=0;
        R12Refuses(()=>DxfR12Codec.Save(output,new[]{p}));Check(output.Position==0 && output.ToArray().SequenceEqual(new byte[]{9}),"Late polyline refusal published output");
    }
    private static void RpBudget()
    {
        var p=new Polyline3D(Enumerable.Range(0,1000).Select(i=>new Vector3(i,i,i)));
        using var output=new MemoryStream();Throws<InvalidDataException>(()=>DxfR12Codec.Save(output,new[]{p},false,new DxfRawOptions(100000,100,100)));
        Equal(0L,output.Length,"Sequence budget failure wrote output");
        var raw=DxfR12Codec.Create(new EntityObject[]{RpPlanar()});int count=raw.Tags.Count;
        Equal(count,DxfR12Codec.Create(new EntityObject[]{RpPlanar()},false,new DxfRawOptions(100000,count,100)).Tags.Count,"Exact tag limit rejected");
        Throws<InvalidDataException>(()=>DxfR12Codec.Create(new EntityObject[]{RpPlanar()},false,new DxfRawOptions(100000,count-1,100)));
    }
    private static void RpSource()
    {
        var doc=new DxfDocument();var p=RpPlanar();doc.Entities.Add(p);var vertices=p.Vertexes.ToArray();var id=p.Handle;var owner=p.Owner;
        DxfR12Codec.Create(new EntityObject[]{p});Check(p.Handle==id && ReferenceEquals(p.Owner,owner) && vertices.SequenceEqual(p.Vertexes),"Export altered source registration");
    }
    private static void RpEdit()
    {
        var p=(Polyline2D)DxfR12Codec.ReadEntities(DxfR12Codec.Create(new EntityObject[]{RpPlanar()})).Single();
        p.SetVertex(1,new Vector2(6,1));p.SetVertexBulge(1,-1);p.SetVertexWidths(1,2,3);p.Reverse();
        RpCompare(p,DxfR12Codec.ReadEntities(R12Reload(DxfR12Codec.Create(new[]{p}),true)).Single());
    }
}
