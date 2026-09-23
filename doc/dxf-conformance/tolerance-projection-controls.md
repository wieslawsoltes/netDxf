# Cross-mode inheritance and absent-override controls

The existing mode-transition suite added by the reconciled tolerance drafts
starts from symmetric styles and explicitly selects a mode for each host. This
additional matrix independently includes symmetric and deviation base styles,
all four explicit target modes, and an absent mode override. It verifies that
the native lower value is emitted only when inheritance cannot represent the
selected effective value. Unselected upper bounds must never be materialized.

The 96 cases cover all six typed versions, both source transports, DIMENSION and
LEADER, and modelspace, paper layouts, referenced and unreferenced blocks. Forty
hosts in every case combine both base modes, five explicit/absent selections,
and four scalar pairs including subnormals and signed zero. Both output
transports are loaded again. Source state, inactive lower bits, sparse dictionary
counts, loaded lower presence/bits, clones, handles and other-application data
are checked. The 288-drawing corpus contains 11,520 hosts.

The independent checker validates physical table and DSTYLE values separately
from typed ezdxf reads, retaining explicit numeric equality for mode inference
and binary64 bits for stored values. Its packet and inventory corruption
controls must reject. This expands regression coverage for the already merged
writer correction; it does not introduce a second production implementation,
change enum admission, weaken existing assertions or qualify native rendering.
Actual executions belong in the PR, not inferred from the matrix definition.

The initial development packet helper confused Int16 values with identifiers;
its final form consumes each complete DSTYLE pair and retains every assertion.
The draft-import workbench duplicated source already published during an earlier
interrupted continuation. The final integration keeps those published draft
suites rather than adding duplicate renamed copies, and carries forward only
this additional base-mode/absent-selection matrix and the merge-queue changes.
