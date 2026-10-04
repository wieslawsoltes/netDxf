// Port of every original case and assertion in pinned SplineFlagTests.cs.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DxfDocument, DxfRawDocument, Spline, SplineKnotParameterization, Line, Vector3, MemoryStream, XData, ApplicationRegistry, XDataRecord, XDataCode } from '../../index.js';
import { InvalidOperationException } from '../../runtime/Errors.js';
import { Run, Check, Equal, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
const artifactDirectory=path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../artifacts/conformance/fixtures');
export function RegisterSplineFlagTests() {
  for(const v of SupportedVersions)for(const b of [false,true])for(let kind=0;kind<5;kind++)for(const [name,parameter] of Object.entries(SplineKnotParameterization))
    Run(`spline/flags/${VersionName(v)}/${BooleanName(b)}/${kind}/${name}`,()=>SplineFlags(v,b,kind,parameter));
}
export function FlagSpline(kind) {
  const points=[new Vector3(0,0,0),new Vector3(3,7,2),new Vector3(9,4,-1),new Vector3(12,2,6)];
  if(kind===1||kind===4)points[3]=points[0];
  const spline=kind>=3?new Spline(points):new Spline(points,[1,.75,1.25,1],2,kind===2);
  spline.StartTangent=new Vector3(2,3,4);spline.EndTangent=new Vector3(5,-6,7);
  const data=new XData(new ApplicationRegistry('SPLINE_FLAGS'));data.XDataRecord.Add(new XDataRecord(XDataCode.Int16,kind));spline.XData.Add(data);return spline;
}
export function SplineFlags(version,binary,kind,parameter) {
  const original=FlagSpline(kind);original.KnotParameterization=parameter;
  let expected=4|parameter;if(kind===1||kind===4)expected|=1;if(kind===2)expected|=1|2|2048;if(kind>=3)expected|=1024;
  let doc=new DxfDocument(version);doc.Entities.Add(original);doc.Entities.Add(original.Clone());doc.Entities.Add(new Line(new Vector3(20,30,40),new Vector3(50,60,70)));
  for(let cycle=0;cycle<3;cycle++) {
    const stream=new MemoryStream(),transport=cycle%2===0?binary:!binary;Check(doc.Save(stream,transport),'Spline flags save failed.');stream.Position=0;
    const raw=DxfRawDocument.Load(stream);
    for(const record of Array.from(raw.Sections).flatMap(s=>Array.from(s.Records)).filter(r=>r.Name==='SPLINE')) {
      const masks=Array.from(record.Tags).filter(t=>t.Code===70);Equal(1,masks.length,'Single SPLINE flag mask');Equal(expected,masks[0].Value,'Independent SPLINE flag mask');
    }
    if(cycle===0&&parameter===SplineKnotParameterization.FitChord){fs.mkdirSync(artifactDirectory,{recursive:true});fs.writeFileSync(path.join(artifactDirectory,`spline-flags-${VersionName(version)}-${BooleanName(binary)}-${kind}.dxf`),stream.ToArray());}
    stream.Position=0;doc=DxfDocument.Load(stream);if(!doc)throw new InvalidOperationException('Spline flag reload failed.');
    for(const spline of doc.Entities.Splines) {
      for(const name of ['CreationMethod','IsClosed','IsClosedPeriodic'])Equal(original[name],spline[name],name+' changed');
      Equal(parameter,spline.KnotParameterization,'Parameterization flag changed');
      for(const name of ['Knots','ControlPoints','Weights','FitPoints']) {
        const first=Array.from(original[name]),next=Array.from(spline[name]);
        Check(first.length===next.length&&first.every((v,i)=>typeof v?.Equals==='function'?v.Equals(next[i]):v===next[i]),'Flag update changed '+name);
      }
      Check(original.StartTangent.Equals(spline.StartTangent),'Start tangent changed');Check(original.EndTangent.Equals(spline.EndTangent),'End tangent changed');
      const records=Array.from(spline.XData.get_Item('SPLINE_FLAGS').XDataRecord);Equal(1,records.length);Equal(kind,records[0].Value,'Following XData lost');
    }
    const lines=Array.from(doc.Entities.Lines);Equal(1,lines.length);Check(new Vector3(20,30,40).Equals(lines[0].StartPoint),'Following LINE changed');Check(stream.CanRead,'Flag serialization closed caller stream.');stream.Dispose();
  }
}
