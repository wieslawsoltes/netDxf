namespace NetDxf.Conformance;

internal static partial class Program
{
    static partial void RunAdditionalTests()
    {
        RegisterBinarySentinelTests();
        RegisterBinaryChunkTests();
        RegisterXDataCloneTests();
        RegisterUcsElevationTests();
        RegisterNamedViewTests();
        RegisterTextFramingTests();
        RegisterMinimalDocumentTests();
        RegisterHeaderProbeTests();
        RegisterStrictTextValueTests();
    }
}
