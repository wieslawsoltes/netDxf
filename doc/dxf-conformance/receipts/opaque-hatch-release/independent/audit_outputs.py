from pathlib import Path
import sys,json,hashlib
import ezdxf
root=Path(sys.argv[1]);files=sorted(root.glob('*.dxf'));assert len(files)==80,len(files);rows=[]
for file in files:
 doc=ezdxf.readfile(file);before={e.dxf.handle for block in doc.blocks for e in block};opaque_before={e.dxf.handle for block in doc.blocks for e in block if e.dxftype()=='OPAQUE_HATCH_TEST'};audit=doc.audit();after={e.dxf.handle for block in doc.blocks for e in block};opaque_after={e.dxf.handle for block in doc.blocks for e in block if e.dxftype()=='OPAQUE_HATCH_TEST'}
 rows.append({'file':file.name,'sha256':hashlib.sha256(file.read_bytes()).hexdigest(),'errors':[str(e) for e in audit.errors],'fixes':[str(e) for e in audit.fixes],'removed_handles':sorted(before-after),'opaque_before':sorted(opaque_before),'opaque_after':sorted(opaque_after)})
result={'reader':'ezdxf '+ezdxf.__version__,'outputs':len(rows),'errors':sum(len(x['errors']) for x in rows),'fixes':sum(len(x['fixes']) for x in rows),'removed_entities':sum(len(x['removed_handles']) for x in rows),'scope':'Structural actual-output audit, not interpretation or evaluation of unknown geometry','files':rows};(root/'audit.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps({k:v for k,v in result.items() if k!='files'}));sys.exit(0 if result['errors']==result['fixes']==result['removed_entities']==0 else 1)
