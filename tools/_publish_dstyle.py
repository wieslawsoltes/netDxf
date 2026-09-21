from pathlib import Path
import subprocess
originals = {'netDxf/IO/DxfReader.cs': 'c68dd838b7d446c57d2ad34c9e48cd51e95be6ac', 'tests/netDxf.Conformance/Program.cs': 'a51af9cfc9acba9d0df590a22d5452dc366adde8', 'tests/netDxf.Conformance/EllipseIoParameterTests.cs': '61f4a2536478b5a87f826c7eeaacc3ef3e7d41a2'}
for file, wanted in originals.items():
    actual = subprocess.check_output(['git', 'hash-object', file], text=True).strip()
    if actual != wanted: raise RuntimeError('Source moved: ' + file)
def replace(file, old, new):
    path = Path(file); data = path.read_bytes(); a = old.encode(); b = new.encode()
    if data.count(a) != 1: raise RuntimeError('Ambiguous source: ' + file)
    path.write_bytes(data.replace(a, b))
replace('netDxf/IO/DxfReader.cs', 'foreach (Dimension dim in this.doc.Entities.Dimensions)', 'foreach (Dimension dim in this.doc.Blocks.SelectMany(block => block.Entities).OfType<Dimension>().ToArray())')
replace('netDxf/IO/DxfReader.cs', 'foreach (Leader leader in this.doc.Entities.Leaders)', 'foreach (Leader leader in this.doc.Blocks.SelectMany(block => block.Entities).OfType<Leader>().ToArray())')
replace('netDxf/IO/DxfReader.cs', '// therefore is better process it at the end, when everything has been created read dimension style overrides', '// Resolve every block/layout, not only the active-layout shortcut. Snapshot each\n            // family before callbacks can register referenced table objects or blocks.')
replace('tests/netDxf.Conformance/Program.cs', '        RegisterDimLfacFidelityTests();', '        RegisterDimLfacFidelityTests();\n        RegisterDStyleContainerTests();')
replace('tests/netDxf.Conformance/EllipseIoParameterTests.cs', '                        // Whole-document loading at epsilon=100 fails in the unchanged\n                        // DIMSTYLE scale setter before reaching ELLIPSE. Exercise the\n                        // actual ellipse codec at that deliberately extreme epsilon.', '                        // Historically DIMSTYLE blocked whole-document loading at 100.\n                        // Retain this direct codec coverage; DimLfacFidelityTests also\n                        // exercises complete document loading at that extreme epsilon.')
Path('tools/_publish_dstyle.py').unlink()
Path('.github/workflows/publish-dstyle.yml').unlink()
