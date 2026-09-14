using System.Security.Cryptography;
using System.Text.Json;
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
    private static readonly string[] MLeaderDefects={"missing-context","missing-context-end","bad-context-start","bad-context-end","bad-node-end","bad-line-end","duplicate-context","duplicate-common","unknown-context","partial-common-vector","partial-text-vector","partial-plane-vector","missing-content-flag","disabled-text-with-data","both-contents","tolerance-content","content-mismatch","arrow-without-handle","attribute-without-width","matrix-short","matrix-long","style-envelope","style-missing-text","wrong-style-type","missing-linetype","wrong-attribute-type","xdata-in-packet"};
    private static void RegisterMLeaderTests()
    {
        Directory.CreateDirectory(ArtifactDirectory);
        Run("mleader/api/clone-components",MLeaderCloneComponents);
        Run("mleader/api/reference-lifecycle",MLeaderReferenceLifecycle);
        Run("mleader/api/foreign-reference-isolation",MLeaderForeignReferences);
        Run("mleader/api/invalid-values-and-transform",MLeaderInvalidValues);
        Run("mleader/api/exact-transform-boundary",MLeaderExactTransforms);
        Run("mleader/api/combined-graphics-references",MLeaderCombinedReferences);
        Run("mleader/api/style-graph-clone",MLeaderStyleGraphClone);
        Run("mleader/api/populated-block-lifecycle",MLeaderPopulatedBlockLifecycle);
        foreach(bool collision in new[]{false,true}){bool c=collision;Run("mleader/api/anonymous-block-rename/"+c,()=>MLeaderAnonymousRename(c));}
        foreach(string kind in new[]{"text","line","block"})foreach(string action in new[]{"attach","detach","move","collision"})
        {string k=kind,a=action;Run("mleader/api/rename-callback/"+k+"/"+a,()=>MLeaderRenameCallback(k,a));}
        foreach(string kind in new[]{"text","line","block"}){string k=kind;Run("mleader/api/throwing-rename/"+k,()=>MLeaderThrowingRename(k));}
        foreach(var version in SupportedVersions)
        foreach(bool binary in new[]{false,true})
        {
            var v=version;bool b=binary;
            if(v<DxfVersion.AutoCad2007){Run($"mleader/unsupported/{v}/{b}",()=>MLeaderOldProfile(v,b));continue;}
            Run($"mleader/independent/{v}/{b}",()=>MLeaderIndependent(v,b));
            Run($"mleader/authored/{v}/{b}",()=>MLeaderAuthored(v,b));
            Run($"mleader/version-preflight/{v}/{b}",()=>MLeaderVersionPreflight(v,b));
            foreach(string defect in MLeaderDefects){string d=defect;Run($"mleader/malformed/{v}/{b}/{d}",()=>MLeaderMalformed(v,b,d));}
        }
    }
    private static string MLeaderFixture(DxfVersion version)=>Path.Combine("tests","fixtures","mleader",$"independent-mleader-R{version.ToString().Replace("AutoCad","")}.dxf");
    private static DxfRawDocument MLeaderRaw(DxfVersion version)
    {
        string path=MLeaderFixture(version);byte[] bytes=File.ReadAllBytes(path);
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine("tests","fixtures","mleader","manifest.json")));
        var fixture=manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(f=>f.GetProperty("file").GetString()==Path.GetFileName(path));
        Equal(fixture.GetProperty("sha256").GetString()!,Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),"MLEADER producer hash");
        return DxfRawDocument.Load(new MemoryStream(bytes));
    }
    private static DxfDocument MLeaderLoad(DxfRawDocument raw,bool binary)
    {using var stream=new MemoryStream();raw.Save(stream,binary);stream.Position=0;return DxfDocument.Load(stream)??throw new Exception("MLEADER failed to load");}
    private static DxfDocument MLeaderDocument()=>MLeaderLoad(MLeaderRaw(DxfVersion.AutoCad2018),false);
    private static void MLeaderThrows(Action action,string message)
    {bool threw=false;try{action();}catch(ArgumentException){threw=true;}catch(InvalidOperationException){threw=true;}catch(NotSupportedException){threw=true;}Check(threw,message);}
    private static void MLeaderThrowingRename(string kind)
    {
        var (doc,leader)=NewMLeader(DxfVersion.AutoCad2018);
        TableObject target;
        if(kind=="text"){var text=doc.TextStyles.Add(new TextStyle("Before","txt.shx"));leader.Properties.TextStyle=text;target=text;}
        else if(kind=="line"){var line=doc.Linetypes.Add(new Linetype("Before"));leader.Properties.LeaderLinetype=line;target=line;}
        else {var block=doc.Blocks.Add(new Block("Before"));leader.Properties.ArrowHead=block.Record;target=block;}
        doc.Entities.Add(leader);
        target.NameChanged+=(_,_)=>throw new InvalidOperationException("observer failure");
        MLeaderThrows(()=>target.Name="After","Throwing observer ignored");Equal("Before",target.Name,"Failed rename changed name");
        if(kind=="text")
        {var value=(TextStyle)target;Check(ReferenceEquals(doc.TextStyles["Before"],value)&&!doc.TextStyles.Contains("After"),"Failed STYLE rename moved index");Equal(1,doc.TextStyles.GetReferences(value).Sum(r=>r.Uses),"Failed STYLE rename lost references");leader.Properties.TextStyle=doc.TextStyles["Standard"];Check(doc.TextStyles.Remove(value),"Failed STYLE rename blocked release");}
        else if(kind=="line")
        {var value=(Linetype)target;Check(ReferenceEquals(doc.Linetypes["Before"],value)&&!doc.Linetypes.Contains("After"),"Failed LTYPE rename moved index");Equal(1,doc.Linetypes.GetReferences(value).Sum(r=>r.Uses),"Failed LTYPE rename lost references");leader.Properties.LeaderLinetype=doc.Linetypes["Continuous"];Check(doc.Linetypes.Remove(value),"Failed LTYPE rename blocked release");}
        else
        {var value=(Block)target;Check(ReferenceEquals(doc.Blocks["Before"],value)&&!doc.Blocks.Contains("After"),"Failed BLOCK rename moved index");Equal(1,doc.Blocks.GetReferences(value).Sum(r=>r.Uses),"Failed BLOCK rename lost references");leader.Properties.ArrowHead=null;Check(doc.Blocks.Remove(value),"Failed BLOCK rename blocked release");}
    }
    private static void MLeaderRenameCallback(string kind,string action)
    {
        var first=new DxfDocument(DxfVersion.AutoCad2018);var second=new DxfDocument(DxfVersion.AutoCad2018);
        TableObject New(string name)=>kind=="text"?new TextStyle(name,"txt.shx"):kind=="line"?new Linetype(name):new Block(name);
        void Add(DxfDocument doc,TableObject value){if(value is TextStyle text)doc.TextStyles.Add(text);else if(value is Linetype line)doc.Linetypes.Add(line);else doc.Blocks.Add((Block)value);}
        bool Remove(DxfDocument doc,TableObject value)=>value is TextStyle text?doc.TextStyles.Remove(text):value is Linetype line?doc.Linetypes.Remove(line):doc.Blocks.Remove((Block)value);
        bool Contains(DxfDocument doc,string name)=>kind=="text"?doc.TextStyles.Contains(name):kind=="line"?doc.Linetypes.Contains(name):doc.Blocks.Contains(name);
        TableObject item=New("Before");if(action!="attach")Add(first,item);
        string? oldRecordHandle=(item as Block)?.Record.Handle;
        item.NameChanged+=(_,_)=>{if(action=="attach")Add(second,item);else if(action=="detach")Check(Remove(first,item),"Callback detach");else if(action=="move"){Check(Remove(first,item),"Callback move removal");Add(second,item);}else Add(first,New("After"));};
        if(action=="collision"){MLeaderThrows(()=>item.Name="After","Callback collision ignored");Equal("Before",item.Name,"Collision changed original name");Check(Contains(first,"Before")&&Contains(first,"After"),"Collision corrupted table");}
        else
        {
            item.Name="After";Equal("After",item.Name,"Callback rename not accepted");Check(!Contains(first,"Before")&&!Contains(second,"Before"),"Stale callback name index");
            if(action=="detach"){Check(!Contains(first,"After"),"Detached item remained indexed");Add(second,item);}
            if(oldRecordHandle!=null&&action!="collision")Check(first.GetObjectByHandle(oldRecordHandle)==null,"Removed block record handle survived");else Check(Contains(second,"After"),"Current owner missed rename");
            if(item is Block block)Equal("After",block.Record.Name,"Block record name diverged");
            Check(Remove(second,item),"Accepted callback rename blocked normal removal");
        }
    }
    private static void MLeaderPopulatedBlockLifecycle()
    {
        var (doc,leader)=NewMLeader(DxfVersion.AutoCad2018);var block=new Block("Populated");var line=new Line(Vector3.Zero,Vector3.UnitX);var definition=new AttributeDefinition("TAG"){Value="stored value"};
        block.Entities.Add(line);block.AttributeDefinitions.Add(definition);doc.Blocks.Add(block);var record=block.Record;
        leader.Properties.ArrowHead=record;doc.Entities.Add(leader);
        string[] handles={block.Handle,record.Handle,line.Handle,definition.Handle};
        Check(!doc.Blocks.Remove(block),"Referenced block was removed");Check(handles.All(handle=>doc.GetObjectByHandle(handle)!=null),"Reference guard changed handles");
        leader.Properties.ArrowHead=null;Check(doc.Blocks.Remove(block),"Released populated block removal");
        Check(handles.All(handle=>doc.GetObjectByHandle(handle)==null),"Removed block or child handle survived");
        Check(ReferenceEquals(block.Record,record)&&record.Owner==null&&record.Handle==null&&block.Handle==null,"Detached block record identity");
        Check(ReferenceEquals(line.Owner,block)&&ReferenceEquals(definition.Owner,block)&&line.Handle==null&&definition.Handle==null,"Detached child ownership");
        doc.Blocks.Add(block);Check(ReferenceEquals(record.Owner,doc.Blocks)&&ReferenceEquals(block.Record,record),"Re-added block identity");
        foreach(DxfObject item in new DxfObject[]{block,record,line,definition})Check(ReferenceEquals(doc.GetObjectByHandle(item.Handle),item),"Re-added child registration");
        Check(ReferenceEquals(line.Owner,block)&&ReferenceEquals(definition.Owner,block),"Re-added child owner");
        using var output=new MemoryStream();Check(doc.Save(output,true),"Re-added populated block save");output.Position=0;var loaded=DxfDocument.Load(output)??throw new Exception("Re-added block reload");
        Equal(1,loaded.Blocks["Populated"].Entities.Count,"Re-added block entity count");Equal(1,loaded.Blocks["Populated"].AttributeDefinitions.Count,"Re-added ATTDEF count");
    }
    private static void MLeaderAnonymousRename(bool collision)
    {
        var raw=MLeaderRaw(DxfVersion.AutoCad2018);var tags=raw.Tags.ToList();
        for(int start=0;start<tags.Count;start++)
        {
            if(tags[start].Code!=0||((string)tags[start].Value!="BLOCK"&&(string)tags[start].Value!="BLOCK_RECORD"))continue;
            int end=start+1;while(end<tags.Count&&tags[end].Code!=0)end++;
            int name=tags.FindIndex(start,end-start,t=>t.Code==2&&(string)t.Value=="QA_MLEADER_ARROW");if(name<0)continue;
            tags[name]=new DxfTag(2,"*U909");
            if((string)tags[start].Value=="BLOCK")
            {int flags=tags.FindIndex(start,end-start,t=>t.Code==70);tags[flags]=new DxfTag(70,(short)(((short)tags[flags].Value)|1));}
        }
        var doc=MLeaderLoad(raw.WithTags(tags),false);var block=doc.Blocks["*U909"];var original=block.Flags;string recordName=block.Record.Name;
        Check(original.HasFlag(BlockTypeFlags.AnonymousBlock),"Anonymous fixture flag");
        if(collision)doc.Blocks.Add(new Block("Taken"));else block.NameChanged+=(_,_)=>throw new InvalidOperationException("anonymous observer");
        MLeaderThrows(()=>block.Name="Taken","Anonymous failed rename accepted");
        Equal("*U909",block.Name,"Anonymous name changed on failure");Equal(recordName,block.Record.Name,"Anonymous record name changed on failure");Equal(original,block.Flags,"Anonymous flag changed on failure");Check(ReferenceEquals(doc.Blocks["*U909"],block),"Anonymous index changed on failure");
    }
    private static void MLeaderStyleGraphClone()
    {
        var (source,leader)=NewMLeader(DxfVersion.AutoCad2018);var block=source.Blocks.Add(new Block("StyleBlock"));
        var original=leader.Properties.Style;original.Properties.ArrowHead=block.Record;original.Properties.Block=block.Record;original.Properties.Description="independent";
        var sourceDictionary=(DxfDictionary)source.Objects.Root["ACAD_MLEADERSTYLE"];
        var local=source.Objects.Clone(sourceDictionary,source.Objects.Root,"LocalStyles");var localStyle=(DxfMLeaderStyle)local["Authored"];
        Check(!ReferenceEquals(original,localStyle)&&ReferenceEquals(original.Properties.TextStyle,localStyle.Properties.TextStyle)&&ReferenceEquals(localStyle.Properties.Block,block.Record),"Same-document style graph clone");
        localStyle.Properties.Description="edited";Equal("independent",original.Properties.Description,"Style clone values shared");
        var destination=new DxfDocument(DxfVersion.AutoCad2018);var mappedBlock=destination.Blocks.Add(new Block("MappedBlock"));
        MLeaderThrows(()=>destination.Objects.Clone(sourceDictionary,destination.Objects.Root,"MissingMap"),"Cross-document style clone accepted foreign references");Check(!destination.Objects.Root.Contains("MissingMap"),"Failed style clone changed destination");
        var mappings=new Dictionary<DxfObject,DxfObject>{{original.Properties.TextStyle,destination.TextStyles["Standard"]},{block.Record,mappedBlock.Record}};
        var mapped=destination.Objects.Clone(sourceDictionary,destination.Objects.Root,"MappedStyles",mappings);var style=(DxfMLeaderStyle)mapped["Authored"];
        Check(ReferenceEquals(style.Properties.TextStyle,destination.TextStyles["Standard"])&&ReferenceEquals(style.Properties.Block,mappedBlock.Record)&&ReferenceEquals(style.Properties.ArrowHead,mappedBlock.Record),"Style graph mappings lost exact references");
        Equal(2,destination.Blocks.GetReferences(mappedBlock).Where(r=>ReferenceEquals(r.Reference,style)).Sum(r=>r.Uses),"Mapped style reference multiplicity");
        Check(source.Objects.Validate().Count==0&&destination.Objects.Validate().Count==0,"Style graph validation failed");
    }
    private static void CheckMLeader(DxfDocument doc)
    {
        Equal(2,doc.Entities.MultiLeaders.Count(),"MULTILEADER entity count");
        var text=doc.Entities.MultiLeaders.Single(l=>l.Context.MText!=null);var block=doc.Entities.MultiLeaders.Single(l=>l.Context.Block!=null);
        text.Validate();block.Validate();
        Check(text.Properties.Style is DxfMLeaderStyle,"Typed MLEADERSTYLE not resolved");
        Check(ReferenceEquals(text.Properties.Style,block.Properties.Style),"Shared style identity lost");
        Equal("QA_STYLE",text.Properties.Style.Properties.Description,"Style description");
        Equal(2,text.Context.Leaders.Count,"Text leader branches");Equal(3,text.Context.Leaders.Sum(l=>l.Lines.Count),"Text leader lines");
        Equal(2,text.Context.Leaders[0].Breaks.Count,"Branch breaks");Equal(2,text.Context.Leaders[0].Lines[0].Breaks.Count,"Indexed line breaks");
        Equal(2,text.Properties.ArrowHeads.Count,"Repeated arrows");Equal(1,block.Properties.BlockAttributes.Count,"Block attributes");
        Equal(2.125,text.Context.TextHeight,"Context text height");Equal(1.0625,text.Context.ArrowHeadSize,"Context arrow size");Equal(.8125,text.Context.LandingGap,"Context landing gap");
        Equal(27.25,text.Context.MText.Width,"Embedded text width");Equal(1.375,text.Context.MText.LineSpacingFactor,"Line spacing");
        Near(2,text.Context.MText.Direction.Modulus(),"Non-unit text direction");Near(3,text.Context.PlaneXAxis.Modulus(),"Non-unit plane X");Near(4,text.Context.PlaneYAxis.Modulus(),"Non-unit plane Y");
        Equal(new Vector3(2,3,4),block.Properties.BlockScale,"Independent common block scale");Equal(new Vector3(1.25,1.75,.625),block.Context.Block.Scale,"Independent context block scale");
        Equal(new Vector3(0,0,2),block.Context.Block.Normal,"Independent block direction magnitude");Equal(16,block.Context.Block.TransformationMatrix.Count,"Stored block matrix");
        Check(ReferenceEquals(block.Properties.BlockAttributes[0].Definition.Owner.Record,block.Context.Block.Block),"ATTDEF membership");
        Check(text.Context.MText.Text.Contains("C:\\fixtures\\part.dxf"),"Literal backslashes changed");
        Equal(new Vector3(20,30,40),doc.Entities.Lines.Single().StartPoint,"Following LINE");
        Check(text.XData.ContainsAppId("QA_MLEADER")&&block.XData.ContainsAppId("QA_MLEADER"),"XData lost");
        Equal(0,doc.Objects.Validate().Count,"Database validation");
    }
    private static void MLeaderIndependent(DxfVersion version,bool binary)
    {
        var doc=MLeaderLoad(MLeaderRaw(version),binary);CheckMLeader(doc);
        string[] handles=doc.Entities.MultiLeaders.Select(l=>l.Handle).ToArray();
        for(int cycle=0;cycle<3;cycle++)
        {
            using var stream=new MemoryStream();Check(doc.Save(stream,cycle%2==0?binary:!binary),"MLEADER save");
            if(cycle==0)File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"independent-mleader-R{version.ToString().Replace("AutoCad","")}-{(binary?"binary":"text")}.dxf"),stream.ToArray());
            stream.Position=0;doc=DxfDocument.Load(stream)??throw new Exception("MLEADER reload");CheckMLeader(doc);
            Check(handles.SequenceEqual(doc.Entities.MultiLeaders.Select(l=>l.Handle)),"Entity handles changed");
        }
    }
    private static (DxfDocument,MultiLeader) NewMLeader(DxfVersion version)
    {
        var doc=new DxfDocument(version);var style=new DxfMLeaderStyle();style.Properties.TextStyle=doc.TextStyles["Standard"];
        doc.Objects.AddMLeaderStyle("Authored",style);var leader=new MultiLeader();leader.Properties.Style=style;
        leader.Properties.TextStyle=doc.TextStyles["Standard"];leader.Properties.LeaderLinetype=doc.Linetypes["Continuous"];leader.Properties.ContentType=0;
        return(doc,leader);
    }
    private static void MLeaderAuthored(DxfVersion version,bool binary)
    {
        var (doc,leader)=NewMLeader(version);doc.Entities.Add(leader);
        var text=(MultiLeader)leader.Clone();text.Properties.ContentType=2;
        text.Context.MText=new MLeaderMTextContent{Style=doc.TextStyles["Standard"],Text="literal \\U+0041; Żółć 測試; \\Pparagraph",ColumnType=2,ColumnWidth=5.25,ColumnGutter=.625};
        text.Context.MText.ColumnHeights.Add(3.125);text.Context.MText.ColumnHeights.Add(0);doc.Entities.Add(text);
        var block=doc.Blocks.Add(new Block("AuthoredBlock"));block.Entities.Add(new Line(Vector3.Zero,Vector3.UnitX));
        var withBlock=(MultiLeader)leader.Clone();withBlock.Properties.ContentType=1;withBlock.Context.Block=new MLeaderBlockContent{Block=block.Record};doc.Entities.Add(withBlock);
        using var stream=new MemoryStream();Check(doc.Save(stream,binary),"Authored MULTILEADER save");File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"mleader-{version}-{(binary?"binary":"text")}.dxf"),stream.ToArray());stream.Position=0;var loaded=DxfDocument.Load(stream)??throw new Exception("Authored load");
        Equal(3,loaded.Entities.MultiLeaders.Count(),"Authored content alternatives");
        var content=loaded.Entities.MultiLeaders.Single(l=>l.Context.MText!=null).Context.MText;
        Equal(text.Context.MText.Text,content.Text,"Literal Unicode-looking escape preserved");Check(content.ColumnHeights.SequenceEqual(new[]{3.125,0.0}),"Column heights lost");
        Equal(0,loaded.Entities.MultiLeaders.Single(l=>l.Context.Block!=null).Context.Block.TransformationMatrix.Count,"Absent matrix was invented");
    }
    private static void MLeaderCloneComponents()
    {
        var doc=MLeaderDocument();var original=doc.Entities.MultiLeaders.Single(l=>l.Context.Block!=null);var clone=(MultiLeader)original.Clone();
        Check(clone.Owner==null&&clone.Handle==null,"Clone retained identity");Check(ReferenceEquals(clone.Properties.Style,original.Properties.Style),"Clone unexpectedly copied style identity");
        clone.Context.Block.TransformationMatrix[0]=7;clone.Context.Leaders[0].Lines[0].Vertices[0]=new Vector3(99,98,97);clone.Properties.BlockAttributes[0].Text="clone";
        Check(original.Context.Block.TransformationMatrix[0]!=7&&original.Properties.BlockAttributes[0].Text!="clone","Clone shares value containers");
        MLeaderThrows(()=>clone.Context.Leaders.Add(original.Context.Leaders[0]),"Owned branch shared");
        var detached=(MLeaderNode)original.Context.Leaders[0].Clone();clone.Context.Leaders.Add(detached);clone.Context.Leaders.Remove(detached);original.Context.Leaders.Add(detached);original.Context.Leaders.Remove(detached);
        doc.Entities.Add(clone);clone.Validate();Check(doc.Entities.Remove(clone),"Clone removal");
    }
    private static void MLeaderReferenceLifecycle()
    {
        var doc=MLeaderDocument();var text=doc.Entities.MultiLeaders.Single(l=>l.Context.MText!=null);var block=doc.Entities.MultiLeaders.Single(l=>l.Context.Block!=null);
        var ts=doc.TextStyles["QA_TEXT"];var lt=doc.Linetypes["QA_LEADER"];var geometry=doc.Blocks["QA_MLEADER_CONTENT"];var definition=block.Properties.BlockAttributes[0].Definition;
        int textUses=doc.TextStyles.GetReferences(ts).Where(r=>r.Reference is MultiLeader||r.Reference is DxfMLeaderStyle).Sum(r=>r.Uses);
        int lineUses=doc.Linetypes.GetReferences(lt).Sum(r=>r.Uses);int blockUses=doc.Blocks.GetReferences(geometry).Sum(r=>r.Uses);
        for(int i=0;i<3;i++)
        {ts.Name="Text"+i;lt.Name="Line"+i;geometry.Name="Block"+i;Equal(textUses,doc.TextStyles.GetReferences(ts).Where(r=>r.Reference is MultiLeader||r.Reference is DxfMLeaderStyle).Sum(r=>r.Uses),"Rename duplicated text uses");Equal(lineUses,doc.Linetypes.GetReferences(lt).Sum(r=>r.Uses),"Rename duplicated line uses");Equal(blockUses,doc.Blocks.GetReferences(geometry).Sum(r=>r.Uses),"Rename duplicated block uses");}
        Check(!doc.TextStyles.Remove(ts)&&!doc.Linetypes.Remove(lt)&&!doc.Blocks.Remove(geometry),"Referenced resource removed");
        Check(!geometry.AttributeDefinitions.Remove(definition.Tag),"Referenced ATTDEF removed");var replacement=new AttributeDefinition(definition.Tag);geometry.AttributeDefinitions[definition.Tag]=replacement;
        Check(ReferenceEquals(geometry.AttributeDefinitions[definition.Tag],definition)&&replacement.Owner==null,"Referenced ATTDEF replaced");
        block.Properties.BlockAttributes.Clear();Check(geometry.AttributeDefinitions.Remove(definition.Tag),"ATTDEF removal after release");
        geometry.AttributeDefinitions.Add(replacement);
        Check(doc.Entities.Remove(text)&&doc.Entities.Remove(block),"Leader removal");
        var style=(DxfMLeaderStyle)doc.Objects.Items.Single(o=>o is DxfMLeaderStyle s&&s.Properties.Description=="QA_STYLE");style.Properties.TextStyle=doc.TextStyles["Standard"];style.Properties.LeaderLinetype=null;style.Properties.Block=null;
        Check(doc.Linetypes.Remove(lt),"Dynamic linetype uses were cached");Check(doc.Blocks.Remove(geometry),"Dynamic block uses were cached");Check(doc.TextStyles.Remove(ts),"Dynamic text uses were cached");
    }
    private static void MLeaderForeignReferences()
    {
        var (doc,leader)=NewMLeader(DxfVersion.AutoCad2018);var foreign=new DxfDocument(DxfVersion.AutoCad2018);
        var ts=doc.TextStyles.Add(new TextStyle("OnlyDynamic","txt.shx"));var ft=foreign.TextStyles.Add(new TextStyle("OnlyDynamic","txt.shx"));
        var lt=doc.Linetypes.Add(new Linetype("OnlyDynamic"));var fl=foreign.Linetypes.Add(new Linetype("OnlyDynamic"));
        var block=doc.Blocks.Add(new Block("OnlyDynamic"));var fb=foreign.Blocks.Add(new Block("OnlyDynamic"));
        leader.Properties.TextStyle=ts;leader.Properties.LeaderLinetype=lt;leader.Properties.ArrowHead=block.Record;doc.Entities.Add(leader);
        Check(!doc.TextStyles.Remove(ft)&&!doc.Linetypes.Remove(fl)&&!doc.Blocks.Remove(fb),"Foreign same-name removal bypassed dynamic references");
        MLeaderThrows(()=>leader.Properties.TextStyle=ft,"Foreign STYLE accepted");MLeaderThrows(()=>leader.Properties.LeaderLinetype=fl,"Foreign LTYPE accepted");MLeaderThrows(()=>leader.Properties.ArrowHead=fb.Record,"Foreign BLOCK accepted");
        Check(ReferenceEquals(doc.TextStyles[ts.Name],ts)&&ReferenceEquals(foreign.TextStyles[ft.Name],ft),"Foreign removal changed identity maps");
        var clone=(MultiLeader)leader.Clone();int count=foreign.Entities.All.Count();MLeaderThrows(()=>foreign.Entities.Add(clone),"Cross-document copy needs explicit references");Equal(count,foreign.Entities.All.Count(),"Failed add mutated collection");Check(clone.Handle==null&&clone.Owner==null,"Failed add mutated identity");
        var mappedStyle=new DxfMLeaderStyle();mappedStyle.Properties.TextStyle=ft;foreign.Objects.AddMLeaderStyle("Mapped",mappedStyle);
        clone.Properties.Style=mappedStyle;clone.Properties.TextStyle=ft;clone.Properties.LeaderLinetype=fl;clone.Properties.ArrowHead=fb.Record;foreign.Entities.Add(clone);clone.Validate();
        Check(ReferenceEquals(clone.Properties.Style,mappedStyle)&&ReferenceEquals(leader.Properties.TextStyle,ts),"Explicit mapping changed source references");
        leader.Properties.TextStyle=doc.TextStyles["Standard"];leader.Properties.LeaderLinetype=doc.Linetypes["Continuous"];leader.Properties.ArrowHead=null;
        Check(doc.TextStyles.Remove(ts)&&doc.Linetypes.Remove(lt)&&doc.Blocks.Remove(block),"Released dynamic references remain");
    }
    private static void MLeaderInvalidValues()
    {
        var (doc,leader)=NewMLeader(DxfVersion.AutoCad2007);doc.Entities.Add(leader);
        MLeaderThrows(()=>leader.Properties.Scale=double.NaN,"Nonfinite scalar accepted");MLeaderThrows(()=>leader.Context.BasePoint=new Vector3(1,double.PositiveInfinity,0),"Nonfinite vector accepted");
        var text=new MLeaderMTextContent();MLeaderThrows(()=>text.Text="bad\uD800","Unpaired UTF16 accepted");MLeaderThrows(()=>text.Text="bad\0","NUL accepted");
        leader.Properties.TextAttachmentDirection=1;MLeaderThrows(leader.Validate,"Public validation ignored registered profile");leader.Properties.TextAttachmentDirection=null;
        Vector3 before=leader.Context.BasePoint;MLeaderThrows(()=>leader.TransformBy(Matrix3.Identity,Vector3.UnitX),"Unsupported transform accepted");Equal(before,leader.Context.BasePoint,"Rejected transform mutated data");leader.TransformBy(Matrix3.Identity,Vector3.Zero);
    }
    private static void MLeaderCombinedReferences()
    {
        var (doc,leader)=NewMLeader(DxfVersion.AutoCad2018);leader.Linetype=doc.Linetypes["Continuous"];doc.Entities.Add(leader);
        var line=doc.Linetypes["Continuous"];var references=doc.Linetypes.GetReferences(line).Where(r=>ReferenceEquals(r.Reference,leader)).ToArray();
        Equal(1,references.Length,"Graphics and context references must share one count entry");Equal(2,references[0].Uses,"Combined graphics/context uses");
        doc.Entities.Remove(leader);Check(!doc.Linetypes.GetReferences(line).Any(r=>ReferenceEquals(r.Reference,leader)),"Removed combined reference survived");
    }
    private static void MLeaderExactTransforms()
    {
        var (_,leader)=NewMLeader(DxfVersion.AutoCad2018);EntityObject entity=leader;
        leader.Context.BasePoint=new Vector3(7,11,13);
        foreach(double value in new[]{double.Epsilon,1e-12,double.NaN,double.PositiveInfinity})
        {
            for(int row=0;row<3;row++)for(int column=0;column<3;column++)
            {
                var matrix=Matrix3.Identity;matrix[row,column]=row==column?(double.IsNaN(value)||double.IsInfinity(value)?value:1+1e-12):value;
                MLeaderThrows(()=>entity.TransformBy(matrix,Vector3.Zero),"Nonidentity Matrix3 accepted");
            }
            foreach(var offset in new[]{new Vector3(value,0,0),new Vector3(0,value,0),new Vector3(0,0,value)})
                MLeaderThrows(()=>entity.TransformBy(Matrix3.Identity,offset),"Nonzero translation accepted");
            for(int row=0;row<4;row++)for(int column=0;column<4;column++)
            {
                var matrix=Matrix4.Identity;matrix[row,column]=row==column?(double.IsNaN(value)||double.IsInfinity(value)?value:1+1e-12):value;
                MLeaderThrows(()=>entity.TransformBy(matrix),"Nonidentity full Matrix4 accepted through base dispatch");
            }
        }
        entity.TransformBy(Matrix4.Identity);entity.TransformBy(Matrix3.Identity,Vector3.Zero);
        Equal(new Vector3(7,11,13),leader.Context.BasePoint,"Transform guard mutated coordinates");
    }
    private static void MLeaderOldProfile(DxfVersion version,bool binary)
    {
        var (doc,leader)=NewMLeader(DxfVersion.AutoCad2007);doc.Entities.Add(leader);doc.DrawingVariables.AcadVer=version;
        using var output=new MemoryStream(new byte[]{4,5,6,7},true);byte[] before=output.ToArray();output.Position=2;
        bool saved=false;try{saved=doc.Save(output,binary);}catch(NotSupportedException){}catch(InvalidOperationException){}
        Check(!saved&&before.SequenceEqual(output.ToArray())&&output.Position==2,"Old-profile failure wrote bytes");
        var raw=MLeaderRaw(DxfVersion.AutoCad2007);var tags=raw.Tags.ToList();int index=tags.FindIndex(t=>t.Code==9&&(string)t.Value=="$ACADVER");tags[index+1]=new DxfTag(1,version==DxfVersion.AutoCad2000?"AC1015":"AC1018");
        tags=tags.Select(tag=>tag.Value is string value?new DxfTag(tag.Code,string.Concat(value.Select(c=>c>127?"\\U+"+((int)c).ToString("X4"):c.ToString()))):tag).ToList();
        MLeaderRejected(DxfRawDocument.Create(tags),binary);
    }
    private static void MLeaderVersionPreflight(DxfVersion version,bool binary)
    {
        var (doc,leader)=NewMLeader(version);doc.Entities.Add(leader);
        if(version==DxfVersion.AutoCad2007)leader.Context.TopAttachment=10;
        else if(version==DxfVersion.AutoCad2010)leader.Properties.LeaderExtendToText=true;
        else {leader.Properties.ContentType=1;leader.Context.Block=new MLeaderBlockContent{Block=doc.Blocks["*Model_Space"].Record};leader.Context.Block.TransformationMatrix.Add(1);}
        using var output=new MemoryStream();bool saved=false;try{saved=doc.Save(output,binary);}catch(NotSupportedException){}catch(InvalidOperationException){}
        Check(!saved&&output.Length==0,"Invalid model wrote output");
    }
    private static void MLeaderRejected(DxfRawDocument raw,bool binary)
    {
        using var input=new MemoryStream();raw.Save(input,binary);input.Position=0;bool accepted=false;
        try{accepted=DxfDocument.Load(input)!=null;}catch(InvalidDataException){}catch(FormatException){}catch(ArgumentException){}catch(NotSupportedException){}catch(InvalidOperationException){}
        Check(!accepted,"Malformed MULTILEADER input accepted");
    }
    private static void MLeaderMalformed(DxfVersion version,bool binary,string defect)
    {
        var raw=MLeaderRaw(version);var records=raw.Sections.SelectMany(s=>s.Records).ToList();
        bool block=defect.StartsWith("attribute")||defect.StartsWith("matrix")||defect=="wrong-attribute-type";
        var record=defect.StartsWith("style-")?records.First(r=>r.Name=="MLEADERSTYLE"):records.Where(r=>r.Name=="MULTILEADER").ElementAt(block?1:0);
        var tags=record.Tags.ToList();int start=tags.FindIndex(t=>t.Code==300),end=tags.FindIndex(t=>t.Code==301);int common=end+1;
        int Find(short code,int from=0)=>tags.FindIndex(from,t=>t.Code==code);
        void Drop(short code,int from=0){tags.RemoveAt(Find(code,from));}
        switch(defect)
        {
            case "missing-context":tags.RemoveRange(start,end-start+1);break;
            case "missing-context-end":tags.RemoveAt(end);break;
            case "bad-context-start":tags[start]=new DxfTag(300,"LEADER{");break;
            case "bad-context-end":tags[end]=new DxfTag(301,"bad}");break;
            case "bad-node-end":tags[Find(303)]=new DxfTag(303,"bad}");break;
            case "bad-line-end":tags[Find(305)]=new DxfTag(305,"bad}");break;
            case "duplicate-context":tags.InsertRange(end+1,tags.GetRange(start,end-start+1));break;
            case "duplicate-common":tags.Insert(common,tags[Find(340,common)]);break;
            case "unknown-context":tags.Insert(start+1,new DxfTag(100,"UnknownContext"));break;
            case "partial-common-vector":Drop(20,common);break;
            case "partial-text-vector":Drop(23);break;
            case "partial-plane-vector":Drop(120);break;
            case "missing-content-flag":Drop(296);break;
            case "disabled-text-with-data":tags[Find(290)]=new DxfTag(290,false);break;
            case "both-contents":tags[Find(296)]=new DxfTag(296,true);break;
            case "tolerance-content":tags[Find(172,common)]=new DxfTag(172,(short)3);break;
            case "content-mismatch":tags[Find(172,common)]=new DxfTag(172,(short)0);break;
            case "arrow-without-handle":Drop(345,common);break;
            case "attribute-without-width":Drop(44,common);break;
            case "matrix-short":Drop(47);break;
            case "matrix-long":tags.Insert(Find(47),new DxfTag(47,99.0));break;
            case "style-envelope":tags[Find(179)]=new DxfTag(179,(short)3);break;
            case "style-missing-text":Drop(342);break;
            case "wrong-style-type":tags[Find(340,common)]=new DxfTag(340,records.First(r=>r.Name=="LINE").Tags.First(t=>t.Code==5).Value);break;
            case "missing-linetype":tags[Find(341,common)]=new DxfTag(341,"FEEE");break;
            case "wrong-attribute-type":tags[Find(330,common)]=new DxfTag(330,records.First(r=>r.Name=="LINE").Tags.First(t=>t.Code==5).Value);break;
            case "xdata-in-packet":tags.InsertRange(Find(23),new[]{new DxfTag(1001,"QA_MLEADER"),new DxfTag(1000,"inside")});break;
            default:throw new Exception(defect);
        }
        MLeaderRejected(raw.WithTags(raw.Tags.Take(record.StartTagIndex).Concat(tags).Concat(raw.Tags.Skip(record.EndTagIndex))),binary);
    }
}
