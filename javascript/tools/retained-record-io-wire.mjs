// Observation-only fixtures. Production record parsers/writers perform every operation.
import * as api from '../index.js';
import * as io from '../runtime/DatabasePayloadIO.js';
import { WireInput, PayloadFailure } from './database-payload-values.mjs';
import { wire } from './model-wire.mjs';
import { dependencySnapshot } from './stored-dependencies-wire.mjs';
import { tableStyleSnapshot } from './table-style-wire.mjs';
import { tableGeometrySnapshot } from './table-geometry-wire.mjs';
import { tableContentSnapshot } from './table-content-wire.mjs';
import { managerSnapshot } from './section-manager-wire.mjs';
import { BinaryCodeValueWriter } from '../netDxf/IO/BinaryCodeValueWriter.js';
import { TextCodeValueWriter } from '../netDxf/IO/TextCodeValueWriter.js';
import { Culture } from '../runtime/GeometryRuntime.js';
import { InvalidOperationException, NullReferenceException } from '../runtime/Errors.js';
import { bytesToBase64 } from './wire.mjs';
const ref=value=>value==null?null:{type:value.constructor.name,handle:value.Handle};
const geometryFields=['SectionType','GeometryValue','Flags','ColorCode','ColorIndex','LayerName','LinetypeName','LinetypeScale','PlotStyleName','Lineweight','FaceTransparency','EdgeTransparency','HatchPatternType','HatchPatternName','HatchAngle','HatchScale','HatchSpacing'];
function snapshot(item) {
  if(item==null)return null;
  const common=wire(item);let details=null;
  if(item instanceof api.DxfSectionSettings)details={sectionType:item.SectionType,types:Array.from(item.TypeSettings,t=>({type:t.SectionType,options:t.GenerationOptions,sources:Array.from(t.SourceObjects,ref),destination:ref(t.DestinationBlock),file:wire(t.DestinationFileName),repeat:t.RepeatGeometryMarkers,geometry:Array.from(t.GeometrySettings,g=>Object.fromEntries(geometryFields.map(k=>[k,wire(g[k])])))}))};
  else if(item instanceof api.DxfStoredSectionManager)details=managerSnapshot(item);
  else if(item instanceof api.DxfStoredField || item instanceof api.DxfStoredDimAssoc || item instanceof api.DxfStoredSunStudy)details=dependencySnapshot(item);
  else if(item instanceof api.DxfTableStyle || item instanceof api.DxfStoredCellStyleMap)details=tableStyleSnapshot(item);
  else if(item instanceof api.DxfStoredTableGeometry)details=tableGeometrySnapshot(item);
  else if(item instanceof api.DxfStoredTableContent)details=tableContentSnapshot(item);
  return {common,details};
}
const pendingNames=['storedFields','storedDimAssocs','storedSunStudies','storedTableContents','storedTableGeometries','storedCellStyleMaps','tableStyles','storedSectionManagers'];
const resolver={StoredField:'ResolveStoredFields',StoredDimAssoc:'ResolveStoredDimAssocReferences',StoredSunStudy:'ResolveStoredSunStudyReferences',StoredTableContent:'ResolveStoredTableContentReferences',StoredTableGeometry:'ResolveStoredTableGeometryReferences',StoredCellStyleMap:'ResolveStoredCellStyleMapReferences',TableStyle:'ResolveTableStyleReferences',SectionSettings:'ResolveSectionSettingsReferences',SectionManager:'ResolveSectionManagerReferences'};
export function retainedRecordIOCall(input) {
  Culture.Current='';api.BlockRecord.DefaultUnits=0;api.Insert.DefaultInsUnits=0;
  const document=new api.DxfDocument(18),context=new io.DatabaseIOContext(document);
  const line=new api.Line(),dimension=new api.AlignedDimension(),section=new api.Section();
  for(const item of [line,dimension,section])document.Entities.Add(item);
  const root=document.NamedObjects,leaf=new api.DxfPlaceholder();root.Add('leaf',leaf);
  const extension=new api.DxfDictionary();document.Objects.SetExtensionDictionary(dimension,extension);
  const resources={document,line,dimension,section,root,leaf,extension,block:line.Owner.Record,style:document.TextStyles.get_Item('Standard')};
  function source(item){const entry=new io.SourceRecordIdentity();entry.Handle=BigInt('0x'+item.Handle);entry.IdentitySeen=true;context.sourceObjectIdentities.add(entry.Handle);context.RecordSourceObject(item,entry);}
  for(const item of document.AddedObjects.Values)if(item.Handle!=='0')source(item);
  document.DrawingVariables.AcadVer=input.version??18;
  let record=new io.DatabaseRecord(),header=null;
  const target=name=>name==='item'?record.Object:resources[name];
  const read=value=>WireInput(value,target);
  const tags=input.tags.map(([code,value])=>new api.DxfTag(code,read(value)));
  const binary=input.mode!=='text',bytes=new api.MemoryStream();
  const text={value:'',count:0,hook:null,WriteLine(value){this.hook?.(++this.count);this.value+=String(value??'')+'\n';},Flush(){}};
  const writer=binary?new BinaryCodeValueWriter(bytes,input.mode==='legacy'):new TextCodeValueWriter(text);
  return input.steps.map(step=>{
    let result=null,error=null;
    try {
      switch(step.method) {
        case 'read': {
          if(input.kind==='StoredEnvelope')result=io.ReadStoredEnvelopePayload(context,record,input.type,tags,step.start??0);
          else if(input.kind==='PrivateXRecord'){const out={};try{result=io.TryReadPrivateXRecord(context,tags,out);}finally{if(out.value!==null)record=out.value;}}
          else if(['StoredField','SectionSettings','SectionManager'].includes(input.kind))record=io['Read'+input.kind+'Record'](context,input.type,tags);
          else record=io['Read'+input.kind+'Record'](context,tags);
          break;
        }
        case 'header':{header={};result=io.TryReadStoredFieldHeader(tags,step.start??0,step.end??tags.length,header);break;}
        case 'shape':result=io.IsStoredSunStudyShape(tags);break;
        case 'register': {
          const item=record.Object;if(item===null)throw new NullReferenceException();const owner=target(step.owner??'root');item.Owner=owner;
          document.Objects.Register(item,item.Handle!==null);
          if(owner instanceof api.DxfDictionary)owner.AddLoaded(step.name??'PAYLOAD',item,step.hard??true);source(item);break;
        }
        case 'resolve':if(resolver[input.kind])io[resolver[input.kind]](context);break;
        case 'source': {
          const item=target(step.target),key=BigInt('0x'+item.Handle);
          if(step.action==='ambiguous')context.acceptedSourceRecords.get(key).Ambiguous=true;
          else if(step.action==='unaccept')context.acceptedSourceObjects.delete(key);
          else if(step.action==='discard')context.sourceObjectIdentities.delete(key);
          else if(step.action==='replace')document.AddedObjects.set_Item(item.Handle,new api.DxfPlaceholder());
          else if(step.action==='unregister')document.AddedObjects.Remove(item.Handle);break;
        }
        case 'set':{const item=step.target==='variables'?document.DrawingVariables:target(step.target??'item');if(item==null)throw new NullReferenceException();item[step.property]=read(step.value);break;}
        case 'reactor':{const item=target(step.target??'item');item.PersistentReactors.Add(target(step.value));break;}
        case 'alias':{const owner=target(step.owner??'root');owner.Remove(step.remove??'PAYLOAD');if(step.name!==null)owner.AddLoaded(step.name,record.Object,step.hard??true);break;}
        case 'opaque':{const item=new api.DxfOpaqueObject(input.type??step.code,[]);item.Owner=root;document.Objects.Register(item,false);root.AddLoaded('Opaque',item,true);source(item);resources.opaque=item;break;}
        case 'class':{const definition=new api.DxfClass(step.name,step.cpp,step.app??'Probe');definition.IsEntity=step.entity??false;definition.InstanceCount=step.count??9;document.Classes.Add(definition);break;}
        case 'prepare':io['Prepare'+input.kind+(input.kind==='StoredEnvelope'||input.kind==='SectionManager'?'Classes':'Class')](document,document.Classes);break;
        case 'validate':result=Array.from(document.Objects.Validate());break;
        case 'write': {
          text.count=0;text.hook=count=>{for(const hook of step.hooks??[])if(hook.at===count){
            if(hook.kind==='throw')throw new InvalidOperationException('Injected retained record writer callback.');
            const item=hook.target==='variables'?document.DrawingVariables:target(hook.target??'item');
            if(hook.kind==='clear')item[hook.property].Clear();else item[hook.property]=read(hook.value);
          }};
          try{result=io['Write'+input.kind+'Payload'](writer,document.DrawingVariables.AcadVer,step.target?target(step.target):record.Object,document);}finally{text.hook=null;}
          break;
        }
        case 'snapshot':break;
        default:throw new InvalidOperationException('Unknown retained observation '+step.method);
      }
    }catch(e){error=PayloadFailure(e);}
    writer.Flush();
    const pending=Object.fromEntries(pendingNames.map(name=>[name,context[name].map(item=>Array.isArray(item)?{item:ref(item[0]),children:item[1],objects:item[2]}:ref(item))]));
    pending.sections=Array.from(context.pendingSectionSettings,([item,types])=>({item:ref(item),types:types.map(t=>({type:t.Type,options:t.Options,destination:t.Destination,file:wire(t.File),repeat:t.RepeatMarkers,sources:t.Sources,geometry:t.Geometry.map(g=>Object.fromEntries(geometryFields.map(k=>[k,wire(g[k])])))}))}));
    return {ok:true,value:structuredClone({result,error,record:{model:snapshot(record.Object),owner:record.Metadata.Owner,extension:record.Metadata.Extension,reactors:record.Metadata.Reactors},header,pending,
      seed:document.DrawingVariables.HandleSeed,apps:Array.from(document.ApplicationRegistries,a=>({name:a.Name,handle:a.Handle})),classes:wire(document.Classes),output:bytesToBase64(binary?bytes.ToArray():new TextEncoder().encode(text.value))})};
  });
}
