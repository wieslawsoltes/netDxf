import os, subprocess, sys, shutil, json
from pathlib import Path
root=Path('/workspace/scratch/2ec4aa26f01f')
repo=root/'netDxf-pr94-fixture-integration'
configuration=sys.argv[1]
out=root/f'pr94-fixture-{configuration.lower()}'
out.mkdir(exist_ok=True)
results=[]
for prefix in ('datatable','table-style','stored-field','composite-table','source-reference'):
    env=dict(os.environ,DXF_TEST_FILTER=prefix+'/',DXF_TEST_ARTIFACTS=str(out))
    log=root/f'pr94-fixture-{configuration.lower()}-{prefix}.log'
    with log.open('w') as stream:
        run=subprocess.run(['/workspace/runtime/dotnet8/dotnet',str(repo/f'tests/netDxf.Conformance/bin/{configuration}/net8.0/netDxf.Conformance.dll')],cwd=repo,env=env,stdout=stream,stderr=subprocess.STDOUT)
    if (out/'results.json').exists():shutil.copyfile(out/'results.json',out/f'{prefix}-results.json')
    results.append(dict(prefix=prefix,exit_code=run.returncode,log=str(log)))
    print(prefix,run.returncode,flush=True)
(out/'focused-runs.json').write_text(json.dumps(results,indent=2)+'\n')
sys.exit(any(r['exit_code'] for r in results))
