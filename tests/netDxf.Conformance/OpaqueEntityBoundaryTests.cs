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
    private static void RegisterOpaqueEntityBoundaryTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        {
            foreach (string field in new[] { "aci", "true-color", "layer", "linetype", "weight", "scale", "visibility", "transparency", "color-name", "shadow", "graphics", "graphics-empty", "graphics-null", "xdata", "app-rename", "layer-rename" })
                Run($"opaque-entity/field/{field}/{version}/{binary}", () => OpaqueField(version, binary, field));
            foreach (string field in new[] { "class-change", "class-replace", "class-remove", "profile", "reactor", "extension", "weight", "scale", "xdata-target" })
                Run($"opaque-entity/preflight/{field}/{version}/{binary}", () => OpaquePreflight(version, binary, field));
        }
        foreach (bool binary in new[] { false, true })
        {
            foreach (string defect in new[] { "common-graphics-empty", "common-graphics-count", "control-value", "control-depth", "space-mismatch", "layout-mismatch", "actual-layer-missing", "target-duplicate", "self-owner", "self-pointer", "null-pointer", "no-class", "acds-data" })
                Run($"opaque-entity/boundary/{defect}/{binary}", () => OpaqueBoundary(defect, binary));
            Run($"opaque-entity/comments/{binary}", () => OpaqueComments(binary));
            Run($"opaque-entity/text-transport/{binary}", () => OpaqueTextTransport(binary));
            Run($"opaque-entity/tag-budget/{binary}", () => OpaqueTagBudget(binary));
            Run($"opaque-entity/block-atomicity/{binary}", () => OpaqueBlockAtomicity(binary));
            Run($"opaque-entity/class-retry/{binary}", () => OpaqueClassRetry(binary));
        }
    }
    private static DxfRawDocument OpaqueModify(DxfRawDocument raw, Action<List<DxfTag>> edit)
    {
        var record = raw.Sections.Single(section => section.Name == "ENTITIES").Records.Single(record => record.Name == OpaqueName);
        var packet = record.Tags.ToList(); edit(packet);
        var tags = raw.Tags.Take(record.StartTagIndex).Concat(packet).Concat(raw.Tags.Skip(record.EndTagIndex));
        return DxfRawDocument.Create(tags);
    }
    private static void OpaqueField(DxfVersion version, bool binary, string field)
    {
        var document = OpaqueLoad(OpaqueFixture(version), binary); var entity = document.Entities.OpaqueEntities.Single();
        var original = entity.SourceTags.ToArray();
        switch (field)
        {
            case "aci": entity.Color = new AciColor(17); break;
            case "true-color": entity.Color = new AciColor(25, 50, 75); break;
            case "layer": entity.Layer = new Layer("changed-layer"); break;
            case "linetype": entity.Linetype = new Linetype("changed-line"); break;
            case "weight": entity.Lineweight = Lineweight.W70; break;
            case "scale": entity.LinetypeScale = 4.125; break;
            case "visibility": entity.IsVisible = false; break;
            case "transparency": entity.Transparency = new Transparency(42); break;
            case "color-name": entity.ColorName = "Book$Blue"; break;
            case "shadow": entity.ShadowMode = EntityShadowMode.Ignore; break;
            case "graphics": entity.ProxyGraphics = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray(); break;
            case "graphics-empty": entity.ProxyGraphics = Array.Empty<byte>(); break;
            case "graphics-null": entity.ProxyGraphics = new byte[] { 1, 2 }; entity.ClearProxyGraphics(); break;
            case "xdata": entity.XData["OPAQUE_TEST"].XDataRecord.Add(new XDataRecord(XDataCode.String, "literal \\U+0041 π")); break;
            case "app-rename": document.ApplicationRegistries["OPAQUE_TEST"].Name = "RENAMED_APP"; break;
            case "layer-rename": entity.Layer = new Layer("before-rename"); entity.Layer.Name = "after-rename"; break;
        }
        bool supported = field != "color-name" || version >= DxfVersion.AutoCad2004;
        supported &= field != "shadow" || version >= DxfVersion.AutoCad2007;
        if (!supported) { OpaqueExpectPreflight(document, !binary, field); return; }
        using var output = new MemoryStream(); Check(document.Save(output, !binary), "Individual common edit save"); output.Position = 0;
        var other = (DxfDocument.Load(output) ?? throw new Exception("Individual common edit reload")).Entities.OpaqueEntities.Single();
        Equal(entity.Color.Index, other.Color.Index, "ACI projection"); Equal(entity.Color.UseTrueColor, other.Color.UseTrueColor, "True-color presence");
        if (entity.Color.UseTrueColor) Equal(AciColor.ToTrueColor(entity.Color), AciColor.ToTrueColor(other.Color), "True-color value");
        Equal(entity.Layer.Name, other.Layer.Name, "Layer value"); Equal(entity.Linetype.Name, other.Linetype.Name, "Linetype value");
        Equal(entity.Lineweight, other.Lineweight, "Lineweight value"); Equal(entity.LinetypeScale, other.LinetypeScale, "Scale value");
        Equal(entity.IsVisible, other.IsVisible, "Visibility value"); Equal(entity.Transparency.Value, other.Transparency.Value, "Transparency value");
        Equal(entity.ColorName, other.ColorName, "Color-name value"); Equal(entity.ShadowMode, other.ShadowMode, "Shadow value");
        Check(entity.ProxyGraphics == null ? other.ProxyGraphics == null : entity.ProxyGraphics.SequenceEqual(other.ProxyGraphics!), "Proxy-cache value");
        Equal(entity.XData.Values.Single().ApplicationRegistry.Name, other.XData.Values.Single().ApplicationRegistry.Name, "XData app name");
        Equal(entity.XData.Values.Single().XDataRecord.Last().Value, other.XData.Values.Single().XDataRecord.Last().Value, "XData final record");
        OpaqueSameTags(original, entity.SourceTags, "Immutable original snapshot after field edit");
        int start = Array.FindIndex(original, tag => tag.Code == 100 && (string)tag.Value == "AcDbQualifiedFutureCurve");
        int actualStart = other.SourceTags.ToList().FindIndex(tag => tag.Code == 100 && (string)tag.Value == "AcDbQualifiedFutureCurve");
        OpaqueSameTags(original.Skip(start).TakeWhile(tag => tag.Code != 1001).ToArray(), other.SourceTags.Skip(actualStart).TakeWhile(tag => tag.Code != 1001).ToArray(), "Private payload after single common field edit");
        byte[] bytes = (byte[])entity.SourceTags.First(tag => tag.Code == 310).Value; bytes[0] ^= 255;
        OpaqueSameTags(original, entity.SourceTags, "Binary snapshot getter isolation");
    }
    private static void OpaqueExpectPreflight(DxfDocument document, bool binary, string label)
    {
        var state = new CompatibilityState(document); byte[] sentinel = { 13, 37, 42, 91 };
        using var output = new MemoryStream(); output.Write(sentinel); output.Position = 2;
        bool saved; try { saved = document.Save(output, binary); } catch (Exception) { saved = false; }
        Check(!saved, "Expected opaque refusal: " + label); Check(output.ToArray().SequenceEqual(sentinel) && output.Position == 2, "Failed stream save changed existing bytes/position"); state.CheckUnchanged();
        string file = Path.Combine(ArtifactDirectory, $"opaque-sentinel-{label}-{binary}.bin"); File.WriteAllBytes(file, sentinel);
        string name = document.Name; string working = document.SupportFolders.WorkingFolder;
        try { saved = document.Save(file, binary); } catch (Exception) { saved = false; }
        Check(!saved && File.ReadAllBytes(file).SequenceEqual(sentinel), "Failed opaque file save changed the destination");
        Equal(name, document.Name, "Failed file save changed document name"); Equal(working, document.SupportFolders.WorkingFolder, "Failed file save changed working folder"); state.CheckUnchanged();
    }
    private static void OpaquePreflight(DxfVersion version, bool binary, string field)
    {
        var document = OpaqueLoad(OpaqueFixture(version), binary); var entity = document.Entities.OpaqueEntities.Single();
        switch (field)
        {
            case "class-change": document.Classes[OpaqueName].ApplicationName = "changed"; break;
            case "class-replace": int index = document.Classes.IndexOf(document.Classes[OpaqueName]); document.Classes[index] = (DxfClass)document.Classes[OpaqueName].Clone(); break;
            case "class-remove": document.Classes.Remove(OpaqueName); break;
            case "profile": document.DrawingVariables.AcadVer = SupportedVersions.First(other => other != version); break;
            case "reactor": entity.PersistentReactors.Add(document.Entities.Lines.Single()); break;
            case "extension": document.Objects.SetExtensionDictionary(entity, new DxfDictionary()); break;
            case "weight": entity.Lineweight = (Lineweight)3; break;
            case "scale": entity.LinetypeScale = double.NaN; break;
            case "xdata-target": entity.XData.Values.Single().XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, "D00D")); break;
        }
        OpaqueExpectPreflight(document, binary, field);
    }
    private static void OpaqueBoundary(string defect, bool binary)
    {
        var raw = OpaqueFixture(DxfVersion.AutoCad2018);
        raw = OpaqueModify(raw, packet =>
        {
            int body = packet.FindIndex(tag => tag.Code == 100 && (string)tag.Value == "AcDbQualifiedFutureCurve");
            if (defect == "common-graphics-empty") packet.Insert(body, new DxfTag(310, Array.Empty<byte>()));
            if (defect == "common-graphics-count") packet.InsertRange(body, new[] { new DxfTag(92, 1), new DxfTag(310, new byte[] { 1, 2 }) });
            if (defect == "control-value") packet.Insert(5, new DxfTag(102, "unframed"));
            if (defect == "control-depth") packet.InsertRange(body, Enumerable.Repeat(new DxfTag(102, "{TOO_DEEP"), 33).Concat(Enumerable.Repeat(new DxfTag(102, "}"), 33)));
            if (defect == "space-mismatch") packet.Insert(body, new DxfTag(67, (short)1));
            if (defect == "layout-mismatch") packet.Insert(body, new DxfTag(410, "AbsentLayout"));
            if (defect == "self-owner") packet.Insert(body + 1, new DxfTag(360, "F001"));
            if (defect == "self-pointer") packet.Insert(body + 1, new DxfTag(340, "F001"));
            if (defect == "null-pointer") packet.Insert(body + 1, new DxfTag(340, "0"));
        });
        var tags = raw.Tags.ToList();
        if (defect == "actual-layer-missing")
        {
            var layer = raw.Sections.Single(section => section.Name == "TABLES").Records.Single(record => record.Name == "LAYER" && record.Tags.Any(tag => tag.Code == 2 && (string)tag.Value == "0"));
            tags.RemoveRange(layer.StartTagIndex, layer.Tags.Count);
        }
        if (defect == "target-duplicate")
        {
            var line = raw.Sections.Single(section => section.Name == "ENTITIES").Records.Single(record => record.Name == "LINE"); tags.InsertRange(line.EndTagIndex, line.Tags);
        }
        if (defect == "no-class")
        {
            var declaration = raw.Sections.Single(section => section.Name == "CLASSES").Records.Single(record => record.Tags.Any(tag => tag.Code == 1 && (string)tag.Value == OpaqueName)); tags.RemoveRange(declaration.StartTagIndex, declaration.Tags.Count);
        }
        if (defect == "acds-data")
            tags.InsertRange(tags.Count - 1, new[] { new DxfTag(0, "SECTION"), new DxfTag(2, "ACDSDATA"), new DxfTag(0, "ACDSSCHEMA"), new DxfTag(90, 1), new DxfTag(0, "ENDSEC") });
        raw = DxfRawDocument.Create(tags);
        using var input = new MemoryStream(); raw.Save(input, binary); input.Position = 0;
        bool allowed = defect is "self-pointer" or "null-pointer" or "no-class";
        DxfDocument? document = null; Exception? error = null;
        try { document = DxfDocument.Load(input); } catch (Exception caught) { error = caught; }
        if (!allowed) { Check(document == null, "Malformed or unsupported boundary accepted: " + defect); return; }
        Check(document != null, "Valid opaque boundary rejected: " + defect + " " + error);
        using var output = new MemoryStream(); Check(document!.Save(output, !binary), "Valid boundary save");
    }
    private static void OpaqueComments(bool binary)
    {
        var raw = OpaqueModify(OpaqueFixture(DxfVersion.AutoCad2018), packet => { packet.Insert(3, new DxfTag(999, "source header comment")); packet.Insert(packet.Count - 4, new DxfTag(999, "private comment")); });
        var document = OpaqueLoad(raw, false);
        if (binary) { OpaqueExpectPreflight(document, true, "comments"); return; }
        using var output = new MemoryStream(); Check(document.Save(output), "ASCII comment save"); output.Position = 0;
        var other = DxfRawDocument.Load(output).Sections.Single(section => section.Name == "ENTITIES").Records.Single(record => record.Name == OpaqueName);
        OpaqueSameTags(document.Entities.OpaqueEntities.Single().SourceTags, other.Tags, "ASCII comments preserved");
    }
    private static void OpaqueTextTransport(bool binary)
    {
        var raw = OpaqueModify(OpaqueFixture(DxfVersion.AutoCad2018), packet => packet.Insert(packet.Count - 4, new DxfTag(300, "private\r\ntext")));
        var document = OpaqueLoad(raw, true);
        if (!binary) { OpaqueExpectPreflight(document, false, "private-crlf"); return; }
        using var output = new MemoryStream(); Check(document.Save(output, true), "Binary CRLF packet save"); output.Position = 0;
        OpaqueSameTags(document.Entities.OpaqueEntities.Single().SourceTags, (DxfDocument.Load(output) ?? throw new Exception("Binary CRLF reload")).Entities.OpaqueEntities.Single().SourceTags, "Binary CRLF unchanged");
    }
    private static void OpaqueTagBudget(bool binary)
    {
        var document = OpaqueLoad(OpaqueFixture(DxfVersion.AutoCad2018), binary); var entity = document.Entities.OpaqueEntities.Single();
        int count = 65536 - entity.SourceTags.Count;
        var data = entity.XData.Values.Single();
        for (int i = 0; i < count; i++) data.XDataRecord.Add(new XDataRecord(XDataCode.Int16, (short)1));
        using var output = new MemoryStream(); Check(document.Save(output, binary), "Exact tag admission boundary save"); output.Position = 0;
        Check(DxfDocument.Load(output) != null, "Exact output tag boundary must reload");
        data.XDataRecord.Add(new XDataRecord(XDataCode.Int16, (short)1));
        OpaqueExpectPreflight(document, binary, "tag-budget");
        data.XDataRecord.RemoveAt(data.XDataRecord.Count - 1);
        using var retry = new MemoryStream(); Check(document.Save(retry, binary), "Budget repair retry");
    }
    private static void OpaqueBlockAtomicity(bool binary)
    {
        var document = OpaqueLoad(OpaqueFixture(DxfVersion.AutoCad2018), binary); var entity = document.Entities.OpaqueEntities.Single();
        var state = new CompatibilityState(document);
        foreach (Action operation in new Action[] { () => Block.Create(document, "copy"), () => entity.Owner.Save(Path.Combine(ArtifactDirectory, "must-not-exist-opaque-block.dxf"), DxfVersion.AutoCad2018, binary), () => new Insert(entity.Owner).Explode(), () => new Insert(entity.Owner).ExplodeCell(0,0), () => new Insert(entity.Owner).ExplodeEnumerable().ToList() })
        {
            bool rejected = false; try { operation(); } catch (NotSupportedException) { rejected = true; }
            Check(rejected, "Block/insert geometry path must reject"); state.CheckUnchanged();
        }
    }
    private static void OpaqueClassRetry(bool binary)
    {
        var document = OpaqueLoad(OpaqueFixture(DxfVersion.AutoCad2018), binary); var declaration = document.Classes[OpaqueName];
        string application = declaration.ApplicationName; declaration.ApplicationName = "changed";
        OpaqueExpectPreflight(document, binary, "class-retry"); declaration.ApplicationName = application;
        using var output = new MemoryStream(); Check(document.Save(output, binary), "Repair only CLASS state permits retry");
    }
}
