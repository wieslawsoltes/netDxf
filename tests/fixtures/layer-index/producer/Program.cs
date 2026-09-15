using IxMilia.Dxf;
using IxMilia.Dxf.Entities;
using IxMilia.Dxf.Objects;
var output = args.Length == 0 ? "." : args[0];
Directory.CreateDirectory(output);
foreach (var year in new[] { 2000, 2004, 2007, 2010, 2013, 2018 })
foreach (var ascii in new[] { true, false })
{
    var file = new DxfFile();
    file.Header.Version = Enum.Parse<DxfAcadVersion>("R" + year);
    var a = new DxfLine(new DxfPoint(1, 2, 3), new DxfPoint(4, 5, 6)) { Transparency = 0x02000000 };
    var b = new DxfLine(new DxfPoint(-1, -2, -3), new DxfPoint(-4, -5, -6)) { Transparency = 0x02000000 };
    file.Entities.Add(a); file.Entities.Add(b);
    var parent = new DxfDictionary { IsHardOwner = true };
    file.NamedObjectDictionary.Add("QA_LAYER_INDEX", parent); file.Objects.Add(parent);
    var index = new DxfLayerIndex { TimeStamp = new DateTime(2000, 1, 1, 15, 0, 0, DateTimeKind.Utc) };
    var empty = new DxfLayerIndex { TimeStamp = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc) };
    parent.Add("INDEX", index); parent.Add("EMPTY", empty); file.Objects.Add(index); file.Objects.Add(empty);
    string[] names = { "Alpha", "Alpha", "alpha", "Missing" };
    for (int i = 0; i < names.Length; i++)
    {
        var buffer = new DxfIdBuffer();
        if (i != 1) buffer.Entities.Add(a);
        if (i == 0) { buffer.Entities.Add(b); buffer.Entities.Add(a); }
        index.LayerNames.Add(names[i]); index.IdBuffers.Add(buffer); index.IdBufferCounts.Add(buffer.Entities.Count);
        file.Objects.Add(buffer);
    }
    file.Save(Path.Combine(output, $"ixmilia-layer-index-R{year}-{(ascii ? "ascii" : "binary")}.dxf"), ascii);
}
