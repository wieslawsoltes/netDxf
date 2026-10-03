from pathlib import Path
import shutil
import sys

root = Path(sys.argv[1]).resolve()
here = Path(__file__).resolve().parent
for name in ('DxfR12SelectionPlan.cs', 'DxfR12Codec.Selection.cs'):
    target = root / 'netDxf' / 'IO' / name
    if target.exists(): raise SystemExit('Refusing to overwrite an existing selection-plan source: ' + str(target))
    shutil.copyfile(here / name, target)
shutil.copyfile(here / 'R12SelectionTests.cs', root / 'tests/netDxf.Conformance/R12SelectionTests.cs')
file = root / 'tests/netDxf.Conformance/R12CodecTests.cs'
text = file.read_text(encoding='utf-8')
anchor = '        RegisterR12AttributeTests();'
if text.count(anchor) != 1: raise SystemExit('Expected one attribute test-registration anchor')
file.write_text(text.replace(anchor, anchor + '\n        RegisterR12SelectionTests();'), encoding='utf-8')
