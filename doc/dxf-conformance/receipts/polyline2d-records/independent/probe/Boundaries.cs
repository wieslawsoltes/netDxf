using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

internal static partial class Program
{
    static void Extra()
    {
        foreach(bool binary in new[]{false,true})
        {
            foreach(int fault in Enumerable.Range(0,8))Run("fixed-topology/"+binary+"/"+fault,()=>Topology(binary,fault));
            Run("owned-clone/"+binary,()=>OwnedClone(binary));
            Run("incoming-reference/"+binary,()=>Incoming(binary));
            Run("child-budget/"+binary,()=>Budget(binary));
            Run("normal-plane/"+binary,()=>NormalPlane(binary));
        }
    }
    static byte[] Input(bool binary)=>File.ReadAllBytes(Path.Combine(Fixtures,"producer-R2018-"+(binary?"binary":"ascii")+".dxf"));
    static void Topology(bool binary,int fault)
    {
        var d=Load(Input(binary));var p=Poly(d,"3D");var vertices=p.Vertexes.ToArray();var smooth=p.SmoothType;double? width=p.ConstantWidth;
        if(fault==0)p.Vertexes.Add(new Polyline2DVertex(8,9));
        if(fault==1)p.Vertexes.RemoveAt(0);
        if(fault==2)p.Vertexes.Reverse();
        if(fault==3)p.Vertexes[0]=(Polyline2DVertex)p.Vertexes[0].Clone();
        if(fault==4||fault==5){string state=State(p);long seed=Seed(d);bool rejected=false;try{if(fault==4)p.SmoothType=PolylineSmoothType.Quadratic;else p.ConstantWidth=0;}catch(NotSupportedException){rejected=true;}Check(rejected&&State(p)==state&&Seed(d)==seed,"Representation setter did not reject atomically");return;}
        if(fault==6)p.Vertexes[0].Bulge=double.NaN;
        if(fault==7)p.Elevation=double.PositiveInfinity;
        Refuse(d,binary,"fixed topology "+fault);
        foreach(Action op in new Action[]{()=>p.Reverse(),()=>p.TransformBy(Matrix3.Identity,Vector3.Zero),()=>p.Clone()})
        {
            var current=p.Vertexes.ToArray();var refs=p.VertexRecords.ToArray();long seed=Seed(d);double elevation=p.Elevation;
            bool refused=false;try{op();}catch(Exception){refused=true;}
            Check(refused&&current.SequenceEqual(p.Vertexes)&&refs.SequenceEqual(p.VertexRecords)&&Seed(d)==seed&&Equals(elevation,p.Elevation),"Invalid topology operation mutated state");
        }
        p.Vertexes.Clear();p.Vertexes.AddRange(vertices);p.SmoothType=smooth;p.ConstantWidth=width;p.Vertexes[0].Bulge=0;p.Elevation=0;Save(d,binary);
    }
    static void OwnedClone(bool binary)
    {
        var d=Load(Input(binary));var p=Poly(d,"33");Check(p.VertexRecords[2].ExtensionDictionary!=null&&p.EndSequenceRecord.ExtensionDictionary!=null,"Owned fixture missing dependencies");long seed=Seed(d);bool rejected=false;try{p.Clone();}catch(NotSupportedException){rejected=true;}Check(rejected&&Seed(d)==seed,"Owned retained clone not rejected atomically");Check(!d.Entities.Remove(p),"Owned retained chain removed without explicit graph release");Save(d,!binary,"owned-metadata-"+binary+".dxf");
    }
    static void Incoming(bool binary)
    {
        var d=Load(Input(binary));var p=Poly(d,"3D");var child=p.VertexRecords[1];var line=new Line(Vector3.Zero,Vector3.UnitX);d.Entities.Add(line);var x=new XData(d.ApplicationRegistries.Add(new ApplicationRegistry("INCOMING_CHILD")));x.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle,child.Handle));line.XData.Add(x);Check(!d.Entities.Remove(p),"Ordinary incoming reference failed to guard parent removal");line.XData.Remove(x.ApplicationRegistry.Name);Check(d.Entities.Remove(p),"Incoming link release failed");
    }
    static void Budget(bool binary)
    {
        var d=Load(Input(binary));var p=Poly(d,"3D");var r=p.VertexRecords[0];var raw=Raw(Input(binary));int sourceCount=Record(raw,r.Handle).Tags.Count-1;
        var x=new XData(d.ApplicationRegistries.Add(new ApplicationRegistry("BUDGET")));r.XData.Add(x);
        for(int i=0;i<4096-sourceCount-1;i++)x.XDataRecord.Add(new XDataRecord(XDataCode.Int16,(short)1));
        var bytes=Save(d,binary);Check(Load(bytes)!=null,"Exact child tag budget should reload");x.XDataRecord.Add(new XDataRecord(XDataCode.Int16,(short)1));Refuse(d,binary,"child budget");x.XDataRecord.RemoveAt(x.XDataRecord.Count-1);Save(d,binary);
    }
    static void NormalPlane(bool binary)
    {
        var d=Load(Input(binary));var p=Poly(d,"3D");var old=p.Vertexes.Select(v=>v.Position).ToArray();p.TransformBy(new Matrix3(0,0,2,0,2,0,-2,0,0),new Vector3(5,7,11));
        Check(p.Normal==Vector3.UnitX,"Plane normal did not rotate");
        var ocs=MathHelper.ArbitraryAxis(p.Normal);
        for(int i=0;i<old.Length;i++)
        {
            var actual=ocs*new Vector3(p.Vertexes[i].Position.X,p.Vertexes[i].Position.Y,p.Elevation);
            Near(5,actual.X,"Tilted world X");Near(old[i].Y*2+7,actual.Y,"Tilted world Y");Near(-old[i].X*2+11,actual.Z,"Tilted world Z");
        }
        Save(d,!binary,"tilted-plane-"+binary+".dxf");
    }
}
