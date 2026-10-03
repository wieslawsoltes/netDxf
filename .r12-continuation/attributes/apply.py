from pathlib import Path
import shutil
import sys

root = Path(sys.argv[1]).resolve()
here = Path(__file__).resolve().parent

def edit(path, before, after):
    file = root / path
    text = file.read_text(encoding='utf-8')
    if text.count(before) != 1:
        raise SystemExit(f'Expected one exact attribute integration anchor: {path}: {before[:100]!r}')
    file.write_text(text.replace(before, after), encoding='utf-8')

text = 'netDxf/IO/DxfR12Codec.Text.cs'
edit(text, 'private void TextEntity(Text text, Vector3 normal)', 'private void TextEntity(Text text, Vector3 normal, short verticalCode = 73)')
edit(text, 'this.Tag(73, vertical)', 'this.Tag(verticalCode, vertical)')
edit(text, 'private static Text ReadTextEntity(Fields fields, Vector3 normal, double thickness, Dictionary<string, TextStyle> styles)', 'private static Text ReadTextEntity(Fields fields, Vector3 normal, double thickness, Dictionary<string, TextStyle> styles, short verticalCode = 73)')
edit(text, 'fields.Integer(73, 0)', 'fields.Integer(verticalCode, 0)')
edit('netDxf/Entities/Insert.SequenceEnd.cs', '        internal EndSequence SequenceEnd\n', '        // Codec inspection must not materialize a source-owned sequence record.\n        internal EndSequence ExistingSequenceEnd { get { return this.sequenceEnd; } }\n\n        internal EndSequence SequenceEnd\n')

blocks = 'netDxf/IO/DxfR12Codec.Blocks.cs'
edit(blocks, '            internal EntityObject[] Entities;', '            internal EntityObject[] Entities;\n            internal AttributeDefinition[] Attributes;')
edit(blocks, '|| block.XrefFile.Length != 0 || ((int)block.Flags & ~1) != 0 || block.AttributeDefinitions.Count != 0)', '|| block.XrefFile.Length != 0 || ((int)block.Flags & ~3) != 0)')
edit(blocks, 'Flags = (short)block.Flags, Entities = block.Entities.ToArray() };', 'Flags = (short)(((int)block.Flags & 1) | (block.AttributeDefinitions.Values.Any(a => (a.Flags & AttributeFlags.Constant) == 0) ? 2 : 0)),\n                    Entities = block.Entities.ToArray(), Attributes = block.AttributeDefinitions.Values.ToArray() };')
edit(blocks, '''                if (insert.Attributes.Count != 0 || insert.EndSequenceRecord != null)
                    throw new NotSupportedException("R12 attribute sequences require attribute-aware interchange.");''', '''                Attribute[] attributes = insert.Attributes.ToArray();
                EndSequence end = insert.ExistingSequenceEnd;
                if (attributes.Length != 0 || end != null) this.Tag(66, (short)1);''')
edit(blocks, '                this.Tag(44, Finite(insert.ColumnSpacing)); this.Tag(45, Finite(insert.RowSpacing));\n                this.Point(210, normal);', '                this.Tag(44, Finite(insert.ColumnSpacing)); this.Tag(45, Finite(insert.RowSpacing));\n                this.Point(210, normal);\n                this.Attributes(insert, attributes, end);')
edit(blocks, '                        foreach (EntityObject entity in block.Entities) this.Entity(entity);', '                        foreach (AttributeDefinition definition in block.Attributes) this.Definition(definition);\n                        foreach (EntityObject entity in block.Entities) this.Entity(entity);')
edit(blocks, 'if ((flags & ~1) != 0 || fields.Integer(67, 0) != 0 || fields.Text(1, "").Length != 0)', 'if ((flags & ~3) != 0 || fields.Integer(67, 0) != 0 || fields.Text(1, "").Length != 0)')
edit(blocks, '''                if (thickness != 0 || fields.Integer(66, 0) != 0)
                    throw new NotSupportedException("Extruded INSERTs or attribute sequences require a different profile.");''', '''                short followed = fields.Integer(66, 0);
                if (thickness != 0 || followed < 0 || followed > 1)
                    throw new NotSupportedException("INSERT thickness or sequence flag is outside the R12 profile.");
                EndSequence end = null;
                List<Attribute> attributes = followed == 0 ? new List<Attribute>() : this.ReadAttributes(records, ref position, out end);''')
edit(blocks, 'var insert = new Insert(new List<Attribute>()) { Block = block, Normal = normal,', 'var insert = new Insert(attributes) { Block = block, Normal = normal,')
edit(blocks, '''                insert.RestoreScale(new Vector3(fields.Number(41, 1), fields.Number(42, 1), fields.Number(43, 1)));
                return insert;''', '''                insert.RestoreScale(new Vector3(fields.Number(41, 1), fields.Number(42, 1), fields.Number(43, 1)));
                insert.RestoreSequenceEnd(end);
                this.attributeOwners.Add(insert);
                return insert;''')
edit(blocks, '                RequireAcyclicBlocks(this.graph);\n            }', '                RequireAcyclicBlocks(this.graph);\n                this.BindAttributes();\n            }')
edit('netDxf/IO/DxfR12Codec.Reader.cs', '                DxfRawRecord record = records[index];\n                var fields = new Fields(record);', '''                DxfRawRecord record = records[index];
                if (string.Equals(record.Name, "ATTDEF", StringComparison.OrdinalIgnoreCase))
                {
                    if (owner == null) throw new NotSupportedException("Attribute definitions require a selected BLOCK owner.");
                    blocks.ReadDefinition(record, owner);
                    continue;
                }
                var fields = new Fields(record);''')
source = (here / 'DxfR12Codec.Attributes.cs').read_text(encoding='utf-8')
for before, after in [
    ('EncodeText(value.Prompt)', 'EncodeTextControls(value.Prompt, this.options.MaximumStringLength)'),
    ('DecodeText(fields.Text(3, ""))', 'DecodeTextControls(fields.Text(3, ""))'),
    ('fields.Vector(210, Vector3.UnitZ)', 'fields.Vector(210, Vector3.UnitZ, false)'),
    ('new EndSequence(null)', 'new EndSequence')]:
    if source.count(before) != 1: raise SystemExit('Attribute helper integration anchor differs: ' + before)
    source = source.replace(before, after)
(root / 'netDxf/IO/DxfR12Codec.Attributes.cs').write_text(source, encoding='utf-8')
shutil.copyfile(here / 'R12AttributeTests.cs', root / 'tests/netDxf.Conformance/R12AttributeTests.cs')
edit('tests/netDxf.Conformance/R12CodecTests.cs', '        RegisterR12BlockTests();', '        RegisterR12BlockTests();\n        RegisterR12AttributeTests();')
