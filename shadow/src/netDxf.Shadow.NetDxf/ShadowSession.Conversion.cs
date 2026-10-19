namespace netDxf.Shadow.NetDxf;

public sealed partial class ShadowSession
{
    private object? ConvertValue(ShadowValue value, Type target, Func<ObjectId, object>? resolve = null) =>
        SourceValueConversion.ConvertValue(value, target, resolve ?? capture.Source);
}
