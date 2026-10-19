# CAD discipline modules

This host-independent library describes optional CAD toolsets. `ICadModule`
provides command metadata, a searchable versioned symbol catalog, bounded analysis,
and pure edit planning. An application registers its chosen modules with
`CadModuleRegistry`; the registry validates command arguments and enforces whether
a module is enabled. Disabling a module removes its commands, symbol catalog and
analysis contributions from use without modifying a drawing. Ordinary CAD editing
still operates on the native DXF entities.

Modules return `DraftingPlan` objects. The host applies a plan through its existing
editor transaction and history, after checking document/workspace/revision identity.
Module state is application configuration and is deliberately separate from DXF
content. Hosts should synchronize enablement with execution when concurrent users
share a registry. Registered module code is trusted application code, not a sandbox
for downloaded extensions.

Commands declare bounded point acquisition, selection requirements and property
fields. Catalog entries expose units, base points, attributes, ports and standards
references; a reference identifies the design basis and does not certify compliance.
Analysis reports include the document identity, deterministic issues and whether
the requested scan completed within its budgets.
