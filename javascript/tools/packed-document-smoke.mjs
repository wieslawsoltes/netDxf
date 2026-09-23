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
