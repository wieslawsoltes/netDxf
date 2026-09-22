from pathlib import Path
import re
import subprocess
originals = {
    'netDxf/IO/DxfWriter.cs': '1cc9af605513a97e3696adecefd09dc5ce9af2aa',
    'tests/netDxf.Conformance/Program.cs': '886f4ca416a3a6924b3878551910211647c72b2f',
}
for path, sha in originals.items():
    if subprocess.check_output(['git', 'hash-object', '--no-filters', path], text=True).strip() != sha:
        raise RuntimeError('Source moved: ' + path)
p = Path('netDxf/IO/DxfWriter.cs'); data = p.read_bytes(); text = data.decode()
nl = '\r\n' if b'\r\n' in data else '\n'
a = text.index('        private void AddDimensionStyleOverridesXData(')
b = text.index('            XData xdataEntry;', a)
part = text[a:b]
fields = {
    'suppressLinearLeadingZeros': 'style.SuppressLinearLeadingZeros',
    'suppressLinearTrailingZeros': 'style.SuppressLinearTrailingZeros',
    'suppressAngularLeadingZeros': 'style.SuppressAngularLeadingZeros',
    'suppressAngularTrailingZeros': 'style.SuppressAngularTrailingZeros',
    'suppressZeroFeet': 'style.SuppressZeroFeet',
    'suppressZeroInches': 'style.SuppressZeroInches',
    'altStackedUnits': 'style.AlternateUnits.StackUnits',
    'altSuppressLinearLeadingZeros': 'style.AlternateUnits.SuppressLinearLeadingZeros',
    'altSuppressLinearTrailingZeros': 'style.AlternateUnits.SuppressLinearTrailingZeros',
    'altSuppressZeroFeet': 'style.AlternateUnits.SuppressZeroFeet',
    'altSuppressZeroInches': 'style.AlternateUnits.SuppressZeroInches',
    'tolSuppressLinearLeadingZeros': 'style.Tolerances.SuppressLinearLeadingZeros',
    'tolSuppressLinearTrailingZeros': 'style.Tolerances.SuppressLinearTrailingZeros',
    'tolSuppressZeroFeet': 'style.Tolerances.SuppressZeroFeet',
    'tolSuppressZeroInches': 'style.Tolerances.SuppressZeroInches',
    'tolAltSuppressLinearLeadingZeros': 'style.Tolerances.AlternateSuppressLinearLeadingZeros',
    'tolAltSuppressLinearTrailingZeros': 'style.Tolerances.AlternateSuppressLinearTrailingZeros',
    'tolAltSuppressZeroFeet': 'style.Tolerances.AlternateSuppressZeroFeet',
    'tolAltSuppressZeroInches': 'style.Tolerances.AlternateSuppressZeroInches',
}
for variable, value in fields.items():
    part, count = re.subn(r'bool ' + variable + r' = (?:false|true);', 'bool ' + variable + ' = ' + value + ';', part)
    if count != 1: raise RuntimeError('Ambiguous field: ' + variable)
def replace(text, old, new):
    if text.count(old) != 1: raise RuntimeError('Ambiguous edit: ' + old)
    return text.replace(old, new)
part = replace(part, 'LinearUnitType altLinearUnitType = LinearUnitType.Decimal;', 'LinearUnitType altLinearUnitType = style.AlternateUnits.LengthUnits;')
part = replace(part, '            bool writeDIMZIN = false;', '            // Composite fields start from the base style; sparse overrides replace only their component.' + nl + '            bool writeDIMZIN = false;')
text = text[:a] + part + text[b:]
text = replace(text, '                    case DimensionStyleOverrideType.AltUnitsStackedUnits:' + nl + '                        altStackedUnits', '                    case DimensionStyleOverrideType.AltUnitsStackedUnits:' + nl + '                        writeDIMALTU = true;' + nl + '                        altStackedUnits')
p.write_bytes(text.encode())
p = Path('tests/netDxf.Conformance/Program.cs')
p.write_bytes(replace(p.read_bytes().decode(), '        RegisterDimensionAffixFidelityTests();', '        RegisterDimensionAffixFidelityTests();\n        RegisterDimensionCompositeOverrideTests();').encode())
Path('tools/_publish_dimension_composite.py').unlink()
Path('.github/workflows/publish-dimension-composite.yml').unlink()
