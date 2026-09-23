from pathlib import Path
import subprocess
originals = {'netDxf/Entities/DimensionBlock.cs': '04a0b31d30a740b3bfd298612c8faa526616fac4', 'tests/netDxf.Conformance/Program.cs': '3cbffe6843c12699d4d95655eb445e4b86d73cbd'}
for path, sha in originals.items():
    if subprocess.check_output(['git', 'hash-object', path], text=True).strip() != sha:
        raise RuntimeError('Source moved: ' + path)
p=Path('netDxf/Entities/DimensionBlock.cs');data=p.read_bytes();s=data.decode();nl='\r\n' if '\r\n' in s else '\n';s=s.replace('\r\n','\n')
def replace(a,b,count=1):
 global s
 assert s.count(a)==count,(a,s.count(a));s=s.replace(a,b)
replace('                ExtLineExtend = dim.Style.ExtLineExtend,','                ExtLineExtend = dim.Style.ExtLineExtend,\n                ExtLineFixed = dim.Style.ExtLineFixed,\n                ExtLineFixedLength = dim.Style.ExtLineFixedLength,')
old='''                    case DimensionStyleOverrideType.ExtLineExtend:
                        copy.ExtLineExtend = (double) styleOverride.Value;
                        break;'''
new=old+'''
                    case DimensionStyleOverrideType.ExtLineFixed:
                        copy.ExtLineFixed = (bool) styleOverride.Value;
                        break;
                    case DimensionStyleOverrideType.ExtLineFixedLength:
                        copy.ExtLineFixedLength = (double) styleOverride.Value;
                        break;'''
replace(old,new)
for i in (1,2):
 for dire in ('vec',f'dirRef{i}'):
  old=f'entities.Add(ExtensionLine(ref{i} + dimexo * {dire}, dimRef{i} + dimexe * {dire}, style, style.ExtLine{i}Linetype));'
  new=f'AddExtensionLine(entities, ref{i}, dimRef{i}, ref{i} + dimexo * {dire}, dimRef{i} + dimexe * {dire}, style, style.ExtLine{i}Linetype);'
  replace(old,new)
for i,angle,refStart,refEnd in [(1,'startAngle','ref1Start','ref1End'),(2,'endAngle','ref2Start','ref2End')]:
 old=f'entities.Add(ExtensionLine(s, Vector2.Polar(dimRef{i}, t*dimexe, {angle}), style, style.ExtLine1Linetype));'
 new=f'AddExtensionLine(entities, t < 0 ? {refStart} : {refEnd}, dimRef{i}, s, Vector2.Polar(dimRef{i}, t*dimexe, {angle}), style, style.ExtLine{i}Linetype);'
 replace(old,new)
for i,angle in [(1,'startAngle'),(2,'endAngle')]:
 old=f'                entities.Add(ExtensionLine(Vector2.Polar(ref{i}, dimexo, {angle} + refAngle), Vector2.Polar(dimRef{i}, dimexe, {angle} + refAngle), style, style.ExtLine1Linetype));'
 new=f'                AddExtensionLine(entities, ref{i}, dimRef{i}, Vector2.Polar(ref{i}, dimexo, {angle} + refAngle), Vector2.Polar(dimRef{i}, dimexe, {angle} + refAngle), style, style.ExtLine{i}Linetype);'
 # The commented unused prototype is not an active renderer; leave it untouched.
 assert s.count('\n'+old)==2,(i,s.count('\n'+old))
 s=s.replace('\n'+old,'\n'+new)
a = """            double dimexo = style.ExtLineOffset * style.DimScaleOverall;
            double dimexe = style.ExtLineExtend * style.DimScaleOverall;
            if (!style.ExtLine1Off)
            {
                AddExtensionLine(entities, ref1, dimRef1, Vector2.Polar(ref1, dimexo, startAngle + refAngle)"""
b = """            // The second angular ray can have a different reference-point radius.
            double refAngle2 = Vector2.Distance(refCenter, ref2) > Vector2.Distance(refCenter, dimRef2) ? MathHelper.PI : 0.0;
""" + a
replace(a, b, 2)
a = 'AddExtensionLine(entities, ref2, dimRef2, Vector2.Polar(ref2, dimexo, endAngle + refAngle), Vector2.Polar(dimRef2, dimexe, endAngle + refAngle), style, style.ExtLine2Linetype);'
replace(a, a.replace('endAngle + refAngle', 'endAngle + refAngle2'), 2)
p.write_bytes(s.replace('\n', nl).encode())
p = Path('tests/netDxf.Conformance/Program.cs')
data = p.read_bytes(); needle = b'        RegisterDimensionResetTests();'
if data.count(needle) != 1: raise RuntimeError('Ambiguous registration')
p.write_bytes(data.replace(needle, needle + b'\n        RegisterDimensionExtensionRenderingTests();'))
Path('tools/_publish_extensions.py').unlink()
Path('.github/workflows/publish-extensions.yml').unlink()
