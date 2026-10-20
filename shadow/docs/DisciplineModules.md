# Symbols and discipline modules

The reusable domain libraries extend native DXF blocks, attributes, lines and XDATA. They have no Studio, web-host, agent or UI dependency.

| Library | Responsibility |
| --- | --- |
| `netDxf.Shadow.Symbols` | Immutable versioned catalogs, native block authoring, pure undoable placement/data/route/connection plans, persistent metadata and bounded explicit-port analysis |
| `netDxf.Shadow.Disciplines` | Common `ICadModule` adapter, discovery mapping, command dispatch and a bounded validation issue sink |
| `netDxf.Shadow.Pid` | Five original equipment/valve/PCE symbols, process pipes, flow direction and PCE data checks |
| `netDxf.Shadow.Piping` | Elbow, tee, reducer and flange symbols; nominal-size and specification checks |
| `netDxf.Shadow.Electrical` | Terminal, switch, resistor, motor and earth symbols; wire circuit and conductor data checks |

`Symbols` references the native Shadow adapter, Drafting and Graph. The Graph dependency supplies geometry-independent SCC/low-link kernels shared with source-reference analysis. `Disciplines` references Symbols and Modules. Each discipline references Disciplines. Samples references Pid to seed its tank-and-pump demonstration with the same data contract.

## Authoring

`SymbolCatalog.Search(query, offset, limit)` performs bounded text search. `Resolve(id, version)` resolves the exact numeric major.minor.patch version; omitted versions select the latest known version. A requested missing version never silently falls back. Definitions include units, insertion base point, standards-reference strings, native attributes, original primitive geometry and named typed ports.

Applications owning a mutable `DxfDocument` can call `SymbolNative.Place`. Editor applications call `SymbolAuthoring.Place` or `ICadModule.Plan`; planning returns an immutable `DraftingPlan` without mutating native objects. Executing the plan adds one undo entry including new block definitions, attributes, metadata and geometry.

```csharp
var module = new netDxf.Shadow.Pid.PidModule();
var plan = module.Plan(session.Current, new CadModuleCommandRequest(
    "place", Points: [new Vec3(50, 25)], SymbolId: "pid.pump.liquid",
    SymbolVersion: "1.1.0",
    Properties: ImmutableDictionary<string, string>.Empty.Add("tag", "P-101")));
plan.Execute(editor);
var report = module.Analyze(session.Current);
```

Every discipline advertises `place`, `route`, `update` and `connect` commands with its own field definitions. Place requires one point and a catalog symbol. Route accepts 2–64 points through the module registry and produces separate tagged LINE segments with explicit reciprocal intermediate-port references. Update changes selected metadata and matching native attributes together; blank form fields preserve existing values. Connect takes exactly two selected domain objects and declared port IDs. Physical connections require compatible port kinds; semantic associations may link unlike kinds. Connections are explicit: crossing geometry never creates a relation.

Native `Insert.CreateWithAttributes` adopts detached attributes and rejects null, already-owned or duplicate-tag entries before changing ownership. The Shadow adapter uses this public factory for explicit attributed INSERT plans, preserving the requested attribute identities and geometry.

## Persistence and analysis

`DomainMetadata` stores bounded ASCII-escaped JSON in ordinary `SHADOW_CAD` XDATA with the `netdxf.domain.v1` envelope. Unicode values survive text and binary DXF. Stable component keys and target-key/port references do not depend on DXF handles or session object IDs. Domain records contain module/class, tag, exact symbol version, property data, local ports and explicit references. Block records also retain units and standards-reference descriptions; block origin retains the base point. Metadata has a 12,000-byte payload limit, 64 properties/ports/references, bounded identifiers, finite coordinate limits and an explicit role allowlist. Invalid imported data produces a diagnostic rather than executable behavior.

INSERT ports follow base point, units, scale, rotation and normal transforms. LINE endpoints follow current geometry; named taps follow their stored relative position on the current segment. Only tagged layout roots enter domain analysis. Block symbol strokes, pipe-arrow strokes, legends and ordinary geometric crossings never become network segments.

Shared checks cover missing/duplicate tags, ambiguous copied identities, attribute/tag differences, unresolved or nonreciprocal references, unknown ports, incompatible port kinds, geometric gaps, nominal-size changes and unassigned ports. `openPorts` declares intentional open boundary ports as a comma-separated list. P&ID adds required process flow, pump/check-valve flow conflicts and PCE function/loop data. Piping checks reducer transitions, specification continuity and route consistency. Electrical checks circuit continuity, positive conductor area and optional positive voltage. These are schematic data validators, not hydraulic, electrical protection or process-safety calculations.

Analysis accepts object and issue limits, selection scope, connection tolerance and cancellation. Reports expose `Complete`, `ObjectsVisited` and `Limitation`; reaching either a shared or discipline issue limit marks the result incomplete. Selection scopes the returned domain objects while reference checks can inspect other scanned layout objects.

`DomainConnectivity.Build` projects declared ports into an immutable network with
weak components, directional minimum-link paths and bounded traces. Internal
`DomainPassage` records explicitly connect two ports with `both`, `forward` or
`reverse` direction; external connection references join compatible endpoints.
Associations remain findings rather than physical edges. A trace includes
predecessor edges for inspecting why each port was reached.

Starter symbol version `1.1.0` adds these passage declarations. Exact `1.0.0`
definitions remain available with their original unspecified internal
connectivity. Missing passage data in older drawings is never inferred from
geometry or silently migrated. An explicit empty passage list means the ports
are separate; absent declarations on a multi-port object report incomplete
interpretation. Newly authored routes and the pump/tank sample declare their
passages.

The [discipline connectivity UI and API](GraphAnalysis.md#discipline-connectivity)
share this library in native Studio, standalone WebAssembly and MCP. Reports
distinguish scan completeness, declaration completeness, truncated findings and
unloaded pages. Source selection is explicit; analysis does not edit the drawing.

## Drawing conventions

The supplied symbols are original schematic artwork and a small reusable starter catalog. P&ID convention references point to [ISO 10628-2](https://www.iso.org/standard/51841.html) and [IEC 62424](https://webstore.iec.ch/en/publication/25442). The electrical catalog identifies [IEC 60617](https://webstore.iec.ch/en/publication/2723) as its reference family; IEC's licensed database remains the authoritative symbol source. The supplied catalogs do not reproduce that database or assert full standards certification. Applications can provide reviewed, versioned catalogs through the same contracts.

## Verification

```sh
dotnet run --project shadow/tests/netDxf.Shadow.Disciplines.Verification
dotnet run --project shadow/tests/netDxf.Shadow.Samples.Verification
```

The fixtures exercise native and planned placement, new blocks, attributes, Unicode, base points and unit transforms, undo/redo, text/binary DXF persistence, direct JSON discovery, pure route planning, reference continuity, malformed-data bounds, cancellation and partial reports, plus actual process-flow, reducer and circuit failures. Connectivity fixtures cover components, directed paths, predecessor proofs, legacy declarations, ambiguous keys, scan/search limits and metadata updates. Sample checks verify the explicit process network separately from the visible geometry and preserve the gallery layouts.
