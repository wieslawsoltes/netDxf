using System.Collections.Immutable;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Symbols;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Editor;
using netDxf.Units;

var checks = 0;
var definition = new SymbolDefinition("test.valve", "1.0.0", "Test valve", "Original verification symbol", "valve", "Millimeters", new(2,0), ["Test convention"],
    [new("TAG", "Tag", Required:true, Visible:true, Position:new(0,-8))],
    [new("in", "process", new(-5,0), new(-1,0)), new("out", "process", new(5,0), new(1,0))],
    [new(SymbolStrokeKind.Circle, Vec3.Zero, Radius:5)]);
var catalog = new SymbolCatalog([definition, definition with { Version = "2.0.0" }]);
Check(catalog.Resolve("test.valve").Version == "2.0.0" && catalog.Resolve("test.valve", "1.0.0").Version == "1.0.0", "Version resolution.");
Check(catalog.Search("VALVE", 1, 1).Items.Length == 1, "Catalog bounded search.");
Throws<ArgumentException>(() => catalog.Resolve("test.valve", "3.0.0"), "No version substitution.");
var document = new DxfDocument(); document.DrawingVariables.InsUnits = DrawingUnits.Millimeters;
var native = SymbolNative.Place(document, definition, new(20,30), "pid", "V-101", new Dictionary<string,string>{{"service","Woda łódź"}}, rotation:90, scale:2);
var original = DomainMetadata.Read(native)!;
Check(original.Tag == "V-101" && original.Properties!["service"] == "Woda łódź", "Native symbol metadata Unicode.");
var session = new ShadowSession(document); var editor = new EditorSession(session);
var first = session.Current.DrawingEntities().Single();
Check(DomainMetadata.Read(first)!.Key == original.Key, "Shadow reads native XDATA.");
var port = DomainMetadata.WorldPorts(session.Current, first, original)[0];
Check(Vec3.Distance(port.Position, new(20,16)) < 1e-8, "Rotated scaled basepoint-aware ports.");
var plan = SymbolAuthoring.Place(session.Current, definition, new(50,0), "pid", "V-102");
plan.Execute(editor);
Check(document.Entities.Inserts.Count() == 2, "Undoable INSERT native creation.");
Check(DomainMetadata.Read(document.Entities.Inserts.Last())!.Tag == "V-102", "Planned XDATA materializes.");
editor.Undo(); Check(document.Entities.Inserts.Count() == 1, "Undo restores native symbols.");
editor.Redo(); Check(document.Entities.Inserts.Count() == 2, "Redo restores native symbols.");
var secondId = session.Current.DrawingEntities().Last().Id!.Value;
var secondData = DomainMetadata.Read(session.Current.Read(secondId))!;
SymbolAuthoring.Attach(session.Current, secondId, secondData with { References = [new("in", original.Key, "out")] }).Execute(editor);
using var stream = new MemoryStream();
Check(document.Save(stream), "Native DXF save."); stream.Position = 0;
var reloaded = DxfDocument.Load(stream)!;
Check(reloaded.Entities.Inserts.Count() == 2, "Native DXF reload symbols.");
var loaded = reloaded.Entities.Inserts.Select(i => DomainMetadata.Read(i)!).ToArray();
Check(loaded.Any(d => d.Key == original.Key && d.Properties!["service"] == "Woda łódź"), "Persistent identity/Unicode roundtrip.");
Check(loaded.Single(d => d.Tag == "V-102").References[0].TargetKey == original.Key, "Persistent endpoint reference roundtrip.");
var empty = new DxfDocument(); empty.DrawingVariables.InsUnits = DrawingUnits.Inches;
var emptySession = new ShadowSession(empty); var emptyEditor = new EditorSession(emptySession);
SymbolAuthoring.Place(emptySession.Current, definition, Vec3.Zero, "pid", "V-200").Execute(emptyEditor);
Check(empty.Blocks.Any(b => b.Name == SymbolNative.BlockName(definition)), "New versioned block definition materializes.");
Check(DomainMetadata.Read(empty.Entities.Inserts.Single())!.Ports.Length == 2, "New block metadata/ports materialize.");
var inchEntity = emptySession.Current.DrawingEntities().Single();
var inchPorts = DomainMetadata.WorldPorts(emptySession.Current, inchEntity, DomainMetadata.Read(inchEntity)!);
Check(Vec3.Distance(inchPorts[0].Position, new(-7 / 25.4, 0)) < 1e-8, "Symbol units convert millimeters to inches.");
Check(Math.Abs(empty.Entities.Inserts.Single().Attributes.Single().Height - 2.5 / 25.4) < 1e-8, "Planned attributes share insertion unit conversion.");
Check(SymbolNative.BlockName(definition) != SymbolNative.BlockName(definition with { Id="test_valve" }), "Distinct catalog IDs retain distinct block names.");
emptyEditor.Undo();
Check(!empty.Entities.Inserts.Any() && !empty.Blocks.Any(b => b.Name == SymbolNative.BlockName(definition)), "Undo removes the newly authored symbol definition and insert.");
emptyEditor.Redo(); Check(empty.Entities.Inserts.Count() == 1, "Redo restores new block, attributed insert and registry graph.");
var detachedDefinition = new AttributeDefinition("CHECK");
var detachedAttribute = new netDxf.Entities.Attribute(detachedDefinition);
Throws<ArgumentException>(() => Insert.CreateWithAttributes(new netDxf.Blocks.Block("Rejected"), [detachedAttribute, detachedAttribute]), "Duplicate attributes rejected before ownership changes.");
Check(detachedAttribute.Owner is null, "Rejected native factory leaves detached inputs unchanged.");
checks += DisciplineChecks.Run();
checks += CadReviewChecks.Run();
Console.WriteLine($"{checks} discipline verification checks passed.");
void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
void Throws<T>(Action action, string message) where T:Exception { try { action(); } catch(T) { checks++; return; } throw new InvalidOperationException(message); }
