namespace netDxf.Shadow.Studio;

public sealed partial class Studio
{
    /// <summary>Replace a drawing only while the captured workspace revision is still current.</summary>
    public void Load(Stream stream, long revision)
    {
        ArgumentNullException.ThrowIfNull(stream);
        lock (gate)
        {
            Check(revision);
            Load(stream);
        }
    }
}
