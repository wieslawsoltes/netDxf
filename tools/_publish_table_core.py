from pathlib import Path
import subprocess
R=Path('.')
originals={'netDxf/IO/DxfReader.cs':'b4c5d594633f5d8b78461fe9f0b9ffa61317586c','netDxf/IO/DxfWriter.cs':'d036777bac6265296cf653d983e5b9b8ac42f7ab','netDxf/Tables/Layer.cs':'5e82ba74ae6553f5db162f62ead4f5944441d02b','tests/netDxf.Conformance/Program.cs':'907221bd42f8bbcaae812cab1d7aa8800e3fdfdb'}
for p,sha in originals.items():
 assert subprocess.check_output(['git','hash-object',str(R/p)],text=True).strip()==sha,p

def edit(p,fn):
 path=R/p; raw=path.read_bytes();crlf=b'\r\n' in raw
 text=raw.decode().replace('\r\n','\n'); changed=fn(text)
 path.write_bytes(changed.replace('\n','\r\n').encode() if crlf else changed.encode())
def rep(s,a,b):
 assert s.count(a)==1,(a[:100],s.count(a));return s.replace(a,b)
def reader(s):
 a=s.index('        private BlockRecord ReadBlockRecord()'); b=s.index('        private DimensionStyle ReadDimensionStyle()',a)
 t=s[a:b]
 t=rep(t,'            DrawingUnits units = DrawingUnits.Unitless;','            DrawingUnits units = DrawingUnits.Unitless;\n            bool hasNativeUnits = false;')
 t=rep(t,'                        units = (DrawingUnits) this.chunk.ReadShort();','                        units = (DrawingUnits) this.chunk.ReadShort();\n                        hasNativeUnits = true;')
 first=t.index('            // here is where DXF versions prior to AutoCad2007 stores the block units')
 last=t.index('            if (!string.IsNullOrEmpty(pointerToLayout)',first)
 t=t[:first]+'''            // XData is attached later. Read the parsed source, not the empty record dictionary.
            // An explicit native field is authoritative; legacy units fill only an absent field.
            if (!hasNativeUnits)
            {
                XData designCenterData = xData.Find(data => string.Equals(data.ApplicationRegistry.Name,
                    ApplicationRegistry.DefaultName, StringComparison.OrdinalIgnoreCase));
                if (designCenterData != null)
                {
                    short? legacyUnits = BlockRecordXData.ReadUnits(designCenterData.XDataRecord);
                    if (legacyUnits.HasValue) record.Units = (DrawingUnits)legacyUnits.Value;
                }
            }

'''+t[last:]
 return s[:a]+t+s[b:]
def writer(s):
 s=rep(s,'''            if (blockRecord.IsForInternalUseOnly)
            {
                return;
            }''','''            if (blockRecord.IsForInternalUseOnly)
            {
                this.WriteXData(blockRecord.XData);
                return;
            }''')
 a=s.index('            AddBlockRecordUnitsXData(blockRecord);');b=s.index('        /// <summary>\n        /// Writes a new line type',a)
 s=s[:a]+'''            this.WriteBlockRecordXData(blockRecord);
        }

'''+s[b:]
 a=s.index('            if (!string.IsNullOrEmpty(layer.Description))'); b=s.index('        /// <summary>\n        /// Writes a new text style',a)
 return s[:a]+'''            this.WriteLayerXData(layer);
        }

'''+s[b:]
def layer(s):
 s=rep(s,'        private string description;','        private string description;\n        internal bool HasDescriptionAssignment { get; private set; }')
 s=rep(s,'            set { this.description = string.IsNullOrEmpty(value) ? string.Empty : value; }','''            set
            {
                this.description = string.IsNullOrEmpty(value) ? string.Empty : value;
                this.HasDescriptionAssignment = true;
            }''')
 s=rep(s,'                Color = (AciColor) this.Color.Clone(),','''                description = this.description,
                HasDescriptionAssignment = this.HasDescriptionAssignment,
                Color = (AciColor) this.Color.Clone(),''')
 return s
edit('netDxf/IO/DxfReader.cs',reader);edit('netDxf/IO/DxfWriter.cs',writer);edit('netDxf/Tables/Layer.cs',layer)
edit('tests/netDxf.Conformance/Program.cs',lambda s:rep(s,'        RegisterDimensionXDataPreservationTests();','        RegisterDimensionXDataPreservationTests();\n        RegisterTableXDataPreservationTests();'))

def layer(s):
 s=rep(s,'        internal bool HasTransparencyAssignment { get; private set; }','        internal bool HasTransparencyAssignment { get; private set; }\n        internal bool HasTransparencyReset { get; private set; }')
 s=rep(s,'''                this.transparency = value ?? throw new ArgumentNullException(nameof(value));
                this.HasTransparencyAssignment = true;''','''                if (value == null) throw new ArgumentNullException(nameof(value));
                // An explicit reset must not depend on a previous Save attaching XData.
                this.HasTransparencyReset |= this.transparency.StoredAlphaValue.HasValue ||
                    this.transparency.Value != 0 || this.transparency.HasValueEdit;
                this.transparency = value;
                this.HasTransparencyAssignment = true;''')
 return rep(s,'                HasTransparencyAssignment = this.HasTransparencyAssignment\n','                HasTransparencyAssignment = this.HasTransparencyAssignment,\n                HasTransparencyReset = this.HasTransparencyReset\n')
edit('netDxf/Tables/Layer.cs',layer)
edit('netDxf/IO/DxfWriter.TableXData.cs',lambda s:rep(s,'''            // Keep the existing alpha-presence and opaque/raw-value policy unchanged.
            bool writeTransparency = layer.Transparency.Value >= 0 &&
                (layer.Transparency.StoredAlphaValue.HasValue || layer.Transparency.Value > 0 ||
                (layer.Transparency.HasValueEdit || layer.HasTransparencyAssignment) && layer.XData.ContainsAppId(transparencyApp));''','''            // Explicit reset/edit intent survives without Save attaching source XData.
            // A fresh unedited opaque value still omits the application.
            bool writeTransparency = layer.Transparency.Value >= 0 &&
                (layer.Transparency.StoredAlphaValue.HasValue || layer.Transparency.Value > 0 ||
                layer.Transparency.HasValueEdit || layer.HasTransparencyReset ||
                layer.HasTransparencyAssignment && layer.XData.ContainsAppId(transparencyApp));'''))
addition='''        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        foreach (bool priorSave in new[] { false, true }) foreach (bool clone in new[] { false, true })
        for (int edit = 0; edit < 3; edit++)
        {
            int e = edit;
            Run($"table-xdata/alpha-reset/{version}/{binary}/{priorSave}/{clone}/{e}", () =>
            {
                var doc = new DxfDocument(version);
                var layer = new Layer(TxLayer) { Transparency = Transparency.FromAlphaValue(0x02000080) };
                doc.Layers.Add(layer);
                if (priorSave)
                {
                    var original = TxSnapshot(layer);
                    using var first = new MemoryStream(); Check(doc.Save(first, binary), "Initial alpha save"); original();
                }
                if (e == 0) layer.Transparency = new Transparency(0);
                else if (e == 1) layer.Transparency.Value = 0;
                else new LayerStateProperties(TxLayer) { Transparency = new Transparency(0) }
                    .CopyTo(layer, LayerPropertiesRestoreFlags.Transparency);
                if (clone)
                {
                    layer = (Layer)layer.Clone(); doc = new DxfDocument(version); doc.Layers.Add(layer);
                }
                var unchanged = TxSnapshot(layer);
                using var output = new MemoryStream(); Check(doc.Save(output, binary), "Opaque reset save"); unchanged();
                Check(!layer.XData.ContainsAppId("AcCmTransparency"), "Save attached source transparency app");
                var raw = LoadRaw(output.ToArray());
                var record = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "LAYER" &&
                    r.Tags.Any(t => t.Code == 2 && Equals(t.Value, TxLayer)));
                Equal(0x020000FF, (int)record.Tags.Single(t => t.Code == 1071).Value, "Opaque reset packed-alpha presence");
                output.Position = 0;
                var loaded = DxfDocument.Load(output)!.Layers[TxLayer];
                Equal((short)0, loaded.Transparency.Value, "Opaque reset projection");
                Equal((int?)0x020000FF, loaded.Transparency.StoredAlphaValue, "Opaque reset stored value");
                var fresh = new Layer("FRESH_ZERO") { Transparency = new Transparency(0) };
                Throws<ArgumentNullException>(() => fresh.Transparency = null!);
                doc.Layers.Add(fresh);
                using var freshOutput = new MemoryStream(); Check(doc.Save(freshOutput, binary), "Fresh opaque save");
                var freshRaw = LoadRaw(freshOutput.ToArray());
                var freshRecord = freshRaw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "LAYER" &&
                    r.Tags.Any(t => t.Code == 2 && Equals(t.Value, "FRESH_ZERO")));
                Check(!freshRecord.Tags.Any(t => t.Code == 1001 && Equals(t.Value, "AcCmTransparency")), "Fresh opaque omission policy");
            });
        }
'''
edit('tests/netDxf.Conformance/TableXDataPreservationTests.cs',lambda s:rep(s,'        var invalid = new XDataRecord[][]',addition+'        var invalid = new XDataRecord[][]'))
def doc(s):
 s=rep(s,'The existing packed-alpha codec, raw-alpha and presence policies are reused.','''The existing packed-alpha codec and raw-alpha policy are reused. Explicit value
edits and property resets after a nondefault/stored value retain packed-alpha
presence even without a previous Save attaching XData. An internal reset flag
is copied by clones and is set only after a valid assignment. Fresh unedited
opaque values keep their previous omission policy.''')
 s=rep(s,'The final harness has 510 cases:','The final harness has 654 cases:')
 s=rep(s,'cases and two missing-layer-slot append cases.','cases, two missing-layer-slot append cases and 144 explicit-alpha-reset cases.')
 s=rep(s,'PR, not inferred from this test definition. One combined local build/run command','''PR, not inferred from this test definition. The initial complete independent
run passed 213 scripts but failed the existing transparency checker: 12 stored
carrier-transfer fixtures expected explicit opaque alpha after a reset. The old
presence predicate depended on a prior Save attaching an application. Production
now retains edit/reset intent without source mutation; the existing checker and
all previous assertions remain unchanged, with 144 new reset/clone/transfer tests.
One combined local build/run command''')
 return s
edit('doc/dxf-conformance/table-xdata-preservation.md',doc)

Path("tools/_publish_table_core.py").unlink()
Path(".github/workflows/publish-table-reviewed.yml").unlink()
