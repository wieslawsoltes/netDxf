using netDxf;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterOpaqueTableContentReferenceTests()
    {
        foreach (bool binary in new[] { false,true }) foreach (string kind in new[] { "STYLE","LTYPE" }) foreach (short code in new short[] { 340,320,329 })
            Run($"table-content/opaque-resource/{kind}/{code}/{binary}",() => OpaqueTableContentReference(kind,code,binary));
    }
    private static void OpaqueTableContentReference(string kind,short code,bool binary)
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2018);
        DxfObject target = kind == "STYLE" ? doc.TextStyles.Add(new TextStyle("PRIVATE_CONTENT_STYLE","Arial.ttf")) : doc.Linetypes.Add(new Linetype("PRIVATE_CONTENT_LTYPE"));
        var placeholder = new DxfXRecord(); doc.Objects.Root.Add("PRIVATE_CONTENT",placeholder);
        using var stream = new MemoryStream(); Check(doc.Save(stream,binary),"opaque carrier save"); stream.Position=0;var raw=DxfRawDocument.Load(stream);
        var record=raw.Sections.Single(s=>s.Name=="OBJECTS").Records.Single(r=>r.Tags.Any(t=>t.Code==5&&(string)t.Value==placeholder.Handle));
        int first=record.Tags.ToList().FindIndex(t=>t.Code==100);
        raw=raw.WithRecord(record,record.Tags.Take(first).Select(t=>t.Code==0?new DxfTag(0,"TABLECONTENT"):t).Concat(new[]{new DxfTag(100,"PrivateContent"),new DxfTag(code,target.Handle)}));
        using var input=new MemoryStream();raw.Save(input,binary);input.Position=0;var loaded=DxfDocument.Load(input)??throw new Exception("opaque content load");
        Check(loaded.Objects.Items.OfType<DxfOpaqueObject>().Any(o=>o.CodeName=="TABLECONTENT"),"private content was not opaque");
        int references=kind=="STYLE"?loaded.TextStyles.GetReferences("PRIVATE_CONTENT_STYLE").Count:loaded.Linetypes.GetReferences("PRIVATE_CONTENT_LTYPE").Count;
        Equal(code==340?1:0,references,"opaque semantic reference count");
        bool removed=kind=="STYLE"?loaded.TextStyles.Remove("PRIVATE_CONTENT_STYLE"):loaded.Linetypes.Remove("PRIVATE_CONTENT_LTYPE");
        Equal(code!=340,removed,"opaque semantic resource removal guard");
        using var output=new MemoryStream();Check(loaded.Save(output,binary),"opaque guarded output");output.Position=0;var saved=DxfRawDocument.Load(output);
        Check(saved.Sections.Single(s=>s.Name=="OBJECTS").Records.Single(r=>r.Name=="TABLECONTENT").Tags.Any(t=>t.Code==code&&(string)t.Value==target.Handle),"opaque handle changed");
        bool present=saved.Sections.Single(s=>s.Name=="TABLES").Records.Any(r=>r.Tags.Any(t=>t.Code==5&&(string)t.Value==target.Handle));
        Equal(code==340,present,"saved resource identity retention");
    }
}
