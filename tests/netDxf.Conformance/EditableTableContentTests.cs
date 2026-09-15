using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterEditableTableContentTests()
    {
        foreach(string file in TableContentFiles)foreach(bool input in new[]{false,true})foreach(bool binary in new[]{false,true})
        {
            Run($"table-content-edit/native-noop/{file}/{input}/{binary}",()=>EditableContentNative(file,input,binary,false));
            Run($"table-content-edit/native-edit/{file}/{input}/{binary}",()=>EditableContentNative(file,input,binary,true));
        }
        foreach(DxfVersion version in SupportedVersions.Where(v=>v>=DxfVersion.AutoCad2004))foreach(bool binary in new[]{false,true})
            Run($"table-content-edit/schema/{version}/{binary}",()=>EditableContentSchema(version,binary));
        foreach(bool binary in new[]{false,true})
        {
            foreach(int fault in Enumerable.Range(0,19))Run($"table-content-edit/atomic/{fault}/{binary}",()=>EditableContentAtomic(fault,binary));
            foreach(int shape in Enumerable.Range(0,8))Run($"table-content-edit/unqualified/{shape}/{binary}",()=>EditableContentUnqualified(shape,binary));
            Run($"table-content-edit/style/{binary}",()=>EditableContentStyle(binary));
            Run($"table-content-edit/table-style/{binary}",()=>EditableContentTableStyle(binary));
            Run($"table-content-edit/snapshots/{binary}",()=>EditableContentSnapshots(binary));
            Run($"table-content-edit/new-target-dispose/{binary}",()=>EditableContentNewTargetDispose(binary));
            Run($"table-content-edit/opaque-style/{binary}",()=>EditableContentOpaqueStyle(binary));
            Run($"table-content-edit/post-edit-lifecycle/{binary}",()=>EditableContentPostEdit(binary));
            foreach(string kind in new[]{"STYLE","LTYPE","APPID","ENTITY","BLOCK_RECORD","BLOCK_MEMBER","ATTRIB","ENDBLK"})
                Run($"table-content-edit/retained-reference/{kind}/{binary}",()=>EditableContentReferences(kind,binary));
        }
        foreach(string text in new[]{"TABLEFORMAT_BEGIN","DATAMAP_BEGIN","CELLCONTENT_BEGIN","LINKEDTABLEDATACELL_BEGIN","ACVALUE_END",@"Literal\U+0041","😀漢字"})
            foreach(DxfVersion version in new[]{DxfVersion.AutoCad2004,DxfVersion.AutoCad2018})foreach(bool binary in new[]{false,true})
                Run($"table-content-edit/text/{text}/{version}/{binary}",()=>EditableContentText(text,version,binary));
        Run("table-content-edit/value-validation",EditableContentValidation);
        Run("table-content-edit/encoded-string-limit",EditableContentStringLimit);
        Run("table-content-edit/metadata-tag-limit",EditableContentMetadataLimit);
        Run("table-content-edit/exact-tag-limit",EditableContentExactTagLimit);
        Run("table-content-edit/nonfinite-wire",EditableContentNonfiniteWire);
    }

    private static object EditedScalar(object value)=>value switch {int i=>i+7,double d=>d+0.125,string s=>"Edited "+s,Vector3 p=>p+new Vector3(1,2,3),_=>throw new Exception("unexpected scalar")};
    private static void EditableContentNative(string file,bool input,bool binary,bool edit)
    {
        byte[] bytes=TableContentSourceBytes(file);if(input)bytes=TableContentRawBytes(TableContentRaw(bytes),true);
        var doc=TableContentLoad(bytes);var contents=doc.Objects.Items.OfType<DxfStoredTableContent>().ToArray();
        var tables=doc.Blocks.SelectMany(b=>b.Entities).OfType<StoredTable>().ToArray();
        var tablePackets=tables.ToDictionary(t=>t.Handle,t=>OwnershipTagValues(t.Payload).ToArray());
        var agreement=tables.ToDictionary(t=>t.Handle,t=>t.BackingLiteralValuesAgree);
        var nativeCounts=new Dictionary<string,int>{{"AF",7},{"116",20},{"747",8},{"13EF",13},{"1385",11},{"131E",11}};
        foreach(var content in contents)
        {
            Equal(nativeCounts[content.Handle],content.StoredValues.Count,"native qualified scalar count");
            var oldPayload=content.Payload;var oldValues=content.StoredValues;var oldSubclasses=content.Subclasses;var oldReferences=content.References;
            var packet=OwnershipTagValues(oldPayload).ToArray();var refs=oldReferences.ToArray();var owner=content.Owner;long seed=OwnershipSeed(doc);int count=doc.Objects.Items.Count;
            var edits=content.StoredValues.Select((v,i)=>v.WithValue(edit?EditedScalar(v.Value):v.Value,v.FormattedText==null?null:edit?"Stored display "+i:v.FormattedText));
            content.ReplaceContent(edit?"Edited name":content.Name,edit?"Edited description":content.Description,content.TableStyle,edits);
            Check(packet.SequenceEqual(OwnershipTagValues(oldPayload)),"native old payload changed");Check(refs.SequenceEqual(oldReferences),"native old references changed");
            if(!edit)Check(ReferenceEquals(oldPayload,content.Payload)&&ReferenceEquals(oldValues,content.StoredValues)&&ReferenceEquals(oldSubclasses,content.Subclasses),"native no-op changed snapshots");
            else foreach(var pair in oldValues.Zip(content.StoredValues))
            {Equal(EditedScalar(pair.First.Value),pair.Second.Value,"native edited scalar");Equal(pair.First.StoredFormatFlags,pair.Second.StoredFormatFlags,"value flags changed");Equal(pair.First.StoredUnitType,pair.Second.StoredUnitType,"value units changed");Equal(pair.First.FormatString,pair.Second.FormatString,"value format changed");}
            Check(ReferenceEquals(owner,content.Owner),"native source owner changed");Equal(seed,OwnershipSeed(doc),"native edit allocated handles");Equal(count,doc.Objects.Items.Count,"native edit changed registration");
        }
        foreach(var table in tables)
        {Check(tablePackets[table.Handle].SequenceEqual(OwnershipTagValues(table.Payload)),"TABLE payload synchronized implicitly");if(edit&&agreement[table.Handle]==true)Equal(false,table.BackingLiteralValuesAgree,"backing comparison stayed stale");}
        var expected=contents.ToDictionary(c=>c.Handle,c=>OwnershipTagValues(c.Payload).ToArray());
        var loaded=TableContentLoad(TableContentSave(doc,binary,$"editable-table-content-{(edit?"native":"noop")}-{file}-{input}-{binary}.dxf"));
        foreach(var content in loaded.Objects.Items.OfType<DxfStoredTableContent>())Check(expected[content.Handle].SequenceEqual(OwnershipTagValues(content.Payload)),"native edited payload changed after reload");
        foreach(var table in loaded.Blocks.SelectMany(b=>b.Entities).OfType<StoredTable>())Check(tablePackets[table.Handle].SequenceEqual(OwnershipTagValues(table.Payload)),"native TABLE changed after save");
        Equal(0,loaded.Objects.Validate().Count,"edited native graph invalid");
    }

    private static List<DxfTag> EditableValueFrame(DxfVersion version,object value)
    {
        int kind=value is int?1:value is double?2:value is string?4:32;
        var result=new List<DxfTag>{new(1,"CELLCONTENT_BEGIN"),new(90,1),new(300,"VALUE")};
        if(version>=DxfVersion.AutoCad2007)result.Add(new(93,6));result.Add(new(90,kind));
        if(value is Vector3 point){result.Add(new(11,point.X));result.Add(new(21,point.Y));result.Add(new(31,point.Z));}
        else result.Add(new((short)(kind==1?91:kind==2?140:1),value));
        if(version>=DxfVersion.AutoCad2007)result.AddRange(new DxfTag[]{new(94,0),new(300,""),new(302,"display"),new(304,"ACVALUE_END")});
        result.Add(new(91,0));result.Add(new(309,"CELLCONTENT_END"));return result;
    }
    private static DxfRawDocument EditableContentRaw(DxfVersion version,Action<DxfDocument,List<DxfTag>>? setup=null)
        =>TableContentMinimal(version,(doc,body)=>
        {
            var linked=new List<DxfTag>{new(90,0),new(91,1),new(301,"ROW"),new(1,"LINKEDTABLEDATAROW_BEGIN"),new(90,4)};
            foreach(object value in new object[]{int.MinValue,-0.0,"text",new Vector3(-0.0,2,3)})
            {linked.AddRange(new DxfTag[]{new(300,"CELL"),new(1,"LINKEDTABLEDATACELL_BEGIN"),new(95,1),new(302,"CONTENT")});linked.AddRange(EditableValueFrame(version,value));linked.Add(new(309,"LINKEDTABLEDATACELL_END"));}
            linked.Add(new(309,"LINKEDTABLEDATAROW_END"));linked.Add(new(92,0));body.RemoveRange(4,3);body.InsertRange(4,linked);setup?.Invoke(doc,body);
        });
    private static DxfDocument EditableContentLoad(DxfVersion version,bool binary,Action<DxfDocument,List<DxfTag>>? setup=null)=>TableContentLoad(TableContentRawBytes(EditableContentRaw(version,setup),binary));
    private static DxfStoredTableContent EditableContentObject(DxfDocument doc)=>doc.Objects.Items.OfType<DxfStoredTableContent>().Single();
    private static void EditableContentSchema(DxfVersion version,bool binary)
    {
        var doc=EditableContentLoad(version,binary);var content=EditableContentObject(doc);Equal(4,content.StoredValues.Count,"scalar kinds projected");
        content.ReplaceContent("name",@"description\U+0041",null,content.StoredValues.Select(v=>v.WithValue(EditedScalar(v.Value),v.FormattedText==null?null:"explicit display")));
        var before=OwnershipTagValues(content.Payload).ToArray();var values=content.StoredValues;content.ReplaceContent(content.Name,content.Description,null,values.Select(v=>v.WithValue(v.Value,v.FormattedText)));
        Check(ReferenceEquals(values,content.StoredValues),"synthetic no-op changed value snapshots");
        var loaded=EditableContentObject(TableContentLoad(TableContentSave(doc,binary,$"editable-table-content-schema-{version}-{binary}.dxf")));
        Check(before.SequenceEqual(OwnershipTagValues(loaded.Payload)),"synthetic edited packet changed");Equal(@"description\U+0041",loaded.Description,"literal Unicode escape changed");
    }

    private sealed class EditableContentSequence : IEnumerable<DxfStoredTableContentValueEdit>
    {
        private readonly DxfStoredTableContentValueEdit value;
        private readonly Action? acquire,move,current,dispose;
        public EditableContentSequence(DxfStoredTableContentValueEdit value,Action? acquire=null,Action? move=null,Action? current=null,Action? dispose=null)
        {this.value=value;this.acquire=acquire;this.move=move;this.current=current;this.dispose=dispose;}
        public IEnumerator<DxfStoredTableContentValueEdit> GetEnumerator(){this.acquire?.Invoke();return new Cursor(this);}
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>this.GetEnumerator();
        private sealed class Cursor : IEnumerator<DxfStoredTableContentValueEdit>
        {
            private readonly EditableContentSequence source;private int index=-1;
            public Cursor(EditableContentSequence source){this.source=source;}
            public DxfStoredTableContentValueEdit Current{get{this.source.current?.Invoke();return this.source.value;}}
            object System.Collections.IEnumerator.Current=>this.Current;
            public bool MoveNext(){this.source.move?.Invoke();return ++this.index==0;}
            public void Dispose()=>this.source.dispose?.Invoke();public void Reset()=>throw new NotSupportedException();
        }
    }
    private static void EditableContentAtomic(int fault,bool binary)
    {
        var doc=EditableContentLoad(DxfVersion.AutoCad2018,binary);var content=EditableContentObject(doc);var request=content.StoredValues[0].WithValue(123,"shown");
        var payload=content.Payload;var values=content.StoredValues;var subclasses=content.Subclasses;var refs=content.References;var packet=OwnershipTagValues(payload).ToArray();
        IEnumerable<DxfStoredTableContentValueEdit> sequence=new[]{request};string name="changed",description="changed";DxfDatabaseObject? style=null;
        Action fail=()=>throw new InvalidOperationException("caller failure");Action reenter=()=>{try{content.ReplaceContent(content.Name,content.Description,null,Array.Empty<DxfStoredTableContentValueEdit>());}catch(InvalidOperationException){}};
        if(fault==0)sequence=null!;if(fault==1)sequence=new DxfStoredTableContentValueEdit[]{null!};if(fault==2)sequence=new[]{request,request};
        if(fault==3)sequence=new[]{EditableContentObject(EditableContentLoad(DxfVersion.AutoCad2018,false)).StoredValues[0].WithValue(123,"shown")};
        if(fault==4)name=null!;if(fault==5)description="bad\0text";
        if(fault==6)sequence=new EditableContentSequence(request,acquire:fail);if(fault==7)sequence=new EditableContentSequence(request,move:fail);
        if(fault==8)sequence=new EditableContentSequence(request,current:fail);if(fault==9)sequence=new EditableContentSequence(request,dispose:fail);
        if(fault==10)sequence=new EditableContentSequence(request,acquire:reenter);if(fault==11)sequence=new EditableContentSequence(request,move:reenter);
        if(fault==12)sequence=new EditableContentSequence(request,current:reenter);if(fault==13)sequence=new EditableContentSequence(request,dispose:reenter);
        if(fault==14)sequence=new EditableContentSequence(request,dispose:()=>doc.DrawingVariables.AcadVer=DxfVersion.AutoCad2013);
        if(fault==15)sequence=new EditableContentSequence(request,dispose:()=>doc.Objects.Root.Remove("CONTENT"));
        if(fault==16)style=new DxfXRecord();
        if(fault==17){content.ReplaceContent("first change",content.Description,null,Array.Empty<DxfStoredTableContentValueEdit>());payload=content.Payload;values=content.StoredValues;subclasses=content.Subclasses;refs=content.References;packet=OwnershipTagValues(payload).ToArray();}
        if(fault==18){doc.DrawingVariables.AcadVer=DxfVersion.AutoCad2013;name=content.Name;description=content.Description;sequence=Array.Empty<DxfStoredTableContentValueEdit>();}
        bool rejected=false;try{content.ReplaceContent(name,description,style,sequence);}catch(ArgumentException){rejected=true;}catch(InvalidOperationException){rejected=true;}catch(NotSupportedException){rejected=true;}
        Check(rejected,"invalid content edit accepted");Check(ReferenceEquals(payload,content.Payload)&&ReferenceEquals(values,content.StoredValues)&&ReferenceEquals(subclasses,content.Subclasses),"rejected edit swapped snapshots");
        Check(packet.SequenceEqual(OwnershipTagValues(content.Payload))&&refs.SequenceEqual(content.References),"rejected edit changed packet or dependencies");
    }

    private static void EditableContentUnqualified(int shape,bool binary)
    {
        var doc=EditableContentLoad(DxfVersion.AutoCad2018,binary,(_,body)=>
        {
            int start=body.FindIndex(t=>t.Code==1&&(string)t.Value=="CELLCONTENT_BEGIN");
            if(shape==0)body[start+3]=new(93,8);
            if(shape==1)body[start+1]=new(90,2);
            if(shape==2)body.Insert(start+6,new(300,"private scalar tail"));
            if(shape==3){int end=body.FindIndex(start,t=>t.Code==309&&(string)t.Value=="CELLCONTENT_END");body[end-1]=new(91,1);}
            if(shape==4){body.Insert(start,new(1,"PRIVATE_BEGIN"));body.Insert(start+1,new(309,"PRIVATE_END"));}
            if(shape==5)body.Insert(3,new(300,"unknown linked header"));
            if(shape==6)body.Insert(body.Count-2,new(100,"PrivateContent"));
            if(shape==7)body.InsertRange(start,new DxfTag[]{new(102,"{PRIVATE"),new(300,"value"),new(102,"}")});
        });
        if(shape>=6){Equal(0,doc.Objects.Items.OfType<DxfStoredTableContent>().Count(),"private shape typed");return;}
        var content=EditableContentObject(doc);Equal(shape==4?0:shape==5?4:3,content.StoredValues.Count,"unqualified frame exposed");
        var before=OwnershipTagValues(content.Payload).ToArray();
        if(shape==5){bool rejected=false;try{content.ReplaceContent("name","description",null,Array.Empty<DxfStoredTableContentValueEdit>());}catch(NotSupportedException){rejected=true;}Check(rejected,"unknown header editable");}
        else content.ReplaceContent(content.Name,content.Description,null,Array.Empty<DxfStoredTableContentValueEdit>());
        Check(before.SequenceEqual(OwnershipTagValues(content.Payload)),"unqualified packet changed");
    }

    private static DxfDocument EditableContentStyleDocument(bool binary,bool opaque=false)
    {
        string handle="";var raw=EditableContentRaw(DxfVersion.AutoCad2018,(source,_)=>{var placeholder=new DxfXRecord();source.Objects.Root.Add("EDIT_STYLE",placeholder);handle=placeholder.Handle;});
        var row=raw.Sections.Single(s=>s.Name=="OBJECTS").Records.Single(r=>r.Tags.Any(t=>t.Code==5&&(string)t.Value==handle));int boundary=row.Tags.ToList().FindIndex(t=>t.Code==100);
        raw=raw.WithRecord(row,row.Tags.Take(boundary).Select(t=>t.Code==0?new DxfTag(0,"TABLESTYLE"):t).Concat(new DxfTag[]{new(100,opaque?"PrivateTableStyle":"AcDbTableStyle")}));
        return TableContentLoad(TableContentRawBytes(raw,binary));
    }
    private static void EditableContentStyle(bool binary)
    {
        var doc=EditableContentStyleDocument(binary);var content=EditableContentObject(doc);var style=doc.Objects.Items.OfType<DxfTableStyle>().Single();
        var old=content.References;content.ReplaceContent(content.Name,content.Description,style,Array.Empty<DxfStoredTableContentValueEdit>());
        Check(ReferenceEquals(style,content.TableStyle)&&content.References.Contains(style),"explicit style identity not retained");Equal(0,old.Count,"old reference snapshot changed");
        Throws<InvalidOperationException>(()=>doc.Objects.EraseOwnedTree(style));content.ReplaceContent(content.Name,content.Description,null,Array.Empty<DxfStoredTableContentValueEdit>());
        doc.Objects.EraseOwnedTree(style);Check(style.IsErased,"released standalone style still protected");
        var loaded=EditableContentObject(TableContentLoad(TableContentSave(doc,binary,$"editable-table-content-style-{binary}.dxf")));Check(loaded.TableStyle==null,"released style not null");
    }
    private static void EditableContentNewTargetDispose(bool binary)
    {
        var doc=EditableContentStyleDocument(binary);var content=EditableContentObject(doc);var style=doc.Objects.Items.OfType<DxfTableStyle>().Single();var before=content.Payload;
        var request=content.StoredValues[0].WithValue(123,"shown");
        Throws<ArgumentException>(()=>content.ReplaceContent(content.Name,content.Description,style,new EditableContentSequence(request,dispose:()=>doc.Objects.EraseOwnedTree(style))));
        Check(style.IsErased&&ReferenceEquals(before,content.Payload)&&content.TableStyle==null,"new target removal during disposal was not revalidated");
        var foreign=EditableContentStyleDocument(binary).Objects.Items.OfType<DxfTableStyle>().Single();Throws<ArgumentException>(()=>content.ReplaceContent(content.Name,content.Description,foreign,new[]{request}));
    }
    private static void EditableContentOpaqueStyle(bool binary)
    {
        var doc=EditableContentStyleDocument(binary,true);var content=EditableContentObject(doc);var style=doc.Objects.Items.OfType<DxfOpaqueObject>().Single(o=>o.CodeName=="TABLESTYLE");
        content.ReplaceContent(content.Name,content.Description,style,Array.Empty<DxfStoredTableContentValueEdit>());
        var loaded=EditableContentObject(TableContentLoad(TableContentSave(doc,binary)));Check(loaded.TableStyle is DxfOpaqueObject&&loaded.TableStyle.Handle==style.Handle,"opaque style actual identity changed");
    }
    private static void EditableContentReferences(string kind,bool binary)
    {
        var doc=EditableContentLoad(DxfVersion.AutoCad2018,binary,(source,body)=>{var target=EditableTarget(source,kind);body.InsertRange(body.Count-2,new[]{new DxfTag(340,target.Handle),new DxfTag(340,target.Handle)});});
        var content=EditableContentObject(doc);var target=content.References.First();var before=content.References;
        content.ReplaceContent("edited",content.Description,null,content.StoredValues.Select(v=>v.WithValue(EditedScalar(v.Value),"shown")));
        Check(content.References.Count==2&&content.References.All(r=>ReferenceEquals(r,target))&&before.All(r=>ReferenceEquals(r,target)),"retained repeated reference identity changed");
        Check(!RemoveEditableTarget(doc,target,kind),"edited content released an unchanged dependency");
        var loaded=EditableContentObject(TableContentLoad(TableContentSave(doc,binary)));Check(loaded.References.Count==2&&loaded.References.All(t=>t.Handle==target.Handle),"retained metadata reference did not reload");
    }
    private static void EditableContentPostEdit(bool binary)
    {
        var doc=TableContentLoad(TableContentRawBytes(TableContentRaw(TableContentSourceBytes("acad_table_simple.dxf")),binary));var content=EditableContentObject(doc);
        content.ReplaceContent("edited",content.Description,content.TableStyle,content.StoredValues.Select(v=>v.WithValue(EditedScalar(v.Value),"display")));
        long seed=OwnershipSeed(doc);int count=doc.Objects.Items.Count;
        Throws<NotSupportedException>(()=>doc.Objects.CloneObject(content,doc.Objects.Root,"COPY"));Throws<NotSupportedException>(()=>doc.Objects.EraseOwnedTree(content));
        Throws<NotSupportedException>(()=>doc.Objects.EraseOwnedTree((DxfDatabaseObject)content.Owner));Check(!doc.Entities.Remove(doc.Entities.StoredTables.Single()),"edited backing TABLE removed");
        doc.DrawingVariables.AcadVer=DxfVersion.AutoCad2010;using var output=new MemoryStream();bool rejected=false;try{rejected=!doc.Save(output,binary);}catch(NotSupportedException){rejected=true;}catch(InvalidOperationException){rejected=true;}Check(rejected,"edited profile conversion accepted");Equal(0L,output.Length,"edited profile rejection emitted bytes");
        Equal(seed,OwnershipSeed(doc),"rejected edited lifecycle allocated handles");Equal(count,doc.Objects.Items.Count,"rejected edited lifecycle changed registration");
    }
    private static void EditableContentTableStyle(bool binary)
    {
        var doc=TableContentLoad(TableContentRawBytes(TableContentRaw(TableContentSourceBytes("acad_table_simple.dxf")),binary));var content=EditableContentObject(doc);var before=content.Payload;
        bool rejected=false;try{content.ReplaceContent(content.Name,content.Description,null,Array.Empty<DxfStoredTableContentValueEdit>());}catch(NotSupportedException){rejected=true;}
        Check(rejected&&ReferenceEquals(before,content.Payload),"TABLE-owned terminal style changed");
    }
    private static void EditableContentSnapshots(bool binary)
    {
        var doc=EditableContentLoad(DxfVersion.AutoCad2018,binary);var content=EditableContentObject(doc);var old=content.StoredValues;var payload=content.Payload;
        var list=new List<DxfStoredTableContentValueEdit>{old[1].WithValue(0.0,"display")};content.ReplaceContent(content.Name,content.Description,null,list);list.Clear();
        Check(!ReferenceEquals(payload,content.Payload),"signed zero treated as no-op");Equal(long.MinValue,BitConverter.DoubleToInt64Bits((double)old[1].Value),"old negative zero snapshot changed");Equal(0L,BitConverter.DoubleToInt64Bits((double)content.StoredValues[1].Value),"positive zero not retained");
        Check(ReferenceEquals(payload[0],content.Payload[0]),"untouched tag identity changed");Equal(int.MinValue,content.StoredValues[0].Value,"caller list mutated stored values");
    }
    private static void EditableContentText(string text,DxfVersion version,bool binary)
    {
        var doc=EditableContentLoad(version,binary);var content=EditableContentObject(doc);var value=content.StoredValues.Single(v=>v.Kind==DxfStoredTableContentValueKind.String);
        content.ReplaceContent(text,text,null,new[]{value.WithValue(text,value.FormattedText==null?null:text)});
        var loaded=EditableContentObject(TableContentLoad(TableContentSave(doc,binary)));Equal(text,loaded.Name,"edited name escaping");Equal(text,loaded.Description,"edited description escaping");
        Equal(text,loaded.StoredValues.Single(v=>v.Kind==DxfStoredTableContentValueKind.String).Value,"edited scalar escaping");
    }
    private static void EditableContentValidation()
    {
        var modern=EditableContentObject(EditableContentLoad(DxfVersion.AutoCad2018,false));var legacy=EditableContentObject(EditableContentLoad(DxfVersion.AutoCad2004,false));
        Action[] invalid={()=>modern.StoredValues[0].WithValue(1.0,"text"),()=>modern.StoredValues[0].WithValue(1),()=>legacy.StoredValues[0].WithValue(1,"text"),()=>modern.StoredValues[1].WithValue(double.NaN,"text"),()=>modern.StoredValues[3].WithValue(new Vector3(0,double.PositiveInfinity,0),"text"),()=>modern.StoredValues[2].WithValue("bad\ud800","text"),()=>modern.StoredValues[2].WithValue("good","bad\udfff"),()=>modern.StoredValues[2].WithValue("bad\0","text")};
        foreach(var action in invalid){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}Check(rejected,"invalid scalar request accepted");}
        var doc=EditableContentLoad(DxfVersion.AutoCad2018,false);var content=EditableContentObject(doc);content.ReplaceContent("line\nbreak",content.Description,null,Array.Empty<DxfStoredTableContentValueEdit>());
        using var output=new MemoryStream();bool textRejected=false;try{textRejected=!doc.Save(output,false);}catch(Exception){textRejected=true;}Check(textRejected&&output.Length==0,"newline text save emitted bytes");
        Equal("line\nbreak",EditableContentObject(TableContentLoad(TableContentSave(doc,true))).Name,"binary newline changed");
    }
    private static void EditableContentStringLimit()
    {
        foreach(var version in new[]{DxfVersion.AutoCad2004,DxfVersion.AutoCad2018})
        {
            var doc=EditableContentLoad(version,true);var content=EditableContentObject(doc);string exact=new('x',DxfStoredTableContent.MaximumEditedStringLength);
            content.ReplaceContent(exact,content.Description,null,Array.Empty<DxfStoredTableContentValueEdit>());Equal(exact,EditableContentObject(TableContentLoad(TableContentSave(doc,true))).Name,"exact admitted string failed");var old=content.Payload;
            foreach(string invalid in new[]{exact+"x",new string('\\',DxfStoredTableContent.MaximumEditedStringLength/7+1)})
            {bool rejected=false;try{content.ReplaceContent(invalid,content.Description,null,Array.Empty<DxfStoredTableContentValueEdit>());}catch(ArgumentException){rejected=true;}Check(rejected&&ReferenceEquals(old,content.Payload),"string admission overflow mutated payload");}
        }
    }
    private static void EditableContentMetadataLimit()
    {
        var doc=EditableContentLoad(DxfVersion.AutoCad2018,false);var content=EditableContentObject(doc);var old=content.Payload;var app=doc.ApplicationRegistries.Add(new ApplicationRegistry("EDIT_LIMIT"));
        var data=new XData(app);for(int i=0;i<1048576;i++)data.XDataRecord.Add(new XDataRecord(XDataCode.Int32,0));content.XData.Add(data);
        bool rejected=false;try{content.ReplaceContent(content.Name,content.Description,null,Array.Empty<DxfStoredTableContentValueEdit>());}catch(InvalidOperationException){rejected=true;}Check(rejected&&ReferenceEquals(old,content.Payload),"metadata-inclusive tag limit accepted");
        using var output=new MemoryStream();CheckSaveRejected(doc,output);Equal(0L,output.Length,"metadata overflow emitted bytes");
    }
    private static void EditableContentExactTagLimit()
    {
        var limits=new DxfRawOptions(maximumTags:1100000);var raw=DxfRawDocument.Create(TableContentMinimal(DxfVersion.AutoCad2018).Tags,false,limits);
        var row=raw.Sections.Single(s=>s.Name=="OBJECTS").Records.Single(r=>r.Name=="TABLECONTENT");var tags=row.Tags.ToList();tags.InsertRange(tags.Count-2,Enumerable.Repeat(new DxfTag(90,0),1048577-tags.Count));raw=raw.WithRecord(row,tags);
        raw=DxfRawDocument.Create(raw.Tags.Where(t=>t.Code!=999),true,limits);using var wire=new MemoryStream();raw.Save(wire,true);
        var doc=TableContentLoad(wire.ToArray());var content=EditableContentObject(doc);Equal(1048574,content.Payload.Count,"exact content payload budget");
        content.ReplaceContent("edited",content.Description,null,Array.Empty<DxfStoredTableContentValueEdit>());
        Equal(1048574,EditableContentObject(TableContentLoad(TableContentSave(doc,true))).Payload.Count,"exact complete content record failed reload");
        var snapshot=content.Payload;content.PersistentReactors.Add(doc.Objects.Root);Throws<InvalidOperationException>(()=>content.ReplaceContent(content.Name,content.Description,null,Array.Empty<DxfStoredTableContentValueEdit>()));
        Check(ReferenceEquals(snapshot,content.Payload),"reactor-inclusive overflow changed snapshot");using var output=new MemoryStream();CheckSaveRejected(doc,output);Equal(0L,output.Length,"reactor-inclusive overflow emitted bytes");
    }
    private static void EditableContentNonfiniteWire()
    {
        var raw=EditableContentRaw(DxfVersion.AutoCad2018);string text=System.Text.Encoding.UTF8.GetString(TableContentRawBytes(raw,false));
        var lines=text.Replace("\r\n","\n").Split('\n');int content=Array.IndexOf(lines,"TABLECONTENT");Check(content>=0,"nonfinite fixture content record missing");int index=Enumerable.Range(content,lines.Length-content-1).First(i=>i%2==0&&lines[i].Trim()=="140");
        foreach(string token in new[]{"NaN","Infinity","-Infinity"})
        {var changed=(string[])lines.Clone();changed[index+1]=token;using var input=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(string.Join("\n",changed)));bool rejected=false;try{rejected=DxfDocument.Load(input)==null;}catch(Exception){rejected=true;}Check(rejected,"nonfinite scalar wire admitted");}
    }
}
