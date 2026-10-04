// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;
using Attribute = netDxf.Entities.Attribute;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterAttributeMTextTests()
    {
        foreach (bool binary in new[] { false, true })
        {
            Run("attribute-mtext/wire/" + binary, () => AmtWire(binary));
            foreach (DxfVersion version in SupportedVersions.Where(v => v != DxfVersion.AutoCad2018))
                Run($"attribute-mtext/older-refusal/{version}/{binary}", () => AmtOlder(version, binary));
            foreach (int fault in Enumerable.Range(0, 8))
                Run($"attribute-mtext/malformed/{fault}/{binary}", () => AmtMalformed(fault, binary));
        }
        Run("attribute-mtext/model/immutable", AmtImmutable);
        Run("attribute-mtext/model/clone", AmtClone);
        Run("attribute-mtext/model/style-lifecycle", AmtReferences);
        Run("attribute-mtext/model/foreign-admission", AmtForeign);
        Run("attribute-mtext/model/transform", AmtTransform);
        Run("attribute-mtext/model/insert-sync", AmtSync);
        Run("attribute-mtext/model/r12-refusal", AmtR12);
    }

    private const string AmtValue = "Embedded αβγ\\PSecond line";
    private static AttributeMText AmtBody(string content, TextStyle style)
    {
        return new AttributeMText(new DxfTag[] {
            new(210,0.0),new(220,0.0),new(230,1.0),new(10,30.0),new(20,40.0),new(30,5.0),
            new(40,2.5),new(41,12.0),new(46,7.0),new(71,(short)5),new(72,(short)1),new(1,content),
            new(7,style.Name),new(11,1.0),new(21,0.0),new(31,0.0),new(42,11.0),new(43,6.0),
            new(73,(short)2),new(44,1.2),new(90,17),new(45,1.5),new(63,(short)3),
            new(421,0x2468ac),new(441,0x02000050) },style);
    }
    private static Insert AmtSeed()
    {
        var style = new TextStyle("EMBEDDED_ONLY", "txt.shx");
        var definition = new AttributeDefinition("MULTILINE") {
            Value="Fallback definition", Prompt="Independent prompt", Position=new Vector3(1,2,3),
            Height=9, WidthFactor=.8, Alignment=TextAlignment.MiddleRight,
            IsBackward=true, IsUpsideDown=true, MultilineText=AmtBody(AmtValue,style) };
        var block = new Block("MTEXT_ATTRIBUTES"); block.AttributeDefinitions.Add(definition);
        block.Entities.Add(new Line(Vector3.Zero,Vector3.UnitX));
        var root = new Insert(block);
        var attribute = root.Attributes.Single();
        attribute.Value="Fallback instance"; attribute.Position=new Vector3(8,9,10);
        attribute.Height=6; attribute.WidthFactor=1.3; attribute.Alignment=TextAlignment.BottomCenter;
        attribute.MultilineText=AmtBody(AmtValue + " instance",style);
        return root;
    }
    private static void AmtCheck(Insert root, bool edited)
    {
        var definition=root.Block.AttributeDefinitions["MULTILINE"];
        var attribute=root.Attributes.Single();
        Equal("Fallback definition",definition.Value,"ATTDEF fallback text");
        Equal("Independent prompt",definition.Prompt,"ATTDEF prompt");
        Equal("Fallback instance",attribute.Value,"ATTRIB fallback text");
        Near(9,definition.Height,"ATTDEF fallback height"); Near(.8,definition.WidthFactor,"ATTDEF width factor");
        Near(6,attribute.Height,"ATTRIB fallback height"); Near(1.3,attribute.WidthFactor,"ATTRIB width factor");
        Equal(TextAlignment.MiddleRight,definition.Alignment,"ATTDEF fallback alignment");
        Equal(TextAlignment.BottomCenter,attribute.Alignment,"ATTRIB fallback alignment");
        Check(definition.IsBackward && definition.IsUpsideDown,"Multiline type overwrote parent generation flags");
        R12Vector(new Vector3(1,2,3),definition.Position); R12Vector(new Vector3(8,9,10),attribute.Position);
        Check(definition.MultilineText!=null && attribute.MultilineText!=null,"Embedded content missing");
        Equal(AmtValue,definition.MultilineText.Value,"Definition multiline default");
        Equal(edited ? "Changed multiline\\PText" : AmtValue+" instance",attribute.MultilineText.Value,"Instance multiline value");
        foreach(var text in new[]{definition.MultilineText,attribute.MultilineText})
        {
            R12Vector(new Vector3(30,40,5),text.Position); Near(2.5,text.Height,"Embedded height");
            Near(12,text.RectangleWidth,"Embedded width"); Equal((double?)7,text.DefinedHeight,"Defined height presence");
            Equal(MTextAttachmentPoint.MiddleCenter,text.AttachmentPoint,"Embedded attachment");
            Equal("EMBEDDED_ONLY",text.Style.Name,"Independent embedded style");
            var detached=text.ToMText(); Check(detached.Owner==null && detached.Handle==null,"Embedded body acquired a database identity");
            Check(detached.BackgroundFill!=null,"Embedded background lost");
        }
        Check(ReferenceEquals(definition.MultilineText.Style,attribute.MultilineText.Style),"Embedded shared style split");
        Equal(edited ? null : (double?)11,attribute.MultilineText.ActualWidth,"Edited saved extents");
    }
    private static void AmtWire(bool binary)
    {
        var root=AmtSeed(); var doc=new DxfDocument(DxfVersion.AutoCad2018); doc.Entities.Add(root);
        foreach(int stage in Enumerable.Range(0,3))
        {
            using var bytes=new MemoryStream();
            bool transport=stage==1 ? !binary : binary;
            Check(doc.Save(bytes,transport),"Multiline attribute save failed");
            File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"attribute-mtext-{(binary?"binary":"text")}-{new[]{"source","output","resave"}[stage]}.dxf"),bytes.ToArray());
            bytes.Position=0; doc=DxfDocument.Load(bytes) ?? throw new InvalidOperationException("Multiline attribute load failed");
            root=doc.Entities.All.OfType<Insert>().Single(); AmtCheck(root,stage!=0);
            Check(ReferenceEquals(root.Attributes.Single().MultilineText.Style,doc.TextStyles["EMBEDDED_ONLY"]),"Style not canonical after reload");
            if(stage==0) root.Attributes.Single().MultilineText=root.Attributes.Single().MultilineText.WithValue("Changed multiline\\PText");
        }
    }
    private static void AmtOlder(DxfVersion version,bool binary)
    {
        var doc=new DxfDocument(version); doc.Entities.Add(AmtSeed());
        using var output=new MemoryStream(); output.WriteByte(77); output.Position=0;
        bool refused=false;
        try { refused=!doc.Save(output,binary); } catch(NotSupportedException) { refused=true; }
        Check(refused,"Older target silently accepted multiline content");
        Check(output.Position==0 && output.Length==1 && output.ToArray()[0]==77 && output.CanWrite,"Older-version refusal touched destination");
    }
    private static void AmtMalformed(int fault,bool binary)
    {
        var doc=new DxfDocument(DxfVersion.AutoCad2018);doc.Entities.Add(AmtSeed());
        using var source=new MemoryStream();Check(doc.Save(source,binary),"Malformed seed save");source.Position=0;
        var raw=DxfRawDocument.Load(source);var row=raw.Sections.SelectMany(s=>s.Records).Single(r=>r.Name=="ATTRIB");
        var tags=row.Tags.ToList();int split=tags.FindIndex(t=>t.Code==101);
        Check(split>=0,"Missing embedded marker in authored fixture");
        switch(fault)
        {
            case 0: tags[split]=new DxfTag(101,"Unknown Object");break;
            case 1: tags.Insert(split+1,new DxfTag(40,99.0));break;
            case 2: tags.RemoveAt(tags.FindIndex(split+1,t=>t.Code==20));break;
            case 3: tags.Insert(split+1,new DxfTag(330,"FFFF"));break;
            case 4: tags.Insert(split+1,new DxfTag(101,"Embedded Object"));break;
            case 5: tags.RemoveAt(tags.FindIndex(split+1,t=>t.Code==1));break;
            case 6: tags[tags.FindIndex(split+1,t=>t.Code==40)]=new DxfTag(40,0.0);break;
            case 7: tags[tags.FindIndex(split+1,t=>t.Code==71)]=new DxfTag(71,(short)10);break;
        }
        var bad=raw.WithRecord(row,tags);using var bytes=new MemoryStream();bad.Save(bytes,binary);bytes.Position=0;
        bool refused=false;try { refused=DxfDocument.Load(bytes)==null; }
        catch(Exception e) when(e is FormatException || e is NotSupportedException || e is ArgumentException || e is InvalidDataException) { refused=true; }
        Check(refused,"Malformed embedded content was accepted or silently skipped");Check(bytes.CanRead,"Reader closed caller stream");
    }
    private static void AmtImmutable()
    {
        var text=AmtBody(AmtValue,TextStyle.Default);var tags=text.Tags.ToArray();
        var edited=text.WithValue("new\\Pcontent");
        Check(tags.SequenceEqual(text.Tags),"Immutable body mutated");Equal(AmtValue,text.Value,"Old body value");
        Check(edited.ActualWidth==null && edited.ActualHeight==null,"Value edit retained stale measured extents");
        Check(ReferenceEquals(text,text.WithValue(text.Value)),"No-op value edit lost identity");
        var copy=text.Tags.ToArray(); copy[0]=new DxfTag(210,7.0);Check(tags.SequenceEqual(text.Tags),"External tag array mutated model");
    }
    private static void AmtClone()
    {
        var root=AmtSeed();var original=root.Attributes.Single();var copy=(Attribute)original.Clone();
        Check(copy.MultilineText!=null && !ReferenceEquals(original.MultilineText.Style,copy.MultilineText.Style),"Attribute clone shared mutable style");
        copy.MultilineText=copy.MultilineText.WithValue("clone");Equal(AmtValue+" instance",original.MultilineText.Value,"Clone modified source");
        var definition=root.Block.AttributeDefinitions["MULTILINE"];var cloned=(AttributeDefinition)definition.Clone();
        Check(cloned.MultilineText!=null && !ReferenceEquals(cloned.MultilineText.Style,definition.MultilineText.Style),"Definition clone lost isolation");
        var block=(Block)root.Block.Clone();Check(block.AttributeDefinitions["MULTILINE"].MultilineText!=null,"Block clone lost multiline default");
    }
    private static void AmtReferences()
    {
        var doc=new DxfDocument();var root=AmtSeed();doc.Entities.Add(root);var style=doc.TextStyles["EMBEDDED_ONLY"];
        Check(style.HasReferences() && !doc.TextStyles.Remove(style),"Embedded-only resource was removable");
        var refs=style.GetReferences();Equal(2,refs.Count,"Embedded style reference inventory");
        root.Attributes.Single().MultilineText=null;Check(!doc.TextStyles.Remove(style),"Definition reference omitted");
        root.Block.AttributeDefinitions["MULTILINE"].MultilineText=null;
        Check(!style.HasReferences() && doc.TextStyles.Remove(style),"Cleared embedded style retained a stale reference");
        Equal(2,refs.Count,"Reference snapshot changed after edits");
    }
    private static void AmtForeign()
    {
        var source=new DxfDocument();var foreign=source.TextStyles.Add(new TextStyle("FOREIGN_EMBEDDED","txt.shx"));
        var root=AmtSeed();root.Attributes.Single().MultilineText=new AttributeMText("foreign",Vector3.Zero,1,0,foreign);
        var destination=new DxfDocument();var before=new CompatibilityState(destination);
        Throws<InvalidOperationException>(()=>destination.Entities.Add(root));before.CheckUnchanged();
        Check(root.Handle==null && root.Owner==null && ReferenceEquals(foreign.Owner,source.TextStyles),"Rejected admission stole source resources");
    }
    private static void AmtTransform()
    {
        var attribute=AmtSeed().Attributes.Single();var before=attribute.MultilineText;
        var shear=new Matrix3(1,1,0,0,1,0,0,0,1);var position=attribute.Position;
        Throws<NotSupportedException>(()=>attribute.TransformBy(shear,Vector3.Zero));
        Check(attribute.Position==position && ReferenceEquals(before,attribute.MultilineText),"Rejected transform changed attribute");
        var matrix=new Matrix3(2,0,0,0,2,0,0,0,2);attribute.TransformBy(matrix,new Vector3(1,2,3));
        R12Vector(new Vector3(61,82,13),attribute.MultilineText.Position);Near(5,attribute.MultilineText.Height,"Embedded transform height");
        Near(24,attribute.MultilineText.RectangleWidth,"Embedded transform width");
    }
    private static void AmtSync()
    {
        var root=AmtSeed();root.TransformAttributes();
        var text=root.Attributes.Single().MultilineText;Check(text!=null,"INSERT transform lost embedded default");
        Equal(AmtValue,text.Value,"INSERT transform did not use definition content");
        var clone=(Insert)root.Clone();Check(clone.Attributes.Single().MultilineText!=null,"INSERT clone lost embedded text");
    }
    private static void AmtR12()
    {
        var root=AmtSeed();using var output=new MemoryStream();output.WriteByte(42);output.Position=0;
        Throws<NotSupportedException>(()=>DxfR12Codec.Save(output,new[]{root}));
        Check(output.Position==0 && output.Length==1 && output.ToArray()[0]==42,"R12 silently dropped multiline data");
    }
}
