namespace netDxf.Shadow.Studio;

// Keep the existing local-server API source compatible. Only this adapter depends on ASP.NET.
public static class StudioDocumentHubHttp
{
    public static object Close(this StudioDocumentHub hub, string id, HttpRequest http, CadCloseDocumentRequest request) => hub.Close(id, StudioHttp.Identity(http), request);
    public static byte[] Save(this StudioDocumentHub hub, string id, HttpRequest http, long revision) => hub.Save(id, StudioHttp.Identity(http), revision);
    public static byte[] Save(this StudioDocumentHub hub, string id, HttpRequest http, long revision, Func<byte[], byte[]> encode) => hub.Save(id, StudioHttp.Identity(http), revision, encode);
    public static object Load(this StudioDocumentHub hub, string id, HttpRequest http, Stream stream, long revision, string name) => hub.Load(id, StudioHttp.Identity(http), stream, revision, name);
}
