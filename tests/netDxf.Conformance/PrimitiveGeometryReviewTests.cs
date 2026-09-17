using netDxf;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly string[] PrimitiveKinds = { "line", "ray", "xline", "solid", "trace", "face", "mesh", "polyline" };
    private static readonly string[] PrimitiveModes = { "translate", "rotate", "mirror", "scale", "shear", "tilted", "oblique", "flat", "small" };
    private static Matrix3 PrimitiveMatrix(string mode) => mode switch
    {
        "translate" => Matrix3.Identity,
        "rotate" => new(0,-1,0, 1,0,0, 0,0,1),
        "mirror" => new(-1,0,0, 0,1,0, 0,0,1),
        "scale" => new(2,0,0, 0,3,0, 0,0,4),
        "shear" => new(1,.75,0, .2,2,0, 0,0,3),
        "tilted" => new(1,0,0, 0,0,-1, 0,1,0),
        "oblique" => new(1,.3,.2, 0,2,.4, .5,0,1),
        "flat" => new(1,0,0, 0,1,0, 0,0,0),
        "small" => Matrix3.Scale(1e-5),
        _ => throw new ArgumentException(mode)
    };

    private static EntityObject Primitive(string kind, string mode = "scale")
    {
        Vector2 a = new(1,2), b = new(5,2), c = new(1,6), d = new(5,6);
        Vector3 p = new(1,2,3), q = new(5,2,4), r = new(5,6,5), s = new(1,6,4);
        EntityObject entity = kind switch
        {
            "line" => new Line(p, q) { Thickness = -2 },
            "ray" => new Ray(p, new Vector3(2,1,3)),
            "xline" => new XLine(p, new Vector3(2,1,3)),
            "solid" => new Solid(a,b,c,d) { Elevation = 7, Thickness = mode == "oblique" ? 0 : -2 },
            "trace" => new Trace(a,b,c,d) { Elevation = 7, Thickness = mode == "oblique" ? 0 : -2 },
            "face" => new Face3D(p,q,r,s) { EdgeFlags = (Face3DEdgeFlags)5 },
            "mesh" => new Mesh(new[] { p,q,r,s }, new[] { new[] { 0,1,2,3 } }, new[] { new MeshEdge(0,1,.5) }) { SubdivisionLevel = 2, BlendCrease = true },
            "polyline" => new Polyline3D(new[] { p,q,r,s }, true),
            _ => throw new ArgumentException(kind)
        };
        entity.Normal = mode == "tilted" ? new Vector3(1,2,3) : Vector3.UnitZ;
        entity.Color = new AciColor(3); entity.IsVisible = false; entity.ProxyGraphics = new byte[] { 1,2,3 };
        return entity;
    }

    private static Vector3[] PrimitiveRaw(EntityObject entity) => entity switch
    {
        Line e => new[] { e.StartPoint, e.EndPoint },
        Ray e => new[] { e.Origin, e.Direction },
        XLine e => new[] { e.Origin, e.Direction },
        Solid e => new[] { new Vector3(e.FirstVertex.X,e.FirstVertex.Y,e.Elevation), new Vector3(e.SecondVertex.X,e.SecondVertex.Y,e.Elevation), new Vector3(e.ThirdVertex.X,e.ThirdVertex.Y,e.Elevation), new Vector3(e.FourthVertex.X,e.FourthVertex.Y,e.Elevation) },
        Trace e => new[] { new Vector3(e.FirstVertex.X,e.FirstVertex.Y,e.Elevation), new Vector3(e.SecondVertex.X,e.SecondVertex.Y,e.Elevation), new Vector3(e.ThirdVertex.X,e.ThirdVertex.Y,e.Elevation), new Vector3(e.FourthVertex.X,e.FourthVertex.Y,e.Elevation) },
        Face3D e => new[] { e.FirstVertex,e.SecondVertex,e.ThirdVertex,e.FourthVertex },
        Mesh e => e.Vertexes.ToArray(),
        Polyline3D e => e.Vertexes.ToArray(),
        _ => throw new ArgumentException(nameof(entity))
    };
    private static double PrimitiveThickness(EntityObject entity) => entity switch { Line e => e.Thickness, Solid e => e.Thickness, Trace e => e.Thickness, _ => 0 };
    private static long[] PrimitiveBits(EntityObject entity) => PrimitiveRaw(entity).Concat(new[] { entity.Normal, new Vector3(PrimitiveThickness(entity),0,0) })
        .SelectMany(v => new[] { v.X,v.Y,v.Z }).Select(BitConverter.DoubleToInt64Bits).ToArray();

    private static void AssertPrimitiveImage(EntityObject before, EntityObject after, Matrix3 matrix, Vector3 translation)
    {
        var source = PrimitiveRaw(before); var actual = PrimitiveRaw(after);
        Equal(source.Length, actual.Length, "primitive vertex inventory");
        for (int i = 0; i < source.Length; i++)
        {
            if ((before is Ray || before is XLine) && i == 1)
            {
                NearReviewedCircularVector(Vector3.Normalize(matrix * source[i]), actual[i], "unit directed image");
                Near(1, actual[i].Modulus(), "normalized infinite direction");
                continue;
            }
            Vector3 p = before is Solid || before is Trace ? MathHelper.ArbitraryAxis(before.Normal) * source[i] : source[i];
            Vector3 q = after is Solid || after is Trace ? MathHelper.ArbitraryAxis(after.Normal) * actual[i] : actual[i];
            NearReviewedCircularVector(matrix * p + translation, q, "world-space primitive point");
        }
        if (before is Line || before is Solid || before is Trace)
            NearReviewedCircularVector(matrix * (before.Normal * PrimitiveThickness(before)), after.Normal * PrimitiveThickness(after), "signed extrusion vector");
        Equal(before.IsVisible, after.IsVisible, "visibility changed"); Equal(before.Color.Index, after.Color.Index, "color changed");
        if (before is Face3D bf && after is Face3D af) Equal(bf.EdgeFlags, af.EdgeFlags, "hidden edges changed");
        if (before is Mesh bm && after is Mesh am)
        {
            Check(bm.Faces.SelectMany(x => x).SequenceEqual(am.Faces.SelectMany(x => x)), "mesh topology changed");
            Equal(bm.SubdivisionLevel, am.SubdivisionLevel, "subdivision changed"); Equal(bm.BlendCrease, am.BlendCrease, "blend crease changed");
            Equal(bm.Edges[0].Crease, am.Edges[0].Crease, "edge crease changed");
        }
    }

    private static void RegisterPrimitiveGeometryReviewTests()
    {
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false,true })
        foreach (string mode in PrimitiveModes)
            Run($"primitive-review/roundtrip/{version}/{binary}/{mode}", () => PrimitiveRoundTrip(version,binary,mode));
        foreach (string kind in PrimitiveKinds)
        {
            foreach (string fault in new[] { "matrix", "translation", "late-nan", "overflow", "projective", "projective-nan" })
                Run($"primitive-review/rejection/{kind}/{fault}", () => PrimitiveRejected(kind,fault));
            foreach (string mode in new[] { "identity", "clone", "affine4", "small", "large" })
                Run($"primitive-review/boundary/{kind}/{mode}", () => PrimitiveBoundary(kind,mode));
        }
        foreach (string kind in new[] { "ray","xline" })
        {
            Run($"primitive-review/direction/{kind}", () => PrimitiveDirections(kind));
            Run($"primitive-review/collapse/{kind}", () => PrimitiveCollapse(kind));
        }
        foreach (string kind in new[] { "solid","trace" })
        foreach (string mode in new[] { "collapse","oblique-extrusion","late-vertex" })
            Run($"primitive-review/plane-reject/{kind}/{mode}", () => PrimitivePlaneRejected(kind,mode));
        foreach (bool binary in new[] { false,true })
            Run($"primitive-review/retained-polyline/{binary}", () => PrimitiveRetainedPolyline(binary));
    }

    private static void PrimitiveRoundTrip(DxfVersion version, bool binary, string mode)
    {
        var originals = PrimitiveKinds.Where(k => k != "mesh" || version >= DxfVersion.AutoCad2010).Select(k => Primitive(k,mode)).ToArray();
        var doc = new DxfDocument(version); var matrix = PrimitiveMatrix(mode); Vector3 translation = new(11,-7,13);
        foreach (var original in originals)
        {
            var transformed = (EntityObject)original.Clone();
            AssertPrimitiveImage(original, transformed, Matrix3.Identity, Vector3.Zero);
            object? list = transformed is Mesh mesh ? mesh.Vertexes : transformed is Polyline3D poly ? poly.Vertexes : null;
            transformed.TransformBy(matrix, translation); AssertPrimitiveImage(original, transformed, matrix, translation);
            Check(transformed.ProxyGraphics == null && original.ProxyGraphics!.Length == 3, "proxy invalidation or clone isolation");
            if (transformed is Mesh m) Check(ReferenceEquals(list,m.Vertexes), "mesh collection identity replaced");
            if (transformed is Polyline3D p) Check(ReferenceEquals(list,p.Vertexes), "polyline collection identity replaced");
            doc.Entities.Add(transformed);
        }
        using var bytes = new MemoryStream(); Check(doc.Save(bytes,binary), "primitive save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"primitive-review-{version}-{binary}-{mode}.dxf"),bytes.ToArray());
        bytes.Position=0; var loaded = DxfDocument.Load(bytes)!;
        var actual = loaded.Entities.All.ToArray(); Equal(originals.Length,actual.Length,"primitive reload count");
        for (int i=0;i<actual.Length;i++) AssertPrimitiveImage(originals[i],actual[i],matrix,translation);
        using var opposite = new MemoryStream(); Check(loaded.Save(opposite,!binary),"primitive opposite save"); opposite.Position=0;
        actual=DxfDocument.Load(opposite)!.Entities.All.ToArray();
        for (int i=0;i<actual.Length;i++) AssertPrimitiveImage(originals[i],actual[i],matrix,translation);
    }

    private static void SetLastPrimitive(EntityObject entity, Vector3 value)
    {
        switch (entity)
        {
            case Line e: e.EndPoint=value; break;
            case Ray e: e.Origin=value; break;
            case XLine e: e.Origin=value; break;
            case Solid e: e.FourthVertex=new Vector2(value.X,value.Y); break;
            case Trace e: e.FourthVertex=new Vector2(value.X,value.Y); break;
            case Face3D e: e.FourthVertex=value; break;
            case Mesh e: e.Vertexes[e.Vertexes.Count-1]=value; break;
            case Polyline3D e: e.Vertexes[e.Vertexes.Count-1]=value; break;
        }
    }
    private static void PrimitiveRejected(string kind, string fault)
    {
        var entity=Primitive(kind); var matrix=Matrix3.Scale(2); Vector3 translation=new Vector3(1,1,1);
        if (fault=="matrix") matrix=new Matrix3(double.NaN,0,0,0,1,0,0,0,1);
        if (fault=="translation") translation=new Vector3(double.PositiveInfinity,0,0);
        if (fault=="late-nan") SetLastPrimitive(entity,new Vector3(double.NaN,2,3));
        if (fault=="overflow") SetLastPrimitive(entity,new Vector3(double.MaxValue,2,3));
        var bits=PrimitiveBits(entity); byte[] proxy=entity.ProxyGraphics!; bool rejected=false;
        try
        {
            if (fault.StartsWith("projective",StringComparison.Ordinal))
                entity.TransformBy(new Matrix4(1,0,0,0,0,1,0,0,0,0,1,0,fault=="projective"?.5:double.NaN,0,0,1));
            else entity.TransformBy(matrix,translation);
        }
        catch(Exception error) when(error is ArgumentException || error is InvalidOperationException || error is NotSupportedException) { rejected=true; }
        Check(rejected,"invalid primitive transform accepted");
        Check(bits.SequenceEqual(PrimitiveBits(entity)),"rejected transform partially modified primitive");
        Check(proxy.SequenceEqual(entity.ProxyGraphics!),"rejection cleared proxy");
    }

    private static void PrimitiveBoundary(string kind,string mode)
    {
        var entity=Primitive(kind); var before=(EntityObject)entity.Clone();
        if (mode=="identity")
        {
            var bits=PrimitiveBits(entity); entity.TransformBy(Matrix3.Identity,Vector3.Zero); entity.TransformBy(Matrix4.Identity);
            Check(bits.SequenceEqual(PrimitiveBits(entity)) && entity.ProxyGraphics!.Length==3,"identity changed state/proxy"); return;
        }
        if (mode=="clone")
        {
            Check(PrimitiveBits(entity).SequenceEqual(PrimitiveBits(before)),"clone lost elevation/geometry");
            entity.TransformBy(Matrix3.Identity,new Vector3(1,1,1));
            Check(before.ProxyGraphics!.Length==3 && entity.ProxyGraphics==null,"clone proxy isolation"); return;
        }
        if (mode=="affine4")
        {
            entity.TransformBy(new Matrix4(1,.75,0,11,.2,2,0,-7,0,0,3,13,0,0,0,1));
            AssertPrimitiveImage(before,entity,PrimitiveMatrix("shear"),new Vector3(11,-7,13)); return;
        }
        double scale=mode=="small"?1e-150:1e150;
        entity.TransformBy(Matrix3.Scale(scale),Vector3.Zero);
        var source=PrimitiveRaw(before); var after=PrimitiveRaw(entity);
        for(int i=0;i<source.Length;i++)
        {
            double divisor=(entity is Ray || entity is XLine) && i==1?1:scale;
            NearReviewedCircularVector(source[i],new Vector3(after[i].X/divisor,after[i].Y/divisor,after[i].Z/divisor),"scaled primitive representation");
        }
        if(entity is Line || entity is Solid || entity is Trace) Near(PrimitiveThickness(before),PrimitiveThickness(entity)/scale,"scaled thickness");
    }

    private static void PrimitiveDirections(string kind)
    {
        var entity=Primitive(kind);
        void Set(Vector3 v) { if(entity is Ray r) r.Direction=v; else ((XLine)entity).Direction=v; }
        foreach(var v in new[] { Vector3.Zero,new Vector3(double.NaN,0,0),new Vector3(0,double.PositiveInfinity,0) })
        {
            var before=PrimitiveBits(entity); Throws<ArgumentException>(()=>Set(v));
            Check(before.SequenceEqual(PrimitiveBits(entity)),"invalid direction setter mutated source");
            Throws<ArgumentException>(()=> { _=kind=="ray"?(EntityObject)new Ray(Vector3.Zero,v):new XLine(Vector3.Zero,v); });
        }
        foreach(double size in new[] { double.Epsilon,1e-250,1e250,double.MaxValue })
        {
            Set(new Vector3(size,size,size));
            Vector3 actual=PrimitiveRaw(entity)[1]; Near(1,actual.Modulus(),"stable direction magnitude");
            Near(1/Math.Sqrt(3),actual.X,"stable direction component");
        }
    }
    private static void PrimitiveCollapse(string kind)
    {
        var entity=Primitive(kind);
        if(entity is Ray ray) ray.Direction=Vector3.UnitX; else ((XLine)entity).Direction=Vector3.UnitX;
        var before=PrimitiveBits(entity);
        Throws<NotSupportedException>(()=>entity.TransformBy(new Matrix3(0,0,0,0,1,0,0,0,1),new Vector3(1,1,1)));
        Check(before.SequenceEqual(PrimitiveBits(entity)) && entity.ProxyGraphics!.Length==3,"collapsed direction changed origin or proxy");
    }
    private static void PrimitivePlaneRejected(string kind,string mode)
    {
        var entity=Primitive(kind);
        Matrix3 matrix=mode=="collapse"?new(0,0,0,0,1,0,0,0,1):new(1,0,.5,0,1,0,0,0,1);
        if(mode=="late-vertex") { SetLastPrimitive(entity,new Vector3(double.NaN,0,0)); matrix=Matrix3.Identity; }
        var before=PrimitiveBits(entity); bool rejected=false;
        try { entity.TransformBy(matrix,new Vector3(1,1,1)); }
        catch(Exception error) when(error is ArgumentException || error is NotSupportedException) { rejected=true; }
        Check(rejected && before.SequenceEqual(PrimitiveBits(entity)) && entity.ProxyGraphics!.Length==3,"planar rejection lost state");
    }
    private static void PrimitiveRetainedPolyline(bool binary)
    {
        var doc=new DxfDocument(DxfVersion.AutoCad2018); doc.Entities.Add(Primitive("polyline"));
        using var bytes=new MemoryStream(); Check(doc.Save(bytes,binary),"polyline source save"); bytes.Position=0;
        var loaded=DxfDocument.Load(bytes)!; var poly=loaded.Entities.Polylines3D.Single();
        var records=poly.VertexRecords.ToArray(); var end=poly.EndSequenceRecord; var coordinates=poly.Vertexes.ToArray();
        Check(records.Length==coordinates.Length,"retained vertex inventory missing");
        poly.TransformBy(PrimitiveMatrix("oblique"),new Vector3(1,1,1));
        Check(records.SequenceEqual(poly.VertexRecords) && ReferenceEquals(end,poly.EndSequenceRecord),"transform replaced retained identities");
        using var output=new MemoryStream(); Check(loaded.Save(output,!binary),"retained transformed save");output.Position=0;
        var again=DxfDocument.Load(output)!.Entities.Polylines3D.Single();
        Check(records.Select(r=>r.Handle).SequenceEqual(again.VertexRecords.Select(r=>r.Handle)),"retained vertex handles changed");
        for(int i=0;i<coordinates.Length;i++) NearReviewedCircularVector(PrimitiveMatrix("oblique")*coordinates[i]+new Vector3(1,1,1),again.Vertexes[i],"retained coordinates");
    }
}
