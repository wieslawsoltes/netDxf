# Tank and pump P&ID sample

`netDxf.Shadow.Samples.ProcessDiagramFactory.Create()` returns a standalone, editable `DxfDocument`. Studio uses it for the default **Tank and pump P&ID.dxf** drawing and adds the existing appearance, paper-space, SHX, dimension, paragraph, linetype and hatch galleries on their own layouts. The factory has no Studio or web-host dependency.

The model sheet shows supply through an isolation valve to a conical-roof, flat-bottom storage tank, then suction isolation, a liquid transfer pump, a check valve, discharge isolation and an outgoing process connection. The tank has separate atmospheric vent and valved drain connections. Direction arrows, nominal pipe sizes, equipment tags, a legend and the drawing title block make the intent explicit.

The tank, pump, manual valves, check valve and PCE request are native reusable `BLOCK` definitions placed with `INSERT`. Equipment tags are editable attributes. Hidden `DESCRIPTION` and `BASIS` attributes record the symbol's purpose without adding text to the sheet. PCE requests have separate editable function and loop-number attributes: LI-101 for level indication, PI-102 for pressure indication and FI-103 for flow indication.

## Drawing basis

Equipment symbol conventions reference **EN ISO 10628-2:2012**, which covers graphical symbols for chemical and petrochemical diagrams and uses the ISO 14617 symbol family. The model uses original DXF geometry for the conical-roof tank, general liquid pump, manual isolation valves and non-return valve. It does not imply a particular pump impeller design. See the [ISO 10628-2 catalogue and scope](https://www.iso.org/standard/51841.html).

The functional ovals and their dashed associations represent **PCE requests**, using **EN 62424:2016** as the representation basis. They describe requested measurement/indication functions; they do not assert a specific transmitter, control-system implementation or physical signal medium. IEC 62424 also defines data exchange between P&ID and PCE tools; this drawing factory does not implement that CAEX exchange. See the [IEC 62424:2016 scope](https://webstore.iec.ch/en/publication/25442) and the [EN adoption preview](https://preview.sist.si/sist-preview/25108/6dfc04d32f8842989cd1e949d8a1d905/SIST-EN-62424-2017.pdf).

This is a software demonstration, with standards references checked against publicly available scopes and previews. It is not a claim of independently verified conformity with every clause of the full standards, a process-safety design, or a construction drawing. No safety, GMP or quality-critical designation is assigned to the PCE requests.

## Layers and geometry

| Layer | Content |
| --- | --- |
| Process piping | Continuous process pipe and direction arrows |
| Equipment | Tank, pump and valve inserts |
| Instrumentation | PCE request inserts and association taps |
| PCE associations | Actual DXF dashed linetype for functional associations |
| Annotations | Pipe labels, descriptions, legend and title |
| Drawing frame | Border and title-block divisions |

Pipe segments stop at equipment and valve connection points; no pipe is drawn through the pump or a valve body. The tank shell has visible nozzle openings. The standard Studio graph analysis remains **geometric topology**, including symbol outlines and sheet geometry. The P&ID module separately reads an explicit persistent network: ten component inserts, twelve process-pipe segments and four signal segments, with named ports and reciprocal references. Legend lines, arrows and block outlines carry no network metadata. Intentional supply/process/vent/drain boundaries are declared open ports. This schematic network is not a hydraulic or process-safety calculation. See [DisciplineModules.md](DisciplineModules.md).

Text uses a native TrueType `PID_Lettering` style with Arial as its family; host font substitution may affect lettering metrics. The standalone verification renderer checks the resulting label bounds on the current host.

## Verification and preview

```sh
dotnet run --project shadow/tests/netDxf.Shadow.Samples.Verification
dotnet run --project shadow/tests/netDxf.Shadow.Samples.Verification -- --render /tmp/shadow-pid.png
```

The verification checks native block/tag identity, functional ovals, hidden metadata, dashed-versus-continuous line semantics, pipe gaps at inline components, drawing bounds and label collisions, then repeats these checks after a DXF export/import round trip. It also verifies that Studio still includes the gallery layouts. The optional render writes a PNG through the same Skia renderer used by the native Studio.
