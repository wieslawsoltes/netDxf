using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterMTextCloneDirectionTests()
    {
        foreach (MTextDrawingDirection direction in Enum.GetValues<MTextDrawingDirection>())
        {
            MTextDrawingDirection d = direction;
            Run($"mtext/direction/clone/{d}", () => MTextDirectionClone(d));
            Run($"mtext/direction/block-clone/{d}", () => MTextDirectionBlockClone(d));
            Run($"mtext/direction/explode/{d}", () => MTextDirectionExplode(d));
            foreach (DxfVersion version in SupportedVersions)
                foreach (bool binary in new[] { false, true })
                {
                    DxfVersion v = version; bool b = binary;
                    Run($"mtext/direction/clone-roundtrip/{d}/{v}/{b}", () => MTextDirectionRoundTrip(d, v, b));
                }
        }
    }

    private static MText DirectionalMText(MTextDrawingDirection direction) => new MText("Line one\\PZażółć 東京", new Vector3(1.25, -2.5, 3.75), 4.5, 20)
    {
        DrawingDirection = direction,
        Rotation = 30,
        AttachmentPoint = MTextAttachmentPoint.MiddleRight,
        LineSpacingFactor = 1.5,
        LineSpacingStyle = MTextLineSpacingStyle.Exact
    };

    private static void MTextDirectionClone(MTextDrawingDirection direction)
    {
        MText original = DirectionalMText(direction);
        var copy = (MText)original.Clone();
        Equal(direction, copy.DrawingDirection, "MText clone lost drawing direction");
        Equal(original.Value, copy.Value, "MText clone value");
        Equal(original.Position, copy.Position, "MText clone position");
        Equal(original.AttachmentPoint, copy.AttachmentPoint, "MText clone attachment");
        Near(original.Height, copy.Height, "MText clone height");
        Near(original.Rotation, copy.Rotation, "MText clone rotation");
        Near(original.RectangleWidth, copy.RectangleWidth, "MText clone width");
        Equal(original.LineSpacingStyle, copy.LineSpacingStyle, "MText clone line spacing style");
        Near(original.LineSpacingFactor, copy.LineSpacingFactor, "MText clone line spacing");
        Check(copy.Owner == null && copy.Handle == null, "MText clone retained document identity.");
        copy.DrawingDirection = direction == MTextDrawingDirection.TopToBottom ? MTextDrawingDirection.LeftToRight : MTextDrawingDirection.TopToBottom;
        Equal(direction, original.DrawingDirection, "Editing the clone changed source direction");
    }

    private static void MTextDirectionBlockClone(MTextDrawingDirection direction)
    {
        var block = new Block("Directional");
        block.Entities.Add(DirectionalMText(direction));
        var insert = new Insert(block);
        var copy = (Insert)insert.Clone();
        Equal(direction, copy.Block.Entities.OfType<MText>().Single().DrawingDirection, "Insert/block clone lost MText direction");
        Check(!ReferenceEquals(insert.Block, copy.Block), "Insert clone reused source block.");
    }

    private static void MTextDirectionExplode(MTextDrawingDirection direction)
    {
        var block = new Block("Directional");
        block.Entities.Add(DirectionalMText(direction));
        var insert = new Insert(block, new Vector3(10, 20, 30));
        MText text = insert.Explode().OfType<MText>().Single();
        Equal(direction, text.DrawingDirection, "Exploding an insert lost MText direction");
        Equal(new Vector3(11.25, 17.5, 33.75), text.Position, "Exploded MText position");
        Equal(direction, block.Entities.OfType<MText>().Single().DrawingDirection, "Explode changed the source direction");
    }

    private static void MTextDirectionRoundTrip(MTextDrawingDirection direction, DxfVersion version, bool binary)
    {
        var document = new DxfDocument(version);
        MText original = DirectionalMText(direction);
        var copy = (MText)original.Clone();
        copy.Position = new Vector3(100, 200, 300);
        document.Entities.Add(original); document.Entities.Add(copy);
        using var stream = new MemoryStream();
        Check(document.Save(stream, binary), "Directional MText save failed."); stream.Position = 0;
        DxfDocument loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Directional MText load failed.");
        MText[] values = loaded.Entities.MTexts.ToArray();
        Equal(2, values.Length, "Directional MText count");
        foreach (MText text in values)
        {
            Equal(direction, text.DrawingDirection, "Direction changed during clone/save/load");
            Equal(original.Value, text.Value, "Directional MText content changed");
        }
        Check(stream.CanRead, "MText round trip closed caller-owned stream.");
    }
}
