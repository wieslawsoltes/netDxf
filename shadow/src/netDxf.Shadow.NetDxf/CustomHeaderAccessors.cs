using netDxf.Header;
using netDxf.IO;

namespace netDxf.Shadow.NetDxf;

/// <summary>Audited public-method adapters; no private reflection or live lists enter the snapshot.</summary>
internal static class CustomHeaderAccessors
{
    internal static SourceAccessor[] Extend(Type type, SourceAccessor[] accessors)
    {
        if (type == typeof(HeaderVariables))
            return accessors.Append(new SourceAccessor("CustomVariables", typeof(List<HeaderVariable>), true,
                source => ((HeaderVariables)source).CustomValues(), Replace))
                .OrderBy(a => a.Name, StringComparer.Ordinal).ToArray();
        if (type == typeof(HeaderVariable))
            return accessors.Select(a => a.Name == "Value" ? a with { Set = (source, value) =>
            {
                var variable = (HeaderVariable)source;
                Validate(variable.GroupCode, value);
                variable.Value = value;
            } } : a).ToArray();
        return accessors;
    }

    private static void Replace(object source, object? replacement)
    {
        var headers = (HeaderVariables)source;
        var values = replacement as List<HeaderVariable> ?? throw new ArgumentException("Custom headers must be a typed list.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var known = headers.KnownNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Validate the entire replacement before touching the live dictionary.
        foreach (var variable in values)
        {
            if (variable is null || string.IsNullOrWhiteSpace(variable.Name) || !variable.Name.StartsWith('$') ||
                variable.Name.Length < 2 || variable.Name.Any(char.IsControl) || !names.Add(variable.Name) || known.Contains(variable.Name))
                throw new ArgumentException("Custom headers need unique, non-control names distinct from known variables.");
            Validate(variable.GroupCode, variable.Value);
        }
        headers.ClearCustomVariables();
        foreach (var variable in values) headers.AddCustomVariable(variable);
    }

    internal static void Validate(short code, object? value)
    {
        // HeaderVariable uses 20/30 as compound vector descriptors, unlike primitive DXF tags.
        var valid = code switch
        {
            20 => value is Vector2 p && double.IsFinite(p.X) && double.IsFinite(p.Y),
            30 => value is Vector3 p && double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z),
            0 or 9 or 999 => false, // Record/header separators and comments cannot be value packets.
            _ => DxfGroupCode.GetValueType(code) switch
            {
                DxfTagValueType.String or DxfTagValueType.Handle => value is string text && !text.Contains('\0') && !text.Contains('\r') && !text.Contains('\n'),
                DxfTagValueType.Double => value is double d && double.IsFinite(d),
                DxfTagValueType.Int16 => value is short,
                DxfTagValueType.Int32 => value is int,
                DxfTagValueType.Int64 => value is long,
                DxfTagValueType.Boolean => value is bool,
                DxfTagValueType.BinaryData => value is byte[] bytes && bytes.Length <= 127,
                _ => false
            }
        };
        if (!valid) throw new ArgumentException($"Custom header group {code} has an incompatible or invalid value.");
    }
}
