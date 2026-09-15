#!/usr/bin/env python3
"""Challenge actual mixed-output fields omitted by the initial integration gate."""
import argparse, copy, hashlib, importlib.util, json, sys
from pathlib import Path
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument("verifier",type=Path)
parser.add_argument("manifest",type=Path)
parser.add_argument("output",type=Path)
parser.add_argument("--tool-imports",type=Path)
args=parser.parse_args()
sys.path.insert(0,str(args.tool_imports or args.verifier.resolve().parent))
spec=importlib.util.spec_from_file_location("mixed_review_target",args.verifier)
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
expected=json.loads(args.manifest.read_text());drawing=Path(str(args.manifest)[:-5]);wire=module.records(drawing.read_bytes())
module.validate(wire,expected)
results=[]
for label,code in [("hatch-knot",40),("hatch-rational-weight",42),("hatch-rational-flag",73),("style-description",3),("style-horizontal-margin",40)]:
    changed=copy.deepcopy(wire);tags=changed[expected["hatch" if label.startswith("hatch") else "style"]]
    start=next(i for i,t in enumerate(tags) if t==(72,4))+1 if label.startswith("hatch") else tags.index((100,"AcDbTableStyle"))+1
    at=next(i for i in range(start,len(tags)) if tags[i][0]==code)
    old=tags[at][1];new="BROKEN-HEADER" if isinstance(old,str) else 0 if code==73 else old+.125
    tags[at]=(code,new)
    try:module.validate(changed,expected);results.append({"mutation":label,"accepted":True,"old":old,"new":new})
    except (ValueError,KeyError,StopIteration,IndexError) as error:results.append({"mutation":label,"accepted":False,"old":old,"new":new,"error":str(error)})
changed=copy.deepcopy(wire);target=changed[expected["target"]]
at=next(i for i,(code,value) in enumerate(target) if code==1005 and wire.get(value,[None])[0]==(0,"SECTION_MANAGER"))
manager=target[at][1];del target[at]
try:module.validate(changed,expected);results.append({"mutation":"legacy-manager-xdata-link","accepted":True,"removed":manager})
except (ValueError,KeyError,StopIteration,IndexError) as error:results.append({"mutation":"legacy-manager-xdata-link","accepted":False,"removed":manager,"error":str(error)})
report={"verifier_sha256":hashlib.sha256(args.verifier.read_bytes()).hexdigest(),"input_sha256":hashlib.sha256(drawing.read_bytes()).hexdigest(),"manifest_sha256":hashlib.sha256(args.manifest.read_bytes()).hexdigest(),"controls":len(results),"accepted_corruptions":sum(x["accepted"] for x in results),"results":results}
args.output.write_text(json.dumps(report,indent=2)+"\n");print(json.dumps(report,indent=2))
