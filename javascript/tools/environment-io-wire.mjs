// Observation-only fixture controller; no alternate payload parser or writer.
import * as api from '../index.js';
import * as io from '../runtime/DatabasePayloadIO.js';
import { WireInput,PayloadFailure } from './database-payload-values.mjs';
import { wire } from './model-wire.mjs';
import { BinaryCodeValueWriter } from '../netDxf/IO/BinaryCodeValueWriter.js';
import { TextCodeValueWriter } from '../netDxf/IO/TextCodeValueWriter.js';
import { Culture } from '../runtime/GeometryRuntime.js';
import { InvalidOperationException,NullReferenceException } from '../runtime/Errors.js';
import { bytesToBase64 } from './wire.mjs';
const ref=value=>value==null?null:{type:value.constructor.name,handle:value.Handle};
export function environmentIOCall(input) {
  Culture.Current='';api.BlockRecord.DefaultUnits=0;api.Insert.DefaultInsUnits=0;
  const document=new api.DxfDocument(input.version??18),context=new io.DatabaseIOContext(document);
  const line=new api.Line(),viewport=new api.Viewport();document.Entities.Add(line);document.Entities.Add(viewport);
  const root=document.NamedObjects,leaf=new api.DxfPlaceholder();root.Add('leaf',leaf);
  const view=document.Views.Add(new api.View('EnvironmentView')),layout=document.Layouts.Add(new api.Layout('EnvironmentPaper'));
  const ext=new api.DxfDictionary();document.Objects.SetExtensionDictionary(line.Owner.Record,ext);
  const foreign=new api.DxfDocument(18),foreignLeaf=new api.DxfPlaceholder();foreign.NamedObjects.Add('foreign',foreignLeaf);
  const resources={line,viewport,root,leaf,view,port:document.Viewport,layout,layoutPlot:layout.PlotSettings,block:line.Owner.Record,style:document.TextStyles.get_Item('Standard'),ext,foreignLeaf,foreignStyle:foreign.TextStyles.get_Item('Standard'),document};
  const source=item=>{const entry=new io.SourceRecordIdentity();entry.Handle=BigInt('0x'+item.Handle);entry.IdentitySeen=true;context.sourceObjectIdentities.add(entry.Handle);context.RecordSourceObject(item,entry);};
  for(const item of document.AddedObjects.Values)if(item.Handle!=='0')source(item);
  let record=new io.DatabaseRecord(),plot=null,shade=null,cursor=0;
  const target=name=>name==='item'?record.Object:name==='settings'?record.Object?.Settings??null:name==='plot'?plot:resources[name];
  const read=value=>{
    if(value&&typeof value==='object'){
      if('enum'in value)return value.value;
      if('h'in value&&target(value.h)==null)throw new NullReferenceException();
    }
    return WireInput(value,target);
  };
  const tags=input.tags.map(([code,value])=>new api.DxfTag(code,read(value)));
  const ownerContext=new io.SunOwnerContext(input.marker??'AcDbViewport');
  const binary=input.mode!=='text',bytes=new api.MemoryStream();
  const text={value:'',count:0,hook:null,WriteLine(value){this.hook?.(++this.count);this.value+=String(value??'')+'\n';},Flush(){}};
  const writer=binary?new BinaryCodeValueWriter(bytes,input.mode==='legacy'):new TextCodeValueWriter(text);
  return input.steps.map(step=>{
    let result=null,error=null;
    try {
      switch(step.method) {
        case 'read':
          if(input.kind==='OutputSettings')result=io.ReadOutputSettingsPayload(context,record,input.type??'PLOTSETTINGS',tags,step.start??0);
          else if(input.kind==='GeoData')result=io.ReadGeoDataPayload(context,record,input.type??'GEODATA',tags,step.start??0);
          else if(input.kind==='Sun'){if(input.payloadOnly)result=io.ReadSunPayload(context,record,tags,step.start??0);else record=io.ReadSunRecord(context,tags);}
          break;
        case 'parse':{const handle={};try{plot=io.ParsePlotSettings(context,tags,handle);}finally{shade=handle.value;}break;}
        case 'split':result=io.SplitGeoDefinition(read(step.text));break;
        case 'geo-tag':{const position={value:step.cursor??cursor};try{result=wire(io.ReadGeoTag(tags,position,step.code));}finally{cursor=position.value;}break;}
        case 'register':{
          const item=record.Object;if(item===null)throw new NullReferenceException();const owner=target(step.owner??'root');item.Owner=owner;
          document.Objects.Register(item,item.Handle!==null);
          if(owner instanceof api.DxfDictionary)owner.AddLoaded(step.name??'PAYLOAD',item,step.hard??true);source(item);break;
        }
        case 'resolve':io['Resolve'+input.kind+(input.kind==='GeoData'?'Hosts':'References')](context);break;
        case 'add-sun':io.AddSunReference(context,read(step.owner),read(step.handle));break;
        case 'write-ref':io.WriteSunReference(writer,input.version??18,read(step.owner));break;
        case 'null-handle':result=io.IsNullSourceHandle(read(step.handle));break;
        case 'observe':ownerContext.Observe(step.code,read(step.value));result=ownerContext.IsPublic;break;
        case 'set':{const item=target(step.target??'item');if(item==null)throw new NullReferenceException();item[step.property]=read(step.value);break;}
        case 'set-sun':api.SunReferences.Set(read(step.owner),read(step.sun),step.present??true);break;
        case 'alias':{
          const owner=target(step.owner??'ext');owner.Remove(step.remove??'ACAD_GEOGRAPHICDATA');if(step.name!==null)owner.AddLoaded(step.name??'ACAD_GEOGRAPHICDATA',record.Object,step.hard??true);break;
        }
        case 'source':{
          const item=target(step.target),key=BigInt('0x'+item.Handle);
          if(step.action==='ambiguous')context.acceptedSourceRecords.get(key).Ambiguous=true;
          else if(step.action==='unaccept')context.acceptedSourceObjects.delete(key);
          else if(step.action==='discard')context.sourceObjectIdentities.delete(key);
          else if(step.action==='replace')document.AddedObjects.set_Item(item.Handle,new api.DxfPlaceholder());
          else if(step.action==='unregister')document.AddedObjects.Remove(item.Handle);break;
        }
        case 'opaque':{const item=new api.DxfOpaqueObject(step.code??'GEODATA',[]);item.Owner=root;document.Objects.Register(item,false);root.AddLoaded('Opaque',item,true);source(item);resources.opaque=item;break;}
        case 'class':{const definition=new api.DxfClass(step.name,step.cpp,step.app??'Probe');definition.IsEntity=step.entity??false;definition.InstanceCount=step.count??9;document.Classes.Add(definition);break;}
        case 'prepare':io['Prepare'+input.kind+'Class'](document,document.Classes);break;
        case 'validate':io.ValidateOutputSettings(document);break;
        case 'database-validate':result=Array.from(document.Objects.Validate());break;
        case 'write':case 'write-plot':{
          text.count=0;text.hook=count=>{for(const hook of step.hooks??[])if(hook.at===count){
            if(hook.kind==='throw')throw new InvalidOperationException('Injected environment writer callback.');
            const item=target(hook.target??'item');
            if(hook.kind==='clear')item[hook.property].Clear();else item[hook.property]=read(hook.value);
          }};
          try{if(step.method==='write-plot')io.WritePlotSettingsPayload(writer,input.version??18,target(step.target??'plot'));
          else result=io['Write'+input.kind+'Payload'](writer,input.version??18,step.target?target(step.target):record.Object);}finally{text.hook=null;}
          break;
        }
        default:throw new InvalidOperationException('Unknown environment observation '+step.method);
      }
    } catch(e) {error=PayloadFailure(e);}
    writer.Flush();
    return {ok:true,value:structuredClone({result,error,record:{model:wire(record.Object),owner:record.Metadata.Owner,extension:record.Metadata.Extension,reactors:record.Metadata.Reactors},
      pending:{shade:context.outputShadeReferences.map(([p,h])=>({settings:wire(p),handle:h})),geo:context.geoDataHosts.map(([g,h])=>({data:ref(g),handle:h})),sun:context.sunReferences.map(([s,h])=>({owner:ref(s),handle:h}))},
      plot:wire(plot),shade,cursor,public:ownerContext.IsPublic,hosts:['port','view','viewport'].map(name=>({name,sun:ref(api.SunReferences.Get(resources[name])),present:api.SunReferences.IsPresent(resources[name])})),
      layouts:Array.from(document.Layouts,l=>({name:l.Name,plot:wire(l.PlotSettings)})),seed:document.DrawingVariables.HandleSeed,apps:Array.from(document.ApplicationRegistries,a=>({name:a.Name,handle:a.Handle})),classes:wire(document.Classes),
      output:bytesToBase64(binary?bytes.ToArray():new TextEncoder().encode(text.value))})};
  });
}
