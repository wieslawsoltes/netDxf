// Observation and input transport only. Production objects own all dispatch and mutation.
import * as api from '../index.js';
import * as io from '../runtime/DatabasePayloadIO.js';
import { PeekDocumentObjects } from '../netDxf/DxfDocument.Objects.js';
import { BinaryCodeValueReader } from '../netDxf/IO/BinaryCodeValueReader.js';
import { BinaryCodeValueWriter } from '../netDxf/IO/BinaryCodeValueWriter.js';
import { TextCodeValueReader } from '../netDxf/IO/TextCodeValueReader.js';
import { TextCodeValueWriter } from '../netDxf/IO/TextCodeValueWriter.js';
import { WireInput,PayloadFailure } from './database-payload-values.mjs';
import { wire } from './model-wire.mjs';
import { doubleBits,bytesToBase64 } from './wire.mjs';
import { Culture } from '../runtime/GeometryRuntime.js';
import { InvalidOperationException,NullReferenceException } from '../runtime/Errors.js';
const ref=item=>item==null?null:{type:item.constructor.name,handle:item.Handle};
const value=(v,code)=>v instanceof Uint8Array?bytesToBase64(v):typeof v==='bigint'?{wide:String(v)}:typeof v==='number'&&api.DxfGroupCode.GetValueType(code)===api.DxfTagValueType.Double?{bits:doubleBits(v)}:v;
const tag=t=>({code:t.Code,value:value(t.Value,t.Code)});
const model=item=>item==null?null:{type:item.constructor.name,code:item.CodeName,handle:item.Handle,owner:ref(item.Owner),extension:ref(item.ExtensionDictionary),reactors:Array.from(item.PersistentReactors,ref),
  entries:item instanceof api.DxfDictionary?Array.from(item.Entries,e=>({name:e.Name,target:ref(e.Target),hard:e.IsHardOwner})):null,
  fallback:ref(item instanceof api.DxfDictionaryWithDefault?item.Default:null),hard:item instanceof api.DxfDictionary?item.IsHardOwner:null,cloning:item instanceof api.DxfDictionary||item instanceof api.DxfXRecord?item.Cloning:null,
  data:item instanceof api.DxfXRecord?Array.from(item.Data,tag):null,payload:item.Payload||item.Tags?Array.from(item.Payload??item.Tags,tag):null,schema:item instanceof api.DxfDictionaryVariable?item.Schema:null,value:item instanceof api.DxfDictionaryVariable?item.Value:null,
  references:item instanceof api.DxfDatabaseObject?Array.from(item.DatabaseReferences,ref):null,
  xdata:Array.from(item.XData.Values,v=>({name:v.ApplicationRegistry.Name,handle:v.ApplicationRegistry.Handle,records:Array.from(v.XDataRecord,t=>({code:t.Code,value:value(t.Value,t.Code)}))}))};
export function objectGraphIOCall(input){
  Culture.Current='';api.BlockRecord.DefaultUnits=0;api.Insert.DefaultInsUnits=0;
  const doc=new api.DxfDocument(input.version),context=new io.DatabaseIOContext(doc),line=new api.Line(),light=new api.Light();doc.Entities.Add(line);doc.Entities.Add(light);
  const resources={line,light,block:line.Owner.Record,style:doc.TextStyles.get_Item('Standard'),layer:doc.Layers.get_Item('0'),groups:doc.Groups,layouts:doc.Layouts,mlineStyles:doc.MlineStyles,images:doc.ImageDefinitions,dgn:doc.UnderlayDgnDefinitions,dwf:doc.UnderlayDwfDefinitions,pdf:doc.UnderlayPdfDefinitions};
  const get=name=>Object.hasOwn(resources,name)?resources[name]:doc.GetObjectByHandle(name),read=v=>WireInput(v,get);
  if(input.admitResources)for(const item of doc.AddedObjects.Values)if(item.Handle!=='0'){
    const source=new io.SourceRecordIdentity();source.Handle=BigInt('0x'+item.Handle);source.IdentitySeen=true;context.sourceObjectIdentities.add(source.Handle);context.RecordSourceObject(item,source);
  }
  const binary=input.mode!=='text',legacy=input.mode==='legacy',inputBytes=new api.MemoryStream(),outputBytes=new api.MemoryStream();
  const host=()=>({text:'',count:0,hook:null,WriteLine(v){this.hook?.(++this.count);this.text+=String(v??'')+'\n';},Flush(){}}),inputText=host(),outputText=host();
  const make=(bytes,text)=>binary?new BinaryCodeValueWriter(bytes,legacy):new TextCodeValueWriter(text),sourceWriter=make(inputBytes,inputText),writer=make(outputBytes,outputText);
  for(const [code,value]of input.tags)sourceWriter.Write(code,read(value));sourceWriter.Flush();inputBytes.Position=0;
  const inner=binary?new BinaryCodeValueReader(inputBytes,undefined,legacy):new TextCodeValueReader(inputText.text);
  const reader=new io.DatabaseMetadataReader(inner,context.entityDatabaseMetadata,context.sourceObjectIdentities);context.Chunk=reader;
  return input.steps.map(step=>{
    let result=null,error=null;const target=step.target??'item';
    try{
      switch(step.method){
        case 'next':reader.Next();break;
        case 'read':{
          const kind=step.kind??'record',value=kind==='dictionary'?io.ReadDictionaryDatabaseRecord(context):kind==='xrecord'?io.ReadXRecordDatabaseRecord(context):io.ReadDatabaseRecord(context),id=step.id??'item';
          if(context.databaseRecords.Count)resources[id]=context.databaseRecords.get_Item(context.databaseRecords.Count-1).Object;
          if(kind==='dictionary'&&value!==null){context.dictionaries.Add(value.Handle,value);if(step.root)context.namedDictionary=value;}
          if(kind==='dictionary'&&value!==null)result={handle:value.Handle,hard:value.IsHardOwner,cloning:value.Cloning,entries:Array.from(value.Entries,e=>({key:e.Key,value:e.Value}))};
          else if(kind==='xrecord'&&value!==null)result={handle:value.Handle,owner:value.OwnerHandle,code:value.Codename,flags:value.Flags,entries:Array.from(value.Entries,tag)};break;
        }
        case 'import':io.ImportDatabaseObjects(context);break;
        case 'lookup':result=ref(context.GetObjectBySourceHandle(read(step.handle)));break;
        case 'validate':result=Array.from(doc.Objects.Validate());break;
        case 'transport':io.ValidateDatabaseTransport(doc,binary);break;
        case 'prepare':io.PrepareDatabaseClasses(doc,doc.Classes);break;
        case 'generated':{const projection=new api.DictionaryObject(null);for(const [handle,name]of step.entries)projection.Entries.Add(read(handle),read(name));resources.generated=projection;break;}
        case 'class':{const item=new api.DxfClass(step.name,step.cpp,'Probe');item.IsEntity=step.entity??false;item.InstanceCount=9;doc.Classes.Add(item);break;}
        case 'set':{const item=target==='variables'?doc.DrawingVariables:get(target);if(item==null)throw new NullReferenceException();item[step.property]=read(step.value);break;}
        case 'reactor':get(target).PersistentReactors.Add(read(step.value));break;
        case 'manage':context.managedReactorHandles.add(read(step.value));break;
        case 'replace':doc.AddedObjects.set_Item(get(target).Handle,new api.DxfPlaceholder());break;
        case 'write':case 'metadata':{
          outputText.count=0;outputText.hook=count=>{if(count===step.throwAt)throw new InvalidOperationException('Injected object writer callback.');if(count===step.versionAt)doc.DrawingVariables.AcadVer=step.newVersion;};
          try{if(step.method==='write')io.WriteDatabaseObject(writer,doc,get(target),step.generated?resources.generated:null);else io.WriteDatabaseMetadata(writer,doc,get(target),step.automatic?.map(read)??null);}finally{outputText.hook=null;}break;
        }
        case 'snapshot':break;
        default:throw new InvalidOperationException('Unknown object graph operation '+step.method);
      }
    }catch(e){error=PayloadFailure(e);}
    writer.Flush();const db=PeekDocumentObjects(doc);
    return {ok:true,value:structuredClone({result,error,reader:{code:reader.Code,value:value(reader.Value,reader.Code),position:reader.CurrentPosition},
      records:Array.from(context.databaseRecords,r=>({item:model(r.Object),owner:r.Metadata.Owner,extension:r.Metadata.Extension,reactors:r.Metadata.Reactors,entries:r.Entries,fallback:r.Default,source:{handle:String(r.SourceIdentity.Handle),ambiguous:r.SourceIdentity.Ambiguous}})),
      objects:db===null?null:Array.from(db.Items,model),root:ref(db?.Root),seed:doc.DrawingVariables.HandleSeed,registry:Array.from(doc.AddedObjects,e=>({key:e.Key,item:ref(e.Value)})),classes:wire(doc.Classes),pendingStyles:context.mleaderReferences.length,output:bytesToBase64(binary?outputBytes.ToArray():new TextEncoder().encode(outputText.text))})};
  });
}
