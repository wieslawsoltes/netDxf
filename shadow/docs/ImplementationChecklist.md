# Shadow Studio implementation milestones

This checklist records the requested scope without credentials. No API keys from
conversation messages are used, saved or committed. Paid evaluation remains off;
opt-in evaluation uses environment credentials and a shared budget no greater than
USD 20. Granular commits and a pull request were subsequently requested explicitly.

The continuing full-scope audit is [ParityAudit.md](ParityAudit.md). Checked
milestones below describe delivered increments; that audit tracks remaining
requirements and must be satisfied before claiming full requested parity.

## Phase 1: requested Studio and agent improvements

- [x] MCP batch authoring, collection edits, schema/method discovery and atomic operations
- [x] Graph queries, algorithms, pagination and Studio analysis panel
- [x] Adaptive ribbon and independent command/log visibility
- [x] INSERT units, scaling, base points, attributes and safe one-level EXPLODE
- [x] MOVE displacement and Ortho/polar/tracking/snap overrides
- [x] Provider caching, bounded tool batches, usage metrics and chat activity
- [x] Reusable sample factories, Core, agent integration, HTTP/browser hosting and shared UI libraries
- [x] Default storage-tank/pump P&ID, references, label checks and round trip
- [x] Codex app-server client and managed login protocol with local fixtures
- [x] Stable ACP client with capabilities, sessions, permissions and optional services
- [x] External-agent host/UI integration validation (65 authenticated HTTP fixture checks; no live login)
- [x] Full supported layers, linetype and text-style managers (1191 native checks and live save/reopen/undo)
- [x] Reusable Markdown renderer and integrated visual checks (including conversation import and task-list styling)
- [x] Final native/browser publish, regression and package checks (24 packages; isolated installed-package consumer on .NET 10; 318 browser tests)

## Phase 2: approved after Phase 1

- [x] Reusable discipline module architecture with independent enable/disable
- [x] P&ID module and connectivity/tag/flow/instrument validation
- [x] Electrical module
- [x] Piping module
- [x] Shared searchable/versioned symbol libraries with attributes, units and standards references
- [x] Drawing-health checks for duplicates, gaps, invalid geometry, unused resources and missing SHX resources when an inventory is supplied (system TTF availability remains unknown)
- [x] Agent edit previews with one undoable batch application
- [x] Chat selection references: attach exact objects as chips with stable IDs, types, handles, document/workspace/revision identity and stale-reference handling
- [x] Recorded CAD workflow tests: draw/edit/undo/save/reload, geometry and render comparisons
- [x] Reproducible agent-efficiency comparison framework with identical frozen drawing tasks, native grading and cost/usage receipts; offline fixtures validated, live provider comparisons not run

## Initial upstream integration and validation

- [x] Fetch and integrate `origin/netstandard` at `43cb3911f25bb49184926b712e5de9f50fd403b9`, preserving local changes and resolving conflicts across relocated libraries and shared UI.
- [x] Preserve upstream Normal/Plan/Goal execution modes, explicit finish receipts, and output-limit continuation together with the local provider, context, caching and modular Studio improvements.
- [x] Native and standalone WebAssembly publishes; all 87 native CAD routes have browser transports and 960 published browser assets match their source.
- [x] Browser regression suite: 354 passing checks; Advanced verification: 1,216; Agent Studio: 97; HTTP: 30; drawing references: 32.
- [x] Additional focused provider, MCP, graph, module, domain, sample, Codex, ACP and evaluation verification suites.
- [x] Live UI checks: five-step workflow replay, before/after edit preview with undo/redo, exact health-finding selection, retained reference chips, module controls and evaluation-report import.
- [x] Standalone browser P&ID analysis: 26 domain records, zero findings from the enabled rules.

The package catalog now contains 33 packages (core plus 32 Shadow libraries).
Development package verification installs them into a fresh consumer/feed/cache and
exercises 14 integration scenarios. The initial development checks used .NET 10
with major roll-forward. The subsequent review uses an isolated official .NET 8
SDK and runtime, without changing the machine SDK. Final package qualification
requires the unchanged clean-checkout pack and installed-consumer gates; the full
platform release matrix remains separate. This work does not publish packages.

## Review corrections

- [x] Preserve nested INSERT dimensions across owner-unit changes during EXPLODE, including unitless boundaries and undo/redo.
- [x] Keep planned and native symbol styles consistent; validate selected discipline objects against their connected context and normalize reducer sizes numerically.
- [x] Guard preview selection order and own deferred property JSON; compare workflow values without large-number precision loss or JSON-escape differences; honor cancellation after baseline initialization.
- [x] Keep Codex protocol reads responsive during callbacks, normalize JSON request IDs, and reject cancelled ACP interactions.
- [x] Bound reasoning buffers, redact external event identifiers and enforce conservative, durable evaluation reservations.
- [x] Fence obsolete module/preview/chat responses and preserve external-agent turns during ordinary same-drawing edits.
- [x] Validate packaged web assets, Razor mounts/imports and notices, and propagate grouped Windows CI command failures.

Review validation includes 366 browser checks, 100 CI tooling checks, 1,221 Advanced
checks, 291 STYLE conformance cases, and an independent 72-DXF STYLE audit, plus the
focused native, provider, protocol, module and domain verification programs. Live
provider requests are excluded from all of these checks.

Live provider requests and interactive provider account login are untested. Provider,
Codex and ACP protocols are covered by local fixtures; imported synthetic evaluation
reports are labeled as such. Actual paid testing spend is USD 0.

Checked items identify implemented scope, not complete AutoCAD product parity.
The discipline catalogs are extensible starter catalogs with original artwork and
standards references, not certified reproductions of the licensed standards.
See [discipline module scope](DisciplineModules.md),
[health, preview and workflow coverage](DrawingHealthAndWorkflows.md),
[reusable projects](ReusableProjects.md), and
[agent integration](coding-agents.md) for supported operations and explicit limits.
