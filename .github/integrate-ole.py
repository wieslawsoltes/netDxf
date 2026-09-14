from pathlib import Path
root=Path('.')
def edit(file,old,new,count=1):
 p=root/file;data=p.read_bytes();old=old.encode();new=new.encode()
 assert data.count(old)==count,(file,data.count(old));p.write_bytes(data.replace(old,new))
edit('netDxf/Entities/EntityType.cs','        Light\r\n','        Light,\r\n\r\n        /// <summary>Inert OLE2FRAME binary object.</summary>\r\n        Ole2Frame\r\n')
edit('netDxf/DxfObjectCode.cs','        public const string Light = "LIGHT";','        public const string Light = "LIGHT";\r\n\r\n        /// <summary>Inert OLE2FRAME entity.</summary>\r\n        public const string Ole2Frame = "OLE2FRAME";')
edit('netDxf/SubclassMarker.cs','        public const string Light = "AcDbLight";','        public const string Light = "AcDbLight";\r\n\r\n        /// <summary>OLE2FRAME subclass.</summary>\r\n        public const string Ole2Frame = "AcDbOle2Frame";')
edit('netDxf/DxfDocument.cs','                case EntityType.Light:\r\n','                case EntityType.Light:\r\n                case EntityType.Ole2Frame:\r\n',2)
edit('netDxf/IO/DxfReader.cs','                case DxfObjectCode.Light:\n','                case DxfObjectCode.Ole2Frame:\n                    dxfObject = this.ReadOle2Frame();\n                    break;\n                case DxfObjectCode.Light:\n')
edit('netDxf/IO/DxfWriter.cs','                case EntityType.Light:\n','                case EntityType.Ole2Frame:\n                    this.WriteOle2Frame((Ole2Frame) entity);\n                    break;\n                case EntityType.Light:\n')
p=root/'netDxf/Collections/DrawingEntities.cs';s=p.read_text();needle='        public IEnumerable<Light> Lights\n';at=s.index(needle);at=s.rfind('        /// <summary>',0,at)
s=s[:at]+'''        /// <summary>Gets the inert OLE2FRAME entities in the active layout.</summary>
        public IEnumerable<Ole2Frame> Ole2Frames
        {
            get { return this.document.Layouts[this.activeLayout].AssociatedBlock.Entities.OfType<Ole2Frame>(); }
        }

'''+s[at:];p.write_text(s)
edit('tests/netDxf.Conformance/RegressionTests.cs','        RegisterLightNameTests();','        RegisterLightNameTests();\n        RegisterOle2FrameTests();')
