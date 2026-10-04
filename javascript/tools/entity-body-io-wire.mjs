// Test-only orchestration; all parsing, mutation and writing use production APIs.
import * as api from '../index.js';
import * as codec from '../runtime/DxfTransport.js';
import { BinaryCodeValueReader } from '../netDxf/IO/BinaryCodeValueReader.js';
import { BinaryCodeValueWriter } from '../netDxf/IO/BinaryCodeValueWriter.js';
import { TextCodeValueReader } from '../netDxf/IO/TextCodeValueReader.js';
import { TextCodeValueWriter } from '../netDxf/IO/TextCodeValueWriter.js';
import { DxfGroupCode, DxfTagValueType as T } from '../netDxf/IO/DxfGroupCode.js';
import { sectionInput } from './transport-sections-wire.mjs';
import { wire } from './model-wire.mjs';
import { resolve } from './model-types.mjs';
import { Culture } from '../runtime/GeometryRuntime.js';
import { bytesToBase64, doubleBits } from './wire.mjs';
import { ArgumentException, NullReferenceException, InvalidOperationException } from '../runtime/Errors.js';
const failure=e=>({type:e.name,param:e.ParamName??null,message:e instanceof ArgumentException||e instanceof NullReferenceException?null:e.Message??e.message,
 inner:e.InnerException?{type:e.InnerException.name,param:e.InnerException.ParamName??null}:null,version:e.Version??null});
const value=(v,type)=>v==null?null:v instanceof Uint8Array?bytesToBase64(v):typeof v==='bigint'?{wide:String(v)}:typeof v==='number'&&type===T.Double?{bits:doubleBits(v)}:v;
export function entityBodyIOCall(input){
  Culture.Current=input.culture??'';api.BlockRecord.DefaultUnits=0;api.Insert.DefaultInsUnits=0;
  const document=new api.DxfDocument(input.version),binary=input.mode!=='text',legacy=input.mode==='legacy';
  const sourceBytes=new api.MemoryStream(),outputBytes=new api.MemoryStream();
  const textHost=()=>({text:'',WriteLine(v){this.text+=String(v??'')+'\n';},Flush(){}}),sourceText=textHost(),outputText=textHost();
  const make=(b,t)=>binary?new BinaryCodeValueWriter(b,legacy):new TextCodeValueWriter(t),sw=make(sourceBytes,sourceText),writer=make(outputBytes,outputText);
  for(const [code,v] of input.tags)sw.Write(code,sectionInput(v));sw.Flush();sourceBytes.Position=0;
  const reader=binary?new BinaryCodeValueReader(sourceBytes,undefined,legacy):new TextCodeValueReader(sourceText.text);
  let writerHook=null,writeCalls=0;const emit=outputText.WriteLine.bind(outputText);outputText.WriteLine=value=>{writeCalls++;writerHook?.(writeCalls);emit(value);};
  let lastType=null,entity=null;const next=reader.Next.bind(reader);reader.Next=()=>{next();lastType=DxfGroupCode.GetValueType(reader.Code);};
  function read(v){if(v&&typeof v==='object'&&'new' in v)return new (resolve(v.new))(...(v.args??[]).map(read));if(v&&typeof v==='object'&&'array' in v)return v.array==='Byte'?Uint8Array.from(v.values.map(read)):v.values.map(read);if(v&&typeof v==='object'&&'enum' in v)return v.value;return sectionInput(v);}
  return input.steps.map(step=>{
    let error=null;
    try{
      switch(step.method){
        case 'next':reader.Next();break;
        case 'read':entity=codec['Read'+input.kind](reader,document,input.kind==='Spline'?(step.stopAtHelix??false):input.entityCode);break;
        case 'write':{
          writeCalls=0;writerHook=count=>{for(const hook of step.hooks??[])if(hook.at===count){
            if(hook.kind==='throw')throw new InvalidOperationException('Injected body writer callback.');
            if(hook.kind==='control')entity.ControlPoints[hook.index]=read(hook.value);
            else entity[hook.property]=read(hook.value);
          }};
          try{codec['Write'+input.kind](writer,input.version,entity,...(input.kind==='Spline'?[step.writeXData??true]:[]));}finally{writerHook=null;}break;
        }
        case 'construct':entity=read(step.value);break;
        case 'set':entity[step.property]=read(step.value);break;
        case 'place':
          if(step.where==='model')document.Entities.Add(entity);
          else if(step.where==='paper')document.Layouts.Add(new api.Layout('BodySheet')).AssociatedBlock.Entities.Add(entity);
          else{const block=new api.Block('UnusedBody');block.Entities.Add(entity);document.Blocks.Add(block);}break;
        case 'class':{const definition=new api.DxfClass(step.name,step.cpp,step.app??'Probe');definition.IsEntity=step.isEntity;definition.InstanceCount=step.count??null;document.Classes.Add(definition);break;}
        case 'prepare':codec.PrepareHelixClass(document,document.Classes);break;
        case 'validate':codec[input.kind==='Helix'?'ValidateHelixVersions':input.kind==='Light'?'ValidateLightVersions':input.kind==='LwPolyline'?'ValidateLwPolylineFidelity':'ValidateAcisEntities'](document);break;
        default:throw new InvalidOperationException('Unknown entity codec operation '+step.method);
      }
    }catch(e){error=failure(e);}
    writer.Flush();
    return {ok:true,value:{error,reader:{code:reader.Code,value:value(reader.Value,lastType),position:reader.CurrentPosition},entity:wire(entity),output:bytesToBase64(binary?outputBytes.ToArray():new TextEncoder().encode(outputText.text)),
      apps:Array.from(document.ApplicationRegistries,app=>({name:app.Name,handle:app.Handle})),seed:document.DrawingVariables.HandleSeed,...(input.captureClasses?{classes:wire(document.Classes)}:{})}};
  });
}
