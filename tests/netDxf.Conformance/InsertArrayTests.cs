using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;
using netDxf.Units;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly Vector3[] ArrayNormals = { Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitX, new Vector3(1, 2, 3) };
    private static readonly double[] ArrayAngles = { 0, 35, 90, 315 };

    private static void RegisterInsertArrayTests()
    {
        Run("insert-array/model", InsertArrayModel);
        Run("insert-array/lazy-large-grid", InsertArrayLarge);
        Run("insert-array/reject-unrepresentable-transform", InsertArrayInvalidTransform);
        foreach (Vector3 normal in ArrayNormals)
            foreach (double angle in ArrayAngles)
            {
                Vector3 n = normal; double a = angle;
                Run($"insert-array/geometry/{n}/{a}", () => InsertArrayGeometry(n, a));
                Run($"insert-array/transforms/{n}/{a}", () => InsertArrayTransform(n, a));
            }
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                foreach (int scenario in Enumerable.Range(0, 7))
                {
                    int s = scenario;
                    Run($"insert-array/independent-input/{v}/{b}/{s}", () => InsertArrayInput(v, b, s));
                }
                foreach (int malformed in Enumerable.Range(0, 4))
                {
                    int m = malformed;
                    Run($"insert-array/invalid-count/{v}/{b}/{m}", () => InsertArrayMalformed(v, b, m));
                }
                Run($"insert-array/clone-roundtrip/{v}/{b}", () => InsertArrayRoundTrip(v, b));
                Run($"insert-array/nested/{v}/{b}", () => InsertArrayNested(v, b));
                Run($"insert-array/units/{v}/{b}", () => InsertArrayUnits(v, b));
                Run($"insert-array/independent-export/{v}/{b}", () => InsertArrayExport(v, b));
            }
    }

    private static Insert ArrayInsert(bool attribute = true)
    {
        var block = new Block("ArrayComponent") { Origin = new Vector3(0.5, -1, 2) };
        block.Entities.Add(new Line(new Vector3(1, 2, 3), new Vector3(4, -2, 5)));
        if (attribute) block.AttributeDefinitions.Add(new AttributeDefinition("TAG") { Value = "P-101", Position = new Vector3(2, 3, 0), Height = 2 });
        var insert = new Insert(block, new Vector3(10, -20, 5))
        {
            ColumnCount = 3, RowCount = 2, ColumnSpacing = 7.5, RowSpacing = -4.25,
            Scale = new Vector3(2, -3, 4), Rotation = 35
        };
        var xdata = new XData(new ApplicationRegistry("ARRAY_TEST"));
        xdata.XDataRecord.Add(new XDataRecord(XDataCode.String, "array metadata")); insert.XData.Add(xdata);
        return insert;
    }

    private static void InsertArrayModel()
    {
        var i = new Insert(new Block("Empty"));
        Equal((short)1, i.RowCount, "default rows"); Equal((short)1, i.ColumnCount, "default columns");
        Near(0, i.RowSpacing, "default row spacing"); Near(0, i.ColumnSpacing, "default column spacing");
        Check(!i.IsMultiple && i.InstanceCount == 1, "Default INSERT is not a singleton.");
        foreach (short invalid in new short[] { 0, -1, short.MinValue })
        {
            Throws<ArgumentOutOfRangeException>(() => i.RowCount = invalid);
            Throws<ArgumentOutOfRangeException>(() => i.ColumnCount = invalid);
        }
        foreach (double invalid in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
        {
            Throws<ArgumentOutOfRangeException>(() => i.RowSpacing = invalid);
            Throws<ArgumentOutOfRangeException>(() => i.ColumnSpacing = invalid);
        }
        Equal(1, i.InstanceCount, "Rejected counts changed state");
        Throws<ArgumentOutOfRangeException>(() => i.GetGridPosition(-1, 0));
        Throws<ArgumentOutOfRangeException>(() => i.GetGridPosition(0, 1));
        Throws<ArgumentOutOfRangeException>(() => i.ExplodeCell(1, 0));
        i.ColumnCount = short.MaxValue; i.RowCount = short.MaxValue;
        Equal(1073676289, i.InstanceCount, "Grid count overflowed");
        i.ColumnSpacing = double.MaxValue;
        Throws<InvalidOperationException>(() => i.GetGridPosition(0, 2));
        Equal(0, i.ExplodeEnumerable().Count(), "Empty large block produced geometry.");
        i = ArrayInsert(); var clone = (Insert)i.Clone();
        CheckArrayFields(i, clone); clone.RowCount = 4; clone.ColumnSpacing = 99;
        Equal((short)2, i.RowCount, "Clone rows changed source"); Near(7.5, i.ColumnSpacing, "Clone spacing changed source");
        Check(!ReferenceEquals(i.Block, clone.Block) && !ReferenceEquals(i.Attributes[0], clone.Attributes[0]), "Clone shares mutable geometry/attributes.");
    }

    private static void ArrayNear(Vector3 expected, Vector3 actual, string name)
    {
        Near(expected.X, actual.X, name + " X"); Near(expected.Y, actual.Y, name + " Y"); Near(expected.Z, actual.Z, name + " Z");
    }

    private static Vector3 ArrayExpectedOffset(Insert insert, int row, int column)
    {
        // Explicit scalar rotation of the two grid coordinates, independently of block Scale.
        double radians = insert.Rotation * Math.PI / 180, x = column * insert.ColumnSpacing, y = row * insert.RowSpacing;
        var ocs = new Vector3(x * Math.Cos(radians) - y * Math.Sin(radians), x * Math.Sin(radians) + y * Math.Cos(radians), 0);
        return MathHelper.Transform(ocs, insert.Normal, CoordinateSystem.Object, CoordinateSystem.World);
    }

    private static void InsertArrayGeometry(Vector3 normal, double angle)
    {
        foreach (var spacing in new (double X, double Y)[] { (7.5, -4.25), (-2, 3), (0, 0), (0, 2) })
        {
            Insert i = ArrayInsert(); i.Normal = normal; i.Rotation = angle;
            i.ColumnSpacing = spacing.X; i.RowSpacing = spacing.Y; i.TransformAttributes();
            Matrix3 transform = i.GetTransformation();
            var child = i.Block.Entities.OfType<Line>().Single();
            var exploded = i.Explode(); Equal(12, exploded.Count, "Array explosion did not retain every logical cell");
            Equal(12, i.ExplodeEnumerable().Count(), "Lazy explosion cell count");
            for (int row = 0; row < i.RowCount; row++)
                for (int column = 0; column < i.ColumnCount; column++)
                {
                    Vector3 offset = ArrayExpectedOffset(i, row, column);
                    ArrayNear(i.Position + offset, i.GetGridPosition(row, column), "grid insertion point");
                    var cell = i.ExplodeCell(row, column); Equal(2, cell.Count, "Cell explosion count");
                    var line = cell.OfType<Line>().Single(); var text = cell.OfType<Text>().Single();
                    ArrayNear(i.Position + offset + transform * (child.StartPoint - i.Block.Origin), line.StartPoint, "cell start");
                    ArrayNear(i.Position + offset + transform * (child.EndPoint - i.Block.Origin), line.EndPoint, "cell end");
                    ArrayNear(i.Attributes[0].Position + offset, text.Position, "array attribute placement");
                    Equal("P-101", text.Value, "Array attribute value");
                    Check(line.Owner == null && line.Handle == null, "Exploded geometry retained database identity.");
                    ArrayNear(line.StartPoint, ((Line)exploded[2 * (row * i.ColumnCount + column)]).StartPoint, "row-major explosion order");
                }
            var first = (Line)exploded[0]; first.StartPoint = new Vector3(999, 999, 999);
            Check(!child.StartPoint.Equals(first.StartPoint) && !((Line)exploded[2]).StartPoint.Equals(first.StartPoint), "Cells share mutable geometry.");
        }
    }

    private static void InsertArrayLarge()
    {
        var i = ArrayInsert(false); i.RowCount = short.MaxValue; i.ColumnCount = short.MaxValue;
        i.Rotation = 0; i.Scale = new Vector3(1, 1, 1); i.RowSpacing = 2; i.ColumnSpacing = 3;
        Equal(1, i.ExplodeEnumerable().Take(1).Count(), "Large grid was not lazily consumable");
        ArrayNear(i.Position + new Vector3(32766 * 3, 32766 * 2, 0), i.GetGridPosition(32766, 32766), "Last large-grid cell");
        Equal(1, i.ExplodeCell(32766, 32766).Count, "Indexed cell expansion materialized the whole grid");
    }

    private static void InsertArrayTransform(Vector3 normal, double angle)
    {
        foreach (Vector3 scale in new[] { new Vector3(2, 3, 4), new Vector3(-2, 3, 1), new Vector3(2, -3, -4), new Vector3(0.5, 0.5, 0.5) })
        {
            var i = ArrayInsert(); i.Normal = normal; i.Rotation = angle; i.TransformAttributes();
            Matrix3 basis = MathHelper.ArbitraryAxis(i.Normal) * Matrix3.RotationZ(angle * MathHelper.DegToRad);
            Matrix3 transform = Matrix3.RotationX(0.35) * Matrix3.RotationY(-0.47) * basis * Matrix3.Scale(scale) * basis.Transpose();
            Vector3 translation = new Vector3(7, -11, 13);
            var before = i.Explode().OfType<Line>().Select(l => (l.StartPoint, l.EndPoint)).ToArray();
            var points = Enumerable.Range(0, 6).Select(c => i.GetGridPosition(c / 3, c % 3)).ToArray();
            Vector3 attribute = i.Attributes[0].Position;
            i.TransformBy(transform, translation);
            var after = i.Explode().OfType<Line>().ToArray(); Equal(before.Length, after.Length, "Transform changed array count");
            for (int cell = 0; cell < 6; cell++)
            {
                ArrayNear(transform * points[cell] + translation, i.GetGridPosition(cell / 3, cell % 3), "transformed grid point");
                ArrayNear(transform * before[cell].StartPoint + translation, after[cell].StartPoint, "transformed start");
                ArrayNear(transform * before[cell].EndPoint + translation, after[cell].EndPoint, "transformed end");
            }
            ArrayNear(transform * attribute + translation, i.Attributes[0].Position, "transformed attribute");
        }
    }

    private static void InsertArrayInvalidTransform()
    {
        foreach (Matrix3 transform in new[] { new Matrix3(1, 0.5, 0, 0, 1, 0, 0, 0, 1), Matrix3.Scale(0, 1, 1), Matrix3.Zero })
        {
            Insert i = ArrayInsert(); i.Rotation = 0; var before = (Insert)i.Clone();
            Throws<NotSupportedException>(() => i.TransformBy(transform, new Vector3(1, 2, 3)));
            CheckArrayFields(before, i); Equal(before.Position, i.Position, "Rejected transform moved INSERT");
            Equal(before.Scale, i.Scale, "Rejected transform changed scale"); Equal(before.Normal, i.Normal, "Rejected transform changed normal");
            Equal(before.Attributes[0].Position, i.Attributes[0].Position, "Rejected transform moved attribute");
        }
    }

    private static void CheckArrayFields(Insert expected, Insert actual)
    {
        Equal(expected.ColumnCount, actual.ColumnCount, "column count"); Equal(expected.RowCount, actual.RowCount, "row count");
        Near(expected.ColumnSpacing, actual.ColumnSpacing, "column spacing"); Near(expected.RowSpacing, actual.RowSpacing, "row spacing");
    }

    private static MemoryStream ArrayFixture(DxfVersion version, bool binary, IEnumerable<(short Code, object Value)> fields)
    {
        // Base records are manually authored by the minimal-document fixture, not DxfDocument.Save.
        using var original = CreateMinimalDocument(version, binary, true, false, true);
        object reader = NewCodeReader(original, binary);
        var stream = new MemoryStream(); object writer = NewCodeWriter(stream, binary); bool insert = false;
        while (true)
        {
            Invoke(reader, "Next"); short code = TagCode(reader); object value = reader.GetType().GetProperty("Value")!.GetValue(reader)!;
            if (code == 0)
            {
                if (insert) foreach (var tag in fields) { Invoke(writer, "Write", tag.Code, tag.Value); if (!binary) Invoke(writer, "Write", (short)999, "array metadata comment"); }
                insert = Equals(value, "INSERT");
            }
            if (insert && code == 100 && Equals(value, "AcDbBlockReference")) value = "AcDbMInsertBlock";
            Invoke(writer, "Write", code, value);
            if (code == 0 && Equals(value, "EOF")) break;
        }
        Invoke(writer, "Flush"); stream.Position = 0; return stream;
    }

    private static void InsertArrayInput(DxfVersion version, bool binary, int scenario)
    {
        var data = new (short Columns, short Rows, double X, double Y)[]
        { (3,2,7.5,-4.25), (1,4,0,3), (4,1,-2,0), (2,2,0,0), (1,1,3,4), (short.MaxValue, short.MaxValue,1,1), (1,1,0,0) }[scenario];
        var tags = new (short, object)[] { (45, data.Y), (71, data.Rows), (44, data.X), (70, data.Columns) };
        using var input = ArrayFixture(version, binary, scenario == 6 ? Array.Empty<(short, object)>() : tags);
        DxfDocument doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Independent MINSERT failed to load.");
        Insert i = doc.Entities.Inserts.Single();
        Equal(data.Rows, i.RowCount, "Authored row count discarded"); Equal(data.Columns, i.ColumnCount, "Authored column count discarded");
        Near(data.X, i.ColumnSpacing, "Authored column spacing"); Near(data.Y, i.RowSpacing, "Authored row spacing");
        Equal("200", i.Handle, "MINSERT handle changed");
        ArrayNear(new Vector3(-1 + (data.Columns - 1) * data.X, 2 + (data.Rows - 1) * data.Y, 3), i.GetGridPosition(data.Rows - 1, data.Columns - 1), "Authored last cell");
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "Authored MINSERT save failed."); output.Position = 0;
        var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Authored MINSERT reload failed.");
        CheckArrayFields(i, loaded.Entities.Inserts.Single()); Check(input.CanRead && output.CanRead, "MINSERT closed caller stream.");
    }

    private static void InsertArrayMalformed(DxfVersion version, bool binary, int scenario)
    {
        short code = scenario < 2 ? (short)70 : (short)71; short value = (scenario & 1) == 0 ? (short)0 : (short)-1;
        using var input = ArrayFixture(version, binary, new[] { (code, (object)value) });
#if DEBUG
        Throws<InvalidDataException>(() => DxfDocument.Load(input));
#else
        Check(DxfDocument.Load(input) == null, "Malformed array count was silently accepted.");
#endif
        Check(input.CanRead, "Malformed MINSERT closed caller stream.");
    }

    private static List<(short Code, object Value)> ArrayWire(byte[] bytes, bool binary)
    {
        using var input = new MemoryStream(bytes); object reader = NewCodeReader(input, binary);
        bool insert = false; var result = new List<(short, object)>();
        while (true)
        {
            Invoke(reader, "Next"); short code = TagCode(reader); object value = reader.GetType().GetProperty("Value")!.GetValue(reader)!;
            if (code == 0) { if (insert || Equals(value, "EOF")) return result; insert = Equals(value, "INSERT"); }
            if (insert) result.Add((code, value));
        }
    }

    private static void InsertArrayRoundTrip(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); var i = ArrayInsert(); i.TransformAttributes(); doc.Entities.Add(i);
        for (int cycle = 0; cycle < 3; cycle++)
        {
            bool transport = cycle == 1 ? !binary : binary;
            using var stream = new MemoryStream(); Check(doc.Save(stream, transport), "MINSERT save failed.");
            var raw = ArrayWire(stream.ToArray(), transport);
            Check(raw.Any(t => t.Code == 100 && Equals(t.Value, "AcDbMInsertBlock")), "Array exported as a single block subclass.");
            Equal(i.ColumnCount, (short)raw.Single(t => t.Code == 70).Value, "wire columns"); Equal(i.RowCount, (short)raw.Single(t => t.Code == 71).Value, "wire rows");
            Near(i.ColumnSpacing, (double)raw.Single(t => t.Code == 44).Value, "wire column spacing"); Near(i.RowSpacing, (double)raw.Single(t => t.Code == 45).Value, "wire row spacing");
            stream.Position = 0; doc = DxfDocument.Load(stream) ?? throw new InvalidOperationException("MINSERT reload failed.");
            var loaded = doc.Entities.Inserts.Single(); CheckArrayFields(i, loaded);
            Equal("P-101", loaded.Attributes.Single().Value, "MINSERT attributes lost");
            Equal("array metadata", (string)loaded.XData["ARRAY_TEST"].XDataRecord.Single().Value, "MINSERT XData lost");
            var copy = (Insert)loaded.Clone(); CheckArrayFields(loaded, copy); i = loaded;
        }
        i.ColumnCount = 1; i.RowCount = 1; i.ColumnSpacing = 0; i.RowSpacing = 0;
        using var singleton = new MemoryStream(); Check(doc.Save(singleton, binary), "Singleton save failed.");
        var single = ArrayWire(singleton.ToArray(), binary);
        Check(single.Any(t => t.Code == 100 && Equals(t.Value, "AcDbBlockReference")), "Singleton subclass not restored.");
        Check(!single.Any(t => t.Code is 70 or 71 or 44 or 45), "Default array fields were invented.");
    }

    private static void InsertArrayNested(DxfVersion version, bool binary)
    {
        Insert child = ArrayInsert(false); var block = new Block("Outer"); block.Entities.Add(child);
        var outer = new Insert(block) { RowCount = 2, ColumnCount = 2, RowSpacing = 100, ColumnSpacing = 200 };
        var doc = new DxfDocument(version); doc.Entities.Add(outer);
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Nested MINSERT save failed."); stream.Position = 0;
        doc = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Nested MINSERT load failed.");
        Insert loaded = doc.Entities.Inserts.Single(); CheckArrayFields(outer, loaded);
        Insert nested = loaded.Block.Entities.OfType<Insert>().Single(); CheckArrayFields(child, nested);
        var cells = loaded.Explode().OfType<Insert>().ToArray(); Equal(4, cells.Length, "Outer array explosion flattened wrong level");
        foreach (Insert cell in cells) { CheckArrayFields(child, cell); Equal(6, cell.Explode().Count, "Nested array multiplicity lost"); }
    }

    private static void InsertArrayUnits(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); doc.DrawingVariables.InsUnits = DrawingUnits.Millimeters;
        Insert i = ArrayInsert(false); i.Block.Record.Units = DrawingUnits.Centimeters; doc.Entities.Add(i);
        var expected = i.Explode().OfType<Line>().Select(l => (l.StartPoint, l.EndPoint)).ToArray();
        Vector3 grid = i.GetGridPosition(1, 2);
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Units MINSERT save failed."); stream.Position = 0;
        var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Units MINSERT load failed.");
        Insert after = loaded.Entities.Inserts.Single(); CheckArrayFields(i, after); ArrayNear(grid, after.GetGridPosition(1, 2), "Block units scaled grid spacing");
        var lines = after.Explode().OfType<Line>().ToArray();
        for (int c = 0; c < lines.Length; c++) { ArrayNear(expected[c].StartPoint, lines[c].StartPoint, "Unit-scaled start"); ArrayNear(expected[c].EndPoint, lines[c].EndPoint, "Unit-scaled end"); }
    }

    private static void InsertArrayExport(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); doc.DrawingVariables.InsUnits = DrawingUnits.Unitless;
        Insert i = ArrayInsert(false); i.Normal = new Vector3(1, 2, 3); doc.Entities.Add(i);
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Independent export failed.");
        string name = $"insert-array-{version}-{binary}";
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, name + ".dxf"), stream.ToArray());
        var cells = Enumerable.Range(0, i.InstanceCount).Select(c =>
        {
            Vector3 p = i.GetGridPosition(c / i.ColumnCount, c % i.ColumnCount);
            Line l = i.ExplodeCell(c / i.ColumnCount, c % i.ColumnCount).OfType<Line>().Single();
            return new { position = new[] { p.X, p.Y, p.Z }, start = new[] { l.StartPoint.X, l.StartPoint.Y, l.StartPoint.Z }, end = new[] { l.EndPoint.X, l.EndPoint.Y, l.EndPoint.Z } };
        });
        File.WriteAllText(Path.Combine(ArtifactDirectory, name + ".json"), JsonSerializer.Serialize(cells));
    }
}
