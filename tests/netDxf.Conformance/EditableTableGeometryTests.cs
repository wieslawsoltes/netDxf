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
    private static void RegisterEditableTableGeometryTests()
    {
        foreach(string file in TableContentFiles)foreach(bool input in new[]{false,true})foreach(bool binary in new[]{false,true})
        {
            Run($"table-geometry-edit/native-noop/{file}/{input}/{binary}",()=>EditableGeometryNative(file,input,binary,false));
            Run($"table-geometry-edit/native-edit/{file}/{input}/{binary}",()=>EditableGeometryNative(file,input,binary,true));
        }
        foreach(DxfVersion version in SupportedVersions.Where(v=>v>=DxfVersion.AutoCad2004))foreach(bool binary in new[]{false,true})
            Run($"table-geometry-edit/schema/{version}/{binary}",()=>EditableGeometrySchema(version,binary));
        foreach(bool binary in new[]{false,true})
        {
            foreach(string kind in new[]{"STYLE","LTYPE","APPID","ENTITY","BLOCK_RECORD","BLOCK_MEMBER","ATTRIB","ENDBLK"})
                Run($"table-geometry-edit/references/{kind}/{binary}",()=>EditableGeometryReferences(kind,binary));
            foreach(int fault in Enumerable.Range(0,20))Run($"table-geometry-edit/atomic/{fault}/{binary}",()=>EditableGeometryAtomic(fault,binary));
            Run($"table-geometry-edit/metadata-noop/{binary}",()=>EditableGeometryMetadataNoop(binary));
            Run($"table-geometry-edit/post-edit-guards/{binary}",()=>EditableGeometryPostEditGuards(binary));
        }
        foreach(int scalar in Enumerable.Range(0,12))Run($"table-geometry-edit/nonfinite-value/{scalar}",()=>EditableGeometryNonfinite(scalar));
        Run("table-geometry-edit/value-snapshots",EditableGeometryValueSnapshots);
        Run("table-geometry-edit/exact-tag-limit",EditableGeometryTagLimit);
        Run("table-geometry-edit/metadata-tag-limit",EditableGeometryMetadataTagLimit);
    }

    private static DxfStoredTableCellGeometry EditableContent(double offset=0)=>new(new Vector3(-1.0-offset,2.0,3.0),new Vector3(4.0,5.0,6.0+offset),-7.0,8.0+offset,9.0,-10.0,int.MinValue);
    private static DxfStoredTableGeometryCell EditableCell(DxfObject? target=null)=>new(int.MinValue,-0.0,-20.0,target,new[]{EditableContent()});
    private static DxfStoredTableGeometryCell CopyGeometryCell(DxfStoredTableGeometryCell cell)=>new(cell.GeometryDataFlags,cell.WidthWithGap,cell.HeightWithGap,cell.GeometryReference,
        cell.Geometry.Select(v=>new DxfStoredTableCellGeometry(v.TopLeftDistance,v.CenterDistance,v.ContentWidth,v.ContentHeight,v.Width,v.Height,v.StoredValue95)));
    private static DxfStoredTableGeometryCell EditedNativeCell(DxfStoredTableGeometryCell cell)=>new(cell.GeometryDataFlags^128,cell.WidthWithGap+0.5,cell.HeightWithGap-0.25,cell.GeometryReference,
        cell.Geometry.Select(v=>new DxfStoredTableCellGeometry(v.TopLeftDistance+new Vector3(1,2,3),v.CenterDistance-new Vector3(3,2,1),v.ContentWidth+0.125,v.ContentHeight-0.125,v.Width+0.75,v.Height-0.75,v.StoredValue95^0x40000000)));

    private static void EditableGeometryNative(string file,bool input,bool binary,bool edit)
    {
        byte[] bytes=TableContentSourceBytes(file);if(input)bytes=TableContentRawBytes(TableContentRaw(bytes),true);
        var doc=TableContentLoad(bytes);var geometries=doc.Objects.Items.OfType<DxfStoredTableGeometry>().ToArray();
        var untouched=doc.Objects.Items.Where(o=>o.CodeName is "TABLECONTENT" or "TABLESTYLE" or "CELLSTYLEMAP").ToDictionary(o=>o.Handle,o=>o switch
        {DxfStoredTableContent content=>OwnershipTagValues(content.Payload).ToArray(),DxfStoredCellStyleMap map=>OwnershipTagValues(map.Payload).ToArray(),DxfTableStyle style=>OwnershipTagValues(style.Tags).ToArray(),_=>Array.Empty<(short Code,string Value)>()});
        foreach(var geometry in geometries)
        {
            var oldPayload=geometry.Payload;var oldCells=geometry.Cells;var oldReferences=geometry.References;
            var oldValues=OwnershipTagValues(oldPayload).ToArray();var owner=geometry.Owner;long seed=OwnershipSeed(doc);int count=doc.Objects.Items.Count;
            if(edit)geometry.ReplaceGeometry(geometry.RowCount+7,geometry.ColumnCount+11,geometry.Cells.Select(EditedNativeCell).Append(new DxfStoredTableGeometryCell(int.MinValue,-1,2,null,Array.Empty<DxfStoredTableCellGeometry>())));
            else geometry.ReplaceGeometry(geometry.RowCount,geometry.ColumnCount,geometry.Cells.Select(CopyGeometryCell));
            Check(OwnershipTagValues(oldPayload).SequenceEqual(oldValues),"previous native payload snapshot changed");
            Equal(0,oldReferences.Count,"previous native references changed");
            if(edit)Equal(oldCells.Count+1,geometry.Cells.Count,"native explicit appended cell");
            else Check(ReferenceEquals(oldPayload,geometry.Payload)&&ReferenceEquals(oldCells,geometry.Cells),"native no-op replaced original packet snapshots");
            Check(ReferenceEquals(owner,geometry.Owner),"native geometry ownership changed");Equal(seed,OwnershipSeed(doc),"native replacement allocated handles");Equal(count,doc.Objects.Items.Count,"native replacement changed registration");
        }
        var expected=geometries.ToDictionary(g=>g.Handle,g=>OwnershipTagValues(g.Payload).ToArray());
        var loaded=TableContentLoad(TableContentSave(doc,binary,$"editable-table-geometry-{(edit?"native":"noop")}-{file}-{binary}.dxf"));
        foreach(var geometry in loaded.Objects.Items.OfType<DxfStoredTableGeometry>())Check(expected[geometry.Handle].SequenceEqual(OwnershipTagValues(geometry.Payload)),"edited native packet changed on reload");
        foreach(var item in loaded.Objects.Items.Where(o=>untouched.ContainsKey(o.Handle)))
        {
            var packet=item switch{DxfStoredTableContent c=>c.Payload,DxfStoredCellStyleMap m=>m.Payload,DxfTableStyle s=>s.Tags,_=>Array.Empty<DxfTag>()};
            Check(untouched[item.Handle].SequenceEqual(OwnershipTagValues(packet)),"unrelated native stored payload changed");
        }
        Equal(0,loaded.Objects.Validate().Count,"native edited graph invalid");
    }

    private static void EditableGeometrySchema(DxfVersion version,bool binary)
    {
        var doc=TableGeometryLoad(TableGeometryRaw(version),binary);var geometry=TableGeometryObject(doc);
        var first=EditableCell();var second=new DxfStoredTableGeometryCell(int.MaxValue,-1.5,2.5,null,Array.Empty<DxfStoredTableCellGeometry>());
        geometry.ReplaceGeometry(777,888,new[]{first,second,first});Equal(3,geometry.Cells.Count,"explicit duplicate cells");
        var oldPayload=geometry.Payload;var oldCells=geometry.Cells;var before=OwnershipTagValues(oldPayload).ToArray();
        geometry.ReplaceGeometry(777,888,geometry.Cells.Select(CopyGeometryCell));Check(ReferenceEquals(oldPayload,geometry.Payload)&&ReferenceEquals(oldCells,geometry.Cells),"authored packet no-op changed snapshots");
        var loaded=TableGeometryObject(TableContentLoad(TableContentSave(doc,binary,$"editable-table-geometry-schema-{version}-{binary}.dxf")));
        Check(before.SequenceEqual(OwnershipTagValues(loaded.Payload)),"edited synthetic packet changed");Equal(777,loaded.RowCount,"independent row count");Equal(888,loaded.ColumnCount,"independent column count");
        loaded.ReplaceGeometry(0,0,Array.Empty<DxfStoredTableGeometryCell>());Equal(0,loaded.Cells.Count,"empty replacement");
    }

    private static DxfObject EditableTarget(DxfDocument doc,string kind)
    {
        if(kind=="STYLE")return doc.TextStyles.Add(new TextStyle("EDIT_TARGET","Arial.ttf"));
        if(kind=="LTYPE")return doc.Linetypes.Add(new Linetype("EDIT_TARGET"));
        if(kind=="APPID")return doc.ApplicationRegistries.Add(new ApplicationRegistry("EDIT_TARGET"));
        if(kind=="ENTITY"){var line=new Line(Vector3.Zero,Vector3.UnitX);doc.Entities.Add(line);return line;}
        if(kind=="ATTRIB")
        {
            var block=new Block("EDIT_TARGET");block.AttributeDefinitions.Add(new AttributeDefinition("KEY"));var insert=new Insert(block);doc.Entities.Add(insert);return insert.Attributes.Single();
        }
        var owner=new Block("EDIT_TARGET");var member=new Line(Vector3.Zero,Vector3.UnitX);owner.Entities.Add(member);doc.Blocks.Add(owner);
        if(kind=="BLOCK_RECORD")return owner.Record;
        if(kind=="ENDBLK")return (DxfObject)typeof(Block).GetProperty("End",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic)!.GetValue(owner)!;
        return member;
    }
    private static bool RemoveEditableTarget(DxfDocument doc,DxfObject target,string kind)=>kind switch
    {"STYLE"=>doc.TextStyles.Remove("EDIT_TARGET"),"LTYPE"=>doc.Linetypes.Remove("EDIT_TARGET"),"APPID"=>doc.ApplicationRegistries.Remove("EDIT_TARGET"),"ENTITY"=>doc.Entities.Remove((EntityObject)target),"ATTRIB"=>doc.Entities.Remove((Insert)target.Owner),_=>doc.Blocks.Remove("EDIT_TARGET")};
    private static void EditableGeometryReferences(string kind,bool binary)
    {
        var raw=TableGeometryRaw(DxfVersion.AutoCad2018,(d,t)=>{var s=d.TextStyles.Add(new TextStyle("EDIT_OLD","Arial.ttf"));t[7]=new DxfTag(330,s.Handle);});
        var doc=TableGeometryLoad(raw,binary);var geometry=TableGeometryObject(doc);var old=doc.TextStyles["EDIT_OLD"];var oldRefs=geometry.References;
        var target=EditableTarget(doc,kind);long seed=OwnershipSeed(doc);var newCell=EditableCell(target);
        geometry.ReplaceGeometry(1,1,new[]{newCell,newCell});Equal(seed,OwnershipSeed(doc),"explicit reference replacement allocated handles");
        Check(geometry.References.Count==2&&geometry.References.All(r=>ReferenceEquals(r,target)),"explicit replacement identity or repetition changed");
        Check(ReferenceEquals(oldRefs.Single(),old),"previous reference snapshot changed");Check(doc.TextStyles.Remove(old),"old dependency guard was not released");
        Check(!RemoveEditableTarget(doc,target,kind),"new geometry dependency removed");
        var loaded=TableGeometryObject(TableContentLoad(TableContentSave(doc,binary,$"editable-table-geometry-reference-{kind}-{binary}.dxf")));
        Check(loaded.References.Count==2&&loaded.References.All(r=>r.Handle==target.Handle),"edited reference did not reload as exact physical identity");
        geometry.ReplaceGeometry(0,0,Array.Empty<DxfStoredTableGeometryCell>());Check(RemoveEditableTarget(doc,target,kind),"released new dependency remained blocked");Equal(0,doc.Objects.Validate().Count,"reference replacement graph invalid");
    }

    private sealed class EditableGeometrySequence : IEnumerable<DxfStoredTableGeometryCell>
    {
        private readonly DxfStoredTableGeometryCell value;
        private readonly Action? during,dispose,acquire;
        private readonly bool fail;
        public EditableGeometrySequence(DxfStoredTableGeometryCell value,Action? during,Action? dispose,bool fail,Action? acquire)
        {this.value=value;this.during=during;this.dispose=dispose;this.fail=fail;this.acquire=acquire;}
        public IEnumerator<DxfStoredTableGeometryCell> GetEnumerator(){this.acquire?.Invoke();return new Cursor(this);}
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>this.GetEnumerator();
        private sealed class Cursor : IEnumerator<DxfStoredTableGeometryCell>
        {
            private readonly EditableGeometrySequence source;
            private int index=-1;
            public Cursor(EditableGeometrySequence source){this.source=source;}
            public DxfStoredTableGeometryCell Current=>this.index==0?this.source.value:throw new InvalidOperationException();
            object System.Collections.IEnumerator.Current=>this.Current;
            public bool MoveNext(){this.index++;if(this.index==0){this.source.during?.Invoke();return true;}if(this.source.fail)throw new InvalidOperationException("caller enumeration failure");return false;}
            public void Reset()=>throw new NotSupportedException();
            public void Dispose()=>this.source.dispose?.Invoke();
        }
    }
    private static IEnumerable<DxfStoredTableGeometryCell> EditableSequence(DxfStoredTableGeometryCell value,Action? during=null,Action? dispose=null,bool fail=false,Action? acquire=null)
        =>new EditableGeometrySequence(value,during,dispose,fail,acquire);
    private static void EditableGeometryAtomic(int fault,bool binary)
    {
        var doc=TableGeometryLoad(TableGeometryRaw(DxfVersion.AutoCad2018),binary);var geometry=TableGeometryObject(doc);var candidate=EditableCell();
        var payload=geometry.Payload;var values=OwnershipTagValues(payload).ToArray();var cells=geometry.Cells;var references=geometry.References;
        long seed=OwnershipSeed(doc);int count=doc.Objects.Items.Count;bool rejected=false;
        Action attempt=()=>geometry.ReplaceGeometry(2,3,new[]{candidate});
        if(fault==0)attempt=()=>geometry.ReplaceGeometry(2,3,null!);
        else if(fault==1)attempt=()=>geometry.ReplaceGeometry(-1,3,new[]{candidate});
        else if(fault==2)attempt=()=>geometry.ReplaceGeometry(2,-1,new[]{candidate});
        else if(fault==3)attempt=()=>geometry.ReplaceGeometry(int.MaxValue,3,new[]{candidate});
        else if(fault==4)attempt=()=>geometry.ReplaceGeometry(2,int.MaxValue,new[]{candidate});
        else if(fault==5)attempt=()=>geometry.ReplaceGeometry(2,3,new DxfStoredTableGeometryCell[]{null!});
        else if(fault==6)attempt=()=>geometry.ReplaceGeometry(2,3,EditableSequence(candidate,acquire:()=>throw new InvalidOperationException("caller acquisition failure")));
        else if(fault==7)attempt=()=>geometry.ReplaceGeometry(2,3,EditableSequence(candidate,fail:true));
        else if(fault==8)attempt=()=>geometry.ReplaceGeometry(2,3,EditableSequence(candidate,dispose:()=>throw new InvalidOperationException("caller disposal failure")));
        else if(fault is 9 or 10)
        {
            void Reenter(){try{geometry.ReplaceGeometry(5,6,new[]{candidate});}catch(InvalidOperationException){}}
            attempt=()=>geometry.ReplaceGeometry(2,3,EditableSequence(candidate,during:fault==9?Reenter:null,dispose:fault==10?Reenter:null));
        }
        else if(fault==11)attempt=()=>geometry.ReplaceGeometry(2,3,EditableSequence(candidate,dispose:()=>doc.DrawingVariables.AcadVer=DxfVersion.AutoCad2013));
        else if(fault==12)attempt=()=>geometry.ReplaceGeometry(2,3,EditableSequence(candidate,dispose:()=>((DxfDictionary)geometry.Owner).Remove("GEOMETRY")));
        else if(fault==13){var foreign=new DxfDocument(DxfVersion.AutoCad2018);attempt=()=>geometry.ReplaceGeometry(2,3,new[]{EditableCell(foreign.TextStyles["Standard"])});}
        else if(fault==14)attempt=()=>geometry.ReplaceGeometry(2,3,new[]{EditableCell(new TextStyle("DETACHED","Arial.ttf"))});
        else if(fault==15)
        {
            var target=EditableTarget(doc,"ATTRIB");seed=OwnershipSeed(doc);count=doc.Objects.Items.Count;
            attempt=()=>geometry.ReplaceGeometry(2,3,EditableSequence(EditableCell(target),dispose:()=>Check(RemoveEditableTarget(doc,target,"ATTRIB"),"caller failed to remove unreferenced owner")));
        }
        else if(fault==16)attempt=()=>geometry.ReplaceGeometry(2,3,Enumerable.Repeat(new DxfStoredTableGeometryCell(0,0,0,null,Array.Empty<DxfStoredTableCellGeometry>()),209715));
        else if(fault is 17 or 18)
        {
            var original=CopyGeometryCell(geometry.Cells.Single());
            attempt=()=>geometry.ReplaceGeometry(geometry.RowCount,geometry.ColumnCount,EditableSequence(original,dispose:()=>
            {if(fault==17)doc.DrawingVariables.AcadVer=DxfVersion.AutoCad2013;else ((DxfDictionary)geometry.Owner).Remove("GEOMETRY");}));
        }
        else attempt=()=>geometry.ReplaceGeometry(2,3,EditableSequence(candidate,acquire:()=>{try{geometry.ReplaceGeometry(4,5,new[]{candidate});}catch(InvalidOperationException){}}));
        try{attempt();}catch(ArgumentException){rejected=true;}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"invalid geometry replacement accepted");Check(ReferenceEquals(payload,geometry.Payload)&&ReferenceEquals(cells,geometry.Cells),"rejected replacement swapped prior snapshots");
        Check(values.SequenceEqual(OwnershipTagValues(geometry.Payload)),"rejected replacement changed payload");Equal(0,references.Count,"rejected replacement changed old references");Equal(seed,OwnershipSeed(doc),"rejected replacement allocated handles");
        if(fault!=15)Equal(count,doc.Objects.Items.Count,"rejected replacement changed registration");
        if(fault is 11 or 17)doc.DrawingVariables.AcadVer=DxfVersion.AutoCad2018;
        if(fault is 12 or 18)((DxfDictionary)geometry.Owner).Add("GEOMETRY",geometry);
        geometry.ReplaceGeometry(2,3,new[]{candidate});Equal(2,geometry.RowCount,"rejection left editing guard stuck");
    }

    private static void EditableGeometryMetadataNoop(bool binary)
    {
        var raw=TableGeometryRaw(DxfVersion.AutoCad2018,(_,tags)=>{tags[7]=new DxfTag(330,"0000");tags[5]=new DxfTag(40,-0.0);});var doc=TableGeometryLoad(raw,binary);var geometry=TableGeometryObject(doc);
        geometry.PersistentReactors.Add(doc.Objects.Root);var data=new XData(doc.ApplicationRegistries.Add(new ApplicationRegistry("EDIT_META")));data.XDataRecord.Add(new XDataRecord(XDataCode.String,"retained"));geometry.XData.Add(data);
        var payload=geometry.Payload;var cells=geometry.Cells;var xdata=geometry.XData["EDIT_META"];var reactors=geometry.PersistentReactors.ToArray();long seed=OwnershipSeed(doc);
        geometry.ReplaceGeometry(geometry.RowCount,geometry.ColumnCount,geometry.Cells.Select(CopyGeometryCell));
        Check(ReferenceEquals(payload,geometry.Payload)&&ReferenceEquals(cells,geometry.Cells),"no-op canonicalized original null handle spelling");
        Check(ReferenceEquals(xdata,geometry.XData["EDIT_META"])&&reactors.SequenceEqual(geometry.PersistentReactors),"replacement changed common metadata");Equal(seed,OwnershipSeed(doc),"metadata no-op allocated");
        var cell=geometry.Cells.Single();Check(BitConverter.DoubleToInt64Bits(cell.WidthWithGap)<0,"negative zero source sign lost");
        geometry.ReplaceGeometry(geometry.RowCount,geometry.ColumnCount,new[]{new DxfStoredTableGeometryCell(cell.GeometryDataFlags,0.0,cell.HeightWithGap,cell.GeometryReference,cell.Geometry)});
        Check(!ReferenceEquals(payload,geometry.Payload)&&BitConverter.DoubleToInt64Bits(geometry.Cells.Single().WidthWithGap)==0,"explicit positive zero was treated as bit-identical no-op");
    }
    private static void EditableGeometryPostEditGuards(bool binary)
    {
        var doc=TableGeometryLoad(TableGeometryRaw(DxfVersion.AutoCad2018),binary);var geometry=TableGeometryObject(doc);geometry.ReplaceGeometry(2,3,new[]{EditableCell()});
        long seed=OwnershipSeed(doc);int count=doc.Objects.Items.Count;
        Throws<NotSupportedException>(()=>doc.Objects.CloneObject(geometry,doc.Objects.Root,"COPY"));Throws<NotSupportedException>(()=>doc.Objects.EraseOwnedTree(geometry));
        doc.DrawingVariables.AcadVer=DxfVersion.AutoCad2013;using var output=new MemoryStream();CheckSaveRejected(doc,output);Equal(0L,output.Length,"edited geometry profile rejection output");Equal(seed,OwnershipSeed(doc),"edited geometry lifecycle allocated");Equal(count,doc.Objects.Items.Count,"edited geometry lifecycle changed registration");
    }
    private static void EditableGeometryNonfinite(int scalar)
    {
        double[] values=Enumerable.Repeat(1.0,12).ToArray();values[scalar]=scalar%2==0?double.NaN:double.PositiveInfinity;
        Throws<ArgumentOutOfRangeException>(()=>
        {
            var content=new DxfStoredTableCellGeometry(new Vector3(values[0],values[1],values[2]),new Vector3(values[3],values[4],values[5]),values[6],values[7],values[8],values[9],0);
            _=new DxfStoredTableGeometryCell(0,values[10],values[11],null,new[]{content});
        });
    }
    private static void EditableGeometryValueSnapshots()
    {
        var values=new List<DxfStoredTableCellGeometry>{EditableContent()};var cell=new DxfStoredTableGeometryCell(0,1,2,null,values);values.Clear();Equal(1,cell.Geometry.Count,"constructor retained caller list");
        Throws<NotSupportedException>(()=>((IList<DxfStoredTableCellGeometry>)cell.Geometry).Clear());
        Throws<ArgumentNullException>(()=>new DxfStoredTableGeometryCell(0,1,2,null,null!));
        Throws<ArgumentException>(()=>new DxfStoredTableGeometryCell(0,1,2,null,new DxfStoredTableCellGeometry[]{null!}));
    }
    private static void EditableGeometryTagLimit()
    {
        var doc=TableGeometryLoad(TableGeometryRaw(DxfVersion.AutoCad2018),true);var geometry=TableGeometryObject(doc);var cell=new DxfStoredTableGeometryCell(0,0,0,null,Array.Empty<DxfStoredTableCellGeometry>());
        geometry.ReplaceGeometry(0,0,Enumerable.Repeat(cell,209714));Equal(1048574,geometry.Payload.Count,"exact record payload budget excluding 5/330");
        var snapshot=geometry.Payload;Throws<ArgumentException>(()=>geometry.ReplaceGeometry(0,0,Enumerable.Repeat(cell,209715)));Check(ReferenceEquals(snapshot,geometry.Payload),"one-cell-over limit changed packet");
        var loaded=TableGeometryObject(TableContentLoad(TableContentSave(doc,true)));Equal(209714,loaded.Cells.Count,"exact maximum record failed codec reload");
    }
    private static void EditableGeometryMetadataTagLimit()
    {
        var doc=TableGeometryLoad(TableGeometryRaw(DxfVersion.AutoCad2018),false);var geometry=TableGeometryObject(doc);geometry.PersistentReactors.Add(doc.Objects.Root);
        var data=new XData(doc.ApplicationRegistries.Add(new ApplicationRegistry("LIMIT_META")));data.XDataRecord.Add(new XDataRecord(XDataCode.String,"one"));geometry.XData.Add(data);
        var cell=new DxfStoredTableGeometryCell(0,0,0,null,Array.Empty<DxfStoredTableCellGeometry>());geometry.ReplaceGeometry(0,0,Enumerable.Repeat(cell,209713));
        var snapshot=geometry.Payload;geometry.XData["LIMIT_META"].XDataRecord.Add(new XDataRecord(XDataCode.String,"two"));
        Throws<InvalidOperationException>(()=>geometry.ReplaceGeometry(0,0,geometry.Cells));Check(ReferenceEquals(snapshot,geometry.Payload),"metadata overflow no-op changed packet");
        using var output=new MemoryStream();CheckSaveRejected(doc,output);Equal(0L,output.Length,"metadata overflow wrote partial record");
    }
}
