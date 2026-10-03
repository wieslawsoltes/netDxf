// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterR12CodecTests()
    {
        RegisterR12PolylineTests();
        RegisterR12TextTests();
        RegisterR12MeshTests();
        RegisterR12LinetypeTests();
        foreach (bool binary in new[] { false, true })
        {
            bool b = binary;
            Run("r12-codec/empty/" + b, () =>
            {
                var raw = DxfR12Codec.Create(Array.Empty<EntityObject>(), b);
                Equal(DxfVersion.AutoCad12, raw.Version, "R12 version");
                Equal(0, DxfR12Codec.ReadEntities(R12Reload(raw, b)).Count, "Empty R12 round trip");
            });
            for (int normal = 0; normal < 4; normal++)
            for (int kind = 0; kind < 7; kind++)
            {
                int n = normal, k = kind;
                Run($"r12-codec/typed/{b}/{n}/{k}", () => R12Typed(b, n, k));
            }
            for (int normal = 0; normal < 3; normal++)
            { int n = normal; Run($"r12-codec/wire/{b}/{n}", () => R12Wire(b, n)); }
            for (int fault = 0; fault < 9; fault++)
            { int f = fault; Run($"r12-codec/read-refusal/{b}/{f}", () => R12ReadRefusal(b, f)); }
        }
        for (int normal = 0; normal < 4; normal++)
        { int n = normal; Run("r12-codec/modern-geometry/" + n, () => R12ModernGeometry(n)); }
        for (int fault = 0; fault < 14; fault++)
        { int f = fault; Run("r12-codec/write-refusal/" + f, () => R12WriteRefusal(f)); }
        Run("r12-codec/late-iterator-and-cancel", R12FailureStaging);
        Run("r12-codec/limits", R12Budgets);
        Run("r12-codec/shared-layers", R12LayerSharing);
        Run("r12-codec/defaults-and-triangle", R12Defaults);
        Run("r12-codec/selection-not-downgrade", R12Selection);
    }

    private static Vector3 R12Normal(int normal) => normal == 0 ? Vector3.UnitZ : normal == 1 ? Vector3.UnitX
        : normal == 2 ? new Vector3(0, 0, -1) : new Vector3(1, 2, 3);
    private static EntityObject[] R12Seeds(int normal)
    {
        EntityObject[] values = {
            new Line(new Vector3(-0.0, 2, 3), new Vector3(4, 5, 6)) { Thickness = -2 },
            new Point(new Vector3(7, 8, 9)) { Thickness = 1.5, Rotation = 30 },
            new Circle(new Vector3(10, 20, 30), 2) { Thickness = -1 },
            new Arc(new Vector3(11, 21, 31), 3, 15, 275) { Thickness = 2 },
            new Face3D(new Vector3(0,0,0), new Vector3(2,0,1), new Vector3(2,3,2), new Vector3(0,3,1)) { EdgeFlags = (Face3DEdgeFlags)5 },
            new Solid(new Vector2(0,0), new Vector2(2,0), new Vector2(0,3), new Vector2(2,3)) { Elevation = 4, Thickness = .5 },
            new Trace(new Vector2(0,0), new Vector2(2,0), new Vector2(0,3), new Vector2(2,3)) { Elevation = -2, Thickness = -.5 }
        };
        var layer = new Layer("SHAPES") { Color = new AciColor(2), IsFrozen = true, IsLocked = true };
        for (int i = 0; i < values.Length; i++)
        {
            values[i].Layer = layer; values[i].Color = new AciColor((short)(i + 1));
            values[i].Linetype = i % 3 == 0 ? Linetype.ByLayer : i % 3 == 1 ? Linetype.ByBlock : Linetype.Continuous;
            if (i != 4) values[i].Normal = R12Normal(normal);
        }
        return values;
    }
    private static byte[] R12Bytes(DxfRawDocument raw, bool binary)
    { using var stream = new MemoryStream(); raw.Save(stream, binary); Check(stream.CanWrite, "R12 save closed stream"); return stream.ToArray(); }
    private static DxfRawDocument R12Reload(DxfRawDocument raw, bool binary)
    { using var stream = new MemoryStream(R12Bytes(raw, binary)); var copy = DxfRawDocument.Load(stream); Check(stream.CanRead, "R12 read closed stream"); return copy; }
    private static void R12Vector(Vector3 a, Vector3 b)
    { Near(a.X, b.X, "R12 X"); Near(a.Y, b.Y, "R12 Y"); Near(a.Z, b.Z, "R12 Z"); }
    private static void R12Compare(EntityObject expected, EntityObject actual)
    {
        Equal(expected.GetType(), actual.GetType(), "Typed R12 entity class");
        Equal(expected.Layer.Name, actual.Layer.Name, "Layer name"); Equal(expected.Layer.Color.Index, actual.Layer.Color.Index, "Layer color");
        Equal(expected.Layer.IsFrozen, actual.Layer.IsFrozen, "Frozen layer"); Equal(expected.Layer.IsLocked, actual.Layer.IsLocked, "Locked layer");
        Equal(expected.Color.Index, actual.Color.Index, "Indexed entity color");
        Check(string.Equals(expected.Linetype.Name, actual.Linetype.Name, StringComparison.OrdinalIgnoreCase), "Linetype changed");
        R12Vector(expected.Normal, actual.Normal);
        switch (expected)
        {
            case Line a:
                var b = (Line)actual; R12Vector(a.StartPoint, b.StartPoint); R12Vector(a.EndPoint, b.EndPoint); Near(a.Thickness,b.Thickness,"Line thickness");
                Equal(BitConverter.DoubleToInt64Bits(a.StartPoint.X), BitConverter.DoubleToInt64Bits(b.StartPoint.X), "LINE signed zero"); break;
            case Point a:
                var p = (Point)actual; R12Vector(a.Position,p.Position); Near(a.Thickness,p.Thickness,"Point thickness"); Near(a.Rotation,p.Rotation,"Point rotation"); break;
            case Circle a:
                var c = (Circle)actual; R12Vector(a.Center,c.Center); Near(a.Radius,c.Radius,"Circle radius"); Near(a.Thickness,c.Thickness,"Circle thickness"); break;
            case Arc a:
                var ar = (Arc)actual; R12Vector(a.Center,ar.Center); Near(a.Radius,ar.Radius,"Arc radius"); Near(a.StartAngle,ar.StartAngle,"Arc start"); Near(a.EndAngle,ar.EndAngle,"Arc end"); Near(a.Thickness,ar.Thickness,"Arc thickness"); break;
            case Face3D a:
                var f = (Face3D)actual; R12Vector(a.FirstVertex,f.FirstVertex); R12Vector(a.SecondVertex,f.SecondVertex); R12Vector(a.ThirdVertex,f.ThirdVertex); R12Vector(a.FourthVertex,f.FourthVertex); Equal(a.EdgeFlags,f.EdgeFlags,"Face flags"); break;
            case Solid a:
                var s = (Solid)actual; Check(a.FirstVertex==s.FirstVertex && a.SecondVertex==s.SecondVertex && a.ThirdVertex==s.ThirdVertex && a.FourthVertex==s.FourthVertex,"Solid corners"); Near(a.Elevation,s.Elevation,"Solid elevation"); Near(a.Thickness,s.Thickness,"Solid thickness"); break;
            case Trace a:
                var t = (Trace)actual; Check(a.FirstVertex==t.FirstVertex && a.SecondVertex==t.SecondVertex && a.ThirdVertex==t.ThirdVertex && a.FourthVertex==t.FourthVertex,"Trace corners"); Near(a.Elevation,t.Elevation,"Trace elevation"); Near(a.Thickness,t.Thickness,"Trace thickness"); break;
        }
    }
    private static void R12Typed(bool binary, int normal, int kind)
    {
        EntityObject entity = R12Seeds(normal)[kind];
        var layer = entity.Layer; var color = entity.Color; string? handle = entity.Handle;
        var raw = DxfR12Codec.Create(new[] { entity }, binary);
        Check(entity.Owner == null && entity.Handle == handle && ReferenceEquals(layer,entity.Layer) && ReferenceEquals(color,entity.Color), "R12 authoring mutated source");
        for (int round = 0; round < 3; round++)
        {
            raw = R12Reload(raw, round % 2 == 0 ? binary : !binary);
            var result = DxfR12Codec.ReadEntities(raw).Single(); R12Compare(entity, result);
            Check(result.Owner == null && result.Handle == "100", "Decoded identity/ownership");
            var snapshot = raw.Tags.ToArray();
            result.Color = new AciColor(7);
            Check(snapshot.SequenceEqual(raw.Tags), "Editing typed projection mutated immutable input");
        }
    }
    private static void R12Wire(bool binary, int normal)
    {
        var seeds = R12Seeds(normal); var raw = DxfR12Codec.Create(seeds, binary);
        string prefix = $"r12-primitives-{normal}-{(binary ? "binary" : "text")}";
        for (int stage = 0; stage < 3; stage++)
        {
            bool format = stage == 1 ? !binary : binary;
            byte[] bytes = R12Bytes(raw, format); File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + new[] { "-source.dxf", "-output.dxf", "-resave.dxf" }[stage]), bytes);
            using var input = new MemoryStream(bytes); raw = DxfRawDocument.Load(input);
            var values = DxfR12Codec.ReadEntities(raw); Equal(seeds.Length,values.Count,"Mixed R12 inventory");
            for (int i = 0; i < values.Count; i++) R12Compare(seeds[i],values[i]);
            raw = DxfR12Codec.Create(values, format);
        }
    }
    private static void R12ModernGeometry(int normal)
    {
        var seeds = R12Seeds(normal); var raw = DxfR12Codec.Create(seeds);
        var modern = new DxfDocument(DxfVersion.AutoCad2000);
        foreach (var entity in seeds) modern.Entities.Add((EntityObject)entity.Clone());
        using var stream = new MemoryStream(); Check(modern.Save(stream),"Modern geometry reference save"); stream.Position = 0;
        var reference = DxfRawDocument.Load(stream);
        var modernRecords = reference.Sections.Single(s=>s.Name=="ENTITIES").Records.ToDictionary(r=>r.Name);
        foreach (var record in raw.Sections.Single(s=>s.Name=="ENTITIES").Records)
        {
            var other = modernRecords[record.Name];
            foreach (var tag in record.Tags.Where(t=>t.Value is double))
            {
                var matches = other.Tags.Where(t=>t.Code==tag.Code).ToArray();
                if (matches.Length == 0 && (double)tag.Value == 0) continue;
                Equal(1,matches.Length,"Modern geometry field inventory " + record.Name + "/" + tag.Code);
                Near((double)matches[0].Value,(double)tag.Value,"Modern/R12 geometry convention " + record.Name + "/" + tag.Code);
            }
        }
    }
    private static void R12Refuses(Action action)
    {
        bool refused = false;
        try { action(); } catch (Exception error) when (error is ArgumentException || error is FormatException || error is NotSupportedException || error is InvalidOperationException || error is InvalidDataException) { refused = true; }
        Check(refused,"Unsupported or malformed R12 input accepted");
    }
    private static void R12ReadRefusal(bool binary, int fault)
    {
        var raw = R12Reload(DxfR12Codec.Create(new EntityObject[] {new Circle(Vector3.Zero,2)}), binary);
        var record = raw.Sections.Single(s=>s.Name=="ENTITIES").Records.Single();
        var tags = record.Tags.ToList();
        switch (fault)
        {
            case 0: tags.Add(new DxfTag(10, 10.0)); break;
            case 1: tags.Add(new DxfTag(100, "AcDbCircle")); break;
            case 2: tags[tags.FindIndex(t=>t.Code==40)] = new DxfTag(40, 0.0); break;
            case 3: tags.Add(new DxfTag(1001,"APP")); tags.Add(new DxfTag(1000,"private")); break;
            case 4: tags.Add(new DxfTag(67,(short)1)); break;
            case 5: for(int i=0;i<tags.Count;i++) if(tags[i].Code==230) tags[i]=new DxfTag(230,0.0); break;
            case 6: tags.RemoveAll(t=>t.Code==10); break;
            case 7: tags[0] = new DxfTag(0,"ELLIPSE"); break;
            case 8: tags[tags.FindIndex(t=>t.Code==6)] = new DxfTag(6,"MISSING_DASH"); break;
        }
        var edited = raw.WithRecord(record,tags); byte[] before = R12Bytes(raw,binary);
        R12Refuses(()=>DxfR12Codec.ReadEntities(edited)); Check(before.SequenceEqual(R12Bytes(raw,binary)),"Rejected decode changed source");
    }
    private static void R12WriteRefusal(int fault)
    {
        EntityObject entity = new Line(Vector3.Zero,Vector3.UnitX);
        switch(fault)
        {
            case 0: entity=new Ellipse(Vector3.Zero,4,2); break;
            case 1: entity.Color=new AciColor(20,30,40); break;
            case 2: entity.ColorName="Book$Color"; break;
            case 3: entity.ShadowMode=EntityShadowMode.Cast; break;
            case 4: entity.ProxyGraphics=Array.Empty<byte>(); break;
            case 5: entity.LinetypeScale=2; break;
            case 6: entity.Lineweight=Lineweight.W30; break;
            case 7: entity.Transparency=new Transparency(25); break;
            case 8: entity.IsVisible=false; break;
            case 9: entity.Layer.Plot=false; break;
            case 10: entity.Layer.Description="Do not silently lose"; break;
            case 11: entity.Layer=new Layer(new string('A',32)); break;
            // Simple named patterns are now supported; complex R13+ content must still reject.
            case 12: entity.Linetype=new Linetype("CUSTOM",new[]{new LinetypeTextSegment("X",TextStyle.Default,1)}); break;
            case 13: entity.XData.Add(new XData(new ApplicationRegistry("APP"))); break;
        }
        using var output = new MemoryStream(); output.WriteByte(123); output.Position=0;
        R12Refuses(()=>DxfR12Codec.Save(output,new[]{entity}));
        Check(output.ToArray().SequenceEqual(new byte[]{123}) && output.Position==0,"Refused R12 output touched destination");
    }
    private static void R12FailureStaging()
    {
        IEnumerable<EntityObject> Broken()
        { yield return new Line(Vector3.Zero,Vector3.UnitX); throw new ApplicationException("late iterator"); }
        using var output = new MemoryStream(); output.WriteByte(17); output.Position=0;
        Throws<ApplicationException>(()=>DxfR12Codec.Save(output,Broken())); Equal(1L,output.Length,"Iterator failure touched output");
        using var cancel = new CancellationTokenSource();
        IEnumerable<EntityObject> Cancelled()
        { yield return new Circle(Vector3.Zero,1); cancel.Cancel(); yield return new Line(Vector3.Zero,Vector3.UnitY); }
        Throws<OperationCanceledException>(()=>DxfR12Codec.Save(output,Cancelled(),true,null,cancel.Token));
        Check(output.Position==0 && output.ToArray()[0]==17,"Cancellation touched output");
    }
    private static void R12Budgets()
    {
        var seeds=R12Seeds(0);
        Throws<InvalidDataException>(()=>DxfR12Codec.Create(seeds,false,new DxfRawOptions(4096,10,1024)));
        using var output=new MemoryStream();
        Throws<InvalidDataException>(()=>DxfR12Codec.Save(output,seeds,false,new DxfRawOptions(100,1000,100)));
        Equal(0L,output.Length,"Byte-budget refusal wrote partial output");
    }
    private static void R12LayerSharing()
    {
        var seeds=R12Seeds(0);var values=DxfR12Codec.ReadEntities(DxfR12Codec.Create(seeds));
        Check(values.All(e=>ReferenceEquals(e.Layer,values[0].Layer)),"Decoded same-name layer was duplicated");
        var a=new Line(Vector3.Zero,Vector3.UnitX){Layer=new Layer("SAME"){Color=new AciColor(1)}};
        var b=new Line(Vector3.Zero,Vector3.UnitY){Layer=new Layer("same"){Color=new AciColor(2)}};
        Throws<InvalidOperationException>(()=>DxfR12Codec.Create(new EntityObject[]{a,b}));
    }
    private static void R12Defaults()
    {
        DxfTag[] tags = {new(0,"SECTION"),new(2,"HEADER"),new(9,"$ACADVER"),new(1,"AC1009"),new(0,"ENDSEC"),
            new(0,"SECTION"),new(2,"ENTITIES"),new(0,"LINE"),new(10,1.0),new(20,2.0),new(11,3.0),new(21,4.0),
            new(0,"3DFACE"),new(10,0.0),new(20,0.0),new(11,1.0),new(21,0.0),new(12,0.0),new(22,1.0),new(0,"ENDSEC"),new(0,"EOF")};
        var values=DxfR12Codec.ReadEntities(DxfRawDocument.Create(tags));var line=(Line)values[0];var face=(Face3D)values[1];
        Equal(0.0,line.StartPoint.Z,"Optional Z default");Equal("0",line.Layer.Name,"Implicit layer");Check(line.Color.IsByLayer,"Optional color default");
        Equal(face.ThirdVertex,face.FourthVertex,"Triangle fourth corner default");
    }
    private static void R12Selection()
    {
        using var stream=new MemoryStream();var modern=new DxfDocument(DxfVersion.AutoCad2018);Check(modern.Save(stream),"Version fixture");stream.Position=0;
        Throws<NotSupportedException>(()=>DxfR12Codec.ReadEntities(DxfRawDocument.Load(stream)));
        var source=new DxfDocument();var line=new Line(Vector3.Zero,Vector3.UnitX);source.Entities.Add(line);
        string handle=line.Handle;var owner=line.Owner;DxfR12Codec.Create(new[]{line});
        Check(line.Handle==handle && ReferenceEquals(owner,line.Owner) && ReferenceEquals(line,source.GetObjectByHandle(handle)),"Selection export changed owning document");
    }
}
