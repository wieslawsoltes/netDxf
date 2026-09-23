using System.Text;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;
using netDxf.Units;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
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
    using var stream = new MemoryStream();
    if (!doc.Save(stream, binary)) throw new InvalidOperationException("Package save failed");
    stream.Position = 0;
    var copy = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Package load failed");
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
    dimension.UserText = "<>";
    dimension.Style.TextFractionHeightScale = 0.5;
    dimension.Style.Tolerances.DisplayMethod = DimensionStyleTolerancesDisplayMethod.Deviation;
    dimension.Style.Tolerances.UpperLimit = 0.25;
    dimension.Style.Tolerances.LowerLimit = 0.125;
    dimension.Style.Tolerances.Precision = 3;
    dimension.Update();
    const string deviation = "{\\A1;10.0000{\\H0.5x;\\S+0.250^ -0.125;}}";
    if (dimension.Block.Entities.OfType<MText>().Single().Value != deviation)
        throw new InvalidOperationException("Installed package tolerance rendering failed");
    using var toleranceStream = new MemoryStream();
    if (!copy.Save(toleranceStream, binary)) throw new InvalidOperationException("Tolerance save failed");
    toleranceStream.Position = 0;
    var toleranceCopy = DxfDocument.Load(toleranceStream) ?? throw new InvalidOperationException("Tolerance load failed");
    var toleranceDimension = toleranceCopy.Entities.Dimensions.Single();
    toleranceDimension.Update();
    if (toleranceDimension.Block.Entities.OfType<MText>().Single().Value != deviation)
        throw new InvalidOperationException("Installed package tolerance regeneration failed");
    toleranceDimension.Style.Tolerances.DisplayMethod = DimensionStyleTolerancesDisplayMethod.Limits;
    toleranceDimension.Update();
    if (toleranceDimension.Block.Entities.OfType<MText>().Single().Value != "{\\H0.5x;\\S10.250^ 9.875;}")
        throw new InvalidOperationException("Installed package limit rendering failed");
    toleranceDimension.Style.Tolerances.DisplayMethod = DimensionStyleTolerancesDisplayMethod.Symmetrical;
    toleranceDimension.Style.Tolerances.LowerLimit = 9; // Inactive in symmetrical mode.
    toleranceDimension.Update();
    if (toleranceDimension.Block.Entities.OfType<MText>().Single().Value != "{\\A1;10.0000{\\H0.5x;±0.250}}")
        throw new InvalidOperationException("Installed package symmetric upper-only rendering failed");
    count++;
}
Console.WriteLine($"PASS: {count} installed-package text/binary round trips; {typeof(DxfDocument).Assembly.Location}");
