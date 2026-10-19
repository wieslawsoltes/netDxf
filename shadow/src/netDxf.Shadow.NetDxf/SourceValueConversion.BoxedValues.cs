namespace netDxf.Shadow.NetDxf;

internal static partial class SourceValueConversion
{
    // Object-typed public values must retain their authored primitive type, not silently become strings.
    // Never load a CLR type or assembly named by a snapshot.
    private static object? ConvertBoxedValue(ShadowValue value, Func<ObjectId, object> resolve)
    {
        var type = value switch
        {
            ShadowValue.Atom a => a.TypeName switch
            {
                "System.String" => typeof(string), "System.Boolean" => typeof(bool),
                "System.Byte" => typeof(byte), "System.SByte" => typeof(sbyte),
                "System.Int16" => typeof(short), "System.UInt16" => typeof(ushort),
                "System.Int32" => typeof(int), "System.UInt32" => typeof(uint),
                "System.Int64" => typeof(long), "System.UInt64" => typeof(ulong),
                "System.Single" => typeof(float), "System.Double" => typeof(double),
                "System.Decimal" => typeof(decimal), "System.Char" => typeof(char),
                "System.Guid" => typeof(Guid), "System.DateTime" => typeof(DateTime),
                "System.DateTimeOffset" => typeof(DateTimeOffset), "System.TimeSpan" => typeof(TimeSpan),
                _ => throw new NotSupportedException($"Unregistered boxed atom {a.TypeName}.")
            },
            ShadowValue.Structure { TypeName: "netDxf.Vector2" } => typeof(Vector2),
            ShadowValue.Structure { TypeName: "netDxf.Vector3" } => typeof(Vector3),
            ShadowValue.Sequence s when !s.Items.IsDefault && s.Items.All(v => v is ShadowValue.Atom { TypeName: "System.Byte" }) => typeof(byte[]),
            _ => throw new NotSupportedException("An object-typed source value needs an explicitly supported detached type.")
        };
        return ConvertValue(value, type, resolve);
    }
}
