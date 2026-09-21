from pathlib import Path
import subprocess
r=Path('.')
originals = {
    'netDxf/IO/DxfReader.cs': '4566c274f99095970ed938122a71b7ed441dd80a',
    'netDxf/Tables/DimensionStyle.cs': '403b79a7e64cdd0bc320d52747aa6dd5f1589702',
    'netDxf/Tables/DimensionStyleOverride.cs': 'bd6f1ec4baf7f3fffbc5798e16c31c6faeaa42c5',
    'tests/netDxf.Conformance/Program.cs': 'ee480e04814ef688ba80cbbbdefd157ffd6d6329',
}
for file, wanted in originals.items():
    actual = subprocess.check_output(['git', 'hash-object', file], text=True).strip()
    if actual != wanted: raise RuntimeError('Source moved: ' + file)
def replace(file,old,new):
 p=r/file; data=p.read_bytes(); a=old.encode(); b=new.encode()
 if b'\r\n' in data:a=a.replace(b'\n',b'\r\n');b=b.replace(b'\n',b'\r\n')
 assert data.count(a)==1,(file,data.count(a))
 p.write_bytes(data.replace(a,b))
replace('netDxf/Tables/DimensionStyle.cs','''                if (MathHelper.IsZero(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "The scale factor cannot be zero.");
                }''','''                if (value == 0.0 || double.IsNaN(value) || double.IsInfinity(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "The scale factor must be finite and nonzero.");
                }''')
replace('netDxf/Tables/DimensionStyle.cs','''        /// DimScaleLinear has no effect on angular dimensions.''','''        /// DimScaleLinear has no effect on angular dimensions.<br />
        /// Finite nonzero values are retained independently of MathHelper.Epsilon, including negative and subnormal values.
        /// Both signs of zero, NaN and infinities are rejected before assignment.''')
replace('netDxf/Tables/DimensionStyleOverride.cs','''                    if (MathHelper.IsZero((double)value))
                    {
                        throw new ArgumentOutOfRangeException(nameof(value), value, string.Format("The DimensionStyleOverrideType.{0} dimension style override cannot be zero.", type));
                    }''','''                    if ((double)value == 0.0 || double.IsNaN((double)value) || double.IsInfinity((double)value))
                    {
                        throw new ArgumentOutOfRangeException(nameof(value), value, string.Format("The DimensionStyleOverrideType.{0} dimension style override must be finite and nonzero.", type));
                    }''')
replace('netDxf/IO/DxfReader.cs','''                        dimlfac = this.chunk.ReadDouble();
                        if (MathHelper.IsZero(dimlfac))''','''                        dimlfac = this.chunk.ReadDouble();
                        if (double.IsNaN(dimlfac) || double.IsInfinity(dimlfac))
                        {
                            throw new FormatException("DIMLFAC must be finite.");
                        }
                        // Scalar admission is exact, not a geometric tolerance test.
                        if (dimlfac == 0.0)''')
replace('tests/netDxf.Conformance/Program.cs','''        RunDimensionStyleParityTests();''','''        RunDimensionStyleParityTests();
        RegisterDimLfacFidelityTests();''')

(r/'tools/_publish_dimlfac.py').unlink()
(r/'.github/workflows/publish-dimlfac.yml').unlink()
