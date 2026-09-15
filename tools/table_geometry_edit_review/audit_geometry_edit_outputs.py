"""Independent ezdxf audit of the explicitly supplied probe output directories."""
import hashlib
import json
from pathlib import Path
import sys

import ezdxf


for argument in sys.argv[1:]:
    directory = Path(argument).resolve()
    records = []
    for path in sorted(directory.glob("*.dxf")):
        drawing = ezdxf.readfile(path)
        audit = drawing.audit()
        records.append({"file": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                        "errors": len(audit.errors), "repairs": len(audit.fixes)})
    result = {"ezdxf": ezdxf.__version__, "outputs": len(records),
              "errors": sum(record["errors"] for record in records),
              "repairs": sum(record["repairs"] for record in records), "files": records}
    (directory / "ezdxf-audit.json").write_text(json.dumps(result, indent=2) + "\n")
    print(directory.name, result["outputs"], "outputs;", result["errors"], "errors;", result["repairs"], "repairs")
    assert records and result["errors"] == result["repairs"] == 0
