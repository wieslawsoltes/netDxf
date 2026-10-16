namespace netDxf.Shadow.Studio;

// Keep the existing local-server API source compatible. Only this adapter depends on ASP.NET.
public sealed partial class StudioDocumentHub
{
    public object Close(string id, HttpRequest http, CadCloseDocumentRequest request) => Close(id, StudioHttp.Identity(http), request);
    public byte[] Save(string id, HttpRequest http, long revision) => Save(id, StudioHttp.Identity(http), revision);
    public byte[] Save(string id, HttpRequest http, long revision, Func<byte[], byte[]> encode) => Save(id, StudioHttp.Identity(http), revision, encode);
    public object Load(string id, HttpRequest http, Stream stream, long revision, string name) => Load(id, StudioHttp.Identity(http), stream, revision, name);
}
