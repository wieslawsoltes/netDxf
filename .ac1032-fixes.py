from pathlib import Path

stage = Path(__file__).resolve().parent / '.ac1032'
p=stage/'apply.py'
s=p.read_text()
s=s.replace('RegisterMTextColumnTests();','RegisterMTextUnicodeChunkTests();')
s=s.replace("fallback='style' if definition else 'this.GetTextStyle(styleName)'", "fallback='style'")
a=s.index('    def styles(text):')
b=s.index("    edit('netDxf/Collections/TextStyles.cs',styles)",a)+len("    edit('netDxf/Collections/TextStyles.cs',styles)")
s=s[:a]+'''    def table_refs(text):
        text=replace(text,'|| (this.list.TryGetValue(name, out T viewportTarget) && this.Owner.ViewportLayerReferences(viewportTarget).Count > 0);',
            '|| (this.list.TryGetValue(name, out T viewportTarget) && this.Owner.ViewportLayerReferences(viewportTarget).Count > 0)\\n                || (this.list.TryGetValue(name, out T embeddedTarget) && this.Owner.HasEmbeddedAttributeTextStyleReference(embeddedTarget));')
        text=replace(text,'|| this.Owner.ViewportLayerReferences(item).Count > 0;',
            '|| this.Owner.ViewportLayerReferences(item).Count > 0\\n                || this.Owner.HasEmbeddedAttributeTextStyleReference(item);')
        return replace(text,'additional.AddRange(this.Owner.ViewportLayerReferences(target));',
            'additional.AddRange(this.Owner.ViewportLayerReferences(target));\\n            additional.AddRange(this.Owner.AttributeMTextReferences(target));')
    edit('netDxf/Collections/TableObjects.cs',table_refs)
'''+s[b:]
p.write_text(s)

p=stage/'DxfDocument.AttributeMText.cs'
s=p.read_text().replace('internal bool HasEmbeddedAttributeTextStyleReference(TextStyle style)',
    'internal bool HasEmbeddedAttributeTextStyleReference(TableObject target)')
s=s.replace('foreach (var entry in this.EmbeddedAttributeTextEntries())\n                if (ReferenceEquals(entry.Value.Style, style)) return true;',
    'if (!(target is TextStyle style)) return false;\n            foreach (var entry in this.EmbeddedAttributeTextEntries())\n                if (ReferenceEquals(entry.Value.Style, style)) return true;')
pos=s.rfind('\n    }')
s=s[:pos]+'''
        internal List<DxfObjectReference> AttributeMTextReferences(TableObject target)
        {
            var result = new List<DxfObjectReference>();
            if (!(target is TextStyle style)) return result;
            foreach (var entry in this.EmbeddedAttributeTextEntries())
                if (ReferenceEquals(entry.Value.Style, style)) result.Add(new DxfObjectReference(entry.Key, 1));
            return result;
        }
'''+s[pos:]
p.write_text(s)

p=stage/'AttributeMText.cs'
s=p.read_text()
s=s.replace('public IReadOnlyList<DxfTag> Tags { get { return this.tags; } }', '''public IReadOnlyList<DxfTag> Tags
        {
            get
            {
                foreach (DxfTag tag in this.tags)
                    if (tag.Code == 7 && (string)tag.Value != this.style.Name)
                        return Array.AsReadOnly(this.tags.Select(t => t.Code == 7 ? new DxfTag(7, this.style.Name) : t).ToArray());
                return this.tags;
            }
        }''')
s=s.replace('new AttributeMText(this.tags.Where(', 'new AttributeMText(this.Tags.Where(')
s=s.replace('var output = this.tags.Where(', 'var output = this.Tags.Where(')
s=s.replace('FillBoxScale =', 'ScaleFactor =')
s=s.replace('(this.Number(45, 1.5) < 1 || this.Number(45, 1.5) > 5)', 'this.Number(45, 1.5) <= 0')
s=s.replace('            if (seen.Contains(421) && (Convert.ToInt32(this.Item(421)) < 0 || Convert.ToInt32(this.Item(421)) > 0xffffff))\n                throw new ArgumentOutOfRangeException(nameof(tags));\n', '')
p.write_text(s)

p=stage/'DxfAttributeMTextCodec.cs'
s=p.read_text().replace('else this.WriteByCode(tag.Code, tag.Value);', '''else if (tag.Value is double number) this.chunk.Write(tag.Code, number);
                else if (tag.Value is short small) this.chunk.Write(tag.Code, small);
                else if (tag.Value is int integer) this.chunk.Write(tag.Code, integer);
                else throw new InvalidDataException("Unexpected embedded MTEXT field type.");''')
p.write_text(s)
print('Prepared current API, shared reference and rename-safe embedded text integration')
