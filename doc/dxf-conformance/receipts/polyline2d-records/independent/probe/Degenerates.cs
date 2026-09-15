using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

internal static partial class Program
{
    static void Degenerate(byte[] bytes,string parent,bool binary,string label,int count)
    {
        var original=Load(bytes);var poly=Poly(original,parent);var raw=Raw(bytes);
        foreach(var child in poly.VertexRecords.Skip(count))raw=raw.WithoutRecord(Record(raw,child.Handle));
        byte[] input=Bytes(raw,binary);var d=Load(input);var p=Poly(d,parent);var models=p.Vertexes.ToArray();var children=p.VertexRecords.ToArray();var seq=p.EndSequenceRecord;
        Check(p.Vertexes.Count==count&&p.VertexRecords.Count==count&&seq!=null&&ReferenceEquals(d.GetObjectByHandle(seq.Handle),seq),"Degenerate chain/end identity missing");
        string state=State(p);long seed=Seed(d);p.Reverse();Check(State(p)==state&&Seed(d)==seed&&children.SequenceEqual(p.VertexRecords)&&models.SequenceEqual(p.Vertexes),"Degenerate Reverse must be a validated identity operation");
        foreach(bool output in new[]{false,true})
        {
            var saved=Save(d,output,label+"-degenerate-"+count+"-stored-"+output+".dxf");var written=Raw(saved);var reloaded=Poly(Load(saved),parent);
            Check(reloaded.VertexRecords.Count==count&&reloaded.Vertexes.Count==count,"Degenerate writer skipped or fabricated points");
            Check(Same(Header(Record(raw,parent)),Header(Record(written,parent))),"Degenerate parent subclass changed");
            foreach(var r in children.Concat(new[]{seq!}))Check(Same(Record(raw,r.Handle).Tags,Record(written,r.Handle).Tags),"Degenerate child/SEQEND complete packet changed");
        }
        var clone=(Polyline2D)p.Clone();d.Entities.Add(clone);Check(clone.VertexRecords.Count==count&&clone.EndSequenceRecord!=null&&!ReferenceEquals(clone.EndSequenceRecord,seq)&&clone.EndSequenceRecord.Handle!=seq!.Handle,"Degenerate clone did not allocate independent physical SEQEND");
        var oldPoints=p.Vertexes.Select(v=>v.Position).ToArray();double elevation=p.Elevation,thickness=p.Thickness;double? start=p.LegacyDefaultStartWidth,end=p.LegacyDefaultEndWidth;
        p.TransformBy(new Matrix3(2,0,0,0,2,0,0,0,2),new Vector3(5,-3,7));Near(elevation*2+7,p.Elevation,"Degenerate transformed parent plane");Near(thickness*2,p.Thickness,"Degenerate transformed thickness");
        Check(p.LegacyDefaultStartWidth==start*2&&p.LegacyDefaultEndWidth==end*2&&p.VertexRecords.SequenceEqual(children),"Degenerate transform changed widths or physical identities");
        for(int i=0;i<count;i++){Near(oldPoints[i].X*2+5,p.Vertexes[i].Position.X,"Singleton transformed X");Near(oldPoints[i].Y*2-3,p.Vertexes[i].Position.Y,"Singleton transformed Y");}
        foreach(bool output in new[]{false,true}){var saved=Save(d,output,label+"-degenerate-"+count+"-transform-"+output+".dxf");Near(p.Elevation,Poly(Load(saved),parent).Elevation,"Reloaded degenerate plane");}
        var target=new Line(Vector3.Zero,Vector3.UnitX);d.Entities.Add(target);seq!.PersistentReactors.Add(target);Check(!d.Entities.Remove(target),"Degenerate SEQEND reactor must protect ordinary target");seq.PersistentReactors.Remove(target);
        var x=new XData(d.ApplicationRegistries.Add(new ApplicationRegistry("DEGENERATE_LINK")));x.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle,target.Handle));seq.XData.Add(x);Check(!d.Entities.Remove(target),"Degenerate SEQEND XData must protect ordinary target");seq.XData.Remove(x.ApplicationRegistry.Name);Check(d.Entities.Remove(target),"Degenerate metadata release did not unblock target");
        var ids=children.Concat(new[]{seq}).ToArray();Check(d.Entities.Remove(p)&&ids.All(r=>d.GetObjectByHandle(r.Handle)==null&&ReferenceEquals(r.Owner,p)),"Degenerate detachment failed to unregister retained records");
        var foreign=new DxfDocument(d.DrawingVariables.AcadVer);long foreignSeed=Seed(foreign);bool refused=false;try{foreign.Entities.Add(p);}catch(NotSupportedException){refused=true;}Check(refused&&Seed(foreign)==foreignSeed&&!foreign.Entities.All.Any()&&p.Owner==null,"Degenerate foreign adoption must reject before mutation");
        d.Entities.Add(p);Check(ids.All(r=>ReferenceEquals(d.GetObjectByHandle(r.Handle),r)),"Degenerate source readoption changed identities");
        var version=d.DrawingVariables.AcadVer;d.DrawingVariables.AcadVer=version==DxfVersion.AutoCad2018?DxfVersion.AutoCad2000:DxfVersion.AutoCad2018;Refuse(d,binary,"degenerate source profile");d.DrawingVariables.AcadVer=version;
        Save(d,binary,label+"-degenerate-"+count+"-lifecycle.dxf");
    }

    static void DummyPoint(byte[] data,string parent,bool binary,string label,int mask)
    {
        var raw=Edit(Raw(data),parent,tags=>
        {
            int subclass=tags.FindIndex(t=>t.Code==100&&Equals(t.Value,"AcDb2dPolyline"));
            for(int i=tags.Count-1;i>subclass;i--)
                if(tags[i].Code is 10 or 20 or 30 && (mask&(1<<(tags[i].Code/10-1)))==0)tags.RemoveAt(i);
        });
        if(mask!=0)
        {
            bool refused=false;try{refused=DxfDocument.Load(new MemoryStream(Bytes(raw,binary)))==null;}
            catch(FormatException e){refused=e.Message=="A retained legacy POLYLINE requires a complete dummy point or its complete omission.";}
            Check(refused,"Partial legacy parent dummy vector did not reject contextually");return;
        }
        var d=Load(Bytes(raw,binary));var p=Poly(d,parent);Near(0,p.Elevation,"Entirely absent parent point has zero elevation");
        foreach(bool output in new[]{false,true})
        {
            var saved=Save(d,output,label+"-omitted-point-unchanged-"+output+".dxf");Check(Same(Header(Record(raw,parent)),Header(Record(Raw(saved),parent))),"Unedited complete omission did not stay exact");
        }
        p.Elevation=7;
        foreach(bool output in new[]{false,true})
        {
            var saved=Save(d,output,label+"-omitted-point-edit-"+output+".dxf");var header=Header(Record(Raw(saved),parent));
            var point=header.Where(t=>t.Code is 10 or 20 or 30).ToArray();Check(point.Length==3&&point.Select(t=>t.Code).SequenceEqual(new short[]{10,20,30}),"Edited omitted point must emit the complete ordered XYZ triple");Near(0,(double)point[0].Value,"Dummy X");Near(0,(double)point[1].Value,"Dummy Y");Near(7,(double)point[2].Value,"Edited Z");Near(7,Poly(Load(saved),parent).Elevation,"Reloaded edited elevation");
        }
        p.TransformBy(Matrix3.Identity,new Vector3(0,0,3));Near(10,p.Elevation,"Edited omitted parent transform");Save(d,!binary,label+"-omitted-point-transform.dxf");
    }
}
