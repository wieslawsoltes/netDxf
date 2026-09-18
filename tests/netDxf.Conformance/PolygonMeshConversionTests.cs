using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static PolygonMesh GridConversionSubject(PolylineSmoothType smooth, int closure)
    {
        var mesh = new PolygonMesh(4, 5, Enumerable.Range(0, 20).Select(i => new Vector3(i % 4, i / 4, i * .125)))
        {
            SmoothType = smooth, IsClosedInU = (closure & 1) != 0, IsClosedInV = (closure & 2) != 0,
            Layer = new Layer("GRID_CONVERSION"), Color = new AciColor(3), IsVisible = false,
            LinetypeScale = 1.75, Lineweight = Lineweight.W35, ProxyGraphics = new byte[] { 2, 3, 5 }
        };
        var data = new XData(new ApplicationRegistry("GRID_CONVERSION"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, "independent")); mesh.XData.Add(data);
        return mesh;
    }

    private static void GridConversionCheck(PolylineSmoothType smooth, int closure, bool explicitDensity, bool owned, short setting, bool detachedBlock = false)
    {
        short oldU = PolygonMesh.DefaultSurfU, oldV = PolygonMesh.DefaultSurfV;
        try
        {
            PolygonMesh.DefaultSurfU = setting; PolygonMesh.DefaultSurfV = (short)(setting + 1);
            var mesh = GridConversionSubject(smooth, closure);
            var document = new DxfDocument(); document.DrawingVariables.SurfU = setting; document.DrawingVariables.SurfV = (short)(setting + 1);
            if (owned) document.Entities.Add(mesh);
            if (detachedBlock) new netDxf.Blocks.Block("DETACHED_GRID").Entities.Add(mesh);
            if (explicitDensity) { mesh.DensityU = 5; mesh.DensityV = 7; }
            var before = mesh.Vertexes.ToArray(); var owner = mesh.Owner; string handle = mesh.Handle;
            var samples = mesh.MeshVertexes();
            int u = smooth == PolylineSmoothType.NoSmooth ? mesh.U : explicitDensity ? 5 : Math.Max(3, setting + 1);
            int v = smooth == PolylineSmoothType.NoSmooth ? mesh.V : explicitDensity ? 7 : Math.Max(3, setting + 2);
            var converted = mesh.ToMesh(); var faces = mesh.Explode();
            Check(converted.Vertexes.SequenceEqual(samples), "Conversion sampling mismatch");
            int cellsU = (closure & 1) != 0 ? u : u - 1, cellsV = (closure & 2) != 0 ? v : v - 1;
            Equal(cellsU * cellsV, faces.Count, "Missing exploded cell"); Equal(faces.Count, converted.Faces.Count, "Missing MESH cell");
            for (int row = 0; row < cellsV; row++) for (int col = 0; col < cellsU; col++)
            {
                int[] expected = { row*u+col, row*u+(col+1)%u, ((row+1)%v)*u+(col+1)%u, ((row+1)%v)*u+col };
                int n = row*cellsU+col; Check(expected.SequenceEqual(converted.Faces[n]), "Grid corner incidence/winding");
                Check(new[] { faces[n].FirstVertex, faces[n].SecondVertex, faces[n].ThirdVertex, faces[n].FourthVertex }
                    .SequenceEqual(expected.Select(i=>samples[i])), "Exploded cell geometry");
            }
            foreach (EntityObject entity in faces.Cast<EntityObject>().Append(converted))
            {
                Equal(mesh.Layer.Name, entity.Layer.Name, "Layer lost"); Equal(mesh.Color.Index, entity.Color.Index, "Color lost");
                Equal(mesh.Lineweight, entity.Lineweight, "Lineweight lost"); Equal(mesh.LinetypeScale, entity.LinetypeScale, "Linetype scale lost");
                Check(!entity.IsVisible && entity.Owner == null && entity.Handle == null && entity.ProxyGraphics == null, "Detached metadata/proxy");
                Check(!ReferenceEquals(entity.Layer, mesh.Layer) && !ReferenceEquals(entity.Color, mesh.Color), "Appearance alias");
                Check(!ReferenceEquals(entity.XData["GRID_CONVERSION"], mesh.XData["GRID_CONVERSION"]), "XData alias");
            }
            Check(!ReferenceEquals(faces[0].Color, faces[1].Color), "Sibling color alias");
            faces[0].XData["GRID_CONVERSION"].XDataRecord.Clear(); Equal(1, mesh.XData["GRID_CONVERSION"].XDataRecord.Count, "Source XData mutated");
            Check(mesh.Vertexes.SequenceEqual(before) && ReferenceEquals(owner, mesh.Owner) && mesh.Handle == handle, "Source changed");
            Check(mesh.ProxyGraphics!.SequenceEqual(new byte[] {2,3,5}), "Source proxy changed");
        }
        finally { PolygonMesh.DefaultSurfU=oldU; PolygonMesh.DefaultSurfV=oldV; }
    }

    private static void RegisterPolygonMeshConversionTests()
    {
        foreach (var smooth in new[] { PolylineSmoothType.NoSmooth, PolylineSmoothType.Quadratic, PolylineSmoothType.Cubic })
        foreach (int closure in Enumerable.Range(0,4)) foreach (bool explicitDensity in new[]{false,true})
        foreach (bool owned in new[]{false,true}) foreach (short setting in new short[]{0,1,6})
            Run($"grid-conversion/model/{smooth}/{closure}/{explicitDensity}/{owned}/{setting}",
                ()=>GridConversionCheck(smooth,closure,explicitDensity,owned,setting));
        foreach (var smooth in new[] { PolylineSmoothType.NoSmooth, PolylineSmoothType.Quadratic, PolylineSmoothType.Cubic })
        foreach (int closure in Enumerable.Range(0,4))
            Run($"grid-conversion/detached-block/{smooth}/{closure}", ()=>GridConversionCheck(smooth,closure,false,false,1,true));
        foreach (bool binary in new[] {false,true}) foreach(bool decorated in new[]{false,true}) foreach(bool trueColor in new[]{false,true})
            Run($"grid-conversion/loaded-children/{binary}/{decorated}/{trueColor}",()=>
            {
                var doc=new DxfDocument();var original=GridConversionSubject(PolylineSmoothType.NoSmooth,3);
                if(trueColor)original.Color=new AciColor(33,132,229);doc.Entities.Add(original);
                using var stream=new MemoryStream();Check(doc.Save(stream,binary),"Source grid save");stream.Position=0;
                var loaded=DxfDocument.Load(stream)??throw new InvalidOperationException("Source grid load");
                var mesh=loaded.Entities.PolygonMeshes.Single();Equal(20,mesh.VertexRecords.Count,"Missing retained controls");
                var identities=mesh.VertexRecords.Select(v=>v.Handle).ToArray();
                if(decorated)
                {
                    var data=new XData(new ApplicationRegistry("CHILD_DATA"));data.XDataRecord.Add(new XDataRecord(XDataCode.String,"retain child"));
                    mesh.VertexRecords.Last().XData.Add(data);
                    Throws<NotSupportedException>(()=>mesh.Explode());Throws<NotSupportedException>(()=>mesh.ToMesh());
                    Equal("retain child",(string)mesh.VertexRecords.Last().XData["CHILD_DATA"].XDataRecord.Single().Value,"Child data changed");
                }
                else
                {
                    var faces=mesh.Explode();Equal(20,faces.Count,"Loaded seam missing");Equal(20,mesh.ToMesh().Faces.Count,"Loaded mesh seam missing");
                    Check(faces.All(f=>f.Color.R==mesh.Color.R && f.Color.G==mesh.Color.G && f.Color.B==mesh.Color.B && f.Color.UseTrueColor==mesh.Color.UseTrueColor),"Loaded colors changed");
                }
                Check(identities.SequenceEqual(mesh.VertexRecords.Select(v=>v.Handle)),"Child identities changed");
                Check(mesh.VertexRecords.All(v=>ReferenceEquals(v,loaded.GetObjectByHandle(v.Handle))),"Child registration changed");
            });
        foreach (bool explode in new[]{false,true})
        {
            Run($"grid-conversion/guard/handle/{explode}",()=>
            {
                var mesh=GridConversionSubject(PolylineSmoothType.NoSmooth,0);
                mesh.XData["GRID_CONVERSION"].XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle,"AB"));
                Throws<NotSupportedException>(()=>{if(explode)mesh.Explode();else mesh.ToMesh();});
                Check(mesh.ProxyGraphics!.SequenceEqual(new byte[]{2,3,5}),"Rejected conversion mutated proxy");
            });
            Run($"grid-conversion/guard/late-nonfinite/{explode}",()=>
            {
                var mesh=GridConversionSubject(PolylineSmoothType.Cubic,0); mesh.Vertexes[19]=new(double.NaN,0,0);
                Throws<InvalidOperationException>(()=>{if(explode)mesh.Explode();else mesh.ToMesh();});
                Check(double.IsNaN(mesh.Vertexes[19].X),"Invalid source mutated");
            });
        }
        foreach(var size in new[]{(65536,65536),(int.MaxValue,3),(1001,1000)})
            Run($"grid-conversion/budget/{size.Item1}/{size.Item2}",()=>
            {
                // Do not invoke an unbounded old sampler during the negative run.
                Check(typeof(PolygonMesh).GetField("MaximumSurfaceSamples")!=null,"Missing preallocation budget");
                var mesh=GridConversionSubject(PolylineSmoothType.Cubic,0);
                Throws<ArgumentOutOfRangeException>(()=>mesh.MeshVertexes(size.Item1,size.Item2));
                Throws<ArgumentOutOfRangeException>(()=>mesh.ToMesh(size.Item1,size.Item2));
            });
        foreach(var version in SupportedVersions) foreach(bool binary in new[]{false,true}) foreach(int closure in Enumerable.Range(0,4))
        foreach(bool asMesh in new[]{false,true}.Where(m=>!m || version>=DxfVersion.AutoCad2010))
            Run($"grid-conversion/wire/{version}/{binary}/{closure}/{asMesh}",()=>
            {
                var source=GridConversionSubject(PolylineSmoothType.NoSmooth,closure); var doc=new DxfDocument(version);
                if(asMesh)doc.Entities.Add(source.ToMesh());else doc.Entities.Add(source.Explode());
                using var stream=new MemoryStream();Check(doc.Save(stream,binary),"Conversion save");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"grid-conversion-{version}-{binary}-{closure}-{asMesh}.dxf"),stream.ToArray());
                stream.Position=0;var loaded=DxfDocument.Load(stream)??throw new InvalidOperationException("Conversion load");
                Equal(0,loaded.Objects.Validate().Count,"Conversion graph");
                int count=((closure&1)!=0?4:3)*((closure&2)!=0?5:4);
                Equal(count,asMesh?loaded.Entities.Meshes.Single().Faces.Count:loaded.Entities.Faces3D.Count(),"Round-trip cell count");
            });
    }
}
