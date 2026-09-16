# Independent mixed editing review

The initial mixed-output verifier accepted six corruptions: a changed HATCH knot,
weight or rational flag; a changed TABLESTYLE description or horizontal margin;
and deletion of the legacy VERTEX's SECTION_MANAGER XData reference. The C# mixed
reload assertions likewise did not check those exact persisted values. This was a
validation gap; the review did not establish a production implementation defect.

The strengthened gate checks the complete retained periodic spline packet and
edited style header, and checks the exact ordered child XData reference list.
The fixture now includes nonempty fit points and tangents, and the C# checks include
manager protection after reloading. The independent probe accepts the new unmodified
fixture and rejects every previously accepted corruption.

The JSON receipt retains the source, input, manifest and verifier hashes and each
mutation result. The same six challenge definitions were used before and after. This is a standalone
historical comparison tool, not a mandatory full-fixture gate: its report records
accepted corruptions, and a successful process exit means the comparison ran.
The `review_` filename keeps it outside automatic `verify_*.py` discovery.
The updated fixture means before/after input hashes intentionally differ.

Generate the `eleventh-mixed/` conformance artifacts, then run the challenge:

```sh
python tools/review_eleventh_mixed_oracle.py \
  tools/verify_eleventh_mixed.py \
  artifacts/eleventh-mixed-acad_table_simple.dxf-False-False-edited.dxf.json \
  independent-mixed-review.json
```

To reproduce the previous false passes, generate the baseline artifacts at the
receipt's baseline commit, extract that version of `verify_eleventh_mixed.py`, and
pass `--tool-imports /path/to/repository/tools` when the extracted verifier is stored
outside the tools directory. The script validates the unmodified input first, then
records acceptance or rejection of each mutation. Native CAD execution is outside
this review.
