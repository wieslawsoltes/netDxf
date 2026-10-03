from pathlib import Path
import shutil
import sys

root = Path(sys.argv[1]).resolve()
here = Path(__file__).resolve().parent
target = root / 'tools/verify_r12_selection_graphs.py'
if target.exists():
    raise SystemExit('Refusing to overwrite an existing graph verifier')
shutil.copyfile(here / target.name, target)
