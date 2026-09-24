// Actual metadata-reader and identity-context observations; no synthesized source evidence.
import * as api from '../index.js';
import { SourceIdentityContext,DatabaseMetadataReader } from '../runtime/DatabasePayloadIO.js';
import { GenericDictionary } from '../runtime/GenericDictionary.js';
import { TableNameComparer } from '../netDxf/Collections/TableObjects.js';
import { TextCodeValueReader } from '../netDxf/IO/TextCodeValueReader.js';
import { TextCodeValueWriter } from '../netDxf/IO/TextCodeValueWriter.js';
import { BinaryCodeValueReader } from '../netDxf/IO/BinaryCodeValueReader.js';
import { BinaryCodeValueWriter } from '../netDxf/IO/BinaryCodeValueWriter.js';
import { PayloadFailure,WireInput } from './database-payload-values.mjs';
import { wire } from './model-wire.mjs';
export function sourceMetadataCall(input){
  const document=new api.DxfDocument(18),line=new api.Line();document.Entities.Add(line);
  const resources={line,root:document.NamedObjects,style:document.DimensionStyles.get_Item('Standard')},context=new SourceIdentityContext(document);
  const read=value=>WireInput(value,name=>resources[name]);
  const bytes=new api.MemoryStream(),text={value:'',WriteLine(v){this.value+=String(v??'')+'\n';},Flush(){}};
  const writer=input.mode==='text'?new TextCodeValueWriter(text):new BinaryCodeValueWriter(bytes,input.mode==='legacy');
  for(const [code,v]of input.tags)writer.Write(code,read(v));writer.Flush();bytes.Position=0;
  const inner=input.mode==='text'?new TextCodeValueReader(text.value):new BinaryCodeValueReader(bytes,undefined,input.mode==='legacy');
  // Map-compatible adapter retains the source's ordinal-ignore-case metadata keys.
  const entries=new GenericDictionary(0,TableNameComparer,'string'),records={set(k,v){entries.set_Item(k,v);}};
  const reader=new DatabaseMetadataReader(inner,records,context.sourceObjectIdentities);context.Chunk=reader;
  const identity=s=>({handle:String(s.Handle),seen:s.IdentitySeen,ambiguous:s.Ambiguous});
  return input.steps.map(step=>{
    let result=null,error=null;
    try{
      switch(step.method){
        case 'next':reader.Next();break;
        case 'skip':reader.SetSkipComments(step.value);break;
        case 'accept':context.RecordSourceObject(resources[step.target],context.CurrentSourceRecord);break;
        case 'validate':context.ValidateSourceIdentityDeclarations();break;
        case 'lookup':{const value=context.GetObjectBySourceHandle(read(step.handle),step.includeMetadata??false);result=value===null?null:{type:value.constructor.name,handle:value.Handle};break;}
        case 'dictionary':result=context.IsAcceptedSourceDictionary(read(step.handle));break;
        case 'replace':document.AddedObjects.set_Item(resources[step.target].Handle,new api.DxfPlaceholder());break;
        case 'cast':result=wire(reader[step.member]());break;
      }
    }catch(e){error=PayloadFailure(e);}
    return {ok:true,value:{result,error,code:reader.Code,position:reader.CurrentPosition,value:wire(reader.Value),current:identity(reader.SourceRecord),
      declared:Array.from(context.sourceObjectIdentities,n=>String(n)),accepted:Array.from(context.acceptedSourceRecords,([key,s])=>({key:String(key),...identity(s)})),
      metadata:Array.from(entries,p=>({key:p.Key,owner:p.Value.Owner,extension:p.Value.Extension,reactors:p.Value.Reactors.slice()}))}};
  });
}
