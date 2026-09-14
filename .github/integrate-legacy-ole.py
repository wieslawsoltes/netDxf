from pathlib import Path
r=Path('.')
def edit(file,old,new,count=1):
 p=r/file;b=p.read_bytes();old=old.encode();new=new.encode();assert b.count(old)==count,(file,b.count(old));p.write_bytes(b.replace(old,new))
edit('netDxf/Entities/EntityType.cs','        Ole2Frame\r\n','        Ole2Frame,\r\n\r\n        /// <summary>Inert legacy OLEFRAME object.</summary>\r\n        OleFrame\r\n')
edit('netDxf/DxfObjectCode.cs','        public const string Ole2Frame = "OLE2FRAME";','        public const string Ole2Frame = "OLE2FRAME";\r\n\r\n        /// <summary>Legacy OLEFRAME entity.</summary>\r\n        public const string OleFrame = "OLEFRAME";')
edit('netDxf/SubclassMarker.cs','        public const string Ole2Frame = "AcDbOle2Frame";','        public const string Ole2Frame = "AcDbOle2Frame";\r\n\r\n        /// <summary>Legacy OLEFRAME subclass.</summary>\r\n        public const string OleFrame = "AcDbOleFrame";')
edit('netDxf/DxfDocument.cs','                case EntityType.Ole2Frame:\r\n','                case EntityType.Ole2Frame:\r\n                case EntityType.OleFrame:\r\n',2)
edit('netDxf/IO/DxfReader.cs','                case DxfObjectCode.Ole2Frame:\n','                case DxfObjectCode.OleFrame:\n                    dxfObject = this.ReadOleFrame();\n                    break;\n                case DxfObjectCode.Ole2Frame:\n')
edit('netDxf/IO/DxfWriter.cs','                case EntityType.Ole2Frame:\n','                case EntityType.OleFrame:\n                    this.WriteOleFrame((OleFrame) entity);\n                    break;\n                case EntityType.Ole2Frame:\n')
p=r/'netDxf/Collections/DrawingEntities.cs';s=p.read_text();needle='        /// <summary>Gets the inert OLE2FRAME entities in the active layout.</summary>';assert s.count(needle)==1
p.write_text(s.replace(needle,'''        /// <summary>Gets the inert legacy OLEFRAME entities in the active layout.</summary>
        public IEnumerable<OleFrame> OleFrames
        {
            get { return this.document.Layouts[this.activeLayout].AssociatedBlock.Entities.OfType<OleFrame>(); }
        }

'''+needle))
edit('tests/netDxf.Conformance/RegressionTests.cs','        RegisterOle2FrameTests();','        RegisterOle2FrameTests();\n        RegisterOleFrameTests();')
