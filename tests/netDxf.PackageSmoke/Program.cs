using System.Text;
using netDxf;
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
    var dim = new AlignedDimension(Vector2.Zero, new Vector2(10, 0), 3, style) { UserText = " " };
    dim.StyleOverrides.Add(DimensionStyleOverrideType.DimPrefix, "");
    dim.StyleOverrides.Add(DimensionStyleOverrideType.DimSuffix, "");
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
        || copy.Entities.Lines.Single().EndPoint != new Vector3(4, 5, 6)
        || copy.Objects.Validate().Count != 0 || !stream.CanRead)
        throw new InvalidOperationException($"Package round trip failed: {version}/{binary}");
    count++;
}
Console.WriteLine($"PASS: {count} installed-package text/binary round trips; {typeof(DxfDocument).Assembly.Location}");
