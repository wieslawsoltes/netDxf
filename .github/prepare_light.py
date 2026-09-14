from pathlib import Path
root = Path('.')
changes = {
 'netDxf/Entities/EntityType.cs': [('        Helix\n', '        Helix,\n\n        /// <summary>Distant, point or spot light.</summary>\n        Light\n')],
 'netDxf/DxfObjectCode.cs': [('        public const string Helix = "HELIX";', '        public const string Helix = "HELIX";\n\n        /// <summary>Light entity.</summary>\n        public const string Light = "LIGHT";')],
 'netDxf/SubclassMarker.cs': [('        public const string Helix = "AcDbHelix";', '        public const string Helix = "AcDbHelix";\n\n        /// <summary>Light subclass.</summary>\n        public const string Light = "AcDbLight";')],
 'netDxf/DxfDocument.cs': [('                case EntityType.Helix:\n', '                case EntityType.Helix:\n                case EntityType.Light:\n')],
 'netDxf/Collections/DrawingEntities.cs': [('        public IEnumerable<Helix> Helices', '        public IEnumerable<Helix> Helices')],
 'netDxf/IO/DxfReader.cs': [('                case DxfObjectCode.Helix:\n', '                case DxfObjectCode.Light:\n                    dxfObject = this.ReadLight();\n                    break;\n                case DxfObjectCode.Helix:\n')],
 'netDxf/IO/DxfWriter.cs': [('            this.ValidateHelixVersions();', '            this.ValidateHelixVersions();\n            this.ValidateLightVersions();'), ('                case EntityType.Helix:\n', '                case EntityType.Light:\n                    this.WriteLight((Light) entity);\n                    break;\n                case EntityType.Helix:\n')],
 'tests/netDxf.Conformance/RegressionTests.cs': [('        RegisterHelixWireTests();', '        RegisterLightTests();\n        RegisterHelixWireTests();')]
}
for name, replacements in changes.items():
 p = root / name
 raw = p.read_bytes(); crlf = b'\r\n' in raw; s = raw.decode().replace('\r\n', '\n')
 for old, new in replacements:
  assert s.count(old) == (2 if name == 'netDxf/DxfDocument.cs' else 1), (name, old)
  s = s.replace(old, new)
 if name == 'netDxf/Collections/DrawingEntities.cs':
  at = s.index('        /// <summary>Gets splines, including derived HELIX')
  s = s[:at] + '''        /// <summary>Gets lights in the active layout.</summary>
        public IEnumerable<Light> Lights
        {
            get { return this.document.Layouts[this.activeLayout].AssociatedBlock.Entities.OfType<Light>(); }
        }

''' + s[at:]
 p.write_bytes(s.replace('\n', '\r\n').encode() if crlf else s.encode())
