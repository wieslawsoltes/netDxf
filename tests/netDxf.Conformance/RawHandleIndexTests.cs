using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static IEnumerable<DxfVersion> HandleProfiles => new[] { DxfVersion.AutoCad12, DxfVersion.AutoCad13, DxfVersion.AutoCad14 }.Concat(SupportedVersions);
    private static string HandleProfileName(DxfVersion version) => version switch
    {
        DxfVersion.AutoCad12 => "AC1009", DxfVersion.AutoCad13 => "AC1012", DxfVersion.AutoCad14 => "AC1014", _ => HeaderVersion(version)
    };
    private static void RegisterRawHandleIndexTests()
    {
        foreach (DxfVersion version in HandleProfiles)
        foreach (bool binary in new[] { false, true })
        {
            DxfVersion v = version; bool b = binary;
            Run($"handles/contexts/{v}/{b}", () => HandleContexts(v, b));
            foreach (int variant in Enumerable.Range(0, 7))
            {
                int k = variant;
                Run($"handles/diagnostics/{v}/{b}/{k}", () => HandleDiagnostics(v, b, k));
            }
        }
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        {
            DxfVersion v = version; bool b = binary;
            Run($"handles/typed-output/{v}/{b}", () => HandleTypedOutput(v, b));
        }
        Run("handles/budgets-cancellation-and-stale-records", HandleIndexGuards);
        Run("handles/deep-owner-chain-no-recursion", HandleDeepChain);
        Run("handles/header-and-xrecord-xdata-context", HandleHeaderAndXRecord);
        Run("handles/read-only-concurrent-lookup", HandleConcurrentLookup);
    }

    private static List<DxfTag> HandleFixture(DxfVersion version)
    {
        return new List<DxfTag>
        {
            new(0,"SECTION"),new(2,"HEADER"),new(9,"$ACADVER"),new(1,HandleProfileName(version)),
            new(9,"$DWGCODEPAGE"),new(3,"ANSI_1252"),new(9,"$HANDSEED"),new(5,"0000AB"),new(0,"ENDSEC"),
            new(0,"SECTION"),new(2,"TABLES"),new(0,"TABLE"),new(2,"DIMSTYLE"),new(5,"11"),
            new(0,"DIMSTYLE"),new(105,"000d"),DxfTag.CreateDimensionStyleArrowName("00aB"),new(330,"11"),
            new(100,"AcDbSymbolTableRecord"),new(100,"AcDbDimStyleTableRecord"),new(2,"Style"),
            new(0,"ENDTAB"),new(0,"ENDSEC"),
            new(0,"SECTION"),new(2,"ENTITIES"),new(0,"LINE"),new(5,"00aB"),
            new(102,"{ACAD_REACTORS"),new(330,"E"),new(102,"}"),
            new(102,"{ACAD_XDICTIONARY"),new(360,"C"),new(102,"}"),
            new(102,"{VENDOR"),new(5,"ab"),new(330,"DEADBEEF"),
            new(102,"{ACAD_REACTORS"),new(330,"C"),new(102,"}"),new(102,"}"),
            new(330,"10"),new(100,"AcDbEntity"),new(8,"0"),new(100,"AcDbLine"),
            new(10,1.0),new(20,2.0),new(30,3.0),new(11,4.0),new(21,5.0),new(31,6.0),
            new(330,"F"),new(340,"E"),new(350,"C"),new(360,"E"),new(390,"C"),new(480,"E"),
            new(320,"AB"),new(1001,"HANDLES_TEST"),new(1002,"{"),new(1005,"E"),new(1002,"}"),
            new(0,"ENDSEC"),new(0,"SECTION"),new(2,"OBJECTS"),
            new(0,"DICTIONARY"),new(5,"10"),new(330,"0"),new(100,"AcDbDictionary"),new(3,"Children"),new(350,"C"),
            new(0,"DICTIONARY"),new(5,"C"),new(330,"AB"),new(100,"AcDbDictionary"),new(3,"Entry"),new(360,"E"),
            new(0,"XRECORD"),new(5,"E"),new(330,"C"),new(100,"AcDbXrecord"),new(280,(short)1),
            new(330,"DEADBEEF"),new(102,"application payload, not a control group"),new(1,"data"),
            new(0,"ENDSEC"),new(0,"SECTION"),new(2,"APP_SECTION"),new(0,"CUSTOM"),new(5,"AB"),
            new(0,"ENDSEC"),new(0,"EOF")
        };
    }
    private static DxfRawDocument HandleRoundTrip(IEnumerable<DxfTag> tags, bool binary)
    {
        var authored = DxfRawDocument.Create(tags, binary);
        using var stream = new MemoryStream(); authored.Save(stream); stream.Position = 0;
        return DxfRawDocument.Load(stream);
    }
    private static void HandleContexts(DxfVersion version, bool binary)
    {
        var doc = HandleRoundTrip(HandleFixture(version), binary);
        using var before = new MemoryStream(); doc.Save(before);
        var index = DxfRawHandleIndex.Create(doc);
        Equal(1,index.FindDefinitions("ab").Count,"Header seed/vendor/unknown identity collision");
        Equal("LINE",index.FindDefinitions("000AB").Single().Record.Name,"Numeric lookup alias");
        Equal((string)doc.Tags[index.FindDefinitions("AB").Single().TagIndex].Value,index.FindDefinitions("AB").Single().Handle,"Decoded spelling lost");
        var authoredIndex=DxfRawHandleIndex.Create(DxfRawDocument.Create(HandleFixture(version)));
        Equal("00aB",authoredIndex.FindDefinitions("AB").Single().Handle,"Authored spelling lost");
        Equal((short)105,index.FindDefinitions("D").Single().Code,"DIMSTYLE group 105 identity");
        Check(index.Occurrences.All(x=> x.Code!=5 || x.Record?.Name!="DIMSTYLE"),"DIMBLK name was indexed as handle");
        var line = index.FindDefinitions("AB").Single().Record;
        var items = index.GetOccurrences(line);
        Equal(1,items.Count(x=>x.Role==DxfRawHandleRole.Owner),"Reactor/application pointer mistaken for owner");
        Equal("10",items.Single(x=>x.Role==DxfRawHandleRole.Owner).CanonicalHandle,"Wrong common owner");
        Equal("E",items.Single(x=>x.Role==DxfRawHandleRole.Reactor).CanonicalHandle,"Reactor lost");
        Equal("C",items.Single(x=>x.Role==DxfRawHandleRole.ExtensionDictionary).CanonicalHandle,"Extension dictionary lost");
        Equal(3,items.Count(x=>x.Role==DxfRawHandleRole.Opaque),"Nested/custom handles incorrectly interpreted");
        Check(items.Any(x=>x.Code==330 && x.Role==DxfRawHandleRole.SoftPointer && x.CanonicalHandle=="F"),"Subclass pointer mistaken for owner");
        Equal("HANDLES_TEST",items.Single(x=>x.Role==DxfRawHandleRole.XData).Context,"XData APPID context");
        Check(index.FindReferences("AB").All(x=>x.Role!=DxfRawHandleRole.Arbitrary),"Nontranslated arbitrary slot included in references");
        Equal(1,index.Occurrences.Count(x=>x.Role==DxfRawHandleRole.HeaderSeed),"Header seed misclassified");
        Equal(1,index.Diagnostics.Count(x=>x.Kind==DxfRawHandleDiagnosticKind.UnresolvedReference),"Opaque payload manufactured dangling pointers");
        Equal("F",index.Diagnostics.Single().Handle,"Unexpected structural diagnostic");
        foreach (var occurrence in index.Occurrences)
            Equal(occurrence.Handle,(string)doc.Tags[occurrence.TagIndex].Value,"Absolute occurrence location");
        using var after = new MemoryStream();doc.Save(after);
        Check(before.ToArray().SequenceEqual(after.ToArray()),"Indexing changed exact original bytes");
    }
    private static void HandleDiagnostics(DxfVersion version, bool binary, int variant)
    {
        var tags=HandleFixture(version);int at=tags.FindIndex(t=>t.Code==0 && Equals(t.Value,"LINE"));
        DxfRawHandleDiagnosticKind expected=variant switch
        {
            0=>DxfRawHandleDiagnosticKind.DuplicateIdentity,1=>DxfRawHandleDiagnosticKind.MultipleIdentities,
            2=>DxfRawHandleDiagnosticKind.NullIdentity,3=>DxfRawHandleDiagnosticKind.MultipleOwners,
            4=>DxfRawHandleDiagnosticKind.InvalidControlGroup,5=>DxfRawHandleDiagnosticKind.OwnerCycle,
            _=>DxfRawHandleDiagnosticKind.AmbiguousReference
        };
        if(variant==0||variant==6)tags.InsertRange(at,new DxfTag[]{new(0,"POINT"),new(5,variant==0?"AB":"E")});
        if(variant==1)tags.Insert(at+2,new(5,"222"));
        if(variant==2)tags[at+1]=new(5,"000");
        if(variant==3)tags.Insert(at+2,new(330,"E"));
        if(variant==4)tags.Insert(at+2,new(102,"}"));
        if(variant==5)
        {
            int owner=tags.FindIndex(at,t=>t.Code==330 && Equals(t.Value,"10"));tags[owner]=new(330,"C");
        }
        var index=DxfRawHandleIndex.Create(HandleRoundTrip(tags,binary));
        Check(index.Diagnostics.Any(x=>x.Kind==expected),"Expected handle diagnostic missing: "+expected);
        if(variant==0)Equal(2,index.FindDefinitions("00ab").Count,"Duplicate definition was overwritten");
        if(variant==5)Equal(2,index.Diagnostics.Count(x=>x.Kind==expected),"Owner cycle membership");
    }
    private static void HandleTypedOutput(DxfVersion version,bool binary)
    {
        var typed=new DxfDocument(version);typed.Entities.Add(new Line(Vector3.Zero,new Vector3(1,2,3)));
        using var stream=new MemoryStream();Check(typed.Save(stream,binary),"Typed fixture save");stream.Position=0;
        var doc=DxfRawDocument.Load(stream);var index=DxfRawHandleIndex.Create(doc);
        var line=doc.Sections.SelectMany(s=>s.Records).Single(r=>r.Name=="LINE");
        var own=index.GetOccurrences(line).Single(x=>x.Role==DxfRawHandleRole.Owner);
        Equal("BLOCK_RECORD",index.FindDefinitions(own.Handle).Single().Record.Name,"Typed common owner resolution");
        Check(!index.Diagnostics.Any(x=>x.Kind is DxfRawHandleDiagnosticKind.DuplicateIdentity or DxfRawHandleDiagnosticKind.InvalidControlGroup or DxfRawHandleDiagnosticKind.OwnerCycle),"Valid typed structure produced contradictory identity/control/owner graph");
    }
    private static void HandleIndexGuards()
    {
        var doc=DxfRawDocument.Create(HandleFixture(DxfVersion.AutoCad2018));var index=DxfRawHandleIndex.Create(doc);
        Throws<ArgumentNullException>(()=>DxfRawHandleIndex.Create(null!));
        Throws<ArgumentOutOfRangeException>(()=>new DxfRawHandleIndexOptions(0));
        Throws<ArgumentOutOfRangeException>(()=>new DxfRawHandleIndexOptions(1,0));
        Throws<InvalidDataException>(()=>DxfRawHandleIndex.Create(doc,new DxfRawHandleIndexOptions(1)));
        var bad=HandleFixture(DxfVersion.AutoCad2018);int at=bad.FindIndex(t=>t.Code==0&&Equals(t.Value,"LINE"));
        bad.InsertRange(at,new DxfTag[]{new(0,"POINT"),new(5,"AB")});
        Throws<InvalidDataException>(()=>DxfRawHandleIndex.Create(DxfRawDocument.Create(bad),new DxfRawHandleIndexOptions(1000,1)));
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        Throws<OperationCanceledException>(()=>DxfRawHandleIndex.Create(doc,cancellationToken:cancel.Token));
        foreach(string handle in new[]{"", " 1", "1 ", "+1", "0x1", "G", "10000000000000000"})
            Throws<ArgumentException>(()=>index.FindDefinitions(handle));
        Throws<ArgumentNullException>(()=>index.FindReferences(null!));
        var other=doc.WithTags(doc.Tags);var stale=other.Sections.SelectMany(s=>s.Records).First();
        Throws<ArgumentException>(()=>index.GetOccurrences(stale));
        Throws<NotSupportedException>(()=>((IList<DxfRawHandleOccurrence>)index.Occurrences).Clear());
        Throws<NotSupportedException>(()=>((IList<DxfRawHandleDiagnostic>)index.Diagnostics).Clear());
        Throws<NotSupportedException>(()=>((IList<DxfRawHandleOccurrence>)index.FindDefinitions("AB")).Clear());
    }
    private static void HandleDeepChain()
    {
        var tags=new List<DxfTag>{new(0,"SECTION"),new(2,"HEADER"),new(9,"$ACADVER"),new(1,"AC1032"),new(0,"ENDSEC"),new(0,"SECTION"),new(2,"OBJECTS")};
        for(int i=12000;i>=1;i--)tags.AddRange(new DxfTag[]{new(0,"DICTIONARY"),new(5,i.ToString("X")),new(330,(i-1).ToString("X")),new(100,"AcDbDictionary")});
        tags.AddRange(new DxfTag[]{new(0,"ENDSEC"),new(0,"EOF")});
        var index=DxfRawHandleIndex.Create(DxfRawDocument.Create(tags));
        Equal(24000,index.Occurrences.Count,"Deep graph lost occurrences");Equal(0,index.Diagnostics.Count,"Deep acyclic graph diagnostics");
    }
    private static void HandleConcurrentLookup()
    {
        var doc=DxfRawDocument.Create(HandleFixture(DxfVersion.AutoCad2018));var index=DxfRawHandleIndex.Create(doc);
        Parallel.For(0,256,i=>{
            Equal("LINE",index.FindDefinitions("aB").Single().Record.Name,"Concurrent identity");
            Check(index.FindReferences("E").Count>1,"Concurrent pointers");
            var independent=DxfRawHandleIndex.Create(doc);
            Check(independent.Occurrences.Select(x=>x.TagIndex).SequenceEqual(index.Occurrences.Select(x=>x.TagIndex)),"Nondeterministic scan");
        });
    }
    private static void HandleHeaderAndXRecord()
    {
        var tags=HandleFixture(DxfVersion.AutoCad2018);
        int header=tags.FindIndex(t=>t.Code==0&&Equals(t.Value,"ENDSEC"));
        tags.InsertRange(header,new DxfTag[]{new(9,"$CUSTOM"),new(5,"AB"),new(9,"$CUSTOMARB"),new(320,"AB"),new(9,"$CMATERIAL"),new(347,"C")});
        int data=tags.FindIndex(t=>t.Code==102&&Equals(t.Value,"application payload, not a control group"));
        tags.InsertRange(data+2,new DxfTag[]{new(1001,"XREC_APP"),new(1005,"C")});
        var index=DxfRawHandleIndex.Create(DxfRawDocument.Create(tags));
        Equal(DxfRawHandleRole.Opaque,index.Occurrences.Single(x=>x.Record?.Name=="$CUSTOM").Role,"Unknown header identity-looking field inferred as reference");
        Equal(DxfRawHandleRole.Arbitrary,index.Occurrences.Single(x=>x.Record?.Name=="$CUSTOMARB").Role,"Header arbitrary handle inferred as translated reference");
        Equal(DxfRawHandleRole.HeaderReference,index.Occurrences.Single(x=>x.Record?.Name=="$CMATERIAL").Role,"Header material pointer lost");
        Equal(DxfRawHandleRole.XData,index.Occurrences.Single(x=>x.Context=="XREC_APP").Role,"XRECORD extension data confused with ordinary payload");
    }

}
