using System.Collections.Immutable;
using netDxf.Shadow;

internal static class SyntheticScene
{
    internal const int Layers = 16;

    internal static ShadowSnapshot Create(int count)
    {
        var entities = ImmutableArray.CreateBuilder<ShadowValue>(count);
        var ids = ImmutableHashSet.CreateBuilder<ObjectId>();
        var objects = ImmutableDictionary.CreateBuilder<ObjectId, ShadowObject>();
        Add("document", "Document", ShadowObjectKind.Document,
            ("Layouts", new ShadowValue.Sequence([Ref("layout")])), ("DrawingVariables", Ref("variables")));
        Add("layout", "Layout", ShadowObjectKind.Layout, ("Name", ShadowValue.From("Model")), ("AssociatedBlock", Ref("model")));
        Add("variables", "DrawingVariables", ShadowObjectKind.Object, ("LtScale", ShadowValue.From(1)));
        Add("bylayer", "Linetype", ShadowObjectKind.Table, ("Name", ShadowValue.From("ByLayer")));
        Add("continuous", "Linetype", ShadowObjectKind.Table, ("Name", ShadowValue.From("Continuous")), ("Segments", new ShadowValue.Sequence([])));
        var normal = ShadowValue.From(Vec3.UnitZ);
        var inherited = new ShadowValue.Structure("Color", ImmutableDictionary<string, ShadowValue>.Empty.Add("IsByLayer", ShadowValue.From(true)));
        for (var i = 0; i < Layers; i++)
            Add("layer/" + i, "Layer", ShadowObjectKind.Table, ("Name", ShadowValue.From("Layer " + i)),
                ("Color", new ShadowValue.Structure("Color", ImmutableDictionary<string, ShadowValue>.Empty
                    .Add("R", ShadowValue.From(40 + i * 7)).Add("G", ShadowValue.From(180 - i * 5))
                    .Add("B", ShadowValue.From(120 + i * 3)).Add("UseTrueColor", ShadowValue.From(true)))),
                ("Lineweight", ShadowValue.From("W25")), ("Linetype", Ref("continuous")), ("IsVisible", ShadowValue.From(true)));
        for (var i = 0; i < count; i++)
        {
            var id = "line/" + i;
            entities.Add(Ref(id)); ids.Add(new(id));
            Add(id, "Line", ShadowObjectKind.Entity, ("Layer", Ref("layer/" + i % Layers)),
                ("Color", inherited), ("Linetype", Ref("bylayer")), ("Lineweight", ShadowValue.From("ByLayer")),
                ("LinetypeScale", ShadowValue.From(1)), ("Normal", normal), ("IsVisible", ShadowValue.From(true)),
                ("StartPoint", ShadowValue.From(new Vec3(i % 200 * 3, i / 200 * 3))),
                ("EndPoint", ShadowValue.From(new Vec3(i % 200 * 3 + 2, i / 200 * 3 + .7))));
        }
        Add("model", "Block", ShadowObjectKind.Block, ("Entities", new ShadowValue.Sequence(entities.MoveToImmutable())));
        return new(Guid.NewGuid(), 0, new("document"), objects.ToImmutable(), ids.ToImmutable(), []);

        void Add(string id, string type, ShadowObjectKind kind, params (string Name, ShadowValue Value)[] properties)
        {
            var key = new ObjectId(id);
            objects.Add(key, new(key, type, kind, properties.ToImmutableDictionary(value => value.Name, value => value.Value), []));
        }
    }
    private static ShadowValue.Reference Ref(string id) => new(new(id));
}
