using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterOleMetadataApiTests()
    {
        for(int mask=0;mask<64;++mask)
        {
            int m=mask;Run($"olemetadata/api/{m}",()=>OleMetadataApi(m));
        }
        foreach(int flags in new[]{-1,64,int.MinValue})
        {
            int f=flags;Run($"olemetadata/api/reject/{f}",()=>OleMetadataRejectFlags(f));
        }
    }
    private static Ole2Frame NewOleMetadataFrame()
    {
        var frame=new Ole2Frame(OlePayload(33),new Vector3(1,2,3),new Vector3(4,5,6),
            @"Literal \U+000A",4,OleObjectType.Static,1);
        var data=new XData(new ApplicationRegistry("OLE_META"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String,"independent"));frame.XData.Add(data);
        return frame;
    }
    private static void OleMetadataApi(int mask)
    {
        var original=NewOleMetadataFrame();var owner=new DxfDocument();owner.Entities.Add(original);
        Equal(Ole2FrameMetadataFields.All,original.MetadataFields,"Constructor output compatibility");
        var selected=original.WithMetadataFields((Ole2FrameMetadataFields)mask);
        Equal((Ole2FrameMetadataFields)mask,selected.MetadataFields,"Explicit selection");
        Check(selected.Owner==null && selected.Handle==null,"Metadata selection copied document identity.");
        Check(!ReferenceEquals(selected.Color,original.Color) && !ReferenceEquals(selected.Layer,original.Layer) &&
              !ReferenceEquals(selected.XData["OLE_META"],original.XData["OLE_META"]),"Selection aliases common mutable state.");
        var clone=(Ole2Frame)selected.Clone();
        Equal(selected.MetadataFields,clone.MetadataFields,"Clone changed optional field selection");
        Equal(original.Description,clone.Description,"Selecting absence changed dormant description");
        Equal(original.UpperLeftCorner,clone.UpperLeftCorner,"Selecting absence changed dormant point");
        Equal(original.TileMode,clone.TileMode,"Selecting absence changed stored mode");
        byte[] bytes=selected.GetBinaryData();bytes[0]^=255;
        Check(original.GetBinaryData().SequenceEqual(OlePayload(33)) && selected.GetBinaryData().SequenceEqual(OlePayload(33)),"Selection aliases bytes.");
        foreach(var frame in new[]{selected,clone})
        {
            var doc=new DxfDocument(DxfVersion.AutoCad2018);doc.Entities.Add(frame);
            using var output=new MemoryStream();Check(doc.Save(output,true),"Selected metadata save failed.");output.Position=0;
            CheckOleMetadataPresence(DxfRawDocument.Load(output).Sections.SelectMany(s=>s.Records).Single(r=>r.Name=="OLE2FRAME"),mask);
        }
        var restored=selected.WithMetadataFields(Ole2FrameMetadataFields.All);
        Equal(original.Description,restored.Description,"Restoring fields lost dormant description");
        Equal(Ole2FrameMetadataFields.All,restored.MetadataFields,"Explicit restoration");
        Equal(Ole2FrameMetadataFields.All,original.MetadataFields,"Selection mutated source flags");
    }
    private static void OleMetadataRejectFlags(int flags)
    {
        var original=NewOleMetadataFrame();
        Throws<ArgumentOutOfRangeException>(()=>original.WithMetadataFields((Ole2FrameMetadataFields)flags));
        Equal(Ole2FrameMetadataFields.All,original.MetadataFields,"Invalid selection mutated source");
        Check(original.GetBinaryData().SequenceEqual(OlePayload(33)),"Invalid selection mutated data.");
    }
}
