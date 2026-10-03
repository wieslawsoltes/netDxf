// Port of pinned StrictTextValueTests.cs; codec reflection uses the original-path class.
import { DxfDocument, MemoryStream, Vector3 } from '../../index.js';
import { TextCodeValueReader } from '../../netDxf/IO/TextCodeValueReader.js';
import { Culture } from '../../runtime/DisplayFormatting.js';
import { FormatException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { Run, Check, Equal, Throws, SupportedVersions, VersionName, HeaderVersion } from './TestHarness.js';

export function* StrictRanges(...ranges) {
  for (const [first, last] of ranges) for (let code = first; code <= last; code++) yield code;
}
export function RegisterStrictTextValueTests() {
  RegisterStrictIntegers('int16', 'ReadShort', StrictRanges([60,79],[170,179],[270,289],[370,389],[400,409],[1060,1070]),
    [['-32768',-32768],['32767',32767],[' \t+0012 \t',12]], '32768','-32769');
  RegisterStrictIntegers('int32', 'ReadInt', StrictRanges([90,99],[420,429],[440,459],[1071,1071]),
    [['-2147483648',-2147483648],['2147483647',2147483647],[' +0012 ',12]], '2147483648','-2147483649');
  RegisterStrictIntegers('int64', 'ReadLong', StrictRanges([160,169]),
    [['-9223372036854775808',-9223372036854775808n],['9223372036854775807',9223372036854775807n],[' +0012 ',12n]],
    '9223372036854775808','-9223372036854775809');
  for (const code of StrictRanges([10,59],[110,149],[210,239],[460,469],[1010,1059])) {
    Run(`text/strict/double/${code}/valid`, () => {
      for (const [text, expected] of [['0',0],[' -1.25e+12 ',-1.25e12],['.5',0.5],['1.',1],['5e-324',Number.MIN_VALUE],['1.7976931348623157E+308',Number.MAX_VALUE]])
        Equal(expected, StrictTextRead(code,text,'ReadDouble'), 'floating-point value');
    });
    Run(`text/strict/double/${code}/invalid`, () => {
      for (const text of ['', ' ', 'garbage', '1,25', '1.2.3', '1e', '--1', '1\0', 'NaN', 'nan', '+NaN', 'Infinity', '-Infinity', '1e9999', '-1e9999']) StrictTextReject(code,text);
    });
  }
  for (const code of StrictRanges([290,299])) {
    Run(`text/strict/bool/${code}/valid`, () => { Equal(false,StrictTextRead(code,' 0 ','ReadBool'),'false'); Equal(true,StrictTextRead(code,' +01 ','ReadBool'),'true'); });
    Run(`text/strict/bool/${code}/invalid`, () => { for (const text of ['', 'true', 'false', '-1', '2', '255', '256', '1.0', '1\0', 'garbage']) StrictTextReject(code,text); });
  }
  for (const code of StrictRanges([5,5],[105,105],[320,369],[390,399],[480,481],[1005,1005])) {
    Run(`text/strict/handle/${code}/valid`, () => {
      for (const [text, expected] of [['0','0'],[' \t000aF \t','AF'],['7fffffffffffffff','7FFFFFFFFFFFFFFF'],['8000000000000000','8000000000000000'],['ffffffffffffffff','FFFFFFFFFFFFFFFF']])
        Equal(expected,StrictTextRead(code,text,'ReadHex'),'normalized unsigned handle');
    });
    Run(`text/strict/handle/${code}/invalid`, () => { for (const text of ['', ' ', 'G', '1G', '-1', '+1', '0xFF', '1 2', '1\0', 'ＦF', '10000000000000000', '00000000000000000']) StrictTextReject(code,text); });
  }
  Run('text/strict/culture-invariance', () => {
    const previous=Culture.Current;
    try { for (const culture of ['pl-PL','fr-FR','ar-SA','tr-TR']) {
      Culture.Current=culture;
      Equal(1.25,StrictTextRead(10,'1.25','ReadDouble'),'invariant decimal');
      Equal(-17,StrictTextRead(70,'-17','ReadShort'),'invariant integer');
      Equal('ABCDEF',StrictTextRead(330,'abcdef','ReadHex'),'invariant handle');
      StrictTextReject(10,'1,25');
    }} finally { Culture.Current=previous; }
  });
  Run('text/strict/ordinary-strings-unchanged', () => {
    for (const code of [0,1,2,3,4,6,7,8,9,100,101,102,300,410,430,470,999,1000,1001,1002,1003,1006,1009])
      Equal('  literal text  ',StrictTextRead(code,'  literal text  ','ReadString'),'string whitespace');
  });
  for (const version of SupportedVersions) {
    const prefix=`text/strict/document/${VersionName(version)}`;
    Run(prefix+'/bad-coordinate',()=>StrictTextDocument(version,'10\ninvalid',false));
    Run(prefix+'/nonfinite-coordinate',()=>StrictTextDocument(version,'10\nNaN',false));
    Run(prefix+'/bad-handle',()=>StrictTextDocument(version,'5\nGARBAGE',false));
    Run(prefix+'/bad-int16',()=>StrictTextDocument(version,'60\n32768',false));
    Run(prefix+'/valid',()=>StrictTextDocument(version,'',true));
  }
}
function RegisterStrictIntegers(type,getter,codes,valid,...overflow) {
  for (const code of codes) {
    Run(`text/strict/${type}/${code}/valid`,()=>{ for (const [text,expected] of valid) Equal(expected,StrictTextRead(code,text,getter),'exact integer and runtime type'); });
    Run(`text/strict/${type}/${code}/invalid`,()=>{ for (const text of [...overflow,'',' ','garbage','1.0','1e2','1,000','--1','1\0']) StrictTextReject(code,text); });
  }
}
export function StrictTextRead(code,text,getter) {
  const reader=new TextCodeValueReader(`${code}\n${text}\n0\nEOF\n`);
  reader.Next(); const result=reader[getter](); reader.Next(); Equal('EOF',reader.ReadString(),'numeric record boundary'); return result;
}
export function StrictTextReject(code,text) {
  const reader=new TextCodeValueReader(`999\ncomment\n${code}\n${text}\n`); reader.Next();
  let failure; try { reader.Next(); } catch(error) { failure=error; }
  Check(failure instanceof FormatException,'Malformed numeric/handle data was silently accepted.');
  Check(failure.message.includes(`group code ${code}`),'Missing group-code diagnostic.');
  Check(failure.message.includes('line 4'),'Missing physical value-line diagnostic.');
}
export function StrictTextDocument(version,replacement,valid) {
  let entity='0\nLINE\n5\nAB\n100\nAcDbEntity\n8\n0\n60\n0\n100\nAcDbLine\n10\n1.25\n20\n-2.5\n30\n3.75\n11\n4\n21\n5\n31\n6\n';
  for (const [prefix,original] of [['10\n','10\n1.25'],['5\n','5\nAB'],['60\n','60\n0']]) if(replacement.startsWith(prefix))entity=entity.replace(original,replacement);
  const file=`0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\n${HeaderVersion(version)}\n9\n$DWGCODEPAGE\n3\nANSI_1252\n9\n$HANDSEED\n5\nFFFF\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n${entity}0\nENDSEC\n0\nEOF\n`;
  const stream=new MemoryStream(new TextEncoder().encode(file));
  try {
    if (valid) { const loaded=DxfDocument.Load(stream); Check(loaded!==null,'Valid fixture rejected.'); const lines=Array.from(loaded.Entities.Lines);Equal(1,lines.length);Equal(new Vector3(1.25,-2.5,3.75),lines[0].StartPoint,'fixture coordinates'); }
    else if(GetTypedIOConfiguration()==='Debug')Throws(FormatException,()=>DxfDocument.Load(stream));
    else Check(DxfDocument.Load(stream)===null,'Malformed document returned a corrupted drawing instead of failing.');
    Check(stream.CanRead,'Load closed a caller-owned stream.');
  } finally { stream.Dispose(); }
}
