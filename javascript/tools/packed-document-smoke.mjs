// Executed only from the offline-installed package, with no checkout/oracle imports.
{
  const api=await import('@netdxf/javascript');
  const standalone=await import('@netdxf/javascript/netDxf/DxfDocument.js');
  if(standalone.DxfDocument!==api.DxfDocument)throw new Error('Document standalone export differs.');
  const doc=new api.DxfDocument(api.DxfVersion.AutoCad2018),line=new api.Line();
  doc.Entities.Add(line);
  if(doc.GetObjectByHandle(line.Handle)!==line||line.Layer!==doc.Layers.get_Item('0'))throw new Error('Packed typed registration failed.');
  const extension=new api.DxfDictionary();extension.Add('value',new api.DxfPlaceholder());doc.Objects.SetExtensionDictionary(line,extension);
  const second=new api.Line();doc.Entities.Add(second);const copy=doc.Objects.CloneExtensionDictionary(line,second);
  if(copy===extension||copy.get_Item('value')===extension.get_Item('value'))throw new Error('Packed ownership clone aliases source.');
  doc.Objects.EraseOwnedTree(copy);if(second.ExtensionDictionary!==null)throw new Error('Packed erasure left reciprocal attachment.');
  doc.Layers.StateManager.AddNew('Snapshot');doc.Layers.StateManager.Restore('Snapshot');
  if(doc.Objects.Validate().Count!==0)throw new Error('Packed graph validation failed.');
  const port=doc.Viewport,sun=new api.DxfSun();doc.Objects.SetSun(port,sun);doc.Objects.EraseOwnedTree(sun);
  if(port.Sun!==null||!sun.IsErased)throw new Error('Packed SUN lifecycle failed.');
}

// Registered annotation APIs must work in the installed browser-safe package.
{
  const a=await import('@netdxf/javascript');
  const doc=new a.DxfDocument(a.DxfVersion.AutoCad2018),section=new a.Section();
  doc.Entities.Add(section);doc.Objects.SetSectionSettings(section,new a.DxfSectionSettings());
  const copy=doc.Objects.CloneSection(section,section.Owner);
  if(copy===section||copy.GeometrySettings===section.GeometrySettings||copy.GeometrySettings.Owner!==copy)throw new Error('Packed SECTION clone aliases source.');
  doc.Objects.EraseSection(copy);if(!copy.IsErased||doc.GetObjectByHandle(copy.Handle)!==null)throw new Error('Packed SECTION erasure failed.');
  const style=new a.DxfMLeaderStyle();style.Properties.TextStyle=doc.TextStyles.get_Item('Standard');doc.Objects.AddMLeaderStyle('Pack',style);
  const leader=new a.MultiLeader();leader.Properties.Style=style;leader.Properties.TextStyle=style.Properties.TextStyle;
  leader.Properties.LeaderLinetype=doc.Linetypes.get_Item('Continuous');leader.Properties.ContentType=0;doc.Entities.Add(leader);leader.Validate();
  if([...doc.Entities.MultiLeaders].length!==1)throw new Error('Packed MULTILEADER registration failed.');
  doc.Entities.Remove(leader);doc.Objects.EraseOwnedTree(style);if(!style.IsErased)throw new Error('Packed style release failed.');
}

// Exercise retained registration through the existing internal constructor adapters.
// This does not claim a typed DXF parser is available in the installed package.
{
  const {DxfDocument,DxfVersion,Polyline3D,Polyline3DRecord,Vector3,DxfTag}=await import('@netdxf/javascript');
  const document=new DxfDocument(DxfVersion.AutoCad2018),parent=new Polyline3D([Vector3.Zero,Vector3.UnitX,Vector3.UnitY]);
  const make=(index,end=false)=>{
    const record=new Polyline3DRecord(end?'SEQEND':'VERTEX',[new DxfTag(5,(0xc0+index).toString(16).toUpperCase()),new DxfTag(330,'A0')]);
    record.SourceVersion=DxfVersion.AutoCad2018;record.IdentityIndex=0;record.OwnerIndex=1;
    return record;
  };
  parent.SetStoredRecords(null,[make(0),make(1),make(2)],make(3,true));document.Entities.Add(parent);
  const original=parent.VertexRecords.get_Item(0),end=parent.EndSequenceRecord;
  if(document.GetObjectByHandle(original.Handle)!==original)throw new Error('Installed retained registration failed.');
  parent.InsertVertex(1,new Vector3(5,6,7));parent.MoveVertex(0,3);parent.RemoveVertexAt(3);
  if(!original.IsRemoved||document.GetObjectByHandle(original.Handle)!==null||parent.EndSequenceRecord!==end)throw new Error('Installed retained topology lifecycle failed.');
}

// The source-profile manager has a real lifecycle, not a source-module-only API.
{
  const {DxfDocument,Section,DxfStoredSectionManager}=await import('@netdxf/javascript');
  const standalone=await import('@netdxf/javascript/netDxf/Objects/DxfStoredSectionManager.js');
  if(standalone.DxfStoredSectionManager!==DxfStoredSectionManager)throw new Error('Manager export mismatch.');
  const doc=new DxfDocument(18),section=new Section();doc.Entities.Add(section);
  const manager=doc.Objects.CreateSectionManager([section,section],false),old=manager.Sections;
  if(doc.Entities.Remove(section)||manager.Tags.Count!==5)throw new Error('Manager membership is not guarded.');
  manager.ReplaceSections([],true);doc.Objects.EraseSectionManager(manager);
  if(!manager.IsErased||old.Count!==2||!doc.Entities.Remove(section))throw new Error('Manager lifecycle/snapshot failure.');
}

// TABLESTYLE/CELLSTYLEMAP APIs use only the installed package, not test fixture helpers.
{
  const a=await import('@netdxf/javascript'),standalone=await import('@netdxf/javascript/netDxf/Objects/DxfTableStyle.js');
  if(standalone.DxfTableStyle!==a.DxfTableStyle)throw new Error('Packed TABLESTYLE standalone identity differs.');
  const d=new a.DxfDocument(a.DxfVersion.AutoCad2018),packet=[[100,'AcDbTableStyle'],[3,'Style'],[70,0],[71,0],[40,0],[41,0],[280,0],[281,0]];
  for(let row=0;row<3;row++)packet.push([7,'Standard'],[140,row+1],[170,5],[62,0],[63,7],[283,1],[90,4],[91,2]);
  const style=new a.DxfTableStyle(d,packet.map(([c,v])=>new a.DxfTag(c,v)),s=>s);d.NamedObjects.Add('TABLE_STYLE',style);style.Resolve(h=>d.GetObjectByHandle(h),s=>s);
  const old=style.Rows;style.ReplaceStyle(null,[old.get_Item(1).WithValues(new a.DxfTableStyleRowValues(6.25,5,2,7,false))]);
  if(style.Rows.get_Item(1).Values.TextHeight!==6.25||old.get_Item(1).Values.TextHeight!==2||d.Objects.Validate().Count!==0)throw new Error('Packed TABLESTYLE edit/snapshot failed.');
  const tags=[[100,'AcDbCellStyleMap'],[90,1],[300,'CELLSTYLE'],[1,'TABLEFORMAT_BEGIN'],[309,'TABLEFORMAT_END'],[1,'CELLSTYLE_BEGIN'],[90,0],[91,-1],[300,'Before'],[309,'CELLSTYLE_END']].map(([c,v])=>new a.DxfTag(c,v));
  const map=new a.DxfStoredCellStyleMap(d,tags,s=>s);d.NamedObjects.Add('CELL_MAP',map);map.Resolve(h=>d.GetObjectByHandle(h));
  const entries=map.Entries;map.ReplaceEntryNames(['After']);if(map.Entries.get_Item(0).Name!=='After'||entries.get_Item(0).Name!=='Before'||d.Objects.Validate().Count!==0)throw new Error('Packed CELLSTYLEMAP snapshot failed.');
}

// TABLEGEOMETRY values and loaded-packet edits use only the installed package.
{
  const api = await import('@netdxf/javascript');
  const module = await import('@netdxf/javascript/netDxf/Objects/DxfStoredTableGeometry.js');
  if (module.DxfStoredTableGeometry !== api.DxfStoredTableGeometry) throw new Error('TABLEGEOMETRY standalone export differs.');
  const doc = new api.DxfDocument(api.DxfVersion.AutoCad2018);
  const packet = [[100,'AcDbTableGeometry'],[90,0],[91,0],[92,0]].map(([code,value])=>new api.DxfTag(code,value));
  const geometry = new api.DxfStoredTableGeometry(doc,packet);
  doc.NamedObjects.Add('geometry',geometry);geometry.Resolve(handle=>doc.StoredTableHandleTarget(handle));
  const value = new api.DxfStoredTableCellGeometry(new api.Vector3(-0,1,2),api.Vector3.Zero,-1,2,3,4,5);
  const cell = new api.DxfStoredTableGeometryCell(0,-0,1,doc.TextStyles.get_Item('Standard'),[value]);
  geometry.ReplaceGeometry(7,8,[cell,cell]);const prior=geometry.Payload;
  geometry.ReplaceGeometry(7,8,[cell,cell]);
  if(geometry.Payload!==prior||geometry.References.Count!==2||!Object.is(geometry.Cells.get_Item(0).WidthWithGap,-0)||doc.Objects.Validate().Count!==0)throw new Error('Packed TABLEGEOMETRY edit differs.');
}

// Original-path retained TABLECONTENT and ACAD_TABLE APIs from the installed
// package. These explicit in-memory packets are not a typed DXF file reader.
{
  const {DxfDocument,DxfVersion,DxfTag,DxfStoredTableContent,StoredTable}=await import('@netdxf/javascript');
  const standalone=await import('@netdxf/javascript/netDxf/Objects/DxfStoredTableContent.js');
  const entity=await import('@netdxf/javascript/netDxf/Entities/StoredTable.js');
  if(standalone.DxfStoredTableContent!==DxfStoredTableContent||entity.StoredTable!==StoredTable)throw new Error('Retained table standalone exports differ.');
  const doc=new DxfDocument(DxfVersion.AutoCad2018),tag=([c,v])=>new DxfTag(c,v);
  const payload=[[100,'AcDbLinkedData'],[1,'Content'],[300,'Description'],[100,'AcDbLinkedTableData'],[90,0],[91,1],[301,'ROW'],
    [1,'LINKEDTABLEDATAROW_BEGIN'],[90,1],[300,'CELL'],[1,'LINKEDTABLEDATACELL_BEGIN'],[95,1],[302,'CONTENT'],[1,'CELLCONTENT_BEGIN'],[90,1],
    [300,'VALUE'],[93,6],[90,4],[1,'text'],[94,0],[300,''],[302,'text'],[304,'ACVALUE_END'],[91,0],[309,'CELLCONTENT_END'],
    [309,'LINKEDTABLEDATACELL_END'],[309,'LINKEDTABLEDATAROW_END'],[92,0],[100,'AcDbFormattedTableData'],[100,'AcDbTableContent'],[340,'0']].map(tag);
  const content=new DxfStoredTableContent(doc,payload);doc.NamedObjects.Add('Content',content);content.Resolve(h=>doc.StoredTableHandleTarget(h));
  const old=content.StoredValues,value=old.get_Item(0);content.ReplaceContent('Edited','Description',null,[value.WithValue('literal\\U+0041','display')]);
  if(content.StoredValues.get_Item(0).Value!=='literal\\U+0041'||old.get_Item(0)!==value||value.Value!=='text')throw new Error('Retained content snapshot/edit mismatch.');
  const table=new StoredTable(doc,[[100,'AcDbBlockReference'],[10,0],[20,0],[30,0],[210,0],[220,0],[230,2],
    [100,'AcDbTable'],[90,22],[91,1],[92,1],[141,3],[142,4],[171,1],[301,'CELL_VALUE'],[93,6],[90,4],[1,'text'],[304,'ACVALUE_END']].map(tag));
  doc.Entities.Add(table);table.Resolve();
  if(table.Grid.get_Item(0,0).LiteralValue!=='text'||table.Normal.Z!==2||table.BackingContent!==null)throw new Error('Retained table grid mismatch.');
  if(!doc.Entities.Remove(table))throw new Error('Unreferenced retained table did not detach.');
  if(doc.Objects.Validate().Count)throw new Error('Retained table registration invalidated the database.');
}
