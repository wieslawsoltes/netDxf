from pathlib import Path
import subprocess
originals = {
    'netDxf/IO/DxfReader.cs': 'fe6055690a0273d1d49b6ebe62133663def02df0',
    'netDxf/IO/DxfWriter.cs': '9b3ec6cb1c3b00b9d9e87b6ac17924beb0f54fd1',
    'tests/netDxf.Conformance/Program.cs': '569a2c999ab43c4ae18b2b1baaaa72b489ed59b9',
}
for path, sha in originals.items():
    if subprocess.check_output(['git', 'hash-object', '--no-filters', path], text=True).strip() != sha:
        raise RuntimeError('Source moved: ' + path)
def replace(path, old, new, count=1):
    p = Path(path); data = p.read_bytes(); a = old.encode(); b = new.encode()
    if b'\r\n' in data: a = a.replace(b'\n', b'\r\n'); b = b.replace(b'\n', b'\r\n')
    if data.count(a) != count: raise RuntimeError('Ambiguous edit: ' + path)
    p.write_bytes(data.replace(a, b))
p = 'netDxf/IO/DxfWriter.cs'
replace(p, 'AddDimensionStyleOverridesXData(leader.XData, leader.StyleOverrides)', 'AddDimensionStyleOverridesXData(leader.XData, leader.StyleOverrides, leader.Style)')
replace(p, 'AddDimensionStyleOverridesXData(dim.XData, dim.StyleOverrides)', 'AddDimensionStyleOverridesXData(dim.XData, dim.StyleOverrides, dim.Style)')
replace(p, 'private void AddDimensionStyleOverridesXData(XDataDictionary xdata, DimensionStyleOverrideDictionary overrides)', 'private void AddDimensionStyleOverridesXData(XDataDictionary xdata, DimensionStyleOverrideDictionary overrides, DimensionStyle style)')
replace(p, '            string prefix = string.Empty;\n            string suffix = string.Empty;', '            // DIMPOST is one combined value. Preserve the inherited component when\n            // only the other component is overridden; an explicit empty string clears it.\n            string prefix = style.DimPrefix;\n            string suffix = style.DimSuffix;')
replace(p, '            string altPrefix = string.Empty;\n            string altSuffix = string.Empty;', '            // DIMAPOST follows the same combined-value rule independently of DIMPOST.\n            string altPrefix = style.AlternateUnits.Prefix;\n            string altSuffix = style.AlternateUnits.Suffix;')
replace(p, 'string altUnits = string.IsNullOrEmpty(style.DimPrefix) ? "" : "[]";', 'string altUnits = string.IsNullOrEmpty(style.AlternateUnits.Prefix) ? "" : "[]";', 2)
p = 'netDxf/IO/DxfReader.cs'
for var, prefix, suffix in [('textPrefixSuffix', 'DimPrefix', 'DimSuffix'), ('altTextPrefixSuffix', 'AltUnitsPrefix', 'AltUnitsSuffix')]:
    old = f'''                                    if (!string.IsNullOrEmpty({var}[0]))
                                    {{
                                        overrides.Add(new DimensionStyleOverride(DimensionStyleOverrideType.{prefix}, {var}[0]));
                                    }}

                                    if (!string.IsNullOrEmpty({var}[1]))
                                    {{
                                        overrides.Add(new DimensionStyleOverride(DimensionStyleOverrideType.{suffix}, {var}[1]));
                                    }}'''
    new = f'''                                    // The stored pair overrides both components, including empty values.
                                    overrides.Add(new DimensionStyleOverride(DimensionStyleOverrideType.{prefix}, {var}[0]));
                                    overrides.Add(new DimensionStyleOverride(DimensionStyleOverrideType.{suffix}, {var}[1]));'''
    replace(p, old, new)
replace('tests/netDxf.Conformance/Program.cs', '        RegisterDimensionLabelScaleTests();', '        RegisterDimensionLabelScaleTests();\n        RegisterDimensionAffixFidelityTests();')
Path('tools/_publish_dimension_affix.py').unlink()
Path('.github/workflows/publish-dimension-affix.yml').unlink()
