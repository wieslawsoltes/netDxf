from pathlib import Path
import subprocess
originals = {'netDxf/Entities/DimensionBlock.cs': '78828db9d4205f2cc14c5da3ca666f3ccc137a1e', 'tests/netDxf.Conformance/Program.cs': '29b35a745f528361cf8de2b904d8af92163cf3f5'}
for path, sha in originals.items():
    if subprocess.check_output(['git', 'hash-object', path], text=True).strip() != sha:
        raise RuntimeError('Source moved: ' + path)
def replace(path, old, new):
    p = Path(path); data = p.read_bytes(); a = old.encode(); b = new.encode()
    if b'\r\n' in data: a = a.replace(b'\n', b'\r\n'); b = b.replace(b'\n', b'\r\n')
    if data.count(a) != 1: raise RuntimeError('Ambiguous edit: ' + path)
    p.write_bytes(data.replace(a, b))
p = 'netDxf/Entities/DimensionBlock.cs'
replace(p, '            string dimText = string.Empty;', '            string dimText = string.Empty;\n            double alternateMeasurement = measure;')
replace(p, '                if (style.DimRoundoff > 0.0)', '                alternateMeasurement = measure * scale;\n                if (style.DimRoundoff > 0.0)')
replace(p, '            dimText = string.Format("{0}{1}{2}", prefix, dimText, style.DimSuffix);', '''            dimText = string.Format("{0}{1}{2}", prefix, dimText, style.DimSuffix);
            if (style.AlternateUnits.Enabled && dimType != DimensionType.Angular && dimType != DimensionType.Angular3Point
                && (string.IsNullOrEmpty(userText) || userText.Contains("<>")))
                dimText += FormatAlternateUnits(alternateMeasurement, style);''')
replace(p, '                // primary units\n                AngularPrecision', '''                // Independent value-object copy: overrides must not edit the base style.
                AlternateUnits = (DimensionStyleAlternateUnits)dim.Style.AlternateUnits.Clone(),
                DimScaleOverall = dim.Style.DimScaleOverall,

                // primary units
                AngularPrecision''')
replace(p, '            foreach (DimensionStyleOverride styleOverride in dim.StyleOverrides.Values)\n            {', '            foreach (DimensionStyleOverride styleOverride in dim.StyleOverrides.Values)\n            {\n                ApplyAlternateUnitOverride(copy.AlternateUnits, styleOverride);')
replace('tests/netDxf.Conformance/Program.cs', '        RegisterAlternateUnitModeTests();', '        RegisterAlternateUnitModeTests();\n        RegisterAlternateDimensionLabelTests();')
Path('tools/_publish_alternate_labels.py').unlink()
Path('.github/workflows/publish-alternate-labels.yml').unlink()
