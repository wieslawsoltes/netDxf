import { TextCodeValueReader } from '../../netDxf/IO/TextCodeValueReader.js';
import { TextCodeValueWriter } from '../../netDxf/IO/TextCodeValueWriter.js';
import { BinaryCodeValueReader } from '../../netDxf/IO/BinaryCodeValueReader.js';
import { BinaryCodeValueWriter } from '../../netDxf/IO/BinaryCodeValueWriter.js';
// Complete pinned UcsElevationTests.cs, preserving the existing detached-model cases.
import { UCS, Vector3, DxfDocument, MemoryStream } from '../../index.js';
import { ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { Run, Near, Equal, Throws, Check, SupportedVersions, VersionName, BooleanName, HeaderVersion } from './TestHarness.js';
export function RegisterUcsElevationTests() {
  Run('ucs/elevation/defaults',()=>{
    Near(0,new UCS('Default').Elevation,'default elevation');
    Near(0,new UCS('Axes',Vector3.Zero,Vector3.UnitX,Vector3.UnitY).Elevation,'axis constructor elevation');
    Near(0,UCS.FromNormal('Normal',Vector3.Zero,Vector3.UnitZ).Elevation,'factory elevation');
  });
  Run('ucs/elevation/finite-values',()=>{
    const ucs=new UCS('Finite');ucs.Elevation=-12.5;
    for(const invalid of [NaN,-Infinity,Infinity]) { Throws(ArgumentOutOfRangeException,()=>{ucs.Elevation=invalid;});Near(-12.5,ucs.Elevation,'invalid assignment changed elevation'); }
  });
  Run('ucs/elevation/clone-and-origin',()=>{
    const source=new UCS('Source',new Vector3(1,2,3),Vector3.UnitY,Vector3.Negate(Vector3.UnitX));source.Elevation=44.125;
    const copy=source.Clone('Copy');Near(source.Elevation,copy.Elevation,'clone elevation');Equal(source.Origin,copy.Origin,'clone origin');Equal(source.GetTransformation(),copy.GetTransformation(),'clone axes');
    copy.Elevation=-9;Near(44.125,source.Elevation,'source elevation isolation');Equal(source.Origin,copy.Origin,'elevation must not move origin');
  });
  for(const v of SupportedVersions)for(const b of [false,true]){const suffix=`${VersionName(v)}/${BooleanName(b)}`;
    Run(`ucs/elevation/document/${suffix}`,()=>UcsElevationRoundTrip(v,b));Run(`ucs/elevation/authored/${suffix}`,()=>UcsElevationAuthoredFixture(v,b));
    Run(`ucs/elevation/missing/${suffix}`,()=>{const stream=UcsElevationFixture(v,b,null);try{const loaded=DxfDocument.Load(stream);Check(loaded!==null,'Missing-elevation fixture failed.');Near(0,loaded.UCSs.get_Item('Fixture').Elevation,'omitted elevation default');}finally{stream.Dispose();}});
  }

}

export function UcsCodeReader(stream,binary){return binary?new BinaryCodeValueReader(stream):new TextCodeValueReader(new TextDecoder().decode(stream.ToArray()));}
export function UcsCodeWriter(stream,binary){return binary?new BinaryCodeValueWriter(stream):new TextCodeValueWriter(stream);}
export function UcsElevationRoundTrip(version,binary){for(const value of [0,-12.5,72.125,-1.25e-6,1e7]){const document=new DxfDocument(version),ucs=new UCS('Elevated',new Vector3(3,4,5),Vector3.UnitY,Vector3.Negate(Vector3.UnitX)),stream=new MemoryStream();ucs.Elevation=value;document.UCSs.Add(ucs);try{Check(document.Save(stream,binary),'UCS fixture save failed.');stream.Position=0;const loaded=DxfDocument.Load(stream);Check(loaded!==null,'UCS fixture load failed.');const copy=loaded.UCSs.get_Item(ucs.Name);Near(value,copy.Elevation,'UCS elevation round trip');Equal(ucs.Origin,copy.Origin,'UCS origin round trip');Equal(ucs.XAxis,copy.XAxis,'UCS X axis round trip');Equal(ucs.YAxis,copy.YAxis,'UCS Y axis round trip');Check(loaded.GetObjectByHandle(copy.Handle)===copy,'UCS handle lookup failed.');stream.Position=0;const reader=UcsCodeReader(stream,binary);let found=false,record='';reader.Next();while(!(reader.Code===0&&reader.ReadString()==='EOF')){if(reader.Code===0)record=reader.ReadString();if(record==='UCS'&&reader.Code===146){Near(value,reader.ReadDouble(),'written group 146');found=true;}reader.Next();}Check(found,'Writer omitted UCS elevation group 146.');}finally{stream.Dispose();}}}
export function UcsElevationAuthoredFixture(version,binary){const stream=UcsElevationFixture(version,binary,-17.625);try{const loaded=DxfDocument.Load(stream);Check(loaded!==null,'Authored UCS fixture failed.');const ucs=loaded.UCSs.get_Item('Fixture');Near(-17.625,ucs.Elevation,'authored elevation');Equal(new Vector3(1,2,3),ucs.Origin,'authored origin');Equal(Vector3.UnitX,ucs.XAxis,'authored X axis');}finally{stream.Dispose();}}
export function UcsElevationFixture(version,binary,elevation){const stream=new MemoryStream(),writer=UcsCodeWriter(stream,binary),tag=(c,v)=>writer.Write(c,v);
  tag(0,'SECTION');tag(2,'HEADER');tag(9,'$ACADVER');tag(1,HeaderVersion(version));tag(9,'$DWGCODEPAGE');tag(3,'ANSI_1252');tag(9,'$HANDSEED');tag(5,'FFFF');tag(0,'ENDSEC');
  tag(0,'SECTION');tag(2,'TABLES');tag(0,'TABLE');tag(2,'UCS');tag(5,'A');tag(330,'0');tag(100,'AcDbSymbolTable');tag(70,1);tag(0,'UCS');tag(5,'B');tag(330,'A');tag(100,'AcDbSymbolTableRecord');tag(100,'AcDbUCSTableRecord');tag(2,'Fixture');tag(70,0);if(elevation!==null)tag(146,elevation);
  for(const [c,v]of[[10,1],[20,2],[30,3],[11,1],[21,0],[31,0],[12,0],[22,1],[32,0],[79,0]])tag(c,v);
  tag(0,'ENDTAB');tag(0,'ENDSEC');tag(0,'SECTION');tag(2,'OBJECTS');tag(0,'DICTIONARY');tag(5,'C');tag(330,'0');tag(100,'AcDbDictionary');tag(280,0);tag(281,1);tag(0,'ENDSEC');tag(0,'EOF');writer.Flush();stream.Position=0;return stream;
}
