// Complete port of pinned LightNameTests.cs.
import { Light, DxfDocument, DxfVersion, DxfTag, MemoryStream } from '../../index.js';
import { ArgumentException, ArgumentNullException, InvalidDataException } from '../../runtime/Errors.js';
import { Run, Equal, Throws, Check, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import { LightTags } from './LightTests.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
export function RegisterLightNameTests() {
  Run('light/name/nonmutating-validation', () => {
    const light = new Light(); light.Name = 'original';
    Throws(ArgumentNullException, () => { light.Name = null; });
    for (const name of ['\r','\n','\0','A\rB','A\nB','A\0B','\r\n']) {
      Throws(ArgumentException, () => { light.Name = name; });
      Equal('original', light.Name, 'Rejected name mutated state');
    }
  });
  for(const v of SupportedVersions)for(const b of [false,true]){
    const id=`${VersionName(v)}/${BooleanName(b)}`;
    Run(`light/name/encoded-delimiters/${id}`,()=>{
      for(const name of ['A\\U+000DB','A\\U+000AB','A\\U+0000B']){
        const tags=LightTags(v,1,false),at=tags.findIndex(t=>t.Code===1&&t.Value.startsWith('Lamp'));tags[at]=new DxfTag(1,name);
        const stream=new MemoryStream(RawFixtureBytes(tags,b));try{if(GetTypedIOConfiguration()==='Debug')Throws(InvalidDataException,()=>DxfDocument.Load(stream));else Check(DxfDocument.Load(stream)===null,'Invalid decoded name accepted.');Check(stream.CanRead,'Invalid name closed caller stream.');}finally{stream.Dispose();}
      }
    });
    if(v<DxfVersion.AutoCad2007)continue;
    Run(`light/name/valid-persistence/${id}`,()=>{
      for(const name of ['', 'A\tB', 'Żółć 灯', 'A\\U+1234B', 'A\\U+000AB', 'C:\\tmp', '\\u+0041', 'x'.repeat(300)]){
        const light=new Light();light.Name=name;const clone=light.Clone();Equal(name,clone.Name,'Cloned name');const doc=new DxfDocument(v);doc.Entities.Add(light);const stream=new MemoryStream();try{Check(doc.Save(stream,b),'Valid name save failed.');stream.Position=0;const restored=DxfDocument.Load(stream);Check(restored!==null,'Valid name reload failed.');const lights=Array.from(restored.Entities.Lights);Equal(1,lights.length,'Single');Equal(name,lights[0].Name,'Round-trip name');}finally{stream.Dispose();}
      }
    });
  }
}
