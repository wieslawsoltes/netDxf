from pathlib import Path
import re, shutil, sys

root = Path(sys.argv[1]).resolve()
stage = Path(__file__).resolve().parent
part = sys.argv[2]

def edit(name, change):
    p = root / name
    original = p.read_bytes()
    text = original.decode('utf-8-sig').replace('\r\n', '\n')
    updated = change(text)
    if updated == text:
        raise RuntimeError('No change: ' + name)
    # Keep the original source's dominant newline style and UTF-8 BOM.
    if original.count(b'\r\n') > original.count(b'\n') // 2:
        updated = updated.replace('\n', '\r\n')
    p.write_bytes((b'\xef\xbb\xbf' if original.startswith(b'\xef\xbb\xbf') else b'') + updated.encode())

def replace(text, old, new, count=1):
    found = text.count(old)
    if found != count: raise RuntimeError(f'Expected {count}, found {found}: {old[:100]}')
    return text.replace(old,new)

def method(text, signature, change):
    start = text.index(signature)
    brace = text.index('{', start)
    # Skip C# comments, regular/verbatim strings and character literals while locating the body.
    token = re.compile(r'//[^\n]*|/\*.*?\*/|@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|[{}]', re.S)
    depth = 0
    for match in token.finditer(text, brace):
        value = match.group()
        if value == '{': depth += 1
        elif value == '}':
            depth -= 1
            if depth == 0:
                end = match.end()
                return text[:start] + change(text[start:end]) + text[end:]
    raise RuntimeError('Unclosed method ' + signature)

def copy(name, destination):
    p = root / destination
    if p.exists(): raise RuntimeError('New source already exists: ' + destination)
    p.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(stage / name, p)

if part == 'model':
    copy('AttributeMText.cs','netDxf/Entities/AttributeMText.cs')
    copy('Attribute.Multiline.cs','netDxf/Entities/Attribute.Multiline.cs')
    for name in ('Attribute','AttributeDefinition'):
        def lifecycle(text):
            # Both independent and block-context cloning paths use this copy point.
            pattern = r'(\s*)(\w+)\.CommonData\.CopyFrom\(this\.CommonData\);'
            text, n = re.subn(pattern, lambda m: m.group(0) + m.group(1) + 'this.CopyMultilineTo(' + m.group(2) + ');', text)
            if not n: raise RuntimeError('No clone hooks in ' + name)
            def transform(body):
                i = body.index('{') + 1
                body = body[:i] + '\n            AttributeMText embeddedCandidate = this.multilineText == null ? null : this.multilineText.TransformBy(transformation, translation);' + body[i:]
                return body[:-1] + '    this.multilineText = embeddedCandidate;\n        }'
            text = method(text,'public void TransformBy(Matrix3 transformation, Vector3 translation)',transform)
            if name == 'Attribute':
                def constructor(body):
                    return body[:-1] + '    this.multilineText = definition.MultilineText;\n        }'
                text = method(text,'public Attribute(AttributeDefinition definition)',constructor)
            return text
        edit('netDxf/Entities/' + name + '.cs',lifecycle)

elif part == 'io':
    copy('DxfAttributeMTextCodec.cs','netDxf/IO/DxfAttributeMTextCodec.cs')
    def reader(text):
        for sig, variable, definition in [('private AttributeDefinition ReadAttributeDefinition()', 'attdef', True),
                                         ('private Attribute ReadAttribute(', 'att', False)]:
            def patch(body):
                b = body.index('{') + 1
                body = body[:b] + '\n            AttributeMText embeddedAttributeText = null;\n            bool attributeSubclass = false;\n            short? multilineType = null;\n' + body[b:]
                loop = re.search(r'while\s*\(this\.chunk\.Code != 0\)\s*\{', body)
                if not loop: raise RuntimeError('Attribute reader loop not found')
                scope = 'AcDbAttributeDefinition' if definition else 'AcDbAttribute'
                expected = 4 if definition else 2
                fallback = 'style' if definition else 'this.GetTextStyle(styleName)'
                guard = '''
                if (this.chunk.Code == 100)
                {
                    attributeSubclass = this.chunk.ReadString() == "%s";
                    this.chunk.Next();
                    continue;
                }
                if (attributeSubclass && this.chunk.Code == 71)
                {
                    if (multilineType.HasValue) throw new FormatException("Duplicate attribute type.");
                    multilineType = this.chunk.ReadShort();
                    if (multilineType.Value != 1 && multilineType.Value != %d)
                        throw new NotSupportedException("Unsupported attribute content type.");
                    this.chunk.Next();
                    continue;
                }
                if (attributeSubclass && this.chunk.Code == 72)
                {
                    if (this.chunk.ReadShort() != 0) throw new NotSupportedException("Unsupported attribute multiline metadata.");
                    this.chunk.Next();
                    continue;
                }
                if (this.chunk.Code == 101)
                {
                    if (embeddedAttributeText != null) throw new FormatException("Duplicate attribute embedded body.");
                    embeddedAttributeText = this.ReadAttributeMText(%s);
                    continue;
                }
''' % (scope,expected,fallback)
                body = body[:loop.end()] + guard + body[loop.end():]
                ret = re.search(r'return\s+(\w+)\s*;\s*}$',body)
                if not ret: raise RuntimeError('Attribute reader return not found')
                host = ret.group(1)
                addition = '''if (multilineType.HasValue && (multilineType.Value != 1) != (embeddedAttributeText != null))
                throw new FormatException("Attribute type and embedded body disagree.");
            %s.MultilineText = embeddedAttributeText;
            ''' % host
                # Assign before common metadata is restored so loading does not invalidate retained graphics.
                marker = re.search(r'(?m)^\s*this\.RegisterDatabaseMetadata\([^;]+;',body)
                if marker:
                    at = marker.start()
                    body = body[:at] + '\n            ' + addition + body[at:]
                else:
                    body = body[:ret.start()] + addition + body[ret.start():]
                return body
            text = method(text,sig,patch)
        return text
    edit('netDxf/IO/DxfReader.cs',reader)
    def writer(text):
        text = replace(text,'this.ValidateMTextColumns();','this.ValidateAttributeMText();\n            this.ValidateMTextColumns();')
        for sig, definition in [('private void WriteAttributeDefinition(',True),('private void WriteAttribute(',False)]:
            def patch(body):
                parameter = re.search(r'\(\s*(?:AttributeDefinition|Attribute)\s+(\w+)',body).group(1)
                old = 'this.WriteXData(' + parameter + '.XData);'
                return replace(body,old,'this.WriteAttributeMText(' + parameter + '.MultilineText, ' + str(definition).lower() + ');\n            ' + old)
            text = method(text,sig,patch)
        return text
    edit('netDxf/IO/DxfWriter.cs',writer)
    def r12(text):
        for signature in ('private static Text AttributeText(AttributeDefinition value)','private static Text AttributeText(Attribute value)'):
            def patch(body):
                b=body.index('{')+1
                return body[:b]+'\n            if (value.MultilineText != null) throw new NotSupportedException("Multiline attributes require AC1032; R12 export cannot discard the embedded content.");'+body[b:]
            text=method(text,signature,patch)
        return text
    edit('netDxf/IO/DxfR12Codec.Attributes.cs',r12)

elif part == 'lifecycle':
    copy('DxfDocument.AttributeMText.cs','netDxf/DxfDocument.AttributeMText.cs')
    def document(text):
        text = replace(text,'this.ValidateStoredTableEntityAdoption(entity);','this.ValidateEmbeddedAttributeEntity(entity);\n            this.ValidateStoredTableEntityAdoption(entity);')
        # Normalize every attribute/definition admission site immediately before registering its fallback style.
        pattern = r'(?m)^(\s*)((?:attribute|att|attDef|attdef|attdefClone)\.Style = this\.textStyles\.Add\([^;]+;)'
        text, count = re.subn(pattern,lambda m:m.group(1)+m.group(2).split('.')[0]+'.NormalizeMultiline(this);'+m.group(1)+m.group(2),text)
        if count < 2: raise RuntimeError('Expected attribute and definition admission hooks, got ' + str(count))
        return text
    edit('netDxf/DxfDocument.cs',document)
    def blocks(text):
        def patch(body):
            parameter=re.search(r'Add\(Block (\w+), bool assignHandle\)',body).group(1)
            b=body.index('{')+1
            return body[:b]+'\n            this.Owner.ValidateEmbeddedAttributeBlock('+parameter+');'+body[b:]
        return method(text,'internal override Block Add(',patch)
    edit('netDxf/Collections/BlockRecords.cs',blocks)
    def styles(text):
        text=replace(text,'if (this.References[item.Name].Count != 0)','if (this.References[item.Name].Count != 0 || this.Owner.HasEmbeddedAttributeTextStyleReference(item))')
        anchor=text.rfind('\n    }')
        methods='''
        /// <inheritdoc />
        public override bool HasReferences(string name) { return this.GetReferences(name).Count != 0; }
        /// <inheritdoc />
        public override bool HasReferences(TextStyle item) { return item != null && this.HasReferences(item.Name); }
        /// <inheritdoc />
        public override IReadOnlyList<DxfObjectReference> GetReferences(TextStyle item)
        { return item == null ? new DxfObjectReference[0] : this.GetReferences(item.Name); }
        /// <inheritdoc />
        public override IReadOnlyList<DxfObjectReference> GetReferences(string name)
        {
            var counts = new Dictionary<DxfObject, int>();
            foreach (DxfObjectReference reference in base.GetReferences(name)) counts.Add(reference.Reference, reference.Uses);
            if (name != null && this.Contains(name))
                foreach (var entry in this.Owner.EmbeddedAttributeTextEntries())
                    if (ReferenceEquals(entry.Value.Style, this[name]))
                    { int count; counts.TryGetValue(entry.Key, out count); counts[entry.Key] = count + 1; }
            var result = new List<DxfObjectReference>();
            foreach (var entry in counts) result.Add(new DxfObjectReference(entry.Key, entry.Value));
            return result.AsReadOnly();
        }
'''
        if 'using System.Collections.Generic;' not in text:
            text=text.replace('using System;','using System;\nusing System.Collections.Generic;')
        return text[:anchor]+methods+text[anchor:]
    edit('netDxf/Collections/TextStyles.cs',styles)
    def transform(text):
        text=replace(text,'private readonly bool clearProxy;','private readonly bool clearProxy;\n            private readonly AttributeMText embedded;')
        # Candidate construction happens before any attributes publish.
        sig='internal InsertTransform PrepareInsertTransform('
        def candidate(body):
            marker='return new InsertTransform('
            pos=body.index(marker)
            # The constructor retains its signature. Assigning the readonly field there uses a
            # precomputed source candidate carried as an added final constructor argument.
            return body[:pos]+body[pos:]
        # Expand the constructor declaration and its one invocation with the body candidate.
        m=re.search(r'internal InsertTransform\((.*?)\)\s*\{',text,re.S)
        if not m: raise RuntimeError('InsertTransform constructor missing')
        old=m.group(0)
        new=old.replace(')\n',', AttributeMText embedded)\n',1) if ')\n' in old else None
        if new is None: new=old[:old.rfind(')')]+', AttributeMText embedded'+old[old.rfind(')'):]
        new+='\n                this.embedded = embedded;'
        text=text[:m.start()]+new+text[m.end():]
        def prepare(body):
            pos=body.index('return new InsertTransform(')
            end=body.index(';',pos)
            call=body[pos:end]
            close=call.rfind(')')
            arg=', this.definition.MultilineText == null ? null : this.definition.MultilineText.TransformBy(transformation, translation)'
            return body[:pos]+call[:close]+arg+call[close:]+body[end:]
        text=method(text,sig,prepare)
        text=replace(text,'target.position = this.position;','target.multilineText = this.embedded;\n                target.position = this.position;')
        return text
    edit('netDxf/Entities/Attribute.InsertTransform.cs',transform)

elif part == 'tests':
    copy('AttributeMTextTests.cs','tests/netDxf.Conformance/AttributeMTextTests.cs')
    def register(text):
        return replace(text,'RegisterMTextColumnTests();','RegisterAttributeMTextTests();\n        RegisterMTextColumnTests();')
    edit('tests/netDxf.Conformance/Program.cs',register)
else:
    raise ValueError(part)
print('Applied',part)
