// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
// Typed whole-document import. Physical source identities are kept separate from generated defaults.
import * as api from '../../index.js';
import { DotNetMath } from '../../runtime/GeometryRuntime.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import * as io from '../../runtime/DxfTransport.js';
import { ReadTypedDocumentInput } from './DxfRawDocument.js';
import { DxfTag } from './DxfTag.js';
import { ReadXDataRecord } from '../../runtime/DxfXDataIO.js';
import { HatchSplineData } from '../Entities/HatchSplineData.js';
import { HatchGradientPatternTypeStringValues } from '../Entities/HatchGradientPatternType.js';
import { ReadVertex } from '../../runtime/FittedPolylineIO.js';
import { DxfVersionNotSupportedException } from './DxfVersionNotSupportedException.js';
import { DatabaseIOContext } from '../../runtime/DatabaseIOContext.js';
import { ReadDatabaseRecord, ReadDictionaryDatabaseRecord, ImportDatabaseObjects } from './DxfReader.Objects.js';
import { DocumentTagReader, envelope, canonicalHandle, tableKinds, managedDictionaries,
  value, lastValue, point, subclass, publicPayload, readXData, dimensionStyleFields,dimensionStyleBooleans,
  setProperty, getProperty, applySuppression,dimensionOverrideFields,zeroSuppressionGroups,dstyleSections } from '../../runtime/TypedDocumentIO.js';
import { BoxedScalar } from '../../runtime/BoxedScalar.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { DecodeDxfText } from '../../runtime/DxfStringEncoding.js';
import { HeaderDateTime, HeaderTimeSpan } from '../../runtime/HeaderTime.js';
import { UnwrapHeaderNumber } from '../../runtime/HeaderBox.js';
import { PathFileNameWithoutExtension } from '../../runtime/SupportFileSystem.js';
import { ArgumentException,ArgumentNullException,FormatException,InvalidDataException,InvalidCastException,NotSupportedException } from '../../runtime/Errors.js';
const primitiveReaders=Object.freeze({ARC:'ReadArc',CIRCLE:'ReadCircle',ELLIPSE:'ReadEllipse',LINE:'ReadLine',POINT:'ReadPoint',RAY:'ReadRay',XLINE:'ReadXLine','3DFACE':'ReadFace3D',SOLID:'ReadSolid',TRACE:'ReadTrace',SPLINE:'ReadSpline',LWPOLYLINE:'ReadLwPolyline',HELIX:'ReadHelix',LIGHT:'ReadLight',OLEFRAME:'ReadOleFrame',OLE2FRAME:'ReadOle2Frame'});
const legacyObjects=new Set(['LAYOUT','GROUP','MLINESTYLE','IMAGEDEF','IMAGEDEF_REACTOR','RASTERVARIABLES','DGNDEFINITION','DWFDEFINITION','PDFDEFINITION']);
const decoded=(tags,code,fallback='')=>DecodeDxfText(value(tags,code,fallback));
const entries=tags=>{const rows=[];let name=null;for(const t of subclass(tags,'AcDbDictionary')){if(t.Code===3)name=DecodeDxfText(t.Value);else if((t.Code===350||t.Code===360)&&name!==null){rows.push([name,canonicalHandle(t.Value),t.Code===360]);name=null;}}return rows;};
const resourceNames=Object.freeze({LAYER:'Layers',LTYPE:'Linetypes',STYLE:'TextStyles',DIMSTYLE:'DimensionStyles'});

export class DxfReader {
  doc=null;chunk=null;Context=null;RawDocument=null;
  constructor(){this.records=[];this.metadata=new Map();this.identities=new Map();this.blockByRecordHandle=new Map();this.deferred=[];this.storedTables=new ReferenceList();}
  Read(stream,supportFolders=new api.SupportFolders()) {
    if(stream==null)throw new ArgumentNullException('stream');
    // Preserve the byte-buffer convenience overload without weakening stream probing.
    if(stream instanceof Uint8Array)stream=new api.MemoryStream(stream,false);
    // The pinned typed reader probes the declared version before raw decoding. In
    // particular, an AC1009 header in modern binary framing must report the version
    // exception, not disappear as a Release null after the raw dialect check fails.
    const binary={value:false},version=api.DxfDocument.CheckDxfFileVersion(stream,binary);
    this.isBinary=binary.value;
    if(version<api.DxfVersion.AutoCad2000)
      throw new DxfVersionNotSupportedException('DXF file version not supported: '+version,version);
    const raw=ReadTypedDocumentInput(stream);this.RawDocument=raw.RawDocument;
    if(raw.Version<api.DxfVersion.AutoCad2000)throw new DxfVersionNotSupportedException('DXF file version not supported: '+raw.Version,raw.Version);
    const variables=new api.HeaderVariables();variables.AcadVer=raw.Version;
    if(!(supportFolders instanceof api.SupportFolders))supportFolders=new api.SupportFolders(supportFolders);
    const doc=this.doc=new api.DxfDocument(variables,false,supportFolders),context=this.Context=new DatabaseIOContext(doc);
    this.tags=Array.from(raw.Tags);this.records=[];this.metadata.clear();this.identities.clear();this.blockByRecordHandle.clear();this.deferred=[];this.storedTables=new ReferenceList();this.consumed=new Set();
    let seed=1n;const declarations=new Map();
    for(const section of raw.Sections)for(const source of section.Records){
      const tags=Array.from(source.Tags),record={Name:source.Name,Section:section.Name,Start:source.StartTagIndex,End:source.EndTagIndex,Tags:tags};
      record.Envelope=envelope(tags,record.Name);this.records.push(record);this.identities.set(record.Start,record.Envelope.Source);
      const identity=record.Envelope.Source;if(identity.IdentitySeen&&identity.Handle!==0n){context.sourceObjectIdentities.add(identity.Handle);if(declarations.has(identity.Handle)){identity.Ambiguous=true;declarations.get(identity.Handle).Ambiguous=true;}else declarations.set(identity.Handle,identity);if(identity.Handle>=seed&&identity.Handle<0x7fffffffffffffffn)seed=identity.Handle+1n;}
    }
    this.ReadHeader();const declared=canonicalHandle(variables.HandleSeed);if(declared&&BigInt('0x'+declared)>seed)seed=BigInt('0x'+declared);doc.NumHandles=seed;
    doc.Comments.Clear();for(const t of this.tags){if(t.Code===0&&t.Value==='SECTION')break;if(t.Code===999)doc.Comments.Add(t.Value);}
    this.InitializeCollections();
    const classes=Array.from(raw.Sections).find(s=>s.Name==='CLASSES');if(classes){this.chunk=context.Chunk=new DocumentTagReader(this.tags,classes.StartTagIndex+1,this.identities);io.ReadClassDefinitions(context);}
    this.ReadTables();this.ReadBlocks();this.ReadManagedObjects();this.ReadLayerStates();this.EnsureDefaultObjects();
    this.ReadEntities();this.ReadObjects();
    // Diagnose an incomplete typed packet before a subsequent section error,
    // but never return a document whose framing failed validation.
    if(raw.DeferredError!==null)throw raw.DeferredError;
    for(const resolve of this.deferred)resolve();
    // Database import runs its own source-qualified resolver phases before entity references.
    ImportDatabaseObjects(context);io.ResolveUcsReferences(context);io.ResolveViewSections(context);
    io.ResolveMultiLeaderReferences(context);io.ResolveSections(context);io.ResolveStoredTables(this.storedTables);
    io.ResolveStoredPolylineRecords(context);io.ResolveStoredPolygonMeshRecords(context);io.ResolveStoredPolyfaceMeshRecords(context);io.ResolveStoredPolyline2DRecords(context);
    io.ResolveMTextColumnLinks(doc);io.ResolveOpaqueEntities(context);
    context.ValidateSourceIdentityDeclarations();
    const thumbnail=Array.from(raw.Sections).find(s=>s.Name==='THUMBNAILIMAGE');if(thumbnail){const cursor=new DocumentTagReader(this.tags,thumbnail.StartTagIndex+1,this.identities);doc.ThumbnailImage=io.DxfThumbnailImage.Read(cursor);}
    doc.Entities.ActiveLayout='Model';return doc;
  }
  Records(section,name=null){return this.records.filter(r=>r.Section===section&&(name===null||r.Name===name));}
  Cursor(record,marker=null){let start=record.Start+record.Envelope.Body;if(marker!==null){const i=record.Tags.findIndex(t=>t.Code===100&&t.Value===marker);if(i<0)throw new InvalidDataException(record.Name+' requires '+marker+'.');start=record.Start+i;}const c=new DocumentTagReader(this.tags,start,this.identities);c.SourceRecord=record.Envelope.Source;this.chunk=this.Context.Chunk=c;return c;}
  Track(item,record,withXData=true){const env=record.Envelope;if(env.Handle)item.Handle=env.Handle;if(withXData)readXData(item,record.Tags,this.doc);this.Context.RecordSourceObject(item,env.Source);if(item.Handle)this.Context.entityDatabaseMetadata.set(item.Handle,{Owner:env.Owner,Extension:env.Extension,Reactors:env.Reactors});this.metadata.set(item,record);return item;}
  ReadHeader(){const doc=this.doc,variables=doc.DrawingVariables,properties=new Map(Object.getOwnPropertyNames(api.HeaderVariableCode).filter(key=>typeof api.HeaderVariableCode[key]==='string'&&api.HeaderVariableCode[key].startsWith('$')).map(key=>[api.HeaderVariableCode[key].toUpperCase(),key]));
    const known=new Map(Array.from(variables.KnownValues(),v=>[v.Name.toUpperCase(),v]));
    let origin=api.Vector3.Zero,xAxis=api.Vector3.UnitX,yAxis=api.Vector3.UnitY;
    // Keep raw tags untouched: group 999 is invisible only to semantic HEADER parsing.
    for(const record of this.Records('HEADER')){const tags=record.Tags.slice(1).filter(tag=>tag.Code!==999),name=record.Name.toUpperCase();if(!tags.length)continue;
      if(name==='$UCSORG'){origin=point(tags,10);continue;}if(name==='$UCSXDIR'){xAxis=point(tags,10);continue;}if(name==='$UCSYDIR'){yAxis=point(tags,10);continue;}
      if(name==='$ACADVER')continue;
      const previous=known.get(name),property=properties.get(name);let v=tags[0].Value;
      if(previous&&property){const current=UnwrapHeaderNumber(previous.Value);
        if(current instanceof api.Vector3)v=point(tags,tags[0].Code);
        else if(current instanceof api.AciColor)v=api.AciColor.FromCadIndex(v);
        else if(current instanceof HeaderDateTime)v=api.DrawingTime.FromJulianCalendar(v);
        else if(current instanceof HeaderTimeSpan)v=api.DrawingTime.EditingTime(v);
        else if(typeof current==='boolean')v=typeof v==='boolean'?v:v!==0;
        else if(typeof v==='string'&&name!=='$HANDSEED')v=DecodeDxfText(v);
        variables[property]=v;
      }else {
        if(name.startsWith('$DIM')&&!['$DIMTSZ','$DIMTVP','$DIMUPT'].includes(name)||['$ACADMAINTVER','$INTERFEREOBJVS','$INTERFEREVPVS'].includes(name))continue;
        let group=tags[0].Code;
        if(group===10){
          // The original custom-header representation records dimension (20/30),
          // while the physical coordinate packet always starts with group 10.
          const x=tags[0].Value,y=tags[1]?.Value;
          if(typeof x!=='number'||typeof y!=='number')throw new InvalidCastException('Custom header coordinates require doubles.');
          if(tags[2]?.Code===30){v=new api.Vector3(x,y,tags[2].Value);group=30;}
          else{v=new api.Vector2(x,y);group=20;}
        }else if(typeof v==='string')v=DecodeDxfText(v);
        else if([2,3,4].includes(tags[0].ValueType))v=new BoxedScalar({2:'Int16',3:'Int32',4:'Int64'}[tags[0].ValueType],v);
        if(variables.ContainsCustomVariable(record.Name))variables.RemoveCustomVariable(record.Name);
        variables.AddCustomVariable(new api.HeaderVariable(record.Name,group,v));
      }
    }
    if(!api.Vector3.ArePerpendicular(xAxis,yAxis)){xAxis=api.Vector3.UnitX;yAxis=api.Vector3.UnitY;}variables.CurrentUCS=new api.UCS('Unnamed',origin,xAxis,yAxis);
  }
  InitializeCollections(){const doc=this.doc,tableHeaders=new Map(this.Records('TABLES','TABLE').map(r=>[value(r.Tags,2),r]));
    for(const [code,property,Type]of tableKinds){const record=tableHeaders.get(code),handle=record?.Envelope.Handle??null;doc[property]=code==='VPORT'?new api.VPorts(doc,handle,false):new api[Type](doc,handle);if(record)this.Track(doc[property],record,false);}
    doc.ShapeStyles=new api.ShapeStyles(doc);
    const root=this.Records('OBJECTS','DICTIONARY').find(r=>r.Envelope.Owner===null||r.Envelope.Owner==='0'),rootEntries=root?entries(root.Tags):[];this.rootRecord=root??null;this.dictionaryNames=new Map();
    for(const r of this.Records('OBJECTS','DICTIONARY'))for(const[name,handle]of entries(r.Tags))if(!this.dictionaryNames.has(handle))this.dictionaryNames.set(handle,name);
    for(const[name,property,Type]of managedDictionaries)doc[property]=new api[Type](doc,rootEntries.find(([n])=>n===name)?.[1]??null);
    const layerHeader=tableHeaders.get('LAYER');if(layerHeader?.Envelope.Extension){const manager=doc.Layers.StateManager;doc.AddedObjects.Remove(manager.Handle);manager.Handle=layerHeader.Envelope.Extension;doc.AddedObjects.Add(manager.Handle,manager);this.Context.layerStateManagerDictionaryHandle=manager.Handle;}
    for(const record of tableHeaders.values())readXData(doc[tableKinds.find(([code])=>code===value(record.Tags,2))?.[1]]??doc,record.Tags,doc);
  }
  ReadTables(){const doc=this.doc;for(const r of this.Records('TABLES','APPID')){const item=this.Track(new api.ApplicationRegistry(decoded(r.Tags,2),false),r);doc.ApplicationRegistries.Add(item,false);}
    for(const r of this.Records('TABLES','STYLE'))this.ReadTextStyle(r);
    for(const r of this.Records('TABLES','LTYPE'))this.ReadLinetype(r);
    for(const item of [api.Linetype.ByLayer,api.Linetype.ByBlock,api.Linetype.Continuous])if(!doc.Linetypes.Contains(item.Name))doc.Linetypes.Add(item);
    for(const r of this.Records('TABLES','LAYER'))this.ReadLayer(r);
    if(!doc.Layers.Contains('0'))doc.Layers.Add(api.Layer.Default);
    if(!doc.TextStyles.Contains(api.TextStyle.DefaultName))doc.TextStyles.Add(api.TextStyle.Default);
    if(!doc.ApplicationRegistries.Contains(api.ApplicationRegistry.DefaultName))doc.ApplicationRegistries.Add(api.ApplicationRegistry.Default);
    for(const r of this.Records('TABLES','UCS'))this.ReadUCS(r);
    for(const r of this.Records('TABLES','VPORT')){const item=io.ReadVPort(this.Cursor(r,'AcDbViewportTableRecord'),this.Context);this.Track(item,r,false);this.AttachTableXData(item);doc.VPorts.AddRecord(item,false);}
    doc.VPorts.EnsureActive();
    for(const r of this.Records('TABLES','VIEW')){const item=io.ReadView(this.Cursor(r,'AcDbViewTableRecord'),this.Context);this.Track(item,r,false);this.AttachTableXData(item);doc.Views.Add(item,false);}
  }
  AttachTableXData(item){const out={};if(this.Context.tableEntryXData.TryGetValue(item,out))item.XData.AddRange(out.value);}
  ReadTextStyle(r){const tags=subclass(r.Tags,'AcDbTextStyleTableRecord'),flags=value(tags,70,0),font=decoded(tags,3),name=decoded(tags,2);let item,table;
    if(flags&1){item=new api.ShapeStyle(font);table=this.doc.ShapeStyles;item.Size=value(tags,40,0);}else{item=new api.TextStyle(name||api.TextStyle.DefaultName,api.TextStyle.DefaultFont,false);item.SetStoredFontFiles(font,decoded(tags,4));table=this.doc.TextStyles;item.Height=Math.max(0,value(tags,40,0));}
    item.Flags=flags;item.TextGenerationFlags=value(tags,71,0);item.WidthFactor=value(tags,41,1);item.LastHeight=value(tags,42,null);item.ObliqueAngle=value(tags,50,0);this.Track(item,r);table.Add(item,false);return item;}
  ReadLinetype(r){const tags=subclass(r.Tags,'AcDbLinetypeTableRecord'),item=new api.Linetype(decoded(tags,2),'',false);item.Description=decoded(tags,3);let i=0;
    while(i<tags.length){if(tags[i].Code!==49){i++;continue;}let end=i+1;while(end<tags.length&&tags[end].Code!==49)end++;const t=tags.slice(i,end),length=value(t,49,0),flags=value(t,74,0);let segment;
      if(flags&2){const style=this.doc.GetObjectByHandle(value(t,340,''));segment=new api.LinetypeTextSegment(decoded(t,9),style instanceof api.TextStyle?style:api.TextStyle.Default,length);}
      else if(flags&4){const style=this.doc.GetObjectByHandle(value(t,340,''));if(!(style instanceof api.ShapeStyle))throw new InvalidDataException('Complex linetype requires its source shape style.');const name=style.ShapeName(value(t,75,0));if(!name){i=end;continue;}segment=new api.LinetypeShapeSegment(name,style,length);}
      else segment=new api.LinetypeSimpleSegment(length);
      if(flags&6){segment.Scale=value(t,46,1);segment.Rotation=value(t,50,0);segment.Offset=point(t,44,2);segment.RotationType=flags&1?api.LinetypeSegmentRotationType.Absolute:api.LinetypeSegmentRotationType.Relative;}item.Segments.Add(segment);i=end;
    }this.Track(item,r);this.doc.Linetypes.Add(item,false);return item;}
  ReadLayer(r){const t=subclass(r.Tags,'AcDbLayerTableRecord'),item=new api.Layer(decoded(t,2),false);const flags=value(t,70,0),color=value(t,62,7);item.IsFrozen=!!(flags&1);item.IsLocked=!!(flags&4);item.IsVisible=color>=0;item.Color=value(t,420)!==null?api.AciColor.FromTrueColor(value(t,420)):api.AciColor.FromCadIndex(Math.abs(color));item.Linetype=this.Resource('Linetypes',decoded(t,6,'Continuous'));item.Plot=value(t,290,true);item.Lineweight=value(t,370,api.Lineweight.Default);this.Track(item,r);
    for(const data of item.XData.Values){if(data.ApplicationRegistry.Name.toUpperCase()==='ACCMTRANSPARENCY'){const n=Array.from(data.XDataRecord).find(t=>t.Code===1071);if(n)item.Transparency=api.Transparency.FromAlphaValue(n.Value);}if(data.ApplicationRegistry.Name.toUpperCase()==='ACAeCLAYERSTANDARD'.toUpperCase()){const strings=Array.from(data.XDataRecord).filter(t=>t.Code===1000);if(strings.length>1)item.Description=strings[1].Value;}}
    this.doc.Layers.Add(item,false);return item;}
  ReadUCS(r){const t=subclass(r.Tags,'AcDbUCSTableRecord'),item=new api.UCS(decoded(t,2),point(t,10),point(t,11,3,api.Vector3.UnitX),point(t,12,3,api.Vector3.UnitY));item.Flags=value(t,70,0);item.Elevation=value(t,146,0);const type=value(t,79,0),base=value(t,346,null);io.CompleteUcsBase(this.Context,item,type,base);
    for(let i=0;i<t.length;i++)if(t[i].Code===71){let end=i+1;while(end<t.length&&t[end].Code!==71)end++;item.SetOrthographicOrigin(t[i].Value,point(t.slice(i+1,end),13));}this.Track(item,r);this.doc.UCSs.Add(item,false);return item;}
  Resource(property,name){if(name===null||name==='')name=property==='Layers'?'0':property==='Linetypes'?'ByLayer':'Standard';const table=this.doc[property],existing=table.get_Item(name);if(existing)return existing;let item;
    if(property==='Layers')item=new api.Layer(name,false);else if(property==='Linetypes')item=new api.Linetype(name,'',false);else if(property==='TextStyles')item=new api.TextStyle(name,api.TextStyle.DefaultFont,false);else if(property==='DimensionStyles')item=new api.DimensionStyle(name,false);else throw new InvalidDataException('Missing '+property+' resource: '+name);return table.Add(item);}
  ReadBlocks(){const doc=this.doc,headers=this.Records('BLOCKS','BLOCK'),ends=this.Records('BLOCKS','ENDBLK');
    const records=this.Records('TABLES','BLOCK_RECORD');for(const header of headers)if(!records.some(r=>decoded(r.Tags,2)===decoded(header.Tags,2)))records.push({Name:'BLOCK_RECORD',Tags:[new DxfTag(0,'BLOCK_RECORD'),new DxfTag(2,decoded(header.Tags,2))],Envelope:{Handle:header.Envelope.Owner,Source:null,Extension:null,Reactors:[],Owner:doc.Blocks.Handle}});
    for(const r of records){const name=decoded(r.Tags,2),h=headers.find(h=>decoded(h.Tags,2)===name),external=h&&!!(value(subclass(h.Tags,'AcDbBlockBegin'),70,0)&4),block=external?new api.Block(name,decoded(subclass(h.Tags,'AcDbBlockBegin'),1),!!(value(subclass(h.Tags,'AcDbBlockBegin'),70,0)&8)):api.Block.CreateOverload('string,System.Collections.Generic.IEnumerable<netDxf.Entities.EntityObject>,System.Collections.Generic.IEnumerable<netDxf.Entities.AttributeDefinition>,bool',name,null,null,false);
      block.Record.Handle=r.Envelope.Handle;block.Record.Units=value(r.Tags,70,0);block.Record.AllowExploding=value(r.Tags,280,1)!==0;block.Record.ScaleUniformly=value(r.Tags,281,0)!==0;
      if(h){const t=subclass(h.Tags,'AcDbBlockBegin');block.Flags=value(t,70,0);block.Origin=point(t,10);block.Description=decoded(t,4);block.Layer=this.Resource('Layers',decoded(h.Envelope.Common,8,'0'));this.Track(block,h);const end=ends.find(e=>e.Start>h.Start&&(!headers.find(next=>next.Start>h.Start&&next.Start<e.Start)));if(end)this.Track(block.End,end);}
      if(!block.Handle)doc.NumHandles=DxfAssign(block,doc.NumHandles);if(!block.Record.Handle)doc.NumHandles=DxfAssign(block.Record,doc.NumHandles);if(!block.End.Handle)doc.NumHandles=DxfAssign(block.End,doc.NumHandles);
      this.Track(block.Record,r);doc.Blocks.Add(block,false);this.blockByRecordHandle.set(block.Record.Handle,block);
    }
    if(!doc.Blocks.Contains(api.Block.DefaultModelSpaceName)){const block=doc.Blocks.Add(api.Block.ModelSpace);this.blockByRecordHandle.set(block.Record.Handle,block);}
    for(const r of this.Records('TABLES','DIMSTYLE'))this.ReadDimensionStyle(r);
    if(!doc.DimensionStyles.Contains(api.DimensionStyle.DefaultName))doc.DimensionStyles.Add(api.DimensionStyle.Default);
  }
  ReadDimensionStyle(r){const t=subclass(r.Tags,'AcDbDimStyleTableRecord'),item=new api.DimensionStyle(decoded(t,2),false);
    for(const[code,property,table]of dimensionStyleFields){let v=value(t,code,undefined);if(v===undefined)continue;if(table){if(v==='0')continue;v=table==='Blocks'?this.blockByRecordHandle.get(canonicalHandle(v)):this.doc.GetObjectByHandle(v);if(!v)throw new InvalidDataException('Unresolved DIMSTYLE resource group '+code);}else if(property==='DecimalSeparator')v=String.fromCharCode(v);setProperty(item,property,v);}
    for(const[code,property]of dimensionStyleBooleans){const v=value(t,code,undefined);if(v===undefined)continue;setProperty(item,property,code===294?(v?api.DimensionStyleTextDirection.RightToLeft:api.DimensionStyleTextDirection.LeftToRight):code===175?!v:!!v);}
    for(const[code,property]of[[176,'DimLineColor'],[177,'ExtLineColor'],[178,'TextColor']])if(value(t,code)!==null)item[property]=api.AciColor.FromCadIndex(value(t,code));
    const post=decoded(t,3),alt=decoded(t,4),split=post.indexOf('<>'),a=alt.indexOf('[]');item.DimPrefix=split<0?'':post.slice(0,split);item.DimSuffix=split<0?post:post.slice(split+2);item.AlternateUnits.Prefix=a<0?'':alt.slice(0,a);item.AlternateUnits.Suffix=a<0?alt:alt.slice(a+2);
    const units=value(t,273,2);item.AlternateUnits.LengthUnits=units===6?4:units===7?5:units===8?6:units;item.AlternateUnits.StackUnits=units===4||units===5;
    item.Tolerances.DisplayMethod=value(t,72,0)?3:value(t,71,0)?(item.Tolerances.LowerLimit===item.Tolerances.UpperLimit?1:2):0;
    for(const[code,target,prefix]of[[78,item,''],[285,item.AlternateUnits,''],[284,item.Tolerances,''],[286,item.Tolerances,'Alternate']])if(value(t,code)!==null)applySuppression(target,value(t,code),prefix);
    const az=value(t,79,0);item.SuppressAngularLeadingZeros=!!(az&1);item.SuppressAngularTrailingZeros=!!(az&2);
    this.Track(item,r);this.doc.DimensionStyles.Add(item,false);return item;}
  ReadLayerStates(){
    const manager=this.Records('OBJECTS','DICTIONARY').find(r=>r.Envelope.Handle===this.Context.layerStateManagerDictionaryHandle);if(!manager)return;
    const children=entries(manager.Tags),handle=(children.find(([name])=>name==='ACAD_LAYERSTATES')??children[0])?.[1];
    const dictionary=this.Records('OBJECTS','DICTIONARY').find(r=>r.Envelope.Handle===handle);if(!dictionary)return;
    for(const[name,identity]of entries(dictionary.Tags)){
      const record=this.Records('OBJECTS','XRECORD').find(r=>r.Envelope.Handle===identity);if(!record)throw new InvalidDataException('Missing layer-state XRECORD '+identity);
      const tags=subclass(record.Tags,'AcDbXrecord'),state=new api.LayerState(name);state.Description=decoded(tags,301);state.PaperSpace=value(tags,290,false);
      const current=decoded(tags,302,'0');if(this.doc.Layers.Contains(current))state.CurrentLayer=current;
      for(let i=0;i<tags.length;i++)if(tags[i].Code===330||tags[i].Code===8){let end=i+1;while(end<tags.length&&tags[end].Code!==330&&tags[end].Code!==8)end++;
        const layer=tags[i].Code===330?this.doc.GetObjectByHandle(tags[i].Value):this.doc.Layers.get_Item(DecodeDxfText(tags[i].Value));if(!(layer instanceof api.Layer)){i=end-1;continue;}
        const t=tags.slice(i+1,end),p=new api.LayerStateProperties(layer.Name);p.Flags=value(t,90,p.Flags);p.Color=value(t,92)!==null?api.AciColor.FromTrueColor(value(t,92)):api.AciColor.FromCadIndex(value(t,62,7));p.Lineweight=value(t,370,p.Lineweight);
        const type=this.doc.GetObjectByHandle(value(t,331,''));p.LinetypeName=type instanceof api.Linetype?type.Name:decoded(t,6,api.Linetype.DefaultName);if(!this.doc.Linetypes.Contains(p.LinetypeName))p.LinetypeName=api.Linetype.DefaultName;
        if(value(t,440)!==null)p.Transparency=api.Transparency.FromAlphaValue(value(t,440));if(!state.Properties.ContainsKey(p.Name))state.Properties.Add(p.Name,p);i=end-1;
      }
      this.Track(state,record);this.doc.Layers.StateManager.Add(state,false);
    }
  }
  ReadManagedObjects(){for(const r of this.Records('OBJECTS')){if(!legacyObjects.has(r.Name))continue;const method={LAYOUT:'ReadLayout',GROUP:'ReadGroup',MLINESTYLE:'ReadMLineStyle',IMAGEDEF:'ReadImageDefinition',RASTERVARIABLES:'ReadRasterVariables',DGNDEFINITION:'ReadUnderlayDefinition',DWFDEFINITION:'ReadUnderlayDefinition',PDFDEFINITION:'ReadUnderlayDefinition',IMAGEDEF_REACTOR:'ReadImageDefinitionReactor'}[r.Name];this[method](r);}}
  EnsureDefaultObjects(){const doc=this.doc;if(!doc.Layouts.Contains('Model')){const layout=api.Layout.ModelSpace;layout.AssociatedBlock=doc.Blocks.get_Item(api.Block.DefaultModelSpaceName);doc.Layouts.Add(layout);}
    if(doc.RasterVariables===null)doc.RasterVariables=new api.RasterVariables(doc);if(!doc.MlineStyles.Contains(api.MLineStyle.DefaultName))doc.MlineStyles.Add(api.MLineStyle.Default);}
  ReadEntities(){let block=null;for(const r of this.Records('BLOCKS')){if(r.Name==='BLOCK'){block=this.doc.Blocks.get_Item(decoded(r.Tags,2));continue;}if(r.Name==='ENDBLK'){block=null;continue;}if(!block)throw new InvalidDataException('Entity outside a BLOCK envelope.');this.ReadEntity(r,block);}
    for(const r of this.Records('ENTITIES')){if(this.consumed?.has(r.Start))continue;let block=this.blockByRecordHandle.get(r.Envelope.Owner);if(!block){const paper=value(r.Envelope.Common,67,0)!==0;block=this.doc.Blocks.get_Item(paper?api.Block.DefaultPaperSpaceName:api.Block.DefaultModelSpaceName);if(!block&&paper){const layout=this.doc.Layouts.Add(new api.Layout('Layout1'));block=layout.AssociatedBlock;}}this.ReadEntity(r,block);}}
  ReadEntity(r,block){if(this.consumed?.has(r.Start))return null;let entity;const code=r.Name;
    if(Object.hasOwn(primitiveReaders,code))entity=io[primitiveReaders[code]](this.Cursor(r),this.doc);
    else if(['3DSOLID','BODY','REGION'].includes(code))entity=io.ReadAcisEntity(this.Cursor(r),this.doc,code);
    else if(['MULTILEADER','MLEADER'].includes(code))entity=io.ReadMultiLeader(this.ContextWithCursor(r));
    else if(['SECTION','SECTIONOBJECT'].includes(code))entity=io.ReadSection(this.ContextWithCursor(r),code);
    else if(code==='ACAD_TABLE')entity=io.ReadStoredTable(this.Cursor(r),this.doc,this.storedTables);
    else if(code==='ATTDEF'){entity=this.ReadAttributeDefinition(r);this.ApplyCommon(entity,r);this.Track(entity,r,false);block.AttributeDefinitions.Add(entity);return entity;}
    else {const dispatch=Object.assign(Object.create(null),{TEXT:'ReadText',MTEXT:'ReadMText',INSERT:'ReadInsert',MESH:'ReadMesh',VIEWPORT:'ReadViewport',POLYLINE:'ReadPolyline',HATCH:'ReadHatch',DIMENSION:'ReadDimension',ARC_DIMENSION:'ReadDimension',LEADER:'ReadLeader',TOLERANCE:'ReadTolerance',MLINE:'ReadMLine',IMAGE:'ReadImage',WIPEOUT:'ReadWipeout',SHAPE:'ReadShape',DGNUNDERLAY:'ReadUnderlay',DWFUNDERLAY:'ReadUnderlay',PDFUNDERLAY:'ReadUnderlay'}),method=dispatch[code];
      if(method){if(typeof this[method]!=='function')throw new NotSupportedException('Typed entity reader is not implemented for '+code);entity=this[method](r,block);}else {this.chunk=this.Context.Chunk=new DocumentTagReader(this.tags,r.Start,this.identities);entity=io.ReadOpaqueEntity(this.Context,r.Section==='BLOCKS');}}
    if(entity==null)return null;this.ApplyCommon(entity,r);this.Track(entity,r,false);
    if(entity instanceof api.Viewport&&entity.Id===1&&block.Record.Layout?.IsPaperSpace){const layout=block.Record.Layout;layout.Viewport=entity;entity.Owner=block;this.doc.AddedObjects.Add(entity.Handle,entity);this.Context.RecordSourceObject(entity,r.Envelope.Source);}
    else block.Entities.Add(entity);return entity;}
  ContextWithCursor(r){this.Cursor(r);return this.Context;}
  ApplyCommon(entity,r){const t=r.Envelope.Common;if(entity instanceof api.DxfOpaqueEntity)return;entity.Layer=this.Resource('Layers',decoded(t,8,'0'));entity.Linetype=this.Resource('Linetypes',decoded(t,6,'ByLayer'));entity.Color=value(t,420)!==null?api.AciColor.FromTrueColor(value(t,420)):api.AciColor.FromCadIndex(value(t,62,256));entity.Lineweight=value(t,370,api.Lineweight.ByLayer);entity.LinetypeScale=value(t,48,1);entity.IsVisible=value(t,60,0)===0;if(value(t,440)!==null)entity.Transparency=api.Transparency.FromAlphaValue(value(t,440));
    const cursor=new DocumentTagReader([...t,new DxfTag(0,'EOF')],0),common=new io.EntityCommonDataReader();while(cursor.Code!==0){if(!io.ReadEntityCommonData(cursor,this.doc.DrawingVariables.AcadVer,common))cursor.Next();}common.Complete();entity.CommonData.ColorName=common.ColorName;entity.CommonData.ShadowMode=common.ShadowMode;entity.CommonData.ProxyGraphics=common.ProxyGraphics;
  }

  ReadViewport(r){const t=publicPayload(r.Tags.slice(r.Envelope.Body)),vp=new api.Viewport(value(t,69,2));
    for(const[code,key,dim]of[[10,'Center',3],[12,'ViewCenter',2],[13,'SnapBase',2],[14,'SnapSpacing',2],[15,'GridSpacing',2],[16,'ViewDirection',3],[17,'ViewTarget',3],[110,'UcsOrigin',3],[111,'UcsXAxis',3],[112,'UcsYAxis',3]])if(value(t,code)!==null)vp[key]=point(t,code,dim);
    for(const[code,key]of[[40,'Width'],[41,'Height'],[68,'Stacking'],[42,'LensLength'],[43,'FrontClipPlane'],[44,'BackClipPlane'],[45,'ViewHeight'],[50,'SnapAngle'],[51,'TwistAngle'],[72,'CircleZoomPercent'],[90,'Status']])if(value(t,code)!==null)vp[key]=value(t,code);
    for(const tag of t)if(tag.Code===331){const layer=this.doc.GetObjectByHandle(tag.Value);if(!(layer instanceof api.Layer))throw new InvalidDataException('Unresolved VIEWPORT frozen layer.');this.deferred.push(()=>vp.FrozenLayers.Add(layer));}
    if(value(t,340)!==null)this.deferred.push(()=>{const boundary=this.doc.GetObjectByHandle(value(t,340));if(boundary===null)throw new InvalidDataException('Unresolved viewport clipping boundary.');vp.ClippingBoundary=boundary;});
    if(value(t,361)!==null)this.Context.sunReferences.push([vp,value(t,361)]);readXData(vp,r.Tags,this.doc);return vp;}
  ReadTextFields(r,item,attribute=false){const t=publicPayload(r.Tags.slice(r.Envelope.Body));item.Style=this.Resource('TextStyles',decoded(t,7,'Standard'));item.Normal=point(t,210,3,api.Vector3.UnitZ);item.Height=value(t,40,1)>0?value(t,40,1):1;item.WidthFactor=value(t,41,1);item.ObliqueAngle=value(t,51,0);item.Rotation=value(t,50,0);item.Value=decoded(t,1);const flags=value(t,71,0);item.IsBackward=!!(flags&2);item.IsUpsideDown=!!(flags&4);
    const h=value(t,72,0),v=value(t,attribute?74:73,0);item.Alignment=v>0?(3-v)*3+h:9+h;const first=point(t,10),second=point(t,11,3,first);let pos=h===0&&v===0?first:second;
    if(h===3||h===5){const delta=api.Vector3.Subtract(second,first);item.Width=delta.Modulus();item.Rotation=api.Vector2.Angle(new api.Vector2(delta.X,delta.Y))*api.MathHelper.RadToDeg;pos=first;}
    item.Position=api.MathHelper.Transform(pos,item.Normal,api.CoordinateSystem.Object,api.CoordinateSystem.World);if(attribute){item.Flags=value(t,70,0);if(item instanceof api.AttributeDefinition)item.Prompt=decoded(t,3);}readXData(item,r.Tags,this.doc);return item;}
  ReadText(r){return this.ReadTextFields(r,new api.Text());}
  ReadAttributeDefinition(r){const t=publicPayload(r.Tags.slice(r.Envelope.Body));return this.ReadTextFields(r,new api.AttributeDefinition(decoded(t,2)),true);}
  ReadAttribute(r){const t=publicPayload(r.Tags.slice(r.Envelope.Body)),item=new api.Attribute(new api.AttributeDefinition(decoded(t,2)));item.Definition=null;this.ReadTextFields(r,item,true);this.ApplyCommon(item,r);this.Track(item,r,false);return item;}
  ReadInsert(r,owner){const t=publicPayload(r.Tags.slice(r.Envelope.Body)),block=this.doc.Blocks.get_Item(decoded(t,2));if(!block)throw new InvalidDataException('Unresolved INSERT block: '+decoded(t,2));const attributes=[];
    if(value(t,66,0)!==0){let next=r.End;while(true){const record=this.records.find(row=>row.Start===next&&row.Section===r.Section);if(!record)throw new InvalidDataException('Truncated INSERT attribute sequence.');this.consumed.add(record.Start);next=record.End;if(record.Name==='SEQEND')break;if(record.Name!=='ATTRIB')throw new InvalidDataException('INSERT requires ATTRIB records followed by SEQEND.');attributes.push(this.ReadAttribute(record));}}
    const item=api.Insert.CreateOverload('System.Collections.Generic.List<netDxf.Entities.Attribute>',attributes);item.Block=block;item.Normal=point(t,210,3,api.Vector3.UnitZ);item.Position=api.MathHelper.Transform(point(t,10),item.Normal,api.CoordinateSystem.Object,api.CoordinateSystem.World);item.Rotation=value(t,50,0);
    const factor=api.UnitHelper.ConversionFactor(block.Record.Units,owner.Record.IsForInternalUseOnly?this.doc.DrawingVariables.InsUnits:owner.Record.Units);item.Scale=new api.Vector3(value(t,41,1)/factor,value(t,42,1)/factor,value(t,43,1)/factor);item.ColumnCount=value(t,70,1);item.RowCount=value(t,71,1);item.ColumnSpacing=value(t,44,0);item.RowSpacing=value(t,45,0);readXData(item,r.Tags,this.doc);
    this.deferred.push(()=>{for(const a of item.Attributes)a.Definition=block.AttributeDefinitions.ContainsTag(a.Tag)?block.AttributeDefinitions.get_Item(a.Tag):null;});return item;}
  ReadMText(r){const chunk=this.Cursor(r,'AcDbMText'),item=new api.MText(),background={value:null},columns={value:null},definedHeight={value:null},position=api.Vector3.Zero,normal=api.Vector3.UnitZ,direction=api.Vector3.UnitX,parts=[];let explicitDirection=false,rotation=0;chunk.Next();
    while(chunk.Code!==0){if(io.TryReadMTextColumnTag(chunk,columns,definedHeight))continue;if(io.TryReadMTextBackground(chunk,background)){chunk.Next();continue;}
      const code=chunk.Code,v=chunk.Value;if(code===1001){readXData(item,r.Tags.slice(chunk.index-r.Start),this.doc);while(chunk.Code!==0)chunk.Next();break;}if(code===101){columns.value=io.ReadMTextEmbeddedColumns(chunk);continue;}
      if(code===1||code===3)parts.push(v);else if([10,20,30].includes(code))position[code===10?'X':code===20?'Y':'Z']=v;else if([210,220,230].includes(code))normal[code===210?'X':code===220?'Y':'Z']=v;
      else if([11,21,31].includes(code)){direction[code===11?'X':code===21?'Y':'Z']=v;explicitDirection=true;}else if(code===50){rotation=v;explicitDirection=false;}
      else if(code===40)item.Height=v>0?v:1;else if(code===41)item.RectangleWidth=Math.max(0,v);else if(code===44)item.LineSpacingFactor=v>=.25&&v<=4?v:1;else if(code===71)item.AttachmentPoint=v;else if(code===72)item.DrawingDirection=v;else if(code===73)item.LineSpacingStyle=v;else if(code===7)item.Style=this.Resource('TextStyles',DecodeDxfText(v));chunk.Next();
    }
    item.Position=position;item.Normal=normal;item.Value=DecodeDxfText(parts.join(''));if(explicitDirection){const d=api.MathHelper.Transform(direction,normal,api.CoordinateSystem.World,api.CoordinateSystem.Object);rotation=api.Vector2.Angle(new api.Vector2(d.X,d.Y));}item.Rotation=rotation*api.MathHelper.RadToDeg;item.BackgroundFill=background.value;item.Columns=columns.value;item.$definedColumnHeight=definedHeight.value;io.ReadMTextColumnXData(item);return item;}
  ReadMesh(r){const t=publicPayload(r.Tags.slice(r.Envelope.Body)),vertices=[],faces=[],edges=[];let at=t.findIndex(t=>t.Code===92);if(at<0)throw new InvalidDataException('MESH is missing its vertex count.');const vertexCount=t[at++].Value;if(!Number.isInteger(vertexCount)||vertexCount<0||vertexCount>(t.length-at)/3)throw new InvalidDataException('Invalid MESH vertex count.');
    for(let i=0;i<vertexCount;i++){if(t[at]?.Code!==10||t[at+1]?.Code!==20||t[at+2]?.Code!==30)throw new InvalidDataException('Truncated MESH vertex.');vertices.push(new api.Vector3(t[at].Value,t[at+1].Value,t[at+2].Value));at+=3;}
    if(t[at]?.Code!==93)throw new InvalidDataException('MESH is missing its face-list size.');const end=at+1+t[at].Value;at++;if(end>t.length||end<at)throw new InvalidDataException('Invalid MESH face-list size.');while(at<end){if(t[at].Code!==90)throw new InvalidDataException('Invalid MESH face.');const n=t[at++].Value;if(n<3||at+n>end)throw new InvalidDataException('Invalid MESH face vertex count.');const face=[];for(let i=0;i<n;i++){if(t[at].Code!==90)throw new InvalidDataException('Invalid MESH index.');face.push(t[at++].Value);}faces.push(face);}
    if(t[at]?.Code===94){const n=t[at++].Value;if(n<0||at+n*2>t.length)throw new InvalidDataException('Invalid MESH edge count.');for(let i=0;i<n;i++){if(t[at]?.Code!==90||t[at+1]?.Code!==90)throw new InvalidDataException('Invalid MESH edge.');edges.push(new api.MeshEdge(t[at++].Value,t[at++].Value));}if(t[at]?.Code!==95||t[at++].Value!==n)throw new InvalidDataException('MESH crease count does not match its edges.');for(let i=0;i<n;i++){if(t[at]?.Code!==140)throw new InvalidDataException('Truncated MESH creases.');edges[i].Crease=t[at++].Value;}}
    const mesh=new api.Mesh(vertices,faces,edges);mesh.BlendCrease=value(t,72,0)!==0;mesh.SubdivisionLevel=value(t,91,0);readXData(mesh,r.Tags,this.doc);return mesh;}

  ReadPolyline(r){const tags=publicPayload(r.Tags.slice(r.Envelope.Body)),marker=r.Tags[r.Envelope.Body]?.Value,flags=value(tags,70,0),smooth=value(tags,75,0),normal=point(tags,210,3,api.Vector3.UnitZ),xdata=new api.XDataDictionary();readXData({XData:xdata},r.Tags,this.doc);let result;
    this.Cursor(r);const context=this.Context;
    if(marker==='AcDbPolyFaceMesh')result=io.ReadStoredPolyfaceMesh(context);
    else if(marker==='AcDb2dPolyline')result=io.ReadStoredPolyline2D(context);
    else if(marker==='AcDb3dPolyline'||marker==='AcDbPolygonMesh'){
      while(this.chunk.Code!==0)this.chunk.Next();
      if(!(flags&6)&&smooth===0)result=marker==='AcDb3dPolyline'?io.ReadStoredPolylineSequence(context,flags,normal,Array.from(xdata.Values)):io.ReadStoredPolygonMeshSequence(context,flags,normal,Array.from(xdata.Values),value(tags,71,0),value(tags,72,0));
      else {const vertices=[];while(this.chunk.Code===0&&this.chunk.ReadString()==='VERTEX'){if(vertices.length>=65536)throw new InvalidDataException('POLYLINE vertex admission limit exceeded.');vertices.push(ReadVertex(context));}
        if(this.chunk.Code!==0||this.chunk.ReadString()!=='SEQEND')throw new InvalidDataException('POLYLINE requires SEQEND.');this.chunk.Next();while(this.chunk.Code!==0)this.chunk.Next();const controls=vertices.filter(v=>!(flags&4)||!!(v.Flags&16));
        if(marker==='AcDb3dPolyline'){result=new api.Polyline3D(controls.map(v=>v.Position),!!(flags&1));result.SmoothType=smooth===8?0:smooth;}
        else{const u=value(tags,71,0),v=value(tags,72,0),points=[];if(controls.length!==u*v)throw new InvalidDataException('POLYGONMESH control count must equal M*N.');for(let i=0;i<controls.length;i++)points[Math.floor(i/v)+(i%v)*u]=controls[i].Position;result=new api.PolygonMesh(u,v,points);result.SmoothType=smooth===8?0:smooth;if(value(tags,73,0)>0)result.DensityU=value(tags,73);if(value(tags,74,0)>0)result.DensityV=value(tags,74);}
        result.Flags=flags;result.Normal=normal;result.XData.AddRange(xdata.Values);
      }
    }else throw new InvalidDataException('Unsupported POLYLINE subclass '+marker);
    for(const child of this.records)if(child.Section===r.Section&&child.Start>=r.End&&child.Start<this.chunk.index)this.consumed.add(child.Start);return result;
  }

  ReadUnderlay(r){const t=publicPayload(r.Tags.slice(r.Envelope.Body)),definition=this.doc.GetObjectByHandle(value(t,340));if(!(definition instanceof api.UnderlayDefinition))throw new InvalidDataException('Unresolved underlay definition.');const item=new api.Underlay(definition),normal=point(t,210,3,api.Vector3.UnitZ);item.Normal=normal;item.Position=api.MathHelper.Transform(point(t,10),normal,api.CoordinateSystem.Object,api.CoordinateSystem.World);item.Scale=new api.Vector2(value(t,41,1),value(t,42,1));item.Rotation=value(t,50,0);item.DisplayOptions=value(t,280,1);item.Contrast=value(t,281,100);item.Fade=value(t,282,0);const points=this.ReadPoints(t,11,2);if(points.length)item.ClippingBoundary=points.length===2?new api.ClippingBoundary(...points):new api.ClippingBoundary(points);readXData(item,r.Tags,this.doc);return item;}
  ReadPoints(tags,code,dimension=3){const points=[];for(let i=0;i<tags.length;i++)if(tags[i].Code===code){const stop=tags.findIndex((t,j)=>j>i&&t.Code===code);points.push(point(tags.slice(i,stop<0?tags.length:stop),code,dimension));}return points;}
  ReadWipeout(r){const t=publicPayload(r.Tags.slice(r.Envelope.Body)),position=point(t,10),u=point(t,11,3,api.Vector3.UnitX),v=point(t,12,3,api.Vector3.UnitY),normal=api.Vector3.Normalize(api.Vector3.CrossProduct(u,v)),origin=api.MathHelper.Transform(position,normal,api.CoordinateSystem.World,api.CoordinateSystem.Object),ux=api.MathHelper.Transform(u,normal,api.CoordinateSystem.World,api.CoordinateSystem.Object),vy=api.MathHelper.Transform(v,normal,api.CoordinateSystem.World,api.CoordinateSystem.Object),points=this.ReadPoints(t,14,2),type=value(t,71,1);
    if(type===2&&points.length>1&&api.Vector2.Equals(points[0],points.at(-1)))points.pop();const transformed=points.map(p=>new api.Vector2(origin.X+(p.X+.5)*ux.X+(.5-p.Y)*vy.X,origin.Y+(p.X+.5)*ux.Y+(.5-p.Y)*vy.Y)),boundary=type===1?new api.ClippingBoundary(...transformed):new api.ClippingBoundary(transformed),item=new api.Wipeout(boundary);item.Normal=normal;item.Elevation=origin.Z;readXData(item,r.Tags,this.doc);return item;}
  ReadTolerance(r){const t=publicPayload(r.Tags.slice(r.Envelope.Body)),item=api.Tolerance.ParseStringRepresentation(decoded(t,1));item.Position=point(t,10);item.Normal=point(t,210,3,api.Vector3.UnitZ);item.Style=this.Resource('DimensionStyles',decoded(t,3,'Standard'));const direction=api.MathHelper.Transform(point(t,11,3,api.Vector3.UnitX),item.Normal,api.CoordinateSystem.World,api.CoordinateSystem.Object);item.Rotation=api.Vector2.Angle(new api.Vector2(direction.X,direction.Y))*api.MathHelper.RadToDeg;readXData(item,r.Tags,this.doc);this.ReadDimensionOverrides(item);return item;}
  ReadLeader(r){const t=publicPayload(r.Tags.slice(r.Envelope.Body)),normal=point(t,210,3,api.Vector3.UnitZ),points=this.ReadPoints(t,10).map(p=>api.MathHelper.Transform(p,normal,api.CoordinateSystem.World,api.CoordinateSystem.Object)),style=this.Resource('DimensionStyles',decoded(t,3,'Standard')),item=new api.Leader(points.map(p=>new api.Vector2(p.X,p.Y)),style,value(t,75,0)!==0);item.Normal=normal;item.Elevation=points[0]?.Z??0;item.ShowArrowhead=value(t,71,1)!==0;item.PathType=value(t,72,0);item.LineColor=api.AciColor.FromCadIndex(value(t,77,256));const direction=api.MathHelper.Transform(point(t,211,3,api.Vector3.UnitX),normal,api.CoordinateSystem.World,api.CoordinateSystem.Object),offset=api.MathHelper.Transform(point(t,213),normal,api.CoordinateSystem.World,api.CoordinateSystem.Object);item.Direction=new api.Vector2(direction.X,direction.Y);item.Offset=new api.Vector2(offset.X,offset.Y);const handle=value(t,340);if(handle)this.deferred.push(()=>{const annotation=this.doc.GetObjectByHandle(handle);if(!annotation)throw new InvalidDataException('Unresolved LEADER annotation.');item.Annotation=annotation;});readXData(item,r.Tags,this.doc);this.ReadDimensionOverrides(item);return item;}
  ReadMLine(r){const t=publicPayload(r.Tags.slice(r.Envelope.Body)),normal=point(t,210,3,api.Vector3.UnitZ),style=this.doc.GetObjectByHandle(value(t,340))??this.doc.MlineStyles.get_Item(decoded(t,2,'Standard'));if(!style)throw new InvalidDataException('Unresolved MLINE style.');const item=new api.MLine([],style,value(t,40,1));item.Normal=normal;item.Flags=value(t,71,1);item.Justification=value(t,70,1);const origin=api.MathHelper.Transform(point(t,10),normal,api.CoordinateSystem.World,api.CoordinateSystem.Object);item.Elevation=origin.Z;
    for(let i=0;i<t.length;i++)if(t[i].Code===11){let end=i+1;while(end<t.length&&t[end].Code!==11)end++;const tags=t.slice(i,end),vectors=[11,12,13].map(code=>{const p=api.MathHelper.Transform(point(tags,code),normal,api.CoordinateSystem.World,api.CoordinateSystem.Object);return new api.Vector2(p.X,p.Y);}),distances=[];for(let n=0;n<tags.length;n++)if(tags[n].Code===74){const count=tags[n].Value;if(count<0||count>tags.length-n-1)throw new InvalidDataException('Invalid MLINE distance count.');const row=new ReferenceList();for(let j=0;j<count;j++){if(tags[++n]?.Code!==41)throw new InvalidDataException('Missing MLINE distance.');row.Add(tags[n].Value);}distances.push(row);}item.Vertexes.Add(new api.MLineVertex(...vectors,distances));i=end-1;}
    if(value(t,72,item.Vertexes.Count)!==item.Vertexes.Count)throw new InvalidDataException('MLINE vertex count mismatch.');readXData(item,r.Tags,this.doc);return item;}
  ReadImage(r){const t=publicPayload(r.Tags.slice(r.Envelope.Body)),definition=this.doc.GetObjectByHandle(value(t,340));if(!(definition instanceof api.ImageDefinition))throw new InvalidDataException('Unresolved IMAGE definition.');const u=point(t,11),v=point(t,12),normal=api.Vector3.Normalize(api.Vector3.CrossProduct(u,v)),factor=api.UnitHelper.ConversionFactor(this.doc.RasterVariables.Units,this.doc.DrawingVariables.InsUnits),item=new api.Image(definition,point(t,10),u.Modulus()*Math.abs(value(t,13,1))/factor,v.Modulus()*Math.abs(value(t,23,1))/factor);item.Normal=normal;const ux=api.MathHelper.Transform(u,normal,api.CoordinateSystem.World,api.CoordinateSystem.Object),vy=api.MathHelper.Transform(v,normal,api.CoordinateSystem.World,api.CoordinateSystem.Object);item.Uvector=new api.Vector2(ux.X,ux.Y);item.Vvector=new api.Vector2(vy.X,vy.Y);item.DisplayOptions=value(t,70,7);item.Clipping=value(t,280,0)!==0;item.Brightness=value(t,281,50);item.Contrast=value(t,282,50);item.Fade=value(t,283,0);const points=this.ReadPoints(t,14,2).map(p=>new api.Vector2(p.X+.5,p.Y+.5)),type=value(t,71,1);if(type===2&&points.length>1&&api.Vector2.Equals(points[0],points.at(-1)))points.pop();if(points.length)item.ClippingBoundary=type===1?new api.ClippingBoundary(...points):new api.ClippingBoundary(points);readXData(item,r.Tags,this.doc);return item;}
  ReadShape(r){const t=publicPayload(r.Tags.slice(r.Envelope.Body)),style=this.doc.GetObjectByHandle(value(t,340));if(!(style instanceof api.ShapeStyle))throw new InvalidDataException('Unresolved SHAPE style.');const name=style.ShapeName(value(t,2,0));if(!name)throw new InvalidDataException('Unresolved SHAPE number.');const item=new api.Shape(name,style);item.Normal=point(t,210,3,api.Vector3.UnitZ);item.Position=api.MathHelper.Transform(point(t,10),item.Normal,api.CoordinateSystem.Object,api.CoordinateSystem.World);item.Size=value(t,40,1);item.Rotation=value(t,50,0);item.WidthFactor=value(t,41,1);item.ObliqueAngle=value(t,51,0);item.Thickness=value(t,39,0);readXData(item,r.Tags,this.doc);return item;}

  ReadHatch(record) {
    // Metadata is record-wide: a counted packet can occur before or after XData.
    // Each packet consumes only its own grammar; scanning for a suffix loses data.
    let name = '', fill = api.HatchFillType.SolidFill, elevation = 0;
    const normal = api.Vector3.UnitZ, xdata = [];
    let patternAngle = 0, patternScale = 1, patternType = api.HatchType.UserDefined;
    let patternStyle = api.HatchStyle.Normal, isDouble = false, associative = false;
    let patternLines = null, gradient = null, boundaries = [], hasBoundaryCount = false;
    let seedPoints = null, pixelSize = null;
    this.Cursor(record);
    while (this.chunk.Code !== 0) {
      switch (this.chunk.Code) {
        case 2: name = DecodeDxfText(this.chunk.ReadString()); this.chunk.Next(); break;
        case 30: elevation = this.chunk.ReadDouble(); this.chunk.Next(); break;
        case 210: normal.X = this.chunk.ReadDouble(); this.chunk.Next(); break;
        case 220: normal.Y = this.chunk.ReadDouble(); this.chunk.Next(); break;
        case 230: normal.Z = this.chunk.ReadDouble(); this.chunk.Next(); break;
        case 70: fill = this.chunk.ReadShort(); this.chunk.Next(); break;
        case 71: if (this.chunk.ReadShort() !== 0) associative = true; this.chunk.Next(); break;
        case 91:
          if (hasBoundaryCount) throw this.InvalidHatchPathData('duplicate group-91 boundary list');
          hasBoundaryCount = true;
          boundaries = this.ReadHatchBoundaryPaths(this.chunk.ReadInt());
          break;
        case 92: case 93:
          throw this.InvalidHatchPathData('path data outside the declared group-91 boundary list');
        case 52: patternAngle = this.chunk.ReadDouble(); this.chunk.Next(); break;
        case 41:
          patternScale = this.chunk.ReadDouble();
          if (patternScale <= 0) patternScale = 1;
          this.chunk.Next(); break;
        case 75: patternStyle = this.chunk.ReadShort(); this.chunk.Next(); break;
        case 76: patternType = this.chunk.ReadShort(); this.chunk.Next(); break;
        case 77: {
          const flag = this.chunk.ReadShort();
          if (flag !== 0 && flag !== 1) throw new InvalidDataException('Invalid HATCH double-pattern flag for group code 77 at position ' + this.chunk.CurrentPosition + ': expected 0 or 1.');
          isDouble = flag === 1; this.chunk.Next(); break;
        }
        case 78:
          if (patternLines !== null) throw this.InvalidHatchPatternData('duplicate group-78 definition list');
          patternLines = this.ReadHatchPatternDefinitionLine(this.chunk.ReadShort()); break;
        case 53: case 43: case 44: case 45: case 46: case 79: case 49:
          throw this.InvalidHatchPatternData('pattern field outside its declared line or dash count');
        case 450: case 451: case 452: case 453: case 460: case 461: case 462: case 470: case 463: case 63: case 421:
          gradient ??= { Fields: 0, Colors: [] }; this.ReadHatchGradientData(gradient); break;
        case 47: pixelSize = this.ReadHatchPixelSize(pixelSize); break;
        case 98:
          if (seedPoints !== null) throw this.InvalidHatchSeedData('duplicate group-98 list');
          seedPoints = this.ReadHatchSeedPoints(); break;
        case 1001: xdata.push(ReadXDataRecord(this.chunk, this.doc)); break;
        default: this.chunk.Next(); break;
      }
    }
    let pattern = gradient === null ? null : this.CreateHatchGradientPattern(gradient);
    if (pattern === null) pattern = new api.HatchPattern(name);
    if (!(pattern instanceof api.HatchGradientPattern)) pattern.Angle = patternAngle;
    pattern.Scale = patternScale; pattern.Type = patternType; pattern.Style = patternStyle;
    pattern.IsDouble = isDouble; pattern.Fill = fill;
    for (const data of patternLines ?? []) pattern.LineDefinitions.Add(this.CreateHatchPatternLine(data, patternScale, patternAngle));

    // An empty input remains an editable metadata entity. The writer's existing
    // boundary preflight rejects export before touching the destination or handles.
    const item = new api.Hatch(pattern, [], associative);
    item.Elevation = elevation; item.Normal = normal; item.PixelSize = pixelSize;
    item.SeedPoints.Clear();
    for (const seed of seedPoints ?? []) item.SeedPoints.Add(seed);
    for (const { path, handles } of boundaries) {
      item.BoundaryPaths.Add(path);
      if (handles.length) this.deferred.push(() => {
        for (const handle of handles) {
          const target = this.doc.GetObjectByHandle(handle);
          if (!target) throw new InvalidDataException('Unresolved HATCH source ' + handle);
          if (item.Associative) { path.AddContour(target); target.AddReactor(item); }
        }
      });
    }
    item.XData.AddRange(xdata);
    if (item.XData.ContainsAppId('ACAD')) {
      const records = item.XData.get_Item('ACAD').XDataRecord, index = io.HatchPatternXData.FindOrigin(records);
      if (index >= 0) pattern.Origin = new api.Vector2(records.get_Item(index).Value, records.get_Item(index + 1).Value);
    }
    return item;
  }
  ReadHatchBoundaryPaths(count) {
    if (count < 0) throw this.InvalidHatchPathData('negative group-91 path count');
    const boundaries = [];
    this.ReadNextHatchEdgeTag();
    for (let index = 0; index < count; index++) {
      if (this.chunk.Code !== 92) throw this.InvalidHatchPathData('expected group code 92 for path ' + index);
      const pathType = this.chunk.ReadInt(), edges = [];
      this.ReadNextHatchEdgeTag();
      if (pathType & 2) {
        const closed = {}, verticesCount = {};
        io.ReadHatchPolylineHeader(this, closed, verticesCount);
        const vertices = [];
        for (let i = 0; i < verticesCount.value; i++) {
          this.RequireHatchPolylineCode(10); const x = this.chunk.ReadDouble(); this.ReadNextHatchPolylineTag();
          this.RequireHatchPolylineCode(20); const y = this.chunk.ReadDouble(); this.ReadNextHatchPolylineTag();
          let bulge = 0;
          if (this.chunk.Code === 42) { bulge = this.chunk.ReadDouble(); this.ReadNextHatchPolylineTag(); }
          vertices.push(new api.Vector3(x, y, bulge));
        }
        edges.push(Object.assign(new api.HatchBoundaryPath.Polyline(), { IsClosed: closed.value, Vertexes: vertices }));
      } else {
        if (this.chunk.Code !== 93) throw this.InvalidHatchPathData('expected group code 93 after non-polyline path flags');
        const edgeCount = this.ReadHatchEdgeCount(93);
        for (let i = 0; i < edgeCount; i++) {
          this.RequireHatchEdgeCode(72); const type = this.chunk.ReadShort();
          if (type < 1 || type > 4) throw this.InvalidHatchEdgeData('unsupported edge type ' + type);
          this.ReadNextHatchEdgeTag();
          edges.push(type === 4 ? this.ReadHatchSplineEdge() : io.ReadHatchScalarEdge(this, type));
        }
      }
      const handles = [];
      if (pathType & 2) {
        const referenceCount = this.ReadHatchPolylineCount(97); this.ReadNextHatchPolylineTag();
        for (let i = 0; i < referenceCount; i++) {
          this.RequireHatchPolylineCode(330); handles.push(this.chunk.ReadHex()); this.ReadNextHatchPolylineTag();
        }
        if ([10, 20, 42, 72, 73, 93, 97, 330].includes(this.chunk.Code))
          throw this.InvalidHatchPolylineData('unexpected data after the counted polyline lists');
      } else {
        const referenceCount = this.ReadHatchEdgeCount(97);
        for (let i = 0; i < referenceCount; i++) {
          this.RequireHatchEdgeCode(330); handles.push(this.chunk.ReadHex()); this.ReadNextHatchEdgeTag();
        }
        if ([72, 97, 330].includes(this.chunk.Code)) throw this.InvalidHatchEdgeData('excess edge or source-reference data');
      }
      const path = api.HatchBoundaryPath.FromEdges(edges); path.PathType = pathType;
      // Grow from complete input packets, never from the advertised capacity.
      boundaries.push({ path, handles });
    }
    return boundaries;
  }
  ReadHatchSplineEdge() {
    const degree = {}, rational = {}, periodic = {}, knotCount = {}, controlCount = {};
    io.ReadHatchSplineHeader(this, degree, rational, periodic, knotCount, controlCount);
    const knots = [], controls = [];
    for (let i = 0; i < knotCount.value; i++) knots.push(this.ReadHatchEdgeDouble(40));
    for (let i = 0; i < controlCount.value; i++) {
      const point = this.ReadHatchEdgePoint(10, 20), weight = this.chunk.Code === 42 ? this.ReadHatchEdgeDouble(42) : 1;
      controls.push(new api.Vector3(point.X, point.Y, weight));
    }
    const spline = Object.assign(new api.HatchBoundaryPath.Spline(), {
      Degree: degree.value, IsRational: rational.value, IsPeriodic: periodic.value, Knots: knots, ControlPoints: controls
    });
    if (this.doc.DrawingVariables.AcadVer >= api.DxfVersion.AutoCad2010) {
      const count = this.ReadHatchEdgeCount(97);
      for (let i = 0; i < count; i++) spline.FitPoints.Add(this.ReadHatchEdgePoint(11, 21));
      while (this.chunk.Code === 12 || this.chunk.Code === 13) {
        if (this.chunk.Code === 12) {
          if (spline.StartTangent !== null) throw this.InvalidHatchEdgeData('duplicate spline start tangent');
          spline.StartTangent = this.ReadHatchEdgePoint(12, 22);
        } else {
          if (spline.EndTangent !== null) throw this.InvalidHatchEdgeData('duplicate spline end tangent');
          spline.EndTangent = this.ReadHatchEdgePoint(13, 23);
        }
      }
    }
    try { HatchSplineData.Validate(spline); }
    catch (error) { if (error instanceof ArgumentException) throw this.InvalidHatchEdgeData(error.message); throw error; }
    return spline;
  }
  ReadHatchSeedPoints() {
    const count = this.chunk.ReadInt();
    if (count < 0) throw this.InvalidHatchSeedData('negative group-98 count');
    const values = [];
    this.ReadNextHatchSeedTag();
    for (let i = 0; i < count; i++) {
      if (this.chunk.Code !== 10) throw this.InvalidHatchSeedData('expected group 10 for seed X');
      const x = this.chunk.ReadDouble(); this.ReadNextHatchSeedTag();
      if (this.chunk.Code !== 20) throw this.InvalidHatchSeedData('expected group 20 for seed Y');
      const y = this.chunk.ReadDouble(); values.push(new api.Vector2(x, y)); this.ReadNextHatchSeedTag();
    }
    return values;
  }
  ReadNextHatchSeedTag() { do { this.chunk.Next(); } while (this.chunk.Code === 999); }
  InvalidHatchSeedData(detail) {
    return new InvalidDataException('Invalid HATCH seed-point data at group code ' + this.chunk.Code + ', position ' + this.chunk.CurrentPosition + ': ' + detail + '.');
  }
  ReadHatchPixelSize(previous) {
    const value = this.chunk.ReadDouble();
    if (previous !== null || value < 0) throw new InvalidDataException('Invalid HATCH pixel size for group code 47 at position ' + this.chunk.CurrentPosition + ': expected one nonnegative value.');
    this.chunk.Next(); return value;
  }
  InvalidHatchPathData(detail) {
    return new InvalidDataException('Invalid HATCH boundary path list at group code ' + this.chunk.Code + ', position ' + this.chunk.CurrentPosition + ': ' + detail + '.');
  }
  // Gradient scalar order is independent of stop order. Counts are constraints,
  // never allocation sizes, and XData is consumed by the surrounding record reader.
  ReadHatchGradientData(data){
    const code=this.chunk.Code,field=code===450?1:code===451?2:code===452?4:code===453?8:
      code===460?16:code===461?32:code===462?64:code===470?128:0;
    if(data.Fields&field)throw this.InvalidHatchGradientData('duplicate scalar field');
    data.Fields|=field;
    switch(code){
      case 450:data.Kind=this.chunk.ReadInt();break;
      case 451:data.Reserved=this.chunk.ReadInt();break;
      case 452:data.Mode=this.chunk.ReadInt();break;
      case 453:data.ColorCount=this.chunk.ReadInt();break;
      case 460:data.Angle=this.chunk.ReadDouble();break;
      case 461:data.Shift=this.chunk.ReadDouble();break;
      case 462:data.Tint=this.chunk.ReadDouble();break;
      case 470:data.Name=this.chunk.ReadString();break;
      case 463:
        if(data.Colors.length===2)throw this.InvalidHatchGradientData('more than two group-463 color stops');
        data.Colors.push({Position:this.chunk.ReadDouble(),Index:null,HasRgb:false,Color:null});break;
      case 63:case 421:{
        if(data.Colors.length===0)throw this.InvalidHatchGradientData('color component without a preceding group-463 stop');
        const color=data.Colors[data.Colors.length-1];
        if(code===63){
          if(color.Index!==null)throw this.InvalidHatchGradientData('duplicate ACI component in a color stop');
          color.Index=this.chunk.ReadShort();
        }else{
          if(color.HasRgb)throw this.InvalidHatchGradientData('duplicate RGB component in a color stop');
          color.Color=api.AciColor.FromTrueColor(this.chunk.ReadInt());color.HasRgb=true;
        }break;
      }
    }
    this.chunk.Next();
  }
  CreateHatchGradientPattern(data){
    if(data.Fields!==255)throw this.InvalidHatchGradientData('expected exactly one of each group 450, 451, 452, 453, 460, 461, 462 and 470');
    if(data.Kind!==0&&data.Kind!==1)throw this.InvalidHatchGradientData('group code 450 must be zero or one');
    if(data.ColorCount!==(data.Kind===0?0:2)||data.Colors.length!==data.ColorCount)
      throw this.InvalidHatchGradientData('group code 453 must declare zero colors for solid or exactly two gradient stops');
    if(data.Kind===0)return null;
    if(data.Reserved!==0)throw this.InvalidHatchGradientData('unsupported reserved value for group code 451');
    if(data.Mode!==0&&data.Mode!==1)throw this.InvalidHatchGradientData('group code 452 must be zero or one');
    if(data.Shift<0||data.Shift>1)throw this.InvalidHatchGradientData('group code 461 shift must be between zero and one','shift');
    if(data.Tint<0||data.Tint>1)throw this.InvalidHatchGradientData('group code 462 tint must be between zero and one','tint');
    for(let i=0;i<data.Colors.length;i++)if(!data.Colors[i].HasRgb||data.Colors[i].Position!==i)
      throw this.InvalidHatchGradientData('expected group-463 values zero then one, each with one group-421 RGB value');
    const type=Object.entries(HatchGradientPatternTypeStringValues).find(([,name])=>OrdinalIgnoreCaseEquals(name,data.Name));
    if(!type)throw this.InvalidHatchGradientData('unsupported gradient name for group code 470');
    const result=new api.HatchGradientPattern(data.Colors[0].Color,data.Colors[1].Color,data.Mode===1,data.Tint,Number(type[0]));
    result.Shift=data.Shift;result.Angle=data.Angle*api.MathHelper.RadToDeg;
    result.Color1AciIndex=data.Colors[0].Index;result.Color2AciIndex=data.Colors[1].Index;
    return result;
  }
  InvalidHatchGradientData(reason,context='data'){
    return new InvalidDataException('Invalid HATCH gradient '+context+' at group code '+this.chunk.Code+', position '+this.chunk.CurrentPosition+': '+reason+'.');
  }
  ReadHatchPatternDefinitionLine(numLines){
    if(numLines<0)throw this.InvalidHatchPatternData('negative group-78 line count');
    const definitions=[];this.ReadNextHatchPatternTag();
    for(let i=0;i<numLines;i++){
      if(this.chunk.Code!==53)throw this.InvalidHatchPatternData('expected group 53 to start the next pattern line');
      const angle=this.chunk.ReadDouble();this.ReadNextHatchPatternTag();
      const origin=api.Vector2.Zero,delta=api.Vector2.Zero,dashes=[];let fields=0;
      while(fields!==31){
        const code=this.chunk.Code,field=code===43?1:code===44?2:code===45?4:code===46?8:code===79?16:0;
        if(!field)throw this.InvalidHatchPatternData('expected unconsumed line fields 43, 44, 45, 46 and 79');
        if(fields&field)throw this.InvalidHatchPatternData('duplicate scalar or dash-count field in one pattern line');
        fields|=field;
        if(code===79){
          const count=this.chunk.ReadShort();if(count<0)throw this.InvalidHatchPatternData('negative group-79 dash count');
          this.ReadNextHatchPatternTag();
          for(let j=0;j<count;j++){if(this.chunk.Code!==49)throw this.InvalidHatchPatternData('expected group 49 for the next dash length');dashes.push(this.chunk.ReadDouble());this.ReadNextHatchPatternTag();}
        }else{
          const target=code===43||code===44?origin:delta;target[code===43||code===45?'X':'Y']=this.chunk.ReadDouble();this.ReadNextHatchPatternTag();
        }
      }
      definitions.push({angle,origin,delta,dashes});
    }
    return definitions;
  }
  CreateHatchPatternLine(data,scale,patternAngle){
    // Match the source operation order. Do not normalize the wire angles before
    // rotating, or divide a completed sum instead of its individual terms.
    const sinOrigin=DotNetMath.Sin(patternAngle*api.MathHelper.DegToRad),cosOrigin=DotNetMath.Cos(patternAngle*api.MathHelper.DegToRad);
    const sinDelta=DotNetMath.Sin(data.angle*api.MathHelper.DegToRad),cosDelta=DotNetMath.Cos(data.angle*api.MathHelper.DegToRad);
    const line=new api.HatchPatternLineDefinition();line.Angle=data.angle-patternAngle;
    line.Origin=new api.Vector2(cosOrigin*data.origin.X/scale+sinOrigin*data.origin.Y/scale,-sinOrigin*data.origin.X/scale+cosOrigin*data.origin.Y/scale);
    line.Delta=new api.Vector2(cosDelta*data.delta.X/scale+sinDelta*data.delta.Y/scale,-sinDelta*data.delta.X/scale+cosDelta*data.delta.Y/scale);
    for(const dash of data.dashes)line.DashPattern.Add(dash/scale);return line;
  }
  ReadNextHatchPatternTag(){do{this.chunk.Next();}while(this.chunk.Code===999);}
  InvalidHatchPatternData(detail){return new InvalidDataException('Invalid HATCH pattern definition at group code '+this.chunk.Code+', position '+this.chunk.CurrentPosition+': '+detail+'.');}
  RequireHatchPolylineCode(code) { if (this.chunk.Code !== code) throw this.InvalidHatchPolylineData('expected group code ' + code); }
  RequireHatchEdgeCode(code) { if (this.chunk.Code !== code) throw this.InvalidHatchEdgeData('expected group code ' + code); }
  InvalidHatchPolylineData(detail) {
    return new InvalidDataException('Invalid HATCH polyline boundary at group code ' + this.chunk.Code + ', position ' + this.chunk.CurrentPosition + ': ' + detail + '.');
  }
  InvalidHatchEdgeData(detail) {
    return new InvalidDataException('Invalid HATCH edge boundary at group code ' + this.chunk.Code + ', position ' + this.chunk.CurrentPosition + ': ' + detail + '.');
  }
  ReadHatchPolylineCount(code) {
    this.RequireHatchPolylineCode(code); const count = this.chunk.ReadInt();
    if (count < 0) throw this.InvalidHatchPolylineData('expected a nonnegative list count');
    return count;
  }
  ReadHatchEdgeCount(code) {
    this.RequireHatchEdgeCode(code); const count = this.chunk.ReadInt();
    if (count < 0) throw this.InvalidHatchEdgeData('negative list count');
    this.ReadNextHatchEdgeTag(); return count;
  }
  ReadHatchPolylineFlag(code) {
    this.RequireHatchPolylineCode(code); const flag = this.chunk.ReadShort();
    if (flag !== 0 && flag !== 1) throw this.InvalidHatchPolylineData('expected a flag of zero or one');
    return flag !== 0;
  }
  ReadHatchEdgeFlag(code) {
    this.RequireHatchEdgeCode(code); const flag = this.chunk.ReadShort();
    if (flag !== 0 && flag !== 1) throw this.InvalidHatchEdgeData('expected a flag of zero or one');
    this.ReadNextHatchEdgeTag(); return flag !== 0;
  }
  ReadNextHatchPolylineTag() { do { this.chunk.Next(); } while (this.chunk.Code === 999); }
  ReadNextHatchEdgeTag() { do { this.chunk.Next(); } while (this.chunk.Code === 999); }
  ReadHatchEdgeDouble(code) {
    this.RequireHatchEdgeCode(code); const value = this.chunk.ReadDouble(); this.ReadNextHatchEdgeTag(); return value;
  }
  ReadHatchEdgePoint(xCode, yCode) { return new api.Vector2(this.ReadHatchEdgeDouble(xCode), this.ReadHatchEdgeDouble(yCode)); }

  ReadDimension(r){const t=publicPayload(r.Tags.slice(r.Envelope.Body)),flags=value(t,70,0),type=r.Name==='ARC_DIMENSION'?7:flags&15,normal=point(t,210,3,api.Vector3.UnitZ),worldToObject=p=>api.MathHelper.Transform(p,normal,api.CoordinateSystem.World,api.CoordinateSystem.Object),xy=p=>new api.Vector2(p.X,p.Y),definition=worldToObject(point(t,10)),ref=code=>xy(worldToObject(point(t,code))),names=['LinearDimension','AlignedDimension','Angular2LineDimension','DiametricDimension','RadialDimension','Angular3PointDimension','OrdinateDimension','ArcLengthDimension'];if(!names[type])throw new InvalidDataException('Unsupported DIMENSION type '+type);const item=new api[names[type]]();item.Normal=normal;item.Elevation=definition.Z;
    if(type===0||type===1){item.FirstReferencePoint=ref(13);item.SecondReferencePoint=ref(14);if(type===0)item.Rotation=value(t,50,0);item.SetDimensionLinePosition(xy(definition));}
    else if(type===3||type===4){item.ReferencePoint=ref(15);item.CenterPoint=type===3?api.Vector2.MidPoint(item.ReferencePoint,xy(definition)):xy(definition);item.DefinitionPoint=xy(definition);}
    else if(type===2){item.StartFirstLine=ref(13);item.EndFirstLine=ref(14);item.StartSecondLine=ref(15);item.EndSecondLine=xy(definition);if(api.Vector2.AreParallel(api.Vector2.Subtract(item.EndFirstLine,item.StartFirstLine),api.Vector2.Subtract(item.EndSecondLine,item.StartSecondLine)))return null;item.SetDimensionLinePosition(point(t,16,2));}
    else if(type===5){item.StartPoint=ref(13);item.EndPoint=ref(14);item.CenterPoint=ref(15);item.SetDimensionLinePosition(xy(definition));}
    else if(type===6){item.Origin=xy(definition);item.Rotation=360-value(t,51,0);item.Axis=flags&64?api.OrdinateDimensionAxis.X:api.OrdinateDimensionAxis.Y;item.FeaturePoint=ref(13);item.LeaderEndPoint=ref(14);item.DefinitionPoint=xy(definition);}
    else{item.CenterPoint=ref(15);const a=ref(13),b=ref(14);item.Radius=api.Vector2.Distance(item.CenterPoint,a);item.StartAngle=api.Vector2.Angle(api.Vector2.Subtract(a,item.CenterPoint))*api.MathHelper.RadToDeg;item.EndAngle=api.Vector2.Angle(api.Vector2.Subtract(b,item.CenterPoint))*api.MathHelper.RadToDeg;item.SetDimensionLinePosition(xy(definition));}
    item.Style=this.Resource('DimensionStyles',decoded(t,3,'Standard'));const block=decoded(t,2);if(block)item.Block=this.doc.Blocks.get_Item(block);item.TextReferencePoint=point(t,11,2);item.TextPositionManuallySet=!!(flags&128);item.TextRotation=value(t,53,0);item.AttachmentPoint=value(t,71,5);item.LineSpacingStyle=value(t,72,1);const spacing=value(t,41,1);item.LineSpacingFactor=spacing>=.25&&spacing<=4?spacing:1;item.UserText=decoded(t,1);readXData(item,r.Tags,this.doc);this.ReadDimensionOverrides(item);return item;
  }
  ReadDimensionOverrides(item){if(!item.XData.ContainsAppId('ACAD'))return;const sections=dstyleSections(item.XData.get_Item('ACAD').XDataRecord),fields=new Map();for(const s of sections){let depth=0;for(let i=0;i<s.Payload.length;i++){const tag=s.Payload[i];if(tag.Code===1002){if(tag.Value==='{')depth++;else depth=Math.max(0,depth-1);}else if(depth===0&&tag.Code===1070&&i+1<s.Payload.length)fields.set(tag.Value,s.Payload[++i].Value);}}
    const add=(name,v)=>{if(item instanceof api.Tolerance){if(name==='TextHeight')item.TextHeight=v;return;}const type=api.DimensionStyleOverrideType[name];if(type===undefined)throw new InvalidDataException('Unknown typed style override '+name);const entry=new api.DimensionStyleOverride(type,v);if(item.StyleOverrides.ContainsType(type))item.StyleOverrides.set_Item(type,entry);else item.StyleOverrides.Add(entry);};
    for(const[id,name,kind,table]of dimensionOverrideFields){if(!fields.has(id))continue;let v=fields.get(id);if(kind==='handle'){if(v==='0'&&table==='Blocks')v=null;else{v=table==='Blocks'?this.blockByRecordHandle.get(canonicalHandle(v)):this.doc.GetObjectByHandle(v);if(v==null)throw new InvalidDataException('Unresolved DSTYLE resource '+id);}}else if(kind==='color')v=api.AciColor.FromCadIndex(v);else if(kind==='bool')v=name==='FitDimLineInside'?!v:!!v;else if(kind==='char')v=String.fromCharCode(v);add(name,v);}
    if(fields.has(69))add('TextFillColor',fields.get(69)===2?api.AciColor.FromCadIndex(fields.get(70)??0):null);
    for(const[id,prefix,names]of zeroSuppressionGroups)if(fields.has(id)){const state={};applySuppression(state,fields.get(id));for(const n of names)add(prefix+n,state[n]);}
    if(fields.has(79)){add('SuppressAngularLeadingZeros',!!(fields.get(79)&1));add('SuppressAngularTrailingZeros',!!(fields.get(79)&2));}
    for(const[id,prefix,separator]of[[3,'Dim','<>'],[4,'AltUnits','[]']])if(fields.has(id)){const text=fields.get(id),at=text.indexOf(separator);add(prefix+'Prefix',at<0?'':text.slice(0,at));add(prefix+'Suffix',at<0?text:text.slice(at+2));}
    if(fields.has(273)){const units=fields.get(273);add('AltUnitsLengthUnits',units===6?4:units===7?5:units===8?6:units);add('AltUnitsStackedUnits',units===4||units===5);}
    if(fields.has(71)||fields.has(72)){const upper=fields.get(47)??item.Style.Tolerances.UpperLimit,lower=fields.get(48)??item.Style.Tolerances.LowerLimit;add('TolerancesDisplayMethod',fields.get(72)?3:fields.get(71)?(upper===lower?1:2):0);}
  }
  ReadObjects(){for(const r of this.Records('OBJECTS')){if(legacyObjects.has(r.Name))continue;this.chunk=this.Context.Chunk=new DocumentTagReader(this.tags,r.Start,this.identities);if(r.Name==='DICTIONARY'||r.Name==='ACDBDICTIONARYWDFLT'){const legacy=ReadDictionaryDatabaseRecord(this.Context);this.Context.dictionaries.Add(legacy.Handle,legacy);if(r===this.rootRecord)this.Context.namedDictionary=legacy;}else ReadDatabaseRecord(this.Context);}}
  ReadLayout(r){const t=subclass(r.Tags,'AcDbLayout'),name=decoded(t,1,this.dictionaryNames.get(r.Envelope.Handle)??'Layout1'),block=this.blockByRecordHandle.get(canonicalHandle(value(t,330,'')))??this.doc.Blocks.get_Item(name.toUpperCase()==='MODEL'?api.Block.DefaultModelSpaceName:api.Block.DefaultPaperSpaceName);
    if(!block)throw new InvalidDataException('LAYOUT has no associated BLOCK_RECORD.');const plot=this.ReadPlotSettings(r),layout=api.Layout.CreateOverload('string,netDxf.Blocks.Block,netDxf.Objects.PlotSettings',name,block,plot);const order=value(t,71,0);if(order>0)layout.TabOrder=order;
    for(const[code,key,dim]of[[10,'MinLimit',2],[11,'MaxLimit',2],[12,'BasePoint',3],[14,'MinExtents',3],[15,'MaxExtents',3],[13,'UcsOrigin',3],[16,'UcsXAxis',3],[17,'UcsYAxis',3]])if(value(t,code)!==null)layout[key]=point(t,code,dim);layout.Elevation=value(t,146,0);this.Track(layout,r);this.doc.Layouts.Add(layout,false);return layout;}
  ReadPlotSettings(r){const t=subclass(r.Tags,'AcDbPlotSettings');if(!t.length)return new api.PlotSettings();const handle={},plot=io.ParsePlotSettings(this.Context,[new DxfTag(100,'AcDbPlotSettings'),...t],handle);this.Context.outputShadeReferences.push([plot,handle.value]);return plot;}
  ReadRasterVariables(r){const t=subclass(r.Tags,'AcDbRasterVariables'),item=new api.RasterVariables(this.doc);item.DisplayFrame=value(t,70,1)!==0;item.DisplayQuality=value(t,71,1);item.Units=value(t,72,0);this.Track(item,r);this.doc.RasterVariables=item;return item;}
  ReadMLineStyle(r){const t=subclass(r.Tags,'AcDbMlineStyle'),name=decoded(t,2,this.dictionaryNames.get(r.Envelope.Handle)??'Standard'),elements=[];for(let i=0;i<t.length;i++)if(t[i].Code===49){let end=i+1;while(end<t.length&&t[end].Code!==49)end++;const s=t.slice(i,end);elements.push(new api.MLineStyleElement(t[i].Value,api.AciColor.FromCadIndex(value(s,62,256)),this.Resource('Linetypes',decoded(s,6,'ByLayer'))));}
    const item=new api.MLineStyle(name,elements);item.Description=decoded(t,3);item.Flags=value(t,70,0);item.FillColor=api.AciColor.FromCadIndex(value(t,62,256));item.StartAngle=value(t,51,90);item.EndAngle=value(t,52,90);this.Track(item,r);this.doc.MlineStyles.Add(item,false);return item;}
  ReadGroup(r){const t=subclass(r.Tags,'AcDbGroup'),name=this.dictionaryNames.get(r.Envelope.Handle)??('*A'+this.doc.GroupNamesIndex++),item=new api.Group(name);item.Description=decoded(t,300);item.IsSelectable=value(t,71,1)!==0;this.Track(item,r);this.deferred.push(()=>{for(const tag of t)if(tag.Code===340){const target=this.doc.GetObjectByHandle(tag.Value);if(!target)throw new InvalidDataException('Unresolved GROUP entity '+tag.Value);item.Entities.Add(target);}this.doc.Groups.Add(item,false);});return item;}
  ReadUnderlayDefinition(r){const t=subclass(r.Tags,'AcDbUnderlayDefinition'),name=this.dictionaryNames.get(r.Envelope.Handle)??PathFileNameWithoutExtension(decoded(t,1)),Type={DGNDEFINITION:'UnderlayDgnDefinition',DWFDEFINITION:'UnderlayDwfDefinition',PDFDEFINITION:'UnderlayPdfDefinition'}[r.Name],property={DGNDEFINITION:'UnderlayDgnDefinitions',DWFDEFINITION:'UnderlayDwfDefinitions',PDFDEFINITION:'UnderlayPdfDefinitions'}[r.Name],item=new api[Type](name,decoded(t,1));if(r.Name==='DGNDEFINITION')item.Layout=decoded(t,2,'Model');else if(r.Name==='PDFDEFINITION')item.Page=decoded(t,2,'1');this.Track(item,r);this.doc[property].Add(item,false);return item;}
  ReadImageDefinition(r){const t=subclass(r.Tags,'AcDbRasterImageDef'),name=this.dictionaryNames.get(r.Envelope.Handle)??PathFileNameWithoutExtension(decoded(t,1)),factor=api.UnitHelper.ConversionFactor(value(t,281,0),api.DrawingUnits.Millimeters),item=new api.ImageDefinition(name,decoded(t,1),value(t,10,1),factor/value(t,11,1),value(t,20,1),factor/value(t,21,1),value(t,281,0));this.Track(item,r);this.doc.ImageDefinitions.Add(item,false);return item;}
  ReadImageDefinitionReactor(r){if(r.Envelope.Handle)this.Context.managedReactorHandles.add(r.Envelope.Handle);}
}
function DxfAssign(item,seed){return api.DxfObject.prototype.AssignHandle.call(item,seed);}
