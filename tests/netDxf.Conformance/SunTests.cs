using System.IO.Compression;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void SunThrows<T>(Action action, string reason) where T : Exception { Throws<T>(action); }
    private static void RunSunTests()
    {
        foreach (var version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2007))
            foreach (bool binary in new[] { false, true })
            {
                foreach (string kind in version < DxfVersion.AutoCad2010 ? new[] { "VPORT", "VIEWPORT" } : new[] { "VPORT", "VIEW", "VIEWPORT" })
                {
                    Run($"sun/values/{version}/{binary}/{kind}", () => SunValues(version, binary, kind));
                    Run($"sun/lifecycle/{version}/{binary}/{kind}", () => SunLifecycle(version, binary, kind));
                    Run($"sun/host-private-context/{version}/{binary}/{kind}", () => SunHostPrivateContext(version, binary, kind));
                }
                foreach (string defect in new[] { "missing-version", "duplicate-version", "duplicate-marker", "nonfinite", "missing-time", "duplicate-intensity", "color-low", "color-high", "rgb-low", "rgb-high", "shadow-low", "shadow-high", "map-small", "map-nonpower", "map-large", "softness-low", "softness-high", "owner-missing", "owner-wrong", "slot-missing", "slot-wrong-kind", "slot-missing-target", "slot-duplicate" })
                    Run($"sun/malformed/{version}/{binary}/{defect}", () => SunMalformed(version, binary, defect));
                foreach (string variant in new[] { "version", "field", "subclass", "header" })
                    Run($"sun/opaque/{version}/{binary}/{variant}", () => SunOpaque(version, binary, variant));
                Run($"sun/null-and-normalized/{version}/{binary}", () => SunNullIdentity(version, binary));
            }
        foreach (bool binary in new[] { false, true })
            foreach (string file in new[] { "acad_table_simple.dxf", "acad_table_with_blk_ref.dxf" })
                Run($"sun/native/{file}/{binary}", () => SunNative(file, binary));
        foreach (var version in new[] { DxfVersion.AutoCad2000, DxfVersion.AutoCad2004 })
            foreach (bool binary in new[] { false, true }) Run($"sun/older-opaque/{version}/{binary}", () => SunOlderOpaque(version, binary));
        Run("sun/atomicity-and-profile", SunAtomicity);
        Run("sun/caller-reentrancy", SunReentrancy);
    }
    private static DxfObject SunHost(DxfDocument doc, string kind, string name)
    {
        if (kind == "VIEW") return doc.Views.Add(new View(name));
        if (kind == "VPORT") return doc.VPorts.AddRecord(new VPort(name));
        var viewport = new Viewport(); doc.Entities.Add(viewport); return viewport;
    }
    private static DxfDatabaseObject? SunOf(DxfObject host)
        => host is View view ? view.Sun : host is VPort vport ? vport.Sun : ((Viewport)host).Sun;
    private static DxfSun SunSettings() => new() { Enabled = true, ColorIndex = 3, TrueColor = 0x123456, Intensity = -2.125, ShadowsEnabled = false, JulianDay = 2455826, StoredTime = 54000000, DaylightSavingTime = true, ShadowType = DxfSunShadowType.AreaSampled, ShadowMapSize = 4096, ShadowSoftness = 255 };
    private static (DxfDocument Document, DxfObject Host, DxfSun Sun) SunSetup(DxfVersion version, string kind = "VPORT")
    {
        var doc = new DxfDocument(version); var host = SunHost(doc, kind, "SunOwner"); var sun = SunSettings(); doc.Objects.SetSun(host, sun);
        return (doc, host, sun);
    }
    private static DxfRawDocument SunRaw(DxfVersion version, bool binary)
    { var setup = SunSetup(version); using var output = new MemoryStream(); Check(setup.Document.Save(output, binary), "SUN seed save"); output.Position = 0; return DxfRawDocument.Load(output); }
    private static DxfDocument SunLoad(DxfRawDocument raw)
    { using var input = new MemoryStream(); raw.Save(input); input.Position = 0; return DxfDocument.Load(input) ?? throw new Exception("SUN load failed."); }
    private static DxfDocument SunRoundTrip(DxfDocument doc, bool binary, string? file = null)
    {
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "SUN save");
        if (file != null) File.WriteAllBytes(Path.Combine(ArtifactDirectory, file), output.ToArray());
        output.Position = 0; return DxfDocument.Load(output) ?? throw new Exception("SUN roundtrip load failed.");
    }
    private static void SunAssert(DxfSun sun, DxfObject host)
    {
        Check(ReferenceEquals(sun.Owner, host) && ReferenceEquals(SunOf(host), sun), "Reciprocal owned SUN");
        Equal(1, sun.StoredVersion, "Stored version"); Check(sun.Enabled && !sun.ShadowsEnabled && sun.DaylightSavingTime, "Stored flags");
        Equal((short)3, sun.ColorIndex, "Independent ACI"); Equal((int?)0x123456, sun.TrueColor, "Independent truecolor"); Equal(-2.125, sun.Intensity, "Finite stored intensity without speculative range");
        Equal(2455826, sun.JulianDay, "Raw Julian day"); Equal(54000000, sun.StoredTime, "Raw native time without conversion"); Equal(DxfSunShadowType.AreaSampled, sun.ShadowType, "Primary enum shadow type two"); Equal((short)4096, sun.ShadowMapSize, "Map size"); Equal((byte)255, sun.ShadowSoftness, "Unsigned softness");
    }
    private static void SunValues(DxfVersion version, bool binary, string kind)
    {
        var (doc, host, sun) = SunSetup(version, kind); string hostId = host.Handle, sunId = sun.Handle;
        for (int cycle = 0; cycle < 3; cycle++)
        {
            SunAssert(sun, host); Equal(0, doc.Objects.Validate().Count, "SUN graph validation");
            doc = SunRoundTrip(doc, binary, cycle == 2 ? $"sun-{kind}-{version}-{binary}.dxf" : null);
            host = doc.GetObjectByHandle(hostId); sun = (DxfSun)doc.GetObjectByHandle(sunId);
        }
        SunAssert(sun, host);
        foreach (short size in new short[] { 64, 128, 256, 512, 1024, 2048, 4096 }) sun.ShadowMapSize = size;
        sun.TrueColor = null; sun.ColorIndex = 256; sun.ShadowType = DxfSunShadowType.ShadowMaps; sun.StoredTime = int.MinValue; sun.JulianDay = int.MaxValue; sun.Intensity = 0; sun.ShadowSoftness = 0;
        doc = SunRoundTrip(doc, binary); sun = (DxfSun)doc.GetObjectByHandle(sunId);
        Equal(null, sun.TrueColor, "Absent truecolor retained"); Equal(int.MinValue, sun.StoredTime, "Raw signed time"); Equal(int.MaxValue, sun.JulianDay, "Raw signed day");
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "CLASS save"); stream.Position = 0;
        var raw = DxfRawDocument.Load(stream); var definition = raw.Sections.Single(s => s.Name == "CLASSES").Records.Single(r => r.Name == "CLASS" && r.Tags.Any(t => t.Code == 1 && (string)t.Value == "SUN"));
        Equal("SCENEOE", (string)definition.Tags.Single(t => t.Code == 3).Value, "Native application name"); Equal(1153, (int)definition.Tags.Single(t => t.Code == 90).Value, "Native proxy flags"); Equal(1, (int)definition.Tags.Single(t => t.Code == 91).Value, "Physical SUN count");
    }
    private static bool SunRemove(DxfDocument doc, DxfObject host)
        => host is View view ? doc.Views.Remove(view) : host is VPort vport ? doc.VPorts.Remove(vport) : doc.Entities.Remove((Viewport)host);
    private static void SunLifecycle(DxfVersion version, bool binary, string kind)
    {
        var (doc, host, sun) = SunSetup(version, kind); var extension = new DxfDictionary(); var record = new DxfXRecord(); record.Data.Add(new DxfTag(331, host.Handle)); record.Data.Add(new DxfTag(340, sun.Handle)); extension.Add("POINTERS", record); doc.Objects.SetExtensionDictionary(sun, extension);
        sun.PersistentReactors.Add(host);
        var app = new XData(new ApplicationRegistry("SUN_TEST")); app.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, host.Handle)); sun.XData.Add(app);
        SunThrows<NotSupportedException>(() => ((ICloneable)host).Clone(), "Ordinary owner clone cannot lose SUN"); Check(!SunRemove(doc, host), "Owned SUN prevents host removal");
        var destination = new DxfDocument(version); var target = SunHost(destination, kind, "CopiedSunOwner");
        var copy = destination.Objects.CloneSun(sun, target); Check(!ReferenceEquals(sun, copy), "Independent SUN clone"); SunAssert(copy, target);
        var copiedRecord = (DxfXRecord)copy.ExtensionDictionary!["POINTERS"]; Equal(target.Handle, (string)copiedRecord.Data[0].Value, "Source host remapped"); Equal(copy.Handle, (string)copiedRecord.Data[1].Value, "Internal SUN remapped");
        Check(ReferenceEquals(copy.PersistentReactors.Single(), target), "Owner reactor remapped"); Equal(target.Handle, (string)copy.XData["SUN_TEST"].XDataRecord[0].Value, "XData host remapped");
        Equal(0, destination.Objects.Validate().Count, "Cloned SUN graph validation"); destination = SunRoundTrip(destination, binary); copy = destination.Objects.Items.OfType<DxfSun>().Single(); target = copy.Owner; copiedRecord = (DxfXRecord)copy.ExtensionDictionary!["POINTERS"];
        var incoming = new DxfIdBuffer(); incoming.References.Add(copy); destination.NamedObjects.Add("INCOMING", incoming); string id = copy.Handle; int count = destination.Objects.Items.Count;
        SunThrows<InvalidOperationException>(() => destination.Objects.EraseOwnedTree(copy), "Incoming pointer blocks erasure"); Equal(count, destination.Objects.Items.Count, "Blocked erasure atomic"); Check(ReferenceEquals(SunOf(target), copy), "Blocked erasure retains slot");
        incoming.References.Clear(); destination.Objects.EraseOwnedTree(copy); Check(copy.IsErased && copiedRecord.IsErased && copy.ExtensionDictionary!.IsErased, "Complete owned subtree erased"); Equal(null, SunOf(target), "SUN slot cleared"); Equal(null, destination.GetObjectByHandle(id), "SUN handle unregistered");
        SunThrows<InvalidOperationException>(() => copy.Enabled = false, "Erased SUN immutable"); SunThrows<ArgumentException>(() => destination.Objects.SetSun(target, copy), "Erased SUN cannot reattach");
        Check(SunRemove(destination, target), "Host removable after explicit SUN erasure"); Equal(0, destination.Objects.Validate().Count, "Erased graph validates");
    }
    private static void SunHostPrivateContext(DxfVersion version, bool binary, string kind)
    {
        foreach (int context in new[] { 0, 1, 2 })
        {
            var (doc, host, sun) = SunSetup(version, kind); using var seed = new MemoryStream(); Check(doc.Save(seed, binary), "Host context seed"); seed.Position = 0; var raw = DxfRawDocument.Load(seed);
            var record = raw.Sections.SelectMany(section => section.Records).Single(record => record.Name == kind && record.Tags.Any(tag => tag.Code == 5 && (string)tag.Value == host.Handle));
            DxfTag[] decoy = context == 2 ? new[] { new DxfTag(1001, "SUN_TRAILER"), new DxfTag(1000, "Stored metadata"), new DxfTag(361, "7ABCDE") } : context == 1 ? new[] { new DxfTag(100, "PrivateSunCarrier"), new DxfTag(361, "7ABCDE") } : new[] { new DxfTag(102, "{PRIVATE_SUN"), new DxfTag(102, "{NESTED"), new DxfTag(361, "7ABCDE"), new DxfTag(102, "}"), new DxfTag(102, "}") };
            raw = raw.WithRecord(record, record.Tags.Concat(decoy)); var loaded = SunLoad(raw); Check(ReferenceEquals(SunOf(loaded.GetObjectByHandle(host.Handle)), loaded.GetObjectByHandle(sun.Handle)), "Private group, later subclass or XData trailer does not redefine public SUN slot");
        }
    }
    private static void SunMalformed(DxfVersion version, bool binary, string defect)
    {
        var raw = SunRaw(version, binary); var sun = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "SUN"); string sunId = (string)sun.Tags.Single(t => t.Code == 5).Value;
        var host = raw.Sections.Single(s => s.Name == "TABLES").Records.Single(r => r.Name == "VPORT" && r.Tags.Any(t => t.Code == 361 && (string)t.Value == sunId));
        var tags = sun.Tags.ToList();
        void Set(short code, object value) { int at = tags.FindIndex(t => t.Code == code); tags[at] = new DxfTag(code, value); }
        switch (defect)
        {
            case "duplicate-marker": tags.Add(new DxfTag(100, "AcDbSun")); break;
            case "nonfinite":
                using (var output = new MemoryStream())
                {
                    raw.Save(output); byte[] bytes = output.ToArray();
                    if (binary) { int at = bytes.AsSpan().IndexOf(BitConverter.GetBytes(-2.125)); Check(at >= 0, "SUN nonfinite injection target"); BitConverter.GetBytes(double.PositiveInfinity).CopyTo(bytes, at); }
                    else bytes = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(bytes).Replace("-2.125", "Infinity"));
                    bool failed = false; try { failed = DxfDocument.Load(new MemoryStream(bytes)) == null; } catch (Exception error) when (error is FormatException || error is ArgumentException || error is InvalidDataException) { failed = true; }
                    Check(failed, "SUN nonfinite raw value rejected");
                }
                return;
            case "missing-version": tags.RemoveAll(t => t.Code == 90); break;
            case "duplicate-version": tags.Add(new DxfTag(90, 1)); break;
            case "missing-time": tags.RemoveAll(t => t.Code == 92); break;
            case "duplicate-intensity": tags.Add(new DxfTag(40, 2.0)); break;
            case "color-low": Set(63, (short)-1); break;
            case "color-high": Set(63, (short)257); break;
            case "rgb-low": Set(421, -1); break;
            case "rgb-high": Set(421, 0x1000000); break;
            case "shadow-low": Set(70, (short)-1); break;
            case "shadow-high": Set(70, (short)3); break;
            case "map-small": Set(71, (short)32); break;
            case "map-nonpower": Set(71, (short)100); break;
            case "map-large": Set(71, (short)8192); break;
            case "softness-low": Set(280, (short)-1); break;
            case "softness-high": Set(280, (short)256); break;
            case "owner-missing": tags.RemoveAll(t => t.Code == 330); break;
            case "owner-wrong": Set(330, (string)raw.Sections.Single(s => s.Name == "OBJECTS").Records.First(r => r.Name == "DICTIONARY").Tags.Single(t => t.Code == 5).Value); break;
            case "slot-missing": raw = raw.WithRecord(host, host.Tags.Where(t => t.Code != 361)); break;
            case "slot-wrong-kind": raw = raw.WithRecord(host, host.Tags.Select(t => t.Code == 361 ? new DxfTag(361, (string)host.Tags.Single(x => x.Code == 5).Value) : t)); break;
            case "slot-missing-target": raw = raw.WithRecord(host, host.Tags.Select(t => t.Code == 361 ? new DxfTag(361, "7ABCDE") : t)); break;
            case "slot-duplicate": raw = raw.WithRecord(host, host.Tags.Concat(new[] { new DxfTag(361, sunId) })); break;
        }
        sun = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "SUN"); raw = raw.WithRecord(sun, tags);
        using var input = new MemoryStream(); raw.Save(input); input.Position = 0; bool rejected = false;
        try { rejected = DxfDocument.Load(input) == null; } catch (Exception error) when (error is FormatException || error is ArgumentException || error is InvalidDataException) { rejected = true; }
        Check(rejected, "Malformed SUN rejected: " + defect);
    }
    private static void SunOpaque(DxfVersion version, bool binary, string variant)
    {
        var raw = SunRaw(version, binary); var sun = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "SUN"); var tags = sun.Tags.ToList();
        if (variant == "version") tags[tags.FindIndex(t => t.Code == 90)] = new DxfTag(90, 2);
        if (variant == "field") tags.Add(new DxfTag(300, "PRIVATE"));
        if (variant == "subclass") tags.Add(new DxfTag(100, "PrivateSun"));
        if (variant == "header") tags.Insert(tags.FindIndex(t => t.Code == 100), new DxfTag(1, "PRIVATE"));
        raw = raw.WithRecord(sun, tags); var doc = SunLoad(raw); var opaque = doc.Objects.Items.OfType<DxfOpaqueObject>().Single(o => o.CodeName == "SUN"); Check(ReferenceEquals(SunOf(opaque.Owner), opaque), "Opaque SUN reciprocal slot retained");
        SunThrows<NotSupportedException>(() => doc.Objects.EraseOwnedTree(opaque), "Opaque erasure rejected"); SunThrows<NotSupportedException>(() => ((ICloneable)opaque.Owner).Clone(), "Opaque SUN prevents host clone loss");
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Opaque SUN output"); output.Position = 0; var after = DxfRawDocument.Load(output).Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "SUN"); Check(OwnershipTagValues(tags).SequenceEqual(OwnershipTagValues(after.Tags)), "Whole opaque SUN record retained");
    }
    private static void SunNullIdentity(DxfVersion version, bool binary)
    {
        var raw = SunRaw(version, binary); var sun = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "SUN"); string id = (string)sun.Tags.Single(t => t.Code == 5).Value;
        var host = raw.Sections.Single(s => s.Name == "TABLES").Records.Single(r => r.Name == "VPORT" && r.Tags.Any(t => t.Code == 361));
        raw = raw.WithRecord(sun, sun.Tags.Select(t => t.Code == 5 ? new DxfTag(5, "000" + id.ToLowerInvariant()) : t));
        host = raw.Sections.Single(section => section.Name == "TABLES").Records.Single(record => record.Name == "VPORT" && record.Tags.Any(tag => tag.Code == 361));
        raw = raw.WithRecord(host, host.Tags.Select(t => t.Code == 361 ? new DxfTag(361, "00" + id.ToLowerInvariant()) : t));
        var doc = SunLoad(raw); Equal(1, doc.Objects.Items.OfType<DxfSun>().Count(), "Normalized physical identity accepted");
        sun = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "SUN"); raw = raw.WithoutRecord(sun); host = raw.Sections.Single(s => s.Name == "TABLES").Records.Single(r => r.Name == "VPORT" && r.Tags.Any(t => t.Code == 361)); raw = raw.WithRecord(host, host.Tags.Select(t => t.Code == 361 ? new DxfTag(361, "000") : t));
        doc = SunLoad(raw); Check(doc.VPorts.Records.All(v => v.Sun == null), "Numeric zero SUN slot remains empty"); doc = SunRoundTrip(doc, binary); Check(doc.VPorts.Records.All(v => v.Sun == null), "Explicit null save/load");
    }
    private static void SunAtomicity()
    {
        var (doc, host, sun) = SunSetup(DxfVersion.AutoCad2018); int count = doc.Objects.Items.Count; string seed = doc.DrawingVariables.HandleSeed;
        SunThrows<InvalidOperationException>(() => doc.Objects.SetSun(host, new DxfSun()), "Occupied slot rejects"); Equal(count, doc.Objects.Items.Count, "Occupied slot atomic"); Equal(seed, doc.DrawingVariables.HandleSeed, "Occupied slot allocates no handle");
        SunThrows<ArgumentException>(() => doc.NamedObjects.Add("INVALID_SUN", new DxfSun()), "Dictionary cannot own SUN");
        foreach (var version in new[] { DxfVersion.AutoCad2000, DxfVersion.AutoCad2004 })
        { var old = new DxfDocument(version); var target = SunHost(old, "VPORT", "Old"); _ = old.Objects; int before = old.Objects.Items.Count; SunThrows<NotSupportedException>(() => old.Objects.SetSun(target, new DxfSun()), "Old profile rejects SUN"); Equal(before, old.Objects.Items.Count, "Old profile atomic"); }
        var r2007 = new DxfDocument(DxfVersion.AutoCad2007); var view = SunHost(r2007, "VIEW", "OldView"); SunThrows<NotSupportedException>(() => r2007.Objects.SetSun(view, new DxfSun()), "Named VIEW conservative R2010 gate");
        var layout = new Layout("UnregisteredViewport"); doc.Layouts.Add(layout);
        Check(!ReferenceEquals(doc.GetObjectByHandle(layout.Viewport.Handle), layout.Viewport), "Fixture exposes retained-only layout viewport");
        { int before = doc.Objects.Items.Count; SunThrows<ArgumentException>(() => doc.Objects.SetSun(layout.Viewport, new DxfSun()), "Retained-only layout viewport is outside registered host scope"); Equal(before, doc.Objects.Items.Count, "Retained-only attachment atomic"); }
        var detached = new DxfSun(); SunThrows<ArgumentOutOfRangeException>(() => detached.Intensity = double.NaN, "Nonfinite API"); SunThrows<ArgumentOutOfRangeException>(() => detached.ShadowMapSize = 100, "Invalid map API");
        var other = new DxfDocument(DxfVersion.AutoCad2018); var targetHost = SunHost(other, "VPORT", "Target"); _ = other.Objects; int originalCount = other.Objects.Items.Count;
        SunThrows<ArgumentException>(() => other.Objects.CloneSun(sun, targetHost, new Dictionary<DxfObject, DxfObject> { [host] = other.Layers["0"] }), "Conflicting owner mapping rejects"); Equal(originalCount, other.Objects.Items.Count, "Clone map failure atomic");
        var pointer = new DxfPlaceholder(); doc.NamedObjects.Add("EXTERNAL", pointer); sun.PersistentReactors.Add(pointer);
        SunThrows<InvalidOperationException>(() => other.Objects.CloneSun(sun, targetHost), "Missing external mapping rejects"); Equal(originalCount, other.Objects.Items.Count, "Missing map allocates no object"); Check(SunOf(targetHost) == null, "Missing map leaves slot empty");
        var replacement = new DxfPlaceholder(); other.NamedObjects.Add("REPLACEMENT", replacement); var clone = other.Objects.CloneSun(sun, targetHost, new Dictionary<DxfObject, DxfObject> { [pointer] = replacement }); Check(ReferenceEquals(clone.PersistentReactors.Single(), replacement), "Explicit external map accepted");
    }
    private static void SunReentrancy()
    {
        var (source, owner, sun) = SunSetup(DxfVersion.AutoCad2018); var target = new DxfDocument(DxfVersion.AutoCad2018); var host = SunHost(target, "VPORT", "Target"); _ = target.Objects;
        int afterCallback = 0;
        SunThrows<InvalidOperationException>(() => target.Objects.CloneSun(sun, host, new ContainerCallbackMappings(() => { target.Objects.SetSun(host, new DxfSun()); afterCallback = target.Objects.Items.Count; })), "Mapping callback fills destination");
        Equal(afterCallback, target.Objects.Items.Count, "Destination callback causes no extra registration"); Check(SunOf(host) != null, "Callback attachment retained"); target.Objects.EraseOwnedTree((DxfSun)SunOf(host)!);
        SunThrows<ArgumentException>(() => target.Objects.CloneSun(sun, host, new ContainerCallbackMappings(() => source.Objects.EraseOwnedTree(sun))), "Mapping callback erases source"); Check(sun.IsErased && SunOf(host) == null, "Erased source cannot produce clone");
        var authored = new DxfSun(); var app = new MetadataCallbackRegistry("SUN_CALLBACK"); int calls = 0; app.Callback = () => { calls++; target.VPorts.Remove((VPort)host); }; authored.XData.Add(new XData(app)); target.Objects.SetSun(host, authored);
        Equal(0, calls, "SUN registration does not invoke APPID override"); Check(ReferenceEquals(target.GetObjectByHandle(host.Handle), host), "SUN owner remains registered");
        var registeredApp = new MetadataCallbackRegistry("SUN_CLONE_CALLBACK"); target.ApplicationRegistries.Add(registeredApp); authored.XData.Add(new XData(registeredApp)); registeredApp.Callback = () => { calls++; target.Objects.EraseOwnedTree(authored); };
        var secondHost = SunHost(target, "VPORT", "Second"); var copy = target.Objects.CloneSun(authored, secondHost); Equal(0, calls, "SUN clone copies stored metadata without override"); Check(!authored.IsErased && ReferenceEquals(SunOf(secondHost), copy), "SUN clone remains coherent");
        var recordOne = target.VPorts.AddRecord(new VPort("MULTIPLE")); var recordTwo = target.VPorts.AddRecord(new VPort("MULTIPLE")); target.Objects.SetSun(recordTwo, new DxfSun()); int count = target.VPorts.Records.Count;
        Check(!target.VPorts.Remove("MULTIPLE") && target.VPorts.Records.Count == count && recordOne.Owner != null, "Configuration removal preflights all physical owners atomically");
    }
    private static void SunOlderOpaque(DxfVersion version, bool binary)
    {
        var raw = SunRaw(DxfVersion.AutoCad2007, binary); var host = raw.Sections.Single(section => section.Name == "TABLES").Records.Single(record => record.Name == "VPORT" && record.Tags.Any(tag => tag.Code == 361)); raw = raw.WithRecord(host, host.Tags.Where(tag => tag.Code != 361));
        var root = raw.Sections.Single(section => section.Name == "OBJECTS").Records.First(record => record.Name == "DICTIONARY"); string rootId = (string)root.Tags.Single(tag => tag.Code == 5).Value;
        var sun = raw.Sections.Single(section => section.Name == "OBJECTS").Records.Single(record => record.Name == "SUN"); string sunId = (string)sun.Tags.Single(tag => tag.Code == 5).Value; raw = raw.WithRecord(sun, sun.Tags.Select(tag => tag.Code == 330 ? new DxfTag(330, rootId) : tag));
        root = raw.Sections.Single(section => section.Name == "OBJECTS").Records.First(record => record.Name == "DICTIONARY"); raw = raw.WithRecord(root, root.Tags.Concat(new[] { new DxfTag(3, "OLD_SUN"), new DxfTag(360, sunId) }));
        var tags = raw.Tags.ToList(); tags[tags.FindIndex(tag => tag.Code == 9 && (string)tag.Value == "$ACADVER") + 1] = new DxfTag(1, version == DxfVersion.AutoCad2000 ? "AC1015" : "AC1018");
        var older = DxfRawDocument.Create(tags, binary); var doc = SunLoad(older); Check(doc.NamedObjects["OLD_SUN"] is DxfOpaqueObject, "Older SUN is entirely opaque"); doc = SunRoundTrip(doc, binary); Check(doc.NamedObjects["OLD_SUN"] is DxfOpaqueObject, "Older opaque SUN survives");
    }
    private static void SunNative(string file, bool binary)
    {
        using var compressed = File.OpenRead(Path.Combine("tests", "fixtures", "table-oracle", file + ".gz")); using var gzip = new GZipStream(compressed, CompressionMode.Decompress); using var original = new MemoryStream(); gzip.CopyTo(original);
        using var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine("tools", "table_oracle", "fixtures.json"))); string hash = inventory.RootElement.GetProperty("files").EnumerateArray().Single(v => v.GetProperty("file").GetString() == file).GetProperty("sha256").GetString()!;
        Equal(hash, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original.ToArray())).ToLowerInvariant(), "Pinned native source hash"); original.Position = 0; var source = DxfRawDocument.Load(original); var sun = source.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "SUN");
        string sourceId = (string)sun.Tags.Single(t => t.Code == 5).Value, ownerId = (string)sun.Tags.Single(t => t.Code == 330).Value;
        var sourceHost = source.Sections.Single(s => s.Name == "TABLES").Records.Single(r => r.Name == "VPORT" && r.Tags.Any(t => t.Code == 5 && (string)t.Value == ownerId)); Equal(sourceId, (string)sourceHost.Tags.Single(t => t.Code == 361).Value, "Native VPORT reciprocal 361");
        var seed = new DxfDocument(DxfVersion.AutoCad2007); var host = seed.VPorts[VPort.DefaultName]; _ = seed.Objects; using var saved = new MemoryStream(); Check(seed.Save(saved, binary), "Native extraction seed"); saved.Position = 0; var raw = DxfRawDocument.Load(saved);
        // Relocate the native SUN identity to a collision-free explicit source handle; keep every subclass value exact.
        const string importedId = "7F000"; var tags = sun.Tags.Select(t => t.Code == 5 ? new DxfTag(5, importedId) : t.Code == 330 ? new DxfTag(330, host.Handle) : t).ToArray();
        var targetHost = raw.Sections.Single(s => s.Name == "TABLES").Records.Single(r => r.Name == "VPORT" && r.Tags.Any(t => t.Code == 5 && (string)t.Value == host.Handle)); raw = raw.WithRecord(targetHost, targetHost.Tags.Concat(new[] { new DxfTag(361, importedId) })); int end = raw.Sections.Single(s => s.Name == "OBJECTS").EndTagIndex - 1; raw = raw.WithTags(raw.Tags.Take(end).Concat(tags).Concat(raw.Tags.Skip(end)));
        var doc = SunLoad(raw); var typed = (DxfSun)doc.GetObjectByHandle(importedId); Equal(DxfSunShadowType.AreaSampled, typed.ShadowType, "Native shadow type2"); Equal(54000000, typed.StoredTime, "Native raw time"); Equal((int?)0xFFFFFF, typed.TrueColor, "Native truecolor"); Equal(0, doc.Objects.Validate().Count, "Native graph validates");
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Native SUN output"); File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"sun-native-{Path.GetFileNameWithoutExtension(file)}-{binary}.dxf"), output.ToArray()); output.Position = 0; var after = DxfRawDocument.Load(output).Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "SUN"); Check(OwnershipTagValues(tags).SequenceEqual(OwnershipTagValues(after.Tags)), "Native complete record exact after disclosed identity and external owner relocation");
    }
}
