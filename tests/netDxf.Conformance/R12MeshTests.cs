// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterR12MeshTests()
    {
        foreach (bool binary in new[] { false, true })
        {
            bool b = binary;
            for (int normal = 0; normal < 4; normal++)
            {
                int n = normal;
                for (int closed = 0; closed < 4; closed++)
                { int c = closed; Run($"r12-mesh/grid/{b}/{n}/{c}", () => RmGridRoundtrip(b,n,c)); }
                for (int style = 0; style < 3; style++)
                { int s = style; Run($"r12-mesh/polyface/{b}/{n}/{s}", () => RmFaceRoundtrip(b,n,s)); }
            }
            for (int normal = 0; normal < 3; normal++)
            { int n = normal; Run($"r12-mesh/wire/{b}/{n}", () => RmWire(b,n)); }
            for (int fault = 0; fault < 27; fault++)
            { int f = fault; Run($"r12-mesh/reject/{b}/{f}", () => RmMalformed(b,f)); }
            for (int variant = 0; variant < 7; variant++)
            { int v = variant; Run($"r12-mesh/advisory/{b}/{v}", () => RmAdvisory(b,v)); }
            Run("r12-mesh/linetype-flags/"+b, () => RmPatternFlags(b));
            Run("r12-mesh/grid-density/"+b, () => RmDensities(b));
            Run("r12-mesh/point-line-faces/"+b, () => RmFaceArities(b));
            Run("r12-mesh/modern-grid-order/"+b, () => RmModernOrder(b));
        }
        for (int fault = 0; fault < 9; fault++)
        { int f = fault; Run("r12-mesh/output-refusal/"+f, () => RmWriteRefusal(f)); }
        Run("r12-mesh/budgets-and-cancellation", RmBudgets);
        Run("r12-mesh/signed-short-boundary", RmSignedBoundary);
        Run("r12-mesh/maximum-grid", RmMaximumGrid);
        Run("r12-mesh/large-face-count", RmLargeFaceCount);
        Run("r12-mesh/edit-clone-and-source", RmEdits);
    }

    private static Layer RmLayer() => new("MESHES") { Color = new AciColor(2) };
    private static PolygonMesh RmGrid(int normal = 0, int closed = 3)
    {
        var points = new List<Vector3>();
        for (int v = 0; v < 3; v++) for (int u = 0; u < 2; u++)
            points.Add(new Vector3(u == 0 && v == 0 ? -0.0 : u*10.0, v*20.0, u+v*.5));
        return new PolygonMesh(2,3,points) { Layer = RmLayer(), Color = new AciColor(3),
            IsClosedInU = (closed&1)!=0, IsClosedInV=(closed&2)!=0, DensityU=5, DensityV=7, Normal=R12Normal(normal) };
    }
    private static PolyfaceMesh RmPolyface(int normal = 0, int style = 2)
    {
        var faces = new[] {new PolyfaceMeshFace(new short[]{1,-2,3}),new PolyfaceMeshFace(new short[]{-1,3,-4,5}),new PolyfaceMeshFace(new short[]{2,3,4,0})};
        if (style != 0) faces[1].Color = new AciColor(6);
        if (style == 2)
        {
            faces[1].Layer = new Layer("FACES") { Color = new AciColor(1), IsVisible = false };
            faces[2].Color = AciColor.ByBlock;
        }
        return new PolyfaceMesh(new[]{new Vector3(-0.0,0,1),new Vector3(4,0,2),new Vector3(4,3,3),new Vector3(0,3,4),new Vector3(2,1,5)}, faces)
            { Layer = RmLayer(), Color = new AciColor(4), Normal=R12Normal(normal) };
    }
    private static void RmBits(Vector3 a, Vector3 b)
    {
        Equal(BitConverter.DoubleToInt64Bits(a.X),BitConverter.DoubleToInt64Bits(b.X),"Mesh X bits");
        Equal(BitConverter.DoubleToInt64Bits(a.Y),BitConverter.DoubleToInt64Bits(b.Y),"Mesh Y bits");
        Equal(BitConverter.DoubleToInt64Bits(a.Z),BitConverter.DoubleToInt64Bits(b.Z),"Mesh Z bits");
    }
    private static void RmSame(EntityObject a, EntityObject b)
    {
        Equal(a.GetType(), b.GetType(), "Mesh typed class"); R12Vector(a.Normal,b.Normal);
        Equal(a.Layer.Name,b.Layer.Name,"Mesh layer"); Equal(a.Color.Index,b.Color.Index,"Mesh color");
        if (a is PolygonMesh g)
        {
            var h=(PolygonMesh)b; Equal(g.U,h.U,"Grid U");Equal(g.V,h.V,"Grid V");
            Equal(g.IsClosedInU,h.IsClosedInU,"Grid U closure");Equal(g.IsClosedInV,h.IsClosedInV,"Grid V closure");
            Equal(g.DensityU,h.DensityU,"Stored U density");Equal(g.DensityV,h.DensityV,"Stored V density");
            for(int u=0;u<g.U;u++)for(int v=0;v<g.V;v++) RmBits(g.GetVertex(u,v),h.GetVertex(u,v));
            Check(h.VertexRecords.Count==0 && h.EndSequenceRecord==null,"R12 grid claimed retained child metadata");
        }
        else
        {
            var p=(PolyfaceMesh)a;var q=(PolyfaceMesh)b;Equal(p.Vertexes.Length,q.Vertexes.Length,"Polyface vertex count");Equal(p.Faces.Count,q.Faces.Count,"Polyface face count");
            for(int i=0;i<p.Vertexes.Length;i++)RmBits(p.Vertexes[i],q.Vertexes[i]);
            for(int i=0;i<p.Faces.Count;i++)
            {
                short[] expected=new short[4];Array.Copy(p.Faces[i].VertexIndexes,expected,p.Faces[i].VertexIndexes.Length);
                Check(expected.SequenceEqual(q.Faces[i].VertexIndexes),"Signed/terminated face indices changed");
                Equal((p.Faces[i].Color??p.Color).Index,(q.Faces[i].Color??q.Color).Index,"Face color");
                var l=p.Faces[i].Layer??p.Layer;var r=q.Faces[i].Layer??q.Layer;
                Equal(l.Name,r.Name,"Face layer");Equal(l.Color.Index,r.Color.Index,"Face layer color");Equal(l.IsVisible,r.IsVisible,"Face layer visibility");
            }
            Check(q.VertexRecords.Count==0 && q.FaceRecords.Count==0 && q.EndSequenceRecord==null,"R12 polyface claimed child identity retention");
        }
    }
    private static void RmGridRoundtrip(bool binary,int normal,int closed)
    {
        var source=RmGrid(normal,closed);var raw=DxfR12Codec.Create(new[]{source});
        for(int i=0;i<3;i++)
        {
            raw=R12Reload(raw,i%2==0?binary:!binary);var item=DxfR12Codec.ReadEntities(raw).Single();
            RmSame(source,item);RmSame(source,(EntityObject)item.Clone());Equal("100",item.Handle,"Grid identity");
            raw=DxfR12Codec.Create(new[]{item});
        }
    }
    private static void RmFaceRoundtrip(bool binary,int normal,int style)
    {
        var source=RmPolyface(normal,style);var raw=DxfR12Codec.Create(new[]{source});
        for(int i=0;i<3;i++)
        {
            raw=R12Reload(raw,i%2==0?binary:!binary);var item=DxfR12Codec.ReadEntities(raw).Single();
            RmSame(source,item);RmSame(source,(EntityObject)item.Clone());
            Check(item.Owner==null,"Selected mesh adopted source ownership");
            raw=DxfR12Codec.Create(new[]{item});
        }
    }
    private static void RmWire(bool binary,int normal)
    {
        EntityObject[] source={RmGrid(normal),RmPolyface(normal),new Line(new Vector3(10,20,30),new Vector3(40,50,60)){Layer=RmLayer()}};
        var raw=DxfR12Codec.Create(source);
        for(int stage=0;stage<3;stage++)
        {
            bool format=stage==1?!binary:binary;byte[] bytes=R12Bytes(raw,format);
            File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"r12-meshes-{normal}-{(binary?"binary":"text")}-{new[]{"source","output","resave"}[stage]}.dxf"),bytes);
            using var stream=new MemoryStream(bytes);raw=DxfRawDocument.Load(stream);var items=DxfR12Codec.ReadEntities(raw);
            Equal(3,items.Count,"Mixed mesh selection count");RmSame(source[0],items[0]);RmSame(source[1],items[1]);R12Compare(source[2],items[2]);
            Equal("112",items[2].Handle,"Following mesh LINE identity");
            raw=DxfR12Codec.Create(items);
        }
    }
    private static IReadOnlyList<DxfRawRecord> RmRecords(DxfRawDocument raw)=>raw.Sections.Single(s=>s.Name=="ENTITIES").Records;
    private static DxfRawDocument RmChange(DxfRawDocument raw,DxfRawRecord record,short code,object value)
    {
        var tags=record.Tags.ToList();int at=tags.FindIndex(t=>t.Code==code);
        if(at<0)tags.Add(new DxfTag(code,value));else tags[at]=new DxfTag(code,value);
        return raw.WithRecord(record,tags);
    }
    private static void RmMalformed(bool binary,int fault)
    {
        bool grid=fault<11; var raw=R12Reload(DxfR12Codec.Create(new EntityObject[]{grid?RmGrid():RmPolyface(),new Line(Vector3.Zero,Vector3.UnitX)}),binary);
        var r=RmRecords(raw);var header=r[0];var child=r[1];var end=r[r.Count-2];var face=grid?child:r[6];
        byte[] original=R12Bytes(raw,binary);
        switch(fault)
        {
            case 0:raw=RmChange(raw,header,71,(short)1);break;
            case 1:raw=RmChange(raw,header,72,(short)257);break;
            case 2:raw=raw.WithoutRecord(child);break;
            case 3:raw=RmChange(raw,header,71,(short)3);break;
            case 4:raw=RmChange(raw,header,75,(short)5);break;
            case 5:raw=RmChange(raw,header,70,(short)80);break;
            case 6:raw=RmChange(raw,child,70,(short)192);break;
            case 7:raw=RmChange(raw,child,62,(short)2);break;
            case 8:raw=RmChange(raw,child,71,(short)1);break;
            case 9:raw=RmChange(raw,header,39,1.0);break;
            case 10:raw=RmChange(raw,header,40,1.0);break;
            case 11:raw=raw.WithoutRecord(end);break;
            case 12:raw=RmChange(raw,child,70,(short)32);break;
            case 13:raw=RmChange(raw,face,71,(short)6);break;
            case 14:raw=RmChange(raw,face,71,(short)0);break;
            case 15:raw=RmChange(raw,face,70,(short)129);break;
            case 16:raw=RmChange(raw,face,62,(short)-1);break;
            case 17:raw=RmChange(raw,child,5,"100");break;
            case 18:raw=RmChange(raw,end,5,"101");break;
            case 19:raw=RmChange(raw,face,42,1.0);break;
            case 20:raw=RmChange(raw,face,6,"CONTINUOUS");break;
            case 21:raw=raw.WithRecord(face,face.Tags.Concat(new[]{new DxfTag(1001,"APP"),new DxfTag(1000,"private")}));break;
            case 22:raw=raw.WithRecord(child,child.Tags.Concat(new[]{new DxfTag(10,1.0)}));break;
            case 23:raw=RmChange(raw,child,8,"OTHER");break;
            case 24:raw=RmChange(raw,end,62,(short)4);break;
            case 25:raw=RmChange(raw,header,73,(short)1);break;
            case 26:raw=RmChange(raw,face,67,(short)1);break;
        }
        byte[] altered=R12Bytes(raw,binary);R12Refuses(()=>DxfR12Codec.ReadEntities(raw));
        Check(altered.SequenceEqual(R12Bytes(raw,binary)),"Rejected mesh decode mutated raw input");
        Check(original.Length>0,"Missing original wire fixture");
    }
    private static void RmAdvisory(bool binary,int variant)
    {
        var seed=RmPolyface();var raw=R12Reload(DxfR12Codec.Create(new[]{seed}),binary);var records=RmRecords(raw);var head=records[0];
        if(variant==0)raw=raw.WithRecord(head,head.Tags.Where(t=>t.Code!=71 && t.Code!=72));
        if(variant==1)raw=RmChange(raw,head,71,(short)-32768);
        if(variant==2)raw=RmChange(raw,head,72,short.MaxValue);
        if(variant==3 || variant==4)
        {
            var tags=raw.Tags.Take(head.EndTagIndex).ToList();
            if(variant==3){foreach(var rec in records.Skip(6).Take(3))tags.AddRange(rec.Tags);foreach(var rec in records.Skip(1).Take(5))tags.AddRange(rec.Tags);}
            else{tags.AddRange(records[6].Tags);tags.AddRange(records[1].Tags);tags.AddRange(records[7].Tags);foreach(var rec in records.Skip(2).Take(4))tags.AddRange(rec.Tags);tags.AddRange(records[8].Tags);}
            tags.AddRange(raw.Tags.Skip(records[9].StartTagIndex));raw=DxfRawDocument.Create(tags);
        }
        if(variant==5)
        {
            var face=records[6];raw=raw.WithRecord(face,face.Tags.Where(t=>t.Code!=10&&t.Code!=20&&t.Code!=30));
        }
        if(variant==6)raw=RmChange(raw,records[6],10,123.0); // Ignored location of a face record.
        raw=R12Reload(raw,binary);RmSame(seed,DxfR12Codec.ReadEntities(raw).Single());
        RmSame(seed,DxfR12Codec.ReadEntities(R12Reload(DxfR12Codec.Create(DxfR12Codec.ReadEntities(raw)),!binary)).Single());
    }
    private static void RmDensities(bool binary)
    {
        foreach(short hint in new short[]{short.MinValue,-1,0,2,201,short.MaxValue})
        {
            var raw=DxfR12Codec.Create(new[]{RmGrid()});raw=RmChange(raw,RmRecords(raw)[0],73,hint);raw=RmChange(raw,RmRecords(raw)[0],74,hint);
            var mesh=(PolygonMesh)DxfR12Codec.ReadEntities(R12Reload(raw,binary)).Single();
            Equal(hint,mesh.DensityU,"Inactive stored density changed");Equal(hint,mesh.DensityV,"Inactive stored density changed");
            var copy=(PolygonMesh)DxfR12Codec.ReadEntities(R12Reload(DxfR12Codec.Create(new[]{mesh}),!binary)).Single();RmSame(mesh,copy);
        }
    }
    private static void RmFaceArities(bool binary)
    {
        var points=RmPolyface().Vertexes;
        foreach(var indices in new[]{new short[]{1},new short[]{-1,2},new short[]{1,-2,3},new short[]{-1,2,3,-4},new short[]{1,0,short.MinValue,300}})
        {
            var mesh=new PolyfaceMesh(points,new[]{indices}){Layer=RmLayer()};
            var copy=(PolyfaceMesh)DxfR12Codec.ReadEntities(R12Reload(DxfR12Codec.Create(new[]{mesh}),binary)).Single();RmSame(mesh,copy);
            Equal(mesh.Explode().Single().GetType(),copy.Explode().Single().GetType(),"Degenerate face interpretation changed");
        }
    }
    private static void RmModernOrder(bool binary)
    {
        var mesh=RmGrid();var doc=new DxfDocument(DxfVersion.AutoCad2000);doc.Entities.Add(mesh);
        using var stream=new MemoryStream();Check(doc.Save(stream,binary),"Modern mesh reference save");stream.Position=0;
        var modern=DxfRawDocument.Load(stream);var classic=DxfR12Codec.Create(new[]{RmGrid()});
        var a=RmRecords(modern).Where(r=>r.Name=="VERTEX").ToArray();var b=RmRecords(classic).Where(r=>r.Name=="VERTEX").ToArray();Equal(a.Length,b.Length,"Modern/classic vertex counts");
        for(int i=0;i<a.Length;i++)foreach(short code in new short[]{10,20,30})
            Equal(BitConverter.DoubleToInt64Bits((double)a[i].Tags.Single(t=>t.Code==code).Value),BitConverter.DoubleToInt64Bits((double)b[i].Tags.Single(t=>t.Code==code).Value),"Modern/classic rectangular grid order");
    }
    private static void RmWriteRefusal(int fault)
    {
        EntityObject mesh=fault<3?RmGrid():RmPolyface();
        switch(fault)
        {
            case 0:((PolygonMesh)mesh).SmoothType=PolylineSmoothType.Quadratic;break;
            case 1:((PolygonMesh)mesh).Vertexes[5]=new Vector3(0,0,double.NaN);break;
            case 2:mesh.XData.Add(new XData(new ApplicationRegistry("PRIVATE")));break;
            case 3:((PolyfaceMesh)mesh).Faces[2].VertexIndexes[2]=6;break;
            case 4:((PolyfaceMesh)mesh).Faces[2].Color=new AciColor(20,40,60);break;
            case 5:((PolyfaceMesh)mesh).Faces[2].Layer=new Layer("MESHES"){Color=new AciColor(5)};break;
            case 6:((PolyfaceMesh)mesh).Vertexes[4]=new Vector3(double.PositiveInfinity,0,0);break;
            case 7:((PolyfaceMesh)mesh).Faces[2].Layer=new Layer("FACES"){Description="Private metadata"};break;
            case 8:((PolyfaceMesh)mesh).Faces[2].VertexIndexes[0]=0;break;
        }
        using var output=new MemoryStream(new byte[]{11,22,33},true);R12Refuses(()=>DxfR12Codec.Save(output,new[]{new Line(Vector3.Zero,Vector3.UnitX),mesh}));
        Equal(0L,output.Position,"Late invalid mesh touched output");Check(output.ToArray().SequenceEqual(new byte[]{11,22,33}),"Rejected mesh changed destination bytes");
    }
    private static void RmBudgets()
    {
        var mesh=RmPolyface();var raw=DxfR12Codec.Create(new[]{mesh});int count=raw.Tags.Count;
        DxfR12Codec.Create(new[]{mesh},false,new DxfRawOptions(1000000,count,1024));
        Throws<InvalidDataException>(()=>DxfR12Codec.Create(new[]{mesh},false,new DxfRawOptions(1000000,count-1,1024)));
        using var output=new MemoryStream();Throws<InvalidDataException>(()=>DxfR12Codec.Save(output,new[]{mesh},false,new DxfRawOptions(100,1000,100)));Equal(0L,output.Length,"Budget failure partially serialized");
        using var cancel=new CancellationTokenSource();cancel.Cancel();Throws<OperationCanceledException>(()=>DxfR12Codec.Save(output,new[]{mesh},false,null,cancel.Token));
    }
    private static void RmSignedBoundary()
    {
        var vertices=Enumerable.Range(0,32768).Select(i=>new Vector3(i,i%7,-i)).ToArray();
        var mesh=new PolyfaceMesh(vertices,new[]{new short[]{short.MinValue,1,32767}});
        var raw=R12Reload(DxfR12Codec.Create(new[]{mesh}),true);var head=RmRecords(raw)[0];
        Equal((short)0,(short)head.Tags.Single(t=>t.Code==71).Value,"Large advisory count overflowed");
        var copy=(PolyfaceMesh)DxfR12Codec.ReadEntities(raw).Single();RmBits(vertices[32767],copy.Vertexes[32767]);
        Equal(short.MinValue,copy.Faces[0].VertexIndexes[0],"Signed index boundary changed");
        Equal(32768,copy.Vertexes.Length,"Advisory count truncated vertices");
    }
    private static void RmMaximumGrid()
    {
        var grid=new PolygonMesh(256,256,Enumerable.Range(0,65536).Select(i=>new Vector3(i%256,i/256,i)));
        var copy=(PolygonMesh)DxfR12Codec.ReadEntities(R12Reload(DxfR12Codec.Create(new[]{grid}),true)).Single();
        Equal(grid.GetVertex(255,254),copy.GetVertex(255,254),"Maximum rectangular grid transposed");
        Equal(grid.GetVertex(254,255),copy.GetVertex(254,255),"Maximum grid corner order");
    }
    private static void RmPatternFlags(bool binary)
    {
        foreach(EntityObject seed in new EntityObject[]{RmGrid(),RmPolyface()})
        {
            var raw=DxfR12Codec.Create(new[]{seed});var header=RmRecords(raw)[0];
            short flags=(short)((short)header.Tags.Single(t=>t.Code==70).Value|128);
            raw=RmChange(raw,header,70,flags);
            var item=DxfR12Codec.ReadEntities(R12Reload(raw,binary)).Single();
            var output=DxfR12Codec.Create(new[]{(EntityObject)item.Clone()});
            Equal(flags,(short)RmRecords(output)[0].Tags.Single(t=>t.Code==70).Value,"Mesh linetype flag not preserved");
        }
    }
    private static void RmLargeFaceCount()
    {
        var mesh=new PolyfaceMesh(new[]{Vector3.Zero,Vector3.UnitX,Vector3.UnitY},
            Enumerable.Range(0,32768).Select(_=>new short[]{1,2,3}));
        var raw=R12Reload(DxfR12Codec.Create(new[]{mesh}),true);
        Equal((short)0,(short)RmRecords(raw)[0].Tags.Single(t=>t.Code==72).Value,"Face count hint overflowed");
        Equal(32768,((PolyfaceMesh)DxfR12Codec.ReadEntities(raw).Single()).Faces.Count,"Large face inventory truncated");
    }
    private static void RmEdits()
    {
        var mesh=RmPolyface();var owner=new DxfDocument();owner.Entities.Add(mesh);string handle=mesh.Handle;
        var faces=mesh.Faces;var vertexes=mesh.Vertexes;var raw=DxfR12Codec.Create(new[]{mesh});byte[] source=R12Bytes(raw,false);
        Check(mesh.Handle==handle && ReferenceEquals(owner.GetObjectByHandle(handle),mesh)&&ReferenceEquals(faces,mesh.Faces)&&ReferenceEquals(vertexes,mesh.Vertexes),"Mesh export changed caller objects");
        var decoded=(PolyfaceMesh)DxfR12Codec.ReadEntities(raw).Single();var clone=(PolyfaceMesh)decoded.Clone();
        clone.SetVertex(0,new Vector3(100,200,300));clone.SetFaceEdgeVisibility(0,0,false);
        Equal((short)1,decoded.Faces[0].VertexIndexes[0],"Clone index edit reached original");
        Check(!ReferenceEquals(decoded.Faces[0],clone.Faces[0]) && !ReferenceEquals(decoded.Vertexes,clone.Vertexes),"Clone shared mutable topology");
        var saved=(PolyfaceMesh)DxfR12Codec.ReadEntities(R12Reload(DxfR12Codec.Create(new[]{clone}),true)).Single();RmSame(clone,saved);
        Check(source.SequenceEqual(R12Bytes(raw,false)),"Mesh projection edit mutated raw source");
    }
}
