using System.Security.Cryptography;
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterViewLiveSectionTests()
    {
        foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2007))
        foreach (bool binary in new[] { false, true })
        {
            var v = version; bool b = binary;
            Run($"view-live-section/producer/{v}/{b}", () => ViewSectionProducer(v, b));
            Run($"view-live-section/lifecycle/{v}/{b}", () => ViewSectionLifecycle(v, b));
            Run($"view-live-section/foreign/{v}/{b}", () => ViewSectionForeign(v, b));
            foreach (string defect in new[] { "missing", "wrong-type", "duplicate", "duplicate-null", "section-identity", "view-identity", "private-handle", "private-ref", "later-subclass", "padded", "zero", "absent" })
            { string d = defect; Run($"view-live-section/input/{v}/{b}/{d}", () => ViewSectionInput(v, b, d)); }
        }
        foreach (DxfVersion version in SupportedVersions.Where(v => v < DxfVersion.AutoCad2007))
        foreach (bool binary in new[] { false, true })
        { var v = version; bool b = binary; Run($"view-live-section/version/{v}/{b}", () => ViewSectionVersion(v, b)); }
    }

    private static DxfDocument ViewSectionLoad(DxfRawDocument raw, bool binary)
    {
        using var input = new MemoryStream(); raw.WithTags(raw.Tags.Where(t => t.Code != 999)).Save(input, binary);
        Check(input.ToArray().AsSpan().StartsWith("AutoCAD Binary DXF"u8) == binary, "VIEW test input transport");
        input.Position = 0; return DxfDocument.Load(input) ?? throw new InvalidDataException("VIEW load returned null");
    }

    private static DxfRawDocument ViewSectionRaw(DxfVersion version)
    {
        var doc = new DxfDocument(version); var section = SectionExample("SECTIONOBJECT"); doc.Entities.Add(section);
        doc.Views.Add(new View("Live") { LiveSection = section });
        doc.Views.Add(new View("Null") { LiveSection = null }); doc.Views.Add(new View("Absent"));
        return DxfRawDocument.Load(new MemoryStream(SectionBytes(doc, false)));
    }

    private static void ViewSectionReject(Action action)
    {
        bool rejected = false;
        try { action(); }
        catch (Exception error) when (error is ArgumentException || error is InvalidOperationException || error is InvalidDataException || error is NotSupportedException || error is FormatException)
        { rejected = true; }
        Check(rejected, "VIEW live-section operation was accepted");
    }

    private static void ViewSectionProducer(DxfVersion version, bool binary)
    {
        string year = version.ToString().Replace("AutoCad", "");
        string filename = $"ezdxf-view-live-section-R{year}-{(binary ? "binary" : "ascii")}.dxf";
        string root = Path.Combine("tests", "fixtures", "view-live-section");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
        var entry = manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(e => e.GetProperty("path").GetString() == filename);
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, filename));
        Equal(entry.GetProperty("sha256").GetString()!, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "VIEW producer fixture hash");
        Check(bytes.AsSpan().StartsWith("AutoCAD Binary DXF"u8) == binary, "VIEW producer input transport");
        var doc = DxfDocument.Load(new MemoryStream(bytes)) ?? throw new InvalidDataException("Producer VIEW input");
        var view = doc.Views["LiveSectionProducer"]; var section = doc.Entities.Sections.Single();
        Check(ReferenceEquals(view.LiveSection, section), "Producer VIEW physical target");
        Equal(entry.GetProperty("section_handle").GetString()!, section.Handle, "Producer section identity");
        Check(view.HasStoredLiveSection && doc.Views["NullSectionProducer"].HasStoredLiveSection && !doc.Views["AbsentSectionProducer"].HasStoredLiveSection, "Producer null presence");
        byte[] output = SectionBytes(doc, binary);
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"view-live-section-producer-{version}-{binary}.dxf"), output);
        var again = DxfDocument.Load(new MemoryStream(output))!;
        Check(ReferenceEquals(again.Views[view.Name].LiveSection, again.Entities.Sections.Single()), "Producer roundtrip identity");
    }

    private static void ViewSectionLifecycle(DxfVersion version, bool binary)
    {
        var doc = ViewSectionLoad(ViewSectionRaw(version), binary); var section = doc.Entities.Sections.Single();
        var view = doc.Views["Live"]; var second = (View)view.Clone("Second"); doc.Views.Add(second);
        Check(ReferenceEquals(second.LiveSection, section), "Local clone shared target");
        var originalOwner = section.Owner; string originalHandle = section.Handle;
        Check(!doc.Entities.Remove(section), "Referenced SECTION removed");
        ViewSectionReject(() => doc.Objects.EraseSection(section));
        Check(section.Owner == originalOwner && section.Handle == originalHandle && !section.IsErased, "Rejected removal changed section identity");
        view.ClearLiveSectionReference(); Check(!view.HasStoredLiveSection && view.LiveSection == null, "Clear retained field");
        Check(!doc.Entities.Remove(section), "Second consumer did not protect section");
        second.LiveSection = null; Check(second.HasStoredLiveSection && second.LiveSection == null, "Explicit null not retained");
        var roundtrip = DxfDocument.Load(new MemoryStream(SectionBytes(doc, binary)))!;
        Check(!roundtrip.Views["Live"].HasStoredLiveSection && roundtrip.Views["Second"].HasStoredLiveSection, "Cleared/null output collapsed");
        second.LiveSection = section; Check(doc.Views.Remove(second), "View consumer removal failed");
        Check(doc.Entities.Remove(section), "Removed view left stale incoming count");
        Check(section.Owner == null && view.LiveSection == null, "Removal owned section through view");
        var replacement = SectionExample("SECTIONOBJECT"); doc.Entities.Add(replacement); view.LiveSection = replacement;
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"view-live-section-authored-{version}-{binary}.dxf"), SectionBytes(doc, binary));
        view.ClearLiveSectionReference(); doc.Objects.EraseSection(replacement);
        Check(replacement.IsErased, "Released section erasure failed");
        ViewSectionReject(() => view.LiveSection = replacement);
        var block = new Block("SectionHost"); doc.Blocks.Add(block); var nested = SectionExample("SECTIONOBJECT"); block.Entities.Add(nested); view.LiveSection = nested;
        Check(!doc.Blocks.Remove(block), "Containing block removed through live reference");
        view.ClearLiveSectionReference(); Check(doc.Blocks.Remove(block), "Released containing block not removed");
    }

    private static void ViewSectionForeign(DxfVersion version, bool binary)
    {
        var source = ViewSectionLoad(ViewSectionRaw(version), binary); var target = new DxfDocument(version);
        var sourceView = source.Views["Live"]; var copy = (View)sourceView.Clone("Foreign");
        var sameName = SectionExample("SECTIONOBJECT"); target.Entities.Add(sameName);
        string seed = target.DrawingVariables.HandleSeed; int count = target.Views.Count;
        ViewSectionReject(() => target.Views.Add(copy));
        Equal(seed, target.DrawingVariables.HandleSeed, "Foreign adoption consumed handles"); Equal(count, target.Views.Count, "Foreign adoption inserted view");
        Check(copy.Owner == null && copy.Handle == null, "Foreign adoption attached refused clone");
        copy.LiveSection = sameName; target.Views.Add(copy); Check(ReferenceEquals(copy.LiveSection, sameName), "Explicit foreign remap failed");
        ViewSectionReject(() => copy.LiveSection = sourceView.LiveSection);
        Check(ReferenceEquals(copy.LiveSection, sameName), "Refused replacement changed existing reference");
        ViewSectionReject(() => copy.LiveSection = new Section());
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"view-live-section-foreign-{version}-{binary}.dxf"), SectionBytes(target, binary));
        Check(sourceView.LiveSection != copy.LiveSection && sourceView.LiveSection!.Owner != null, "Foreign remap changed source");
    }

    private static void ViewSectionInput(DxfVersion version, bool binary, string defect)
    {
        var raw = ViewSectionRaw(version);
        var section = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "SECTIONOBJECT");
        string handle = (string)section.Tags.First(t => t.Code == 5).Value;
        string layer = (string)raw.Sections.SelectMany(s => s.Records).First(r => r.Name == "LAYER").Tags.First(t => t.Code == 5).Value;
        raw = MLeaderNativeReplace(raw, record =>
        {
            var tags = record.Tags.ToList();
            bool live = record.Name == "VIEW" && tags.Any(t => t.Code == 2 && Equals(t.Value, "Live"));
            if (record.Name == "SECTIONOBJECT" && defect == "section-identity") tags.Insert(2, new DxfTag(5, handle));
            else if (record.Name == "SECTIONOBJECT" && defect == "private-handle")
            {
                tags.RemoveAll(t => t.Code == 5); tags.InsertRange(1, new[] { new DxfTag(102, "{PRIVATE"), new DxfTag(5, handle), new DxfTag(102, "}") });
            }
            else if (live)
            {
                int at = tags.FindIndex(t => t.Code == 334);
                switch (defect)
                {
                    case "missing": tags[at] = new DxfTag(334, "ABCDEF01"); break;
                    case "wrong-type": tags[at] = new DxfTag(334, layer); break;
                    case "duplicate": tags.Insert(at, new DxfTag(334, handle)); break;
                    case "duplicate-null": tags.Insert(at, new DxfTag(334, "0")); break;
                    case "view-identity": tags.Insert(2, tags.First(t => t.Code == 5)); break;
                    case "private-ref": tags.InsertRange(at, new[] { new DxfTag(102, "{PRIVATE"), new DxfTag(334, "ABCDEF01"), new DxfTag(102, "}") }); break;
                    case "later-subclass": tags.Add(new DxfTag(100, "PrivateView")); tags.Add(new DxfTag(334, "ABCDEF01")); break;
                    case "padded": tags[at] = new DxfTag(334, "000" + handle.ToLowerInvariant()); break;
                    case "zero": tags[at] = new DxfTag(334, "0000"); break;
                    case "absent": tags.RemoveAt(at); break;
                }
            }
            return tags;
        });
        if (defect is "missing" or "wrong-type" or "duplicate" or "duplicate-null" or "section-identity" or "view-identity" or "private-handle")
        { ViewSectionReject(() => ViewSectionLoad(raw, binary)); return; }
        var doc = ViewSectionLoad(raw, binary); var view = doc.Views["Live"];
        if (defect == "absent") Check(!view.HasStoredLiveSection && view.LiveSection == null, "Absent VIEW field became present");
        else if (defect == "zero") Check(view.HasStoredLiveSection && view.LiveSection == null, "Numeric-null VIEW field changed");
        else Check(ReferenceEquals(view.LiveSection, doc.Entities.Sections.Single()), "VIEW reference used private/alternate target");
    }

    private static void ViewSectionVersion(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); var view = new View("Unsupported") { LiveSection = null };
        string seed = doc.DrawingVariables.HandleSeed;
        ViewSectionReject(() => doc.Views.Add(view)); Equal(seed, doc.DrawingVariables.HandleSeed, "Version rejection allocated identity");
        view.ClearLiveSectionReference(); doc.Views.Add(view);
        ViewSectionReject(() => view.LiveSection = null); Check(!view.HasStoredLiveSection, "Rejected field assignment mutated presence");
        var modern = new DxfDocument(DxfVersion.AutoCad2018); modern.Views.Add(new View("Null") { LiveSection = null });
        modern.DrawingVariables.AcadVer = version;
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 }, true); stream.Position = 2;
        seed = modern.DrawingVariables.HandleSeed; bool rejected = false;
        try { rejected = !modern.Save(stream, binary); } catch (NotSupportedException) { rejected = true; }
        Check(rejected, "Older profile silently dropped live-section field");
        Equal(seed, modern.DrawingVariables.HandleSeed, "Profile rejection allocated identity"); Equal(2L, stream.Position, "Profile rejection moved stream");
        Check(stream.ToArray().SequenceEqual(new byte[] { 1, 2, 3, 4, 5 }), "Profile rejection changed stream bytes");
    }
}
