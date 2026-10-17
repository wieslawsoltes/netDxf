using System.Collections.Immutable;
using netDxf.Shadow;
using netDxf.Shadow.Geometry;

internal static class SceneDependencyReuseVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("scene-dependency-reuse/reader notification order matches uncached access", () =>
        {
            var snapshot = Snapshot();
            var actual = new Observer(); var expected = new Observer();
            Exercise(snapshot.Document.Observe(actual));
            ExerciseReference(new(snapshot, new ShadowValue.Reference(snapshot.Root), expected));
            Require(actual.Reads.SequenceEqual(expected.Reads), "Caching reference resolution changed dependency notifications.");
            var independent = new Observer();
            var reader = snapshot.Document.Observe(actual).Observe(independent);
            var before = actual.Reads.Count;
            Require(ReferenceEquals(reader.Object, snapshot.Objects[snapshot.Root]));
            Require(actual.Reads.Count == before && independent.Reads.Count == 1, "Observe retained the previous observer.");
        }));
        tests.Add(("scene-dependency-reuse/default and missing readers preserve query semantics", () =>
        {
            ShadowReader empty = default;
            Require(empty.Snapshot is null && empty.Value is null && empty.Id is null && empty.Object is null);
            Require(!empty.Exists && empty.Type == "" && empty.Text == "" && !empty.Items.Any());
            Require(empty.Number(7) == 7 && empty.Boolean(true) && empty.Vector(new(1, 2, 3)) == new Vec3(1, 2, 3));
            Require(empty["Any"].Snapshot is null && empty.Resolve(new("absent")).Snapshot is null);
            var snapshot = Snapshot(); var observer = new Observer();
            var reader = snapshot.Read(new("absent")).Observe(observer);
            Require(reader.Exists && reader.Id == new ObjectId("absent") && observer.Reads.Count == 0);
            Require(reader.Object is null && !reader["Any"].Exists && observer.Reads.Count == 2);
            Require(observer.Reads.All(read => read.Id == new ObjectId("absent") && read.Node is null));
            var added = Node("absent", "Layer", ShadowObjectKind.Table, ("Name", ShadowValue.From("new")));
            var next = snapshot with { Revision = 1, Objects = snapshot.Objects.Add(added.Id, added) };
            Require(reader.Object is null && ReferenceEquals(next.Read(added.Id).Object, added), "A cached reader escaped its immutable snapshot.");
        }));
        tests.Add(("scene-dependency-reuse/readset freezes preserve order updates and isolation", () =>
        {
            var reads = new ShadowReadSet(); Require(reads.Freeze().IsEmpty);
            var first = Node("first", "Line", ShadowObjectKind.Entity);
            reads.Read(first.Id, first); reads.Read(first.Id, first);
            var frozen = reads.Freeze();
            Require(frozen.Length == 1 && ReferenceEquals(frozen[0].Node, first));
            var replacement = first with { TypeName = "Circle" };
            reads.Read(new(new string(first.Id.Value.ToCharArray())), replacement);
            Require(ReferenceEquals(reads.Freeze()[0].Id.Value, first.Id.Value), "A dependency update replaced its original key instance.");
            for (var i = 0; i < 1000; i++)
            {
                var node = Node("dependency/" + i, "Layer", ShadowObjectKind.Table);
                reads.Read(node.Id, node); reads.Read(node.Id, node);
            }
            reads.Read(new("dependency/7"), null); reads.Read(first.Id, null);
            var result = reads.Freeze();
            Require(result.Length == 1001 && result[0].Id == first.Id && result[0].Node is null);
            Require(result.Skip(1).Select(read => read.Id.Value).SequenceEqual(Enumerable.Range(0, 1000).Select(i => "dependency/" + i)));
            Require(result[8].Node is null && frozen.Length == 1 && ReferenceEquals(frozen[0].Node, first));
            reads.Read(first.Id, replacement);
            Require(result[0].Node is null && ReferenceEquals(reads.Freeze()[0].Node, replacement));
        }));
        tests.Add(("scene-dependency-reuse/shared resource edits invalidate every dependent root", () =>
        {
            var snapshot = Snapshot(); var builder = new CadSceneBuilder();
            var first = builder.Build(snapshot);
            Require(first.Occurrences.Length == 2 && first.Diagnostics.IsEmpty);
            var retained = builder.Build(snapshot);
            Require(builder.LastBuildStatistics.RootsReused == 2 && ReferenceEquals(first.Occurrences[0], retained.Occurrences[0]));
            var variable = snapshot.Objects[new("variables")];
            var scaled = Replace(snapshot, variable with { Properties = variable.Properties.SetItem("LtScale", ShadowValue.From(2.5)) });
            var second = builder.Build(scaled);
            Require(builder.LastBuildStatistics.RootsProjected == 2 && builder.LastBuildStatistics.RootsReused == 0);
            Require(first.Occurrences.All(item => item.Style.Dashes.AsSpan().SequenceEqual(new double[] { 1, -1 })));
            Require(second.Occurrences.All(item => item.Style.Dashes.AsSpan().SequenceEqual(new double[] { 2.5, -2.5 })));
            var layer = scaled.Objects[new("layer")];
            var recolored = Replace(scaled, layer with { Properties = layer.Properties.SetItem("Color", Color(14, 75, 193)) });
            var third = builder.Build(recolored);
            Require(builder.LastBuildStatistics.RootsProjected == 2 && third.Occurrences.All(item => item.Style.Argb == 0xff0e4bc1));
            var unrelated = Node("unrelated", "Style", ShadowObjectKind.Table);
            var fourth = builder.Build(recolored with { Revision = recolored.Revision + 1, Objects = recolored.Objects.Add(unrelated.Id, unrelated) });
            Require(builder.LastBuildStatistics.RootsReused == 2 && ReferenceEquals(third.Occurrences[0], fourth.Occurrences[0]));
        }));
        tests.Add(("scene-dependency-reuse/local and formerly missing references invalidate only their roots", () =>
        {
            var snapshot = Snapshot(); var builder = new CadSceneBuilder();
            var first = builder.Build(snapshot);
            var entity = snapshot.Objects[new("line/0")];
            var edited = Replace(snapshot, entity with { Properties = entity.Properties.SetItem("StartPoint", ShadowValue.From(new Vec3(-5, 7))) });
            var second = builder.Build(edited);
            Require(builder.LastBuildStatistics.RootsProjected == 1 && builder.LastBuildStatistics.RootsReused == 1);
            Require(ReferenceEquals(first.Occurrences[1], second.Occurrences[1]));
            Require(((LineCurve)second.Occurrences[0].Geometry.Contours[0].Curves[0]).A == new Vec3(-5, 7));
            entity = edited.Objects[new("line/0")];
            var missing = Replace(edited, entity with { Properties = entity.Properties.SetItem("Layer", new ShadowValue.Reference(new("missing-layer"))) });
            var third = builder.Build(missing);
            Require(third.Occurrences[0].Style.Layer == "");
            var layer = missing.Objects[new("layer")] with { Id = new("missing-layer") };
            var present = missing with { Revision = missing.Revision + 1, Objects = missing.Objects.Add(layer.Id, layer) };
            var fourth = builder.Build(present);
            Require(builder.LastBuildStatistics.RootsProjected == 1 && builder.LastBuildStatistics.RootsReused == 1);
            Require(fourth.Occurrences[0].Style.Layer == "Visible" && ReferenceEquals(third.Occurrences[1], fourth.Occurrences[1]));
        }));
    }

    private static void Exercise(ShadowReader reader)
    {
        _ = reader.Object; _ = reader.Object;
        foreach (var layout in reader["Layouts"].Items)
        {
            _ = layout.Type;
            foreach (var entity in layout["AssociatedBlock"]["Entities"].Items)
            {
                _ = entity.Type; _ = entity["StartPoint"].Vector(); _ = entity["Layer"]["Name"].Text;
                _ = entity.Resolve(new("layer"))["Linetype"]["Segments"].Items.Select(item => item["Length"].Number()).ToArray();
            }
        }
        _ = reader.Resolve(new("absent")).Object; _ = reader.Resolve(new("absent"))["Any"].Text;
    }
    private static void ExerciseReference(ReferenceReader reader)
    {
        _ = reader.Object; _ = reader.Object;
        foreach (var layout in reader["Layouts"].Items)
        {
            _ = layout.Type;
            foreach (var entity in layout["AssociatedBlock"]["Entities"].Items)
            {
                _ = entity.Type; _ = entity["StartPoint"].Vector(); _ = entity["Layer"]["Name"].Text;
                _ = entity.Resolve(new("layer"))["Linetype"]["Segments"].Items.Select(item => item["Length"].Number()).ToArray();
            }
        }
        _ = reader.Resolve(new("absent")).Object; _ = reader.Resolve(new("absent"))["Any"].Text;
    }
    // Independent pre-cache reader: every Object access resolves the immutable table.
    private readonly struct ReferenceReader(ShadowSnapshot snapshot, ShadowValue value, IShadowReadObserver observer)
    {
        private ShadowValue Value => value;
        public ShadowObject? Object
        {
            get
            {
                if (value is not ShadowValue.Reference reference) return null;
                var node = snapshot.Objects.GetValueOrDefault(reference.Id); observer.Read(reference.Id, node); return node;
            }
        }
        public string Type => Object?.Type ?? (value is ShadowValue.Structure structure ? structure.TypeName[(structure.TypeName.LastIndexOf('.') + 1)..] : "");
        public string Text => value is ShadowValue.Atom atom ? atom.Text : "";
        public double Number() => double.TryParse(Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : 0;
        public Vec3 Vector() => new(this["X"].Number(), this["Y"].Number(), this["Z"].Number());
        public ReferenceReader Resolve(ObjectId id) => new(snapshot, new ShadowValue.Reference(id), observer);
        public ReferenceReader this[string key] => new(snapshot, Object is { } node ? node[key] :
            value is ShadowValue.Structure structure ? structure.Fields.GetValueOrDefault(key, ShadowValue.Null) : ShadowValue.Null, observer);
        public IEnumerable<ReferenceReader> Items
        {
            get
            {
                var source = value as ShadowValue.Sequence ?? this["$items"].Value as ShadowValue.Sequence;
                if (source is not null) foreach (var item in source.Items) yield return new(snapshot, item, observer);
            }
        }
    }
    private sealed class Observer : IShadowReadObserver
    {
        internal readonly List<ShadowDependency> Reads = new();
        public void Read(ObjectId id, ShadowObject? node) => Reads.Add(new(id, node));
    }
    private static ShadowSnapshot Snapshot()
    {
        ShadowValue.Reference Ref(string id) => new(new(id));
        ShadowValue.Sequence Sequence(params ShadowValue[] values) => new(values.ToImmutableArray());
        var objects = new[]
        {
            Node("document", "Document", ShadowObjectKind.Document, ("Layouts", Sequence(Ref("layout"))), ("DrawingVariables", Ref("variables"))),
            Node("layout", "Layout", ShadowObjectKind.Layout, ("Name", ShadowValue.From("Model")), ("AssociatedBlock", Ref("model"))),
            Node("model", "Block", ShadowObjectKind.Block, ("Entities", Sequence(Ref("line/0"), Ref("line/1")))),
            Node("variables", "DrawingVariables", ShadowObjectKind.Object, ("LtScale", ShadowValue.From(1))),
            Node("layer", "Layer", ShadowObjectKind.Table, ("Name", ShadowValue.From("Visible")), ("Color", Color(80, 90, 170)),
                ("Lineweight", ShadowValue.From("25")), ("Linetype", Ref("linetype"))),
            Node("linetype", "Linetype", ShadowObjectKind.Table, ("Name", ShadowValue.From("Dashed")), ("Segments", Sequence(
                new ShadowValue.Structure("Segment", ImmutableDictionary<string, ShadowValue>.Empty.Add("Length", ShadowValue.From(1))),
                new ShadowValue.Structure("Segment", ImmutableDictionary<string, ShadowValue>.Empty.Add("Length", ShadowValue.From(-1)))))),
            Node("bylayer", "Linetype", ShadowObjectKind.Table, ("Name", ShadowValue.From("ByLayer")))
        }.Concat(Enumerable.Range(0, 2).Select(i => Node("line/" + i, "Line", ShadowObjectKind.Entity,
            ("StartPoint", ShadowValue.From(new Vec3(0, i * 2))), ("EndPoint", ShadowValue.From(new Vec3(10, i * 2))),
            ("Layer", Ref("layer")), ("Linetype", Ref("bylayer")), ("Lineweight", ShadowValue.From("ByLayer"))))).ToImmutableDictionary(node => node.Id);
        return new(Guid.NewGuid(), 0, new("document"), objects, ImmutableHashSet.Create(new ObjectId("line/0"), new ObjectId("line/1")), []);
    }
    private static ShadowValue Color(double red, double green, double blue) => new ShadowValue.Structure("Color",
        ImmutableDictionary<string, ShadowValue>.Empty.Add("R", ShadowValue.From(red)).Add("G", ShadowValue.From(green)).Add("B", ShadowValue.From(blue)).Add("UseTrueColor", ShadowValue.From(true)));
    private static ShadowObject Node(string id, string type, ShadowObjectKind kind, params (string Name, ShadowValue Value)[] properties) =>
        new(new(id), type, kind, properties.ToImmutableDictionary(property => property.Name, property => property.Value), []);
    private static ShadowSnapshot Replace(ShadowSnapshot snapshot, ShadowObject node) =>
        snapshot with { Revision = snapshot.Revision + 1, Objects = snapshot.Objects.SetItem(node.Id, node) };
    private static void Require(bool condition, string message = "Assertion failed.") { if (!condition) throw new InvalidOperationException(message); }
}
