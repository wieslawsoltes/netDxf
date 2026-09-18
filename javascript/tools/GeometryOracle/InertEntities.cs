// Test-only structural serialization of the pinned production objects, not a replacement implementation.
using System.Linq;
using netDxf.Entities;
internal static partial class Program
{
    private static bool InertEntityWire(EntityObject entity, object common, out object? result)
    {
        result=null;
        if (entity is OleFrame ole) result=new {common,version=ole.OleVersion,present=ole.HasOleVersion,length=ole.BinaryDataLength,bytes=Wire(ole.GetBinaryData())};
        else if (entity is Ole2Frame ole2) result=new {common,version=ole2.OleVersion,fields=(int)ole2.MetadataFields,upper=Wire(ole2.UpperLeftCorner),lower=Wire(ole2.LowerRightCorner),description=Wire(ole2.Description),kind=(int)ole2.ObjectType,tile=ole2.TileMode,length=ole2.BinaryDataLength,bytes=Wire(ole2.GetBinaryData())};
        else if (entity is AcisEntity acis) result=new {common,version=acis.ModelerFormatVersion,chunks=acis.EncodedSatChunks.Select(Wire).ToArray(),lines=acis.SatLines.Select(Wire).ToArray(),history=acis is Solid3D solid3d?solid3d.HistoryHandle:null};
        else return false;
        return true;
    }
}
