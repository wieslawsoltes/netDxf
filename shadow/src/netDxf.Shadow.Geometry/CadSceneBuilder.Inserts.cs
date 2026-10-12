namespace netDxf.Shadow.Geometry;

public sealed partial class CadSceneBuilder
{
    private readonly record struct InsertCell(Affine3 Transform, Affine3 AttributeTransform, string Key);

    private static IEnumerable<InsertCell> InsertCells(ShadowReader entity, ShadowReader block,
        Affine3 parent, string key, int maximumOccurrences)
    {
        var basis = Affine3.ObjectCoordinates(entity["Normal"].Vector(Vec3.UnitZ));
        var rotation = Affine3.RotateZ(entity["Rotation"].Number() * Math.PI / 180);
        var factor = UnitMeters(block["Record"]["Units"].Text) / UnitMeters(entity["Owner"]["Record"]["Units"].Text);
        var local = basis * rotation * Affine3.Scale(entity["Scale"].Vector(new(1, 1, 1)) * factor) * Affine3.Move(-block["Origin"].Vector());
        var rowValue = Math.Max(1, entity["RowCount"].Number(1));
        var columnValue = Math.Max(1, entity["ColumnCount"].Number(1));
        if (!double.IsFinite(rowValue * columnValue) || rowValue * columnValue > maximumOccurrences)
            throw new InvalidOperationException("Insert array exceeds occurrence budget.");
        for (var row = 0; row < (int)rowValue; row++)
            for (var column = 0; column < (int)columnValue; column++)
            {
                var offset = (basis * rotation).Direction(new(column * entity["ColumnSpacing"].Number(), row * entity["RowSpacing"].Number()));
                var instance = parent * Affine3.Move(entity["Position"].Vector() + offset) * local;
                // Attached attributes are already expressed in the insert's parent coordinate system.
                yield return new(instance, parent * Affine3.Move(offset), key + $"[{row},{column}]/");
            }
    }
}
