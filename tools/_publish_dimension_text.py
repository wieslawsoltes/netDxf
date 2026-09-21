from pathlib import Path
import re
import subprocess

originals = {
    'netDxf/Entities/DimensionBlock.cs': '5063224ff33ebc5f029959c074595a0c77ba0ed8',
    'tests/netDxf.Conformance/Program.cs': '4d24ec474daa44d5ffde696a5291936499d272c6',
}
for name, sha in originals.items():
    actual = subprocess.check_output(['git', 'hash-object', name], text=True).strip()
    if actual != sha: raise RuntimeError('Source moved: ' + name)
path = Path('netDxf/Entities/DimensionBlock.cs')
data = path.read_bytes()
old = b'public static class DimensionBlock'
assert data.count(old) == 1
data = data.replace(old, b'public static partial class DimensionBlock')
pattern = rb'            dim.TextReferencePoint = ([^;\r\n]+);\r\n            dim.TextPositionManuallySet = false;\r\n\r\n(?:            // drawing block\r\n)?            return new Block\(name, entities, null, false\) \{ ?Flags = BlockTypeFlags.AnonymousBlock ?\};'
data, count = re.subn(pattern, lambda m: b'            return FinishTextBlock(dim, name, entities, ' + m[1] + b');', data)
assert count == 8, count
path.write_bytes(data)
path = Path('tests/netDxf.Conformance/Program.cs')
data = path.read_bytes(); old = b'        RegisterDStyleContainerTests();'
assert data.count(old) == 1
path.write_bytes(data.replace(old, old + b'\n        RegisterDimensionTextBlockTests();'))
Path('tools/_publish_dimension_text.py').unlink()
Path('.github/workflows/publish-dimension-text.yml').unlink()
