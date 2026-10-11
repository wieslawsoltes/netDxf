using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using netDxf.Blocks;
using netDxf.Collections;
using netDxf.Entities;
using netDxf.Tables;

namespace netDxf.Shadow.Dimensions;

/// <summary>Rehydrates only the presentation-relevant public model into a private object graph.
/// No source binding, file access, arbitrary CLR activation or mutable object escapes this reader.</summary>
internal sealed class DimensionSnapshotReader(DimensionProjectionOptions options)
{
    private static readonly Type[] ModelTypes =
    [
        typeof(AlignedDimension), typeof(LinearDimension), typeof(Angular2LineDimension), typeof(Angular3PointDimension),
        typeof(RadialDimension), typeof(DiametricDimension), typeof(OrdinateDimension), typeof(ArcLengthDimension),
        typeof(DimensionStyle), typeof(DimensionStyleOverrideDictionary), typeof(DimensionStyleAlternateUnits), typeof(DimensionStyleTolerances),
        typeof(AciColor), typeof(Transparency), typeof(TextStyleFontData), typeof(TextStyle), typeof(Layer), typeof(Linetype), typeof(LinetypeSimpleSegment),
        typeof(LinetypeTextSegment), typeof(Block), typeof(Insert), typeof(Line), typeof(Arc), typeof(Circle), typeof(Ellipse),
        typeof(Solid), typeof(Trace), typeof(Face3D), typeof(Text), typeof(MText), typeof(Polyline2D), typeof(Polyline2DVertex),
        typeof(Polyline3D), typeof(Spline), typeof(netDxf.Entities.Point), typeof(AttributeDefinition)
    ];
    private static readonly IReadOnlyDictionary<string, Type> Types = ModelTypes.ToDictionary(t => t.FullName!, StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Properties = new();
    private readonly Dictionary<ObjectId, object> copies = new();
    private readonly HashSet<ObjectId> active = new();
    private int values;

    internal Dimension Read(ShadowReader source)
    {
        Budget(0);
        // Construct from independent definition coordinates, not derived points or
        // the old generated block. Constructor selection must never change with reflection order.
        var style = (DimensionStyle)Object(source["Style"], 1);
        Vector2 P(string name) => (Vector2)Value(source[name], typeof(Vector2), 1)!;
        double N(string name) => (double)Value(source[name], typeof(double), 1)!;
        Dimension dimension = source.Type switch
        {
            "AlignedDimension" => new AlignedDimension(P("FirstReferencePoint"), P("SecondReferencePoint"), N("Offset"), style),
            "LinearDimension" => new LinearDimension(P("FirstReferencePoint"), P("SecondReferencePoint"), N("Offset"), N("Rotation"), style),
            "Angular2LineDimension" => new Angular2LineDimension(P("StartFirstLine"), P("EndFirstLine"), P("StartSecondLine"), P("EndSecondLine"), N("Offset"), style),
            "Angular3PointDimension" => new Angular3PointDimension(P("CenterPoint"), P("StartPoint"), P("EndPoint"), N("Offset"), style),
            "RadialDimension" => new RadialDimension(P("CenterPoint"), P("ReferencePoint"), style),
            "DiametricDimension" => new DiametricDimension(P("CenterPoint"), P("ReferencePoint"), style),
            "OrdinateDimension" => new OrdinateDimension(P("Origin"), P("FeaturePoint"), P("LeaderEndPoint"), EnumValue<OrdinateDimensionAxis>(source["Axis"]), style) { Rotation = N("Rotation") },
            "ArcLengthDimension" => new ArcLengthDimension(P("CenterPoint"), N("Radius"), N("StartAngle"), N("EndAngle"), N("Offset"), style),
            _ => throw new NotSupportedException("Unsupported detached dimension definition: " + source.Type)
        };
        dimension.Normal = (Vector3)Value(source["Normal"], typeof(Vector3), 1)!;
        dimension.Elevation = N("Elevation");
        dimension.UserText = (string)Value(source["UserText"], typeof(string), 1)!;
        dimension.TextRotation = N("TextRotation");
        dimension.AttachmentPoint = EnumValue<MTextAttachmentPoint>(source["AttachmentPoint"]);
        dimension.LineSpacingStyle = EnumValue<MTextLineSpacingStyle>(source["LineSpacingStyle"]);
        dimension.LineSpacingFactor = N("LineSpacingFactor");
        CopyOverrides(dimension.StyleOverrides, source["StyleOverrides"], 1);
        if (source["TextPositionManuallySet"].Boolean()) dimension.TextReferencePoint = P("TextReferencePoint");
        else dimension.TextPositionManuallySet = false;
        dimension.Update();
        return dimension;
    }

    private void CopyOverrides(DimensionStyleOverrideDictionary target, ShadowReader source, int depth)
    {
        Budget(depth);
        foreach (var item in source["Values"].Items)
        {
            Budget(depth + 1);
            target.Add(new DimensionStyleOverride(EnumValue<DimensionStyleOverrideType>(item["Type"]),
                Value(item["Value"], typeof(object), depth + 1)!));
        }
    }

    private object Object(ShadowReader source, int depth)
    {
        Budget(depth);
        var node = source.Object ?? throw new ArgumentException("A detached dependency is missing.");
        if (active.Contains(node.Id)) throw new NotSupportedException("Cyclic dimension resource dependency.");
        if (copies.TryGetValue(node.Id, out var existing)) return existing;
        if (copies.Count >= options.MaximumSourceObjects) throw new InvalidOperationException("Dimension source-object budget exceeded.");
        if (!Types.TryGetValue(node.TypeName, out var type)) throw new NotSupportedException($"Dimension dependency {node.TypeName} needs an explicit materializer.");
        active.Add(node.Id);
        try
        {
            object result;
            if (type == typeof(DimensionStyleOverrideDictionary))
            {
                var overrides = new DimensionStyleOverrideDictionary();
                copies.Add(node.Id, overrides);
                CopyOverrides(overrides, source, depth + 1);
                return overrides;
            }
            if (type == typeof(Block))
            {
                if (source["IsXRef"].Boolean()) throw new NotSupportedException("External arrow blocks require an explicit resource resolver.");
                if (source["AttributeDefinitions"]["Values"].Items.Any()) throw new NotSupportedException("Arrow attribute definitions need an explicit materializer.");
                var block = new Block(source["Name"].Text) { Origin = (Vector3)Value(source["Origin"], typeof(Vector3), depth + 1)! };
                copies.Add(node.Id, block);
                foreach (var child in source["Entities"].Items)
                    block.Entities.Add((EntityObject)Object(child, depth + 1));
                block.Record.Units = EnumValue<netDxf.Units.DrawingUnits>(source["Record"]["Units"]);
                return block;
            }
            if (type == typeof(AciColor))
            {
                result = source["UseTrueColor"].Boolean()
                    ? new AciColor((byte)source["R"].Number(), (byte)source["G"].Number(), (byte)source["B"].Number())
                    : source["IsByBlock"].Boolean() ? AciColor.ByBlock : source["IsByLayer"].Boolean() ? AciColor.ByLayer
                    : new AciColor((short)source["Index"].Number(7));
                copies.Add(node.Id, result); return result;
            }
            if (type == typeof(TextStyle))
            {
                var family = source["FontFamilyName"].Text;
                result = family.Length > 0 && !source["FontFile"].Text.EndsWith(".shx", StringComparison.OrdinalIgnoreCase)
                    ? new TextStyle(source["Name"].Text, family, EnumValue<FontStyle>(source["FontStyle"]))
                    : new TextStyle(source["Name"].Text, source["FontFile"].Text.Length == 0 ? TextStyle.DefaultFont : source["FontFile"].Text);
            }
            else if (type == typeof(Transparency))
            {
                result = source["IsByBlock"].Boolean() ? Transparency.ByBlock : source["IsByLayer"].Boolean() ? Transparency.ByLayer
                    : new Transparency((short)source["Value"].Number());
                copies.Add(node.Id, result); return result;
            }
            else result = Construct(type, source, depth);
            copies.Add(node.Id, result);
            Populate(result, source, depth);
            return result;
        }
        finally { active.Remove(node.Id); }
    }

    private object Construct(Type type, ShadowReader source, int depth)
    {
        if (type == typeof(Spline))
            return new Spline(source["ControlPoints"].Items.Select(p => (Vector3)Value(p, typeof(Vector3), depth + 1)!),
                source["Weights"].Items.Select(p => p.Number(1)), source["Knots"].Items.Select(p => p.Number()),
                (short)source["Degree"].Number(), source["IsClosedPeriodic"].Boolean());
        var empty = type.GetConstructor(Type.EmptyTypes);
        if (empty is not null) return empty.Invoke(null);
        foreach (var constructor in type.GetConstructors().OrderBy(c => c.GetParameters().Length))
        {
            var args = constructor.GetParameters(); var values = new object?[args.Length]; var found = true;
            for (var i = 0; i < args.Length; i++)
            {
                var name = source.Object!.Properties.Keys.FirstOrDefault(n => string.Equals(n, args[i].Name, StringComparison.OrdinalIgnoreCase));
                if (name is not null) values[i] = Value(source[name], args[i].ParameterType, depth + 1);
                else if (args[i].HasDefaultValue) values[i] = args[i].DefaultValue;
                else { found = false; break; }
            }
            if (found) return constructor.Invoke(values);
        }
        throw new NotSupportedException($"Cannot construct detached dimension dependency {type.Name}.");
    }

    private void Populate(object target, ShadowReader source, int depth)
    {
        if (target is Insert && source["Attributes"].Items.Any())
            throw new NotSupportedException("Custom dimension arrows with attached attributes require an explicit materializer.");
        if (target is Block && source["AttributeDefinitions"]["Values"].Items.Any())
            throw new NotSupportedException("Custom dimension arrows with attribute definitions require an explicit materializer.");
        if (target is Ellipse ellipse) ellipse.SetAxis(source["MajorAxis"].Number(), source["MinorAxis"].Number());
        foreach (var property in Properties.GetOrAdd(target.GetType(), t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && p.GetMethod is { IsPublic: true })
            .OrderBy(p => p.Name == "TextPositionManuallySet" ? 1 : 0).ThenBy(p => p.Name, StringComparer.Ordinal).ToArray()))
        {
            var name = property.Name;
            if (name is "Owner" or "Handle" or "Name" or "XData" or "ExtensionDictionary" or "Reactors" or "ProxyGraphics" or "Associations" or "Record") continue;
            if (target is Dimension && name == "Block") continue;
            if (target is TextStyle && name is "FontFile" or "FontFamilyName" or "FontStyle") continue;
            var value = source[name]; if (!value.Exists) continue;
            Budget(depth);
            if (target is Dimension dimension && name == "StyleOverrides") CopyOverrides(dimension.StyleOverrides, value, depth + 1);
            else if (property.SetMethod is { IsPublic: true })
            {
                try { property.SetValue(target, Value(value, property.PropertyType, depth + 1)); }
                catch (Exception error) { throw new InvalidOperationException($"{target.GetType().Name}.{name}: {error.Message}", error); }
            }
            else if (name is "AlternateUnits" or "Tolerances") Populate(property.GetValue(target)!, value, depth + 1);
            else if (name is "Entities" or "Vertexes" or "Segments" or "FitPoints")
            {
                var collection = property.GetValue(target);
                if (collection is Array) continue; // Constructor initialized immutable-cardinality spline coordinates.
                if (collection is null) throw new NotSupportedException("A required owned collection is absent.");
                var contract = collection.GetType().GetInterfaces().FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ICollection<>));
                if (contract is null) throw new NotSupportedException("A dimension dependency collection has no public mutable contract.");
                contract.GetMethod("Clear")!.Invoke(collection, null);
                var element = contract.GetGenericArguments()[0]; var add = contract.GetMethod("Add")!;
                foreach (var item in value.Items) add.Invoke(collection, [Value(item, element, depth + 1)]);
            }
        }
        if (target is Block block && source["Record"]["Units"].Exists) block.Record.Units = EnumValue<netDxf.Units.DrawingUnits>(source["Record"]["Units"]);
    }

    private object? Value(ShadowReader source, Type target, int depth)
    {
        try { return ConvertValue(source, target, depth); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Dimension value {source.Type} -> {target.Name}: {error.Message}", error);
        }
    }

    private object? ConvertValue(ShadowReader source, Type target, int depth)
    {
        Budget(depth);
        if (!source.Exists) return null;
        target = Nullable.GetUnderlyingType(target) ?? target;
        if (source.Id is not null)
        {
            var result = Object(source, depth);
            return target.IsInstanceOfType(result) ? result : throw new ArgumentException("Dimension dependency type mismatch.");
        }
        if (target == typeof(TextStyleFontData)) return new TextStyleFontData(source["FamilyName"].Text, checked((int)source["Flags"].Number()));
        if (target == typeof(Vector2)) return new Vector2(source["X"].Number(), source["Y"].Number());
        if (target == typeof(Vector3)) return new Vector3(source["X"].Number(), source["Y"].Number(), source["Z"].Number());
        if (source.Value is ShadowValue.Atom atom)
        {
            if (target == typeof(object))
            {
                target = atom.TypeName switch
                {
                    "System.String" => typeof(string), "System.Boolean" => typeof(bool), "System.Char" => typeof(char),
                    "System.Int16" => typeof(short), "System.Int32" => typeof(int), "System.Double" => typeof(double),
                    _ => typeof(DxfDocument).Assembly.GetType(atom.TypeName) is { IsEnum: true } e ? e : throw new NotSupportedException("Unknown dimension override scalar.")
                };
            }
            if (target.IsEnum) return Enum.Parse(target, atom.Text);
            var value = Convert.ChangeType(atom.Text, target, CultureInfo.InvariantCulture);
            if (value is double d && !double.IsFinite(d)) throw new ArgumentException("Dimension values must be finite.");
            if (value is string text && text.Length > options.MaximumTextLength) throw new InvalidOperationException("Dimension text budget exceeded.");
            return value;
        }
        if (source.Value is ShadowValue.Sequence)
        {
            var element = target.IsArray ? target.GetElementType() : target.GetInterfaces().Append(target)
                .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
            if (element is null) throw new NotSupportedException("Unknown dimension value sequence.");
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
            foreach (var item in source.Items) list.Add(Value(item, element, depth + 1));
            if (!target.IsArray) return list;
            var array = Array.CreateInstance(element, list.Count); list.CopyTo(array, 0); return array;
        }
        throw new NotSupportedException($"Unsupported dimension value {target.Name}.");
    }

    private void Budget(int depth)
    {
        if (depth > options.MaximumDepth || ++values > options.MaximumSourceValues)
            throw new InvalidOperationException("Dimension dependency budget exceeded.");
    }
    private static T EnumValue<T>(ShadowReader value) where T : struct, Enum => Enum.TryParse<T>(value.Text, out var result) ? result : default;
}
