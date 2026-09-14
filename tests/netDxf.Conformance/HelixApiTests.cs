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
    private static void RegisterHelixApiTests()
    {
        Run("helix/api/defaults-clone-conversion", HelixApiClone);
        Run("helix/api/finite-validation", HelixApiValidation);
        foreach (int operation in Enumerable.Range(0, 6))
        {
            int op = operation;
            Run($"helix/api/transform/{op}", () => HelixTransform(op));
        }
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                foreach (int placement in Enumerable.Range(0, 4))
                {
                    DxfVersion v = version; bool b = binary; int p = placement;
                    Run($"helix/api/profile/{v}/{b}/{p}", () => HelixExportProfile(v, b, p));
                }
        foreach (bool binary in new[] { false, true })
        {
            bool b = binary;
            foreach (int failure in Enumerable.Range(0, 10))
            {
                int f = failure;
                Run($"helix/api/invalid/{b}/{f}", () => HelixInvalid(b, f));
            }
            Run($"helix/api/absent-parameters/{b}", () => HelixAbsentParameters(b));
            Run($"helix/api/class-conflict/{b}", () => HelixClassConflict(b));
            Run($"helix/api/class-preservation/{b}", () => HelixClassPreservation(b));
        }
    }

    private static Helix NewHelixFixture()
    {
        using var input = new MemoryStream(RawFixtureBytes(HelixWireTags(DxfVersion.AutoCad2018, 1, true, false), false));
        return (Helix)(DxfDocument.Load(input) ?? throw new InvalidOperationException("HELIX fixture rejected.")).Entities.Helices.Single().Clone();
    }

    private static void HelixApiClone()
    {
        Throws<ArgumentNullException>(() => new Helix(null!));
        var curve = new Spline(HelixWireControls, new[] { 1.0, 1.25, 1.5, 1.75 }, 3);
        var authored = new Helix(curve);
        Equal(29, authored.MajorReleaseNumber, "Default major release"); Equal(63, authored.MaintenanceReleaseNumber, "Default maintenance");
        Equal(Vector3.Zero, authored.AxisBasePoint, "Default axis base"); Equal(Vector3.UnitX, authored.StartPoint, "Default start");
        Equal(Vector3.UnitZ, authored.AxisVector, "Default axis"); Equal(1.0, authored.Radius, "Default radius");
        Equal(1.0, authored.Turns, "Default turns"); Equal(1.0, authored.TurnHeight, "Default turn height");
        Equal(HelixConstraint.TurnHeight, authored.Constraint, "Default constraint"); Check(authored.IsRightHanded, "Default handedness");
        authored.ControlPoints[0] = Vector3.Zero;
        Equal(HelixWireControls[0], curve.ControlPoints[0], "HELIX constructor aliases source controls");
        var source = NewHelixFixture(); var copy = (Helix)source.Clone(); var spline = source.ToSpline();
        Equal(EntityType.Helix, copy.Type, "Clone entity type"); Equal(EntityType.Spline, spline.Type, "Downgrade entity type");
        Equal("SPLINE", spline.CodeName, "ToSpline retained HELIX identity");
        Check(copy.Handle == null && copy.Owner == null && spline.Handle == null, "Copy retained database identity.");
        Check(source.ControlPoints.SequenceEqual(spline.ControlPoints) && source.Weights.SequenceEqual(spline.Weights), "ToSpline refitted geometry.");
        Equal(source.AxisBasePoint, copy.AxisBasePoint, "Clone axis base"); Equal(source.AxisVector, copy.AxisVector, "Clone axis magnitude");
        Equal(source.StartPoint, copy.StartPoint, "Clone start point"); Equal(source.TurnHeight, copy.TurnHeight, "Clone signed pitch");
        copy.ControlPoints[0] = Vector3.Zero; copy.Weights[0] = 5; copy.Knots[0] = -1;
        copy.XData["HELIX_TEST"].XDataRecord.Clear(); copy.Radius = 19;
        Check(source.ControlPoints.SequenceEqual(HelixWireControls), "Clone aliases controls.");
        Equal(1.0, source.Weights[0], "Clone aliases weights"); Equal(0.0, source.Knots[0], "Clone aliases knots");
        Equal(1, source.XData["HELIX_TEST"].XDataRecord.Count, "Clone aliases XData"); Equal(2.75, source.Radius, "Clone aliases metadata");
        var block = new Block("HelixClone"); block.Entities.Add((Helix)source.Clone());
        var insert = new Insert(block); var clonedInsert = (Insert)insert.Clone();
        clonedInsert.Block.Entities.OfType<Helix>().Single().ControlPoints[0] = Vector3.Zero;
        Equal(HelixWireControls[0], block.Entities.OfType<Helix>().Single().ControlPoints[0], "Nested clone aliases controls");
        var doc = new DxfDocument(DxfVersion.AutoCad2018); doc.Entities.Add((Helix)source.Clone());
        Equal(1, doc.Entities.Helices.Count(), "Typed collection"); Equal(1, doc.Entities.Splines.Count(), "Inherited spline collection");
        Check(doc.Entities.Remove(doc.Entities.Helices.Single()), "HELIX remove failed."); Equal(0, doc.Entities.All.Count(), "HELIX remove left entity");
    }

    private static void HelixApiValidation()
    {
        var h = NewHelixFixture();
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Throws<ArgumentOutOfRangeException>(() => h.Radius = invalid); Throws<ArgumentOutOfRangeException>(() => h.Turns = invalid);
            Throws<ArgumentOutOfRangeException>(() => h.TurnHeight = invalid);
            foreach (Vector3 p in new[] { new Vector3(invalid, 0, 0), new Vector3(0, invalid, 0), new Vector3(0, 0, invalid) })
            {
                Throws<ArgumentOutOfRangeException>(() => h.AxisVector = p); Throws<ArgumentOutOfRangeException>(() => h.AxisBasePoint = p);
                Throws<ArgumentOutOfRangeException>(() => h.StartPoint = p);
            }
        }
        Throws<ArgumentOutOfRangeException>(() => h.AxisVector = Vector3.Zero);
        Throws<ArgumentOutOfRangeException>(() => h.Radius = -1); Throws<ArgumentOutOfRangeException>(() => h.Turns = 0);
        Throws<ArgumentOutOfRangeException>(() => h.MajorReleaseNumber = -1); Throws<ArgumentOutOfRangeException>(() => h.MaintenanceReleaseNumber = -1);
        Throws<ArgumentOutOfRangeException>(() => h.Constraint = (HelixConstraint)3);
        Equal(2.75, h.Radius, "Failed setter mutated radius"); Equal(3.125, h.Turns, "Failed setter mutated turns");
        Equal(new Vector3(0, 0, 2), h.AxisVector, "Failed setter mutated axis");
        h.Radius = 0; h.TurnHeight = 0; h.Turns = 1000; h.AxisVector = new Vector3(0, 0, 9);
        Equal(9.0, h.AxisVector.Z, "Setter normalized magnitude");
        Check(h.ControlPoints.SequenceEqual(HelixWireControls), "Editing parameters refitted control polygon.");
    }

    private static void HelixTransform(int operation)
    {
        var h = NewHelixFixture(); var original = (Helix)h.Clone();
        Matrix3 matrix = operation switch
        {
            0 => Matrix3.Identity,
            1 => Matrix3.RotationX(0.6) * Matrix3.RotationZ(-0.4),
            2 => Matrix3.RotationY(0.3) * Matrix3.Scale(2),
            3 => Matrix3.Reflection(Vector3.UnitX) * Matrix3.Scale(3),
            4 => Matrix3.Scale(2, 3, 4),
            _ => Matrix3.Scale(0)
        };
        Vector3 translation = new(101, -33, 77);
        if (operation >= 4)
        {
            Throws<NotSupportedException>(() => h.TransformBy(matrix, translation));
            Check(h.ControlPoints.SequenceEqual(original.ControlPoints), "Rejected affine transform mutated controls.");
            Equal(original.AxisBasePoint, h.AxisBasePoint, "Rejected transform mutated parameters");
            var block = new Block("HelixAffine"); block.Entities.Add(h);
            // Nonzero nonuniform INSERT transforms explicitly degrade to stored SPLINE geometry.
            if (operation == 4)
            {
                var insert = new Insert(block, translation) { Scale = new Vector3(2, 3, 4) };
                var curve = insert.Explode().OfType<Spline>().Single();
                Check(curve is not Helix && curve.CodeName == "SPLINE", "Affine explosion retained false HELIX parameters.");
                for (int i = 0; i < curve.ControlPoints.Length; ++i)
                    NearFitVector(matrix * original.ControlPoints[i] + translation, curve.ControlPoints[i], "Affine exploded curve");
            }
            return;
        }
        double scale = operation == 2 ? 2 : operation == 3 ? 3 : 1;
        h.TransformBy(matrix, translation);
        NearFitVector(matrix * original.AxisBasePoint + translation, h.AxisBasePoint, "Axis base transform");
        NearFitVector(matrix * original.StartPoint + translation, h.StartPoint, "Start point transform");
        NearFitVector(matrix * original.AxisVector, h.AxisVector, "Axis vector transform");
        for (int i = 0; i < h.ControlPoints.Length; ++i) NearFitVector(matrix * original.ControlPoints[i] + translation, h.ControlPoints[i], "Spline control transform");
        NearFitVector(matrix * original.StartTangent!.Value, h.StartTangent!.Value, "Spline tangent transform");
        Near(original.Radius * scale, h.Radius, "Radius scale"); Near(original.TurnHeight * scale, h.TurnHeight, "Pitch scale");
        Equal(operation != 3, h.IsRightHanded, "Reflection handedness"); Equal(original.Turns, h.Turns, "Transform changed turn count");
        var doc = new DxfDocument(DxfVersion.AutoCad2018); doc.Entities.Add(h);
        using var stream = new MemoryStream(); Check(doc.Save(stream), "Transformed HELIX save failed."); stream.Position = 0;
        var loaded = DxfDocument.Load(stream)!.Entities.Helices.Single(); NearFitVector(h.Normal, loaded.Normal, "Inherited HELIX normal");
    }

    private static void HelixExportProfile(DxfVersion version, bool binary, int placement)
    {
        var doc = new DxfDocument(version); var h = (Helix)NewHelixFixture().Clone();
        switch (placement)
        {
            case 0: doc.Entities.Add(h); break;
            case 1: doc.Layouts.Add(new Layout("HelixPaper")); doc.Entities.ActiveLayout = "HelixPaper"; doc.Entities.Add(h); doc.Entities.ActiveLayout = "Model"; break;
            case 2:
                var inner = new Block("HelixInner"); inner.Entities.Add(h);
                var outer = new Block("HelixOuter"); outer.Entities.Add(new Insert(inner)); doc.Entities.Add(new Insert(outer)); break;
            default: var unused = new Block("HelixUnused"); unused.Entities.Add(h); doc.Blocks.Add(unused); break;
        }
        using var stream = new MemoryStream(); byte[] bytes = { 4, 5, 6 }; stream.Write(bytes); stream.Position = 1;
        var handle = h.Handle; var seed = doc.DrawingVariables.HandleSeed; int apps = doc.ApplicationRegistries.Count, layouts = doc.Layouts.Count;
        if (version < DxfVersion.AutoCad2007)
        {
#if DEBUG
            Throws<NotSupportedException>(() => doc.Save(stream, binary));
#else
            Check(!doc.Save(stream, binary), "Older profile emitted HELIX.");
#endif
            Check(bytes.SequenceEqual(stream.ToArray()), "HELIX preflight wrote bytes."); Equal(1L, stream.Position, "HELIX preflight advanced stream");
            Equal(handle, h.Handle, "HELIX preflight changed identity"); Equal(seed, doc.DrawingVariables.HandleSeed, "HELIX preflight allocated handles");
            Equal(apps, doc.ApplicationRegistries.Count, "HELIX preflight registered APPIDs"); Equal(layouts, doc.Layouts.Count, "HELIX preflight added layout");
        }
        else
        {
            stream.SetLength(0); stream.Position = 0; Check(doc.Save(stream, binary), "Supported HELIX save failed.");
            stream.Position = 0; var loaded = DxfDocument.Load(stream)!;
            var actual = loaded.Blocks.SelectMany(b => b.Entities).OfType<Helix>().Single();
            Equal(h.AxisVector, actual.AxisVector, "Block HELIX axis"); Check(h.ControlPoints.SequenceEqual(actual.ControlPoints), "Block HELIX controls");
            Equal(1, loaded.Classes["HELIX"].InstanceCount, "HELIX CLASS instance count");
            Check(!doc.Classes.Contains("HELIX"), "Writer attached synthesized class to caller collection.");
        }
    }

    private static void HelixInvalid(bool binary, int failure)
    {
        var tags = HelixWireTags(DxfVersion.AutoCad2018, 1, true, false);
        int marker = tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbHelix"));
        int Find(short code) => tags.FindIndex(marker + 1, t => t.Code == code);
        switch (failure)
        {
            case 0: tags.RemoveAt(marker); break;
            case 1: tags.Insert(Find(40), new(40, 1.0)); break;
            case 2: tags.RemoveAt(Find(22)); break;
            case 3: tags[Find(32)] = new(32, 0.0); break;
            case 4: tags[Find(280)] = new(280, (short)3); break;
            case 5: tags[Find(90)] = new(90, -1); break;
            case 6: tags[Find(41)] = new(41, 0.0); break;
            case 7: tags[Find(40)] = new(40, -1.0); break;
            case 8: tags.Insert(marker + 1, new(100, "AcDbHelix")); break;
            default: tags.RemoveRange(marker, tags.Count - marker); break;
        }
        using var stream = new MemoryStream(RawFixtureBytes(tags, binary));
#if DEBUG
        try { DxfDocument.Load(stream); throw new InvalidOperationException("Invalid HELIX accepted."); }
        catch (Exception error) when (error is InvalidDataException || error is EndOfStreamException) { }
#else
        Check(DxfDocument.Load(stream) == null, "Invalid HELIX accepted.");
#endif
        Check(stream.CanRead, "Malformed HELIX closed caller stream.");
    }

    private static void HelixAbsentParameters(bool binary)
    {
        var tags = HelixWireTags(DxfVersion.AutoCad2018, 1, true, false);
        int marker = tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbHelix"));
        tags.RemoveRange(marker + 1, tags.FindIndex(t => t.Code == 1001) - marker - 1);
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var h = DxfDocument.Load(input)!.Entities.Helices.Single();
        Equal(1.0, h.Radius, "Absent radius default"); Equal(Vector3.UnitZ, h.AxisVector, "Absent axis default");
        Equal("after helix", (string)h.XData["HELIX_TEST"].XDataRecord.Single().Value, "Absent parameter XData");
    }

    private static void HelixClassConflict(bool binary)
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2018); doc.Entities.Add((Helix)NewHelixFixture().Clone());
        doc.Classes.Add(new DxfClass("HELIX", "WrongClass", "Other"));
        using var output = new MemoryStream();
#if DEBUG
        Throws<InvalidDataException>(() => doc.Save(output, binary));
#else
        Check(!doc.Save(output, binary), "Conflicting HELIX class accepted.");
#endif
        Equal(0L, output.Length, "Class conflict wrote bytes");
    }

    private static void HelixClassPreservation(bool binary)
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2018); doc.Entities.Add((Helix)NewHelixFixture().Clone());
        doc.Classes.Add(new DxfClass("HELIX", "AcDbHelix", "Authored application") { IsEntity = true, ProxyFlags = 17, InstanceCount = 77 });
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Compatible HELIX class rejected."); output.Position = 0;
        var definition = DxfDocument.Load(output)!.Classes["HELIX"];
        Equal("Authored application", definition.ApplicationName, "Authored class application"); Equal(17, definition.ProxyFlags, "Authored proxy flags");
        Equal(1, definition.InstanceCount, "Recomputed class count"); Equal(77, doc.Classes["HELIX"].InstanceCount, "Save mutated caller class");
    }
}
