using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterStoredTableGeometryTests()
    {
        foreach (string file in TableContentFiles) foreach (bool input in new[] { false, true }) foreach (bool binary in new[] { false, true })
            Run($"table-geometry/native/{file}/{input}/{binary}", () => TableGeometryNative(file,input,binary));
        foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2004)) foreach (bool binary in new[] {false,true})
        {
            Run($"table-geometry/schema/{version}/{binary}", () => TableGeometrySchema(version,binary));
            Run($"table-geometry/empty/{version}/{binary}", () => TableGeometryEmpty(version,binary));
        }
        foreach (bool binary in new[] {false,true})
        {
            foreach (int fault in Enumerable.Range(0,28)) Run($"table-geometry/malformed/{binary}/{fault}", () => TableGeometryMalformed(binary,fault));
            foreach (int variant in Enumerable.Range(0,8)) Run($"table-geometry/opaque/{binary}/{variant}", () => TableGeometryOpaque(binary,variant));
            foreach (int op in Enumerable.Range(0,9)) Run($"table-geometry/lifecycle/{binary}/{op}", () => TableGeometryLifecycle(binary,op));
            foreach (string kind in new[]{"STYLE","LTYPE","APPID","ENTITY","BLOCK_RECORD","BLOCK_MEMBER"})
                Run($"table-geometry/dependency/{binary}/{kind}", () => TableGeometryDependency(binary,kind));
        }
    }
    private static List<DxfTag> TableGeometryPayload() => new() {
        new(100,"AcDbTableGeometry"),new(90,1),new(91,1),new(92,1),
        new(93,7),new(40,10.5),new(41,20.25),new(330,"0"),new(94,1),
        new(10,1.0),new(20,2.0),new(30,3.0),new(11,4.0),new(21,5.0),new(31,6.0),
        new(43,7.0),new(44,8.0),new(45,9.0),new(46,10.0),new(95,11) };
    private static DxfRawDocument TableGeometryRaw(DxfVersion version, Action<DxfDocument,List<DxfTag>>? setup=null)
    {
        var doc=new DxfDocument(version); var placeholder=new DxfXRecord(); doc.Objects.Root.Add("GEOMETRY",placeholder);
        var body=TableGeometryPayload();setup?.Invoke(doc,body);
        var raw=TableContentRaw(TableContentSave(doc,false));
        var record=raw.Sections.Single(s=>s.Name=="OBJECTS").Records.Single(r=>r.Tags.Any(t=>t.Code==5&&(string)t.Value==placeholder.Handle));
        int marker=record.Tags.ToList().FindIndex(t=>t.Code==100);
        return raw.WithRecord(record,record.Tags.Take(marker).Select(t=>t.Code==0?new DxfTag(0,"TABLEGEOMETRY"):t).Concat(body));
    }
    private static DxfDocument TableGeometryLoad(DxfRawDocument raw,bool binary) => TableContentLoad(TableContentRawBytes(raw,binary));
    private static DxfStoredTableGeometry TableGeometryObject(DxfDocument doc) => doc.Objects.Items.OfType<DxfStoredTableGeometry>().Single();
    private static void TableGeometryNative(string file,bool input,bool binary)
    {
        byte[] source=TableContentSourceBytes(file); if(input)source=TableContentRawBytes(TableContentRaw(source),true);
        var doc=TableContentLoad(source);var objects=doc.Objects.Items.OfType<DxfStoredTableGeometry>().ToArray();
        Equal(file.StartsWith("sample_",StringComparison.Ordinal)?2:1,objects.Length,"native geometry count");
        var before=objects.ToDictionary(g=>g.Handle,g=>OwnershipTagValues(g.Payload).ToArray());
        foreach(var geometry in objects)
        {
            Equal(geometry.RowCount*geometry.ColumnCount,geometry.Cells.Count,"native cells independently counted");
            Check(geometry.Owner is DxfXRecord,"exact native wrapper type");
            Check(ReferenceEquals(doc.GetObjectByHandle(geometry.Owner.Handle),geometry.Owner),"exact native owner identity");
            Equal(0,geometry.References.Count,"pinned native geometry reference nulls");
            Check(geometry.Cells.All(c=>c.GeometryReference==null),"native cell references null");
        }
        Equal(0,doc.Objects.Validate().Count,"native geometry schema");
        var loaded=TableContentLoad(TableContentSave(doc,binary,$"table-geometry-native-{file}-{binary}.dxf"));
        foreach(var geometry in loaded.Objects.Items.OfType<DxfStoredTableGeometry>())
            Check(before[geometry.Handle].SequenceEqual(OwnershipTagValues(geometry.Payload)),"native geometry packet changed");
    }
    private static void TableGeometrySchema(DxfVersion version,bool binary)
    {
        var doc=TableGeometryLoad(TableGeometryRaw(version),binary);var geometry=TableGeometryObject(doc);var cell=geometry.Cells.Single();var item=cell.Geometry.Single();
        Equal(version,geometry.SourceVersion,"source profile");Equal(1,geometry.RowCount,"rows");Equal(1,geometry.ColumnCount,"columns");
        Equal(7,cell.GeometryDataFlags,"raw geometry flags");Equal(10.5,cell.WidthWithGap,"width gap");Equal(20.25,cell.HeightWithGap,"height gap");
        Equal(new Vector3(1,2,3),item.TopLeftDistance,"top left vector");Equal(new Vector3(4,5,6),item.CenterDistance,"center vector");
        Equal(7.0,item.ContentWidth,"content width");Equal(8.0,item.ContentHeight,"content height");Equal(9.0,item.Width,"width");Equal(10.0,item.Height,"height");Equal(11,item.StoredValue95,"raw95");
        var before=OwnershipTagValues(geometry.Payload).ToArray();var loaded=TableGeometryObject(TableContentLoad(TableContentSave(doc,binary,$"table-geometry-schema-{version}-{binary}.dxf")));
        Check(before.SequenceEqual(OwnershipTagValues(loaded.Payload)),"synthetic schema packet changed");
    }
    private static void TableGeometryEmpty(DxfVersion version,bool binary)
    {
        var raw=TableGeometryRaw(version,(_,tags)=>{tags.RemoveRange(4,tags.Count-4);tags[1]=new DxfTag(90,0);tags[2]=new DxfTag(91,0);tags[3]=new DxfTag(92,0);});
        var geometry=TableGeometryObject(TableGeometryLoad(raw,binary));Equal(0,geometry.Cells.Count,"empty cells");Equal(0,geometry.RowCount,"empty rows");
    }
    private static void TableGeometryMalformed(bool binary,int fault)
    {
        var raw=TableGeometryRaw(DxfVersion.AutoCad2018,(_,tags)=>
        {
            if(fault<3)tags[fault+1]=new DxfTag((short)(90+fault),-1);
            else if(fault<6)tags[fault-2]=new DxfTag((short)(87+fault),int.MaxValue);
            else if(fault==6)tags[3]=new DxfTag(92,0);
            else if(fault==7)tags[3]=new DxfTag(92,2);
            else if(fault==8)tags[8]=new DxfTag(94,-1);
            else if(fault==9)tags[8]=new DxfTag(94,int.MaxValue);
            else if(fault==10)tags[8]=new DxfTag(94,0);
            else if(fault==11)tags[8]=new DxfTag(94,2);
            else if(fault<20)tags.RemoveAt(new[]{1,2,3,4,5,7,8,9}[fault-12]);
            else if(fault==20)tags.RemoveAt(tags.Count-1);
            else if(fault==21)tags.Add(new DxfTag(95,2));
            else if(fault==22)tags.Insert(0,new DxfTag(100,"AcDbTableGeometry"));
            else if(fault==23){var t=tags[1];tags[1]=tags[2];tags[2]=t;}
            else if(fault==24)tags[7]=new DxfTag(330,"FFFFFFFFFFFFFFFE");
            else if(fault==25)tags.Insert(1,new DxfTag(1000,"stray public xdata"));
            else if(fault==26)tags[5]=new DxfTag(40,-12345.625);
            else tags[9]=new DxfTag(10,-12345.625);
        });
        byte[] bytes=TableContentRawBytes(raw,binary);
        if(fault>=26)
        {
            if(binary)
            {
                byte[] needle=BitConverter.GetBytes(-12345.625);int at=bytes.AsSpan().IndexOf(needle);
                Check(at>=0&&bytes.AsSpan(at+needle.Length).IndexOf(needle)<0,"unique binary nonfinite injection slot");
                BitConverter.GetBytes(fault==26?double.NaN:double.PositiveInfinity).CopyTo(bytes,at);
            }
            else
            {
                string text=System.Text.Encoding.UTF8.GetString(bytes);Check(text.Split("-12345.625").Length==2,"unique text nonfinite injection slot");
                bytes=System.Text.Encoding.UTF8.GetBytes(text.Replace("-12345.625",fault==26?"NaN":"Infinity"));
            }
        }
        bool rejected=false;
        try {using var input=new MemoryStream(bytes);rejected=DxfDocument.Load(input)==null;}
        catch(FormatException){rejected=true;}catch(ArgumentException){rejected=true;}catch(InvalidDataException){rejected=true;}
        Check(rejected,"malformed public geometry accepted or made opaque");
    }
    private static void TableGeometryOpaque(bool binary,int variant)
    {
        var raw=TableGeometryRaw(variant==0?DxfVersion.AutoCad2000:DxfVersion.AutoCad2018,(_,tags)=>
        {
            if(variant==1)tags[0]=new DxfTag(100,"PrivateTableGeometry");
            if(variant==2)tags.Add(new DxfTag(100,"PrivateGeometryExtension"));
            if(variant==3)tags.Add(new DxfTag(300,"private field"));
            if(variant==4)tags.InsertRange(1,new[]{new DxfTag(102,"{PRIVATE"),new DxfTag(1000,"private value"),new DxfTag(102,"}")});
            if(variant==5)tags.AddRange(new[]{new DxfTag(100,"PrivateGeometryExtension"),new DxfTag(1000,"private value")});
            if(variant==6)tags.InsertRange(0,new[]{new DxfTag(102,"{PRIVATE"),new DxfTag(1000,"private header"),new DxfTag(102,"}")});
            if(variant==7)tags.Add(new DxfTag(320,"EEEEEEEE"));
        });
        var doc=TableGeometryLoad(raw,binary);var opaque=doc.Objects.Items.OfType<DxfOpaqueObject>().Single(o=>o.CodeName=="TABLEGEOMETRY");var before=OwnershipTagValues(opaque.Tags).ToArray();
        var loaded=TableContentLoad(TableContentSave(doc,binary,$"table-geometry-opaque-{variant}-{binary}.dxf"));
        Check(before.SequenceEqual(OwnershipTagValues(loaded.Objects.Items.OfType<DxfOpaqueObject>().Single(o=>o.CodeName=="TABLEGEOMETRY").Tags)),"private geometry packet changed");
    }
    private static void TableGeometryLifecycle(bool binary,int operation)
    {
        var doc=TableGeometryLoad(TableGeometryRaw(DxfVersion.AutoCad2018),binary);var geometry=TableGeometryObject(doc);int count=doc.Objects.Items.Count();
        if(operation==0)Throws<NotSupportedException>(()=>doc.Objects.CloneObject(geometry,doc.Objects.Root,"COPY"));
        else if(operation==1)Throws<NotSupportedException>(()=>doc.Objects.EraseOwnedTree(geometry));
        else if(operation==2)Throws<NotSupportedException>(()=>((IList<DxfTag>)geometry.Payload).Clear());
        else if(operation==3)Throws<NotSupportedException>(()=>((IList<DxfStoredTableGeometryCell>)geometry.Cells).Clear());
        else if(operation==4)Throws<NotSupportedException>(()=>((IList<DxfStoredTableCellGeometry>)geometry.Cells[0].Geometry).Clear());
        else if(operation==5)
        {
            doc.DrawingVariables.AcadVer=DxfVersion.AutoCad2013;using var output=new MemoryStream();bool rejected=false;
            try{rejected=!doc.Save(output,binary);}catch(NotSupportedException){rejected=true;}catch(InvalidOperationException){rejected=true;}
            Check(rejected,"stored geometry profile changed");Equal(0L,output.Length,"profile rejection output");
        }
        else if(operation==6)
        {
            doc.Classes.Remove("TABLEGEOMETRY");var loaded=TableContentLoad(TableContentSave(doc,binary));Equal(1152,loaded.Classes["TABLEGEOMETRY"].ProxyFlags,"CLASS flags");
        }
        else if(operation==7)
        {
            doc.Classes.Remove("TABLEGEOMETRY");doc.Classes.Add(new DxfClass("TABLEGEOMETRY","PrivateClass","Private"));using var output=new MemoryStream();CheckSaveRejected(doc,output);Equal(0L,output.Length,"CLASS conflict output");
        }
        else Check(typeof(DxfStoredTableGeometry).GetConstructors().Length==0,"geometry authoring constructor exposed");
        Equal(count,doc.Objects.Items.Count(),"lifecycle changed membership");
    }
    private static void TableGeometryDependency(bool binary,string kind)
    {
        string handle="";var raw=TableGeometryRaw(DxfVersion.AutoCad2018,(doc,tags)=>
        {
            DxfObject target;
            if(kind=="STYLE")target=doc.TextStyles.Add(new TextStyle("GEOMETRY_STYLE","Arial.ttf"));
            else if(kind=="LTYPE")target=doc.Linetypes.Add(new Linetype("GEOMETRY_LTYPE"));
            else if(kind=="APPID")target=doc.ApplicationRegistries.Add(new ApplicationRegistry("GEOMETRY_APP"));
            else if(kind=="ENTITY"){var line=new Line(Vector3.Zero,Vector3.UnitX);doc.Entities.Add(line);target=line;}
            else{var block=new Block("GEOMETRY_BLOCK");var line=new Line(Vector3.Zero,Vector3.UnitX);block.Entities.Add(line);doc.Blocks.Add(block);target=kind=="BLOCK_RECORD"?block.Record:line;}
            handle=target.Handle;tags[7]=new DxfTag(330,handle);
        });
        var doc=TableGeometryLoad(raw,binary);var geometry=TableGeometryObject(doc);var target=doc.GetObjectByHandle(handle);
        Check(ReferenceEquals(geometry.Cells[0].GeometryReference,target)&&ReferenceEquals(geometry.References.Single(),target),"geometry exact source identity");
        bool removed=kind switch{"STYLE"=>doc.TextStyles.Remove("GEOMETRY_STYLE"),"LTYPE"=>doc.Linetypes.Remove("GEOMETRY_LTYPE"),"APPID"=>doc.ApplicationRegistries.Remove("GEOMETRY_APP"),"ENTITY"=>doc.Entities.Remove((EntityObject)target),_=>doc.Blocks.Remove("GEOMETRY_BLOCK")};
        Check(!removed,"geometry dependency removed");Equal(0,doc.Objects.Validate().Count,"geometry source graph changed");
    }
}
