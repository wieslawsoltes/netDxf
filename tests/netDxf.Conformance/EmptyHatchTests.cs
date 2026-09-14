using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterEmptyHatchReadTests()
    {
        foreach(DxfVersion version in SupportedVersions)
        foreach(bool binary in new[]{false,true})
        foreach(bool absent in new[]{false,true})
        foreach(bool associative in new[]{false,true})
        {
            DxfVersion v=version;bool b=binary,a=absent,s=associative;
            Run($"hatch/empty/read/{v}/{b}/{a}/{s}",()=>EmptyHatchRead(v,b,a,s));
        }
    }
    private static void RegisterEmptyHatchExportTests()
    {
        foreach(DxfVersion version in SupportedVersions)
        foreach(bool binary in new[]{false,true})
        foreach(int placement in Enumerable.Range(0,4))
        foreach(int pattern in Enumerable.Range(0,3))
        {
            DxfVersion v=version;bool b=binary;int p=placement,f=pattern;
            Run($"hatch/empty/preflight/{v}/{b}/{p}/{f}",()=>EmptyHatchPreflight(v,b,p,f));
        }
    }
    private static void EmptyHatchRead(DxfVersion version,bool binary,bool absent,bool associative)
    {
        var tags=HatchPathCountTags(version,0);
        if(absent)tags.RemoveAll(t=>t.Code==91);
        int association=tags.FindIndex(t=>t.Code==71);tags[association]=new(71,(short)(associative?1:0));
        using var input=new MemoryStream(RawFixtureBytes(tags,binary));
        var doc=DxfDocument.Load(input)??throw new InvalidOperationException("Empty metadata fixture rejected");
        Equal(1,doc.Entities.Hatches.Count(),"Empty HATCH disappeared");var hatch=doc.Entities.Hatches.Single();
        Equal(0,hatch.BoundaryPaths.Count,"Empty HATCH invented geometry");
        Equal(associative,hatch.Associative,"Association metadata lost");Equal(2.5,hatch.Elevation,"Empty elevation lost");
        Equal(new Vector2(2,3),hatch.SeedPoints.Single(),"Empty seed lost");
        Equal("after pattern",(string)hatch.XData["DOUBLE_TEST"].XDataRecord.Single().Value,"Empty XData lost");
        Check(hatch.Handle!=null&&ReferenceEquals(hatch,doc.GetObjectByHandle(hatch.Handle)),"Empty identity not registered");
        var copy=(Hatch)hatch.Clone();Equal(0,copy.BoundaryPaths.Count,"Empty clone invented geometry");
        copy.SeedPoints.Clear();Equal(1,hatch.SeedPoints.Count,"Empty clone aliases metadata");
        Equal(new Vector3(20,30,40),doc.Entities.Lines.Single().StartPoint,"Empty entity consumed following line");
        input.Position=0;var raw=DxfRawDocument.Load(input);using var unchanged=new MemoryStream();raw.Save(unchanged);
        Check(input.ToArray().SequenceEqual(unchanged.ToArray()),"Raw empty-HATCH preservation changed");
        // Retained objects are repairable; adding an explicit valid boundary makes typed export possible.
        var boundary=new HatchBoundaryPath(new EntityObject[]{new Circle(Vector3.Zero,3)});
        hatch.BoundaryPaths.Add(boundary);
        using var output=new MemoryStream();Check(doc.Save(output,!binary),"Repaired empty HATCH cannot export");output.Position=0;
        Equal(1,(DxfDocument.Load(output)??throw new InvalidOperationException("Repaired reload failed")).Entities.Hatches.Single().BoundaryPaths.Count,"Repaired geometry lost");
    }
    private static void EmptyHatchPreflight(DxfVersion version,bool binary,int placement,int pattern)
    {
        HatchPattern fill=pattern switch{0=>HatchPattern.Solid,1=>HatchPattern.Line,_=>new HatchGradientPattern()};
        var hatch=new Hatch(fill,false);var doc=new DxfDocument(version);
        switch(placement)
        {
            case 0:doc.Entities.Add(hatch);break;
            case 1:doc.Layouts.Add(new Layout("EmptyPaper"));doc.Entities.ActiveLayout="EmptyPaper";doc.Entities.Add(hatch);doc.Entities.ActiveLayout="Model";break;
            case 2:var inner=new Block("EmptyInner");inner.Entities.Add(hatch);var outer=new Block("EmptyOuter");outer.Entities.Add(new Insert(inner));doc.Entities.Add(new Insert(outer));break;
            default:var unused=new Block("EmptyUnused");unused.Entities.Add(hatch);doc.Blocks.Add(unused);break;
        }
        using var output=new MemoryStream();byte[] original={1,2,3,4,5};output.Write(original);output.Position=2;
        string seed=doc.DrawingVariables.HandleSeed;string? handle=hatch.Handle;
        int apps=doc.ApplicationRegistries.Count,layouts=doc.Layouts.Count;
#if DEBUG
        try{doc.Save(output,binary);throw new InvalidOperationException("Empty HATCH silently exported");}
        catch(InvalidDataException e){Check(e.Message.Contains("HATCH")&&e.Message.Contains("boundary"),"Empty preflight lacks context");}
#else
        Check(!doc.Save(output,binary),"Empty HATCH export falsely reported success");
#endif
        Check(original.SequenceEqual(output.ToArray()),"Empty preflight changed stream bytes");Equal(2L,output.Position,"Empty preflight advanced stream");
        Equal(seed,doc.DrawingVariables.HandleSeed,"Empty preflight allocated handles");Equal(handle,hatch.Handle,"Empty preflight changed identity");
        Equal(apps,doc.ApplicationRegistries.Count,"Empty preflight registered APPIDs");Equal(layouts,doc.Layouts.Count,"Empty preflight created layouts");
    }
}
