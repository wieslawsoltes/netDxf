using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterMLeaderNativeTests()
    {
        Run("mleader-native/api/optional-envelope",MLeaderNativeApi);
        foreach(var version in SupportedVersions.Where(v=>v>=DxfVersion.AutoCad2007))
        foreach(bool binary in new[]{false,true})
        {
            var v=version;bool b=binary;
            foreach(string mode in new[]{"absent","explicit-default","nondefault","clear"})
            {string m=mode;Run($"mleader-native/line-color/{v}/{b}/{m}",()=>MLeaderNativeLineColor(v,b,m));}
            foreach(string mode in new[]{"present","entity-absent","style-absent","both-absent"})
            {string m=mode;Run($"mleader-native/presence/{v}/{b}/{m}",()=>MLeaderNativePresence(v,b,m));}
            foreach(string mode in new[]{"unknown-before","unknown-after","private-subclass","private-class"})
            {string m=mode;Run($"mleader-native/opaque/{v}/{b}/{m}",()=>MLeaderNativeOpaque(v,b,m));}
            foreach(string defect in new[]{"version-zero","version-three","version-duplicate","version-misplaced","absent-before-common","style-zero","style-three","style-duplicate","style-misplaced","duplicate-field-private","partial-vector-private","duplicate-subclass-private"})
            {string d=defect;Run($"mleader-native/malformed/{v}/{b}/{d}",()=>MLeaderNativeMalformed(v,b,d));}
        }
        foreach(bool binary in new[]{false,true})
        {
            bool b=binary;Run($"mleader-native/original-packets/R2007/{b}",()=>MLeaderNativeOriginalPackets(b));
            foreach(string name in new[]{"acad_table_simple.dxf","acad_table_with_blk_ref.dxf"})
            {string n=name;Run($"mleader-native/original-file/{n}/{b}",()=>MLeaderNativeOriginalFile(n,b));}
        }
    }
    private static DxfRawDocument MLeaderNativeReplace(DxfRawDocument raw,Func<DxfRawRecord,IEnumerable<DxfTag>?> edit)
    {
        var tags=raw.Tags.ToList();
        foreach(var record in raw.Sections.SelectMany(s=>s.Records).OrderByDescending(r=>r.StartTagIndex))
        {
            var replacement=edit(record);if(replacement==null)continue;
            tags.RemoveRange(record.StartTagIndex,record.EndTagIndex-record.StartTagIndex);tags.InsertRange(record.StartTagIndex,replacement);
        }
        return raw.WithTags(tags);
    }
    private static string MLeaderNativeHandle(DxfRawRecord record)=>(string)record.Tags.First(t=>t.Code==5).Value;
    private static byte[] MLeaderNativeUnzip(string path)
    {using var input=File.OpenRead(path);using var unzip=new GZipStream(input,CompressionMode.Decompress);using var output=new MemoryStream();unzip.CopyTo(output);return output.ToArray();}
    private static void MLeaderNativeWrite(DxfDocument doc,string name,bool binary)
    {using var output=new MemoryStream();Check(doc.Save(output,binary),"Native MLEADER save");File.WriteAllBytes(Path.Combine(ArtifactDirectory,name+"-"+(binary?"binary":"text")+".dxf"),output.ToArray());output.Position=0;Check(DxfDocument.Load(output)!=null,"Native MLEADER reload");}
    private static void MLeaderNativeApi()
    {
        var freshLine=new MLeaderLine();Check(freshLine.StoredColor==null,"New line invented explicit color");Equal(unchecked((int)0xC1000000),freshLine.Color,"New line effective ByBlock color");
        var (doc,leader)=NewMLeader(DxfVersion.AutoCad2018);var style=leader.Properties.Style;
        Equal((short)2,leader.StoredVersion!.Value,"Default entity envelope");Equal((short)2,style.StoredEnvelopeValue!.Value,"Default style envelope");
        leader.StoredVersion=null;style.StoredEnvelopeValue=null;Equal((short)2,leader.Version,"Absent effective grammar");
        Check(((MultiLeader)leader.Clone()).StoredVersion==null,"Entity clone invented envelope");
        var source=(DxfDictionary)doc.Objects.Root["ACAD_MLEADERSTYLE"];var copy=doc.Objects.Clone(source,doc.Objects.Root,"CopiedStyles");
        Check(((DxfMLeaderStyle)copy["Authored"]).StoredEnvelopeValue==null,"Style graph clone invented envelope");
        foreach(short bad in new short[]{-1,0,1,3,short.MaxValue})
        {short value=bad;MLeaderThrows(()=>leader.StoredVersion=value,"Unknown entity grammar accepted");MLeaderThrows(()=>style.StoredEnvelopeValue=value,"Unknown style envelope accepted");}
        Check(leader.StoredVersion==null&&style.StoredEnvelopeValue==null,"Rejected setter changed field presence");
    }
    private static void MLeaderNativeLineColor(DxfVersion version,bool binary,string mode)
    {
        const int byBlock=unchecked((int)0xC1000000);int? expected=mode=="explicit-default"?byBlock:mode=="nondefault"?unchecked((int)0xC2112233):null;
        var raw=MLeaderNativeReplace(MLeaderRaw(version),record=>
        {
            if(record.Name!="MULTILEADER")return null;bool line=false;var tags=new List<DxfTag>();
            foreach(var tag in record.Tags)
            {if(tag.Code==304&&Equals(tag.Value,"LEADER_LINE{"))line=true;if(!line||tag.Code!=92)tags.Add(tag);if(tag.Code==305)line=false;}
            return tags;
        });
        var doc=MLeaderLoad(raw,binary);int count=0;
        foreach(var leader in doc.Entities.MultiLeaders)
        {
            foreach(var line in leader.Context.Leaders.SelectMany(n=>n.Lines))
            {
                count++;Check(line.StoredColor==null,"Missing input color was materialized");Equal(byBlock,line.Color,"Absent effective color");
                if(mode=="explicit-default")line.Color=byBlock;
                else if(mode=="nondefault")line.StoredColor=expected;
                else if(mode=="clear"){line.Color=unchecked((int)0xC2554433);line.StoredColor=null;}
                Equal(expected,line.StoredColor,"Stored color API presence/value");Equal(expected??byBlock,line.Color,"Effective color API value");
                var copy=(MLeaderLine)line.Clone();Equal(expected,copy.StoredColor,"Line clone color presence");copy.Color=12345;Equal(expected,line.StoredColor,"Line clone changed original color");
            }
            var clone=(MultiLeader)leader.Clone();Check(clone.Context.Leaders.SelectMany(n=>n.Lines).All(l=>l.StoredColor==expected),"Entity clone line-color presence");
        }
        Equal(5,count,"Line-color fixture line inventory");
        using var output=new MemoryStream();Check(doc.Save(output,binary),"Line-color save");var written=DxfRawDocument.Load(new MemoryStream(output.ToArray()));int colors=0,lines=0;
        foreach(var record in written.Sections.SelectMany(s=>s.Records).Where(r=>r.Name=="MULTILEADER"))
        {bool line=false;foreach(var tag in record.Tags){if(tag.Code==304&&Equals(tag.Value,"LEADER_LINE{")){line=true;lines++;}if(line&&tag.Code==92){colors++;Equal(expected!.Value,(int)tag.Value,"Wire explicit color");}if(tag.Code==305)line=false;}}
        Equal(5,lines,"Wire line inventory");Equal(expected.HasValue?5:0,colors,"Wire color physical presence");
        output.Position=0;var loaded=DxfDocument.Load(output)??throw new Exception("Line-color reload");
        Check(loaded.Entities.MultiLeaders.SelectMany(l=>l.Context.Leaders).SelectMany(n=>n.Lines).All(l=>l.StoredColor==expected&&l.Color==(expected??byBlock)),"Reloaded line-color presence/value");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"native-color-{version}-{mode}-{(binary?"binary":"text")}.dxf"),output.ToArray());
    }
    private static void MLeaderNativePresence(DxfVersion version,bool binary,string mode)
    {
        bool entityAbsent=mode=="entity-absent"||mode=="both-absent",styleAbsent=mode=="style-absent"||mode=="both-absent";
        var raw=MLeaderNativeReplace(MLeaderRaw(version),r=>r.Name=="MULTILEADER"&&entityAbsent?r.Tags.Where(t=>t.Code!=270):r.Name=="MLEADERSTYLE"&&styleAbsent?r.Tags.Where(t=>t.Code!=179):null);
        var doc=MLeaderLoad(raw,binary);CheckMLeader(doc);
        foreach(var leader in doc.Entities.MultiLeaders)
        {Equal(entityAbsent,!leader.StoredVersion.HasValue,"Entity physical presence");Equal((short)2,leader.Version,"Effective grammar");Equal(entityAbsent,!((MultiLeader)leader.Clone()).StoredVersion.HasValue,"Entity clone physical presence");}
        foreach(var style in doc.Objects.Items.OfType<DxfMLeaderStyle>())Equal(styleAbsent,!style.StoredEnvelopeValue.HasValue,"Style physical presence");
        using var output=new MemoryStream();Check(doc.Save(output,binary),"Presence save");
        var written=DxfRawDocument.Load(new MemoryStream(output.ToArray()));
        foreach(var record in written.Sections.SelectMany(s=>s.Records).Where(r=>r.Name=="MULTILEADER"||r.Name=="MLEADERSTYLE"))
            Equal(record.Name=="MULTILEADER"?entityAbsent:styleAbsent,!record.Tags.Any(t=>t.Code==(record.Name=="MULTILEADER"?270:179)),"Wire field presence");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"native-presence-{version}-{mode}-{(binary?"binary":"text")}.dxf"),output.ToArray());
        output.Position=0;var loaded=DxfDocument.Load(output)??throw new Exception("Presence reload");CheckMLeader(loaded);
        foreach(var leader in loaded.Entities.MultiLeaders)Equal(entityAbsent,!leader.StoredVersion.HasValue,"Reloaded physical presence");
    }
    private static void MLeaderNativeOpaque(DxfVersion version,bool binary,string mode)
    {
        var raw=MLeaderRaw(version);string handle=MLeaderNativeHandle(raw.Sections.SelectMany(s=>s.Records).First(r=>r.Name=="MLEADERSTYLE"));
        raw=MLeaderNativeReplace(raw,r=>
        {
            if(mode=="private-class"&&r.Name=="MULTILEADER")return Array.Empty<DxfTag>();
            if(r.Name=="CLASS"&&r.Tags.Any(t=>t.Code==1&&(string)t.Value=="MLEADERSTYLE")&&mode=="private-class")
                return r.Tags.Select(t=>t.Code==2?new DxfTag(2,"PrivateMLeaderStyle"):t.Code==3?new DxfTag(3,"PRIVATE_APP"):t.Code==91?new DxfTag(91,19):t);
            if(r.Name!="MLEADERSTYLE"||(MLeaderNativeHandle(r)!=handle&&mode!="private-class"))return null;
            var tags=r.Tags.ToList();int xdata=tags.FindIndex(t=>t.Code==1001);if(xdata<0)xdata=tags.Count;
            // This reference is deliberately unresolved. It must remain in the opaque packet,
            // and its discarded typed-shell fixup must never enter document resolution.
            int text=tags.FindIndex(t=>t.Code==342);tags[text]=new DxfTag(342,"FEEEE");
            int at=mode=="unknown-before"?tags.FindIndex(t=>t.Code==179)+1:xdata;
            if(mode=="private-subclass")tags.InsertRange(at,new[]{new DxfTag(100,"PrivateMLeaderStyleData"),new DxfTag(342,"FDDDD"),new DxfTag(300,"opaque private data")});
            else tags.Insert(at,new DxfTag(298,true));
            if(!tags.Any(t=>t.Code==1001))tags.AddRange(new[]{new DxfTag(1001,"QA_MLEADER"),new DxfTag(1000,"private style XData"),new DxfTag(1005,"FCCCC")});
            return tags;
        });
        var doc=MLeaderLoad(raw,binary);Check(doc.GetObjectByHandle(handle) is DxfOpaqueObject,"Private style was not retained opaque");
        if(mode!="private-class")CheckMLeader(doc);else Equal(0,doc.Objects.Items.OfType<DxfMLeaderStyle>().Count(),"Private classes gained typed style");
        using var output=new MemoryStream();Check(doc.Save(output,binary),"Opaque style save");
        var written=DxfRawDocument.Load(new MemoryStream(output.ToArray()));
        foreach(var old in raw.Sections.SelectMany(s=>s.Records).Where(r=>r.Name=="MLEADERSTYLE"&&(mode=="private-class"||MLeaderNativeHandle(r)==handle)))
        {
            var current=written.Sections.SelectMany(s=>s.Records).Single(r=>r.Name=="MLEADERSTYLE"&&MLeaderNativeHandle(r)==MLeaderNativeHandle(old));
            Equal(old.Tags.Count,current.Tags.Count,"Opaque exact packet length");
            for(int i=0;i<old.Tags.Count;i++){Equal(old.Tags[i].Code,current.Tags[i].Code,"Opaque code order");Equal(old.Tags[i].Value,current.Tags[i].Value,"Opaque stored value");}
        }
        if(mode=="private-class")
        {var cls=written.Sections.SelectMany(s=>s.Records).Single(r=>r.Name=="CLASS"&&r.Tags.Any(t=>t.Code==1&&(string)t.Value=="MLEADERSTYLE"));Equal("PrivateMLeaderStyle",(string)cls.Tags.First(t=>t.Code==2).Value,"Private CLASS overwritten");Equal(19,(int)cls.Tags.First(t=>t.Code==91).Value,"Private CLASS count overwritten");}
        File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"native-opaque-{version}-{mode}-{(binary?"binary":"text")}.dxf"),output.ToArray());
    }
    private static void MLeaderNativeMalformed(DxfVersion version,bool binary,string defect)
    {
        bool style=defect.StartsWith("style-")||defect.EndsWith("-private");bool done=false;
        var raw=MLeaderNativeReplace(MLeaderRaw(version),r=>
        {
            if(done||r.Name!=(style?"MLEADERSTYLE":"MULTILEADER"))return null;done=true;var tags=r.Tags.ToList();
            int Find(short code)=>tags.FindIndex(t=>t.Code==code);short code=style?(short)179:(short)270;int index=Find(code);
            switch(defect)
            {
                case "version-zero":case "style-zero":tags[index]=new DxfTag(code,(short)0);break;
                case "version-three":case "style-three":tags[index]=new DxfTag(code,(short)3);break;
                case "version-duplicate":case "style-duplicate":tags.Insert(index,tags[index]);break;
                case "version-misplaced":case "style-misplaced":var marker=tags[index];tags.RemoveAt(index);tags.Insert(tags.FindIndex(t=>t.Code==1001),marker);break;
                case "absent-before-common":tags.RemoveAt(index);tags.Insert(index,new DxfTag(90,0));break;
                case "duplicate-field-private":tags.Insert(Find(342),tags[Find(342)]);tags.Insert(Find(342),new DxfTag(298,true));break;
                case "partial-vector-private":tags.RemoveAt(Find(49));tags.Insert(Find(342),new DxfTag(298,true));break;
                case "duplicate-subclass-private":tags.Insert(Find(342),new DxfTag(298,true));tags.Insert(Find(342),new DxfTag(100,"AcDbMLeaderStyle"));break;
                default:throw new Exception(defect);
            }
            return tags;
        });MLeaderRejected(raw,binary);
    }
    private static void MLeaderNativeOriginalPackets(bool binary)
    {
        string folder=Path.Combine("tests","fixtures","mleader-native");using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"manifest.json")));
        byte[] bytes=MLeaderNativeUnzip(Path.Combine(folder,"native-mleader-R2007.dxf.gz"));
        Equal(manifest.RootElement.GetProperty("decoded_sha256").GetString()!,Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),"Native scaffold digest");
        var doc=MLeaderLoad(DxfRawDocument.Load(new MemoryStream(bytes)),binary);Equal(15,doc.Entities.MultiLeaders.Count(),"Exact original native leader inventory");
        foreach(var leader in doc.Entities.MultiLeaders){Check(leader.StoredVersion==null,"Native omitted 270 invented");leader.Validate();Check(leader.Context.MText!=null,"Original native text context lost");Check(leader.Context.Leaders.SelectMany(n=>n.Lines).All(l=>l.StoredColor==null&&l.Color==unchecked((int)0xC1000000)),"Native omitted line color changed");}
        Equal(2,doc.Objects.Items.OfType<DxfMLeaderStyle>().Count(),"Original style inventory");
        foreach(var style in doc.Objects.Items.OfType<DxfMLeaderStyle>())Check(style.StoredEnvelopeValue==null,"Native omitted 179 invented");
        var last=doc.GetObjectByHandle("B27") as MultiLeader??throw new Exception("Original last leader identity");
        Check(last.ExtensionDictionary!=null&&last.ExtensionDictionary.Handle=="13CD","Original extension dictionary lost");
        Check(doc.GetObjectByHandle("13CE") is DxfXRecord,"Original extension XRecord lost");
        MLeaderNativeWrite(doc,"native-original-R2007",binary);
    }
    private static void MLeaderNativeOriginalFile(string name,bool binary)
    {
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine("tools","table_oracle","fixtures.json")));
        var item=manifest.RootElement.GetProperty("files").EnumerateArray().Single(f=>f.GetProperty("file").GetString()==name);
        byte[] bytes=MLeaderNativeUnzip(Path.Combine("tests","fixtures","table-oracle",name+".gz"));
        Equal(item.GetProperty("sha256").GetString()!,Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),"Original source digest");
        var raw=DxfRawDocument.Load(new MemoryStream(bytes));var doc=MLeaderLoad(raw,binary);
        string[] handles=raw.Sections.SelectMany(s=>s.Records).Where(r=>r.Name=="MLEADERSTYLE").Select(MLeaderNativeHandle).ToArray();
        Check(handles.Length>0&&handles.All(h=>doc.GetObjectByHandle(h) is DxfOpaqueObject),"Native private styles need whole opaque retention");
        MLeaderNativeWrite(doc,"native-full-"+Path.GetFileNameWithoutExtension(name),binary);
    }
}
