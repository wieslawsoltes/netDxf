// Observation-only fixture controller. The runtime performs all parsing and binding.
import * as api from '../index.js';
import * as io from '../runtime/DatabasePayloadIO.js';
import { WireInput,PayloadFailure } from './database-payload-values.mjs';
import { wire } from './model-wire.mjs';
import { BinaryCodeValueWriter } from '../netDxf/IO/BinaryCodeValueWriter.js';
import { TextCodeValueWriter } from '../netDxf/IO/TextCodeValueWriter.js';
import { Culture } from '../runtime/GeometryRuntime.js';
import { InvalidOperationException,NullReferenceException } from '../runtime/Errors.js';
import { bytesToBase64 } from './wire.mjs';
function model(value){
  if(value instanceof api.DxfSortentsTable)return {model:wire(value),block:ref(value.BlockRecord),entries:Array.from(value.Entries,e=>({entity:ref(e.Entity),key:e.SortHandle}))};
  return wire(value);
}
const ref=value=>value==null?null:{type:value.constructor.name,handle:value.Handle};
export function databasePayloadCall(input){
  Culture.Current='';api.BlockRecord.DefaultUnits=0;api.Insert.DefaultInsUnits=0;
  const document=new api.DxfDocument(input.version??18),context=new io.DatabaseIOContext(document),resources={};
  const line=new api.Line(),light=new api.Light();document.Entities.Add(line);document.Entities.Add(light);
  Object.assign(resources,{line,light,block:line.Owner.Record,root:document.NamedObjects,style:document.TextStyles.get_Item('Standard')});
  for(const name of ['buffer','buffer2','leaf']){
    const item=name==='leaf'?new api.DxfPlaceholder():new api.DxfIdBuffer();document.NamedObjects.Add(name,item);resources[name]=item;
  }
  resources.buffer.References.Add(line);
  const source=(item,ambiguous=false)=>{
    const record=new io.SourceRecordIdentity();record.Handle=BigInt('0x'+item.Handle);record.IdentitySeen=true;record.Ambiguous=ambiguous;
    context.sourceObjectIdentities.add(record.Handle);context.RecordSourceObject(item,record);
  };
  for(const item of document.AddedObjects.Values)if(item.Handle!=='0')source(item);
  let record=new io.DatabaseRecord();
  const target=name=>name==='item'?record.Object:resources[name];
  const read=value=>WireInput(value,target);
  const tags=input.tags.map(([code,value])=>new api.DxfTag(code,read(value)));
  const binary=input.mode!=='text',bytes=new api.MemoryStream();
  const text={value:'',count:0,hook:null,WriteLine(value){this.hook?.(++this.count);this.value+=String(value??'')+'\n';},Flush(){}};
  const writer=binary?new BinaryCodeValueWriter(bytes,input.mode==='legacy'):new TextCodeValueWriter(text);
  const headerResult=()=>({owner:record.Metadata.Owner,extension:record.Metadata.Extension,reactors:record.Metadata.Reactors,refs:record.ContainerReferences,keys:record.SortKeys});
  return input.steps.map(step=>{
    let result=null,error=null;
    try{
      switch(step.method){
        case 'read':{
          const start=step.start??0;
          switch(input.kind){
            case 'Container':result=io.ReadContainerPayload(context,record,input.type,tags,start);break;
            case 'LightList':result=io.ReadLightListPayload(context,record,input.type??'LIGHTLIST',tags,start);break;
            case 'DataTable':if(input.payloadOnly)result=io.ReadDataTablePayload(context,record,tags,start);else record=io.ReadDataTableRecord(context,tags);break;
            case 'LayerIndex':record=io.ReadLayerIndexRecord(context,tags);break;
            case 'LayerFilterPointer':record=io.ReadLayerFilterPointerRecord(context,input.type,tags);break;
          }break;
        }
        case 'register':{
          const owner=target(step.owner??'root');if(record.Object===null)throw new NullReferenceException();record.Object.Owner=owner;
          document.Objects.Register(record.Object,record.Object.Handle!==null);
          if(owner instanceof api.DxfDictionary)owner.AddLoaded('PAYLOAD',record.Object,true);
          source(record.Object);break;
        }
        case 'owner':{
          const item=target(step.target);if(item.Owner instanceof api.DxfDictionary)for(const e of [...item.Owner.Entries])if(e.Target===item)item.Owner.Remove(e.Name);
          item.Owner=target(step.owner);break;
        }
        case 'resolve':
          if(input.kind==='Container')io.ResolveContainerReferences(context,record);
          else if(input.kind==='DataTable')io.ResolveDataTableReferences(context);
          else if(input.kind==='LightList')io.ResolveLightListReferences(context);
          else if(input.kind==='LayerIndex')io.ResolveLayerIndexReferences(context);break;
        case 'source':{
          const item=target(step.target),key=BigInt('0x'+item.Handle);
          if(step.action==='ambiguous')context.acceptedSourceRecords.get(key).Ambiguous=true;
          else if(step.action==='unaccept')context.acceptedSourceObjects.delete(key);
          else if(step.action==='discard')context.sourceObjectIdentities.delete(key);
          else if(step.action==='replace')document.AddedObjects.set_Item(item.Handle,new api.DxfPlaceholder());
          else if(step.action==='validate')context.ValidateSourceIdentityDeclarations();break;
        }
        case 'lookup':result=ref(context.GetObjectBySourceHandle(read(step.handle),step.includeMetadata??false));break;
        case 'clear':target(step.target).References.Clear();break;
        case 'set':target(step.target??'item')[step.property]=read(step.value);break;
        case 'class':{
          const definition=new api.DxfClass(step.name,step.cpp,step.app??'Probe');definition.IsEntity=step.entity??false;definition.InstanceCount=step.count??9;document.Classes.Add(definition);break;
        }
        case 'prepare':{
          const fn=io['Prepare'+(input.kind==='LayerFilterPointer'?'LayerFilterPointerClasses':input.kind+'Class')];fn(document,document.Classes);break;
        }
        case 'write':{
          text.count=0;text.hook=count=>{for(const hook of step.hooks??[])if(hook.at===count){
            if(hook.kind==='throw')throw new InvalidOperationException('Injected database writer callback.');
            else if(hook.kind==='clear')target(hook.target??'item')[hook.property].Clear();
            else target(hook.target??'item')[hook.property]=read(hook.value);
          }};
          try{result=input.kind==='Container'?io.WriteContainerPayload(writer,record.Object):io['Write'+input.kind+'Payload'](writer,input.version??18,record.Object);}finally{text.hook=null;}break;
        }
        case 'snapshot':break;
        default:throw new Error('Unknown database payload command.');
      }
    }catch(e){error=PayloadFailure(e);}
    writer.Flush();
    // Snapshot immediately: later commands must not rewrite earlier observations.
    return {ok:true,value:structuredClone({result,error,record:{model:model(record.Object),...headerResult()},
      pending:{tables:context.dataTableReferences.map(([t,r,c])=>({table:ref(t),rows:r,columns:c.map(v=>({type:v.Type,name:v.Name,values:wire(v.Values)}))})),
        lights:context.lightListReferences.map(([l,entries])=>({list:ref(l),entries:entries.map(([handle,name])=>[handle,wire(name)])})),indexes:Array.from(context.pendingLayerIndexes,([index,entries])=>({index:ref(index),entries}))},
      seed:document.DrawingVariables.HandleSeed,apps:Array.from(document.ApplicationRegistries,a=>({name:a.Name,handle:a.Handle})),classes:wire(document.Classes),
      output:bytesToBase64(binary?bytes.ToArray():new TextEncoder().encode(text.value))})};
  });
}
