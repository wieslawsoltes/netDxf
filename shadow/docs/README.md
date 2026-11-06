# Shadow documentation

Start with the [architecture and API guide](../README.md), choose libraries from
the [package catalog](../../nuget/README.md#choose-a-package), or run the
[native Studio](../samples/netDxf.Shadow.Studio/README.md) and
[browser Studio](../samples/netDxf.Shadow.Studio.Browser/README.md).

The [support guide](Support.md) records outstanding work and links historical
implementation logs. Full AutoCAD parity is not established; each feature below
has its own supported operations, refusals and resource limits.

## Source and editing

| Guide | Use it for |
| --- | --- |
| [Source consistency](SourceConsistency.md) | Serialized adapters, external changes, validation and reentrancy |
| [Branches](Branches.md) | Immutable branches, merge previews, history and source-save normalization |
| [Affine editing](AffineEditing.md) | Transform support, occurrence selection and exact hatch translation |
| [Custom headers](CustomHeaders.md) | Detached header capture and source-backed edits |
| [Block authoring](BlockAuthoring.md) | Definitions, libraries, attributes, insertion and synchronization |
| [CAD authoring](CadAuthoring.md) | Reusable block and hatch construction APIs |
| [Layer states](LayerStates.md) | Saved states, selective restore and LAS transfer |
| [Health and workflows](DrawingHealthAndWorkflows.md) | Diagnostics, edit previews, atomic application and isolated replay |

## Geometry and presentation

| Guide | Use it for |
| --- | --- |
| [Hatch islands](HatchIslands.md) | Fill selection, numerical limits and source editing |
| [Complex linetypes](Linetypes.md) | Source preservation, resource providers and bounded rendering |
| [Linetype placement and scale](LinetypePlacement.md) | Continuity, fitting, glyph frames, spatial transport and annotation scale |
| [Paragraphs and colors](Paragraphs.md) | MTEXT composition, spacing, indexed colors and true color |
| [SHX typography](Shx.md) | Explicit font resources, glyph execution and vertical flow |
| [BIGFONT](BigFont.md) | Companion fonts, explicit Unicode maps and Studio map import |
| [Paper viewports](PaperViewports.md) | Orthographic composition, picking, budgets and layer overrides |
| [Perspective](Perspective.md) | Opt-in cameras, homogeneous clipping and work-plane editing |
| [Dimension picture export](DimensionPictureExport.md) | Native dimension pictures and regeneration limits |
| [DWG exchange](../src/netDxf.Shadow.Exchange.Dwg/README.md) | Conversion profiles, diagnostics and hatch semantic transfer |

## Studio, analysis and integration

| Guide | Use it for |
| --- | --- |
| [CAD workspace](CadWorkspace.md) | Drawing commands, drafting aids, selection and input transactions |
| [Workspace layout](WorkspaceLayout.md) | Palettes, ribbon, keyboard navigation and saved UI state |
| [Graph analysis](GraphAnalysis.md) | Geometric topology, source references and discipline connectivity |
| [Discipline modules](DisciplineModules.md) | Symbols, P&ID, piping, electrical data and schedules |
| [Process diagram](ProcessDiagram.md) | Tank-and-pump sample basis and verification boundaries |
| [Reusable projects](ReusableProjects.md) | Shared libraries, host boundaries and composition examples |
| [Coding agents](coding-agents.md) | Provider sessions, workspaces, native companions and recovery |
| [Browser MCP](browser-mcp.md) | Site tools, MessagePort transport and permissions |

Use the [CI/release guide](../../doc/CI-RELEASE.md) for current verification and
package qualification. Generated artifacts belong to their run; historical test
totals are not a feature-completeness measure.
