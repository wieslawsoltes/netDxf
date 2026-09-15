# Opaque HATCH release evidence

Production713cca8 implements the bounded release mapping. Tests2857b20 add336 new cases and24 additional aggregate negatives; existing proxy/duplicate controls retain their counts. All336 new cases and429 HATCH source regressions pass in both configurations. The two physical gates require672 and48 final output files per configuration and reject4 and3 corruption controls respectively.

`output-packets.tar.gz` retains2,064 actual owner output DXFs (984 opaque/HATCH and48 ordinary HATCH outputs per configuration), including intermediate lifecycle exports; the672 final opaque/HATCH outputs receive the complete independent packet/backlink comparison and structural audit. Every archive member has a SHA256 entry in `output-packets-manifest.json`.

`independent/` contains the separate reviewer's unchanged harness, exact results and audit code. Its final80/80 Debug and80/80 Release runs compare with12/80 on the frozen baseline. The172 archived independent DXFs consist of12 baseline and160 final outputs. All160 final outputs pass ezdxf1.4.4 structural audit with zero errors, fixes or removed entities. Unknown records remain DXFTagStorage; no application geometry semantics or native CAD execution is claimed.

`initial-diagnostic/` preserves the earlier owner diagnostic unchanged. Its60/72 result deliberately includes24 expected save-refusal tests under the previous contract; it is not the independent before/after assertion set. The12 producer cleanup failures demonstrate the unreleased backlink removal guard. The final qualification JSON explains the separate native-transform fixture correction as well.

All supplied JSON/harness/audit files in `independent/` are copied byte-for-byte from the independent reviewer. The production DLLs are pinned by SHA256 in the qualification receipt; subsequent documentation commits do not change their source.
