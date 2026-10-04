// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Security.Cryptography;
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;
using Att = netDxf.Entities.Attribute;
namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterAttributeMTextTests()
    {
        foreach(bool binary in new[]{false,true})
        {
            for(int profile=0;profile<6;profile++){int p=profile;Run($"attribute-mtext/independent/{p}/{binary}",()=>AmIndependent(p,binary));}
            for(int attachment=1;attachment<=9;attachment++){int a=attachment;Run($"attribute-mtext/wire/{a}/{binary}",()=>AmWire(a,binary));}
            for(int fault=0;fault<20;fault++){int f=fault;Run($"attribute-mtext/malformed/{f}/{binary}",()=>AmMalformed(f,binary));}
            foreach(var version in SupportedVersions.Where(v=>v!=DxfVersion.AutoCad2018))
                Run($"attribute-mtext/downsave/{version}/{binary}",()=>AmDownsave(version,binary));
        }
        for(int operation=0;operation<8;operation++){int o=operation;Run("attribute-mtext/lifecycle/"+o,()=>AmLifecycle(o));}
        for(int operation=0;operation<10;operation++){int o=operation;Run("attribute-mtext/transform/"+o,()=>AmTransform(o));}
        Run("attribute-mtext/clone-definition-binding",AmClone);
        Run("attribute-mtext/constructor-and-sync",AmSync);
        Run("attribute-mtext/definition-transform-atomic",()=>
        {
            var (_,root)=AmSeed();var d=root.Block.AttributeDefinitions["VALUE"];var before=d.GetMText();var position=d.Position;
            Throws<NotSupportedException>(()=>d.TransformBy(Matrix3.Scale(1,2,1),Vector3.Zero));
            R12Vector(position,d.Position);R12Vector(before.Position,d.GetMText().Position);
        });
        Run("attribute-mtext/noop-preserves-proxy",()=>
        {
            var (_,root)=AmSeed();var a=root.Attributes.Single();a.ProxyGraphics=new byte[]{1,2,3};
            a.TransformBy(Matrix3.Identity,Vector3.Zero);
            Check(a.ProxyGraphics!.SequenceEqual(new byte[]{1,2,3}),"No-op invalidated attribute proxy");
        });
        Run("attribute-mtext/reader-reuse",()=>{AmWire(1,false);AmWire(9,true);});
    }
    private static string AmFixture(int profile,bool binary)
    {
        string root=Path.GetFullPath("tests/fixtures/attribute-mtext");
        string name=$"independent-attribute-mtext-{profile}-{binary.ToString().ToLowerInvariant()}.dxf";
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"manifest.json")));
        var row=manifest.RootElement.GetProperty("files").EnumerateArray().Single(e=>e.GetProperty("file").GetString()==name);
        string path=Path.Combine(root,name);
        Equal(row.GetProperty("sha256").GetString(),Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),"Independent input hash");
        return path;
    }
    private static byte[] AmBytes(DxfDocument doc,bool binary)
    {using var stream=new MemoryStream();Check(doc.Save(stream,binary),"Multiline attribute save failed");Check(stream.CanWrite,"Writer closed stream");return stream.ToArray();}
    private static DxfDocument AmLoad(byte[] bytes)
    {using var stream=new MemoryStream(bytes);var d=DxfDocument.Load(stream);Check(stream.CanRead,"Reader closed stream");return d??throw new InvalidOperationException("Multiline attribute load failed");}
    private static void AmIndependent(int profile,bool binary)
    {
        var doc=DxfDocument.Load(AmFixture(profile,binary))??throw new InvalidOperationException("Independent fixture did not load");
        for(int stage=0;stage<3;stage++)
        {
            var root=doc.Entities.Inserts.Single();var def=root.Block.AttributeDefinitions["VALUE"];var att=root.Attributes.Single();
            foreach(var tuple in new[]{(def.GetMText(),def.Value,def.Position,def.Height,def.WidthFactor,def.Style.Name,def.IsBackward,def.IsUpsideDown,def.IsPositionLocked,0),
                                     (att.GetMText(),att.Value,att.Position,att.Height,att.WidthFactor,att.Style.Name,att.IsBackward,att.IsUpsideDown,att.IsPositionLocked,1)})
            {
                var (text,value,position,height,width,style,back,up,locked,index)=tuple;
                Check(text!=null,"Embedded content disappeared");
                Equal(index==0?"fallback-definition":"fallback-instance",value,"Embedded value overwrote fallback");
                R12Vector(new Vector3(-1-index,-2,-3),position);Near(1.25,height,"Fallback height");Near(.75,width,"Fallback width");
                Equal("FALLBACK",style,"Embedded style overwrote fallback");Check(back&&up,"Attribute type overwrote text-generation flags");Equal(profile%2==1?(bool?)true:null,locked,"Optional Int16 lock flag");
                Equal("BODY",text!.Style.Name,"Independent embedded style");R12Vector(new Vector3(10+index,20,30),text.Position);
                Near(3.5,text.Height,"Embedded height");Near(12.25,text.RectangleWidth,"Embedded reference width");Equal((double?)22.5,text.DefinedHeight,"Defined height");
                Equal((MTextAttachmentPoint)(profile+1),text.AttachmentPoint,"Attachment point");
                if(profile>=2){Equal((double?)8.5,text.ActualWidth,"Actual width");Equal((double?)9.5,text.ActualHeight,"Actual height");Equal((double?)37.5,text.Rotation,"Stored rotation");}
                if(profile>=4){Check(text.BackgroundFill!=null,"Background missing");Equal((int?)0x123456,text.BackgroundFill!.TrueColor,"Background truecolor");}
                string expected=profile==3?"Zażółć 世界 😀 "+string.Concat(Enumerable.Repeat("áβ字😀",180))+"\\Ptail":"paragraph one\\Pparagraph two";
                Equal((index==0?"DEF:":"ATT:")+expected,text.Value,"Independent embedded content");
            }
            Check(ReferenceEquals(att.Definition,def),"Canonical definition binding");
            Check(!doc.TextStyles.Remove("BODY"),"Embedded-only style removal allowed");
            byte[] bytes=AmBytes(doc,stage==1?!binary:binary);
            File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"attribute-mtext-independent-{profile}-{binary}-{stage}.dxf"),bytes);
            doc=AmLoad(bytes);
        }
    }
    private static (DxfDocument,Insert) AmSeed(int attachment=1)
    {
        var block=new Block("MULTILINE");var def=new AttributeDefinition("VALUE"){Value="fallback",Position=new Vector3(1,2,3),Height=1,IsBackward=true,IsUpsideDown=true};
        block.AttributeDefinitions.Add(def);var root=new Insert(block);var doc=new DxfDocument(DxfVersion.AutoCad2018);doc.Entities.Add(root);
        var content=new AttributeMText{Value="rich\\Pcontent 😀",Position=new Vector3(10,20,30),Height=3,RectangleWidth=20,DefinedHeight=40,ActualWidth=18,ActualHeight=7,
            Style=new TextStyle("BODY","romans.shx"),Normal=Vector3.UnitZ,TextDirection=Vector3.UnitY,Rotation=15,AttachmentPoint=(MTextAttachmentPoint)attachment,
            DrawingDirection=MTextDrawingDirection.LeftToRight,LineSpacingFactor=1.3,LineSpacingStyle=MTextLineSpacingStyle.Exact,
            BackgroundFill=new MTextBackgroundFill{Flags=MTextBackgroundFillFlags.UseColor,ScaleFactor=1.5,ColorIndex=3,TrueColor=0x123456}};
        def.SetMText(content);root.Attributes.Single().SetMText(content);def.IsPositionLocked=true;root.Attributes.Single().IsPositionLocked=false;
        return (doc,root);
    }
    private static void AmWire(int attachment,bool binary)
    {
        var (doc,root)=AmSeed(attachment);var snapshot=root.Attributes.Single().GetMText();snapshot.Value="unpublished";
        Equal("rich\\Pcontent 😀",root.Attributes.Single().GetMText().Value,"Getter leaked mutable snapshot");
        snapshot=root.Attributes.Single().GetMText();snapshot.Value="edited\\P多行";root.Attributes.Single().SetMText(snapshot);snapshot.Value="post-publication edit";
        for(int stage=0;stage<3;stage++)
        {
            var a=root.Attributes.Single();Equal("fallback",a.Value,"Fallback changed during content edit");Equal("edited\\P多行",a.GetMText().Value,"Edited content lost");
            Equal((MTextAttachmentPoint)attachment,a.GetMText().AttachmentPoint,"Authored attachment");Equal((bool?)false,a.IsPositionLocked,"Explicit false lock");Check(a.IsBackward&&a.IsUpsideDown,"Generation flags changed");
            byte[] bytes=AmBytes(doc,binary);File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"attribute-mtext-authored-{attachment}-{binary}-{stage}.dxf"),bytes);
            doc=AmLoad(bytes);root=doc.Entities.Inserts.Single();
        }
    }
    private static void AmMalformed(int fault,bool binary)
    {
        var (doc,_)=AmSeed();using var rawStream=new MemoryStream(AmBytes(doc,binary));var raw=DxfRawDocument.Load(rawStream);
        var row=raw.Sections.SelectMany(s=>s.Records).Single(r=>r.Name=="ATTRIB");var tags=row.Tags.ToList();
        int start=tags.FindIndex(t=>t.Code==101);int subtype=tags.FindIndex(t=>t.Code==100&&(string)t.Value=="AcDbAttribute");
        int Find(short code)=>tags.FindIndex(start+1,t=>t.Code==code);
        void Set(short code,object value){int i=Find(code);if(i>=0)tags[i]=new DxfTag(code,value);else tags.Add(new DxfTag(code,value));}
        switch(fault)
        {
            case 0:tags[start]=new DxfTag(101,"Not Embedded Object");break;
            case 1:tags.Add(new DxfTag(101,"Embedded Object"));break;
            case 2:Set(40,0.0);break;case 3:Set(41,-1.0);break;case 4:Set(71,(short)0);break;
            case 5:Set(72,(short)2);break;case 6:Set(73,(short)0);break;case 7:Set(44,4.1);break;
            case 8:tags.Add(new DxfTag(40,3.0));break;case 9:tags.RemoveAt(Find(20));break;
            case 10:tags.RemoveAt(Find(21));break;case 11:Set(11,0.0);Set(21,0.0);Set(31,0.0);break;
            case 12:tags.RemoveAt(Find(1));break;case 13:tags.Add(new DxfTag(3,"after-terminal"));break;
            case 14:tags.Add(new DxfTag(7,"Standard"));break;
            case 15:tags[tags.FindIndex(subtype+1,t=>t.Code==280)]=new DxfTag(280,(short)3);break;
            case 16:tags[tags.FindIndex(subtype+1,t=>t.Code==71)]=new DxfTag(71,(short)4);break;
            case 17:tags.RemoveRange(start,tags.Count-start);break;
            case 18:tags.Add(new DxfTag(75,(short)1));break;
            case 19:tags.InsertRange(start,new DxfTag[]{new(100,"AcDbXrecord"),new(340,"ABC")});break;
        }
        var changed=raw.WithRecord(row,tags);using var output=new MemoryStream();changed.Save(output,binary);output.Position=0;
        bool refused=false;
        try{refused=DxfDocument.Load(output)==null;}catch(Exception error)when(error is ArgumentException||error is InvalidDataException||error is NotSupportedException||error is FormatException){refused=true;}
        Check(refused,"Malformed embedded payload accepted: "+fault);Check(output.CanRead,"Failed reader closed stream");
    }
    private static void AmDownsave(DxfVersion version,bool binary)
    {
        var (doc,_)=AmSeed();doc.DrawingVariables.AcadVer=version;
        using var stream=new MemoryStream();stream.WriteByte(19);stream.Position=0;
        bool refused=false;try{refused=!doc.Save(stream,binary);}catch(NotSupportedException){refused=true;}
        Check(refused&&stream.Position==0&&stream.Length==1&&stream.ToArray()[0]==19,"Downsave silently discarded content or wrote output");
    }
    private static void AmLifecycle(int operation)
    {
        var (doc,root)=AmSeed();var a=root.Attributes.Single();var d=root.Block.AttributeDefinitions["VALUE"];
        switch(operation)
        {
            case 0:
                var clone=a.GetMText();clone.Style.Name="OTHER";a.SetMText(clone);
                Check(doc.TextStyles.Contains("OTHER"),"Replacement style not registered");Check(!doc.TextStyles.Remove("OTHER"),"Live style removed");
                a.SetMText(null!);Check(doc.TextStyles.Remove("OTHER"),"Cleared embedded style remains referenced");break;
            case 1:a.SetMText(null!);d.SetMText(null!);Check(doc.TextStyles.Remove("BODY"),"Removed content retained style use");break;
            case 2:doc.TextStyles["BODY"].Name="RENAMED";Equal("RENAMED",a.GetMText().Style.Name,"Shared style rename not reflected");Check(!doc.TextStyles.Remove("RENAMED"),"Renamed style unprotected");break;
            case 3:doc.Entities.Remove(root);d.SetMText(null!);Check(doc.TextStyles.Remove("BODY"),"Detached INSERT retained embedded style use");break;
            case 4:Check(doc.TextStyles.GetReferences("BODY").Sum(r=>r.Uses)==2,"Exact two embedded-style uses");break;
            case 5:a.SetMText(new AttributeMText{Style=doc.TextStyles["Standard"]});Check(doc.TextStyles.GetReferences("Standard").Any(r=>ReferenceEquals(r.Reference,a)&&r.Uses==2),"Common/embedded use aggregation");break;
            case 6:var source=new AttributeMText{Value="before",Style=new TextStyle("NEW","txt.shx")};a.SetMText(source);source.Value="after";Check(source.Style.Owner==null,"Setter adopted caller resource");Equal("before",a.GetMText().Value,"Setter retained mutable content");break;
            case 7:Throws<NotSupportedException>(()=>DxfR12Codec.Create(new[]{root}));break;
        }
    }
    private static void AmTransform(int operation)
    {
        var (doc,root)=AmSeed();var a=root.Attributes.Single();var before=a.GetMText();Vector3 fallback=a.Position;
        Matrix3 matrix=operation<6?Matrix3.RotationZ(operation*Math.PI/6)*Matrix3.Scale(2):operation==6?Matrix3.Scale(1,2,1):operation==7?Matrix3.Scale(-1,1,1):operation==8?Matrix3.Scale(0):new Matrix3(1,1,0,0,1,0,0,0,1);
        if(operation>=6)
        {Throws<NotSupportedException>(()=>root.TransformBy(matrix,Vector3.UnitX));R12Vector(fallback,a.Position);R12Vector(before.Position,a.GetMText().Position);return;}
        root.TransformBy(matrix,new Vector3(3,4,5));
        R12Vector(matrix*before.Position+new Vector3(3,4,5),a.GetMText().Position);Near(before.Height*2,a.GetMText().Height,"Transformed height");
        Check(doc.TextStyles.GetReferences("BODY").Sum(r=>r.Uses)==2,"Transform changed resource uses");AmLoad(AmBytes(doc,false));
    }
    private static void AmClone()
    {
        var (_,root)=AmSeed();var clone=(Insert)root.Clone();var a=clone.Attributes.Single();
        Check(ReferenceEquals(a.Definition,clone.Block.AttributeDefinitions["VALUE"]),"Cloned attribute is not bound to copied canonical definition");
        var body=a.GetMText();body.Value="clone";a.SetMText(body);Equal("rich\\Pcontent 😀",root.Attributes.Single().GetMText().Value,"Clone shared content");
        var doc=new DxfDocument(DxfVersion.AutoCad2018);doc.Entities.Add(clone);AmLoad(AmBytes(doc,true));
        Check(clone.Explode().OfType<MText>().Any(m=>m.Value=="clone"),"Explode discarded multiline content");
    }
    private static void AmSync()
    {
        var block=new Block("SYNC");var d=new AttributeDefinition("VALUE");d.SetMText(new AttributeMText{Value="definition",Position=Vector3.UnitY});block.AttributeDefinitions.Add(d);
        var root=new Insert(block,Vector3.UnitX);Equal("definition",root.Attributes.Single().GetMText().Value,"Constructor lost MTEXT");
        var t=root.Attributes.Single().GetMText();t.Value="instance";root.Attributes.Single().SetMText(t);for(int i=0;i<3;i++)
        {
            root.Sync();Equal("instance",root.Attributes.Single().GetMText().Value,"Sync replaced instance value");
            R12Vector(new Vector3(1,1,0),root.Attributes.Single().GetMText().Position);
            R12Vector(Vector3.UnitY,d.GetMText().Position);
        }
    }
}
