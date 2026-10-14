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

    internal object Create(ShadowObject node, IShadowObjectResolver resolver)
    {
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
