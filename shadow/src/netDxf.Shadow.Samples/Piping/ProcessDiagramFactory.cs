using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Units;

namespace netDxf.Shadow.Samples;

/// <summary>
/// Original, editable native-DXF P&amp;ID demonstration. Symbol conventions reference
/// EN ISO 10628-2 and PCE-request representation references EN 62424. The sample is
/// a drawing demonstration, not a certified plant design or a CAEX implementation.
/// </summary>
public static class ProcessDiagramFactory
{
    public const string DrawingName = "Tank and pump P&ID.dxf";

    public static DxfDocument Create()
    {
        var document = new DxfDocument();
        var processLines = new List<Line>(); var associationLines = new List<Line>();
        document.DrawingVariables.InsUnits = DrawingUnits.Millimeters;
        var lettering = new TextStyle("PID_Lettering", "Arial", FontStyle.Regular);
        var pipe = Layer("Process piping", 80, 195, 222, Lineweight.W35);
        var equipment = Layer("Equipment", 220, 229, 239, Lineweight.W25);
        var annotation = Layer("Annotations", 220, 229, 239, Lineweight.W18);
        var instrument = Layer("Instrumentation", 227, 199, 126, Lineweight.W18);
        var signal = Layer("PCE associations", 227, 199, 126, Lineweight.W18);
        var sheet = Layer("Drawing frame", 130, 151, 173, Lineweight.W13);
        var actionLine = new Linetype("PCE_DASHED", [new LinetypeSimpleSegment(4), new LinetypeSimpleSegment(-2)], "Functional association / signal line");
        signal.Linetype = actionLine;
        foreach (var layer in new[] { pipe, equipment, annotation, instrument, signal, sheet }) document.Layers.Add(layer);

        var tank = Symbol("PID_StorageTank_ConicalRoof", "Tank with conical roof and flat bottom", "EN ISO 10628-2 / vessel and tank", 0, 30);
        // The shell has explicit nozzle gaps: inlet at (-25,55), outlet at (25,10), drain at (0,0).
        Stroke(tank, -25, 0, -25, 52); Stroke(tank, -25, 58, -25, 65);
        Stroke(tank, -25, 65, -3, 76); Stroke(tank, 3, 76, 25, 65);
        Stroke(tank, 25, 65, 25, 13); Stroke(tank, 25, 7, 25, 0);
        Stroke(tank, 25, 0, 3, 0); Stroke(tank, -3, 0, -25, 0);
        // Short nozzles make piping terminate at equipment, not through its body.
        Stroke(tank, -30, 55, -25, 55); Stroke(tank, 25, 10, 30, 10);
        Stroke(tank, 0, 76, 0, 82); Stroke(tank, 0, 0, 0, -5);

        var pump = Symbol("PID_LiquidPump", "Liquid transfer pump", "EN ISO 10628-2 / liquid pumps", 0, -20);
        pump.Entities.Add(Ink(new Circle(Vector2.Zero, 11)));
        // The direction triangle is part of the liquid-pump symbol, separate from pipe flow arrows.
        Stroke(pump, -5, -6, 7, 0); Stroke(pump, 7, 0, -5, 6); Stroke(pump, -5, 6, -5, -6);
        Stroke(pump, -16, 0, -11, 0); Stroke(pump, 11, 0, 16, 0);

        var valve = Symbol("PID_ManualValve", "Manual isolation valve", "EN ISO 10628-2 / valves", 0, -13);
        Stroke(valve, -6, -4, -6, 4); Stroke(valve, -6, 4, 6, -4);
        Stroke(valve, 6, -4, 6, 4); Stroke(valve, 6, 4, -6, -4);
        Stroke(valve, 0, 0, 0, 8); Stroke(valve, -3, 8, 3, 8);

        var checkValve = Symbol("PID_CheckValve", "Check valve; flow left to right", "EN ISO 10628-2 / check valves", 0, -13);
        Stroke(checkValve, -6, -4, -6, 4); Stroke(checkValve, -6, 4, 3, 0);
        Stroke(checkValve, 3, 0, -6, -4); Stroke(checkValve, 3, -5, 3, 5); Stroke(checkValve, 3, 0, 6, 0);

        var pce = new Block("PID_PCE_Request");
        pce.Entities.Add(Ink(new Ellipse(Vector2.Zero, 22, 15)));
        Attribute(pce, "FUNCTION", "PI", 0, 2.5, 3.3);
        Attribute(pce, "LOOP", "102", 0, -3, 3.1);
        Attribute(pce, "TAG", "PI-102", 0, 0, 2, hidden: true);
        Attribute(pce, "DESCRIPTION", "Process control engineering request", 0, 0, 2, hidden: true);
        Attribute(pce, "BASIS", "EN 62424:2016 / PCE request", 0, 0, 2, hidden: true);

        Place(tank, 80, 110, "T-101", equipment);
        Place(pump, 185, 120, "P-101", equipment);
        Place(valve, 35, 165, "V-101", equipment);
        Place(valve, 133, 120, "V-102", equipment);
        Place(checkValve, 237, 120, "NRV-101", equipment);
        Place(valve, 275, 120, "V-103", equipment);
        // A horizontal drain valve keeps the tag upright and makes the drain branch easy to follow.
        Place(valve, 105, 78, "V-104", equipment);

        Pipe(20, 165, 29, 165); Pipe(41, 165, 50, 165);
        Pipe(110, 120, 127, 120); Pipe(139, 120, 169, 120);
        Pipe(201, 120, 231, 120); Pipe(243, 120, 269, 120); Pipe(281, 120, 389, 120);
        Pipe(80, 192, 80, 207); Pipe(80, 207, 91, 207);
        Pipe(80, 105, 80, 78); Pipe(80, 78, 99, 78); Pipe(111, 78, 135, 78);
        Arrow(22, 165); Arrow(157, 120); Arrow(351, 120); Arrow(132, 78);
        // Open-ended boundary connections are labeled; no implied return loop is drawn.
        Label("FROM SUPPLY", 20, 177, 3.2); Label("TO PROCESS", 348, 135, 3.2);
        Label("VENT TO ATMOSPHERE", 96, 205, 3); Label("TO DRAIN", 117, 86, 3);
        Label("DN 50 / P-101-SUCTION", 115, 139, 3.1);
        Label("DN 40 / P-101-DISCHARGE", 245, 139, 3.1);
        Label("STORAGE TANK", 80, 131, 3.4, centered: true);
        Label("TRANSFER PUMP", 185, 91, 3.4, centered: true);

        Request("LI", "101", 129, 173, "Tank level indication");
        Request("PI", "102", 216, 173, "Pump discharge pressure indication");
        Request("FI", "103", 318, 173, "Discharge flow indication");
        Association(105, 151, 129, 151); Association(129, 151, 129, 165.5);
        Association(216, 120, 216, 165.5); Association(318, 120, 318, 165.5);
        Tap(105, 151); Tap(216, 120); Tap(318, 120);
        Label("Level", 129, 187, 3, centered: true); Label("Pressure", 216, 187, 3, centered: true); Label("Flow", 318, 187, 3, centered: true);

        // Original drawing frame and an explicit legend keep symbol, line and tag meaning inspectable.
        Rectangle(10, 10, 410, 242, sheet);
        Segment(10, 219, 410, 219, sheet); Segment(10, 55, 410, 55, sheet);
        Label("PIPING & INSTRUMENTATION DIAGRAM", 19, 228, 5.4);
        Label("Storage tank and liquid transfer pump", 232, 229, 3.5);
        Label("SYMBOL / LINE LEGEND", 20, 45, 3.2);
        Segment(20, 35, 41, 35, pipe); Label("Process pipe / flow direction", 47, 33.7, 2.9); Arrow(39, 35);
        Segment(20, 25, 41, 25, signal); Label("PCE functional association", 47, 23.7, 2.9);
        Label("LI / PI / FI: level / pressure / flow indication", 20, 15, 2.8);
        Segment(188, 10, 188, 55, sheet); Segment(310, 10, 310, 55, sheet);
        Label("DRAWING BASIS", 196, 45, 3.2);
        Label("Equipment: EN ISO 10628-2", 196, 35, 2.9);
        Label("PCE requests: EN 62424", 196, 25, 2.9);
        Label("Demonstration / not for construction", 196, 15, 2.8);
        Label("SHADOW / PID-001", 320, 43, 4);
        Label("REV 0     SHEET 1 / 1", 320, 30, 3);
        Label("SCHEMATIC / NOT TO SCALE", 320, 17, 2.9);
        ProcessDiagramMetadata.Attach(document, processLines, associationLines);
        document.Entities.ActiveLayout = "Model";
        return document;

        Layer Layer(string name, byte red, byte green, byte blue, Lineweight weight) => new(name) { Color = new AciColor(red, green, blue), Lineweight = weight };
        T Ink<T>(T entity) where T : EntityObject { entity.Color = AciColor.ByBlock; entity.Lineweight = Lineweight.ByBlock; return entity; }
        void Stroke(Block block, double ax, double ay, double bx, double by) => block.Entities.Add(Ink(new Line(new Vector2(ax, ay), new Vector2(bx, by))));
        void Attribute(Block block, string name, string value, double x, double y, double height, bool hidden = false)
            => block.AttributeDefinitions.Add(new AttributeDefinition(name) { Value = value, Position = new(x, y, 0), Height = height,
                Alignment = TextAlignment.MiddleCenter, Style = lettering, Color = AciColor.ByBlock, Flags = hidden ? AttributeFlags.Hidden : AttributeFlags.None });
        Block Symbol(string name, string description, string basis, double tagX, double tagY)
        {
            var block = new Block(name); Attribute(block, "TAG", "TAG", tagX, tagY, 3.6);
            Attribute(block, "DESCRIPTION", description, 0, 0, 2, true); Attribute(block, "BASIS", basis, 0, 0, 2, true); return block;
        }
        Insert Place(Block block, double x, double y, string tag, Layer layer)
        {
            var insert = new Insert(block, new Vector2(x, y)) { Layer = layer };
            insert.Attributes.AttributeWithTag("TAG").Value = tag; document.Entities.Add(insert); return insert;
        }
        void Request(string function, string loop, double x, double y, string description)
        {
            var insert = Place(pce, x, y, function + "-" + loop, instrument);
            insert.Attributes.AttributeWithTag("FUNCTION").Value = function; insert.Attributes.AttributeWithTag("LOOP").Value = loop;
            insert.Attributes.AttributeWithTag("DESCRIPTION").Value = description;
        }
        void Segment(double ax, double ay, double bx, double by, Layer layer) => document.Entities.Add(new Line(new Vector2(ax, ay), new Vector2(bx, by)) { Layer = layer });
        void Pipe(double ax, double ay, double bx, double by)
        { var line = new Line(new Vector2(ax, ay), new Vector2(bx, by)) { Layer = pipe }; document.Entities.Add(line); processLines.Add(line); }
        void Association(double ax, double ay, double bx, double by)
        { var line = new Line(new Vector2(ax, ay), new Vector2(bx, by)) { Layer = signal }; document.Entities.Add(line); associationLines.Add(line); }
        void Tap(double x, double y) => document.Entities.Add(new Circle(new Vector2(x, y), .65) { Layer = instrument });
        void Arrow(double x, double y) { Segment(x - 3, y + 1.6, x, y, pipe); Segment(x, y, x - 3, y - 1.6, pipe); }
        void Label(string value, double x, double y, double height, bool centered = false)
            => document.Entities.Add(new Text(value, new Vector2(x, y), height) { Layer = annotation, Style = lettering, Alignment = centered ? TextAlignment.MiddleCenter : TextAlignment.BaselineLeft });
        void Rectangle(double x1, double y1, double x2, double y2, Layer layer)
        { Segment(x1, y1, x2, y1, layer); Segment(x2, y1, x2, y2, layer); Segment(x2, y2, x1, y2, layer); Segment(x1, y2, x1, y1, layer); }
    }
}
