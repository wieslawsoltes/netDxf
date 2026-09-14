using System.Collections.Generic;
using netDxf;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterVPortApiTests()
    {
        Run("vport/api/configuration-operations", VPortConfigurationOperations);
        Run("vport/api/identity-and-xdata-references", VPortIdentityReferences);
        Run("vport/api/validation-nonmutation", VPortValidation);
        Run("vport/api/clone-all-stored-state", VPortCloneState);
        Run("vport/api/foreign-document-isolation", VPortForeignDocument);
        Run("vport/api/foreign-xdata-registry-isolation", VPortForeignXDataRegistry);
        Run("vport/api/rename-rejection-transaction", VPortRenameRejection);
        Run("vport/api/rename-observer-ownership", VPortRenameObserverOwnership);
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                Run($"vport/api/physical-records/{v}/{b}", () => VPortPhysicalRecords(v,b));
                Run($"vport/api/named-only-default/{v}/{b}", () => VPortNamedOnly(v,b));
                Run($"vport/api/references-after-rename/{v}/{b}", () => VPortReferencesAfterRename(v,b));
                Run($"vport/api/duplicate-handle/{v}/{b}", () => VPortDuplicateHandle(v,b));
            }
    }

    private static void VPortConfigurationOperations()
    {
        var doc = new DxfDocument();
        VPort initial = doc.Viewport;
        var upper = new VPort("Work") { LowerLeftCorner = new Vector2(0,0.5) };
        var lower = new VPort("Work") { UpperRightCorner = new Vector2(1,0.5) };
        Check(ReferenceEquals(upper,doc.VPorts.Add(upper)),"Add named configuration");
        Check(ReferenceEquals(upper,doc.VPorts.Add(lower)),"Legacy Add resolves existing configuration");
        Check(lower.Owner == null,"Skipped Add mutated detached tile");
        doc.VPorts.AddRecord(lower);
        Check(ReferenceEquals(lower,doc.VPorts.AddRecord(lower)),"Adding same physical record must be idempotent");
        Equal(2,doc.VPorts.Count,"Configuration count"); Equal(3,doc.VPorts.Records.Count,"Physical record count");
        Equal(2,doc.VPorts.GetConfiguration("wORk").Count,"Case-insensitive grouping");
        Equal(2,doc.VPorts.Count(),"Enumeration exposes configuration representatives");
        var snapshot=doc.VPorts.GetConfiguration("Work");
        lower.Name="Review";
        Check(ReferenceEquals(lower,doc.VPorts["Review"]),"Renamed tile index");
        Equal(2,snapshot.Count,"Configuration snapshot changed after rename");
        upper.Name="Review";
        Check(ReferenceEquals(upper,doc.VPorts["Review"]),"Renaming earlier record must preserve first-by-file-order representative");
        Equal(2,doc.VPorts.GetConfiguration("Review").Count,"Rename into existing configuration");
        Check(!doc.VPorts.Contains("Work"),"Old configuration remained indexed");
        string upperHandle=upper.Handle;
        Check(doc.VPorts.Remove(upper),"Remove representative tile");
        Check(doc.GetObjectByHandle(upperHandle)==null && upper.Owner==null && upper.Handle==null,"Removed tile identity remained registered");
        Check(ReferenceEquals(lower,doc.VPorts["Review"]),"Remaining tile was not promoted");
        Check(doc.VPorts.Remove("review"),"Remove named configuration");
        Check(!doc.VPorts.Remove("review"),"Absent configuration should not remove");
        var active2=VPort.Active; doc.VPorts.AddRecord(active2);
        Check(doc.VPorts.Remove(initial),"Removing first active while another exists");
        Check(ReferenceEquals(active2,doc.Viewport),"Second active did not become current");
        Check(!doc.VPorts.Remove(active2) && !doc.VPorts.Remove("*ACTIVE"),"Last active removed");
        doc.VPorts.AddRecord(new VPort("Clear")); doc.VPorts.AddRecord(new VPort("Clear"));
        doc.VPorts.Clear(); Equal(1,doc.VPorts.Records.Count,"Clear did not remove complete named configurations");
        Throws<ArgumentException>(()=>active2.Name="CannotRenameActive");
        Throws<ArgumentNullException>(()=>doc.VPorts.AddRecord(null!));
        Throws<ArgumentNullException>(()=>doc.VPorts.GetConfiguration(null!));
    }

    private static void VPortIdentityReferences()
    {
        VPort one=new VPort("same"), two=new VPort("same");
        Check(!one.Equals(two) && !((TableObject)one).Equals(two) && !((object)one).Equals(two),"Same-name records compare equal");
        Check(((IEquatable<TableObject>)one).Equals(one),"Identity equality through interface");
        var dictionary = new Dictionary<TableObject,int> {{one,1},{two,2}};
        int hash=one.GetHashCode(); one.Name="renamed";
        Equal(hash,one.GetHashCode(),"Mutable-name hash changed"); Equal(1,dictionary[one],"Renaming corrupted dictionary identity");
        Check(new View("Named").Equals(new View("named")),"Unrelated table equality changed");
        TableObject namedView = new View("same");
        Check(namedView.Equals(new UCS("same")),"Legacy non-VPORT cross-table name equality changed");
        Check(!namedView.Equals(two) && !((TableObject)two).Equals(namedView),"Cross-table typed equality is asymmetric");
        Check(!((object)namedView).Equals(two) && !((object)two).Equals(namedView),"Cross-table object equality is asymmetric");
        Check(!((IEquatable<TableObject>)namedView).Equals(two) && !((IEquatable<TableObject>)two).Equals(namedView),"Cross-table interface equality is asymmetric");
        var doc=new DxfDocument();
        one.XData.Add(new XData(new ApplicationRegistry(VPortApplication))); two.XData.Add(new XData(new ApplicationRegistry(VPortApplication)));
        doc.VPorts.AddRecord(one); doc.VPorts.AddRecord(two);
        var refs=doc.ApplicationRegistries.GetReferences(VPortApplication);
        Equal(2,refs.Count,"Distinct tile XData references collapsed");
        one.Name="second rename"; Check(doc.VPorts.Remove(one),"Rename/remove tile");
        refs=doc.ApplicationRegistries.GetReferences(VPortApplication);
        Equal(1,refs.Count,"Rename/remove orphaned registry reference");
        Check(ReferenceEquals(two,refs[0].Reference),"Removed wrong tile reference");
    }

    private static void VPortValidation()
    {
        var record=new VPort("Validation");
        Throws<ArgumentNullException>(()=>new VPort(null!)); Throws<ArgumentNullException>(()=>new VPort("  "));
        Throws<ArgumentException>(()=>new VPort("*Other"));
        Check(new VPort("*aCtIvE").IsReserved,"Active constructor did not recognize case");
        foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
        {
            Throws<ArgumentOutOfRangeException>(()=>record.LowerLeftCorner=new Vector2(bad,0));
            Throws<ArgumentOutOfRangeException>(()=>record.ViewTarget=new Vector3(0,bad,0));
            Throws<ArgumentOutOfRangeException>(()=>record.ViewDirection=new Vector3(0,1,bad));
            Throws<ArgumentOutOfRangeException>(()=>record.ViewHeight=bad);
            Throws<ArgumentOutOfRangeException>(()=>record.ViewAspectRatio=bad);
            Throws<ArgumentOutOfRangeException>(()=>record.LensLength=bad);
            Throws<ArgumentOutOfRangeException>(()=>record.FrontClippingPlane=bad);
            Throws<ArgumentOutOfRangeException>(()=>record.UcsElevation=bad);
        }
        Throws<ArgumentException>(()=>record.ViewDirection=Vector3.Zero);
        Throws<ArgumentException>(()=>record.UcsXAxis=Vector3.Zero);
        Throws<ArgumentOutOfRangeException>(()=>record.ViewHeight=0);
        Throws<ArgumentOutOfRangeException>(()=>record.ViewAspectRatio=-1);
        Throws<ArgumentOutOfRangeException>(()=>record.CircleSides=0);
        Throws<ArgumentOutOfRangeException>(()=>record.UcsIcon=4);
        Throws<ArgumentOutOfRangeException>(()=>record.SnapStyle=2);
        Throws<ArgumentOutOfRangeException>(()=>record.SnapIsopair=3);
        Throws<ArgumentOutOfRangeException>(()=>record.RenderMode=(ViewRenderMode)7);
        Throws<ArgumentOutOfRangeException>(()=>record.ViewMode=(ViewModeFlags)65536);
        Equal(Vector3.UnitZ,record.ViewDirection,"Rejected vector assignment mutated state");
        Equal(Vector2.Zero,record.LowerLeftCorner,"Rejected corner assignment mutated state");
        Near(10,record.ViewHeight,"Rejected scalar assignment mutated state");
    }

    private static void VPortCloneState()
    {
        using var input=new MemoryStream(RawFixtureBytes(VPortFixtureTags(DxfVersion.AutoCad2018),false));
        var doc=DxfDocument.Load(input)!; var source=doc.VPorts.Records[2];
        var clone=(VPort)source.Clone("Renamed");
        foreach(var property in typeof(VPort).GetProperties().Where(p=>p.CanRead&&p.CanWrite&&p.Name!="Owner"&&p.Name!="Name"&&p.Name!="Handle"))
            Equal(property.GetValue(source),property.GetValue(clone),"Cloned VPORT property "+property.Name);
        Check(!clone.Equals(source)&&clone.Owner==null&&clone.Handle==null,"Clone shares physical identity");
        var active=(VPort)doc.Viewport.Clone();
        Equal(doc.Viewport.SnapMode,active.SnapMode,"SnapMode clone");
        Equal(doc.Viewport.LowerLeftCorner,active.LowerLeftCorner,"Active rectangle clone");
    }

    private static void VPortRenameRejection()
    {
        var doc = new DxfDocument(); var record = new VPort("Before"); doc.VPorts.AddRecord(record);
        string handle = record.Handle;
        record.NameChanged += (_, _) => throw new InvalidOperationException("Rejected by observer");
        Throws<InvalidOperationException>(() => record.Name = "After");
        Equal("Before", record.Name, "Rejected rename changed record");
        Check(ReferenceEquals(record,doc.VPorts["Before"]) && !doc.VPorts.Contains("After"), "Rejected rename changed index");
        Equal(1,doc.VPorts.GetConfiguration("Before").Count,"Rejected rename changed configuration");
        Check(ReferenceEquals(record,doc.GetObjectByHandle(handle)),"Rejected rename changed identity");
        var blank = new VPort("Valid");
        Throws<ArgumentException>(() => blank.Name = "   ");
        Equal("Valid",blank.Name,"Blank rename changed detached name");
        Throws<ArgumentException>(() => ((TableObject)blank).Name = "   ");
        var early = new VPort("Early");
        early.NameChanged += (_,_) => throw new InvalidOperationException("Early observer");
        doc.VPorts.AddRecord(early);
        Throws<InvalidOperationException>(() => early.Name = "NewEarly");
        Check(ReferenceEquals(early,doc.VPorts["Early"]) && !doc.VPorts.Contains("NewEarly"),"Early observer rejection changed index");
    }

    private static void VPortRenameObserverOwnership()
    {
        var first = new DxfDocument(); var second = new DxfDocument();
        var attached = new VPort("Detached");
        attached.NameChanged += (_,_) => first.VPorts.AddRecord(attached);
        attached.Name = "Attached";
        Check(ReferenceEquals(attached,first.VPorts["Attached"]) && !first.VPorts.Contains("Detached"),"Attach during rename left stale index");
        var moved = new VPort("Source"); first.VPorts.AddRecord(moved);
        moved.NameChanged += (_,_) => { Check(first.VPorts.Remove(moved),"Observer remove"); second.VPorts.AddRecord(moved); };
        moved.Name = "Destination";
        Check(!first.VPorts.Contains("Source") && !first.VPorts.Contains("Destination"),"Move during rename left source index");
        Check(ReferenceEquals(moved,second.VPorts["Destination"]) && !second.VPorts.Contains("Source"),"Move during rename left destination index");
        var removed = new VPort("Removed"); first.VPorts.AddRecord(removed);
        removed.NameChanged += (_,_) => first.VPorts.Remove(removed);
        removed.Name = "DetachedAfterEvent";
        Check(removed.Owner == null && !first.VPorts.Contains("Removed") && !first.VPorts.Contains("DetachedAfterEvent"),"Removal during rename created stale index");
    }

    private static void VPortForeignDocument()
    {
        var first=new DxfDocument(); var second=new DxfDocument(); var record=new VPort("Tile");
        first.VPorts.AddRecord(record); string handle=record.Handle;
        Throws<ArgumentException>(()=>second.VPorts.AddRecord(record));
        Check(ReferenceEquals(record.Owner,first.VPorts)&&ReferenceEquals(first.GetObjectByHandle(handle),record),"Foreign insert changed source ownership");
        Equal(1,second.VPorts.Records.Count,"Foreign insert mutated destination");
        var copy=(VPort)record.Clone(); second.VPorts.AddRecord(copy);
        Check(ReferenceEquals(copy.Owner,second.VPorts)&&!ReferenceEquals(copy,record),"Cloned import failed");
    }

    private static void VPortForeignXDataRegistry()
    {
        var first = new DxfDocument(); var second = new DxfDocument();
        var registry = first.ApplicationRegistries.Add(new ApplicationRegistry("FOREIGN_VPORT"));
        string registryHandle = registry.Handle;
        var record = new VPort("Unowned"); record.XData.Add(new XData(registry));
        Throws<ArgumentException>(() => second.VPorts.AddRecord(record));
        Check(ReferenceEquals(registry.Owner,first.ApplicationRegistries) && registry.Handle == registryHandle,"Foreign XData registry ownership changed");
        Check(ReferenceEquals(first.GetObjectByHandle(registryHandle),registry),"Foreign registry handle index changed");
        Check(record.Owner == null && record.Handle == null,"Rejected foreign metadata assigned record ownership");
        Equal(1,second.VPorts.Records.Count,"Rejected foreign metadata changed destination records");
        Check(!second.ApplicationRegistries.Contains("FOREIGN_VPORT"),"Rejected foreign metadata created destination registry");
        var clone = (VPort)record.Clone(); second.VPorts.AddRecord(clone);
        Check(ReferenceEquals(clone.XData["FOREIGN_VPORT"].ApplicationRegistry.Owner,second.ApplicationRegistries),"Independent XData clone did not import");
        Check(ReferenceEquals(registry.Owner,first.ApplicationRegistries),"Cloned metadata import changed source registry");
    }

    private static void VPortPhysicalRecords(DxfVersion version,bool binary)
    {
        using var input=new MemoryStream(RawFixtureBytes(VPortFixtureTags(version),binary));
        var doc=DxfDocument.Load(input)!;
        Equal(4,doc.VPorts.Records.Count,"Loaded physical records"); Equal(2,doc.VPorts.Count,"Loaded configurations");
        Equal(2,doc.VPorts.GetConfiguration("*Active").Count,"Loaded active tiles");
        Equal(2,doc.VPorts.GetConfiguration(VPortConfigurationName).Count,"Loaded named tiles");
        Check(ReferenceEquals(doc.Viewport,doc.VPorts.Records[0]),"Current viewport not first active");
        var refs=doc.ApplicationRegistries.GetReferences(VPortApplication);
        Equal(5,refs.Count,"Table plus four tile XData references");
    }

    private static void VPortNamedOnly(DxfVersion version,bool binary)
    {
        var tags=VPortFixtureTags(version,count:1);
        int marker=tags.FindIndex(t=>t.Code==100&&Equals(t.Value,"AcDbViewportTableRecord"));
        int name=tags.FindIndex(marker,t=>t.Code==2); tags[name]=new(2,"Saved");
        using var input=new MemoryStream(RawFixtureBytes(tags,binary));
        var doc=DxfDocument.Load(input)!;
        Equal(2,doc.VPorts.Records.Count,"Named-only table must gain current viewport");
        Equal("Saved",doc.VPorts.Records[0].Name,"Named-only record order");
        Check(string.Equals(doc.Viewport.Name,VPort.DefaultName,StringComparison.OrdinalIgnoreCase),"Default active missing");
        Check(ReferenceEquals(doc.GetObjectByHandle("100"),doc.VPorts["Saved"]),"Named-only handle lost");
    }

    private static void VPortReferencesAfterRename(DxfVersion version,bool binary)
    {
        using var input=new MemoryStream(RawFixtureBytes(VPortFixtureTags(version),binary));
        var doc=DxfDocument.Load(input)!; VPort tile=doc.VPorts.Records[2]; string handle=tile.Handle;
        tile.Name="Renamed Tile";
        Check(doc.VPorts.Remove(tile),"Renamed loaded tile removal");
        Check(doc.GetObjectByHandle(handle)==null,"Removed loaded tile handle retained");
        Equal(4,doc.ApplicationRegistries.GetReferences(VPortApplication).Count,"Renamed loaded reference count");
        using var output=new MemoryStream(); Check(doc.Save(output,binary),"Post-removal save failed"); output.Position=0;
        var loaded=DxfDocument.Load(output)!; Equal(3,loaded.VPorts.Records.Count,"Removed record reappeared");
    }

    private static void VPortDuplicateHandle(DxfVersion version,bool binary)
    {
        var tags=VPortFixtureTags(version);
        int second=tags.FindIndex(t=>t.Code==5&&Equals(t.Value,"101")); tags[second]=new(5,"100");
        using var input=new MemoryStream(RawFixtureBytes(tags,binary));
#if DEBUG
        Throws<ArgumentException>(()=>DxfDocument.Load(input));
#else
        Check(DxfDocument.Load(input)==null,"Duplicate VPORT identity accepted");
#endif
    }
}
