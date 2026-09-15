# Independent CELLSTYLEMAP review

The unchanged independent review harness records 58 runtime checks against
production commit `fc0fdef74dd40767340e0628ae2dbf542b228a0e`. Its exact measured
library, harness, results and output-audit hashes are in
[`cell-style-map-review/summary.json`](../../doc/dxf-conformance/cell-style-map-review/summary.json).
The neighboring result and audit files are retained unchanged from that review.
The harness includes unused helpers inherited from earlier independent object
reviews; its entry point invokes only `CellStyles`.

To review a separately built net8.0 library, build this project with
`-p:DxfReviewLibrary=/absolute/path/to/netDxf.netstandard.dll` and run its DLL
with two arguments: the repository directory and a fresh output directory.
The standard `verify_stored_cell_style_map.py` gate covers the separate main
conformance artifacts; it does not consume this harness's output filenames.

The measured review passed all 58 cases and audited 26 exported DXFs with ezdxf
1.4.4, with no errors or repairs. This is storage and identity qualification;
native AutoCAD open/AUDIT/save/reopen and formatting evaluation were not run.
