using System.Collections.Immutable;
using System.Globalization;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;

internal static class CustomHeaderVerification
{
    public static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action run) => tests.Add(("custom-headers/" + name, run));
        Test("method-backed values are detached and no-op refresh shares identity", () =>
        {
            var doc = new DxfDocument(); var header = new HeaderVariable("$MSLTSCALE", 70, (short)1);
            doc.DrawingVariables.AddCustomVariable(header); var session = new ShadowSession(doc); var old = session.Current;
            Check(Find(old, "$msltscale")["Value"].Number() == 1); Check(ReferenceEquals(old, session.Refresh()));
            session.MutateSource(_ => header.Value = (short)0);
            Check(Find(old, "$MSLTSCALE")["Value"].Number() == 1 && Find(session.Current, "$MSLTSCALE")["Value"].Number() == 0);
        });
        foreach (var pair in new (short Code, ShadowValue Value, Type Type)[]
        {
            (70, Atom("System.Int16", "123"), typeof(short)), (90, Atom("System.Int32", "123456"), typeof(int)),
            (160, Atom("System.Int64", "9007199254740993"), typeof(long)), (40, ShadowValue.From(1.25), typeof(double)),
            (1, ShadowValue.From("Zażółć Ω"), typeof(string)), (290, ShadowValue.From(true), typeof(bool)),
            (20, ShadowValue.From(new Vec3(2,3), true), typeof(Vector2)), (30, ShadowValue.From(new Vec3(2,3,4)), typeof(Vector3)),
            (310, new ShadowValue.Sequence([Atom("System.Byte", "0"), Atom("System.Byte", "255")]), typeof(byte[]))
        }) Test("create and undo typed value " + pair.Type.Name, () =>
        {
            var doc = new DxfDocument(); var session = new ShadowSession(doc); var editor = new EditorSession(session);
            var before = session.Current;
            editor.Execute("Set header", CustomHeaderEdits.Set(before, "$SHADOW_CUSTOM", pair.Code, pair.Value));
            Check(doc.DrawingVariables.TryGetCustomVariable("$shadow_custom", out var value) && value.Value.GetType() == pair.Type);
            var captured = Find(session.Current, "$SHADOW_CUSTOM")["Value"];
            if (pair.Value is ShadowValue.Structure vector)
                foreach (var field in vector.Fields) Check(ShadowValue.Equivalent(captured[field.Key].Value, field.Value));
            else Check(ShadowValue.Equivalent(captured.Value, pair.Value));
            editor.Undo(); Check(!doc.DrawingVariables.ContainsCustomVariable("$SHADOW_CUSTOM"));
            editor.Redo(); Check(doc.DrawingVariables.TryGetCustomVariable("$SHADOW_CUSTOM", out value) && value.Value.GetType() == pair.Type);
            Check(!Find(before, "$SHADOW_CUSTOM").Exists);
        });
        Test("scalar boxed value remains typed across canonical undo", () =>
        {
            var doc = new DxfDocument(); doc.DrawingVariables.AddCustomVariable(new("$V", 70, (short)1));
            var session = new ShadowSession(doc); var editor = new EditorSession(session); var id = Find(session.Current, "$V").Id!.Value;
            editor.SetProperty(id, "Value", Atom("System.Int16", "2"));
            Check(doc.DrawingVariables.TryGetCustomVariable("$V", out var variable) && variable.Value is short s && s == 2);
            editor.Undo(); Check((short)variable.Value == 1); editor.Redo(); Check((short)variable.Value == 2);
        });
        Test("case-insensitive replacement and group changes preserve membership", () =>
        {
            var doc = new DxfDocument(); doc.DrawingVariables.AddCustomVariable(new("$MYVALUE", 70, (short)1));
            var session = new ShadowSession(doc); var editor = new EditorSession(session);
            editor.Execute("Change type", CustomHeaderEdits.Set(session.Current, "$myvalue", 40, ShadowValue.From(3.5)));
            Check(doc.DrawingVariables.CustomValues().Count == 1 && Find(session.Current, "$MYVALUE")["Value"].Number() == 3.5);
            editor.Undo(); Check(Find(session.Current, "$myvalue")["GroupCode"].Number() == 70);
        });
        Test("remove prunes unreachable variable and restores historical identity", () =>
        {
            var doc = new DxfDocument(); doc.DrawingVariables.AddCustomVariable(new("$V", 1, "hello"));
            var session = new ShadowSession(doc); var editor = new EditorSession(session); var id = Find(session.Current, "$V").Id!.Value;
            editor.Execute("Remove", CustomHeaderEdits.Remove(session.Current, "$v")); Check(!session.Current.Objects.ContainsKey(id));
            editor.Undo(); Check(Find(session.Current, "$V").Id == id); editor.Redo(); Check(!session.Current.Objects.ContainsKey(id));
        });
        Test("unchanged value and absent removal do not add history", () =>
        {
            var doc = new DxfDocument(); doc.DrawingVariables.AddCustomVariable(new("$V", 70, (short)1));
            var session = new ShadowSession(doc); var editor = new EditorSession(session); var before = session.Current;
            editor.Execute("Same", CustomHeaderEdits.Set(before, "$v", 70, Atom("System.Int16", "1")));
            editor.Execute("Absent", CustomHeaderEdits.Remove(session.Current, "$absent"));
            Check(ReferenceEquals(before, session.Current) && editor.State.UndoCount == 0);
        });
        foreach (var item in new (string Name, short Code, ShadowValue Value)[]
        {
            ("$LTSCALE",40,ShadowValue.From(3)), ("$BAD",70,ShadowValue.From(2)), ("$BAD",9,ShadowValue.From("injection")),
            ("$BAD",40,Atom("System.Double","NaN")), ("$BAD",70,Atom("Arbitrary.Type","2"))
        }) Test("invalid replacement is atomic " + item.Name + "/" + item.Code + "/" + item.Value, () =>
        {
            var doc = new DxfDocument(); var session = new ShadowSession(doc); var editor = new EditorSession(session); var before = session.Current;
            Throws(() => editor.Execute("Invalid", CustomHeaderEdits.Set(before,item.Name,item.Code,item.Value)));
            Check(ReferenceEquals(session.Current,before) && doc.DrawingVariables.CustomValues().Count == 0 && editor.State.UndoCount == 0);
        });
        Test("unobserved header changes conflict with unrelated geometry edits", () =>
        {
            var doc = new DxfDocument(); var variable = new HeaderVariable("$V",70,(short)1); doc.DrawingVariables.AddCustomVariable(variable);
            var line = new Line(Vector3.Zero,Vector3.UnitX); doc.Entities.Add(line); var session = new ShadowSession(doc); var editor = new EditorSession(session);
            variable.Value=(short)2;
            Throws(() => editor.SetProperty(new("H:"+line.Handle.ToUpperInvariant()), "EndPoint",ShadowValue.From(new Vec3(5,0))));
            Check(line.EndPoint == Vector3.UnitX && Find(session.Current,"$V")["Value"].Number() == 1);
        });
        foreach(var binary in new[]{false,true}) Test("source ASCII/binary roundtrip " + binary, () =>
        {
            var doc = new DxfDocument(); var session = new ShadowSession(doc); var editor = new EditorSession(session);
            editor.Execute("Set",CustomHeaderEdits.Set(session.Current,"$SHADOW_SCALE",40,ShadowValue.From(2.75)));
            using var stream = new MemoryStream(); Check(doc.Save(stream,binary)); stream.Position=0;
            var loaded = DxfDocument.Load(stream)!; Check(Find(new ShadowSession(loaded).Current,"$SHADOW_SCALE")["Value"].Number() == 2.75);
        });
        Test("isolated branch header edit merges through the canonical adapter", () =>
        {
            var doc=new DxfDocument(); doc.DrawingVariables.AddCustomVariable(new("$V",70,(short)1)); var session=new ShadowSession(doc);
            var memory=new ShadowMemoryStore(session.Current); var branch=new EditorSession(memory);
            branch.Execute("Header",CustomHeaderEdits.Set(branch.Snapshot,"$V",70,Atom("System.Int16","3")));
            Check((short)doc.DrawingVariables.CustomValues().Single().Value == 1);
            var source=new EditorSession(session); source.Execute("Adopt",ShadowGraphPatch.Between(session.Current,branch.Snapshot));
            Check((short)doc.DrawingVariables.CustomValues().Single().Value == 3); source.Undo(); Check((short)doc.DrawingVariables.CustomValues().Single().Value == 1);
        });
    }
    private static ShadowValue.Atom Atom(string type,string text)=>new(type,text);
    private static ShadowReader Find(ShadowSnapshot snapshot,string name)=>snapshot.Document["DrawingVariables"]["CustomVariables"].Items.FirstOrDefault(v=>string.Equals(v["Name"].Text,name,StringComparison.OrdinalIgnoreCase));
    private static void Check(bool condition){if(!condition)throw new Exception("Custom header contract failed.");}
    private static void Throws(Action action){try{action();}catch(Exception e) when(e is ArgumentException or InvalidOperationException or NotSupportedException){return;} throw new Exception("Invalid input was accepted.");}
}
