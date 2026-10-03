from pathlib import Path
import shutil
import sys

root = Path(sys.argv[1]).resolve()
here = Path(__file__).resolve().parent

def edit(path, before, after):
    file = root / path
    text = file.read_text(encoding='utf-8')
    if text.count(before) != 1:
        raise SystemExit(f'Expected one exact integration anchor: {path}: {before[:90]!r}')
    file.write_text(text.replace(before, after), encoding='utf-8')

core = 'netDxf/IO/DxfR12Codec.cs'
edit(core, 'private readonly List<DxfTag> body = new List<DxfTag>();', 'private List<DxfTag> body = new List<DxfTag>();')
edit(core, '&& type != typeof(PolygonMesh) && type != typeof(PolyfaceMesh))', '&& type != typeof(PolygonMesh) && type != typeof(PolyfaceMesh) && type != typeof(Insert))')
edit(core, 'if (entity is Text text) this.TextEntity(text, normal);', 'if (entity is Insert insert) this.InsertEntity(insert, normal);\n                else if (entity is Text text) this.TextEntity(text, normal);')
edit(core, 'internal DxfRawDocument Complete(bool binary)\n            {', 'internal DxfRawDocument Complete(bool binary)\n            {\n                List<DxfTag> blockBody = this.WriteBlocks();')
edit(core, 'this.Add(prefix, 0, "SECTION"); this.Add(prefix, 2, "BLOCKS"); this.Add(prefix, 0, "ENDSEC");', 'this.Add(prefix, 0, "SECTION"); this.Add(prefix, 2, "BLOCKS");\n                prefix.AddRange(blockBody); // These tags were already budgeted while captured.\n                this.Add(prefix, 0, "ENDSEC");')

reader = root / 'netDxf/IO/DxfR12Codec.Reader.cs'
text = reader.read_text(encoding='utf-8')
a = text.index('            var result = new List<EntityObject>();')
b = text.index('        private static Layer ReadLayer', a)
old = text[a:b]
loop = old[:old.index('            // Canonicalize default linetypes')]
loop = loop.replace('            var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);\n', '').replace('entities.Records', 'records')
anchor = '                    case "TEXT":'
assert loop.count(anchor) == 1
loop = loop.replace(anchor, '                    case "INSERT":\n                        entity = blocks.ReadInsert(fields, normal, thickness, records, ref index, layerName);\n                        break;\n' + anchor)
replacement = '''            var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var blocks = new BlockReader(document, patterns, layers, styles, handles);
            List<EntityObject> result = ReadEntityRecords(entities.Records, patterns, layers, styles, handles, blocks, null);
            blocks.Complete();
            // Canonicalize default linetypes of implicit layers, including block/face-only layers.
            foreach (Layer layer in layers.Values) layer.Linetype = ResolveLinetype(layer.Linetype.Name, patterns, true);
            return new ReadOnlyCollection<EntityObject>(result);
        }

        private static List<EntityObject> ReadEntityRecords(IReadOnlyList<DxfRawRecord> records,
            Dictionary<string, Linetype> patterns, Dictionary<string, Layer> layers,
            Dictionary<string, TextStyle> styles, HashSet<string> handles, BlockReader blocks, netDxf.Blocks.Block owner)
        {
''' + loop + '''            return result;
        }

'''
text = text[:a] + replacement + text[b:]
text = text.replace('It does not expand blocks or interpret unrelated sections/HEADER settings.', 'Reachable acyclic BLOCK definitions and INSERT arrays retain shared identity without expansion.\n        /// It does not interpret unrelated sections/HEADER settings. Layout/xref blocks and distinct\n        /// same-named source block objects require a separate collision or layout policy.')
reader.write_text(text, encoding='utf-8')
shutil.copyfile(here / 'DxfR12Codec.Blocks.cs', root / 'netDxf/IO/DxfR12Codec.Blocks.cs')
shutil.copyfile(here / 'R12BlockTests.cs', root / 'tests/netDxf.Conformance/R12BlockTests.cs')
edit('tests/netDxf.Conformance/R12CodecTests.cs', '        RegisterR12LinetypeTests();', '        RegisterR12LinetypeTests();\n        RegisterR12BlockTests();')
