using System.Reflection;

namespace netDxf.Shadow.Studio;

internal static class StudioAgentSchema
{
    private static readonly Lazy<object[]> RequestTypes = new(() => typeof(Studio).Assembly.GetTypes()
        .Where(t => t.IsPublic && t.Namespace == typeof(Studio).Namespace && (t.Name.EndsWith("Request", StringComparison.Ordinal) || t.Name == "StudioRequestIdentity"))
        .OrderBy(t => t.Name, StringComparer.Ordinal).Select(t => (object)new
        {
            name = t.Name,
            fields = t.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.GetIndexParameters().Length == 0)
                .Select(p => new { name = char.ToLowerInvariant(p.Name[0]) + p.Name[1..], type = p.PropertyType.Name,
                    values = p.PropertyType.IsEnum ? Enum.GetNames(p.PropertyType) : null }).ToArray()
        }).ToArray());
    public static object[] DescribeRequests() => RequestTypes.Value;
}
