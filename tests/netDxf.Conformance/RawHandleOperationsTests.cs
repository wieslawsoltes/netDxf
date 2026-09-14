using netDxf;
using netDxf.Header;
using netDxf.Entities;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterRawHandleOperationsTests()
    {
        foreach (DxfVersion version in HandleProfiles)
        foreach (bool binary in new[] { false,true })
        {
            DxfVersion v=version;bool b=binary;
            Run($"handles/remap/permutation/{v}/{b}",()=>HandlePermutation(v,b));
            Run($"handles/remap/fresh-target/{v}/{b}",()=>HandleFreshTarget(v,b));
            Run($"handles/closure/{v}/{b}",()=>HandleClosure(v,b));
        }
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        { DxfVersion v=version;bool b=binary;Run($"handles/remap/typed/{v}/{b}",()=>HandleRemapTyped(v,b)); }
        foreach(int failure in Enumerable.Range(0,12))
        { int f=failure;Run($"handles/remap/reject/{f}",()=>HandleRemapInvalid(f)); }
        Run("handles/operations/cancel-stale-and-budget",HandleOperationGuards);
        Run("handles/operations/closure-evidence",HandleClosureEvidence);
    }
    private static List<DxfTag> HandleOperationFixture(DxfVersion version)
    {
        return new List<DxfTag>{
            new(0,"SECTION"),new(2,"HEADER"),new(9,"$ACADVER"),new(1,HandleProfileName(version)),new(9,"$DWGCODEPAGE"),new(3,"ANSI_1252"),
            new(9,"$HANDSEED"),new(5,"31"),new(0,"ENDSEC"),
            new(0,"SECTION"),new(2,"OBJECTS"),
            new(0,"DICTIONARY"),new(5,"10"),new(330,"0"),new(100,"AcDbDictionary"),new(3,"Hard"),new(360,"20"),new(3,"Soft"),new(350,"30"),
            new(0,"XRECORD"),new(5,"20"),new(330,"10"),new(100,"AcDbXrecord"),new(280,(short)1),new(1,"literal 10 is application text"),
            new(0,"XRECORD"),new(5,"30"),new(330,"10"),new(100,"AcDbXrecord"),new(280,(short)1),new(1,"data"),new(0,"ENDSEC"),
            new(0,"SECTION"),new(2,"ENTITIES"),new(0,"LINE"),new(5,"A"),
            new(102,"{ACAD_REACTORS"),new(330,"30"),new(102,"}"),
            new(330,"10"),new(100,"AcDbEntity"),new(100,"AcDbLine"),
            new(10,1.0),new(20,2.0),new(30,3.0),new(11,4.0),new(21,5.0),new(31,6.0),
            new(340,"20"),new(330,"99"),new(320,"10"),new(1001,"REFS"),new(1005,"30"),new(0,"ENDSEC"),new(0,"EOF")
        };
    }
    private static void HandlePermutation(DxfVersion version,bool binary)
    {
        var source=HandleRoundTrip(HandleOperationFixture(version),binary);var index=DxfRawHandleIndex.Create(source);
        using var original=new MemoryStream();source.Save(original);
        var mapping=new Dictionary<string,string>{{"0010","20"},{"20","10"},{"30","A"},{"a","30"}};
        var edited=index.RemapHandles(mapping);var next=DxfRawHandleIndex.Create(edited);
        Equal("DICTIONARY",next.FindDefinitions("20").Single().Record.Name,"Simultaneous swap overwrote identity");
        Equal("LINE",next.FindDefinitions("30").Single().Record.Name,"Permutation lost entity");
        Equal(source.Tags.Count,edited.Tags.Count,"Remap modified tag count");
        foreach(var item in index.Occurrences)
        {
            string actual=(string)edited.Tags[item.TagIndex].Value;
            if(item.Role is DxfRawHandleRole.Identity||item.IsReference)
            {
                string expected=item.CanonicalHandle switch{"10"=>"20","20"=>"10","30"=>"A","A"=>"30",_=>item.Handle};
                Equal(expected,actual,"Reference did not follow simultaneous permutation");
            }
            else Equal(item.Handle,actual,"Opaque/arbitrary/seed field was rewritten");
        }
        Check(source.HasOriginalBytes&&!edited.HasOriginalBytes,"Edited/original byte ownership");
        using var originalAgain=new MemoryStream();source.Save(originalAgain);
        Check(original.ToArray().SequenceEqual(originalAgain.ToArray()),"Source changed during remapping");
        using var output=new MemoryStream();edited.Save(output,!binary);output.Position=0;
        var loaded=DxfRawDocument.Load(output);SameRawTags(edited.Tags,loaded.Tags);
        var noop=next.RemapHandles(new Dictionary<string,string>{{"20","0020"}});
        Check(ReferenceEquals(noop,edited),"Numeric no-op allocated a changed snapshot");
        Throws<ArgumentException>(()=>next.GetOccurrences(index.FindDefinitions("10").Single().Record));
    }
    private static void HandleFreshTarget(DxfVersion version,bool binary)
    {
        var source=HandleRoundTrip(HandleOperationFixture(version),binary);var index=DxfRawHandleIndex.Create(source);
        var edited=index.RemapHandles(new Dictionary<string,string>{{"10","1000"}});var next=DxfRawHandleIndex.Create(edited);
        Equal("1001",next.Occurrences.Single(x=>x.Role==DxfRawHandleRole.HeaderSeed).CanonicalHandle,"HANDSEED did not advance");
        Equal(0,next.FindDefinitions("10").Count,"Old identity remained");
        Equal(1,next.FindDefinitions("1000").Count,"New identity absent");
        var changed=new HashSet<int>(index.Occurrences.Where(x=>(x.CanonicalHandle=="10"&&(x.IsReference||x.Role==DxfRawHandleRole.Identity))||x.Role==DxfRawHandleRole.HeaderSeed).Select(x=>x.TagIndex));
        for(int i=0;i<source.Tags.Count;i++)if(!changed.Contains(i))Check(ReferenceEquals(source.Tags[i],edited.Tags[i]),"Unrelated tag object replaced");
        Equal("10",next.Occurrences.Single(x=>x.Role==DxfRawHandleRole.Arbitrary).Handle,"Arbitrary handle was translated");
    }
    private static void HandleClosure(DxfVersion version,bool binary)
    {
        var index=DxfRawHandleIndex.Create(HandleRoundTrip(HandleOperationFixture(version),binary));
        var root=index.FindDefinitions("10").Single().Record;
        var closure=index.GetDependencyClosure(new[]{root,root});
        Equal(2,closure.Records.Count,"Default hard-ownership closure");Check(closure.AreSelectedReferencesResolved,"Hard closure unresolved");
        var all=index.GetDependencyClosure(new[]{root},DxfRawReferenceTraversal.All);
        Equal(3,all.Records.Count,"Soft ownership/parent closure");
        var line=index.FindDefinitions("A").Single().Record;
        var lineClosure=index.GetDependencyClosure(new[]{line},DxfRawReferenceTraversal.All);
        Equal(4,lineClosure.Records.Count,"Cycle-tolerant all-reference closure");
        Equal("99",lineClosure.UnresolvedReferences.Single().CanonicalHandle,"Dangling reference hidden");
        Check(!lineClosure.AreSelectedReferencesResolved,"Unresolved closure incorrectly certified");
        Equal(1,index.GetDependencyClosure(new[]{line},DxfRawReferenceTraversal.None).Records.Count,"No-traversal root identity");
        Throws<NotSupportedException>(()=>((IList<DxfRawRecord>)closure.Records).Clear());
    }
    private static void HandleRemapInvalid(int failure)
    {
        var tags=HandleOperationFixture(DxfVersion.AutoCad2018);
        var map=new Dictionary<string,string>{{"10","100"}};
        switch(failure)
        {
            case 0:map=new(){{"10","30"}};break; // unchanged identity collision
            case 1:map=new(){{"10","99"}};break; // capture a formerly dangling pointer
            case 2:map=new(){{"10","100"},{"20","100"}};break;
            case 3:map=new(){{"10","100"},{"0010","200"}};break;
            case 4:map=new(){{"0","100"}};break;
            case 5:map=new(){{"10","0"}};break;
            case 6:map=new(){{"99","100"}};break;
            case 7:tags.Insert(tags.FindIndex(t=>t.Code==1&&Equals(t.Value,"data")),new(330,"10"));break; // opaque XRECORD payload
            case 8:tags.InsertRange(tags.FindIndex(t=>t.Code==100&&Equals(t.Value,"AcDbEntity")),new DxfTag[]{new(102,"{VENDOR"),new(340,"100"),new(102,"}")});break;
            case 9:map=new(){{"10","FFFFFFFFFFFFFFFF"}};break;
            case 10:tags.InsertRange(tags.FindIndex(t=>t.Code==0&&Equals(t.Value,"LINE")),new DxfTag[]{new(0,"POINT"),new(5,"30")});break;
            case 11:tags.InsertRange(tags.FindIndex(t=>t.Code==9&&Equals(t.Value,"$HANDSEED")),new DxfTag[]{new(9,"$HANDSEED"),new(5,"31")});break;
        }
        var source=DxfRawDocument.Create(tags);var index=DxfRawHandleIndex.Create(source);
        if(failure<=6)Throws<ArgumentException>(()=>index.RemapHandles(map));
        else if(failure==9)Throws<OverflowException>(()=>index.RemapHandles(map));
        else Throws<InvalidOperationException>(()=>index.RemapHandles(map));
        for(int i=0;i<tags.Count;i++)Check(ReferenceEquals(tags[i],source.Tags[i]),"Failed remap mutated source");
    }
    private static void HandleOperationGuards()
    {
        var source=DxfRawDocument.Create(HandleOperationFixture(DxfVersion.AutoCad2018));var index=DxfRawHandleIndex.Create(source);
        using var cts=new CancellationTokenSource();cts.Cancel();
        Throws<OperationCanceledException>(()=>index.RemapHandles(new Dictionary<string,string>{{"10","100"}},cts.Token));
        Throws<OperationCanceledException>(()=>index.GetDependencyClosure(Array.Empty<DxfRawRecord>(),cancellationToken:cts.Token));
        Throws<ArgumentNullException>(()=>index.RemapHandles(null!));
        Throws<ArgumentNullException>(()=>index.GetDependencyClosure(null!));
        Throws<ArgumentOutOfRangeException>(()=>index.GetDependencyClosure(Array.Empty<DxfRawRecord>(),(DxfRawReferenceTraversal)256));
        var foreign=source.WithTags(source.Tags).Sections.SelectMany(s=>s.Records).First();
        Throws<ArgumentException>(()=>index.GetDependencyClosure(new[]{foreign}));
        bool disposed=false;
        IEnumerable<DxfRawRecord> Endless(){try{while(true)yield return index.FindDefinitions("10").Single().Record;}finally{disposed=true;}}
        var limited=DxfRawHandleIndex.Create(source,new DxfRawHandleIndexOptions(100,100));
        Throws<InvalidDataException>(()=>limited.GetDependencyClosure(Endless()));Check(disposed,"Bounded root enumerator leaked");
        Check(ReferenceEquals(source,index.RemapHandles(new Dictionary<string,string>())) ,"Empty mapping did not preserve original");
    }
    private static void HandleClosureEvidence()
    {
        var source=DxfRawDocument.Create(HandleFixture(DxfVersion.AutoCad2018));var index=DxfRawHandleIndex.Create(source);
        var closure=index.GetDependencyClosure(new[]{index.FindDefinitions("AB").Single().Record},DxfRawReferenceTraversal.All);
        Check(closure.UninterpretedHandles.Count>0,"Opaque closure limitation hidden");
        var tags=HandleOperationFixture(DxfVersion.AutoCad2018);int at=tags.FindIndex(t=>t.Code==0&&Equals(t.Value,"LINE"));
        tags.InsertRange(at,new DxfTag[]{new(0,"POINT"),new(5,"20")});
        var ambiguous=DxfRawHandleIndex.Create(DxfRawDocument.Create(tags));
        var result=ambiguous.GetDependencyClosure(new[]{ambiguous.FindDefinitions("10").Single().Record});
        Equal(1,result.AmbiguousReferences.Count,"Ambiguous targets silently selected");
        Equal(1,result.Records.Count,"Ambiguous dependency expanded");
    }
    private static void HandleRemapTyped(DxfVersion version, bool binary)
    {
        var typed=new DxfDocument(version); typed.Comments.Clear();
        typed.Entities.Add(new Line(new Vector3(1,2,3),new Vector3(4,5,6)));
        typed.Entities.Add(new Circle(new Vector3(7,8,9),2.5));
        using var stream=new MemoryStream();Check(typed.Save(stream,binary),"Remap typed fixture save");stream.Position=0;
        var raw=DxfRawDocument.Load(stream);var index=DxfRawHandleIndex.Create(raw);
        var map=index.Occurrences.Where(x=>x.Role==DxfRawHandleRole.Identity).ToDictionary(x=>x.CanonicalHandle,x=>(x.NumericHandle+4096).ToString("X"));
        var changed=index.RemapHandles(map);var after=DxfRawHandleIndex.Create(changed);
        Equal(index.Occurrences.Count,after.Occurrences.Count,"Full drawing remap changed handle inventory");
        Equal(index.Diagnostics.Count,after.Diagnostics.Count,"Full drawing remap introduced graph diagnostics");
        using var output=new MemoryStream();changed.Save(output,!binary);output.Position=0;
        var loaded=DxfDocument.Load(output)??throw new InvalidOperationException("Remapped drawing cannot load through typed reader");
        Equal(new Vector3(1,2,3),loaded.Entities.Lines.Single().StartPoint,"Remap changed line start");
        Equal(2.5,loaded.Entities.Circles.Single().Radius,"Remap changed circle radius");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"handle-remap-{version}-{binary}.dxf"),output.ToArray());
    }

}
