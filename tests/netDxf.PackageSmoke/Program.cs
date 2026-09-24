using System.Text;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;
using netDxf.Units;

#if !NETFRAMEWORK
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
// The predecessor of this fixed positive finite value is available on every target.
double nearTurn = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(360.0) - 1);
#if NET6_0_OR_GREATER
if (nearTurn != Math.BitDecrement(360.0)) throw new InvalidOperationException("Portable endpoint fixture differs from BitDecrement");
#endif
// These assertions run against each selected installed package assembly.
netDxf.GTE.GVector nullVector = null!;
var geometryVector = new netDxf.GTE.GVector(new[] { 3.0, 4.0 });
if (!(nullVector == (netDxf.GTE.GVector)null!) || geometryVector == nullVector || !(geometryVector != nullVector))
    throw new InvalidOperationException("GVector null equality failed");
var doubledVector = 2 * geometryVector;
if (doubledVector[0] != 6 || doubledVector[1] != 8 || netDxf.GTE.GVector.Dot(geometryVector, geometryVector) != 25)
    throw new InvalidOperationException("GVector arithmetic failed");
foreach (double scale in new[] { double.Epsilon, 1.0, 1e300 })
{
    var vector = new netDxf.GTE.GVector(new[] { 3 * scale, 4 * scale });
    double length = netDxf.GTE.GVector.Normalize(ref vector, true);
    if (double.IsNaN(length) || double.IsInfinity(length) || length <= 0
        || Math.Abs(vector[0] - .6) > 2e-15 || Math.Abs(vector[1] - .8) > 2e-15)
        throw new InvalidOperationException("GVector robust normalization failed");
}
int count = 0;
foreach (var version in new[] { DxfVersion.AutoCad2000, DxfVersion.AutoCad2004, DxfVersion.AutoCad2007,
    DxfVersion.AutoCad2010, DxfVersion.AutoCad2013, DxfVersion.AutoCad2018 })
foreach (bool binary in new[] { false, true })
{
    var doc = new DxfDocument(version) { BuildDimensionBlocks = true };
    var style = new DimensionStyle("PACKAGE_DIM") { DimPrefix = "S:", DimSuffix = ":END" };
    style.AlternateUnits.LengthUnits = LinearUnitType.WindowsDesktop;
    style.TextFillColor = new AciColor(2);
    style.DimArrow1 = new Block("PACKAGE_ARROW", new EntityObject[] { new Line(Vector2.Zero, Vector2.UnitX) });
    style.DimArrow2 = style.DimArrow1;
    style.LeaderArrow = style.DimArrow1;
    var dim = new AlignedDimension(Vector2.Zero, new Vector2(10, 0), 3, style) { UserText = " " };
    dim.StyleOverrides.Add(DimensionStyleOverrideType.DimPrefix, "");
    dim.StyleOverrides.Add(DimensionStyleOverrideType.DimSuffix, "");
    foreach (var reset in new[] { DimensionStyleOverrideType.DimArrow1, DimensionStyleOverrideType.DimArrow2,
        DimensionStyleOverrideType.LeaderArrow, DimensionStyleOverrideType.TextFillColor })
        dim.StyleOverrides.Add(reset, null);
    doc.Entities.Add(dim);
    doc.Entities.Add(new Line(new Vector3(1, 2, 3), new Vector3(4, 5, 6)));
    // Direct edits must not serialize display bytes from the old circular geometry.
    byte[] circularProxy = { 1, 3, 7, 255 };
    var circle = new Circle(new Vector3(2, 3, 4), 2) { ProxyGraphics = circularProxy };
    circle.Radius = 2;
    if (!circle.ProxyGraphics.SequenceEqual(circularProxy))
        throw new InvalidOperationException("Identical circular assignment discarded the proxy");
    circle.Radius = 3;
    var arc = new Arc(new Vector3(4, 5, 6), 4, 30, 210) { ProxyGraphics = circularProxy };
    arc.StartAngle = 1e-13;
    arc.EndAngle = nearTurn;
    if (circle.ProxyGraphics != null || arc.ProxyGraphics != null)
        throw new InvalidOperationException("Circular edit retained stale proxy graphics");
    doc.Entities.Add(circle);
    doc.Entities.Add(arc);
    using var stream = new MemoryStream();
    if (!doc.Save(stream, binary)) throw new InvalidOperationException("Package save failed");
    stream.Position = 0;
    var copy = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Package load failed");
    if (copy.Entities.Circles.Single().Radius != 3 || copy.Entities.Arcs.Single().StartAngle != 1e-13
        || copy.Entities.Arcs.Single().EndAngle != nearTurn
        || copy.Entities.Circles.Single().ProxyGraphics != null || copy.Entities.Arcs.Single().ProxyGraphics != null)
        throw new InvalidOperationException("Installed circular edit round trip failed");
    var dimension = copy.Entities.Dimensions.Single();
    dimension.Update();
    if (copy.DrawingVariables.AcadVer != version || dimension.UserText != " " || dimension.Block.Entities.OfType<MText>().Any()
        || dimension.Style.AlternateUnits.LengthUnits != LinearUnitType.WindowsDesktop
        || (string)dimension.StyleOverrides[DimensionStyleOverrideType.DimPrefix].Value != ""
        || (string)dimension.StyleOverrides[DimensionStyleOverrideType.DimSuffix].Value != ""
        || dimension.Style.TextFillColor.Index != 2
        || dimension.StyleOverrides[DimensionStyleOverrideType.DimArrow1].Value != null
        || dimension.StyleOverrides[DimensionStyleOverrideType.DimArrow2].Value != null
        || dimension.StyleOverrides[DimensionStyleOverrideType.LeaderArrow].Value != null
        || dimension.StyleOverrides[DimensionStyleOverrideType.TextFillColor].Value != null
        || copy.Entities.Lines.Single().EndPoint != new Vector3(4, 5, 6)
        || copy.Objects.Validate().Count != 0 || !stream.CanRead)
        throw new InvalidOperationException($"Package round trip failed: {version}/{binary}");
    // Exercise the installed package's tolerance renderer and coupled wire settings.
    dimension.UserText = "<>";
    dimension.Style.DimLengthUnits = LinearUnitType.Decimal;
    dimension.Style.LengthPrecision = 2;
    dimension.Style.TextFractionHeightScale = 0.5;
    dimension.Style.Tolerances.DisplayMethod = DimensionStyleTolerancesDisplayMethod.Symmetrical;
    dimension.Style.Tolerances.UpperLimit = 0.25;
    dimension.Style.Tolerances.LowerLimit = 99; // Inactive for symmetric tolerances.
    dimension.Style.Tolerances.Precision = 3;
    dimension.Update();
    if (!dimension.Block.Entities.OfType<MText>().Single().Value.Contains("±0.250"))
        throw new InvalidOperationException("Installed tolerance generation failed");
    using var toleranceStream = new MemoryStream();
    if (!copy.Save(toleranceStream, binary)) throw new InvalidOperationException("Tolerance package save failed");
    toleranceStream.Position = 0;
    var toleranceCopy = DxfDocument.Load(toleranceStream) ?? throw new InvalidOperationException("Tolerance package load failed");
    var toleranceDimension = toleranceCopy.Entities.Dimensions.Single();
    toleranceDimension.Update();
    if (toleranceDimension.Style.Tolerances.DisplayMethod != DimensionStyleTolerancesDisplayMethod.Symmetrical
        || toleranceDimension.Style.Tolerances.LowerLimit != 0.25
        || !toleranceDimension.Block.Entities.OfType<MText>().Single().Value.Contains("±0.250"))
        throw new InvalidOperationException("Installed symmetric tolerance round trip failed");
    // Exercise fixed extensions from the installed package, including unrelated overrides.
    dimension.Style.ExtLineFixed = true;
    dimension.Style.ExtLineFixedLength = 1;
    dimension.Style.ExtLineOffset = .5;
    dimension.Style.ExtLineExtend = .25;
    dimension.Style.ExtLine1Linetype = new Linetype("SMOKE_EXT1");
    dimension.Style.ExtLine2Linetype = new Linetype("SMOKE_EXT2");
    dimension.Update();
    var extensionLines = dimension.Block.Entities.OfType<Line>().Where(l => l.Linetype.Name.StartsWith("SMOKE_EXT")).ToArray();
    if (extensionLines.Length != 2 || extensionLines.Any(l => Math.Abs(l.StartPoint.Y - 2) > 1e-9 || Math.Abs(l.EndPoint.Y - 3.25) > 1e-9))
        throw new InvalidOperationException("Installed fixed extension geometry failed");
    // Fraction delimiters inside tolerance rows must not become outer Unicode escapes.
    dimension.Style.DimLengthUnits = LinearUnitType.Fractional;
    dimension.Style.SuppressZeroFeet = false;
    dimension.Style.SuppressZeroInches = false;
    dimension.Style.Tolerances.DisplayMethod = DimensionStyleTolerancesDisplayMethod.Deviation;
    dimension.Style.Tolerances.UpperLimit = .5;
    dimension.Style.Tolerances.LowerLimit = .25;
    dimension.Style.Tolerances.SuppressZeroFeet = false;
    dimension.Style.Tolerances.SuppressZeroInches = false;
    dimension.Update();
    const string fractionRows = @"\S+0 1\/2^ -0 1\/4;";
    if (!dimension.Block.Entities.OfType<MText>().Single().Value.Contains(fractionRows))
        throw new InvalidOperationException("Installed fractional tolerance escaping failed");
    using var fractionStream = new MemoryStream();
    if (!copy.Save(fractionStream, binary)) throw new InvalidOperationException("Fraction package save failed");
    fractionStream.Position = 0;
    var fractionCopy = DxfDocument.Load(fractionStream) ?? throw new InvalidOperationException("Fraction package load failed");
    var fraction = fractionCopy.Entities.Dimensions.Single();
    fraction.Update();
    if (!fraction.Block.Entities.OfType<MText>().Single().Value.Contains(fractionRows))
        throw new InvalidOperationException("Installed fractional tolerance round trip failed");
    // Verify the installed renderer, not only an in-tree test assembly: allowances
    // are selected-unit scalars and fraction rows must survive typed reloading.
    var semanticDoc = new DxfDocument(version) { BuildDimensionBlocks = true };
    var angularStyle = new DimensionStyle("SMOKE_ANGULAR_UNITS")
    {
        DimAngularUnits = AngleUnitType.Radians, AngularPrecision = 2, TextFractionHeightScale = .5,
        Tolerances = new DimensionStyleTolerances {
            DisplayMethod = DimensionStyleTolerancesDisplayMethod.Limits, UpperLimit = .25, LowerLimit = .125, Precision = 3 }
    };
    var angular = new Angular2LineDimension(Vector2.Zero, Vector2.UnitX, Vector2.Zero, Vector2.UnitY, 3, angularStyle)
        { UserText = "<>TAIL" };
    var fractionStyle = new DimensionStyle("SMOKE_STACK_ROWS")
    {
        DimLengthUnits = LinearUnitType.Fractional, LengthPrecision = 2, FractionType = FractionFormatType.NotStacked,
        TextFractionHeightScale = .5, Tolerances = new DimensionStyleTolerances {
            DisplayMethod = DimensionStyleTolerancesDisplayMethod.Deviation, UpperLimit = .5, LowerLimit = .25, Precision = 2 }
    };
    var fractional = new AlignedDimension(Vector2.Zero, new Vector2(10.5, 0), 3, fractionStyle) { UserText = "<>TAIL" };
    semanticDoc.Entities.Add(angular); semanticDoc.Entities.Add(fractional);
    const string angularExpected = @"{\H0.5x;\S1.821r^ 1.446r;}TAIL";
    const string fractionExpected = @"{\A1;10 1/2{\H0.5x;\S+0 1\/2^ -0 1\/4;}}TAIL";
    for (int generation = 0; generation < 2; generation++)
    {
        foreach (var host in semanticDoc.Entities.Dimensions) host.Update();
        if (semanticDoc.Entities.Dimensions.OfType<Angular2LineDimension>().Single().Block.Entities.OfType<MText>().Single().Value != angularExpected
            || semanticDoc.Entities.Dimensions.OfType<AlignedDimension>().Single().Block.Entities.OfType<MText>().Single().Value != fractionExpected)
            throw new InvalidOperationException("Installed tolerance units/stack grammar failed");
        using var semanticStream = new MemoryStream();
        if (!semanticDoc.Save(semanticStream, binary)) throw new InvalidOperationException("Semantic package save failed");
        semanticStream.Position = 0;
        semanticDoc = DxfDocument.Load(semanticStream) ?? throw new InvalidOperationException("Semantic package reload failed");
    }
    // Shared by ordinary package consumption and all eight exact-asset runtime profiles.
    var primitiveLine = new Line(Vector3.Zero, Vector3.UnitX);
    var primitivePoint = new netDxf.Entities.Point(Vector3.Zero);
    var primitiveRay = new Ray(Vector3.Zero, Vector3.UnitX);
    var primitiveXline = new XLine(Vector3.Zero, Vector3.UnitX);
    EntityObject[] primitiveHosts = { primitiveLine, primitiveLine, primitiveLine,
        primitivePoint, primitivePoint, primitivePoint, primitiveRay, primitiveRay, primitiveXline, primitiveXline };
    Action[] primitiveNoOps = {
        () => primitiveLine.StartPoint = primitiveLine.StartPoint, () => primitiveLine.EndPoint = primitiveLine.EndPoint,
        () => primitiveLine.Thickness = primitiveLine.Thickness, () => primitivePoint.Position = primitivePoint.Position,
        () => primitivePoint.Thickness = primitivePoint.Thickness, () => primitivePoint.Rotation = primitivePoint.Rotation + 360,
        () => primitiveRay.Origin = primitiveRay.Origin, () => primitiveRay.Direction = new Vector3(8,0,0),
        () => primitiveXline.Origin = primitiveXline.Origin, () => primitiveXline.Direction = new Vector3(8,0,0) };
    Action[] primitiveEdits = {
        () => primitiveLine.StartPoint = new Vector3(2,3,4), () => primitiveLine.EndPoint = new Vector3(5,6,7),
        () => primitiveLine.Thickness = 2, () => primitivePoint.Position = new Vector3(8,9,10),
        () => primitivePoint.Thickness = -2, () => primitivePoint.Rotation = 90,
        () => primitiveRay.Origin = new Vector3(11,12,13), () => primitiveRay.Direction = Vector3.UnitY,
        () => primitiveXline.Origin = new Vector3(14,15,16), () => primitiveXline.Direction = Vector3.UnitY };
    for (int field = 0; field < primitiveHosts.Length; field++)
    {
        primitiveHosts[field].ProxyGraphics = new byte[] { 1,3,7,255 };
        primitiveNoOps[field]();
        if (!(primitiveHosts[field].ProxyGraphics ?? Array.Empty<byte>()).SequenceEqual(new byte[] { 1,3,7,255 }))
            throw new InvalidOperationException("Installed primitive no-op changed proxy");
        primitiveEdits[field]();
        if (primitiveHosts[field].ProxyGraphics != null)
            throw new InvalidOperationException("Installed primitive edit retained stale proxy");
    }
    var primitiveDoc = new DxfDocument(version);
    primitiveDoc.Entities.Add(primitiveLine); primitiveDoc.Entities.Add(primitivePoint);
    primitiveDoc.Entities.Add(primitiveRay); primitiveDoc.Entities.Add(primitiveXline);
    using var primitiveStream = new MemoryStream();
    if (!primitiveDoc.Save(primitiveStream, binary)) throw new InvalidOperationException("Primitive package save failed");
    primitiveStream.Position = 0;
    var primitiveCopy = DxfDocument.Load(primitiveStream) ?? throw new InvalidOperationException("Primitive package load failed");
    if (primitiveCopy.Entities.Lines.Single().ProxyGraphics != null || primitiveCopy.Entities.Points.Single().ProxyGraphics != null
        || primitiveCopy.Entities.Rays.Single().ProxyGraphics != null || primitiveCopy.Entities.XLines.Single().ProxyGraphics != null)
        throw new InvalidOperationException("Installed primitive round trip restored stale graphics");
    var affinePoint = new netDxf.Entities.Point(new Vector3(1,2,3)) { Thickness = -2, Rotation = 30 };
    affinePoint.ProxyGraphics = new byte[] { 1,3,7,255 };
    affinePoint.TransformBy(Matrix3.Scale(2,3,4), Vector3.Zero);
    if (affinePoint.Position != new Vector3(2,6,12) || affinePoint.Normal != Vector3.UnitZ
        || affinePoint.Thickness != -8 || affinePoint.ProxyGraphics != null)
        throw new InvalidOperationException("Installed POINT signed extrusion transform failed");
    affinePoint.ProxyGraphics = new byte[] { 1,3,7,255 };
    var projective = Matrix4.Identity; projective.M44 = 2;
    bool pointRejected = false;
    try { affinePoint.TransformBy(projective); } catch (NotSupportedException) { pointRejected = true; }
    if (!pointRejected || affinePoint.Position != new Vector3(2,6,12) || affinePoint.Thickness != -8
        || !(affinePoint.ProxyGraphics ?? Array.Empty<byte>()).SequenceEqual(new byte[] { 1,3,7,255 }))
        throw new InvalidOperationException("Installed POINT rejection changed geometry");
    var pointDoc = new DxfDocument(version); pointDoc.Entities.Add(affinePoint);
    using var pointStream = new MemoryStream();
    if (!pointDoc.Save(pointStream,binary)) throw new InvalidOperationException("POINT package save failed");
    pointStream.Position = 0;
    var pointCopy = DxfDocument.Load(pointStream) ?? throw new InvalidOperationException("POINT package reload failed");
    if (pointCopy.Entities.Points.Single().Position != new Vector3(2,6,12)
        || pointCopy.Entities.Points.Single().Thickness != -8)
        throw new InvalidOperationException("Installed POINT extrusion round trip failed");
    // Switching from a symmetric base must recover its inactive lower allowance.
    var transitionStyle = new DimensionStyle("PACKAGE_TRANSITION") { TextFractionHeightScale = .5 };
    transitionStyle.Tolerances.DisplayMethod = DimensionStyleTolerancesDisplayMethod.Symmetrical;
    transitionStyle.Tolerances.UpperLimit = .25; transitionStyle.Tolerances.LowerLimit = .125;
    transitionStyle.Tolerances.Precision = 3;
    var transition = new AlignedDimension(Vector2.Zero, new Vector2(10,0), 3, transitionStyle) { UserText = "<>" };
    transition.StyleOverrides.Add(DimensionStyleOverrideType.TolerancesDisplayMethod, DimensionStyleTolerancesDisplayMethod.Deviation);
    var transitionDoc = new DxfDocument(version) { BuildDimensionBlocks = true }; transitionDoc.Entities.Add(transition);
    string transitionLabel = transition.Block.Entities.OfType<MText>().Single().Value;
    using var transitionStream = new MemoryStream();
    if (!transitionDoc.Save(transitionStream,binary)) throw new InvalidOperationException("Tolerance-transition package save failed");
    if (transition.StyleOverrides.Count != 1 || transitionStyle.Tolerances.LowerLimit != .125)
        throw new InvalidOperationException("Tolerance-transition save mutated source");
    transitionStream.Position = 0;
    var transitionCopy = DxfDocument.Load(transitionStream) ?? throw new InvalidOperationException("Tolerance-transition package load failed");
    var transitionDim = transitionCopy.Entities.Dimensions.Single();
    if (!transitionDim.StyleOverrides.ContainsType(DimensionStyleOverrideType.TolerancesLowerLimit)
        || (double)transitionDim.StyleOverrides[DimensionStyleOverrideType.TolerancesLowerLimit].Value != .125)
        throw new InvalidOperationException("Installed package lost reactivated tolerance lower allowance");
    transitionDim.Update();
    if (transitionDim.Block.Entities.OfType<MText>().Single().Value != transitionLabel)
        throw new InvalidOperationException("Installed package changed deviation label after reload");
    // Shared by ordinary NuGet selection and all eight exact-asset profiles.
    var normalLine = new Line(new Vector3(1,2,3), new Vector3(4,5,6));
    byte[] normalProxy = { 1,7,19,33,255 };
    normalLine.ProxyGraphics = normalProxy;
    normalLine.Normal = new Vector3(0,0,8);
    if (!(normalLine.ProxyGraphics ?? Array.Empty<byte>()).SequenceEqual(normalProxy))
        throw new InvalidOperationException("Installed normal no-op lost proxy");
    normalLine.Normal = Vector3.UnitX;
    if (normalLine.ProxyGraphics != null) throw new InvalidOperationException("Installed normal edit retained proxy");
    normalLine.ProxyGraphics = normalProxy;
    bool normalRejected = false;
    try { normalLine.Normal = Vector3.Zero; } catch (ArgumentException) { normalRejected = true; }
    if (!normalRejected || normalLine.Normal != Vector3.UnitX
        || !(normalLine.ProxyGraphics ?? Array.Empty<byte>()).SequenceEqual(normalProxy))
        throw new InvalidOperationException("Installed normal rejection changed state");
    var normalDef = new AttributeDefinition("NORMAL") { Value = "definition" };
    normalDef.ProxyGraphics = normalProxy; normalDef.Normal = Vector3.UnitX;
    if (normalDef.ProxyGraphics != null) throw new InvalidOperationException("Installed ATTDEF retained stale normal proxy");
    var normalBlock = new Block("NORMAL_PACKAGE"); normalBlock.AttributeDefinitions.Add(normalDef);
    var normalInsert = new Insert(normalBlock);
    var normalDoc = new DxfDocument(version); normalDoc.Entities.Add(normalLine); normalDoc.Entities.Add(normalInsert);
    var normalAttribute = normalInsert.Attributes.Single();
    normalAttribute.Normal = Vector3.UnitX; normalAttribute.ProxyGraphics = normalProxy;
    normalAttribute.Normal = Vector3.UnitZ;
    if (normalAttribute.ProxyGraphics != null) throw new InvalidOperationException("Installed ATTRIB retained stale normal proxy");
    using var normalStream = new MemoryStream();
    if (!normalDoc.Save(normalStream,binary)) throw new InvalidOperationException("Normal package save failed");
    normalStream.Position = 0;
    var normalCopy = DxfDocument.Load(normalStream) ?? throw new InvalidOperationException("Normal package reload failed");
    var normalLoadedLine = normalCopy.Entities.Lines.Single();
    if (normalLoadedLine.Normal != Vector3.UnitX
        || !(normalLoadedLine.ProxyGraphics ?? Array.Empty<byte>()).SequenceEqual(normalProxy)
        || normalCopy.Blocks["NORMAL_PACKAGE"].AttributeDefinitions["NORMAL"].Normal != Vector3.UnitX
        || normalCopy.Blocks["NORMAL_PACKAGE"].AttributeDefinitions["NORMAL"].ProxyGraphics != null
        || normalCopy.Entities.Inserts.Single().Attributes.Single().Normal != Vector3.UnitZ
        || normalCopy.Entities.Inserts.Single().Attributes.Single().ProxyGraphics != null)
        throw new InvalidOperationException("Installed normal/proxy round trip changed state");
    // Run value-key operations against the actual installed target assembly.
    var equalityVector = new netDxf.GTE.GVector(new[] { 0.0, double.NaN, 2.0 });
    var alternateNaN = BitConverter.Int64BitsToDouble(0x7ff8000000001234L);
    var equalVector = new netDxf.GTE.GVector(new[] { -0.0, alternateNaN, 2.0 });
    var differentVector = new netDxf.GTE.GVector(new[] { double.Epsilon, double.NaN, 2.0 });
    if (equalityVector != equalVector || equalityVector == differentVector
        || !new HashSet<netDxf.GTE.GVector> { equalityVector }.Contains(equalVector))
        throw new InvalidOperationException("Installed vector equality/hash contract failed");
    foreach (long payload in new[] { 0x7ff8000000000001L, 0x7ff0000000000001L, unchecked((long)0xfff8000000001234UL) })
    {
        var payloadKey = new netDxf.GTE.GVector(new[] { -0.0, BitConverter.Int64BitsToDouble(payload), 2.0 });
        if (equalityVector != payloadKey || equalityVector.GetHashCode() != payloadKey.GetHashCode()
            || !new HashSet<netDxf.GTE.GVector> { equalityVector }.Contains(payloadKey))
            throw new InvalidOperationException("Installed target distinguished equal NaN payload hashes");
    }
    var equalityMatrix = new netDxf.GTE.GMatrix(1, 3, equalityVector.Vector);
    var equalMatrix = new netDxf.GTE.GMatrix(1, 3, equalVector.Vector);
    if (equalityMatrix != equalMatrix || equalityMatrix == (netDxf.GTE.GMatrix)null!
        || equalityMatrix == new netDxf.GTE.GMatrix(3, 1, equalVector.Vector)
        || !new Dictionary<netDxf.GTE.GMatrix, int> { [equalityMatrix] = 1 }.ContainsKey(equalMatrix))
        throw new InvalidOperationException("Installed matrix equality/hash contract failed");
    count++;
}
Console.WriteLine($"PASS: {count} installed-package text/binary round trips; {typeof(DxfDocument).Assembly.Location}");
#if PACKAGE_TARGET_SMOKE
TargetAssetEvidence.Complete(count);
#endif
