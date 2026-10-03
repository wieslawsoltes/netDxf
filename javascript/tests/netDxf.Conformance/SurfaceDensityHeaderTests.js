// Port of pinned SurfaceDensityHeaderTests.cs; all original cases and assertions retained.
import { DxfDocument, MemoryStream } from '../../index.js';
import { NewCodeWriter } from '../support/CodecFactory.js';
import { ReadRawHeader } from './CustomHeaderUnicodeTests.js';
import { Run, Check, Equal, SupportedVersions, VersionName, HeaderVersion, BooleanName } from './TestHarness.js';
const orders=[['$SPLINESEGS','$SURFU','$SURFV'],['$SPLINESEGS','$SURFV','$SURFU'],['$SURFU','$SPLINESEGS','$SURFV'],['$SURFU','$SURFV','$SPLINESEGS'],['$SURFV','$SPLINESEGS','$SURFU'],['$SURFV','$SURFU','$SPLINESEGS']];
export function RegisterSurfaceDensityHeaderTests(){for(const v of SupportedVersions)for(const b of [false,true]){const suffix=`${VersionName(v)}/${BooleanName(b)}`;
  for(let order=0;order<orders.length;order++)Run(`header/surface-density/order/${suffix}/${order}`,()=>SurfaceHeaderOrder(v,b,order));
  Run(`header/surface-density/omitted/${suffix}`,()=>SurfaceHeaderOmitted(v,b));Run(`header/surface-density/existing-recovery/${suffix}`,()=>SurfaceHeaderRecovery(v,b));Run(`header/surface-density/all-admitted-values/${suffix}`,()=>SurfaceHeaderRoundTrips(v,b));
}}
function read(version,binary,values){const stream=new MemoryStream();try{const writer=NewCodeWriter(stream,binary),T=(code,value)=>writer.Write(code,value);
  T(0,'SECTION');T(2,'HEADER');T(9,'$ACADVER');T(1,HeaderVersion(version));T(9,'$DWGCODEPAGE');T(3,'ANSI_1252');
  for(const [name,value]of values){T(9,name);if(!binary)T(999,'surface density value follows');T(70,value);}T(0,'ENDSEC');T(0,'EOF');writer.Flush();stream.Position=0;
  const doc=DxfDocument.Load(stream);Check(doc!==null,'Surface HEADER failed to load.');Check(stream.CanRead,'Surface HEADER load closed caller stream.');return doc;
}finally{stream.Dispose();}}
function verify(doc,u,v,spline){Equal(u,doc.DrawingVariables.SurfU,'$SURFU surface density');Equal(v,doc.DrawingVariables.SurfV,'$SURFV surface density');Equal(spline,doc.DrawingVariables.SplineSegs,'$SPLINESEGS must not be overwritten by surface density');}
export function SurfaceHeaderOrder(version,binary,order){for(const [u,v]of [[2,200],[200,2],[13,29],[199,3],[6,6]]){const values={'$SURFU':u,'$SURFV':v,'$SPLINESEGS':17};verify(read(version,binary,orders[order].map(n=>[n,values[n]])),u,v,17);}}
export function SurfaceHeaderOmitted(v,b){verify(read(v,b,[]),6,6,8);verify(read(v,b,[['$SURFU',13]]),13,6,8);verify(read(v,b,[['$SURFV',29]]),6,29,8);verify(read(v,b,[['$SPLINESEGS',17]]),6,6,17);}
export function SurfaceHeaderRecovery(v,b){for(const raw of [-32768,-1,0,1,201,32767]){verify(read(v,b,[['$SPLINESEGS',17],['$SURFU',raw],['$SURFV',29]]),6,29,17);verify(read(v,b,[['$SPLINESEGS',17],['$SURFU',13],['$SURFV',raw]]),13,6,17);}}
export function SurfaceHeaderRoundTrips(version,binary){for(let u=2;u<=200;u++){const v=202-u;let doc=new DxfDocument(version);doc.DrawingVariables.SurfU=u;doc.DrawingVariables.SurfV=v;doc.DrawingVariables.SplineSegs=237;
  for(let cycle=0;cycle<2;cycle++){const transport=cycle===0?binary:!binary,output=new MemoryStream();try{Check(doc.Save(output,transport),'Surface header save failed.');const raw=ReadRawHeader(output.ToArray(),transport);
    for(const [name,value]of [['$SURFU',u],['$SURFV',v],['$SPLINESEGS',237]]){const tags=raw.get(name);Equal(1,tags.length,'Single header value');Equal(70,tags[0].Code,'Surface HEADER group code');Equal(value,tags[0].Value,'Surface HEADER exact wire value');}
    verify(doc,u,v,237);output.Position=0;doc=DxfDocument.Load(output);Check(doc!==null,'Surface header reload failed.');verify(doc,u,v,237);Check(output.CanRead,'Surface round trip closed caller stream.');
  }finally{output.Dispose();}}
}}
