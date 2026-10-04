// Port of all original cases/assertions in pinned HeaderCommentTests.cs.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DxfDocument, DxfVersion, Vector2, Vector3, MemoryStream } from '../../index.js';
import { UnwrapHeaderNumber } from '../../runtime/HeaderBox.js';
import { GetHeaderEnvironment } from '../../runtime/HeaderTime.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { EndOfStreamException, InvalidOperationException } from '../../runtime/Errors.js';
import { CustomHeaderValue } from './CustomHeaderUnicodeTests.js';
import { Run, Check, Equal, Near, Throws, SupportedVersions, VersionName, HeaderVersion } from './TestHarness.js';
const artifactDirectory=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../artifacts/conformance/fixtures');
export function RegisterHeaderCommentTests(){const gaps=HeaderCommentPairs(DxfVersion.AutoCad2018).length-1;
  for(const v of SupportedVersions){for(let gap=1;gap<=gaps;gap++)Run(`header/comments/single-gap/${VersionName(v)}/${gap}`,()=>HeaderCommentsLoad(v,gap,false));
    Run(`header/comments/all-gaps/${VersionName(v)}`,()=>HeaderCommentsLoad(v,-1,false));Run(`header/comments/crlf/${VersionName(v)}`,()=>HeaderCommentsLoad(v,-1,true));Run(`header/comments/none/${VersionName(v)}`,()=>HeaderCommentsLoad(v,-2,false));
    for(let m=0;m<4;m++)Run(`header/comments/truncated/${VersionName(v)}/${m}`,()=>HeaderCommentsTruncated(v,m));}
}
export function HeaderCommentPairs(version){return [[0,'SECTION'],[2,'HEADER'],[9,'$ACADVER'],[1,HeaderVersion(version)],[9,'$DWGCODEPAGE'],[3,'ANSI_1252'],[9,'$ANGBASE'],[50,15],
  [9,'$INSBASE'],[10,1.25],[20,-2.5],[30,3.75],[9,'$UCSORG'],[10,10],[20,20],[30,30],[9,'$UCSXDIR'],[10,1],[20,0],[30,0],[9,'$UCSYDIR'],[10,0],[20,1],[30,0],
  [9,'$PROJECTNAME'],[1,'ENDSEC'],[9,'$HYPERLINKBASE'],[1,'EOF'],[9,'$USERI1'],[70,-12],[9,'$USERR1'],[40,1.25],[9,'$LIMMIN'],[10,-1.25],[20,-2.5],[9,'$EXTMIN'],[10,-10],[20,-20],[30,-30],
  [9,'$DIMDLI'],[40,.25],[9,'$DIMPOST'],[1,'ignored dimension override'],[9,'$LASTSAVEDBY'],[1,'comments test'],[9,'$INSUNITS'],[70,4]];}
export function HeaderCommentsText(version,gap,crlf){const rows=[],T=(code,value)=>rows.push(String(code),String(value)),pairs=HeaderCommentPairs(version);
  for(let i=0;i<pairs.length;i++){T(...pairs[i]);if(i>=1&&(i===gap||gap===-1))for(const comment of ['', 'ENDSEC','EOF','$ACADVER','AC1009','999'])T(999,comment);}
  for(const row of [[0,'ENDSEC'],[0,'SECTION'],[2,'ENTITIES'],[0,'LINE'],[5,'200'],[100,'AcDbEntity'],[8,'0'],[100,'AcDbLine'],[10,1],[20,2],[30,3],[11,4],[21,5],[31,6],[0,'ENDSEC'],[0,'EOF']])T(...row);
  const newline=crlf?'\r\n':'\n';return rows.join(newline)+newline;
}
export function CheckHeaderCommentsDocument(doc,version,roundTripped=false){const v=doc.DrawingVariables;
  Equal(version,v.AcadVer,'Comments changed the actual version');Near(15,v.Angbase,'Comments changed a modeled scalar');
  Check(new Vector3(1.25,-2.5,3.75).Equals(v.InsBase),'Comments changed a modeled vector');Check(new Vector3(10,20,30).Equals(v.CurrentUCS.Origin),'Comments changed UCS origin');Check(Vector3.UnitX.Equals(v.CurrentUCS.XAxis),'Comments changed UCS X axis');Check(Vector3.UnitY.Equals(v.CurrentUCS.YAxis),'Comments changed UCS Y axis');
  Equal('ENDSEC',CustomHeaderValue(doc,'$PROJECTNAME').Value,'Literal ENDSEC value changed');Equal('EOF',CustomHeaderValue(doc,'$HYPERLINKBASE').Value,'Literal EOF value changed');Equal(-12,UnwrapHeaderNumber(CustomHeaderValue(doc,'$USERI1').Value),'Custom integer changed');Equal(1.25,CustomHeaderValue(doc,'$USERR1').Value,'Custom real changed');
  Check(new Vector2(-1.25,-2.5).Equals(CustomHeaderValue(doc,'$LIMMIN').Value),'Custom 2D vector changed');Check(new Vector3(-10,-20,-30).Equals(CustomHeaderValue(doc,'$EXTMIN').Value),'Custom 3D vector changed');
  Check(!v.ContainsCustomVariable('$DIMDLI')&&!v.ContainsCustomVariable('$DIMPOST'),'Comments altered the existing dimension-override policy.');Equal(roundTripped&&version===DxfVersion.AutoCad2000?GetHeaderEnvironment().UserName():'comments test',v.LastSavedBy,'Comments changed last-saved-by');Equal(4,v.InsUnits,'Comments changed units');
  const lines=Array.from(doc.Entities.Lines);Equal(1,lines.length,'Exactly one following line');Check(new Vector3(1,2,3).Equals(lines[0].StartPoint),'Comments disrupted the following section');Check(new Vector3(4,5,6).Equals(lines[0].EndPoint),'Comments disrupted the following entity');
}
export function HeaderCommentsLoad(version,gap,crlf){const fixture=HeaderCommentsText(version,gap,crlf),input=new MemoryStream(new TextEncoder().encode(fixture));try{
  const binary={};Equal(version,DxfDocument.CheckDxfFileVersion(input,binary),'Commented version probe');Check(!binary.value,'Commented text was detected as binary.');input.Position=0;
  const doc=DxfDocument.Load(input);if(!doc)throw new InvalidOperationException('Valid HEADER comments prevented loading.');CheckHeaderCommentsDocument(doc,version);Check(input.CanRead,'Comments closed caller stream.');
  for(const transport of [false,true]){const output=new MemoryStream();try{Check(doc.Save(output,transport),'Commented document failed to save.');output.Position=0;const loaded=DxfDocument.Load(output);if(!loaded)throw new InvalidOperationException('Commented document reload failed.');CheckHeaderCommentsDocument(loaded,version,true);}finally{output.Dispose();}}
  if(gap===-1&&!crlf){fs.mkdirSync(artifactDirectory,{recursive:true});fs.writeFileSync(path.join(artifactDirectory,`header-comments-${VersionName(version)}.dxf`),fixture);}
}finally{input.Dispose();}}
export function HeaderCommentsTruncated(version,scenario){const prefix='0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\n'+HeaderVersion(version)+'\n',tails=['999\n','999\ncomplete comment\n','9\n$USERI1\n999\ncomment instead of a required value\n','9\n$INSBASE\n10\n1\n999\n'];
  const input=new MemoryStream(new TextEncoder().encode(prefix+tails[scenario]));try{if(GetTypedIOConfiguration()==='Debug')Throws(EndOfStreamException,()=>DxfDocument.Load(input));else Check(DxfDocument.Load(input)===null,'Truncated HEADER/comment was accepted.');Check(input.CanRead,'Malformed comment closed caller stream.');}finally{input.Dispose();}
}
