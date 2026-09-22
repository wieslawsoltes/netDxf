using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly List<object> Results = new();
    private static int failures;
    private static readonly string ArtifactDirectory = Path.GetFullPath(
        Environment.GetEnvironmentVariable("DXF_TEST_ARTIFACTS") ?? "artifacts/conformance");
    private static readonly string? TestFilter = Environment.GetEnvironmentVariable("DXF_TEST_FILTER");

    private static int Main()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Directory.CreateDirectory(ArtifactDirectory);
        foreach (DxfVersion version in SupportedVersions)
        {
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion capturedVersion = version;
                bool capturedBinary = binary;
                Run($"document/{version}/{(binary ? "binary" : "text")}",
                    () => DocumentRoundTrip(capturedVersion, capturedBinary));
            }
        }
        Run("binary/valid-sentinel-and-string", ValidBinarySentinel);
        RegisterEntityCloneQualificationTests();
        RunAdditionalTests();
        RegisterObservableCollectionInsertTests();
        RegisterMTextUnicodeChunkTests();
        RegisterEntityTextFramingTests();
        RegisterMTextOrientationOrderTests();
        RegisterEntityTextFilePreflightTests();
        RegisterMatrixIdentityReviewTests();
        RegisterArbitraryAxisReviewTests();
        RegisterVector4ComponentReviewTests();
        RegisterIdentityConsumerReviewTests();
        RegisterPolygonMeshConversionTests();
        RegisterPolygonMeshBezierTests();
        RegisterPolygonMeshAffineTests();
        RegisterPolyfaceConstructionTests();
        RegisterLegacyVertexAffineTests();
        RegisterPolylineExplosionTests();
        RegisterPolylineProjectionTests();
        RegisterSplinePolylineConversionTests();
        RegisterSplineFitInputTests();
        RegisterSplineActiveDomainTests();
        RegisterHatchEdgeInputTests();
        RegisterHatchBulgeExplosionTests();
        RegisterSplineAffineAtomicTests();
        RegisterSplineCountPolicyTests();
        RegisterEllipseSamplingTests();
        RegisterEllipseAffineSafetyTests();
        RegisterEllipseParameterEvaluationTests();
        RegisterEllipseIoParameterTests();
        RegisterConicSplineTests();
        RegisterSplineKnotInsertionTests();
        RegisterSplineSplitTests();
        RegisterSplineBezierTests();
        RegisterSplineDegreeElevationTests();
        RegisterSplineTrimTests();
        RegisterSplineParameterTests();
        RegisterRawLineGeometryTests();
        RegisterRawGeometryXDataScaleTests();
        RegisterRawPointGeometryTests();
        RegisterRawFace3DGeometryTests();
        RegisterRawQuadGeometryTests();
        RegisterQuadMutationTests();
        RegisterQuadNormalCallbackTests();
        RegisterRawInfiniteGeometryTests();
        RegisterRawEllipseGeometryTests();
        RegisterEllipseRawPlaneTests();
        RegisterRawLwPolylineGeometryTests();
        RegisterRawLwPolylineTopologyTests();
        RegisterRawLwPolylineReverseTests();
        RegisterPolylineWidthProxyTests();
        RegisterPolylineAffineSafetyTests();
        RegisterPolylineReverseAtomicTests();
        RegisterEllipseAxisProxyTests();
        RegisterEllipseMutationProxyTests();
        RegisterFace3DEdgeVisibilityTests();
        RegisterRawConicGeometryTests();
        RegisterRawConicPlaneTests();
        RegisterRawLineXDataScopeTests();
        RegisterTextGroupCodeNulTests();
        RunTextHexTests();
        RunTableXDataTests();
        RunThumbnailImageTests();
        RegisterMeshEdgeDiagnosticTests();
        RegisterVertexAffineReviewTests();
        RegisterMeshDecompositionTests();
        RegisterDirectionAssignmentTests();
        RegisterDirectionCachedNormalizationTests();
        RegisterInfiniteLineTransformTests();
        RunNamedObjectDatabaseTests();
        RunTypedContainerTests();
        RunGeoDataTests();
        RegisterMixedModuleIntegrationTests();
        RunDimensionStyleParityTests();
        RegisterDimLfacFidelityTests();
        RegisterDStyleContainerTests();
        RegisterDimensionTextBlockTests();
        RegisterDimensionTextLiteralTests();
        RegisterDimensionLabelScaleTests();
        RegisterDimensionAffixFidelityTests();
        RegisterDimensionCompositeOverrideTests();
        RegisterAlternateUnitModeTests();
        RegisterDimensionXDataPreservationTests();
        RegisterTableXDataPreservationTests();
        RunOutputSettingsTests();
        RunLayerFilterPointerTests();
        RunLayerIndexTests();
        RegisterStoredDimAssocTests();
        RegisterPolyline3DRecordTests();
        RegisterPolygonMeshRecordTests();
        RegisterPolyfaceRecordTests();
        RegisterRetainedRecordTargetRemovalTests();
        RegisterPolyline2DRecordTests();
        RegisterPolylineTopologyTests();
        RegisterPolyfaceGrammarTests();
        RunTypedObjectErasureTests();
        RunLightListTests();
        RunDataTableTests();
        RunSunTests();
        RegisterStoredSunStudyTests();
        RegisterVersionCompatibilityTests();
        RegisterPolyfaceVersionCompatibilityTests();
        RegisterPolyline2DVersionCompatibilityTests();
        RegisterOpaqueEntityTests();
        RegisterOpaqueEntityBoundaryTests();
        RunUcsBaseTests();
        RunSunStudyProducerRawTests();
        RegisterStoredTableTests();
        RegisterStoredTableContentTests();
        RegisterStoredTableGeometryTests();
        RegisterEditableTableGeometryTests();
        RegisterEditableTableContentTests();
        RegisterStoredCellStyleMapTests();
        RegisterCellStyleMapEditingTests();
        RegisterStoredTableLifecycleReviewTests();
        RegisterStoredTableFidelityTests();
        RegisterStoredTableNameSpellingTests();
        RegisterTableStyleTests();
        RegisterEditableTableStyleTests();
        RegisterStoredFieldTests();
        RegisterFifthMixedModuleTests();
        RegisterSixthMixedModuleTests();
        RegisterSeventhMixedModuleTests();
        RegisterEighthMixedModuleTests();
        RegisterNinthMixedModuleTests();
        RegisterTenthMixedModuleTests();
        RegisterEleventhMixedModuleTests();
        RegisterCompositeTableOwnershipTests();
        RegisterPrivateXRecordTests();
        RunSourceReferenceIdentityTests();
        RunSourceIdentityMetadataTests();
        RunNumericHandleTests();
        RegisterSectionTests();
        RegisterViewLiveSectionTests();
        RegisterSectionManagerTests();
        RegisterSectionManagerMembershipTests();
        RegisterSectionManagerLifecycleTests();
        RegisterSectionLifecycleTests();
        RunSectionSettingsTests();
        RegisterSectionProducerTests();
        RunAppIdXDataLifecycleTests();
        RegisterTransparencyStoredTests();
        File.WriteAllText(Path.Combine(ArtifactDirectory, "results.json"),
            JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
        if (Results.Count == 0)
        {
            Console.Error.WriteLine($"No conformance tests matched DXF_TEST_FILTER={TestFilter}.");
            return 1;
        }
        Console.WriteLine($"Conformance: {Results.Count - failures} passed; {failures} failed.");
        return failures == 0 ? 0 : 1;
    }

    private static readonly DxfVersion[] SupportedVersions =
    {
        DxfVersion.AutoCad2000, DxfVersion.AutoCad2004, DxfVersion.AutoCad2007,
        DxfVersion.AutoCad2010, DxfVersion.AutoCad2013, DxfVersion.AutoCad2018
    };

    // Each feature PR adds an independent test file and a registration here.
    static partial void RunAdditionalTests();

    private static void Run(string name, Action test)
    {
        if (!string.IsNullOrEmpty(TestFilter) && !name.StartsWith(TestFilter, StringComparison.Ordinal)) return;
        try
        {
            test();
            Results.Add(new { name, passed = true, error = (string?)null });
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception exception)
        {
            failures++;
            Results.Add(new { name, passed = false, error = exception.ToString() });
            Console.Error.WriteLine($"FAIL {name}: {exception}");
        }
    }

    private static void DocumentRoundTrip(DxfVersion version, bool binary)
    {
        var document = new DxfDocument(version);
        document.Entities.Add(new Line(new Vector3(-1.25, 2.5, 3.75), new Vector3(100, -20, 7)));
        document.Entities.Add(new Circle(new Vector3(4, 5, 6), 2.5));
        document.Entities.Add(new Arc(new Vector3(2, 3, 4), 7.5, 15, 270));
        document.Entities.Add(new Point(new Vector3(-8, 9, 10)));
        using var output = new MemoryStream();
        Check(document.Save(output, binary), "Save returned false.");
        Check(output.CanWrite, "Save closed a caller-owned stream.");
        byte[] bytes = output.ToArray();
        string path = Path.Combine(ArtifactDirectory, $"{version}-{(binary ? "binary" : "text")}.dxf");
        File.WriteAllBytes(path, bytes);
        using (var probe = new MemoryStream(bytes))
        {
            Equal(version, DxfDocument.CheckDxfFileVersion(probe, out bool detectedBinary), "version probe");
            Equal(binary, detectedBinary, "format probe");
        }
        using var input = new MemoryStream(bytes);
        DxfDocument loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("Load returned null.");
        Check(input.CanRead, "Load closed a caller-owned stream.");
        Equal(version, loaded.DrawingVariables.AcadVer, "loaded version");
        Line line = loaded.Entities.Lines.Single();
        Near(-1.25, line.StartPoint.X, "line start X");
        Near(2.5, line.StartPoint.Y, "line start Y");
        Near(3.75, line.StartPoint.Z, "line start Z");
        Near(100, line.EndPoint.X, "line end X");
        Near(-20, line.EndPoint.Y, "line end Y");
        Near(7, line.EndPoint.Z, "line end Z");
        Circle circle = loaded.Entities.Circles.Single();
        Near(2.5, circle.Radius, "circle radius");
        Near(6, circle.Center.Z, "circle center Z");
        Arc arc = loaded.Entities.Arcs.Single();
        Near(7.5, arc.Radius, "arc radius");
        Near(15, arc.StartAngle, "arc start angle");
        Near(270, arc.EndAngle, "arc end angle");
        Near(-8, loaded.Entities.Points.Single().Position.X, "point X");
    }

    private static readonly byte[] BinarySentinel = Encoding.ASCII.GetBytes("AutoCAD Binary DXF\r\n\u001a\0");

    private static void ValidBinarySentinel()
    {
        using var stream = new MemoryStream();
        stream.Write(BinarySentinel);
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write((short)0);
            writer.Write(Encoding.UTF8.GetBytes("EOF\0"));
        }
        stream.Position = 0;
        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        object codeReader = CreateBinaryReader(reader);
        Invoke(codeReader, "Next");
        Equal("EOF", (string)Invoke(codeReader, "ReadString")!, "binary string");
    }

    // Reflection tests the actual signed library rather than compiling duplicate production sources.
    private static object CreateBinaryReader(BinaryReader reader)
    {
        Type type = typeof(DxfDocument).Assembly.GetType("netDxf.IO.BinaryCodeValueReader", true)!;
        try
        {
            return Activator.CreateInstance(type, reader, Encoding.UTF8)!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static object? Invoke(object instance, string name, params object[] arguments)
    {
        MethodInfo method = instance.GetType().GetMethod(name) ?? throw new MissingMethodException(name);
        try { return method.Invoke(instance, arguments); }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}.");
    }

    private static void Near(double expected, double actual, string message)
    {
        if (!double.IsFinite(actual) || Math.Abs(expected - actual) > 1e-10 * Math.Max(1, Math.Abs(expected)))
            throw new InvalidOperationException($"{message}: expected {expected.ToString("R", CultureInfo.InvariantCulture)}, actual {actual.ToString("R", CultureInfo.InvariantCulture)}.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
