from pathlib import Path

stage = Path(__file__).resolve().parent / '.ac1032'
p=stage/'apply.py'
s=p.read_text()
s=s.replace("'if (this.References[item.Name].Count != 0)'", "'if (this.HasReferences(item))'")
s=s.replace("'if (this.References[item.Name].Count != 0 || this.Owner.HasEmbeddedAttributeTextStyleReference(item))'", "'if (this.HasReferences(item) || this.Owner.HasEmbeddedAttributeTextStyleReference(item))'")
s=s.replace('RegisterMTextColumnTests();','RegisterMTextUnicodeChunkTests();')
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
p.write_text(s)
print('Prepared current-base integration and style-rename corrections')
