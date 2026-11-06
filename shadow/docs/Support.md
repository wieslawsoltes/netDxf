# Shadow support and remaining work

Full AutoCAD parity is not established. Capture, source editing, presentation,
Studio workflows and interoperability have separate contracts. A passing test
qualifies the behavior it exercises; preserved metadata does not imply that a
consumer can edit, evaluate or render it.

Use the [topic index](README.md) for API contracts and the
[CI/release guide](../../doc/CI-RELEASE.md) for current qualification commands and
run evidence. The table below retains the outstanding product scope from the
former parity audit. In particular, Studio layer-management gaps are separate
from the [host-supplied rendering override API](PaperViewports.md#immutable-viewport-layer-overrides).

## Supported areas and follow-up work

| Area and supported contract | Remaining work / completion evidence |
| --- | --- |
| [Native authoring and MCP](coding-agents.md) | Build a per-native-entity/property/method coverage inventory, close unsupported authoring/editing shapes, and exercise them through actual Studio/MCP workflows. Generic reflection access alone does not prove safe full editing support. |
| [Graph analysis](GraphAnalysis.md) | Continue large/imported drawing validation and broader discipline engineering workflows. Geometric topology and source ownership/dependencies do not imply physical process connectivity or engineering simulation. |
| [Ribbon and palettes](WorkspaceLayout.md) | Continue command discoverability and accessibility as commands are added. CUIx interoperability and native multi-monitor windows remain open. |
| [Block authoring and insertion](BlockAuthoring.md) | Command-line -ATTDEF and richer definition text settings, duplicate-tag layout copying, library catalog/version workflows and remaining transfer metadata, richer insertion/array workflows and supported attributed/nonuniform explode cases. Dynamic/parametric block behavior is not implemented. |
| [Editing and drafting aids](CadWorkspace.md) | Extend entity/coordinate-system coverage for each command, curve trim/extend/offset, richer arrays and additional acquisition modes. Existing planar restrictions remain material. |
| [Layer, line and text-style managers](LayerStates.md) | Native filter dictionaries, automatic resource reconstruction, individual viewport overrides, plot-style workflows, text editing/formatting and externally referenced resource behavior. |
| [Provider caching](coding-agents.md) | Verify supported provider/model combinations with opt-in real accounts, measure cache hits on repeated identical tasks, and compare quality/latency/cost. Fixtures do not establish actual provider cache performance. |
| [Coding chat and Markdown](coding-agents.md) | Continue conversation editing/branching/search and accessibility/workspace usability; measure long conversation rendering under realistic tool-output loads. |
| [Reusable libraries and hosts](ReusableProjects.md) | Preserve library dependency boundaries and package consumer evidence for each new feature. App-specific glue remains in hosts; no duplicate implementations between native/browser. |
| [Tank-and-pump P&ID](ProcessDiagram.md) | Review each displayed symbol and drafting convention against authoritative standards material and document exact clauses/editions. Starter artwork and reference links alone do not prove standards conformance. |
| [Codex and ACP clients](coding-agents.md) | Real installed-agent login/reconnect/session interoperability remains unverified. Publish tested protocol/version compatibility and extend context controls for other agents where supported. Do not invent unsupported Claude/Gemini subscription-login methods. |
| [Discipline modules](DisciplineModules.md) | These are starter schematic modules. Expand discipline catalogs and authoring, tag/spec/circuit management, reports/schedules and project-level workflows. Electrical protection calculations, hydraulic analysis, 3D piping and isometrics are not implemented. |
| [Recorded CAD workflows](DrawingHealthAndWorkflows.md) | Grow recorded scenarios across remaining commands and entity/resource families. Add rendered regression coverage where semantic hashes omit clipping/paint/external-resource behavior. |
| [Discipline validation](DisciplineModules.md) | Expand rules to the new discipline data and symbol families. Test fault cases on representative imported drawings. Geometry crossings are intentionally not automatic connectivity. |
| [Agent previews and undo](DrawingHealthAndWorkflows.md) | Broaden proposal operations with new command families and ensure all agent mutation surfaces offer an appropriate preview path. |
| [Drawing health](DrawingHealthAndWorkflows.md) | Extend invalid-geometry coverage to splines, self-intersections and hatch boundaries, external resources, and additional native entities. A clean bounded report must not be presented as full drawing validity. |
| [Symbol libraries](BlockAuthoring.md) | Persistent user-library catalogs, version migration/replacement, search/paging and broader reviewed catalogs; extend reference and attribute preservation across version updates. |
| [Agent efficiency evaluation](../src/netDxf.Shadow.Agents.Evaluation/README.md) | Actual task-success/latency/tokens/cache/cost comparisons are outstanding. Do not substitute synthetic reports for live comparison results. |
| [Drawing references in chat](coding-agents.md) | Exercise references across new editing commands and external-agent steering. Expand explicit resource/nested-object workflows beyond ordinary selected occurrences as needed. |

## Additional boundaries

Discipline isolation, schedules and graph cuts describe explicitly supplied
connectivity and metadata. They do not simulate equipment behavior, purchased
quantities or operational risk. Connection repair/disconnect/reconnect authoring,
richer reports, project workflows and catalog/version updates remain follow-up
work; see [discipline modules](DisciplineModules.md) and [graph analysis](GraphAnalysis.md).

Rendering has explicit consumer policies for [linetype placement](LinetypePlacement.md),
[BIGFONT mappings](BigFont.md), [vertical SHX typography](Shx.md#vertical-typography),
[perspective viewports](Perspective.md) and [layer overrides](PaperViewports.md#immutable-viewport-layer-overrides).
Those bounded policies do not establish proprietary-renderer equivalence. Complete
paragraph bidi/shaping and field typography, custom geometry/ACIS evaluation,
complete dimension regeneration, arbitrary affine editing, exact miter/gradient
behavior and certified analytic island predicates remain outside full parity.
Consult each feature's refusal and resource-limit contracts.

Provider, Codex and ACP fixtures do not establish real account login, installed-agent
interoperability or measured provider/cache performance. Live evaluation remains
opt-in; [evaluation instructions](../tests/netDxf.Shadow.Agents.LiveEvaluation/README.md)
define credential and budget requirements. Synthetic reports must stay labeled.

## Historical implementation records

The chronological [parity audit](https://github.com/wieslawsoltes/netDxf/blob/2080d937a1caf8fb8f49cee6b04f88f12f17a0be/shadow/docs/ParityAudit.md)
and [milestone checklist](https://github.com/wieslawsoltes/netDxf/blob/2080d937a1caf8fb8f49cee6b04f88f12f17a0be/shadow/docs/ImplementationChecklist.md)
remain available at the revision before consolidation. They contain dated test
totals and superseded limitations; use current guides and CI results for present
behavior.
