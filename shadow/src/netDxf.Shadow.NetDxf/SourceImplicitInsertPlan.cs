using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Units;
using Att = netDxf.Entities.Attribute;

namespace netDxf.Shadow.NetDxf;

/// <summary>New INSERT defaults use proposed definition values and frames, without mutating live definitions during preparation.</summary>
internal sealed class SourceImplicitInsertPlan(Insert insert, Att[] attributes, AttributeDefinitionBindingChange[] bindings,
    (Att Attribute, TextStyle Style)[] embeddedStyles)
{
    private readonly Att[] before = insert.Attributes.ToArray();
    private int bound;
    private bool applied;

    internal static SourceImplicitInsertPlan Prepare(ObjectId id, Insert insert, ShadowSnapshot baseline, ShadowSnapshot target,
        Func<ObjectId, object> resolve, Func<ShadowValue, Type, object?> convert)
    {
        var node = target.Read(id); var block = node["Block"];
        T Value<T>(ShadowReader value, T fallback) => value.Exists ? (T)convert(value.Value, typeof(T))! : fallback;
        var frame = new Block("ATTRIBUTE_FRAME") { Origin = Value(block["Origin"], insert.Block.Origin) };
        frame.Record.Units = Value(block["Record"]["Units"], insert.Block.Record.Units);
        var owner = node["Owner"];
        var units = Insert.DefaultInsUnits;
        if (owner.Id is { } ownerId && resolve(ownerId) is Block nativeOwner)
            units = owner["Record"]["Layout"].Exists
                ? Value(target.Document["DrawingVariables"]["InsUnits"], nativeOwner.Record.Owner?.Owner.DrawingVariables.InsUnits ?? units)
                : Value(owner["Record"]["Units"], nativeOwner.Record.Units);
        var carrier = Insert.CreateWithAttributes(frame, Array.Empty<Att>());
        carrier.Position = insert.Position; carrier.Scale = insert.Scale; carrier.Rotation = insert.Rotation; carrier.Normal = insert.Normal;
        var matrix = carrier.GetTransformation(units); var translation = carrier.Position - matrix * frame.Origin;
        var mirror = target.Document["DrawingVariables"]["MirrText"].Boolean();
        var attributes = new List<Att>(); var bindings = new List<AttributeDefinitionBindingChange>();
        var embeddedStyles = new List<(Att Attribute, TextStyle Style)>();
        foreach (var entry in block["AttributeDefinitions"].Items)
        {
            var proposed = entry["Value"];
            if (proposed.Id is not { } definitionId || resolve(definitionId) is not AttributeDefinition definition)
                throw new ArgumentException("The insertion requires a consistent definition dictionary.");
            // Copy only writable definition state. XData, extension dictionaries and
            // proxy bytes are not constructor defaults for ATTRIB and may be very large.
            var detached = new AttributeDefinition(definition.Tag);
            foreach (var accessor in SourceAccessors.For(typeof(AttributeDefinition)))
                if (accessor.CanWrite && accessor.Name != "ProxyGraphics") accessor.Set(detached, accessor.Get(definition));
            var original = baseline.Read(definitionId).Object;
            foreach (var (name, value) in proposed.Object!.Properties)
            {
                var accessor = SourceAccessors.Find(typeof(AttributeDefinition), name);
                if (accessor is not { CanWrite: true } || name == "ProxyGraphics") continue;
                // Canonical resource objects may themselves be renamed in this batch.
                // Rebind their references even when the definition's reference is unchanged.
                if (original is not null && value is not ShadowValue.Reference && name != "TextData" && ShadowValue.Equivalent(original[name], value)) continue;
                accessor.Set(detached, convert(value, accessor.ValueType));
            }
            var attribute = Att.CreateTransformed(detached, matrix, translation, mirror);
            if (attribute.GetTextData(false)?.Content is not null)
            {
                var style = proposed["TextData"]["Content"]["Style"].Id is { } styleId
                    ? (TextStyle)resolve(styleId) : definition.GetTextData(false)?.Content?.Style;
                if (style is not null) embeddedStyles.Add((attribute, style));
            }
            bindings.Add(attribute.PrepareDefinitionBinding(proposed["Tag"].Text, definition));
            attributes.Add(attribute);
        }
        return new(insert, attributes.ToArray(), bindings.ToArray(), embeddedStyles.ToArray());
    }

    internal void Apply()
    {
        // Stored text packets clone styles. Refresh these detached copies after
        // resource edits, so registration cannot resurrect a pre-rename name.
        // Geometry was prepared already; this changes only the embedded resource.
        foreach (var (attribute, style) in embeddedStyles)
        {
            var data = attribute.GetTextData(false)!;
            data.Content.Style = style; attribute.SetTextData(data);
        }
        foreach (var binding in bindings) { binding.Apply(); bound++; }
        applied = true; insert.ReplaceAttributes(attributes);
    }

    internal void Rollback()
    {
        if (applied) { insert.ReplaceAttributes(before); applied = false; }
        for (int i = bound - 1; i >= 0; i--) bindings[i].Rollback();
        bound = 0;
    }
}
