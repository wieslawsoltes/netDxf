using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterOleVersionPresenceApiTests()
    {
        foreach (short value in new short[] { 0, 1, 7, short.MaxValue })
        {
            short v = value;
            Run($"oleframe/version-presence/api/{v}", () => OleVersionPresenceApi(v));
        }
    }

    private static void OleVersionPresenceApi(short value)
    {
        var source = new OleFrame(OlePayload(128), value) { Color = AciColor.Red };
        Check(source.HasOleVersion, "Existing constructor stopped emitting a version.");
        var document = new DxfDocument(DxfVersion.AutoCad2018); document.Entities.Add(source);
        var absent = source.WithOleVersionPresence(false);
        Check(!absent.HasOleVersion && source.HasOleVersion, "Selecting absence mutated the source.");
        Equal(value, absent.OleVersion, "Selecting absence modified a dormant value");
        Check(absent.Handle == null && absent.Owner == null, "Copy retained database identity.");
        absent.Color = AciColor.Blue; Equal((short)1, source.Color.Index, "Copy changed source color");
        byte[] data = absent.GetBinaryData(); data[0] = 77;
        Check(source.GetBinaryData().SequenceEqual(OlePayload(128)), "Copy aliases source payload.");
        Check(absent.GetBinaryData().SequenceEqual(OlePayload(128)), "Getter exposes mutable payload.");
        var clone = (OleFrame)absent.Clone(); Check(!clone.HasOleVersion, "Clone invented version metadata.");
        var restored = clone.WithOleVersionPresence(true);
        Check(restored.HasOleVersion && !clone.HasOleVersion, "Presence restoration mutated source.");
        Equal(value, restored.OleVersion, "Restoration lost the dormant value");
        var block = new Block("AbsentVersion"); block.Entities.Add(absent);
        var insert = new Insert(block);
        var nested = (Insert)insert.Clone();
        Check(!nested.Block.Entities.OfType<OleFrame>().Single().HasOleVersion, "Nested clone invented a version.");
        Check(!insert.Explode().OfType<OleFrame>().Single().HasOleVersion, "Identity explosion invented a version.");
        Throws<NotSupportedException>(() => clone.TransformBy(Matrix3.Identity, Vector3.UnitX));
        Check(!clone.HasOleVersion, "Rejected transform changed presence.");
    }
}
