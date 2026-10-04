from pathlib import Path
import difflib, re, shutil, sys
root = Path(sys.argv[1]).resolve()
stage = Path(__file__).resolve().parent
part = sys.argv[2]

def edit(name, change):
    p=root/name; original=p.read_bytes()
    decoded=original.decode('utf-8-sig',errors='surrogateescape')
    text=decoded.replace('\r\n','\n');updated=change(text)
    if updated==text: raise RuntimeError('No change: '+name)
    old=text.splitlines(True); new=updated.splitlines(True); physical=decoded.splitlines(True)
    ending='\r\n' if original.count(b'\r\n')>original.count(b'\n')//2 else '\n'
    output=[]
    for op,a,b,c,d in difflib.SequenceMatcher(None,old,new,autojunk=False).get_opcodes():
        if op=='equal': output.extend(physical[a:b])
        elif op in ('insert','replace'): output.extend(line.replace('\n',ending) for line in new[c:d])
    p.write_bytes((b'\xef\xbb\xbf' if original.startswith(b'\xef\xbb\xbf') else b'')+''.join(output).encode('utf-8',errors='surrogateescape'))

def replace(text,old,new,count=1):
    found=text.count(old)
    if found!=count: raise RuntimeError(f'Expected {count}, found {found}: {old[:100]}')
    return text.replace(old,new)

def method(text,signature,change):
    start=text.index(signature);brace=text.index('{',start)
    token=re.compile(r'//[^\n]*|/\*.*?\*/|@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|[{}]',re.S)
    depth=0
    for match in token.finditer(text,brace):
        if match.group()=='{':depth+=1
        elif match.group()=='}':
            depth-=1
            if depth==0:return text[:start]+change(text[start:match.end()])+text[match.end():]
    raise RuntimeError('Unclosed '+signature)

def copy(name,destination):
    p=root/destination
    if p.exists():raise RuntimeError('Already exists '+destination)
    p.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(stage/name,p)

if part=='model':
    copy('AttributeMText.cs','netDxf/Entities/AttributeMText.cs')
    copy('Attribute.Multiline.cs','netDxf/Entities/Attribute.Multiline.cs')
    def model(text):
        text=text.replace('else if (tag.Code == 11)','else if (VectorCode(tag.Code, 11))')
        old='Vector3 x = matrix * Vector3.UnitX, y = matrix * Vector3.UnitY, z = matrix * Vector3.UnitZ;'
        text=replace(text,old,'if (matrix == Matrix3.Identity && translation == Vector3.Zero) return this;\n            '+old)
        pos=text.index('        internal static bool IsSupportedCode')
        return text[:pos]+'''        internal static bool Same(AttributeMText first, AttributeMText second)
        {
            return ReferenceEquals(first, second) || (first != null && second != null
                && ReferenceEquals(first.Style, second.Style) && first.Tags.SequenceEqual(second.Tags));
        }
'''+text[pos:]
    edit('netDxf/Entities/AttributeMText.cs',model)
    def binding(text):
        text=text.replace('internal void NormalizeMultiline(DxfDocument document)',
            'internal void RestoreMultilineText(AttributeMText value) { this.multilineText = value; }\n        internal void NormalizeMultiline(DxfDocument document)')
        def document(body):
            return '''internal static DxfDocument Document(DxfObject host)
        {
            if (host is Attribute attribute) return attribute.Owner?.Owner?.Record.Owner?.Owner;
            if (host is AttributeDefinition definition) return definition.Owner?.Record.Owner?.Owner;
            return null;
        }'''
        return method(text,'internal static DxfDocument Document(',document)
    edit('netDxf/Entities/Attribute.Multiline.cs',binding)
    for name in ('Attribute','AttributeDefinition'):
        def lifecycle(text):
            text,n=re.subn(r'this\.CopyCommonDataTo\((\w+)\);',lambda m:m.group(0)+'\n            this.CopyMultilineTo('+m.group(1)+');',text)
            if not n:raise RuntimeError('No verified clone hook in '+name)
            def transform(body):
                i=body.index('{')+1
                body=body[:i]+'\n            AttributeMText embeddedCandidate = this.multilineText == null ? null : this.multilineText.TransformBy(transformation, translation);'+body[i:]
                return body[:-1]+'    this.multilineText = embeddedCandidate;\n        }'
            text=method(text,'public void TransformBy(Matrix3 transformation, Vector3 translation)',transform)
            if name=='Attribute':
                text=method(text,'public Attribute(AttributeDefinition definition)',lambda b:b[:-1]+'    this.multilineText = definition.MultilineText;\n        }')
            return text
        edit('netDxf/Entities/'+name+'.cs',lifecycle)

elif part=='io':
    copy('DxfAttributeMTextCodec.cs','netDxf/IO/DxfAttributeMTextCodec.cs')
    def reader(text):
        for signature,definition in [('private AttributeDefinition ReadAttributeDefinition()',True),('private Attribute ReadAttribute(',False)]:
            def patch(body):
                b=body.index('{')+1
                body=body[:b]+'\n            AttributeMText embeddedAttributeText = null;\n            bool attributeSubclass = false;\n            short? multilineType = null;\n'+body[b:]
                loop=re.search(r'while\s*\(this\.chunk\.Code != 0\)\s*\{',body)
                if not loop:raise RuntimeError('Attribute reader loop missing')
                scope='AcDbAttributeDefinition' if definition else 'AcDbAttribute'
                expected=4 if definition else 2
                fallback='style' if definition else 'this.GetTextStyle(styleName)'
                guard='''
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
'''%(scope,expected,fallback)
                body=body[:loop.end()]+guard+body[loop.end():]
                ret=re.search(r'return\s+(\w+)\s*;\s*}$',body)
                if not ret:raise RuntimeError('Attribute return missing')
                addition='''if (multilineType.HasValue && (multilineType.Value != 1) != (embeddedAttributeText != null))
                throw new FormatException("Attribute type and embedded body disagree.");
            %s.RestoreMultilineText(embeddedAttributeText);
            '''%ret.group(1)
                return body[:ret.start()]+addition+body[ret.start():]
            text=method(text,signature,patch)
        return text
    edit('netDxf/IO/DxfReader.cs',reader)
    def writer(text):
        text=replace(text,'this.ValidateMTextColumns();','this.ValidateAttributeMText();\n            this.ValidateMTextColumns();')
        for signature,definition in [('private void WriteAttributeDefinition(',True),('private void WriteAttribute(',False)]:
            def patch(body):
                parameter=re.search(r'\(\s*(?:AttributeDefinition|Attribute)\s+(\w+)',body).group(1)
                old='this.WriteXData('+parameter+'.XData);'
                return replace(body,old,'this.WriteAttributeMText('+parameter+'.MultilineText, '+str(definition).lower()+');\n            '+old)
            text=method(text,signature,patch)
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

elif part=='lifecycle':
    copy('DxfDocument.AttributeMText.cs','netDxf/DxfDocument.AttributeMText.cs')
    copy('Insert.Multiline.cs','netDxf/Entities/Insert.Multiline.cs')
    edit('netDxf/Entities/Insert.Multiline.cs',lambda t:t.replace('candidate.MultilineText = attribute.MultilineText == null ? null\n                    : (definition.MultilineText ?? attribute.MultilineText).WithValue(attribute.MultilineText.Value);',
        'candidate.RestoreMultilineText(attribute.MultilineText == null ? null\n                    : (definition.MultilineText ?? attribute.MultilineText).WithValue(attribute.MultilineText.Value));'))
    def document(text):
        text=replace(text,'this.ValidateStoredTableEntityAdoption(entity);','this.ValidateEmbeddedAttributeEntity(entity);\n            this.ValidateStoredTableEntityAdoption(entity);')
        pattern=r'(?m)^(\s*)((?:attribute|att|attDef|attdef|attdefClone)\.Style = this\.textStyles\.Add\([^;]+;)'
        text,count=re.subn(pattern,lambda m:m.group(1)+m.group(2).split('.')[0]+'.NormalizeMultiline(this);'+m.group(1)+m.group(2),text)
        if count<2:raise RuntimeError('Attribute/definition admission hooks '+str(count))
        return text
    edit('netDxf/DxfDocument.cs',document)
    def blocks(text):
        def patch(body):
            parameter=re.search(r'Add\(Block (\w+), bool assignHandle\)',body).group(1);b=body.index('{')+1
            return body[:b]+'\n            this.Owner.ValidateEmbeddedAttributeBlock('+parameter+');'+body[b:]
        return method(text,'internal override Block Add(',patch)
    edit('netDxf/Collections/BlockRecords.cs',blocks)
    def styles(text):
        text=replace(text,'if (this.References[item.Name].Count != 0)','if (this.References[item.Name].Count != 0 || this.Owner.HasEmbeddedAttributeTextStyleReference(item))')
        if 'using System.Collections.Generic;' not in text:text=text.replace('using System;','using System;\nusing System.Collections.Generic;')
        anchor=text.rfind('\n    }')
        return text[:anchor]+'''
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
'''+text[anchor:]
    edit('netDxf/Collections/TextStyles.cs',styles)
    def transform(text):
        text=replace(text,'position = this.position, normal = this.normal, height = this.height,','multilineText = this.multilineText,\n                position = this.position, normal = this.normal, height = this.height,')
        text=replace(text,'if (translationOnly) next.position = InfiniteLineTransform.TransformPoint(matrix, next.position, translation);',
            'if (translationOnly)\n            {\n                next.position = InfiniteLineTransform.TransformPoint(matrix, next.position, translation);\n                next.multilineText = this.multilineText == null ? null : this.multilineText.TransformBy(matrix, translation);\n            }')
        text=replace(text,'|| this.isBackward != next.isBackward || this.isUpsideDown != next.isUpsideDown;',
            '|| this.isBackward != next.isBackward || this.isUpsideDown != next.isUpsideDown\n                || !AttributeMText.Same(this.multilineText, next.multilineText);')
        text=replace(text,'this.position = next.position; this.normal = next.normal; this.height = next.height;',
            'this.multilineText = next.multilineText;\n            this.position = next.position; this.normal = next.normal; this.height = next.height;')
        return text
    edit('netDxf/Entities/Attribute.InsertTransform.cs',transform)
    def insert(text):
        def prepend(body,statement):
            b=body.index('{')+1;return body[:b]+'\n            '+statement+body[b:]
        text=method(text,'public void TransformAttributes()',lambda b:prepend(b,'if (this.HasMultilineAttributes()) { this.TransformMultilineAttributes(); return; }'))
        text=method(text,'public void Sync()',lambda b:prepend(b,'this.PreflightMultilineDefaults();'))
        text=method(text,'public Insert(Block block, Vector3 position)',lambda b:replace(b,'atts.Add(att);',
            'if (att.MultilineText != null) att.RestoreMultilineText(att.MultilineText.TransformBy(Matrix3.Identity, this.position - this.block.Origin));\n                atts.Add(att);'))
        text=method(text,'private List<EntityObject> ExplodeCellCore(',lambda b:replace(b,'// the attributes will be exploded as a Text entity',
            'if (attribute.MultilineText != null)\n                {\n                    entities.Add(ExplodeMultilineAttribute(attribute, arrayOffset));\n                    continue;\n                }\n                // Ordinary attributes are exploded as TEXT.'))
        return text
    edit('netDxf/Entities/Insert.cs',insert)

elif part=='tests':
    copy('AttributeMTextTests.cs','tests/netDxf.Conformance/AttributeMTextTests.cs')
    edit('tests/netDxf.Conformance/AttributeMTextTests.cs',lambda t:t.replace('Equal(AmtValue,text.Value,"INSERT transform did not use definition content");',
        'Equal(AmtValue+" instance",text.Value,"INSERT transform overwrote instance content");'))
    edit('tests/netDxf.Conformance/Program.cs',lambda t:replace(t,'RegisterMTextColumnTests();','RegisterAttributeMTextTests();\n        RegisterMTextColumnTests();'))
else:raise ValueError(part)
print('Applied',part)
