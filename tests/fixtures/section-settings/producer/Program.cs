using IxMilia.Dxf;
using IxMilia.Dxf.Entities;
using IxMilia.Dxf.Objects;
string output = args.Length == 0 ? "." : args[0]; Directory.CreateDirectory(output);
foreach (int year in new[] { 2007, 2010, 2013, 2018 }) foreach (bool ascii in new[] { true, false })
{
    var file = new DxfFile(); file.Header.Version = Enum.Parse<DxfAcadVersion>("R" + year);
    file.Entities.Add(new DxfLine(new DxfPoint(1, 2, 3), new DxfPoint(4, 5, 6)) { Transparency = 0x02000000 });
    var settings = new DxfSectionSettings { SectionType = 4 };
    var type = new DxfSectionTypeSettings { SectionType = 2, IsGenerationOption = true, DestinationFileName = "inert.dwg" };
    for (int i = 0; i < 3; i++) type.GeometrySettings.Add(new DxfSectionGeometrySettings
    {
        SectionType = 2, GeometryCount = 1 << i, BitFlags = 5, Color = DxfColor.FromRawValue((short)(i + 1)),
        LayerName = i == 0 ? "*_BackgroundLines" : "0", LineTypeName = "Continuous", LineTypeScale = 1.125 + i,
        PlotStyleName = "ByColor", LineWeight = 40, FaceTransparency = 30, EdgeTransparency = 70,
        HatchPatternType = 1, HatchPatternName = "ANSI31", HatchAngle = -1.25 + i, HatchScale = 21.5 + i, HatchSpacing = 0.125 + i
    });
    settings.SectionTypeSettings.Add(type);
    settings.SectionTypeSettings.Add(new DxfSectionTypeSettings { SectionType = 1, DestinationFileName = "" });
    file.NamedObjectDictionary.Add("QA_SECTION_SETTINGS_PRODUCER", settings); file.Objects.Add(settings);
    file.Save(Path.Combine(output, $"ixmilia-section-settings-R{year}-{(ascii ? "ascii" : "binary")}.dxf"), ascii);
}
