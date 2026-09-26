// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import * as api from '../index.js';
import { DxfVersionCompatibilityDiagnostic as Diagnostic, DxfVersionCompatibilityReport as Report,
  DxfVersionCompatibilityKind as Kind } from './DxfVersionCompatibilityReport.js';
import { ArgumentOutOfRangeException } from '../runtime/Errors.js';
const is=(value,name)=>typeof api[name]==='function'&&value instanceof api[name];
const versionName=value=>Object.keys(api.DxfVersion).find(name=>api.DxfVersion[name]===value)??String(value);
class VersionCompatibilityAnalysis {
  constructor(document,target){this.document=document;this.target=target;this.diagnostics=[];}
  Add(code,source,property,message,kind=Kind.WriterRejection){
    const name=is(source,'DxfObject')?source.CodeName:is(source,'DxfClass')?'CLASS':'HEADER';
    this.diagnostics.push(new Diagnostic(code,kind,source,name,property,message));
  }
  Minimum(minimum,code,source,property,feature){
    if(this.target<minimum)this.Add(code,source,property,feature+' requires the '+versionName(minimum)+' or later writer profile.');
  }
  SourceProfile(source,version){
    if(this.target!==version)this.Add('STORED_SOURCE_PROFILE',source,'SourceVersion',
      'This stored packet requires its original '+versionName(version)+' writer profile; automatic schema conversion is unavailable.');
  }
  Run(){
    if(this.target<13){
      this.Add('DXF_WRITER_PROFILE_UNSUPPORTED',this.document.DrawingVariables,'AcadVer','The typed writer supports AutoCad2000 through AutoCad2018 profiles.');
      return new Report(this.document.DrawingVariables.AcadVer,this.target,this.diagnostics);
    }
    // Never read Objects/NamedObjects, mutate state or clone metadata during analysis.
    for(const block of this.document.Blocks){
      for(const definition of block.AttributeDefinitions.Values)this.Common(definition,definition.CommonData);
      for(const entity of block.Entities){this.Entity(entity);if(is(entity,'Insert'))for(const attribute of entity.Attributes)this.Common(attribute,attribute.CommonData);}
    }
    for(const item of this.document.AddedObjects.Values)this.RegisteredObject(item);
    for(const view of this.document.Views){
      if(view.IsCameraPlottable)this.Minimum(15,'VIEW_CAMERA_PROFILE',view,'IsCameraPlottable','A plottable named-view camera');
      if(view.HasStoredLiveSection)this.Minimum(15,'VIEW_LIVE_SECTION_PROFILE',view,'LiveSection','A stored VIEW live-section slot (including explicit null)');
      this.SunSlot(view);
    }
    for(const port of this.document.VPorts.Records)this.SunSlot(port);
    for(const layout of this.document.Layouts)if(layout.PlotSettings?.ShadePlotObject!=null)
      this.Minimum(15,'PLOT_SHADE_REFERENCE_PROFILE',layout,'PlotSettings.ShadePlotObject','A layout shade-plot reference');
    if(this.target===13){
      if(this.document.DrawingVariables.LastSavedBy!=null&&this.document.DrawingVariables.LastSavedBy!=='')
        this.Add('HEADER_LAST_SAVED_BY_OMITTED',this.document.DrawingVariables,'LastSavedBy','The R2000 writer omits $LASTSAVEDBY.',Kind.DataOmission);
      for(const definition of this.document.Classes)if(definition.InstanceCount!==null)
        this.Add('CLASS_INSTANCE_COUNT_OMITTED',definition,'InstanceCount','The R2000 writer omits the explicit CLASS group-91 instance count.',Kind.DataOmission);
    }
    return new Report(this.document.DrawingVariables.AcadVer,this.target,this.diagnostics);
  }
  Common(source,data){
    if(data.ColorName!==null)this.Minimum(14,'ENTITY_COLOR_NAME_PROFILE',source,'ColorName','Entity color-name data');
    if(data.ShadowMode!==null)this.Minimum(15,'ENTITY_SHADOW_MODE_PROFILE',source,'ShadowMode','Entity shadow-mode data');
  }
  Entity(entity){
    this.Common(entity,entity.CommonData);
    for(const [name,minimum,code,label] of [['Mesh',16,'MESH_PROFILE','MESH entities'],['Helix',15,'HELIX_PROFILE','HELIX entities'],
      ['Light',15,'LIGHT_PROFILE','LIGHT entities'],['Section',15,'SECTION_PROFILE','SECTION entities']])
      if(is(entity,name))this.Minimum(minimum,code,entity,'Type',label);
    if(is(entity,'Viewport'))this.SunSlot(entity);
    if(is(entity,'DxfOpaqueEntity')||is(entity,'StoredTable'))this.SourceProfile(entity,entity.SourceVersion);
    if(is(entity,'Polyline2D')&&!entity.HasStoredRecords)for(let i=0;i<entity.Vertexes.Count;i++)
      if(entity.Vertexes.get_Item(i).VertexIdentifier!==null)this.Minimum(17,'LWPOLYLINE_VERTEX_ID_PROFILE',entity,'Vertexes['+i+'].VertexIdentifier','LWPOLYLINE vertex identifiers');
    if(is(entity,'MText'))this.MText(entity);
    if(is(entity,'Hatch'))this.Hatch(entity);
    if(is(entity,'MultiLeader'))this.MultiLeader(entity);
    if(is(entity,'AcisEntity')){
      if(this.target>=17)this.Add('ACIS_SAT_PROFILE',entity,'EncodedSatChunks','Stored SAT entities support R2000 through R2010. The R2013+ writer requires unimplemented SAB/ACDSDATA support.');
      if(is(entity,'Solid3D')&&entity.HistoryHandle!==null)this.Minimum(15,'ACIS_HISTORY_PROFILE',entity,'HistoryHandle','Explicit ACIS history data');
    }
  }
  MText(text){
    if(text.BackgroundFill!==null){
      this.Minimum(15,'MTEXT_BACKGROUND_PROFILE',text,'BackgroundFill','Stored MTEXT background data');
      if((text.BackgroundFill.Flags&api.MTextBackgroundFillFlags.TextFrame)!==0)this.Minimum(18,'MTEXT_FRAME_PROFILE',text,'BackgroundFill.Flags','MTEXT text frames');
    }
    if(text.Columns===null)return;
    const storage=text.Columns.Storage;
    if(storage===api.MTextColumnStorage.Embedded)this.Minimum(18,'MTEXT_EMBEDDED_COLUMNS_PROFILE',text,'Columns.Storage','Embedded MTEXT columns');
    else if(storage===api.MTextColumnStorage.Direct)this.Minimum(15,'MTEXT_DIRECT_COLUMNS_PROFILE',text,'Columns.Storage','Direct MTEXT columns');
    else if(storage===api.MTextColumnStorage.LegacyLinked&&this.target>=18)
      this.Add('MTEXT_LINKED_COLUMNS_PROFILE',text,'Columns.Storage','Legacy linked MTEXT columns require a pre-R2018 profile and explicit conversion for R2018.');
  }
  Hatch(hatch){
    if(is(hatch.Pattern,'HatchGradientPattern')&&this.target===13)this.Add('HATCH_GRADIENT_OMITTED',hatch,'Pattern',
      'The R2000 writer omits the gradient packet and retains only the base hatch fill.',Kind.DataOmission);
    for(let i=0;i<hatch.BoundaryPaths.Count;i++)for(let j=0;j<hatch.BoundaryPaths.get_Item(i).Edges.Count;j++){
      const spline=hatch.BoundaryPaths.get_Item(i).Edges.get_Item(j);
      if(!(spline instanceof api.HatchBoundaryPath.Spline))continue;
      const prefix='BoundaryPaths['+i+'].Edges['+j+'].';
      if(spline.FitPoints.Count!==0)this.Minimum(16,'HATCH_SPLINE_FIT_PROFILE',hatch,prefix+'FitPoints','HATCH spline fit data');
      if(spline.StartTangent!==null)this.Minimum(16,'HATCH_SPLINE_FIT_PROFILE',hatch,prefix+'StartTangent','HATCH spline start-tangent data');
      if(spline.EndTangent!==null)this.Minimum(16,'HATCH_SPLINE_FIT_PROFILE',hatch,prefix+'EndTangent','HATCH spline end-tangent data');
    }
  }
  MultiLeader(leader){
    this.Minimum(15,'MULTILEADER_PROFILE',leader,'Type','MULTILEADER entities');
    const properties=leader.Properties;
    for(const [field,code] of [['TextAttachmentDirection',271],['TextBottomAttachment',272],['TextTopAttachment',273]])
      if(properties[field]!==null)this.Minimum(16,'MULTILEADER_OPTIONAL_FIELD_PROFILE',leader,'Properties.'+field,'Stored MULTILEADER group '+code);
    if(properties.LeaderExtendToText!==null)this.Minimum(17,'MULTILEADER_OPTIONAL_FIELD_PROFILE',leader,'Properties.LeaderExtendToText','Stored MULTILEADER group 295');
    for(const [field,code] of [['TopAttachment',272],['BottomAttachment',273]])if(leader.Context[field]!==null)
      this.Minimum(16,'MULTILEADER_OPTIONAL_FIELD_PROFILE',leader,'Context.'+field,'Stored MULTILEADER context group '+code);
    for(let i=0;i<leader.Context.Leaders.Count;i++)if(leader.Context.Leaders.get_Item(i).AttachmentDirection!==null)
      this.Minimum(16,'MULTILEADER_OPTIONAL_FIELD_PROFILE',leader,'Context.Leaders['+i+'].AttachmentDirection','Stored MULTILEADER branch group 271');
  }
  SunSlot(host){if(api.SunReferences.IsPresent(host))this.Minimum(is(host,'View')?16:15,'SUN_OWNER_PROFILE',host,'Sun','The stored SUN owner slot (including explicit null)');}
  RegisteredObject(item){
    if(['Polyline3DRecord','PolygonMeshRecord','PolyfaceMeshRecord','Polyline2DRecord','DxfTableStyle','DxfStoredTableContent','DxfStoredTableGeometry',
      'DxfStoredCellStyleMap','DxfStoredField','DxfStoredDimAssoc','DxfStoredSectionManager','DxfStoredSunStudy'].some(name=>is(item,name))){this.SourceProfile(item,item.SourceVersion);return;}
    const rules=[['DxfDataTable',14,'DATATABLE_PROFILE','StoredVersion','Typed DATATABLE data'],['DxfGeoData',16,'GEODATA_PROFILE','Version','Typed GEODATA version 2'],
      ['DxfLightList',15,'LIGHTLIST_PROFILE','StoredVersion','Typed LIGHTLIST data'],['DxfSun',15,'SUN_PROFILE','StoredVersion','Typed SUN data'],
      ['DxfMLeaderStyle',15,'MLEADERSTYLE_PROFILE','Properties','Typed MLEADERSTYLE data'],['DxfSectionSettings',15,'SECTIONSETTINGS_PROFILE','TypeSettings','Typed SECTIONSETTINGS data'],
      ['DxfSortentsTable',14,'SORTENTSTABLE_PROFILE','Entries','SORTENTSTABLE data']];
    for(const [name,minimum,code,property,feature] of rules)if(is(item,name)){this.Minimum(minimum,code,item,property,feature);return;}
    if(is(item,'DxfPlotSettingsObject')&&item.Settings.ShadePlotObject!==null)this.Minimum(15,'PLOT_SHADE_REFERENCE_PROFILE',item,'Settings.ShadePlotObject','A page-setup shade-plot reference');
  }
}
export function InstallVersionCompatibility(Type){
  Type.prototype.AnalyzeVersionCompatibility=function(targetVersion){
    if(targetVersion===api.DxfVersion.Unknown||!Object.values(api.DxfVersion).includes(targetVersion))throw new ArgumentOutOfRangeException('targetVersion');
    return new VersionCompatibilityAnalysis(this,targetVersion).Run();
  };
}
