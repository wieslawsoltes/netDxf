from pathlib import Path
import subprocess
originals = {
    'netDxf/Entities/DimensionBlock.cs': '9e69a934ea78dbacab74d58c3536efcb66e774f7',
    'netDxf/DxfDocument.cs': 'b32ecf51b2e6f37384007cd049952334acd89cd1',
    'netDxf/Collections/BlockRecords.cs': 'd9b14f477ee3c091bea0f00409e906d37e3c7a1d',
    'tests/netDxf.Conformance/Program.cs': '0c0f5c649be7384ce9a0527c1e6d25c3f5534669',
}
for path, sha in originals.items():
    if subprocess.check_output(['git', 'hash-object', path], text=True).strip() != sha:
        raise RuntimeError('Source moved: ' + path)
def replace(path, old, new, count=1):
    p=Path(path); data=p.read_bytes(); a=old.encode(); b=new.encode()
    if b'\r\n' in data: a=a.replace(b'\n',b'\r\n'); b=b.replace(b'\n',b'\r\n')
    if data.count(a)!=count: raise RuntimeError('Ambiguous edit: '+path)
    p.write_bytes(data.replace(a,b))
p='netDxf/Entities/DimensionBlock.cs'
replace(p, '''                double scale = Math.Abs(style.DimScaleLinear);
                if (owner != null)
                {
                    Layout layout = owner.Record.Layout;
                    if (layout != null)
                    {
                        // if DIMLFAC is negative the scale value is only applied to dimensions in PaperSpace
                        if (style.DimScaleLinear < 0 && !layout.IsPaperSpace)
                        {
                            scale = 1.0;
                        }
                    }
                }''', '''                double scale = style.DimScaleLinear;
                if (scale < 0.0)
                {
                    // Negative DIMLFAC applies only to an explicit paper-space owner.
                    // A detached or ordinary block definition has no paper-space context.
                    Layout layout = owner == null ? null : owner.Record.Layout;
                    scale = layout != null && layout.IsPaperSpace ? -scale : 1.0;
                }''')
replace(p, 'LinearUnitFormat.ToDecimal(measure*style.DimScaleLinear, unitFormat)', 'LinearUnitFormat.ToDecimal(measure, unitFormat)')
replace(p, '            string prefix = string.Empty;', '            string prefix = style.DimPrefix;')
# Move generic dispatch to the reviewed owner-context partial, preserving its cases.
s=Path(p).read_bytes(); start=s.index(b'            Block block;',s.index(b'public static Block Build(Dimension dim, string name)'))
end=s.index(b'            return block;', start)+len(b'            return block;')
Path(p).write_bytes(s[:start]+b'            return BuildForOwner(dim, name, dim.Owner);'+s[end:])
for kind in ['AlignedDimension','LinearDimension','Angular2LineDimension','Angular3PointDimension','DiametricDimension','RadialDimension','OrdinateDimension','ArcLengthDimension']:
    signature='        public static Block Build('+kind+' dim, string name)'
    replace(p,signature,signature+'\n        {\n            return Build(dim, name, dim.Owner);\n        }\n\n        private static Block Build('+kind+' dim, string name, Block owner)')
replace(p, '            List<string> texts = FormatDimensionText(measure, dim.DimensionType, dim.UserText, style, dim.Owner)', '            List<string> texts = FormatDimensionText(measure, dim.DimensionType, dim.UserText, style, owner)',7)
replace(p, 'FormatDimensionText(radius, dim.DimensionType, dim.UserText, style, dim.Owner)', 'FormatDimensionText(radius, dim.DimensionType, dim.UserText, style, owner)')
replace('netDxf/DxfDocument.cs', '        internal void AddEntityToDocument(EntityObject entity, bool assignHandle)', '''        internal void AddEntityToDocument(EntityObject entity, bool assignHandle)
        {
            this.AddEntityToDocument(entity, assignHandle, entity == null ? null : entity.Owner);
        }

        internal void AddEntityToDocument(EntityObject entity, bool assignHandle, Block owner)''')
replace('netDxf/DxfDocument.cs','Block dimBlock = DimensionBlock.Build(dim, "DimBlock");','Block dimBlock = DimensionBlock.BuildForOwner(dim, "DimBlock", owner);')
replace('netDxf/Collections/BlockRecords.cs','AddEntityToDocument(entity, assignHandle)','AddEntityToDocument(entity, assignHandle, block)')
replace('netDxf/Collections/BlockRecords.cs','AddEntityToDocument(e.Item, string.IsNullOrEmpty(e.Item.Handle))','AddEntityToDocument(e.Item, string.IsNullOrEmpty(e.Item.Handle), (Block)sender)')
replace('tests/netDxf.Conformance/Program.cs','        RegisterDimensionTextLiteralTests();','        RegisterDimensionTextLiteralTests();\n        RegisterDimensionLabelScaleTests();')
Path('tools/_publish_dimension_scale.py').unlink()
Path('.github/workflows/publish-dimension-scale.yml').unlink()
