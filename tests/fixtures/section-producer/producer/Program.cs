using IxMilia.Dxf;
using IxMilia.Dxf.Entities;

string output = args.Length == 0 ? "." : args[0];
Directory.CreateDirectory(output);
foreach (int year in new[] { 2007, 2010, 2013, 2018 })
foreach (bool ascii in new[] { true, false })
{
    var file = new DxfFile();
    file.Header.Version = Enum.Parse<DxfAcadVersion>("R" + year);
    var section = new DxfEntitySection
    {
        State = 7,
        Flags = 17,
        Name = "Producer section",
        VerticalDirection = new DxfVector(0.25, -0.5, 2.0),
        TopHeight = 17.125,
        BottomHeight = -2.75,
        IndicatorTransparency = 37,
        IndicatorColor = DxfColor.FromRawValue(5),
        IndicatorColorName = "ProducerColor",
        GeometrySettings = null,
        Transparency = 0x02000000,
    };
    section.Vertices.Add(new DxfPoint(1.125, 2.25, 3.5));
    section.Vertices.Add(new DxfPoint(-4.75, 5.125, -6.25));
    section.Vertices.Add(new DxfPoint(7.5, -8.75, 9.125));
    section.BackLineVertices.Add(new DxfPoint(10.25, -11.5, 12.75));
    section.BackLineVertices.Add(new DxfPoint(-13.125, 14.25, -15.5));
    file.Entities.Add(section);
    file.Save(Path.Combine(output, $"ixmilia-section-R{year}-{(ascii ? "ascii" : "binary")}.dxf"), ascii);
}
