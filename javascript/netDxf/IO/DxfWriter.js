// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
// Native JavaScript whole-document writer. Existing partial codecs retain their original paths.
import * as api from '../../index.js';
import * as io from '../../runtime/DxfTransport.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { Copy, DotNetMath } from '../../runtime/GeometryRuntime.js';
import { TextCodeValueWriter } from './TextCodeValueWriter.js';
import { BinaryCodeValueWriter } from './BinaryCodeValueWriter.js';
import { DxfVersionNotSupportedException } from './DxfVersionNotSupportedException.js';
import { DxfVersionStringValues } from '../Header/DxfVersion.js';
import { DictionaryObject } from '../Objects/DictionaryObject.js';
import { Vertex } from '../Entities/Vertex.js';
import { EndSequence } from '../Entities/EndSequence.js';
import { DxfObject } from '../DxfObject.js';
import { WriteXData, WriteXDataRecords } from '../../runtime/DxfXDataIO.js';
import { EncodeDxfText, EncodeDxfDatabaseText } from '../../runtime/DxfStringEncoding.js';
import { UnwrapHeaderNumber } from '../../runtime/HeaderBox.js';
import { HeaderDateTime, HeaderTimeSpan } from '../../runtime/HeaderTime.js';
import { HatchGradientPatternTypeStringValues } from '../Entities/HatchGradientPatternType.js';
import { Encoding } from '../../runtime/Encoding.js';
import { writePoint, tableKinds, managedDictionaries, dimensionStyleFields,
  dimensionStyleBooleans, dimensionOverrideFields,zeroSuppressionGroups,dimensionOverrideBase,dstyleSections, getProperty, suppression } from '../../runtime/TypedDocumentIO.js';
import { ArgumentException, ArgumentNullException, InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
const bodyWriters=Object.freeze({ARC:'WriteArc',CIRCLE:'WriteCircle',ELLIPSE:'WriteEllipse',LINE:'WriteLine',POINT:'WritePoint',
  RAY:'WriteRay',XLINE:'WriteXLine','3DFACE':'WriteFace3D',SOLID:'WriteSolid',TRACE:'WriteTrace',SPLINE:'WriteSpline',
  LWPOLYLINE:'WriteLwPolyline',HELIX:'WriteHelix',LIGHT:'WriteLight',OLEFRAME:'WriteOleFrame',OLE2FRAME:'WriteOle2Frame'});
const required=(value,name)=>{if(value==null)throw new ArgumentNullException(name);return value;};
const signed=value=>(value<<16)>>16;

export class DxfWriter {
  doc=null; chunk=null; isBinary=false; activeSection=''; activeTable='';
  constructor(){this.insertEndSequences=new Map();this.polylines=new Map();this.imageDefReactors=new Map();}
  EncodeNonAsciiCharacters(text){return EncodeDxfText(text,this.doc.DrawingVariables.AcadVer);}
  EncodeDatabaseString(text){return EncodeDxfDatabaseText(text,this.doc.DrawingVariables.AcadVer);}
  WriteXData(data){WriteXData(this.chunk,()=>this.doc.DrawingVariables.AcadVer,data);}
  WriteDatabaseMetadata(item,reactors=null){io.WriteDatabaseMetadata(this.chunk,this.doc,item,reactors);}
  PreflightOpaqueEntities(document,binary){this.doc=document;this.isBinary=binary;io.PreflightOpaqueEntities(document,binary);}
  Write(stream,document,binary=false) {
    this.doc=required(document,'document');this.isBinary=binary;
    if(typeof binary!=='boolean')throw new ArgumentException('A Boolean transport flag is required.','binary');
    const version=document.DrawingVariables.AcadVer;
    if(version<api.DxfVersion.AutoCad2000)throw new DxfVersionNotSupportedException('DXF file version not supported: '+version,version);
    if(!Object.hasOwn(DxfVersionStringValues,version))throw new DxfVersionNotSupportedException('Unknown DXF file version: '+version,version);
    // Validate before creating the output codec. None of these refusals truncate a caller stream.
    for(const name of ['ValidateOpaqueEntities','ValidateStoredPolylineRecords','ValidateStoredPolygonMeshRecords',
      'ValidateStoredPolyfaceMeshRecords','ValidateStoredPolyline2DRecords','ValidateStoredDimensionHeaders','ValidateTextStyleStrings',
      'ValidateAcisEntities','ValidateEntityCommonDataVersions','ValidateUcsReferences','ValidateMTextBackgroundVersions',
      'ValidateMTextColumns','ValidateLwPolylineFidelity','ValidateMeshVersions','ValidateDocumentMeshOutput','ValidatePolyfaceMeshOutput',
      'ValidateHatchSourceRelations','ValidateHatchSplineData','ValidateHatchSplineFitVersions','ValidateHelixVersions','ValidateLightVersions',
      'ValidateMultiLeaders','ValidateStoredTables','ValidateSections','ValidateHatchBoundaryPresence','ValidateOutputSettings']) {
      if(typeof io[name]!=='function')throw new InvalidOperationException('Missing writer integration: '+name);
      io[name](document,binary);
    }
    const errors=Array.from(document.Objects.Validate());if(errors.length)throw new InvalidOperationException('Invalid OBJECTS database: '+errors.join('; '));
    io.ValidateDatabaseTransport(document,binary);
    const definitions=io.PrepareClassDefinitions(document);io.ValidateOpaqueEntityClasses(document,definitions,binary);
    if(document.Layouts.Count===1)document.Layouts.Add(new api.Layout('Layout1'));
    this.PreprocessEntities();
    for(const name of ['AcCmTransparency','AcAecLayerStandard','GradientColor1ACI','GradientColor2ACI'])document.ApplicationRegistries.Add(new api.ApplicationRegistry(name));
    const dictionaries=this.PrepareDictionaries();
    document.DrawingVariables.HandleSeed=document.NumHandles.toString(16).toUpperCase();
    required(stream,'stream');if(!stream.CanWrite||typeof stream.Write!=='function')throw new ArgumentException('A writable stream is required.','stream');
    this.chunk=binary?new BinaryCodeValueWriter(stream,false,Encoding.UTF8):new TextCodeValueWriter(stream,Encoding.UTF8);
    const write=this.chunk.Write.bind(this.chunk);this.chunk.Write=(code,value)=>{if(value===undefined)throw new InvalidOperationException('Uninitialized output value for group '+code);return write(code,value);};
    this.activeSection='';this.activeTable='';
    if(!binary)for(const comment of document.Comments)this.WriteComment(comment);
    this.BeginSection('HEADER');
    for(const variable of document.DrawingVariables.KnownValues())this.WriteSystemVariable(variable);
    const ucs=document.DrawingVariables.CurrentUCS;
    for(const [name,p] of [['$UCSORG',ucs.Origin],['$UCSXDIR',ucs.XAxis],['$UCSYDIR',ucs.YAxis]]){this.chunk.Write(9,name);writePoint(this.chunk,10,p);}
    const active=document.DimensionStyles.get_Item(document.DrawingVariables.DimStyle);
    if(active){this.WriteActiveDimensionStyleSystemVariables(active);io.WriteStoredDimensionHeaders(this.chunk,document,active);}
    for(const variable of document.DrawingVariables.CustomValues()) {
      if(variable.Name.toUpperCase()==='$ACADMAINTVER'||active&&io.IsStoredDimensionHeader(variable.Name))continue;
      this.WriteSystemVariable(variable,true);
    }
    this.EndSection();
    this.BeginSection('CLASSES');for(const definition of definitions)io.WriteClassDefinition(this.chunk,document,definition);this.EndSection();
    this.BeginSection('TABLES');
    for(const [code,property] of tableKinds){
      const table=document[property],records=code==='VPORT'?table.Records:table.Items;
      this.BeginTable(code,table.Handle,Math.min(records.Count??table.Count,32767),table.XData);
      for(const item of records)this.WriteTableRecord(code,item);
      if(code==='STYLE')for(const item of document.ShapeStyles)this.WriteShapeStyle(item);
      this.EndTable();
    }
    this.EndSection();
    this.BeginSection('BLOCKS');for(const block of document.Blocks)this.WriteBlock(block);this.EndSection();
    this.BeginSection('ENTITIES');
    for(const layout of document.Layouts){
      if(layout.IsPaperSpace&&layout.AssociatedBlock.Name!=='*Paper_Space')continue;
      if(layout.IsPaperSpace&&layout.Viewport)this.WriteEntity(layout.Viewport,layout);
      for(const definition of layout.AssociatedBlock.AttributeDefinitions.Values)this.WriteAttributeDefinition(definition,layout);
      for(const entity of layout.AssociatedBlock.Entities)this.WriteEntity(entity,layout);
    }
    this.EndSection();
    this.BeginSection('OBJECTS');
    for(const dictionary of dictionaries){if(dictionary===dictionaries[0])io.WriteDatabaseObject(this.chunk,document,document.NamedObjects,dictionary);else this.WriteDictionary(dictionary);}
    for(const object of document.Objects.Items)if(object!==document.NamedObjects)io.WriteDatabaseObject(this.chunk,document,object);
    for(const group of document.Groups)this.WriteGroup(group,document.Groups.Handle);
    for(const layout of document.Layouts)this.WriteLayout(layout,document.Layouts.Handle);
    for(const style of document.MlineStyles)this.WriteMLineStyle(style,document.MlineStyles.Handle);
    for(const property of ['UnderlayDgnDefinitions','UnderlayDwfDefinitions','UnderlayPdfDefinitions'])for(const definition of document[property])this.WriteUnderlayDefinition(definition,document[property].Handle);
    this.WriteRasterVariables(document.RasterVariables,document.NamedObjects.Handle);
    for(const definition of document.ImageDefinitions){for(const reactor of this.imageDefReactors.get(definition.Handle)?.values()??[])this.WriteImageDefReactor(reactor);this.WriteImageDef(definition,document.ImageDefinitions.Handle);}
    for(const state of document.Layers.StateManager.Items)this.WriteLayerState(state,dictionaries.at(-1).Handle);
    this.EndSection();io.DxfThumbnailImage.Write(this.chunk,document.ThumbnailImage);this.Close();
  }
  BeginSection(name){if(this.activeSection)throw new InvalidOperationException('A DXF section is already open.');this.activeSection=name;this.chunk.Write(0,'SECTION');this.chunk.Write(2,name);}
  EndSection(){if(this.activeTable)throw new InvalidOperationException('A table remains open.');this.chunk.Write(0,'ENDSEC');this.activeSection='';}
  BeginTable(name,handle,count,data){if(this.activeTable)throw new InvalidOperationException('A DXF table is already open.');this.activeTable=name;const c=this.chunk;c.Write(0,'TABLE');c.Write(2,name);c.Write(5,handle);
    const table=this.doc.GetObjectByHandle(handle);if(table)this.WriteDatabaseMetadata(table);
    if(name==='LAYER'){c.Write(102,'{ACAD_XDICTIONARY');c.Write(360,this.doc.Layers.StateManager.Handle);c.Write(102,'}');}
    c.Write(330,'0');c.Write(100,'AcDbSymbolTable');c.Write(70,count);this.WriteXData(data);}
  EndTable(){this.chunk.Write(0,'ENDTAB');this.activeTable='';}
  Close(){this.chunk.Write(0,'EOF');this.chunk.Flush();}
  WriteComment(comment){this.chunk.Write(999,comment);}
  WriteSystemVariable(variable,custom=false){
    const c=this.chunk,name=variable.Name;let v=UnwrapHeaderNumber(variable.Value),code=variable.GroupCode;
    if(name==='$LASTSAVEDBY'&&this.doc.DrawingVariables.AcadVer<=13)return;
    c.Write(9,name);
    if(name==='$ACADVER')v=DxfVersionStringValues[v];
    else if(v instanceof api.AciColor)v=v.Index;
    else if(v instanceof HeaderDateTime)v=api.DrawingTime.ToJulianCalendar(v);
    else if(v instanceof HeaderTimeSpan)v=Number(v.Ticks)/864000000000;
    else if(v instanceof api.Vector3){writePoint(c,custom&&code===30?10:code,v);return;}
    else if(custom&&code===20&&v instanceof api.Vector2){writePoint(c,10,v,2);return;}
    else if(typeof v==='boolean'&&code<290)v=v?1:0;
    else if(typeof v==='string'&&!['$HANDSEED','$DWGCODEPAGE'].includes(name))v=this.EncodeNonAsciiCharacters(v);
    c.Write(code,v);
  }
  WriteActiveDimensionStyleSystemVariables(style){
    // Evaluate each property after its group-9 callback, as the C# writer does.
    const emit=(name,code,read)=>{this.chunk.Write(9,'$'+name);this.chunk.Write(code,read());};
    const field=(name,code,property)=>emit(name,code,()=>{const v=getProperty(style,property);return typeof v==='boolean'?(v?1:0):v;});
    const zeros=(settings,prefix='')=>suppression(settings[prefix+'SuppressLinearLeadingZeros'],settings[prefix+'SuppressLinearTrailingZeros'],settings[prefix+'SuppressZeroFeet'],settings[prefix+'SuppressZeroInches']);
    field('DIMADEC',70,'AngularPrecision');field('DIMALT',70,'AlternateUnits.Enabled');
    field('DIMALTD',70,'AlternateUnits.LengthPrecision');field('DIMALTF',40,'AlternateUnits.Multiplier');
    field('DIMALTRND',40,'AlternateUnits.Roundoff');field('DIMALTTD',70,'Tolerances.AlternatePrecision');
    emit('DIMALTTZ',70,()=>zeros(style.Tolerances,'Alternate'));
    this.chunk.Write(9,'$DIMALTU');
    switch(style.AlternateUnits.LengthUnits){
      case api.LinearUnitType.Scientific:this.chunk.Write(70,1);break;
      case api.LinearUnitType.Decimal:this.chunk.Write(70,2);break;
      case api.LinearUnitType.Engineering:this.chunk.Write(70,3);break;
      case api.LinearUnitType.Architectural:this.chunk.Write(70,style.AlternateUnits.StackUnits?4:6);break;
      case api.LinearUnitType.Fractional:this.chunk.Write(70,style.AlternateUnits.StackUnits?5:7);break;
    }
    emit('DIMALTZ',70,()=>zeros(style.AlternateUnits));
    // The pinned source deliberately conditions the alternate placeholder on DimPrefix.
    emit('DIMAPOST',1,()=>{const marker=style.DimPrefix==null||style.DimPrefix===''?'':'[]';return this.EncodeNonAsciiCharacters(style.AlternateUnits.Prefix+marker+style.AlternateUnits.Suffix);});
    field('DIMATFIT',70,'FitOptions');field('DIMAUNIT',70,'DimAngularUnits');field('DIMASZ',40,'ArrowSize');
    const angular=(style.SuppressAngularLeadingZeros?1:0)|(style.SuppressAngularTrailingZeros?2:0);
    emit('DIMAZIN',70,()=>angular);
    if(style.DimArrow1===null&&style.DimArrow2===null){
      emit('DIMSAH',70,()=>0);emit('DIMBLK',1,()=>'');
    }else if(style.DimArrow1===null){
      emit('DIMSAH',70,()=>1);emit('DIMBLK1',1,()=>'');emit('DIMBLK2',1,()=>this.EncodeNonAsciiCharacters(style.DimArrow2.Name));
    }else if(style.DimArrow2===null){
      emit('DIMSAH',70,()=>1);emit('DIMBLK1',1,()=>this.EncodeNonAsciiCharacters(style.DimArrow1.Name));emit('DIMBLK2',1,()=>'');
    }else if(OrdinalIgnoreCaseEquals(style.DimArrow1.Name,style.DimArrow2.Name)){
      emit('DIMSAH',70,()=>0);emit('DIMBLK',1,()=>this.EncodeNonAsciiCharacters(style.DimArrow1.Name));
    }else{
      emit('DIMSAH',70,()=>1);emit('DIMBLK1',1,()=>this.EncodeNonAsciiCharacters(style.DimArrow1.Name));emit('DIMBLK2',1,()=>this.EncodeNonAsciiCharacters(style.DimArrow2.Name));
    }
    emit('DIMLDRBLK',1,()=>style.LeaderArrow===null?'':this.EncodeNonAsciiCharacters(style.LeaderArrow.Name));
    field('DIMCEN',40,'CenterMarkSize');
    emit('DIMCLRD',70,()=>style.DimLineColor.Index);emit('DIMCLRE',70,()=>style.ExtLineColor.Index);emit('DIMCLRT',70,()=>style.TextColor.Index);
    field('DIMDEC',70,'LengthPrecision');field('DIMDLE',40,'DimLineExtend');field('DIMDLI',40,'DimBaselineSpacing');
    emit('DIMDSEP',70,()=>signed(style.DecimalSeparator.charCodeAt(0)));
    field('DIMEXE',40,'ExtLineExtend');field('DIMEXO',40,'ExtLineOffset');field('DIMFXLON',70,'ExtLineFixed');field('DIMFXL',40,'ExtLineFixedLength');
    field('DIMGAP',40,'TextOffset');field('DIMJUST',70,'TextHorizontalPlacement');field('DIMLFAC',40,'DimScaleLinear');
    field('DIMLUNIT',70,'DimLengthUnits');field('DIMLWD',70,'DimLineLineweight');field('DIMLWE',70,'ExtLineLineweight');
    emit('DIMPOST',1,()=>{const marker=style.DimPrefix==null||style.DimPrefix===''?'':'<>';return this.EncodeNonAsciiCharacters(style.DimPrefix+marker+style.DimSuffix);});
    field('DIMRND',40,'DimRoundoff');field('DIMSCALE',40,'DimScaleOverall');
    field('DIMSD1',70,'DimLine1Off');field('DIMSD2',70,'DimLine2Off');field('DIMSE1',70,'ExtLine1Off');field('DIMSE2',70,'ExtLine2Off');
    emit('DIMSOXD',70,()=>style.FitDimLineInside?0:1);
    field('DIMTAD',70,'TextVerticalPlacement');field('DIMTDEC',70,'Tolerances.Precision');field('DIMTFAC',40,'TextFractionHeightScale');
    if(style.TextFillColor!==null){emit('DIMTFILL',70,()=>2);emit('DIMTFILLCLR',70,()=>style.TextFillColor.Index);}
    field('DIMTIH',70,'TextInsideAlign');field('DIMTIX',70,'FitTextInside');
    if(style.Tolerances.DisplayMethod===api.DimensionStyleTolerancesDisplayMethod.Deviation)
      emit('DIMTM',40,()=>api.MathHelper.IsZero(style.Tolerances.LowerLimit)?api.MathHelper.Epsilon:style.Tolerances.LowerLimit);
    else field('DIMTM',40,'Tolerances.LowerLimit');
    field('DIMTMOVE',70,'FitTextMove');field('DIMTOFL',70,'FitDimLineForce');field('DIMTOH',70,'TextOutsideAlign');
    const mode=style.Tolerances.DisplayMethod,T=api.DimensionStyleTolerancesDisplayMethod;
    switch(mode){
      case T.None:emit('DIMTOL',70,()=>0);emit('DIMLIM',70,()=>0);break;
      case T.Symmetrical:case T.Deviation:emit('DIMTOL',70,()=>1);emit('DIMLIM',70,()=>0);break;
      case T.Limits:emit('DIMTOL',70,()=>0);emit('DIMLIM',70,()=>1);break;
    }
    field('DIMTOLJ',70,'Tolerances.VerticalPlacement');field('DIMTP',40,'Tolerances.UpperLimit');field('DIMTXT',40,'TextHeight');field('DIMTXTDIRECTION',70,'TextDirection');
    emit('DIMTZIN',70,()=>zeros(style.Tolerances));emit('DIMZIN',70,()=>zeros(style));
    field('DIMFRAC',70,'FractionType');
    emit('DIMLTYPE',6,()=>this.EncodeNonAsciiCharacters(style.DimLineLinetype.Name));
    emit('DIMLTEX1',6,()=>this.EncodeNonAsciiCharacters(style.ExtLine1Linetype.Name));
    emit('DIMLTEX2',6,()=>this.EncodeNonAsciiCharacters(style.ExtLine2Linetype.Name));
  }
  TableEnvelope(item,subclass,handleCode=5){const c=this.chunk;c.Write(0,item.CodeName);c.Write(handleCode,item.Handle);this.WriteDatabaseMetadata(item);c.Write(330,item.Owner.Handle);c.Write(100,'AcDbSymbolTableRecord');c.Write(100,subclass);}
  WriteTableRecord(code,item){switch(code){case'APPID':return this.WriteApplicationRegistry(item);case'VPORT':return io.WriteVPort(this.chunk,this.doc,item);case'LTYPE':return this.WriteLinetype(item);case'LAYER':return this.WriteLayer(item);case'STYLE':return this.WriteTextStyle(item);case'DIMSTYLE':return this.WriteDimensionStyle(item);case'VIEW':return io.WriteView(this.chunk,this.doc,item);case'UCS':return this.WriteUCS(item);case'BLOCK_RECORD':return this.WriteBlockRecord(item.Record);default:throw new NotSupportedException('Unknown table: '+code);}}
  WriteApplicationRegistry(item){this.TableEnvelope(item,'AcDbRegAppTableRecord');this.chunk.Write(2,this.EncodeNonAsciiCharacters(item.Name));this.chunk.Write(70,0);this.WriteXData(item.XData);}
  WriteLayer(layer){const c=this.chunk;this.TableEnvelope(layer,'AcDbLayerTableRecord');c.Write(2,this.EncodeNonAsciiCharacters(layer.Name));c.Write(70,(layer.IsFrozen?1:0)|(layer.IsLocked?4:0));c.Write(62,layer.IsVisible?layer.Color.Index:-layer.Color.Index);if(layer.Color.UseTrueColor)c.Write(420,api.AciColor.ToTrueColor(layer.Color));c.Write(6,this.EncodeNonAsciiCharacters(layer.Linetype.Name));c.Write(290,layer.Plot);c.Write(370,layer.Lineweight);c.Write(390,'0');
    for(const app of layer.XData.AppIds)if(!['AcCmTransparency','AcAecLayerStandard'].includes(app))WriteXDataRecords(c,()=>this.doc.DrawingVariables.AcadVer,app,layer.XData.get_Item(app).XDataRecord);
    WriteXDataRecords(c,()=>this.doc.DrawingVariables.AcadVer,'AcCmTransparency',[new api.XDataRecord(1071,api.Transparency.ToAlphaValue(layer.Transparency))]);
    WriteXDataRecords(c,()=>this.doc.DrawingVariables.AcadVer,'AcAecLayerStandard',[new api.XDataRecord(1000,''),new api.XDataRecord(1000,layer.Description??'')]);}
  WriteLinetype(line){const c=this.chunk;this.TableEnvelope(line,'AcDbLinetypeTableRecord');c.Write(2,this.EncodeNonAsciiCharacters(line.Name));c.Write(70,0);c.Write(3,this.EncodeNonAsciiCharacters(line.Description));c.Write(72,65);c.Write(73,line.Segments.Count);c.Write(40,line.Length());
    for(const segment of line.Segments){c.Write(49,segment.Length);const complex=segment instanceof api.LinetypeTextSegment||segment instanceof api.LinetypeShapeSegment;
      if(!complex){c.Write(74,0);continue;}const text=segment instanceof api.LinetypeTextSegment;c.Write(74,(text?2:4)|(segment.RotationType===api.LinetypeSegmentRotationType.Absolute?1:0));c.Write(75,text?0:segment.Style.ShapeNumber(segment.Name));c.Write(340,segment.Style.Handle);c.Write(46,segment.Scale);c.Write(50,segment.Rotation);c.Write(44,segment.Offset.X);c.Write(45,segment.Offset.Y);if(text)c.Write(9,this.EncodeNonAsciiCharacters(segment.Text));}
    this.WriteXData(line.XData);}
  WriteTextStyle(style){const c=this.chunk;this.TableEnvelope(style,'AcDbTextStyleTableRecord');c.Write(2,io.EncodeStyleString(style.Name,this.doc.DrawingVariables.AcadVer));c.Write(3,io.EncodeStyleString(style.FontFile,this.doc.DrawingVariables.AcadVer));if(style.BigFont)c.Write(4,io.EncodeStyleString(style.BigFont,this.doc.DrawingVariables.AcadVer));c.Write(70,style.Flags);c.Write(71,style.TextGenerationFlags);c.Write(40,style.Height);c.Write(41,style.WidthFactor);if(style.LastHeight!==null)c.Write(42,style.LastHeight);c.Write(50,style.ObliqueAngle);io.WriteStyleXData(c,this.doc.DrawingVariables.AcadVer,style.XData);}
  WriteShapeStyle(style){const c=this.chunk;this.TableEnvelope(style,'AcDbTextStyleTableRecord');c.Write(2,'');c.Write(3,io.EncodeStyleString(style.File,this.doc.DrawingVariables.AcadVer));c.Write(70,style.Flags);c.Write(71,style.TextGenerationFlags);c.Write(40,style.Size);c.Write(41,style.WidthFactor);if(style.LastHeight!==null)c.Write(42,style.LastHeight);c.Write(50,style.ObliqueAngle);io.WriteStyleXData(c,this.doc.DrawingVariables.AcadVer,style.XData);}
  WriteDimensionStyle(style){const c=this.chunk;this.TableEnvelope(style,'AcDbDimStyleTableRecord',105);c.Write(2,this.EncodeNonAsciiCharacters(style.Name));c.Write(70,0);c.Write(3,this.EncodeNonAsciiCharacters(style.DimPrefix+(style.DimPrefix?'<>':'')+style.DimSuffix));c.Write(4,this.EncodeNonAsciiCharacters(style.AlternateUnits.Prefix+(style.AlternateUnits.Prefix?'[]':'')+style.AlternateUnits.Suffix));
    for(const [code,property,table]of dimensionStyleFields){if(table){const item=getProperty(style,property);if(item)c.Write(code,table==='Blocks'?item.Record.Handle:item.Handle);continue;}let v=getProperty(style,property);if(property==='DecimalSeparator'&&typeof v==='string')v=v.charCodeAt(0);c.Write(code,v);}
    for(const [code,property]of dimensionStyleBooleans){const v=getProperty(style,property);c.Write(code,code===294?v===api.DimensionStyleTextDirection.RightToLeft:code===290?v:code===175?(v?0:1):(v?1:0));}
    c.Write(71,[1,2].includes(style.Tolerances.DisplayMethod)?1:0);c.Write(72,style.Tolerances.DisplayMethod===3?1:0);
    for(const [code,property]of [[176,'DimLineColor'],[177,'ExtLineColor'],[178,'TextColor']])c.Write(code,style[property].Index);
    if(style.TextFillColor){c.Write(69,2);c.Write(70,style.TextFillColor.Index);}
    for(const [code,part,prefix]of [[78,style,''],[284,style.Tolerances,''],[285,style.AlternateUnits,''],[286,style.Tolerances,'Alternate']])c.Write(code,suppression(part[prefix+'SuppressLinearLeadingZeros'],part[prefix+'SuppressLinearTrailingZeros'],part[prefix+'SuppressZeroFeet'],part[prefix+'SuppressZeroInches']));
    c.Write(79,(style.SuppressAngularLeadingZeros?1:0)|(style.SuppressAngularTrailingZeros?2:0));
    const units=style.AlternateUnits.LengthUnits;c.Write(273,units===4?(style.AlternateUnits.StackUnits?4:6):units===5?(style.AlternateUnits.StackUnits?5:7):units===6?8:units);
    c.Write(173,style.DimArrow1||style.DimArrow2?1:0);this.WriteXData(style.XData);}
  WriteUCS(ucs){const c=this.chunk;this.TableEnvelope(ucs,'AcDbUCSTableRecord');c.Write(2,this.EncodeNonAsciiCharacters(ucs.Name));c.Write(70,ucs.Flags);writePoint(c,10,ucs.Origin);writePoint(c,11,ucs.XAxis);writePoint(c,12,ucs.YAxis);c.Write(79,ucs.OrthographicViewType);c.Write(146,ucs.Elevation);if(ucs.BaseUcsHandlePresent)c.Write(346,ucs.BaseUcs?.Handle??'0');for(let n=1;n<=6;n++){const p={};if(ucs.TryGetOrthographicOrigin(n,p)){c.Write(71,n);writePoint(c,13,p.value);}}this.WriteXData(ucs.XData);}
  WriteBlockRecord(record){const c=this.chunk;this.TableEnvelope(record,'AcDbBlockTableRecord');c.Write(2,this.EncodeNonAsciiCharacters(record.Name));c.Write(340,record.Layout?.Handle??'0');if(this.doc.DrawingVariables.AcadVer>=15){c.Write(70,record.Units);c.Write(280,record.AllowExploding?1:0);c.Write(281,record.ScaleUniformly?1:0);}this.WriteXData(record.XData);}
  WriteBlock(block){const c=this.chunk,layout=block.Record.Layout;c.Write(0,'BLOCK');c.Write(5,block.Handle);this.WriteDatabaseMetadata(block);c.Write(330,block.Record.Handle);c.Write(100,'AcDbEntity');if(layout)c.Write(67,layout.IsPaperSpace?1:0);c.Write(8,this.EncodeNonAsciiCharacters(block.Layer.Name));c.Write(100,'AcDbBlockBegin');c.Write(2,this.EncodeNonAsciiCharacters(block.Name));c.Write(70,block.Flags);writePoint(c,10,block.Origin);c.Write(3,this.EncodeNonAsciiCharacters(block.Name));c.Write(1,this.EncodeNonAsciiCharacters(block.XrefFile));if(block.Description)c.Write(4,this.EncodeNonAsciiCharacters(block.Description));this.WriteXData(block.XData);
    if(!layout||layout.IsPaperSpace&&block.Name!=='*Paper_Space'){if(layout?.Viewport)this.WriteEntity(layout.Viewport,layout);for(const a of block.AttributeDefinitions.Values)this.WriteAttributeDefinition(a,layout);for(const entity of block.Entities)this.WriteEntity(entity,layout);}
    c.Write(0,'ENDBLK');c.Write(5,block.End.Handle);this.WriteDatabaseMetadata(block.End);c.Write(330,block.Record.Handle);c.Write(100,'AcDbEntity');if(layout)c.Write(67,layout.IsPaperSpace?1:0);c.Write(8,this.EncodeNonAsciiCharacters(block.Layer.Name));c.Write(100,'AcDbBlockEnd');this.WriteXData(block.End.XData);}
  WriteEntityCommonCodes(entity,layout=null,codeName=entity.CodeName,owner=null){const c=this.chunk;c.Write(0,codeName);c.Write(5,entity.Handle);this.WriteDatabaseMetadata(entity);c.Write(330,owner??entity.Owner.Record.Handle);c.Write(100,'AcDbEntity');if(layout)c.Write(67,layout.IsPaperSpace?1:0);c.Write(8,this.EncodeNonAsciiCharacters(entity.Layer.Name));c.Write(62,entity.Color.Index);if(entity.Color.UseTrueColor)c.Write(420,api.AciColor.ToTrueColor(entity.Color));if(entity.Transparency.Value>=0)c.Write(440,api.Transparency.ToAlphaValue(entity.Transparency));c.Write(6,this.EncodeNonAsciiCharacters(entity.Linetype.Name));c.Write(370,entity.Lineweight);c.Write(48,entity.LinetypeScale);c.Write(60,entity.IsVisible?0:1);io.WriteEntityCommonData(c,this.doc.DrawingVariables.AcadVer,entity.CommonData);}
  WriteEntity(entity,layout=null){if(entity instanceof api.DxfOpaqueEntity){io.WriteOpaqueEntity(this.chunk,this.doc,entity);return;}
    if(entity instanceof api.Polyline3D||entity instanceof api.PolygonMesh||entity instanceof api.PolyfaceMesh||entity instanceof api.Polyline2D&&(entity.SmoothType!==0||entity.HasStoredRecords))return this.WriteLegacyPolyline(entity,layout);
    this.WriteEntityCommonCodes(entity,layout);const code=entity.CodeName;
    if(Object.hasOwn(bodyWriters,code))return io[bodyWriters[code]](this.chunk,this.doc.DrawingVariables.AcadVer,entity);
    if(entity instanceof api.AcisEntity)return io.WriteAcisEntity(this.chunk,this.doc.DrawingVariables.AcadVer,entity);
    if(entity instanceof api.MultiLeader)return io.WriteMultiLeader(this.chunk,this.doc,entity);
    if(entity instanceof api.Section)return io.WriteSection(this.chunk,this.doc,entity);
    if(entity instanceof api.StoredTable)return io.WriteStoredTable(this.chunk,this.doc,entity);
    const name={TEXT:'WriteText',MTEXT:'WriteMText',INSERT:'WriteInsert',MESH:'WriteMesh',VIEWPORT:'WriteViewport',HATCH:'WriteHatch',DIMENSION:'WriteDimension',ARC_DIMENSION:'WriteDimension',LEADER:'WriteLeader',TOLERANCE:'WriteTolerance',MLINE:'WriteMLine',IMAGE:'WriteImage',WIPEOUT:'WriteWipeout',SHAPE:'WriteShape',DGNUNDERLAY:'WriteUnderlay',DWFUNDERLAY:'WriteUnderlay',PDFUNDERLAY:'WriteUnderlay'}[code];
    if(!name||typeof this[name]!=='function')throw new NotSupportedException('Typed entity writer is not implemented for '+code+'.');this[name](entity);}

  WriteViewport(vp){const c=this.chunk;c.Write(100,'AcDbViewport');for(const [code,key,dim]of [[10,'Center',3],[12,'ViewCenter',2],[13,'SnapBase',2],[14,'SnapSpacing',2],[15,'GridSpacing',2],[16,'ViewDirection',3],[17,'ViewTarget',3],[110,'UcsOrigin',3],[111,'UcsXAxis',3],[112,'UcsYAxis',3]])writePoint(c,code,vp[key],dim);
    for(const [code,key]of [[40,'Width'],[41,'Height'],[68,'Stacking'],[69,'Id'],[42,'LensLength'],[43,'FrontClipPlane'],[44,'BackClipPlane'],[45,'ViewHeight'],[50,'SnapAngle'],[51,'TwistAngle'],[72,'CircleZoomPercent'],[90,'Status']])c.Write(code,vp[key]);
    for(const layer of vp.FrozenLayers)c.Write(331,layer.Handle);if(vp.ClippingBoundary)c.Write(340,vp.ClippingBoundary.Handle);if(vp.SunHandlePresent||vp.Sun)c.Write(361,vp.Sun?.Handle??'0');this.WriteXData(vp.XData);}
  WriteTextBody(text,subclass='AcDbText',verticalCode=73){const c=this.chunk;c.Write(100,'AcDbText');c.Write(1,this.EncodeNonAsciiCharacters(text.Value));
    const p=api.MathHelper.Transform(text.Position,text.Normal,api.CoordinateSystem.World,api.CoordinateSystem.Object);writePoint(c,10,p);c.Write(40,text.Height);c.Write(41,text.WidthFactor);c.Write(50,text.Rotation);c.Write(51,text.ObliqueAngle);c.Write(7,this.EncodeNonAsciiCharacters(text.Style.Name));
    let end=p;if(text.Alignment===api.TextAlignment.Fit||text.Alignment===api.TextAlignment.Aligned){const v=api.Vector2.Rotate(new api.Vector2(text.Width,0),text.Rotation*api.MathHelper.DegToRad);end=api.Vector3.Add(p,new api.Vector3(v.X,v.Y,0));}writePoint(c,11,end);writePoint(c,210,text.Normal);c.Write(71,(text.IsBackward?2:0)|(text.IsUpsideDown?4:0));
    const a=text.Alignment;let horizontal,vertical;if(a<=11){horizontal=a%3;vertical=3-Math.floor(a/3);}else{horizontal=a-9;vertical=0;}c.Write(72,horizontal);c.Write(100,subclass);c.Write(verticalCode,vertical);}
  WriteText(text){this.WriteTextBody(text);this.WriteXData(text.XData);}
  WriteAttributeDefinition(attribute,layout=null){this.WriteEntityCommonCodes(attribute,layout);this.WriteTextBody(attribute,'AcDbAttributeDefinition',74);this.chunk.Write(2,this.EncodeNonAsciiCharacters(attribute.Tag));this.chunk.Write(3,this.EncodeNonAsciiCharacters(attribute.Prompt));this.chunk.Write(70,attribute.Flags);this.WriteXData(attribute.XData);}
  WriteAttribute(attribute){this.WriteEntityCommonCodes(attribute,null,attribute.CodeName,attribute.Owner.Handle);this.WriteTextBody(attribute,'AcDbAttribute',74);this.chunk.Write(2,this.EncodeNonAsciiCharacters(attribute.Tag));this.chunk.Write(70,attribute.Flags);this.WriteXData(attribute.XData);}
  WriteMText(text){const c=this.chunk;c.Write(100,'AcDbMText');writePoint(c,10,text.Position);writePoint(c,210,text.Normal);this.WriteMTextChunks(this.EncodeNonAsciiCharacters(text.Value));c.Write(40,text.Height);c.Write(41,text.RectangleWidth);c.Write(44,text.LineSpacingFactor);
    const v=api.Vector2.Rotate(api.Vector2.UnitX,text.Rotation*api.MathHelper.DegToRad);v.Normalize();const direction=api.MathHelper.Transform(new api.Vector3(v.X,v.Y,0),text.Normal,api.CoordinateSystem.Object,api.CoordinateSystem.World);writePoint(c,11,direction);c.Write(71,text.AttachmentPoint);c.Write(72,text.DrawingDirection);c.Write(73,text.LineSpacingStyle);c.Write(7,this.EncodeNonAsciiCharacters(text.Style.Name));io.WriteMTextBackground(c,this.doc.DrawingVariables.AcadVer,text.BackgroundFill);io.WriteMTextColumnDefinition(c,this.doc,text,direction);io.WriteMTextColumnXData(c,this.doc,text);}
  WriteMTextChunks(text){let start=0;while(text.length-start>250){this.chunk.Write(3,text.slice(start,start+250));start+=250;}this.chunk.Write(1,text.slice(start));}
  WriteInsert(insert){const c=this.chunk;c.Write(100,insert.IsMultiple?'AcDbMInsertBlock':'AcDbBlockReference');c.Write(2,this.EncodeNonAsciiCharacters(insert.Block.Name));writePoint(c,10,api.MathHelper.Transform(insert.Position,insert.Normal,api.CoordinateSystem.World,api.CoordinateSystem.Object));
    const scale=api.UnitHelper.ConversionFactor(insert.Block.Record.Units,insert.Owner.Record.IsForInternalUseOnly?this.doc.DrawingVariables.InsUnits:insert.Owner.Record.Units);c.Write(41,insert.Scale.X*scale);c.Write(42,insert.Scale.Y*scale);c.Write(43,insert.Scale.Z*scale);c.Write(50,insert.Rotation);if(insert.ColumnCount!==1)c.Write(70,insert.ColumnCount);if(insert.RowCount!==1)c.Write(71,insert.RowCount);if(insert.ColumnSpacing!==0)c.Write(44,insert.ColumnSpacing);if(insert.RowSpacing!==0)c.Write(45,insert.RowSpacing);writePoint(c,210,insert.Normal);
    if(insert.Attributes.Count)c.Write(66,1);this.WriteXData(insert.XData);for(const attribute of insert.Attributes)this.WriteAttribute(attribute);if(insert.Attributes.Count){const end=this.insertEndSequences.get(insert);c.Write(0,'SEQEND');c.Write(5,end.Handle);this.WriteDatabaseMetadata(end);c.Write(100,'AcDbEntity');c.Write(8,this.EncodeNonAsciiCharacters(insert.Layer.Name));}}
  WriteMesh(mesh){const c=this.chunk;c.Write(100,'AcDbSubDMesh');c.Write(71,2);c.Write(72,mesh.BlendCrease?1:0);c.Write(91,mesh.SubdivisionLevel);c.Write(92,mesh.Vertexes.Count);for(const p of mesh.Vertexes)writePoint(c,10,p);c.Write(93,Array.from(mesh.Faces).reduce((n,face)=>n+face.length+1,0));for(const face of mesh.Faces){c.Write(90,face.length);for(const i of face)c.Write(90,i);}c.Write(94,mesh.Edges.Count);for(const edge of mesh.Edges){c.Write(90,edge.StartVertexIndex);c.Write(90,edge.EndVertexIndex);}c.Write(95,mesh.Edges.Count);for(const edge of mesh.Edges)c.Write(140,edge.Crease);c.Write(90,0);this.WriteXData(mesh.XData);}
  WriteShape(shape){const c=this.chunk;c.Write(100,'AcDbShape');c.Write(39,shape.Thickness);writePoint(c,10,api.MathHelper.Transform(shape.Position,shape.Normal,api.CoordinateSystem.World,api.CoordinateSystem.Object));c.Write(40,shape.Size);c.Write(2,shape.Name);c.Write(50,shape.Rotation);c.Write(41,shape.WidthFactor);c.Write(51,shape.ObliqueAngle);writePoint(c,210,shape.Normal);this.WriteXData(shape.XData);}

  WriteUnderlay(item){const c=this.chunk;c.Write(100,'AcDbUnderlayReference');c.Write(340,item.Definition.Handle);writePoint(c,10,api.MathHelper.Transform(item.Position,item.Normal,api.CoordinateSystem.World,api.CoordinateSystem.Object));c.Write(41,item.Scale.X);c.Write(42,item.Scale.Y);c.Write(43,1);c.Write(50,item.Rotation);writePoint(c,210,item.Normal);c.Write(280,item.DisplayOptions);c.Write(281,item.Contrast);c.Write(282,item.Fade);if(item.ClippingBoundary)for(const p of item.ClippingBoundary.Vertexes)writePoint(c,11,p,2);this.WriteXData(item.XData);}
  WriteWipeout(item){const c=this.chunk,b=new api.BoundingRectangle(item.ClippingBoundary.Vertexes),scale=Math.max(b.Width,b.Height),origin=new api.Vector3(b.Min.X,b.Min.Y,item.Elevation),world=p=>api.MathHelper.Transform(p,item.Normal,api.CoordinateSystem.Object,api.CoordinateSystem.World);c.Write(100,'AcDbWipeout');writePoint(c,10,world(origin));writePoint(c,11,world(new api.Vector3(scale,0,0)));writePoint(c,12,world(new api.Vector3(0,scale,0)));c.Write(13,1);c.Write(23,1);for(const[code,v]of[[280,1],[281,50],[282,50],[283,0],[71,item.ClippingBoundary.Type]])c.Write(code,v);const points=Array.from(item.ClippingBoundary.Vertexes);if(item.ClippingBoundary.Type===2)points.push(points[0]);c.Write(91,points.length);for(const p of points){c.Write(14,(p.X-origin.X)/scale-.5);c.Write(24,-((p.Y-origin.Y)/scale-.5));}this.WriteXData(item.XData);}
  WriteTolerance(item){const c=this.chunk;c.Write(100,'AcDbFcf');c.Write(3,this.EncodeNonAsciiCharacters(item.Style.Name));writePoint(c,10,item.Position);c.Write(1,this.EncodeNonAsciiCharacters(item.ToStringRepresentation()));writePoint(c,210,item.Normal);const angle=item.Rotation*api.MathHelper.DegToRad;writePoint(c,11,api.MathHelper.Transform(new api.Vector3(Math.cos(angle),Math.sin(angle),0),item.Normal,api.CoordinateSystem.Object,api.CoordinateSystem.World));this.WriteDimensionXData(item);}
  WriteLeader(item){const c=this.chunk;c.Write(100,'AcDbLeader');c.Write(3,item.Style.Name);c.Write(71,item.ShowArrowhead?1:0);c.Write(72,item.PathType);c.Write(73,item.Annotation===null?3:item.Annotation instanceof api.Tolerance?1:item.Annotation instanceof api.Insert?2:0);c.Write(74,0);c.Write(75,item.HasHookline?1:0);c.Write(76,item.Vertexes.Count);for(const p of item.Vertexes)writePoint(c,10,api.MathHelper.Transform(new api.Vector3(p.X,p.Y,item.Elevation),item.Normal,api.CoordinateSystem.Object,api.CoordinateSystem.World));c.Write(77,item.LineColor.Index);if(item.Annotation)c.Write(340,item.Annotation.Handle);writePoint(c,210,item.Normal);writePoint(c,211,api.Vector3.Normalize(api.MathHelper.Transform(item.Direction,item.Normal,0)));writePoint(c,213,api.MathHelper.Transform(item.Offset,item.Normal,item.Elevation));this.WriteDimensionXData(item);}
  WriteMLine(item){const c=this.chunk;c.Write(100,'AcDbMline');c.Write(2,this.EncodeNonAsciiCharacters(item.Style.Name));c.Write(340,item.Style.Handle);for(const[code,v]of[[40,item.Scale],[70,item.Justification],[71,item.Flags],[72,item.Vertexes.Count],[73,item.Style.Elements.Count]])c.Write(code,v);const world=(p,z)=>api.MathHelper.Transform(new api.Vector3(p.X,p.Y,z),item.Normal,api.CoordinateSystem.Object,api.CoordinateSystem.World);writePoint(c,10,item.Vertexes.Count?world(item.Vertexes.get_Item(0).Position,item.Elevation):api.Vector3.Zero);writePoint(c,210,item.Normal);for(const v of item.Vertexes){writePoint(c,11,world(v.Position,item.Elevation));writePoint(c,12,world(v.Direction,0));writePoint(c,13,world(v.Miter,0));for(const distances of v.Distances){c.Write(74,distances.Count??distances.length);for(const d of distances)c.Write(41,d);c.Write(75,0);}}this.WriteXData(item.XData);}
  WriteImage(item){const c=this.chunk,factor=api.UnitHelper.ConversionFactor(this.doc.RasterVariables.Units,this.doc.DrawingVariables.InsUnits),world=(p)=>api.MathHelper.Transform(new api.Vector3(p.X,p.Y,0),item.Normal,api.CoordinateSystem.Object,api.CoordinateSystem.World);c.Write(100,'AcDbRasterImage');writePoint(c,10,item.Position);writePoint(c,11,api.Vector3.Multiply(factor,world(api.Vector2.Multiply(item.Width/item.Definition.Width,item.Uvector))));writePoint(c,12,api.Vector3.Multiply(factor,world(api.Vector2.Multiply(item.Height/item.Definition.Height,item.Vvector))));c.Write(13,item.Definition.Width);c.Write(23,item.Definition.Height);c.Write(340,item.Definition.Handle);c.Write(70,item.DisplayOptions);c.Write(280,item.Clipping?1:0);c.Write(281,item.Brightness);c.Write(282,item.Contrast);c.Write(283,item.Fade);c.Write(360,this.imageDefReactors.get(item.Definition.Handle).get(item.Handle).Handle);c.Write(71,item.ClippingBoundary.Type);const points=Array.from(item.ClippingBoundary.Vertexes);if(item.ClippingBoundary.Type===2)points.push(points[0]);c.Write(91,points.length);for(const p of points){c.Write(14,p.X-.5);c.Write(24,p.Y-.5);}this.WriteXData(item.XData);}

  WriteHatch(hatch){const c=this.chunk,p=hatch.Pattern;c.Write(100,'AcDbHatch');writePoint(c,10,new api.Vector3(0,0,hatch.Elevation));writePoint(c,210,hatch.Normal);c.Write(2,this.EncodeNonAsciiCharacters(p.Name));c.Write(70,p.Fill);c.Write(71,hatch.Associative?1:0);c.Write(91,hatch.BoundaryPaths.Count);
    for(const path of hatch.BoundaryPaths){c.Write(92,path.PathType);if(!(path.PathType&2))c.Write(93,path.Edges.Count);for(const edge of path.Edges)this.WriteHatchEdge(edge);c.Write(97,path.Entities.Count);for(const entity of path.Entities)c.Write(330,entity.Handle);}
    c.Write(75,p.Style);c.Write(76,p.Type);if(p.Fill===0){c.Write(52,p.Angle);c.Write(41,p.Scale);c.Write(77,p.IsDouble?1:0);c.Write(78,p.LineDefinitions.Count);this.WriteHatchPatternDefinitionLines(p);}
    if(hatch.PixelSize!==null)c.Write(47,hatch.PixelSize);c.Write(98,hatch.SeedPoints.Count);for(const seed of hatch.SeedPoints)writePoint(c,10,seed,2);
    const gradient=p instanceof api.HatchGradientPattern;if(gradient&&this.doc.DrawingVariables.AcadVer>api.DxfVersion.AutoCad2000){for(const[code,v]of[[450,1],[451,0],[460,p.Angle*api.MathHelper.DegToRad],[461,p.Shift],[452,p.SingleColor?1:0],[462,p.Tint],[453,2],[463,0]])c.Write(code,v);if(p.Color1AciIndex!==null)c.Write(63,p.Color1AciIndex);c.Write(421,api.AciColor.ToTrueColor(p.Color1));c.Write(463,1);if(p.Color2AciIndex!==null)c.Write(63,p.Color2AciIndex);c.Write(421,api.AciColor.ToTrueColor(p.Color2));c.Write(470,HatchGradientPatternTypeStringValues[p.GradientType]);}
    const written=new Set();for(const app of hatch.XData.AppIds){let records=hatch.XData.get_Item(app).XDataRecord;const key=app.toUpperCase();written.add(key);if(key==='ACAD')records=io.HatchPatternXData.WithOrigin(records,p.Origin);else if(gradient&&key==='GRADIENTCOLOR1ACI')records=io.HatchPatternXData.WithColorIndex(records,p.Color1.Index);else if(gradient&&key==='GRADIENTCOLOR2ACI')records=io.HatchPatternXData.WithColorIndex(records,p.Color2.Index);WriteXDataRecords(c,()=>this.doc.DrawingVariables.AcadVer,app,records);}
    if(!written.has('ACAD'))WriteXDataRecords(c,()=>this.doc.DrawingVariables.AcadVer,'ACAD',io.HatchPatternXData.WithOrigin(null,p.Origin));if(gradient)for(const[n,color]of[[1,p.Color1],[2,p.Color2]])if(!written.has('GRADIENTCOLOR'+n+'ACI'))WriteXDataRecords(c,()=>this.doc.DrawingVariables.AcadVer,'GradientColor'+n+'ACI',io.HatchPatternXData.WithColorIndex(null,color.Index));
  }
  WriteHatchPatternDefinitionLines(pattern) {
    for (const line of pattern.LineDefinitions) {
      // Capture scale/angle before the callback, but read geometry after it.
      // Keep the source's product grouping: scaling a rotated vector changes bits.
      const scale = pattern.Scale, angle = line.Angle + pattern.Angle;
      this.chunk.Write(53, angle);
      const sinOrigin = DotNetMath.Sin(pattern.Angle * api.MathHelper.DegToRad);
      const cosOrigin = DotNetMath.Cos(pattern.Angle * api.MathHelper.DegToRad);
      const origin = new api.Vector2(
        cosOrigin * line.Origin.X * scale - sinOrigin * line.Origin.Y * scale,
        sinOrigin * line.Origin.X * scale + cosOrigin * line.Origin.Y * scale);
      this.chunk.Write(43, origin.X); this.chunk.Write(44, origin.Y);
      const sinDelta = DotNetMath.Sin(angle * api.MathHelper.DegToRad);
      const cosDelta = DotNetMath.Cos(angle * api.MathHelper.DegToRad);
      const delta = new api.Vector2(
        cosDelta * line.Delta.X * scale - sinDelta * line.Delta.Y * scale,
        sinDelta * line.Delta.X * scale + cosDelta * line.Delta.Y * scale);
      this.chunk.Write(45, delta.X); this.chunk.Write(46, delta.Y);
      this.chunk.Write(79, line.DashPattern.Count);
      for (const dash of line.DashPattern) this.chunk.Write(49, dash * scale);
    }
  }
  WriteHatchEdge(edge) {
    const c = this.chunk, type = edge.Type;
    if (type === 0) {
      c.Write(72, 1); c.Write(73, edge.IsClosed ? 1 : 0); c.Write(93, edge.Vertexes.length);
      for (const value of edge.Vertexes) {
        // A C# foreach Vector3 is copied before the first component callback.
        const vertex = Copy(value);
        c.Write(10, vertex.X); c.Write(20, vertex.Y); c.Write(42, vertex.Z);
      }
      return;
    }
    c.Write(72, type);
    if (type === 1) {
      // Scalar properties, unlike foreach locals, are re-read per component.
      c.Write(10, edge.Start.X); c.Write(20, edge.Start.Y);
      c.Write(11, edge.End.X); c.Write(21, edge.End.Y);
    } else if (type === 2 || type === 3) {
      c.Write(10, edge.Center.X); c.Write(20, edge.Center.Y);
      if (type === 3) { c.Write(11, edge.EndMajorAxis.X); c.Write(21, edge.EndMajorAxis.Y); }
      c.Write(40, type === 2 ? edge.Radius : edge.MinorRatio);
      c.Write(50, edge.StartAngle); c.Write(51, edge.EndAngle); c.Write(73, edge.IsCounterclockwise ? 1 : 0);
    } else if (type === 4) {
      c.Write(94, edge.Degree); c.Write(73, edge.IsRational ? 1 : 0); c.Write(74, edge.IsPeriodic ? 1 : 0);
      c.Write(95, edge.Knots.length); c.Write(96, edge.ControlPoints.length);
      for (const knot of edge.Knots) c.Write(40, knot);
      for (const value of edge.ControlPoints) {
        const point = Copy(value);
        c.Write(10, point.X); c.Write(20, point.Y);
        if (edge.IsRational || point.Z !== 1) c.Write(42, point.Z);
      }
      if (this.doc.DrawingVariables.AcadVer >= api.DxfVersion.AutoCad2010) {
        c.Write(97, edge.FitPoints.Count);
        for (const value of edge.FitPoints) {
          const point = Copy(value); c.Write(11, point.X); c.Write(21, point.Y);
        }
        const nullableValue = value => {
          if (value === null) throw new InvalidOperationException('Nullable object must have a value.');
          return value;
        };
        if (edge.StartTangent !== null) {
          c.Write(12, nullableValue(edge.StartTangent).X); c.Write(22, nullableValue(edge.StartTangent).Y);
        }
        if (edge.EndTangent !== null) {
          c.Write(13, nullableValue(edge.EndTangent).X); c.Write(23, nullableValue(edge.EndTangent).Y);
        }
      }
    } else throw new NotSupportedException('Unknown HATCH edge type ' + type);
  }

  WriteDimension(dim){const c=this.chunk;c.Write(100,'AcDbDimension');if(dim.Block)c.Write(2,this.EncodeNonAsciiCharacters(dim.Block.Name));const world=p=>api.MathHelper.Transform(new api.Vector3(p.X,p.Y,dim.Elevation),dim.Normal,api.CoordinateSystem.Object,api.CoordinateSystem.World);writePoint(c,10,world(dim.DefinitionPoint));writePoint(c,11,new api.Vector3(dim.TextReferencePoint.X,dim.TextReferencePoint.Y,dim.Elevation));let flags=dim.DimensionType|32|(dim.TextPositionManuallySet?128:0);if(dim instanceof api.OrdinateDimension){c.Write(51,360-dim.Rotation);if(dim.Axis===api.OrdinateDimensionAxis.X)flags|=64;}c.Write(53,dim.TextRotation);c.Write(70,flags);c.Write(71,dim.AttachmentPoint);c.Write(72,dim.LineSpacingStyle);c.Write(41,dim.LineSpacingFactor);c.Write(1,this.EncodeNonAsciiCharacters(dim.UserText));writePoint(c,210,dim.Normal);c.Write(3,this.EncodeNonAsciiCharacters(dim.Style.Name));this.PrepareDimensionXData(dim);
    const type=dim.DimensionType,marker=['AcDbAlignedDimension','AcDbAlignedDimension','AcDb2LineAngularDimension','AcDbDiametricDimension','AcDbRadialDimension','AcDb3PointAngularDimension','AcDbOrdinateDimension','AcDbArcDimension'][type];if(!marker)throw new NotSupportedException('Unsupported dimension subtype '+type);c.Write(100,marker);
    if(type===0||type===1){writePoint(c,13,world(dim.FirstReferencePoint));writePoint(c,14,world(dim.SecondReferencePoint));if(type===0){c.Write(50,dim.Rotation);c.Write(100,'AcDbRotatedDimension');}}
    else if(type===3||type===4){writePoint(c,15,world(dim.ReferencePoint));c.Write(40,0);}
    else if(type===2){writePoint(c,13,world(dim.StartFirstLine));writePoint(c,14,world(dim.EndFirstLine));writePoint(c,15,world(dim.StartSecondLine));writePoint(c,16,new api.Vector3(dim.ArcDefinitionPoint.X,dim.ArcDefinitionPoint.Y,dim.Elevation));}
    else if(type===5){writePoint(c,13,world(dim.StartPoint));writePoint(c,14,world(dim.EndPoint));writePoint(c,15,world(dim.CenterPoint));}
    else if(type===6){writePoint(c,13,world(dim.FeaturePoint));writePoint(c,14,world(dim.LeaderEndPoint));}
    else{writePoint(c,13,world(api.Vector2.Polar(dim.CenterPoint,dim.Radius,dim.StartAngle*api.MathHelper.DegToRad)));writePoint(c,14,world(api.Vector2.Polar(dim.CenterPoint,dim.Radius,dim.EndAngle*api.MathHelper.DegToRad)));writePoint(c,15,world(dim.CenterPoint));}
    this.WriteXData(dim.XData);
  }
  PrepareDimensionXData(item){const fields=new Map(),types=api.DimensionStyleOverrideType,overrides=new Map(Array.from(item.StyleOverrides?.Values??[],entry=>[Object.keys(types).find(name=>types[name]===entry.Type),entry.Value]));
    // The pinned writer uses these composite defaults, not inherited style values.
    const resolve=name=>overrides.has(name)?overrides.get(name):name.endsWith('Prefix')||name.endsWith('Suffix')?'':name.endsWith('SuppressZeroFeet')||name.endsWith('SuppressZeroInches')?true:name==='AltUnitsLengthUnits'?2:name==='TolerancesUpperLimit'||name==='TolerancesLowerLimit'?0:name==='TolerancesDisplayMethod'?0:false,set=(id,code,v)=>fields.set(id,[new api.XDataRecord(1070,id),new api.XDataRecord(code,v)]);
    for(const[id,name,kind,table]of dimensionOverrideFields){if(!overrides.has(name))continue;let v=overrides.get(name),code=kind==='handle'?1005:kind==='real'?1040:1070;if(kind==='handle')v=v===null?'0':table==='Blocks'?v.Record.Handle:v.Handle;else if(kind==='color')v=v.Index;else if(kind==='char')v=typeof v==='string'?v.charCodeAt(0):v.Value?.charCodeAt?.(0)??v;else if(kind==='bool')v=name==='FitDimLineInside'?(v?0:1):(v?1:0);set(id,code,v);}
    if(overrides.has('DimArrow1')||overrides.has('DimArrow2'))set(173,1070,1);
    if(overrides.has('TextFillColor')){const color=overrides.get('TextFillColor');set(69,1070,color===null?0:2);if(color!==null)set(70,1070,color.Index);}
    if(item instanceof api.Tolerance)set(140,1040,item.TextHeight);
    for(const[id,prefix,names]of zeroSuppressionGroups)if(names.some(n=>overrides.has(prefix+n)))set(id,1070,suppression(...names.map(n=>resolve(prefix+n))));
    if(['SuppressAngularLeadingZeros','SuppressAngularTrailingZeros'].some(n=>overrides.has(n)))set(79,1070,(resolve('SuppressAngularLeadingZeros')?1:0)|(resolve('SuppressAngularTrailingZeros')?2:0));
    for(const[id,prefix,separator]of[[3,'Dim','<>'],[4,'AltUnits','[]']])if(['Prefix','Suffix'].some(n=>overrides.has(prefix+n)))set(id,1000,resolve(prefix+'Prefix')+separator+resolve(prefix+'Suffix'));
    if(overrides.has('AltUnitsLengthUnits')||overrides.has('AltUnitsStackedUnits')){const units=resolve('AltUnitsLengthUnits'),stack=resolve('AltUnitsStackedUnits');set(273,1070,units===4?(stack?4:6):units===5?(stack?5:7):units===6?8:units);}
    if(['TolerancesDisplayMethod','TolerancesUpperLimit','TolerancesLowerLimit'].some(n=>overrides.has(n))){const type=resolve('TolerancesDisplayMethod');set(71,1070,type===1||type===2?1:0);set(72,1070,type===3?1:0);if(overrides.has('TolerancesUpperLimit'))set(47,1040,resolve('TolerancesUpperLimit'));if(overrides.has('TolerancesLowerLimit'))set(48,1040,resolve('TolerancesLowerLimit'));}
    if(!fields.size)return;
    let data;if(item.XData.ContainsAppId('ACAD')){data=item.XData.get_Item('ACAD');data.XDataRecord.Clear();}
    else{data=new api.XData(new api.ApplicationRegistry('ACAD'));item.XData.Add(data);}
    data.XDataRecord.Add(new api.XDataRecord(1000,'DSTYLE'));data.XDataRecord.Add(new api.XDataRecord(1002,'{'));
    for(const pair of fields.values())for(const record of pair)data.XDataRecord.Add(record);
    data.XDataRecord.Add(new api.XDataRecord(1002,'}'));
  }
  WriteDimensionXData(item){this.PrepareDimensionXData(item);this.WriteXData(item.XData);}

  PreprocessEntities(){this.insertEndSequences.clear();this.polylines.clear();this.imageDefReactors.clear();
    for(const block of this.doc.Blocks){if(!block.End.Handle)this.doc.NumHandles=block.End.AssignHandle(this.doc.NumHandles);
      for(const entity of block.Entities){
        if(entity instanceof api.Insert&&entity.Attributes.Count){const end=new EndSequence();end.Owner=entity;this.doc.NumHandles=end.AssignHandle(this.doc.NumHandles);this.insertEndSequences.set(entity,end);}
        if(entity instanceof api.Polyline3D||entity instanceof api.PolygonMesh||entity instanceof api.PolyfaceMesh||entity instanceof api.Polyline2D&&(entity.SmoothType!==0||entity.HasStoredRecords)){if(!entity.HasStoredRecords)this.PreprocessPolyline(entity);}
        if(entity instanceof api.Image){let reactors=this.imageDefReactors.get(entity.Definition.Handle);if(!reactors)this.imageDefReactors.set(entity.Definition.Handle,reactors=new Map());const reactor=new api.ImageDefinitionReactor(entity.Handle);this.doc.NumHandles=reactor.AssignHandle(this.doc.NumHandles);reactors.set(entity.Handle,reactor);}
      }
      const vp=block.Record.Layout?.Viewport;if(vp&&!vp.Handle)this.doc.NumHandles=vp.AssignHandle(this.doc.NumHandles);
    }
  }

  PreprocessPolyline(entity){const vertices=[],proxy={vertices,end:new EndSequence(),densityU:0,densityV:0};this.polylines.set(entity,proxy);
    const add=(point,flags,marker,width=0,face=null)=>{const vertex=new Vertex();vertex.Owner=entity;this.doc.NumHandles=vertex.AssignHandle(this.doc.NumHandles);vertices.push({Handle:vertex.Handle,Point:point,Flags:flags,Marker:marker,Width:width,Face:face});};
    if(entity instanceof api.PolyfaceMesh){for(const p of entity.Vertexes)add(p,192,'AcDbPolyFaceMeshVertex');for(const face of entity.Faces)add(api.Vector3.Zero,128,'AcDbFaceRecord',0,face);}
    else if(entity instanceof api.PolygonMesh){const smooth=entity.SmoothType!==0;for(let u=0;u<entity.U;u++)for(let v=0;v<entity.V;v++)add(entity.GetVertex(u,v),smooth?80:64,'AcDbPolygonMeshVertex');
      if(smooth){proxy.densityU=Math.max(3,entity.DensityU||this.doc.DrawingVariables.SurfU+1);proxy.densityV=Math.max(3,entity.DensityV||this.doc.DrawingVariables.SurfV+1);const samples=entity.MeshVertexes(proxy.densityU,proxy.densityV);for(let u=0;u<proxy.densityU;u++)for(let v=0;v<proxy.densityV;v++)add(samples.get_Item(u+v*proxy.densityU),72,'AcDbPolygonMeshVertex');}}
    else{const is2D=entity instanceof api.Polyline2D,smooth=entity.SmoothType!==0,marker=is2D?'AcDb2dVertex':'AcDb3dPolylineVertex',width=is2D&&entity.Vertexes.Count?entity.Vertexes.get_Item(0).StartWidth:0;for(const p of entity.Vertexes)add(is2D?new api.Vector3(p.Position.X,p.Position.Y,entity.Elevation):p,(is2D?0:32)|(smooth?16:0),marker,width);
      if(smooth){const count=entity.Vertexes.Count??entity.Vertexes.length,precision=this.doc.DrawingVariables.SplineSegs*(entity.IsClosed?count:count-1);for(const p of entity.PolygonalVertexes(precision))add(is2D?new api.Vector3(p.X,p.Y,entity.Elevation):p,(is2D?0:32)|8,marker,width);}}
    proxy.end.Owner=entity;this.doc.NumHandles=proxy.end.AssignHandle(this.doc.NumHandles);
  }
  WriteLegacyPolyline(entity,layout){const c=this.chunk;this.WriteEntityCommonCodes(entity,layout,'POLYLINE');
    if(entity.HasStoredRecords&&entity instanceof api.PolyfaceMesh){io.WriteStoredPolyfaceMeshHeader(c,this.doc,entity);io.WriteStoredPolyfaceMeshRecords(c,this.doc,entity);return;}
    if(entity.HasStoredRecords&&entity instanceof api.Polyline2D){io.WriteStoredPolyline2DHeader(c,this.doc,entity);io.WriteStoredPolyline2DRecords(c,this.doc,entity);return;}
    const polygon=entity instanceof api.PolygonMesh,polyface=entity instanceof api.PolyfaceMesh,is2D=entity instanceof api.Polyline2D,proxy=this.polylines.get(entity);c.Write(100,polygon?'AcDbPolygonMesh':polyface?'AcDbPolyFaceMesh':is2D?'AcDb2dPolyline':'AcDb3dPolyline');c.Write(10,0);c.Write(20,0);c.Write(30,is2D?entity.Elevation:0);
    if(polygon){c.Write(71,entity.U);c.Write(72,entity.V);c.Write(73,proxy?.densityU??0);c.Write(74,proxy?.densityV??0);}c.Write(70,entity.Flags);c.Write(75,entity.SmoothType??0);writePoint(c,210,entity.Normal);this.WriteXData(entity.XData);
    if(entity.HasStoredRecords){(polygon?io.WriteStoredPolygonMeshRecords:io.WriteStoredPolylineRecords)(c,this.doc,entity);return;}
    for(const vertex of proxy.vertices){c.Write(0,'VERTEX');c.Write(5,vertex.Handle);c.Write(330,entity.Handle);c.Write(100,'AcDbEntity');const layer=vertex.Face?vertex.Face.Layer:entity.Layer,color=vertex.Face?vertex.Face.Color:entity.Color;if(layer)c.Write(8,this.EncodeNonAsciiCharacters(layer.Name));if(color){c.Write(62,color.Index);if(color.UseTrueColor)c.Write(420,api.AciColor.ToTrueColor(color));}if(!vertex.Face)c.Write(100,'AcDbVertex');c.Write(100,vertex.Marker);if(vertex.Face){let i=0;for(const index of vertex.Face.VertexIndexes)c.Write(71+i++,index);}writePoint(c,10,vertex.Point);c.Write(70,vertex.Flags);c.Write(40,vertex.Width);c.Write(41,vertex.Width);}
    c.Write(0,'SEQEND');c.Write(5,proxy.end.Handle);c.Write(330,entity.Handle);c.Write(100,'AcDbEntity');c.Write(8,this.EncodeNonAsciiCharacters(entity.Layer.Name));
  }
  PrepareDictionaries(){const doc=this.doc,root=new DictionaryObject(doc);root.Handle=doc.NamedObjects.Handle;const result=[root];
    for(const [name,property]of managedDictionaries){const table=doc[property],dictionary=new DictionaryObject(root);dictionary.Handle=table.Handle;dictionary.XData.AddRange(table.XData.Values);for(const item of table.Items)dictionary.Entries.Add(item.Handle,item.Name);result.push(dictionary);root.Entries.Add(dictionary.Handle,name);}
    root.Entries.Add(doc.RasterVariables.Handle,'ACAD_IMAGE_VARS');const manager=new DictionaryObject(doc.Layers);manager.Handle=doc.Layers.StateManager.Handle;result.push(manager);
    const states=new DictionaryObject(manager);doc.NumHandles=states.AssignHandle(doc.NumHandles);manager.Entries.Add(states.Handle,'ACAD_LAYERSTATES');for(const state of doc.Layers.StateManager.Items)states.Entries.Add(state.Handle,state.Name);result.push(states);return result;}
  WriteDictionary(dictionary){const c=this.chunk;c.Write(0,'DICTIONARY');c.Write(5,dictionary.Handle);this.WriteDatabaseMetadata(dictionary);c.Write(330,dictionary.Owner?.Handle??'0');c.Write(100,'AcDbDictionary');c.Write(280,dictionary.IsHardOwner?1:0);c.Write(281,dictionary.Cloning);for(const pair of dictionary.Entries){c.Write(3,this.EncodeNonAsciiCharacters(pair.Value));c.Write(dictionary.IsHardOwner?360:350,pair.Key);}this.WriteXData(dictionary.XData);}
  ObjectEnvelope(item,owner,subclass){const c=this.chunk;c.Write(0,item.CodeName);c.Write(5,item.Handle);this.WriteDatabaseMetadata(item);c.Write(330,owner);if(subclass)c.Write(100,subclass);}
  WriteLayout(layout,owner){this.ObjectEnvelope(layout,owner);io.WritePlotSettingsPayload(this.chunk,this.doc.DrawingVariables.AcadVer,layout.PlotSettings);const c=this.chunk;c.Write(100,'AcDbLayout');c.Write(1,this.EncodeNonAsciiCharacters(layout.Name));c.Write(71,layout.TabOrder);for(const [code,key,dimension]of [[10,'MinLimit',2],[11,'MaxLimit',2],[12,'BasePoint',3],[14,'MinExtents',3],[15,'MaxExtents',3]])writePoint(c,code,layout[key],dimension);c.Write(146,layout.Elevation);for(const [code,key]of [[13,'UcsOrigin'],[16,'UcsXAxis'],[17,'UcsYAxis']])writePoint(c,code,layout[key]);c.Write(76,0);c.Write(330,layout.AssociatedBlock.Record.Handle);this.WriteXData(layout.XData);}
  WriteRasterVariables(item,owner){this.ObjectEnvelope(item,owner,'AcDbRasterVariables');this.chunk.Write(90,0);this.chunk.Write(70,item.DisplayFrame?1:0);this.chunk.Write(71,item.DisplayQuality);this.chunk.Write(72,item.Units);this.WriteXData(item.XData);}
  WriteGroup(group,owner){this.ObjectEnvelope(group,owner,'AcDbGroup');const c=this.chunk;c.Write(300,this.EncodeNonAsciiCharacters(group.Description));c.Write(70,group.IsUnnamed?1:0);c.Write(71,group.IsSelectable?1:0);for(const entity of group.Entities)c.Write(340,entity.Handle);this.WriteXData(group.XData);}
  WriteMLineStyle(style,owner){this.ObjectEnvelope(style,owner,'AcDbMlineStyle');const c=this.chunk;c.Write(2,this.EncodeNonAsciiCharacters(style.Name));c.Write(70,style.Flags);c.Write(3,this.EncodeNonAsciiCharacters(style.Description));c.Write(62,style.FillColor.Index);c.Write(51,style.StartAngle);c.Write(52,style.EndAngle);c.Write(71,style.Elements.Count);for(const element of style.Elements){c.Write(49,element.Offset);c.Write(62,element.Color.Index);c.Write(6,this.EncodeNonAsciiCharacters(element.Linetype.Name));}this.WriteXData(style.XData);}
  WriteUnderlayDefinition(definition,owner){this.ObjectEnvelope(definition,owner,'AcDbUnderlayDefinition');const c=this.chunk;c.Write(1,this.EncodeNonAsciiCharacters(definition.File));c.Write(2,this.EncodeNonAsciiCharacters(definition.Layout??definition.Page??'default'));this.WriteXData(definition.XData);}
  WriteImageDef(definition,owner){this.ObjectEnvelope(definition,owner,'AcDbRasterImageDef');const c=this.chunk;c.Write(90,0);c.Write(1,this.EncodeNonAsciiCharacters(definition.File));c.Write(10,definition.Width);c.Write(20,definition.Height);const factor=api.UnitHelper.ConversionFactor(definition.ResolutionUnits,api.DrawingUnits.Millimeters);c.Write(11,factor/definition.HorizontalResolution);c.Write(21,factor/definition.VerticalResolution);c.Write(280,1);c.Write(281,definition.ResolutionUnits);this.WriteXData(definition.XData);}
  WriteImageDefReactor(reactor){const c=this.chunk;c.Write(0,'IMAGEDEF_REACTOR');c.Write(5,reactor.Handle);c.Write(330,reactor.ImageHandle);c.Write(100,'AcDbRasterImageDefReactor');c.Write(90,2);c.Write(330,reactor.ImageHandle);}
  WriteLayerState(state,owner){const c=this.chunk;c.Write(0,'XRECORD');c.Write(5,state.Handle);io.WriteDatabaseMetadata(c,this.doc,state,[owner]);c.Write(330,owner);c.Write(100,'AcDbXrecord');c.Write(280,1);c.Write(91,2047);c.Write(301,this.EncodeNonAsciiCharacters(state.Description));c.Write(290,state.PaperSpace);c.Write(302,this.EncodeNonAsciiCharacters(state.CurrentLayer));
    for(const p of state.Properties.Values){c.Write(330,this.doc.Layers.get_Item(p.Name).Handle);c.Write(90,p.Flags);c.Write(62,p.Color.Index);c.Write(370,p.Lineweight);c.Write(331,this.doc.Linetypes.get_Item(p.LinetypeName).Handle);c.Write(440,p.Transparency.StoredAlphaValue??(p.Transparency.Value===0?0:api.Transparency.ToAlphaValue(p.Transparency)));if(p.Color.UseTrueColor)c.Write(92,api.AciColor.ToTrueColor(p.Color));}
  }
}
