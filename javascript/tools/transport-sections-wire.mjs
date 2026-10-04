// Test-only orchestration. Production codecs/helpers perform all mutations and IO.
import * as api from '../index.js';
import { MemoryStream } from '../runtime/MemoryStream.js';
import { BinaryCodeValueReader } from '../netDxf/IO/BinaryCodeValueReader.js';
import { BinaryCodeValueWriter } from '../netDxf/IO/BinaryCodeValueWriter.js';
import { TextCodeValueReader } from '../netDxf/IO/TextCodeValueReader.js';
import { TextCodeValueWriter } from '../netDxf/IO/TextCodeValueWriter.js';
import { DxfTagValueType as T, DxfGroupCode } from '../netDxf/IO/DxfGroupCode.js';
import { DxfThumbnailImage } from '../netDxf/IO/DxfThumbnailImage.js';
import * as commonIO from '../netDxf/IO/DxfEntityCommonData.js';
import * as backgroundIO from '../netDxf/IO/DxfMTextBackground.js';
import { ValidateMeshOutput, ValidateDocumentMeshOutput } from '../netDxf/IO/DxfMeshWriteValidation.js';
import { ValidateMeshVersions } from '../netDxf/IO/DxfMeshVersion.js';
import { ArgumentException, NullReferenceException, InvalidOperationException } from '../runtime/Errors.js';
import { Culture } from '../runtime/GeometryRuntime.js';
import { fromBits, doubleBits, bytesToBase64 } from './wire.mjs';
export function sectionInput(value) {
  if (value == null || typeof value !== 'object') return value;
  if ('bytes' in value) return Uint8Array.from(value.bytes);
  if ('pattern' in value) return Uint8Array.from({length:value.pattern}, (_,i)=>i*73+19);
  if ('double' in value) return fromBits(value.double);
  if ('long' in value) return BigInt(value.long);
  if ('utf16' in value) return value.utf16.map(v=>String.fromCharCode(v)).join('');
  for (const kind of ['int','short','byte']) if (kind in value) return value[kind];
  throw new Error('Unknown section input.');
}
const wireValue=(value,type)=>value==null?null:value instanceof Uint8Array?bytesToBase64(value):typeof value==='bigint'?{wide:String(value)}:typeof value==='number'&&type===T.Double?{bits:doubleBits(value)}:value;
const failure=error=>({type:error.name,param:error.ParamName??null,message:error instanceof ArgumentException||error instanceof NullReferenceException?null:error.Message??error.message,
  inner:error.InnerException?{type:error.InnerException.name,param:error.InnerException.ParamName??null}:null,version:error.Version??null});
const commonSnapshot=data=>({color:data.ColorName,shadow:data.ShadowMode,seen:data.ColorNameSeen,declared:data.DeclaredLength,actual:data.ActualLength,proxy:wireValue(data.ProxyGraphics),
  payload:data.Payload===null?null:{bytes:bytesToBase64(data.Payload.ToArray()),open:data.Payload.CanRead}});
const backgroundSnapshot=b=>b===null?null:{flags:b.Flags,scale:wireValue(b.ScaleFactor,T.Double),index:b.ColorIndex,color:b.TrueColor,name:b.ColorName,transparency:b.Transparency};
export function transportSectionsCall(input) {
  Culture.Current=input.culture??'';api.BlockRecord.DefaultUnits=0;api.Insert.DefaultInsUnits=0;
  const binary=input.mode!=='text',legacy=input.mode==='legacy',version=input.version,document=new api.DxfDocument(version);
  const sourceBytes=new MemoryStream(),outputBytes=new MemoryStream();
  const textHost=()=>({text:'',WriteLine(value){this.text+=String(value??'')+'\n';},Flush(){}});
  const sourceText=textHost(),outputText=textHost();
  const makeWriter=(bytes,text)=>binary?new BinaryCodeValueWriter(bytes,legacy):new TextCodeValueWriter(text);
  const sourceWriter=makeWriter(sourceBytes,sourceText),writer=makeWriter(outputBytes,outputText);
  for (const [code,value] of input.tags) sourceWriter.Write(code,sectionInput(value));
  sourceWriter.Flush();sourceBytes.Position=0;
  const reader=binary?new BinaryCodeValueReader(sourceBytes,undefined,legacy):new TextCodeValueReader(sourceText.text);
  const data=new commonIO.EntityCommonDataReader(),background={value:null},entity=new api.Line();let mesh=null,lastType=null;
  const next=reader.Next.bind(reader);reader.Next=()=>{next();lastType=DxfGroupCode.GetValueType(reader.Code);};
  for (const [name,value] of Object.entries(input.common??{})) entity.CommonData[name]=sectionInput(value);
  if (input.background!=null) {background.value=new api.MTextBackgroundFill();for(const [name,value] of Object.entries(input.background)) background.value[name]=sectionInput(value);}
  if (input.mesh) {
    mesh=new api.Mesh(input.mesh.vertices.map(v=>new api.Vector3(...v.map(sectionInput))),input.mesh.faces);
    if(input.mesh.repeat){const face=Array(input.mesh.repeat.length).fill(0);for(let i=0;i<input.mesh.repeat.count;i++)mesh.Faces.Add(face);}
    for(const edge of input.mesh.edges)mesh.Edges.Add(edge===null?null:new api.MeshEdge(edge[0],edge[1],sectionInput(edge[2])));
  }
  if (input.placement) {
    const toAdd=input.target==='background'?Object.assign(new api.MText(),{BackgroundFill:background.value}):mesh??entity;
    if(['attribute','definition'].includes(input.placement)) {
      const block=new api.Block('Attributes'),definition=new api.AttributeDefinition('TAG');block.AttributeDefinitions.Add(definition);
      const insert=new api.Insert(block);document.Entities.Add(insert);
      const destination=(input.placement==='attribute'?insert.Attributes.get_Item(0):definition).CommonData;
      for(const name of ['ColorName','ShadowMode','ProxyGraphics'])destination[name]=entity.CommonData[name];
    }else if(input.placement==='model')document.Entities.Add(toAdd);
    else if(input.placement==='paper')document.Layouts.Add(new api.Layout('Sheet')).AssociatedBlock.Entities.Add(toAdd);
    else {const block=new api.Block('Unused');block.Entities.Add(toAdd);document.Blocks.Add(block);}
  }
  return input.steps.map(step=>{
    let result=null,error=null;
    try {
      switch(step.method) {
        case 'next':reader.Next();lastType=DxfGroupCode.GetValueType(reader.Code);break;
        case 'thumbnailRead':result=DxfThumbnailImage.Read(reader);lastType=DxfGroupCode.GetValueType(reader.Code);break;
        case 'thumbnailWrite':DxfThumbnailImage.Write(writer,sectionInput(step.data));break;
        case 'commonRead':commonIO.ReadEntityCommonData(reader,version,data);break;
        case 'complete':data.Complete();break;
        case 'backgroundRead':result=backgroundIO.TryReadMTextBackground(reader,background);break;
        case 'commonWrite':commonIO.WriteEntityCommonData(writer,version,entity.CommonData);break;
        case 'backgroundWrite':backgroundIO.WriteMTextBackground(writer,version,background.value);break;
        case 'validateCommon':commonIO.ValidateEntityCommonDataVersion(entity.CommonData,version);break;
        case 'validateCommonDocument':commonIO.ValidateEntityCommonDataVersions(document);break;
        case 'validateBackgroundDocument':backgroundIO.ValidateMTextBackgroundVersions(document);break;
        case 'meshValidate':ValidateMeshOutput(mesh,'Probe');break;
        case 'meshDocument':ValidateDocumentMeshOutput(document);break;
        case 'meshVersions':ValidateMeshVersions(document);break;
        default:throw new InvalidOperationException('Unknown section observation: '+step.method);
      }
    }catch(e){error=failure(e);}
    writer.Flush();
    // Thumbnail Read may have stopped after advancing the supplied reader.
    const type=lastType;
    return {ok:true,value:{result:wireValue(result),error,reader:{code:reader.Code,value:wireValue(reader.Value,type),position:reader.CurrentPosition},
      output:bytesToBase64(binary?outputBytes.ToArray():new TextEncoder().encode(outputText.text)),common:commonSnapshot(data),background:backgroundSnapshot(background.value)}};
  });
}
