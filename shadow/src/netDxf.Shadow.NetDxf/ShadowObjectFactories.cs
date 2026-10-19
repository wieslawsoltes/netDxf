using System.Reflection;

namespace netDxf.Shadow.NetDxf;

/// <summary>Explicit construction extension point for source types with special invariants.</summary>
public interface IShadowObjectResolver
{
    object Resolve(ObjectId id);
    object? Convert(ShadowValue value, Type targetType);
}

/// <summary>
/// Constructs only public data-model types from the linked netDxf assembly. It never resolves
/// arbitrary assemblies, executes user code, or deserializes unrestricted CLR type names.
/// Applications can register audited factories before attaching the session.
/// </summary>
public sealed class ShadowObjectFactories
{
    private readonly Dictionary<Type, Func<ShadowObject, IShadowObjectResolver, object>> factories = new();
    private readonly object gate = new();

    public void Register<T>(Func<ShadowObject, IShadowObjectResolver, T> factory) where T : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (gate) factories[typeof(T)] = (node, resolver) => factory(node, resolver);
    }

    internal object Create(ShadowObject node, IShadowObjectResolver resolver, out bool initializeInsertAttributes)
    {
        initializeInsertAttributes = false;
        var type = typeof(DxfDocument).Assembly.GetType(node.TypeName, throwOnError: false, ignoreCase: false)
            ?? throw new NotSupportedException($"Unknown netDxf data-model type {node.TypeName}.");
        Func<ShadowObject, IShadowObjectResolver, object>? factory;
        lock (gate) factories.TryGetValue(type, out factory);
        if (factory is not null) return factory(node, resolver) ?? throw new InvalidOperationException("A source factory returned null.");
        if (!type.IsClass || type.IsAbstract || !type.IsVisible || type == typeof(DxfDocument) ||
            !(type.Namespace is "netDxf.Entities" or "netDxf.Blocks" or "netDxf.Tables" or "netDxf.Objects" or "netDxf.Header" or "netDxf.Collections" or "netDxf"))
            throw new NotSupportedException($"{node.TypeName} is not a constructible source data-model type.");
        // These immutable/compound values cannot be reconstructed by choosing an empty
        // constructor followed by public setters. In particular RGB and ellipse axes are
        // read-only, and a control-point spline must not pick the fit-point constructor.
        if (type == typeof(AciColor))
        {
            bool Flag(string name) => node[name] is ShadowValue.Atom a && bool.TryParse(a.Text, out var value) && value;
            if (Flag("UseTrueColor") || node["R"] is ShadowValue.Atom && node["Index"] is ShadowValue.NullValue)
                return new AciColor((byte)resolver.Convert(node["R"], typeof(byte))!,
                    (byte)resolver.Convert(node["G"], typeof(byte))!, (byte)resolver.Convert(node["B"], typeof(byte))!);
            if (node["Index"] is ShadowValue.Atom)
                return AciColor.FromCadIndex((short)resolver.Convert(node["Index"], typeof(short))!);
        }
        if (type == typeof(Transparency))
        {
            if (node["StoredAlphaValue"] is ShadowValue.Atom)
                return Transparency.FromAlphaValue((int)resolver.Convert(node["StoredAlphaValue"], typeof(int))!);
            if (node["Value"] is ShadowValue.Atom)
                return Transparency.FromCadIndex((short)resolver.Convert(node["Value"], typeof(short))!);
        }
        if (type == typeof(XData) && node["ApplicationRegistry"] is ShadowValue.Reference registry)
            return new XData((netDxf.Tables.ApplicationRegistry)resolver.Resolve(registry.Id));
        if (type == typeof(netDxf.Blocks.Block) && node["Name"] is ShadowValue.Atom blockName)
        {
            if (node["IsXRef"] is ShadowValue.Atom xref && bool.TryParse(xref.Text, out var external) && external)
            {
                var flags = (netDxf.Blocks.BlockTypeFlags)resolver.Convert(node["Flags"], typeof(netDxf.Blocks.BlockTypeFlags))!;
                return new netDxf.Blocks.Block(blockName.Text, (string)resolver.Convert(node["XrefFile"], typeof(string))!,
                    (flags & netDxf.Blocks.BlockTypeFlags.XRefOverlay) != 0);
            }
            if (blockName.Text.StartsWith('*')) return netDxf.Blocks.Block.CreateAnonymous(blockName.Text);
        }
        if (type == typeof(netDxf.Entities.Attribute) && node["Definition"] is ShadowValue.Reference attributeDefinition)
        {
            var attribute = new netDxf.Entities.Attribute((netDxf.Entities.AttributeDefinition)resolver.Resolve(attributeDefinition.Id));
            // A retained definition may be renamed later in the same transaction.
            // Construct the new instance with its explicit proposed tag; the identity
            // phase binds the actual definition after the prepared rename is applied.
            if (node["Tag"] is ShadowValue.Atom tag && !string.Equals(tag.Text, attribute.Tag, StringComparison.Ordinal))
                attribute.PrepareDefinitionBinding(tag.Text, null).Apply();
            return attribute;
        }
        if (type == typeof(netDxf.Objects.DxfSortOrderEntry) && node["Target"] is ShadowValue.Reference target)
            return new netDxf.Objects.DxfSortOrderEntry((DxfObject)resolver.Resolve(target.Id),
                (string)resolver.Convert(node["SortHandle"], typeof(string))!);
        if (type == typeof(netDxf.Objects.DxfDictionaryEntry) && node["Target"] is ShadowValue.NullValue)
        {
            if (node["IsHardOwner"] is not ShadowValue.Atom owner || !bool.TryParse(owner.Text, out var hardOwner) || hardOwner)
                throw new NotSupportedException("Unresolved dictionary entries require explicit soft ownership.");
            return netDxf.Objects.DxfDictionaryEntry.CreateUnresolvedSoftReference(
                (string)resolver.Convert(node["Name"], typeof(string))!,
                (string)resolver.Convert(node["TargetHandle"], typeof(string))!);
        }
        if (type == typeof(netDxf.Tables.TextStyle))
        {
            var style = netDxf.Tables.TextStyle.FromStoredFontFiles(
                (string)resolver.Convert(node["Name"], typeof(string))!,
                (string)resolver.Convert(node["FontFile"], typeof(string))!,
                (string)resolver.Convert(node["BigFont"], typeof(string))!);
            if (node["ExtendedFontData"] is not ShadowValue.NullValue)
                style.ExtendedFontData = (netDxf.Tables.TextStyleFontData)resolver.Convert(
                    node["ExtendedFontData"], typeof(netDxf.Tables.TextStyleFontData))!;
            return style;
        }
        if (type == typeof(netDxf.Entities.Ellipse) && node["MajorAxis"] is ShadowValue.Atom && node["MinorAxis"] is ShadowValue.Atom)
            return new netDxf.Entities.Ellipse(Vector3.Zero, (double)resolver.Convert(node["MajorAxis"], typeof(double))!,
                (double)resolver.Convert(node["MinorAxis"], typeof(double))!);
        if (type == typeof(netDxf.Entities.Spline) && node["ControlPoints"] is ShadowValue.Sequence)
        {
            var controls = (Vector3[])resolver.Convert(node["ControlPoints"], typeof(Vector3[]))!;
            var weights = node["Weights"] is ShadowValue.NullValue ? null : (double[])resolver.Convert(node["Weights"], typeof(double[]))!;
            var degree = node["Degree"] is ShadowValue.NullValue ? (short)3 : (short)resolver.Convert(node["Degree"], typeof(short))!;
            var periodic = node["IsClosedPeriodic"] is ShadowValue.Atom a && bool.TryParse(a.Text, out var closed) && closed;
            if (node["Knots"] is ShadowValue.Sequence)
                return new netDxf.Entities.Spline(controls, weights, (double[])resolver.Convert(node["Knots"], typeof(double[]))!, degree, periodic);
            return new netDxf.Entities.Spline(controls, weights, degree, periodic);
        }
        // Boundary-path overloads accept either entities or analytic edges. Resolve the
        // requested representation explicitly rather than guessing the first constructor.
        // Initialize polyline vertices before clone-based source preflight examines them.
        if (type == typeof(netDxf.Entities.HatchBoundaryPath.Polyline))
            return new netDxf.Entities.HatchBoundaryPath.Polyline
            {
                Vertexes = (Vector3[])resolver.Convert(node["Vertexes"], typeof(Vector3[]))!,
                IsClosed = node["IsClosed"] is ShadowValue.Atom closed && bool.TryParse(closed.Text, out var flag) && flag
            };
        if (type == typeof(netDxf.Entities.HatchBoundaryPath) && node["Edges"] is ShadowValue.Sequence)
        {
            var edges = (netDxf.Entities.HatchBoundaryPath.Edge[])resolver.Convert(node["Edges"], typeof(netDxf.Entities.HatchBoundaryPath.Edge[]))!;
            // Authored paths omit the storage flags. Let the native constructor
            // derive the polyline representation instead of assuming edge records.
            var pathType = node["PathType"] is ShadowValue.NullValue
                ? new netDxf.Entities.HatchBoundaryPath(edges).PathType
                : (netDxf.Entities.HatchBoundaryPathTypeFlags)resolver.Convert(node["PathType"], typeof(netDxf.Entities.HatchBoundaryPathTypeFlags))!;
            return netDxf.Entities.HatchBoundaryPath.FromStoredEdges(edges, pathType,
                node["Entities"] is ShadowValue.NullValue ? Array.Empty<netDxf.Entities.EntityObject>()
                    : (netDxf.Entities.EntityObject[])resolver.Convert(node["Entities"], typeof(netDxf.Entities.EntityObject[]))!);
        }
        if (type == typeof(netDxf.Entities.Insert) && node["Block"] is ShadowValue.Reference explicitBlock && node["Attributes"] is ShadowValue.Sequence)
        {
            var insert = netDxf.Entities.Insert.CreateWithAttributes((netDxf.Blocks.Block)resolver.Resolve(explicitBlock.Id),
                Array.Empty<netDxf.Entities.Attribute>());
            // Stored drawings can contain distinct attributes with equal tags. Exact
            // graph reconstruction preserves those instances, unlike default authoring.
            insert.ReplaceAttributes((netDxf.Entities.Attribute[])resolver.Convert(node["Attributes"], typeof(netDxf.Entities.Attribute[]))!);
            if (node["EndSequenceRecord"] is ShadowValue.Reference) insert.EnsureAttributeSequenceEnd();
            return insert;
        }
        if (type == typeof(netDxf.Entities.Insert) && node["Block"] is ShadowValue.Reference definition && node["Attributes"] is ShadowValue.NullValue)
        {
            // Definition edits in this same patch have not reached the live block yet.
            // The session prepares defaults from the proposed snapshot before mutation,
            // then adopts them after definition names are updated and before registration.
            var block = (netDxf.Blocks.Block)resolver.Resolve(definition.Id);
            var position = node["Position"] is ShadowValue.NullValue ? Vector3.Zero : (Vector3)resolver.Convert(node["Position"], typeof(Vector3))!;
            var insert = netDxf.Entities.Insert.CreateWithAttributes(block, Array.Empty<netDxf.Entities.Attribute>());
            insert.Position = position;
            if (node["Scale"] is not ShadowValue.NullValue) insert.Scale = (Vector3)resolver.Convert(node["Scale"], typeof(Vector3))!;
            if (node["Rotation"] is not ShadowValue.NullValue) insert.Rotation = (double)resolver.Convert(node["Rotation"], typeof(double))!;
            if (node["Normal"] is not ShadowValue.NullValue) insert.Normal = (Vector3)resolver.Convert(node["Normal"], typeof(Vector3))!;
            initializeInsertAttributes = true;
            return insert;
        }
        var empty = type.GetConstructor(Type.EmptyTypes);
        if (empty is not null) return empty.Invoke(null);
        // Constructor arguments are matched by their semantic property names. This handles
        // named table records, block references, vertices and other ordinary netDxf objects.
        foreach (var constructor in type.GetConstructors().OrderBy(c => c.GetParameters().Length))
        {
            var parameters = constructor.GetParameters();
            var values = new object?[parameters.Length];
            var matched = true;
            for (var index = 0; index < parameters.Length; index++)
            {
                var parameter = parameters[index];
                var property = node.Properties.FirstOrDefault(p => string.Equals(p.Key, parameter.Name, StringComparison.OrdinalIgnoreCase));
                if (property.Key is not null) values[index] = resolver.Convert(property.Value, parameter.ParameterType);
                else if (parameter.HasDefaultValue) values[index] = parameter.DefaultValue;
                else { matched = false; break; }
            }
            if (matched) return constructor.Invoke(values);
        }
        throw new NotSupportedException($"{node.TypeName} needs an explicit ShadowObjectFactories registration or additional constructor properties.");
    }
}
